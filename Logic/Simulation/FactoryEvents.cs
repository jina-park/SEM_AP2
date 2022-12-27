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

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class FactoryEvents
    {
        public void ON_SHIFT_CHANGE0(Mozart.SeePlan.Simulation.AoFactory aoFactory, ref bool handled)
        {
            Logger.MonitorInfo(string.Format("{0}..... {1}", aoFactory.NowDT.ToString("yyyy-MM-dd HH:mm:ss"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        public void ON_DONE0(AoFactory aoFactory, ref bool handled)
        {
            //OutCollector.OnDone(aoFactory);

            //InFlowMaster.Reset();

            //WeightHelper.WriteWeightPresetLog();
        }

        public void ON_BEGIN_INITIALIZE0(AoFactory aoFactory, ref bool handled)
        {

        }

        public void WRITE_DELAY_SHORT_REPORT(AoFactory aoFactory, ref bool handled)
        {
            // 전체 Demand
            var demands = InputMart.Instance.SEMGeneralPegPart.Rows;

            foreach(var pp in demands)
            {
                // Demand에 Pegging된 Lot들에 대한 Short, Delay Report 작성              
                pp.WritePeggedLotReport();
                

                // Demand 잔여 수량에 대한 short Report 작성
                pp.WriteRemainDemandReport();                
            }
        }

    }
}