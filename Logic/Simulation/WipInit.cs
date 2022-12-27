using SEM_AREA.Persists;
using SEM_AREA.Outputs;
using SEM_AREA.Inputs;
using Mozart.Common;
using Mozart.SeePlan;
using Mozart.SeePlan.DataModel;
using Mozart.SeePlan.Simulation;
using Mozart.Task.Execution;
using SEM_AREA.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;
using Mozart.Data;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class WipInit
    {
        public IList<Mozart.SeePlan.Simulation.IHandlingBatch> GET_WIPS0(ref bool handled, IList<Mozart.SeePlan.Simulation.IHandlingBatch> prevReturnValue)
        {
            List<IHandlingBatch> list = new List<IHandlingBatch>();

            // Pegged Wips
            if (GlobalParameters.Instance.ReleasePeggedLots == true)
            {
                foreach (var wipInfo in InputMart.Instance.SEMWipInfo.Values.Where(x => x.PeggedTargets.Count > 0))
                {
                    var lot = CreateHelper.CreateLot(wipInfo, LotState.WIP);
                    list.Add(lot);
                    InputMart.Instance.SEMLot.Add(lot.LotID, lot);
                }
            }
            else
            {
                foreach (var wipInfo in InputMart.Instance.SEMWipInfo.Values)
                {
                    var lot = CreateHelper.CreateLot(wipInfo, LotState.WIP);
                    list.Add(lot);
                    InputMart.Instance.SEMLot.Add(lot.LotID, lot);
                }
            }

            // Unpegged Run Wips
            if (GlobalParameters.Instance.ReleasePeggedLots == true && GlobalParameters.Instance.ReleaseUnpeggedRunLots == true)
            {
                var unpegRunWips = InputMart.Instance.SEMWipInfo.Values.Where(x => x.PeggedTargets == null || x.PeggedTargets.Count == 0).Where(x => x.WipState.ToUpper() == "RUN");
                foreach (var unpegRunWip in unpegRunWips)
                {
                    var lot = CreateHelper.CreateLot(unpegRunWip, LotState.WIP);
                    lot.CurrentState = EntityState.RUN;
                    list.Add(lot);
                    InputMart.Instance.SEMLot.Add(lot.LotID, lot);
                }
            }

            // Unpegged res fix Lot
            var unpegFixedLotArrangeWips = InputMart.Instance.SEMWipInfo.Values.Where(x => x.PeggedTargets == null || x.PeggedTargets.Count == 0).Where(x => x.IsResFixLotArrange);
            foreach (var wip in unpegFixedLotArrangeWips)
            {
                if (InputMart.Instance.SEMLot.ContainsKey(wip.LotID))
                    continue;

                var lot = CreateHelper.CreateLot(wip, LotState.WIP);
                list.Add(lot);

                lot.PlanWip.PegWipInfo = lot.PlanWip.SEMPegWipInfos.FirstOrDefault();
                if (lot.PlanWip.PegWipInfo == null)
                    lot.PlanWip.PegWipInfo = lot.PlanWip.UnusablePegWipInfos.FirstOrDefault();
                lot.PlanWip.PegWipInfo.SemOperTargets.ForEach(x => x.Lpst = DateTime.MaxValue);
                InputMart.Instance.SEMLot.Add(lot.LotID, lot);
            }

            // Unpeg Urgent Wip
            var unpegUrgentWips = InputMart.Instance.SEMWipInfo.Values.Where(x => x.IsUrgentUnpeg);
            foreach (var wip in unpegUrgentWips)
            {
                if (InputMart.Instance.SEMLot.ContainsKey(wip.LotID))
                    continue;

                var lot = CreateHelper.CreateLot(wip, LotState.WIP);
                list.Add(lot);

                lot.PlanWip.PegWipInfo = lot.PlanWip.SEMPegWipInfos.FirstOrDefault();
                if (lot.PlanWip.PegWipInfo == null)
                    continue;
                    
                lot.PlanWip.PegWipInfo.SemOperTargets.ForEach(x => x.Lpst = DateTime.MaxValue);

                InputMart.Instance.SEMLot.Add(lot.LotID, lot);
            }

            // Run Wait 셋팅
            foreach (var lot in list)
            {
                var wip = (lot as SEMLot).Wip;

                if (wip.WipState.ToUpper() == "RUN" && (wip.InitialSEMStep != null && wip.InitialSEMStep.IsProcessing))
                    lot.CurrentState = EntityState.RUN;
                else
                {
                    lot.CurrentState = EntityState.WAIT;
                }
            }

            return list;
        }

        public string GET_LOADING_EQUIPMENT0(IHandlingBatch hb, ref bool handled, string prevReturnValue)
        {
			SEMLot lot = hb.Sample as SEMLot;

			return lot.WipInfo.WipEqpID;
		}

        public void ON_BEGIN_INIT0(AoFactory factory, IList<IHandlingBatch> wips, ref bool handled)
        {

		}

        public void ON_BEGIN_INIT1(AoFactory factory, IList<IHandlingBatch> wips, ref bool handled)
        {
            //List<IHandlingBatch> result = new List<IHandlingBatch>();

            //List<SEMLot> list = wips.ToSEMLotList();

            //List<SEMLot> lots = new List<SEMLot>();
            //foreach (var wip in list)
            //{
            //    var lot_debug = wip;
            //    if (lots.Contains(lot_debug) == false)
            //    {
            //        lots.Add(lot_debug);
            //    }
            //    else
            //    {
            //        WIP_LOG wl = new WIP_LOG();
            //        wl.MODULE_NAME = "ON_BEGIN_INIT1";
            //        wl.LOT_ID = lot_debug.LotID;
            //        wl.PROCESS_ID = lot_debug.CurrentProcessID;
            //        wl.PRODUCT_ID = lot_debug.CurrentProductID;
            //        OutputMart.Instance.WIP_LOG.Add(wl);
            //    }
            //}
        }

        public void LOT_ARRANGE_RESTRICTION(AoFactory factory, IHandlingBatch hb, ref bool handled)
        {
            //var lot = hb.Sample as SEMLot;
            
            //var lotArr = InputMart.Instance.LOT_ARRANGELotIDView.FindRows(lot.LotID).FirstOrDefault();
            //if (lotArr == null)
            //    return;

            //#region Validation
            //var arrange = ArrangeHelper2.GetArrange(lot.PlanWip.PegWipInfo, hb.CurrentStep.StepID, "SIM_LOCATE_FOR_DISPATCH");
            //if (arrange.Count == 0)
            //{
            //    ErrHist.WriteIf("", ErrCategory.SIMULATION, ErrLevel.WARNING, lot.LotID, lot.CurrentProductID, "", lotArr.RESOURCE_ID,
            //        lotArr.OPER_ID, "NO_MACTHING_ARRANGE_FOUND", $"{lotArr.LOT_ID} ({lotArr.PRODUCT_ID}) at {lotArr.OPER_ID} has 0 matching arrange.");
            //    return;
            //}

            //string eqpID = lotArr.RESOURCE_ID;
            //if (InputMart.Instance.SEMEqp.ContainsKey(eqpID) == false)
            //{
            //    ErrHist.WriteIf("", ErrCategory.SIMULATION, ErrLevel.WARNING, lot.LotID, lot.CurrentProductID, "", eqpID, lotArr.OPER_ID,
            //        "NO_MATCHING_STD_RESOURCE", $"{lotArr.RESOURCE_ID} for LOT_ARRANGE ({lotArr.LOT_ID}) is not defined in STD. resource information");
            //    return;
            //}

            //ProcTimeInfo procTime = new ProcTimeInfo();
            //procTime = TimeHelper.GetTactTime(eqpID, lot, true);
            //if (procTime.TactTime == TimeSpan.Zero || procTime.FlowTime == TimeSpan.Zero)
            //{
            //    ErrHist.WriteIf("", ErrCategory.SIMULATION, ErrLevel.WARNING, lot.LotID, lot.CurrentProductID, "", eqpID, lotArr.OPER_ID,
            //        "NO_MATCHING_CYCLE_TIME_INFO", $"{lotArr.LOT_ID}({lot.CurrentProductID}) at {lotArr.OPER_ID} on resource {lotArr.RESOURCE_ID} has not matching cycle time information");
            //    return;
            //}
            //#endregion

            //AoEquipment aeqp = null;
            //if (factory.Equipments.TryGetValue(eqpID, out aeqp) == false)
            //{
            //    ErrHist.WriteIf("", ErrCategory.SIMULATION, ErrLevel.WARNING, lot.LotID, lot.CurrentProductID, "", eqpID, lotArr.OPER_ID,
            //        "NO_AO_EQUIPMENT_IN_CURRENT_FACTORY", $"The matching AoEquipment {eqpID} is not found in current factory.");
            //    return;
            //}
            //else
            //    aeqp.AddOutBuffer(hb);
        }

        public void LOCATE_FOR_RUN1(AoFactory factory, IHandlingBatch hb, ref bool handled)
        {
            var wipInitiator = ServiceLocator.Resolve<WipInitiator>();

            AoEquipment e = null;
            string eqpID = wipInitiator.GetLoadingEquipment(hb);

            SEMLot lot = hb.Sample as SEMLot;

            // Checks WIP state that is Run, but processing is completed and located in Outport. 
            bool trackOut = wipInitiator.CheckTrackOut(factory, hb);

            if (string.IsNullOrEmpty(eqpID) || factory.Equipments.TryGetValue(eqpID, out e) == false)
            {
                //If there is not Equipment, handle through Bucketing.6
                factory.AddToBucketer(hb);
                ModelContext.Current.ErrorLister.Write(Mozart.DataActions.ErrorType.Warning, Strings.CAT_SIM_INIT,
                    string.Format(Strings.WARN_INVALID_INITIAL_EQP, eqpID, hb.Sample.LotID));
            }
            else
            {
                SEMAoEquipment aeqp = e as SEMAoEquipment;
                SEMEqp eqp = e.Target as SEMEqp;

                aeqp.ProcessingLot = lot;
                aeqp.LotStartTime = lot.Wip.LastTrackInTime;
                aeqp.LotEndTime = lot.Wip.LastTrackInTime;

                // 장비에 Run 재공이 있는 경우에도 SETUP 여부를 판단하지만 SETUP은 하지않고 eqp의 jobcondition 정보만 update해줌
                bool isNeedSetup = SetupMaster.IsNeedSetup(eqp, lot, true, true);

                eqp.UpdateJobCondition(hb.Sample as SEMLot);


                // Same Lot 관련 셋팅
                if (GlobalParameters.Instance.ApplyContinualProcessingSameLot)
                {
                    if (lot.HasSameLot(true))
                    {
                        if (aeqp.SetEqpSameLotProcessingMode(lot, true))
                            aeqp.RemoveSameLot(lot);
                        else
                            lot.SameLots.Clear();
                    }
                }

                if (trackOut)
                {
                    e.AddOutBuffer(hb);
                }
                else
                {
                    e.AddRun(hb);
                }
            }
        }
        public DateTime FIX_START_TIME0(AoEquipment aeqp, IHandlingBatch hb, ref bool handled, DateTime prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;

            return lot.WipInfo.LastTrackInTime;
        }

        public bool CHECK_TRACK_OUT0(AoFactory factory, IHandlingBatch hb, ref bool handled, bool prevReturnValue)
        {
            // 기준정보상의 Run 재공이 simulation 시간 기준으로 Track out 했는지 확인

            SEMLot lot = hb.Sample as SEMLot;
            SEMEqp eqp;

            if (InputMart.Instance.SEMEqp.TryGetValue(lot.Wip.WipEqpID, out eqp) == false)
                return false; // 없을수 없음

            // [TODO]장비 type에 따라 계산 방식이 달라야함, 현재는 table type 장비밖에없지만 추후에는 inline 장비가 들어오면 계산 방식이 달라져야함
            double tactTime = TimeHelper.GetCycleTime(lot.CurrentSEMStep, eqp.EqpID);
            TimeSpan processingTime = TimeSpan.FromMinutes(tactTime * lot.UnitQtyDouble);
            DateTime wipEndTime = lot.Wip.LastTrackInTime + processingTime;

            ProcTimeInfo time = TimeHelper.GetProcessTime(lot, eqp);
            double tactTime1 = time.TactTime.TotalMinutes;

            if (wipEndTime < factory.NowDT)
                return true;

            return false;
        }

        public IList<IHandlingBatch> LINK_LOT_DEMAND(ref bool handled, IList<IHandlingBatch> prevReturnValue)
        {
            foreach (var pp in InputMart.Instance.SEMGeneralPegPart.Rows)
            {
                foreach(var planWipPair in pp.PeggedWip)
                {
                    SEMPlanWip plawip = planWipPair.Key;
                    Tuple<double,double> pegQty = planWipPair.Value;

                    SEMLot lot;

                    // Link Lot-Demand
                    if( InputMart.Instance.SEMLot.TryGetValue(plawip.Wip.LotID, out lot) == false)
                    {
                        WriteLog.WriteErrorLog("Pegging 된 lot을 탐색할 수 없습니다.");
                    }
                    else
                    {
                        pp.PeggedLots.Add(lot, pegQty);
                        lot.PeggingDemands.Add(pp, pegQty);
                    }

                    // Create DelayShortInfo
                    DelayShortInfo info = new DelayShortInfo();
                    info.LotID = lot.LotID;
                    info.DemandID = pp.DemandID;
                    //info.PegDemandQty = pegQty.Item1;
                    //info.PegLotQty = pegQty.Item2;
                    info.PlanState = "PLANNING";
                    lot.DelayShortInfoDic.Add(pp.DemandID, info);

                    // Link splitLot - PlanWip
                    if (lot.Wip.IsMultiLotProdChange)
                    {
                        SEMPlanWip planWip = lot.Wip.SplitPlanWips.Where(x => x.IsPegged == true).FirstOrDefault();

                        if (planWip == null)
                        {
                            WriteLog.WriteErrorLog("MultiProductChange된 SplitLot의 pegging정보를 찾을 수 없습니다.");
                            continue;
                        }

                        lot.Wip.PlanWip = plawip;
                    }
                }
            }
            return prevReturnValue;
        }

        public void LOCATE_FOR_DISPATCH2(AoFactory factory, IHandlingBatch hb, ref bool handled)
        {
            if (hb.IsFinished)
            {
                factory.Router.AddInitial((Entity)hb, hb.IsFinished);
            }
            else
            {
                var router = EntityControl.Instance;

                string dispatchKey = router.GetLotDispatchingKey(hb);
                DispatchingAgent da = factory.GetDispatchingAgent(dispatchKey);
                if (da == null)
                {
                    if (factory.DispatchingAgents.Count > 0)
                    {
                        //ModelContext.Current.ErrorLister.Write("Entity/WipInit/LocateForDispatch", DataActions.ErrorType.Warning, Strings.CAT_SIM_SECONDRESOURCE,
                        //    string.Format(Strings.WARN_INVALID_IMPLEMENTATION, "Entity/WipInit/LocateForDispatch"));
                        da = factory.DispatchingAgents.FirstOrDefault().Value;
                    }
                    else
                        throw new InvalidOperationException(Strings.EXCEPTION_NO_REGISTERED_DISPATCHINGAGENT);
                }
                da.Take(hb);
            }
        }

        public void LOCATE_FOR_DISPATCH3(AoFactory factory, IHandlingBatch hb, ref bool handled)
        {
            if (hb.IsFinished)            
            {
                factory.Router.AddInitial((Entity)hb, hb.IsFinished);
            }
            else
            {
                var router = EntityControl.Instance;
                string dispatchKey = router.GetLotDispatchingKey(hb);
                DispatchingAgent da = factory.GetDispatchingAgent(dispatchKey);

                if (da == null)
                {
                    if (factory.DispatchingAgents.Count > 0)
                    {
                        ModelContext.Current.ErrorLister.Write("Entity/WipInit/LocateForDispatch", Mozart.DataActions.ErrorType.Warning, Strings.CAT_SIM_SECONDRESOURCE,
                            string.Format(Strings.WARN_INVALID_IMPLEMENTATION, "Entity/WipInit/LocateForDispatch"));

                        da = factory.DispatchingAgents.FirstOrDefault().Value;
                    }
                    else
                        throw new InvalidOperationException(Strings.EXCEPTION_NO_REGISTERED_DISPATCHINGAGENT);

                }

                var lot = hb.Sample as SEMLot;
                var wip = lot.WipInfo as SEMWipInfo;

                if (wip.IsResFixLotArrange)
                {
                    if (wip.LotArrangedEqpDic.Count > 0)
                    {
                        ICollection<SEMEqp> semEqpList;

                        wip.LotArrangedEqpDic.TryGetValue(lot.CurrentStepID, out semEqpList);

                        if (semEqpList != null)
                        {

                            foreach (SEMEqp semEqp in semEqpList)
                            {
                                AoEquipment aoEqp = da.GetEquipment(semEqp.EqpID);
                                aoEqp.AddInBuffer(hb);
                            }
                        }
                    }
                }
                else
                    da.Take(hb);
            }
        }

        public IList<IHandlingBatch> SORT_ONLY_LOT_ARRANGE_WIPS(ref bool handled, IList<IHandlingBatch> prevReturnValue)
        {
            // only lot arrange 먼저, 일반 lot을 나중에 넣음.
            // 정렬된 only lot arrange Lot은 이후 rocate for run에서 sort한 순서로 투입됨 

            List<IHandlingBatch> result = new List<IHandlingBatch>();

            List<SEMLot> list = prevReturnValue.ToSEMLotList();

            // Res Fixed lot arrange wip을 우선순위에 따라 정렬
            var ResFixLotArrangeLotGroup = list.Where(x => x.Wip.IsResFixLotArrange).GroupBy(x=>x.Wip.ResFixLotArrangeEqp);
            foreach(var ResFixLotArrangeLots in ResFixLotArrangeLotGroup)
            {
                var lotList = ResFixLotArrangeLots.ToList();
                lotList.Sort(new Comparers.ResFixLotArrangeCompare());
                result.AddRange(lotList);
            }

            // 일반 lot
            var normalLotList = list.Where(x => x.Wip.IsResFixLotArrange == false).ToList();
            result.AddRange(normalLotList);

            return result;
        }

        public void ON_END_INIT0(AoFactory factory, IList<IHandlingBatch> wips, ref bool handled)
        {
            //List<IHandlingBatch> result = new List<IHandlingBatch>();

            //List<SEMLot> list = wips.ToSEMLotList();

            //List<SEMLot> lots = new List<SEMLot>();
            //foreach (var wip in list)
            //{
            //    var lot_debug = wip;
            //    if (lots.Contains(lot_debug) == false)
            //    {
            //        lots.Add(lot_debug);
            //    }
            //    else
            //    {
            //        WIP_LOG wl = new WIP_LOG();
            //        wl.MODULE_NAME = "ON_END_INIT0";
            //        wl.LOT_ID = lot_debug.LotID;
            //        wl.PROCESS_ID = lot_debug.CurrentProcessID;
            //        wl.PRODUCT_ID = lot_debug.CurrentProductID;
            //        OutputMart.Instance.WIP_LOG.Add(wl);
            //    }
            //}
        }


        public IList<IHandlingBatch> SET_SAME_LOT_INFO(ref bool handled, IList<IHandlingBatch> prevReturnValue)
        {
            var lots = InputMart.Instance.SEMLot;
            foreach (var lotPair in lots)
            {
                var lot = lotPair.Value;
                var wip = lot.Wip;

                if (wip.HasSameLot() == false)
                    continue;

                var sameLots = wip.GetSameLots();

                bool isValidSamelot = IsValidSameLot(lot, sameLots);

                if (isValidSamelot == false)
                    continue;

                lot.SameLots = sameLots;

                WriteLog.WriteSameLotLog("CreateLot", lot, sameLots, true, "");
            }

            return prevReturnValue;
        }

        public bool IsValidSameLot(SEMLot lot, List<SEMLot> sameLots)
        {
            // 연속생산할 SG4430이전 lot 유무 확인
            if (sameLots.Where(x => x.Wip.IsPushWip() == false).Count() == 0)
            {
                WriteLog.WriteSameLotLog("CreateLot", lot, sameLots, false, "NoWip");
                return false;
            }

            // group key 동일 여부 확인
            foreach (var targetLot in sameLots)
            {
                var lotJobKey = lot.GetJobConditionKeyForSameLot();
                var targetLotJobKey = targetLot.GetJobConditionKeyForSameLot();

                if (lotJobKey != targetLotJobKey)
                {
                    WriteLog.WriteSameLotLog("CreateLot", lot, sameLots, false, "NotMatchWorkGroupKey");

                    return false;
                }
            }

            return true;
        }
    }
}