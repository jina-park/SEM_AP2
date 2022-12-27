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

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class AgentAssignEqpHelper
    {
        public static List<EqpAssignInfo> Infos = new List<EqpAssignInfo>();

        public static List<AssignEqp> DoFilterAssignEqp(SEMWorkStep ws, List<AssignEqp> assignEqps, JobChangeContext context)
        {
            List<AssignEqp> result = new List<AssignEqp>();

            SEMWorkGroup wg = ws.Group as SEMWorkGroup;
            SEMWorkStep upWorkStep = ws as SEMWorkStep;

            DoFilterInit(upWorkStep);

            List<EqpAssignInfo> infos = upWorkStep.GetEqpAssignInfo();

            // Set filter info         
            infos.SetAssignInfo(upWorkStep, assignEqps);

            // DoFilter
            infos.DoFilterAssignEqp(upWorkStep, assignEqps);

            // Sort
            //infos.Sort(new Comparers.AssignEqpCompare());
            infos.Sort(upWorkStep);

            // Set Priority
            //infos.SetPriority();

            // Return
            if (wg.IsResFix)
                result = infos.Select(x => x.AssignEqp).ToList();
            else
                result = infos.Where(x => x.IsFiltered == false).Select(x => x.AssignEqp).ToList();

            // return 값이 없는 경우 이 위치에서 log 작성
            if (result.IsNullOrEmpty())
            {
                // write log 
                foreach (var info in Infos)
                    info.WriteJobChangeAssignEqpFilterLog(upWorkStep as SEMWorkStep);
            }

            return result;
        }

        public static void DoFilterInit(SEMWorkStep ws)
        {
            Infos = new List<EqpAssignInfo>();
        }

        public static List<EqpAssignInfo> GetEqpAssignInfo(this SEMWorkStep ws)
        {
            List<EqpAssignInfo> infos = new List<EqpAssignInfo>();

            foreach (var e in ws.LoadableEqps)
            {
                EqpAssignInfo ei = new EqpAssignInfo();
                ei.Eqp = e.Target as SEMEqp;
                ei.AoEqp = e as SEMAoEquipment;

                var assignEqp = e.GetAssignEqp();
                if (assignEqp != null)
                {
                    ei.AssignEqp = assignEqp;
                    ei.WorkStep = assignEqp.WorkStep as SEMWorkStep;

                    if (assignEqp.WorkStep != null)
                        ei.WorkEqp = assignEqp.WorkStep.LoadedEqps.Where(x => x.Target.EqpID == e.EqpID).FirstOrDefault() as SEMWorkEqp;

                    ei.Result = "Assignable";

                }

                infos.Add(ei);
            }

            Infos.AddRange(infos);

            return infos;
        }


        public static void SetAssignInfo(this List<EqpAssignInfo> infos, SEMWorkStep upWorkStep, List<AssignEqp> assignableEqps)
        {
            foreach (var info in infos)
            {
                if (info.AssignEqp == null)
                {
                    info.SetInfo("Filtered", $"Not Found Eqp", true);

                    return;
                }

                // DueState 계산
                info.SetDueState(upWorkStep);

                // Waiting Time 계산
                info.SetWaitingTime();

                //SetupTime 계산                
                info.SetSetupTime(upWorkStep);
            }
        }

        private static void SetDueState(this EqpAssignInfo info, SEMWorkStep upWorkStep)
        {
            info.upWsLpstGapDay = upWorkStep.WsData.MainProfile.GetLpstGapDay();
            info.upWsState = upWorkStep.WsData.MainProfile.GetDueState();

            info.LpstGapDay = info.AoEqp.Data.MainProfile.GetLpstGapDay();
            info.DueState = info.AoEqp.Data.MainProfile.GetDueState();
        }

        public static double GetLpstGapDay(this Profile profile)
        {
            if (profile == null)
                return double.NaN;

            var now = AoFactory.Current.NowDT;
            var result = (now - profile.Lpst).TotalDays;

            return Math.Round(result, 2);
        }

        public static string GetDueState(this Profile profile)
        {
            if (profile == null)
                return Constants.None;

            var now = AoFactory.Current.NowDT;
            var lpstGapDay = (now - profile.Lpst).TotalDays;

            var result = AgentHelper.GetDueState(lpstGapDay);

            return result;
        }

        private static void SetWaitingTime(this EqpAssignInfo info)
        {
            var now = AoFactory.Current.NowDT;
            var eqpEndTime = info.AoEqp.LotEndTime == DateTime.MinValue ? now : info.AoEqp.LotEndTime;

            info.WaitingTime = (eqpEndTime - now).TotalMinutes;
        }

        private static void SetSetupTime(this EqpAssignInfo info, SEMWorkStep ws)
        {
            var wLot = ws.Inflows.FirstOrDefault();

            if (wLot == null)
                return;

            var sampleLot = wLot.Lot as SEMLot;
            double setupTime = info.Eqp.GetSetupTimeForJCAgent(sampleLot);

            info.SetupTime = setupTime;
        }


        public static void SetInfo(this EqpAssignInfo info, string result, string reason, bool isfiltered)
        {
            if (info.Result.IsNullOrEmpty() == false || info.Reason.IsNullOrEmpty() == false || info.IsFiltered) { }

            info.Result = result;
            info.Reason = reason;
            info.IsFiltered = isfiltered;
        }


        public static void DoFilterAssignEqp(this List<EqpAssignInfo> infos, SEMWorkStep upWorkStep, List<AssignEqp> assignableEqps)
        {
            foreach (var info in infos)
            {
                // 할당 가능한 list에 없는 장비 fiter
                if (info.AssignEqp != null && assignableEqps.Contains(info.AssignEqp) == false)
                {
                    if (upWorkStep.LoadedEqps.Any(x => x.Target.EqpID == info.EqpID))
                    {
                        //이미 현재 WG에 할당되어있음
                        info.SetInfo("Assigned", $"Already Assigned in Work Group", true);

                        //WriteLog.WriteJobChangeAssignEqpFilterLog(upWorkStep, e.EqpID, "Assigned", $"Already Assigned in Work Group");
                    }
                    else
                    {
                        //이미 다른 WG에 할당 됨
                        info.SetInfo("Filtered", $"Assigned in Other High Priority Work Group({info.AssignEqp.WorkStep.Group.Key.ToString()})", true);

                        //WriteLog.WriteJobChangeAssignEqpFilterLog(upWorkStep, e.EqpID, "Filtered", $"Assigned in Other High Priority Work Group({asEqp.WorkStep.Group.Key.ToString()})");
                    }
                    continue;
                }

                // 이미 같은 Job 그룹에 할당되어있는 장비 Filter
                //if (upWorkStep.GetLoadedEqpList().Contains(info.AoEqp))
                //{
                //    string gKey = info.AssignEqp.WorkStep.Group.Key.ToString();
                //    info.SetInfo("Assigned", $"Already Assigned in Job Group ({gKey})", true);

                //    //WriteLog.WriteJobChangeAssignEqpFilterLog(upWorkStep, aeqp.EqpID, "Assigned", $"Already Assigned in Job Group ({gKey})");
                //    continue;
                //}

                // 다른 Lot에 fixed인 장비 Filter
                if (info.Eqp.ResFixedWips.Count > 0)
                {
                    var fixedLot = info.Eqp.ResFixedWips.First();

                    if (upWorkStep.Wips.Any(x => x.Lot.LotID == fixedLot.LotID) == false)
                    {
                        info.SetInfo("Filtered", "Res Fixed Eqp", true);

                        // WriteLog.WriteJobChangeAssignEqpFilterLog(upWorkStep, eqp.Target.EqpID, "Filtered", $"Res Fixed Eqp");
                        continue;
                    }
                }

                // Reel Label Wip 작업중(예정)인 장비 Filter
                if (info.WorkStep.HasReelLabeledWip())
                {
                    info.SetInfo("Filtered", $"Processing ReelLabelWip", true);
                    continue;
                }

                // Same Lot을 연속 작업중인 장비 Filter
                if (info.AoEqp.IsSameLotHold)
                {
                    info.SetInfo("Filtered", $"Processing Same Lot", true);
                    continue;
                }

                if (upWorkStep.HasUrgentWip() == false || upWorkStep.HasReelLabeledWip() == false)
                {
                    // DueState 비교
                    if (info.IsCheckDueStateFilter(upWorkStep))
                    {
                        if (IsDueStateFilter(info.DueState, info.upWsState))
                        {
                            info.SetInfo("Filtered", "Lower Due State", true);
                            info.DueStateLog = "X";
                            continue;
                        }
                    }

                    // 장비의 현 wg 다음 lot 유입 시간 보다 job change 후 투입 가능 시간(setup끝나는 시간)이 더 늦은 경우 filter
                    // = 지금은 투입할 lot이 없지만 곧 lot이 들어오는 경우 filter
                    if (info.WorkStep != null)
                    {
                        var inflowFastLot = info.AoEqp.Data.Profiles.Where(x=>x.WorkLotType=="BUSY"|| x.WorkLotType=="UNPLAN").FirstOrDefault();
                        if (inflowFastLot != null)
                        {
                            var eqpEndTime = info.AoEqp.LotEndTime == DateTime.MinValue ? AoFactory.Current.NowDT : info.AoEqp.LotEndTime;
                            var setupEndTime = eqpEndTime.AddMinutes(info.SetupTime);

                            // 장비의 다음 유입 lot 들어오는 시간이 
                            if (inflowFastLot.LotAvailableTime < setupEndTime)
                            {
                                var lot = inflowFastLot.Lot.Lot as SEMLot;
                                var lpst = lot.GetLPST(info.WorkStep.StepKey);
                                var lpstGapDay = (inflowFastLot.LotAvailableTime - lpst).TotalDays;
                                var lotDueState = AgentHelper.GetDueState(lpstGapDay);

                                if (IsDueStateFilter(lotDueState, info.upWsState))
                                {
                                    info.SetInfo("Filtered", "waiting next lot is better", true);
                                    continue;
                                }
                            }
                        }
                    }
                }

                if (upWorkStep.HasReelLabeledWip() == false)
                {
                    // 긴급 우선순위 fiter
                    if (upWorkStep.HasUrgentWip() || info.WorkStep.HasUrgentWip())
                    {
                        var upWsUrgentPriority = upWorkStep.GetUrgentPriority();
                        var eqpUrgnetPriority = info.WorkStep.GetUrgentPriority();

                        if (upWsUrgentPriority >= eqpUrgnetPriority)
                        {
                            info.SetInfo("Filtered", "Urgent Eqp", true);

                            continue;
                        }
                    }
                }


            }
        }

        private static bool IsCheckDueStateFilter(this EqpAssignInfo info, SEMWorkStep upWs)
        {
            if (upWs.HasUrgentWip())
                return false;

            if (info.WorkStep == null)
                return false;

            if (info.WorkStep.WsData.DecidedOperationType == OperationType.Down)
                return false;

            if (info.IsResFix)
                return false;

            return true;
        }

        private static bool IsLongSetupTime(this EqpAssignInfo info)
        {
            string stepID = info.WorkStep == null ? info.Eqp.OperIDs.FirstOrDefault() : info.WorkStep.StepKey;


            if (stepID == "SG4430")
            {
                if (info.SetupTime > 60)
                    return true;
                else
                    return false;
            }
            else
            {
                if (info.SetupTime > 360)
                    return true;
                else
                    return false;
            }
        }

        public static bool IsDueStateFilter(string eqpWsState, string upWsState)
        {
            bool isDueStateFilter = false;

            if (eqpWsState == Constants.Delay)
            {
                isDueStateFilter = true;
            }
            else if (eqpWsState == Constants.Normal)
            {
                if (upWsState == Constants.Delay)
                {
                    //Filter x
                }
                else if (upWsState == Constants.Normal)
                {
                    isDueStateFilter = true;
                }
                else if (upWsState == Constants.Precede)
                {
                    isDueStateFilter = true;
                }
                else if (upWsState == Constants.None)
                {
                    isDueStateFilter = true;
                }
                else
                {
                    WriteLog.WriteErrorLog("알 수 없는 DueState입니다.");
                }
            }
            else if (eqpWsState == Constants.Precede)
            {
                if (upWsState == Constants.Delay)
                {
                    //Filter x
                }
                else if (upWsState == Constants.Normal)
                {
                    //Filter x
                }
                else if (upWsState == Constants.Precede)
                {
                    isDueStateFilter = true;
                }
                else if (upWsState == Constants.None)
                {
                    isDueStateFilter = true;
                }
                else
                {
                    WriteLog.WriteErrorLog("알 수 없는 DueState입니다.");
                }
            }
            else if (eqpWsState == Constants.None)
            {
                if (upWsState == Constants.Delay)
                {
                    //Filter x
                }
                else if (upWsState == Constants.Normal)
                {
                    //Filter x
                }
                else if (upWsState == Constants.Precede)
                {
                    //Filter x
                }
                else if (upWsState == Constants.None)
                {
                    isDueStateFilter = true;
                }
                else
                {
                    WriteLog.WriteErrorLog("알 수 없는 DueState입니다.");
                }
            }
            else
            {
                WriteLog.WriteErrorLog("알 수 없는 DueState입니다.");
                //error
            }
            return isDueStateFilter;
        }

        public static void SetPriority(this List<EqpAssignInfo> infos)
        {
            int priority = 0;
            foreach (var info in infos)
            {
                info.Priority = priority++;

                // wating time

                // setup time

                // factory

                // setup시간과  idle 시간 비교
            }
        }

        public static EqpAssignInfo SelectAssignEqp(this List<AssignEqp> assignableEqps, SEMWorkStep upWs)
        {
            if (upWs.IsResFix)
            {
                var eqp = assignableEqps.FirstOrDefault();
                var info = Infos.Where(x => x.EqpID == eqp.Target.EqpID).FirstOrDefault();
                return info;
            }

            var firstEqp = assignableEqps.First();
            var firstInfo = Infos.Where(x => x.EqpID == firstEqp.Target.EqpID).FirstOrDefault();

            // 셋업시간이 길면서 현재 진행중인 작업이 많이 남은 장비 filter
            //if (firstInfo.WaitingTime > 180 && firstInfo.IsLongSetupTime())
            //{
            //    firstInfo.SetInfo("Filtered", "Too Long SetupTime", true);

            //    foreach (var eqp in assignableEqps)
            //    {
            //        var info = Infos.Where(x => x.EqpID == eqp.Target.EqpID).FirstOrDefault();

            //        if (info == firstInfo)
            //            continue;

            //        var eqpWs = info.WorkStep;

            //        info.SetInfo("Filtered", "AssignPostpone", true);
            //    }

            //    return null;
            //}
            /////

            // 할당할 장비에 투입 가능 시간이 60분이상이면 판단 유예
            if (firstInfo.WaitingTime > 60)
            {
                firstInfo.SetInfo("Filtered", "Assinable But Processing Eqp", true);

                foreach (var eqp in assignableEqps)
                {
                    var info = Infos.Where(x => x.EqpID == eqp.Target.EqpID).FirstOrDefault();

                    if (info == firstInfo)
                        continue;

                    var eqpWs = info.WorkStep;

                    if(info.IsFiltered == false)
                        info.SetInfo("Filtered", "Assinable But AssignPostpone", true);
                }

                return null;
            }

            if (upWs.WsData.DecisionReason == "(Delay up)")
            {
                var newInTime = AoFactory.Current.NowDT.AddMinutes(firstInfo.WaitingTime + firstInfo.SetupTime);

                var delayProfiles = upWs.WsData.Profiles.GetDelayProfiles();
                foreach (var p in delayProfiles)
                {
                    if (p.InTime < newInTime)
                    {
                        firstInfo.SetInfo("Filtered", "New Assign Make Delay", true);

                        foreach (var eqp in assignableEqps)
                        {
                            var info = Infos.Where(x => x.EqpID == eqp.Target.EqpID).FirstOrDefault();

                            if (info == firstInfo)
                                continue;

                            var eqpWs = info.WorkStep;

                            if (info.IsFiltered == false)
                                info.SetInfo("Filtered", "AssignPostpone(New Assign Make Delay)", true);
                        }

                        return null;
                    }
                }

                firstInfo.Reason = "Delay Up Assign";
            }


            return firstInfo;
        }

        private static void Sort(this List<EqpAssignInfo> infos, SEMWorkStep upWorkStep)
        {
            if (infos.IsNullOrEmpty() || infos.Count == 1)
                return;

            var firstInfo = infos.First();
            if (firstInfo.upWsState == Constants.Delay || upWorkStep.HasUrgentWip())
            {
                infos.Sort(new Comparers.AssignEqpCompareForUrgent());
            }
            else 
            {
                infos.Sort(new Comparers.AssignEqpCompare());
            }

            return;
        }


    }
}