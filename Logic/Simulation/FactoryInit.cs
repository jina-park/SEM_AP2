using Mozart.SeePlan.Simulation;
using SEM_AREA.Persists;
using SEM_AREA.Outputs;
using SEM_AREA.Inputs;
using SEM_AREA.DataModel;
using Mozart.Task.Execution;
using Mozart.Extensions;
using Mozart.Collections;
using Mozart.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class FactoryInit
    {
        public IEnumerable<Mozart.SeePlan.DataModel.WeightPreset> GET_WEIGHT_PRESETS0(Mozart.SeePlan.Simulation.AoFactory factory, ref bool handled, IEnumerable<Mozart.SeePlan.DataModel.WeightPreset> prevReturnValue)
        {
            return InputMart.Instance.SEMWeightPreset.Values.ToList();
        }

        public void INITIALIZE_WIP_GROUP0(AoFactory factory, IWipManager wipManager, ref bool handled)
        {
            factory.WipManager.AddGroup("StepWips", "CurrentStepID", "CurrentProductID");
        }
    }
}