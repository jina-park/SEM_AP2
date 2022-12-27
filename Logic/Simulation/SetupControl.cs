using Mozart.SeePlan.DataModel;
using Mozart.SeePlan.Simulation;
using Mozart.Simulation.Engine;
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
    public partial class SetupControl
    {
        public Time GET_SETUP_TIME0(Mozart.SeePlan.Simulation.AoEquipment e, IHandlingBatch hb, ref bool handled, Time prevReturnValue)
        {
            var lot = hb.Sample as SEMLot;
            var eqp = e.Target as SEMEqp;
            var aeqp = e as SEMAoEquipment;

            double setupTime = eqp.GetSetupTime(lot);
            eqp.SetupTime = TimeSpan.FromMinutes(setupTime);

            eqp.UpdateJobCondition(lot);

            SetupMaster.WriteSetupLog(eqp, lot);

            aeqp.LotEndTime = aeqp.LotEndTime.AddMinutes(setupTime);

            return Time.FromMinutes(setupTime);
        }

        public void ON_BEGIN_SETUP0(AoEquipment aeqp, AoProcess proc, ref bool handled)
        {
            (aeqp.Target as SEMEqp).LastSetupTime = aeqp.NowDT;
        }
    }
}