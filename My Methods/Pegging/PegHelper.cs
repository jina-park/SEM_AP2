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
using Mozart.SeePlan.DataModel;
using Mozart.SeePlan.Pegging;
using Mozart.SeePlan;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class PegHelper
    {
        internal static Step GetPrevStep(PegPart pegPart, Step currentStep)
        {
            Step prevStep = currentStep.GetDefaultPrevStep();

            //SEMGeneralPegPart pp = pegPart.ToSemPegPart();
            //if (pp.HasBom)
            //{
            //    prevStep = pp.Bom.ChangeStep.GetDefaultPrevStep();
            //}

            return prevStep;
        }


        public static List<PegPart> GetCopiedPegParts(SEMGeneralPegPart perentPart)
        {
            if (true)//InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                List<PegPart> list = new List<PegPart>();

                string currentProdID = perentPart.Product.ProductID;
                string currentStepID = perentPart.CurrentStep.StepID;

                List<SEMBOM> bomList = InputMart.Instance.SEMBOMView.FindRows(currentProdID).ToList();

                foreach (SEMBOM bom in bomList)
                {
                    perentPart.IsMasterPegPart = true;
                    perentPart.Phase = 9999;
                    perentPart.ProdChangedStep = bom.FromStep;//이코드는 의도대로 동작하지 않는듯...

                    SEMProduct changeProd = InputMart.Instance.SEMProduct[bom.FromProductID];
                    SEMProcess changeProc = changeProd.SEMProcess;
                    SEMGeneralStep changeStep = (SEMGeneralStep)changeProc.FindStep(bom.ToStepID);

                    SEMGeneralPegPart childPP = perentPart.Clone() as SEMGeneralPegPart;
                    childPP.Product = changeProd;
                    childPP.IsProdChanged = true;
                    childPP.Phase = 1;
                    childPP.MasterPegPart = perentPart;

                    InputMart.Instance.SEMGeneralPegPart.ImportRow(childPP);

                    foreach (var target in childPP.PegTargetList)
                    {
                        SEMGeneralPegTarget childTarget = (SEMGeneralPegTarget)target;
                        SEMGeneralPegTarget masterTarget = ((perentPart.PegTargetList).Where(x => x.DueDate == childTarget.DueDate).FirstOrDefault() as SEMGeneralPegTarget);

                        masterTarget.IsMasterPegTarget = true;
                        childTarget.IsMasterPegTarget = false;

                        masterTarget.MasterPegTarget = masterTarget;
                        childTarget.MasterPegTarget = masterTarget;

                        childTarget.IsProdChanged = true;
                    }

                    list.Add(childPP);

                    perentPart.ChildPegParts.Add(childPP);
                    perentPart.CanCopyPegPart = false;
                }

                return list;
            }
        }

        public static bool CanPegPhase(SEMGeneralPegPart pp, bool isRun)
        {
            if (true)//InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                SEMGeneralStep step = pp.CurrentStep as SEMGeneralStep;

                bool cond1 = pp.Phase == 9999;
                bool cond2 = pp.Phase == 1 && step.Sequence > pp.ProdChangedStep.Sequence;
                bool cond3 = pp.Phase == 1 && step.Sequence == pp.ProdChangedStep.Sequence && isRun;

                return cond1 || cond2 || cond3;
            }
        }
        
        //public static void SetPegWipInfo(SEMPlanWip planWip)
        //{
        //    #region values
        //    List<SEMPegWipInfo> pwiList = new List<SEMPegWipInfo>();
        //    planWip.SEMPegWipInfos = pwiList;
        //    string targetStepID = PegWipInfoManager.GetTargetStepID(planWip); //Demand Step
        //    MultiDictionary<string, string> changedHistory = new MultiDictionary<string, string>();
        //    #endregion

        //    #region wip의 Avail Time 구하기
        //    SEMPegWipInfo orgPwi = CreateHelper.CreatePegWipInfo(planWip, targetStepID);

        //    AddPwiList(orgPwi, true, pwiList);

        //    if (targetStepID == orgPwi.FlowStepID) 
        //    {//initial Step이 TargetStep인 wip은 object를 추가로 만듦
        //        string nextTargetStepID = PegWipInfoManager.GetNextTargetStepID(targetStepID);
        //        if (nextTargetStepID != string.Empty)
        //        {
        //            SEMPegWipInfo orgPwi2 = CreateHelper.CreatePegWipInfo(planWip, nextTargetStepID);
        //            orgPwi2.IsTargetOper = true;

        //            AddPwiList(orgPwi2, true, pwiList);
        //        }
        //        else
        //        {
        //            WriteLog.WriteWipAvailDateLog(orgPwi, 0, 0);
        //        }
        //    }

        //    for (int i = 0; i < pwiList.Count(); i++)
        //    {
        //        #region 루프 초기화
        //        SEMPegWipInfo flowPwi = pwiList[i];
        //        flowPwi.SplitSeq = i;
        //        targetStepID = flowPwi.TargetOperID;

        //        int j = 0;
        //        #endregion

        //        while (flowPwi.FlowStepID != targetStepID)
        //        {

        //            #region 무한루프 방지
        //            if (j == 1000)
        //                break;
        //            #endregion

        //            WriteLog.WriteWipAvailDateLog(flowPwi, i, j);

        //            #region bom적용
        //            // 기종명 변경
        //            List<SEMBOM> nameBomList = BomHelper.GetFromToBom(flowPwi, true);
        //            if (nameBomList != null && nameBomList.Count() > 0)
        //            {
        //                flowPwi.SetProdNameChangeInfo(nameBomList.FirstOrDefault());
        //            }

        //            // Product 변경
        //            if (PegWipInfoManager.CanChangeProduct(flowPwi))
        //            {
        //                List<SEMBOM> bomList = BomHelper.GetFromToBom(flowPwi, false);

        //                foreach (SEMBOM bom in bomList)
        //                {
        //                    #region WIP 내 BOM 중복 여부 확인
        //                    string value = CommonHelper.CreateKey(bom.ToProductID, bom.ToCustomerID);

        //                    ICollection<string> tmp;
        //                    changedHistory.TryGetValue(bom.ChangeType, out tmp);

        //                    if (tmp != null && tmp.Contains(value))
        //                        continue;

        //                    changedHistory.Add(bom.ChangeType, value);
        //                    #endregion

        //                    SEMPegWipInfo newPwi = PegWipInfoManager.ClonePegWipInfo(flowPwi);     
        //                    newPwi.SetProdChangeInfo(flowPwi, bom);

        //                    AddPwiList(newPwi, true, pwiList);
        //                }
        //            }
        //            #endregion

        //            #region nextOper, tat++
        //            SEMGeneralStep nextOper = OperationManager.GetNextOper(flowPwi.FlowStep);
        //            //string nextOperID = OperationManager.GetNextOperID(flowPwi);
        //            if (nextOper == null)
        //            {
        //                InputMart.Instance.SEMPegWipInfo.Rows.Remove(flowPwi);
        //                break;  //[TODO] err log (next oper가 없음)
        //            }

        //            double tat = flowPwi.CalcTat;
        //            flowPwi.FlowStep = nextOper;
        //            flowPwi.FlowStepID = nextOper.StepID;
        //            flowPwi.FlowRouteId = OperationManager.GetRouteID(nextOper.StepID);
        //            flowPwi.TotalTat += tat;
        //            flowPwi.CalcAvailDate = flowPwi.CalcAvailDate.AddMinutes(tat);
        //            flowPwi.CalcTat = TimeHelper.GetTat(flowPwi.FlowProductID, flowPwi.FlowStepID);

        //            if (nextOper.StepID == targetStepID)
        //            {
        //                string nextTargetStepID = PegWipInfoManager.GetNextTargetStepID(targetStepID);

        //                if (nextTargetStepID == string.Empty)
        //                { // 마지막 Target
        //                    flowPwi.CalcTat = 0;
        //                    flowPwi.IsTargetOper = true;

        //                    WriteLog.WriteWipAvailDateLog(flowPwi, i, j + 1);

        //                    break;
        //                }
        //                else
        //                { // 다음 Target 존재
        //                    SEMPegWipInfo prevPwi = PegWipInfoManager.ClonePegWipInfo(flowPwi);
        //                    AddPwiList(prevPwi, false, pwiList);

        //                    flowPwi.TargetOperID = nextTargetStepID;
        //                    flowPwi.IsTargetOper = true;

        //                    targetStepID = nextTargetStepID;
        //                }
        //            }

        //            #endregion

        //            j++;
        //        }
        //    }
        //    #endregion
        //}

        public static void AddPwiList(SEMPegWipInfo pwi, bool addForeachList, List<SEMPegWipInfo> pwiList)
        {
            InputMart.Instance.SEMPegWipInfo.ImportRow(pwi);
            if (addForeachList)
                pwiList.Add(pwi);
        }


    }
}