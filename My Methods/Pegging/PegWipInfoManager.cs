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
    public static partial class PegWipInfoManager
    {
        public static void SetPegWipInfo(SEMPlanWip planWip)
        {
            #region values
            List<SEMPegWipInfo> pwiList = new List<SEMPegWipInfo>();
            string targetOperId = PegWipInfoManager.GetTargetStepID(planWip); // targetOperId = Demand Step
            int splitSeq = 0;
            bool isWaitWip = planWip.Wip.CurrentState == EntityState.WAIT ? true : false;  // Run 재공이 initalStep에서 wait 로직을 타지 않게 함
            List<SEMBOM> bomList = new List<SEMBOM>();
            #endregion

            //CreateSemPegWipInfo(planWip, targetOperId, pwiList);

            SEMPegWipInfo orgPwi = CreateHelper.CreatePegWipInfo(planWip, targetOperId);

            PegHelper.AddPwiList(orgPwi, true, pwiList);

            // pwi 생성 직후 Log 
            // string nextOperID = OperationManager.GetNextOperID(orgPwi.FlowStep);

            // PegWipInfo 전개 : Wip이 BOM 탈 수 있는 모든 경우의 수 및 AvailTime 구하기
            for (int i = 0; i < pwiList.Count(); i++)
            {
                SEMPegWipInfo flowPwi = pwiList[i];

                if (flowPwi.IsSkipLogic)
                    continue;

                targetOperId = flowPwi.TargetOperID;

                int j = 1;    // 무한루프 방지             
                while (j < 10000)
                {
                    if (isWaitWip)
                    {
                        // AvailableTime 저장     
                        if (flowPwi.AvailableTimeDic.ContainsKey(flowPwi.FlowStepID) == false)
                            flowPwi.AvailableTimeDic.Add(flowPwi.FlowStepID, flowPwi.CalcAvailDate);

                        // MC 기종 변경 / 1:N 변경 / SG4170,4190,4210,4220,4230 / LotProductChange 포함
                        bomList.Clear();
                        if (BomHelper.CanMCProdChange(flowPwi, ref bomList))
                        {
                            // MC는 한 wip에 한 번의 change만 허용하기때문에 ProdFix함
                            flowPwi.IsProdFixed = true;

                            if (bomList.First().IsMultiLotProdChange)
                            {
                                // MultiLotProductChange
                                foreach (SEMBOM bom in bomList)
                                {
                                    // Clone PWI
                                    SEMPegWipInfo newPwi = ClonePegWipInfo(flowPwi);
                                    PegHelper.AddPwiList(newPwi, true, pwiList);
                                    splitSeq++;
                                    newPwi.BranchSeq = splitSeq;

                                    newPwi.SetProductChangeInfo(flowPwi, bom);

                                    newPwi.SplitLotID = CreateHelper.GetSerialLotID(flowPwi.LotID, "L");
                                    newPwi.SplitProductID = bom.ToProductID;
                                    newPwi.IsMultiLotProdChange = true;
                                    newPwi.FlowQty = bom.Qty;
                                    newPwi.WipInfo.LotSplitDic.Add(newPwi.FlowProductID, newPwi.FlowQty);
                                    newPwi.MCBom = bom;

                                    PrePegger.SetSemOperTarget(newPwi, "MC");
                                }

                                // 로그 작성
                                WriteLog.WriteWipAvailDateLog(flowPwi, "MultiLotProdChange", splitSeq, j);

                                // LotProdChange는 자기자신을 유지하지않음
                                PegWipInfoManager.RemovePwi(flowPwi);
                                planWip.SEMPegWipInfos.Remove(flowPwi);
                                flowPwi.IsUsable = false;

                                break;
                            }
                            else if (bomList.First().IsLotProdChange == true && bomList.First().IsMultiLotProdChange == false)
                            {
                                // SingleLotProdChange
                                flowPwi.ProductChange(bomList.First());

                                PrePegger.SetSemOperTarget(flowPwi, "MC");
                            }
                            else
                            {
                                // BOM

                                foreach (var bom in bomList)
                                {
                                    // Clone PWI
                                    SEMPegWipInfo newPwi = ClonePegWipInfo(flowPwi);
                                    PegHelper.AddPwiList(newPwi, true, pwiList);
                                    splitSeq++;
                                    newPwi.BranchSeq = splitSeq;
                                    newPwi.SetProductChangeInfo(flowPwi, bom);

                                    PrePegger.SetSemOperTarget(newPwi, "MC");
                                }
                            }
                        }


                        // WR 기종명 변경 / 1:1 변경 / SM9999
                        bomList.Clear();
                        if (BomHelper.CanWRProdChange(flowPwi, ref bomList))
                        {
                            flowPwi.ProductChange(bomList.First());

                            PrePegger.SetSemOperTarget(flowPwi, "WR");
                        }


                        // BT 기종명 변경 / 1:N 변경 / SG6094
                        bomList.Clear();
                        if (BomHelper.CanBTProdChange(flowPwi, ref bomList))
                        {
                            bool isFirstBom = true;

                            SEMPegWipInfo tempPwi = ClonePegWipInfo(flowPwi);

                            foreach (SEMBOM bom in bomList)
                            {
                                // BT는 자기자신을 유지하지않음
                                // 첫 BOM은 name change를 하고 그다음 bom 부터 pwi 복제

                                //Small Lot Qty
                                //if(flowPwi.WipInfo.IsSmallLot)
                                //{
                                //    flowPwi.CalcLeadTime(GlobalParameters.Instance.SmallLotStockLeadTime * 1440);
                                //}

                                if (isFirstBom)
                                {
                                    flowPwi.ProductChange(bom);

                                    isFirstBom = false;

                                    PrePegger.SetSemOperTarget(flowPwi, "BT");
                                }
                                else
                                {
                                    // Clone PWI
                                    SEMPegWipInfo newPwi = ClonePegWipInfo(tempPwi);
                                    PegHelper.AddPwiList(newPwi, true, pwiList);
                                    splitSeq++;
                                    newPwi.BranchSeq = splitSeq;
                                    newPwi.SetProductChangeInfo(flowPwi, bom);

                                    PrePegger.SetSemOperTarget(newPwi, "BT");
                                }
                            }
                        }

                    }

                    // wip이 지나간 Processing Operation를 기록, pegging시 arrange확인용도
                    if (flowPwi.ProcessingOpers.ContainsKey(flowPwi.FlowStepID) == false && flowPwi.FlowStep.IsProcessing)
                    {
                        flowPwi.ProcessingOpers.Add(flowPwi.FlowStepID, flowPwi.FlowProduct);
                    }


                    // 여기까지 Wait 상태
                    ////////////////////////////////
                    // 여기부터 Run 상태


                    // LeadTime(TAT) 계산
                    double tat = flowPwi.FlowStep.GetTat(flowPwi.WipInfo.IsSmallLot); 
                    flowPwi.CalcLeadTime(tat);

                    // Yield 계산                
                    double yield = flowPwi.FlowStep.GetYield(flowPwi.FlowProductID, flowPwi.WipInfo.PowderCond, flowPwi.WipInfo.CompositionCode, out bool isSpecialYield);
                    flowPwi.CalcYield(yield, isSpecialYield);

                    // 여기까지 Run
                    ////////////////////////////////////////////
                    // 여기부터 STEP OUT


                    // WC-WT 기종 변경 / 1:N 변경 / SM9999
                    bomList.Clear();
                    if (BomHelper.CanWCWTProdChange(flowPwi, ref bomList))
                    {
                        foreach (SEMBOM bom in bomList)
                        {
                            // Clone PWI
                            SEMPegWipInfo newPwi = ClonePegWipInfo(flowPwi);
                            splitSeq++;
                            newPwi.BranchSeq = splitSeq;
                            newPwi.SetProductChangeInfo(flowPwi, bom);

                            // Change한 Product의 Next Oper 확인
                            SEMGeneralStep bomNextOper = OperationManager.GetNextOper(bom.ToStep);
                            string nextOperID = bomNextOper == null ? "NO_NEXT_OPER" : bomNextOper.StepID;

                            // 로그 작성
                            WriteLog.WriteWipAvailDateLog(newPwi, nextOperID, splitSeq, j);

                            // LPST log 생성                            
                            PrePegger.SetSemOperTarget(newPwi, bom.ChangeType);
                            PrePegger.SetSemOperTarget(newPwi); 
                            
                            if (bomNextOper == null)
                                continue;

                            // BOM Next Oper  -  WC는 StepOut단계에서 ProdChange를 하였기 때문에 nextOper를 하고 list에 넣어줌   
                            newPwi.SetNextStep(bomNextOper, tat, yield);
                            
                            PegHelper.AddPwiList(newPwi, true, pwiList);
                        }
                    }

                    // Check Target Oper
                    if (flowPwi.FlowStepID == targetOperId)
                    {
                        flowPwi.IsTargetOper = true;

                        // 다음 TargetOper 확인
                        string nextTargetoperId = PegWipInfoManager.GetNextTargetStepID(targetOperId);
                        if (nextTargetoperId == string.Empty)
                        {
                            // SG4460, 마지막 oper

                            // TargetOper에서는 Wait 시점에서도 Log 작성 (Demand는 Target Oper의 Wait 재공을 보기 때문에)
                            WriteLog.WriteWipAvailDateLog(flowPwi, "TARGET_OPER", i, j);

                            // LPST log 생성
                            PrePegger.SetSemOperTarget(flowPwi);

                            break;
                        }
                        else
                        {
                            // SG4090, 다음 Target 존재

                            // TargetOper에서는 Wait 시점에서도 Log 작성 (Demand는 Target Oper의 Wait 재공을 보기 때문에)
                            WriteLog.WriteWipAvailDateLog(flowPwi, "TARGET_OPER", i, j);

                            SEMPegWipInfo prevPwi = ClonePegWipInfo(flowPwi);

                            // LPST log 생성
                            PrePegger.SetSemOperTarget(prevPwi);
                            PegHelper.AddPwiList(prevPwi, true, pwiList);

                            prevPwi.IsSkipLogic = true;

                            planWip.SEMPegWipInfos.Add(prevPwi);

                            flowPwi.TargetOperID = nextTargetoperId;
                            flowPwi.DemKey = PrePegHelper.CreateDgKey(flowPwi);

                            targetOperId = nextTargetoperId;
                        }
                    }

                    // NextOper 확인
                    SEMGeneralStep nextOper = OperationManager.GetNextOper(flowPwi.FlowStep);
                    if (nextOper == null)
                    {
                        PegWipInfoManager.RemovePwi(flowPwi);
                        //planWip.SEMPegWipInfos.Remove(flowPwi);

                        flowPwi.IsSkipLogic = true;
                        flowPwi.IsUsable = false;

                        WriteLog.WritePegWipFilterLog(flowPwi, null, false, "NO_NEXT_OPER", "NO_NEXT_OPER", "");
                        WriteLog.WriteWipAvailDateLog(flowPwi, "NO_NEXT_OPER", i, j);

                        flowPwi.PlanWip.UnpegReason.Add("NO_NEXT_OPER(NO_BOM)");
                        //flowPwi.PlanWip.UnpegDetailReason.Add("");

                        break;
                    }

                    //Write Log : Log는 Step Out 시점에서 작성 (BOM 등 변화를 확인하기 위해)
                    WriteLog.WriteWipAvailDateLog(flowPwi, nextOper.StepID, i, j);

                    // LPST log 생성
                    PrePegger.SetSemOperTarget(flowPwi);

                    // Next Oper        
                    flowPwi.SetNextStep(nextOper, tat, yield);

                    isWaitWip = true;

                    j++;
                    if (j == 10000)
                        WriteLog.WriteErrorLog($"LOT_ID:{planWip.LotID} 무한루프 에러 발생");
                }
            }

            wipCnt++;
            if (wipCnt % 100 == 0)
                Logger.MonitorInfo($"=========> Prepeg Init \t Create PWIs : {planWip.LotID} \t {DateTime.Now.ToString("HH:mm:ss:ffffff")}");

            planWip.SEMPegWipInfos.AddRange(pwiList.Where(x=>x.IsUsable));
            planWip.UnusablePegWipInfos.AddRange(pwiList.Where(x => x.IsUsable == false));
        }

        private static int wipCnt = 0;

        private static void CalcLeadTime(this SEMPegWipInfo pwi, double tat)
        {
            // TAT 계산
            pwi.FlowTat = tat;
            pwi.TotalTat += tat;
            pwi.CalcOutDate = pwi.CalcAvailDate.AddMinutes(tat);


            if (pwi.FlowStepID == "SG3910")
                pwi.SG3910AvailableTime = pwi.CalcAvailDate;
            else if (pwi.FlowStepID == "SG4430")
                pwi.SG4430AvailableTime = pwi.CalcAvailDate;
        }

        private static void CalcYield(this SEMPegWipInfo pwi, double yield, bool isSpecialYield)
        {
            pwi.FlowQty = pwi.FlowQty * yield; // Math.Floor(flowPwi.FlowQty * yield);
            pwi.FlowYield = yield;
            pwi.AccumYield *= yield;
            pwi.IsSpecialYield = isSpecialYield;
        }

        public static void RemovePwi(SEMPegWipInfo pwi)
        {
            // 전체 PegWipInfo
            InputMart.Instance.SEMPegWipInfo.Rows.Remove(pwi);
        }

        public static void SetNextStep(SEMPegWipInfo flowPwi, SEMGeneralStep nextOper)
        {
            //// TAT 계산
            //double tat = flowPwi.CalcTat;
            //flowPwi.TotalTat += tat;
            //flowPwi.CalcAvailDate = flowPwi.CalcAvailDate.AddMinutes(tat);

            //// Next Oper            
            //flowPwi.FlowStep = nextOper;
            //flowPwi.FlowStepID = nextOper.StepID;
            //flowPwi.FlowRouteId = nextOper.SEMRouteID;

            ////Yield 계산 
            //double yield = OperationManager.GetYield(flowPwi.FlowProductID, flowPwi.FlowStepID);
            //flowPwi.FlowQty = Math.Floor(flowPwi.FlowQty * yield);
            //flowPwi.FlowYield = yield;
            //flowPwi.AccumYield *= yield;
            //if (yield != 1)
            //{
            //    string key = CommonHelper.CreateKey(flowPwi.FlowStepID, flowPwi.FlowProductID);
            //    flowPwi.YieldHistory.Add(key, yield);
            //}

            ////Get Calc TAT (NextStep의 TAT)
            //flowPwi.CalcTat = TimeHelper.GetTat(flowPwi.FlowProductID, flowPwi.FlowStepID);
        }


        public static void CreateSemPegWipInfo(SEMPlanWip planWip, string targetOperID, List<SEMPegWipInfo> pwiList)
        {
            //SEMPegWipInfo orgPwi = CreateHelper.CreatePegWipInfo(planWip, targetOperID);

            //PegHelper.AddPwiList(orgPwi, true, pwiList);

            //if (targetOperID == orgPwi.FlowStepID)
            //{//initial Step이 TargetStep인 wip은 object를 추가로 만듦
            //    string nextTargetStepID = PegWipInfoManager.GetNextTargetStepID(targetOperID);
            //    if (nextTargetStepID != string.Empty)
            //    {//Bulk Wip이 OIOper Wait Wip이면 Taret을 Taping으로 재설정해서 다시한번 구함
            //        SEMPegWipInfo orgPwi2 = CreateHelper.CreatePegWipInfo(planWip, nextTargetStepID);
            //        orgPwi2.IsTargetOper = true;

            //        PegHelper.AddPwiList(orgPwi2, true, pwiList);
            //    }
            //    else
            //    {// Target Oper == TapingOper (다음 Target x)
                    
            //    }
            //}

            //WriteLog.WriteWipAvailDateLog(orgPwi, 0, 0);
        }


        public static string GetTargetStepID(SEMPlanWip wip)
        {
            SEMGeneralStep initialStep = wip.WipInfo.InitialStep as SEMGeneralStep;

            string targetStepID = string.Empty;
            if (initialStep.SEMRouteID == "SGC117")
                targetStepID = Constants.SORTING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC118")
                targetStepID = Constants.SORTING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC119")
                targetStepID = Constants.SORTING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC120")
                targetStepID = Constants.SORTING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC121")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC122")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SMC123")
                targetStepID = Constants.TAPING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC123")
                targetStepID = Constants.TAPING_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC135")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC136")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC137")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC138")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC139")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC140")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC141")
                targetStepID = Constants.OI_OPER_ID;
            else if (initialStep.SEMRouteID == "SGC142")
                targetStepID = Constants.OI_OPER_ID;
            else
            {
                WriteLog.WriteErrorLog($"TargetOperID 오류 // LOT_ID : '{wip.LotID}' @ OPER_ID : '{targetStepID}'");
            }

            return targetStepID;
        }


        public static SEMPegWipInfo ClonePegWipInfo(SEMPegWipInfo orgPwi)
        {
            SEMPegWipInfo clone = new SEMPegWipInfo();

            clone.DemKey = orgPwi.DemKey;
            //clone.DemKeyList = orgPwi.DemKeyList;

            clone.PlanWip = orgPwi.PlanWip;
            clone.WipInfo = orgPwi.WipInfo;
            clone.TargetOperID = orgPwi.TargetOperID;
            clone.SiteID = orgPwi.SiteID;

            clone.BranchSeq = orgPwi.BranchSeq;
            clone.FromBranchSeq = orgPwi.FromBranchSeq;
            //clone.Seq = orgPwi.Seq;

            clone.FlowProduct = orgPwi.FlowProduct;
            clone.FlowCustomerID = orgPwi.FlowCustomerID;
            clone.FlowStep = orgPwi.FlowStep;
            clone.FlowStepID = orgPwi.FlowStepID;
            clone.FlowRouteId = orgPwi.FlowRouteId;

            clone.FlowQty = orgPwi.FlowQty;
            clone.FlowYield = orgPwi.FlowYield;
            clone.IsSpecialYield = orgPwi.IsSpecialYield;
            clone.CalcYield = orgPwi.CalcYield;
            clone.AccumYield = orgPwi.AccumYield;
            //clone.YieldHistory = new Dictionary<string, double>( orgPwi.YieldHistory);

            clone.MCBom = orgPwi.MCBom;
            clone.MC_ChangedProductID = orgPwi.MC_ChangedProductID;
            clone.MC_ChangedProdOperID = orgPwi.MC_ChangedProdOperID;
            clone.MC_ChangedCustomerID = orgPwi.MC_ChangedCustomerID;
            clone.MC_IsChangedProd = orgPwi.MC_IsChangedProd;

            clone.WRBom = orgPwi.WRBom;
            clone.WR_ChangedProductID = orgPwi.WR_ChangedProductID;
            clone.WR_ChangedProdOperID = orgPwi.WR_ChangedProdOperID;
            clone.WR_ChangedCustomerID = orgPwi.WR_ChangedCustomerID;
            clone.WR_IsChangedProd = orgPwi.WR_IsChangedProd;

            clone.WCBom = orgPwi.WCBom;
            clone.WC_ChangedProductID = orgPwi.WC_ChangedProductID;
            clone.WC_ChangedProdOperID = orgPwi.WC_ChangedProdOperID;
            clone.WC_ChangedCustomerID = orgPwi.WC_ChangedCustomerID;
            clone.WC_IsChangedProd = orgPwi.WC_IsChangedProd;

            clone.WTBom = orgPwi.WTBom;
            clone.WT_ChangedProductID = orgPwi.WT_ChangedProductID;
            clone.WT_ChangedProdOperID = orgPwi.WT_ChangedProdOperID;
            clone.WT_ChangedCustomerID = orgPwi.WT_ChangedCustomerID;
            clone.WT_IsChangedProd = orgPwi.WT_IsChangedProd;

            clone.BTBom = orgPwi.BTBom;
            clone.BT_ChangedProductID = orgPwi.BT_ChangedProductID;
            clone.BT_ChangedProdOperID = orgPwi.BT_ChangedProdOperID;
            clone.BT_ChangedCustomerID = orgPwi.BT_ChangedCustomerID;
            clone.BT_IsChangedProd = orgPwi.BT_IsChangedProd;

            clone.TotalTat = orgPwi.TotalTat;
            clone.CalcTat = orgPwi.CalcTat;
            clone.CalcAvailDate = orgPwi.CalcAvailDate;
            clone.CalcOutDate = orgPwi.CalcOutDate;
            clone.AvailableTimeDic.AddRange(orgPwi.AvailableTimeDic);

            clone.IsTargetOper = orgPwi.IsTargetOper;
            clone.IsLotOper = orgPwi.IsLotOper;
            clone.IsLotProdChange = orgPwi.IsLotProdChange;
            clone.IsMultiLotProdChange = orgPwi.IsMultiLotProdChange;
            clone.IsProdFixed = orgPwi.IsProdFixed;

            clone.SplitLotID = orgPwi.SplitLotID;
            clone.SplitProductID = orgPwi.SplitProductID;

            clone.ProcessingOpers.AddRange(orgPwi.ProcessingOpers);
            clone.SemOperTargets.AddRange(orgPwi.SemOperTargets);
            clone.EndCustomerList.AddRange(orgPwi.EndCustomerList);
            clone.OperDic.AddRange(orgPwi.OperDic);
            clone.NextOperDic.AddRange(orgPwi.NextOperDic);

            return clone;
        }


        public static string GetNextTargetStepID(string currnetTargetStepID)
        {
            if (currnetTargetStepID == Constants.SORTING_OPER_ID)
                return string.Empty;
            if (currnetTargetStepID == Constants.OI_OPER_ID)
                return Constants.TAPING_OPER_ID;
            else if (currnetTargetStepID == Constants.TAPING_OPER_ID)
                return string.Empty;

            return string.Empty;
        }

        public static bool CanChangeProduct(SEMPegWipInfo flowPwi)
        {
            string currentArea = string.Empty;
            
            if (flowPwi.TargetOperID == Constants.OI_OPER_ID)
            {
                if (flowPwi.WR_IsChangedProd)
                    return false;
            }
            else if (flowPwi.TargetOperID == Constants.TAPING_OPER_ID)
            {
                if (flowPwi.BT_IsChangedProd)
                    return false;
            }
            

            return true;
        }

        
        public static void PwiValidate(SEMPegWipInfo pwi, Dictionary<string, List<DemandGroup>> initDemGroupDic)
        {
            //Avail Wip List Set
            if (initDemGroupDic.ContainsKey(pwi.DemKey) == false)
            {
                if (string.IsNullOrEmpty(pwi.UnUsableReason) == false)
                    pwi.UnUsableReason += ", ";

                WriteLog.WritePegWipFilterLog(pwi, null, false, "DEMAND", "NO_DEMAND", "DEMAND 테이블 참조");

                pwi.UnUsableReason += Constants.NO_TARGET;
                pwi.IsUsable = false;
            }
        }

        public static void SetNextStep(this SEMPegWipInfo pwi, SEMGeneralStep nextStep, double tat, double yield)
        {
            pwi.OperDic.Add(pwi.FlowStepID, pwi.FlowStep);
            pwi.NextOperDic.Add(pwi.FlowStepID, nextStep);

            pwi.CalcAvailDate = pwi.CalcOutDate;
            pwi.CalcTat = tat;
            pwi.CalcYield = yield;
            pwi.FlowStep = nextStep;
            pwi.FlowStepID = nextStep.StepID;
            pwi.FlowRouteId = nextStep.SEMRouteID;
            pwi.Seq++;
        }

        public static bool HasDemand(this SEMPegWipInfo pwi)
        {
            bool hasDemand = InputMart.Instance.SEMGeneralMoMaster.Values.Where(x => x.Product.ProductID == pwi.FlowProductID).IsNullOrEmpty() ? false : true;

            return hasDemand;
        }


    }
}