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
using Mozart.SeePlan.General.DataModel;
using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;
using Mozart.SeePlan;
using Mozart.SeePlan.DataModel;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class AgentHelper
    {
        public static SEMWorkAgent _agent;

        public static void SetAgent()
        {
            _agent = AoFactory.Current.JobChangeManger.GetAgent("DEFAULT") as SEMWorkAgent;
        }


        public static JobConditionGroup GetJobConditionGroup(this SEMWorkAgent agent, string jobCondKey)
        {
            var jgroup = agent.TryGetJobConditionGroup(jobCondKey);

            if (jgroup == null)
            {
                jgroup = CreateHelper.CreateJobContionGroup(jobCondKey);

                agent.JobConditionGroups.Add(jgroup);
            }

            return jgroup;
        }

        public static JobConditionGroup TryGetJobConditionGroup(this SEMWorkAgent agent, string jobCondKey)
        {
            var jgroup = agent.JobConditionGroups.Where(x => x.Key == jobCondKey).FirstOrDefault();

            return jgroup;
        }

        public static string GetStepKey(this JobConditionGroup jGroup)
        {
            if (jGroup.StepKey.IsNullOrEmpty())
            {
                var ws = jGroup.Steps.FirstOrDefault();
                if (ws == null)
                    return string.Empty;

                jGroup.StepKey = ws.StepKey;
            }

            return jGroup.StepKey;
        }

        public static SEMWorkStep CreateDummyWorkStep(SEMWorkAgent agent, string stepID)
        {
            string jobGroupKey = "DummyJobGroup" + stepID;
            JobConditionGroup jgroup = agent.GetJobConditionGroup(jobGroupKey);

            // Work Group
            string workGroupKey = "DummyWorkGroup" + stepID;
            SEMWorkGroup wg = agent.GetGroup(workGroupKey) as SEMWorkGroup; ;
            wg.JobConditionKey = jobGroupKey;
            wg.JobConditionGroup = jgroup;
            wg.GroupKey = workGroupKey;
            jgroup.Groups.Add(wg);

            SEMGeneralStep targetStep = InputMart.Instance.SEMGeneralStep.Rows.Where(x => x.StepID == stepID).FirstOrDefault();

            SEMWorkStep ws = wg.GetStep(stepID, targetStep) as SEMWorkStep;
            ws.JobConditionGroup = jgroup;
            ws.IsDummy = true;
            jgroup.Steps.Add(ws);

            var eqps = AoFactory.Current.Equipments;
            foreach (var e in eqps)
            {
                var aeqp = e.Value as SEMAoEquipment;
                var eqp = aeqp.Target as SEMEqp;

                if (eqp.OperIDs.Contains(stepID))
                {                 
                    ws.AddLoadableEqp(aeqp);
                    aeqp.WorkStepList.Add(ws);

                    jgroup.LoadableEqps.Add(aeqp);
                }
            }

            WORK_LOT_LOG log = new WORK_LOT_LOG();
            log.JOB_COND_KEY = jobGroupKey;
            log.WORK_GROUP_KEY = workGroupKey;
            log.WORK_STEP = stepID;
            log.LOADABLE_EQP = ws.LoadableEqps.Select(x => x.EqpID).OrderBy(x => x).ListToString();
            log.LOADABLE_EQP_CNT = ws.LoadableEqps.Count;
            OutputMart.Instance.WORK_LOT_LOG.Add(log);

            agent.DummyWorkSteps.Add(stepID, ws);

            return ws;
        }

        public static SEMWorkStep GetDummyWorkStep(this SEMWorkAgent agent, string stepID)
        {
            SEMWorkStep ws;
            if (agent.DummyWorkSteps.TryGetValue(stepID, out ws) == false)
                ws = CreateDummyWorkStep(agent, stepID);

            return ws;
        }

        public static void AddDummyWorkStep(this SEMWorkAgent agent, SEMAoEquipment aeqp)
        {
            var eqp = aeqp.Target as SEMEqp;
            SEMWorkStep ws;
            if (eqp.OperIDs.Contains("SG4430"))
                ws = agent.GetDummyWorkStep("SG4430");
            else
                ws = agent.GetDummyWorkStep("SG3910");

            ws.AddLoadedEqp(aeqp);

            aeqp.WorkStep = ws;
        }

        public static bool IsAgentTargetStep(SEMGeneralStep step)
        {
            if (step == null)
                return false;

            if (step.StdStep.IsProcessing)
            {
                if (step.StdStep.RouteID == "SGC117")
                {
                    if (step.StdStep.OperName.Contains("외관선별"))
                    {
                        return true;
                    }
                    else if (step.StdStep.OperName.Contains("전극소성"))
                    {
                        return false;
                    }
                    else if (step.StdStep.OperName.Contains("전극도포"))
                    {
                        return false;
                    }
                    else
                    {
                        WriteLog.WriteErrorLog($"알 수 없는 RouteName :{step.StdStep.RouteName} {step.StepID}");
                        return false;
                    }
                }
                else if (step.StdStep.RouteID == "SGC118")
                {
                    return false;
                }
                else if (step.StdStep.RouteID == "SGC119")
                {
                    return true;
                }
                else if (step.StdStep.RouteID == "SGC120")
                {
                    return true;
                }
                else
                {
                    WriteLog.WriteErrorLog($"알 수 없는 RouteID :{step.StdStep.RouteID}");
                    return false;
                }
            }
            else
                return false;
        }

        //
        // Lot의 현재 step에서 Target Step을 구함
        //
        public static SEMGeneralStep GetAgentTargetStep(this SEMLot lot, bool isRun)
        {
            SEMGeneralStep ts = isRun ? lot.GetNextOperForJC(lot.CurrentSEMStep) : lot.CurrentSEMStep;

            while (ts != null && IsAgentTargetStep(ts) == false)
            {               
                ts = lot.GetNextOperForJC(ts);
            }

            return ts;
        }

        //
        // Lot의 지정된 step에서 Target step을 구함
        //
        public static SEMGeneralStep GetAgentTargetStep(this SEMLot lot, SEMGeneralStep step)
        {
            SEMGeneralStep ts = step;

            while (ts != null && IsAgentTargetStep(ts) == false)
            {
                ts = lot.GetNextOperForJC(ts);
            }

            return ts;
        }

        public static void SetLoadedEqp(SEMLot lot, SEMEqp eqp)
        {
            var agentInitControl = ServiceLocator.Resolve<JobChangeInit>();

            var wagentName = agentInitControl.GetWorkAgentName(lot);
            if (string.IsNullOrEmpty(wagentName))
                return;

            var wmanager = AoFactory.Current.JobChangeManger;

            SEMWorkAgent agent = wmanager.GetAgent(wagentName) as SEMWorkAgent;
            if (agent == null)
                return;

            SEMGeneralStep targetStep = lot.GetAgentTargetStep(false);     // Run재공의 현재 work group을 알기 위해 isrun은 false
            if (targetStep == null)
                return;

            // JobConditionGroup                                                // work group 상위 개념, job condition이 같은 group 끼리 묶음
            string jobCondKey = lot.GetJobConditionGroupKey(targetStep);
            JobConditionGroup jgroup = agent.GetJobConditionGroup(jobCondKey);
            if (jgroup == null)
                return;

            if (AoFactory.Current.Equipments.TryGetValue(eqp.EqpID, out var aeqp))
            {
                InputMart.Instance.InitLoadedEqpDic.Add(eqp.EqpID, jgroup);
                jgroup.InitLoadedEqps.Add(aeqp as SEMAoEquipment);
                WriteLog.WriteRunWorkLotLog(jgroup, lot, aeqp.EqpID);
            }
        }


        public static void WriteJobChangeInitLog(WorkStep ws)
        {
            SEMWorkStep wStep = ws as SEMWorkStep;
            SEMWorkGroup wg = wStep.Group as SEMWorkGroup;
            DateTime now = AoFactory.Current.NowDT;
            //var wsdata = ws.Data as WorkStepData;
            INIT_LOG log = new INIT_LOG();

            log.VERSION_NO = ModelContext.Current.VersionNo;
            log.WORK_GROUP_KEY = AgentHelper.GetWorkGroupName(ws);
            log.JOB_COND_KEY = wg.JobConditionKey;
            log.WORK_STEP = ws.Key.ToString();
            log.LOADABLE_EQP_CNT = ws.LoadableEqps.Count();
            string loadableEqpStrings = null;
            foreach (var eqp in ws.LoadableEqps)
                loadableEqpStrings += eqp.EqpID + ", ";
            log.LOADABLE_EQP = loadableEqpStrings;
            log.LOADED_EQP_CNT = ws.LoadedEqps.Count();
            string loadedEqpStrings = null;

            foreach (var weqp in ws.LoadedEqps)
            {
                var aeqp = weqp.Target;
                loadedEqpStrings += aeqp.EqpID + ", ";
            }

            log.LOADED_EQP = loadedEqpStrings;

            List<WorkLot> wLots = ws.Wips;
            int runWip = 0;
            int waitWip = 0;
            int inflowWip = 0;
            string lotIDs = string.Empty;
            foreach (var wLot in wLots)
            {
                var lot = wLot.Lot as SEMLot;

                if (IsAgentTargetStep(lot.CurrentSEMStep) && lot.CurrentState.ToString().ToUpper() == "RUN")
                {
                    runWip += lot.UnitQty;
                }
                else if (lot.CurrentState.ToString().ToUpper() == "WAIT")
                {
                    waitWip += lot.UnitQty;
                }
                else
                {
                    inflowWip += lot.UnitQty;
                }
            }

            log.RUN_WIP = runWip;
            log.WAIT_WIP = waitWip;
            log.IN_PROFILE_WIP = inflowWip;

            log.INFLOW_WIP_CNT = wStep.TotalWips.Count;
            log.INFLOW_WIPS = wStep.TotalWips.Select(x => x.LotID).ListToString();

            //log.REMAIN_WORK
            OutputMart.Instance.INIT_LOG.Add(log);
        }

        internal static void WriteAssignEqpLog(SEMWorkStep ws, List<AssignEqp> assignEqps, Dictionary<AssignEqp, double> eqpSetupTimeDic, List<string> alreadyLoadedEqpIDs, List<string> arrangeFilterEqps, List<string> timeFilterEqps, List<string> upHoldFilterEqps, List<string> lotArrangeFilterEqps)
        {
            Dictionary<string, double> sortedEqpSetupTime = eqpSetupTimeDic.OrderBy(o => o.Value).ToDictionary(x => x.Key.Target.EqpID, x => x.Value);
            DateTime now = AoFactory.Current.NowDT;

            FILTER_ASSIGN_EQP_LOG log = new FILTER_ASSIGN_EQP_LOG();

            log.EVENT_TIME = now.DbToString();
            log.WORK_STEP = ws.Key.ToString();
            log.WORK_GROUP_KEY = AgentHelper.GetWorkGroupName(ws);
            log.EQPS = string.Join(", ", assignEqps.Select(x => x.Target.EqpID).ToList());
            log.EQP_CNT = assignEqps.Count;
            log.RETURN_ASSIGN_EQPS = string.Join(", ", sortedEqpSetupTime.Select(i => $"{i.Key}({i.Value})").ToList());
            log.RETURN_ASSIGN_CNT = sortedEqpSetupTime.Count;
            log.ALREADY_LOADED_FILTER_EQPS = string.Join(", ", alreadyLoadedEqpIDs);
            log.ALREADY_LOADED_FILTER_EQP_CNT = alreadyLoadedEqpIDs.Count;
            log.ARRANGE_FILTER_EQPS = string.Join(", ", arrangeFilterEqps);
            log.ARRANGE_FILTER_CNT = arrangeFilterEqps.Count;
            log.TIME_FILTER_EQPS = string.Join(", ", timeFilterEqps);
            log.TIME_FILTER_EQP_CNT = timeFilterEqps.Count;
            log.UP_HOLD_FILTER_EQPS = string.Join(", ", upHoldFilterEqps);
            log.UP_HOLD_FILTER_EQP_CNT = upHoldFilterEqps.Count;
            log.LOT_ARRANGE_FILTER_EQPS = string.Join(", ", lotArrangeFilterEqps);
            log.LOT_ARRANGE_FILTER_EQP_COUNT = lotArrangeFilterEqps.Count;

            OutputMart.Instance.FILTER_ASSIGN_EQP_LOG.Add(log);
        }


        public static void WriteWSDecisionLog(SEMWorkStep ws, int phase, string reason, string detailReason, int rank)
        {
            DateTime now = AoFactory.Current.NowDT;
            var wsdata = ws.Data as WorkStepData;
            DECISION_LOG log = new DECISION_LOG();

            List<WorkLot> wLots = ws.Wips;

            log.EVENT_TIME = now.DbToString();
            log.PHASE = phase;
            log.VERSION_NO = ModelContext.Current.VersionNo;
            log.RANK = rank;
            log.WORK_GROUP = AgentHelper.GetWorkGroupName(ws);
            log.WORK_STEP = ws.Key.ToString();
            log.DECISION_TYPE = wsdata.DecidedOperationType.ToString();
            log.DECISION_REASON = reason;
            log.DECISION_DETAIL_REASON = detailReason;

            log.INPROFILE_WIP_QTY = ws.Wips.Sum(x => x.Lot.UnitQty);
            log.NEEDED_CAPA = wsdata.NeededCapa;
            log.INFLOW_QTY = wsdata.InflowWipQty;
            log.DEMAND_QTY = (int)wsdata.InflowDemandQty;
            log.CAPA = wsdata.Capa;
            log.CAPA_DETAIL = wsdata.CapaDetail;

            log.LOADED_EQP_CNT = ws.LoadedEqpCount;
            log.LOADED_EQPS = string.Join(",", ws.LoadedEqpIDs);

            log.TOTAL_LOADED_EQP_CNT = ws.GetLoadedEqpCount();
            log.TOTAL_LOADED_EQPS = ws.GetLoadedEqpList().Select(x => x.EqpID).ListToString();

            log.CURRENT_WORK_LOAD_HR = Math.Round(wsdata.WorkLoadHour, 2);
            log.CURRENT_PROFILE_END_TIME = wsdata.CurrentProfileEnd;
            log.CHECK_UP_EQP_ID = wsdata.CheckUpEqp;
            log.CHECK_UP_PROFILE_END_TIME = wsdata.CheckUpPrfileEnd;
            log.CHECK_UP_WORK_LOAD_HR = wsdata.CheckUpWorkLoadHr;
            log.PRIORITY = wsdata.Priority;
            log.PRIORITY_CALC_LOG = wsdata.PriorityLog;

            if (wsdata.FastLot != null)
            {
                log.FAST_LOT_ID = wsdata.FastLot.Lot.LotID;
                log.LPST = wsdata.Lpst;
            }

            log.INFLOW_WIP_CNT = wsdata.InflowWipCnt;
            log.INFLOW_WIPS = wsdata.InflowWips.ListToString();

            log.WIP_CNT = ws.Wips.Count;
            log.WIPS = ws.Wips.Select(x => x.Lot.LotID).ListToString();

            wsdata.DecisionLogs.Add(log);
            OutputMart.Instance.DECISION_LOG.Add(log);
        }

        private static string ToDemandOper(string step)
        {
            if (step == "SG3910")
                return "SG4090";


            return "SG4460";
        }

        public static int GetTypePriority(WorkStep workStep)
        {
            int result = 100;
            if (workStep.OperationType == OperationType.Down)
                result = 0;
            else if (workStep.OperationType == OperationType.Keep)
                result = 1;
            else if (workStep.OperationType == OperationType.Up)
                result = 2;

            return result;
        }

        //public static void SetWSPriority(this SEMWorkStep ws, string reason, WorkLot resFixLot = null)
        //{
        //    var wsdt = ws.Data as WorkStepData;
        //    DateTime now = AoFactory.Current.NowDT;

        //    double priority = 0;
        //    string priorityLog = string.Empty;

        //    // Res Fix Lot
        //    if (resFixLot != null)
        //    {
        //        SEMLot lot = resFixLot.Lot as SEMLot;

        //        // Res Fix 점수
        //        priority += 10000000000;
        //        priorityLog = priorityLog + $"RES_FIX(10000000000)/";

        //        // 작업조건 일치
        //        SEMEqp eqp = lot.Wip.LotArrangedEqpDic[ws.Key.ToString()].First();
        //        if (eqp.IsNeedSetup(lot) == false)
        //        {
        //            priority += 1000;
        //            priorityLog = priorityLog + $"NO_SETUP(1000)/";
        //        }

        //        // ReelLabel 유무
        //        if (ws.HasReelLabeledWip())
        //        {
        //            priority += 100;
        //            priorityLog = priorityLog + $"LABEL(100)/";
        //        }

        //        // 납기
        //        DateTime lpst = lot.GetLPST(ws.Key.ToString());
        //        double lpstGapDay = lpst == DateTime.MaxValue ? double.NaN : Math.Round((now - lpst).TotalDays, 2);
        //        double dueScore = ws.GetDueScore();
        //        string dueState = GetDueState(lpstGapDay);
        //        priority += dueScore / 100000;
        //        priorityLog = priorityLog + $"{dueState}({dueScore / 100000})/";

        //        priority -= lpstGapDay / 10;
        //        priorityLog = priorityLog + $"LPST({lpstGapDay / 10})/";

        //        // 도착 시간 빠름
        //        double waitDay = Math.Round((now - lot.PreEndTime).TotalDays);
        //        priority += waitDay;
        //        priorityLog = priorityLog + $"WAIT({waitDay})/";
        //    }
        //    else
        //    {
        //        if (wsdt.FastLot != null)
        //        {
        //            SEMLot lot = wsdt.FastLot.Lot as SEMLot;
        //            DateTime lpst = lot.GetLPST(ws.Key.ToString());
        //            double lpstGapDay = Math.Round((now - lpst).TotalDays, 2);
        //            string dueState = GetDueState(lpstGapDay);
        //            double dueScore = ws.GetDueScore();
        //            double waitDay = Math.Round((now - lot.PreEndTime).TotalDays);

        //            // ReelLabel 유무
        //            if (ws.HasReelLabeledWip())
        //            {
        //                priority += 1000000;
        //                priorityLog = priorityLog + $"LABEL(1000000)/";
        //            }

        //            // 납기 factor
        //            priority += dueScore;
        //            priorityLog = priorityLog + $"dueState({dueState})/";

        //            priority += lpstGapDay * 10;
        //            priorityLog = priorityLog + $"LPST({lpstGapDay * 10})/";

        //            // 도착 시간 빠름
        //            priority += waitDay * 0.1;
        //            priorityLog = priorityLog + $"WAIT({waitDay * 0.1})/";


        //            if (ws.GetLoadedEqpCount() == 0) //step.LoadedEqpCount == 0)
        //            {
        //                if (ws.HasUrgentWip())
        //                {
        //                    priority += 10000;
        //                    priorityLog = priorityLog + $"/UrgentWipNoEqp(10000)";
        //                }
        //                else
        //                {
        //                    priority += 1;
        //                    priorityLog = priorityLog + $"/NoEqp(1)";
        //                }
        //            }

        //        }
        //        else
        //        {
        //            priority = -9999999999;
        //            priorityLog = $"NoWip";
        //        }
        //    }

        //    wsdt.Priority = priority;
        //    wsdt.PriorityLog = priorityLog;
        //}

        public static bool HasReelLabeledWip(this SEMWorkStep ws)
        {
            if (ws == null)
                return false;

            var inflowLots = ws.Inflows.Where(x => x.AvailableTime < AoFactory.Current.NowDT.AddHours(1)).ToList();
            bool result = inflowLots.Any(x => (x.Lot as SEMLot).Wip.IsReelLabeled);

            return result;
        }

        public static bool HasUrgentWip(this SEMWorkStep ws)
        {
            if (ws == null)
                return false;

            var inflowLots = ws.Inflows.Where(x => x.AvailableTime < AoFactory.Current.NowDT.AddHours(1)).ToList();
            bool result = inflowLots.Any(x => (x.Lot as SEMLot).Wip.IsUrgent);

            return result;
        }

        public static SEMWipInfo GetUrgentWip(this SEMWorkStep ws)
        {
            if (ws == null)
                return null;

            var inflowLots = ws.Inflows.Where(x => x.AvailableTime < AoFactory.Current.NowDT.AddHours(1) && (x.Lot as SEMLot).Wip.IsUrgent).ToList();

            var result = inflowLots.OrderBy(x => (x.Lot as SEMLot).Wip.UrgentPriority).FirstOrDefault();

            if (result != null)
                return (result.Lot as SEMLot).Wip;

            return null;
        }

        public static int GetUrgentPriority(this SEMWorkStep ws)
        {
            if (ws == null)
                return 99999;

            var wip = ws.GetUrgentWip();

            if (wip == null)
                return 99999;

            return wip.UrgentPriority;
        }


        public static double GetDueScore(this SEMWorkStep ws)
        {
            double result = 0;

            var lpstGapDay = ws.GetLpatGapDay();
            var dueState = GetDueState(lpstGapDay);

            GetDueScore(dueState);

            return result;
        }

        public static double GetDueScore(string dueState)
        {
            double result = 0;

            if (dueState == Constants.Delay)
                result = 100000;
            else if (dueState == Constants.Normal)
                result = 10000;
            else if (dueState == Constants.Precede)
                result = 1000;
            else
                result = 0;

            return result;
        }

        public static int GetLoadedEqpCount(this SEMWorkStep ws)
        {
            //if (ws.WsData != null && ws.WsData.LoadedEqpCount != 0)
            //    return ws.WsData.LoadedEqpCount;

            var list = ws.GetLoadedWorkEqpList();

            return list.Count;
        }


        public static List<SEMAoEquipment> GetLoadedEqpList(this SEMWorkStep ws)
        {
            var loadedWorkEqps = ws.GetLoadedWorkEqpList();

            List<SEMAoEquipment> result = new List<SEMAoEquipment>();

            loadedWorkEqps.ForEach(x => result.Add(x.Target as SEMAoEquipment));

            return result;
        }

        public static List<SEMWorkEqp> GetLoadedWorkEqpList(this SEMWorkStep ws)
        {
            //if (ws.WsData != null && ws.WsData.LoadedEqpCount != 0)
            //    return ws.WsData.LoadedEqpList;

            List<SEMWorkEqp> result = new List<SEMWorkEqp>();

            foreach (var wStep in ws.JobConditionGroup.Steps)
            {
                if (ws.IsResFix == false && wStep.IsResFix)
                    continue;

                foreach (var e in wStep.LoadedEqps)
                {
                    var weqp = e as SEMWorkEqp;

                    if (ws.LoadableEqps.Contains(e.Target))
                        result.Add(weqp);
                }
            }

            //캐싱
            //if (ws.WsData != null)
            //    ws.WsData.LoadedEqpList.AddRange(result);

            return result;
        }


        public static DateTime GetMinLPST(this WorkStep step)
        {
            DateTime result = DateTime.MaxValue.AddSeconds(-1); //Max value 보다는 조금 작은 시간

            foreach (var wLot in step.Wips)
            {
                //var pt = (wLot.Lot as SEMLot).Wip.PeggedTargets.FirstOrDefault();
                //var duedate = pt == null ? DateTime.MaxValue : pt.DueDate;

                var lpst = (wLot.Lot as SEMLot).GetLPST(step.Key.ToString());

                result = lpst < result ? lpst : result;
                //result = Math.Max((now - lpst).TotalDays, result);
            }

            return result;
        }

        public static SEMWorkStep GetUpWorkStep(this JobConditionGroup jGroup, SEMAoEquipment aeqp)
        {
            // 현 jobGroup 내에서 우선순위가 가장 높은 work step을 return
            SEMWorkStep result = null;
            var min = double.MaxValue;
            var now = AoFactory.Current.NowDT;
            foreach (var ws in jGroup.Steps)
            {
                if (ws.LoadableEqps.Contains(aeqp) == false)
                    continue;

                var upTimeFence = aeqp.LotEndTime == null ? now.AddHours(3) : aeqp.LotEndTime.AddHours(3);

                var inflowQty = ws.GetInflowQty(upTimeFence);

                var loadedEqpCnt = ws.GetLoadedEqpCount() + 1;  // +1을 해주는이유, 0으로 나누기 방지

                var value = inflowQty / loadedEqpCnt;

                if (value < min)
                {
                    min = value;
                    result = ws;
                }


            }

            return result;
        }

        public static double GetInflowQty(this SEMWorkStep ws, DateTime fence)
        {
            double result = 0;
            foreach (var l in ws.Inflows)
            {
                if (l.AvailableTime <= fence)
                    result += l.Lot.UnitQtyDouble;
            }

            return result;
        }


        public static DateTime GetProfileEndTime(WorkStep ws, Time gap)
        {
            WorkLot sel = null;
            var atime = ws.Agent.Now;

            foreach (var wlot in ws.Profiles.Values)
            {
                var t1 = wlot.AvailableTime - atime;
                if (t1.TotalHours > gap)
                    break;

                sel = wlot;

                if (sel != null)
                    atime = sel.OutTime;
            }

            return (DateTime)atime;
        }

        public static string GetWorkGroupKey(this SEMLot lot, SEMGeneralStep targetStep, WorkAgent agent)
        {
            string workGroupKey = lot.GetJobConditionGroupKey(targetStep);

            // Res Fix
            if (lot.Wip.IsResFixLotArrange && lot.Wip.LotArrangeOperID == targetStep.StepID)
            {
                int num;
                if (InputMart.Instance.ResFixCntDic.TryGetValue(lot.LotID, out num))
                {
                    workGroupKey += $"@ResFix{num}";
                }
                else
                {
                    num = InputMart.Instance.ResFixCnt++;
                    InputMart.Instance.ResFixCntDic.Add(lot.LotID, num);
                    workGroupKey += $"@ResFix{num}";
                }
                return workGroupKey;
            }

            var workGroup = agent.GetSameArrangeGroup(lot, targetStep.StepID, workGroupKey);

            if (workGroup != null)
            {
                return workGroup.Key.ToString();
            }
            else
            {
                int arrCnt = 0;
                if (InputMart.Instance.WorkGroupArrangeCntDic.TryGetValue(workGroupKey, out arrCnt) == false)
                {
                    InputMart.Instance.WorkGroupArrangeCntDic.Add(workGroupKey, 0);
                    workGroupKey += $"@Arrange{arrCnt}";
                }
                else
                {
                    InputMart.Instance.WorkGroupArrangeCntDic[workGroupKey] = arrCnt + 1;
                    workGroupKey += $"@Arrange{arrCnt + 1}";
                }

                return workGroupKey;
            }

        }

        public static string GetJobConditionGroupKey(this SEMLot lot, SEMGeneralStep targetStep)
        {
            //SEMGeneralStep targetStep = lot.GetAgentTargetStep(isRun);//GetAgentTargetStep(lot.CurrentSEMStep, isRun);
            if (targetStep == null)
                return "NO_PROCESSING_GROUP";

            SEMProduct targetProd = lot.GetProduct(targetStep.StepID);

            string workGroupKey = string.Empty;
            if (targetStep.StdStep.RouteID == "SGC117")
            {
                if (targetStep.StdStep.OperName.Contains("외관선별"))
                {
                    workGroupKey = CommonHelper.CreateKey(targetProd.ChipSize, targetProd.ThicknessCodeValue);
                }
                else if (targetStep.StdStep.OperName.Contains("전극소성"))
                {
                    //[TODO] 
                    string profile = targetProd.TermFiringProfile.Where(x => x.OPER_ID == targetStep.StepID || x.OPER_ID == "ALL").Select(x => x.PROFILE_CODE).FirstOrDefault();

                    workGroupKey = CommonHelper.CreateKey(targetProd.ProductID, targetStep.StepID, profile);
                }
                else if (targetStep.StdStep.OperName.Contains("전극도포"))
                {
                    //[TODO] 
                    string pasteGroup = targetProd.ModelOutsidePaste.Where(x => x.OPER_ID == targetStep.StepID).Select(x => x.PASTE_GROUP).FirstOrDefault();

                    workGroupKey = CommonHelper.CreateKey(targetStep.StepID, targetProd.ChipSize, targetProd.ThicknessCodeValue, pasteGroup);
                }
                else
                {
                    workGroupKey = "UNKNOWN01";
                    WriteLog.WriteErrorLog($"알 수 없는 RouteName :{targetStep.StdStep.RouteName} {lot.LotID} {targetStep.StepID}");
                }
            }
            else if (targetStep.StdStep.RouteID == "SGC118")
            {
                workGroupKey = CommonHelper.CreateKey(targetProd.ProductID, targetStep.StepID);
            }
            else if (targetStep.StdStep.RouteID == "SGC119")
            {
                string recipeID = lot.Wip.PlatingRecipe.Where(x => x.PRODUCT_ID == targetProd.ProductID && x.OPER_ID == targetStep.StepID || x.OPER_ID == "ALL").Select(x => x.RECIPE_ID).FirstOrDefault();

                workGroupKey = CommonHelper.CreateKey(targetProd.ProductID, targetStep.StepID, recipeID);
            }
            else if (targetStep.StdStep.RouteID == "SGC120")
            {
                workGroupKey = targetProd.ProductID;
            } 
            else
            {
                workGroupKey = "UNKNOWN02";

                WriteLog.WriteErrorLog($"알 수 없는 RouteID :{targetStep.StdStep.RouteID} {lot.LotID}");
            }

            return workGroupKey;
        }

        //if (targetStep.StepID == "SG3500" || targetStep.StepID == "SG3531" || targetStep.StepID == "SG7964")
        //{ }
        //else if (targetStep.StepID == "SG3520" || targetStep.StepID == "SG3550" || targetStep.StepID == "SG3570" || targetStep.StepID == "SG3585")
        //{ }
        //else if (targetStep.StepID == "SG3530" || targetStep.StepID == "SG3560" || targetStep.StepID == "SG3580" || targetStep.StepID == "SG5000"|| targetStep.StepID == "SG5221")
        //{ }
        //else if (targetStep.StepID == "SG3530")
        //{ }

        public static string GetJobConditionGroupKey(this SEMEqp eqp)
        {
            string jobCondkey = string.Empty;

            if (eqp.OperIDs.Contains("SG3910"))
            {
                string chipSize = string.Empty;
                string thicknessValue = string.Empty;
                string prodGroup = string.Empty;

                eqp.StepJobConditions.TryGetValue("C000000014", out chipSize);
                eqp.StepJobConditions.TryGetValue("C000000015", out thicknessValue);
                eqp.StepJobConditions.TryGetValue("C000000064", out prodGroup);

                //prodGroup = prodGroup == "AUTOMOTIVE" ? "AUTOMOTIVE" : "ITorINDUSTRY";

                jobCondkey = CommonHelper.CreateKey(chipSize, thicknessValue, prodGroup);//, demandWeek);
            }
            else if (eqp.OperIDs.Contains("SG4430"))
            {
                string prodID = string.Empty;
                string chipSize = string.Empty;
                string carrierType = string.Empty;
                string tapingCode = string.Empty;
                string tapingThickness = string.Empty;
                string reelInch = string.Empty;
                string pitch = string.Empty;

                eqp.StepJobConditions.TryGetValue("C000000001", out prodID);
                //eqp.StepJobConditions.TryGetValue("C000000014", out chipSize);
                //eqp.StepJobConditions.TryGetValue("C000000038", out carrierType);
                eqp.StepJobConditions.TryGetValue("C000000040", out tapingCode);
                eqp.StepJobConditions.TryGetValue("C000000041", out tapingThickness);
                //eqp.StepJobConditions.TryGetValue("C000000047", out reelInch);
                //eqp.StepJobConditions.TryGetValue("C000000150", out pitch);

                jobCondkey = CommonHelper.CreateKey(prodID, tapingCode, tapingThickness);//, demandWeek);
            }
            else
            {
                WriteLog.WriteErrorLog($"{eqp.EqpID}의 OperID를 찾을 수 없습니다.");
            }

            return jobCondkey;
        }

        public static List<WorkGroup> GetWorkGroupsByJobCondKey(this WorkAgent agent, string jobConditionKey)
        {
            List<WorkGroup> result = agent.Groups.Where(x => (x as SEMWorkGroup).JobConditionKey == jobConditionKey).ToList();

            return result;
        }


        public static WorkGroup GetSameArrangeGroup(this WorkAgent agent, SEMLot lot, string stepID, string workGroupKey)
        {
            var groups = agent.GetWorkGroupsByJobCondKey(workGroupKey);
            if (groups.Count == 0)
                return null;

            var arr = lot.GetArrange(stepID);
            foreach (var wg in groups)
            {
                var workGroup = wg as SEMWorkGroup;
                var ws = wg.TryGetStep(stepID);

                if (ws == null)
                    continue;

                if (ws.LoadableEqps.Count != arr.Count)
                    continue;

                if (workGroup.IsResFix)
                    continue;

                bool isSameArrange = true;
                foreach (var eqp in ws.LoadableEqps)
                {
                    if (arr.Contains(eqp.EqpID) == false)
                    {
                        isSameArrange = false;
                        break;
                    }
                }

                if (isSameArrange)
                    return workGroup;
            }

            return null;
        }


        public static string GetWorkGroupName(WorkStep ws)
        {
            return ws.Group.Key.ToString();
        }


        public static IEnumerable<AoEquipment> GET_LOADABLE_EQPS(WorkStep wstep)
        {
            HashSet<string> eqpHashSet;
            List<AoEquipment> eqpList = new List<AoEquipment>();

            //var workLots = wstep.Wips;
            //foreach (var wlot in workLots)
            //{
            //    var lot = wlot.Batch.Sample as SEMLot;
            //    if (lot.PlanWip != null && lot.PlanWip.PegWipInfo != null && wstep.Steps.Count() > 0)
            //        elist.AddRange(ArrangeHelper2.GetArrange(lot.PlanWip.PegWipInfo, wstep.Steps[0].StepID.ToString(), lot.CurrentProductID, "SIM"));
            //}

            eqpHashSet = GET_LOADABLE_EQP_SET(wstep);

            foreach (var eqpID in eqpHashSet)
            {
                var eqp = AoFactory.Current.GetEquipment(eqpID);
                if (eqp != null)
                    eqpList.Add(eqp);
            }

            return eqpList;
        }


        public static HashSet<string> GET_LOADABLE_EQP_SET(WorkStep wstep)
        {
            var workLots = wstep.Wips;
            if (workLots == null || workLots.Count < 1)
                return null;

            HashSet<string> eqpHashSet = new HashSet<string>();

            foreach (var wlot in workLots)
            {
                var lot = wlot.Batch.Sample as SEMLot;


                if (lot.PlanWip != null && lot.PlanWip.PegWipInfo != null && wstep.Steps.Count() > 0)
                    eqpHashSet.AddRange(ArrangeHelper2.GetArrange(lot, wstep.Key.ToString(), "SIM"));
            }
            return eqpHashSet;
        }

        public static IEnumerable<AoEquipment> GET_LOADABLE_EQPS_BY_LOTARRANGE(WorkStep wstep)
        {
            HashSet<AoEquipment> eqpList = new HashSet<AoEquipment>();

            foreach (var wLot in wstep.Wips)
            {
                var semLot = wLot.Lot as SEMLot;
                SEMWipInfo wipInfo = semLot.WipInfo as SEMWipInfo;

                if (wipInfo.IsResFixLotArrange == true && wipInfo.LotArrangeOperID == wstep.Key.ToString())
                {
                    eqpList.Add(wipInfo.ResFixLotArrangeEqp.AoEqp);
                }
            }
            return eqpList;
        }

        public static bool IsRun(this SEMLot lot)
        {
            try
            {
                if (lot.CurrentPlan.LoadedResource != null && lot.CurrentPlan.IsStarted)
                    return true;

                return false;
            }
            catch (Exception e)
            {
                WriteLog.WriteErrorLog(e.Message);
                //WriteHelper.WriteErrorHistory(ErrorLevel.FATAL, string.Format("ErrorMessage : {0}   MethodName : {1}", e.Message, System.Reflection.MethodInfo.GetCurrentMethod().Name));
                return default(bool);
            }
        }

        public static bool IsRunningWip(this SEMLot lot)
        {
            if (lot.Wip.WipState.ToUpper() == "RUN" && lot.CurrentStepID == lot.Wip.InitialStep.StepID)
                return true;

            return false;        
        }

        public static int ProfileWipCompare(WorkLot x, WorkLot y)
        {
            var xlot = x.Lot as SEMLot;
            var ylot = y.Lot as SEMLot;

            //int cmp = xlot.InTarget.TargetDate.CompareTo(ylot.InTarget.TargetDate);

            return 0;

        }

        public static bool IsFilterLot(WorkLot lot, WorkEqp weqp, WorkStep wstep)
        {
            // ResFix Filter
            SEMLot slot = lot.Lot as SEMLot;
            if (slot.Wip.IsResFixLotArrange && slot.Wip.LotArrangeOperID == slot.CurrentStepID)
            {
                if (weqp.Target.EqpID != slot.Wip.ResFixLotArrangeEqp.EqpID)
                {
                    return true;
                }
            }

            return false;
        }
        public static Time TransferTime(SEMLot lot)
        {
            //return Time.FromMinutes(5);
            return Time.Zero;
        }
        public static int GetPhase()
        {
            DateTime now = AoFactory.Current.NowDT;

            return InputMart.Instance.AgentPhaseIndex[now.DbToString()];
        }

        public static string CheckReClassifyState(WorkStep step, JobChangeContext context)
        {
            var now = AoFactory.Current.NowDT;
            var wsdata = step.Data as WorkStepData;
            string state = "Normal";

            // Up 판단한지 얼마 안됐으면 Hold
            //if (InputMart.Instance.DecisionDic.TryGetValue(step.Group.Key.ToString(), out DateTime decisionTime))
            //{
            //    if ((now - decisionTime).TotalHours <= InputMart.Instance.GlobalParameters.UpDownMaintainHour)
            //    {
            //        state = "Up Hold";
            //    }
            //    else
            //    {
            //        InputMart.Instance.DecisionDic.Remove(step.Group.Key.ToString());
            //    }
            //}    

            if (wsdata.EqpUpCount >= InputMart.Instance.GlobalParameters.MaxUpEqpCount)
                state = "MaxUp";

            if (context.CurrentDownSteps.Contains(step))
                state = "Down";

            if (wsdata.DecidedOperationType == OperationType.Up)
                state = "Up";

            return state;
        }

        public class AoEquipmentCompare : IComparer<AoEquipment>
        {
            WorkStep WorkStep = null;

            public int Compare(AoEquipment x, AoEquipment y)
            {

                if (x.Equals(y))
                    return 0;
                int cmp = AgentHelper.GetCompareAoEquipment(x, y, WorkStep);

                return cmp;
            }

            public AoEquipmentCompare(WorkStep ws)
            {
                this.WorkStep = ws;
            }
        }

        public static int GetCompareAoEquipment(AoEquipment x, AoEquipment y, WorkStep ws)
        {
            int cmp = 0;

            if (cmp == 0)
            {
                Time xInTime = x.GetNextInTime(false);
                Time yInTime = y.GetNextInTime(false);

                cmp = xInTime.CompareTo(yInTime);
            }

            if (cmp == 0)
                cmp = x.EqpID.CompareTo(y.EqpID);

            return cmp;
        }

        public static bool HasDelayLot(this WorkStep ws)
        {
            foreach (var wLot in ws.Wips)
            {
                var semLot = wLot.Lot as SEMLot;
                SEMWipInfo wipInfo = semLot.WipInfo as SEMWipInfo;
                if (semLot.PlanWip == null || semLot.PlanWip.PegWipInfo == null)
                    continue;

                DateTime now = AoFactory.Current.NowDT;
                DateTime lpst = SimHelper.GetLPST(semLot, ws.Steps[0].StepID);

                if ((now - lpst).TotalDays > InputMart.Instance.GlobalParameters.AcceptableDelayDay)
                {
                    return true;
                }
            }

            return false;
        }

        public static double GetLpatGapDay(this SEMWorkStep ws)
        {
            if (ws == null)
                return double.NaN;

            if(ws.WsData == null)
                return double.NaN;

            if (ws.WsData.Lpst == DateTime.MaxValue)
                return double.NaN;


            double result = Math.Round((AoFactory.Current.NowDT - ws.WsData.Lpst).TotalDays, 2);
            //double result = Math.Round((AoFactory.Current.NowDT - ws.GetLpst()).TotalDays, 2);

            return result;
        }

        public static DateTime GetLpst(this SEMWorkStep ws)
        {
            if (ws.WsData == null || ws.WsData.InflowFenceLots.Count == 0)
                return DateTime.MaxValue;

            var loadedEqpCnt = ws.GetLoadedEqpCount();

            if (ws.WsData.InflowFenceLots.Count < loadedEqpCnt)
                return DateTime.MaxValue;

            Dictionary<SEMLot, DateTime> lpstDic = new Dictionary<SEMLot, DateTime>();

            foreach (var l in ws.WsData.InflowFenceLots)
            {
                var lot = l.Lot as SEMLot;
                var lpst = lot.GetLPST(ws.StepKey);

                lpstDic.Add(lot, lpst);
            }

            var lpstList = lpstDic.OrderBy(x => x.Value).Select(x => x.Value).ToList();

            if (loadedEqpCnt != 0 && lpstList.Count == loadedEqpCnt)
                loadedEqpCnt--;

            var sampleLpst = lpstList[loadedEqpCnt];

            return sampleLpst;
        }

        public static string GetDueState(double lpstGapDay)
        {
            string result = string.Empty;

            if (double.IsNaN(lpstGapDay))
            { 
                result = Constants.None;
            }
            else if (lpstGapDay < GlobalParameters.Instance.AcceptablePrecedeDay * -1)
            {
                // 과잉 선행이면
                result = Constants.Precede;
            }
            else if (GlobalParameters.Instance.AcceptablePrecedeDay * -1 < lpstGapDay && lpstGapDay < GlobalParameters.Instance.AcceptableDelayDay)
            {
                // 과잉 선행은 아니지만, delay도 아닐때
                result = Constants.Normal;
            }
            else if (GlobalParameters.Instance.AcceptableDelayDay < lpstGapDay)
                result = Constants.Delay;
            else
                result = Constants.None;          
                
            return result;
        }

        public static Dictionary<AssignEqp, double> GetAssignEqpsSetupTime(this List<AssignEqp> assignableEqps, WorkStep upWorkStep)
        {
            Dictionary<AssignEqp, double> eqpSetupTimeDic = new Dictionary<AssignEqp, double>();

            if (assignableEqps == null || assignableEqps.Count == 0)
                return eqpSetupTimeDic;

            foreach (var fEqp in assignableEqps)
            {
                var eqp = fEqp.Target.Target as SEMEqp;
                foreach (var wlot in upWorkStep.Inflows)
                {
                    var lot = wlot.Batch.Sample as SEMLot;
                    double setupTime = eqp.GetSetupTimeForJCAgent(lot);
                    if (eqpSetupTimeDic.ContainsKey(fEqp))
                        eqpSetupTimeDic[fEqp] = eqpSetupTimeDic[fEqp] > setupTime ? setupTime : eqpSetupTimeDic[fEqp];
                    else
                        eqpSetupTimeDic.Add(fEqp, setupTime);
                }
            }

            return eqpSetupTimeDic;
        }





        public static SEMProduct GetProduct(this SEMLot lot, string stepID)
        {
            SEMProduct product;

            // Pegging 정보가 없는 경우 현재 Product가 대상 Product
            if (lot.PlanWip == null || lot.PlanWip.PegWipInfo == null)
                return lot.Product as SEMProduct;

            product = lot.PlanWip.PegWipInfo.GetProduct(stepID);

            return product;
        }

        public static SEMProduct GetProduct(this SEMPlanWip planWip, string stepID)
        {
            SEMProduct product;

            if (planWip.PegWipInfo == null)
                return null;

            product = planWip.PegWipInfo.GetProduct(stepID);

            return product;
        }


        public static SEMProduct GetProduct(this SEMPegWipInfo pwi, string stepID)
        {
            SEMProduct product;

            if (pwi.ProcessingOpers.TryGetValue(stepID, out product) == false)
            {
                WriteLog.WriteErrorLog($"{pwi.LotID}의 {stepID}에 해당하는 Product를 찾을 수 없습니다");
                return null;
            }

            return product;
        }



        public static void SetClassifyInfo(this WorkStep step, DateTime inflowFence, ref int demandQty, ref DateTime dueDate, ref WorkLot fastLot, ref int inflowWip)
        {
            var ws = step as SEMWorkStep;
            var stepdata = ws.Data as WorkStepData;

            var loadedEqps = ws.GetLoadedEqpList();

            foreach (var wLot in ws.Inflows)
            {
                var semLot = wLot.Lot as SEMLot;
                SEMWipInfo wipInfo = semLot.WipInfo as SEMWipInfo;
                var now = AoFactory.Current.NowDT;
                var lpst = semLot.GetLPST(ws.Key.ToString());

                if (semLot.Wip.WipState.ToUpper() == "RUN" && semLot.CurrentStepID == ws.Key.ToString())
                    continue;

                var pt = wipInfo.PeggedTargets.OrderBy(x => x.DueDate).FirstOrDefault();
                if (pt != null)
                {
                    if (wLot.AvailableTime <= ModelContext.Current.EndTime)
                    {
                        demandQty += (int)pt.Qty;

                        if (pt.DueDate < dueDate)
                        {
                            fastLot = wLot;
                            dueDate = pt.DueDate;
                            stepdata.DueDate = pt.DueDate;
                            stepdata.Lpst = lpst;
                        }
                    }
                }

                if (wLot.AvailableTime <= ModelContext.Current.EndTime)
                {
                    inflowWip += wLot.Lot.UnitQty;
                    stepdata.InflowWipCnt++;
                    stepdata.InflowWips.Add(wLot.Lot.LotID);
                }

                var lpstGap = (now - lpst).TotalDays;
                var dueState = GetDueState(lpstGap);

                if (dueState == "Precede")
                    stepdata.PrecedeLots.Add(wLot);
                else if (dueState == "Normal")
                    stepdata.NormalLots.Add(wLot);
                else if (dueState == "Delay")
                    stepdata.DelayLots.Add(wLot);
            }

            if (fastLot == null && ws.Inflows.Count !=0)
            {
                fastLot = ws.Inflows.First();

                var lot = fastLot.Lot as SEMLot;
                if (lot.PeggingDemands.IsNullOrEmpty() == false)
                {
                    var pp = lot.PeggingDemands.First().Key;
                    dueDate = pp.DueDate;
                    stepdata.DueDate = pp.DueDate;
                    stepdata.Lpst = lot.GetLPST(ws.StepKey);
                }
                else 
                {
                    dueDate = DateTime.MaxValue.AddSeconds(-1);
                    stepdata.DueDate = DateTime.MaxValue.AddSeconds(-1);
                    stepdata.Lpst = DateTime.MaxValue.AddSeconds(-1);
                }

            }

            stepdata.FastLot = fastLot;
            stepdata.InflowWipQty = inflowWip;
            stepdata.InflowDemandQty = demandQty;
        }

        public static double GetCapa(this SEMWorkStep step, WorkLot fastLot, DateTime dueDate, ref string capaDetail)
        {
            double capa = 0;
            DateTime now = AoFactory.Current.NowDT;
            var data = step.Data as WorkStepData;
            var loadedEqpList = step.GetLoadedEqpList();

            if (fastLot != null && loadedEqpList.Count() > 0)
            {
                SEMLot lot = fastLot.Lot as SEMLot;

                TimeSpan gapTime = data.Lpst - now;

                //if (gapTime.TotalMinutes <= 0)
                //{
                //    string a = string.Empty;
                //    var lpst = data.Lpst;
                //    while (gapTime.TotalMinutes <= 0)
                //    {
                //        lpst = lpst.AddDays(7);
                //        gapTime = lpst - now;
                //        a += "+7Day";
                //    }
                //    capaDetail = $"gapTime({Math.Round(gapTime.TotalMinutes)}) = LPST({data.Lpst.ToString()}) - now({now.ToString()}) {a} // Capa = ";

                //}
                //else
                //{
                //    capaDetail = $"gapTime({Math.Round(gapTime.TotalMinutes)}) = LPST({data.Lpst.ToString()})-now({now.ToString()}) // Capa = ";
                //}



                foreach (var aeqp in loadedEqpList)
                {
                    // todo 작업중인 시간은 capa에서 제외
                    if (aeqp.LotEndTime == DateTime.MinValue)
                        gapTime = ModelContext.Current.EndTime - now;
                    else
                        gapTime = ModelContext.Current.EndTime - aeqp.LotEndTime;

                    if (gapTime.TotalSeconds < 0)
                        gapTime = TimeSpan.FromSeconds(0);

                    //double ct = TimeHelper.GetCycleTime(lot.Product.ProductID, step.Key.ToString(), aeqp.EqpID);

                    //capa += gapTime.TotalMinutes / ct;
                    capa += gapTime.TotalMinutes;

                    capaDetail += $"+ {Math.Round(gapTime.TotalMinutes)}";
                }
                capaDetail += $" = {Math.Round(capa, 1)}";
            }

            capa = Math.Round(capa, 1);
            return capa;
        }

        public static double GetNeededCapa(this SEMWorkStep ws)
        {
            double result = 0;

            var loadedEqps = ws.GetLoadedEqpList();

            foreach (var l in ws.Inflows)
            {
                SEMLot lot = l.Lot as SEMLot;

                if (ModelContext.Current.EndTime < l.AvailableTime)
                    continue;

                if (ModelContext.Current.EndTime < lot.GetLPST(ws.StepKey))
                    continue;

                double sumCyclTime = 0;
                int cnt = 0;
                foreach (var eqp in loadedEqps)
                {
                    if (ws.LoadableEqps.Contains(eqp) == false)
                        continue;
                    double ct = TimeHelper.GetCycleTime(lot.Product.ProductID, ws.Key.ToString(), eqp.EqpID);
                    sumCyclTime += ct;
                    cnt++;
                }

                if (cnt != 0)
                {
                    double avgCycleTime = sumCyclTime / cnt;
                    result += lot.UnitQtyDouble * avgCycleTime;
                }
            }

            result = Math.Round(result, 1);

            return result;
        }

        public static bool IsDelay(DateTime lpst)
        {
            var now = AoFactory.Current.Now;

            if ((now - lpst).TotalDays > GlobalParameters.Instance.AcceptableDelayDay)
                return true;

            return false;
        }

        public static bool IsResFix(this WorkStep step, ref WorkLot fixedLot, ref SEMEqp fixEqp)
        {
            foreach (var wLot in step.Wips)
            {
                var semLot = wLot.Lot as SEMLot;
                SEMWipInfo wipInfo = semLot.WipInfo as SEMWipInfo;

                if (wipInfo.IsResFixLotArrange == true && wipInfo.LotArrangeOperID == step.Key.ToString())
                {
                    fixedLot = wLot;
                    fixEqp = wipInfo.ResFixLotArrangeEqp;

                    return true;
                }
            }

            return false;
        }

        public static bool CanUp(this SEMWorkStep ws, double neededCapa, ref string reason)
        {
            return false;
            //int availableWipCnt = 0;
            //var now = AoFactory.Current.Now;

            //var loadedEqps = ws.GetLoadedEqpList();

            //foreach (var l in ws.Inflows)
            //{
            //    if (l.AvailableTime < now)
            //    {
            //        availableWipCnt++;
            //    }
            //}

            //if (loadedEqps.Count + 1 >= availableWipCnt)
            //{
            //    reason = "(LessWipCnt)";
            //    return false;
            //}

            ////[TODO] 임시조치
            //if (ws.LoadableEqps.Count > 5 && loadedEqps.Count >= ws.LoadableEqps.Count * GlobalParameters.Instance.MaxUpEqpRatio)
            //{
            //    reason = "(MaxEqpCnt)";
            //    return false;
            //}

            //bool isDelay = ws.WsData.Lpst < now;
            //if (isDelay)
            //{
            //    if (neededCapa > ws.WsData.Capa)
            //    {
            //        reason = $"needCapa({neededCapa}) > capa({ws.WsData.Capa}))";
            //        return true;
            //    }
            //}
            //else
            //{
            //    if (neededCapa > ws.WsData.Capa * (1 + GlobalParameters.Instance.AcceptableOverCapaPer))
            //    {
            //        reason = $"needCapa({neededCapa}) > capa({ws.WsData.Capa} * {(1 + GlobalParameters.Instance.AcceptableOverCapaPer)}))";
            //        return true;
            //    }
            //}

            //return false;
        }

        public static bool HasInflow(this SEMWorkStep ws, DateTime loadedEqpEndTime)
        {
            // idle로 기다릴 수 있는 시간
            double allowArrivalGapHr = 1.5;

            // 장비내 작업중인 작업물이 끝난 시간 + allowArrivalGapHr 이내 사용 가능한 wip이 하나라도 있으면 true
            Time inflowFenceTime = new Time(loadedEqpEndTime.AddHours(allowArrivalGapHr));
            bool hasInflows = false;
            foreach (var l in ws.Inflows)
            {
                if (l.AvailableTime < inflowFenceTime)
                {
                    hasInflows = true;
                    ws.WsData.InflowFenceLots.Add(l);
                }
            }

            return hasInflows;
        }

        public static void WriteJobChangeInitLog(WorkAgent agent)
        {
            var workGroups = agent.Groups;

            foreach (var workGroup in workGroups)
            {
                foreach (var step in workGroup.Steps)
                {
                    AgentHelper.WriteJobChangeInitLog(step);
                }
            }
        }

        public static JobConditionGroup GetJobConditionGroup(this SEMLot lot)
        {
            var wGroup = lot.CurrentWorkGroup;

            if (wGroup == null)
                return null;

            var jGroup = wGroup.JobConditionGroup;

            return jGroup;
        }

        public static List<SEMLot> GetSameJobConditionWips(AoEquipment eqp, IList<SEMLot> wips)
        {
            List<SEMLot> result = new List<SEMLot>();

            if (GlobalParameters.Instance.UseJobChangeAgent == false)
                return result;

            if (wips.IsNullOrEmpty())
                return result;

            var sampleLot = wips.First() as SEMLot;
            var wGroup = sampleLot.CurrentWorkGroup;

            var jGroup = sampleLot.GetJobConditionGroup();

            result = jGroup.GetSameJobConditionWips(eqp);

            return result;
        }


        public static List<SEMLot> GetSameJobConditionWips(this JobConditionGroup jGroup, AoEquipment e)
        {
            List<SEMLot> result = new List<SEMLot>();

            var aeqp = e as SEMAoEquipment;
            var eqp = e.Target as SEMEqp;

            foreach (var ws in jGroup.Steps)
            {
                if (ws.LoadableEqps.Contains(e) == false)
                    continue;

                if (ws.IsResFix && ws.WorkGroup.FixedResource.EqpID != e.EqpID)
                    continue;

                foreach (var l in ws.Inflows)
                {
                    var lot = l.Lot as SEMLot;                    

                    if (eqp.OperIDs.Contains(lot.CurrentStepID) == false)
                        continue;

                    result.Add(lot);
                }
            }

            return result;
        }

        public static List<WorkLot> GetSameJobConditionWorkLots(SEMWorkStep ws, AoEquipment eqp, List<WorkLot> wips)
        {
            List<WorkLot> result = new List<WorkLot>();

            if (GlobalParameters.Instance.UseJobChangeAgent == false)
                return result;

            if (wips.IsNullOrEmpty())
                return result;

            var jGroup = ws.JobConditionGroup;

            result = jGroup.GetSameJobConditionWorkLots(eqp);

            return result;
        }

        public static List<WorkLot> GetSameJobConditionWorkLots(this JobConditionGroup jGroup, AoEquipment eqp)
        {
            List<WorkLot> result = new List<WorkLot>();

            foreach (var ws in jGroup.Steps)
            {
                if (ws.LoadableEqps.Contains(eqp) == false)
                    continue;

                if (ws.IsResFix)
                    continue;

                foreach (var l in ws.Inflows)
                {
                    var lot = l.Lot as SEMLot;

                    result.Add(l);
                }
            }

            return result;
        }

        public static void SetResFixInfo(SEMLot lot, SEMAoEquipment aeqp)
        {
            SEMWipInfo wip = lot.Wip;
            SEMEqp eqp = aeqp.Target as SEMEqp;

            bool isResFixLot = wip.IsResFixLotArrange && lot.CurrentStepID == wip.LotArrangeOperID;
            if (isResFixLot)
            {
                // res fix 정보 삭제
                eqp.ResFixedWips.Remove(wip);

                // Res fix가 완료되면 작업조건이 동일한 일반 WG에 강제 할당
                if (eqp.ResFixedWips.Count == 0)
                {
                    var agent = AoFactory.Current.JobChangeManger.GetAgent("DEFAULT");
                    var jGroup = lot.GetJobConditionGroup();

                    if (jGroup == null)
                        return;

                    // work step에서 select lot을 삭제, 아래 ws를 가져올 때 현재 ws을 제외 하려고
                    WorkLot wlot = lot.CurrentWorkStep.Wips.Where(x => x.Lot.LotID == lot.LotID).FirstOrDefault();
                    if (lot.CurrentWorkStep != null && wlot != null)
                    {
                        lot.CurrentWorkStep.Wips.Remove(wlot);
                        lot.CurrentWorkStep.Inflows.Remove(wlot);
                    }

                    // 작업조건이 같은 ws중 우선 순위가 가장 높은 ws을 가져옴
                    var upWorkStep = jGroup.GetUpWorkStep(aeqp);//new List<SEMWorkStep>();

                    if (upWorkStep == null)
                        return;

                    // Trade
                    agent.Down(lot.CurrentWorkStep, eqp.AoEqp);
                    agent.Up(upWorkStep, eqp.AoEqp);

                    eqp.AoEqp.WorkStep = upWorkStep;

                    WriteDecisionLog(lot.CurrentWorkStep, "DOWN", "SameJobConditionTrade", "ResFixDoneDown");
                    WriteDecisionLog(upWorkStep, "UP", "SameJobConditionTrade", "ResFixDoneUp");
                    WriteAssignEqpLog(upWorkStep, lot.CurrentWorkStep, eqp.AoEqp, "ResFixDoneTrade");

                }
            }
        }

        public static void WriteAssignEqpFilterLog(SEMWorkStep upWs, SEMWorkStep downWs, SEMAoEquipment eqp, string reason)
        {
            FILTER_ASSIGN_EQP_LOG2 row = new FILTER_ASSIGN_EQP_LOG2();

            row.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            row.UP_WORK_GROUP = upWs != null ? upWs.GroupKey : string.Empty;
            //row.UP_LPST_GAP_DAY = info.upWsLpstGapDay;
            //row.UP_DUE_STATE = info.upWsState;

            row.EQP_ID = eqp.EqpID;
            //row.EQP_DECISION_TYPE = isEqpAssigned ? "DOWN" : info.WorkStep.WsData.DecidedOperationType.ToString();
            //row.EQP_LPST_GAP_DAY = info.LpstGapDay;
            //row.EQP_DUE_STATE = info.DueState;

            row.RESULT = "Filtered";
            row.REASON = reason;

            //row.WAITING_TIME = Math.Round(info.WaitingTime, 2);
            //row.SETUP_TIME = info.SetupTime;

            //row.PRIORITY = info.Priority.ToString();

            //row.STEP_ID = ws.StepKey;
            //row.DUE_STATE_LOG = info.DueStateLog;
            row.EQP_WORK_GROUP = downWs != null ? downWs.GroupKey : string.Empty;

            OutputMart.Instance.FILTER_ASSIGN_EQP_LOG2.Add(row);
        }


        public static void WriteDecisionLog(SEMWorkStep ws, string decisionType, string reason, string detailReason)
        {
            var now = AoFactory.Current.NowDT;

            DECISION_LOG log = new DECISION_LOG();

            log.EVENT_TIME = now.DbToString();
            log.VERSION_NO = ModelContext.Current.VersionNo;
            log.WORK_GROUP = AgentHelper.GetWorkGroupName(ws);
            log.WORK_STEP = ws.Key.ToString();
            log.DECISION_TYPE = decisionType;
            log.DECISION_REASON = reason;
            log.DECISION_DETAIL_REASON = detailReason;

            log.LOADED_EQP_CNT = ws.LoadedEqpCount;
            log.LOADED_EQPS = string.Join(",", ws.LoadedEqpIDs);

            log.TOTAL_LOADED_EQP_CNT = ws.GetLoadedEqpCount();
            log.TOTAL_LOADED_EQPS = ws.GetLoadedEqpList().Select(x => x.EqpID).ListToString();

            log.INFLOW_WIPS = ws.Inflows.QueueToString();
            log.INFLOW_WIP_CNT = ws.Inflows.Count;

            log.WIP_CNT = ws.Wips.Count;
            log.WIPS = ws.Wips.Select(x => x.Lot.LotID).ListToString();

            OutputMart.Instance.DECISION_LOG.Add(log);
        }

        public static void WriteAssignEqpLog(SEMWorkStep upWs, SEMWorkStep downWs, SEMAoEquipment eqp, string reason)
        {
            FILTER_ASSIGN_EQP_LOG2 row = new FILTER_ASSIGN_EQP_LOG2();

            row.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            row.UP_WORK_GROUP = upWs.GroupKey;
            //row.UP_LPST_GAP_DAY = info.upWsLpstGapDay;
            //row.UP_DUE_STATE = info.upWsState;

            row.EQP_ID = eqp.EqpID;
            //row.EQP_DECISION_TYPE = isEqpAssigned ? "DOWN" : info.WorkStep.WsData.DecidedOperationType.ToString();
            //row.EQP_LPST_GAP_DAY = info.LpstGapDay;
            //row.EQP_DUE_STATE = info.DueState;

            row.RESULT = "New Assign";
            row.REASON = reason;

            //row.WAITING_TIME = Math.Round(info.WaitingTime, 2);
            //row.SETUP_TIME = info.SetupTime;

            //row.PRIORITY = info.Priority.ToString();

            //row.STEP_ID = ws.StepKey;
            //row.DUE_STATE_LOG = info.DueStateLog;
            row.EQP_WORK_GROUP = downWs.GroupKey;

            OutputMart.Instance.FILTER_ASSIGN_EQP_LOG2.Add(row);
        }

        public static AssignEqp GetAssignEqp(this AoEquipment aeqp)
        {
            AssignEqp result = null;

            if (_agent.AssignedInfos.TryGetValue(aeqp, out result) == false)
            {
                WriteLog.WriteErrorLog($"AssignEqp를 찾을 수 없습니다.");
            }

            return result;
        }

        public static double GetExpectedSetupTime(this SEMWorkStep ws)
        {
            double result = 0;
            foreach (var a in _agent.JobConditionGroups)
            {



            }

            return result;
        }


        public static SEMWorkLot CreateSEMWorkLot(SEMLot lot, Time availablaTime, object wstepKey, Step targetStep, SEMWorkStep ws)
        {
            SEMWorkLot wLot = new SEMWorkLot(lot, availablaTime, wstepKey, targetStep);
            wLot.Lpst = lot.GetLPST(wstepKey.ToString());
            wLot.WorkStep = ws;
            wLot.LoadableEqp = ws.GetLoadableEqps();

            return wLot;
        }

        public static SEMWorkLot RecreateSEMWorkLot(SEMLot lot, SEMWorkLot wLot)
        {
            SEMWorkLot newWLot = new SEMWorkLot(lot, wLot.AvailableTime, wLot.StepKey, wLot.Step);

            newWLot.Lpst = wLot.Lpst;
            newWLot.WorkStep = wLot.WorkStep;
            newWLot.LoadableEqp = wLot.LoadableEqp;

            return newWLot;
        }

        public static List<SEMAoEquipment> GetLoadableEqps(this SEMWorkStep ws)
        {
            List<SEMAoEquipment> loadableEqps = new List<SEMAoEquipment>();
            ws.LoadableEqps.ForEach(x => loadableEqps.Add(x as SEMAoEquipment));
            return loadableEqps;
        }

        public static void TradeInJobGroup(this SEMWorkAgent agent)
        {
            // AvailableLot이 없는 WS은 DoFilter에서 호출되지않아 JobGroup의 lot을 투입할 수 없음
            // 작업물이 없는 WS은 loaded eqp를 같은 job그룹내 다른 ws에 할당

            foreach (var jGroup in agent.JobConditionGroups)
            {
                Dictionary<SEMAoEquipment, SEMWorkStep> downEqpPairs = jGroup.GetDownEqps();

                if (downEqpPairs.Count() == 0)
                    continue;

                List<SEMAoEquipment> downEqps = downEqpPairs.Keys.ToList();

                foreach (var aeqp in downEqps)
                {
                    var downWs = downEqpPairs[aeqp];
                    var upWs = jGroup.GetUpWorkStepForJobGroupTrade(aeqp);

                    if (upWs != null)
                    {
                        agent.Down(downWs, aeqp);
                        agent.Up(upWs, aeqp);

                        aeqp.WorkStep = upWs;

                        WriteDecisionLog(downWs, "DOWN", "SameJobConditionTrade", "NoProfileDown");
                        WriteDecisionLog(upWs, "UP", "SameJobConditionTrade", "NoProfileUp");
                        WriteAssignEqpLog(upWs, downWs, aeqp, "NoProfileTrade");
                    }
                }
            }
        }

        private static SEMWorkStep GetUpWorkStepForJobGroupTrade(this JobConditionGroup jGroup, SEMAoEquipment downAeqp)
        {
            SEMWorkStep upWs = null;
            
            var now = AoFactory.Current.NowDT;

            Dictionary<SEMWorkStep, int> steps = new Dictionary<SEMWorkStep, int>();

            foreach (var ws in jGroup.Steps)
            {
                if (ws.LoadedEqps.Any(x => x.Target.EqpID == downAeqp.EqpID))
                    continue;

                if (ws.LoadableEqps.Contains(downAeqp) == false)
                    continue;

                var loadableWipCnt = ws.Wips.Where(x => x.AvailableTime < now.AddHours(1)).Count();
                if (loadableWipCnt == 0)
                    continue;

                steps.Add(ws, loadableWipCnt);                
            }

            if (steps.IsNullOrEmpty())
                return null;

            var a = steps.OrderByDescending(x => x.Value);

            return a.FirstOrDefault().Key;
        }


        private static Dictionary<SEMAoEquipment, SEMWorkStep> GetDownEqps(this JobConditionGroup jGroup)
        {
            var now = AoFactory.Current.NowDT;

            Dictionary<SEMAoEquipment, SEMWorkStep> downEqps = new Dictionary<SEMAoEquipment, SEMWorkStep>();

            // AvailableLot이 없는 WS과 장비를 downEqps에 Add
            foreach (var ws in jGroup.Steps)
            {
                var availableLot = ws.Wips.Where(x => now.AddHours(1.5) > x.AvailableTime);
                if (availableLot.Count() == 0)
                {
                    ws.LoadedEqps.ForEach(x => downEqps.Add(x.Target as SEMAoEquipment, ws));
                }
            }

            if (downEqps.Count() == 0)
                return downEqps;

            // 투입 가능한 lot이 많은 장비순으로 정렬
            downEqps = downEqps.OrderByDescending(x => x.Key.ArrangedLots.Count).ToDictionary(x => x.Key, x => x.Value);

            return downEqps;
        }

        public static SEMWorkStep GetUpWorkStep(this HashSet<SEMWorkStep> steps, SEMAoEquipment aeqp)
        {
            // 장비가 ws을 선택
            // 1. setup시간 짧은것 우선
            // 2. 납기 빠른것 우선
            var wsList = new List<AssignInfo>();

            foreach (var ws in steps)
            {
                if (ws.Wips.Count == 0)
                    continue;

                AssignInfo assignInfo = new AssignInfo();
                assignInfo.Key = ws.GroupKey;
                assignInfo.WorkStep = ws;

                var sampleLot = ws.GetSampleLot();
                if (sampleLot == null)
                    continue;

                var eqp = aeqp.Target as SEMEqp;
                double setupTime = eqp.GetSetupTimeForJCAgent(sampleLot);
                assignInfo.SetupTime = setupTime;
                assignInfo.Lpst = ws.WsData.MainProfile.Lpst;

                wsList.Add(assignInfo);
            }

            var orderedList = wsList.OrderBy(X => X.SetupTime).ThenBy(x => x.Lpst);
            var result = orderedList.FirstOrDefault();
            if (result == null)
                return null;

            return result.WorkStep;        
        }

        public static SEMLot GetSampleLot(this SEMWorkStep ws)
        {
            if (ws.WsData.MainProfile == null)
                return null;

            return ws.WsData.MainProfile.Lot.Lot as SEMLot;
        }


        public static bool CanTrade(SEMWorkStep UpWs, SEMWorkStep DownWs, SEMAoEquipment aeqp, ref string filterReason)
        {
            if (UpWs == null)
            {
                filterReason = "NoUpWorkStep";
                return false;
            }

            if (DownWs == null)
            {
                filterReason = "NoDownWorkStep";
                return false;
            }

            if (DownWs.IsDummy)
            {
                filterReason = "ResFixEqp";
                return false;
            }

            var loadedEqps = UpWs.GetLoadedEqpList();
            var availableWips = UpWs.WsData.AvailWips;

            // lot보다 장비가 많은 경우 UP하지 않음
            if (loadedEqps.Count != 0 && loadedEqps.Count + 1 > availableWips.Count())
            {
                filterReason = "WipCntShortage";
                return false;
            }

            // 최대 보유 비율보다 많으면 UP하지 않음
            if (UpWs.LoadableEqps.Count > 5 && loadedEqps.Count >= UpWs.LoadableEqps.Count * GlobalParameters.Instance.MaxUpEqpRatio)
            {
                filterReason = "MaxEqp";
                return false;
            }

            // setup하는것 보다 다음 lot을 기다리는게 빠르면 up하지 않음
            if (IsNextLotBetter(UpWs, aeqp))
            {
                filterReason = "SetupTimeIsLongerThanNextLotAvilTime";
                return false;
            }

            return true;
        }

        public static bool IsNextLotBetter(SEMWorkStep upWs, SEMAoEquipment aeqp)
        {
            var eqpNextLot = aeqp.Data.Profiles.Where(x => x.WorkLotType == "BUSY" || x.WorkLotType == "UNPLAN").OrderBy(x => x.LotAvailableTime).FirstOrDefault();
            var upWsLot = upWs.WsData.MainProfile.Lot.Lot as SEMLot;

            if (eqpNextLot != null)
            {
                var eqpEndTime = aeqp.LotEndTime == DateTime.MinValue ? AoFactory.Current.NowDT : aeqp.LotEndTime;

                double setupTime = (aeqp.Target as SEMEqp).GetSetupTimeForJCAgent(upWsLot);
                var setupEndTime = eqpEndTime.AddMinutes(setupTime);

                // 장비의 다음 유입 lot 들어오는 시간이 
                if (eqpNextLot.LotAvailableTime < setupEndTime)
                {
                    return true;
                    var lot = eqpNextLot.Lot.Lot as SEMLot;
                    var lpst = lot.GetLPST(aeqp.WorkStep.StepKey);
                    var lpstGapDay = (eqpNextLot.LotAvailableTime - lpst).TotalDays;
                    var lotDueState = AgentHelper.GetDueState(lpstGapDay);

                    if (AgentAssignEqpHelper.IsDueStateFilter(lotDueState, upWs.WsData.MainProfile.DueState))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
