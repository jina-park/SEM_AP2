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
using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class WriteOperPlan
    {
        public static void WriteProdChangeOperPlan(SEMLot lot, SEMBOM bom)
        {
            OPER_PLAN row = new OPER_PLAN();
            SEMGeneralStep step = lot.CurrentSEMStep;
            DateTime now = AoFactory.Current.NowDT;

            row.SUPPLYCHAIN_ID = "MLCC";
            row.PLAN_ID = InputMart.Instance.PlanID;
            row.SITE_ID = InputMart.Instance.SiteID;
            row.TO_SITE_ID = InputMart.Instance.SiteID;
            row.EXE_MODULE = "PLAN";
            row.STAGE_ID = "PLAN";

            row.LOT_ID = lot.LotID;
            row.FACTORY_ID = lot.FactoryID.IsNullOrEmpty() ? "-" : lot.FactoryID;
            row.ROUTE_ID = step.SEMRouteID;
            row.OPER_ID = step.StepID;
            row.OPER_SEQ = step.Sequence;
            row.PLAN_TYPE = bom == null ? "WR" : bom.ChangeType;
            row.PLAN_SEQ = ++InputMart.Instance.OperPlanSeq;
            row.PROCESSING_TYPE = lot.CurrentSEMStep.StdStep.IsProcessing ? "PROCESSING" : "DUMMY";

            var nextOper = lot.GetNextOper();
            row.TO_ROUTE_ID = nextOper == null ? string.Empty : nextOper.SEMRouteID;
            row.TO_OPER_ID = nextOper == null ? string.Empty : nextOper.StepID;

            row.IS_LOT_ARRANGE = "N";
            row.IS_RES_FIX_LOT_ARRANGE = "N";

            row.PRODUCT_ID = lot.CurrentProductID;
            row.CUSTOMER_ID = lot.CurrentCustomerID;
            //row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();  //Wip의 EndCustomerID

            row.ORDERNO = (decimal)lot.Wip.OrderNO;

            row.RESOURCE_ID = "Dummy";
            row.EQP_STATUS = "BUSY";

            row.RESOURCE_GROUP = "";
            row.RESOURCE_MES = "";
            row.WORK_AREA = "";

            row.LEAD_TIME = 0;
            row.PROC_TIME = 0;
            row.TACT_TIME = 0;
            row.SETUP_TIME = 0;
            row.CAPACITY_USAGE = 0;

            row.PRE_END_TIME_DT = lot.PreEndTime == DateTime.MinValue ? DateTime.MinValue : lot.PreEndTime;
            row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();

            row.START_TIME_DT = now;
            row.START_TIME = now.DateTimeToString();

            var endTime = now;
            row.END_TIME_DT = endTime;
            row.END_TIME = endTime.DateTimeToString();

            row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);
            row.IS_CT_OPER = "N";

            lot.PreEndTime = endTime;

            // BOM From 정보 입력
            row.FROM_PROD_ID = lot.FromProductID;
            row.FROM_CUSTOMER_ID = lot.FromCustomerID;
            row.MODEL_CHANGE = lot.FromProductID.IsNullOrEmpty() ? string.Empty : "Y";

            // Lot의 From 정보 초기화
            lot.FromProductID = string.Empty;
            lot.FromCustomerID = string.Empty;

            // Wip 수량
            row.YIELD = 1;
            row.IS_SPECIAL_YIELD = "N";

            row.IN_QTY = (decimal)lot.UnitQtyDouble;
            row.PLAN_QTY = (decimal)lot.UnitQtyDouble;

            // ProductChange는 수율 적용하지 않음
            //lot.UnitQty = 
            //lot.UnitQtyDouble

            if (lot.CurrentWorkGroup != null)
            {
                row.JOB_COND_KEY = lot.CurrentWorkGroup.JobConditionGroup.Key;
                row.WORK_GROUP_KEY = lot.CurrentWorkGroup.GroupKey;
            }

            row.IS_LOT_PROD_CHANGE = IsLotProdChange(lot, bom, row);
            row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

            row.URGENT_CODE = lot.Wip.UrgentCode;
            row.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            row.WIP_REEL_LABELING = lot.Wip.IsReelLabeled ? "Y" : "N";

            // Demand 정보 입력
            var pegTar = (lot.WipInfo as SEMWipInfo).PeggedTargets.FirstOrDefault();
            if (pegTar != null)
            {
                var semPp = pegTar.PegPart as SEMGeneralPegPart;

                row.DEMAND_QTY = (int)semPp.DemandQty;
                row.BULK_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DemandID;
                row.TAPING_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DemandID : "";
                row.BULK_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pegTar.Week;
                row.TAPING_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? pegTar.Week : "";
                row.BULK_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DueDate.DateTimeToString();
                row.TAPING_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DueDate.DateTimeToString() : "";
                row.PRIORITY = semPp.Priority.ToString();
                row.END_CUSTOMER_ID = semPp.EndCustomerID; // Demand의 EndCustomerID

                DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
                row.LPST_DT = lpst;
                row.LPST = lpst.DateTimeToString();
                row.LAST_TARGET_DATE = semPp.DueDate.DateTimeToString();
                if (lpst != DateTime.MinValue)
                    row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays;
            }
            else
            {
                row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
            }

            if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
            {
                row.SPLIT_REASON = lot.SplitReason;
                lot.IsSplitRecorded = true;
            }

            if (lot.CurrentStepID == "SG6094")
            {
                if (InputMart.Instance.OperPlanSmallLotBTDic.TryGetValue(lot.LotID, out var prevRow))
                {
                    row.PRE_END_TIME = prevRow.PRE_END_TIME;
                    row.PRE_END_TIME_DT = prevRow.PRE_END_TIME_DT;
                    row.START_TIME = prevRow.START_TIME;
                    row.START_TIME_DT = prevRow.START_TIME_DT;
                    row.END_TIME = prevRow.END_TIME;
                    row.END_TIME_DT = prevRow.END_TIME_DT;
                    row.LEAD_TIME = prevRow.LEAD_TIME;
                    row.LPST = prevRow.LPST;
                    row.LPST_DT = prevRow.LPST_DT;
                    row.WAITING_TIME = lot.GetWaitingTime(prevRow.PRE_END_TIME_DT, prevRow.START_TIME_DT);

                    //OutputMart.Instance.OPER_PLAN.Table.Rows.Remove(prevRow);
                }
                else
                {
                    WriteLog.WriteErrorLog($"{lot.LotID} Small Lot로직 확인 바람");
                }
            }

            OutputMart.Instance.OPER_PLAN.Add(row);
        }


        public static void WriteDummyOperPlan(SEMLot lot, SEMBOM bom)
        {
            OPER_PLAN row = new OPER_PLAN();
            SEMGeneralStep step = lot.CurrentSEMStep;
            DateTime now = AoFactory.Current.NowDT;

            row.SUPPLYCHAIN_ID = "MLCC";
            row.PLAN_ID = InputMart.Instance.PlanID;
            row.SITE_ID = InputMart.Instance.SiteID;
            row.TO_SITE_ID = InputMart.Instance.SiteID;
            row.EXE_MODULE = "PLAN";
            row.STAGE_ID = "PLAN";

            row.LOT_ID = lot.LotID;
            row.FACTORY_ID = lot.FactoryID.IsNullOrEmpty() ? "-" : lot.FactoryID;
            row.ROUTE_ID = step.SEMRouteID;
            row.OPER_ID = step.StepID == "SG6094_BT" ? "SG6094" : step.StepID;
            row.OPER_SEQ = step.StepID == "SG6094_BT" ? 41002000 : step.Sequence;
            row.PLAN_TYPE = "BT";
            row.PLAN_SEQ = ++InputMart.Instance.OperPlanSeq;
            row.PROCESSING_TYPE = "DUMMY";

            var nextOper = lot.GetNextOper();
            row.TO_ROUTE_ID = nextOper == null ? string.Empty : "SGC123";
            row.TO_OPER_ID = nextOper == null ? string.Empty : "SG6095";

            row.IS_LOT_ARRANGE = "N";
            row.IS_RES_FIX_LOT_ARRANGE = "N";

            row.PRODUCT_ID = bom.ToProductID;
            row.CUSTOMER_ID = bom.ToCustomerID;
            //row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();  //Wip의 EndCustomerID

            row.ORDERNO = (decimal)lot.Wip.OrderNO;

            row.RESOURCE_ID = "Dummy";
            row.EQP_STATUS = "BUSY";

            row.RESOURCE_GROUP = "";
            row.RESOURCE_MES = "";
            row.WORK_AREA = "";

            var tat = lot.CurrentSEMStep.GetTat(lot.Wip.IsSmallLot);
            row.LEAD_TIME = (decimal)tat;
            row.PROC_TIME = 0;
            row.TACT_TIME = 0;
            row.SETUP_TIME = 0;
            row.CAPACITY_USAGE = 0;

            row.PRE_END_TIME_DT = lot.PreEndTime == DateTime.MinValue ? DateTime.MinValue : lot.PreEndTime;
            row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();

            row.START_TIME_DT = now;
            row.START_TIME = now.DateTimeToString();

            var endTime = now.AddMinutes(tat);
            row.END_TIME_DT = endTime;
            row.END_TIME = endTime.DateTimeToString();

            row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);
            row.IS_CT_OPER = "N";

            lot.PreEndTime = endTime;

            // BOM From 정보 입력
            row.FROM_PROD_ID = lot.CurrentProductID;
            row.FROM_CUSTOMER_ID = lot.CurrentCustomerID;
            row.MODEL_CHANGE = "Y";

            // Lot의 From 정보 초기화
            //lot.FromProductID = string.Empty;
            //lot.FromCustomerID = string.Empty;

            // Wip 수량
            row.YIELD = 1;
            row.IS_SPECIAL_YIELD = "N";

            row.IN_QTY = (decimal)lot.UnitQtyDouble;
            row.PLAN_QTY = (decimal)lot.UnitQtyDouble;

            // ProductChange는 수율 적용하지 않음
            //lot.UnitQty = 
            //lot.UnitQtyDouble

            row.URGENT_CODE = lot.Wip.UrgentCode;
            row.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            row.WIP_REEL_LABELING = lot.Wip.IsReelLabeled ? "Y" : "N";

            if (lot.CurrentWorkGroup != null)
            {
                row.JOB_COND_KEY = lot.CurrentWorkGroup.JobConditionGroup.Key;
                row.WORK_GROUP_KEY = lot.CurrentWorkGroup.GroupKey;
            }

            row.IS_LOT_PROD_CHANGE = IsLotProdChange(lot, bom, row);
            row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

            // Demand 정보 입력
            var pegTar = (lot.WipInfo as SEMWipInfo).PeggedTargets.FirstOrDefault();
            if (pegTar != null)
            {
                var semPp = pegTar.PegPart as SEMGeneralPegPart;

                row.DEMAND_QTY = (int)semPp.DemandQty;
                row.BULK_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DemandID;
                row.TAPING_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DemandID : "";
                row.BULK_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pegTar.Week;
                row.TAPING_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? pegTar.Week : "";
                row.BULK_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DueDate.DateTimeToString();
                row.TAPING_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DueDate.DateTimeToString() : "";
                row.PRIORITY = semPp.Priority.ToString();
                row.END_CUSTOMER_ID = semPp.EndCustomerID; // Demand의 EndCustomerID

                string stepID = lot.CurrentStepID == "SG6094_BT" ? "SG6094" : lot.CurrentStepID;

                DateTime lpst = SimHelper.GetLPST(lot, stepID);
                row.LPST_DT = lpst;
                row.LPST = lpst.DateTimeToString();
                row.LAST_TARGET_DATE = semPp.DueDate.DateTimeToString();
                if (lpst != DateTime.MinValue)
                    row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays;
            }
            else
            {
                row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
            }

            if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
            {
                row.SPLIT_REASON = lot.SplitReason;
                lot.IsSplitRecorded = true;
            }

            InputMart.Instance.OperPlanSmallLotBTDic.Add(lot.LotID, row);

            //OutputMart.Instance.OPER_PLAN.Add(row);
        }


        public static void WriteProdChangeOperPlanDetail(SEMLot lot, SEMBOM bom, SEMGeneralPegPart pp)
        {
            OPER_PLAN_DETAIL row = new OPER_PLAN_DETAIL();
            SEMGeneralStep step = lot.CurrentSEMStep;
            DateTime now = AoFactory.Current.NowDT;

            //Small lot의 경우 BT에서 SmallLotStockLeadTime만큼 LeadTime을 적용한다. 
            //bool isSmallLot = lot.Wip.IsSmallLot && bom != null && bom.ChangeType == "BT";

            row.SUPPLYCHAIN_ID = "MLCC";
            row.PLAN_ID = InputMart.Instance.PlanID;
            row.SITE_ID = InputMart.Instance.SiteID;


            row.LOT_ID = lot.LotID;
            row.ROUTE_ID = step.SEMRouteID;
            row.OPER_ID = step.StepID;
            row.OPER_SEQ = step.Sequence;
            row.PLAN_TYPE = bom == null ? "WR" : bom.ChangeType;
            row.PLAN_SEQ = ++InputMart.Instance.OperPlanDetailSeq;

            row.PRODUCT_ID = lot.CurrentProductID;
            row.CUSTOMER_ID = lot.CurrentCustomerID;
            //row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();  //Wip의 EndCustomerID

            row.ORDERNO = lot.Wip.OrderNO.ToString();

            row.RESOURCE_ID = "Dummy";
            row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();
            //row.START_TIME_DT = now;
            row.START_TIME = now.DateTimeToString();
            //row.END_TIME_DT = now;
            var endTime = now;
            row.END_TIME = endTime.DateTimeToString();

            row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);

            double a = lot.PeggingDemands[pp].Item1 / lot.PeggingDemands.Values.Sum(x => x.Item1);
            row.IN_QTY = (decimal)(lot.UnitQtyDouble * a);
            row.PLAN_QTY = (decimal)(lot.UnitQtyDouble /* * step.Yield*/ * a);
            row.WEEK_NO = pp.Week;
            // ProductChange는 수율 적용하지 않음

            row.IS_LOT_PROD_CHANGE = IsLotProdChangeDetail(lot, bom, row);
            row.IS_LOT_ARRANGE = "N";
            row.IS_RES_FIX_LOT_ARRANGE = "N";
            row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

            row.URGENT_CODE = lot.Wip.UrgentCode;
            row.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            // Demand 정보 입력
            if (pp != null)
            {
                row.DEMAND_QTY = (decimal)lot.PeggingDemands[pp].Item1;
                row.BULK_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DemandID;
                row.TAPING_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DemandID : "";
                row.BULK_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.Week;
                row.TAPING_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.Week : "";
                row.BULK_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DueDate.DateTimeToString();
                row.TAPING_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DueDate.DateTimeToString() : "";
                row.PRIORITY = pp.Priority.ToString();
                row.END_CUSTOMER_ID = pp.EndCustomerID; // Demand의 EndCustomerID

                DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
                row.LPST = lpst.DateTimeToString();
                row.LAST_TARGET_DATE = pp.DueDate.DateTimeToString();
                if (lpst != DateTime.MinValue)
                    row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays;

            }
            else
            {
                row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
            }

            if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
            {
                // row.SPLIT_REASON = lot.SplitReason;
                // lot.IsSplitRecorded = true;
            }

            OutputMart.Instance.OPER_PLAN_DETAIL.Add(row);
        }

        private static string IsLotProdChangeDetail(SEMLot lot, SEMBOM bom, OPER_PLAN_DETAIL row)
        {
            ICollection<SEMBOM> tempBOM;
            if (InputMart.Instance.LotProdChangeDic.TryGetValue(row.LOT_ID, out tempBOM))
            {
                SEMBOM lotProdChangeBOM = tempBOM.FirstOrDefault();
                return lot.Wip.IsMultiLotProdChange == false && bom == lotProdChangeBOM ? "Y" : "N";
            }
            else
                return "N";
        }

        public static void WriteBucketOperPlan(SEMLot lot, Time tat)
        {
            SEMGeneralStep step = lot.CurrentStep as SEMGeneralStep;
            SEMEqp eqp = lot.CurrentPlan.LoadedResource as SEMEqp;
            DateTime now = AoFactory.Current.NowDT;

            OPER_PLAN row = new OPER_PLAN();

            row.SUPPLYCHAIN_ID = "MLCC";
            row.PLAN_ID = InputMart.Instance.PlanID;
            row.SITE_ID = InputMart.Instance.SiteID;
            row.TO_SITE_ID = InputMart.Instance.SiteID;
            row.EXE_MODULE = "PLAN";
            row.STAGE_ID = "PLAN";

            row.LOT_ID = lot.LotID;
            row.FACTORY_ID = lot.FactoryID.IsNullOrEmpty() ? "-" : lot.FactoryID;
            row.ROUTE_ID = step.SEMRouteID;
            row.OPER_ID = step.StepID;
            row.OPER_SEQ = step.Sequence;
            row.PLAN_TYPE = step.StepID == "SG6094_BT" ? "BT" : "OPER";
            row.PLAN_SEQ = ++InputMart.Instance.OperPlanSeq;
            row.PROCESSING_TYPE = lot.CurrentSEMStep.StdStep.IsProcessing ? "PROCESSING" : "DUMMY";


            var nextOper = lot.GetNextOper();
            row.TO_ROUTE_ID = nextOper == null ? string.Empty : nextOper.SEMRouteID;
            row.TO_OPER_ID = nextOper == null ? string.Empty : nextOper.StepID == "SG6094_BT" ? "BT" : nextOper.StepID;

            row.IS_LOT_ARRANGE = "N";
            row.IS_RES_FIX_LOT_ARRANGE = "N";

            // Product 정보입력
            row.PRODUCT_ID = lot.CurrentProductID;
            row.CUSTOMER_ID = lot.CurrentCustomerID;
            // row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();  //Wip의 EndCustomerID

            row.ORDERNO = (decimal)lot.Wip.OrderNO;

            string resID = string.Empty;
            if (lot.Wip.IsNoResRunLot)
            {
                resID = lot.Wip.WipEqpID;
                lot.Wip.IsNoResRunLot = false;
            }
            else
                resID = "Dummy";

            row.RESOURCE_ID = resID;
            row.EQP_STATUS = "BUSY";//(lot.CurrentPlan as SEMPlanInfo).EqpState.ToString();


            double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
            row.YIELD = (decimal)yield;
            row.IS_SPECIAL_YIELD = isSpecialYield ? "Y" : "N";

            row.IN_QTY = (decimal)lot.UnitQtyDouble;
            row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * yield);

            lot.UnitQtyDouble = lot.UnitQtyDouble * yield;

            DateTime endTime = now.AddMinutes(tat.TotalMinutes);
            row.LEAD_TIME = (decimal)tat.TotalMinutes;
            row.PROC_TIME = 0;
            row.TACT_TIME = 0;
            row.SETUP_TIME = 0;
            row.CAPACITY_USAGE = 0;

            row.START_TIME = now.DateTimeToString(true);
            row.START_TIME_DT = now;

            row.END_TIME = endTime.DateTimeToString();
            row.END_TIME_DT = endTime;

            row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();
            row.PRE_END_TIME_DT = lot.PreEndTime == DateTime.MinValue ? DateTime.MinValue : lot.PreEndTime;

            row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);

            row.IS_CT_OPER = "N";

            lot.PreEndTime = endTime;

            // BOM From 정보 입력
            row.FROM_PROD_ID = lot.FromProductID;
            row.FROM_CUSTOMER_ID = lot.FromCustomerID;
            row.MODEL_CHANGE = lot.FromProductID.IsNullOrEmpty() ? string.Empty : "Y";

            // Lot의 From 정보 초기화
            lot.FromProductID = string.Empty;
            lot.FromCustomerID = string.Empty;

            if (lot.CurrentWorkGroup != null)
            {
                row.JOB_COND_KEY = lot.CurrentWorkGroup.JobConditionGroup.Key;
                row.WORK_GROUP_KEY = lot.CurrentWorkGroup.GroupKey;
            }

            row.IS_LOT_ARRANGE = "N";
            row.IS_LOT_PROD_CHANGE = "N";
            row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

            row.URGENT_CODE = lot.Wip.UrgentCode;
            row.URGENT_PRIORITY = lot.Wip.UrgentPriority;

            row.WIP_REEL_LABELING = lot.Wip.IsReelLabeled ? "Y" : "N";

            // Demand 정보 입력
            var pegTar = (lot.WipInfo as SEMWipInfo).PeggedTargets.FirstOrDefault();
            if (pegTar != null)
            {
                var semPp = pegTar.PegPart as SEMGeneralPegPart;

                row.DEMAND_QTY = (int)semPp.DemandQty;
                row.BULK_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DemandID;
                row.TAPING_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DemandID : "";
                row.BULK_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pegTar.Week;
                row.TAPING_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? pegTar.Week : "";
                row.BULK_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DueDate.DateTimeToString();
                row.TAPING_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DueDate.DateTimeToString() : "";
                row.PRIORITY = semPp.Priority.ToString();
                row.END_CUSTOMER_ID = semPp.EndCustomerID; // Demand의 EndCustomerID

                DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
                row.LPST_DT = lpst;
                row.LPST = lpst.DateTimeToString();
                row.LAST_TARGET_DATE = semPp.DueDate.DateTimeToString();
                if (lpst != DateTime.MinValue)
                    row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays; // 음수면 납기보다 빨리 만든것

                //if (nextOper == null) // 마지막 oper 면 아래 정보 써줌
                //{
                //    if (row.LPST_GAP_DAY < 0)
                //        lot.PlanState = "ONTIME";
                //    else
                //        lot.PlanState = "DELAY";

                //    lot.IsLPSTLate = row.LPST_DT > semPp.DueDate;
                //}

            }
            else
            {
                row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
            }

            if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
            {
                row.SPLIT_REASON = lot.SplitReason;
                lot.IsSplitRecorded = true;
            }

            OutputMart.Instance.OPER_PLAN.Add(row);
        }

        public static void WriteBucketOperPlanDetail(SEMLot lot, Time tat, SEMGeneralPegPart pp)
        {
            SEMGeneralStep step = lot.CurrentStep as SEMGeneralStep;
            SEMEqp eqp = lot.CurrentPlan.LoadedResource as SEMEqp;
            DateTime now = AoFactory.Current.NowDT;

            OPER_PLAN_DETAIL row = new OPER_PLAN_DETAIL();

            row.SUPPLYCHAIN_ID = "MLCC";
            row.PLAN_ID = InputMart.Instance.PlanID;
            row.SITE_ID = InputMart.Instance.SiteID;

            row.LOT_ID = lot.LotID;
            row.ROUTE_ID = step.SEMRouteID;
            row.OPER_ID = step.StepID;
            row.OPER_SEQ = step.Sequence;
            row.PLAN_TYPE = "OPER";
            row.PLAN_SEQ = ++InputMart.Instance.OperPlanDetailSeq;

            // Product 정보입력
            row.PRODUCT_ID = lot.CurrentProductID;
            row.CUSTOMER_ID = lot.CurrentCustomerID;

            row.ORDERNO = lot.Wip.OrderNO.ToString();

            row.RESOURCE_ID = "Dummy";
                        
            double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
            double a = lot.PeggingDemands[pp].Item1 / lot.PeggingDemands.Values.Sum(x => x.Item1);
            row.IN_QTY = (decimal)(lot.UnitQtyDouble * a);
            row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * yield * a);

            DateTime endTime = now.AddMinutes(tat.TotalMinutes);
            row.START_TIME = now.DateTimeToString(true);
            row.END_TIME = endTime.DateTimeToString();

            row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);

            row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();

            row.IS_LOT_ARRANGE = "N";
            row.IS_LOT_PROD_CHANGE = "N";
            row.IS_RES_FIX_LOT_ARRANGE = "N";
            row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

            row.URGENT_CODE = lot.Wip.UrgentCode;
            row.URGENT_PRIORITY = lot.Wip.UrgentPriority;
            row.WEEK_NO = pp.Week;

            // Demand 정보 입력
            if (pp != null)
            {
                row.DEMAND_QTY = (decimal)lot.PeggingDemands[pp].Item1;
                row.BULK_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DemandID;
                row.TAPING_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DemandID : "";
                row.BULK_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.Week;
                row.TAPING_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.Week : "";
                row.BULK_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DueDate.DateTimeToString();
                row.TAPING_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DueDate.DateTimeToString() : "";
                row.PRIORITY = pp.Priority.ToString();
                row.END_CUSTOMER_ID = pp.EndCustomerID; // Demand의 EndCustomerID

                DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
                row.LPST = lpst.DateTimeToString();
                row.LAST_TARGET_DATE = pp.DueDate.DateTimeToString();
                if (lpst != DateTime.MinValue)
                    row.LPST_GAP_DAY = (decimal)(now - lpst).TotalDays; // 음수면 납기보다 빨리 만든것

                var nextOper = lot.GetNextOper();

                // DELAY_SHORT_RPT정보 입력
                if (lot.CurrentStepID == pp.TargetOperID) // 마지막 oper 면 아래 정보 써줌
                {
                    DelayShortInfo info;
                    if (lot.DelayShortInfoDic.TryGetValue(pp.DemandID, out info) == false)
                        WriteLog.WriteErrorLog($"DelayShortInfo를 찾을 수 없습니다(1). LOT_ID{lot.LotID}  DEMAND_ID:{pp.DemandID}");

                    // 마지막 공정의 start time 입력
                    info.LotCompleteTime = now;
                    lot.LastStartTime = now;
                }
            }
            else
            {
                row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
            }

            OutputMart.Instance.OPER_PLAN_DETAIL.Add(row);
        }

        public static void WriteProcessingOperPlan(SEMEqp eqp, SEMLot lot, LoadingStates state)
        {
            SEMGeneralStep step = lot.CurrentSEMStep;
            DateTime now = AoFactory.Current.NowDT;
            string key = CommonHelper.CreateKey(eqp.ResID, lot.CurrentStepID, lot.LotID);

            OPER_PLAN row;
            if (InputMart.Instance.OperPlanDic.TryGetValue(key, out row))
            {
                // TRACK_OUT
                double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
                row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * yield);
                row.END_TIME_DT = now;
                row.END_TIME = now.DateTimeToString();

                lot.UnitQtyDouble = lot.UnitQtyDouble * yield;

                lot.PreEndTime = now;

                InputMart.Instance.OperPlanDic.Remove(key);
                OutputMart.Instance.OPER_PLAN.Add(row);

                return;
            }
            else
            {
                // TRACK_IN

                bool isRunWip = lot.Wip.WipState == "Run" && lot.CurrentStepID == lot.Wip.InitialStep.StepID ? true : false;

                row = new OPER_PLAN();

                row.SUPPLYCHAIN_ID = "MLCC";
                row.PLAN_ID = InputMart.Instance.PlanID;
                row.SITE_ID = InputMart.Instance.SiteID;
                row.TO_SITE_ID = InputMart.Instance.SiteID;
                row.EXE_MODULE = "PLAN";
                row.STAGE_ID = "PLAN";

                row.LOT_ID = lot.LotID;
                row.FACTORY_ID = eqp.FactoryID;

                lot.FactoryID = eqp.FactoryID;
                lot.FloorID = eqp.FloorID;

                lot.Wip.FactoryID = eqp.FactoryID;
                lot.Wip.FloorID = eqp.FloorID;

                row.ROUTE_ID = step.SEMRouteID;
                row.OPER_ID = step.StepID;
                row.OPER_SEQ = step.Sequence;
                row.PLAN_TYPE = "OPER";
                row.PLAN_SEQ = ++InputMart.Instance.OperPlanSeq;
                row.PROCESSING_TYPE = lot.CurrentSEMStep.StdStep.IsProcessing ? "PROCESSING" : "DUMMY";

                var nextOper = lot.GetNextOper();
                row.TO_ROUTE_ID = nextOper == null ? string.Empty : nextOper.SEMRouteID;
                row.TO_OPER_ID = nextOper == null ? string.Empty : nextOper.StepID;

                row.IS_RES_FIX_LOT_ARRANGE = IsResFixLotArrange(eqp, lot);
                row.IS_LOT_ARRANGE = IsLotArrange(eqp, lot);
                row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

                // Product 정보입력
                row.PRODUCT_ID = lot.CurrentProductID;
                row.CUSTOMER_ID = lot.CurrentCustomerID;
                //row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();

                row.ORDERNO = (decimal)lot.Wip.OrderNO;

                row.RESOURCE_ID = eqp.ResID;
                row.EQP_STATUS = state.ToString();

                row.RESOURCE_GROUP = eqp.ResourceGroup == null ? "-" : eqp.ResourceGroup;
                row.RESOURCE_MES = eqp.ResourceMES == null ? "-" : eqp.ResourceMES;
                row.WORK_AREA = eqp.WORK_AREA == null ? "-" : eqp.WORK_AREA;

                row.JOB_CONDITION = lot.GetJobConditionToString();
                row.CONDITION_SET = lot.GetConditionSet(eqp.ResID, step.StepID);

                row.URGENT_CODE = lot.Wip.UrgentCode;
                row.URGENT_PRIORITY = lot.Wip.UrgentPriority;

                row.WIP_REEL_LABELING = lot.Wip.IsReelLabeled ? "Y" : "N";

                if (state == LoadingStates.BUSY)
                {
                    row.PRE_END_TIME_DT = lot.PreEndTime == DateTime.MinValue ? DateTime.MinValue : lot.PreEndTime;
                    row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();

                    DateTime startTime = isRunWip ? lot.Wip.LastTrackInTime : AoFactory.Current.NowDT;
                    row.START_TIME_DT = startTime;
                    row.START_TIME = startTime.DateTimeToString();

                    row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);

                    double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
                    row.YIELD = (decimal)yield;
                    row.IS_SPECIAL_YIELD = isSpecialYield ? "Y" : "N";

                    row.IN_QTY = (decimal)lot.UnitQtyDouble;

                    double tactTime = TimeHelper.GetCycleTime(step, eqp.EqpID);
                    double capaUsage = tactTime * lot.UnitQtyDouble;
                    if (isRunWip)
                        capaUsage -= (ModelContext.Current.StartTime - lot.Wip.LastTrackInTime).TotalMinutes; // run재공의 경우 Plan Start Time 이전의 capa는 뺌

                    row.LEAD_TIME = 0;
                    row.PROC_TIME = 0;
                    row.TACT_TIME = (decimal)tactTime;
                    row.SETUP_TIME = 0;
                    row.CAPACITY_USAGE = (decimal)capaUsage;

                    row.IS_SAME_LOT_PROCESSING = lot.IsSameLotProcessing ? "Y" : "N";
                    row.IS_CT_OPER = step.IsCTOper(eqp.EqpID) ? "Y" : "N";
                }
                else if (state == LoadingStates.SETUP)
                {
                    row.PLAN_TYPE = "SETUP";

                    row.YIELD = 1; //(decimal)step.Yield;
                    row.IS_SPECIAL_YIELD = "N";
                    row.IN_QTY = (decimal)lot.UnitQtyDouble;
                    row.PLAN_QTY = (decimal)lot.UnitQtyDouble;

                    //SETUP은 수율 적용하지 않음
                    //lot.UnitQty = 
                    //lot.UnitQtyDouble

                    row.LEAD_TIME = 0;
                    row.PROC_TIME = 0;
                    row.TACT_TIME = 0;
                    row.SETUP_TIME = (decimal)eqp.SetupTime.TotalMinutes;
                    row.CAPACITY_USAGE = (decimal)eqp.SetupTime.TotalMinutes;

                    DateTime startTime = AoFactory.Current.NowDT;
                    DateTime endTime = AoFactory.Current.NowDT + eqp.SetupTime;
                    row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();
                    row.PRE_END_TIME_DT = lot.PreEndTime == DateTime.MinValue ? DateTime.MinValue : lot.PreEndTime;

                    row.START_TIME = startTime.DateTimeToString(true);
                    row.START_TIME_DT = startTime;

                    row.END_TIME = endTime.DateTimeToString();
                    row.END_TIME_DT = endTime;

                    row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);
                    row.IS_CT_OPER = "N";

                    row.IS_ONLY_MODEL_CHANGE = lot.IsOnlyModelChange ? "Y" : "N";

                    lot.IsOnlyModelChange = false;
                    //lot.PreEndTime = endTime;
                }

                // BOM From 정보 입력
                row.FROM_PROD_ID = lot.FromProductID;
                row.FROM_CUSTOMER_ID = lot.FromCustomerID;
                row.MODEL_CHANGE = lot.FromProductID.IsNullOrEmpty() ? string.Empty : "Y";

                // Lot의 From 정보 초기화
                lot.FromProductID = string.Empty;
                lot.FromCustomerID = string.Empty;

                if (lot.CurrentWorkGroup != null)
                {
                    row.JOB_COND_KEY = lot.CurrentWorkGroup.JobConditionGroup.Key;
                    row.WORK_GROUP_KEY = lot.CurrentWorkGroup.GroupKey;
                }

                row.IS_LOT_PROD_CHANGE = "N";
                row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

                // Demand 정보 입력
                var pegTar = (lot.WipInfo as SEMWipInfo).PeggedTargets.FirstOrDefault();
                if (pegTar != null)
                {
                    var semPp = pegTar.PegPart as SEMGeneralPegPart;

                    row.DEMAND_QTY = (int)semPp.DemandQty;
                    row.BULK_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DemandID;
                    row.TAPING_DEMAND_ID = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DemandID : "";
                    row.BULK_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pegTar.Week;
                    row.TAPING_WEEK = semPp.TargetOperID == Constants.TAPING_OPER_ID ? pegTar.Week : "";
                    row.BULK_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? "" : semPp.DueDate.DateTimeToString();
                    row.TAPING_DUE_DATE = semPp.TargetOperID == Constants.TAPING_OPER_ID ? semPp.DueDate.DateTimeToString() : "";
                    row.PRIORITY = semPp.Priority.ToString();
                    row.END_CUSTOMER_ID = semPp.EndCustomerID; // Demand의 EndCustomerID

                    DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);
                    row.LPST_DT = lpst;
                    row.LPST = lpst.DateTimeToString();
                    row.LAST_TARGET_DATE = semPp.DueDate.DateTimeToString();
                    if (lpst != DateTime.MinValue)
                        row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays;
                }
                else
                {
                    row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                    row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
                }

                if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
                {
                    row.SPLIT_REASON = lot.SplitReason;
                    lot.IsSplitRecorded = true;
                }

                if (state == LoadingStates.SETUP)
                    OutputMart.Instance.OPER_PLAN.Add(row);
                else
                    InputMart.Instance.OperPlanDic.Add(key, row); //ProcessingOper의 BUSY는 TRACK_OUT에서 시점에서 OutputMart에 add
            }
        }


        public static void WriteProcessingOperPlanDetail(SEMEqp eqp, SEMLot lot, LoadingStates state, SEMGeneralPegPart pp)
        {
            SEMGeneralStep step = lot.CurrentSEMStep;
            DateTime now = AoFactory.Current.NowDT;

            string key = CommonHelper.CreateKey(eqp.ResID, lot.CurrentStepID, lot.LotID, pp.DemandID);

            OPER_PLAN_DETAIL row;
            if (InputMart.Instance.OperPlanDetailDic.TryGetValue(key, out row))
            {
                // TRACK_OUT
                double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
                double a = lot.PeggingDemands[pp].Item1 / lot.PeggingDemands.Values.Sum(x => x.Item1);
                row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * yield * a);

                //row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * step.Yield);
                //row.END_TIME_DT = now;
                row.END_TIME = now.DateTimeToString();

                //lot.UnitQtyDouble = lot.UnitQtyDouble * step.Yield;

                //lot.PreEndTime = now;


                InputMart.Instance.OperPlanDetailDic.Remove(key);
                OutputMart.Instance.OPER_PLAN_DETAIL.Add(row);

                return;
            }
            else
            {
                // TRACK_IN

                bool isRunWip = lot.Wip.WipState == "Run" && lot.CurrentStepID == lot.Wip.InitialStep.StepID ? true : false;

                row = new OPER_PLAN_DETAIL();

                row.SUPPLYCHAIN_ID = "MLCC";
                row.PLAN_ID = InputMart.Instance.PlanID;
                row.SITE_ID = InputMart.Instance.SiteID;

                row.LOT_ID = lot.LotID;

                lot.FactoryID = eqp.FactoryID;
                lot.FloorID = eqp.FloorID;

                lot.Wip.FactoryID = eqp.FactoryID;
                lot.Wip.FloorID = eqp.FloorID;

                row.ROUTE_ID = step.SEMRouteID;
                row.OPER_ID = step.StepID;
                row.OPER_SEQ = step.Sequence;
                row.PLAN_TYPE = "OPER";
                row.PLAN_SEQ = ++InputMart.Instance.OperPlanDetailSeq;

                // Product 정보입력
                row.PRODUCT_ID = lot.CurrentProductID;
                row.CUSTOMER_ID = lot.CurrentCustomerID;
                //row.END_CUSTOMER_ID = lot.Wip.EndCustomerList.ListToString();  //Wip의 EndCustomerID

                row.ORDERNO = lot.Wip.OrderNO.ToString();

                row.RESOURCE_ID = eqp.ResID;
                //row.EQP_STATUS = state.ToString();

                //row.RESOURCE_GROUP = eqp.ResourceGroup;
                //row.RESOURCE_MES = eqp.ResourceMES;

                row.URGENT_CODE = lot.Wip.UrgentCode;
                row.URGENT_PRIORITY = lot.Wip.UrgentPriority;
                row.WEEK_NO = pp.Week;

                if (state == LoadingStates.BUSY)
                {
                    //row.YIELD = (decimal)step.Yield;
                    //row.IN_QTY = (decimal)lot.UnitQtyDouble;
                    double a = lot.PeggingDemands[pp].Item1 / lot.PeggingDemands.Values.Sum(x => x.Item1);
                    row.IN_QTY = (decimal)(lot.UnitQtyDouble * a);

                    //row.LEAD_TIME = 0;
                    //row.PROC_TIME = 0;
                    //row.TACT_TIME = (decimal)TimeHelper.GetCycleTime(step, eqp.EqpID);
                    //row.SETUP_TIME = 0;
                    //row.CAPACITY_USAGE = row.TACT_TIME * (decimal)lot.UnitQtyDouble;   // TACT_TIME * QTY

                    DateTime startTime = isRunWip ? lot.Wip.LastTrackInTime : AoFactory.Current.NowDT;
                    row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();
                    //row.START_TIME_DT = startTime;
                    row.START_TIME = startTime.DateTimeToString();

                    row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);
                }
                else if (state == LoadingStates.SETUP)
                {
                    row.PLAN_TYPE = "SETUP";

                    double yield = lot.CurrentSEMStep.GetYield(lot.CurrentProductID, lot.Wip.PowderCond, lot.Wip.CompositionCode, out bool isSpecialYield);
                    double a = lot.PeggingDemands[pp].Item1 / lot.PeggingDemands.Values.Sum(x => x.Item1);// 이 lot의 전체 수량중 해당 demand에 pegging된 수량 비율 (한 lot은 여러 demand에 pegging될 수 있다.)
                    row.IN_QTY = (decimal)(lot.UnitQtyDouble * a);
                    row.PLAN_QTY = (decimal)(lot.UnitQtyDouble * yield * a);

                    //SETUP은 수율 적용하지 않음
                    //lot.UnitQty = 
                    //lot.UnitQtyDouble

                    //row.LEAD_TIME = 0;
                    //row.PROC_TIME = 0;
                    //row.TACT_TIME = 0;
                    //row.SETUP_TIME = (decimal)eqp.SetupTime.TotalMinutes;
                    //row.CAPACITY_USAGE = 0;

                    DateTime startTime = AoFactory.Current.NowDT;
                    DateTime endTime = AoFactory.Current.NowDT + eqp.SetupTime;
                    row.PRE_END_TIME = lot.PreEndTime == DateTime.MinValue ? string.Empty : lot.PreEndTime.DateTimeToString();
                    row.START_TIME = startTime.DateTimeToString(true);
                    //row.START_TIME_DT = startTime;
                    row.END_TIME = endTime.DateTimeToString();

                    row.WAITING_TIME = lot.GetWaitingTime(lot.PreEndTime, now);
                    //row.END_TIME_DT = endTime;

                    //lot.PreEndTime = endTime;
                }

                //// BOM From 정보 입력
                //row.FROM_PROD_ID = lot.FromProductID;
                //row.FROM_CUSTOMER_ID = lot.FromCustomerID;
                //row.MODEL_CHANGE = lot.FromProductID.IsNullOrEmpty() ? string.Empty : "Y";

                //// Lot의 From 정보 초기화
                //lot.FromProductID = string.Empty;
                //lot.FromCustomerID = string.Empty;

                row.IS_LOT_PROD_CHANGE = "N";
                ICollection<SEMEqp> tempEqp;
                if (lot.Wip.LotArrangedEqpDic.TryGetValue(lot.CurrentStepID, out tempEqp))
                {
                    SEMEqp lotArrangedEqp = tempEqp.FirstOrDefault();
                    row.IS_LOT_ARRANGE = (lot.Wip.IsResFixLotArrange == false || lot.Wip.IsResFixLotArrange == true) && eqp == lotArrangedEqp ? "Y" : "N";
                }
                else
                    row.IS_LOT_ARRANGE = "N";

                row.IS_RES_FIX_LOT_ARRANGE = IsResFixLotArrange(eqp, lot);
                row.IS_SMALL_LOT = lot.Wip.IsSmallLot ? "Y" : "N";

                // Demand 정보 입력
                if (pp != null)
                {
                    row.DEMAND_QTY = (decimal)lot.PeggingDemands[pp].Item1;
                    row.BULK_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DemandID;
                    row.TAPING_DEMAND_ID = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DemandID : "";
                    row.BULK_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.Week;
                    row.TAPING_WEEK = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.Week : "";
                    row.BULK_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? "" : pp.DueDate.DateTimeToString();
                    row.TAPING_DUE_DATE = pp.TargetOperID == Constants.TAPING_OPER_ID ? pp.DueDate.DateTimeToString() : "";
                    row.PRIORITY = pp.Priority.ToString();
                    row.END_CUSTOMER_ID = pp.EndCustomerID; // Demand의 EndCustomerID

                    DateTime lpst = SimHelper.GetLPST(lot, lot.CurrentStepID);

                    row.LPST = lpst.DateTimeToString();
                    row.LAST_TARGET_DATE = pp.DueDate.DateTimeToString();
                    if (lpst != DateTime.MinValue)
                        row.LPST_GAP_DAY = (decimal)(AoFactory.Current.NowDT - lpst).TotalDays;
                }
                else
                {
                    row.BULK_DEMAND_ID = Constants.Unpeg;  //"(UNPEG)"
                    row.TAPING_DEMAND_ID = Constants.Unpeg; //"(UNPEG)"
                }

                //if ((lot.IsCapaSplitLot || lot.IsMultiLotSplit) && !lot.IsSplitRecorded)
                //{
                //    row.SPLIT_REASON = lot.SplitReason;
                //    lot.IsSplitRecorded = true;
                //}

                if (state == LoadingStates.SETUP)
                    OutputMart.Instance.OPER_PLAN_DETAIL.Add(row);
                else
                    InputMart.Instance.OperPlanDetailDic.Add(key, row); //ProcessingOper의 BUSY는 TRACK_OUT에서 시점에서 OutputMart에 add
            }
        }

        private static string GetJobConditionToString(this SEMLot lot)
        {
            string result = string.Empty;

            Dictionary<string, string> jobConditionDic;
            if (lot.StepJobConditions != null && lot.StepJobConditions.TryGetValue(lot.CurrentStepID, out jobConditionDic))
            {
                result = jobConditionDic.GetJobCondition();
            }
            else
            {
                WriteLog.WriteErrorLog($"JobCondition을 찾을 수 없습니다LOT_ID:{lot.LotID} OPER_ID:{lot.CurrentStepID}");
            }

            return result;
        }

        private static string IsLotArrange(SEMEqp eqp, SEMLot lot)
        {
            ICollection<SEMEqp> tempEqp;
            if (lot.Wip.LotArrangedEqpDic.TryGetValue(lot.CurrentStepID, out tempEqp))
            {
                return tempEqp.Contains(eqp) ? "Y" : "N";
            }
            else
                return "N";
        }

        private static string IsResFixLotArrange(SEMEqp eqp, SEMLot lot)
        {
            ICollection<SEMEqp> tempEqp;
            if (lot.Wip.LotArrangedEqpDic.TryGetValue(lot.CurrentStepID, out tempEqp))
            {
                SEMEqp lotArrangedEqp = tempEqp.FirstOrDefault();
                return lot.Wip.IsResFixLotArrange == true && eqp == lotArrangedEqp ? "Y" : "N";
            }
            else
                return "N";
        }

        private static string IsLotProdChange(SEMLot lot, SEMBOM bom, OPER_PLAN row)
        {
            ICollection<SEMBOM> tempBOM;
            if (InputMart.Instance.LotProdChangeDic.TryGetValue(row.LOT_ID, out tempBOM))
            {
                SEMBOM lotProdChangeBOM = tempBOM.FirstOrDefault();
                return lot.Wip.IsMultiLotProdChange == false && bom == lotProdChangeBOM ? "Y" : "N";
            }
            else
                return "N";
        }

        public static string GetConditionSet(this SEMLot lot, string eqpID, string stepID)
        {
            string result = string.Empty;

            SEMPegWipInfo pwi = lot.GetPegWipInfo(stepID);

            if (pwi == null)
            {
                if (lot.Wip.WipState.ToUpper() == "RUN")
                    result = "NONE(RunWip)";
                else
                    result = "NONE";
            }
            else
            {
                result = pwi.GetConditionSet(eqpID);

                if (result == "NONE")
                {
                    if (lot.Wip.IsResFixLotArrange)
                        result = $"NONE(ResFixLotArragne)";
                    else if (lot.Wip.IsLotArrange)
                        result = $"NONE(LotArragne)";

                    if (lot.Wip.WipState.ToUpper() == "RUN")
                        result = $"NONE(RunWip)";
                }
            }

            return result;
        }

        private static SEMPegWipInfo GetPegWipInfo(this SEMLot lot, string stepID)
        {
            SEMPegWipInfo pwi = null;
            if (lot.PlanWip == null)
            {
                return null;
            }

            if (lot.PlanWip.PegWipInfo == null)
            {
                foreach (var p in lot.PlanWip.SEMPegWipInfos)
                {
                    var prod = p.GetProduct(stepID);
                    if (prod == null)
                        return null;

                    if (lot.CurrentProductID == prod.ProductID)
                    {
                        pwi = p;
                        break;
                    }
                }

            }
            else
            {
                pwi = lot.PlanWip.PegWipInfo;
            }

            return pwi;
        }

        public static string GetConditionSet(this SEMPegWipInfo pwi, string eqpID)
        {
            string result = string.Empty;
            if (pwi.ConditionSet.TryGetValue(eqpID, out result) == false)
            {
                result = "NONE";
            }

            return result;
        }

        public static decimal GetWaitingTime(this SEMLot lot, DateTime preEndTime, DateTime startTime)
        {
            var planStartTime = InputMart.Instance.CutOffDateTime;

            bool isRunPlan = lot.Wip.IsRun && lot.Wip.InitialStep.StepID == lot.CurrentStepID;
            if (isRunPlan == false && preEndTime < planStartTime)
                preEndTime = planStartTime;

            var waitingTime = startTime.RoundToMs() - preEndTime.RoundToMs();

            decimal result = (decimal)Math.Round(waitingTime.TotalDays, 3);

            return result;
        }

    }

}