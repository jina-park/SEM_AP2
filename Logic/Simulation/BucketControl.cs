using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;
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
    public partial class BucketControl
    {
        public Time GET_BUCKET_TIME0(Mozart.SeePlan.Simulation.IHandlingBatch hb, AoBucketer bucketer, ref bool handled, Time prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            SEMGeneralStep step = hb.CurrentStep as SEMGeneralStep;

            Time tat = Time.FromMinutes(step.GetTat(lot.Wip.IsSmallLot));

            return tat;
        }

        public Time WRITE_OPER_PLAN(IHandlingBatch hb, AoBucketer bucketer, ref bool handled, Time prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            SEMGeneralStep step = hb.CurrentStep as SEMGeneralStep;

            // SM9999는 OPER_PLAN을 작성하지 않음 (다른 메서드에서 작성)
            if (step.StepID == Constants.SM9999)
                return prevReturnValue;

            Time tat = prevReturnValue;

            // OPER_PLAN 작성
            if (step.StepID == "SG6094_BT")
            {
                WriteOperPlan.WriteDummyOperPlan(lot, lot.PlanWip.PegWipInfo.BTBom);
            }
            else
            {
                foreach (var pp in lot.PeggingDemands)
                    WriteOperPlan.WriteBucketOperPlanDetail(lot, tat, pp.Key);
                WriteOperPlan.WriteBucketOperPlan(lot, tat);

            }

            return prevReturnValue;
        }
    }
}