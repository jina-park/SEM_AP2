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
using Mozart.Simulation.Engine;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class DecisionHelper
    {
        public static void ClassifyDecisionType(SEMWorkAgent agent)
        {
            foreach (var jGroup in agent.JobConditionGroups)
            {
                jGroup.ClassifyDecisionType();
            }
        }

        public static void ClassifyDecisionType(this JobConditionGroup jGroup, bool isReCalc = false)
        {
            // job group과 work group의 up down을 결정
            SetDecisionType(jGroup);

            foreach (var ws in jGroup.Steps)
            {
                // ws을 평가
                ws.SetDecisionType();

                // ws 우선순위 셋팅
                ws.SetPriority();
            }

            // DECISION_LOG 작성
            WriteDecisionLog(jGroup);
        }

        public static void SetDecisionType(JobConditionGroup jGroup)
        {
            jGroup.SetNeededCapa();
            jGroup.SetHadCapa();

            var loadedEqps = jGroup.GetLoadedEqp();

            var wips = new List<WorkLot>();
            foreach (var ws in jGroup.Steps)
            {
                wips.AddRange(ws.Wips);
                foreach (var l in ws.Wips)
                {
                    if (l.AvailableTime <= AoFactory.Current.Now)
                    {
                        ws.WsData.AvailWips.Add(l as SEMWorkLot);
                        jGroup.Data.AvailWips.Add(l as SEMWorkLot);
                    }
                }
            }

            var profiles = jGroup.Profiles.Where(x => x.WorkLotType == "UNPLAN" || x.WorkLotType == "BUSY");
            if (profiles.Count() == 0)
            {
                jGroup.Data.Decision = OperationType.Down;
                jGroup.Data.DecisionReason = "(No Profile)";

                foreach (var ws in jGroup.Steps)
                {
                    ws.WsData.DecidedOperationType = OperationType.Down;
                    ws.WsData.DecisionReason = "(No Profile)";
                }
                return;
            }

            DateTime now = AoFactory.Current.NowDT;
            DateTime loadedEqpEndTime = now;
            foreach (var aeqp in loadedEqps)
            {
                if (aeqp.LotEndTime > now && loadedEqpEndTime < aeqp.LotEndTime)
                    loadedEqpEndTime = aeqp.LotEndTime;
            }

            bool hasInflow = jGroup.HasInflow(loadedEqpEndTime);
            if (hasInflow)
            {
                // 할당 가능 장비가 없는 경우 UP
                bool isNewUp = false;
                foreach (var ws in jGroup.Steps)
                {
                    if (ws.LoadedEqps.Count == 0)
                    {
                        var arrivedProfiles = ws.WsData.Profiles.GetArrivedProfiles();
                        if (arrivedProfiles.Count > 0)
                        {
                            jGroup.Data.Decision = OperationType.Up;
                            jGroup.Data.DecisionReason = "(NewUp)";

                            ws.WsData.DecidedOperationType = OperationType.Up;
                            ws.WsData.DecisionReason = "(NewUp)";

                            isNewUp = true;
                        }
                    }
                }

                if (isNewUp)
                    return;

                var delayProfiles = jGroup.Profiles.GetDelayProfiles();

                // 보유 Capa가 부족한 경우
                if (jGroup.Data.NeedCapa > jGroup.Data.Capa)
                {
                    if (delayProfiles.Count() > 0)
                    {
                        var upWsProfile = delayProfiles.OrderByDescending(x => x.LpstGapDay).First();

                        upWsProfile.LotWorkStep.WsData.DecidedOperationType = OperationType.Up;
                        upWsProfile.LotWorkStep.WsData.DecisionReason = "(Delay Capa up)";

                        jGroup.Data.Decision = OperationType.Up;
                        jGroup.Data.DecisionReason = "(Delay Capa up)";

                        return;
                    }
                    else if (jGroup.Data.NeedCapa > jGroup.Data.Capa * (1 + GlobalParameters.Instance.AcceptableOverCapaPer))
                    {
                        var profile = jGroup.Profiles.Where(x => x.WorkLotType == "UNPLAN" || x.WorkLotType == "BUSY").FirstOrDefault();
                        if (profile != null)
                        {
                            profile.LotWorkStep.WsData.DecidedOperationType = OperationType.Up;
                            profile.LotWorkStep.WsData.DecisionReason = "(Capa up)";

                            jGroup.Data.Decision = OperationType.Up;
                            jGroup.Data.DecisionReason = "(Capa up)";

                            return;
                        }
                    }
                }

                // Delay가 예상되는 경우 - 결과가 좋지 못해 삭제
                if (delayProfiles.Count > 0)
                {
                    var p = delayProfiles.OrderBy(x => x.LotAvailableTime).First();

                    if (p.LotAvailableTime < now.AddHours(1))
                    {
                        p.LotWorkStep.WsData.DecidedOperationType = OperationType.Up;
                        p.LotWorkStep.WsData.DecisionReason = "(Delay up)";

                        jGroup.Data.Decision = OperationType.Up;
                        jGroup.Data.DecisionReason = "(Delay up)";
                        return;
                    }
                }
            }
            else
            {
                jGroup.Data.Decision = OperationType.Down;
                jGroup.Data.DecisionReason = "(No Profile)";
            }
        }

        public static bool HasInflow(this JobConditionGroup jGroup, DateTime loadedEqpEndTime)
        {
            // idle로 기다릴 수 있는 시간
            double allowArrivalGapHr = 1.5;

            // 장비내 작업중인 작업물이 끝난 시간 + allowArrivalGapHr 이내 사용 가능한 wip이 하나라도 있으면 true
            Time inflowFenceTime = new Time(loadedEqpEndTime.AddHours(allowArrivalGapHr));
            //bool hasInflows = false;

            foreach (var l in jGroup.Data.Wips)
            {
                if (l.AvailableTime < inflowFenceTime)
                {
                    return true;

                    //hasInflows = true;
                    //ws.WsData.InflowFenceLots.Add(l);
                }
            }
            return false;
        }

        public static void WriteDecisionLog(JobConditionGroup jGroup)
        {
            WriteJobGroupDecisionLog(jGroup);

            foreach (var ws in jGroup.Steps)
            {
                WriteWorkStepDecisionLog(ws);
            }
        }

        private static void SetNeededCapa(this JobConditionGroup jGroup)
        {
            double neededCapa = 0;

            var wips = new List<WorkLot>();
            foreach (var ws in jGroup.Steps)
            {
                wips.AddRange(ws.Wips);
            }

            var loadedEqps = jGroup.GetLoadedEqp();

            foreach (var l in wips)
            {
                SEMLot lot = l.Lot as SEMLot;

                if (ModelContext.Current.EndTime < l.AvailableTime)
                    continue;

                if (ModelContext.Current.EndTime < lot.GetLPSTForJC(jGroup.StepKey))
                    continue;

                double sumCyclTime = 0;
                int cnt = 0;

                foreach (var eqp in loadedEqps)
                {
                    double ct = TimeHelper.GetCycleTime(lot.Product.ProductID, jGroup.StepKey, eqp.EqpID);
                    sumCyclTime += ct;
                    cnt++;
                }

                if (cnt != 0)
                {
                    double avgCycleTime = sumCyclTime / cnt;
                    neededCapa += lot.UnitQtyDouble * avgCycleTime;
                }
            }
            neededCapa = Math.Round(neededCapa, 2);

            jGroup.Data.NeedCapa = neededCapa;
        }


        private static void SetHadCapa(this JobConditionGroup jGroup)
        {
            double hadCapa = 0;
            DateTime now = AoFactory.Current.NowDT;

            var loadedEqps = jGroup.GetLoadedEqp();

            foreach (var aeqp in loadedEqps)
            {
                TimeSpan gapTime;

                // [todo] setup 시간은 capa에서 제외
                if (aeqp.LotEndTime == DateTime.MinValue)
                    gapTime = ModelContext.Current.EndTime - now;
                else
                    gapTime = ModelContext.Current.EndTime - aeqp.LotEndTime;

                if (gapTime.TotalSeconds < 0)
                    gapTime = TimeSpan.FromSeconds(0);

                hadCapa += gapTime.TotalMinutes;
            }

            hadCapa = Math.Round(hadCapa, 2);

            jGroup.Data.Capa = hadCapa;
        }

        private static void SetDecisionType(this SEMWorkStep ws)
        {
            string decisionReason = string.Empty;

            if (ws.IsResFix)
            {
                ws.SetResfixDecisionType(ref decisionReason);
            }
            else
            {
                var loadedEqps = ws.GetLoadedEqpList();
                var availableWips = ws.WsData.AvailWips;

                // 최종 up가능 판단
                if (ws.WsData.DecidedOperationType == OperationType.Up)
                {
                    // lot보다 장비가 많은 경우 UP하지 않음
                    if (loadedEqps.Count != 0 && loadedEqps.Count + 1 > availableWips.Count())
                    {
                        ws.WsData.DecidedOperationType = OperationType.Keep;
                        ws.WsData.DecisionReason = "(WipCntShortage)";
                    }

                    // 최대 보유 비율보다 많으면 UP하지 않음
                    if (ws.LoadableEqps.Count > 5 && loadedEqps.Count >= ws.LoadableEqps.Count * GlobalParameters.Instance.MaxUpEqpRatio)
                    {
                        ws.WsData.DecidedOperationType = OperationType.Keep;
                        ws.WsData.DecisionReason = "(MaxEqpCnt)";
                        return;
                    }
                }

                // 최종 Down 판단
                var profiles = ws.WsData.Profiles.Where(x => x.WorkLotType == "UNPLAN" || x.WorkLotType == "BUSY");
                if (profiles.Count() == 0)
                {
                    ws.WsData.DecidedOperationType = OperationType.Down;
                    ws.WsData.DecisionReason = "(NoProfile)";
                }
            }
        }


        private static void SetResfixDecisionType(this SEMWorkStep ws, ref string decisionReason)
        {
            SEMWorkLot fixedLot = ws.Wips.FirstOrDefault() as SEMWorkLot;

            if (fixedLot != null)
            {
                ws.WsData.DecidedOperationType = OperationType.Up;
                decisionReason = "ResFixUp";

                ws.WsData.IsLotArrange = true;
            }
            else
            {
                ws.WsData.DecidedOperationType = OperationType.Down;
                decisionReason = "No Profile";

                ws.WsData.IsLotArrange = true;
            }
        }

        private static void SetNormalDecisionType(this SEMWorkStep ws, ref string decisionReason)
        {
            SEMWorkLot upMaker = null;
            if (ws.IsUp(ref decisionReason, ref upMaker))
            {
                ws.WsData.DecidedOperationType = OperationType.Up;
                ws.WsData.DecisionReason = decisionReason;
                ws.WsData.UpMaker = upMaker;
            }
            else if (ws.IsDown(ref decisionReason))
            {
                ws.WsData.DecidedOperationType = OperationType.Down;
                ws.WsData.DecisionReason = decisionReason;
            }
            else if (ws.IsKeep(ref decisionReason))
            {
                ws.WsData.DecidedOperationType = OperationType.Keep;
                ws.WsData.DecisionReason = decisionReason;
            }
            else
            {
                ws.WsData.DecidedOperationType = OperationType.Keep;
                ws.WsData.DecisionReason = "Default Keep";
            }
        }

        private static bool IsUp(this SEMWorkStep ws, ref string decisionReason, ref SEMWorkLot upMakeLot)
        {
            bool result = false;

            var loadedEqps = ws.GetLoadedEqpList();

            // lot보다 장비가 많은 경우 UP하지 않음
            int availableWipCnt = 0;
            foreach (var l in ws.Inflows)
            {
                if (l.AvailableTime < AoFactory.Current.Now)
                {
                    availableWipCnt++;
                }
            }
            if (loadedEqps.Count + 1 >= availableWipCnt)
            {
                decisionReason = "(LessWipCnt)";
                upMakeLot = null;
                return false;
            }

            // 최대 보유 비율보다 많으면 UP하지 않음
            if (ws.LoadableEqps.Count > 5 && loadedEqps.Count >= ws.LoadableEqps.Count * GlobalParameters.Instance.MaxUpEqpRatio)
            {
                decisionReason = "(MaxEqpCnt)";
                return false;
            }

            // 할당 가능 장비가 없는 경우 UP
            if (loadedEqps.Count == 0)
            {
                var arrivedProfiles = ws.WsData.Profiles.GetArrivedProfiles();
                if (arrivedProfiles.Count > 0)
                {
                    decisionReason = "New Up";
                    upMakeLot = arrivedProfiles.First().Lot;
                    return true;
                }
            }


            //var delayProfiles = ws.WsData.Profiles.GetDelayProfiles();
            //if (delayProfiles.Count() > 0)
            //{
            //    SEMAoEquipment tradeEqp = ws.GetTryTradeEqp(delayProfiles);

            //    if (tradeEqp == null)
            //    {
            //        decisionReason = $"Keep, No Up Eqp";
            //        result = false;
            //    }
            //    else 
            //    {
            //        ws.TryUp(delayProfiles, tradeEqp);

            //        int phase = ws.WsData.Profiles.First().Phase;

            //        if (ws.IsDelaySolved(delayProfiles, out Profile solvedProfile))
            //        {
            //            decisionReason = $"Add Up,({phase}) Delay lot is comming {solvedProfile.LotID}({solvedProfile.GetLpstGapDay()}, {solvedProfile.LotAvailableTime} )";
            //            result = true;
            //        }
            //        else
            //        {
            //            var delayProfile = delayProfiles.OrderByDescending(x => x.LpstGapDay).First();
            //            decisionReason = $"Keep, ({phase}) Delay lot is comming but can't solve {delayProfile.LotID}({delayProfile.GetLpstGapDay()}, {delayProfile.LotAvailableTime} )";
            //            result = false;
            //        }

            //        ws.TryDown(tradeEqp);
            //    }
            //}


            return result;
        }

        private static void TryUp(this SEMWorkStep ws, List<Profile> delayProfiles, SEMAoEquipment upEqp)
        {
            ws.AddLoadedEqp(upEqp, false);
            upEqp.IsNeedProfileSetup = true;

            ws.JobConditionGroup.Reset(true);
            ws.JobConditionGroup.DoProfile(true);

            upEqp.IsNeedProfileSetup = false;
        }

        private static SEMAoEquipment GetTryTradeEqp(this SEMWorkStep ws, List<Profile> delayProfiles)
        {           

            var loadedEqps = ws.GetLoadedEqpList();
            foreach (var delayProfile in delayProfiles)
            {
                var loadableEqps = new List<SEMAoEquipment>();

                foreach (var e in ws.LoadableEqps)
                {
                    var aeqp = e as SEMAoEquipment;

                    if (loadedEqps.Contains(e))
                        continue;

                    if (delayProfile.Lot.LoadableEqp.Contains(aeqp) == false)
                        continue;
                    loadableEqps.Add(aeqp);
                }

                if (loadableEqps.IsNullOrEmpty())
                    continue;

                var result = loadableEqps.OrderBy(x => x.Data.MainProfile.LpstGapDay).First();
                return result;
            }

            return null;
        }

        private static bool IsDelaySolved(this SEMWorkStep ws, List<Profile> delayProfiles, out Profile solvedProfile)
        {
            foreach (var p in delayProfiles)
            {
                var newDelayProfile = ws.JobConditionGroup.Profiles.Where(x => x.LotID == p.LotID).FirstOrDefault();
                if (newDelayProfile == null)
                {
                    solvedProfile = null;
                    return false;
                } 

                if (newDelayProfile.DueState != Constants.Delay)
                {
                    solvedProfile = p;
                    return true;
                }

                // [ToDo] 이거 할까??
                if (newDelayProfile.LpstGapDay < p.LpstGapDay)
                {
                    solvedProfile = p;
                    return true;
                }
            }

            solvedProfile = null;
            return false;
        }

        public static List<Profile> GetArrivedProfiles(this List<Profile> profiles)
        {
            if (profiles.IsNullOrEmpty())
                return new List<Profile>();

            var now = AoFactory.Current.NowDT;
            var arriveFenceTime = now.AddMinutes(GlobalParameters.Instance.AgentCycleMinutes);
            var result = profiles.Where(x => x.LotAvailableTime < arriveFenceTime).ToList();

            return result;
        }

        public static List<Profile> GetDelayProfiles(this List<Profile> profiles)
        {
            if (profiles.IsNullOrEmpty())
                return new List<Profile>();

            var now = AoFactory.Current.NowDT;
            var inflowFenceTime = now.AddMinutes(GlobalParameters.Instance.AgentCycleMinutes);

            List<Profile> result = profiles.Where(x => x.LotAvailableTime < inflowFenceTime
                                                    && ((x.GetDueState() == Constants.Delay) || (x.GetDueState() == Constants.Delay && x.WorkLotType == "UNPLAN"))).ToList();
            for (int i = result.Count - 1; i >= 0; i--)
            {
                var p = result[i];
                if (p.LotAvailableTime == DateTime.MinValue)
                    result.Remove(p);
            }

            return result;
        }

        private static void TryDown(this SEMWorkStep ws, SEMAoEquipment upEqp)
        {
            ws.RemoveLoadedEqp(upEqp, false);

            ws.JobConditionGroup.Reset(true);
            ws.JobConditionGroup.DoProfile(true);
        }

        private static bool IsDown(this SEMWorkStep ws, ref string decisionReason)
        {
            var profiles = ws.WsData.Profiles.Where(x => x.WorkLotType == "UNPLAN" || x.WorkLotType == "BUSY");
            if (profiles.Count() == 0)
            {
                decisionReason = "No Profile";
                return true;
            }

            return false;
        }

        private static bool IsKeep(this SEMWorkStep ws, ref string decisionReason)
        {
            decisionReason = decisionReason.IsNullOrEmpty() ? "Default Keep" : decisionReason;
            return true;
        }

        private static void SetEqpDecisionType(this SEMWorkStep ws)
        {
            // weqp.Profiles.Where(x => x.WorkLotType == "BUSY").ToList().First()로 계산하면 될 듯


            //foreach (var e in ws.LoadedEqps)
            //{
            //    var weqp = e as SEMWorkEqp;

            //    var profiles = weqp.Profiles.Where(x => x.WorkLotType == "BUSY").ToList();
            //    if (profiles.IsNullOrEmpty())
            //    {
            //        //weqp.DecisionType = OperationType.Down;
            //        weqp.DueState = Constants.None;

            //        continue;
            //    }

            //    weqp.DueState = profiles.First().DueState;
            //}
        }

        private static void SetPriority(this SEMWorkStep ws)
        {
            var wsdt = ws.Data as WorkStepData;
            DateTime now = AoFactory.Current.NowDT;

            double priority = 0;
            string priorityLog = string.Empty;

            bool isResFix = ws.IsResFix && ws.Wips.Count > 0;

            // Res Fix Lot
            if (isResFix)
            {
                SEMLot lot = ws.Wips.First().Lot as SEMLot;

                // Res Fix 점수
                priority += 10000000;
                priorityLog = priorityLog + $"RES_FIX(10000000000)/";

                // 작업조건 일치
                SEMEqp eqp = lot.Wip.LotArrangedEqpDic[ws.Key.ToString()].First();
                if (eqp.IsNeedSetup(lot) == false)
                {
                    priority += 1000;
                    priorityLog = priorityLog + $"NO_SETUP(1000)/";
                }

                // ReelLabel 유무
                if (ws.HasReelLabeledWip())
                {
                    priority += 100;
                    priorityLog = priorityLog + $"LABEL(100)/";
                }

                // 납기
                DateTime lpst = lot.GetLPST(ws.Key.ToString());
                double lpstGapDay = lpst == DateTime.MaxValue ? 9999 : Math.Round((now - lpst).TotalDays, 2);
                double dueScore = ws.GetDueScore();
                string dueState = AgentHelper.GetDueState(lpstGapDay);

                priority += dueScore / 100000;
                priorityLog = priorityLog + $"{dueState}({dueScore / 100000})/";

                priority -= lpstGapDay / 10;
                priorityLog = priorityLog + $"LPST({lpstGapDay / 10})/";


                // 도착 시간 빠름
                double waitDay = Math.Round((now - lot.PreEndTime).TotalDays);
                priority += waitDay;
                priorityLog = priorityLog + $"WAIT({waitDay})/";
            }
            else
            {
                var profile = ws.WsData.Profiles.Where(x => x.WorkLotType == "BUSY" || x.WorkLotType == "UNPLAN").FirstOrDefault();

                if (profile == null)
                {
                    priority = -9999999999;
                    priorityLog = $"NoWip";
                }
                else
                {
                    SEMLot lot = profile.Lot.Lot as SEMLot;

                    DateTime lpst = profile.Lpst;

                    double dueScore = AgentHelper.GetDueScore(profile.GetDueState());
                    double waitDay = Math.Round((now - lot.PreEndTime).TotalDays);

                    // ReelLabel 유무
                    if (ws.HasReelLabeledWip())
                    {
                        priority += 1000000;
                        priorityLog = priorityLog + $"LABEL(1000000)/";
                    }

                    // 납기 factor
                    priority += dueScore;
                    priorityLog = priorityLog + $"dueState({profile.GetDueState()})/";

                    priority += profile.GetLpstGapDay() * 10;
                    priorityLog = priorityLog + $"LPST({profile.GetLpstGapDay() * 10})/";

                    // 도착 시간 빠름
                    priority += waitDay * 0.1;
                    priorityLog = priorityLog + $"WAIT({waitDay * 0.1})/";

                    if (ws.GetLoadedEqpCount() == 0) //step.LoadedEqpCount == 0)
                    {
                        bool hasReelLabledWip = ws.HasReelLabeledWip();
                        bool hasUrgentWip = ws.HasUrgentWip();

                        if (hasReelLabledWip && hasUrgentWip)
                        {
                            priority += 110000;
                            priorityLog = priorityLog + $"/Labled&UrgentWipNoEqp(110000)";
                        }
                        else if(hasReelLabledWip) 
                        {
                            priority += 100000;
                            priorityLog = priorityLog + $"/LabledWipNoEqp(100000)";
                        }
                        else if (hasUrgentWip)
                        {
                            priority += 10000;
                            priorityLog = priorityLog + $"/UrgentWipNoEqp(10000)";
                        }
                        else
                        {
                            priority += 1;
                            priorityLog = priorityLog + $"/NoEqp(1)";
                        }
                    }

                    if (ws.GetLoadedEqpCount() == 0) //step.LoadedEqpCount == 0)
                    {
                        priority += 1;
                        priorityLog = priorityLog + $"/NoEqp(1)";
                    }

                }
            }

            wsdt.Priority = priority;
            wsdt.PriorityLog = priorityLog;
        }

        private static void WriteJobGroupDecisionLog(JobConditionGroup jGroup)
        {
            Outputs.DECISION_LOG2 log = new DECISION_LOG2();

            log.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            log.PHASE = jGroup.Data.Phase;

            log.JOB_GROUP = jGroup.Key;
            log.WORK_GROUP = jGroup.Key;
            log.STEP_ID = jGroup.StepKey;

            log.DECISION_TYPE = jGroup.Data.Decision.ToString();
            log.DECISION_REASON = jGroup.Data.DecisionReason;

            log.LOADED_EQP_CNT = jGroup.Data.LoadedEqps.Count();
            log.LOADED_EQPS = jGroup.Data.LoadedEqps.Select(x => x.EqpID).ListToString();

            log.PROFILE_INFO = jGroup.Data.LoadedEqps.GetProfileInfo();

            log.WIP_CNT = jGroup.Data.Wips.Count;
            log.WIPS = jGroup.Data.Wips.OrderBy(x => x.AvailableTime).Select(x => x.Lot.LotID).ListToString();

            log.AVAIL_WIP_CNT = jGroup.Data.AvailWips.Count();            
            log.AVAIL_WIPS = jGroup.Data.AvailWips.OrderBy(x=>x.AvailableTime).Select(x => x.Lot.LotID).ListToString();

            log.CAPA = jGroup.Data.Capa;
            log.NEEDED_CAPA = jGroup.Data.NeedCapa;

            OutputMart.Instance.DECISION_LOG2.Add(log);
        }

        public static string GetProfileInfo(this JobConditionGroup jGroup)
        {
            string profileInfo = string.Empty;

            int i = 0;
            foreach (var aeqp in jGroup.Data.LoadedEqps)
            {
                if (i != 0)
                    profileInfo += ", ";

                var p = aeqp.Data.Profiles.Where(x => x.WorkLotType == "BUSY").FirstOrDefault();
                profileInfo += $"{aeqp.EqpID}({p.LotID}/{p.GetDueState()}({p.GetLpstGapDay()}))";
            }

            return profileInfo;
        }

        private static void WriteWorkStepDecisionLog(SEMWorkStep ws)
        {
            Outputs.DECISION_LOG2 log = new DECISION_LOG2();
            log.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            log.PHASE = ws.JobConditionGroup.Data.Phase;

            log.JOB_GROUP = ws.JobConditionGroup.Key;
            log.WORK_GROUP = ws.GroupKey;
            log.STEP_ID = ws.StepKey;

            log.PRIORITY = ws.WsData.Priority;
            log.PRIORITY_LOG = ws.WsData.PriorityLog;

            log.DECISION_TYPE = ws.WsData.DecidedOperationType.ToString();
            log.DECISION_REASON = ws.WsData.DecisionReason;

            log.LOADED_EQP_CNT = ws.LoadedEqps.Count();
            log.LOADED_EQPS = ws.LoadedEqps.Select(x => x.Target.EqpID).ListToString();

            var loadedEqps = ws.GetLoadedEqpList();
            log.TOTAL_LOADED_EQP = $"({loadedEqps.Count}){loadedEqps.Select(x => x.EqpID).ListToString()}";

            log.PROFILE_INFO = GetProfileInfo(loadedEqps);

            log.WIP_CNT = ws.Wips.Count;
            log.WIPS = ws.Wips.OrderBy(x=>x.AvailableTime).Select(x => x.Lot.LotID).ListToString();

            log.AVAIL_WIP_CNT = ws.WsData.AvailWips.Count();
            log.AVAIL_WIPS = ws.WsData.AvailWips.OrderBy(x => x.AvailableTime).Select(x => x.Lot.LotID).ListToString();

            log.CAPA = ws.WsData.Capa;
            log.NEEDED_CAPA = ws.WsData.NeededCapa;

            OutputMart.Instance.DECISION_LOG2.Add(log);
        }

        public static string GetProfileInfo(this List<SEMAoEquipment> loadedEqps)
        {
            string profileInfo = string.Empty;

            int i = 0;
            foreach (var aeqp in loadedEqps)
            {
                if (i != 0)
                    profileInfo += ",  ";

                var p = aeqp.Data.MainProfile;
                if (p == null)
                    profileInfo += $"{aeqp.EqpID}(NoWip/{double.NaN}({Constants.None}))";
                else
                    profileInfo += $"{aeqp.EqpID}({p.LotID}/{p.GetDueState()}({p.GetLpstGapDay()}/{p.LotAvailableTime.DbToString()}))";
                i++;
            }

            return profileInfo;
        }
    }
}