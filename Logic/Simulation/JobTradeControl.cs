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
using Mozart.SeePlan.DataModel;
using Mozart.SeePlan;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class JobTradeControl
    {
        public OperationType CLASSIFY_OPERATION_TYPE0(WorkStep step, JobChangeContext context, out object reason, ref bool handled, OperationType prevReturnValue)
        {
            SEMWorkStep ws = step as SEMWorkStep;

            reason = ws.WsData.DecisionReason;

            return ws.WsData.DecidedOperationType;
        }


        public float CALCULATE_PRIORITY0(WorkStep step, object reason, JobChangeContext context, ref bool handled, float prevReturnValue)
        {
            var wsdata = step.Data as WorkStepData;

            float result = (float)wsdata.Priority;

            return result;
        }


        public List<AssignEqp> DO_FILTER_ASSIGN_EQP0(WorkStep ws, List<AssignEqp> assignEqps, JobChangeContext context, ref bool handled, List<AssignEqp> prevReturnValue)
        {
            List<AssignEqp> result = AgentAssignEqpHelper.DoFilterAssignEqp(ws as SEMWorkStep, assignEqps, context);

            return result;
        }        


        public int COMPARE_ASSIGN_EQP0(AssignEqp x, AssignEqp y, ref bool handled, int prevReturnValue)
        {
            //대상 장비에서 우선순위는 아래를 따름
            //1순위 : 현재 Idle 인 장비
            //2순위 : WorkStep 이 Down > KEEP > UP 인 장비
            //3순위 : Priority 가 낮은 WorkStep 의 장비 (장비에 할당중인 WorkStep 의 총 load 가 적은 장비)
            int xidle = x.WorkStep == null ? 1 : 0;
            int yidle = y.WorkStep == null ? 1 : 0;

            int cmp = yidle.CompareTo(xidle);
            if (cmp != 0)
                return cmp;

            if (x.WorkStep == null || y.WorkStep == null)
                return cmp;

            cmp = AgentHelper.GetTypePriority(x.WorkStep).CompareTo(AgentHelper.GetTypePriority(y.WorkStep));
            if (cmp != 0)
                return cmp;
            var xdata = x.WorkStep.Data as WorkStepData;
            var ydata = y.WorkStep.Data as WorkStepData;

            double xGap = (double)(xdata.WorkLoadHour);
            double yGap = (double)(ydata.WorkLoadHour);

            cmp = xGap.CompareTo(yGap);

            return cmp;
        }

        public IEnumerable<AoEquipment> SELECT_DOWN_EQP1(WorkStep wstep, JobChangeContext context, ref bool handled, IEnumerable<AoEquipment> prevReturnValue)
        {
            List<AoEquipment> list = new List<AoEquipment>();
            foreach (var wEqp in wstep.LoadedEqps)
            {
                if (list.Contains(wEqp.Target) == false)
                {
                    list.Add(wEqp.Target);
                }
            }
            return list.Count > 0 ? list : null;
        }


        public bool CAN_ASSIGN_MORE0(WorkStep upWorkStep, JobChangeContext context, ref bool handled, bool prevReturnValue)
        {
            //var step = upWorkStep;

            //foreach (var wLot in step.Wips)
            //{
            //    var semLot = wLot.Lot as SEMLot;
            //    SEMWipInfo wipInfo = semLot.WipInfo as SEMWipInfo;

            //    if (wipInfo.IsResFixLotArrange)
            //    {
            //        if (wipInfo.LotArrangedEqpDic.Count > 0)
            //        {
            //            ICollection<SEMEqp> semEqpList;

            //            wipInfo.LotArrangedEqpDic.TryGetValue(semLot.CurrentStepID, out semEqpList);

            //            if (semEqpList != null)
            //            {
            //                foreach (var eqp in semEqpList)
            //                {
            //                    if (step.LoadableEqps.Contains(eqp.AoEqp) == false)
            //                    {
            //                        step.AddLoadableEqp(eqp.AoEqp);

            //                    }
            //                }
            //                return true; 
            //            }
            //        }
            //    }
            //}

            //그냥 loaded를 하면 장비만 할당만 받고 끝이다. 
            //do filter등을 거쳐서, 장비 queue를 지우고 뺏어오고 이런과정을 거쳐야 한다. 
            //up이라고 판단하더라도, 장비를 못가져올 수도 있기 때문에 우선순위를 올린다. 
            //loadable에 해당장비는 당연히 있어야 한다. 

            return false;
        }

        public List<AssignEqp> SELECT_ASSIGN_EQP1(WorkStep upWorkStep, List<AssignEqp> assignEqps, JobChangeContext context, ref bool handled, List<AssignEqp> prevReturnValue)
        {
            List<AssignEqp> result = null; 
            if (assignEqps.IsNullOrEmpty())
                return null;

            var upWs = upWorkStep as SEMWorkStep;

            var selectEqp = assignEqps.SelectAssignEqp(upWs);
            if (selectEqp != null)
            {
                result = new List<AssignEqp>() { assignEqps[0] };
                selectEqp.Result = "New Assign";
            }

            // write log 
            foreach (var info in AgentAssignEqpHelper.Infos)
                info.WriteJobChangeAssignEqpFilterLog(upWorkStep as SEMWorkStep);

            return result;
        }

        public WorkStep SELECT_UP_STEP1(List<WorkStep> upWorkSteps, JobChangeContext context, ref bool handled, WorkStep prevReturnValue)
        {

            return prevReturnValue;
        }

        public int COMPARE_UP_STEP1(WorkStep x, WorkStep y, ref bool handled, int prevReturnValue)
        {
            return y.Priority.CompareTo(x.Priority);
        }

        public OperationType SET_DOWN_EQP(WorkStep step, JobChangeContext context, out object reason, ref bool handled, OperationType prevReturnValue)
        {
            var ws = step as SEMWorkStep;

            reason = ws.WsData.DecisionReason;

            if(ws.IsDummy)
                return prevReturnValue;

            if (prevReturnValue == OperationType.Down)
            {
                foreach (var eqp in step.LoadedEqps)
                {
                    var aeqp = eqp.Target as SEMAoEquipment;
                    
                    InputMart.Instance.DownEqps.Add(aeqp);

                }
            }            

            return prevReturnValue;
        }
    }
}