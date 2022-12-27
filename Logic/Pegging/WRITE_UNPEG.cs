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
    public partial class WRITE_UNPEG
    {
        public void WRITE_UNPEG0(Mozart.SeePlan.Pegging.PegPart pegPart, ref bool handled)
        {
            foreach (var planWip in InputMart.Instance.PlanWipList)
            {
                WriteUnpegHistory(planWip);
            }
        }

        private void WriteUnpegHistory(SEMPlanWip planWip)
        {
            if (planWip.Qty == 0)
            {
                //
                // Pegging 완료된 wip
                //
            }
            else if (planWip.MapCount == 0)
            {
                //
                // 한번도 Pegging되지 않은 wip
                // Filter된 Wip 포함
                //

                bool isNoArrnage = planWip.UnpegReason.Contains("NO_ARRANGE") || planWip.UnpegReason.Contains("MASTER_DATA_NO_ARRANGE");
                bool isUnpegUrgentPlanWip = planWip.WipInfo.IsUrgent && planWip.WipInfo.InitialSEMStep.StdStep.IsProcessing && isNoArrnage == false && planWip.WipInfo.InitialSEMStep.StdStep.IsProcessing;
                if (isUnpegUrgentPlanWip)
                {
                    planWip.WipInfo.IsUrgentUnpeg = true;
                }

                    WriteUnpegHistorForNormalWip(planWip);
                }
            else if (planWip.MapCount != 0 && planWip.Qty > 0)
            {
                WriteLog.WriteErrorLog($"OverPegging 로직 오류 LOT_ID={planWip.LotID}");
            }
            else
            {
                WriteLog.WriteErrorLog($"Unpegging 로직 오류 LOT_ID={planWip.LotID}");
            }
        }

        private void WriteUnpegHistorForNormalWip(SEMPlanWip planWip)
        {
            bool isNoTarget = planWip.UnpegReason.Count == 0 ? true : false;
            bool isNoBom = planWip.UnpegReason.Count == 1 && planWip.UnpegReason.First() == "NO_NEXT_OPER(NO_BOM)" ? true : false;
            bool isNoArrnage = planWip.UnpegReason.Contains("NO_ARRANGE") || planWip.UnpegReason.Contains("MASTER_DATA_NO_ARRANGE");

            if (isNoTarget)
            {
                string category = "NO_TARGET";
                string reason = "NO_TARGET";
                string detailCode = "NO_TARGET";
                string detailData = "";

                WriteLog.WriteUnpegHistory(planWip, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(planWip, "NO_TARGET");
            }
            else if (isNoBom)
            {
                string category = "NO_TARGET";
                string reason = "NO_TARGET";
                string detailCode = "NO_NEXT_OPER(NO_BOM)";
                string detailData = planWip.UnpegDetailReason.ToList().ListToString();

                WriteLog.WriteUnpegHistory(planWip, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(planWip, "NO_TARGET");
            }
            else if (isNoArrnage)
            {
                planWip.UnpegReason.Remove("NO_NEXT_OPER(NO_BOM)");

                string category = "NOT_MATCH_TARGET";
                string reason = planWip.UnpegReason.ToList().ListToString();
                string detailCode = $"NO_ARRANGE_OPER : {planWip.NoArrangeOpers.ListToString()}";
                string detailData = planWip.NoArrnageReason.ListToString();

                WriteLog.WriteUnpegHistory(planWip, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(planWip, "NOT_MATCH_TARGET");
            }
            else
            {
                // unpeg 사유가 다양하면 그중 "NO_NEXT_OPER(NO_BOM)"는 출력하지 않는다.
                if (planWip.UnpegReason.Count > 1 && planWip.UnpegReason.Contains("NO_NEXT_OPER(NO_BOM)"))
                    planWip.UnpegReason.Remove("NO_NEXT_OPER(NO_BOM)");

                string category = "NOT_MATCH_TARGET";
                string reason = planWip.UnpegReason.ToList().ListToString();
                string detailCode = "";
                string detailData = planWip.UnpegReason.Count() == 0 ? "" : planWip.UnpegDetailReason.ToList().ListToString();

                WriteLog.WriteUnpegHistory(planWip, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(planWip, "NOT_MATCH_TARGET");
            }
        }



        public void WRITE_REMAINDEMAND(PegPart pegPart, ref bool handled)
        {
            //잔여 demand를 확인하기위한 테스트용 output 
            MergedPegPart mergedPegPart = pegPart as MergedPegPart;
            foreach (var item in mergedPegPart.Items)
            {
                foreach (var target in item.PegTargetList)
                {
                    SEMGeneralPegTarget pt = target as SEMGeneralPegTarget;
                    SEMGeneralPegPart pp = pt.PegPart as SEMGeneralPegPart;

                    //주석 : 테이블 삭제
                    //Outputs.REMAIN_DEMAND_LOG row = new REMAIN_DEMAND_LOG();
                    //row.DEMAND_ID = pt.SemMoPlan.DemandID;
                    //row.PRODUCT_ID = pp.Product.ProductID;
                    //row.QTY = pt.Qty;
                    //row.DUE_DATE = pt.Mo.DueDate;

                    //OutputMart.Instance.REMAIN_DEMAND_LOG.Add(row);
                }
            }
        }
    }
}