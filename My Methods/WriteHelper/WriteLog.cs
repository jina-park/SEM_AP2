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
using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class WriteLog
    {

        internal static void CollectDemand(DEMAND item)
        {
            //주석 : 테이블 삭제
            //DEMAND_LOG log = OutputMart.Instance.DEMAND_LOG.Find(item.DEMAND_ID, item.PRODUCT_ID);

            //if (log == null)
            //{
            //    DateTime dueDate = ConvertHelper.StringToDateTime(item.DUE_DATE, false);
            //    log = new DEMAND_LOG();

            //    log.DEMAND_ID = item.DEMAND_ID;
            //    log.PRODUCT_ID = item.PRODUCT_ID;
            //    log.WEEK_NO = DateUtility.WeekNoOfYearF(dueDate);
            //    log.DUE_DATE = dueDate == DateTime.MinValue ? dueDate : Mozart.SeePlan.ShopCalendar.StartTimeOfNextDay(dueDate).AddSeconds(-1);
            //    log.DEMAND_QTY = (double)item.QTY;
            //}
            //else
            //    log.DEMAND_QTY += (double)item.QTY;

            //OutputMart.Instance.DEMAND_LOG.Add(log);
        }


        internal static void WriteUnpegHistory(SEMPlanWip planWip, string category, string reason, string detailCode, string detailData = "")
        {
            //[todo] ?
            // PlanLogHelper.UnpegLog(wip.Qty);

            Outputs.UNPEG_HISTORY hist = new Outputs.UNPEG_HISTORY();

            hist.VERSION_NO = ModelContext.Current.VersionNo;
            hist.SUPPLYCHAIN_ID = InputMart.Instance.SupplyChainID;
            hist.PLAN_ID = InputMart.Instance.PlanID;
            hist.SITE_ID = InputMart.Instance.SiteID;
            hist.EXE_MODULE = "Pegging";
            hist.STAGE_ID = "Pegging"; //AP는 작성하지 않음

            hist.LOT_ID = planWip.LotID;
            hist.ROUTE_ID = planWip.WipInfo.WipRouteID;
            hist.OPER_ID = planWip.WipInfo.InitialStep.StepID;
            hist.PRODUCT_ID = planWip.WipProductID;
            hist.CUSTOMER_ID = planWip.WipInfo.CustomerID;
            hist.PLAN_SEQ = ++InputMart.Instance.UnpegPlanSeq;
            hist.STATUS = planWip.WipInfo.WipState;

            hist.WIP_QTY = (decimal)planWip.Qty;
            hist.UNPEG_QTY = (decimal)planWip.Qty;

            hist.UNPEG_CATEGORY = category;
            hist.UNPEG_REASON = reason;

            if (detailCode.Length > 30)
            {
                string noSpaceDetailCode = detailCode.Replace(" ", "");
                if (noSpaceDetailCode.Length < 30)
                {
                    detailCode = noSpaceDetailCode;
                }
                else
                {
                    detailData = noSpaceDetailCode.Substring(29, noSpaceDetailCode.Length - 29) + " // " + detailData;
                    detailCode = noSpaceDetailCode.Substring(0, 29);
                }
            }

            if (detailData.Length > 3000)
            {
                detailData = detailData.Substring(0, 3000);
            }

            hist.DETAIL_CODE = detailCode;
            hist.DETAIL_DATA = detailData;

            hist.ORDERNO = (decimal)planWip.WipInfo.OrderNO;

            hist.IS_SMALL_LOT = planWip.WipInfo.IsSmallLot ? "Y" : "N";

            hist.URGENT_CODE = planWip.WipInfo.UrgentCode;
            hist.URGENT_PRIORITY = planWip.WipInfo.UrgentPriority;

            hist.IS_RES_FIX = planWip.WipInfo.IsResFixLotArrange ? "Y" : "N";
            hist.WIP_REEL_LABELING = planWip.WipInfo.IsReelLabeled ? "Y" : "N";

            OutputMart.Instance.UNPEG_HISTORY.Add(hist);
        }


        internal static void WriteUnpegHistory(WIP item, SEMProduct prod, string category, string reason, string detailCode, string detailData = "")
        {
            Outputs.UNPEG_HISTORY hist = new Outputs.UNPEG_HISTORY();

            hist.VERSION_NO = ModelContext.Current.VersionNo;
            hist.SUPPLYCHAIN_ID = InputMart.Instance.SupplyChainID;
            hist.PLAN_ID = InputMart.Instance.PlanID;
            hist.SITE_ID = InputMart.Instance.SiteID;
            hist.EXE_MODULE = "Pegging";
            hist.STAGE_ID = "Pegging"; //AP는 작성하지 않음

            hist.LOT_ID = item.LOT_ID;
            hist.ROUTE_ID = item.ROUTE_ID;
            hist.OPER_ID = item.OPER_ID;
            hist.PRODUCT_ID = item.PRODUCT_ID;
            hist.CUSTOMER_ID = item.CUSTOMER_ID;
            hist.PLAN_SEQ = ++InputMart.Instance.UnpegPlanSeq;
            hist.STATUS = item.STATUS;

            hist.WIP_QTY = item.QTY;
            hist.UNPEG_QTY = item.QTY;

            hist.UNPEG_CATEGORY = category;
            hist.UNPEG_REASON = reason;
            hist.DETAIL_CODE = detailCode.Length > 30 ? "" : detailCode;
            hist.DETAIL_DATA = detailData;

            hist.IS_SMALL_LOT = prod != null && (double)item.QTY < prod.SmallLotQty ? "Y" : "N";

            hist.URGENT_CODE = item.URGENT_CODE;
            hist.URGENT_PRIORITY = (int)item.URGENT_PRIORITY;

            hist.ORDERNO = item.ORDERNO;

            hist.IS_RES_FIX = "";
            hist.WIP_REEL_LABELING = item.REEL_LABELING == "Y" ? "Y" : "N";
            OutputMart.Instance.UNPEG_HISTORY.Add(hist);
        }

        public static void WriteWipAvailDateLog(SEMPegWipInfo pwi, string nextOperID, int splitSeq, int seq)
        {
            SEMWipInfo wip = pwi.WipInfo;

            WIP_AVAIL_DATE_LOG wal = new WIP_AVAIL_DATE_LOG();

            wal.LOT_ID = wip.LotID;
            wal.BRANCH_SEQ = pwi.BranchSeq;
            wal.SEQ = pwi.Seq;
            wal.FROM_BRANCH_SEQ = pwi.FromBranchSeq;

            wal.WIP_ROUTE_ID = wip.WipRouteID;
            wal.WIP_OPER_ID = wip.InitialStep.StepID;

            wal.FLOW_ROUTE_ID = pwi.FlowRouteId;
            wal.FLOW_OPER_ID = pwi.FlowStepID;
            wal.NEXT_OPER_ID = nextOperID;
            wal.IS_LOT_OPER = pwi.FlowStep.IsLotOper ? "Y" : "N";
            wal.TARGET_OPER_ID = pwi.TargetOperID;
            wal.IS_TARGET_OPER = pwi.IsTargetOper ? "Y" : "N";

            wal.WIP_PRODUCT = pwi.WipInfo.WipProductID;
            wal.FLOW_PROD_ID = pwi.FlowProductID;
            wal.MC_CHANGED_PROD = pwi.MC_ChangedProductID;
            wal.WR_CHANGED_PROD = pwi.WR_ChangedProductID;
            wal.WC_CHANGED_PROD = pwi.WC_ChangedProductID;
            wal.WT_CHANGED_PROD = pwi.WT_ChangedProductID;
            wal.BT_CHANGED_PROD = pwi.BT_ChangedProductID;

            wal.MC_IS_PROD_CHANGED = pwi.MC_IsChangedProd && pwi.WipInfo.IsProdFixed ? "N" : pwi.MC_IsChangedProd ? "Y" : "N";
            wal.WR_IS_PROD_CHANGED = pwi.WR_IsChangedProd ? "Y" : "N";
            wal.WC_IS_PROD_CHANGED = pwi.WC_IsChangedProd ? "Y" : "N";
            wal.WT_IS_PROD_CHANGED = pwi.WT_IsChangedProd ? "Y" : "N";
            wal.BT_IS_PROD_CHANGED = pwi.BT_IsChangedProd ? "Y" : "N";

            wal.MC_PROD_CHANGED_OPER = pwi.MC_ChangedProdOperID;
            wal.WR_PROD_CHANGED_OPER = pwi.WR_ChangedProdOperID; //SM9999 값으로 고정 일 것으로 예상
            wal.WC_PROD_CHANGED_OPER = pwi.WC_ChangedProdOperID; //SM9999 값으로 고정 일 것으로 예상
            wal.WT_PROD_CHANGED_OPER = pwi.WT_ChangedProdOperID; //SM9999 값으로 고정 일 것으로 예상
            wal.BT_PROD_CHANGED_OPER = pwi.BT_ChangedProdOperID; //SG6090 값으로 고정 일 것으로 예상

            wal.WIP_CUST = wip.CustomerID;
            wal.FLOW_CUST = pwi.FlowCustomerID;
            wal.MC_CHANGED_CUST = pwi.MC_ChangedCustomerID;
            wal.WR_CHANGED_CUST = pwi.WR_ChangedCustomerID;
            wal.WC_CHANGED_CUST = pwi.WC_ChangedCustomerID;
            wal.WT_CHANGED_CUST = pwi.WT_ChangedCustomerID;
            wal.BT_CHANGED_CUST = pwi.BT_ChangedCustomerID;

            wal.TOTAL_TAT = pwi.TotalTat;
            wal.FLOW_TAT = pwi.FlowTat;

            wal.WIP_AVAIL_DATE = pwi.WipAvailDate;
            wal.CALC_AVAIL_DATE = pwi.CalcAvailDate;

            wal.WIP_QTY = wip.UnitQty;
            wal.FLOW_QTY = pwi.FlowQty;
            wal.FLOW_YIELD = pwi.FlowYield;
            wal.ACCUM_YIELD = pwi.AccumYield;

            wal.IS_PROD_FIX = pwi.WipInfo.IsProdFixed ? "Y" : "N";
            wal.IS_LOT_PROD_CHANGE = pwi.IsLotProdChange ? "Y" : "N";
            wal.IS_MULTI_LOT_PROD_CHANGE = pwi.IsMultiLotProdChange ? "Y" : "N";
            wal.IS_PROCESSING = pwi.FlowStep.StdStep.IsProcessing ? "Y" : "N";

            pwi.IsTargetOper = false;

            OutputMart.Instance.WIP_AVAIL_DATE_LOG.Table.Rows.Add(wal);
        }


        public static void WritePegWipInfo(SEMPegWipInfo pwi)
        {
            Outputs.PEG_WIP_INFO log = new PEG_WIP_INFO();

            log.LOT_ID = pwi.LotID;

            log.WIP_ROUTE_ID = pwi.WipInfo.WipRouteID;
            log.WIP_OPER_ID = pwi.WipInfo.InitialStep.StepID;

            log.TARGET_ROUTE_ID = OperationManager.GetRouteID(pwi.TargetOperID);
            log.TARGET_OPER_ID = pwi.TargetOperID;

            log.WIP_PRODUCT = pwi.WipProductID;
            log.FLOW_PROD_ID = pwi.FlowProductID;
            log.MC_CHANGED_PROD = pwi.MC_ChangedProductID;
            log.WR_CHANGED_PROD = pwi.WR_ChangedProductID;
            log.WC_CHANGED_PROD = pwi.WC_ChangedProductID;
            log.WT_CHANGED_PROD = pwi.WT_ChangedProductID;
            log.BT_CHANGED_PROD = pwi.BT_ChangedProductID;


            log.MC_IS_PROD_CHANGED = pwi.MC_IsChangedProd && pwi.WipInfo.IsProdFixed ? "N" : pwi.MC_IsChangedProd ? "Y" : "N";
            log.WR_IS_PROD_CHANGED = pwi.WR_IsChangedProd ? "Y" : "N";
            log.WC_IS_PROD_CHANGED = pwi.WC_IsChangedProd ? "Y" : "N";
            log.WT_IS_PROD_CHANGED = pwi.WT_IsChangedProd ? "Y" : "N";
            log.BT_IS_PROD_CHANGED = pwi.BT_IsChangedProd ? "Y" : "N";

            log.MC_PROD_CHANGED_OPER = pwi.MC_ChangedProdOperID;
            log.WR_PROD_CHANGED_OPER = pwi.WR_ChangedProdOperID;
            log.WC_PROD_CHANGED_OPER = pwi.WC_ChangedProdOperID;
            log.WT_PROD_CHANGED_OPER = pwi.WT_ChangedProdOperID;
            log.BT_PROD_CHANGED_OPER = pwi.BT_ChangedProdOperID;

            log.WIP_CUST = pwi.WipCustomerID;
            log.FLOW_CUST = pwi.FlowCustomerID;
            log.MC_CHANGED_CUST = pwi.MC_ChangedCustomerID;
            log.WR_CHANGED_CUST = pwi.WR_ChangedCustomerID;
            log.WC_CHANGED_CUST = pwi.WC_ChangedCustomerID;
            log.WT_CHANGED_CUST = pwi.WT_ChangedCustomerID;
            log.BT_CHANGED_CUST = pwi.BT_ChangedCustomerID;
            log.END_CUSTOMER_IDS = pwi.EndCustomerList.ListToString();

            log.WIP_AVAIL_DATE = pwi.WipAvailDate;
            log.CALC_AVAIL_DATE = pwi.CalcAvailDate;

            log.TOTAL_TAT = pwi.TotalTat;

            log.WIP_QTY = pwi.WipInfo.UnitQty;
            log.YIELD_QTY = pwi.FlowQty;
            log.YIELD = pwi.AccumYield;

            log.IS_USABLE_WIP = pwi.IsUsable ? "Y" : "N";
            log.REASON = pwi.UnUsableReason;
            log.PEG_WIP_IDX = pwi.PegWipIdx;

            OutputMart.Instance.PEG_WIP_INFO.Add(log);

        }

        public static void WriteDemandGroupLog(DemandGroup dg)
        {
            DEMAND_GROUP_LOG dgl = new DEMAND_GROUP_LOG();
            dgl.KEY = dg.Key;
            dgl.SITE_ID = dg.SiteId;
            dgl.PROD_ID = dg.ProductId;
            dgl.CUST_ID = dg.CustomerId;
            dgl.TARGET_OPER = dg.TargetOperId;
            dgl.DEMAND_CNT = dg.InitDemandList.Count();
            dgl.AVAIL_WIP_CNT = dg.AvailWipList.Count();

            List<string> demList = dg.InitDemandList.Select(r => r.DemandID).ToList();
            dgl.DEMAND_LIST = demList.ListToString();

            List<string> wipList = dg.AvailWipList.Select(r => r.WipInfo.LotID).ToList();
            dgl.AVAIL_WIP_LIST = wipList.ListToString();

            OutputMart.Instance.DEMAND_GROUP_LOG.Table.Rows.Add(dgl);

        }

        public static void WritePrePegLog(DemandGroup demGroup, SEMGeneralPegPart selectedPp, SEMPegWipInfo selectedWip,
            double targetQty, double pegWipQty, double pegDemQty, double remainWipQty, double remainDemQty,
            int phase, int seq, int priority, bool isPeg, bool isSpillOverPeg = false, bool isOverPeg = false)
        {
            PRE_PEG_LOG ppl = new PRE_PEG_LOG();

            ppl.PHASE = phase;
            ppl.DEM_SEQ = seq;
            ppl.PEG_SEQ = priority;

            ppl.SITE_ID = demGroup.SiteId;

            ppl.DEM_KEY = demGroup.Key;
            ppl.DEM_ID = selectedPp.DemandID;
            ppl.DEM_DUE_DATE = selectedPp.DueDate;
            ppl.DEM_PRIORITY = selectedPp.Priority.ToString();
            ppl.DEM_CUST_ID = demGroup.CustomerId;
            ppl.DEM_END_CUSTOMER_ID = selectedPp.EndCustomerID;
            ppl.DEM_OPER_ID = demGroup.TargetOperId;

            ppl.DEM_QTY = targetQty;  // Pegging 전 Demand 수량

            ppl.PEG_WIP_QTY = pegWipQty;
            ppl.PEG_DEM_QTY = pegDemQty;

            ppl.WIP_REMAIN_QTY = remainWipQty;
            ppl.DEM_REMAIN_QTY = remainDemQty;

            ppl.PRODUCT_ID = demGroup.ProductId;

            ppl.IS_PEG = isPeg ? "Y" : "N";
            ppl.IS_SPILLOVER_PEG = isSpillOverPeg ? "Y" : "N";
            ppl.IS_OVER_PEG = isOverPeg ? "Y" : "N";

            if (selectedWip != null)
            {
                ppl.LOT_ID = selectedWip.WipInfo.LotID;

                ppl.WIP_QTY = selectedWip.PlanWip.QtyForPrePeg + pegWipQty;
                ppl.WIP_QTY_YIELD = (selectedWip.PlanWip.QtyForPrePeg + pegWipQty) * selectedWip.AccumYield;

                ppl.WIP_AVAIL_DATE = selectedWip.CalcAvailDate;
                ppl.WIP_CUSTMER_ID = selectedWip.FlowCustomerID;
                ppl.WIP_END_CUSTOMER_ID = selectedPp.EndCustomerID;
                //ppl.WIP_END_CUSTOMER_ID = selectedWip.EndCustomerList.ListToString();  // Wip기준 EndCustomerID
                ppl.WIP_OPER_ID = selectedWip.WipInfo.InitialStep.StepID;

                ppl.PEG_WIP_IDX = selectedWip.PegWipIdx;

                if (selectedWip.IsWriteLpstLog || selectedWip.IsMultiLotProdChange)
                    WriteLotLpst(selectedWip, selectedPp, pegWipQty);
                else
                {
                    foreach (var ot in selectedWip.SemOperTargets)
                    {
                        string key = CommonHelper.CreateKey(selectedWip.LotID, ot.OperId, ot.PlanType);
                        InputMart.Instance.LotLpstDic.TryGetValue(key, out var row);
                        if (row != null)
                        {
                            row.PEGGING_QTY += (decimal)pegWipQty;
                            row.QTY += (decimal)pegWipQty;
                        }
                    }
                }
            }

            OutputMart.Instance.PRE_PEG_LOG.Table.Rows.Add(ppl);
        }

        public static void WriteLotLpst(SEMPegWipInfo pwi, SEMGeneralPegPart pp, double pegQty)
        {
            SEMWipInfo wip = pwi.WipInfo;

            double totalTat = 0;

            SEMOperTarget btOt = null;

            for (int i = pwi.SemOperTargets.Count - 1; i >= 0; i--)
            {
                SEMOperTarget ot = pwi.SemOperTargets[i];
                if (ot.OperId == "SG6094_BT")
                {
                    btOt = ot;
                    continue;
                }

                LOT_LPST row = new LOT_LPST();

                row.VERSION_NO = ModelContext.Current.VersionNo;
                row.SUPPLYCHAIN_ID = InputMart.Instance.SupplyChainID;
                row.PLAN_ID = InputMart.Instance.PlanID;
                row.SITE_ID = ot.SiteId;
                row.TO_SITE_ID = InputMart.Instance.SiteID;
                row.EXE_MODULE = "Pegging";
                row.STAGE_ID = "Pegging";
                row.FACTORY_ID = wip.FactoryID == null ? "-" : wip.FactoryID;

                row.LOT_ID = ot.LotId;
                row.ROUTE_ID = ot.RouteId;
                row.OPER_ID = ot.OperId;
                row.OPER_SEQ = ot.OperSeq;
                row.PLAN_TYPE = ot.PlanType;

                row.PRODUCT_ID = ot.ProductId;
                row.CUSTOMER_ID = ot.CustomerId;
                row.END_CUSTOMER_ID = pp.EndCustomerID;  //Demand의 EndCustomerID
                //row.END_CUSTOMER_ID = ot.EndCustomerIDs; // Wip의 EndCustomerID

                row.PLAN_SEQ = ++InputMart.Instance.LPSTPlanSeq;

                SEMGeneralStep nextOper = ot.SEMGeneralStep.GetNextOper();
                if (nextOper != null)
                {
                    row.TO_ROUTE_ID = nextOper.SEMRouteID;
                    row.TO_OPER_ID = nextOper.StepID;
                }


                row.LEAD_TIME = (decimal)ot.LeadTime;
                row.TACT_TIME = (decimal)ot.OperTat;
                row.CALC_TAT = (decimal)ot.CalcTat;

                row.YIELD = (decimal)ot.OperYield;
                row.IS_SPECIAL_YIELD = ot.IsSpecialYield ? "Y" : "N";

                row.CALC_YIELD = (decimal)ot.CalcYield;

                row.WIP_QTY = (decimal)wip.UnitQty;
                row.WIP_YIELD_QTY = (decimal)ot.Qty;
                row.PEGGING_QTY = (decimal)pegQty;

                totalTat += ot.LeadTime;
                DateTime lpst = pp.DueDate.AddMinutes(-totalTat);
                row.LPST_DT = lpst;
                row.LPST = lpst.DateTimeToString();
                row.DUE_DATE_DT = pp.DueDate;
                row.LAST_TARGET_DATE = pp.DueDate.DateTimeToString();

                row.DEMAND_QTY = (int)pp.DemandQty;
                row.DEMAND_DUE_DATE = pp.DueDate.DateTimeToString();

                if (pwi.SortingPeggingPart.Count() > 0)
                {
                    var pt = pp.PegTargetList.First();
                    var mp = pt.MoPlan as SEMGeneralMoPlan;

                    row.DEMAND_ID = pp.DemandID;
                    row.BULK_MODEL = mp.BulkModel;
                    row.BULK_WEEK = mp.BulkWeek;
                    row.BULK_DUE_DATE = pp.DueDate.DateTimeToString();
                    row.TAPING_MODEL = mp.TapingModel;
                    row.TAPING_CUSTOMER_ID = mp.TapingCustomerID;
                    row.TAPING_WEEK = mp.TapingWeek;
                    row.TAPING_DUE_DATE = pp.DueDate.DateTimeToString();
                }

                if (pwi.BulkPeggingPart.Count() > 0)
                {
                    row.DEMAND_ID = pwi.BulkPeggingPart.Last().DemandID;
                    row.BULK_DEMAND_ID = pwi.BulkPeggingPart.Last().DemandID;
                    row.BULK_MODEL = pwi.BulkPeggingPart.Last().ProductID;
                    row.BULK_WEEK = DateUtility.WeekNoOfYearF(pwi.BulkPeggingPart.Last().FirstSEMPegTarget.DueDate);
                    row.BULK_DUE_DATE = pwi.BulkPeggingPart.Last().FirstSEMPegTarget.DueDate.DateTimeToString();
                }

                if (pwi.TapingPeggingPart.Count() > 0)
                {
                    row.DEMAND_ID = pwi.TapingPeggingPart.Last().DemandID;
                    row.TAPING_DEMAND_ID = pwi.TapingPeggingPart.Last().DemandID;
                    row.TAPING_MODEL = pwi.TapingPeggingPart.Last().ProductID;
                    row.TAPING_CUSTOMER_ID = pwi.TapingPeggingPart.Last().CustomerID;
                    row.TAPING_WEEK = DateUtility.WeekNoOfYearF(pwi.TapingPeggingPart.Last().FirstSEMPegTarget.DueDate);
                    row.TAPING_DUE_DATE = pwi.TapingPeggingPart.Last().FirstSEMPegTarget.DueDate.DateTimeToString();
                }

                row.PRIORITY = pp.Priority.ToString();
                row.QTY = (decimal)pegQty;
                row.TOTAL_TAT = (decimal)totalTat;

                row.FROM_PROD_ID = ot.FromProdID;
                row.FROM_CUSTOMER_ID = ot.FromCustomerID;
                row.MODEL_CHANGE = ot.FromProdID.IsNullOrEmpty() ? "N" : "Y";

                row.ORDERNO = (decimal)wip.OrderNO;

                if (InputMart.Instance.LotProdChangeDic.ContainsKey(row.LOT_ID))
                {
                    row.IS_LOT_PROD_CHANGE = pwi.WipInfo.IsMultiLotProdChange == false && row.PLAN_TYPE == "MC" ? "Y" : "N";
                }
                else
                    row.IS_LOT_PROD_CHANGE = "N";

                row.IS_SMALL_LOT = pwi.WipInfo.IsSmallLot ? "Y" : "N";

                row.URGENT_CODE = pwi.WipInfo.UrgentCode;
                row.URGENT_PRIORITY = pwi.WipInfo.UrgentPriority;

                ot.Lpst = lpst;

                if (ot.PlanType == "BT")
                {
                    row.LEAD_TIME = (decimal)btOt.LeadTime;
                    row.TACT_TIME = (decimal)btOt.CalcTat;
                }

                string key = CommonHelper.CreateKey(pwi.LotID, ot.OperId, ot.PlanType);
                InputMart.Instance.LotLpstDic.Add(key, row);

                //OutputMart.Instance.LOT_LPST.Table.Rows.Add(row);
            }

            // 한 wip이 여러 demand에 pegging 되어도 LPST Log는 한번만 씀
            pwi.IsWriteLpstLog = false;

            InputMart.Instance.PlanSeq++;
        }

        public static void WritePegValidationWip_UNPEG(WIP wip, string reason)
        {
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();

            log.LOT_ID = wip.LOT_ID;
            log.DEMAND_ID = "-";
            log.QTY = Convert.ToDouble(wip.QTY);
            log.REASON = reason;
            log.IS_PEG = "N";

            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }

        public static void WritePegValidationWip_UNPEG(SEMPegWipInfo wip, string reason, double unpegQty)
        {
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();

            log.LOT_ID = wip.LotID;
            log.DEMAND_ID = "-";
            log.QTY = unpegQty;
            log.REASON = reason;
            log.IS_PEG = "N";

            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }

        public static void WritePegValidationWipYiled(SEMPegWipInfo wip, string demandID, string reason, double yeildGap)
        {
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();

            log.LOT_ID = wip.LotID;
            log.DEMAND_ID = demandID;
            log.QTY = yeildGap;
            log.REASON = reason;
            log.IS_PEG = "Y";

            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }

        public static void WritePegValidationWip_TOTAL(double qty)
        {
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();

            log.LOT_ID = "Total Wip";
            log.QTY = qty;
            log.IS_PEG = "WIP";

            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }

        public static void WritePegValidationWip_UNPEG(SEMPlanWip wip, string reason)
        {
            Outputs.PEG_VALIDATION_WIP log = new PEG_VALIDATION_WIP();

            log.LOT_ID = wip.LotID;
            log.DEMAND_ID = "-";
            log.QTY = (double)wip.Qty;
            log.REASON = reason;
            log.IS_PEG = "N";

            OutputMart.Instance.PEG_VALIDATION_WIP.Add(log);
        }

        public static void WriteDemandLog(SEMGeneralPegPart pp)
        {
            //주석 : 테이블 삭제
            //DEMAND_LOG log = new DEMAND_LOG();

            //DateTime dueDate = pp.DueDate;

            //log.DEMAND_ID = pp.DemandID;
            //log.PRODUCT_ID = pp.ProductID;
            //log.CUSTOMER_ID = pp.CustomerID;
            //log.END_CUSTOMER_ID = pp.EndCustomerID;
            //log.DEMAND_CUSTOMER_ID = pp.DemandCustomerID;
            //log.WEEK_NO = DateUtility.WeekNoOfYearF(dueDate);
            //log.DUE_DATE = dueDate == DateTime.MinValue ? dueDate : Mozart.SeePlan.ShopCalendar.StartTimeOfNextDay(dueDate).AddSeconds(-1);
            //log.DEMAND_QTY = (double)pp.TargetQty;


            //OutputMart.Instance.DEMAND_LOG.Add(log);
        }

        public static void WriteBomVaildation(BOM bom, bool isValid, string reason)
        {
            Outputs.BOM_VALIDATION log = new BOM_VALIDATION();

            //log.NUM = 0;
            log.SITE_ID = bom.SITE_ID;
            log.OPER_ID = bom.OPER_ID;
            log.FROM_PROD_ID = bom.FROM_PROD_ID;
            log.TO_PROD_ID = bom.TO_PROD_ID;
            log.FROM_CUSTOMER_ID = bom.FROM_CUSTOMER_ID;
            log.TO_CUSTOMER_ID = bom.TO_CUSTOMER_ID;
            log.QTY_PER = bom.QTY_PER;
            log.PRIORITY = bom.PRIORITY;
            log.CHANGE_TYPE = bom.CHANGE_TYPE;
            log.IS_VALID = isValid ? "Y" : "N";
            log.REASON = reason;

            OutputMart.Instance.BOM_VALIDATION.Add(log);
        }

        public static void WritePegWipFilterLog(SEMPegWipInfo pwi, SEMGeneralPegPart pp, bool isPeg, string category, string reason, string detailReason)
        {
            Outputs.PEG_WIP_FILTER_LOG log = new PEG_WIP_FILTER_LOG();

            log.LOT_ID = pwi.LotID;
            log.PRODUCT_ID = pwi.FlowProductID;
            log.CUSTOMER_ID = pwi.FlowCustomerID;
            //log.END_CUSTOMER_ID = pp.EndCustomerID; // Demand의 EndCustomerID
            log.END_CUSTOMER_ID = pwi.EndCustomerList.ListToString();  // Wip의 EndCustomerID
            if (pp != null)
            {
                log.DEMAND_KEY = pp.DemKey;
                log.DEMAND_ID = pp.DemandID;
                log.DEMAND_CUSTOMER_ID = pp.CustomerID;
                log.DEMAND_ENDCUSTOMER_ID = pp.EndCustomerID;
            }

            //log.IS_PEG = isPeg ? "Y" : "N";
            log.CATEGORY = category;
            log.REASON = reason;
            log.DETAIL_REASON = detailReason;

            log.OPER_ID = pwi.FlowStepID;
            log.HAS_MC = pwi.MC_IsChangedProd ? "Y" : "N";
            log.HAS_WR = pwi.WR_IsChangedProd ? "Y" : "N";
            log.HAS_WC = pwi.WC_IsChangedProd || pwi.WT_IsChangedProd ? "Y" : "N";
            log.HAS_BT = pwi.BT_IsChangedProd ? "Y" : "N";

            OutputMart.Instance.PEG_WIP_FILTER_LOG.Add(log);
        }

        public static void WritePegValidationDemand_PEG(SEMGeneralPegPart pp, SEMPegWipInfo wip, double qty)
        {
            Outputs.PEG_VALIDATION_DEMAND log = new PEG_VALIDATION_DEMAND();

            log.DEMAND_KEY = pp.DemKey;
            log.DEMAND_ID = pp.DemandID;
            log.LOT_ID = wip.LotID;
            log.PRODUCT_ID = pp.ProductID;
            log.CUSTOMER_ID = pp.CustomerID;
            log.END_CUSTOMER_ID = pp.EndCustomerID;
            log.DUE_DATE = pp.DueDate;
            log.QTY = qty;
            log.WEEK = pp.FirstSEMPegTarget.Week;
            log.PRIORITY = pp.Priority.ToString();

            log.REASON = "";
            //log.REASON_TABLE = "-";
            log.REASON_KEY = "-";

            log.IS_PEG = "Y";

            OutputMart.Instance.PEG_VALIDATION_DEMAND.Add(log);
        }

        public static void WritePegValidationDemand_UNPEG(Inputs.DEMAND demand, string reason, string reasonKey)
        {
            Outputs.PEG_VALIDATION_DEMAND log = new PEG_VALIDATION_DEMAND();

            log.DEMAND_KEY = "";
            log.DEMAND_ID = demand.DEMAND_ID;
            log.PRODUCT_ID = demand.PRODUCT_ID;
            log.CUSTOMER_ID = demand.CUSTOMER_ID;
            log.END_CUSTOMER_ID = demand.END_CUSTOMER_ID;
            log.DUE_DATE = ConvertHelper.ConvertStringToDateTime(demand.DUE_DATE);
            log.QTY = (double)demand.QTY;

            log.REASON = reason;
            log.REASON_KEY = reasonKey;

            //if (reason == "ZERO_QTY")
            //{
            //    log.REASON_TABLE = "DEMAND";
            //    log.REASON_KEY = "";
            //}
            //else if (reason == "NO_PRODUCT")
            //{
            //    log.REASON_TABLE = "PRODUCT";
            //    log.REASON_KEY = "PRODUCT_ID : " + demand.PRODUCT_ID;
            //}
            //else if (reason == "NO_PROD_OPER")
            //{
            //    log.REASON_TABLE = "PRODUCT_OPER";
            //    log.REASON_KEY = "PRODUCT_ID : " + demand.PRODUCT_ID;
            //}
            //else if (reason == "INVALID_DATE")
            //{
            //    log.REASON_TABLE = "";
            //    log.REASON_KEY = "";
            //}
            //else
            //{

            //}

            log.IS_PEG = "N";

            OutputMart.Instance.PEG_VALIDATION_DEMAND.Add(log);
        }

        public static void WritePegValidationDemand_UNPEG(SEMGeneralPegPart pp, string reason, string detail)
        {
            Outputs.PEG_VALIDATION_DEMAND log = new PEG_VALIDATION_DEMAND();

            log.DEMAND_KEY = pp.DemKey;
            log.DEMAND_ID = pp.DemandID;
            log.PRODUCT_ID = pp.ProductID;
            log.CUSTOMER_ID = pp.CustomerID;
            log.END_CUSTOMER_ID = pp.EndCustomerID;
            log.DUE_DATE = pp.DueDate;
            log.QTY = pp.TargetQty;
            log.WEEK = pp.FirstSEMPegTarget.Week;
            log.PRIORITY = pp.Priority.ToString();


            log.REASON = reason;
            if (reason == "NO_MATERIAL_CODE")
            {
                log.DETAIL_REASON = "TAPING_MATERIAL_ALT";
                log.REASON_KEY = "CHIP_SIZE : " + pp.SemProduct.ChipSize +
                                " / THICKNESS : " + pp.SemProduct.Thickness +
                                " / TAPINGTYPE : " + pp.SemProduct.TapingType +
                                " / CARRIER_TYPE : " + pp.SemProduct.CarrierType;
            }
            else if (reason == "NO_WIP")
            {
                log.DETAIL_REASON = "PEG_WIP_INFO";
                log.REASON_KEY = "FLOW_PRODUCT_ID : " + pp.ProductID +
                                " / FLOW_CUSTOMER_ID : " + pp.CustomerID;
            }
            else if (reason == "NOT_ENOUGH_WIP")
            {
                // [TODO] 총 DemandQty와 Maximum WipQty 출력
                log.DETAIL_REASON = "";
                log.REASON_KEY = "";
            }
            else if (reason == "ALL_WIPS_FILTERED")
            {
                log.DETAIL_REASON = "PEG_WIP_FILTER_LOG";
                log.REASON_KEY = "DEMAND_KEY : " + pp.DemKey;
            }

            log.IS_PEG = "N";

            OutputMart.Instance.PEG_VALIDATION_DEMAND.Add(log);
        }

        public static void WritePegValidationDemand_TOTAL(double qty)
        {
            Outputs.PEG_VALIDATION_DEMAND log = new PEG_VALIDATION_DEMAND();

            log.DEMAND_ID = "TOTAL_DEMAND";
            log.IS_PEG = "-";
            log.QTY = qty;

            OutputMart.Instance.PEG_VALIDATION_DEMAND.Add(log);
        }

        public static void WriteErrorLog(string reason)
        {
            string className1 = new System.Diagnostics.StackTrace().GetFrame(3).GetMethod().Name;
            string className2 = new System.Diagnostics.StackTrace().GetFrame(2).GetMethod().Name;
            string methodName = new System.Diagnostics.StackTrace().GetFrame(1).GetMethod().Name;
            Logger.MonitorInfo(string.Format("** WARNING : {0} \n  위치 : {1} \n  {2}  \n  {3}", reason, className1, className2, methodName));
        }

        public static void WriteProductChangeLog(SEMLot lot, SEMBOM bom)
        {
            Outputs.PRODUCT_CHANGE_LOG log = new PRODUCT_CHANGE_LOG();

            log.VERSION_NO = ModelContext.Current.VersionNo;
            log.SITE = InputMart.Instance.SiteID;

            log.ROUTE_ID = InputMart.Instance.RouteDic[bom.ToStepID];
            log.ROUTE_NAME = "";
            log.OPER_ID = bom.ToStepID;
            log.OPER_NAME = "";

            log.FROM_MODEL = bom.FromProductID;
            log.FROM_CUSTOMER = bom.FromCustomerID;

            log.TO_MODEL = bom.ToProductID;
            log.TO_CUSTOMER = bom.ToCustomerID;

            log.LOT_NO = lot.LotID;
            log.CHANGE_TIME = DateTime.MinValue;
            log.WIP_STOCK_QTY = lot.UnitQtyDouble;
            log.CHANGE_QTY = lot.UnitQtyDouble;

            log.BULK_MODLE = "";
            log.BULK_WEEK = "";
            log.TAPING_CUSTOMER = "";
            log.TAPING_WEEK = "";

            log.CHANGE_TYPE = bom.ChangeType;
            log.IS_LOT_PRODUCT_CHANGE = bom.IsLotProdChange ? "Y" : "N";

            OutputMart.Instance.PRODUCT_CHANGE_LOG.Add(log);
        }

        public static void WriteWipYieldLog(SEMWipInfo wip, string demandID, double yieldGap)
        {
            WIP_YIELD_LOG log = new WIP_YIELD_LOG();

            log.PLAN_ID = InputMart.Instance.PlanID;
            log.SITE_ID = InputMart.Instance.SiteID;
            log.LOT_ID = wip.LotID;
            log.DEMAND_ID = demandID;
            log.YIELD_QTY = yieldGap;
            log.WIP_STATUS = wip.WipState;
            log.ROUTE_ID = wip.WipRouteID;

            OutputMart.Instance.WIP_YIELD_LOG.Add(log);
        }

        public static string GetJobCondition(this Dictionary<string, string> StepJobConditions)
        {
            string result = string.Empty;

            Dictionary<string, string> temp = new Dictionary<string, string>();
            foreach (var cond in StepJobConditions)
            {
                string conKey = SetupMaster.GetJobConditionFieldName(cond.Key);
                if (temp.ContainsKey(conKey) == false)
                    temp.Add(conKey, cond.Value);
            }
            result = temp.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value).DictionaryToString();

            return result;
        }


        public static void WriteEqpInflowWipLog(SEMEqp eqp, SEMLot lot, SEMGeneralStep step)
        {
            DateTime now = AoFactory.Current.NowDT;

            var row = OutputMart.Instance.EQP_INFLOW_WIP_LOG.Find(now.ToString("yyyyMMdd HHmmss"), eqp.EqpID, step.StepID);

            if (row != null)
            {
                row.INFLOW_WIP_QTY += lot.UnitQty;
                row.LOT_LIST += string.Concat(",", lot.LotID);
            }
            else
            {
                EQP_INFLOW_WIP_LOG newRow = new EQP_INFLOW_WIP_LOG();

                newRow.VERSION_NO = ModelContext.Current.VersionNo;
                newRow.EVENT_TIME = now.ToString("yyyyMMdd HHmmss");
                newRow.EQP_ID = eqp.EqpID;
                newRow.STEP_ID = step.StepID;
                newRow.REASON = "WAIT Inflow Wip";
                newRow.INFLOW_WIP_QTY = lot.UnitQty;
                newRow.LOT_LIST = lot.LotID;

                OutputMart.Instance.EQP_INFLOW_WIP_LOG.Add(newRow);
            }
        }

        public static void WriteWorkLotLog(SEMWorkGroup wg, SEMWorkStep ws, SEMLot lot, DateTime availableTime)
        {
            WORK_LOT_LOG log = new WORK_LOT_LOG();

            log.LOT_ID = lot.LotID;
            log.AVAILABLE_TIME = availableTime;
            if (wg != null)
            {
                log.WORK_GROUP_KEY = wg != null ? wg.GroupKey : string.Empty;
                log.JOB_COND_KEY = wg != null ? wg.JobConditionKey : string.Empty;
            }

            if (ws != null)
            {
                log.LPST = lot.GetLPST(ws.Key.ToString());

                log.WORK_STEP = ws != null ? ws.Key.ToString() : string.Empty;

                var arr = lot.GetArrange(ws.Key.ToString()).OrderBy(x => x).ToList();
                log.LOADABLE_EQP = arr.ListToString();
                log.LOADABLE_EQP_CNT = arr.Count;
            }

            log.URGENT_TYPE = lot.Wip.UrgentCode;
            log.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            OutputMart.Instance.WORK_LOT_LOG.Add(log);
        }

        public static void WriteRunWorkLotLog(JobConditionGroup jg, SEMLot lot, string eqpID)
        {
            WORK_LOT_LOG log = new WORK_LOT_LOG();

            log.LOT_ID = lot.LotID;
            log.AVAILABLE_TIME = DateTime.MinValue;

            log.WORK_GROUP_KEY = "";
            log.JOB_COND_KEY = jg.Key;

            log.LOADED_EQP = eqpID;

            log.URGENT_TYPE = lot.Wip.UrgentCode;
            log.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            OutputMart.Instance.WORK_LOT_LOG.Add(log);
        }

        public static void WriteJobChangeAssignEqpFilterLog(this EqpAssignInfo info, SEMWorkStep ws)
        {
            bool isEqpAssigned = info.AssignEqp == null || info.WorkStep == null ? true : false;

            FILTER_ASSIGN_EQP_LOG2 row = new FILTER_ASSIGN_EQP_LOG2();
            row.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            row.UP_WORK_GROUP = ws.GroupKey;
            row.UP_LPST_GAP_DAY = info.upWsLpstGapDay;
            row.UP_DUE_STATE = info.upWsState;

            row.EQP_ID = info.EqpID;
            row.EQP_DECISION_TYPE = isEqpAssigned ? "DOWN" : info.WorkStep.WsData.DecidedOperationType.ToString();
            row.EQP_LPST_GAP_DAY = info.LpstGapDay;
            row.EQP_DUE_STATE = info.DueState;

            row.RESULT = info.Result;
            row.REASON = info.Reason;

            row.WAITING_TIME = Math.Round(info.WaitingTime, 2);
            row.SETUP_TIME = info.SetupTime;

            row.PRIORITY = info.Priority.ToString();

            row.STEP_ID = ws.StepKey;
            row.DUE_STATE_LOG = info.DueStateLog;
            row.EQP_WORK_GROUP = isEqpAssigned ? "NoAssign" : info.AssignEqp.WorkStep.Group.Key.ToString();
            
            OutputMart.Instance.FILTER_ASSIGN_EQP_LOG2.Add(row);
        }

        public static void WriteSameLotLog(string category, SEMWipInfo wip, List<SEMWipInfo> sameWips, bool isValid, string reason)
        {
            SAME_LOT_LOG log = new SAME_LOT_LOG();

            log.CATEGORY = category;
            log.LOT_ID = wip.LotID;
            log.LOT_NAME = wip.LotName;
            log.IS_REEL_LABELED = wip.IsReelLabeled ? "Y" : "N";
            log.ARRANGE = "-";
            log.SAME_LOT_LIST = sameWips.Select(x => x.LotID).ListToString();
            log.IS_VALID = isValid ? "Y" : "N";
            log.INVALID_REASON = reason;

            OutputMart.Instance.SAME_LOT_LOG.Add(log);
        }

        public static void WriteSameLotLog(string category, SEMLot lot, List<SEMLot> sameLots, bool isValid, string reason)
        {
            SAME_LOT_LOG log = new SAME_LOT_LOG();

            log.CATEGORY = category;
            log.LOT_ID = lot.LotID;
            log.LOT_NAME = lot.Wip.LotName;
            log.IS_REEL_LABELED = lot.Wip.IsReelLabeled ? "Y" : "N";
            log.ARRANGE = lot.GetArrange("SG4430").ListToString();
            log.SAME_LOT_LIST = sameLots.Select(x => x.LotID).ListToString();
            log.IS_VALID = isValid ? "Y" : "N";
            log.INVALID_REASON = reason;

            OutputMart.Instance.SAME_LOT_LOG.Add(log);
        }

        public static void WriteDemand2(DEMAND demand)
        {
            DEMAND2 demand2 = new DEMAND2();

            demand2.SUPPLYCHAIN_ID = demand.SUPPLYCHAIN_ID;
            demand2.PLAN_ID = demand.PLAN_ID;
            demand2.DEMAND_ID = demand.DEMAND_ID;
            demand2.SITE_ID = demand.SITE_ID;
            demand2.PRODUCT_ID = demand.PRODUCT_ID;
            demand2.CUSTOMER_ID = demand.CUSTOMER_ID;
            demand2.END_CUSTOMER_ID = demand.END_CUSTOMER_ID;
            demand2.ROUTE_ID = demand.ROUTE_ID;
            demand2.OPER_ID = demand.OPER_ID;
            demand2.LOT_ID = demand.LOT_ID;
            demand2.QTY = demand.QTY;
            demand2.PLAN_QTY = demand.PLAN_QTY;
            demand2.RESULT_QTY = demand.RESULT_QTY;
            demand2.TAPING_MODEL = demand.TAPING_MODEL;
            demand2.BULK_MODEL = demand.BULK_MODEL;
            demand2.TAPING_CUSTOMER_ID = demand.TAPING_CUSTOMER_ID;
            demand2.BULK_WEEK = demand.BULK_WEEK;
            demand2.TAPING_WEEK = demand.TAPING_WEEK;
            demand2.DUE_DATE = demand.DUE_DATE;
            demand2.WEEK = CommonHelper.GetWeekNo(CommonHelper.StringToDateTime(demand.DUE_DATE, true));
            demand2.PRIORITY = demand.PRIORITY;
            demand2.PRE_BUILD = demand.PRE_BUILD;
            demand2.LATE_BUILD = demand.LATE_BUILD;

            OutputMart.Instance.DEMAND2.Add(demand2);
        }
    }
}