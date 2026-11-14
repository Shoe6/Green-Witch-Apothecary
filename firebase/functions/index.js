const { onCall, HttpsError } = require("firebase-functions/v2/https");
const { initializeApp } = require("firebase-admin/app");
const { getDatabase } = require("firebase-admin/database");

initializeApp();

const TOTAL_GROWTH_STAGES = 4; // 0: Seed, 1: Sprout, 2: Vegetative, 3: Mature/Harvestable
const DEFAULT_GROWTH_DURATION_SECONDS = 3600; // 1 hour baseline AFK growth window

/**
 * Computes AFK growth stage from planted epoch seconds and current server epoch seconds.
 * Modifies effective growth speed when soil pH or sunlight mismatches plant requirements.
 */
function computePlantGrowthState(plant, nowEpochSeconds) {
  const plantedEpoch = Number(plant.time_planted_epoch || nowEpochSeconds);
  const baseDuration = Number(plant.growth_duration_seconds || DEFAULT_GROWTH_DURATION_SECONDS);

  const envNeeds = plant.environmental_needs || {};
  const soilState = plant.soil_state || {};

  let rateMultiplier = 1.0;
  if (envNeeds.sunlight && soilState.sunlight && envNeeds.sunlight !== soilState.sunlight) {
    rateMultiplier *= 0.65;
  }
  if (envNeeds.pH && soilState.pH && envNeeds.pH !== soilState.pH) {
    rateMultiplier *= 0.7;
  }

  const elapsedSeconds = Math.max(0, nowEpochSeconds - plantedEpoch);
  const effectiveElapsed = elapsedSeconds * rateMultiplier;
  const progress = Math.min(1.0, effectiveElapsed / Math.max(1, baseDuration));
  const growthStage = Math.min(TOTAL_GROWTH_STAGES - 1, Math.floor(progress * (TOTAL_GROWTH_STAGES - 1)));

  return {
    growth_stage: growthStage,
    progress_ratio: Number(progress.toFixed(4)),
    is_harvestable: progress >= 1.0,
    elapsed_seconds: elapsedSeconds,
    last_evaluated_epoch: nowEpochSeconds,
    estimated_ready_epoch: Math.ceil(plantedEpoch + baseDuration / rateMultiplier)
  };
}

/**
 * Plants a reagent in a player's 3x3 homestead grid using authoritative server Epoch time.
 */
exports.plantReagent = onCall(async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Anonymous or signed-in auth is required.");
  }

  const uid = request.auth.uid;
  const { plotKey, itemId, environmentalNeeds, soilState, growthDurationSeconds } = request.data || {};

  if (!plotKey || !itemId) {
    throw new HttpsError("invalid-argument", "plotKey and itemId are required.");
  }

  const nowEpoch = Math.floor(Date.now() / 1000);
  const plantRecord = {
    item_id: itemId,
    location: plotKey,
    growth_stage: 0,
    environmental_needs: environmentalNeeds || { sunlight: "Full Sun", pH: "Neutral" },
    soil_state: soilState || { sunlight: "Full Sun", pH: "Neutral" },
    growth_duration_seconds: Number(growthDurationSeconds || DEFAULT_GROWTH_DURATION_SECONDS),
    time_planted_epoch: nowEpoch,
    last_evaluated_epoch: nowEpoch,
    is_harvestable: false
  };

  await getDatabase().ref(`players/${uid}/plants/${plotKey}`).set(plantRecord);
  return { status: "planted", plant: plantRecord };
});

/**
 * Evaluates all planted crops for the authenticated player against authoritative server Epoch time.
 */
exports.syncAfkPlantGrowth = onCall(async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Anonymous or signed-in auth is required.");
  }

  const uid = request.auth.uid;
  const db = getDatabase();
  const plantsRef = db.ref(`players/${uid}/plants`);
  const snapshot = await plantsRef.get();

  if (!snapshot.exists()) {
    return { server_epoch: Math.floor(Date.now() / 1000), updated_plants: {} };
  }

  const nowEpoch = Math.floor(Date.now() / 1000);
  const plants = snapshot.val();
  const updates = {};
  const evaluated = {};

  for (const [plotKey, plant] of Object.entries(plants)) {
    const computed = computePlantGrowthState(plant, nowEpoch);
    evaluated[plotKey] = { ...plant, ...computed };
    updates[`${plotKey}/growth_stage`] = computed.growth_stage;
    updates[`${plotKey}/progress_ratio`] = computed.progress_ratio;
    updates[`${plotKey}/is_harvestable`] = computed.is_harvestable;
    updates[`${plotKey}/last_evaluated_epoch`] = computed.last_evaluated_epoch;
    updates[`${plotKey}/estimated_ready_epoch`] = computed.estimated_ready_epoch;
  }

  await plantsRef.update(updates);
  return { server_epoch: nowEpoch, updated_plants: evaluated };
});

module.exports.computePlantGrowthState = computePlantGrowthState;
