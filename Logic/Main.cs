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
using Mozart.DataActions;

namespace SEM_AREA.Logic
{
    [FeatureBind()]
    public partial class Main
    {
        public bool IS_CONTINUE_EXECUTION0(ExecutionModule next, ModelContext context, ref bool handled, bool prevReturnValue)
        {
            GlobalParameters.Instance.RunModules.ForEach(x => x = x.Trim());
            if (GlobalParameters.Instance.RunModules.Contains(next.Name))
                return true;
            return false;
        }

        public void SETUP_QUERY_ARGS1(ModelTask task, ModelContext context, ref bool handled)
        {
            context.QueryArgs["#commandTimeout"] = 300;
        }

        public void SHOW_BUILD_INFO(ModelContext context, ref bool handled)
        {
            System.Reflection.Assembly seeplan = System.Reflection.Assembly.GetAssembly(typeof(Mozart.SeePlan.ShopCalendar));

            System.Reflection.Assembly general = System.Reflection.Assembly.GetAssembly(typeof(Mozart.SeePlan.General.GeneralLibrary));

            System.Reflection.Assembly semPlanning = System.Reflection.Assembly.GetExecutingAssembly();

            Logger.MonitorInfo("SeePlan Lib : {0} / {1}", seeplan.GetName().Version.ToString(), seeplan.Location);
            Logger.MonitorInfo("General Lib : {0} / {1}", general.GetName().Version.ToString(), general.Location);
            Logger.MonitorInfo("SEMPlanning Lib : {0} / {1} / {2}", semPlanning.GetName().Version.ToString(), semPlanning.Location, semPlanning.GetLinkerTime());


#if DEBUG
            Logger.MonitorInfo(string.Format("{0} -> {1}", semPlanning.GetName().Name, "DEBUG"));
#else
            Logger.MonitorInfo(string.Format("{0} -> {1}", semPlanning.GetName().Name, "RELEASE"));
#endif
        }

        public void SHUTDOWN0(ModelTask task, ref bool handled)
        {
            Outputs.MONITORING row = new MONITORING();
            row.PLAN_ID = InputMart.Instance.PlanID;

            if (task.Context.HasErrors)
            {
                DataItem dt = task.Context.Outputs.GetItem("MONITORING");
                if (dt != null)
                    dt.SetActiveAction("EngineFail");

                row.RESULT = "FAIL";
            }
            else 
            {
                DataItem dt = task.Context.Outputs.GetItem("MONITORING");
                if (dt != null)
                    dt.SetActiveAction("EngineSuccess");

                row.RESULT = "SUCCESS";
            }

            OutputMart.Instance.MONITORING.Add(row);

            ModelContext.Current.Outputs.Save<SEM_AREA.Outputs.MONITORING>("MONITORING", OutputMart.Instance.MONITORING.Table.Rows, ModelContext.Current.QueryArgs);
        }
    }
}