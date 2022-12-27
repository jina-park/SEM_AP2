using Mozart.Simulation.Engine;
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
using Mozart.SeePlan.DataModel;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class JobProfileControl
    {
        public IEnumerable<Mozart.SeePlan.Simulation.IHandlingBatch> GET_MAPPING_LOT0(Mozart.SeePlan.Simulation.IHandlingBatch hb, ref bool handled, IEnumerable<IHandlingBatch> prevReturnValue)
        {
            try
            {
                List<IHandlingBatch> list = new List<IHandlingBatch>();
                list.Add(hb);
                SEMLot lot = hb.Sample as SEMLot;

                //if (lot.LotID.Equals("CI16B7R_SG3910_W"))
                //    Console.WriteLine("A");

                return list;
            }
            catch (Exception e)
            {
                WriteLog.WriteErrorLog(e.Message);
                //WriteHelper.WriteErrorHistory(ErrorLevel.FATAL,
                //string.Format("ErrorMessage : {0}   MethodName : {1}",
                //e.Message, System.Reflection.MethodInfo.GetCurrentMethod().Name));

                return default(IEnumerable<IHandlingBatch>);
            }
        }

        public WorkStep UPDATE_FIND_STEP0(IList<WorkStep> list, WorkStep step, WorkLot wlot, ref bool handled, WorkStep prevReturnValue)
        {
            SEMLot lot = wlot.Batch.Sample as SEMLot;
            bool isRun = lot.IsRun();

            SEMGeneralStep ts = lot.GetAgentTargetStep(isRun);
            if (ts == null)
                return null;

            var groupKey = lot.GetWorkGroupKey(ts, step.Agent);
            var wg = step.Agent.GetGroup(groupKey) as SEMWorkGroup;
            var ws = wg.TryGetStep(ts.StepID);

            if (ws == null)
                return null;

            return ws;
        }

        public int COMPARE_PROFILE_STEP0(WorkStep x, WorkStep y, ref bool handled, int prevReturnValue)
        {
            int cmp = x.Order.CompareTo(y.Order);
            return cmp;
        }

        public void ON_BEGIN_PROFILING0(WorkLoader wl, Mozart.Simulation.Engine.Time now, ref bool handled)
        {
            //소팅하도록 처리(하단의 SortProfileLot 함수를 구현하는 경우 매번 소팅을 하게 됨으로 속도이슈 발생 가능)
            //여기서 WorkLoader의 대상 Lot 을 소팅하는 경우 1회 소팅하여 처리하게 됨
            foreach (var ws in wl.Steps)
            {
                ws.Wips.Sort(AgentHelper.ProfileWipCompare);
            }

            InputMart.Instance.GapDayDic.Clear();
            foreach(var ws in wl.Steps)
            {
                if (ws.Data == null)
                    ws.Data = new WorkStepData();

                var data = ws.Data as WorkStepData;

                foreach(var wip in ws.Wips)
                {                    
                    SEMLot lot = wip.Lot as SEMLot;

                    if (lot.CurrentStepID != ws.Key.ToString())
                        continue;
                    
                    var lpst = lot.GetLPST(lot.CurrentStepID);
                    var gap = (AoFactory.Current.Now - lpst).TotalDays;

                    if (data.MinGapDay > gap)
                        data.MinGapDay = gap;

                    if (data.MaxGapDay < gap)
                        data.MaxGapDay = gap;                    

                }

                if(InputMart.Instance.GapDayDic.ContainsKey(ws.Group.Key.ToString()) == false)
                    InputMart.Instance.GapDayDic.Add(ws.Group.Key.ToString(), data.MinGapDay);
            }
        }

        public List<WorkLot> DO_FILTER_LOT0(WorkEqp weqp, WorkStep wstep, List<WorkLot> list, ref bool handled, List<WorkLot> prevReturnValue)
        {
            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return list;

            // filtering 로직 구현  
            SEMWorkStep ws = wstep as SEMWorkStep;
            List<WorkLot> result = new List<WorkLot>();

            //var sameJobConditionWorkLots = AgentHelper.GetSameJobConditionWorkLots(ws, weqp.Target, list);

            foreach (var lot in list)
            {
                if (!AgentHelper.IsFilterLot(lot, weqp, wstep))
                    result.Add(lot);
            }

            if (result.Count == 0)
                weqp.Stop = true;

            return result;
        }

        public TimeSpan GET_TACT_TIME0(WorkEqp weqp, WorkLot wlot, ref bool handled, TimeSpan prevReturnValue)
        {
            var lot = wlot.Batch.Sample as SEMLot;

            ProcTimeInfo time = TimeHelper.GetProcessTime(lot, weqp.Target.Target as SEMEqp);

            if (time.TactTime != null)
                return time.TactTime;

            return TimeSpan.Zero;
        }

        public void GET_PROFILE_TIMES0(WorkStep wstep, WorkEqp weqp, WorkLot wlot, bool isFirstLoading, ref Time setupStartTime, ref Time setupEndTime, ref Time busyStartTime, ref Time busyEndTime, ref Time pmStartTime, ref Time pmEndTime, ref bool addBusyProfile, ref bool handled)
        {
            if (isFirstLoading)
            {
                var aeqp = weqp.Target;
                SEMPlanInfo lplan = aeqp.LastPlan as SEMPlanInfo;
                if (lplan != null)
                {
                    var product = BopHelper.FindProduct(lplan.ProductID);
                    SEMWipInfo winfo = wlot.Lot as SEMWipInfo;
                    SEMLot lot = wlot.Lot as SEMLot;
                    SEMGeneralStep targetStep = lot.GetAgentTargetStep(lot.CurrentSEMStep);

                    //string wgroupKey = lot.GetWorkGroupKey(targetStep); // 호출시점 확인해야함....
                    if (wstep.Group.Key.ToString() != lot.CurrentWorkGroup.GroupKey)
                    {
                        Time setuptime = Time.FromHours(4);

                        setupStartTime = weqp.AvailableTime;
                        setupEndTime = setupStartTime + setuptime;
                        busyStartTime = busyStartTime + setuptime;
                        busyEndTime = busyEndTime + setuptime;
                    }
                }
            }

            // PM Schedule 을 입력하기 위해서는 
            // PM 정보가 argument 의 busystart, busyend 와 겹치는지 확인한 후 겹치는 경우에 
            // busystart/end, pmStart/end 값을 모두 조정합니다.  
        }

        public IEnumerable<WorkLot> ADVANCE0(WorkStep wstep, WorkLot wlot, ref bool handled, IEnumerable<WorkLot> prevReturnValue)
        {
            // next work step으로 넘어가는 것이 다른 Work Group으로 넘어가는것이기 때문에 advance하는 의미가 없음
            // 다음 work group에서는 유입량으로 계산 하지 않음

            return null;
        }

        public WorkLot UPDATE0(WorkStep wstep, WorkLot wlot, ref bool handled, WorkLot prevReturnValue)
        {
            if (InputMart.Instance.GlobalParameters.UseJobChangeAgent == false)
                return wlot;

            //대상 wlot 이 다음 wstep 에 도달하는 시간(availableTime) 계산 
            var lot = wlot.Batch.Sample as SEMLot;
            var currentStep = lot.CurrentStep as SEMGeneralStep;
            var step = lot.IsRun() ? lot.GetNextOperForJC(currentStep) : currentStep;

            var targetStepID = wstep.Key.ToString();
            var productID = lot.CurrentProductID;
            DateTime availableTime = AoFactory.Current.NowDT;

            if (lot.CurrentState == EntityState.MOVE)
                Console.WriteLine();

            //Update 할때 lot 상태에 따라 wlot 의 targetstep 에서의 availabletime 업데이트 
            if (step.StepID != targetStepID)
            {
                if (lot.CurrentState == EntityState.RUN && lot.CurrentSEMStep.StdStep.IsProcessing)
                {
                    //시작시각 부터 남은 시간을계산해서 available time 업데이트 
                    if (lot.CurrentPlan.LoadedResource != null)
                    {
                        var aeqp = AoFactory.Current.GetEquipment(lot.CurrentPlan.LoadedResource.ResID) as SEMAoEquipment;
                        var eqp = aeqp.Target as SEMEqp;

                        double setupTime = eqp.GetSetupTime(lot);
                        availableTime = availableTime.AddMinutes(setupTime);

                        var procTime = TimeHelper.GetProcessTime(lot, eqp);
                        availableTime = availableTime.AddSeconds(procTime.TactTime.TotalSeconds * lot.UnitQty);
                    }
                    else
                    {
                        var tat = step.GetTat(lot.Wip.IsSmallLot);
                        availableTime = availableTime.AddMinutes(tat);
                    }

                    step = lot.GetNextOperForJC(step);

                }

                while (step.StepID != targetStepID)
                {
                    var tat = step.GetTat(lot.Wip.IsSmallLot);
                    availableTime = availableTime.AddMinutes(tat);
                    step = lot.GetNextOperForJC(step);
                }
            }
            else
            {
                if (lot.CurrentState != EntityState.RUN)
                {
                    if (lot.AfterTransferTime != AoFactory.Current.NowDT)
                        wlot.AvailableTime = availableTime.AddMinutes(AgentHelper.TransferTime(lot).TotalMinutes);
                    else
                        wlot.AvailableTime = availableTime;
                }
            }
            wlot.AvailableTime = availableTime;
            return wlot;
        }

        public List<WorkStep> RETURN_NULL(WorkGroup wgroup, List<WorkStep> wsteps, bool reCalc, ref bool handled, List<WorkStep> prevReturnValue)
        {
            return null;
        }


    }
}