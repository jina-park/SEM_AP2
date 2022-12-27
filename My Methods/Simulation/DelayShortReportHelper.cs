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

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class DelayShortReportHelper
    {
        public static void WritePeggedLotReport(this SEMGeneralPegPart pp)
        {
            // Demand에 Pegging된 lot 별로 작성
            foreach (var peggedLotInfo in pp.PeggedLots)
            {
                peggedLotInfo.WriteDelayShortReport(pp);
            }                
        }

        public static void WriteDelayShortReport(this KeyValuePair<SEMLot, Tuple<double,double>> peggedLotInfo, SEMGeneralPegPart pp)
        {
            SEMLot peggedLot = peggedLotInfo.Key;
            double pegDemandQty = peggedLotInfo.Value.Item1;
            double lotOverPegQty = peggedLotInfo.Value.Item2;

            string reason = string.Empty;
            string state = string.Empty;

            DELAY_SHORT_RPT row = new DELAY_SHORT_RPT();

            row.SITE_ID = InputMart.Instance.SiteID;

            if (pp.TargetOperID == Constants.SORTING_OPER_ID)
            {
                row.ROUTE_ID = "SGC120";
                row.ROUTE_NAME = "Sorting";
            }
            else if (pp.TargetOperID == Constants.OI_OPER_ID)
            {
                row.ROUTE_ID = "SGC122";
                row.ROUTE_NAME = "Outgoing Inspection";
            }
            else if (pp.TargetOperID == Constants.TAPING_OPER_ID)
            {
                row.ROUTE_ID = "SGC123";
                row.ROUTE_NAME = "Taping";
            }

            row.OPER_ID = pp.TargetOperID;
            row.DEMAND_ID = pp.DemandID;
            row.PRODUCT_ID = pp.ProductID;
            row.WEEK = pp.Week;
            row.DUE_DATE = pp.DueDate.DateTimeToString();
            row.START_TIME = peggedLot.LastStartTime.DateTimeToString();
            row.LOT_ID = peggedLot.LotID;
            row.DEMAND_QTY = (decimal)pegDemandQty;  // pp.DemandQty;
            row.PEGGING_QTY = (decimal)lotOverPegQty;
            row.DELAY_SHORT_QTY = (decimal)pegDemandQty;
            row.DELAY_SHORT_PER = (decimal)(pegDemandQty / pp.DemandQty);

            DelayShortInfo info;
            if (peggedLot.DelayShortInfoDic.TryGetValue(pp.DemandID, out info) == false)
            {
                WriteLog.WriteErrorLog($"DelayShortInfo를 찾을 수 없습니다(2). LOT_ID{peggedLot.LotID}  DEMAND_ID:{pp.DemandID}"); // 오류
                OutputMart.Instance.DELAY_SHORT_RPT.Add(row);
                return;
            }

            if (info.LotCompleteTime == DateTime.MinValue)
            {
                info.PlanState = "PLANNING";
            }
            else if (ModelContext.Current.EndTime < peggedLot.LastStartTime)
            {
                info.PlanState = "PLANNING";
            }
            else if (pp.DueDate < info.LotCompleteTime)
            {
                info.PlanState = "DELAY";
                info.DelayDay = (info.LotCompleteTime - pp.DueDate).TotalDays;
            }
            else
            {
                info.PlanState = "ONTIME";
            }

            bool isArriveLate = ModelContext.Current.EndTime <= peggedLot.Wip.AvailableTime;
            if (info.PlanState == "PLANNING")
            {
                bool isLeadTimeLate = ModelContext.Current.EndTime < peggedLot.PlanWip.PegWipInfo.CalcAvailDate;

                state = "SHORT";
                if (isArriveLate)
                {
                    reason = "SHORT - Plan Horizon보다 늦게 도착";
                }
                else
                {
                    if (isLeadTimeLate)
                        reason = "SHORT - LeadTime 늦음";
                    else
                    {
                        bool isAlmostDone = IsAlmostDone(peggedLot);
                        if (isAlmostDone)
                            reason = "SHORT - LeadTime 늦음";
                        else
                            reason = "SHORT - CAPA 부족";
                    }
                }
            }

            else if (info.PlanState == "DELAY")
            {
                bool isLeadTimeLate = pp.DueDate < peggedLot.PlanWip.PegWipInfo.CalcAvailDate;

                state = "DELAY";
                reason = isArriveLate? "DELAY - Plan Horizon보다 늦게 도착" : isLeadTimeLate ? "DELAY - LeadTime 늦음" : "DELAY - CAPA 부족";
            }
            else if (info.PlanState == "ONTIME")
            {
                state = "ONTIME";
                reason = "";
            }
            else
            {
                WriteLog.WriteErrorLog($"peggedLot PlanState 오류 LOT_ID:{peggedLot.LotID} DEMAND_ID:{pp.DemandID}");
            }

            row.REASON = reason;
            row.DELAY_SHORT = state;
            row.DELAY_DAYS = (decimal)info.DelayDay;

            row.WIP_AVAILABLE_TIME = peggedLot.Wip.AvailableTime;
            row.CALC_AVAILABLE_TIME = peggedLot.PlanWip.PegWipInfo.CalcAvailDate;

            OutputMart.Instance.DELAY_SHORT_RPT.Add(row);
        }

        public static void WriteRemainDemandReport(this SEMGeneralPegPart pp)
        {
            double remainDemandQty = pp.TargetQty > 0 ? pp.TargetQty : 0;

            if (remainDemandQty <= 0)
                return;

            DELAY_SHORT_RPT row = new DELAY_SHORT_RPT();

            row.SITE_ID = InputMart.Instance.SiteID;
            if (pp.TargetOperID == Constants.SORTING_OPER_ID)
            {
                row.ROUTE_ID = "SGC120";
                row.ROUTE_NAME = "Sorting";
            }
            else if (pp.TargetOperID == Constants.TAPING_OPER_ID)
            {
                row.ROUTE_ID = "SGC123";
                row.ROUTE_NAME = "Taping";
            }
            else
            {
                row.ROUTE_ID = "SGC122";
                row.ROUTE_NAME = "Outgoing Inspection";
            }

            row.OPER_ID = pp.TargetOperID;
            row.DEMAND_ID = pp.DemandID;
            row.PRODUCT_ID = pp.ProductID;
            row.WEEK = pp.Week;
            row.DUE_DATE = pp.DueDate.DateTimeToString();
            row.DELAY_DAYS = 0;
            row.DELAY_SHORT = "SHORT";
            row.DEMAND_QTY = (decimal)remainDemandQty;
            row.PEGGING_QTY = 0;
            row.DELAY_SHORT_QTY = (decimal)remainDemandQty;
            row.DELAY_SHORT_PER = (decimal)(remainDemandQty / pp.DemandQty);
            row.REASON = "UNPEG";
            row.DETAIL_REASON = pp.UnpegReason;
            row.DETAIL_FILTER_REASON = pp.UnpegFilterReason;

            OutputMart.Instance.DELAY_SHORT_RPT.Add(row);
        }

        private static bool IsAlmostDone(SEMLot lot)
        {
            string demandOperID = lot.PeggingDemands.Keys.First().TargetOperID;

            // 코드 이렇게 짜면 확산때 힘듦
            if (demandOperID == "SG4460")
            {
                if (lot.CurrentSEMStep.Sequence > 41006000 || (lot.CurrentSEMStep.Sequence == 41006000 && lot.CurrentState == EntityState.RUN))
                    return true;
                else
                    return false;
            }
            else if (demandOperID == "SG4090")
            {
                if (lot.CurrentSEMStep.Sequence > 31000002 || (lot.CurrentSEMStep.Sequence == 31000002 && lot.CurrentState == EntityState.RUN))
                    return true;
                else
                    return false;
            }
            else
                return false;
        }
    }
}