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
using System.Xml.Linq;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class TimeHelper
    {
        public static void SetCycleTimeForProdOper()
        {
            //CycleTime을 OPER_ID와 PRODUCT_ID를 key로 그루핑함(step 탐색시간 절약을 위해)
            var groups = InputMart.Instance.CYCLE_TIME.Rows.GroupBy(x => new { OPER_ID = x.OPER_ID, PROD_ID = x.PRODUCT_ID }, x => x);
            foreach (var group in groups)
            {
                #region SetCycleTime
                SEMGeneralStep step = InputMart.Instance.SEMGeneralStepView.FindRows(group.Key.PROD_ID, group.Key.OPER_ID).FirstOrDefault();

                if (step != null)
                {
                    foreach (var row in group)
                    {
                        SEMEqp eqp;
                        InputMart.Instance.SEMEqp.TryGetValue(row.RESOURCE_ID, out eqp);
                        if (eqp == null)
                        {
                            //WriteLog.WriteErrorLog($"CYCLE_TIME의 RESOURCE를 찾을 수 없습니다. RESOURCE_ID:{row.RESOURCE_ID}"); //로그가 너무많아서 시간이 오래걸려서 패스
                            if (row.RESOURCE_ID == "ALL")
                            {
                                double ct = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);
                                step.CycleTimeDic.Add(row.RESOURCE_ID, ct);
                            }
                            continue;
                        }

                        // Tact
                        double tactTime = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);
                        step.CycleTimeDic.Add(row.RESOURCE_ID, tactTime);
                    }
                }                
                #endregion

                bool isCTOper = InputMart.Instance.CTOperDic.TryGetValue(group.Key.OPER_ID, out var ctOpers);
                if (isCTOper)
                {
                    foreach (var operID in ctOpers)
                    {
                        #region SetCycleTime
                        SEMGeneralStep ctStep = InputMart.Instance.SEMGeneralStepView.FindRows(group.Key.PROD_ID, operID).FirstOrDefault();

                        if (ctStep != null)
                        {
                            foreach (var row in group)
                            {
                                SEMEqp eqp;
                                InputMart.Instance.SEMEqp.TryGetValue(row.RESOURCE_ID, out eqp);
                                if (eqp == null)
                                {
                                    //WriteLog.WriteErrorLog($"CYCLE_TIME의 RESOURCE를 찾을 수 없습니다. RESOURCE_ID:{row.RESOURCE_ID}"); //로그가 너무많아서 시간이 오래걸려서 패스
                                    if (row.RESOURCE_ID == "ALL")
                                    {
                                        double ct = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);
                                                    ctStep.CTOperDic.Add(row.RESOURCE_ID, ct);
                                    }
                                    continue;
                                }
                                // Tact
                                double tactTime = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);
                                ctStep.CTOperDic.Add(row.RESOURCE_ID, tactTime);
                            }
                        }
                                    #endregion
                    }
                }
            }
        }

        public static void SetCycleTimeForLotOper()
        {
            foreach (var lo in InputMart.Instance.SEMLotOperView.Table.Rows)
            {
                var processingOpers = lo.Steps.Where(x => x.StdStep.IsProcessing);

                foreach (var oper in processingOpers)
                {
                    bool isCTOper = oper.StdStep.CTOperID.IsNullOrEmpty() == false;

                    if (isCTOper)
                    {
                        var cycleTimes = InputMart.Instance.CYCLE_TIMEByOperProd.FindRows(oper.StdStep.CTOperID, oper.ProductID);

                        foreach (var row in cycleTimes)
                        {
                            double ct = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);

                            oper.CTOperDic.Add(row.RESOURCE_ID, ct);
                        }
                    }
                    else
                    {
                        var cycleTimes = InputMart.Instance.CYCLE_TIMEByOperProd.FindRows(oper.StepID, oper.ProductID);

                        foreach (var row in cycleTimes)
                        {
                            double ct = GetCycleTime((double)row.TACT_TIME, row.TACT_TIME_UOM);

                            oper.CycleTimeDic.Add(row.RESOURCE_ID, ct);
                        }
                    }

                }
            }
        }

        public static ProcTimeInfo GetTactTime(string eqpID, SEMLot lot, bool applyEqpUtilization = false)
        {
            SEMGeneralStep step = lot.CurrentStep as SEMGeneralStep;

            //StepTime st = step.GetStepTime(eqpID, lot.CurrentProductID);

            double tactTime = TimeHelper.GetCycleTime(step, eqpID);

            float utlization = 1;
            if (applyEqpUtilization)
                utlization = InputMart.Instance.SEMEqp[eqpID].Utilization;

            ProcTimeInfo time = new ProcTimeInfo();

            if (tactTime != 0)
            {
                time.FlowTime = TimeSpan.FromMinutes(tactTime / utlization);
                time.TactTime = TimeSpan.FromMinutes(tactTime / utlization);

                lot.CurrentPlan.TactTime = time.TactTime;
            }
            else
            {
                time.FlowTime = TimeSpan.FromMinutes(GlobalParameters.Instance.DefaultCycleTime);
                time.TactTime = TimeSpan.FromMinutes(GlobalParameters.Instance.DefaultCycleTime);
            }

            return time;
        }

        public static ProcTimeInfo GetProcessTime(SEMLot lot, SEMEqp eqp)
        {
            ProcTimeInfo time = new ProcTimeInfo();

            string key = CommonHelper.CreateKey(eqp.EqpID, lot.CurrentStepID, lot.LotID);
            if (InputMart.Instance.EqpOperLotTactTimeDic.TryGetValue(key, out time) == false)
            {
                time = TimeHelper.GetTactTime(eqp.EqpID, lot, true);
                InputMart.Instance.EqpOperLotTactTimeDic.Add(key, time);
            }
            else
                time = InputMart.Instance.EqpOperLotTactTimeDic[key];

            return time;
        }

        public static ProcTimeInfo GetProcessTime(SEMLot lot, string stepID, SEMEqp eqp)
        {
            ProcTimeInfo time = new ProcTimeInfo();

            string key = CommonHelper.CreateKey(eqp.EqpID, stepID, lot.LotID);
            if (InputMart.Instance.EqpOperLotTactTimeDic.TryGetValue(key, out time) == false)
            {
                time = TimeHelper.GetTactTime(eqp.EqpID, lot, true);
                InputMart.Instance.EqpOperLotTactTimeDic.Add(key, time);
            }
            else
                time = InputMart.Instance.EqpOperLotTactTimeDic[key];

            return time;
        }

        public static StepTime GetStepTime(this SEMGeneralStep step, string eqpID, string productID)
        {
            // key = EQP_ID
            Dictionary<string, StepTime> list;
            if (step.StepTimes.TryGetValue(productID, out list))
            {
                StepTime st = null;
                if (list.TryGetValue(eqpID, out st))
                    return st;
            }

            return null;
        }

        public static double GetTat(this SEMGeneralStep step, bool isSmallLot)
        {
            if (isSmallLot && step.StepID == "SG6094_BT")
                return GlobalParameters.Instance.SmallLotStockLeadTime * 1440;

            return step.TAT;
        }

        public static double GetCycleTime(this SEMGeneralStep step, string eqpID)
        {
            double cycleTime;

            bool isCTOper = InputMart.Instance.CTOperDic.ContainsKey(step.StepID);
            if (isCTOper)
            {
                if (step.CTOperDic.TryGetValue(eqpID, out cycleTime))
                    return cycleTime;

                if (step.CTOperDic.TryGetValue("ALL", out cycleTime))
                    return cycleTime;

                if (step.CycleTimeDic.TryGetValue(eqpID, out cycleTime))
                    return cycleTime;

                if (step.CycleTimeDic.TryGetValue("ALL", out cycleTime))
                    return cycleTime;
            }
            else
                {
                if (step.CycleTimeDic.TryGetValue(eqpID, out cycleTime))
                    return cycleTime;

                if (step.CycleTimeDic.TryGetValue("ALL", out cycleTime))
                    return cycleTime;

                if (step.CTOperDic.TryGetValue(eqpID, out cycleTime))
                    return cycleTime;

                if (step.CTOperDic.TryGetValue("ALL", out cycleTime))
                    return cycleTime;
                }

            return 0;
            }

        public static bool IsCTOper(this SEMGeneralStep step, string eqpID)
        {
            if (step.CTOperDic.ContainsKey(eqpID))
                return true;

            if (step.CTOperDic.ContainsKey("ALL"))
                return true;

            return false;
        }

        public static double GetCycleTime(string productID, string stepID, string eqpID)
        {
            // [TODO] step 가져오는 탐색 속도를 개선해야함
            SEMGeneralStep step = InputMart.Instance.SEMGeneralStepView.FindRows(productID, stepID).FirstOrDefault();
            if (step == null)
                return InputMart.Instance.GlobalParameters.DefaultCycleTime;

            double cycleTime = step.GetCycleTime(eqpID);

            if (cycleTime == 0)
                WriteLog.WriteErrorLog($"CycleTime == 0");

            return cycleTime;
        }

        public static double GetSecondsByUom(double time, string uom)
        {
            switch(uom.ToUpper())
            {
                case "SEC":
                    time *= 1;
                    break;
                case "MIN":
                    time *= 60;
                    break;
                case "HOUR":
                    time *= 60 * 60;
                    break;
                case "DAY":
                    time *= 60 * 60 * 24;
                    break;                
            }

            return time;
        }

        public static double GetMinutesByUom(double tat, string uom)
        {
            double result = tat;

            if (uom == "SEC")
                result = tat / 60;
            else if (uom == "MIN")
                result = tat;
            else if (uom == "HOUR")
                result = tat * 60;
            else if (uom == "DAY")
                result = tat * 60 * 24;
            else 
                WriteLog.WriteErrorLog($"UOM이 올바르지 않습니다.");

            return result;
        }

        public static double GetCycleTime(double value, string uom)
        {
            double result = value;

            if (uom == "SEC")
                result = (double)value / 60;
            else if (uom == "MIN" || uom == null || uom == string.Empty)
                result = (double)value;
            else if (uom == "HOUR")
                result = (double)value * 60;
            else if (uom == "DAY")
                result = (double)value * 60 * 24;
            else
            {
                result = (double)value;
                WriteLog.WriteErrorLog($"TACT_TIME_UOM정보를 알 수 없습니다. TACT_TIME_UOM : {uom} ");
            }

            return result;
        }

        public static DateTime GetDateTimeWithoutSecond(this DateTime dt)
        {
            return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0);
        }

        public static bool HasCycleTime(this SEMGeneralStep step, string eqpID)
        {
            var cycleTime = TimeHelper.GetCycleTime(step, eqpID);

            if (cycleTime == 0)
                    return false;

            return true;
                }

        public static bool HasCycleTime(this SEMGeneralStep step)
        {
            if (step.CycleTimeDic.IsNullOrEmpty() && step.CTOperDic.IsNullOrEmpty())
                return false;

            return true;
        }
    }
}