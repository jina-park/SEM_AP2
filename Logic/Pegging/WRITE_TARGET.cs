using Mozart.SeePlan.DataModel;
using Mozart.SeePlan.Pegging;
using SEM_AREA.Persists;
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
using SEM_AREA.Outputs;
using Mozart.SeePlan;

namespace SEM_AREA.Logic.Pegging
{
    [FeatureBind()]
    public partial class WRITE_TARGET
    {
        public void WRITE_TARGET0(Mozart.SeePlan.Pegging.PegPart pegPart, bool isOut, ref bool handled)
        {
            //SEMGeneralStep step = pegPart.CurrentStep as SEMGeneralStep;
            //SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;

            //if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //{
            //    if (PegHelper.CanPegPhase(pp, isOut))
            //        return;
            //}

            //string inOutType = string.Empty;

            //if (pegPart.CurrentStage.Properties.Keys.Contains("InOutType"))
            //    inOutType = pegPart.CurrentStage.Properties.Get("InOutType").ToString();

            ////string stepType = string.IsNullOrEmpty(inOutType) ? pegPart.CurrentStep.StepID : inOutType;

            //bool isExtraAdd = string.IsNullOrEmpty(inOutType) ? false : true;

            //foreach (SEMGeneralPegTarget pegTarget in pegPart.PegTargetList)
            //{
            //    Outputs.OPER_TARGET st = new Outputs.OPER_TARGET();

            //    double qty = Math.Ceiling(pegTarget.Qty);
            //    st.VERSION = ModelContext.Current.VersionNo;
            //    st.SITE_ID = (pegPart.MoMaster as SEMGeneralMoMaster).SiteID;
            //    st.STEP_ID = string.IsNullOrEmpty(inOutType) ? pegPart.CurrentStep.StepID : inOutType;
            //    st.SEQUENCE = pegTarget.Seq++;
            //    st.PRODUCT_ID = pp.Product.ProductID;

            //    if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //    {
            //        if (pp.Phase == 0)
            //        {
            //            st.IN_QTY = isOut ? 0 : qty;
            //            st.OUT_QTY = isOut ? qty : 0;
            //        }
            //        else if (pp.Phase == 1)
            //        {
            //            st.IN_QTY = isOut ? 0 : Math.Ceiling(pegTarget.MasterPegTarget.Qty);
            //            st.OUT_QTY = isOut ? Math.Ceiling(pegTarget.MasterPegTarget.Qty) : 0;
            //        }
            //    }
            //    else
            //    {
            //        st.IN_QTY = isOut ? 0 : qty;
            //        st.OUT_QTY = isOut ? qty : 0;
            //    }

            //    st.TARGET_DATE = pegTarget.DueDate;
            //    st.MO_DUE_DATE = (pegTarget as SEMGeneralPegTarget).Mo.DueDate;
            //    st.MO_WEEK_NO = (pegTarget as SEMGeneralPegTarget).Mo.WeekNo;
            //    st.MO_PRIORITY = (pegTarget as SEMGeneralPegTarget).Mo.Priority.ToString();
            //    st.MO_PRODUCT_ID = (pegTarget as SEMGeneralPegTarget).Mo.ProductID;
            //    st.TARGET_SHIFT = pegTarget.DueDate.ShiftStartTimeOfDayT();
                
            //    if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            //    {
            //        st.DEMAND_ID = pegTarget.SemMoPlan.DemandID + "_" + pp.Phase.ToString(); //Phase 임시 출력
            //    }
            //    else
            //    {
            //        st.DEMAND_ID = pegTarget.SemMoPlan.DemandID;
            //    }

            //    st.STEP_SEQ = step != null ? step.Sequence : -1;

            //    if (isExtraAdd)
            //    {
            //        st.IN_QTY = qty;
            //        st.OUT_QTY = qty;
            //    }

            //    pegTarget.Qty = Math.Ceiling(pegTarget.Qty);

            //    OutputMart.Instance.OPER_TARGET.Add(st);
            //}
        }

        public object GET_STEP_PLAN_KEY0(PegPart pegPart, ref bool handled, object prevReturnValue)
        {
            //stepPlan에 들어가는 key (forwardPegging시 사용)
            if (pegPart.CurrentStage.State == "CellBankStage"
                || pegPart.CurrentStage.State == "OutStage"
                || pegPart.CurrentStage.State == "InStage")
            {
                return null;
            }

            return (pegPart as SEMGeneralPegPart).Product.ProductID;
            
        }

        public Mozart.SeePlan.DataModel.StepTarget CREATE_STEP_TARGET0(PegTarget pegTarget, object stepPlanKey, Step step, bool isRun, ref bool handled, Mozart.SeePlan.DataModel.StepTarget prevReturnValue)
        {
            //forwardPlan에 사용할 stepTarget생성
            SEMGeneralPegTarget pt = pegTarget as SEMGeneralPegTarget;
            SEMStepTarget st = new SEMStepTarget(stepPlanKey, step, pt.Qty, pt.DueDate, isRun);

            st.Mo = pegTarget.MoPlan as SEMGeneralMoPlan;
            st.Product = (pt.PegPart as SEMGeneralPegPart).Product as SEMProduct;

            return st;
        }
    }
}