using Mozart.Task.Execution.Persists;
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
using Mozart.SeePlan;
using Mozart.SeePlan.Simulation;
using Mozart.SeePlan.DataModel;
using System.Globalization;
using System.Data;

namespace SEM_AREA.Logic
{
    [FeatureBind()]
    public partial class PersistInputs
    {
        public void OnAction_OPERATION(IPersistContext context)
        {            
            //if (list.FirstOrDefault().OperID=="") { } // Engine fail test code
            InputMart.Instance.OPERATION.DefaultView.Sort = "SEQUENCE";

            List<SEMStdStep> list = new List<SEMStdStep>();

            foreach (OPERATION item in InputMart.Instance.OPERATION.DefaultView)
            {
                // StdStep 생성
                SEMStdStep stdStep = CreateHelper.CreateStdStep(item);

                InputMart.Instance.SEMStdStep.ImportRow(stdStep);

                list.Add(stdStep);

                // Route별 operation
                InputMart.Instance.RouteDic.Add(item.OPER_ID, item.ROUTE_ID);

                // Demand Operation Seq 셋팅
                if (item.OPER_ID == Constants.OI_OPER_ID)
                {
                    OperationManager.OISeq = Convert.ToInt32(item.SEQUENCE);
                }
                else if (item.OPER_ID == Constants.TAPING_OPER_ID)
                {
                    OperationManager.TapingSeq = Convert.ToInt32(item.SEQUENCE);
                }
                else if (item.OPER_ID == Constants.SORTING_OPER_ID)
                { 
                    OperationManager.SortingSeq = Convert.ToInt32(item.SEQUENCE);
                }

                // BT 가상 Oper 생성
                if (item.OPER_ID == "SM9999")
                {                    
                    SEMStdStep btStdStep = CreateHelper.CreateBTStdStep();
                    InputMart.Instance.SEMStdStep.ImportRow(btStdStep);
                    list.Add(btStdStep);
                }

                // 4430 oper seq
                if (item.OPER_ID == "SG4430")
                    InputMart.Instance.SG4430Sequence = Convert.ToInt32(item.SEQUENCE);

                //// TapingProcessingOperSeq 셋팅
                //if (item.OPER_TYPE == "T" && item.AP_PROCESSING_TYPE == "PROCESSING")
                //    InputMart.Instance.TapingProcessingOperSeq = (int)item.SEQUENCE;

                // CTOperDic 셋팅
                if(item.CT_OPER_ID.IsNullOrEmpty() == false)
                    InputMart.Instance.CTOperDic.Add(item.CT_OPER_ID, item.OPER_ID);
            }

            //기본 정보 설정
            CreateHelper.LinkStdStep(list);
        }

        public void OnAction_PRODUCT_OPER(IPersistContext context)
        {
            InputMart.Instance.PRODUCT_OPER.DefaultView.Sort = "PRODUCT_ID, SEQUENCE, OPER_ID";

            SEMProcess proc = null;

            SEMGeneralStep prevStep = null;
            foreach (PRODUCT_OPER item in InputMart.Instance.PRODUCT_OPER.DefaultView)
            {
                //아래 대공정들은 AREA1,3 에서 다루므로 저장하지 않음
                if (item.ROUTE_ID == "SGC113" || item.ROUTE_ID == "SGC100" || item.ROUTE_ID == "SGC114" ||
                    item.ROUTE_ID == "SGC115" || item.ROUTE_ID == "SGC116" || item.ROUTE_ID == "SGC121" ||
                    item.ROUTE_ID == "SGC122" || item.ROUTE_ID == "SMC123" || item.ROUTE_ID == "SGC123")
                    continue;

                string key = item.PRODUCT_ID;

                if (prevStep == null || prevStep.Key != key)
                    prevStep = null;

                if (proc == null || proc.Key != key)
                    proc = CreateHelper.GetSafeProcess(item.PRODUCT_ID);

                SEMStdStep stdStep = BopHelper.FindStdStep(item.OPER_ID);
                if (stdStep == null)
                {
                    #region Write ErrorHistory
                    ErrHist.WriteIf(string.Format("{0}/{1}", item.PRODUCT_ID, item.OPER_ID),
                                ErrCategory.PERSIST,
                                ErrLevel.ERROR,
                                Constants.NULL_ID,
                                Constants.NULL_ID,
                                item.PRODUCT_ID,
                                Constants.NULL_ID,
                                item.OPER_ID,
                                "NOT FOUND STD_STEP",
                                "Table:ProcStep"
                                );
                    #endregion

                    continue;
                }
                    
                SEMGeneralStep step = CreateHelper.CreateStep(item, stdStep);
                proc.Steps.Add(step);
                InputMart.Instance.SEMGeneralStep.ImportRow(step);

                if (prevStep == null)
                    prevStep = step;

                //prevStep.NextStepId = step.StepID;

                prevStep = step;

                SEMYield y = CreateHelper.CreateYield(item);
                key = CommonHelper.CreateKey(item.PRODUCT_ID, item.OPER_ID);
                if (InputMart.Instance.SEMYield.ContainsKey(key) == false)
                    InputMart.Instance.SEMYield.Add(key, y);
                step.Yield = (double)item.YIELD;

                if (stdStep.IsProcessing == true && stdStep.StepType == "T")
                    InputMart.Instance.TapingProcessingOperSeq.Add(item.PRODUCT_ID, (int)item.SEQUENCE);

                // BT Dummy Oper 생성
                if (item.OPER_ID == "SM9999")
                { 
                    SEMGeneralStep btStep = CreateHelper.CreateBTDummyStep(item);
                    proc.Steps.Add(btStep);
                    InputMart.Instance.SEMGeneralStep.ImportRow(btStep);
                    if (prevStep == null)
                        prevStep = btStep;
                    prevStep = btStep;
                }

            }

            foreach (SEMProcess item in InputMart.Instance.SEMProcess.Rows)
            {
                //Process 내 Step을 연결
                item.LinkSteps();

                //Process 내 Steps를 SEMGeneralStep type으로 가져 올 수 있게 함
                foreach(var step in item.Steps)
                {
                    item.SEMGeneralSteps.Add(step as SEMGeneralStep);
                }
            }

            #region CheckVaild
            List<SEMProcess> invalidProcs = new List<SEMProcess>();
            foreach (SEMProcess item in InputMart.Instance.SEMProcess.Rows)
            {
                if (item.Steps.Count == 0)
                {
                    invalidProcs.Add(item);

                    ErrHist.WriteIf(string.Format("{0}/{1}", item.ProcessID, "LoadProcess"),
                            ErrCategory.PERSIST,
                            ErrLevel.ERROR,
                            Constants.NULL_ID,
                            Constants.NULL_ID,
                            item.ProcessID,
                            Constants.NULL_ID,
                            Constants.NULL_ID,
                            "NO-STEPS",
                            $"{item.ProcessID} Process does not have steps"
                            );

                    continue;
                }
            }

            foreach (SEMProcess item in invalidProcs)
                InputMart.Instance.SEMProcess.Rows.Remove(item);

            #endregion
            
        }


        public void OnAction_PRODUCT(IPersistContext context)
        {
           
            foreach (var item in InputMart.Instance.PRODUCT.DefaultView)
            {
                bool hasError = false;

                // SEMProcess proc = ValidationHelper.CheckProcess(item.FACTORY_ID, item.PROCESS_ID, "Product", ref hasError);
                SEMProcess proc = ValidationHelper.CheckProcess("", item.PRODUCT_ID, "Product", ref hasError);
                if (hasError)
                    continue;

                string key = item.PRODUCT_ID;

                if (InputMart.Instance.SEMProduct.ContainsKey(key))
                {
                    #region Write ErrorHist
                    ErrHist.WriteIf(
                            string.Format("LoadProd{0}", item.PRODUCT_ID),
                            ErrCategory.PERSIST,
                            ErrLevel.WARNING,
                            Constants.NULL_ID,
                            item.PRODUCT_ID,
                            item.PRODUCT_ID,
                            Constants.NULL_ID,
                            Constants.NULL_ID,
                            "DUPLICATION PRODUCT",
                            "Table:Product");
                    #endregion

                    continue;
                }

                SEMProduct prod = CreateHelper.CreateProduct(item, proc);
                    
                InputMart.Instance.SEMProduct.Add(key, prod);

                var lastStep = proc.Steps.Last() as SEMGeneralStep;
                proc.LastStep = lastStep;


                #region 자재코드 정보 입력

                var altList = InputMart.Instance.TAPING_MATERIAL_ALT.Rows.Where(x => x.CHIP_SIZE == item.CHIP_SIZE && x.TAPING_TYPE == item.TAPING_TYPE && x.THICKNESS == item.THICKNESS && x.CARRIER_TYPE == item.CARRIER_TYPE);
                foreach (var alt in altList)
                {
                    Tuple<string, string> prodAlt = new Tuple<string, string>(alt.FROM_ITEM, alt.TO_ITEM);

                    prod.MaterialList.Add(prodAlt);
                }
                #endregion

                //주석 박진아 : 사용하지 않는 기능
                //CreateHelper.AddProcessMap(prod, proc);
            }
        }

        public void OnAction_DEMAND(IPersistContext context)
        {
            var totalDemandQty = (double)InputMart.Instance.DEMAND.Rows.Sum(x => x.QTY);
            WriteLog.WritePegValidationDemand_TOTAL(totalDemandQty);

            IOrderedEnumerable<DEMAND> demands = InputMart.Instance.DEMAND.Rows.OrderBy(x => x.PRODUCT_ID).ThenBy(x => x.DUE_DATE);
            foreach (DEMAND item in demands)
            {
                //WriteLog.CollectDemand(item);
                DateTime due = ConvertHelper.StringToDateTime(item.DUE_DATE, false);

                if (item.QTY == 0)
                {
                    WriteLog.WritePegValidationDemand_UNPEG(item, "ZERO_QTY", "");
                    continue;
                }

                if (due < ShopCalendar.SplitDate(context.ModelContext.StartTime).AddDays(-1))
                {
                    WriteLog.WritePegValidationDemand_UNPEG(item, "INVALID_DATE", "");
                    continue;
                }

                string prodID = item.PRODUCT_ID;
                DateTime dtDue = ConvertHelper.StringToDateTime(item.DUE_DATE, true);
                
                string key = item.DEMAND_ID;

                SEMGeneralMoMaster mm;
                if (InputMart.Instance.SEMGeneralMoMaster.TryGetValue(key, out mm) == false)
                {
                    bool hasError = false;
                    SEMProduct prod = ValidationHelper.CheckProduct(prodID, "Demand", ref hasError);
                    if (hasError)
                    {
                        WriteLog.WritePegValidationDemand_UNPEG(item, "NO_PRODUCT", "");
                        continue;
                    }

                    SEMGeneralStep step = ValidationHelper.CheckStep(prod.Process.ProcessID, item.OPER_ID, "Demand", ref hasError);
                    if (hasError)
                    {
                        WriteLog.WritePegValidationDemand_UNPEG(item, "NO_PROD_OPER", "");
                        continue;
                    }

                    mm = new SEMGeneralMoMaster(prod, item.CUSTOMER_ID);
                    mm.SiteID = item.SITE_ID;
                    mm.LastStep = step;
                    mm.TargetOperID = item.OPER_ID;
                    mm.TargetQty = Convert.ToDouble(item.QTY);
                    mm.DemandID = item.DEMAND_ID;
                    mm.EndCustomerID = item.END_CUSTOMER_ID == null ? string.Empty : item.END_CUSTOMER_ID;
                    mm.Priority = Convert.ToDouble(item.PRIORITY);
                    mm.Week = CommonHelper.GetWeekNo(CommonHelper.StringToDateTime(item.DUE_DATE, true));

                    if (item.OPER_ID == Constants.OI_OPER_ID)
                    {
                        // CustomerID는 0000000000으로 고정
                        mm.DemandCustomerID = item.CUSTOMER_ID;
                    }
                    else if (item.OPER_ID == Constants.TAPING_OPER_ID)
                    {
                        if (item.CUSTOMER_ID == "0000000000") // 일반 거래선,
                        {
                            // EndCustomerID만 사용 
                            mm.DemandCustomerID = item.END_CUSTOMER_ID;
                        }
                        else if (item.CUSTOMER_ID != "0000000000" && item.END_CUSTOMER_ID == "0000000000") // 특화거래선
                        {
                            // CustomerID만 사용
                            mm.DemandCustomerID = item.CUSTOMER_ID;
                        }
                        else if (item.CUSTOMER_ID  != "0000000000" && item.END_CUSTOMER_ID != "0000000000")
                        {
                            // err : 이런데이터는 들어오지 않음
                        }
                    }

                    step.StdStep.IsDemandStep = true;

                    InputMart.Instance.SEMGeneralMoMaster.Add(key, mm);
                }

                SEMGeneralMoPlan mo = CreateHelper.CreateMoPlan(mm, item, dtDue);

                mm.MoPlanList.Add(mo);
            }

            
            List<double> priorityList = InputMart.Instance.SEMGeneralMoMaster.Values
                .Select(x => x.Priority)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            InputMart.Instance.DemPriorityList.AddRange(priorityList);

            // Demand의 Week중 가장 작은 값, Default 값에 사용됨
            InputMart.Instance.MinDemandWeek = InputMart.Instance.DEMAND.Rows.Select(x => x.TAPING_WEEK).Distinct().OrderBy(x => x).FirstOrDefault();

        }

        public void OnAction_BOM(IPersistContext context)
        {           
            if (InputMart.Instance.GlobalParameters.ApplyBom == true)
            {

                foreach (Inputs.BOM item in InputMart.Instance.BOM.DefaultView)
                {
                    SEMProduct fromProd = null;
                    SEMProduct toProd = null;
                    SEMGeneralStep fromStep = null;
                    SEMGeneralStep toStep = null;

                    // Validation
                    bool isValidBom = ValidationHelper.IsValidBom(item, ref fromProd, ref toProd, ref fromStep, ref toStep);
                    if (isValidBom == false)
                        continue;

                    SEMBOM bom = new DataModel.SEMBOM();
                                       
                    bom.FromProduct = fromProd;
                    bom.ToProduct = toProd;

                    bom.FromStep = fromStep;
                    bom.ToStep = toStep;

                    bom.FromCustomerID = item.FROM_CUSTOMER_ID;
                    bom.ToCustomerID = item.TO_CUSTOMER_ID;
                        
                    bom.ChangeType = item.CHANGE_TYPE;
                    bom.IsLotProdChange = false;

                    InputMart.Instance.SEMBOM.ImportRow(bom);
                    
                    WriteLog.WriteBomVaildation(item, true, string.Empty);
                }

                List<string> changeTypeList = new List<string>();
                changeTypeList.Add(Constants.MC);
                changeTypeList.Add(Constants.WC);
                InputMart.Instance.BomChangeTypeDic.Add(BomChangeType.ProdChange, changeTypeList);

                changeTypeList = new List<string>();
                changeTypeList.Add(Constants.WR);
                changeTypeList.Add(Constants.BT);
                InputMart.Instance.BomChangeTypeDic.Add(BomChangeType.NameChange, changeTypeList);

                List<string> bomOperList = InputMart.Instance.BOM.Rows.Select(x => x.OPER_ID).Distinct().ToList();
                InputMart.Instance.BomOperaionList.AddRange(bomOperList);
            }            
        }

        public bool OnAfterLoad_RESOURCE(RESOURCE entity)
        {
            // 주석 :  ONLY_LOT_ARRANGE 조건에서, VALID가 N이여도 Lot이 무조건 투입되기 때문에 VALID가 N인 EQP도 object를 만들어줌
            //if (entity.VALID == "N")
            //    return false;

            SEMEqp eqp;

            if (InputMart.Instance.SEMEqp.TryGetValue(entity.RESOURCE_ID, out eqp))
            {
                eqp.OperIDs.Add(entity.OPER_ID);

                if (entity.OPER_ID == "SG3910" || entity.OPER_ID == "SG4430")
                    eqp.ResGroup = entity.OPER_ID;

                return true;
            }

            if (entity.UTILIZATION <= 0)
                return false;

            eqp = CreateHelper.CreateEqp(entity);

            InputMart.Instance.SEMEqp.Add(entity.RESOURCE_ID, eqp);

            SEMWeightPreset wp;
            if (InputMart.Instance.SEMWeightPreset.TryGetValue(eqp.PresetID, out wp))
                eqp.Preset = wp;
                        
            return true;
        }

        public void OnAction_WIP(IPersistContext context)
        {
            // Wip Count
            var totalWipQty = (double) InputMart.Instance.WIP.Rows.Sum(x => x.QTY);
            WriteLog.WritePegValidationWip_TOTAL(totalWipQty);

            // Create Wip
            foreach (WIP item in InputMart.Instance.WIP.DefaultView)
            {
                try
                {
                    SEMProduct prod = null;
                    SEMGeneralStep step = null;
                    bool hasError = false;

                    ValidationHelper.CheckWip(item, ref prod, ref step, ref hasError);
                    if (hasError)
                        continue;

                    SEMWipInfo wip = CreateHelper.CreateWip(item, step, prod);

                    InputMart.Instance.SEMWipInfo.Add(wip.LotID, wip);
                }
                catch (Exception e)
                {
                    WriteLog.WriteErrorLog(e.Message);
                }
            }

            // Set Same Lot Info
            if (GlobalParameters.Instance.ApplyContinualProcessingSameLot)
            {
                var lotGroups = InputMart.Instance.SEMWipInfo.GroupBy(x => x.Value.LotName);

                foreach (var group in lotGroups)
                {
                    List<SEMWipInfo> wipList = group.Select(x => x.Value).ToList();

                    if (wipList.IsValidSameLot() == false)
                        continue;

                    var lotName = group.First().Value.LotName;

                    foreach (var wipPair in group)
                    {
                        var wip = wipPair.Value;

                        //samelot 조건 : LotID 앞자리 동일, ReelLabel 동일
                        var sameLotList = group.Where(x => x.Value.IsReelLabeled == wip.IsReelLabeled).Select(x => x.Value).ToList();

                        if (sameLotList.Count == 1)
                        {
                            WriteLog.WriteSameLotLog("CreateWip", wip, sameLotList, false, "InvalidGroup(ReelLabel)");
                            continue;
                        }

                        wip.SameLots = sameLotList;

                        WriteLog.WriteSameLotLog("CreateWip", wip, sameLotList, true, "");
                    }
                }
            }
        }

        public void OnAction_CYCLE_TIME(IPersistContext context)
        {
            TimeHelper.SetCycleTimeForProdOper();

            TimeHelper.SetCycleTimeForLotOper();

            //foreach (var stdStep in InputMart.Instance.SEMStdStep.Rows)
            //{
            //    if (stdStep.IsProcessing == false)
            //        continue;

            }

        public void OnAction_LEAD_TIME(IPersistContext context)
        {   
            //Product가 없는 데이터가 먼저 들어와 step L/T값을 셋팅후 Product가 있는 데이터를 다시 덮어 씌움
            InputMart.Instance.LEAD_TIME.DefaultView.Sort = "PRODUCT_ID, OPER_ID ASC";

            foreach (LEAD_TIME item in InputMart.Instance.LEAD_TIME.DefaultView)
            {
                if (item.PRODUCT_ID == null || item.PRODUCT_ID == "" || item.PRODUCT_ID == "-")  //PRODUCT_ID가 공백인 데이터는 더이상 들어오지 않으나 조건문 유지
                {
                    List<SEMGeneralStep> steps = InputMart.Instance.SEMGeneralStepView2.FindRows(item.OPER_ID).ToList();
                    if (steps == null || steps.Count == 0)
                        continue;
                    
                    double tat = TimeHelper.GetMinutesByUom(item.TAT, item.TAT_UOM);

                    foreach (SEMGeneralStep step in steps)
                    {
                        step.TAT = tat;
                    }
                }
                else
                {
                    SEMGeneralStep step = InputMart.Instance.SEMGeneralStepView.FindRows(item.PRODUCT_ID, item.OPER_ID).FirstOrDefault();   //
                    if (step == null)
                        continue;

                    double tat = TimeHelper.GetMinutesByUom(item.TAT, item.TAT_UOM);
                    
                    step.TAT = tat;

                }
            }      

             // LotOper의 tat는 wip persist시 셋팅됨
        }

        public void OnAction_LOT_OPER(IPersistContext context)
        {
            InputMart.Instance.LOT_OPER.DefaultView.Sort = "LOT_ID, PRODUCT_ID, CUSTOMER_ID, ROUTE_ID, SEQUENCE ASC";

            var groups = InputMart.Instance.LOT_OPER.Rows.GroupBy(x => x.LOT_ID);
            foreach (var group in groups)
            {
                var sample = group.Last();

                #region Validation
                // Area2의 데이터 여부 확인
                if (InputMart.Instance.RouteDic.Values.Contains(sample.ROUTE_ID) == false)
                    continue;

                // ProdOper에 현재 route 존재 여부 확인
                bool isValidRouteID = InputMart.Instance.SEMGeneralStep.Rows.Where(x => x.ProductID == sample.PRODUCT_ID).Select(x => x.SEMRouteID).Contains(sample.ROUTE_ID);
                if (isValidRouteID == false)
                {
                    InputMart.Instance.InvalidLotOper_NotFoundRouteID.Add(sample.LOT_ID, sample.ROUTE_ID);
                    continue;
                }

                // SGC120의 경우 Demand  Oper 존재 여부 확인
                if (sample.ROUTE_ID == "SGC120")
                {
                    bool hasDemandOper = group.Where(x=>x.OPER_ID == "SG3890").Count() > 0 ? true : false;
                    if (hasDemandOper == false)
                    {
                        InputMart.Instance.InvalidLotOper_NoDemandOper.Add(sample.LOT_ID);
                        continue;
                    }
                }
                #endregion

                SEMLotOper lotOper = new SEMLotOper();

                lotOper.LotID = sample.LOT_ID;
                lotOper.ProductID = sample.PRODUCT_ID;
                lotOper.SEMRouteID = sample.ROUTE_ID;
                lotOper.CustomerID = sample.CUSTOMER_ID;

                InputMart.Instance.SEMLotOper.ImportRow(lotOper);

                var sortedGroup = group.OrderBy(x => x.SEQUENCE).ToList();
                foreach (var item in sortedGroup)
                {
                    SEMGeneralStep step = new SEMGeneralStep(item.OPER_ID);

                    step.StdStep = BopHelper.FindStdStep(item.OPER_ID);
                    step.Sequence = Convert.ToInt32(item.SEQUENCE);
                    step.SEMRouteID = item.ROUTE_ID;
                    step.IsLotOper = true;
                    step.ProductID = item.PRODUCT_ID;
                    step.Yield = 1;

                    SEMYield y = CreateHelper.CraeteLotOperYield(item);
                    string key = CommonHelper.CreateKey(item.PRODUCT_ID, item.OPER_ID);
                    if (InputMart.Instance.SEMYield.ContainsKey(key) == false)
                        InputMart.Instance.SEMYield.Add(key, y);

                    if (lotOper.Steps.Count != 0)
                    {
                        step.PrevOper = lotOper.Steps.Last();
                        step.PrevOper.NextOper = step;
                    }

                    lotOper.Steps.Add(step);
                }
            }
        }

        public bool OnAfterLoad_LOT_PRODUCT_CHANGE(LOT_PRODUCT_CHANGE entity)
        {
            #region validation

            bool hasError = false;

            SEMProduct fromProd = ValidationHelper.CheckProduct(entity.FROM_PROD_ID , "InterShopBom", ref hasError);
            if (hasError)
                return false;

            SEMProduct toProd = ValidationHelper.CheckProduct(entity.TO_PROD_ID, "InterShopBom", ref hasError);
            if (hasError)
                return false;

            SEMGeneralStep fromStep = ValidationHelper.CheckStep(entity.FROM_PROD_ID, entity.OPER_ID, "InterShopBom", ref hasError);
            if (hasError)
                return false;

            SEMGeneralStep toStep = ValidationHelper.CheckStep(entity.TO_PROD_ID, entity.OPER_ID, "InterShopBom", ref hasError);
            if (hasError)
                return false;

            SEMWipInfo wipInfo;
            if(InputMart.Instance.SEMWipInfo.TryGetValue(entity.LOT_ID, out wipInfo) == false)
            {
                // err : Wip없음
                // [TODO] 로그 찍기
                return false;
            }
            //SEMBOM bom;
            //if (InputMart.Instance.LotProdChangeDic.TryGetValue(entity.LOT_ID, out bom))
            //{

            //}

            #endregion

            SEMBOM bom = new SEMBOM();

            bom.FromStep = fromStep;
            bom.ToStep = toStep;

            bom.FromProduct = fromProd;

            bom.ToProduct = toProd;

            bom.ChangeType = Constants.MC; //LotProductChange는 MC 

            bom.WipInfo = wipInfo;
            bom.WipInfo.IsLotProdChange = true;
            bom.LotID = entity.LOT_ID;
            bom.IsLotProdChange = true;
            bom.Qty = (double) entity.QTY;

            InputMart.Instance.LotProdChangeDic.Add(entity.LOT_ID, bom);

            return false;
        }

        public void OnAction_LOT_PRODUCT_CHANGE(IPersistContext context)
        {
            // Multi Lot Prod Change 여부 셋팅
            foreach (var boms in InputMart.Instance.LotProdChangeDic)
            {               
                
                if (boms.Value.Count() == 1)
                {
                    // Single Lot Product Change.
                    boms.Value.First().IsMultiLotProdChange = false;
                    boms.Value.First().WipInfo.IsMultiLotProdChange = false;
                }
                else
                {
                    // Multi Lot Product Change
                    foreach (var bom in boms.Value)  
                    {
                        bom.IsMultiLotProdChange = true;
                        bom.WipInfo.IsMultiLotProdChange = true;
                    }

                    WriteLog.WriteErrorLog($"MultiLotProdChange 로직이 들어왔습니다. LOT_ID : {boms.Key}");
                }
            }
        }

        public bool OnAfterLoad_WIP_JOB_CONDITION(WIP_JOB_CONDITION entity)
        {
            if (InputMart.Instance.GlobalParameters.UseMatrialRestriction)
            {
                SEMWipInfo wip;

                #region Validation

                if (InputMart.Instance.SEMWipInfo.TryGetValue(entity.LOT_ID, out wip) == false)
                {
                    //WriteLog.WriteErrorLog($"OnAfterLoad_WIP_JOB_CONDITION : Wip이 없음 // LOT_ID = '{entity.LOT_ID}' ");
                    return false;
                }

                #endregion

                // Wip에 자재제약 정보 입력
                if (InputMart.Instance.GlobalParameters.UseMatrialRestriction)
                {
                    if (entity.CONDITION_TYPE_CODE == Constants.Powder)
                    {
                        wip.PowderCond = entity.CON_VALUE;
                    }
                    else if (entity.CONDITION_TYPE_CODE == Constants.CompositionCode)
                    {
                        wip.CompositionCode = entity.CON_VALUE;
                    }
                    else if (entity.CONDITION_TYPE_CODE == Constants.TAPING_MAT)
                    {
                        wip.FromTapingMatCond = entity.CON_VALUE;
                    }

                    wip.WipJobConditions.Add(entity.CONDITION_TYPE_CODE, entity.CON_VALUE);

                    InputMart.Instance.WipJobCondDic.Add(entity.LOT_ID, entity);
                }

            }
            return true;
        }

        public bool OnAfterLoad_PLAN_MASTER(PLAN_MASTER entity)
        {
            InputMart.Instance.SupplyChainID = entity.SUPPLYCHAIN_ID;
            InputMart.Instance.SiteID = entity.SITE_ID;
            InputMart.Instance.PlanID = entity.PLAN_ID;
            InputMart.Instance.CutOffDateTime = ConvertHelper.ConvertStringToDateTime(entity.CUTOFFDATE + entity.CUTOFFTIME);
            InputMart.Instance.EffEndDateTime = ConvertHelper.ConvertStringToDateTime(entity.EFFENDDATE + entity.CUTOFFTIME);

            var pmast = entity;
            if (pmast != null)
            {
                DateTime startTime = DateTime.ParseExact(pmast.EFFSTARTDATE + pmast.CUTOFFTIME.Substring(0, 2) + "0000", "yyyyMMddHHmmss", CultureInfo.CurrentCulture);
                DateTime endTime = DateTime.ParseExact(pmast.EFFENDDATE + "000000", "yyyyMMddHHmmss", CultureInfo.CurrentCulture);// .AddDays(1); [why add days???]
                //DateTime endTime = DateTime.ParseExact(pmast.EFFENDDATE + pmast.CUTOFFTIME, "yyyyMMddHHmmss", CultureInfo.CurrentCulture);// .AddDays(1); [why add days???]

                ModelContext.Current.StartTime = startTime;
                ModelContext.Current.EndTime = endTime;

                ModelContext.Current.Arguments["start-time"] = startTime.ToString("yyyy-MM-dd HH:mm:ss");
                ModelContext.Current.Arguments["period"] = (int)(endTime - startTime).TotalDays;

                GlobalParameters.Instance.start_time = startTime;
                GlobalParameters.Instance.period = (int)(endTime - startTime).TotalDays;

                //ModelContext.Current.QueryArgs.Add("SUPPLYCHAIN_ID", planMaster.SUPPLYCHAIN_ID);
                //ModelContext.Current.QueryArgs.Add("PLAN_ID", planMaster.PLAN_ID);
            }

            return true;
        }

        public bool OnAfterLoad_RESOURCE_JOB_CONDITION(RESOURCE_JOB_CONDITION entity)
        {
            if (InputMart.Instance.SEMEqp.ContainsKey(entity.RESOURCE_ID) == false)
                return false;

            if (entity.CONDITION_TYPE_CODE == "C000000049")
            {
                var conValue = Convert.ToDouble(entity.CON_VALUE);
                entity.CON_VALUE = (conValue / 1000).ToString();
            }

            if (InputMart.Instance.SEMJobCondition.ContainsKey(entity.CONDITION_TYPE_CODE) == false)
            {
                SEMJobCondition job = new SEMJobCondition();
                
                job.ConditionCode = entity.CONDITION_TYPE_CODE;
                job.ConditionName = entity.CONDITION_TYPE_NAME;
                job.Table = entity.ODS_TABLE;
                job.Field = entity.ODS_FIELD;

                InputMart.Instance.SEMJobCondition.Add(entity.CONDITION_TYPE_CODE, job);
            }

            return true;
        }

        public bool OnAfterLoad_SETUP_CONDITION(SETUP_CONDITION entity)
        {
            SEMEqp eqp = null;
            if (InputMart.Instance.SEMEqp.TryGetValue(entity.RESOURCE_ID, out eqp) == false)
            {
                ErrHist.WriteIf(string.Format("{0}/{1}/{2}", entity.RESOURCE_ID, entity.CONDITION_TYPE_CODE, "SETUP_CONDITION"),
                                ErrCategory.PERSIST,
                                ErrLevel.ERROR,
                                Constants.NULL_ID,
                                Constants.NULL_ID,
                                Constants.NULL_ID,
                                entity.RESOURCE_ID,
                                Constants.NULL_ID,
                                "NOT FOUND REOURCE",
                                $"Table: SETUP_CONDITION"
                                );

                return false;
            }

            if (InputMart.Instance.SEMJobCondition.ContainsKey(entity.CONDITION_TYPE_CODE) == false)
            {
                SEMJobCondition job = new SEMJobCondition();

                job.ConditionCode = entity.CONDITION_TYPE_CODE;
                job.ConditionName = entity.CONDITION_TYPE_NAME;
                job.Table = entity.ODS_TABLE;
                job.Field = entity.ODS_FIELD;

                InputMart.Instance.SEMJobCondition.Add(entity.CONDITION_TYPE_CODE, job);
            }

            return true;
        }

        public bool OnAfterLoad_LAST_JOB_CONDITION(LAST_JOB_CONDITION entity)
        {
            SEMEqp eqp = null;
            if (InputMart.Instance.SEMEqp.TryGetValue(entity.RESOURCE_ID, out eqp) == false)
            {
                ErrHist.WriteIf(string.Format("{0}/{1}/{2}", entity.RESOURCE_ID, entity.CONDITION_TYPE_CODE, "LAST_JOB_CONDITION"),
                                ErrCategory.PERSIST,
                                ErrLevel.ERROR,
                                Constants.NULL_ID,
                                Constants.NULL_ID,
                                Constants.NULL_ID,
                                entity.RESOURCE_ID,
                                Constants.NULL_ID,
                                "NOT FOUND REOURCE",
                                $"Table: LAST_JOB_CONDITION"
                                );

                return false;
            }

            // RESOURCE_JOB_CONDITION과 SETUP_CONDTION에 없는 job코드는 적용하지 않음  
            if (InputMart.Instance.SEMJobCondition.ContainsKey(entity.CONDITION_TYPE_CODE) == false)            
                return false;            

            eqp.SetJobCondition(entity);

            return false;
        }

        public void OnAction_WEIGHT_PRESET(IPersistContext context)
        {
            foreach (WEIGHT_PRESET item in InputMart.Instance.WEIGHT_PRESET.DefaultView)
            {
                SEMWeightPreset preset = WeightHelper.GetSafeWeightPreset(item.PRESET_ID);

                if (preset == null)
                    continue;


                RawWeightFactor weightFactor = null;
                if (InputMart.Instance.RawWeightFactor.TryGetValue(item.FACTOR_ID, out weightFactor) == false)
                    continue;

                if (weightFactor == null || weightFactor.IsActive == false)
                    continue;

                if (weightFactor.FactorKind == WeightFactorType.FACTOR)
                {
                    SEMWeightFactor factor = CreateHelper.CreateWeightFactor(item);
                    preset.FactorList.Add(factor);
                    preset.MapPresetID = item.PRESET_ID;
                }
                //else
                //{
                //    SEMWeightFilter filter = new SEMWeightFilter();
                //    filter.FilterID = weightFactor.FactorID;

                //    if (preset.FilterList.ContainsKey(filter.FilterID) == false)
                //        preset.FilterList.Add(filter.FilterID, filter);
                //}
            }
        }

        public bool OnAfterLoad_WEIGHT_FACTOR(WEIGHT_FACTOR entity)
        {
            if (InputMart.Instance.RawWeightFactor.ContainsKey(entity.FACTOR_ID) == true)
                return false;

            RawWeightFactor factor = new RawWeightFactor();
            factor.FactorID = entity.FACTOR_ID;
            factor.IsActive = MyHelper.ToBoolYN(entity.IS_ACTIVE);
            factor.FactorKind = WeightFactorType.FACTOR;
            factor.FactorDesc = entity.FACTOR_DESC;

            InputMart.Instance.RawWeightFactor.Add(factor.FactorID, factor);

            return false;
        }

        public bool OnAfterLoad_CYCLE_TIME(CYCLE_TIME entity)
        {
            string key = CommonHelper.CreateKey(entity.RESOURCE_ID, entity.OPER_ID, entity.PRODUCT_ID);
            InputMart.Instance.CycleTimeDic.Add(key, entity);
            return true;
        }

        public bool OnAfterLoad_LOT_ARRANGE(LOT_ARRANGE entity)
        {
            InputMart.Instance.LotArrangeDic.Add(entity.LOT_ID, entity);

            return false;
        }

        public void OnAction_LOT_ARRANGE(IPersistContext context)
        {
            foreach(var lotArrangePair in InputMart.Instance.LotArrangeDic)
            {
                string lotId = lotArrangePair.Key;
                ICollection<LOT_ARRANGE> lotArrangeList = lotArrangePair.Value;

                SEMWipInfo wip = null;
                SEMEqp eqp = null;

                if (lotArrangeList.Count == 1 && lotArrangeList.First().RESOURCE_FIXED == "Y")
                {
                    LOT_ARRANGE entity = lotArrangeList.First();

                    // RES_FIXED_LOT_ARRANGE
                    string inValidreason = string.Empty;
                    bool isValidLotArrange = ValidationHelper.IsValidResFixedLotArrange(entity, ref wip, ref eqp, ref inValidreason);

                    if(isValidLotArrange)
                    {                        
                        wip.IsResFixLotArrange = true;
                        wip.IsLotArrange = true;
                        wip.LotArrangeOperID = entity.OPER_ID;
                        wip.ResFixLotArrangeEqp = eqp;
                        wip.LotArrangedEqpDic.Add(entity.OPER_ID, eqp);

                        eqp.ResFixedWips.Add(wip);

                        // ResFixLotArrangeDic에 추가
                        if (InputMart.Instance.ResFixedLotArrangeDic.ContainsKey(lotId) == false)
                            InputMart.Instance.ResFixedLotArrangeDic.Add(lotId, eqp);
                    }
                    else
                    {
                        WriteLog.WriteErrorLog($"ResFixedLotArrange가 적용되지 않았습니다. LOT_ID:{lotId} reason : {inValidreason}");
                    }
                }
                else
                {
                    // Normal LOT_ARRNAGE
                    foreach (LOT_ARRANGE entity in lotArrangeList)
                    {
                        bool isValidLotArrange = ValidationHelper.IsValidNoramlLotArrange(entity, ref wip, ref eqp);
                        if (isValidLotArrange)
                        {
                            wip.IsLotArrange = true;
                            wip.LotArrangeOperID = entity.OPER_ID;
                            wip.LotArrangedEqpDic.Add(entity.OPER_ID, eqp);
                        }
                    }
                }
            }
        }

        public void OnAction_SETUP_CONDITION(IPersistContext context)
        {
            var eqps = InputMart.Instance.SEMEqp.Values.ToArray();

            foreach (var eqp in eqps)
            {
                // 장비에 SETUP_CONDITION 정보가 없으면 해당 장비 사용하지않음
                if (InputMart.Instance.SETUP_CONDITION.Rows.Any(x => x.RESOURCE_ID == eqp.EqpID) == false)
                {
                    eqp.HasSetupCondition = false;
                    InputMart.Instance.NoSetupCondEqpList.Add(eqp);
                    continue;
                }

                // CONDITION_TYPE_CODE 조합으로 eqp를 분류
                var conditions = InputMart.Instance.SETUP_CONDITION.Rows.Where(x => x.RESOURCE_ID == eqp.EqpID).Select(x=>x.CONDITION_TYPE_CODE).Distinct().OrderBy(x=>x);
                string key = CommonHelper.CreateKey(conditions);
                eqp.SetupGroupKey = key;
                InputMart.Instance.SetupGroupEqpMDic.Add(key, eqp);
            }
        }

        public bool OnAfterLoad_MODEL_OUTSIDE_PASTE(MODEL_OUTSIDE_PASTE entity)
        {
            if (entity.PRODUCT_ID.Contains("_"))
            {
                SEMProduct prod = null;
                if (InputMart.Instance.SEMProduct.TryGetValue(entity.PRODUCT_ID, out prod))
                {
                    entity.PASTE_GROUP = entity.PASTE_GROUP.IsNullOrEmpty() ? "" : entity.PASTE_GROUP;

                    prod.ModelOutsidePaste.Add(entity);
                }
            }
            else
            {
                var products = GetProducts(entity);

                foreach (var prod in products)
                { 
                    prod.ModelOutsidePaste.Add(entity);
                }
            }

            return false;
        }

        public List<SEMProduct> GetProducts(MODEL_OUTSIDE_PASTE entity)
        {
            List<SEMProduct> result = new List<SEMProduct>();

            foreach (var pair in InputMart.Instance.SEMProduct)
            {
                var prod = pair.Value;
                // 값이 '_'를 제외한 부분만 일치 

                char[] w = prod.ProductID.ToCharArray();
                char[] r = entity.PRODUCT_ID.ToCharArray();

                bool isMatched = true;

                for (int i = 0; i < r.Length; i++)  //r.Length까지 하는 이유 : w보다 r이 짧게 들어오는 데이터가 있음, 있는데 까지만 비교
                {
                    if (r[i] == '_')
                        continue;

                    if (w[i] != r[i])
                    {
                        isMatched = false;
                        break;
                    }
                }

                if (isMatched)
                {
                    result.Add(prod);
                }
            }

            return result;        
        }

        public bool OnAfterLoad_PLATING_RECIPE(PLATING_RECIPE entity)
        {
            if (InputMart.Instance.SEMWipInfo.TryGetValue(entity.LOT_ID, out var wip))
            {
                entity.RECIPE_ID = entity.RECIPE_ID.IsNullOrEmpty() ? "" : entity.RECIPE_ID;

                wip.PlatingRecipe.Add(entity);
                return true;
            }
            else
                return false;
        }

        public bool OnAfterLoad_TERM_FIRING_PROFILE(TERM_FIRING_PROFILE entity)
        {
            SEMProduct prod = null;
            if (InputMart.Instance.SEMProduct.TryGetValue(entity.PRODUCT_ID, out prod))
            {
                entity.PROFILE_CODE = entity.PROFILE_CODE.IsNullOrEmpty() ? "" : entity.PROFILE_CODE;

                prod.TermFiringProfile.Add(entity);
            }
            return false;
        }

        public bool OnAfterLoad_OPERATION(OPERATION entity)
        {
            CommonHelper.WriteBuildInfo();

            return true;
        }

        public bool OnAfterLoad_SORTING_JOB_CONDITION(SORTING_JOB_CONDITION entity)
        {
            SEMProduct prod = null;
            if (InputMart.Instance.SEMProduct.TryGetValue(entity.PRODUCT_ID, out prod))
            {
                entity.SORTING_PLATE = entity.SORTING_PLATE.IsNullOrEmpty() ? "" : entity.SORTING_PLATE;
                entity.LOAD_PLATE = entity.LOAD_PLATE.IsNullOrEmpty() ? "" : entity.LOAD_PLATE;
                entity.CHIP_GUIDE = entity.CHIP_GUIDE.IsNullOrEmpty() ? "" : entity.CHIP_GUIDE;

                prod.SortingJobCondition.Add(entity);
            }
         
            return true;
        }

        public bool OnAfterLoad_DEMAND(DEMAND entity)
        {
            WriteLog.WriteDemand2(entity);

            return true;
        }
    }
}