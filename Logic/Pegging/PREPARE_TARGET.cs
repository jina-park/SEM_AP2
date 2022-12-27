using SEM_AREA.Outputs;
using SEM_AREA.Inputs;
using Mozart.SeePlan.Pegging;
using SEM_AREA.Persists;
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
    public partial class PREPARE_TARGET
    {
        public PegPart PREPARE_TARGET0(PegPart pegPart, ref bool handled, PegPart prevReturnValue)
        {
            //SEMGeneralPegPart pPart;
            //SEMGeneralPegTarget pTarget;
            //MergedPegPart mpp = pegPart as MergedPegPart;

            //foreach (var moMaster in InputMart.Instance.SEMGeneralMoMaster.Values)
            //{
            //    pPart = new SEMGeneralPegPart(moMaster, moMaster.Product);

            //    foreach (var moPlan in moMaster.MoPlanList)
            //    {
            //        pTarget = new SEMGeneralPegTarget(pPart, moPlan as SEMGeneralMoPlan);
            //        pPart.AddPegTarget(pTarget);
            //    }
            //    mpp.Merge(pPart);
            //}

            //return pegPart;

            //초기화
            // PegMaster.InitPegMaster();

            //최초 MergedPegPart 생성.
            MergedPegPart mpp = pegPart as MergedPegPart;

            switch (pegPart.CurrentStage.StageID)
            {
                case ("Int"):
                    PrePareTarget(mpp);
                    break;

                default:
                    break;
            }

            InputMart.Instance.SEMMergedPegPart = mpp;
            return mpp;
        }

        private void PrePareTarget(MergedPegPart mpp)
        {
            mpp.Items.Clear();

            foreach (var mm in InputMart.Instance.SEMGeneralMoMaster.Values)
            {
                SEMGeneralPegPart semPp = new SEMGeneralPegPart(mm, mm.Product);
                semPp.Week = mm.Week;
                semPp.DemandID = mm.DemandID;
                semPp.Phase = 0;
                semPp.SiteID = mm.SiteID;
                semPp.TargetOperID = mm.TargetOperID;
                semPp.CustomerID = mm.Customer;
                semPp.EndCustomerID = mm.EndCustomerID;
                semPp.DemandCustomerID = mm.DemandCustomerID;
                semPp.DueDate = mm.MoPlanList[0].DueDate;
                semPp.TargetQty = mm.TargetQty;
                semPp.DemandQty = mm.TargetQty;
                semPp.Priority = mm.Priority;

                if (InputMart.Instance.GlobalParameters.ApplyBom == true)
                    BomHelper.SetBomInfo(semPp);

                foreach (SEMGeneralMoPlan mo in mm.MoPlanList)
                {
                    SEMGeneralPegTarget pt = CreateHelper.CreateSemPegTarget(semPp, mo);

                    pt.Week = mo.WeekNo;

                    semPp.AddPegTarget(pt);

                    //if (pp.SampleMs == null)
                    //    pp.SampleMs = pt;
                }

                mpp.Merge(semPp);

                InputMart.Instance.SEMGeneralPegPart.ImportRow(semPp);

                PrePeg.MakeDemandGroup(semPp);

            }
        }
    }
}