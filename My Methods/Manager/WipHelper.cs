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
    public static partial class WipHelper
    {
        public static List<string> GetEndCustomerList(string lotID)
        {
            var list = InputMart.Instance.STOCK_CUSTOMER_CONDITION.Rows.Where(x => x.LOT_ID == lotID);
            if (list.Count() == 0)
                return new List<string>();

            return list.Select(y => y.CUSTOMER_ID).ToList();
        }

        public static string GetToTapingMatCond(SEMPegWipInfo pegWip, SEMProduct prod)
        {
            string fromCond = pegWip.FromTapingMatCond;
            var matAltList = InputMart.Instance.TAPING_MATERIAL_ALT.Rows.Where(x => x.CHIP_SIZE == prod.ChipSize && x.TAPING_TYPE == prod.TapingType && x.THICKNESS == prod.Thickness && x.CARRIER_TYPE == prod.CarrierType);

            if (matAltList.Count() == 0)
                return string.Empty;

            foreach (var alt in matAltList)
            {
                if (alt.FROM_ITEM == fromCond || alt.TO_ITEM == fromCond)
                {
                    pegWip.FP_I_TAPING_MATERIAL_ALT = alt;
                    return alt.TO_ITEM;
                }
            }

            return string.Empty;
        }

        public static TAPING_MATERIAL_ALT GetTAPING_MATERIAL_ALT(this SEMLot lot)
        {
            if (lot.PlanWip == null || lot.PlanWip.PegWipInfo == null || lot.PlanWip.PegWipInfo.FP_I_TAPING_MATERIAL_ALT == null)
            {
                string fromCond = lot.Wip.FromTapingMatCond;

                SEMProduct prod = lot.Product as SEMProduct;

                TAPING_MATERIAL_ALT matAltList = GetTAPING_MATERIAL_ALT(lot.SEMProduct, fromCond);

                //캐싱
                if (lot.PlanWip != null && lot.PlanWip.PegWipInfo != null)
                    lot.PlanWip.PegWipInfo.FP_I_TAPING_MATERIAL_ALT = matAltList;

                return matAltList;
            }
            else
            {
                return lot.PlanWip.PegWipInfo.FP_I_TAPING_MATERIAL_ALT;
            }
        }

        public static string GetTapeThickness(this SEMLot lot)
        {
            var tma = lot.GetTAPING_MATERIAL_ALT();

            if (tma == null)
                return string.Empty;

            return tma.TAPE_THICKNESS.ToString();
        }

        public static TAPING_MATERIAL_ALT GetTAPING_MATERIAL_ALT(this SEMPegWipInfo pwi, SEMProduct prod)
        {
            if (pwi.FP_I_TAPING_MATERIAL_ALT != null)
                return pwi.FP_I_TAPING_MATERIAL_ALT;

            string fromCond = pwi.WipInfo.FromTapingMatCond;

            TAPING_MATERIAL_ALT tapingMatAlt = GetTAPING_MATERIAL_ALT(prod, fromCond);

            //캐싱
            pwi.FP_I_TAPING_MATERIAL_ALT = tapingMatAlt;

            return tapingMatAlt;

        }

        public static TAPING_MATERIAL_ALT GetTAPING_MATERIAL_ALT(SEMProduct prod, string fromCond)
        {
            TAPING_MATERIAL_ALT tapingMatAlt = InputMart.Instance.TAPING_MATERIAL_ALT.Rows.Where(x => x.FROM_ITEM == fromCond && x.CHIP_SIZE == prod.ChipSize && x.TAPING_TYPE == prod.TapingType && x.THICKNESS == prod.Thickness && x.CARRIER_TYPE == prod.CarrierType).FirstOrDefault();
            return tapingMatAlt;
        }

        public static bool IsPushWip(this SEMWipInfo wip)
        {
            // Push Wip : SG4430 Run, SG4430 이후에 있는 Wip

            // SG4430 이후 Lot
            if (wip.InitialSEMStep.StdStep.StepSeq > InputMart.Instance.SG4430Sequence)
                return true;

            // SG4430 Running Lot
            if (wip.InitialSEMStep.StdStep.StepSeq == InputMart.Instance.SG4430Sequence && wip.WipState.ToUpper() == "RUN")
                return true;

            return false;
        }

        public static List<SEMLot> ToSEMLotList(this IList<IHandlingBatch> prevReturnValue)
        {
            List<SEMLot> resulst = new List<SEMLot>();
            foreach (var hb in prevReturnValue)
            {
                resulst.Add(hb.Sample as SEMLot);
            }

            return resulst;
        }

        public static bool IsEnterWarehouse(this SEMLot lot)
        {
            //
            //현재 lot이 bulk에서 sm9999에 들어온 lot인지 판단
            //

            // 현재 step이 9999 가 아닌 lot
            if (lot.CurrentStepID != Constants.SM9999)
                return false;

            // 이미 9999에 있는 lot
            if (lot.Wip.InitialStep.StepID == Constants.SM9999)
                return false;

            return true;
        }

        public static bool IsPeggedLot(this SEMLot lot)
        {
            if (lot.PlanWip == null)
                return false;

            if (lot.PlanWip.IsPeggedPlanWip())
                return true;
            else
                return false;
        }

        public static bool IsPeggedPlanWip(this SEMPlanWip planWip)
        {
            if (planWip.WipInfo == null)
                return false;

            if (planWip.WipInfo.IsPeggedWip())
                return true;
            else
                return false;
        }

        public static bool IsPeggedWip(this SEMWipInfo wip)
        {
            if (wip.PeggedTargets.IsNullOrEmpty())
                return false;
            else
                return true;
        }

        public static SEMEqp GetRunEqp(this SEMWipInfo wip)
        {
            if (wip.WipEqpID.IsNullOrEmpty())
                return null;

            SEMEqp eqp;

            if (InputMart.Instance.SEMEqp.TryGetValue(wip.WipEqpID, out eqp) == false)
                WriteLog.WriteErrorLog($"Run 재공({wip.LotID})의 EQP({wip.WipEqpID})를 찾을 수 없습니다.");
            

            return eqp;
        }

        public static SEMAoEquipment GetRunAoEqp(this SEMWipInfo wip)
        {
            if (wip.WipEqpID.IsNullOrEmpty())
                return null;

            SEMAoEquipment aeqp = AoFactory.Current.GetEquipment(wip.WipEqpID) as SEMAoEquipment;

            if(aeqp == null)
                WriteLog.WriteErrorLog($"Run 재공({wip.LotID})의 EQP({wip.WipEqpID})를 찾을 수 없습니다.");

            return aeqp;
        }

        public static SEMLot GetLot(this SEMWipInfo wip)
        {
            SEMLot lot;

            if (InputMart.Instance.SEMLot.TryGetValue(wip.LotID, out lot) == false)
                WriteLog.WriteErrorLog($"Wip의 Lot을 찾을 수 없습니다.({lot.LotID})");

            return lot;
        }


        public static List<SEMLot> GetCandidateWips(this List<SEMLot> lots, SEMAoEquipment aeqp)
        {
            if (GlobalParameters.Instance.UseJobChangeAgent == false)
                return lots;

            List<SEMLot> candidateLots = null;

            var eqp = aeqp.Target as SEMEqp;

            // Res Fix
            if (eqp.ResFixedWips.Count > 0)
            {
                candidateLots = lots.GetResFixLots(eqp);
            }

            if (candidateLots.IsNullOrEmpty() == false)
                return candidateLots;

            // 후보 lot을 모두 가져옴
            lots = AgentHelper.GetSameJobConditionWips(aeqp, lots);

            // SameLot Processing
            if (candidateLots.IsNullOrEmpty() && aeqp.IsSameLotHold)
            {
                candidateLots = lots.GetSameLots(aeqp);
            }


            if (candidateLots.IsNullOrEmpty())
                candidateLots = lots;

            return candidateLots;
        }

        public static List<SEMLot> GetResFixLots(this List<SEMLot> lots, SEMEqp eqp)
        {
            var result = lots.Where(x => x.Wip.IsResFixLotArrange && x.Wip.LotArrangeOperID == x.CurrentStepID && x.Wip.ResFixLotArrangeEqp.EqpID == eqp.EqpID).ToList();

            return result;
        }

        public static bool IsValidSameLot(this List<SEMWipInfo> wipList)
        {
            if (wipList.Count() == 1)
            {
                //foreach (var wip in wipList)
                //WriteLog.WriteSameLotLog("CreateWip", wip, wipList, false, "");

                return false;
            }

            if (wipList.Select(x => x.Product.ProductID).Distinct().Count() > 1)
            {
                foreach (var wip in wipList)
                    WriteLog.WriteSameLotLog("CreateWip", wip, wipList, false, "DifferentProduct");

                return false;
            }

            if (wipList.Where(x => x.IsPushWip() == false).Count() == 0)
            {
                foreach (var wip in wipList)
                    WriteLog.WriteSameLotLog("CreateWip", wip, wipList, false, "AllWipsDone");

                return false;
            }

            return true;
        }

        public static bool HasSameLot(this SEMWipInfo wip)
        {
            if (wip.SameLots.IsNullOrEmpty())
                return false;

            return true;
        }

        public static List<SEMLot> GetSameLots(this SEMWipInfo wip)
        {
            var sameLots = new List<SEMLot>();

            foreach (var w in wip.SameLots)
            {
                if (InputMart.Instance.SEMLot.TryGetValue(w.LotID, out var l))
                    sameLots.Add(l);
            }
            return sameLots;

        }

        public static bool HasSameLot(this SEMLot lot, bool isWipInit = false)
        {
            if (lot.SameLots.IsNullOrEmpty())
                return false;

            return true;
        }

        public static List<SEMLot> GetSameLots(this List<SEMLot> lots, SEMAoEquipment aeqp)
        {
            List<SEMLot> result = new List<SEMLot>();
            foreach (var l in aeqp.ProcessingSameLots)
            {
                if (lots.Contains(l))
                {
                    result.Add(l);
                }
            }
            return result;
        }

        public static bool IsSameLotFilter(this SEMLot lot, SEMAoEquipment aeqp)
        {
            if (GlobalParameters.Instance.ApplyContinualProcessingSameLot == false)
                return false;

            // res fix lot으로 same lot 로직 적용대상 아님
            if (lot.Wip.IsResFixLotArrange && lot.Wip.LotArrangeOperID == lot.CurrentStepID)
                return false;

            // same lot 로직 적용 대상이 아님
            if (lot.HasSameLot() == false)
                return false;

            string eqpOperID = (aeqp.Target as SEMEqp).OperIDs.FirstOrDefault();

            // 그룹내 res fix lot이 있으면 해당 장비로만 감
            var hasFixLot = lot.SameLots.Any(x => x.Wip.IsResFixLotArrange && lot.Wip.LotArrangeOperID == eqpOperID);
            if (hasFixLot)
            {
                var resFixLot = lot.SameLots.Where(x => x.Wip.IsResFixLotArrange).FirstOrDefault();
                if (lot.SameLotEqp.IsNullOrEmpty() && resFixLot.Wip.ResFixLotArrangeEqp.EqpID != aeqp.EqpID)
                    return true;
            }

            // 아직 가야할 장비가 정해지지 않음
            if (lot.SameLotEqp.IsNullOrEmpty())
                return false;

            // 가야할 장비가 맞음
            if (lot.SameLotEqp.Contains(aeqp))
                return false;

            return true;
        }

        public static bool IsResFixLotFilter(this SEMLot lot, SEMAoEquipment aeqp)
        {
            if (lot.Wip.IsResFixLotArrange && lot.CurrentStepID == lot.Wip.LotArrangeOperID)
            {
                if (lot.Wip.ResFixLotArrangeEqp.EqpID == aeqp.EqpID)
                    return false;

                return true;
            }
            return false;
        }

        public static string GetJobConditionKeyForSameLot(this SEMLot lot)
        {
            if (lot.CurrentWorkGroup != null)
                return lot.CurrentWorkGroup.JobConditionKey;

            var ts = lot.GetAgentTargetStep(false);

            if (ts == null)
                return string.Empty;

            var condKey = lot.GetJobConditionGroupKey(ts);
            return condKey;
        }

        public static bool SetEqpSameLotProcessingMode(this SEMAoEquipment aeqp, SEMLot lot, bool isWipInit = false)
        {
            var sameLots = lot.GetSameLot(aeqp, isWipInit);

            if (sameLots.Count() == 1)
            {
                return false;
            }

            aeqp.ProcessingSameLots = sameLots;
            aeqp.IsSameLotHold = true;
            aeqp.SameLotName = lot.Wip.LotName;

            aeqp.ProcessingSameLots.ForEach(x => x.SameLotEqp.Add(aeqp));
            aeqp.ProcessingSameLots.ForEach(x => x.IsSameLotProcessing = true);

            return true;
        }

        public static List<SEMLot> GetSameLot(this SEMLot lot, SEMAoEquipment aeqp, bool isWipInit = false)
        {
            var result = new List<SEMLot>();
            if (isWipInit)
            {
                result = lot.SameLots;
            }
            else
            {
                result = lot.SameLots.Where(x => x.CurrentSEMStep.StdStep.StepSeq <= InputMart.Instance.SG4430Sequence).ToList();
            }

            var lotList = new List<SEMLot>();
            lotList.AddRange(result);

            // 다른 장비에 fix된 lot 삭제
            foreach (var l in lotList)
            {
                if (l.IsResFixLot(aeqp))
                {
                    if (l.Wip.ResFixLotArrangeEqp.EqpID != aeqp.EqpID)
                    {
                        if (l.Wip.ResFixLotArrangeEqp.RunWip != null && l.Wip.ResFixLotArrangeEqp.RunWip.LotName != l.Wip.LotName)
                            result.Remove(l);
                    }
                }
            }

            return result;

        }

        public static bool IsResFixLot(this SEMLot lot, SEMAoEquipment aeqp)
        {
            var eqpOperIDs = (aeqp.Target as SEMEqp).OperIDs;

            if (lot.Wip.IsResFixLotArrange && eqpOperIDs.Contains(lot.Wip.LotArrangeOperID))
                return true;

            return false;
        }


        public static void RemoveSameLot(this SEMAoEquipment aeqp, SEMLot lot)
        {
            foreach (var eqp in lot.SameLotEqp)
            {
                eqp.ProcessingSameLots.Remove(lot);

                if (eqp.ProcessingSameLots.IsNullOrEmpty())
                    aeqp.IsSameLotHold = false;
            }
        }



    }
}