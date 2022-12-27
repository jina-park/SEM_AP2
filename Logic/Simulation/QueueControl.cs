using Mozart.Simulation.Engine;
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
    public partial class QueueControl
    {
        public void ON_NOT_FOUND_DESTINATION0(Mozart.SeePlan.Simulation.DispatchingAgent da, IHandlingBatch hb, int destCount, ref bool handled)
        {
            SEMLot lot = hb.ToSEMLot();

            //TODO:
            string reason = "ON NOT FOUND DESTINATION0";

            //주석 : 박진아 추후 필요시 주석해제
            //if (lot.NoTargetFiltered == true)
            //    reason = "NO TARGET FILTERED";

            if (lot.CurrentSEMStep.StdStep.IsProcessing)
            {
                string key = string.Format("{0}/{1}/{2}", "NotFoundArrange", lot.CurrentSEMStep.StepID, lot.CurrentProductID);

                ErrHist.WriteIf(key,
                    ErrCategory.SIMULATION,
                    ErrLevel.WARNING,
                    lot.LotID,
                    lot.CurrentProductID,
                    lot.CurrentProcessID,
                    "NO_ARRANGE",
                    lot.CurrentStepID,
                    reason,
                    string.Format("Check Arrange → LOT_ID:{0}, {1}", lot.LotID, key)
                    );
                return;
            }

            da.Factory.AddToBucketer(hb);
        }

        public bool IS_BUCKET_PROCESSING0(DispatchingAgent da, IHandlingBatch hb, ref bool handled, bool prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            SEMGeneralStep step = lot.CurrentStep as SEMGeneralStep;

            // RUN재공이 RESOURCE정보가 없는 경우
            if (lot.Wip.IsNoResRunLot)
            {
                // lot.Wip.IsNoResRunLot은 TrackOut 시점에서 false로 셋팅됨
                return true;
            }

            // Processing Oper인데 CycleTime 정보가 없는경우
            bool hasCycleTime = step.HasCycleTime();
            if (step.IsProcessing && hasCycleTime == false)
                return true;

            return !step.IsProcessing;
        }

        public bool IS_HOLD0(DispatchingAgent da, IHandlingBatch hb, ref bool handled, bool prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            DateTime now = AoFactory.Current.NowDT;
            
            if (lot.Wip.AvailableTime > now)
                return true;


            //if (lot.Wip.IsSmallLot == true && lot.CurrentStepID == "SG6094" && lot.IsSmallLotHolded == false)
            //{
            //    // OPER_PLAN 로그 작성
            //    foreach (var pp in lot.PeggingDemands)
            //        WriteOperPlan.WriteProdChangeOperPlanDetail(lot, lot.PlanWip.PegWipInfo.BTBom, pp.Key);
            //    WriteOperPlan.WriteProdChangeOperPlan(lot, lot.PlanWip.PegWipInfo.BTBom);

            //    lot.IsSmallLotHolded = true;

            //    return true;
            //}
            return false;
        }

        public Time GET_HOLD_TIME0(DispatchingAgent da, IHandlingBatch hb, ref bool handled, Time prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            DateTime now = AoFactory.Current.NowDT;


            //if (lot.Wip.IsSmallLot == true && lot.CurrentStepID == "SG6094")
            //    return Time.FromHours(GlobalParameters.Instance.SmallLotStockLeadTime * 24);
           
            return lot.Wip.AvailableTime - now;
        }

        public void ON_HOLD_EXIT0(DispatchingAgent dispatchingAgent, IHandlingBatch hb, ref bool handled)
        {
            SEMLot lot = hb.Sample as SEMLot;

            var pair = InputMart.Instance.HoldLotDic.Where(x => x.Key.Lot.LotID == lot.LotID).FirstOrDefault();

            if (pair.Key == null || pair.Value == null)
                return;

            var wlot = pair.Key;
            var ws = pair.Value;

            ws.RemoveWip(wlot);
            ws.AddWip(wlot);

            lot.CurrentWorkLot = wlot;
        }

        public bool INTERCEPT_IN0(DispatchingAgent da, IHandlingBatch hb, ref bool handled, bool prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;
            SEMPlanInfo lastPlan = lot.PreviousPlan as SEMPlanInfo;
            

            SEMWipInfo wipInfo = lot.WipInfo as SEMWipInfo;

            if (wipInfo.RouteCode != null && lastPlan != null)
            {
                var sortingOperConnection = InputMart.Instance.SORTING_OPER_CONNECTIONView.FindRows(wipInfo.RouteCode, lot.PreviousStep.StepID).FirstOrDefault();
                if (sortingOperConnection != null)
                {
                    var aeqp = AoFactory.Current.GetEquipment(lastPlan.LoadedResource.ResID);
                    aeqp.AddInBuffer(lot);

                    return true;
                }
            }

            return false;
        }
    }
}