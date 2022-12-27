using Mozart.RuleFlow;
using Mozart.SeePlan.Pegging;
using Mozart.SeePlan.DataModel;
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
    public partial class Main
    {
        public Step GETLASTPEGGINGSTEP(Mozart.SeePlan.Pegging.PegPart pegPart)
        {
            //[POC] 불필요하게 복잡한코드 추후 삭제
            //SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;
            //SEMProduct product = pp.Product as SEMProduct;
            //Step step = product.Process.LastStep;
            //
            //pp.AddCurrentPlan(product, step as SEMGeneralStep);
            //
            //return step;
            /////////////////////////////////          
            ///

            SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;
            SEMProcess proc = pp.Product.Process as SEMProcess;

            //if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //{
            //    if (pp.Phase == 1)
            //    {
            //        return pp.ProdChangedStep;
            //    }
            //}
            return (pp.MoMaster as SEMGeneralMoMaster).LastStep; //proc.Steps.Last;
        }

        public Step GETPREVPEGGINGSTEP(PegPart pegPart, Step currentStep)
        {
            return null;

            // 이전 컨셉 TEST 코드
            //Step step = currentStep.GetDefaultPrevStep();
            //SEMGeneralPegPart pp = pegPart.ToSemPegPart();
            //if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //{
            //    if (step == null && pp.Phase == 0)
            //    {
            //        double remainQty = pp.PegTargetList.Sum(x => x.Qty);

            //        if (remainQty > 0 && pp.BomDic.Count > 0)
            //        {
            //            pp.CanCopyPegPart = true;
            //            return pp.Product.Process.LastStep;
            //        }
            //    }
            //}
            //return step;
            ////////////////////////////////////

            //[POC] 불필요하게 복잡한코드 추후 삭제(StepPlan Object도)
            //SEMGeneralPegPart pp = pegPart.ToSemPegPart();
            //
            //// Step prevStep = PegHelper.GetPrevStep(pp, currentStep);
            //
            //Step prevStep = currentStep.GetDefaultPrevStep();
            //
            //if (pp.HasBom)
            //{
            //    prevStep = pp.Bom.ChangeStep.GetDefaultPrevStep();
            //}
            //
            //pp.Bom = null;
            //
            //pp.AddCurrentPlan(pp.SemProduct, prevStep as SEMGeneralStep);
            //
            //return prevStep;
            //
            // return currentStep.GetDefaultPrevStep();
            /////////////////
        }

        public string SelectCase_IsOut(PegPart part)
        {
            if (part.CurrentStep == (part.MoMaster as SEMGeneralMoMaster).Product.Process.LastStep)
                return "OUT";

            return null;
        }

        public string SelectCase_IsIn(PegPart part)
        {
            Step prevStep = part.CurrentStep.GetDefaultPrevStep();

            if (prevStep == null)
                return "IN";

            return null;
        }

        public int COMPAREPEGPART(PegPart x, PegPart y)
        {
            #region value
            int cmp = 0;
            SEMGeneralPegPart x_dem = x as SEMGeneralPegPart;
            SEMGeneralPegPart y_dem = y as SEMGeneralPegPart;

            if (object.ReferenceEquals(x, y))
                return -1;
            #endregion

            cmp = x_dem.TargetOperID.CompareTo(y_dem.TargetOperID) * -1;
            if (cmp != 0)
                return cmp;

            cmp = x_dem.Priority.CompareTo(y_dem.Priority);
            if (cmp != 0)
                return cmp;

            cmp = x_dem.DueDate.CompareTo(y_dem.DueDate);
            if (cmp != 0)
                return cmp;

            cmp = x_dem.DemandQty.CompareTo(y_dem.DemandQty) * -1;
            if (cmp != 0)
                return cmp;

            cmp = x_dem.DemandID.CompareTo(y_dem.DemandID);
            if (cmp != 0)
                return cmp;

            return -1;


#if (false) // 이전 로직
            //SEMGeneralPegPart ppX = x as SEMGeneralPegPart;
            //SEMGeneralPegPart ppY = y as SEMGeneralPegPart;

            //if (ppX.TargetOperID == Constants.TAPING_OPER_ID && ppY.TargetOperID == Constants.OI_OPER_ID)
            //    return 1;

            //if (ppY.TargetOperID == Constants.TAPING_OPER_ID && ppX.TargetOperID == Constants.OI_OPER_ID)
            //    return -1;

            //int cmp = ppX.DueDate.CompareTo(ppY.DueDate);

            //return cmp;



            SEMGeneralPegPart ppX = x as SEMGeneralPegPart;
            SEMGeneralPegPart ppY = y as SEMGeneralPegPart;

            int cmp = 0;

            return cmp;
            //PegPart 내 수량 존재 우선
            bool hasQtyX = x.PegTargetList.Sum(p => p.Qty) == 0? false : true;
            bool hasqtyY = y.PegTargetList.Sum(p => p.Qty) == 0 ? false : true;
            cmp = hasQtyX.CompareTo(hasqtyY) * -1;
            if (cmp != 0)
                return cmp;

            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                //Phase = 0 우선
                cmp = ppX.Phase.CompareTo(ppY.Phase);
                if (cmp != 0)
                    return cmp;
            }

            //sequence 높은 것 우선 
            int xSeq = 9999;
            int ySeq = 9999;      
            if (ppX.CurrentStep != null)
                xSeq = (ppX.CurrentStep as SEMGeneralStep).Sequence;
            if (ppY.CurrentStep != null)
                ySeq = (ppY.CurrentStep as SEMGeneralStep).Sequence;
            cmp = xSeq.CompareTo(ySeq) * -1;
            if (cmp != 0)
                return cmp;


            //
            var xmp = x.MoMaster.MoPlanList.FirstOrDefault() as SEMGeneralMoPlan;
            var ymp = y.MoMaster.MoPlanList.FirstOrDefault() as SEMGeneralMoPlan;
            return xmp.DemandID.CompareTo(ymp.DemandID);
#endif
        }

        public int COMPAREPEGTARGET(PegTarget x, PegTarget y)
        {        
            return 0;
        }

        public void ONENDFLOW(Mozart.RuleFlow.IFlow flow)
        {

        }

        public IEnumerable<PegPart> SPLITPEGPART(PegPart pegPart)
        {
            List<PegPart> list = new List<PegPart>();
            list.Add(pegPart);

            //if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //{
            //    SEMGeneralPegPart perentPegPart = (SEMGeneralPegPart)pegPart;

            //    if (perentPegPart.CanCopyPegPart == false)
            //        return list;

            //    List<PegPart> copiedPartList = PegHelper.GetCopiedPegParts(perentPegPart);

            //    list.AddRange(copiedPartList);
            //}

            return list;
        }
    }
}