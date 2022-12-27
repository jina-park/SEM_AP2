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
    public static partial class SimHelper
    {
        internal static SEMEqp ToSEMEqp(this SEMAoEquipment aeqp)
        {
            return aeqp.Target as SEMEqp;
        }

        internal static SEMAoEquipment ToSEMAoEquipment(this AoEquipment aeqp)
        {
            return aeqp as SEMAoEquipment;
        }

        internal static SEMLot ToSEMLot(this IHandlingBatch hb)
        {
            if (hb == null)
                return null;

            return hb.Sample as SEMLot;
        }

        public static DateTime GetLPST(this SEMLot lot, string operID)
        {
            if (lot.PlanWip == null)
            {
                //WriteLog.WriteErrorLog($"PlanWip을 찾을 수 없습니다. LOT_ID : {lot.LotID}");
                return DateTime.MaxValue;
            }

            if (lot.PlanWip.PegWipInfo == null)
            {
                //WriteLog.WriteErrorLog($"PegWipInfo를 찾을 수 없습니다. LOT_ID : {lot.LotID}");
                return DateTime.MaxValue;
            }

            var lpst = GetLPST(lot.PlanWip.PegWipInfo, operID);

            return lpst;
        }

        public static DateTime GetLPST(SEMPegWipInfo pwi, string operID)
        {
            SEMOperTarget ot = pwi.SemOperTargets.Where(x => x.OperId == operID && x.PlanType == "OPER").FirstOrDefault();
            if (ot == null)
            {
                WriteLog.WriteErrorLog($"LPST를 찾을 수 없습니다. LOT_ID : {pwi.LotID}, OPER_ID : {operID}");
                return DateTime.MaxValue;
            }

            if (ot.Lpst == DateTime.MinValue)
            {
                WriteLog.WriteErrorLog($"LPST값 이상 {pwi.LotID}/{operID}/{ot.Lpst}");
                return DateTime.MaxValue;
            }

            return ot.Lpst;
        }

        public static DateTime GetLPSTForJC(this SEMLot lot, string operID)
        {
            var pwi = lot.PlanWip.PegWipInfo;

            if (lot.PlanWip == null)
            {
                //WriteLog.WriteErrorLog($"PlanWip을 찾을 수 없습니다. LOT_ID : {lot.LotID}");
                return DateTime.MaxValue;
            }

            if (lot.PlanWip.PegWipInfo == null)
            {
                //WriteLog.WriteErrorLog($"PegWipInfo를 찾을 수 없습니다. LOT_ID : {lot.LotID}");
                return DateTime.MaxValue;
            }

            var ts = lot.GetAgentTargetStep(false);
            
            SEMOperTarget ot = pwi.SemOperTargets.Where(x => x.OperId == ts.StepID && x.PlanType == "OPER").FirstOrDefault();            
            if (ot == null)
            {
                WriteLog.WriteErrorLog($"LPST를 찾을 수 없습니다. LOT_ID : {pwi.LotID}, OPER_ID : {operID}");
                return DateTime.MaxValue;
            }

            if (ot.Lpst == DateTime.MinValue)
            {
                WriteLog.WriteErrorLog($"LPST값 이상 {pwi.LotID}/{operID}/{ot.Lpst}");
                return DateTime.MaxValue;
            }

            return ot.Lpst;
        }

        public static double GetUnitQty(this SEMLot lot)
        {
            var samplingOperQty = InputMart.Instance.SAMPLING_OPER_QTY.Rows.Where(x => x.LOT_ID == lot.LotID && (double)x.ORDERNO == lot.Wip.OrderNO && x.OPER_ID == lot.CurrentStepID);
            if (samplingOperQty.IsNullOrEmpty() == false)
            {
                return (double)samplingOperQty.First().INSPECT_SAMPLE_QTY;
            }

            samplingOperQty = InputMart.Instance.SAMPLING_OPER_QTY.Rows.Where(x => x.LOT_ID == "ALL" && x.OPER_ID == lot.CurrentStepID);
            if (samplingOperQty.IsNullOrEmpty() == false)
            {
                return (double)samplingOperQty.First().INSPECT_SAMPLE_QTY;
            }

            return lot.UnitQty;
        }

        public static double GetYield(this SEMGeneralStep step, string productID, string powderCode, string compositionCode, out bool isSpecialYiled)
        {
            double yield = step.Yield;
            isSpecialYiled = false;

            var row = InputMart.Instance.SPECIAL_YIELD.Rows.Where(x => x.ROUTE_ID == step.SEMRouteID && x.OPER_ID == step.StepID && x.PRODUCT_ID == productID && x.POWDER_ID == powderCode && x.COMPOSITION_CODE == compositionCode).FirstOrDefault();
            if (row != null)
            {
                yield = (double)row.YIELD;
                isSpecialYiled = true;
            }

            return yield;
            
        }
    }
}