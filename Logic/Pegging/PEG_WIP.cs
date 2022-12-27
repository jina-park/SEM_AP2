using Mozart.SeePlan.Pegging.Rule;
using Mozart.SeePlan.Pegging;
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

namespace SEM_AREA.Logic.Pegging
{
    [FeatureBind()]
    public partial class PEG_WIP
    {
        public IList<Mozart.SeePlan.Pegging.IMaterial> GET_WIPS0(Mozart.SeePlan.Pegging.PegPart pegPart, bool isRun, ref bool handled, IList<IMaterial> prevReturnValue)
        {
            //        SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;
            //        SEMGeneralStep step = pp.CurrentStep as SEMGeneralStep;

            //        List<IMaterial> result = new List<IMaterial>();

            //        if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //        {
            //            if (PegHelper.CanPegPhase(pp, isRun))
            //                return result;
            //        }

            //        string key = CommonHelper.CreateKey(pp.Product.ProductID, step.StepID);
            //        HashSet<SEMPlanWip> wips = new HashSet<SEMPlanWip>();

            //        //InputMart.Instance.PlanWips.TryGetValue(step, out wips)
            //        if (InputMart.Instance.PlanWips_tmp.TryGetValue(key, out wips) == false)
            //            wips = new HashSet<SEMPlanWip>();

            //        foreach (SEMPlanWip planWip in wips)
            //        {
            //            if (planWip.Qty == 0)
            //                continue;

            //            if (isRun != planWip.IsRunWip)
            //                continue;

            //            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //            {
            //                if (pp.Phase == 1 && planWip.IsFixedProd == true)
            //                    continue;
            //            }

            //            if (planWip.Wip.Product.ProductID != pp.Product.ProductID)
            //                continue;

            ////주석 박진아 : 무슨 조건문일까? 
            //            if (planWip.WipInfo.PeggedTargets.Count > 0 && planWip.WipInfo.PeggedTargets.ElementAt(0).ProductID != pp.Product.ProductID)
            //                continue;

            //            planWip.MapCount++;

            //            result.Add(planWip);
            //        }

            //return result;

            var wips = new List<IMaterial>();
            SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;

            foreach (var wipPair in pp.PrePeggedWip)
            {
                SEMPlanWip planWip = wipPair.Key as SEMPlanWip;
                if (pp.PeggedWip.Keys.Where(x => x.LotID == planWip.LotID).Count() == 0)
                {
                    planWip.HasTarget = true;
                    wips.Add(planWip);
                }
            }

            InputMart.Instance.TargetOperIDForSort = pp.TargetOperID;
            
            return wips;
        }

        public void WRITE_PEG0(PegTarget target, IMaterial m, double qty, ref bool handled)
        {
            var planWip = m as SEMPlanWip;
            SEMGeneralPegTarget pt = target as SEMGeneralPegTarget;
            SEMGeneralPegPart pp = pt.PegPart as SEMGeneralPegPart;
            SEMGeneralMoPlan mo = pt.MoPlan as SEMGeneralMoPlan;


            // pegging 수량
            var pegQty = pp.PrePeggedWip[planWip];
            double pegDemQty = pegQty.Item1;
            double pegWipQty = pegQty.Item2;

            // Pegging 후 PlanWip 수량 조절
            if (pp.IsOverPegging)
            {
                qty = pp.AvailPegQty;
                m.Qty = 0;
            }
            //qty = qty;
            planWip.Qty = planWip.Qty;

            // pegging 횟수 기록
            planWip.MapCount++;

            // PEG_HISTORY 작성
            Outputs.PEG_HISTORY pegHist = new Outputs.PEG_HISTORY();

            pegHist.VERSION_NO = ModelContext.Current.VersionNo;
            pegHist.SUPPLYCHAIN_ID = InputMart.Instance.SupplyChainID;
            pegHist.PLAN_ID = InputMart.Instance.PlanID;
            pegHist.SITE_ID = InputMart.Instance.SiteID;

            pegHist.DEMAND_ID = pp.DemandID;
            pegHist.ROUTE_ID = OperationManager.GetRouteID(pp.TargetOperID);
            pegHist.OPER_ID = pp.TargetOperID;
            pegHist.PRIORITY = pp.Priority.ToString();
            pegHist.DUE_DATE = pp.DueDate;
            pegHist.WEEK_NO = pt.Week;
            pegHist.PRODUCT_ID = pp.ProductID;
            pegHist.CUSTOMER_ID = pp.CustomerID;
            pegHist.END_CUSTOMER_ID = pp.EndCustomerID;

            pegHist.DEMAND_QTY = target.Qty + pegDemQty;
            pegHist.WIP_QTY = planWip.Qty + pegWipQty;

            pegHist.PEG_DEMAND_QTY = pegDemQty;
            pegHist.PEG_WIP_QTY = pegWipQty;

            pegHist.REMAIN_DEMAND_QTY = target.Qty;
            pegHist.REMAIN_WIP_QTY = planWip.Qty;

            pegHist.LOT_ID = planWip.WipInfo.LotID;
            pegHist.WIP_PRODUCT_ID = planWip.WipInfo.WipProductID;
            pegHist.WIP_CUSTOMER_ID = planWip.WipInfo.CustomerID;
            pegHist.WIP_END_CUSTOMER_ID = planWip.WipInfo.EndCustomerList.ListToString();
            pegHist.WIP_ROUTE_ID = planWip.WipInfo.WipRouteID;
            pegHist.WIP_OPER_ID = planWip.WipInfo.InitialStep.StepID;
            pegHist.WIP_STATE = m.State;

            pegHist.CALC_AVIL_DATE = planWip.PegWipInfo.CalcAvailDate;
            pegHist.TOTAL_TAT = planWip.PegWipInfo.TotalTat;
            pegHist.TOTAL_YIELD = planWip.PegWipInfo.AccumYield;

            pegHist.IS_SMALL_LOT = planWip.WipInfo.IsSmallLot ? "Y" : "N";

            pegHist.URGENT_CODE = planWip.WipInfo.UrgentCode;
            pegHist.URGENT_PRIORITY = planWip.WipInfo.UrgentPriority;

            string resFixOper = pp.TargetOperID == "SG4090" ? "SG3910" : "SG4430";
            pegHist.IS_RES_FIX = planWip.WipInfo.IsResFixLotArrange && planWip.WipInfo.LotArrangeOperID == resFixOper ? "Y" : "N";

            pegHist.WIP_REEL_LABELING = planWip.WipInfo.IsReelLabeled ? "Y" : "N";

            pegHist.PLAN_SEQ = ++InputMart.Instance.PegSeq;

            //if(planWip.WipInfo.IsMultiLotProdChange)
            //{
            //    planWip.SplitLotID
            //} [TODO] split lotid출력

            OutputMart.Instance.PEG_HISTORY.Add(pegHist);

            // 검증용 PEG_VALIDATION_LOG 작성
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();
            log.LOT_ID = planWip.LotID;
            log.DEMAND_ID = mo.DemandID;
            log.QTY = pegWipQty;
            log.IS_PEG = "Y";
            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }


        public int SORT_WIP0(Mozart.SeePlan.Pegging.Rule.MaterialInfo x, MaterialInfo y, ref bool handled, int prevReturnValue)
        {
            SEMPlanWip xWip = x.Material as SEMPlanWip;
            SEMPlanWip yWip = y.Material as SEMPlanWip;

            SEMGeneralPegPart xPp = x.PegPart as SEMGeneralPegPart;
            SEMGeneralPegPart yPp = y.PegPart as SEMGeneralPegPart;

            int cmp = 0;

            if (cmp == 0)
            {
                bool xSameProd = xWip.WipProductID == xPp.Product.ProductID;
                bool ySameProd = yWip.WipProductID == yPp.Product.ProductID;

                cmp = ySameProd.CompareTo(xSameProd);
            }

            if (cmp == 0)
                cmp = xWip.IsHold.CompareTo(yWip.IsHold);

            //둘다 Hold일 경우
            if (cmp == 0 && xWip.IsHold && yWip.IsHold)
            {
                //Hold > Move
                cmp = xWip.LotState.CompareTo(yWip.LotState);

                //가용시간 빠른 것
                if (cmp == 0)
                    cmp = xWip.AvailableTime.CompareTo(yWip.AvailableTime);
            }

            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                if (cmp != 0)
                {
                    //아래코드가 맞는지 디버깅으로 확인할 것
                }
                if (cmp == 0 && xPp.Phase == 0)
                    cmp = xWip.IsFixedProd.CompareTo(yWip.IsFixedProd);
            }

            if (cmp == 0)
                cmp = xWip.WipInfo.WipStateTime.CompareTo(yWip.WipInfo.WipStateTime);

            if (cmp == 0)
            {
                if (GlobalParameters.Instance.PegWipSortAsc == true)
                    cmp = xWip.Qty.CompareTo(yWip.Qty);
                else
                    cmp = xWip.Qty.CompareTo(yWip.Qty) * -1;
            }


            if (cmp == 0)
                cmp = string.Compare(xWip.WipProductID, yWip.WipProductID);

            return cmp;
        }

        public bool CAN_PEG_MORE0(PegTarget target, IMaterial m, bool isRun, ref bool handled, bool prevReturnValue)
        {
            //SEMPlanWip wip = m.ToSemPlanWip();
            //SEMGeneralPegTarget pt = target.ToSemPegTarget();

            //if (wip.IsHold == false)
            //    return true;

            return true;
        }

        public double AVAIL_PEG_QTY0(PegTarget target, IMaterial m, bool isRun, ref bool handled, double prevReturnValue)
        {
            SEMGeneralPegTarget pt = target as SEMGeneralPegTarget;
            SEMGeneralPegPart pp = pt.PegPart as SEMGeneralPegPart;

            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                if (pp.Phase == 1)
                {
                    return Math.Min(m.Qty, pt.MasterPegTarget.Qty);
                }
            }

            return m.Qty;
        }

        public void UPDATE_PEG_INFO0(PegTarget target, IMaterial m, double qty, ref bool handled)
        {
            SEMGeneralPegTarget pt = target as SEMGeneralPegTarget;
            SEMGeneralPegPart pp = pt.PegPart as SEMGeneralPegPart;

            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                if (pp.Phase == 1)
                {
                    pt.MasterPegTarget.Qty -= qty;

                    if (pt.MasterPegTarget.Qty == 0)
                    {
                        foreach (var childPp in pp.MasterPegPart.ChildPegParts)
                        {
                            foreach (var childPt in childPp.PegTargetList)
                                childPt.Qty = 0;
                        }
                    }
                }
            }


            m.ToSemPlanWip().WipInfo.PeggedTargets.Add(target.ToSemPegTarget());
        }

        public bool IS_REMOVE_EMPTY_TARGET0(PegPart pegpart, ref bool handled, bool prevReturnValue)
        {
            return true;
        }

        public double AVAIL_PEG_QTY1(PegTarget target, IMaterial m, bool isRun, ref bool handled, double prevReturnValue)
        {
            var pp = target.PegPart as SEMGeneralPegPart;
            var planWip = m as SEMPlanWip;


            // Item1 == PegDemQty
            // Item2 == PegWipQty
            var pegQty = pp.PrePeggedWip[planWip];
            var availPegQty = pegQty.Item1;

            #region OverPegging 확인을 위한 셋팅
            pp.AvailPegQty = availPegQty;


            if (Math.Round(target.Qty, 5) < Math.Round(pegQty.Item1, 5))
                pp.IsOverPegging = true;
            #endregion

            return availPegQty;
        }

        public void UPDATE_PEG_INFO1(PegTarget target, IMaterial m, double qty, ref bool handled)
        {
            SEMGeneralPegPart pp = target.PegPart as SEMGeneralPegPart;
            SEMPlanWip planWip = m as SEMPlanWip;

            // Peg 수량
            // Item1 : Demand 기준 peg 수량(수율을 고려한 Peg 수량)
            // Item2 : Wip 기준 Pegging 수량(OverPeg를 고려한 Peg 수량)
            Tuple<double, double> pegQty = pp.PrePeggedWip[planWip];

            // Wip잔량 조절
            m.Qty = m.Qty + qty - pegQty.Item2;

            // Wip잔량이 매우 작을경우 0으로 세팅 [TODO]추후 정밀하게 조정
            m.Qty = Math.Abs(m.Qty) < 0.0000001 ? 0 : m.Qty;

            // OverPegging시 수량 조절
            #region OverPegging
            if (pp.IsOverPegging)
            {
                qty = pp.AvailPegQty;
                target.Qty = 0;        // 음수를 0으로 바꿔줌
                m.Qty = 0;
            }
            #endregion

            // PlanWip과 Target 매핑
            SEMPlanWip semPw = m as SEMPlanWip;
            semPw.LastPegedTarget = target as SEMGeneralPegTarget;
            semPw.WipInfo.PeggedTargets.Add(target as SEMGeneralPegTarget);
            semPw.IsPegged = true;

            pp.PeggedWip.Add(semPw, pegQty);
            semPw.PeggingDemands.Add(pp, pegQty);
        }

        public int SORT_WIP1(MaterialInfo x, MaterialInfo y, ref bool handled, int prevReturnValue)
        {
            if (object.ReferenceEquals(x, y))
                return 0;


            var xPlanWip = x.Material as SEMPlanWip;
            var yPlanWip = y.Material as SEMPlanWip;

            SEMWipInfo x_wip = xPlanWip.WipInfo;
            SEMWipInfo y_wip = yPlanWip.WipInfo;

            // 1. Demand Oper에 도착시간 빠른 것 우선
            int cmp = xPlanWip.PegWipInfo.CalcAvailDate.CompareTo(yPlanWip.PegWipInfo.CalcAvailDate);
            if (cmp != 0)
                return cmp;

            // 2.Run중인 lot 우선
            if (x_wip.WipState.ToUpper() == "RUN" && y_wip.WipState.ToUpper() != "RUN")
                return -1;

            if (x_wip.WipState.ToUpper() != "RUN" && y_wip.WipState.ToUpper() == "RUN")
                return 1;

            // 3. ResFixedWip 우선
            string resFixOper = InputMart.Instance.TargetOperIDForSort == "SG4090" ? "SG3910" : "SG4430";
            bool xIsResFix = x_wip.IsResFixLotArrange && x_wip.LotArrangeOperID == resFixOper;
            bool yIsResFix = y_wip.IsResFixLotArrange && y_wip.LotArrangeOperID == resFixOper;

            if (xIsResFix && yIsResFix == false)
                return -1;

            if (xIsResFix == false && yIsResFix)
                return 1;

            // 4. ReelLabel된것 우선
            if (x_wip.IsReelLabeled && y_wip.IsReelLabeled == false)
                return -1;

            if (x_wip.IsReelLabeled == false && y_wip.IsReelLabeled)
                return 1;

            // 5. 긴급도 높은 wip(UrgentPriority가 작은것)우선
            cmp = x_wip.UrgentPriority.CompareTo(y_wip.UrgentPriority);
            if (cmp != 0)
                return cmp;

            // 6. WipArrivalTime 빠른것 우선
            cmp = x_wip.WipArrivedTime.CompareTo(y_wip.WipArrivedTime);
            if (cmp != 0)
                return cmp;

            // 7. Wip Qty 많은 것 우선
            cmp = x_wip.UnitQty.CompareTo(y_wip.UnitQty) * -1;

            return cmp;
        }
    }
}