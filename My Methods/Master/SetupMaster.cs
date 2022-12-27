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
using System.Text;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class SetupMaster
    {
        #region Obsolete
#if false
        private static SETUP_CONDITION createEqpJobCond(SETUP_CONDITION setupCond, string toCondValue)
        {
            SETUP_CONDITION s = new SETUP_CONDITION();
            s.CONDITION_TYPE_CODE = setupCond.CONDITION_TYPE_CODE;
            s.FROM_CON_VALUE = toCondValue;
            s.ODS_FIELD = setupCond.ODS_FIELD;
            s.ODS_TABLE = setupCond.ODS_TABLE;
            s.WILD_CODE = setupCond.WILD_CODE;
            s.SET_TIME = setupCond.SET_TIME;
            return s;
        }
        internal static bool IsSameProd(SEMAoEquipment aeqp, SEMLot lot)
        {
        	if (aeqp.LastPlan != null)
        	{
        		if ((aeqp.LastPlan as SEMPlanInfo).ProductID == lot.CurrentProductID)
        			return true;
        	}

        	return false;
        }
        public static float GetSetupTime(AoEquipment aeqp, string shopID, string stepID, string productID, string prodVer, string productionType, bool markingAcid = false)
        {
        	float totalSetupTime = 0;
        	SEMAoEquipment eqp = aeqp.ToSEMAoEquipment();

        	SEMProduct eqpProduct = eqp.LastPlan != null ? (eqp.LastPlan as SEMPlanInfo).Product : null;
        	SEMProduct lotProduct = BopHelper.FindProduct(productID);

        	if (eqpProduct == null || lotProduct == null)
        		return totalSetupTime;

        	if (stepID == Constants.SG3520)
        	{
        		if (eqpProduct.ChipSize != lotProduct.ChipSize)
        			totalSetupTime = 30;
        	}
        	else if (stepID == Constants.SG3810)
        	{
        		if (eqpProduct.Capacity != lotProduct.Capacity)
        			totalSetupTime = 30;
        	}
        	else if (stepID == Constants.SG3910)
        	{
        		if (eqpProduct.Thickness != lotProduct.Thickness)
        			totalSetupTime = 30;
        	}

        	//SetupInfo info = CreateHelper.CreateSetupInfo(eqp, shopID, stepID, productID, prodVer, productionType);

        	//string eqpGroup = eqp.TargetEqp.EqpGroup;
        	//float setupTime = GetSetupTime(eqpGroup, info);
        	//float acidChangeTime = AcidMaster.GetAcidChangeTime(eqp, stepID, productID);
        	//float totalSetupTime = setupTime + acidChangeTime;

        	////용액교체 Setup 발생시 EqpPlan 기록을 위해 표시
        	//if (markingAcid && acidChangeTime > 0)
        	//	AcidMaster.SetSetupMark(eqp, true);

        	return totalSetupTime;
        }
#endif
        #endregion

        public static double GetSetupTime(this SEMEqp eqp, SEMLot lot)
        {
            double retVal = 0;

            string key = lot.CurrentStepID + eqp.EqpID;
            ICollection<double> setTimes = null;
            if (lot.StepJobSetupTimes.TryGetValue(key, out setTimes) == false)
            {
                bool isNeedSetup = IsNeedSetup(eqp, lot, true);
                if (isNeedSetup == true && lot.StepJobSetupTimes.TryGetValue(key, out setTimes) == false)
                    throw new Exception($"No setup time information has been set for lot {lot.LotID}");
            }

            if (setTimes == null)
                retVal = 0;
            else
                retVal = setTimes.Max();
            
            return retVal;
        }

        public static double GetSetupTimeForJCAgent(this SEMEqp eqp, SEMLot lot)
        {
            double retVal = 0;
            string key = lot.CurrentStepID + eqp.EqpID;
            ICollection<double> setTimes = null;
            if (lot.StepJobSetupTimes.TryGetValue(key, out setTimes) == false)
            {
                bool isNeedSetup = IsNeedSetup(eqp, lot, true);
                if (isNeedSetup == true && lot.StepJobSetupTimes.TryGetValue(key, out setTimes) == false)
                    throw new Exception($"No setup time information has been set for lot {lot.LotID}");
            }

            if (setTimes == null)
            {
                retVal = 0;
            }
            else
            {
                retVal = setTimes.Max();
            }

            lot.StepJobSetupTimes.Remove(key);

            return retVal;
        }

        public static bool IsNeedSetup(this SEMEqp eqp, SEMLot lot, bool forceSearchJobConds = false, bool isWriteLog = false)
        {            
            bool retVal = false;

            retVal = isNeedSetup(eqp, lot, forceSearchJobConds, isWriteLog);

            #region Unpegged Run Lots never incurrs setup
            // For the first time a RUN lot is dispatched to a resource, no setup is needed
            // Otherwise, comment this 'if statement'
            if (lot.Wip.CurrentState == EntityState.RUN && lot.CurrentStepID == lot.Wip.InitialStep.StepID)
                return false;
            #endregion


            return retVal;
        }
        private static bool isNeedSetup(SEMEqp eqp, SEMLot lot, bool forceSearchJobConds, bool isWriteLog)
        {

            bool retVal = false;

            var setupConds = InputMart.Instance.SETUP_CONDITIONResourceView.FindRows(eqp.EqpID);
            if (setupConds == null && setupConds.Count() == 0)  // No specific setup conditions, then, no setup is needed
                return false;

            if (forceSearchJobConds == false)
                if (eqp.StepJobConditions == null || eqp.StepJobConditions.Count == 0) // No resource last conditions, then, no setup is needed
                    return false;


            // 기존 기록 초기화
            if (isWriteLog)
            {
                if (lot.StepJobConditions.ContainsKey(lot.CurrentStepID))
                    lot.StepJobConditions[lot.CurrentStepID].Clear();

                if (lot.StepJobCondLogs.ContainsKey(lot.CurrentStepID))
                    lot.StepJobCondLogs[lot.CurrentStepID].Clear();

                lot.IsOnlyModelChange = false;
            }

            List<string> conTypeCodes = null;
            List<string> conAndVals = null;

            if (eqp.IsNeedSetupForConditions(lot, setupConds, isWriteLog, out conTypeCodes, out conAndVals) == true)
                retVal = true;

            return retVal;
        }

        #region Setup Condition Compliance Check
        private static bool IsNeedSetupForConditions(this SEMEqp eqp, SEMLot lot, IEnumerable<SETUP_CONDITION> conds, bool isWriteLog, out List<string> conTypeCodes, out List<string> conAndVals, bool shouldMatchAll = false)
        {
            conTypeCodes = new List<string>();
            conAndVals = new List<string>();

            bool isNeedSetup = false;

            List<string> resultList = new List<string>();

            var condTypeCodeGroups = conds.GroupBy(x => x.CONDITION_TYPE_CODE);
            foreach (var condTypeCodeGroup in condTypeCodeGroups)
            { 
                if (IsNeedSetupForCondGroup(eqp, lot, condTypeCodeGroup, isWriteLog))
                {
                    conTypeCodes.Add(condTypeCodeGroup.Key);

                    isNeedSetup = true;

                    resultList.Add(condTypeCodeGroup.Key);
                }
            }

            if (isWriteLog)
                lot.IsOnlyModelChange = IsOnlyModelChange(resultList);


            return isNeedSetup;
        }

        private static bool IsOnlyModelChange(List<string> resultList)
        {
            if (resultList.Count() == 1 && resultList[0] == "C000000001")
                return true;

            return false;
        }

        private static bool IsNeedSetupForCondGroup(SEMEqp eqp, SEMLot lot, IEnumerable<SETUP_CONDITION> condTypeCodeGroup, bool isWriteLog)
        {
            List<double> setTimes = new List<double>();

            int complianceCnt = 0;
            foreach (var cond in condTypeCodeGroup)
            {                
                if (IsNeedSetupForCondition(eqp, lot, cond, isWriteLog) == true)
                {
                    complianceCnt++;
                }
            }

            if (complianceCnt == 0)
            {
                return false;
            }
            else
            {
                // conAndVal = "";
                return true;
            }
        }

        private static bool IsNeedSetupForCondition(SEMEqp eqp, SEMLot lot, SETUP_CONDITION cond, bool isWriteLog)
        {
            object eqpPropVal = eqp.GetPropValueForSetup(cond);
            object lotPropVal = lot.GetPropValueForSetup(cond);

            bool isNeedSetup = IsNeedSetupForCondition(cond, eqpPropVal, lotPropVal, out string a);

            string conditionLog = GetConditionLog(cond, eqpPropVal, lotPropVal);

            lot.AddConditionInfo(eqp, cond, a, conditionLog, isWriteLog, isNeedSetup);
            
            return isNeedSetup;
        }
        #endregion

        #region Get Property Value
        private static object GetPropValueForSetup(this SEMLot lot, SETUP_CONDITION setupCond)
        {
            string propName = setupCond.ODS_FIELD;

            object foundValue = null;
            if (setupCond.ODS_TABLE == "FP_I_PRODUCT")
            {
                var product = lot.SEMProduct;
                if (product == null)
                    return "";

                foundValue = product.FP_I_PRODUCT.GetPropValue(propName);
            }
            else if (setupCond.ODS_TABLE == "FP_I_WIP_JOB_CONDITION")
            {
                string conditionTypecode = setupCond.CONDITION_TYPE_CODE;
                if (lot.Wip.WipJobConditions.TryGetValue(conditionTypecode, out foundValue) == false)
                    foundValue = string.Empty;
            }
            else if (setupCond.ODS_TABLE == "FP_I_TAPING_MATERIAL_ALT")
            {
                foundValue = lot.GetTAPING_MATERIAL_ALT().GetPropValue(propName);
            }
            else if (setupCond.ODS_TABLE.IsNullOrEmpty())
            {
                if (setupCond.CONDITION_TYPE_CODE == "C000000002") // Current Operation
                {
                    foundValue = lot.CurrentStepID;
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000020") // Sorting Test Wheel
                { 
                    var product = lot.SEMProduct;
                    if (InputMart.Instance.SEMEqp.TryGetValue(setupCond.RESOURCE_ID, out var eqp))
                    {
                        List<string> testWheel = product.SortingJobCondition.Where(x => x.OPER_ID == lot.CurrentStepID && x.RES_GROUP_ID == eqp.ResourceGroup).Select(x => x.TEST_WHEEL).ToList();
                        foundValue = testWheel;
                    }
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000022") // TERM Firing Profile
                {
                    var product = lot.SEMProduct;
                    List<string> profileCode = product.TermFiringProfile.Where(x => x.RESOURCE_ID == setupCond.RESOURCE_ID).Select(x => x.PROFILE_CODE).ToList(); //FirstOrDefault??????????????????????????
                    foundValue = profileCode;
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000050") // Sorting Load Plate
                {
                    var product = lot.SEMProduct;
                    if (InputMart.Instance.SEMEqp.TryGetValue(setupCond.RESOURCE_ID, out var eqp))
                    {
                        List<string> testWheel = product.SortingJobCondition.Where(x => x.OPER_ID == lot.CurrentStepID && x.RES_GROUP_ID == eqp.ResourceGroup).Select(x => x.LOAD_PLATE).ToList();
                        foundValue = testWheel;
                    }
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000051") // Sorting Plate
                {
                    var product = lot.SEMProduct;
                    if (InputMart.Instance.SEMEqp.TryGetValue(setupCond.RESOURCE_ID, out var eqp))
                    {
                        List<string> sortingPlate = product.SortingJobCondition.Where(x => x.OPER_ID == lot.CurrentStepID && x.RES_GROUP_ID == eqp.ResourceGroup).Select(x => x.SORTING_PLATE).ToList();
                        foundValue = sortingPlate;
                    }
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000052") // Sorting Chip Guide
                {
                    var product = lot.SEMProduct;
                    if (InputMart.Instance.SEMEqp.TryGetValue(setupCond.RESOURCE_ID, out var eqp))
                    {
                        List<string> testWheel = product.SortingJobCondition.Where(x => x.RES_GROUP_ID == eqp.ResourceGroup).Select(x => x.CHIP_GUIDE).ToList();
                        foundValue = testWheel;
                    }
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000063") // Paste Group
                {
                    var product = lot.SEMProduct;
                    if (InputMart.Instance.SEMEqp.TryGetValue(setupCond.RESOURCE_ID, out var eqp))
                    {
                        List<string> modelOutsidePaste = product.ModelOutsidePaste.Where(x => x.OPER_ID == lot.CurrentStepID && x.RES_GROUP_ID == eqp.ResourceGroup).Select(x => x.PASTE_GROUP).ToList();
                        foundValue = modelOutsidePaste;
                    }
                }
                else if (setupCond.CONDITION_TYPE_CODE == "C000000066") // Planting Recipe ID
                {
                    List<string> platingRecipe = lot.Wip.PlatingRecipe.Where(x => x.PRODUCT_ID == lot.CurrentProductID && x.OPER_ID == lot.CurrentStepID).Select(x => x.RECIPE_ID).ToList();
                    foundValue = platingRecipe;
                }
                else
                {
                    WriteLog.WriteErrorLog($"CONDITION_TYPE_CODE 정보를 가져올 수 없습니다.{setupCond.CONDITION_TYPE_CODE} {setupCond.CONDITION_TYPE_NAME}");

                    foundValue = string.Empty;
                    //throw new Exception("Cannot determine which object (or table) to refer to.");
                }
            }
            else
            {
                WriteLog.WriteErrorLog($"Table 정보를 가져올 수 없습니다.{setupCond.ODS_TABLE}");
            }

            if (foundValue == null)
                foundValue = string.Empty;

            return foundValue;
        }

        private static object GetPropValueForSetup(this SEMEqp eqp, SETUP_CONDITION setupCond)
        {
            string eqpPropVal = "";
            if (eqp.StepJobConditions.TryGetValue(setupCond.CONDITION_TYPE_CODE, out eqpPropVal) == false)
                return string.Empty;

            return eqpPropVal;
        }

        private static bool IsNeedSetupForCondition(SETUP_CONDITION cond, object eqpPropVal, object lotPropVal, out string selectedLotValue)
        {
            bool result = false;
            selectedLotValue = string.Empty;

            if (lotPropVal is string)
            {
                string lotValue = lotPropVal as string;
                string eqpValue = eqpPropVal as string;

                selectedLotValue = lotValue;
                result = IsNeedSetupForCondition(cond, eqpValue, lotValue);

            }
            else if (lotPropVal is List<string>)
            {
                var lotPropValList = lotPropVal as List<string>;

                foreach (var val in lotPropValList)
                {
                    string lotValue = val;
                    string eqpValue = eqpPropVal as string;

                    if (IsNeedSetupForCondition(cond, eqpValue, lotValue))
                    {
                        selectedLotValue = lotValue;
                        result = true;
                        break;
                    }
                }

                if (selectedLotValue.IsNullOrEmpty())
                    selectedLotValue = lotPropValList.ListToString();
            }

            return result;
        }

        private static string GetConditionLog(SETUP_CONDITION cond, object eqpPropVal, object lotPropVal)
        {
            string conVal = string.Empty;

            if (lotPropVal is string)
            {
                string lotValue = lotPropVal as string;
                string eqpValue = eqpPropVal as string;

                conVal = $"{cond.CONDITION_TYPE_NAME}:EQP={eqpValue}→LOT={lotValue}";

            }
            else if (lotPropVal is List<string>)
            {
                var lotPropValList = lotPropVal as List<string>;
                conVal = $"{cond.CONDITION_TYPE_NAME}:EQP={eqpPropVal}→LOT={lotPropValList.ListToString()}";
            }
            else 
            {
                conVal = $"{cond.CONDITION_TYPE_NAME}:EQP={eqpPropVal}→LOT=Error";
            }

            return conVal;
        }

        private static void AddConditionInfo(this SEMLot lot, SEMEqp eqp, SETUP_CONDITION cond, string lotPropVal, string conVal, bool isWriteLog, bool isNeedSetup)
        {
            if (isWriteLog)
            {
                // Setup 여부 계산은 CONDITION_TYPE_CODE 사용
                if (lot.StepJobConditions.ContainsKey(lot.CurrentStepID, cond.CONDITION_TYPE_CODE) == false)
                {
                    lot.StepJobConditions.Add(lot.CurrentStepID, cond.CONDITION_TYPE_CODE, lotPropVal.ToString());
                }

                // Log는 CONDITION_TYPE_NAME으로 작성
                if (cond.CONDITION_TYPE_NAME != null) // [TODO] 에러방지를 위한 임시 문구, 해결해야함
                {
                    if (lot.StepJobCondLogs.ContainsKey(lot.CurrentStepID, cond.CONDITION_TYPE_NAME) == false)
                    {
                        lot.StepJobCondLogs.Add(lot.CurrentStepID, cond.CONDITION_TYPE_NAME, conVal);
                    }
                    else
                    {
                        if (isNeedSetup == true)
                            lot.StepJobCondLogs[lot.CurrentStepID, cond.CONDITION_TYPE_NAME] = conVal;
                    }
                }
            }

            // setup을 진행하거나 장비에 작업조건이 없는경우 lot에 작업조건을 저장함, lot에 저장된 작업조건은 이후 eqp에 저장됨
            if (isNeedSetup == true)
            {
                lot.StepJobSetupTimes.Add(lot.CurrentStepID + eqp.EqpID, cond.SET_TIME);
            }
        }

        private static bool IsNeedSetupForCondition(SETUP_CONDITION cond, string eqpPropVal, string lotPropVal)
        {
            bool retVal;

            if (eqpPropVal.ToString() == string.Empty)  // 설비의 마지막 조건이 없는 것으로 간주하여 셋업
                retVal = true;
            else if (lotPropVal.ToString() == eqpPropVal.ToString())   // 투입 될 재공의 조건과 현재 설비의 조건이 같으므로 셋업 X
                retVal = false;
            else if (cond.FROM_CON_VALUE == "ALL" && cond.TO_CON_VALUE == "ALL")     // All → All
            {
                if (lotPropVal.ToString() == eqpPropVal.ToString())     // 설비 마지막 조건 & 투입재공 조건이 같으므로 셋업 X
                    retVal = false;
                else
                    retVal = true;
            }
            else if (cond.FROM_CON_VALUE == "ALL" && cond.TO_CON_VALUE != "ALL")    // All → Some
            {
                if (lotPropVal.ToString() == cond.TO_CON_VALUE)     // 설비 마지막 조건 상관 없이 투입재공 조건이 달라지므로 셋업 O
                    retVal = true;
                else
                    retVal = false;
            }
            else if (cond.FROM_CON_VALUE != "ALL" && cond.FROM_CON_VALUE == "ALL")  // Some → All
            {
                if (eqpPropVal.ToString() == cond.FROM_CON_VALUE)   // 설비의 특정 마지막 조건에서 투입재공 조건 상관 업싱 셋업 O
                    retVal = true;                                    // 설비 마지막 == 투입재공인 경우는 위에서 이미 셋업 X로 종료
                else
                    retVal = false;
            }
            else                                                                    // Some → Some
            {
                if (cond.FROM_CON_VALUE == eqpPropVal.ToString() && cond.TO_CON_VALUE == lotPropVal.ToString())
                    retVal = true;
                else
                    retVal = false;
            }

            return retVal;
        }

        private static SEMProduct getProduct(this SEMLot lot)
        {
            SEMProduct prod = null;
            if (InputMart.Instance.SEMProduct.TryGetValue(lot.CurrentProductID, out prod) == false)
                return null;

            return prod;
        }
        #endregion

        #region Set/Update Eqp Job Condition
        public static void SetJobCondition(this SEMEqp eqp, LAST_JOB_CONDITION lastCond)
        {
            eqp.StepJobConditions.Add(lastCond.CONDITION_TYPE_CODE, lastCond.CON_VALUE);

            return;

            //SETUP_CONDITION s = null;
            //if (eqp.LastSetupCondition.TryGetValue(lastCond.CONDITION_TYPE_CODE, out s) == false)
            //    eqp.LastSetupCondition.Add(lastCond.CONDITION_TYPE_CODE, s = new SETUP_CONDITION());

            //s.CONDITION_TYPE_CODE = lastCond.CONDITION_TYPE_CODE;
            //s.FROM_CON_VALUE = lastCond.CON_VALUE;
            //s.ODS_FIELD = lastCond.CONDITION_TYPE_NAME;
        }

        public static void UpdateJobCondition(this SEMEqp eqp, SEMLot lot)
        {
            if (lot.StepJobConditions != null && lot.StepJobConditions.Count > 0 && lot.StepJobConditions.ContainsKey(lot.CurrentStepID) == true)
                eqp.StepJobConditions = lot.StepJobConditions[lot.CurrentStepID];

            return;

            //string key = CommonHelper.CreateKey(eqp.EqpID, lot.LotID, lot.CurrentStepID);
            //ICollection<SETUP_CONDITION> setupConds = null;
            //if (InputMart.Instance.EqpJobConditionDic.TryGetValue(key, out setupConds) == false)
            //{
            //    eqp.LastSetupCondition = null;
            //    return;
            //}

            //Dictionary<string, SETUP_CONDITION> lotSetupConds = new Dictionary<string, SETUP_CONDITION>();
            //foreach (var setupCond in setupConds)
            //{
            //    if (lotSetupConds.ContainsKey(setupCond.CONDITION_TYPE_CODE) == false)
            //        lotSetupConds.Add(setupCond.CONDITION_TYPE_CODE, setupCond);
            //}

            //if (eqp.LastSetupCondition == null)
            //{
            //    eqp.LastSetupCondition = lotSetupConds;
            //    return;
            //}

            //int eqpLastCondsCnt = eqp.LastSetupCondition == null ? 0 : eqp.LastSetupCondition.Count;
            //int updateCondsCnt = lotSetupConds == null ? 0 : lotSetupConds.Count;

            //List<string> str = new List<string>();
            //var condsToLoop = eqpLastCondsCnt > updateCondsCnt ? eqp.LastSetupCondition : lotSetupConds;
            //foreach (var cond in condsToLoop)
            //{
            //    string conTypeCode = cond.Key;

            //    SETUP_CONDITION sEqp = null;
            //    eqp.LastSetupCondition.TryGetValue(conTypeCode, out sEqp);
            //    string fromValue = sEqp == null ? "" : sEqp.FROM_CON_VALUE;

            //    SETUP_CONDITION sUpd = null;
            //    lotSetupConds.TryGetValue(conTypeCode, out sUpd);
            //    string toValue = sUpd == null ? "" : sUpd.FROM_CON_VALUE;

            //    // Update the eqp job condition to a new value
            //    if (sEqp != null && sUpd != null)
            //        sEqp = sUpd;

            //    // For logging regardless of the from/to value is null or not
            //    str.Add($"{conTypeCode} : (FROM = {fromValue} -> TO = {toValue})"); //
            //}

            //// If the eqp job condition should be set to a whole new set
            //// Is it neccessary to remove the remaining conditions sets?
            //// If it is, then next code should be activated
            //// Otherwise, keep it commented
            ////eqp.LastSetupCondition = updateCondsDic;

            //SETUP_LOG s = new SETUP_LOG();
            //s.REOURCE_ID = eqp.EqpID;
            //s.LOT_ID = lot.LotID;
            //s.START_TIME = AoFactory.Current.NowDT;
            //s.SETUP_TIME = GetSetupTime(eqp, lot);
            //s.END_TIME = s.START_TIME.AddMinutes(s.SETUP_TIME);
            //s.OPER_ID = lot.CurrentStepID;
            //s.JOB_CONDITIONS = str.ListToString("\t/\t");
            //s.PRODUCT_ID = lot.CurrentProductID;

            //OutputMart.Instance.SETUP_LOG.Add(s);
        }
        #endregion

        #region Logs

        public static SETUP_LOG WriteSetupLog(SEMEqp eqp, SEMLot lot)
        {
            string key = lot.CurrentStepID + eqp.EqpID;
            SETUP_LOG sl = null;
            if (lot.StepJobSetupLogs.TryGetValue(key, out sl) == false)
            {
                double setTime = lot.StepJobSetupTimes[key].Max();
                InputMart.Instance.ResFixedLotArrangeDic.TryGetValue(lot.LotID, out SEMEqp dicEqp);
                string arrType = eqp == dicEqp ? "ResFixLotArrange" : "Normal";
                string jobcondition;
                string conandvalue;
                //
                //if (lot.StepJobCondLogs.ContainsKey(lot.CurrentStepID, cond.CONDITION_TYPE_NAME) 
                if (lot.StepJobCondLogs.ContainsKey(lot.CurrentStepID) == false)
                {
                    jobcondition = "";
                    conandvalue = "";
                } 
                else
                {
                    var orderedLogs = lot.StepJobCondLogs[lot.CurrentStepID].OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value);
                    jobcondition = orderedLogs.Keys.ListToString(", ");
                    conandvalue = orderedLogs.Values.ListToString(", ");
                }

                sl = new SETUP_LOG()
                {
                    LOT_ID = lot.LotID,
                    PRODUCT_ID = lot.CurrentProductID,
                    REOURCE_ID = eqp.EqpID,
                    START_TIME = AoFactory.Current.NowDT,
                    END_TIME = AoFactory.Current.NowDT.AddMinutes(setTime),
                    JOB_CONDITIONS = jobcondition,
                    CON_AND_VALUE = conandvalue,
                    OPER_ID = lot.CurrentStepID,
                    SETUP_TIME = setTime,
                    ARRANGE_TYPE = arrType
                };
                
                lot.StepJobSetupLogs.Add(key, sl);
            }

            OutputMart.Instance.SETUP_LOG.Add(sl);

            return sl;
        }
        #endregion

        public static string GetJobConditionFieldName(string conditionCode)
        {
            SEMJobCondition job;
            if(InputMart.Instance.SEMJobCondition.TryGetValue(conditionCode, out job))
            {
                if (job.ConditionName != null)
                    return job.ConditionName;
                else
                    return job.ConditionCode;               
            }
            else
            {
                WriteLog.WriteErrorLog($"알 수 없는 job코드 입니다. JobCode:{conditionCode}");
                return "Unknown";
            }            
        }
    }
}