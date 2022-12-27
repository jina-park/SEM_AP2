using Mozart.SeePlan.DataModel;
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
    public partial class EqpEvents
    {
        public void WRITE_OPER_PLAN(Mozart.SeePlan.Simulation.AoEquipment aeqp, IHandlingBatch hb, LoadingStates state, ref bool handled)
        {
            SEMLot lot = hb.Sample as SEMLot;

            SEMGeneralStep step = lot.CurrentSEMStep;
            SEMEqp eqp = aeqp.Target as SEMEqp;
            DateTime now = AoFactory.Current.NowDT;

            foreach(var pp in lot.PeggingDemands)
                WriteOperPlan.WriteProcessingOperPlanDetail(eqp, lot, state, pp.Key);

            WriteOperPlan.WriteProcessingOperPlan(eqp, lot, state);
        }
    }
}