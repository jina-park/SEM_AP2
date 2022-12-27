using Mozart.SeePlan.DataModel;
using Mozart.SeePlan.Simulation;
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
using Mozart.Simulation.Engine;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class AgentInit
    {

        public IEnumerable<string> GET_WORK_AGENT_NAMES0(WorkManager wmanager, ref bool handled, IEnumerable<string> prevReturnValue)
        {
            return new string[] { "DEFAULT" };

        }

        public void INITIALIZE_AGENT0(Mozart.SeePlan.Simulation.WorkAgent wagent, ref bool handled)
        {
            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return;

            AgentHelper.SetAgent();

            wagent.AgentType = AgentType.TRADE; //up대상 공정(workstep)을 우선 결정하는 방식. 

            //AgentInterval 마다 Agent 호출되어 [Profile 생성  Profile 판단  장비할당 순으로 반복함 
            var interval = InputMart.Instance.GlobalParameters.AgentCycleMinutes;
            if (interval <= 0)
                interval = 60;

            //Agent 수행 주기 설정
            wagent.Interval = Time.FromMinutes(interval);
            wagent.IsReleaseDownEqp = InputMart.Instance.GlobalParameters.ApplyIsReleaseDownEqp; //false 
            wagent.AddDummyProfile = InputMart.Instance.GlobalParameters.ApplyDummyProfile; // //false 

            //JobChange 결정후 프로파일을 다시 그리도록 설정
            wagent.IsReProfiling = true;
        }

        public string GET_WORK_AGENT_NAME0(IHandlingBatch hb, ref bool handled, string prevReturnValue)
        {
            var currentStep = hb.CurrentStep as SEMGeneralStep;
            var lot = hb.Sample as SEMLot;

            //if (!AgentHelper.IsAgentInStep(currentStep))
            //    return null;

            return "DEFAULT";
        }

        public object GET_WORK_GROUP_KEY0(IHandlingBatch hb, WorkAgent wagent, ref bool handled, object prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;

            SEMGeneralStep targetStep = lot.GetAgentTargetStep(false);//GetAgentTargetStep(lot.CurrentSEMStep, isRun);            

            string workGroupKey = lot.GetWorkGroupKey(targetStep, wagent);

            return workGroupKey;
        }

        public object GET_WORK_STEP_KEY0(IHandlingBatch hb, WorkGroup wgroup, ref bool handled, object prevReturnValue)
        {
            var lot = hb.Sample as SEMLot;

            SEMGeneralStep ts = lot.GetAgentTargetStep(false);
            if (ts == null)
                return string.Empty;

            return ts.StepID;
        }

        public Step GET_TARGET_STEP0(IHandlingBatch hb, WorkGroup wgroup, object wstepKey, ref bool handled, Step prevReturnValue)
        {
            SEMLot lot = hb.Sample as SEMLot;

            SEMGeneralStep ts = lot.GetAgentTargetStep(false);

            return ts;
        }

        public Time GET_AVAILABLE_TIME0(IHandlingBatch hb, WorkStep wstep, Step targetStep, ref bool handled, Time prevReturnValue)
        {
            var lot = hb.Sample as SEMLot;
            var step = lot.CurrentStep as SEMGeneralStep;

            if (step == targetStep)
                return AoFactory.Current.Now;

            var productID = lot.CurrentProductID;
            var availableTime = AoFactory.Current.NowDT;

            if (lot.Wip.AvailableTime > AoFactory.Current.NowDT)
                availableTime = lot.Wip.AvailableTime;

            if (lot.CurrentState == EntityState.RUN)
            {
                //시작시각 부터 남은 시간을계산해서 available time 업데이트 
                if (lot.CurrentPlan.LoadedResource != null)
                {
                    var aeqp = AoFactory.Current.GetEquipment(lot.CurrentPlan.LoadedResource.ResID);
                    SEMEqp eqp = aeqp.Target as SEMEqp;

                    var procTime = TimeHelper.GetProcessTime(lot, eqp);
                    DateTime nInTime = availableTime.AddSeconds(procTime.TactTime.TotalSeconds * lot.UnitQty);
                    availableTime = nInTime;
                }
                else
                {
                    var tat = step.GetTat(lot.Wip.IsSmallLot);
                    availableTime = availableTime.AddMinutes(tat);
                }

                step = lot.GetNextOperForJC(step);
            }


            while (step != targetStep)
            {
                if (step != null)
                {
                    var tat = step.GetTat(lot.Wip.IsSmallLot);
                    availableTime = availableTime.AddMinutes(tat);
                    step = lot.GetNextOperForJC(step);
                }
            }

            return availableTime;
        }

        public void INITIALIZE_WORK_GROUP0(WorkGroup wgroup, ref bool handled)
        {
            SEMWorkGroup wg = wgroup as SEMWorkGroup;

            wgroup.Ordered = InputMart.Instance.GlobalParameters.ApplyOrderedProfiling;

            if (wg.Key.ToString().Contains("ResFix"))
                wg.IsResFix = true;
        }

        public IEnumerable<AoEquipment> GET_LOADABLE_EQPS0(WorkStep wstep, ref bool handled, IEnumerable<AoEquipment> prevReturnValue)
        {
            List<string> elist = new List<string>();
            List<AoEquipment> eqpList = new List<AoEquipment>();

            //var workLots = wstep.Wips;
            //foreach (var wlot in workLots)
            //{
            //    var lot = wlot.Batch.Sample as SEMLot;
            //    elist.AddRange(ArrangeHelper2.GetArrange(lot.PlanWip.PegWipInfo, lot.CurrentStepID, lot.CurrentProductID, "SIM"));
            //}

            //foreach (var eqpID in elist)
            //{
            //    var eqp = AoFactory.Current.GetEquipment(eqpID);
            //    if (eqp != null)
            //        eqpList.Add(eqp);
            //}

            return eqpList;
        }

        public WorkStep ADD_WORK_LOT1(IHandlingBatch hb, ref bool handled, WorkStep prevReturnValue)
        {
            try
            {
                var agentInitControl = ServiceLocator.Resolve<JobChangeInit>();

                SEMLot lot = hb.Sample as SEMLot;

                SEMPlanInfo plan = lot.CurrentPlan as SEMPlanInfo;
                SEMEqp runningEqp = plan.LoadedResource as SEMEqp;

                bool isRunningLot = runningEqp != null;

                var wagentName = agentInitControl.GetWorkAgentName(hb);
                if (string.IsNullOrEmpty(wagentName))
                {
                    WriteLog.WriteWorkLotLog(null, null, lot, DateTime.MaxValue);
                    return null;
                }

                var wmanager = AoFactory.Current.JobChangeManger;

                SEMWorkAgent agent = wmanager.GetAgent(wagentName) as SEMWorkAgent;
                if (agent == null)
                {
                    WriteLog.WriteWorkLotLog(null, null, lot, DateTime.MaxValue);
                    return null;
                }

                // TargetStep                                                   // SEM특징 (group과 step이 1:1), Target Step 먼저 구함
                SEMGeneralStep targetStep = lot.GetAgentTargetStep(isRunningLot);      //[TODO] isRun은 왜 항상 false????
                if (targetStep == null)
                {
                    WriteLog.WriteWorkLotLog(null, null, lot, DateTime.MaxValue);
                    return null;
                }

                // JobConditionGroup                                                // work group 상위 개념, job condition이 같은 group 끼리 묶음
                string jobCondKey = lot.GetJobConditionGroupKey(targetStep);
                JobConditionGroup jgroup = agent.GetJobConditionGroup(jobCondKey);

                // Work Group
                SEMWorkGroup wgroup;
                string wgroupKey;
                if (runningEqp != null &&
                    (runningEqp.IsValid == false || runningEqp.HasSetupCondition == false))
                {
                    wgroupKey = string.Concat(lot.LotID, "@", "Invalid@NoSetupCondition");
                }
                else
                {
                    //var wgroupKey = agentInitControl.GetWorkGroupKey(hb, wagent);
                    wgroupKey = lot.GetWorkGroupKey(targetStep, agent);
                }

                if (wgroupKey == null)
                {
                    WriteLog.WriteWorkLotLog(null, null, lot, DateTime.MaxValue);
                    return null;
                }

                wgroup = agent.GetGroup(wgroupKey) as SEMWorkGroup;
                wgroup.FixedResource = lot.Wip.ResFixLotArrangeEqp;
                wgroup.GroupKey = wgroupKey.ToString();
                wgroup.JobConditionGroup = jgroup;
                wgroup.JobConditionKey = jobCondKey;

                jgroup.Groups.Add(wgroup);

                // Work Step
                string wstepKey = targetStep.StepID;
                SEMWorkStep wstep = wgroup.GetStep(wstepKey, targetStep) as SEMWorkStep;
                wstep.JobConditionGroup = jgroup;
                jgroup.Steps.Add(wstep);

                var availablaTime = agentInitControl.GetAvailableTime(lot, wstep, targetStep);

                // loadble eqp 
                if (wgroup.IsResFix)
                {
                    var fixedEqp = wgroup.FixedResource;
                    wstep.AddLoadableEqp(fixedEqp.AoEqp);

                    fixedEqp.AoEqp.WorkStepList.Add(wstep);

                    jgroup.LoadableEqps.Add(fixedEqp.AoEqp);
                }
                else
                {
                    var loadableEqps = lot.GetArrange(targetStep.StepID);
                    bool isRunWip = wstep.Key.ToString() == lot.Wip.InitialStep.StepID && lot.Wip.WipState.ToUpper() == "RUN";
                    if (isRunWip == false && loadableEqps != null)
                    {
                        foreach (var eqpID in loadableEqps)
                        {
                            var aeqp = AoFactory.Current.GetEquipment(eqpID) as SEMAoEquipment;

                            wstep.AddLoadableEqp(aeqp);
                            aeqp.WorkStepList.Add(wstep);

                            jgroup.LoadableEqps.Add(aeqp);

                            if (availablaTime < ModelContext.Current.EndTime)
                            {
                                aeqp.ArrangedLots.Add(lot);
                                aeqp.WorkStepList.Add(wstep);
                            }
                        }
                    }
                }

                // Work Lot
                SEMWorkLot wlot = AgentHelper.CreateSEMWorkLot(lot, availablaTime, wstepKey, targetStep, wstep);

                // hold lot이 아니거나, 곧 hold가 풀릴 lot은 workstep에 add
                if (lot.CurrentState != EntityState.HOLD
                    || (lot.CurrentState == EntityState.HOLD && lot.Wip.AvailableTime < ModelContext.Current.EndTime))
                    wstep.AddWip(wlot);
                else
                    InputMart.Instance.HoldLotDic.Add(wlot, wstep);

                lot.CurrentWorkLot = wlot;

                wstep.TotalWips.Add(lot);

                lot.CurrentWorkGroup = wgroup;
                lot.CurrentWorkStep = wstep;
                lot.WorkStepDic.Add(lot.WorkStepDic.Count + 1, wstep);


                // loaded eqp
                //[주석] run재공은 arrange가 달라 다른 work group으로 되어 loaded 하지 않음 
                //if (runningEqp != null && runningEqp.ResFixedWips.Count == 0)
                //    wstep.AddLoadedEqp(runningEqp.AoEqp);

                // Log
                WriteLog.WriteWorkLotLog(wgroup, wstep, lot, (DateTime)availablaTime);


                //
                // Next Target Step 관련 정보 셋팅     
                // 미래에 lot이 진행할 work group 및 work step을 미리 생성함
                //

                SEMGeneralStep targetStepNextStep = lot.GetNextOperForJC(targetStep.StepID);

                while (targetStepNextStep != null)
                {
                    SEMGeneralStep nextTargetStep = lot.GetAgentTargetStep(targetStepNextStep);
                    if (nextTargetStep == null)
                        break;

                    // JobConditionGroup                                               
                    string nextJobCondKey = lot.GetJobConditionGroupKey(nextTargetStep);
                    JobConditionGroup nextJGroup = agent.GetJobConditionGroup(nextJobCondKey);

                    // Work Group Key
                    string nextWgKey = lot.GetWorkGroupKey(nextTargetStep, agent);
                    if (nextWgKey == null)
                        return null;

                    // Work Group
                    SEMWorkGroup nextWgroup = agent.GetGroup(nextWgKey) as SEMWorkGroup;

                    nextWgroup.GroupKey = nextWgKey.ToString();
                    nextWgroup.JobConditionGroup = nextJGroup;
                    nextWgroup.JobConditionKey = nextJobCondKey;

                    nextJGroup.Groups.Add(nextWgroup);

                    // Work Step
                    string nextWorkStepKey = nextTargetStep.StepID;
                    SEMWorkStep nextWstep = nextWgroup.GetStep(nextWorkStepKey, nextTargetStep) as SEMWorkStep;
                    nextWstep.JobConditionGroup = nextJGroup;
                    nextJGroup.Steps.Add(nextWstep);

                    nextWgroup.JobConditionKey = lot.GetJobConditionGroupKey(nextTargetStep as SEMGeneralStep);

                    // loadble eqp 
                    var arrList = lot.GetArrange(nextWorkStepKey);
                    foreach (var eqpID in arrList)
                    {
                        var aeqp = AoFactory.Current.GetEquipment(eqpID) as SEMAoEquipment;

                        nextWstep.AddLoadableEqp(aeqp);
                        jgroup.LoadableEqps.Add(aeqp);

                        if (availablaTime < ModelContext.Current.EndTime)
                        {
                            aeqp.ArrangedLots.Add(lot);
                            aeqp.WorkStepList.Add(nextWstep);
                        }
                    }
                    nextWstep.TotalWips.Add(lot);

                    // Get Available Time
                    DateTime availTime = DateTime.MaxValue;
                    lot.PlanWip.PegWipInfo.AvailableTimeDic.TryGetValue(nextTargetStep.StepID, out availTime);

                    // 
                    lot.WorkStepDic.Add(lot.WorkStepDic.Count + 1, nextWstep);

                    // Write Log
                    WriteLog.WriteWorkLotLog(nextWgroup, nextWstep, lot, availTime);

                    targetStepNextStep = lot.GetNextOperForJC(nextTargetStep.StepID);
                }

                return wstep;
            }
            catch (Exception e)
            {
                WriteLog.WriteErrorLog($"{e.Message}");
                return null;
            }
        }

        public void INITIALIZE_WORK_MANAGER1(WorkManager wmanager, ref bool handled)
        {
            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return;

            var agentInitControl = ServiceLocator.Resolve<JobChangeInit>();
            var agents = agentInitControl.GetWorkAgentNames(wmanager);
            if (agents != null)
            {
                foreach (var agent in agents)
                    wmanager.Add(agent);
            }

        }

        public void INITIALIZE_WORK_STEP1(WorkStep wstep, ref bool handled)
        {
            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return;

            SEMWorkStep ws = wstep as SEMWorkStep;

            wstep.AllowedArrivalGap = Time.FromHours(InputMart.Instance.GlobalParameters.AllowedArrivalGapHr);
            wstep.DownInterval = Time.FromHours(InputMart.Instance.GlobalParameters.DownIntervalHr);
            wstep.UpInterval = Time.FromHours(InputMart.Instance.GlobalParameters.UpIntervalHr);
            wstep.NewUpInterval = Time.FromHours(InputMart.Instance.GlobalParameters.NewUpIntervalHr);

            //[확인보완]
            //이코드는 설명이 필요함(WorkStep의 Steps 의 의미와 없는 경우 이슈는?)
            //var step = wstep.Key as SEMGeneralStep;
            //if (wstep.Steps.Contains(step) == false)
            //    wstep.Steps.Add(step);
        }
    }
}