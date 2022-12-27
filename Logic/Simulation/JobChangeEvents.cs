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
using Mozart.SeePlan;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class JobChangeEvents
    {

        public void ON_BEFORE_PROFILE(WorkAgent wagent, ref bool handled)
        {
            var agent = wagent as SEMWorkAgent;

            var now = AoFactory.Current.NowDT;

            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return;

            if (!InputMart.Instance.AgentPhaseIndex.ContainsKey(AoFactory.Current.NowDT.DbToString()))
                InputMart.Instance.AgentPhaseIndex.Add(AoFactory.Current.NowDT.DbToString(), 0);

            MultiDictionary<SEMWorkStep, SEMAoEquipment> removeEqps = new MultiDictionary<SEMWorkStep, SEMAoEquipment>();

            foreach (var group in wagent.Groups)
            {
                foreach (var step in group.Steps)
                {
                    var ws = step as SEMWorkStep;
                    // WorkStepData 초기화
                    ws.Data = null;

                    // ResFix 장비 할당 해제
                    if (ws.IsDummy)
                        continue;

                    foreach (var eqp in ws.LoadedEqps)
                    {
                        var semEqp = eqp.Target.Target as SEMEqp;
                        if (semEqp.ResFixedWips.Count() > 0)
                        {
                            removeEqps.Add(ws, eqp.Target as SEMAoEquipment);
                        }
                    }

                }
            }

            foreach (var pair in removeEqps)
            {
                var downWs = pair.Key;

                foreach (var aeqp in pair.Value)
                { 
                    var upWs = agent.GetDummyWorkStep(pair.Key.StepKey);

                    wagent.Down(downWs, aeqp);
                    wagent.Up(upWs, aeqp);

                    aeqp.WorkStep = upWs;

                    AgentHelper.WriteDecisionLog(downWs, "DOWN", "ResFixInit", "ResFixReset");
                    AgentHelper.WriteDecisionLog(upWs, "UP", "ResFixInit", "ResFixReset");

                    AgentHelper.WriteAssignEqpLog(upWs, downWs, aeqp, "ResFixResetTrade");
                }
            }

            // DownEqps 초기화
            InputMart.Instance.DownEqps.Clear();

            agent.TradeInJobGroup();
        }

        public void ON_CALCULATE0(WorkGroup wgroup, ref bool handled)
        {
            //DateTime now = AoFactory.Current.NowDT;

            //foreach (var ws in wgroup.Steps)
            //{
            //    AgentHelper.WriteProfile(ws, null, "ON_CALCULATE0");
            //}
        }

        public void ON_AFTER_UNASSIGN_EQP0(WorkStep step, AssignEqp unassignedEqp, JobChangeContext context, ref bool handled)
        {
            var ws = step as SEMWorkStep;
            var aeqp = unassignedEqp.Target as SEMAoEquipment;

            aeqp.WorkStep = null;

            InputMart.Instance.DownEqps.Remove(unassignedEqp.Target as SEMAoEquipment);
        }

        public void ON_AFTER_ASSIGN_EQP0(WorkStep step, List<AssignEqp> assignedEqps, JobChangeContext context, ref bool handled)
        {
            var ws = step as SEMWorkStep;

            foreach (var e in assignedEqps)
            {
                var aeqp = e.Target as SEMAoEquipment;
                aeqp.WorkStep = ws;
            }

            //if (InputMart.Instance.DecisionDic.ContainsKey(step.Group.Key.ToString()))
            //    InputMart.Instance.DecisionDic[step.Group.Key.ToString()] = AoFactory.Current.NowDT;
            //else
            //    InputMart.Instance.DecisionDic.Add(step.Group.Key.ToString(), AoFactory.Current.NowDT);
        }

        public void ADD_LOADED_EQP(WorkAgent wagent, ref bool handled)
        {
            SEMWorkAgent agent = wagent as SEMWorkAgent;

            foreach (var e in AoFactory.Current.Equipments)
            {
                SEMAoEquipment aeqp = e.Value as SEMAoEquipment;
                SEMEqp eqp = aeqp.Target as SEMEqp;

                if (eqp.ResFixedWips.Count > 0)
                {
                    agent.AddDummyWorkStep(aeqp);
                    continue;
                }

                JobConditionGroup jGroup;
                string jobCondkey = string.Empty;

                bool isRunningEqp = aeqp.ProcessingLot == null ? false : true;
                if (isRunningEqp)
                {
                    SEMWipInfo wip = eqp.RunWip;
                    SEMLot lot = wip.GetLot();

                    // work step에서 select lot을 삭제, 아래 ws를 가져올 때 현재 ws을 제외 하려고
                    if (lot.CurrentWorkStep != null)
                    {
                        WorkLot wlot = lot.CurrentWorkStep.Wips.Where(x => x.Lot.LotID == lot.LotID).FirstOrDefault();
                        if (wlot != null)
                        {
                            lot.CurrentWorkStep.Wips.Remove(wlot);
                            lot.CurrentWorkStep.Inflows.Remove(wlot);
                        }
                    }

                    jobCondkey = lot.GetJobConditionGroupKey(wip.InitialSEMStep);
                }
                else
                {
                    jobCondkey = eqp.GetJobConditionGroupKey();
                }

                jGroup = agent.GetJobConditionGroup(jobCondkey);

                if (jGroup == null)
                {
                    agent.AddDummyWorkStep(aeqp);
                    continue;
                }
                // 동일 작업조건 ws중 우선순위가 가장 높은 ws에 load
                SEMWorkStep upWorkStep = jGroup.GetUpWorkStep(aeqp);

                if (upWorkStep == null)
                {
                    agent.AddDummyWorkStep(aeqp);
                    continue;
                }

                upWorkStep.AddLoadedEqp(aeqp);
                
                aeqp.WorkStep = upWorkStep;
            }
        }

        public void WRITE_INIT_LOG(WorkAgent wagent, ref bool handled)
        {
            // Write Init Log
            AgentHelper.WriteJobChangeInitLog(wagent);
        }

        public void ASSIGN_DOWN_EQP(WorkAgent wagent, ref bool handled)
        {
            // 이전 로직에서 할당되지 않은 장비를 추가 할당

            SEMWorkAgent agent = wagent as SEMWorkAgent;

            foreach (var aeqp in InputMart.Instance.DownEqps)
            {
                var upWs = aeqp.WorkStepList.GetUpWorkStep(aeqp);
                var downWs = aeqp.WorkStep;

                string filterReason = string.Empty;
                bool canTrade = AgentHelper.CanTrade(upWs, downWs, aeqp, ref filterReason);

                if (canTrade == false)
                {
                    AgentHelper.WriteAssignEqpFilterLog(upWs, downWs, aeqp, filterReason);
                    continue;
                }

                agent.Down(aeqp.WorkStep, aeqp);
                agent.Up(upWs, aeqp);

                aeqp.WorkStep = upWs;

                AgentHelper.WriteDecisionLog(downWs, "DOWN", "NoProfile", "IdleEqpDown");
                AgentHelper.WriteDecisionLog(upWs, "UP", "IdleEqpUp", "IdleEqpUp");

                AgentHelper.WriteAssignEqpLog(upWs, downWs, aeqp, "IdleEqpTrade");
            }

        }

        public void SORT_JOB_GROUP(WorkAgent wagent, ref bool handled)
        {
            SEMWorkAgent agent = wagent as SEMWorkAgent;

            agent.JobConditionGroups.Sort(new Comparers.JobGroupCompare());
        }

        public void PROFILE_JOB_GROUP(WorkAgent wagent, ref bool handled)
        {
            SEMWorkAgent agent = wagent as SEMWorkAgent;

            ProfileHelper.Profile(agent);
        }

        public void CLASSIFY_DECISION_TYPE_BY_JOB_GROUP(WorkAgent wagent, ref bool handled)
        {
            SEMWorkAgent agent = wagent as SEMWorkAgent;
            
            DecisionHelper.ClassifyDecisionType(agent);
        }

        public void RE_PROFILE_DOWN_GROUP(WorkStep step, AssignEqp unassignedEqp, JobChangeContext context, ref bool handled)
        {
            SEMWorkStep ws = step as SEMWorkStep;

            ProfileHelper.ReProfile(ws.JobConditionGroup);
        }

        public void RE_PROFILE_UP_GROUP(WorkStep step, List<AssignEqp> assignedEqps, JobChangeContext context, ref bool handled)
        {
            SEMWorkStep ws = step as SEMWorkStep;

            ProfileHelper.ReProfile(ws.JobConditionGroup);
        }


    }
}