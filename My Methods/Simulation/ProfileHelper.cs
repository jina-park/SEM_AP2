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
    public static partial class ProfileHelper
    {
        public static JobConditionGroup JobGroup;

        public static void Profile(SEMWorkAgent agent)
        {
            OnInitializeProfile(agent);

            DoProfile(agent);

            OnAfterProfile(agent);
        }

        public static void OnInitializeProfile(SEMWorkAgent agent)
        {
            // Job Group 내 profile 관련정보 초기화
            foreach (var jGroup in agent.JobConditionGroups)
            {
                jGroup.Reset();
            }
        }

        public static void DoProfile(SEMWorkAgent agent, bool isReCalc = false)
        {
            foreach (var jGroup in agent.JobConditionGroups)
            {
                jGroup.DoProfile(isReCalc);
            }
        }

        public static void DoProfile(this JobConditionGroup jGroup, bool isReCalc = false)
        {
            jGroup.Calculate(isReCalc);

            foreach (var ws in jGroup.Steps)
                ws.SetMainProfile();
        }

        public static void OnAfterProfile(SEMWorkAgent agent)
        { }

        public static void Reset(this JobConditionGroup jGroup, bool isReCalc = false)
        {
            int phase = isReCalc ? jGroup.Data.Phase + 1 : 0;
            jGroup.Data = new JobGroupData();
            jGroup.Data.Phase = phase;

            bool isResetWips = !isReCalc;
            jGroup.Steps.ForEach(x => x.Reset(isResetWips));
            jGroup.Steps.ForEach(x => x.Data = new WorkStepData());
            jGroup.Steps.ForEach(x => x.LoadedEqps.ForEach(y => (y.Target as SEMAoEquipment).Reset()));
            
            jGroup.Profiles.Clear();
        }


        private static void Calculate(this JobConditionGroup jGroup, bool isReCalc = false)
        {
            JobGroup = jGroup;
            
            // Profile 대상 Eqp List
            var loadedEqps = jGroup.GetLoadedEqp();

            // Profile 대상 lot List
            var lots = jGroup.GetLots();

            //관련정보 캐싱
            jGroup.Data.LoadedEqps = loadedEqps;
            jGroup.Data.Wips = lots;

            // 투입 우선순위에 따라 lot 정렬
            lots = lots.OrderBy(x=>x.Lpst).ToList();

            // 이미 장비에 들어있는 lot에 대한 Profile 생성
            ProfileInit(loadedEqps);

            // 장비와 lot을 선택하여 Load
            DateTime now = AoFactory.Current.NowDT;

            int i = 0;
            while (true)
            {
                // Select Eqp
                var selectedEqp = loadedEqps.SelectProfileEqp();
                if (selectedEqp == null)
                    break;

                now = selectedEqp.Data.AvailableTime;

                //if (ModelContext.Current.EndTime < now)
                //{
                //    selectedEqp.IsProfileDone = true;
                //    continue;
                //}

                // Select Lot
                var avilableLots = lots.GetAvailableLots(selectedEqp);
                if (avilableLots.IsNullOrEmpty())
                {
                    AddIdleProfile(selectedEqp, null, now);

                    selectedEqp.Data.IsProfileDone = true;
                    continue;
                }

                var selectedLot = avilableLots.SelectLot(now);
                if (now < selectedLot.AvailableTime)
                {
                    AddIdleProfile(selectedEqp, selectedLot, now);

                    now = (DateTime)selectedLot.AvailableTime;
                }

                // 장비에 lot을 Load
                selectedEqp.Load(selectedLot, now);

                // profile list에서 load한 lot을 삭제
                lots.Remove(selectedLot);

                // load한 lot을 다음 work step으로 Advance
                Advance(selectedLot, isReCalc);

                // 무한루프 방지
                i++;
                if (i == 10000)
                {
                    WriteLog.WriteErrorLog($"무한루프 발생 {AoFactory.Current.NowDT.DbToString()} {JobGroup.Key}");
                    break;
                }
            }

            // 투입되지 않고 남은 lot에 대해 log작성
            WriteRemainLotsProfile(lots);
        }


        public static void ProfileInit(List<SEMAoEquipment> loadedEqps)
        {
            foreach (var aeqp in loadedEqps)
            {
                if (aeqp.ProcessingLot == null)
                {
                    aeqp.Data.AvailableTime = AoFactory.Current.NowDT;
                }
                else
                {
                    aeqp.Data.AvailableTime = aeqp.LotEndTime;

                    Profile p = new Profile();
                    p.Eqp = aeqp;
                    p.EqpID = aeqp.EqpID;
                    p.Lot = null;
                    p.LotID = aeqp.ProcessingLot.LotID;
                    p.InTime = aeqp.LotStartTime;
                    p.OutTime = aeqp.LotEndTime;
                    p.JobGroup = JobGroup;

                    p.EqpWorkStep = aeqp.Data.WorkStep;
                    p.LotWorkStep = null;

                    p.WorkGroup = aeqp.Data.WorkStep.Group as SEMWorkGroup;
                    p.WorkLotType = "RUNNING";

                    p.IsAdvanced = false;
                    p.IsArriveWait = false;
                    p.IsRunningLot = true;
                    p.IsShort = false;

                    p.Phase = JobGroup.Data.Phase;

                    aeqp.Data.Profiles.Add(p);
                    JobGroup.Profiles.Add(p);

                    WriteProfileLog(p);
                }
            }
        }

        private static SEMWorkLot GetSampleLot(this JobConditionGroup jGroup)
        {
            var step = jGroup.Steps.FirstOrDefault();
            if (step == null)
                return null;

            var lot = step.Wips.FirstOrDefault();
            return lot as SEMWorkLot;
        }

        public static List<SEMAoEquipment> GetLoadedEqp(this JobConditionGroup jGroup)
        {
            List<SEMAoEquipment> result = new List<SEMAoEquipment>();

            foreach (var ws in jGroup.Steps)
            {
                foreach (var e in ws.LoadedEqps)
                {
                    var aeqp = e.Target as SEMAoEquipment;

                    // aeqp.Reset();
                    aeqp.Data.WorkStep = ws;

                    result.Add(aeqp);
                }
            }

            return result;
        }

        private static void Reset(this SEMAoEquipment aeqp)
        {
            aeqp.Data = new WorkEqpData();

            aeqp.Data.AvailableTime = AoFactory.Current.NowDT;
            aeqp.Data.IsProfileDone = false;
            aeqp.Data.DueState = string.Empty;
            aeqp.Data.Profiles.Clear();
        }

        public static List<SEMWorkLot> GetLots(this JobConditionGroup jGroup)
        {
            List<WorkLot> wips = new List<WorkLot>();

            foreach (var ws in jGroup.Steps)
            {
                wips.AddRange(ws.Wips);
            }


            List<SEMWorkLot> result = new List<SEMWorkLot>();
            foreach (var wl in wips)
            {
                var wLot = wl as SEMWorkLot;
                wLot.Data = new WorkLotData();
                //초기화 할 값없는지 확인하기
                wLot.IsArriveWait = false;
                //
                result.Add(wLot);
            }
            return result;
        }

        public static SEMAoEquipment SelectProfileEqp(this List<SEMAoEquipment> eqps)
        {
            var selectedEqp = eqps.Where(x => x.Data.IsProfileDone == false).OrderBy(x => x.Data.AvailableTime).FirstOrDefault();

            return selectedEqp;
        }

        public static List<SEMWorkLot> GetAvailableLots(this List<SEMWorkLot> lots, SEMAoEquipment eqp)
        {
            var result = new List<SEMWorkLot>();

            foreach (var wLot in lots)
            {                
                if (wLot.WorkStep.LoadableEqps.Contains(eqp) == false)
                    continue;

                result.Add(wLot);
            }

            return result;
        }

        public static SEMWorkLot SelectLot(this List<SEMWorkLot> lots, DateTime now)
        {
            SEMWorkLot selectedLot = null;
            foreach (var wLot in lots)
            {
                if (now < wLot.AvailableTime)
                    continue;

                if (selectedLot == null)
                    selectedLot = wLot;
                else
                    wLot.IsArriveWait = true;
            }

            // 현재 도착한 lot이 없으면 가장먼저 도착하는 lot을 선택
            if(selectedLot == null)
                selectedLot = lots.OrderBy(x => x.AvailableTime).First();
            return selectedLot;
        }

        private static void AddIdleProfile(SEMAoEquipment selectedEqp, SEMWorkLot selectedLot, DateTime now)
        {
            Profile p = new Profile();
            p.Eqp = selectedEqp;
            p.EqpID = selectedEqp.EqpID;

            p.Lot = null;
            p.LotID = string.Empty;

            p.InTime = now;
            p.OutTime = selectedLot == null ? ModelContext.Current.EndTime : (DateTime)selectedLot.AvailableTime;
            p.WorkLotType = "IDLE";
            p.JobGroup = JobGroup;
            p.WorkGroup = selectedEqp.Data.WorkStep.WorkGroup;
            
            p.EqpWorkStep = selectedEqp.Data.WorkStep;
            p.LotWorkStep = selectedLot == null ? null : selectedLot.WorkStep;

            p.Lpst = DateTime.MaxValue;
            p.LpstGapDay = -99999999;
            p.DueState = Constants.None;

            p.IsAdvanced = false;
            p.IsRunningLot = false;
            p.IsShort = false;
            p.IsArriveWait = false;

            p.Phase = JobGroup.Data.Phase;

            selectedEqp.Data.Profiles.Add(p);
            selectedEqp.Data.WorkStep.WsData.Profiles.Add(p);
            JobGroup.Profiles.Add(p);

            WriteProfileLog(p);
        }


        public static void Load(this SEMAoEquipment aeqp, SEMWorkLot wlot, DateTime now)
        {
            if (aeqp.IsNeedProfileSetup)
            {
                Profile setupProfile = CreateSetupProfile(aeqp, wlot, now);

                aeqp.Data.Profiles.Add(setupProfile);
                JobGroup.Profiles.Add(setupProfile); 
                wlot.Data.Profiles.Add(setupProfile);


                WriteProfileLog(setupProfile);

                now = setupProfile.OutTime;
                aeqp.Data.AvailableTime = setupProfile.OutTime;
                aeqp.IsNeedProfileSetup = false;
            }

            Profile busyProfile = CreateBusyProfile(aeqp, wlot, now);

            aeqp.Data.Profiles.Add(busyProfile);
            aeqp.Data.WorkStep.WsData.Profiles.Add(busyProfile);
            JobGroup.Profiles.Add(busyProfile);

            wlot.Data.Profiles.Add(busyProfile);

            WriteProfileLog(busyProfile);

            aeqp.Data.AvailableTime = busyProfile.OutTime;
        }

        public static Profile CreateSetupProfile(SEMAoEquipment aeqp, SEMWorkLot wlot, DateTime now)
        {
            var eqp = aeqp.Target as SEMEqp;
            var setupTime = eqp.GetSetupTimeForJCAgent(wlot.Lot as SEMLot);
            
            Profile p = new Profile();

            p.Eqp = aeqp;
            p.EqpID = aeqp.EqpID;
            p.Lot = wlot;
            p.LotID = wlot.LotID;
            p.InTime = now;
            p.OutTime = now.AddMinutes(setupTime);
            p.JobGroup = JobGroup;

            p.EqpWorkStep = aeqp.Data.WorkStep;
            p.LotWorkStep = wlot.WorkStep;

            p.WorkGroup = aeqp.Data.WorkStep.Group as SEMWorkGroup;
            p.WorkLotType = "SETUP";

            double lpstGap = Math.Round((p.InTime - wlot.Lpst).TotalDays, 2);
            p.Lpst = wlot.Lpst;
            p.LpstGapDay = lpstGap;
            p.DueState = AgentHelper.GetDueState(lpstGap);

            p.IsShort = lpstGap > 0 && now < ModelContext.Current.EndTime ? true : false;
            p.IsRunningLot = false;
            p.IsAdvanced = wlot.IsAdvanced;
            p.IsArriveWait = wlot.IsArriveWait;

            p.Phase = JobGroup.Data.Phase;

            return p;
        }

        private static Profile CreateBusyProfile(SEMAoEquipment aeqp, SEMWorkLot wlot, DateTime now)
        {
            DateTime outTime = GetOutTime(aeqp, wlot, now);

            Profile p = new Profile();
            p.Eqp = aeqp;
            p.EqpID = aeqp.EqpID;

            p.Lot = wlot;
            p.LotID = wlot.LotID;

            p.LotAvailableTime = (DateTime)wlot.AvailableTime;
            p.InTime = now;
            p.OutTime = outTime;
            p.WaitingTime = Math.Round((now - wlot.AvailableTime).TotalMinutes, 2);
            p.JobGroup = JobGroup;

            p.EqpWorkStep = aeqp.Data.WorkStep;
            p.LotWorkStep = wlot.WorkStep;

            p.WorkGroup = aeqp.Data.WorkStep.WorkGroup as SEMWorkGroup;
            p.WorkLotType = "BUSY";

            double lpstGap = Math.Round((p.InTime - wlot.Lpst).TotalDays, 2);
            p.Lpst = wlot.Lpst;
            p.LpstGapDay = lpstGap;
            p.DueState = AgentHelper.GetDueState(lpstGap);

            p.IsShort = lpstGap > 0 && now < ModelContext.Current.EndTime ? true : false;
            p.IsRunningLot = false;
            p.IsAdvanced = wlot.IsAdvanced;
            p.IsArriveWait = wlot.IsArriveWait;

            p.Phase = JobGroup.Data.Phase;

            return p;
        }

        public static DateTime GetOutTime(SEMAoEquipment aeqp, SEMWorkLot wlot, DateTime inTime)
        {
            var lot = wlot.Lot as SEMLot;
            var step = lot.GetOper(wlot.StepKey.ToString());

            double cycleTime = 0;
            if (step == null)
            {
                cycleTime = GlobalParameters.Instance.DefaultCycleTime;
            }
            else 
            {
                cycleTime = TimeHelper.GetCycleTime(step, aeqp.EqpID);
            }

            double processingTime = cycleTime * lot.UnitQtyDouble;

            DateTime outTime = inTime.AddMinutes(processingTime);
            return outTime;
        }

        public static void WriteProfileLog(Profile p)
        {
            Outputs.PROFILE_LOG2 row = new PROFILE_LOG2();

            row.EVENT_TIME = AoFactory.Current.NowDT.DbToString();
            row.PHASE = p.Phase;

            row.JOB_GROUP = p.JobGroup.Key;
            row.WORK_GROUP = p.WorkGroup.GroupKey;

            row.STEP_ID = p.JobGroup.StepKey;

            row.EQP_ID = p.EqpID;
            row.PROFILE_TYPE = p.WorkLotType;

            row.LOT_AVAILABLE_TIME = p.LotAvailableTime;
            row.IN_TIME = p.InTime;
            row.OUT_TIME = p.OutTime;
            row.PROCESSING_TIME = Math.Round((p.OutTime - p.InTime).TotalMinutes, 2);
            row.WAITING_TIME = p.WaitingTime;

            row.IS_RUNNING_LOT = p.IsRunningLot ? "Y" : "N";
            row.IS_ADVANCED = p.IsAdvanced ? "Y" : "N";
            row.IS_ARRIVE_WAIT = p.IsArriveWait ? "Y" : "N";
            row.IS_SHORT = p.IsShort ? "Y" : "N";

            row.DESC = p.Desc;

            if (p.WorkLotType != "IDLE")
            {
                SEMLot lot = p.GetLot();
                if (lot == null)
                    return;

                row.LOT_ID = lot.LotID;
                row.LOT_LPST = p.Lpst;
                row.LOT_AVAILABLE_TIME = p.LotAvailableTime;
                row.LOT_OPER_ID = lot.CurrentStepID;

                row.LOT_LPST_GAP = p.LpstGapDay;
                row.LOT_DUE_STATE = p.DueState;

                //row.CYCLE_TIME = row.PROCESSING_TIME / lot.UnitQtyDouble;
                row.QTY = lot.UnitQtyDouble;
                row.LOT_PRODUCT_ID = lot.CurrentProductID;
            }
            OutputMart.Instance.PROFILE_LOG2.Add(row);
        
        }

        private static SEMLot GetLot(this Profile p)
        {
            SEMLot lot = null;
            if (p.IsRunningLot)
            {
                InputMart.Instance.SEMLot.TryGetValue(p.LotID, out lot);
                if (lot == null)
                    return null;
            }
            else
            {
                lot = p.Lot.Lot as SEMLot;
            }
            return lot;
        }

        private static void Advance(SEMWorkLot wlot, bool isReCalc)
        {
            if (isReCalc)
                return;

            var currentWorkStep = wlot.WorkStep;

            SEMLot lot = wlot.Lot as SEMLot;
            var prevProfile = wlot.Data.Profiles.Where(x => x.WorkLotType == "BUSY").FirstOrDefault();

            var currentStepPair = lot.WorkStepDic.Where(x => x.Value == currentWorkStep).FirstOrDefault();
            if (currentStepPair.Value == null)
            {
                return;
            }

            if (lot.WorkStepDic.TryGetValue(currentStepPair.Key + 1, out var nextWorkStep) == false)
            {
                // last step
                return;
            }

            var currentStep = lot.GetOper(currentWorkStep.StepKey);
            if (currentStep == null)
                return;

            var ns = currentStep.GetNextOper();
            var ts = lot.GetAgentTargetStep(ns);
            if (ts == null)
                return;

            // Available Time 계산
            Time availableTime = prevProfile.OutTime;
            while (ns != ts)
            {
                var tat = ns.TAT;
                if (tat > 0)
                    availableTime += Time.FromMinutes(tat);

                ns = lot.GetNextOperForJC(ns);
            }

            // 새 work lot 생성
            SEMWorkLot newWLot = new SEMWorkLot(wlot.Lot, availableTime, nextWorkStep.StepKey, ns);            
            newWLot.Lpst = lot.GetLPST(nextWorkStep.StepKey);
            newWLot.WorkStep = nextWorkStep;
            newWLot.LoadableEqp = nextWorkStep.GetLoadableEqps();
            newWLot.IsAdvanced = true;

            // 다음 work step에 투입
            nextWorkStep.Wips.Add(newWLot);
        }


        private static void WriteRemainLotsProfile(List<SEMWorkLot> lots)
        {
            foreach (var remainWLot in lots)
            {
                SEMLot remainLot = remainWLot.Lot as SEMLot;

                Profile p = new Profile();
                p.Lot = remainWLot;
                p.LotID = remainLot.LotID;

                p.Eqp = null;
                p.EqpID = string.Empty;
                
                p.LotAvailableTime = (DateTime)remainWLot.AvailableTime;
                p.WorkLotType = "UNPLAN";
                p.JobGroup = JobGroup;
                p.EqpWorkStep = null;
                p.LotWorkStep = remainWLot.WorkStep;
                p.WorkGroup = remainWLot.WorkStep.WorkGroup;

                double lpstGap = Math.Round((AoFactory.Current.NowDT - remainWLot.Lpst).TotalDays, 2);
                p.Lpst = remainWLot.Lpst;
                p.LpstGapDay = lpstGap;
                p.DueState = AgentHelper.GetDueState(lpstGap);

                p.IsRunningLot = false;
                p.IsAdvanced = remainWLot.IsAdvanced;
                p.IsArriveWait = remainWLot.IsArriveWait;
                p.IsShort = true;

                p.Phase = JobGroup.Data.Phase;

                if (HasArrange(remainWLot))
                    p.Desc = "No Loadable Eqp";

                JobGroup.Profiles.Add(p);
                remainWLot.WorkStep.WsData.Profiles.Add(p);

                WriteProfileLog(p);
            }

        }

        private static bool HasArrange(SEMWorkLot wLot)
        {
            foreach (var eqp in JobGroup.GetLoadedEqp())
            {
                if (wLot.LoadableEqp.Any(x => x.EqpID == eqp.EqpID))
                {
                    return true;
                }
            }
            return false;
        }

        public static void ReProfile(JobConditionGroup jGroup)
        {
            jGroup.Reset(true);

            jGroup.DoProfile(true);

            jGroup.ClassifyDecisionType(true);
        }


        private static void SetMainProfile(this SEMWorkStep ws)
        {
            ws.WsData.MainProfile = ws.GetMainProFile();

            foreach (var eqp in ws.LoadedEqps)
            {
                var aeqp = eqp.Target as SEMAoEquipment;
                aeqp.Data.MainProfile = aeqp.GetMainProFile();
            }
        }


        public static Profile GetMainProFile(this SEMWorkStep ws)
        {
            Profile result = null;

            var profiles = ws.WsData.Profiles.GetCandidateProfiles();

            if (profiles.IsNullOrEmpty() == false)
                result = profiles.GetMainProfile();
            //else
            //{
            //    result = ws.WsData.Profiles.Where(x => (x.WorkLotType == "BUSY" || x.WorkLotType == "UNPLAN")).FirstOrDefault();
            //    if (result == null)
            //        result = ws.WsData.Profiles.Where(x => (x.WorkLotType == "IDEL")).FirstOrDefault();
            //}

            return result;
        }

        public static Profile GetMainProFile(this SEMAoEquipment aeqp)
        {
            Profile result = null;

            var profiles = aeqp.Data.Profiles.GetCandidateProfiles();

            if (profiles.IsNullOrEmpty() == false)
                result = profiles.GetMainProfile();
            //else
            //{
            //    result = aeqp.Data.Profiles.Where(x => (x.WorkLotType == "BUSY" || x.WorkLotType == "UNPLAN")).FirstOrDefault();
            //    if (result == null)
            //        result = aeqp.Data.Profiles.Where(x => (x.WorkLotType == "IDLE")).FirstOrDefault();
            //}

            return result;
        }

        public static List<Profile> GetCandidateProfiles(this List<Profile> totalProfiles)
        {
            var now = AoFactory.Current.NowDT;

            List<Profile> candidateProfiles = totalProfiles.Where(x => (x.WorkLotType == "BUSY" || x.WorkLotType == "UNPLAN") && x.LotAvailableTime < now.AddHours(1)).ToList();

            return candidateProfiles;
        }


        public static Profile GetMainProfile(this IEnumerable<Profile> profiles)
        {
            var delayProfile = profiles.Where(x => x.GetDueState() == Constants.Delay).OrderByDescending(x => x.GetLpstGapDay()).FirstOrDefault();
            if (delayProfile != null)
                return delayProfile;

            var normalProfile = profiles.Where(x => x.GetDueState() == Constants.Normal).OrderByDescending(x => x.GetLpstGapDay()).FirstOrDefault();
            if (normalProfile != null)
                return normalProfile;

            var precedeProfile = profiles.Where(x => x.GetDueState() == Constants.Precede).OrderByDescending(x => x.GetLpstGapDay()).FirstOrDefault();
            if (precedeProfile != null)
                return precedeProfile;

            var noneProfile = profiles.FirstOrDefault();
            return noneProfile;
        }

    }
}