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
using System.Text;
using Mozart.SeePlan.Simulation;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class ArrangeHelper2
    {
        public static MultiDictionary<string, string> arrangeDic = new MultiDictionary<string, string>();
        public static Dictionary<string, string> finalFilterReasonDic = new Dictionary<string, string>();

        // NoArrange Reason
        public const string operHasNoCycleTime = " Has No Cycle Time ";
        public const string noSetupCondition = "No Setup Condition";
        public const string noCycleTime = "No Cycle Time";
        public const string operarionIsNotFound = "OPERATION is not Found";
        public const string operIsNotProcessing = "OPERATION is not Processing";
        public const string allConditionBlocked = "All Condition Sets blocked";
        public const string UnUsable = "불가";

        public static List<string> GetArrange(this SEMLot lot, string stepID, string callingModule = "SIM")
        {
            var result = lot.GetCachedArrange(stepID, callingModule);

            if (lot.HasSameLot())
            {
                foreach (var l in lot.SameLots)
                {
                    bool isCheckArrangeOfSameLot = lot.Wip.IsPushWip() == false;
                    if (isCheckArrangeOfSameLot == false)
                        continue;

                    var arrangeOfSameLot = l.GetCachedArrange(stepID);

                    for (int i = result.Count - 1; i >= 0 ; i--)
                    {
                        if (arrangeOfSameLot.Contains(result[i]) == false)
                            result.RemoveAt(i);
                    }
                }
            }

            return result;
        }

        private static List<string> GetCachedArrange(this SEMLot lot, string stepID, string callingModule = "SIM")
        {
            string noArrangeReason = string.Empty;

            var result = new List<string>();

            var product = lot.GetProduct(stepID);
            if (product == null)
                return result;

            string productID = product.ProductID;

            if (lot.PlanWip != null && lot.PlanWip.PegWipInfo != null)
                result = GetArrange(lot.PlanWip.PegWipInfo, stepID, productID, ref noArrangeReason, callingModule);
            else if (lot.PlanWip != null)  // [TODO] pwi없는 GetArrange만들기, FirstOrDefault를 쓰면 안됨
                result = GetArrange(lot.PlanWip.SEMPegWipInfos.FirstOrDefault(), stepID, productID, ref noArrangeReason, callingModule);
            else
                result = new List<string>(); // [TODO] pwi없는 GetArrange만들기

            return result;
        }

        public static List<string> GetArrange(this SEMPegWipInfo pwi, string stepID, string productID, ref string noArrangeReason, string callingModule = "PEG")
        {
            try
            {
                #region Init

                arrangeDic.Clear();
                finalFilterReasonDic.Clear();
                InputMart.Instance.ArrLogDic.Clear();

                #endregion

                #region Validation
                if (pwi == null)
                {
                    noArrangeReason = $"Error01";
                    //pwi.PlanWip.NoArrnageReason.Add($"Error01");
                    return new List<string>();
                }
                var stdStep = InputMart.Instance.SEMStdStepView.FindRows(stepID).FirstOrDefault();
                if (stdStep == null)
                {
                    noArrangeReason = $"{operarionIsNotFound}";
                    pwi.PlanWip.NoArrnageReason.Add(operarionIsNotFound);
                    return new List<string>();
                }
                if (stdStep.IsProcessing == false)
                {
                    noArrangeReason = $"MASTER_DATA_ERROR - {operIsNotProcessing}";
                    pwi.PlanWip.NoArrnageReason.Add($"{stdStep.OperID} {operIsNotProcessing}");
                    return new List<string>();
                }
                #endregion

                // 캐싱된 값 리턴
                ICollection<SEMEqp> eqps = null;
                if (pwi.ArrangedEqps.TryGetValue(stepID, out eqps) == true)
                    return eqps.Select(x => x.ResID).Distinct().ToList();

                // Run 재공은 현재 run 중인 장비만 Arrange로 가짐
                bool isRun = pwi.WipInfo.WipState.ToUpper() == "RUN" && pwi.WipInfo.WipStepID == stepID;
                if (isRun)
                {
                    var r = new List<string> { pwi.WipInfo.WipEqpID };
                    CachingArrangeResult(r, pwi, stepID);

                    writeArrangeLog(pwi, stepID, productID, pwi.WipInfo.WipEqpID, "Arranged", "Run Wip", $"", null, false, callingModule);
                    WriteArrangeLog(pwi, productID, stepID);

                    return r;
                }

                bool isLotArrange = false;
                bool isResFixLotArrange = false;

                #region Get Resources
                List<string> eqpIDs = GetArrangableEqpIDs(pwi, stepID, productID, callingModule, ref isLotArrange, ref isResFixLotArrange, out string noResReason);
                if (eqpIDs.IsNullOrEmpty())
                {
                    writeArrangeLog(pwi, stepID, productID, noResReason, "Filtered", "", $"{stepID} Has {noResReason} ", null, isLotArrange, callingModule, true);

                    pwi.PlanWip.NoArrnageReason.Add($"{stepID} has {noResReason}");
                    noArrangeReason = $"{stepID} Has ({noResReason})";
                    return new List<string>();
                }
                #endregion

                if (isResFixLotArrange == false)
                {
                    #region No Cycle Time Filter
                    List<string> noCycleTimeEqp = new List<string>();
                    if (pwi.OperDic.TryGetValue(stepID, out var step) == false)
                        WriteLog.WriteErrorLog($"oper를 찾을 수 없습니다 LotID:{pwi.LotID}.");
                    else
                    {
                        foreach (var eqpID in eqpIDs)
                        {
                            if (step.HasCycleTime(eqpID) == false)
                            {
                                noCycleTimeEqp.Add(eqpID);
                            }
                        }
                    }

                    // All eqp has  no Cycle Time
                    if (eqpIDs.Count() == noCycleTimeEqp.Count())
                    {
                        if (InputMart.Instance.GlobalParameters.ApplyWriteNoCycleTimeArrangeLog)
                            writeArrangeLog(pwi, stepID, productID, $"No Cycle Time", "Filtered", "", $"{stepID} {operHasNoCycleTime} ", null, isLotArrange, callingModule, true);

                        pwi.PlanWip.NoArrnageReason.Add($"{stepID} {operHasNoCycleTime}");
                        noArrangeReason = $"{stepID} {operHasNoCycleTime}";
                        return new List<string>();
                    }

                    // Some eqp has no Cycle Time
                    foreach (var eqpID in noCycleTimeEqp)
                    {
                        if (InputMart.Instance.GlobalParameters.ApplyWriteNoCycleTimeArrangeLog)
                            writeArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "", noCycleTime, null, isLotArrange, callingModule);

                        pwi.PlanWip.NoArrnageReason.Add(noCycleTime);
                        eqpIDs.Remove(eqpID);
                    }
                    #endregion


                    #region No Setup Condition Eqp Filter
                    foreach (var eqp in InputMart.Instance.NoSetupCondEqpList)
                    {
                        if (eqpIDs.Contains(eqp.EqpID))
                        {
                            writeArrangeLog(pwi, stepID, productID, eqp.EqpID, "Filtered", "", noSetupCondition, null, isLotArrange, callingModule);

                            if (isResFixLotArrange == false)
                                eqpIDs.Remove(eqp.EqpID);

                            pwi.PlanWip.NoArrnageReason.Add(noSetupCondition);
                        }
                    }
                    #endregion
                }


                #region No arrange to return
                // 로그 작성을 위해 주석처리
                //if (eqpIDs.Count == 0)
                //    return new List<string>();
                #endregion


                #region Get Arrange
                for (int i = eqpIDs.Count - 1; i >= 0; i--)
                {
                    string eqpID = eqpIDs[i];

                    var resJobConds = getResourceJobConditions(eqpID);
                    var jobCategoryCondsDic = resJobConds.GroupBy(x => x.JOB_CATEGORY).ToDictionary(x => x.Key, x => x.AsEnumerable());

                    #region 불가
                    bool isUnableFound = false;
                    IEnumerable<RESOURCE_JOB_CONDITION> unableConds = null;
                    if (jobCategoryCondsDic.TryGetValue(Constants.JobCategoryUnable, out unableConds) == true && isLotArrange == false)
                    {
                        if (unableConds != null && unableConds.Count() > 0)
                        {
                            var unableCondSets = unableConds.GroupBy(x => x.CONDITION_SETS).OrderBy(x => x.Key);
                            foreach (var unableCondSet in unableCondSets.OrderBy(x => x.Count()))
                            {
                                if (pwi.isComplyingCondSet(stepID, unableCondSet) == true)
                                {
                                    isUnableFound = true;
                                    eqpIDs.Remove(eqpID);
                                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "불가", "불가 : O", unableCondSet, isLotArrange, isResFixLotArrange, callingModule);

                                    pwi.PlanWip.NoArrnageReason.Add(UnUsable);

                                    break;
                                }

                                //// Clear logs if none of the condition is complying
                                //string key = unableCondSet.Key;
                                //if (pwi.StepArrangeLogs.ContainsKey(key) == true)
                                //    pwi.StepArrangeLogs.Remove(key);
                            }
                        }
                    }

                    if (isUnableFound == true)
                        continue;
                    //else
                    //    pwi.StepArrangeLogs.Clear();
                    #endregion

                    #region 전용
                    bool isDfound = false;
                    IGrouping<string, RESOURCE_JOB_CONDITION> dFoundCondSet = null;
                    int dConCnt = 0;

                    IEnumerable<RESOURCE_JOB_CONDITION> dedicateConds = null;
                    if (jobCategoryCondsDic.TryGetValue(Constants.JobCategoryDedicated, out dedicateConds) == true)
                    {
                        if (dedicateConds != null && dedicateConds.Count() > 0)
                        {
                            var dedicateCondSets = dedicateConds.GroupBy(x => x.CONDITION_SETS);
                            dConCnt = dedicateCondSets.Count();

                            foreach (var dedicateCondSet in dedicateCondSets.OrderBy(x => x.Count()))
                            {
                                if (pwi.isComplyingCondSet(stepID, dedicateCondSet) == true)
                                {
                                    isDfound = true;
                                    dFoundCondSet = dedicateCondSet;

                                    //writeArrLog(pwi, stepID, productID, eqpID, "Arranged", "Dedicated Resource", dedicateFoundCondSet, callingModule);
                                    break;
                                }
                            }

                            // 부합하는 조건이 없으면 최종 Condition Set 기록 --> 로그 기록 시점에 사용
                            if (dFoundCondSet == null)
                                dFoundCondSet = dedicateCondSets.Last();
                        }
                    }

                    //if (isDfound == false)
                    //{
                    //    if (pwi.StepArrangeLogs.ContainsKey(stepID + eqpID) == true)
                    //        pwi.StepArrangeLogs[stepID + eqpID].Clear();
                    //}
                    #endregion

                    #region 지정
                    bool isAfound = false;
                    IGrouping<string, RESOURCE_JOB_CONDITION> aFoundCondSet = null;
                    int aConCnt = 0;

                    IEnumerable<RESOURCE_JOB_CONDITION> assignedConds = null;
                    if (jobCategoryCondsDic.TryGetValue(Constants.JobCategoryAssigned, out assignedConds) == true)
                    {
                        if (assignedConds != null && assignedConds.Count() > 0)
                        {
                            var assignedCondSets = assignedConds.GroupBy(x => x.CONDITION_SETS);
                            aConCnt = assignedCondSets.Count();

                            foreach (var assignedCondSet in assignedCondSets.OrderBy(x => x.Count()))
                            {
                                if (pwi.isComplyingCondSet(stepID, assignedCondSet) == true)
                                {
                                    isAfound = true;
                                    aFoundCondSet = assignedCondSet;
                                    //writeArrLog(pwi, stepID, productID, eqpID, "Arranged.", "Assigned Resource", assignedCondSet.Key, callingModule);
                                    break;
                                }
                            }

                            // 부합하는 조건이 없으면 최종 Condition Set 기록 --> 로그 기록 시점에 사용
                            if (aFoundCondSet == null)
                                aFoundCondSet = assignedCondSets.Last();
                        }
                    }
                    #endregion

                    #region 점유
                    bool isOfound = false;
                    IGrouping<string, RESOURCE_JOB_CONDITION> oFoundCondSet = null;
                    int oConCnt = 0;

                    IEnumerable<RESOURCE_JOB_CONDITION> occupiedConds = null;
                    if (jobCategoryCondsDic.TryGetValue(Constants.JobCategoryOccupied, out occupiedConds) == true)
                    {
                        if (occupiedConds != null && occupiedConds.Count() > 0)
                        {
                            var occupiedCondSets = occupiedConds.GroupBy(x => x.CONDITION_SETS);
                            oConCnt = occupiedCondSets.Count();

                            foreach (var occupiedCondSet in occupiedCondSets.OrderBy(x => x.Count()))
                            {
                                if (pwi.isComplyingCondSet(stepID, occupiedCondSet) == true)
                                {
                                    isOfound = true;
                                    oFoundCondSet = occupiedCondSet;
                                    //writeArrLog(pwi, stepID, productID, eqpID, "Arranged.", "Assigned Resource", assignedCondSet.Key, callingModule);
                                    break;
                                }
                            }

                            // 부합하는 조건이 없으면 최종 Condition Set 기록 --> 로그 기록 시점에 사용
                            if (oFoundCondSet == null)
                                oFoundCondSet = occupiedCondSets.Last();
                        }
                    }
                    #endregion

                    WriteArrangeLog(pwi, stepID, productID, eqpID, isOfound, isDfound, isAfound, aFoundCondSet, dFoundCondSet, oFoundCondSet, oConCnt, dConCnt, aConCnt, callingModule, isLotArrange, isResFixLotArrange, ref eqpIDs);
                }

                #endregion

                List<string> result = new List<string>();

                // Arrange 최종 조건 filter
                if (isLotArrange == false)
                {
                    result = FilterFinalCondition1(pwi);

                    if (result.IsNullOrEmpty()) // 조건에 맞는 장비가 없음
                    {
                        noArrangeReason = $"{stepID} is {allConditionBlocked}";
                        pwi.PlanWip.NoArrnageReason.Add($"{stepID} is {allConditionBlocked}");
                    }

                    // 최종 조건에 따른 Arrange 로그 수정
                    EditArrangeLog(result);
                }
                else
                {
                    foreach (var a in arrangeDic)
                    {
                        result.AddRange(a.Value);
                    }
                }

                // Arrange 결과 pwi에 캐싱
                CachingArrangeResult(result, pwi, stepID);

                // 로그 작성
                WriteArrangeLog(pwi, productID, stepID);

                // 결과 정렬
                result.Sort(new EqpStringComparer(pwi.WipInfo));

                return result;
            }
            catch (Exception e)
            {
                WriteLog.WriteErrorLog($"{e.Message}");   
                return new List<string>();
            }
        }

        private static List<string> FilterFinalCondition1(SEMPegWipInfo pwi)
        {
            List<string> result = new List<string>();

            // 점유 장비가 있으면 점유만 arrange
            if (arrangeDic.TryGetValue("O", out ICollection<string> o)) 
            {
                result.AddRange(o);

                result = FilterFinalCondition2(pwi, result);

                if (result.IsNullOrEmpty() == false)
                {
                    foreach (var log in arrangeDic)
                    {
                        string cond = string.Empty;

                        if (log.Key == "O")
                            continue;
                        else if (log.Key == "D")
                            cond = "전용";
                        else if (log.Key == "A")
                            cond = "지정";
                        else if (log.Key == "E")
                            cond = "Exampt";
                        else
                            cond = "?";

                        foreach (var eqpID in log.Value)
                            finalFilterReasonDic.Add(eqpID, $"점유 Arrange로 인해 {cond}장비 fiter");
                    }
                    return result;
                }
            }

            // 점유장비가 없으면 전용, 지정 장비만 arrange
            if (arrangeDic.ContainsKey("D") || arrangeDic.ContainsKey("A")) 
            {
                List<string> r = new List<string>();

                if (arrangeDic.TryGetValue("D", out ICollection<string> d))
                    result.AddRange(d);
                if (arrangeDic.TryGetValue("A", out ICollection<string> a))
                    result.AddRange(a);

                result = FilterFinalCondition2(pwi, result);

                if (result.IsNullOrEmpty() == false)
                {
                    foreach (var log in arrangeDic)
                    {
                        string cond = string.Empty;

                        if (log.Key == "O" || log.Key == "D"|| log.Key == "A")
                            continue;
                        else if (log.Key == "E")
                            cond = "Exampt";
                        else
                            cond = "?";

                        foreach (var eqpID in log.Value)
                            finalFilterReasonDic.Add(eqpID, $"전용, 지정 Arrange로 인해 {cond}장비 fiter");
                    }
                    return result;
                }
            }

            // 점유 전용 지정 장비가 없으면 조건없는 장비에 arrange(Exampt)
            if (arrangeDic.ContainsKey("E"))
            {
                if (arrangeDic.TryGetValue("E", out ICollection<string> e))
                {
                    result.AddRange(e);
                    result = FilterFinalCondition2(pwi, result);
                }
            }
            
            return result;
        }

        private static List<string> FilterFinalCondition2(SEMPegWipInfo pwi, List<string> eqps)
        {
            List<string> result = new List<string>();

            List<SEMEqp> eqpList = new List<SEMEqp>();
            foreach (var eqpID in eqps)
            {
                if (InputMart.Instance.SEMEqp.TryGetValue(eqpID, out var eqp) == false)
                    continue;

                eqpList.Add(eqp);
            }

            if (pwi.WipInfo.ResProductGroup.ToUpper() == "ALL")
            { 
                //모두 통과 (filter안함)
            }
            else 
            {
                List<SEMEqp> removeEqpList = new List<SEMEqp>();

                foreach (var eqp in eqpList)
                {
                    if (eqp.ResProductGroup.IsNullOrEmpty() || eqp.ResProductGroup == pwi.WipInfo.ResProductGroup)
                    { 
                        // Filter 안함
                    }
                    else
                    {
                        removeEqpList.Add(eqp);
                    }
                }

                foreach (var eqp in removeEqpList)
                {
                    eqpList.Remove(eqp);

                    if(finalFilterReasonDic.ContainsKey(eqp.EqpID) == false)
                        finalFilterReasonDic.Add(eqp.EqpID,$"ResProductGroup 불일치");
                }

                //var sameGroupEqps = eqpList.Where(x => x.ResProductGroup == pwi.WipInfo.ResProductGroup).ToList();
                //var blankGouepEqps = eqpList.Where(x => x.ResProductGroup.IsNullOrEmpty()).ToList();
                
                //eqpList.Clear();
                //eqpList.AddRange(sameGroupEqps);
                //eqpList.AddRange(blankGouepEqps);
            }

            if (eqpList.Any(x => x.FloorID == pwi.WipInfo.FloorID))
            {
                eqpList = eqpList.Where(x => x.FloorID == pwi.WipInfo.FloorID).ToList();
            }
            else if (eqpList.Any(x => x.FactoryID == pwi.WipInfo.FactoryID))
            {
                eqpList = eqpList.Where(x => x.FactoryID == pwi.WipInfo.FactoryID).ToList();
            }
            else 
            { 
            }

            result = eqpList.Select(x => x.EqpID).ToList();

            return result;
        }

        private static void WriteArrangeLog(SEMPegWipInfo pwi, string productID, string stepID)
        {
            string key = CommonHelper.CreateKey(productID, stepID);
            bool isWriteLog = pwi.PlanWip.ArrangeLogList.Contains(key) ? false : true;
            if (isWriteLog)
            {
                foreach (var log in InputMart.Instance.ArrLogDic)
                {
                    if(finalFilterReasonDic.TryGetValue(log.Value.RESOURCE_ID, out string ffl))
                    { 
                        log.Value.FINAL_FILTER_REASON = ffl;
                    }
                    OutputMart.Instance.ARRANGE_LOG.Add(log.Value);
                }
                pwi.PlanWip.ArrangeLogList.Add(key);
            }
        }

        private static List<string> GetArrangableEqpIDs(SEMPegWipInfo pwi, string stepID, string productID, string callingModule, ref bool isLotArrange, ref bool isResFixLotArrange, out string noResReason)
        {
            List<string> result = new List<string>();
            noResReason = string.Empty;

            if (pwi.WipInfo.IsLotArrange && pwi.WipInfo.LotArrangeOperID == stepID)
            {
                ICollection<SEMEqp> lotArrangeEqpList;

                List<string> lotArrangeEqps = new List<string>();

                if (pwi.WipInfo.IsResFixLotArrange)
                {
                    //
                    // ResFix Lot Arrange
                    //

                    SEMEqp eqp = pwi.WipInfo.ResFixLotArrangeEqp;// lotArrangeEqpList.First();

                    // ARRNAGE_LOG 로그 작성
                    //writeArrangeLog(pwi, stepID, productID, eqp.EqpID, "Arranged", "Resource Fixed Lot Arrange", "", null, isLotArrange, callingModule);

                    // pwi에 Arrange 정보를 캐싱(런타임 감소를 위해)
                    //pwi.ArrangedEqps.Add(stepID, eqp);


                    // 결과에 추가
                    lotArrangeEqps.Add(eqp.EqpID);

                    isResFixLotArrange = true;
                }
                else
                {
                    //
                    // 일반 LotArrange
                    //

                    if (pwi.WipInfo.LotArrangedEqpDic.TryGetValue(stepID, out lotArrangeEqpList))
                    {
                        foreach (SEMEqp lotEqp in lotArrangeEqpList)
                        {
                            // ARRNAGE_LOG 로그 작성
                            //writeArrangeLog(pwi, stepID, productID, lotEqp.EqpID, "Arranged", "Normal Lot Arrange", "", null, callingModule);

                            // pwi에 Arrange 정보를 캐싱(런타임 감소를 위해)
                            //pwi.ArrangedEqps.Add(stepID, lotEqp);

                            // 결과에 추가
                            lotArrangeEqps.Add(lotEqp.EqpID);
                        }
                    }
                    else
                    {
                        // 다른 Step에서 LotArrange였음
                        //writeArrangeLog(pwi, stepID, productID, "", "Filtered", "Normal Lot Arrange Error", $"Arrange Data Not Found", null, callingModule);
                    }
                }

                if (lotArrangeEqps.Count > 0)
                {
                    result = lotArrangeEqps;
                    isLotArrange = true;

                    return result;
                }
            }
            
            List<string> operResources = getOperResources(stepID);
            if (operResources.IsNullOrEmpty())
            {
                noResReason = "No Resource";
                return operResources;
            }

            result = operResources.DoFilter(pwi, stepID, ref noResReason);   

            return result;
        }

        private static List<string> DoFilter(this List<string> eqpIDs, SEMPegWipInfo pwi, string stepID, ref string noResReason)
        {
            // Get Eqps
            List<SEMEqp> eqps = new List<SEMEqp>();
            foreach (string eqpID in eqpIDs)
            {
                if (InputMart.Instance.SEMEqp.TryGetValue(eqpID, out var eqp))
                {
                    eqps.Add(eqp);
                }
            }            
            
            var prod = pwi.getProduct(stepID);

            string routeID = InputMart.Instance.RouteDic[stepID];

            List<string> validResourceGroups = new List<string>();
            List<string> validEqps = new List<string>();


            if (routeID == "SGC117")
            {
                SEMStdStep stdStep = InputMart.Instance.SEMStdStep.Rows.Where(x => x.OperID == stepID).FirstOrDefault();
                if (stdStep.OperName.Contains("외관선별"))
                {
                    // filter 하지 않음
                    noResReason = "";
                }
                else if (stdStep.OperName.Contains("전극소성"))
                {
                    validEqps =  prod.TermFiringProfile.Where(x => x.OPER_ID == stepID || x.OPER_ID == "ALL").Select(x => x.RESOURCE_ID).Distinct().ToList();
                    
                    for (int i = eqps.Count() - 1; i >= 0; i--)
                    {
                        SEMEqp eqp = eqps[i];

                        if (validEqps.Contains(eqp.EqpID) == false)
                        {
                            eqps.RemoveAt(i);
                            continue;
                        }
                    }
                    noResReason = "No Term-Firing Profile";
                }
                else if (stdStep.OperName.Contains("전극도포"))
                {
                    validResourceGroups = prod.ModelOutsidePaste.Where(x => x.OPER_ID == stepID).Select(x => x.RES_GROUP_ID).Distinct().ToList();

                    for (int i = eqps.Count() - 1; i >= 0; i--)
                    {
                        SEMEqp eqp = eqps[i];

                        if (validResourceGroups.Contains(eqp.ResourceGroup) == false)
                        {
                            eqps.RemoveAt(i);
                            continue;
                        }
                    }
                    noResReason = "No Outside Paste";
                }
                else 
                {
                    WriteLog.WriteErrorLog($"알 수 없는 route name : {stdStep.RouteName}");
                }
            }
            else if (routeID == "SGC118")
            {
                validEqps = prod.TermFiringProfile.Where(x => x.OPER_ID == stepID || x.OPER_ID == "ALL").Select(x => x.RESOURCE_ID).Distinct().ToList();
                for (int i = eqps.Count() - 1; i >= 0; i--)
                {
                    SEMEqp eqp = eqps[i];

                    if (validEqps.Contains(eqp.EqpID) == false)
                    {
                        eqps.RemoveAt(i);
                        continue;
                    }
                }
                noResReason = "No Term-Firing Profile";
            }
            else if (routeID == "SGC119")
            {
                validResourceGroups = pwi.WipInfo.PlatingRecipe.Where(x => x.OPER_ID == stepID).Select(x => x.RESOURCE_TYPE).Distinct().ToList();
                
                for (int i = eqps.Count() - 1; i >= 0; i--)
                {
                    SEMEqp eqp = eqps[i];

                    if (validResourceGroups.Contains(eqp.ResourceGroup) == false)
                    {
                        eqps.RemoveAt(i);
                        continue;
                    }
                }
                noResReason = "No Plating Recipe";
            }
            else if (routeID == "SGC120")
            {
                validResourceGroups = prod.SortingJobCondition.Select(x => x.RES_GROUP_ID).Distinct().ToList();
                
                for (int i = eqps.Count() - 1; i >= 0; i--)
                {
                    SEMEqp eqp = eqps[i];

                    if (validResourceGroups.Contains(eqp.ResourceGroup) == false)
                    {
                        eqps.RemoveAt(i);
                        continue;
                    }
                }
                noResReason = "No Sorting Job Condition";
            }
            else 
            {
                WriteLog.WriteErrorLog($"알 수 없는 route : {routeID}");
            }

            return eqps.Select(x => x.EqpID).ToList();
        }

        #region Compliance Check Logics        
        private static bool isComplyingCondSet(this SEMPegWipInfo pwi, string stepID, IGrouping<string, RESOURCE_JOB_CONDITION> condSet, bool shouldMatchAll = true)
        {
            if (pwi.ConditionSetLogs.ContainsKey(condSet.Key) == true)
                return pwi.ConditionSetLogs[condSet.Key];

            var condTypeCodeGroups = condSet.GroupBy(x => x.CONDITION_TYPE_CODE);
            int complyCnt = 0;

            foreach (var condTypeCodeGroup in condTypeCodeGroups.OrderBy(x => x.Key))
            {
                if (isComplyingCondTypeCodeGroup(pwi, stepID, condTypeCodeGroup.AsEnumerable()) == true)
                {
                    complyCnt++;
                }
            }

            if (complyCnt == 0)
            {
                if (pwi.ConditionSetLogs.ContainsKey(condSet.Key) == false)
                    pwi.ConditionSetLogs.Add(condSet.Key, false);
                else
                    pwi.ConditionSetLogs[condSet.Key] = false;
                return false;
            }

            bool isPartiallyComplying = condTypeCodeGroups.Count() > complyCnt;

            bool retVal = shouldMatchAll && !isPartiallyComplying ? true : false;

            if (pwi.ConditionSetLogs.ContainsKey(condSet.Key) == false)
                pwi.ConditionSetLogs.Add(condSet.Key, retVal);
            else
                pwi.ConditionSetLogs[condSet.Key] = retVal;

            return retVal;

            #region Obsolete
#if false
            if (shouldMatchAll == true)
            {
                if (isPartiallyComplying == true)
                    return false;
                else
                    return true;
            }
            else
            {
                if (isPartiallyComplying == true)
                    return false;
                else
                    return true;
            }
#endif
            #endregion
        }
        private static bool isComplyingCondTypeCodeGroup(SEMPegWipInfo pwi, string stepID, IEnumerable<RESOURCE_JOB_CONDITION> condTypeCodeGroup, bool shouldMatchAll = false)
        {
            if (condTypeCodeGroup.Count() == 1)
                shouldMatchAll = true;

            int complyCnt = 0;

            foreach (var resJobCond in condTypeCodeGroup.OrderBy(x => x.CONDITION_TYPE_CODE))
            {
                string conAndVal = "";
                if (isComplyingCondition(pwi, stepID, resJobCond, conAndVal))
                    complyCnt++;
            }

            if (complyCnt == 0)
                return false;

            bool isPartiallyComplying = condTypeCodeGroup.Count() > complyCnt;

            if (shouldMatchAll == true)
            {
                if (isPartiallyComplying == true)
                    return false;
                else
                    return true;
            }
            else
            {
                if (isPartiallyComplying == true)
                    return true;
                else
                    return false;
            }
        }
        private static bool isComplyingCondition(SEMPegWipInfo pwi, string stepID, RESOURCE_JOB_CONDITION resJobCond, string consAndVals = null)
        {
            // Wip 조건
            object wipCond = GetWipCond(resJobCond, pwi, stepID);

            // 장비 작업조건
            string resCond = resJobCond.CON_VALUE;

            // 조건 만족여부 확인
            bool retVal = isComplyingCondition(resJobCond, wipCond, resCond);

            // 로그 작성
            consAndVals = $"({resJobCond.ODS_FIELD} : EQP {resJobCond.INEQUALITY_SIGN} {resJobCond.CON_VALUE} → LOT = {wipCond})";
            pwi.AddArrangeLog(resJobCond, consAndVals, retVal);

            return retVal;
        }

        public static object GetWipCond(RESOURCE_JOB_CONDITION resJobCond, SEMPegWipInfo pwi, string stepID)
        {
            object wipValue = null;

            if (resJobCond.CONDITION_TYPE_CODE == "C000000049") // C000000049 = Lot Qty
            {
                var ot = pwi.SemOperTargets.Where(x => x.OperId == stepID).FirstOrDefault();
                if (ot == null)
                {
                    WriteLog.WriteErrorLog($"OperTarget을 찾을 수 없습니다.");
                    wipValue = null;
                }
                else
                {
                    wipValue = ot.Qty;
                }
            }
            else if (resJobCond.CONDITION_TYPE_CODE == "C000000053")
            {
                wipValue = pwi.WipInfo.LotClass;
            }
            else if (resJobCond.ODS_TABLE.IsNullOrEmpty())
            {
                if (resJobCond.CONDITION_TYPE_CODE == "C000000002") //Current Operation
                {
                    wipValue = stepID;
                }
                else if (resJobCond.CONDITION_TYPE_CODE == "C000000023") // Outside Paste/PASTE_PROD_ID
                {
                    if (InputMart.Instance.SEMEqp.TryGetValue(resJobCond.RESOURCE_ID, out var eqp) == false)
                    {
                        wipValue = string.Empty;
                        return wipValue;
                    }

                    var prod = pwi.getProduct(stepID);
                    var outsidePastes = prod.ModelOutsidePaste.Where(x => x.OPER_ID == stepID && x.RES_GROUP_ID == eqp.ResourceGroup && x.PASTE_PROD_ID == resJobCond.CON_VALUE).FirstOrDefault();
                    wipValue = outsidePastes == null ? string.Empty : outsidePastes.PASTE_PROD_ID;

                    if ((string)wipValue == string.Empty)
                    {
                        outsidePastes = prod.ModelOutsidePaste.Where(x => x.OPER_ID == stepID && x.RES_GROUP_ID == eqp.ResourceGroup).FirstOrDefault();
                        wipValue = outsidePastes == null ? string.Empty : outsidePastes.PASTE_PROD_ID;
                    }
                }
                else if (resJobCond.CONDITION_TYPE_CODE == "C000000048") //Dipping Type
                {
                    if (InputMart.Instance.SEMEqp.TryGetValue(resJobCond.RESOURCE_ID, out var eqp) == false)
                    {
                        wipValue = string.Empty;
                        return wipValue;
                    }

                    var prod = pwi.getProduct(stepID);
                    List<string> outsidePastes = prod.ModelOutsidePaste.Where(x => x.OPER_ID == stepID && x.RES_GROUP_ID == eqp.ResGroup).Select(x => x.DIPPING_TYPE).ToList();
                    List<string> termFiringProfiles = prod.TermFiringProfile.Where(x => x.RESOURCE_ID == resJobCond.RESOURCE_ID && (x.OPER_ID == stepID || x.OPER_ID == "ALL")).Select(x => x.DIPPING_TYPE).ToList();

                    List<string> result = new List<string>();
                    result.AddRange(outsidePastes);
                    result.AddRange(termFiringProfiles);

                    wipValue = result.Distinct();
                }
                else if (resJobCond.CONDITION_TYPE_CODE == "C000000078") //Plating Barrel/BARREL_TYPE
                {
                    if (InputMart.Instance.SEMEqp.TryGetValue(resJobCond.RESOURCE_ID, out var eqp) == false)
                    {
                        wipValue = string.Empty;
                        return wipValue;
                    }

                    string barrelType = pwi.WipInfo.PlatingRecipe.Where(x => x.OPER_ID == stepID).Select(x => x.BARREL_TYPE).FirstOrDefault();
                    wipValue = barrelType;
                }
                else
                {
                    WriteLog.WriteErrorLog($"RESOURCE_JOB_CONDITION 오류 {resJobCond.RESOURCE_ID}, {resJobCond.CONDITION_SETS}, {resJobCond.CONDITION_TYPE_CODE}, {resJobCond.CONDITION_TYPE_NAME}");
                }
            }
            else
            {
                object table = GetTable(resJobCond.ODS_TABLE, pwi, stepID, resJobCond);
                wipValue = table.GetPropValue(resJobCond.ODS_FIELD);
            }

            string wipCond = wipValue == null ? string.Empty : wipValue.ToString();
            return wipCond;
        }

        public static object GetTable(string odsTbl, SEMPegWipInfo pwi, string stepID, RESOURCE_JOB_CONDITION resJobCond)
        {
            object table;
            switch (odsTbl)
            {
                case "FP_I_PRODUCT":
                    table = pwi.getProduct(stepID).FP_I_PRODUCT;
                    break;
                case "FP_I_TAPING_MATERIAL_ALT":
                    table = pwi.GetTAPING_MATERIAL_ALT(pwi.getProduct(stepID));
                    break;
                case "FP_I_WIP_JOB_CONDITION":
                    string conditionTypecode = resJobCond.CONDITION_TYPE_CODE;
                    if (pwi.WipInfo.WipJobConditions.ContainsKey(conditionTypecode) == false)
                        table = null;
                    else
                        table = pwi.WipInfo.WipJobConditions[conditionTypecode];
                    break;
                case "FP_I_WIP":
                    table = null;
                    break;
                default:
                    table = null;
                    break;
            }

            return table;
        }

        public static bool isComplyingCondition(RESOURCE_JOB_CONDITION resJobCond, object wipValue, string resValue)//, string sign, bool isWildCode)
        {
            bool retVal = false;

            string sign = resJobCond.INEQUALITY_SIGN;
            bool isWildCode = resJobCond.WILD_CODE == "Y";

            if (wipValue is string)
            {
                string wv = wipValue as string;
                retVal = isComplyingCondition(resJobCond, wv, resValue);                
            }
            else if (wipValue is List<string>)
            {
                List<string> wipValueList = wipValue as List<string>;

                foreach (var wv in wipValueList)
                { 
                    retVal = isComplyingCondition(resJobCond, wv, resValue);

                    if (retVal)
                        return retVal;
                }
            }
            
            return retVal;
        }

        public static bool isComplyingCondition(RESOURCE_JOB_CONDITION resJobCond, string wipValue, string resValue)//, string sign, bool isWildCode)
        {
            bool retVal = true;

            string sign = resJobCond.INEQUALITY_SIGN;
            bool isWildCode = resJobCond.WILD_CODE == "Y";

            try
            {
                if (sign == "=" || sign == null || sign == string.Empty)
                {
                    if (isWildCode)
                    {
                        // 값이 '_'를 제외한 부분만 일치 

                        char[] w = wipValue.ToCharArray();
                        char[] r = resValue.ToCharArray();

                        for (int i = 0; i < r.Length; i++)  //r.Length까지 하는 이유 : w보다 r이 짧게 들어오는 데이터가 있음, 있는데 까지만 비교
                        {
                            if (r[i] == '_')
                                continue;

                            if (w[i] != r[i])
                            {
                                retVal = false;
                                break;
                            }
                        }
                    }
                    else
                    {
                        // 값이 완벽히 일치

                        if (wipValue.ToString() == resValue)
                            retVal = true;
                        else
                            retVal = false;
                    }
                }
                else
                {
                    double r = 0;
                    double w = 0;

                    if (resJobCond.CONDITION_TYPE_CODE == "C000000017") //C000000017 = Capacity
                    {
                        r = ValueToDouble(resValue);
                        w = ValueToDouble(wipValue);
                    }
                    else if (resJobCond.CONDITION_TYPE_CODE == "C000000049") // C000000049 = Lot Qty
                    {
                        r = Convert.ToDouble(resValue);
                        w = Convert.ToDouble(wipValue);
                    }

                    if (sign == "<")
                    {
                        if (w < r)
                            retVal = true;
                        else
                            retVal = false;
                    }
                    else if (sign == "<=" || sign == "=<")
                    {
                        if (w <= r)
                            retVal = true;
                        else
                            retVal = false;
                    }
                    else if (sign == ">")
                    {
                        if (w > r)
                            retVal = true;
                        else
                            retVal = false;
                    }
                    else if (sign == ">=" || sign == "=>")
                    {
                        if (w >= r)
                            retVal = true;
                        else
                            retVal = false;
                    }
                    else
                    {
                        WriteLog.WriteErrorLog($"RESOURCE_JOB_CONDITION INEQULITY_SIGN ERROR");
                    }
                }

            }
            catch (Exception e)
            {
                WriteLog.WriteErrorLog($"resValue:{resValue} // wipValue:{wipValue} // {e}");
                return false;
            }

            return retVal;
        }


        private static double ValueToDouble(string value)
        {
            double result;
            if (value.Contains("R"))
            {
                var v = value.Split('R');
                if (v.Count() == 1)
                {
                    result = Convert.ToDouble(v[0]) * 0.01;
                }
                else
                {
                    result = Convert.ToDouble(v[0] + "." + v[1]);
                }

            }
            else
            {
                result = Convert.ToDouble(value.Substring(0, 2)) * Math.Pow(10, Convert.ToDouble(value.Substring(2, 1)));
            }

            return result;
        }


        public static void AddArrangeLog (this SEMPegWipInfo pwi, RESOURCE_JOB_CONDITION resJobCond, string consAndVals, bool retVal)
        {
            string key1 = resJobCond.CONDITION_SETS;
            string key2 = resJobCond.CONDITION_TYPE_CODE;
            Dictionary<string, string> innerDic = null;
            if (pwi.StepArrangeLogs.TryGetValue(key1, out innerDic) == false)
                pwi.StepArrangeLogs.Add(key1, innerDic = new Dictionary<string, string>());

            string val = "";
            if (innerDic.TryGetValue(key2, out val) == false)
                innerDic.Add(key2, val = consAndVals);

            if (retVal == true)     // Resource Job Condtion과 일치/불일치 상관없이 모두 기록하므로 일치하는 조건은 반드시 조건값 업데이트
                innerDic[key2] = consAndVals;
        }

        #endregion

        #region Get Resources
        private static Dictionary<string, IEnumerable<string>> operResourcesDic = null;
        private static List<string> getOperResources(string stepID)
        {
            if (operResourcesDic == null)
                operResourcesDic = new Dictionary<string, IEnumerable<string>>();

            IEnumerable<string> targetResources = null;
            if (operResourcesDic.TryGetValue(stepID, out targetResources) == true)
                return targetResources.ToList();

            var resources = InputMart.Instance.RESOURCEOperView.FindRows(stepID).Where(x => x.VALID == "Y");
            if (resources == null || resources.Count() == 0)
                return new List<string>();

            var resourceIDs = resources.Select(x => x.RESOURCE_ID).Distinct();
            operResourcesDic.Add(stepID, resourceIDs);

            return resourceIDs.ToList();
        }
        private static Dictionary<string, IEnumerable<string>> operProdResourcesDic = null;
        private static IEnumerable<string> getTactTimeResources(string stepID, string productID)
        {
            if (operProdResourcesDic == null)
                operProdResourcesDic = new Dictionary<string, IEnumerable<string>>();

            IEnumerable<string> targetResources = null;
            if (operProdResourcesDic.TryGetValue(stepID + productID, out targetResources) == true)
                return targetResources;

            var resources = InputMart.Instance.CYCLE_TIMEByOperProd.FindRows(stepID, productID);
            if (resources == null || resources.Count() == 0)
                return null;

            var resourceIDs = resources.Select(x => x.RESOURCE_ID).Distinct();
            operProdResourcesDic.Add(stepID + productID, resourceIDs);

            return resourceIDs;
        }
        #endregion

        #region Get Resource Job Conditions
        private static Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>> resJobCondsEqpDic = null;
        private static IEnumerable<RESOURCE_JOB_CONDITION> getResourceJobConditions(string eqpID)
        {
            if (resJobCondsEqpDic == null)
                resJobCondsEqpDic = new Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>>();

            IEnumerable<RESOURCE_JOB_CONDITION> resJobConds = null;
            if (resJobCondsEqpDic.TryGetValue(eqpID, out resJobConds) == true)
                return resJobConds;

            var targetResJobConds = InputMart.Instance.RESOURCE_JOB_CONDITIONResourceView.FindRows(eqpID);
            if (targetResJobConds == null || targetResJobConds.Count() == 0)
                return new List<RESOURCE_JOB_CONDITION>();

            resJobCondsEqpDic.Add(eqpID, targetResJobConds);

            return targetResJobConds;
        }
        private static Dictionary<string, Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>>> resJobCondsEqpCondSetDic = null;
        private static IEnumerable<RESOURCE_JOB_CONDITION> getResourceJobConditions(string eqpID, string condSet)
        {
            if (resJobCondsEqpCondSetDic == null)
                resJobCondsEqpCondSetDic = new Dictionary<string, Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>>>();

            Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>> temp = null;
            if (resJobCondsEqpCondSetDic.TryGetValue(eqpID, out temp) == false)
                resJobCondsEqpCondSetDic.Add(eqpID, temp = getResourceJobConditions(eqpID).GroupBy(x => x.CONDITION_SETS).ToDictionary(x => x.Key, x => x.AsEnumerable()));

            IEnumerable<RESOURCE_JOB_CONDITION> val = null;
            temp.TryGetValue(condSet, out val);

            return val;
        }
        private static Dictionary<string, Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>>> resJobCondsEqpConSetCondTypeCodeDic = null;
        private static IEnumerable<RESOURCE_JOB_CONDITION> getResourceJobCondition(string eqpID, string condSet, string condTypeCode)
        {
            if (resJobCondsEqpConSetCondTypeCodeDic == null)
                resJobCondsEqpConSetCondTypeCodeDic = new Dictionary<string, Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>>>();

            var b = getResourceJobConditions(eqpID, condSet).GroupBy(x => x.CONDITION_TYPE_CODE).ToDictionary(x => x.Key, x => x.AsEnumerable());

            string key = CommonHelper.CreateKey(eqpID, condSet);
            Dictionary<string, IEnumerable<RESOURCE_JOB_CONDITION>> temp = null;
            if (resJobCondsEqpConSetCondTypeCodeDic.TryGetValue(key, out temp) == false)
                resJobCondsEqpConSetCondTypeCodeDic.Add(key, temp = b);

            if (temp != null)
                return temp[condTypeCode];

            return null;
        }
        #endregion

        public static void WriteArrangeLog(SEMPegWipInfo pwi, string stepID, string productID, string eqpID, bool isOfound, bool isDfound, bool isAfound,
            IGrouping<string, RESOURCE_JOB_CONDITION> aFoundCondSet, IGrouping<string, RESOURCE_JOB_CONDITION> dFoundCondSet, IGrouping<string, RESOURCE_JOB_CONDITION> oFoundCondSet,
            int oConCnt, int dConCnt, int aConCnt, string callingModule, bool isLotArrange, bool isResFixLotArrange, ref List<string> eqpIDs)
        {
            // O : 점유
            // D : 전용
            // A : 지정
            // E : 통과(조건 없음)


            if (isOfound)
            {
                arrangeDic.Add("O", eqpID);

                if (isDfound && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정 & 점유", "점유 : O / 전용 : O / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정 & 점유", "점유 : O / 전용 : O / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정 & 점유", "점유 : O / 전용 : O / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (isDfound && isAfound == false && aConCnt > 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 점유", "점유 : O / 전용 : O / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 점유", "점유 : O / 전용 : O / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 점유", "점유 : O / 전용 : O / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (isDfound && isAfound == false && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 점유", "점유 : O / 전용 : O / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 점유", "점유 : O / 전용 : O / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정 & 점유", "점유 : O / 전용 : X / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정 & 점유", "점유 : O / 전용 : X / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정 & 점유", "점유 : O / 전용 : X / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound == false)
                {
                    //pwi.StepArrangeLogs.Clear();

                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", $"점유 : O / 전용 : X / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", $"점유 : O / 전용 : X / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", $"점유 : O / 전용 : X / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (isDfound == false && dConCnt > 0 && aConCnt == 0)
                {
                    //pwi.StepArrangeLogs.Clear();
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", "점유 : O / 전용 : X / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", "점유 : O / 전용 : X / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (dConCnt == 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : O / 전용 : - / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : O / 전용 : - / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (dConCnt == 0 && isAfound == false && aConCnt > 0)
                {
                    //pwi.StepArrangeLogs.Clear();
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : O / 전용 : - / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : O / 전용 : - / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else if (dConCnt == 0 && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "점유", "점유 : O / 전용 : - / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                }
                else
                {
                    WriteLog.WriteErrorLog("Arrange 진리표 오류1");
                }
            }
            else if (isOfound == false && oConCnt > 0)
            {
                if (isDfound && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정", "점유 : X / 전용 : O / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정", "점유 : X / 전용 : O / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정", "점유 : X / 전용 : O / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound && isAfound == false && aConCnt > 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : X / 전용 : O / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : X / 전용 : O / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : X / 전용 : O / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound && isAfound == false && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : X / 전용 : O / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : X / 전용 : O / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : X / 전용 : X / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : X / 전용 : X / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : X / 전용 : X / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("A", eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound == false && aConCnt > 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", $"점유 : X / 전용 : X / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", $"점유 : X / 전용 : X / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", $"점유 : X / 전용 : X / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    
                    if(isLotArrange == false)
                        eqpIDs.Remove(eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : X / 전용 : X / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : X / 전용 : X / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else if (dConCnt == 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : X / 전용 : - / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : X / 전용 : - / 지정 : O", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("A", eqpID);
                }
                else if (dConCnt == 0 && isAfound == false && aConCnt > 0)
                {
                    //pwi.StepArrangeLogs.Clear();
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : X / 전용 : - / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : X / 전용 : - / 지정 : X", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else if (dConCnt == 0 && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : X / 전용 : - / 지정 : -", oFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else
                {
                    WriteLog.WriteErrorLog("Arrange 진리표 오류2");
                }
            }
            else if (isOfound == false && oConCnt == 0)
            {
                if (isDfound && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정", "점유 : - / 전용 : O / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용 & 지정", "점유 : - / 전용 : O / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound && isAfound == false && aConCnt > 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : - / 전용 : O / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : - / 전용 : O / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound && isAfound == false && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "전용", "점유 : - / 전용 : O / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("D", eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : - / 전용 : X / 지정 : O", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : - / 전용 : X / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("A", eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && isAfound == false && aConCnt > 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", $"점유 : - / 전용 : X / 지정 : X", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", $"점유 : - / 전용 : X / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else if (isDfound == false && dConCnt > 0 && aConCnt == 0)
                {
                    //pwi.StepArrangeLogs.Clear();
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : - / 전용 : X / 지정 : -", dFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);
                    
                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else if (dConCnt == 0 && isAfound)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "지정", "점유 : - / 전용 : - / 지정 : O", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("A", eqpID);
                }
                else if (dConCnt == 0 && isAfound == false && aConCnt > 0)
                {
                    //pwi.StepArrangeLogs.Clear();
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Filtered", "All conditions blocked", "점유 : - / 전용 : - / 지정 : X", aFoundCondSet, isLotArrange, isResFixLotArrange, callingModule);

                    if (isLotArrange)
                        arrangeDic.Add("A", eqpID);
                    else
                        eqpIDs.Remove(eqpID);
                }
                else if (dConCnt == 0 && aConCnt == 0)
                {
                    writeJobArrangeLog(pwi, stepID, productID, eqpID, "Arranged", "Exempt Condition", "점유 : - / 전용 : - / 지정 : -", null, isLotArrange, isResFixLotArrange, callingModule);

                    arrangeDic.Add("E", eqpID);
                }
                else
                {
                    WriteLog.WriteErrorLog("Arrange 진리표 오류3");
                }
            }
            else
            {
                WriteLog.WriteErrorLog("Arrange 진리표 오류4");
            }
        }


        private static SEMProduct getProduct(this SEMPegWipInfo pwi, string stepID)
        {
            SEMProduct product;

            if (pwi.ProcessingOpers.TryGetValue(stepID, out product) == false)
            {
                product = pwi.WipInfo.Product as SEMProduct;
            }

            return product;
        }
        private static void writeJobArrangeLog(SEMPegWipInfo pwi, string stepID, string productID, string resID, string jobResult, string category, string path, IGrouping<string, RESOURCE_JOB_CONDITION> condSet, bool isLotArrange, bool isResFixLotArrange, string callingModule)
        {
            string key = CommonHelper.CreateKey(pwi.LotID, productID, stepID, resID, callingModule);

            ARRANGE_LOG prevLog = null;
            if (InputMart.Instance.ArrLogDic.TryGetValue(key, out prevLog) == true)
            {
                if (condSet == null || condSet.Count() == 0)
                    return;

                string[] conSets = prevLog.CONDITION_SET.Split(',');
                conSets.ForEach(x => x.Trim());

                if (conSets.Contains(condSet.Key) == false)
                {
                    prevLog.CONDITION_SET += $", {condSet.Key}";
                    prevLog.CONS_AND_VALUES += " / " + pwi.StepArrangeLogs[condSet.Key].Select(x => $"{condSet.First().JOB_CATEGORY} {x.Key} {x.Value}").ListToString(" / ");
                }

                return;
            }

            string r = jobResult == "Arranged" ? "O" : "X";
            DateTime calcAvailTime = DateTime.MinValue;
            pwi.AvailableTimeDic.TryGetValue(stepID, out calcAvailTime);

            if (isLotArrange)
            {
                if (isResFixLotArrange)
                    category = $"Resource Fixed Lot Arrange({category})";
                else
                    category = $"Normal Lot Arrange({category})";
            }

            string resFactoryID = string.Empty;
            string resFloorID = string.Empty;
            string resProdGroup = string.Empty;

            SEMEqp eqp;
            if (InputMart.Instance.SEMEqp.TryGetValue(resID, out eqp))
            {
                resFactoryID = eqp.FactoryID;
                resFloorID = eqp.FloorID;
                resProdGroup = eqp.ResProductGroup;
            }

            ARRANGE_LOG al = new ARRANGE_LOG
            {
                LOT_ID = pwi.LotID,
                CONDITION_SET = condSet == null ? "" : condSet.Key,
                MODULE = callingModule,
                JOB_CATEGORY = category,
                PATH = path,
                OPER_ID = stepID,
                PRODUCT_ID = productID,
                RESOURCE_ID = resID,
                RESULT = "",  //result는 최종조건까지 확인해서 결정
                JOB_RESULT = r,
                CONS_AND_VALUES = condSet == null ? "" : pwi.StepArrangeLogs[condSet.Key].Select(x => $"{condSet.First().JOB_CATEGORY} {x.Key} {x.Value}").ListToString(" / "),
                WIP_AVAILABLE_TIME = pwi.WipInfo.AvailableTime,
                CALC_AVAILABLE_TIME = calcAvailTime,
                RESOURCE_FACTORY_ID = resFactoryID,
                RESOURCE_FLOOR_ID = resFloorID,
                RESOURCE_PROD_GROUP = resProdGroup,
                WIP_FACTORY_ID = pwi.WipInfo.FactoryID,
                WIP_FLOOR_ID = pwi.WipInfo.FloorID,
                WIP_RES_PROD_GROUP = pwi.WipInfo.ResProductGroup
            };

            // LotArrange는 미리 셋팅해줌
            if (isLotArrange)
                al.RESULT = "Arranged";

            InputMart.Instance.ArrLogDic.Add(key, al);
            //OutputMart.Instance.ARRANGE_LOG.Add(al);
        }

        private static void writeArrangeLog(SEMPegWipInfo pwi, string stepID, string productID, string resID, string result, string category, string path, IGrouping<string, RESOURCE_JOB_CONDITION> condSet, bool isLotArrange, string callingModule, bool isWriteLog = false)
        {
            string key = CommonHelper.CreateKey(pwi.LotID, productID, stepID, resID, callingModule);
            
            var calaAvailTime = DateTime.MinValue;
            pwi.AvailableTimeDic.TryGetValue(stepID, out calaAvailTime);

            if(isLotArrange)
                category = $"Normal Lot Arrange({category})";

            string resFactoryID = string.Empty;
            string resFloorID = string.Empty;
            string resProdGroup = string.Empty;

            SEMEqp eqp;
            if (InputMart.Instance.SEMEqp.TryGetValue(resID, out eqp))
            {
                resFactoryID = eqp.FactoryID;
                resFloorID = eqp.FloorID;
                resProdGroup = eqp.ResProductGroup;
            }

            ARRANGE_LOG al = new ARRANGE_LOG
            {
                LOT_ID = pwi.LotID,
                CONDITION_SET = condSet == null ? "" : condSet.Key,
                MODULE = callingModule,
                JOB_CATEGORY = category,
                PATH = path,
                OPER_ID = stepID,
                PRODUCT_ID = productID,
                RESOURCE_ID = resID,
                RESULT = result,
                JOB_RESULT = result == "Arranged" ? "O" : "X",
                CONS_AND_VALUES = condSet == null ? "" : pwi.StepArrangeLogs[condSet.Key].Select(x => $"{condSet.First().JOB_CATEGORY} {x.Key} {x.Value}").ListToString(" / "),
                WIP_AVAILABLE_TIME = pwi.WipInfo.AvailableTime,
                CALC_AVAILABLE_TIME = calaAvailTime,
                RESOURCE_FACTORY_ID = resFactoryID,
                RESOURCE_FLOOR_ID = resFloorID,
                RESOURCE_PROD_GROUP = resProdGroup,
                WIP_FACTORY_ID = pwi.WipInfo.FactoryID,
                WIP_FLOOR_ID = pwi.WipInfo.FloorID,
                WIP_RES_PROD_GROUP = pwi.WipInfo.ResProductGroup
            };

            InputMart.Instance.ArrLogDic.Add(key, al);

            if (isWriteLog)
            {
                string key2 = CommonHelper.CreateKey(productID, stepID);
                isWriteLog = pwi.PlanWip.ArrangeLogList.Contains(key2) ? false : true;

                if (isWriteLog)
                {
                    OutputMart.Instance.ARRANGE_LOG.Add(al);
                    pwi.PlanWip.ArrangeLogList.Add(key2);
                }
            }
        }

        public static void WriteArrangeLog(LOT_ARRANGE lotArrange, SEMWipInfo wip, string category, string reason)
        { 
            try 
            {
                string resFactoryID = string.Empty;
                string resFloorID = string.Empty;
                string resProdGroup = string.Empty;

                SEMEqp eqp;
                if (InputMart.Instance.SEMEqp.TryGetValue(lotArrange.RESOURCE_ID, out eqp))
                {
                    resFactoryID = eqp.FactoryID;
                    resFloorID = eqp.FloorID;
                    resProdGroup = eqp.ResProductGroup;
                }

                ARRANGE_LOG al = new ARRANGE_LOG
                {
                    LOT_ID = lotArrange.LOT_ID,
                    CONDITION_SET = "",
                    MODULE = "PERSIST",
                    JOB_CATEGORY = category,
                    PATH = "",
                    OPER_ID = lotArrange.OPER_ID,
                    PRODUCT_ID = lotArrange.PRODUCT_ID,
                    RESOURCE_ID = lotArrange.RESOURCE_ID,
                    RESULT = "Filtered",
                    JOB_RESULT = "X",
                    CONS_AND_VALUES = reason,
                    WIP_AVAILABLE_TIME = wip == null ? DateTime.MinValue : wip.AvailableTime,
                    RESOURCE_FACTORY_ID = resFactoryID,
                    RESOURCE_FLOOR_ID = resFloorID,
                    RESOURCE_PROD_GROUP = resProdGroup,
                    WIP_FACTORY_ID = wip == null ? "" : wip.FactoryID,
                    WIP_FLOOR_ID = wip == null ? "" : wip.FloorID,
                    WIP_RES_PROD_GROUP = wip == null ? "" : wip.ResProductGroup
                };
                OutputMart.Instance.ARRANGE_LOG.Add(al);
            }
            catch (Exception e)
            { 
                WriteLog.WriteErrorLog(e.Message);
            }
        }


        public static void CachingArrangeResult(ICollection<string> result, SEMPegWipInfo pwi, string stepID)
        {
            foreach (string eqpID in result)
            {
                SEMEqp eqp = null;
                if (InputMart.Instance.SEMEqp.TryGetValue(eqpID, out eqp) == true)
                {
                    pwi.ArrangedEqps.Add(stepID, eqp);
                    eqp.ArrangedWip.Add(pwi.WipInfo);

                    //writeArrLog(pwi.LotID, stepID, productID, eqpID, "Arranged", "Exempt from RESOURCE_JOB_CONDITION", null, callingModule, null);
                }
                else
                {
                    //WriteLog.WriteErrorLog($"장비를 찾을 수 없습니다. EPQ_ID : {eqpID}");
                }
            }

            // Condition Set 기록
            foreach (var al in InputMart.Instance.ArrLogDic)
            {
                if(pwi.ConditionSet.ContainsKey(al.Value.RESOURCE_ID) == false && al.Value.JOB_RESULT == "O")
                    pwi.ConditionSet.Add(al.Value.RESOURCE_ID, al.Value.CONDITION_SET);
            }
        }

        public static void EditArrangeLog(List<string> result)
        {
            foreach (var al in InputMart.Instance.ArrLogDic)
            {
                if (result.Contains(al.Value.RESOURCE_ID))
                    al.Value.RESULT = "Arranged";
                else
                    al.Value.RESULT = "Filtered";
            }
        }
    }
    class EqpStringComparer : IComparer<string>
    {
        private SEMWipInfo wip;
        public EqpStringComparer(SEMWipInfo semWip)
        {
            this.wip = semWip;
        }
        public int Compare(string x, string y)
        {
            SEMEqp eqpx = InputMart.Instance.SEMEqp[x];
            SEMEqp eqpy = InputMart.Instance.SEMEqp[y];

            int cmp = 0;

            // Since FloorID is a combination of a factory and floor,
            // this will be checking the same factory at the same time.
            if (this.wip.FloorID == eqpx.FloorID && this.wip.FloorID != eqpy.FloorID)
                cmp = -1;
            else if (this.wip.FloorID != eqpx.FloorID && this.wip.FloorID == eqpy.FloorID)
                cmp = 1;

            if (cmp != 0)
                return cmp;

            if (this.wip.FactoryID == eqpx.FactoryID && this.wip.FactoryID != eqpy.FactoryID)
                cmp = -1;
            else if (this.wip.FactoryID != eqpx.FactoryID && this.wip.FactoryID == eqpy.FactoryID)
                cmp = 1;

            if (cmp != 0)
                return cmp;

            return cmp;
        }
    }
}