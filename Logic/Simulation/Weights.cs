using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;
using Mozart.SeePlan.DataModel;
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
using Mozart.SeePlan;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class Weights
    {
        [Group("SEM_WEIGHTS")]
        public WeightValue ARRANGE_PRIORITY(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            if (factor.Factor == 0)
                return new WeightValue(factor, 0);

            int arrCnt = 0;
            double score = 0;

            SEMLot lot = entity as SEMLot;

            ICollection<SEMEqp> eqps = null;
            if (lot.PlanWip.PegWipInfo.ArrangedEqps.TryGetValue(lot.CurrentStepID, out eqps) == false)
                arrCnt = 0;
            else
                arrCnt = eqps.Count;

            score = (double)arrCnt / InputMart.Instance.SEMEqp.Count;

            string desc = $"Lot : {lot.LotID}\tArrange Count: {arrCnt}";

            return new WeightValue(factor, score, desc);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue WAIT_PRIORITY(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            if (factor.Factor == 0)
                return new WeightValue(factor, 0);

            double score = 0;
            string desc = string.Empty;

            SEMLot lot = entity as SEMLot;
            
            score = (now - lot.PreEndTime).TotalDays / 1000;

            return new WeightValue(factor, score, desc);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue LOT_QTY_PRIORITY(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            return new WeightValue(factor, 0);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue JOB_CHANGE_PRIORITY(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            if (factor.Factor == 0)
                return new WeightValue(factor, 0);

            double score = 0d;

            var lot = entity as SEMLot;
            var aeqp = target as SEMAoEquipment;
            var eqp = aeqp.Target as SEMEqp;

            var setupTime = eqp.GetSetupTime(lot);

            score = 1 - setupTime / 2880;

            return new WeightValue(factor, score, $"setupTime:{setupTime}");
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue STEP_TARGET_PRIORITY(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            if (factor.Factor == 0)
                return new WeightValue(factor, 0);

            SEMLot lot = entity as SEMLot;

            var lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);

            if (lpst == DateTime.MinValue || lpst == DateTime.MaxValue)
                return new WeightValue(factor, 0);

            double score = (now - lpst).TotalDays;

            string desc = string.Empty;

            return new WeightValue(factor, score, desc);
        }


        [Group("SEM_WEIGHTS")]
        public WeightValue URGENT(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            var lot = entity as SEMLot;
            var aeqp = target as SEMAoEquipment;

            double score = 0d;

            if (lot.Wip.IsUrgent == false)
                return new WeightValue(factor, score, string.Empty);

            int priorityCnt = InputMart.Instance.UrgentCodeDic.Keys.Count;
            int priority = lot.Wip.UrgentPriority;

            score = priorityCnt - priority + 1;

            score = score * 10000;

            return new WeightValue(factor, score, string.Empty);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue SAME_LOT(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            if (GlobalParameters.Instance.ApplyContinualProcessingSameLot == false)
                return new WeightValue(factor, 0);

            var lot = entity as SEMLot;
            var aeqp = target as SEMAoEquipment;

            double score = 0;

            if (aeqp.IsSameLotHold)
            {
                if (aeqp.ProcessingSameLots.Contains(lot))
                {
                    score = 10000000;
                }
            }

            return new WeightValue(factor, score, string.Empty);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue REEL_LABEL(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            double score = 0;

            var lot = entity as SEMLot;

            if (lot.Wip.IsReelLabeled)
                score = 100000000;

            return new WeightValue(factor, score, string.Empty);
        }

        [Group("SEM_WEIGHTS")]
        public WeightValue RES_FIX(ISimEntity entity, DateTime now, ActiveObject target, WeightFactor factor, IDispatchContext ctx)
        {
            double totalScore = 0;
            double score = 0;
            string desc = string.Empty;

            var lot = entity as SEMLot;
            var aeqp = target as SEMAoEquipment;
            var eqp = aeqp.Target as SEMEqp;

            bool isResFixedLot = lot.Wip.IsResFixLotArrange && lot.CurrentStepID == lot.Wip.LotArrangeOperID;

            if(isResFixedLot == false)
                return new WeightValue(factor, totalScore, "Not Res Fix Lot");

            // ResFix 여부
            score = 1 * 10000; 
            totalScore += score;
            desc += $"ResFixLot({score})"; 

            // Setup 여부
            var setupTime = eqp.GetSetupTime(lot);
            if (setupTime == 0)
            {
                score = 1 * 1000;
                totalScore += score;
                desc += $"NoSetupTime({score})";
            }
            else
            {
                score = -1 * setupTime * 10;
                totalScore -= score;
                desc += $"SetupTime({score})";
            }

            // 납기
            var lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
            if (lpst == DateTime.MinValue || lpst == DateTime.MaxValue)
            {
                score = 0;
                totalScore += 0;
                desc += $"Unpeg({score})";
            }
            else
            {
                score = (now - lpst).TotalDays;
                totalScore += score;
                desc += $"LpstGapDay({score})";
            }

            // 대기시간
            score = (now - lot.PreEndTime).TotalDays / 1000;
            totalScore += score;
            desc += $"WaitTime({score})";

            return new WeightValue(factor, totalScore, desc);
        }
    }
}