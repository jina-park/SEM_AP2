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
using Mozart.Simulation.Engine;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class BomHelper
    {
        public static void SetBomInfo(SEMGeneralPegPart pp)
        {
            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {
                string currentProdID = pp.Product.ProductID;

                var bomList = InputMart.Instance.SEMBOMView.FindRows(currentProdID).ToList();
                if (bomList.Count() > 0)
                {
                    pp.HasBom = true;
                    foreach (var item in bomList)
                    {
                        pp.BomDic.Add(item.FromStep, item.FromProduct);
                    }
                }
            }
        }
        //public static List<SEMBOM> GetFromToBom(SEMPegWipInfo pwi, bool isProdNameChange = false)
        //{
        //    ICollection<SEMBOM> bomList;

        //    if (IsLotProdChange(pwi, isProdNameChange))
        //    {
        //        bomList = BomHelper.GetLotProdChangeBom(pwi.LotID, pwi.FlowStepID, isProdNameChange);
        //        pwi.IsLotProdChange = bomList.Count > 0 ? false : true; //자재 제약 적용시 값 셋팅 위치 변경 필요
        //    }
        //    else
        //    {
        //        bomList = BomHelper.GetFromToBom(pwi.FlowStepID, pwi.FlowProductID, pwi.FlowCustomerID, isProdNameChange);
        //    }

        //    return bomList.ToList();
        //}

        public static bool IsLotProdChange(SEMPegWipInfo pwi)
        {
            if (pwi.TargetOperID == Constants.TAPING_OPER_ID)
                return false;

            if (pwi.IsLotProdChange == false)
                return false;

            return true;
        }

        public static List<SEMBOM> GetLotProdChangeBom(string lotID, string operID)
        {
            ICollection<SEMBOM> boms;

            if (InputMart.Instance.LotProdChangeDic.TryGetValue(lotID, out boms))
            {
                if (boms.First().FromStepID != operID)
                    return new List<SEMBOM>();

                return boms.ToList();
            }
            else
            {   
                return new List<SEMBOM>();
            }
        }

        public static List<SEMBOM> GetFromToBom(string operId, string fromProdId, string fromCustomer, bool isProdNameChange = false)
        {
            List<SEMBOM> bomList = InputMart.Instance.SEMBOMView3.FindRows(fromProdId, operId, fromCustomer).ToList();

            bomList = bomList.FilterChangeType(isProdNameChange);

            return bomList;
        }

        public static List<SEMBOM> GetFromToBom(string fromProdId, string fromCustomer, string operId, string changeType)
        {
            List<SEMBOM> bomList = InputMart.Instance.SEMBOMView4.FindRows(fromProdId, fromCustomer, operId, changeType).ToList();
                       
            return bomList;
        }

        public static List<SEMBOM> FilterChangeType(this ICollection<SEMBOM> bomList, bool isProdNameChange)
        {
            if (isProdNameChange == true)
            {
                List<string> nameChangeCode = InputMart.Instance.BomChangeTypeDic[BomChangeType.NameChange];
                return bomList.Where(r => nameChangeCode.Contains(r.ChangeType)).ToList();
            }
            else
            {
                List<string> prodChangeCode = InputMart.Instance.BomChangeTypeDic[BomChangeType.ProdChange];
                return bomList.Where(r => prodChangeCode.Contains(r.ChangeType)).ToList();
            }
        }

        //public static List<SEMBOM> GetFromToBom(string fromOper, string fromProdId, string fromCustomer, bool isProdNameChange = false)
        //{
        //    List<SEMBOM> bomList = InputMart.Instance.SEMBOMView3.FindRows(fromProdId, fromOper, fromCustomer).ToList();
                       
        //    if (isProdNameChange == true)
        //    {
        //        List<string> nameChangeCode = InputMart.Instance.BomChangeTypeDic[BomChangeType.NameChange];
        //        return bomList.Where(r => nameChangeCode.Contains(r.ChangeType)).ToList();
        //    }
        //    else
        //    {
        //        List<string> prodChangeCode = InputMart.Instance.BomChangeTypeDic[BomChangeType.ProdChange];
        //        return bomList.Where(r => prodChangeCode.Contains(r.ChangeType)).ToList();
        //    }
        //}

        public static void ProductChange(this SEMPegWipInfo flowPwi, SEMBOM bom)
        {
            flowPwi.FromProdID = flowPwi.FlowProductID;
            flowPwi.FromCustID = flowPwi.FlowCustomerID;

            flowPwi.FlowProduct = bom.ToProduct;
            flowPwi.FlowCustomerID = bom.ToCustomerID == null ? flowPwi.FlowCustomerID  : bom.ToCustomerID;
            flowPwi.FlowStep = bom.ToStep;
            flowPwi.DemKey = PrePegHelper.CreateDgKey(flowPwi);

            if (bom.ChangeType == Constants.MC)
            {
                // LotProductChange
                flowPwi.MC_ChangedProductID = bom.ToProductID;
                flowPwi.MC_IsChangedProd = true;
                flowPwi.MC_ChangedCustomerID = bom.ToCustomerID == null ? flowPwi.FlowCustomerID : bom.ToCustomerID; // LotProdChange과정에서 null 로 셋팅될 수 있음
                flowPwi.MC_ChangedProdOperID = bom.ToStepID;
                flowPwi.MCBom = bom;
            }
            else if (bom.ChangeType == Constants.WR)
            {
                flowPwi.WR_ChangedProductID = bom.ToProductID;
                flowPwi.WR_IsChangedProd = true;
                flowPwi.WR_ChangedCustomerID = bom.ToCustomerID;
                flowPwi.WR_ChangedProdOperID = bom.ToStepID;
                flowPwi.WRBom = bom;
            }
            else if (bom.ChangeType == Constants.WC)
            {
                flowPwi.WC_ChangedProductID = bom.ToProductID;
                flowPwi.WC_IsChangedProd = true;
                flowPwi.WC_ChangedCustomerID = bom.ToCustomerID;
                flowPwi.WC_ChangedProdOperID = flowPwi.FlowStepID;
                flowPwi.WCBom = bom;
            }
            else if (bom.ChangeType == Constants.WT)
            {
                flowPwi.WT_ChangedProductID = bom.ToProductID;
                flowPwi.WT_IsChangedProd = true;
                flowPwi.WT_ChangedCustomerID = bom.ToCustomerID;
                flowPwi.WT_ChangedProdOperID = flowPwi.FlowStepID;
                flowPwi.WTBom = bom;
            }
            else if (bom.ChangeType == Constants.BT)
            {
                flowPwi.BT_ChangedProductID = bom.ToProductID;
                flowPwi.BT_IsChangedProd = true;
                flowPwi.BT_ChangedCustomerID = bom.ToCustomerID;                
                flowPwi.BT_ChangedProdOperID = bom.ToStepID;
                flowPwi.BTBom = bom;
            }
        }

        public static void SetProductChangeInfo(this SEMPegWipInfo newPwi, SEMPegWipInfo flowPwi, SEMBOM bom)
        {
            newPwi.ProductChange(bom);

            newPwi.FromBranchSeq = flowPwi.BranchSeq;
            newPwi.Seq = 0;

            if (bom.IsMultiLotProdChange)
            {
                newPwi.FlowQty = bom.Qty;
            }

        }

        public static bool CanMCProdChange(SEMPegWipInfo pwi, ref List<SEMBOM> bomList)
        {

            if (pwi.TargetOperID == Constants.TAPING_OPER_ID)
                return false;

            if (pwi.MC_IsChangedProd)
                return false;

            if (pwi.IsProdFixed)
                return false;

            if (pwi.IsLotProdChange)
            {
                // LotProdChange 로직
                bomList = GetLotProdChangeBom(pwi.LotID, pwi.FlowStepID);
            }
            else
            {
                // 일반 BOM 로직
                bomList = GetFromToBom(pwi.FlowProductID, pwi.FlowCustomerID, pwi.FlowStepID, Constants.MC);
            }

            if(bomList.Count > 0)
                return true;

            return false;
        }


        public static bool CanWRProdChange(SEMPegWipInfo pwi, ref List<SEMBOM> bomList)
        {
            if (pwi.FlowStepID != Constants.SM9999) // 탐색시간 절약
                return false;

            // 4090에서 9999 갈 때만 WR product변경 가능
            // 이미 9999에 있는 wip은 WR 불가
            // 9999에 있는 wip중 STOCK_WAIT은 wr가능
            if (pwi.WipInfo.InitialStep.StepID == Constants.SM9999 && pwi.WipInfo.DataType != "STOCK_WAIT")
                return false;                
            
            bomList = GetFromToBom(pwi.FlowProductID, pwi.FlowCustomerID, pwi.FlowStepID,Constants.WR);

            if (bomList.Count > 0)
                return true;

            return false;

        }
        
        public static bool CanWCWTProdChange(SEMPegWipInfo pwi, ref List<SEMBOM> bomList)
        {
            if (pwi.FlowStepID != Constants.SM9999) // 탐색시간 절약
                return false;

            var wcBomList = GetFromToBom(pwi.FlowProductID, pwi.FlowCustomerID, pwi.FlowStepID, Constants.WC);
            var wtBomList = GetFromToBom(pwi.FlowProductID, pwi.FlowCustomerID, pwi.FlowStepID, Constants.WT);

            bomList.AddRange(wcBomList);
            bomList.AddRange(wtBomList);

            if (bomList.Count > 0)
                return true;

            return false;
        }

        public static bool CanBTProdChange(SEMPegWipInfo pwi, ref List<SEMBOM> bomList)
        {
            if (pwi.FlowStepID != "SG6094") // 탐색시간 절약
                return false;

            bomList = GetFromToBom(pwi.FlowProductID, pwi.FlowCustomerID, pwi.FlowStepID, Constants.BT);

            // [TODO] BT가 무조건 있는게 확정되고, BT가 없다면 NO_BOM 로그작성

            if (bomList.Count > 0)
                return true;

            return false;
        }

        public static SEMBOM GetBom(SEMLot lot, bool isStepOut)
        {
            // Pegging단계에서 wip이 변경될 bom을 확정하고 PlanWip의 PegWipInfo에 기록
            // Forward단계에서 PegWipInfo에 기록된 정보로 Product Change

            // isStepOut : 호출위치가 Step종료시점인지 시작시점인지 구분
            // true : WC, WT
            // false : MC, WR, BT

            SEMWipInfo wip = lot.WipInfo as SEMWipInfo;
            SEMPlanWip planWip = wip.PlanWip;
            SEMPegWipInfo pwi = planWip.PegWipInfo;

            // Pegging되지 않았으나 Forward를 태우는 lot (run재공, ResFixLotArrange 등)
            if (planWip == null || pwi == null)
                return null;

            bool canMCChange = false;
            bool canWRChange = false;
            bool canWCChange = false;
            bool canWTChange = false;
            bool canBTChange = false;

            if (isStepOut)
            {
                canWCChange = lot.IsWCChanged == false && pwi.WC_IsChangedProd && lot.CurrentStepID == pwi.WC_ChangedProdOperID;
                canWTChange = lot.IsWTChanged == false && pwi.WT_IsChangedProd && lot.CurrentStepID == pwi.WT_ChangedProdOperID;
            }
            else
            {
                canMCChange = lot.IsMCChanged == false && pwi.MC_IsChangedProd && lot.CurrentStepID == pwi.MC_ChangedProdOperID && pwi.IsMultiLotProdChange == false;
                canWRChange = lot.IsWRChanged == false && pwi.WR_IsChangedProd && lot.CurrentStepID == pwi.WR_ChangedProdOperID;
                canBTChange = lot.IsBTChanged == false && pwi.BT_IsChangedProd && lot.CurrentStepID == pwi.BT_ChangedProdOperID;
            }

            SEMBOM bom = null;

            if (canMCChange)
            {
                bom = pwi.MCBom;
            }
            else if (canWRChange)
            {
                bom = pwi.WRBom;
            }
            else if(canWCChange)
            {
                bom = pwi.WCBom;
            }
            else if (canWTChange)
            {
                bom = pwi.WTBom;
            }
            else if (canBTChange)
            {
                bom = pwi.BTBom;
            }
            else
            {
                // Product 변경 안함 
                return null;  
            }

            return bom;
        }

        public static void ProductChange(this SEMLot lot, SEMBOM bom)
        {
            // From 정보 셋팅            
            lot.FromProductID = lot.CurrentProductID;
            lot.FromCustomerID = lot.CurrentCustomerID;


            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent)
            {
                var prod = lot.SEMProduct;

                var wm = Mozart.SeePlan.Simulation.AoFactory.Current.WipManager;
                wm.Changing(lot);

                var agent = AoFactory.Current.JobChangeManger.GetAgent("DEFAULT");
                if (agent != null)
                {
                    SEMGeneralStep tStep = lot.GetAgentTargetStep(lot.CurrentSEMStep);
                    var workGroup = lot.CurrentWorkGroup;
                    var workStep = lot.CurrentWorkStep;

                    SEMWipInfo winfo = lot.WipInfo as SEMWipInfo;

                    bool isRun = bom.ChangeType == "WR" ? false : true;

                    if (tStep != null && lot.CurrentWorkStep != null)
                    {
                        SEMPlanInfo plan = new SEMPlanInfo(bom.ToStep);
                        plan.ProductID = bom.ToProduct.ProductID;
                        plan.ProcessID = bom.ToProduct.Process.ProcessID;
                        plan.LotID = lot.LotID;
                        plan.UnitQty = lot.UnitQtyDouble;
                        lot.Route = bom.ToProduct.Process;
                        lot.Product = bom.ToProduct;

                        lot.SetToPlan(plan);


                        var wLot = lot.CurrentWorkLot;
                        if (wLot == null)
                            WriteLog.WriteErrorLog($"wlot을 찾을 수 없습니다.{lot.LotID}{tStep.StepID}");

                        SEMWorkLot newWLot = AgentHelper.RecreateSEMWorkLot(lot, wLot);

                        workStep.RemoveWip(wLot);
                        workStep.AddWip(newWLot);
                        

                        //lot.WorkGroup = workGroup as SEMWorkGroup;
                        //lot.WorkStep = workStep as SEMWorkStep;
                        lot.CurrentWorkLot = newWLot;

                        wm.Changed(lot);
                    }
                    else //SG3910을 거쳐서 SG4430 공정으로 가는 재공 처리
                    {
                        SEMPlanInfo plan = new SEMPlanInfo(bom.ToStep);
                        plan.ProductID = bom.ToProduct.ProductID;
                        plan.ProcessID = bom.ToProduct.Process.ProcessID;
                        plan.LotID = lot.LotID;
                        plan.UnitQty = lot.UnitQtyDouble;
                        lot.Route = bom.ToProduct.Process;
                        lot.Product = bom.ToProduct;

                        if (tStep != null)
                        {
                            var wLot = lot.CurrentWorkLot;
                            if (wLot == null)
                                WriteLog.WriteErrorLog($"wlot을 찾을 수 없습니다.{lot.LotID}{tStep.StepID}");
                            else
                            {
                                SEMWorkLot newWLot = AgentHelper.RecreateSEMWorkLot(lot, wLot);

                                workStep.RemoveWip(wLot);
                                workStep.AddWip(newWLot);

                                lot.CurrentWorkLot = newWLot;
                            }
                        }

                        lot.SetToPlan(plan);

                        wm.Changed(lot);
                    }
                }
                else 
                {
                    IWipManager w = AoFactory.Current.WipManager;

                    //lot의 속성을 수정가능한 상태로 변경
                    w.Changing(lot);

                    // Lot의 CurrentStep을 변경 (PlanInfo를 사용하여 currentStep 정보를 바꿔줌)
                    SEMPlanInfo plan = new SEMPlanInfo(bom.ToStep);
                    plan.Product = bom.ToProduct;
                    plan.ProductID = bom.ToProduct.ProductID;
                    plan.ProcessID = bom.ToProduct.Process.ProcessID;
                    plan.LotID = lot.LotID;
                    plan.UnitQty = lot.UnitQtyDouble;
                    //plan.Init(bom.ToStep);
                    //lot.SetCurrentPlan(plan);

                    lot.SetToPlan(plan);

                    // Lot 정보 변경
                    lot.Product = bom.ToProduct;
                    lot.CurrentCustomerID = bom.IsLotProdChange ? lot.CurrentCustomerID : bom.ToCustomerID;  // LotProductChange는 customerId를 유지

                    //lot의 속성을 수정 완료 상태로 변경
                    w.Changed(lot);
                }
                
            }
            else
            {
                IWipManager wm = AoFactory.Current.WipManager;

                //lot의 속성을 수정가능한 상태로 변경
                wm.Changing(lot);

                // Lot의 CurrentStep을 변경 (PlanInfo를 사용하여 currentStep 정보를 바꿔줌)
                SEMPlanInfo plan = new SEMPlanInfo(bom.ToStep);
                plan.Product = bom.ToProduct;
                plan.ProductID = bom.ToProduct.ProductID;
                plan.ProcessID = bom.ToProduct.Process.ProcessID;
                plan.LotID = lot.LotID;
                plan.UnitQty = lot.UnitQtyDouble;
                //plan.Init(bom.ToStep);
                //lot.SetCurrentPlan(plan);

                lot.SetToPlan(plan);

                // Lot 정보 변경
                lot.Product = bom.ToProduct;
                lot.CurrentCustomerID = bom.IsLotProdChange ? lot.CurrentCustomerID : bom.ToCustomerID;  // LotProductChange는 customerId를 유지

                //lot의 속성을 수정 완료 상태로 변경
                wm.Changed(lot);
            }

        }

        public static void WRProductChange(this SEMLot lot)
        {
            SEMBOM bom = BomHelper.GetBom(lot, false);
            if (bom != null)
            {
                // Lot의 Product 변경
                lot.ProductChange(bom);

                // ProductChange 로그 작성
                WriteLog.WriteProductChangeLog(lot, bom);

                foreach (var pp in lot.PeggingDemands)
                    WriteOperPlan.WriteProdChangeOperPlanDetail(lot, bom, pp.Key);
                WriteOperPlan.WriteProdChangeOperPlan(lot, bom);
            }
            else
            {
                if (lot.PlanWip.PegWipInfo.WC_IsChangedProd == false && lot.PlanWip.PegWipInfo.WT_IsChangedProd == false) // 창고내 다른 BOM이 없으면 출력
                {
                    // OPER_PLAN 로그 작성 
                    // WR product Change를 하지 않아도 9999에 들어오는 lot은 모두 oper plan 작성
                    foreach (var pp in lot.PeggingDemands)
                        WriteOperPlan.WriteProdChangeOperPlanDetail(lot, null, pp.Key);
                    WriteOperPlan.WriteProdChangeOperPlan(lot, null);
                }
            }
        }

        public static void MCBTProductChange(this SEMLot lot)
        {
            SEMBOM bom = BomHelper.GetBom(lot, false);
            if (bom == null)
                return;

            // Lot의 Product 변경
            lot.ProductChange(bom);

            // ProductChange 로그 작성
            WriteLog.WriteProductChangeLog(lot, bom);

            // OPER_PLAN 로그 작성
            foreach (var pp in lot.PeggingDemands)
                WriteOperPlan.WriteProdChangeOperPlanDetail(lot, bom, pp.Key);
            WriteOperPlan.WriteProdChangeOperPlan(lot, bom);
        }
    }
}