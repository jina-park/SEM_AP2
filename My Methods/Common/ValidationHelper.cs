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

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class ValidationHelper
        {
        public static SEMProduct CheckProduct(string productID, string where, ref bool hasError)
        {
            SEMProduct prod;
            InputMart.Instance.SEMProduct.TryGetValue(productID, out prod);
            if (prod == null)
            {
                hasError = true;

                ErrHist.WriteIf(where + productID,
                    ErrCategory.PERSIST,
                    ErrLevel.WARNING,
                    Constants.NULL_ID,
                    productID,
                    Constants.NULL_ID,
                    Constants.NULL_ID,
                    Constants.NULL_ID,
                    "NOT FOUND PRODUCT",
                    string.Format("Table:{0}", where)
                    );
            }

            return prod;
        }

        public static string GetReason_Product(string productID)
        {
            bool hasProduct = InputMart.Instance.PRODUCT.Rows.Where(x => x.PRODUCT_ID == productID).Count() > 0 ? true : false;
            if (hasProduct == false)
                return "NOT_FOUND_PRODUCT";

            bool hasProdOper = InputMart.Instance.PRODUCT_OPER.Rows.Where(x => x.PRODUCT_ID == productID).Count() > 0 ? true : false;
            if (hasProdOper == false)
                return "NOT_FOUND_PRODUCT_PROD_OPER";

            return "UNKNOWN_REASON_PRODUCT";
        }

        public static SEMProduct FindProduct(string productID)
        {
            SEMProduct prod;
            InputMart.Instance.SEMProduct.TryGetValue(productID, out prod);

            return prod;
        }

        public static SEMGeneralStep CheckStep(string processID, string stepID, string where, ref bool hasError)
        {
            SEMGeneralStep step = BopHelper.FindStep(processID, stepID);

            if (step == null)
            {
                hasError = true;

                ErrHist.WriteIf(where + processID + stepID,
                   ErrCategory.PERSIST,
                   ErrLevel.WARNING,
                   Constants.NULL_ID,
                   Constants.NULL_ID,
                   processID,
                   Constants.NULL_ID,
                   stepID,
                   "NOT FOUND STEP",
                   string.Format("Table: {0}", where)
                   );
            }

            return step;
        }

        public static SEMProcess CheckProcess(string siteId, string productId, string where, ref bool hasError)
        {
            SEMProcess process = BopHelper.FindProcess(productId);

            if (process == null)
            {
                hasError = true;

                ErrHist.WriteIf(where + productId,
                   ErrCategory.PERSIST,
                   ErrLevel.WARNING,
                   Constants.NULL_ID,
                   productId,
                   productId,
                   Constants.NULL_ID,
                   Constants.NULL_ID,
                   "NOT FOUND PROCESS",
                   string.Format("Table:{0}", where)
                   );
            }

            return process;
        }

        public static void CheckWip(WIP item, ref SEMProduct prod, ref SEMGeneralStep step, ref bool hasError)
        {
            // Product Validation
            string productID = item.PRODUCT_ID;
            prod = CheckProduct(productID, "Wip", ref hasError);
            if (hasError)
            {
                string category = "MASTER_DATA";
                string reason = GetReason_Product(item.PRODUCT_ID);
                string detailCode = "";
                string detailData = $"PRODUCT_ID : {item.PRODUCT_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.PRODUCT_ID, item, ErrLevel.ERROR, "NOT_FOUND_PRODUCT", "Table:Wip");

                return;
            }

            // 다른 Area WIP, AP3는 SGC121 이후 대공정만 취급함
            if (InputMart.Instance.RouteDic.Values.Contains(item.ROUTE_ID) == false)
            {
                hasError = true;

                string category = "ETC";
                string reason = "OTHER_AREA_LOT";
                string detailCode = "";
                string detailData = $"WIP_ROUTE_ID : {item.ROUTE_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                return;
            }

            //// Small Lot Qty
            // 주석 : Small Lot Qty 로직 변경
            //if (item.ROUTE_ID != "SGC123" && (double)item.QTY < prod.SmallLotQty)
            //{
            //    hasError = true;

            //    string category = "ETC";
            //    string reason = "SMALL_LOT_QTY";
            //    string detailCode = "";
            //    string detailData = $"WIP_QTY({(double)item.QTY}) > PRODUCT_SMALL_LOT_QTY({prod.SmallLotQty.ToString()})";

            //    WriteLog.WriteUnpegHistory(item, category, reason, detailCode, detailData);
            //    WriteLog.WritePegValidationWip_UNPEG(item, reason);

            //    ErrHist.WriteLoadWipError(item.OPER_ID, item, ErrLevel.ERROR, "SMALL_LOT_QTY", "Table:Wip");

            //    InputMart.Instance.SmallLotList.Add(item.LOT_ID);

            //    return;
            //}

            // Process Validation
            string processID = prod.SEMProcess.ProcessID;
            if (processID != item.PRODUCT_ID)
            {
                hasError = true;
                //SubStep의 Process 변화 확인

                string category = "MASTER_DATA";
                string reason = "NOT_FOUND_PROCESS";
                string detailCode = "";
                string detailData = $"PRODUCT_ID : {item.PRODUCT_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                string key = string.Format("Wip_ProcCheck{0}", item.LOT_ID);
                ErrHist.WriteLoadWipError(key, item, ErrLevel.INFO, "NOT_FOUND_PROCESS", string.Format("{0} → {1}", item.PRODUCT_ID, processID));

                return;
            }

            // ProdOper & LotOper Validation
            string stepID = item.OPER_ID;
            SEMLotOper lo = InputMart.Instance.SEMLotOperView.FindRows(item.LOT_ID).FirstOrDefault();
            if (lo == null)
            {
                // ProdOper 체크
                step = CheckStep(processID, stepID, "Wip", ref hasError);
                if (hasError)
                {
                    string category = "MASTER_DATA";
                    string reason = "NOT_FOUND_PROD_OPER";
                    string detailCode = "";
                    string detailData = $"OPER_ID : {item.OPER_ID}";

                    WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                    WriteLog.WritePegValidationWip_UNPEG(item, reason);

                    ErrHist.WriteLoadWipError(item.OPER_ID, item, ErrLevel.ERROR, "NOT_FOUND_PROD_OPER", "Table:Wip");
                    return;
                }
            }
            else
            {
                // LotOper체크
                step = lo.Steps.Where(x => x.StepID == item.OPER_ID).FirstOrDefault();
                if (step == null)
                {
                    hasError = true;

                    string category = "MASTER_DATA";
                    string reason = "NOT_FOUND_LOT_OPER";
                    string detailCode = "";
                    string detailData = $"OPER_ID : {item.OPER_ID}";

                    WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                    WriteLog.WritePegValidationWip_UNPEG(item, reason);

                    ErrHist.WriteLoadWipError(item.OPER_ID, item, ErrLevel.ERROR, "NOT_FOUND_LOT_OPER", "Table:Wip");
                    return;
                }
            }

            // Wip 중복 체크
            if (InputMart.Instance.SEMWipInfo.ContainsKey(item.LOT_ID))
            {
                hasError = true;

                string category = "MASTER_DATA";
                string reason = "DUPLICATION_LOT_ID";
                string detailCode = "";
                string detailData = $"LOT_ID : {item.LOT_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "DUPLICATION_LOT_ID", "Table:Wip");

                return;
            }

            // EndCustomerCheck
            if (item.OPER_ID == Constants.SM9999 && item.END_CUSTOMER_CHECK == "Y" && WipHelper.GetEndCustomerList(item.LOT_ID).Count == 0)
            {
                hasError = true;

                string category = "MASTER_DATA";
                string reason = "NO_END_CUSTOMER_ID";
                string detailCode = "";
                string detailData = $"LOT_ID : {item.LOT_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "NO_END_CUSTOMER_ID", "Table:Wip");

                return;
            }

            if (item.STATUS.IsNullOrEmpty())
            {
                hasError = true;

                string category = "MASTER_DATA";
                string reason = "STATUS_IS_EMPTY";
                string detailCode = "";
                string detailData = $"LOT_ID : {item.LOT_ID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "STATUS_IS_EMPTY", "Table:Wip");
         
                return;
            }

            // Resource Validation
            if (item.STATUS.ToUpper() == "RUN")
            {
                if (step.StdStep.IsProcessing == false)
                {
                    item.STATUS = "WAIT";
                    item.RESOURCE_ID = string.Empty;
                    //hasError = true;

                    //string category = "MASTER_DATA";
                    //string reason = "DUMMY_OPER_RUNNING";
                    //string detailCode = "";
                    //string detailData = $"LOT_ID : {item.LOT_ID}";

                    //WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                    //WriteLog.WritePegValidationWip_UNPEG(item, reason);

                    //ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "DUMMY_OPER_RUNNING", "Table:Wip");
                    return;
                }

                if (item.RESOURCE_ID.IsNullOrEmpty())
                {
                    //hasError = true;

                    //string category = "MASTER_DATA";
                    //string reason = "EMPTY_RESOURCE_ID";
                    //string detailCode = "";
                    //string detailData = $"LOT_ID : {item.LOT_ID}";

                    //WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                    //WriteLog.WritePegValidationWip_UNPEG(item, reason);

                    //ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "EMPTY_RESOURCE_ID", "Table:Wip");

                    //return;
                }

                SEMEqp eqp;
                if (InputMart.Instance.SEMEqp.TryGetValue(item.RESOURCE_ID, out eqp) == false)
                {
                    //hasError = true;

                    //string category = "MASTER_DATA";
                    //string reason = "NO_RESOURCE";
                    //string detailCode = "";
                    //string detailData = $"LOT_ID : {item.LOT_ID}";

                    //WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                    //WriteLog.WritePegValidationWip_UNPEG(item, reason);

                    //ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "NO_RESOURCE", "Table:Wip");

                    //return;
                }
            }

            // LotOper에 대응되는 Prod Oper 존재 여부
            bool isInvalidLotOper = InputMart.Instance.InvalidLotOper_NotFoundRouteID.TryGetValue(item.LOT_ID, out string routeID);
            if (isInvalidLotOper)
            {
                hasError = true;

                string category = "MASTER_DATA";
                string reason = "INVALID_LOT_OPER";
                string detailCode = "NOT_FOUND_PROD_OPER";
                string detailData = $"PROD_OPER has no RouteID : {routeID}";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "INVALID_LOT_OPER", "Table:Wip");

                return;
            }

            // LotOper에 Demand Oper 존재 
            isInvalidLotOper = InputMart.Instance.InvalidLotOper_NoDemandOper.Contains(item.LOT_ID);
            if (isInvalidLotOper)
            {
                hasError = true;

                string category = "MASTER_DATA";
                string reason = "INVALID_LOT_OPER";
                string detailCode = "NO_FOUND_DEMAND_OPER";
                string detailData = $"LOT_OPER has no Demand Oper(SG3890)";

                WriteLog.WriteUnpegHistory(item, prod, category, reason, detailCode, detailData);
                WriteLog.WritePegValidationWip_UNPEG(item, reason);

                ErrHist.WriteLoadWipError(item.LOT_ID, item, ErrLevel.ERROR, "INVALID_LOT_OPER", "Table:Wip");

                return;
            }
        }

        public static bool IsValidBom(BOM item, ref SEMProduct fromProd, ref SEMProduct toProd, ref SEMGeneralStep fromStep, ref SEMGeneralStep toStep)
        {
            bool isValidBom = true;
            bool hasError = false;
            string reason = string.Empty;

            fromProd = ValidationHelper.CheckProduct(item.FROM_PROD_ID, "OnAction_BOM", ref hasError);
            if (hasError)
            {
                isValidBom = false;
                hasError = false;
                string prodReason = ValidationHelper.GetReason_Product(item.FROM_PROD_ID);
                if (prodReason == "NOT_FOUND_PRODUCT")
                    reason += "NO_FROM_PRODUCT / ";
                else if (prodReason == "NOT_FOUND_PRODUCT_PROD_OPER")
                    reason += "NO_FROM_PRODUCT_OPER / ";
                else if (prodReason == "NO_SORTING_PLATE")
                    reason += "NO_FROM_PRODUCT_SORTING_PLATE / ";

                if (InputMart.Instance.GlobalParameters.ApplyWriteDetailBOMLog == false)
                {
                    WriteLog.WriteBomVaildation(item, false, reason);
                    return false;
                }
            }

            toProd = ValidationHelper.CheckProduct(item.TO_PROD_ID, "OnAction_BOM", ref hasError);
            if (hasError)
            {
                isValidBom = false;
                hasError = false;
                string prodReason = ValidationHelper.GetReason_Product(item.TO_PROD_ID);
                if (prodReason == "NOT_FOUND_PRODUCT")
                    reason += "NO_TO_PRODUCT / ";
                else if (prodReason == "NOT_FOUND_PRODUCT_PROD_OPER")
                    reason += "NO_TO_PRODUCT_OPER / ";
                else if(prodReason == "NO_SORTING_PLATE")
                    reason += "NO_TO_PRODUCT_SORTING_PLATE / ";

                if (InputMart.Instance.GlobalParameters.ApplyWriteDetailBOMLog == false)
                {
                    WriteLog.WriteBomVaildation(item, false, reason);
                    return false;
                }
            }

            fromStep = ValidationHelper.CheckStep(item.FROM_PROD_ID, item.OPER_ID, "OnAction_BOM", ref hasError);
            if (hasError)
            {
                isValidBom = false;
                hasError = false;
                reason += "INVALID_FROM_OPER / ";

                if (InputMart.Instance.GlobalParameters.ApplyWriteDetailBOMLog == false)
                {
                    WriteLog.WriteBomVaildation(item, false, reason);
                    return false;
                }
            }

            toStep = ValidationHelper.CheckStep(item.TO_PROD_ID, item.OPER_ID, "OnAction_BOM", ref hasError);
            if (hasError)
            {
                isValidBom = false;
                hasError = false;
                reason += "INVALID_TO_OPER / ";

                if (InputMart.Instance.GlobalParameters.ApplyWriteDetailBOMLog == false)
                {
                    WriteLog.WriteBomVaildation(item, false, reason);
                    return false;
                }
            }

            if (isValidBom == false)
            {
                WriteLog.WriteBomVaildation(item, false, reason);
                return false;
            }

            return true;
        }

        public static bool IsValidResFixedLotArrange(LOT_ARRANGE entity, ref SEMWipInfo wip, ref SEMEqp eqp, ref string inValidReason)
        {
            // Wip이 없는경우
            if (wip == null && InputMart.Instance.SEMWipInfo.TryGetValue(entity.LOT_ID, out wip) == false)
            {
                if (InputMart.Instance.SmallLotList.Contains(entity.LOT_ID))
                    inValidReason = $"Small Lot Qty Filtered Lot / LOT_ID : {entity.LOT_ID}";
                else
                    inValidReason = $"Not Fount Lot / LOT_ID : {entity.LOT_ID}";

                ArrangeHelper2.WriteArrangeLog(entity, wip, $"Resource Fixed Lot Arrange", inValidReason);
                return false;
            }

            // Wip의 InitialStep과 LotArrange 대상 Oper가 다른 경우
            SEMGeneralStep step = wip.InitialSEMStep;
            if (step.StepID != entity.OPER_ID)
            {
                //inValidReason = $"Invalid OPER_ID : {entity.OPER_ID} is not InitialOper";
                //ArrangeHelper2.WriteArrangeLog(entity, wip, $"Resource Fixed Lot Arrange", inValidReason);
                //return false;
            }

            // LotArrange 대상 Oper가 ProcessingOper가 아닌 경우
            if (step.StdStep.IsProcessing == false)
            {
                inValidReason = $"Invalid OPER_ID : {entity.OPER_ID} is DummyOperation";
                // 해당 로그는 고객요청으로 작성하지 않음 
                //ArrangeHelper2.WriteArrangeLog(entity,  wip, $"Only Lot Arrange - ResoureFixed = {isResFixed}", inValidReason);
                return false;
            }

            // LotArrange 대상 장비를 찾을 수 없는 경우
            if (InputMart.Instance.SEMEqp.TryGetValue(entity.RESOURCE_ID, out eqp) == false)
            {
                inValidReason = $"Invalid RESOURCE_ID : {entity.RESOURCE_ID}";
                ArrangeHelper2.WriteArrangeLog(entity, wip, $"Resource Fixed Lot Arrange", inValidReason);
                return false;
            }

            // 주석 : ResFixLotArrange는 CycleTime이 없어도 defaultCycleTime값으로 투입
            //if (step.HasCycleTime(eqp.EqpID) == false)
            //{
            //    ArrangeHelper2.WriteArrangeLog(entity, "Normal Lot Arrange", $"No Cycle Time");
            //    return false;
            //}


            // 주석 : ResFixLotArrange는 SETUP_CONDITION정보를 찾을 수 없어도 투입
            // 장비의 SETUP_CONDITION정보를 찾을 수 없는 경우
            //if(InputMart.Instance.NoSetupCondEqpList.Contains(eqp))
            //{
            //    inValidReason = $"No Setup Condition";
            //    ArrangeHelper2.WriteArrangeLog(entity, $"ResoureFixed Lot Arrange", inValidReason);
            //    return false;
            //}

            return true;
        }

        public static bool IsValidNoramlLotArrange(LOT_ARRANGE entity, ref SEMWipInfo wip, ref SEMEqp eqp)
        {
            string inValidReason = string.Empty;

            // Wip이 없는경우
            if (wip == null && InputMart.Instance.SEMWipInfo.TryGetValue(entity.LOT_ID, out wip) == false)
            {
                if (InputMart.Instance.SmallLotList.Contains(entity.LOT_ID))
                    inValidReason = $"Small Lot Qty Filtered Lot";
                else if (InputMart.Instance.InvalidLotOper_NotFoundRouteID.TryGetValue(entity.LOT_ID, out string routeID))
                    inValidReason = $"Invalid Lot Oper Filtered Lot/ PROD_OPER has no {routeID}";
                else if (InputMart.Instance.InvalidLotOper_NoDemandOper.Contains(entity.LOT_ID))
                    inValidReason = $"Invalid Lot Oper Filtered Lot / Not Found Demand Oper(SG3890)";
                else
                    inValidReason = $"Not Fount Lot / LOT_ID : {entity.LOT_ID}";

                ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", inValidReason);
                return false;
            }

            // Wip의 InitialStep과 LotArrange 대상 Oper가 다른 경우
            //SEMGeneralStep step = wip.InitialSEMStep;
            //if (step.StepID != entity.OPER_ID)
            //{
            //    ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", $"Invalid OPER_ID : {entity.OPER_ID} is not InitialOper");
            //    return false;
            //}

            // LotArrange 대상 Oper가 ProcessingOper가 아닌 경우
            SEMStdStep stdStep = InputMart.Instance.SEMStdStep.Rows.Where(x => x.OperID == entity.OPER_ID).FirstOrDefault();
            if (stdStep == null)
            {
                ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", $"Invalid OPER_ID : {entity.OPER_ID}");
                return false;
            }

            if (stdStep.IsProcessing == false)
            {
                // 해당 로그는 고객요청으로 작성하지 않음 
                //ArrangeHelper2.WriteArrangeLog(entity, wip,  "Normal Lot Arrange", $"Invalid OPER_ID : {entity.OPER_ID} is DummyOperation");
                return false;
            }

            // LotArrange 대상 장비를 찾을 수 없는 경우
            if (InputMart.Instance.SEMEqp.TryGetValue(entity.RESOURCE_ID, out eqp) == false)
            {
                ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", $"Invalid RESOURCE_ID : {entity.RESOURCE_ID}");
                return false;
            }

            // 장비의 SETUP_CONDITION정보를 찾을 수 없는 경우
            if (InputMart.Instance.NoSetupCondEqpList.Contains(eqp))
            {
                inValidReason = $"No Setup Condition";
                ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", inValidReason);
                return false;
            }

            // 주석 : arrange 계산시에 출력하기로..
            // CycleTime이 없는 경우
            //if (step.HasCycleTime(eqp.EqpID) == false)
            //{
            //    ArrangeHelper2.WriteArrangeLog(entity, wip, "Normal Lot Arrange", $"No Cycle Time");
            //    return false;
            //}

            return true;
        }
    }
}