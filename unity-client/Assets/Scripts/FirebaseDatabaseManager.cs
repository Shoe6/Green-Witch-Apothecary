using System;
using System.Collections.Generic;
using UnityEngine;

namespace GreenWitchApothecary.Data
{
    /// <summary>
    /// Coordinates Firebase Anonymous Authentication, the 7 NoSQL RTDB collections
    /// under players/{uid}/ (users, world_state, inventories, garden_plots, workstations,
    /// companions, economy_and_karma), and AFK Unix epoch millisecond synchronization
    /// including workstation completion_timestamp preservation (zero burn/rot penalties).
    /// Requires Firebase Unity SDK (FirebaseAuth, FirebaseDatabase, FirebaseFunctions).
    /// </summary>
    public class FirebaseDatabaseManager : MonoBehaviour
    {
        public const string CollectionUsers = "users";
        public const string CollectionWorldState = "world_state";
        public const string CollectionInventories = "inventories";
        public const string CollectionGardenPlots = "garden_plots";
        public const string CollectionWorkstations = "workstations";
        public const string CollectionCompanions = "companions";
        public const string CollectionEconomyAndKarma = "economy_and_karma";

        public string UserId { get; private set; }
        public bool IsAuthenticated { get; private set; }

        [Header("7 NoSQL Collections (Runtime State)")]
        public UserProfileData UserProfile { get; private set; } = new UserProfileData();
        public WorldStateData WorldState { get; private set; } = new WorldStateData();
        public PlayerInventoriesData Inventories { get; private set; } = new PlayerInventoriesData();
        public Dictionary<string, GardenPlotData> GardenPlots { get; private set; } = new Dictionary<string, GardenPlotData>();
        public Dictionary<string, WorkstationData> Workstations { get; private set; } = new Dictionary<string, WorkstationData>();
        public Dictionary<string, CompanionData> Companions { get; private set; } = new Dictionary<string, CompanionData>();
        public EconomyAndKarmaData EconomyAndKarma { get; private set; } = new EconomyAndKarmaData();

        public event Action<string> OnAuthenticated;
        public event Action<string> OnSaveCompleted;
        public event Action<WorkstationAfkEvaluation> OnWorkstationCompleted;
        public event Action<string> OnAshTrapBlocked;
        public event Action<string, int> OnAshTrapEmptied;
        public event Action<CompanionData> OnCompanionLootReady;
        public event Action<int> OnExpiredContractsPruned;

        private void Awake()
        {
            InitializeDefaultCollections("local_witch_uid");
        }

        private void Start()
        {
            SignInAnonymously();
        }

        public static long GetCurrentEpochMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Initializes all 7 NoSQL collections with schema-compliant defaults.
        /// </summary>
        public void InitializeDefaultCollections(string uid)
        {
            long nowMs = GetCurrentEpochMs();
            UserId = string.IsNullOrEmpty(uid) ? "local_witch_uid" : uid;

            UserProfile = new UserProfileData
            {
                user_id = UserId,
                gold = 100,
                stamina = 100,
                backpack_tier = NoSqlSchemaConstants.BackpackSatchel,
                unlocked_biomes = new List<string> { "Homestead", "Meadows" }
            };

            WorldState = new WorldStateData
            {
                world_seed = "GWA-SPRING-0001",
                current_season = NoSqlSchemaConstants.SeasonSpring,
                current_weather = NoSqlSchemaConstants.WeatherClear,
                global_timestamp = nowMs
            };

            Inventories = new PlayerInventoriesData
            {
                backpack = new List<InventoryItemData>(),
                storage_chest = new List<InventoryItemData>(),
                icebox = new IceboxInventoryData
                {
                    decay_paused = true,
                    items = new List<InventoryItemData>()
                }
            };

            GardenPlots = new Dictionary<string, GardenPlotData>
            {
                {
                    "A1",
                    new GardenPlotData
                    {
                        plot_id = "A1",
                        planted_seed = string.Empty,
                        soil_ph = 6.5f,
                        hydration_state = true,
                        death_timer_timestamp = 0L
                    }
                }
            };

            Workstations = new Dictionary<string, WorkstationData>
            {
                {
                    "stove_slot_1",
                    new WorkstationData
                    {
                        workstation_id = "stove_slot_1",
                        workstation_type = NoSqlSchemaConstants.WorkstationSmallWoodstove,
                        active_recipe = string.Empty,
                        completion_timestamp = 0L,
                        ash_level = 0,
                        ash_capacity = WorkstationData.GetDefaultAshCapacity(NoSqlSchemaConstants.WorkstationSmallWoodstove)
                    }
                }
            };

            Companions = new Dictionary<string, CompanionData>
            {
                {
                    NoSqlSchemaConstants.CompanionCrow,
                    new CompanionData
                    {
                        companion_id = NoSqlSchemaConstants.CompanionCrow,
                        affinity_level = 1,
                        deployment_status = NoSqlSchemaConstants.DeploymentIdle,
                        target_biome = "Meadows",
                        return_timestamp = 0L
                    }
                },
                {
                    NoSqlSchemaConstants.CompanionSquirrel,
                    new CompanionData
                    {
                        companion_id = NoSqlSchemaConstants.CompanionSquirrel,
                        affinity_level = 1,
                        deployment_status = NoSqlSchemaConstants.DeploymentIdle,
                        target_biome = "Homestead",
                        return_timestamp = 0L
                    }
                },
                {
                    NoSqlSchemaConstants.CompanionBadger,
                    new CompanionData
                    {
                        companion_id = NoSqlSchemaConstants.CompanionBadger,
                        affinity_level = 1,
                        deployment_status = NoSqlSchemaConstants.DeploymentIdle,
                        target_biome = "Meadows",
                        return_timestamp = 0L
                    }
                }
            };

            EconomyAndKarma = new EconomyAndKarmaData
            {
                village_karma = 0,
                merchants_guild_rep = 0,
                active_contracts = new List<ContractData>()
            };
        }

        public void SignInAnonymously()
        {
#if FIREBASE_AUTH_AVAILABLE
            Firebase.Auth.FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result != null)
                {
                    UserId = task.Result.User.UserId;
                    UserProfile.user_id = UserId;
                    IsAuthenticated = true;
                    OnAuthenticated?.Invoke(UserId);
                }
            });
#else
            Debug.Log("[FirebaseDatabaseManager] Firebase SDK not yet imported in Unity Editor.");
#endif
        }

        /// <summary>
        /// Starts an AFK crafting recipe on a workstation using Unix epoch milliseconds.
        /// Blocks starting a new cook on woodstoves if ash_level >= ash_capacity until EmptyAshTrap is called.
        /// When currentEpochMs >= completion_timestamp, output is ready in a preserved state
        /// with zero burn or rot penalties.
        /// </summary>
        public WorkstationData StartWorkstationCrafting(
            string workstationId,
            string workstationType,
            string recipeId,
            long craftDurationMs)
        {
            if (string.IsNullOrEmpty(workstationId))
            {
                workstationId = "stove_slot_1";
            }

            Workstations.TryGetValue(workstationId, out var existing);
            string resolvedType = !string.IsNullOrEmpty(workstationType)
                ? workstationType
                : existing != null && !string.IsNullOrEmpty(existing.workstation_type)
                    ? existing.workstation_type
                    : NoSqlSchemaConstants.WorkstationSmallWoodstove;

            int resolvedCapacity = existing != null && existing.ash_capacity > 0
                ? existing.ash_capacity
                : WorkstationData.GetDefaultAshCapacity(resolvedType);
            int currentAshLevel = existing != null ? Mathf.Max(0, existing.ash_level) : 0;

            var candidate = new WorkstationData
            {
                workstation_id = workstationId,
                workstation_type = resolvedType,
                active_recipe = recipeId ?? string.Empty,
                completion_timestamp = 0L,
                ash_level = currentAshLevel,
                ash_capacity = resolvedCapacity
            };

            if (candidate.IsAshTrapFull())
            {
                Debug.LogWarning(
                    $"[FirebaseDatabaseManager] Blocked new cook on '{workstationId}': Woodstove ash trap is full ({currentAshLevel}/{resolvedCapacity}). Empty the ash trap first.");
                OnAshTrapBlocked?.Invoke(workstationId);
                return null;
            }

            long nowMs = GetCurrentEpochMs();
            long completionMs = string.IsNullOrEmpty(recipeId)
                ? 0L
                : nowMs + Math.Max(0L, craftDurationMs);

            candidate.completion_timestamp = completionMs;
            if (!string.IsNullOrEmpty(recipeId) && resolvedCapacity > 0)
            {
                candidate.ash_level = Mathf.Min(resolvedCapacity, currentAshLevel + 1);
            }

            Workstations[workstationId] = candidate;
            WorldState.global_timestamp = nowMs;
            WriteCollectionPath($"{CollectionWorkstations}/{workstationId}", candidate.ToDictionary());
            return candidate;
        }

        /// <summary>
        /// Empties a woodstove's ash trap, resetting ash_level to 0 and adding the collected
        /// Wood Ash (for the Pearlash/soap engine) to the player's backpack or storage_chest on overflow.
        /// </summary>
        public int EmptyAshTrap(
            string workstationId,
            string woodAshItemId = NoSqlSchemaConstants.ItemWoodAsh,
            string ashQuality = NoSqlSchemaConstants.QualityStandard)
        {
            if (string.IsNullOrEmpty(workstationId) ||
                !Workstations.TryGetValue(workstationId, out var workstation) ||
                workstation == null)
            {
                return 0;
            }

            int collectedAsh = Mathf.Max(0, workstation.ash_level);
            if (collectedAsh <= 0)
            {
                return 0;
            }

            workstation.ash_level = 0;
            long nowMs = GetCurrentEpochMs();
            WorldState.global_timestamp = nowMs;

            var woodAshItem = new InventoryItemData
            {
                item_id = string.IsNullOrEmpty(woodAshItemId) ? NoSqlSchemaConstants.ItemWoodAsh : woodAshItemId,
                quantity = collectedAsh,
                quality = string.IsNullOrEmpty(ashQuality) ? NoSqlSchemaConstants.QualityStandard : ashQuality
            };

            int capacity = UserProfile.GetBackpackSlotCapacity();
            if (Inventories.backpack.Count < capacity)
            {
                Inventories.backpack.Add(woodAshItem);
            }
            else
            {
                Inventories.storage_chest.Add(woodAshItem);
            }

            WriteCollectionPath($"{CollectionWorkstations}/{workstationId}", workstation.ToDictionary());
            WriteCollectionPath(CollectionInventories, Inventories.ToDictionary(capacity));
            OnAshTrapEmptied?.Invoke(workstationId, collectedAsh);
            return collectedAsh;
        }

        /// <summary>
        /// Evaluates all workstations against current or authoritative server epoch milliseconds.
        /// Any workstation where currentEpochMs >= completion_timestamp is flagged as ready in a
        /// preserved state with zero burn or rot penalties.
        /// </summary>
        public Dictionary<string, WorkstationAfkEvaluation> EvaluateAfkWorkstations(long? authoritativeEpochMs = null)
        {
            long evalEpochMs = authoritativeEpochMs ?? GetCurrentEpochMs();
            WorldState.global_timestamp = evalEpochMs;

            var results = new Dictionary<string, WorkstationAfkEvaluation>();
            foreach (var kvp in Workstations)
            {
                if (kvp.Value == null) continue;
                WorkstationAfkEvaluation evaluation = kvp.Value.EvaluateAfkState(evalEpochMs);
                results[kvp.Key] = evaluation;

                if (evaluation.is_complete)
                {
                    OnWorkstationCompleted?.Invoke(evaluation);
                }
            }
            return results;
        }

        /// <summary>
        /// Collects the finished item from a workstation once currentEpochMs >= completion_timestamp.
        /// Item is collected in a preserved state (zero burn/rot penalty), added to inventories
        /// (backpack up to backpack_tier capacity, or storage_chest on overflow), and workstation is reset.
        /// </summary>
        public bool TryCollectCompletedWorkstation(
            string workstationId,
            string craftedQuality = NoSqlSchemaConstants.QualityStandard,
            int outputQuantity = 1,
            long? authoritativeEpochMs = null)
        {
            if (string.IsNullOrEmpty(workstationId) || !Workstations.TryGetValue(workstationId, out var workstation) || workstation == null)
            {
                return false;
            }

            long nowMs = authoritativeEpochMs ?? GetCurrentEpochMs();
            if (!workstation.IsReadyInPreservedState(nowMs))
            {
                return false;
            }

            var craftedItem = new InventoryItemData
            {
                item_id = workstation.active_recipe,
                quantity = Mathf.Max(1, outputQuantity),
                quality = string.IsNullOrEmpty(craftedQuality) ? NoSqlSchemaConstants.QualityStandard : craftedQuality
            };

            int capacity = UserProfile.GetBackpackSlotCapacity();
            if (Inventories.backpack.Count < capacity)
            {
                Inventories.backpack.Add(craftedItem);
            }
            else
            {
                Inventories.storage_chest.Add(craftedItem);
            }

            workstation.active_recipe = string.Empty;
            workstation.completion_timestamp = 0L;
            WorldState.global_timestamp = nowMs;

            WriteCollectionPath($"{CollectionWorkstations}/{workstationId}", workstation.ToDictionary());
            WriteCollectionPath(CollectionInventories, Inventories.ToDictionary(capacity));
            return true;
        }

        /// <summary>
        /// Evaluates companion foraging timers and economy_and_karma contract expirations.
        /// Expired contracts are silently removed with zero Karma penalty.
        /// </summary>
        public void EvaluateAfkCompanionsAndContracts(long? authoritativeEpochMs = null)
        {
            long nowMs = authoritativeEpochMs ?? GetCurrentEpochMs();
            WorldState.global_timestamp = nowMs;

            foreach (var kvp in Companions)
            {
                if (kvp.Value != null && kvp.Value.IsLootReadyToResolve(nowMs))
                {
                    OnCompanionLootReady?.Invoke(kvp.Value);
                }
            }

            int prunedCount = EconomyAndKarma.PruneExpiredContracts(nowMs);
            if (prunedCount > 0)
            {
                OnExpiredContractsPruned?.Invoke(prunedCount);
            }
        }

        /// <summary>
        /// Builds the full RTDB dictionary payload for all 7 NoSQL collections under players/{uid}.
        /// </summary>
        public Dictionary<string, object> BuildFullSevenCollectionPayload(long? authoritativeEpochMs = null)
        {
            long nowMs = authoritativeEpochMs ?? GetCurrentEpochMs();
            UserProfile.user_id = string.IsNullOrEmpty(UserId) ? UserProfile.user_id : UserId;
            WorldState.global_timestamp = nowMs;

            var gardenPlotsDict = new Dictionary<string, object>();
            foreach (var kvp in GardenPlots)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.plot_id = string.IsNullOrEmpty(kvp.Value.plot_id) ? kvp.Key : kvp.Value.plot_id;
                    if (kvp.Value.IsWithered(nowMs))
                    {
                        kvp.Value.hydration_state = false;
                    }
                    gardenPlotsDict[kvp.Key] = kvp.Value.ToDictionary();
                }
            }

            var workstationsDict = new Dictionary<string, object>();
            foreach (var kvp in Workstations)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.workstation_id = string.IsNullOrEmpty(kvp.Value.workstation_id) ? kvp.Key : kvp.Value.workstation_id;
                    workstationsDict[kvp.Key] = kvp.Value.ToDictionary();
                }
            }

            var companionsDict = new Dictionary<string, object>();
            foreach (var kvp in Companions)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.companion_id = string.IsNullOrEmpty(kvp.Value.companion_id) ? kvp.Key : kvp.Value.companion_id;
                    companionsDict[kvp.Key] = kvp.Value.ToDictionary();
                }
            }

            return new Dictionary<string, object>
            {
                { CollectionUsers, UserProfile.ToDictionary() },
                { CollectionWorldState, WorldState.ToDictionary() },
                { CollectionInventories, Inventories.ToDictionary(UserProfile.GetBackpackSlotCapacity()) },
                { CollectionGardenPlots, gardenPlotsDict },
                { CollectionWorkstations, workstationsDict },
                { CollectionCompanions, companionsDict },
                { CollectionEconomyAndKarma, EconomyAndKarma.ToDictionary(nowMs) }
            };
        }

        /// <summary>
        /// Saves all 7 NoSQL collections on sleep trigger and evaluates AFK workstation,
        /// companion, and contract timestamps.
        /// </summary>
        public void SavePlayerStateOnSleep(int day, float stamina, Dictionary<string, GardenPlotData> gardenPlots = null)
        {
            long saveEpochMs = GetCurrentEpochMs();
            UserProfile.stamina = Mathf.Clamp(Mathf.RoundToInt(stamina), 0, 100);
            WorldState.global_timestamp = saveEpochMs;

            if (gardenPlots != null)
            {
                foreach (var kvp in gardenPlots)
                {
                    GardenPlots[kvp.Key] = kvp.Value;
                }
            }

            EvaluateAfkWorkstations(saveEpochMs);
            EvaluateAfkCompanionsAndContracts(saveEpochMs);

            if (!IsAuthenticated)
            {
                Debug.LogWarning("[FirebaseDatabaseManager] Skipping cloud save: user not authenticated.");
                return;
            }

            var payload = BuildFullSevenCollectionPayload(saveEpochMs);
#if FIREBASE_DATABASE_AVAILABLE
            var dbRef = Firebase.Database.FirebaseDatabase.DefaultInstance
                .GetReference($"players/{UserId}");
            dbRef.UpdateChildrenAsync(payload).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    OnSaveCompleted?.Invoke($"Saved 7 NoSQL collections on Day {day} at epoch ms {saveEpochMs}");
                }
            });
#else
            Debug.Log($"[FirebaseDatabaseManager] Queued 7-collection sleep save (Day {day}, EpochMs {saveEpochMs}).");
#endif
        }

        /// <summary>
        /// Legacy overload converting PlantInstanceData entries into garden_plots before saving all 7 collections.
        /// </summary>
        public void SavePlayerStateOnSleep(int day, float stamina, Dictionary<string, PlantInstanceData> plants)
        {
            if (plants != null)
            {
                foreach (var kvp in plants)
                {
                    if (kvp.Value == null) continue;
                    string plotId = string.IsNullOrEmpty(kvp.Value.location) ? kvp.Key : kvp.Value.location;
                    GardenPlots[plotId] = new GardenPlotData
                    {
                        plot_id = plotId,
                        planted_seed = kvp.Value.item_id ?? string.Empty,
                        soil_ph = 6.5f,
                        hydration_state = true,
                        death_timer_timestamp = kvp.Value.estimated_ready_epoch > 0
                            ? kvp.Value.estimated_ready_epoch * 1000L
                            : 0L
                    };
                }
            }

            SavePlayerStateOnSleep(day, stamina, (Dictionary<string, GardenPlotData>)null);
        }

        private void WriteCollectionPath(string relativePath, Dictionary<string, object> payload)
        {
            if (!IsAuthenticated)
            {
                return;
            }

#if FIREBASE_DATABASE_AVAILABLE
            var dbRef = Firebase.Database.FirebaseDatabase.DefaultInstance
                .GetReference($"players/{UserId}/{relativePath}");
            dbRef.UpdateChildrenAsync(payload);
#endif
        }
    }
}

