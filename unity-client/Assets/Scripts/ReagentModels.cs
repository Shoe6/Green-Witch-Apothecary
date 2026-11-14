using System;
using System.Collections.Generic;

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
}
