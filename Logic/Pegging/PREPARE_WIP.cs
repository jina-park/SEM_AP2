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
    public partial class PREPARE_WIP
    {
        public PegPart PREPARE_WIP0(PegPart pegPart, ref bool handled, PegPart prevReturnValue)
        {
            foreach (SEMWipInfo wip in InputMart.Instance.SEMWipInfo.Values)
            {
                #region Validation
                if (wip.Process == null)
                {
                    // WriteUnpegHistory
                    continue;
                }

                if(wip.InitialStep == null)
                {
                    // WriteUnpegHistory
                    continue;
                }
                #endregion

                // PlanWip 생성
                SEMPlanWip planWip = CreateHelper.CreatePlanWip(wip);

                // Wip의 AvailTime, BOM 경우의 수, 수율 등 계산
                PegWipInfoManager.SetPegWipInfo(planWip);

                // MultiLotProductChange의 경우
                if (wip.IsMultiLotProdChange)
                {
                    try
                    {

                        ICollection<SEMBOM> boms;
                        if (InputMart.Instance.LotProdChangeDic.TryGetValue(wip.LotID, out boms))
                        {
                            foreach (var bom in boms)
                            {
                                // split Pwi
                                var splitPwis = planWip.SEMPegWipInfos.Where(x => x.SplitProductID == bom.ToProductID);

                                if (splitPwis == null)
                                    continue;

                                // split pwi에 맞는 PlanWip 생성
                                SEMPlanWip splitPlanWip = CreateHelper.CreateSplitedPlanWip(wip, bom);

                                // PlanWip - PWI 매핑
                                splitPwis.ForEach(x => x.PlanWip = splitPlanWip);
                                splitPlanWip.SEMPegWipInfos.AddRange(splitPwis);

                                // WipInfo - PlanWip 매핑
                                wip.SplitPlanWips.Add(splitPlanWip);

                                // PlanWipList에 Add
                                InputMart.Instance.PlanWipList.Add(splitPlanWip);
                            }

                            wip.PlanWip = planWip;
                            wip.SplitLotsRemaninQty = wip.UnitQty;

                            // SplitPlanWip은 add 하고 원래 PlanWip은 넣지않음
                            // InputMart.Instance.PlanWipList.Add(planWip); 
                        }
                    }
                    catch (Exception e)
                    {
                        WriteLog.WriteErrorLog($"{e.Message}");

                        InputMart.Instance.PlanWipList.Add(planWip);

                        // WipInfo와 매핑
                        wip.PlanWip = planWip;
                    }
                }
                else
                {
                    // PlanWipList에 Add
                    InputMart.Instance.PlanWipList.Add(planWip);

                    // WipInfo와 매핑
                    wip.PlanWip = planWip;
                }

                foreach (var pwi in planWip.SEMPegWipInfos)
                {
                    bool hasDemand = pwi.HasDemand();
                    if (hasDemand)
                    {
                        foreach (var step in pwi.OperDic)
                        {
                            if (step.Value.IsProcessing)
                            {
                                string dummy = string.Empty;
                                pwi.GetArrange(step.Value.StepID, pwi.GetProduct(step.Value.StepID).ProductID, ref dummy);
                            }
                        }
                    }
                }
                
            }

            return pegPart;
        }
    }
}