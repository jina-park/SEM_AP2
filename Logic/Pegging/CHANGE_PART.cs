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
    public partial class CHANGE_PART
    {
        public List<object> GET_PART_CHANGE_INFOS0(PegPart pegPart, bool isRun, ref bool handled, List<object> prevReturnValue)
        {
            return new List<object>();
            //SEMGeneralPegPart pp = (SEMGeneralPegPart)pegPart;

            //string currentProdID = pp.Product.ProductID;
            //string currentStepID = (pp.CurrentStage.Tag as SEMGeneralStep).StepID;

            //var list = InputMart.Instance.SEMBOMView.FindRows(currentProdID, currentStepID).ToList<object>();

            //return list;
        }


        public PegPart APPLY_PART_CHANGE_INFO0(PegPart pegPart, object partChangeInfo, bool isRun, ref bool handled, PegPart prevReturnValue)
        {
            //PART CHANGE 사용 보류
            return pegPart;

            //주석 박진아 : BOM로직
            //SEMBom bom = partChangeInfo as SEMBom;

            //SEMGeneralPegPart pp = pegPart.ToSemPegPart();

            // pp.AddCurrentPlan(bom.Product, bom.CurrentStep);

            //CreateHelper.AddCurrentPlan(pp, bom.Product, bom.CurrentStep);

            //pp.Product = bom.ChangeProduct;
            //pp.Bom = bom;
            ////////////////////////////////////////////


            //SEMGeneralPegPart pp = (SEMGeneralPegPart)pegPart;

            //if (true) // 우선은 항상 바뀌는 것으로 구현, 추후에 조건에 따라 prod change가 일어나는 것으로 수정 해야함
            //{
            //    SEMGeneralPegPart p = pegPart as SEMGeneralPegPart;
            //    //SEMGeneralPegPart ppp = p.Clone() as SEMGeneralPegPart;
            //    //ppp.Week = "TEST";
            //    //InputMart.Instance.SEMMergedPegPart.Merge(ppp);

            //    //SEMBOM bom = (SEMBOM)partChangeInfo;

            //    //SEMProduct changeProd = InputMart.Instance.SEMProduct[bom.FromProductID];
            //    //SEMProcess changeProc = InputMart.Instance.SEMProcess.Rows.Where(x => x.ProcessID == bom.FromProductID).FirstOrDefault();


            //    //SEMGeneralStep changeStep = (SEMGeneralStep)changeProc.FindStep(bom.ToStepID);
            //    ////PegStage changeStage = pegPart.CurrentStage.Model.GetStage(changeStep);
            //    ////var model = pegPart.CurrentStage.Model.GetStage(changeStep);

            //    //ppp.Product = changeProd;
            //    //(ppp.PegTargetList.First() as SEMGeneralPegTarget).SemMoPlan.DemandID += "-";

            //    //ppp.CurrentStage = changeStage;

            //    return p;
            //}

        }

    }
}