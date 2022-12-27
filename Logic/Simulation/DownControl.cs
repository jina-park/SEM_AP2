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
using System.Globalization;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class DownControl
    {
        public IEnumerable<Mozart.SeePlan.DataModel.PMSchedule> GET_PM_LIST(Mozart.SeePlan.Simulation.PMEvents fe, AoEquipment aeqp, ref bool handled, IEnumerable<Mozart.SeePlan.DataModel.PMSchedule> prevReturnValue)
        {
            if (GlobalParameters.Instance.ApplyPM == false)
                return null;

            List<EqpDownSchedule> pmList = new List<EqpDownSchedule>();

            var list = InputMart.Instance.RESOURCE_CALENDARViewByResID.FindRows(aeqp.Target.ResID);
            if (list == null || list.Count() == 0)
                return null;

            foreach (var pmEvt in list)
            {
                double duration = TimeHelper.GetSecondsByUom(pmEvt.DURATION, pmEvt.DURATION_UOM);

                DateTime startTime = DateTime.ParseExact(pmEvt.START_TIME.Trim(), "yyyyMMddHHmmss", CultureInfo.CurrentCulture);
                EqpDownSchedule pm = new EqpDownSchedule(startTime, (int)duration, EqpDownType.PM);
                pm.ScheduleType = DownScheduleType.ShiftBackwardStartTimeOnly;

                pmList.Add(pm);
            }

            return pmList;
        }

        public IEnumerable<PMSchedule> GET_CAPA_CAL_DOWN_LIST(PMEvents fe, AoEquipment aeqp, ref bool handled, IEnumerable<PMSchedule> prevReturnValue)
        {
            if (GlobalParameters.Instance.ApplyCapaCalendar == false)
                return prevReturnValue;

            List<EqpDownSchedule> calendarDowns = new List<EqpDownSchedule>();

            DateTime simStartDate = ModelContext.Current.StartTime;
            DateTime simEndDate = ModelContext.Current.EndTime;
            var simDates = InputMart.Instance.SimDurationDates;
            if (simDates.Count == 0)
            {
                for (DateTime cudDate = simStartDate; cudDate < simEndDate; cudDate = cudDate.AddDays(1))
                    simDates.Add(cudDate.Date.ToString("yyyyMMdd"));
            }

            // Capacity calendar
            var capCals = InputMart.Instance.CAPACITY_CALENDARByResID.FindRows(aeqp.Target.ResID);
            if (capCals == null || capCals.Count() == 0)
                return null;
            else
                capCals = capCals.OrderBy(x => x.EFF_START_DATE);

            var ccDates = capCals.Select(x => x.EFF_START_DATE).Distinct();

            List<DateTimeRange> dtrs = new List<DateTimeRange>();

            foreach (var simDate in simDates)   // to see if any day of capay is missing during the simulation dates
            {
                DateTime start = DateTime.MinValue;
                DateTime end = DateTime.MinValue;
                DateTimeRange ccRange = null;
                if (ccDates.Contains(simDate) == false) // if a whole day's capa is missing, the eqp cannot do anything on this day.
                {
                    start = DateTime.ParseExact(simDate, "yyyyMMdd", CultureInfo.CurrentCulture);
                    end = start.AddDays(1);
                    ccRange = new DateTimeRange(start, end);

                    dtrs.Add(ccRange);
                }
                else        // if a day capa exists, then have to see if it has any complementing time range
                {
                    var cc = capCals.Where(x => x.EFF_START_DATE == simDate).FirstOrDefault();
                    if (cc == null)
                        continue;

                    if (simDate == simDates[0])     // for the first date in simulation period
                        start = InputMart.Instance.CutOffDateTime.GetDateTimeWithoutSecond();
                    else
                        start = DateTime.ParseExact(cc.EFF_START_DATE, "yyyyMMdd", CultureInfo.CurrentCulture);

                    end = start.AddSeconds(TimeHelper.GetSecondsByUom((double)cc.VALUE, cc.VALUE_UOM));
                    ccRange = new DateTimeRange(start, end);

                    start = start.Date;                                         
                    end = start.AddDays(1);                                     
                    DateTimeRange simRange = new DateTimeRange(start, end);     // simRange      |===========================| (equivalent to 1 day)
                                                                                // ccRange           |================|
                    dtrs.AddRange(simRange.GetCompelementsOf(ccRange));         // compelemtns   |***|      and       |******| (returns list of DateTimeRange)
                }
            }

            foreach (var dtr in dtrs)
            {
                EqpDownSchedule eds = new EqpDownSchedule(dtr.Start, (int)(dtr.End - dtr.Start).TotalSeconds, EqpDownType.CAPADOWN);
                eds.ScheduleType = DownScheduleType.ShiftBackwardStartTimeOnly;
                calendarDowns.Add(eds);
            }

            if (prevReturnValue == null)
                prevReturnValue = new List<PMSchedule>();

            var prev = prevReturnValue.ToList();
            prev.AddRange(calendarDowns);            

            return prev;
        }
        public IEnumerable<PMSchedule> GET_HOLIDAY_DOWN_LIST(PMEvents fe, AoEquipment aeqp, ref bool handled, IEnumerable<PMSchedule> prevReturnValue)
        {
            if (GlobalParameters.Instance.ApplyWorkDays == false)
                return prevReturnValue;

            List<EqpDownSchedule> holidyDowns = new List<EqpDownSchedule>();

            string factoryID = (aeqp.Target as SEMEqp).FactoryID;

            DateTime simStartDate = ModelContext.Current.StartTime;
            DateTime simEndDate = ModelContext.Current.EndTime;
            var simDates = InputMart.Instance.SimDurationDates;
            if (simDates.Count == 0)
            {
                for (DateTime curDate = simStartDate; curDate < simEndDate; curDate = curDate.AddDays(1))
                    simDates.Add(curDate.Date.ToString("yyyyMMdd"));
            }

            // Work day info
            var workDays = InputMart.Instance.WORK_DAY_INFOByFactoryID.FindRows(factoryID);
            if (workDays == null || workDays.Count() == 0)
                return null;
            else
                workDays = workDays.OrderBy(x => x.WORK_DATE);

            var ccDates = workDays.Select(x => x.WORK_DATE).Distinct();

            foreach (var simDate in simDates)
            {
                if (ccDates.Contains(simDate))
                    continue;

                DateTime ccStart = DateTime.ParseExact(simDate, "yyyyMMdd", CultureInfo.CurrentCulture);
                double duration = TimeHelper.GetSecondsByUom(1, "DAY");

                EqpDownSchedule downSched = new EqpDownSchedule(ccStart, (int)duration, EqpDownType.HOLIDAY);
                downSched.ScheduleType = DownScheduleType.ShiftBackwardStartTimeOnly;

                holidyDowns.Add(downSched);
            }

            if (prevReturnValue == null)
                prevReturnValue = new List<PMSchedule>();

            var prev = prevReturnValue.ToList();
            prev.AddRange(holidyDowns);

            return prev.OrderBy(x => x.StartTime);
        }
        public void WRITE_PM_OPER_PLAN(AoEquipment aeqp, PMSchedule fs, DownEventType det, ref bool handled)
        {
            var eqp = aeqp.Target as SEMEqp;
            var eqpDown = fs as EqpDownSchedule;

            string key = CommonHelper.CreateKey(eqp.ResID, fs.GetHashCode().ToString(), eqpDown.DownType.ToString());
            OPER_PLAN row;
            if (InputMart.Instance.OperPlanDic.TryGetValue(key, out row) == false)
            {
                row = new OPER_PLAN
                {
                    SUPPLYCHAIN_ID = "MLCC",
                    PLAN_ID = InputMart.Instance.PlanID,
                    SITE_ID = InputMart.Instance.SiteID,
                    TO_SITE_ID = InputMart.Instance.SiteID,
                    EXE_MODULE = "PLAN",
                    STAGE_ID = "PLAN",
                    FACTORY_ID = eqp.FactoryID,
                    EQP_STATUS = "DOWN",
                    PLAN_TYPE = eqpDown.DownType.ToString(),
                    PLAN_SEQ = ++InputMart.Instance.OperPlanSeq,
                    RESOURCE_ID = eqp.ResID,
                    RESOURCE_GROUP = eqp.ResourceGroup == null ? "-" : eqp.ResourceGroup,
                    RESOURCE_MES = eqp.ResourceMES,
                    WORK_AREA = "",
            };
                InputMart.Instance.OperPlanDic.Add(key, row);
                OutputMart.Instance.OPER_PLAN.Add(row);
            }

            switch (det)
            {
                case DownEventType.Start:
                    row.START_TIME_DT = AoFactory.Current.NowDT;
                    row.START_TIME = row.START_TIME_DT.DateTimeToString();
                    break;
                case DownEventType.End:
                    row.END_TIME_DT = AoFactory.Current.NowDT;
                    row.END_TIME = row.END_TIME_DT.DateTimeToString();
                    break;
            }

        }

    }
}