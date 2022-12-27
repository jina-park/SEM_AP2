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
using Mozart.SeePlan;
using Mozart.SeePlan.General.DataModel;
using Mozart.SeePlan.DataModel;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class CreateHelper
    {
        private static Dictionary<string, int> serialLotIDs = new Dictionary<string, int>();

        /// <summary>
        /// Returns sequencial lot id with a templated value (eg. _001) added at the end of the original lot id.
        /// </summary>
        /// <param name="lotID"></param>
        /// <param name="suffix"></param>
        /// <returns></returns>
        internal static string GetSerialLotID(string lotID, string suffix = "")
        {
            // suffix = "MLP"  MultiLotProductChange시 붙이는 코드
            // suffix = "CAP"  CapaSplit시 붙이는 코드
            // suffix = " "  ~~ 시 붙이는 코드

            int cnt = 0;
            if (serialLotIDs.TryGetValue(lotID, out cnt) == false)
                serialLotIDs.Add(lotID, 1);
            else
                cnt = serialLotIDs[lotID]++;

            return $"{lotID}_{suffix}{cnt.ToString("D3")}";            
        }

        internal static SEMWipInfo CreateWip(WIP item, SEMGeneralStep step, SEMProduct prod)
        {
            WipArea wipArea = OperationManager.GetWipArea(step.Sequence);

            SEMWipInfo wip = new SEMWipInfo();

            wip.LotID = item.LOT_ID;
            wip.Product = prod;
            wip.InitialStep = step;
            wip.Process = prod.Process;
            wip.UnitQty = (double)item.QTY;
            wip.WipState = item.STATUS;
            wip.SiteID = item.SITE_ID;
            wip.FactoryID = item.FACTORY_ID;
            wip.FloorID = item.FLOOR_ID;
            wip.CustomerID = item.CUSTOMER_ID;
            wip.CurrentState = ConvertHelper.GetEntityState(item.STATUS);
            wip.OrderNO = (double)item.ORDERNO;
            wip.DataType = item.DATA_TYPE.IsNullOrEmpty() ? string.Empty : item.DATA_TYPE;
            wip.LotClass = item.LOT_CLASS.IsNullOrEmpty() ? string.Empty : item.LOT_CLASS;

            if (item.END_CUSTOMER_CHECK == "Y")
            {
                if (item.OPER_ID == "SM9999")
                {
                    // 창고
                    List<string> customerList = WipHelper.GetEndCustomerList(item.LOT_ID);
                    if (customerList.Count() == 0)
                    {
                        // err : STOCK_CUSTOMER_CONDITION 데이터가 없음
                        WriteLog.WriteErrorLog($"STOCK_CUSTOMER_CONDITION 데이터가 없음 // LOT_ID = '{wip.LotID}' ");
                    }
                    else
                    {
                        wip.EndCustomerList = customerList;
                        wip.IsEndCustomerCheck = true;
                    }
                }
                else
                {
                    // 창고 이후 oper
                    wip.CustomerID = "0000000000";
                    wip.EndCustomerList.Add(item.CUSTOMER_ID);
                    wip.IsEndCustomerCheck = true;
                }
            }

            wip.WipEqpID = step.IsProcessing ? item.RESOURCE_ID : string.Empty;
            wip.WipRouteID = item.ROUTE_ID;
            wip.WipStepID = item.OPER_ID;
            wip.WipProcessID = item.PRODUCT_ID;
            wip.WipProductID = item.PRODUCT_ID;
            wip.WipArea = wipArea;
            wip.WipArrivedTime = ConvertHelper.ConvertStringToDateTime(item.ARRIVED_TIME);

            wip.LastTrackInTime = ConvertHelper.ConvertStringToDateTime(item.TRACK_IN_TIME);
            wip.AvailableTime = item.AVAILABLE_TIME == "00000000000000" ? InputMart.Instance.CutOffDateTime : ConvertHelper.ConvertStringToDateTime(item.AVAILABLE_TIME);
            wip.IsInitWip = true;
            wip.IsInputLot = false;
            wip.IsProdFixed = item.MODEL_FIXED == "Y" ? true : false;
            wip.IsSplitedWip = item.LOT_ID.EndsWith("_W") || item.LOT_ID.EndsWith("_R") ? false : true;
            wip.IsSmallLot = item.ROUTE_ID != "SGC123" && (double)item.QTY < prod.SmallLotQty ? true : false;
            wip.ResProductGroup = item.RES_PRODUCT_GROUP.IsNullOrEmpty() ? string.Empty : item.RES_PRODUCT_GROUP;

            wip.LotName = item.LOT_ID.Split('_').FirstOrDefault();

            wip.UrgentCode = item.URGENT_CODE == null ? string.Empty : item.URGENT_CODE;
            wip.UrgentPriority = (int)item.URGENT_PRIORITY;
            wip.IsUrgent = item.URGENT_CODE == null ? false : true;

            wip.IsReelLabeled = item.REEL_LABELING == "Y" ? true : false;

            if (wip.WipState.ToUpper() == "RUN" && wip.InitialStep.StepID == "SG4430")
                wip.IsReelLabeled = true;

            InputMart.Instance.UrgentCodeDic.Add(wip.UrgentPriority, wip);

            wip.IsReelLabeled = item.REEL_LABELING == "Y" ? true : false;
            if (wip.WipState.ToUpper() == "RUN" && wip.InitialStep.StepID == "SG4430")
                wip.IsReelLabeled = true;

            if (item.STATUS.ToUpper() == "RUN" && step.StdStep.IsProcessing)
            {
                if (InputMart.Instance.SEMEqp.TryGetValue(item.RESOURCE_ID, out var eqp))
                    eqp.RunWip = wip;
                else
                    wip.IsNoResRunLot = true;
            }

            wip.RouteCode = item.ROUTE_CODE == null ? string.Empty : item.ROUTE_CODE;

            OperationManager.SetLotOperInfo(wip);

            return wip;
        }

        internal static SEMProcess GetSafeProcess(string processID)
        {
            SEMProcess proc = BopHelper.FindProcess(processID);

            if (proc == null)
            {
                proc = CreateProcess(processID);

                InputMart.Instance.SEMProcess.Rows.Add(proc);
            }

            return proc;
        }

        internal static SEMProcess CreateProcess(string processID)
        {
            SEMProcess proc = new SEMProcess();

            proc.Key = processID;
            proc.ProcessID = processID;

            return proc;
        }

        internal static SEMPlanWip CreatePlanWip(SEMWipInfo wip)
        {
            SEMPlanWip planWip = new SEMPlanWip(wip);
            
            planWip.LotID = wip.LotID;
            planWip.Step = wip.InitialStep as SEMGeneralStep;
            planWip.MapStep = wip.InitialStep;
            planWip.QtyForPrePeg = wip.UnitQty;

            planWip.AvailableTime = wip.AvailableTime;

            return planWip;
        }

        internal static SEMPlanWip CreateSplitedPlanWip(SEMWipInfo wip, SEMBOM bom)
        {
            SEMPlanWip planWip = new SEMPlanWip(wip);

            planWip.LotID = CreateHelper.GetSerialLotID(wip.LotID, "MLP");
            planWip.Step = wip.InitialStep as SEMGeneralStep;
            planWip.MapStep = wip.InitialStep;
            planWip.AvailableTime = wip.AvailableTime;
            planWip.QtyForPrePeg = wip.UnitQty;

            planWip.Qty = bom.Qty;
            planWip.SplitQty = bom.Qty;
            planWip.SplitToProduct = bom.ToProduct;
            planWip.IsSplitLot = true;
            planWip.SplitOperID = bom.ToStepID;

            return planWip;
        }

        internal static SEMLot CreateOneDayCapaSplitLot(this SEMLot lot, double qty, string prefix, string splitReason)
        {
            SEMLot n = CreateHelper.CreateLot(lot.Wip, LotState.CREATE);
            n.LotID = GetSerialLotID(lot.Wip.LotID, prefix);
            n.Product = lot.Product;
            n.CurrentCustomerID = lot.CurrentCustomerID;
            n.FactoryID = lot.FactoryID;
            n.FloorID = lot.FloorID;

            n.UnitQtyDouble = qty;
            n.OriginalLot = lot;
            n.IsMultiLotSplit = lot.IsMultiLotSplit;
            n.IsCapaSplitLot = false;
            n.SplitReason = splitReason;
            n.IsSplitRecorded = false;
            //n.LotState = LotState.CREATE;


            n.PlanSteps = lot.PlanSteps;
            n.DemandProdID = lot.DemandProdID;
            n.InflowLot = lot.InflowLot;
            n.QTimeInfo = lot.QTimeInfo;
            n.LogDetail = lot.LogDetail;
            n.FactoryID = lot.FactoryID;
            n.FloorID = lot.FloorID;
            n.OriginalLot = lot.OriginalLot;
            n.SplitReason = lot.SplitReason;
            n.IsSplitRecorded = lot.IsSplitRecorded;
            n.IsMCChanged = lot.IsMCChanged;
            n.IsWRChanged = lot.IsWRChanged;
            n.IsWCChanged = lot.IsWCChanged;
            n.IsBTChanged = lot.IsBTChanged;
            n.PlanWip = lot.PlanWip;
            n.FromProductID = lot.FromProductID;
            n.FromCustomerID = lot.FromCustomerID;
            n.PreEndTime = lot.PreEndTime;
            n.CurrentCustomerID = lot.CurrentCustomerID;

            SEMPlanInfo newPlan = new SEMPlanInfo();
            newPlan.ProductID = lot.Product.ProductID;
            newPlan.ProcessID = lot.Product.Process.ProcessID;
            newPlan.LotID = lot.LotID;
            newPlan.UnitQty = lot.UnitQtyDouble;
            newPlan.Init(lot.CurrentStep);
            n.SetCurrentPlan(newPlan);

            return n;
        }
        internal static SEMLot CreateMultiProdSplitLot(this SEMLot lot, SEMPlanWip planWip, string prefix, string splitReason)
        {
            SEMLot n = CreateHelper.CreateLot(lot.Wip, LotState.CREATE);
            n.PlanWip = planWip;
            n.LotID = GetSerialLotID(lot.LotID, prefix);
            n.UnitQtyDouble = planWip.PegWipInfo.FlowQty;
            n.OriginalLot = lot;
            n.IsMultiLotSplit = true;
            n.IsCapaSplitLot = false;
            n.SplitReason = splitReason;
            n.IsSplitRecorded = false;

            n.PlanSteps = lot.PlanSteps;
            n.DemandProdID = lot.DemandProdID;
            n.InflowLot = lot.InflowLot;
            n.QTimeInfo = lot.QTimeInfo;
            n.LogDetail = lot.LogDetail;
            n.FactoryID = lot.FactoryID;
            n.FloorID = lot.FloorID;
            n.OriginalLot = lot.OriginalLot;
            n.SplitReason = lot.SplitReason;
            n.IsSplitRecorded = lot.IsSplitRecorded;
            n.IsMCChanged = lot.IsMCChanged;
            n.IsWRChanged = lot.IsWRChanged;
            n.IsWCChanged = lot.IsWCChanged;
            n.IsBTChanged = lot.IsBTChanged;
            n.PlanWip = lot.PlanWip;
            n.FromProductID = lot.FromProductID;
            n.FromCustomerID = lot.FromCustomerID;
            n.PreEndTime = lot.PreEndTime;
            n.CurrentCustomerID = lot.CurrentCustomerID;

            SEMPlanInfo newPlan = new SEMPlanInfo();
            newPlan.ProductID = lot.Product.ProductID;
            newPlan.ProcessID = lot.Product.Process.ProcessID;
            newPlan.LotID = lot.LotID;
            newPlan.UnitQty = lot.UnitQtyDouble;
            newPlan.Init(lot.CurrentStep);
            n.SetCurrentPlan(newPlan);

            return n;
        }

        internal static SEMLot CreateLot(SEMWipInfo wip, LotState state, bool isDummy = false)
        {
            SEMLot lot = new SEMLot(wip);
            lot.PlanWip = wip.PlanWip;
            lot.WipInfo = wip;
            lot.LotID = wip.LotID;
            lot.Product = wip.Product;
            lot.UnitQty = (int)wip.UnitQty;
            lot.UnitQtyDouble = wip.UnitQty;
            lot.LineID = wip.SiteID;
            lot.FactoryID = wip.FactoryID;
            lot.FloorID = wip.FloorID;

            lot.CurrentCustomerID = wip.CustomerID;
            lot.CurrentState = wip.CurrentState;
            lot.LotState = state;

            lot.PlanSteps = new List<string>();
            lot.PreEndTime = lot.Wip.WipArrivedTime;

            lot.FromProductID = string.Empty;
            lot.FromCustomerID = string.Empty;

            if (lot.Wip.IsMultiLotProdChange && state ==LotState.WIP)
            {
                SEMPlanWip planWip = lot.Wip.SplitPlanWips.Where(x => x.IsPegged == true).FirstOrDefault();

                if (planWip == null)
                {
                    WriteLog.WriteErrorLog("MultiProductChange된 SplitLot의 pegging정보를 찾을 수 없습니다.");                        
                }
                else
                {
                    lot.Wip.PlanWip = planWip;
                    lot.PlanWip = planWip;
                }
            }

            return lot;
        }

        public static SEMWipInfo CreateWipInfo(SEMProduct product, double qty)
        {
            //jobchange 메뉴얼
            var proc = product.Process as SEMProcess;
            if (proc == null)
                return null;

            var wip = new SEMWipInfo();
            //wip.LineID = product.LineID;
            wip.LotID = CreateLotID(product.ProductID);
            wip.CurrentState = EntityState.WAIT;
            wip.Product = product;
            wip.Process = proc;
            wip.InitialStep = proc.FirstStep as SEMGeneralStep;
            wip.UnitQty = qty;

            return wip;
        }
        public static string CreateLotID(string prod)
        {
            //jobchange 메뉴얼
            if (InputMart.Instance.ProductMaxLotID.ContainsKey(prod) == false)
                InputMart.Instance.ProductMaxLotID.Add(prod, 0);

            InputMart.Instance.ProductMaxLotID[prod]++;
            string lotID = string.Format("L{0}{1}", prod,
                            InputMart.Instance.ProductMaxLotID[prod].ToString("0000"));

            return lotID;
        }

        internal static SEMProduct CreateProduct(PRODUCT item, SEMProcess proc)
        {
            SEMProduct prod = new SEMProduct(item.PRODUCT_ID, proc);

            //prod.FactoryId = item.SITE_ID;
            prod.Key = item.PRODUCT_ID;
            prod.ChipSize = item.CHIP_SIZE;
            prod.Capacity = item.CAPACITY;
            prod.TapingType = item.TAPING_TYPE;
            prod.Thickness = item.THICKNESS;
            prod.Devation= item.DEVATION;
            prod.Voltage = item.VOLTAGE;
            prod.PlatingMethod = item.PLATING_METHOD;
            prod.Special = item.SPECIAL;
            prod.PackingMinQty = Convert.ToInt32(item.PACKING_MIN_QTY);
            prod.SmallLotQty = (double)item.SMALL_LOT_QTY;
            prod.ChipQty = (double)item.CHIP_QTY;
            prod.Pitch = item.PITCH.ToString();
            prod.HorizontalVertical = item.HORIZONTAL_VERTICAL;
            prod.CarrierType = item.CARRIER_TYPE == null ? string.Empty : item.CARRIER_TYPE;
            prod.ReelInch = item.REEL_INCH == null ? string.Empty : item.REEL_INCH;
            prod.TapingType = item.TAPING_TYPE;
            prod.Special = item.SPECIAL;
            prod.Voltage = item.VOLTAGE;
            prod.Devation = item.DEVATION;
            prod.Temperature = item.TEMPERATURE;
            prod.ProductGroup = item.PRODUCT_GROUP;
            prod.ThicknessValue = item.THICKNESS_VALUE;
            prod.ThicknessCodeValue = item.THICKNESS_CODE_VALUE;
            prod.FP_I_PRODUCT = item;
            return prod;
        }

        //[todo]
        //internal static void AddAltProd(SEMProduct prod, SEMProduct changeProd, SEMGeneralStep changeStep, List<SEMProduct> checkProds)
        //{
        //    checkProds.Add(changeProd);

        //    foreach (SEMGeneralStep step in prod.Process.Steps)
        //    {
        //        if (changeStep.StdStep.StepSeq < step.StdStep.StepSeq)
        //            break;

        //        step.AltProductList.Add(changeProd);
        //    }

        //    if (changeProd.HasPrevInterBom)
        //    {
        //        foreach (var info in changeProd.PrevInterBoms)
        //        {
        //            //changeStep = info.ChangeStep;

        //            //foreach (FabStep step in prod.Process.Steps)
        //            //{
        //            //	if (changeStep.StdStep.StepSeq < step.StdStep.StepSeq)
        //            //		break;

        //            //	step.AltProductList.Add(changeProd);
        //            //}


        //            if (checkProds.Contains(info.ChangeProduct))
        //                continue;

        //            AddAltProd(prod, info.ChangeProduct, info.ChangeStep, checkProds);
        //        }
        //    }
        //}

        internal static SEMGeneralStep CreateStep(PRODUCT_OPER item, SEMStdStep stdStep)
        {
            SEMGeneralStep step = new SEMGeneralStep(item.OPER_ID);
            step.StdStep = stdStep;
            step.StepType = stdStep.StepType;
            step.Sequence = Convert.ToInt32(item.SEQUENCE); //BopBuilder.InitializeRoute 에서 다시 설정됨
            step.SEMRouteID = item.ROUTE_ID;
            step.ProductID = item.PRODUCT_ID;
            return step;
        }

        internal static SEMGeneralStep CreateBTDummyStep(PRODUCT_OPER item)
        {
            SEMStdStep stdStep = BopHelper.FindStdStep("SG6094_BT");

            SEMGeneralStep step = new SEMGeneralStep("SG6094_BT");
            step.StdStep = stdStep;
            step.StepType = stdStep.StepType;
            step.Sequence = 41001500;
            step.SEMRouteID = stdStep.RouteID;
            step.ProductID = item.PRODUCT_ID;
            step.Yield = 1;
            step.TAT = 0;
            return step;
        }

        internal static SEMStdStep CreateStdStep(OPERATION item)
        {
            SEMStdStep stdStep = new SEMStdStep();

            stdStep.Key = item.OPER_ID;

            stdStep.RouteID = item.ROUTE_ID;
            stdStep.RouteName = item.ROUTE_NAME;
            stdStep.RouteNameE = item.ROUTE_NAME_E;
            
            stdStep.OperID = item.OPER_ID;
            stdStep.OperName = item.OPER_NAME;
            stdStep.OperNameE = item.OPER_NAME_E;

            stdStep.StepType = item.OPER_TYPE;
            stdStep.StepSeq = Convert.ToInt32(item.SEQUENCE);

            stdStep.IsProcessing = item.AP_PROCESSING_TYPE == "PROCESSING" ? true : false;
            stdStep.IsLotSplit = item.LOT_SPLIT == "Y" ? true : false;

            stdStep.CTOperID = item.CT_OPER_ID.IsNullOrEmpty() ? "" : item.CT_OPER_ID;

            return stdStep;
        }

        internal static SEMStdStep CreateBTStdStep()
        {
            SEMStdStep btStdStep = new SEMStdStep();

            btStdStep.Key = "SG6094_BT";

            btStdStep.RouteID = "SGC123";
            btStdStep.RouteName = "SmallLotStock";
            btStdStep.RouteNameE = "SmallLotStock";

            btStdStep.OperID = "SG6094_BT";
            btStdStep.OperName = "SmallLotStock";
            btStdStep.OperNameE = "SmallLotStock";

            btStdStep.StepType = "T";
            btStdStep.StepSeq = 410015;

            btStdStep.IsProcessing = false;

            return btStdStep;
        }

        public static void LinkStdStep(List<SEMStdStep> list)
        {
            SEMStdStep prev = null;
            for (int i = 0; i < list.Count; i++)
            {
                SEMStdStep step = list[i];

                if (i == 0)
                    step.IsInputStep = true;

                if (prev != null)
                    step.PrevStep = prev;

                prev = step;

                if (i + 1 < list.Count)
                    step.NexStep = list[i + 1];
            }
        }

        public static void AddProcessMap(SEMProduct prod, SEMProcess proc)
        {
            if (prod == null)
                return;

            if (proc == null || proc.Steps.Count == 0)
                return;

            var maps = InputMart.Instance.ProcessMaps;

            string productID = prod.ProductID;

            foreach (SEMGeneralStep step in proc.Steps)
            {
                string stepID = step.StepID;

                string key = CommonHelper.CreateKey(productID, stepID);
                if (maps.ContainsKey(key) == false)
                {
                    maps.Add(key, proc);
                }
            }
        }

        public static void AddAltProd(SEMProduct prod, SEMProduct changeProd, SEMGeneralStep changeStep, List<SEMProduct> checkProds)
        {
            //checkProds.Add(changeProd);

            //foreach (SEMGeneralStep step in prod.Process.Steps)
            //{
            //    if (changeStep.StdStep.StepSeq < step.StdStep.StepSeq)
            //        break;

            //    step.AltProductList.Add(changeProd);
            //}

            //if (changeProd.HasPrevBom)
            //{
            //    foreach (var info in changeProd.PrevBoms)
            //    {

            //        if (checkProds.Contains(info.ChangeProduct))
            //            continue;

            //        AddAltProd(prod, info.ChangeProduct, info.ChangeStep, checkProds);
            //    }
            //}
        }

        internal static StepTime CreateStepTime(CYCLE_TIME ct, SEMGeneralStep step)
        {
            StepTime st = new StepTime();

            st.EqpID = ct.RESOURCE_ID;
            st.Step = step;
            st.ProductID = ct.PRODUCT_ID;
            if (ct.TACT_TIME_UOM == "SEC")
                st.TactTime = (float)ct.TACT_TIME / 60;
            else if (ct.TACT_TIME_UOM == "MIN")
                st.TactTime = (float)ct.TACT_TIME;
            else if (ct.TACT_TIME_UOM == "HOUR")
                st.TactTime = (float)ct.TACT_TIME * 60;
            else if (ct.TACT_TIME_UOM == "DAY")
                st.TactTime = (float)ct.TACT_TIME * 60 * 24;

            st.ProcTime = st.TactTime;

            return st;
        }

        internal static SEMStepTat CreateStepTat(LEAD_TIME item, double runTat, bool isMainLine)
        {
            SEMStepTat tat = new SEMStepTat();

            tat.ProductID = item.PRODUCT_ID;
            tat.OperID = item.OPER_ID;
            //tat.TAT = runTat;  //단위 : 분(Minute)
            tat.IsMain = isMainLine;
            if (item.TAT_UOM == "SEC")
            {
                tat.Tat = TimeSpan.FromSeconds(item.TAT);
                tat.TAT = item.TAT / 60;
            }
            else if (item.TAT_UOM == "MIN")
            {
                tat.Tat = TimeSpan.FromMinutes(item.TAT);
                tat.TAT = item.TAT;
            }
            else if (item.TAT_UOM == "HOUR")
            {
                tat.Tat = TimeSpan.FromHours(item.TAT);
                tat.TAT = item.TAT * 60;
            }
            else if (item.TAT_UOM == "DAY")
            {
                tat.Tat = TimeSpan.FromDays(item.TAT);
                tat.TAT = item.TAT * 60 * 24;
            }
            else
            {
                //에러메세지
            }
            return tat;
        }

        internal static void AddStepTat(this SEMGeneralStep step, SEMStepTat tat)
        {
            string key = step.StepID; // 박진아 : key가 의미없지만 임시로 설정

            if (step.StepTats.ContainsKey(key) == false)
                step.StepTats.Add(key, tat);
        }

        internal static void AddCurrentPlan(this SEMGeneralPegPart pp, SEMProduct prod, SEMGeneralStep step)
        {
            if (prod == null || step == null)
            {
                //TODO : Write Error
                return;
            }

            PlanStep plan = new PlanStep();
            plan.Product = prod;
            plan.Step = step;

            AddCurrentPlan(pp, plan);
        }

        internal static void AddCurrentPlan(SEMGeneralPegPart pp, PlanStep plan)
        {
            if (pp.Steps == null)
                pp.Steps = new List<PlanStep>();

            pp.Steps.Add(plan);
        }

        internal static SEMGeneralPegTarget CreateSemPegTarget(SEMGeneralPegPart pp, SEMGeneralMoPlan mo)
        {
            SEMGeneralPegTarget pt = new SEMGeneralPegTarget(pp, mo);
            // pt.TargetKey = mo.TargetKey;
            pt.Product = (SEMProduct)pp.Product;

            return pt;
        }

        internal static ConditionGroup CreateConditionGroup(string key, string stepID)
        {
            ConditionGroup obj = new ConditionGroup();
            obj.Key = key;
            obj.StepID = stepID;

            return obj;
        }


        internal static ProfileInfo CreateProfileInfo(ProfileEqp eqp, InflowLot lot, DateTime inTime, DateTime outTime, string status)
        {
            ProfileInfo obj = new ProfileInfo();
            obj.Equipment = eqp;
            obj.Lot = lot;
            obj.InTime = inTime;
            obj.OutTime = outTime;
            obj.Status = status;

            return obj;
        }

        internal static ProfileEqp CreateProfileEqp(SEMAoEquipment aeqp)
        {
            ProfileEqp obj = new ProfileEqp();
            obj.Equipment = aeqp;

            return obj;
        }

        public static SEMYield CreateYield(PRODUCT_OPER item)
        {
            SEMYield y = new SEMYield();
            y.ProductID = item.PRODUCT_ID;
            y.OperID = item.OPER_ID;

            if (item.YIELD == null || item.YIELD <= 0 || item.YIELD > 1)
                y.Yield = 1d;
            else
                y.Yield = (double)item.YIELD;

            return y;
        }
        public static SEMYield CraeteLotOperYield(LOT_OPER item)
        {
            SEMYield y = new SEMYield();
            y.ProductID = item.PRODUCT_ID;
            y.OperID = item.OPER_ID;
            y.Yield = 1;    // Default 값 설정 파라미터 추가해서 받아오도록 수정 필요

            return y;
        }
        public static SEMGeneralMoPlan CreateMoPlan(SEMGeneralMoMaster mm, DEMAND item, DateTime dtDue)
        {
            SEMGeneralMoPlan mo = new SEMGeneralMoPlan(mm, (float)item.QTY, dtDue);

            mo.DueDate = dtDue;
            mo.Qty = Convert.ToDouble(item.QTY);
            mo.Priority = item.PRIORITY;
            mo.WeekNo = CommonHelper.GetWeekNo(CommonHelper.StringToDateTime(item.DUE_DATE, true));
            mo.LineType = CommonHelper.ToEnum<LineType>("", LineType.MAIN);

            mo.MoMaster = mm;
            mo.DemandID = item.DEMAND_ID;

            mo.TapingModel = item.TAPING_MODEL;
            mo.BulkModel = item.BULK_MODEL;
            mo.TapingCustomerID = item.TAPING_CUSTOMER_ID;
            mo.BulkWeek = item.BULK_WEEK;
            mo.TapingWeek = item.TAPING_WEEK;
            mo.PreBuild = item.PRE_BUILD;
            mo.LastBuild = item.LATE_BUILD;

            // mo.TargetKey = PegHelper.CreateTargetKey(mo.Priority.ToString(), string.Empty);

            return mo;
        }

        public static SEMEqp CreateEqp(RESOURCE item)
        {
            SEMEqp eqp = new SEMEqp();
            eqp.EqpID = item.RESOURCE_ID;
            eqp.ResID = item.RESOURCE_ID;
            eqp.ResGroup = item.OPER_ID;
            eqp.Utilization = (float)item.UTILIZATION;
            eqp.LineID = item.SITE_ID;
            eqp.IsValid = MyHelper.ToBoolYN(item.VALID);
            eqp.FactoryID = item.FACTORY_ID;
            eqp.FloorID = item.FLOOR_ID;

            eqp.OperIDs.Add(item.OPER_ID);

            eqp.ResourceGroup = item.RESOURCE_GROUP;
            eqp.ResourceMES = item.RESOURCE_MES;
            eqp.ResProductGroup = item.RES_PRODUCT_GROUP.IsNullOrEmpty() ? string.Empty : item.RES_PRODUCT_GROUP;
            eqp.WORK_AREA = item.WORK_AREA;

            eqp.SimType = Mozart.SeePlan.DataModel.SimEqpType.Table;
            eqp.DispatchingRule = DispatcherType.WeightSorted.ToString();
            eqp.DispatcherType = DispatcherType.WeightSorted;

            if (item.PRESET_ID.IsNullOrEmpty())
            {
                if (item.OPER_ID == "SG3910")
                    eqp.PresetID = "PRSET_3910";
                else if (item.OPER_ID == "SG4140")
                    eqp.PresetID = "PRSET_4140";
                else if (item.OPER_ID == "SG5170")
                    eqp.PresetID = "PRSET_5170";
                else if (item.OPER_ID == "SG4430")
                    eqp.PresetID = "PRSET_4430";
                else
                    eqp.PresetID = "DEFAULT";
            }
            else
                eqp.PresetID = item.PRESET_ID;

            var preset = WeightHelper.GetSafeWeightPreset(eqp.PresetID);
            preset.MapPresetID = eqp.PresetID;
            eqp.Preset = preset;

            return eqp;
        }

        public static SEMPegWipInfo CreatePegWipInfo(SEMPlanWip planWip, string targetStepID)
        {
            SEMPegWipInfo obj = new SEMPegWipInfo();

            obj.PlanWip = planWip;
            obj.WipInfo = planWip.WipInfo;
            obj.TargetOperID = targetStepID;
            obj.SiteID = InputMart.Instance.SiteID;

            obj.BranchSeq = 0;
            
            obj.FlowProduct = planWip.Wip.Product as SEMProduct;
            obj.FlowCustomerID = planWip.WipInfo.CustomerID;
            obj.FlowStep = planWip.WipInfo.InitialStep as SEMGeneralStep;
            obj.FlowStepID = planWip.WipInfo.InitialStep.StepID;
            obj.FlowRouteId = (planWip.WipInfo.InitialStep as SEMGeneralStep).SEMRouteID;

            obj.DemKey = PrePegHelper.CreateDgKey(obj);

            obj.CalcAvailDate = planWip.AvailableTime;            

            obj.TotalTat = 0d;
            obj.FlowTat = 0d; 
            obj.CalcTat = 0d;

            //double yield = OperationManager.GetYield(obj.FlowProductID, obj.FlowStepID);
            obj.AccumYield = 1d;
            obj.FlowYield = 1d;
            obj.CalcYield = 1d;
            obj.FlowQty = planWip.WipInfo.UnitQty;

            obj.IsTargetOper = obj.FlowStepID == obj.TargetOperID;
            obj.IsLotProdChange = planWip.WipInfo.IsLotProdChange;
            obj.IsMultiLotProdChange = planWip.WipInfo.IsMultiLotProdChange;
            obj.IsLotOper = planWip.WipInfo.IsLotOper;
            
            if (obj.WipInfo.IsProdFixed)
            {
                obj.IsProdFixed = true;

                if (obj.WipInfo.WipArea == WipArea.BulkArea)
                    obj.MC_IsChangedProd = true;
            }

            return obj;
        }



        public static SEMWeightFactor CreateWeightFactor(WEIGHT_PRESET item)
        {
            var factorType = MyHelper.ToEnum<FactorType>("LOTTYPE", FactorType.LOTTYPE);
            var order = MyHelper.ToEnum<OrderType>(item.ORDER_TYPE, OrderType.ASC);

            bool isAllow = MyHelper.ToBoolYN(item.ALLOW_FILTER);

            var wfactor = new SEMWeightFactor(item.FACTOR_ID, item.FACTOR_WEIGHT, item.SEQUENCE, factorType, order, item.CRITERIA, isAllow);

            return wfactor;
        }

        public static JobConditionGroup CreateJobContionGroup(string key)
        {
            JobConditionGroup jGroup = new JobConditionGroup();
                        
            jGroup.Key = key;

            return jGroup;        
        }
    }
}