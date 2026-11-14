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

console.log("All AFK Epoch growth unit tests passed.");
