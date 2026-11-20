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

const VALID_WORKSTATION_TYPES = new Set([
  "Small Woodstove",
  "Medium Woodstove",
  "Large Woodstove",
  "Distiller",
  "Drying Rack"
]);

const DEFAULT_ASH_CAPACITY_BY_TYPE = {
  "Small Woodstove": 10,
  "Medium Woodstove": 20,
  "Large Woodstove": 30,
  "Distiller": 0,
  "Drying Rack": 0
};

function getDefaultAshCapacity(workstationType) {
  return DEFAULT_ASH_CAPACITY_BY_TYPE[workstationType] ?? 0;
}

/**
 * Returns true if the workstation has an ash trap (ash_capacity > 0)
 * and ash_level >= ash_capacity, blocking new cooks until emptied.
 */
function isAshTrapFull(workstation) {
  if (!workstation) return false;
  const resolvedType = workstation.workstation_type || "Small Woodstove";
  const ashCapacity =
    workstation.ash_capacity !== undefined && workstation.ash_capacity !== null
      ? Math.max(0, Number(workstation.ash_capacity))
      : getDefaultAshCapacity(resolvedType);
  const ashLevel = Math.max(0, Number(workstation.ash_level || 0));
  return ashCapacity > 0 && ashLevel >= ashCapacity;
}

/**
 * Empties the ash trap on a woodstove, resetting ash_level to 0 and yielding
 * collected Wood Ash for the Pearlash/soap engine.
 */
function emptyWorkstationAshTrap(workstation) {
  const resolvedType = (workstation && workstation.workstation_type) || "Small Woodstove";
  const ashCapacity =
    workstation && workstation.ash_capacity !== undefined && workstation.ash_capacity !== null
      ? Math.max(0, Number(workstation.ash_capacity))
      : getDefaultAshCapacity(resolvedType);
  const collectedWoodAsh = Math.max(0, Number((workstation && workstation.ash_level) || 0));

  return {
    collected_wood_ash: collectedWoodAsh,
    item_id: "wood_ash",
    workstation: {
      ...workstation,
      workstation_type: resolvedType,
      ash_level: 0,
      ash_capacity: ashCapacity
    }
  };
}

/**
 * Validates and prepares a new workstation crafting record.
 * Blocks starting a new cook if ash_level >= ash_capacity on a woodstove.
 */
function prepareWorkstationCraft(existingWorkstation, params, nowEpochMs) {
  const { workstationId, workstationType, activeRecipe, durationMs, ashLevel, ashCapacity } =
    params || {};
  if (!workstationId || !activeRecipe) {
    throw new HttpsError("invalid-argument", "workstationId and activeRecipe are required.");
  }

  const resolvedType =
    workstationType ||
    (existingWorkstation && existingWorkstation.workstation_type) ||
    "Small Woodstove";
  if (!VALID_WORKSTATION_TYPES.has(resolvedType)) {
    throw new HttpsError("invalid-argument", `Unsupported workstationType: ${resolvedType}`);
  }

  const resolvedCapacity =
    ashCapacity !== undefined && ashCapacity !== null
      ? Math.max(0, Number(ashCapacity))
      : existingWorkstation &&
          existingWorkstation.ash_capacity !== undefined &&
          existingWorkstation.ash_capacity !== null
        ? Math.max(0, Number(existingWorkstation.ash_capacity))
        : getDefaultAshCapacity(resolvedType);

  const currentAshLevel =
    ashLevel !== undefined && ashLevel !== null
      ? Math.max(0, Number(ashLevel))
      : Math.max(0, Number((existingWorkstation && existingWorkstation.ash_level) || 0));

  if (isAshTrapFull({ workstation_type: resolvedType, ash_level: currentAshLevel, ash_capacity: resolvedCapacity })) {
    throw new HttpsError(
      "failed-precondition",
      `Woodstove ash trap is full (${currentAshLevel}/${resolvedCapacity}). Empty the ash trap before starting a new cook.`
    );
  }

  const craftDurationMs = Math.max(0, Number(durationMs ?? 60000));
  const nextAshLevel =
    resolvedCapacity > 0 ? Math.min(resolvedCapacity, currentAshLevel + 1) : 0;

  return {
    workstation_id: workstationId,
    workstation_type: resolvedType,
    active_recipe: activeRecipe,
    completion_timestamp: nowEpochMs + craftDurationMs,
    ash_level: nextAshLevel,
    ash_capacity: resolvedCapacity
  };
}

/**
 * Evaluates a workstation's AFK completion_timestamp (Unix epoch ms).
 * When nowEpochMs >= completion_timestamp, the crafted output is ready in a
 * preserved state with zero burn or rot penalties regardless of overtime duration.
 */
function computeWorkstationAfkState(workstation, nowEpochMs) {
  const resolvedType = workstation.workstation_type || "Small Woodstove";
  const activeRecipe = workstation.active_recipe || "";
  const completionTimestamp = Number(workstation.completion_timestamp || 0);
  const ashCapacity =
    workstation.ash_capacity !== undefined && workstation.ash_capacity !== null
      ? Math.max(0, Number(workstation.ash_capacity))
      : getDefaultAshCapacity(resolvedType);
  const ashLevel = Math.max(0, Number(workstation.ash_level || 0));
  const hasActiveRecipe = Boolean(activeRecipe) && completionTimestamp > 0;
  const isComplete = hasActiveRecipe && nowEpochMs >= completionTimestamp;
  const remainingMs = hasActiveRecipe ? Math.max(0, completionTimestamp - nowEpochMs) : 0;

  return {
    workstation_id: workstation.workstation_id || "",
    workstation_type: resolvedType,
    active_recipe: activeRecipe,
    completion_timestamp: completionTimestamp,
    ash_level: ashLevel,
    ash_capacity: ashCapacity,
    is_ash_trap_full: isAshTrapFull({ workstation_type: resolvedType, ash_level: ashLevel, ash_capacity: ashCapacity }),
    is_complete: isComplete,
    remaining_ms: remainingMs,
    preserved_state: true,
    burn_penalty: 0,
    rot_penalty: 0,
    status: !hasActiveRecipe ? "Idle" : isComplete ? "Completed" : "Crafting",
    last_evaluated_timestamp: nowEpochMs
  };
}

/**
 * Evaluates a garden_plots entry against death_timer_timestamp (Unix epoch ms).
 */
function computeGardenPlotAfkState(plot, nowEpochMs) {
  const deathTimer = Number(plot.death_timer_timestamp || 0);
  const hasSeed = Boolean(plot.planted_seed);
  const isWithered = hasSeed && deathTimer > 0 && nowEpochMs >= deathTimer;

  return {
    plot_id: plot.plot_id || "",
    planted_seed: plot.planted_seed || "",
    soil_ph: Number(plot.soil_ph ?? 6.5),
    hydration_state: isWithered ? false : Boolean(plot.hydration_state),
    death_timer_timestamp: deathTimer,
    is_withered: isWithered
  };
}

/**
 * Evaluates a companion's foraging return_timestamp (Unix epoch ms).
 */
function computeCompanionAfkState(companion, nowEpochMs) {
  const returnTimestamp = Number(companion.return_timestamp || 0);
  const isForaging = companion.deployment_status === "Foraging";
  const isLootReady = isForaging && returnTimestamp > 0 && nowEpochMs >= returnTimestamp;

  return {
    companion_id: companion.companion_id || "Crow",
    affinity_level: Math.min(100, Math.max(1, Number(companion.affinity_level || 1))),
    deployment_status: companion.deployment_status || "Idle",
    target_biome: companion.target_biome || "Meadows",
    return_timestamp: returnTimestamp,
    is_loot_ready: isLootReady
  };
}

/**
 * Silently removes expired contracts from economy_and_karma with zero Karma penalty.
 */
function pruneExpiredContracts(economyAndKarma, nowEpochMs) {
  const contracts = Array.isArray(economyAndKarma.active_contracts)
    ? economyAndKarma.active_contracts
    : [];
  const activeContracts = contracts.filter((contract) => {
    const exp = Number(contract.expiration_timestamp || 0);
    return exp <= 0 || nowEpochMs < exp;
  });

  return {
    village_karma: Math.min(100, Math.max(-100, Number(economyAndKarma.village_karma || 0))),
    merchants_guild_rep: Math.min(100, Math.max(0, Number(economyAndKarma.merchants_guild_rep || 0))),
    active_contracts: activeContracts,
    expired_removed_count: contracts.length - activeContracts.length,
    karma_penalty_applied: 0
  };
}

/**
 * Starts an AFK crafting job on a player's workstation using authoritative server epoch ms.
 * Blocks starting a new cook if the woodstove ash trap is full (ash_level >= ash_capacity).
 */
exports.startWorkstationCraft = onCall(async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Anonymous or signed-in auth is required.");
  }

  const uid = request.auth.uid;
  const { workstationId, workstationType, activeRecipe, durationMs, ashLevel, ashCapacity } =
    request.data || {};

  if (!workstationId || !activeRecipe) {
    throw new HttpsError("invalid-argument", "workstationId and activeRecipe are required.");
  }

  const db = getDatabase();
  const wsRef = db.ref(`players/${uid}/workstations/${workstationId}`);
  const existingSnapshot = await wsRef.get();
  const existingWorkstation = existingSnapshot.exists() ? existingSnapshot.val() : null;

  const nowEpochMs = Date.now();
  const record = prepareWorkstationCraft(
    existingWorkstation,
    { workstationId, workstationType, activeRecipe, durationMs, ashLevel, ashCapacity },
    nowEpochMs
  );

  await wsRef.set(record);
  await db.ref(`players/${uid}/world_state/global_timestamp`).set(nowEpochMs);

  return {
    status: "crafting_started",
    server_timestamp: nowEpochMs,
    workstation: computeWorkstationAfkState(record, nowEpochMs)
  };
});

/**
 * Evaluates all player workstations against authoritative server epoch ms,
 * ensuring completed recipes remain in a preserved state with zero burn/rot penalties.
 */
exports.syncAfkWorkstations = onCall(async (request) => {
  if (!request.auth) {
    throw new HttpsError("unauthenticated", "Anonymous or signed-in auth is required.");
  }

  const uid = request.auth.uid;
  const db = getDatabase();
  const nowEpochMs = Date.now();
  const workstationsRef = db.ref(`players/${uid}/workstations`);
  const snapshot = await workstationsRef.get();

  await db.ref(`players/${uid}/world_state/global_timestamp`).set(nowEpochMs);

  if (!snapshot.exists()) {
    return { global_timestamp: nowEpochMs, workstations: {} };
  }

  const rawWorkstations = snapshot.val();
  const evaluated = {};
  for (const [workstationId, ws] of Object.entries(rawWorkstations)) {
    evaluated[workstationId] = computeWorkstationAfkState(
      { ...ws, workstation_id: ws.workstation_id || workstationId },
      nowEpochMs
    );
  }

  return { global_timestamp: nowEpochMs, workstations: evaluated };
});

module.exports.computePlantGrowthState = computePlantGrowthState;
module.exports.getDefaultAshCapacity = getDefaultAshCapacity;
module.exports.isAshTrapFull = isAshTrapFull;
module.exports.emptyWorkstationAshTrap = emptyWorkstationAshTrap;
module.exports.prepareWorkstationCraft = prepareWorkstationCraft;
module.exports.computeWorkstationAfkState = computeWorkstationAfkState;
module.exports.computeGardenPlotAfkState = computeGardenPlotAfkState;
module.exports.computeCompanionAfkState = computeCompanionAfkState;
module.exports.pruneExpiredContracts = pruneExpiredContracts;


