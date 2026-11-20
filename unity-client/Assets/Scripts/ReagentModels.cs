using System;
using System.Collections.Generic;
using UnityEngine;

namespace GreenWitchApothecary.Data
{
    [Serializable]
    public class ReagentDefinition
    {
        public string item_id;
        public string display_name;
        public string biome_group;
        public string biome_source;
        public string target_ailment;
        public string crafting_process;
        public string pH_Value;
        public List<string> state_modifiers;
        public List<string> crafting_tags;
    }

    [Serializable]
    public class PlantInstanceData
    {
        public string item_id;
        public string location;
        public int growth_stage;
        public long time_planted_epoch;
        public long last_evaluated_epoch;
        public long estimated_ready_epoch;
        public bool is_harvestable;
    }

    /// <summary>
    /// Canonical string enum constants for the 7 Firebase NoSQL collections.
    /// </summary>
    public static class NoSqlSchemaConstants
    {
        // 1. users.backpack_tier
        public const string BackpackSatchel = "Satchel";
        public const string BackpackMediumRucksack = "Medium Rucksack";
        public const string BackpackLargeExpedition = "Large Expedition Backpack";

        // 2. world_state.current_season
        public const string SeasonSpring = "Spring";
        public const string SeasonSummer = "Summer";
        public const string SeasonAutumn = "Autumn";
        public const string SeasonWinter = "Winter";

        // 2. world_state.current_weather
        public const string WeatherClear = "Clear";
        public const string WeatherRain = "Rain";
        public const string WeatherFog = "Fog";
        public const string WeatherHeatwave = "Heatwave";

        // 3. inventories item quality
        public const string QualityStandard = "Standard";
        public const string QualityFine = "Fine";
        public const string QualityMasterwork = "Masterwork";

        // 5. workstations.workstation_type & ash trap output
        public const string WorkstationSmallWoodstove = "Small Woodstove";
        public const string WorkstationMediumWoodstove = "Medium Woodstove";
        public const string WorkstationLargeWoodstove = "Large Woodstove";
        public const string WorkstationDistiller = "Distiller";
        public const string WorkstationDryingRack = "Drying Rack";
        public const string ItemWoodAsh = "wood_ash";

        // 6. companions.companion_id & deployment_status
        public const string CompanionCrow = "Crow";
        public const string CompanionSquirrel = "Squirrel";
        public const string CompanionBadger = "Badger";
        public const string DeploymentIdle = "Idle";
        public const string DeploymentForaging = "Foraging";
    }

    /// <summary>
    /// 1. users (Player Profile) collection schema.
    /// </summary>
    [Serializable]
    public class UserProfileData
    {
        public string user_id = string.Empty;
        public int gold = 100;
        public int stamina = 100;
        public string backpack_tier = NoSqlSchemaConstants.BackpackSatchel;
        public List<string> unlocked_biomes = new List<string> { "Homestead", "Meadows" };

        public int GetBackpackSlotCapacity()
        {
            switch (backpack_tier)
            {
                case NoSqlSchemaConstants.BackpackLargeExpedition:
                    return 30;
                case NoSqlSchemaConstants.BackpackMediumRucksack:
                    return 20;
                default:
                    return 10;
            }
        }

        public bool HasExpeditionTent()
        {
            return backpack_tier == NoSqlSchemaConstants.BackpackLargeExpedition;
        }

        public void ClampRanges()
        {
            gold = Mathf.Max(0, gold);
            stamina = Mathf.Clamp(stamina, 0, 100);
            if (string.IsNullOrEmpty(backpack_tier))
            {
                backpack_tier = NoSqlSchemaConstants.BackpackSatchel;
            }
            if (unlocked_biomes == null || unlocked_biomes.Count == 0)
            {
                unlocked_biomes = new List<string> { "Homestead", "Meadows" };
            }
        }

        public Dictionary<string, object> ToDictionary()
        {
            ClampRanges();
            return new Dictionary<string, object>
            {
                { "user_id", user_id ?? string.Empty },
                { "gold", gold },
                { "stamina", stamina },
                { "backpack_tier", backpack_tier },
                { "unlocked_biomes", unlocked_biomes }
            };
        }
    }

    /// <summary>
    /// 2. world_state collection schema.
    /// </summary>
    [Serializable]
    public class WorldStateData
    {
        public string world_seed = "GWA-SPRING-0001";
        public string current_season = NoSqlSchemaConstants.SeasonSpring;
        public string current_weather = NoSqlSchemaConstants.WeatherClear;
        public long global_timestamp;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "world_seed", string.IsNullOrEmpty(world_seed) ? "GWA-SPRING-0001" : world_seed },
                { "current_season", string.IsNullOrEmpty(current_season) ? NoSqlSchemaConstants.SeasonSpring : current_season },
                { "current_weather", string.IsNullOrEmpty(current_weather) ? NoSqlSchemaConstants.WeatherClear : current_weather },
                { "global_timestamp", Math.Max(0L, global_timestamp) }
            };
        }
    }

    /// <summary>
    /// Item entry inside backpack, storage_chest, or icebox.
    /// </summary>
    [Serializable]
    public class InventoryItemData
    {
        public string item_id = string.Empty;
        public int quantity = 1;
        public string quality = NoSqlSchemaConstants.QualityStandard;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "item_id", item_id ?? string.Empty },
                { "quantity", Mathf.Max(1, quantity) },
                { "quality", string.IsNullOrEmpty(quality) ? NoSqlSchemaConstants.QualityStandard : quality }
            };
        }
    }

    /// <summary>
    /// Icebox container in inventories with decay_paused always true.
    /// </summary>
    [Serializable]
    public class IceboxInventoryData
    {
        public bool decay_paused = true;
        public List<InventoryItemData> items = new List<InventoryItemData>();

        public Dictionary<string, object> ToDictionary()
        {
            decay_paused = true;
            var serializedItems = new List<Dictionary<string, object>>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null && !string.IsNullOrEmpty(item.item_id))
                    {
                        serializedItems.Add(item.ToDictionary());
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "decay_paused", true },
                { "items", serializedItems }
            };
        }
    }

    /// <summary>
    /// 3. inventories collection schema.
    /// </summary>
    [Serializable]
    public class PlayerInventoriesData
    {
        public List<InventoryItemData> backpack = new List<InventoryItemData>();
        public List<InventoryItemData> storage_chest = new List<InventoryItemData>();
        public IceboxInventoryData icebox = new IceboxInventoryData();

        public Dictionary<string, object> ToDictionary(int maxBackpackSlots = 10)
        {
            var backpackList = new List<Dictionary<string, object>>();
            if (backpack != null)
            {
                int limit = Mathf.Min(backpack.Count, Mathf.Max(1, maxBackpackSlots));
                for (int i = 0; i < limit; i++)
                {
                    if (backpack[i] != null && !string.IsNullOrEmpty(backpack[i].item_id))
                    {
                        backpackList.Add(backpack[i].ToDictionary());
                    }
                }
            }

            var chestList = new List<Dictionary<string, object>>();
            if (storage_chest != null)
            {
                foreach (var item in storage_chest)
                {
                    if (item != null && !string.IsNullOrEmpty(item.item_id))
                    {
                        chestList.Add(item.ToDictionary());
                    }
                }
            }

            if (icebox == null)
            {
                icebox = new IceboxInventoryData();
            }

            return new Dictionary<string, object>
            {
                { "backpack", backpackList },
                { "storage_chest", chestList },
                { "icebox", icebox.ToDictionary() }
            };
        }
    }

    /// <summary>
    /// 4. garden_plots collection schema.
    /// </summary>
    [Serializable]
    public class GardenPlotData
    {
        public string plot_id = "A1";
        public string planted_seed = string.Empty;
        public float soil_ph = 6.5f;
        public bool hydration_state = true;
        public long death_timer_timestamp;

        public bool IsWithered(long currentEpochMs)
        {
            return !string.IsNullOrEmpty(planted_seed) &&
                   death_timer_timestamp > 0 &&
                   currentEpochMs >= death_timer_timestamp;
        }

        public Dictionary<string, object> ToDictionary()
        {
            soil_ph = Mathf.Clamp(soil_ph, 0f, 14f);
            return new Dictionary<string, object>
            {
                { "plot_id", plot_id ?? string.Empty },
                { "planted_seed", planted_seed ?? string.Empty },
                { "soil_ph", Math.Round(soil_ph, 2) },
                { "hydration_state", hydration_state },
                { "death_timer_timestamp", Math.Max(0L, death_timer_timestamp) }
            };
        }
    }

    /// <summary>
    /// Snapshot of a workstation's AFK completion_timestamp and Ash Trap evaluation.
    /// </summary>
    [Serializable]
    public struct WorkstationAfkEvaluation
    {
        public string workstation_id;
        public string workstation_type;
        public string active_recipe;
        public long completion_timestamp;
        public int ash_level;
        public int ash_capacity;
        public bool is_ash_trap_full;
        public bool is_complete;
        public long remaining_ms;
        public bool preserved_state;
        public int burn_penalty;
        public int rot_penalty;
    }

    /// <summary>
    /// 5. workstations (AFK Crafting Engine) collection schema.
    /// If current epoch ms >= completion_timestamp, output is ready in a preserved state
    /// with zero burn or rot penalties. Woodstoves track ash_level against ash_capacity
    /// and block new cooks when IsAshTrapFull() is true until emptied.
    /// </summary>
    [Serializable]
    public class WorkstationData
    {
        public string workstation_id = "stove_slot_1";
        public string workstation_type = NoSqlSchemaConstants.WorkstationSmallWoodstove;
        public string active_recipe = string.Empty;
        public long completion_timestamp;
        public int ash_level;
        public int ash_capacity = 10;

        public static int GetDefaultAshCapacity(string type)
        {
            switch (type)
            {
                case NoSqlSchemaConstants.WorkstationSmallWoodstove:
                    return 10;
                case NoSqlSchemaConstants.WorkstationMediumWoodstove:
                    return 20;
                case NoSqlSchemaConstants.WorkstationLargeWoodstove:
                    return 30;
                default:
                    return 0;
            }
        }

        public bool IsWoodstove()
        {
            return workstation_type == NoSqlSchemaConstants.WorkstationSmallWoodstove ||
                   workstation_type == NoSqlSchemaConstants.WorkstationMediumWoodstove ||
                   workstation_type == NoSqlSchemaConstants.WorkstationLargeWoodstove;
        }

        public int GetEffectiveAshCapacity()
        {
            if (ash_capacity > 0) return ash_capacity;
            return GetDefaultAshCapacity(workstation_type);
        }

        /// <summary>
        /// Returns true when a woodstove's ash_level >= ash_capacity, blocking new cooks
        /// until the ash trap is emptied for Wood Ash (Pearlash/soap engine).
        /// </summary>
        public bool IsAshTrapFull()
        {
            int capacity = GetEffectiveAshCapacity();
            return capacity > 0 && Mathf.Max(0, ash_level) >= capacity;
        }

        public bool HasActiveRecipe()
        {
            return !string.IsNullOrEmpty(active_recipe) && completion_timestamp > 0;
        }

        public bool IsReadyInPreservedState(long currentEpochMs)
        {
            return HasActiveRecipe() && currentEpochMs >= completion_timestamp;
        }

        public long GetRemainingTimeMs(long currentEpochMs)
        {
            if (!HasActiveRecipe()) return 0L;
            return Math.Max(0L, completion_timestamp - currentEpochMs);
        }

        public WorkstationAfkEvaluation EvaluateAfkState(long currentEpochMs)
        {
            int effectiveCap = GetEffectiveAshCapacity();
            int clampedAsh = Mathf.Max(0, ash_level);
            bool complete = IsReadyInPreservedState(currentEpochMs);
            return new WorkstationAfkEvaluation
            {
                workstation_id = workstation_id,
                workstation_type = workstation_type,
                active_recipe = active_recipe,
                completion_timestamp = completion_timestamp,
                ash_level = clampedAsh,
                ash_capacity = effectiveCap,
                is_ash_trap_full = IsAshTrapFull(),
                is_complete = complete,
                remaining_ms = GetRemainingTimeMs(currentEpochMs),
                preserved_state = true,
                burn_penalty = 0,
                rot_penalty = 0
            };
        }

        public Dictionary<string, object> ToDictionary()
        {
            string resolvedType = string.IsNullOrEmpty(workstation_type)
                ? NoSqlSchemaConstants.WorkstationSmallWoodstove
                : workstation_type;
            ash_capacity = IsWoodstove() ? Mathf.Max(1, GetEffectiveAshCapacity()) : Mathf.Max(0, ash_capacity);
            ash_level = Mathf.Max(0, ash_level);

            return new Dictionary<string, object>
            {
                { "workstation_id", workstation_id ?? string.Empty },
                { "workstation_type", resolvedType },
                { "active_recipe", active_recipe ?? string.Empty },
                { "completion_timestamp", Math.Max(0L, completion_timestamp) },
                { "ash_level", ash_level },
                { "ash_capacity", ash_capacity }
            };
        }
    }

    /// <summary>
    /// 6. companions collection schema.
    /// </summary>
    [Serializable]
    public class CompanionData
    {
        public string companion_id = NoSqlSchemaConstants.CompanionCrow;
        public int affinity_level = 1;
        public string deployment_status = NoSqlSchemaConstants.DeploymentIdle;
        public string target_biome = "Meadows";
        public long return_timestamp;

        public bool IsLootReadyToResolve(long currentEpochMs)
        {
            return deployment_status == NoSqlSchemaConstants.DeploymentForaging &&
                   return_timestamp > 0 &&
                   currentEpochMs >= return_timestamp;
        }

        public Dictionary<string, object> ToDictionary()
        {
            affinity_level = Mathf.Clamp(affinity_level, 1, 100);
            return new Dictionary<string, object>
            {
                { "companion_id", string.IsNullOrEmpty(companion_id) ? NoSqlSchemaConstants.CompanionCrow : companion_id },
                { "affinity_level", affinity_level },
                { "deployment_status", string.IsNullOrEmpty(deployment_status) ? NoSqlSchemaConstants.DeploymentIdle : deployment_status },
                { "target_biome", target_biome ?? "Meadows" },
                { "return_timestamp", Math.Max(0L, return_timestamp) }
            };
        }
    }

    [Serializable]
    public class ContractRequestedItemData
    {
        public string item_id = string.Empty;
        public int quantity = 1;

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                { "item_id", item_id ?? string.Empty },
                { "quantity", Mathf.Max(1, quantity) }
            };
        }
    }

    [Serializable]
    public class ContractData
    {
        public string contract_id = string.Empty;
        public string target_faction = string.Empty;
        public List<ContractRequestedItemData> requested_items = new List<ContractRequestedItemData>();
        public long expiration_timestamp;

        public bool IsExpired(long currentEpochMs)
        {
            return expiration_timestamp > 0 && currentEpochMs >= expiration_timestamp;
        }

        public Dictionary<string, object> ToDictionary()
        {
            var itemsList = new List<Dictionary<string, object>>();
            if (requested_items != null)
            {
                foreach (var req in requested_items)
                {
                    if (req != null && !string.IsNullOrEmpty(req.item_id))
                    {
                        itemsList.Add(req.ToDictionary());
                    }
                }
            }

            return new Dictionary<string, object>
            {
                { "contract_id", contract_id ?? string.Empty },
                { "target_faction", target_faction ?? string.Empty },
                { "requested_items", itemsList },
                { "expiration_timestamp", Math.Max(0L, expiration_timestamp) }
            };
        }
    }

    /// <summary>
    /// 7. economy_and_karma collection schema.
    /// Expired contracts are silently removed with zero Karma penalty.
    /// </summary>
    [Serializable]
    public class EconomyAndKarmaData
    {
        public int village_karma;
        public int merchants_guild_rep;
        public List<ContractData> active_contracts = new List<ContractData>();

        /// <summary>
        /// Silently removes contracts past expiration_timestamp without applying any Karma penalty.
        /// </summary>
        public int PruneExpiredContracts(long currentEpochMs)
        {
            if (active_contracts == null)
            {
                active_contracts = new List<ContractData>();
                return 0;
            }
            return active_contracts.RemoveAll(c => c == null || c.IsExpired(currentEpochMs));
        }

        public Dictionary<string, object> ToDictionary(long currentEpochMs)
        {
            PruneExpiredContracts(currentEpochMs);
            village_karma = Mathf.Clamp(village_karma, -100, 100);
            merchants_guild_rep = Mathf.Clamp(merchants_guild_rep, 0, 100);

            var contractsList = new List<Dictionary<string, object>>();
            foreach (var contract in active_contracts)
            {
                if (contract != null && !string.IsNullOrEmpty(contract.contract_id))
                {
                    contractsList.Add(contract.ToDictionary());
                }
            }

            return new Dictionary<string, object>
            {
                { "village_karma", village_karma },
                { "merchants_guild_rep", merchants_guild_rep },
                { "active_contracts", contractsList }
            };
        }
    }
}

