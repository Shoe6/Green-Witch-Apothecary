const assert = require("node:assert/strict");
const { computePlantGrowthState } = require("./index");

const planted = 1700000000;

// 1. Halfway through ideal 3600s growth
const half = computePlantGrowthState(
  {
    time_planted_epoch: planted,
    growth_duration_seconds: 3600,
    environmental_needs: { sunlight: "Full Sun", pH: "Neutral" },
    soil_state: { sunlight: "Full Sun", pH: "Neutral" },
  },
  planted + 1800
);
assert.equal(half.progress_ratio, 0.5);
assert.equal(half.growth_stage, 1);
assert.equal(half.is_harvestable, false);

// 2. Mature after 3600s
const mature = computePlantGrowthState(
  {
    time_planted_epoch: planted,
    growth_duration_seconds: 3600,
    environmental_needs: { sunlight: "Full Sun", pH: "Neutral" },
    soil_state: { sunlight: "Full Sun", pH: "Neutral" },
  },
  planted + 3600
);
assert.equal(mature.progress_ratio, 1.0);
assert.equal(mature.growth_stage, 3);
assert.equal(mature.is_harvestable, true);

// 3. Mismatched soil pH slows growth by 0.7x
const slowed = computePlantGrowthState(
  {
    time_planted_epoch: planted,
    growth_duration_seconds: 3600,
    environmental_needs: { sunlight: "Full Sun", pH: "Acidic" },
    soil_state: { sunlight: "Full Sun", pH: "Neutral" },
  },
  planted + 3600
);
assert.equal(slowed.progress_ratio, 0.7);
assert.equal(slowed.is_harvestable, false);

const {
  isAshTrapFull,
  emptyWorkstationAshTrap,
  prepareWorkstationCraft,
  computeWorkstationAfkState,
  computeGardenPlotAfkState,
  computeCompanionAfkState,
  pruneExpiredContracts,
} = require("./index");

const baseEpochMs = 1732142080000;

// 4. Workstation AFK completion_timestamp in-progress vs completed in preserved state
const wsCrafting = computeWorkstationAfkState(
  {
    workstation_id: "stove_slot_1",
    workstation_type: "Small Woodstove",
    active_recipe: "weavers_rest_draught",
    completion_timestamp: baseEpochMs + 60000,
    ash_level: 2,
    ash_capacity: 10,
  },
  baseEpochMs + 15000
);
assert.equal(wsCrafting.is_complete, false);
assert.equal(wsCrafting.remaining_ms, 45000);
assert.equal(wsCrafting.status, "Crafting");
assert.equal(wsCrafting.ash_level, 2);
assert.equal(wsCrafting.ash_capacity, 10);
assert.equal(wsCrafting.is_ash_trap_full, false);

// Even 24 hours past completion_timestamp, item is preserved with zero burn/rot penalties
const wsCompletedAfk = computeWorkstationAfkState(
  {
    workstation_id: "stove_slot_1",
    workstation_type: "Small Woodstove",
    active_recipe: "weavers_rest_draught",
    completion_timestamp: baseEpochMs + 60000,
    ash_level: 3,
    ash_capacity: 10,
  },
  baseEpochMs + 86400000
);
assert.equal(wsCompletedAfk.is_complete, true);
assert.equal(wsCompletedAfk.remaining_ms, 0);
assert.equal(wsCompletedAfk.preserved_state, true);
assert.equal(wsCompletedAfk.burn_penalty, 0);
assert.equal(wsCompletedAfk.rot_penalty, 0);
assert.equal(wsCompletedAfk.status, "Completed");

// 5. Garden plot death timer check
const plotAlive = computeGardenPlotAfkState(
  {
    plot_id: "A1",
    planted_seed: "herb_chamomile",
    soil_ph: 6.5,
    hydration_state: true,
    death_timer_timestamp: baseEpochMs + 120000,
  },
  baseEpochMs + 60000
);
assert.equal(plotAlive.is_withered, false);

const plotWithered = computeGardenPlotAfkState(
  {
    plot_id: "A1",
    planted_seed: "herb_chamomile",
    soil_ph: 6.5,
    hydration_state: true,
    death_timer_timestamp: baseEpochMs + 120000,
  },
  baseEpochMs + 180000
);
assert.equal(plotWithered.is_withered, true);

// 6. Companion foraging return_timestamp resolution
const crowReturned = computeCompanionAfkState(
  {
    companion_id: "Crow",
    affinity_level: 5,
    deployment_status: "Foraging",
    target_biome: "Meadows",
    return_timestamp: baseEpochMs + 30000,
  },
  baseEpochMs + 30000
);
assert.equal(crowReturned.is_loot_ready, true);

// 7. Economy & Karma silent contract expiration with zero karma penalty
const prunedEconomy = pruneExpiredContracts(
  {
    village_karma: 25,
    merchants_guild_rep: 40,
    active_contracts: [
      {
        contract_id: "expired_01",
        target_faction: "Village",
        requested_items: [{ item_id: "herb_yarrow", quantity: 2 }],
        expiration_timestamp: baseEpochMs - 1000,
      },
      {
        contract_id: "active_02",
        target_faction: "Merchants Guild",
        requested_items: [{ item_id: "st_johns_ease_balm", quantity: 1 }],
        expiration_timestamp: baseEpochMs + 60000,
      },
    ],
  },
  baseEpochMs
);
assert.equal(prunedEconomy.active_contracts.length, 1);
assert.equal(prunedEconomy.active_contracts[0].contract_id, "active_02");
assert.equal(prunedEconomy.village_karma, 25);
assert.equal(prunedEconomy.karma_penalty_applied, 0);

// 8. Woodstove Ash Trap mechanic:
// (a) Block a new woodstove craft when ash_level >= ash_capacity
const fullWoodstove = {
  workstation_id: "stove_slot_1",
  workstation_type: "Small Woodstove",
  active_recipe: "",
  completion_timestamp: 0,
  ash_level: 10,
  ash_capacity: 10,
};
assert.equal(isAshTrapFull(fullWoodstove), true);
assert.throws(
  () =>
    prepareWorkstationCraft(
      fullWoodstove,
      {
        workstationId: "stove_slot_1",
        workstationType: "Small Woodstove",
        activeRecipe: "weavers_rest_draught",
        durationMs: 60000,
      },
      baseEpochMs
    ),
  (err) => err.code === "failed-precondition" && /ash trap is full/i.test(err.message)
);

// (b) Allow the craft once the ash trap is emptied (generating Wood Ash for Pearlash/soap engine)
const emptied = emptyWorkstationAshTrap(fullWoodstove);
assert.equal(emptied.collected_wood_ash, 10);
assert.equal(emptied.item_id, "wood_ash");
assert.equal(emptied.workstation.ash_level, 0);
assert.equal(isAshTrapFull(emptied.workstation), false);

const startedAfterEmpty = prepareWorkstationCraft(
  emptied.workstation,
  {
    workstationId: "stove_slot_1",
    workstationType: "Small Woodstove",
    activeRecipe: "weavers_rest_draught",
    durationMs: 60000,
  },
  baseEpochMs
);
assert.equal(startedAfterEmpty.active_recipe, "weavers_rest_draught");
assert.equal(startedAfterEmpty.completion_timestamp, baseEpochMs + 60000);
assert.equal(startedAfterEmpty.ash_level, 1);
assert.equal(startedAfterEmpty.ash_capacity, 10);

console.log("All AFK Epoch growth, Woodstove Ash Trap, and 7 NoSQL schema unit tests passed.");


