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
using System.Text;
using Mozart.Simulation.Engine;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class DispatcherControl
    {
        public Type GET_LOT_BATCH_TYPE0(ref bool handled, Type prevReturnValue)
        {
            return typeof(SEMLotBatch);
        }

        public IHandlingBatch[] DO_SELECT0(DispatcherBase db, AoEquipment aeqp, IList<IHandlingBatch> wips, IDispatchContext ctx, ref bool handled, IHandlingBatch[] prevReturnValue)
        {
            Mozart.SeePlan.DataModel.WeightPreset preset = aeqp.Target.Preset;
            SEMAoEquipment eqp = aeqp as SEMAoEquipment;
            SEMEqp semEqp = aeqp.Target as SEMEqp;
            SEMPlanInfo lastPlan = aeqp.LastPlan as SEMPlanInfo;

            IHandlingBatch[] selected = null;

            if (wips.Count > 0)
            {
                IList<IHandlingBatch> newlist = new List<IHandlingBatch>(wips);
                var control = DispatchControl.Instance;

                newlist = control.Evaluate(db, newlist, ctx);
                selected = control.Select(db, eqp, newlist);
                
                // 로그 작성
                if (control.IsWriteDispatchLog(eqp))
                    DispatchLogHelper.AddDispatchInfo(eqp, newlist, selected, preset);

                if (selected.Count() == 0)
                    return selected;

                SEMLot lot = selected.First() as SEMLot;

                // res fix 정보 셋팅
                AgentHelper.SetResFixInfo(lot, eqp);
                
            }

            return selected;
        }

        public bool IS_WRITE_DISPATCH_LOG0(AoEquipment aeqp, ref bool handled, bool prevReturnValue)
        {
            return true;
        }

        public void WRITE_DISPATCH_LOG0(DispatchingAgent da, EqpDispatchInfo info, ref bool handled)
        {
			var targetEqp = info.TargetEqp as SEMEqp;
			string eqpID = targetEqp.EqpID;

			var eqp = da.GetEquipment(eqpID) as SEMAoEquipment;

			DispatchLogHelper.WriteDispatchLog(eqp, info);
		}

        public string ADD_DISPATCH_WIP_LOG1(Mozart.SeePlan.DataModel.Resource eqp, EntityDispatchInfo info, ILot lot, Mozart.SeePlan.DataModel.WeightPreset wp, ref bool handled, string prevReturnValue)
        {
			string log = string.Empty;

			var flot = lot as SEMLot;
			var targetEqp = eqp as SEMEqp;

			log = DispatchLogHelper.GetDispatchWipLog(targetEqp, info, flot, wp);

			return log;
		}

        public void ON_DISPATCHED0(DispatchingAgent da, AoEquipment e, IHandlingBatch[] wips, ref bool handled)
        {
            if (GlobalParameters.Instance.ApplyContinualProcessingSameLot == false)
                return;

            var hb = wips.First();
            if (hb == null)
                return;

            var lot = hb as SEMLot;
            var aeqp = e as SEMAoEquipment;

            if (lot.HasSameLot() == false)
                return;

            // lot이 가야할 장비가있는데 same lot 장비에 dispatch 되지 않은 상황이면
            if (lot.SameLotEqp.Count > 1 && lot.SameLotEqp.Contains(aeqp) == false)
            {
                if (lot.Wip.IsResFixLotArrange && lot.CurrentStepID == lot.Wip.LotArrangeOperID)
                { }
                else
                {
                    WriteLog.WriteErrorLog($"Samelot 로직 오류 {lot.LotID} {aeqp.EqpID} {lot.SameLotEqp.Select(x => x.EqpID).ListToString()}");
                }
            }

            // 장비가 다른 same lot이 들어왔거나 same lot mode가 아니면
            if (aeqp.SameLotName != lot.Wip.LotName || aeqp.IsSameLotHold == false)
            {
                // 장비를 same lot 작업 상태로 바꿈
                aeqp.SetEqpSameLotProcessingMode(lot);
            }

            // selected lot은 eqp의 lot list에서 삭제해줌
            aeqp.RemoveSameLot(lot);

        }

        public IHandlingBatch[] ONE_DAY_CAPA_SPLIT(DispatcherBase db, AoEquipment aeqp, IList<IHandlingBatch> wips, ref bool handled, IHandlingBatch[] prevReturnValue)
        {
            var lot = prevReturnValue[0].Sample as SEMLot;
            if (lot.IsCapaSplitLot == true || lot.Wip.IsSplitedWip == true)
                return prevReturnValue;

            var stdStep = (lot.CurrentStep as SEMGeneralStep).StdStep;
            if (stdStep.IsLotSplit == false)
                return prevReturnValue;

            if (lot.Wip.OrderNO == 0)
                return prevReturnValue;

            double tactTime = TimeHelper.GetCycleTime(lot.CurrentSEMStep, aeqp.EqpID);

            // 1 day = 1440 minutes
            double neededCapa = tactTime * lot.UnitQtyDouble;

            // 사용될 capa가 1440(oneday)보다 작으면 로직 적용안함
            if (neededCapa <= 1440)
                return prevReturnValue;

            // lot의 Arrange가 1대면 로직 적용안함
            if (lot.GetArrange(lot.CurrentStepID, "FW").Count == 1)
                return prevReturnValue;

            Dictionary<SEMLot, double> lotQtyDic = new Dictionary<SEMLot, double>();

            // split 수량 설정 
            double oneDayQty = Math.Floor(1440 / tactTime);
			double remainQty = lot.UnitQtyDouble - oneDayQty;

            // remainQty가 minPackQty 보다 작으면 로직 적용안함
            int minPackQty = (lot.Wip.Product as SEMProduct).PackingMinQty;
            if (remainQty < minPackQty)
                return prevReturnValue;

            // 기존 lot 수량 차감
			lot.UnitQtyDouble = oneDayQty;    // Update mother lot's qty. to the maximum number can be done in one day
            lot.IsCapaSplitLot = true; 

            lotQtyDic.Add(lot, lot.UnitQtyDouble);

            // Split Lot 생성, 위에서 차감한 수량 적용
            SEMLot splitLot = lot.CreateOneDayCapaSplitLot(remainQty, "CAP", "EXCEED_ONE_DAY_CAPA_SPLIT");
            lotQtyDic.Add(splitLot, remainQty);


            // Split lot을 demand에 수량에 맞게 배분 
            var demands = lot.PeggingDemands; // 현재 Lot을 pegging한 Demands
            lot.PeggingDemands = new Dictionary<SEMGeneralPegPart, Tuple<double,double>>(); // split한대로 새로 넣기위해 초기화

            SEMLot splitedLot = lotQtyDic.First().Key;
            double lotQty = lotQtyDic.First().Value;

            foreach (var pair in demands)
            {
                SEMGeneralPegPart pp = pair.Key;
                double pegWipQty = pair.Value.Item1;
                double pegDemandQty = pair.Value.Item2;

                pp.PeggedLots.Remove(lot);

                while(pegWipQty > 0)
                {
                    while (lotQty > 0)
                    {
                        double peggingQty1 = Math.Min(pegWipQty, lotQty);
                        double peggingQty2 = Math.Min(pegDemandQty, lotQty);

                        Tuple<double, double> peggingQtyTp = new Tuple<double, double>(peggingQty1, peggingQty2);

                        pp.PeggedLots.Add(splitedLot, peggingQtyTp);                            
                        splitedLot.PeggingDemands.Add(pp, peggingQtyTp);
                            
                        lotQty -= peggingQty1;
                        pegWipQty -= peggingQty1;

                        if (pegWipQty == 0)
                            break;

                        if (lotQty == 0)
                        {
                            lotQtyDic.Remove(splitedLot);

                            if (lotQtyDic.Count == 0)
                                break; 

                            splitedLot = lotQtyDic.First().Key;
                            lotQty = lotQtyDic.First().Value;

                            break;
                        }                            
                    }

                    if (lotQty == 0)
                        break;
                }
            }

            // split lot의 DelayShortInfo 생성
            lot.DelayShortInfoDic.Clear();
            foreach (var pair in lot.PeggingDemands)
            {
                var pp = pair.Key;
                var pegQty = pair.Value;

                // Create DelayShortInfo
                DelayShortInfo info = new DelayShortInfo();
                info.LotID = lot.LotID;
                info.DemandID = pp.DemandID;
                //info.PegDemandQty = pegQty.Item1;
                //info.PegLotQty = pegQty.Item2;
                info.PlanState = "PLANNING";
                lot.DelayShortInfoDic.Add(pp.DemandID, info);
            }

            // split lot의 DelayShortInfo 생성
            foreach (var pair in splitLot.PeggingDemands)
            {
                var pp = pair.Key;
                var pegQty = pair.Value;                        

                // Create DelayShortInfo
                DelayShortInfo info = new DelayShortInfo();
                info.LotID = splitLot.LotID;
                info.DemandID = pp.DemandID;
                //info.PegDemandQty = pegQty.Item1;
                //info.PegLotQty = pegQty.Item2;
                info.PlanState = "PLANNING";
                splitLot.DelayShortInfoDic.Add(pp.DemandID, info);
            }

            // [TODO] lotQtyDic에 남은 split lot은 forward peg 대상


            // split lot을 plan에 적용
            AoFactory.Current.In(splitLot); 
            aeqp.DispatchingAgent.ReEnter(splitLot); 
            InputMart.Instance.SEMLot.Add(splitLot.LotID, splitLot);

            return prevReturnValue;
        }
    }
}