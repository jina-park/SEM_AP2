using Mozart.Simulation.Engine;
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
    public partial class Route
    {
        public IList<string> GET_LOADABLE_EQP_LIST0(Mozart.SeePlan.Simulation.DispatchingAgent da, IHandlingBatch hb, ref bool handled, IList<string> prevReturnValue)
        {
            var lot = hb.Sample as SEMLot;
            var wip = lot.WipInfo as SEMWipInfo;

            string noArrangeReason = string.Empty;

            return ArrangeHelper2.GetArrange(wip.PlanWip.PegWipInfo, lot.CurrentStepID, lot.CurrentProductID, ref noArrangeReason, "PLAN");
        }

        public LoadInfo CREATE_LOAD_INFO0(ILot lot, Step task, ref bool handled, LoadInfo prevReturnValue)
        {
            SEMLot slot = lot as SEMLot;
            SEMGeneralStep step = task as SEMGeneralStep;

            SEMPlanInfo info = new SEMPlanInfo(step);
            //info.ShopID = step.ShopID;
            info.LotID = slot.LotID;
            info.Product = slot.SEMProduct;
            info.UnitQty = slot.UnitQtyDouble;

            info.ProductID = info.Product.ProductID;
            info.ProcessID = info.Product.Process.ProcessID;

            //info.ProductionType = flot.ProductionType;

            info.InQty = slot.UnitQtyDouble;

            //info.ProductVersion = flot.OrigProductVersion;

            if (slot.CurrentProcessID != info.ProcessID)
            {
                slot.Route = step.Process;
            }

            info.WipInfo = slot.Wip;
            info.Lot = slot;

            if (slot.PlanSteps == null)
                slot.PlanSteps = new List<string>();

            //flot.PlanSteps.Add(step.StepKey);

            return info;
        }

        public void PROUDUCT_CHANGE(DispatchingAgent da, IHandlingBatch hb, ref bool handled)
        {
            SEMLot sLot = hb.Sample as SEMLot;

            if (sLot.IsEnterWarehouse())
            {
                // WR에 chnage 해당하는 포인트
                sLot.WRProductChange();
            }
            else
            {
                // MC, BT chnage에 해당하는 포인트
                sLot.MCBTProductChange();
            }
        }

        public Step WC_WT_PRODUCT_CHANGE(ILot lot, LoadInfo loadInfo, Step step, DateTime now, ref bool handled, Step prevReturnValue)
        {
            // WC, WT Change에 해당하는 포인트

            // 창고가 아니면 WC 변경 하지 않음
            if (step.StepID != Constants.SM9999)
                return step;

            SEMLot slot = lot as SEMLot;

            // BOM 가져오기
            SEMBOM bom = BomHelper.GetBom(slot, true);
            if (bom == null)
                return step; // Product Change 하지 않음

            // Lot의 Product 변경
            slot.ProductChange(bom);

            // ProductChange 로그 작성
            WriteLog.WriteProductChangeLog(slot, bom);

            // OPER_PLAN 로그 작성
            foreach (var pp in slot.PeggingDemands)
                WriteOperPlan.WriteProdChangeOperPlanDetail(slot, bom, pp.Key);
            WriteOperPlan.WriteProdChangeOperPlan(slot, bom);

            // BOM의 ToStep 리턴
            return bom.ToStep;
        }

        public Step GET_NEXT_STEP1(ILot lot, LoadInfo loadInfo, Step step, DateTime now, ref bool handled, Step prevReturnValue)
        {
            SEMLot sLot = lot as SEMLot;
            SEMGeneralStep nextStep = sLot.GetNextOper();

            return nextStep;
        }

        public Step UPDATE_WORK_LOT(ILot l, LoadInfo loadInfo, Step s, DateTime now, ref bool handled, Step prevReturnValue)
        {
            SEMGeneralStep step = s as SEMGeneralStep;
            if (step.StdStep.IsProcessing == false)
                return prevReturnValue;

            SEMLot lot = l as SEMLot;


            if (lot.CurrentWorkStep != null)
            {
                lot.CurrentWorkStep.RemoveWip(lot);
            }            
            

            var agent = AoFactory.Current.JobChangeManger.GetAgent("DEFAULT");
            if (agent == null)
                return prevReturnValue;

            SEMGeneralStep tStep = lot.GetAgentTargetStep(prevReturnValue as SEMGeneralStep);
            if (tStep == null)
                return prevReturnValue;

            string workGroupKey = lot.GetWorkGroupKey(tStep, agent);
            var workGroup = agent.GetGroup(workGroupKey);
            var workStep = workGroup.GetStep(tStep.StepID) as SEMWorkStep;

            var ns = prevReturnValue as SEMGeneralStep;
            DateTime availableTime = AoFactory.Current.NowDT;

            while (ns.StepID != tStep.StepID)
            {
                var tat = ns.GetTat(lot.Wip.IsSmallLot);
                availableTime = availableTime.AddMinutes(tat);
                ns = lot.GetNextOperForJC(ns);
            }

            SEMWorkLot newWLot = new SEMWorkLot(lot, availableTime, workStep.StepKey, tStep);
            newWLot.Lpst = lot.GetLPST(tStep.StepID);
            newWLot.WorkStep = workStep;
            newWLot.LoadableEqp = workStep.GetLoadableEqps();

            workStep.RemoveWip(lot);
            workStep.AddWip(newWLot);

            lot.CurrentWorkGroup = workGroup as SEMWorkGroup;
            lot.CurrentWorkStep = workStep as SEMWorkStep;
            lot.CurrentWorkLot = newWLot;

            return prevReturnValue;
        }

        public void MULTI_LOT_PROD_CHANGE(DispatchingAgent da, IHandlingBatch hb, ref bool handled)
        {
            var lot = hb.Sample as SEMLot;
            
            // Do not split the lot that has already been split from the beginning  [TODO] Pegging 단계로 옮기기
            if (lot.LotID.EndsWith("_W") == false && lot.LotID.EndsWith("_R") == false)
                return;

            var lotProdChangeBoms = InputMart.Instance.LotProdChangeDic[lot.LotID];
            if (lotProdChangeBoms == null || lotProdChangeBoms.Count <= 1)
                return;
            
            var wip = lot.WipInfo as SEMWipInfo;
            var splitPlans = wip.SplitPlanWips;
            var firstPwip = splitPlans.FirstOrDefault();
            if (firstPwip == null)
            {
                ErrHist.WriteIf("", ErrCategory.SIMULATION, ErrLevel.WARNING, lot.LotID, lot.CurrentProductID, "", "NOT_DISPATCHED_YET", hb.CurrentStep.StepID,
                    "NULL_PLAN_WIP", $"{lot.LotID} has no plan wip(s) under the instance. How is it even possible?");
                return;
            }
                
            if (wip.IsMultiLotProdChange == false || hb.CurrentStep.StepID != firstPwip.SplitOperID)
                return;

            // planWip  갯수 만큼 Split Lot  생성
            foreach (var planWip in splitPlans)
            {
                SEMBOM bom = planWip.PegWipInfo.MCBom;

                //Split Lot 생성
                SEMLot newLot = lot.CreateMultiProdSplitLot(planWip, "MLP", "MULTI_LOT_PROD_CHANGE_SPLIT");
                 newLot.Product = bom.ToProduct;

                // LotList에 Add
                InputMart.Instance.SEMLot.Add(newLot.LotID, newLot);

                // Lot - Demand Link
                newLot.PeggingDemands.AddRange(planWip.PeggingDemands);               
                foreach(var pair in planWip.PeggingDemands)
                {
                    var pp = pair.Key;
                    var pegQty = pair.Value;

                    pp.PeggedLots.Add(newLot, pegQty);
                    pp.PeggedLots.Remove(lot);
                }

                // Split Lot을 Factory In
                AoFactory.Current.In(newLot);
                da.ReEnter(newLot);
                InputMart.Instance.SEMLot.Add(newLot.LotID, newLot);
            }

            // 기존 Lot은 Factory Out
            da.Remove(hb);
            AoFactory.Current.Out(hb);            
        }

        public void ON_DONE0(IHandlingBatch hb, ref bool handled)
        {
            //List<ISimEntity> entityList = AoFactory.Current.WipManager.GetWips().ToList();
            //List<ISimEntity> notDuplicateEntityList = new List<ISimEntity>();
            //foreach (var entity in entityList)
            //{
            //    if (notDuplicateEntityList.Contains(entity) == false)
            //    {
            //        notDuplicateEntityList.Add(entity);
            //    }
            //    else
            //    {
            //        var lot_debug = entity as SEMLot;
            //        WIP_LOG wl = new WIP_LOG();
            //        wl.MODULE_NAME = "ON_DONE0";
            //        wl.LOT_ID = lot_debug.LotID;
            //        wl.PROCESS_ID = lot_debug.CurrentProcessID;
            //        wl.PRODUCT_ID = lot_debug.CurrentProductID;
            //        OutputMart.Instance.WIP_LOG.Add(wl);
            //    }
            //}

            //var lot = hb.Sample as SEMLot;
            //if (lot.PlanWip == null)
            //    return;

            //if (lot.PlanWip.PegWipInfo == null)
            //    return;

            //var pegParts = new List<SEMGeneralPegPart>();
            //pegParts.AddRange(lot.PlanWip.PegWipInfo.BulkPeggingPart);
            //pegParts.AddRange(lot.PlanWip.PegWipInfo.TapingPeggingPart);

            //foreach (var pegPart in pegParts)
            //{
            //    string demID = pegPart.DemandID;
            //    double prodQty = pegPart.TargetQty;

            //    var report = InputMart.Instance.DemandStats[demID];
            //    report.PALN_QTY += pegPart.TargetQty;
            //    report.SHORT_QTY -= pegPart.TargetQty;

            //    if (AoFactory.Current.NowDT > pegPart.DueDate)
            //        report.DELAY_QTY += pegPart.TargetQty;
            //}
        }

        public void ON_END_TASK0(IHandlingBatch hb, ActiveObject ao, DateTime now, ref bool handled)
        {
            //List<ISimEntity> entityList = AoFactory.Current.WipManager.GetWips().ToList();
            //List<ISimEntity> notDuplicateEntityList = new List<ISimEntity>();
            //foreach (var entity in entityList)
            //{
            //    if (notDuplicateEntityList.Contains(entity) == false)
            //    {
            //        notDuplicateEntityList.Add(entity);
            //    }
            //    else
            //    {
            //        var lot_debug = entity as SEMLot;
            //        WIP_LOG wl = new WIP_LOG();
            //        wl.MODULE_NAME = "ON_END_TASK0";
            //        wl.LOT_ID = lot_debug.LotID;
            //        wl.PROCESS_ID = lot_debug.CurrentProcessID;
            //        wl.PRODUCT_ID = lot_debug.CurrentProductID;
            //        OutputMart.Instance.WIP_LOG.Add(wl);
            //    }
            //}
        }

        public void ON_START_TASK0(IHandlingBatch hb, ActiveObject ao, DateTime now, ref bool handled)
        {
            //List<ISimEntity> entityList = AoFactory.Current.WipManager.GetWips().ToList();
            //List<ISimEntity> notDuplicateEntityList = new List<ISimEntity>();
            //foreach (var entity in entityList)
            //{
            //    if (notDuplicateEntityList.Contains(entity) == false)
            //    {
            //        notDuplicateEntityList.Add(entity);
            //    }
            //    else
            //    {
            //        var lot_debug = entity as SEMLot;
            //        WIP_LOG wl = new WIP_LOG();
            //        wl.MODULE_NAME = "ON_START_TASK0";
            //        wl.LOT_ID = lot_debug.LotID;
            //        wl.PROCESS_ID = lot_debug.CurrentProcessID;
            //        wl.PRODUCT_ID = lot_debug.CurrentProductID;
            //        OutputMart.Instance.WIP_LOG.Add(wl);
            //    }
            //}
        }
    }
}