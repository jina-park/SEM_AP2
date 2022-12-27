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
using Mozart.SeePlan.General.DataModel;
using Mozart.SeePlan.DataModel;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class OperationManager
    {
        static public int TapingSeq;
        static public int OISeq;
        static public int SortingSeq;


        public static string GetNextOperID(SEMGeneralStep step)
        {
            SEMGeneralStep nextStep = GetNextOper(step);

            if (nextStep == null)
                return string.Empty;

            return nextStep.StepID;
        }

        public static SEMGeneralStep GetNextOper(this Step step)
        {
            return GetNextOper(step as SEMGeneralStep);
        }

        public static SEMGeneralStep GetNextOper(SEMGeneralStep step)
        {
            if (step.IsLotOper) //lotOper의 NextStep
            {               
                return step.NextOper;
            }
            else //일반 Step의 NextStep
            {
                return step.NextStep as SEMGeneralStep;
            }
        }

        public static string GetNextRouteId(this string routeID)
        {
            if (routeID == Constants.SGC117)
                return Constants.SGC118;
            else if (routeID == Constants.SGC118)
                return Constants.SGC119;
            else if (routeID == Constants.SGC119)
                return Constants.SGC120;
            else if (routeID == Constants.SGC120)
                return string.Empty;

            return null;
        }

        public static string GetRouteID(string stepID)
        {
            string routeId;

            if (InputMart.Instance.RouteDic.TryGetValue(stepID, out routeId) == false)
                return string.Empty;

            if (routeId == Constants.SGC117)
                return Constants.SGC117;
            else if (routeId == Constants.SGC118)
                return Constants.SGC118;
            else if (routeId == Constants.SGC119)
                return Constants.SGC119;
            else if (routeId == Constants.SGC120)
                return Constants.SGC120;

            return string.Empty;
        }

        public static void SetLotOperInfo(SEMWipInfo wip)
        {
            SEMLotOper lo = InputMart.Instance.SEMLotOperView.FindRows(wip.LotID).FirstOrDefault();

            if (lo == null)
            {
                wip.IsLotOper = false;
                return;
            }
            
            lo.WipInfo = wip;

            wip.LotOper = lo;
            wip.IsLotOper = true;

            //LotOper의 마지막Step의 NextStep을 셋팅            
            string nextRouteID = lo.SEMRouteID.GetNextRouteId();
            SEMGeneralStep lastStep = lo.Steps.Last();
            lastStep.NextOper = lo.WipInfo.SEMProcess.SEMGeneralSteps.Where(x => x.SEMRouteID == nextRouteID).FirstOrDefault();
            lastStep.IsEndLotOper = true;

            //lotoper의 tat를 셋팅
            foreach(var step in lo.Steps)
            {
                var tat = InputMart.Instance.LEAD_TIME.Rows.Where(x => x.PRODUCT_ID == lo.ProductID && x.OPER_ID == step.StepID).FirstOrDefault();
                if(tat == null)
                {
                    WriteLog.WriteErrorLog($"LotOper의 LeadTime을 찾을 수 없습니다 LOT_ID:{lo.LotID} OPER_ID:{step.StepID}");
                }

                step.TAT = tat.TAT;
            }

        }

        public static WipArea GetWipArea(int seq)
        {
            WipArea res = WipArea.BulkArea;
            if (seq > OISeq)
                res = WipArea.TapingArea;

            return res;
        }

        public static SEMGeneralStep GetNextOperForJC(this SEMLot lot,  SEMGeneralStep step)
        {
            SEMGeneralStep nextStep;

            if (lot.PlanWip == null || lot.PlanWip.PegWipInfo == null)            
                return null;

            nextStep = lot.GetNextOperForJC(step.StepID);

            //if(nextStep == null)
            //    nextStep = step.GetNextOper();            

            return nextStep;

        }

        public static SEMGeneralStep GetNextOperForJC(this SEMLot lot, string stepID)
        {
            SEMGeneralStep nextStep;

            if (lot.PlanWip == null || lot.PlanWip.PegWipInfo == null)
                return null;

            lot.PlanWip.PegWipInfo.NextOperDic.TryGetValue(stepID, out nextStep);

            return nextStep;
        }

        public static SEMGeneralStep GetOper(this SEMLot lot, string stepID)
        {
            SEMGeneralStep step;

            if (lot.PlanWip == null || lot.PlanWip.PegWipInfo == null)
                return null;

            lot.PlanWip.PegWipInfo.OperDic.TryGetValue(stepID, out step);

            return step;
        }

        public static SEMGeneralStep GetNextOper(this SEMLot lot)
        {
            SEMGeneralStep currentStep = lot.CurrentStep as SEMGeneralStep;
           
            //임시 예외처리 
            if (lot.Wip.WipState == null || lot.PlanWip == null)
            {
                return null;
            }

            if(lot.IsPeggedLot())
            {
                //
                // PEGGED LOT 
                //

                // BulkDemand에 Pegging된 Lot은 Bulk Demand oper(SG4090)까지만 진행하고 Next Step없음
                if (lot.IsBulkPeggedLot() && currentStep.StepID == Constants.OI_OPER_ID)
                    return null;

                SEMGeneralStep nextStep = currentStep.GetNextOper();

                return nextStep;
            }
            else
            {
                //
                // UNPEGGED LOT
                // Run 재공이나 ResFixLotArrange Lot의 경우 pegging되지않아도 FW를 해서 여기 조건문에 들어옴.
                // Push Wip만 끝까지 진행하고 나머지는 next step 진행하지 않고 종료
                //

                // PushWip이 아니면 현재 step만 진행하고 끝
                if (lot.Wip.IsPushWip())
                {
                    SEMGeneralStep nextStep = currentStep.GetNextOper();

                    return nextStep;
                }


                return null;
            }
        }

        public static bool IsBulkPeggedLot(this SEMLot lot)
        {
            SEMPegWipInfo pwi = lot.Wip.PlanWip.PegWipInfo;
            if (pwi.TapingPeggingPart == null || pwi.TapingPeggingPart.Count == 0)
                return true;

            return false;
        }
    }
}