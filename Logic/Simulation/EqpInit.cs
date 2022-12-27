using Mozart.SeePlan.Simulation;
using Mozart.SeePlan.DataModel;
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
    public partial class EqpInit
    {
        public IEnumerable<Mozart.SeePlan.DataModel.Resource> GET_EQP_LIST0(ref bool handled, IEnumerable<Mozart.SeePlan.DataModel.Resource> prevReturnValue)
        {
            List<Mozart.SeePlan.DataModel.Resource> list = new List<Mozart.SeePlan.DataModel.Resource>();

            foreach (SEMEqp eqp in InputMart.Instance.SEMEqp.Values)
            {
                list.Add(eqp);
            }

            return list;
        }

        public void INITIALIZE_EQUIPMENT0(Mozart.SeePlan.Simulation.AoEquipment aeqp, ref bool handled)
        {
            // Last job condition of an equipment is all set at the stage of persist of the table LAST_JOB_CONDITION
            // No need to initialize the last job condition again here

            // SEMEqp와 SEMAoEquipment의 상호 참조
            SEMEqp eqp = aeqp.Target as SEMEqp;
            eqp.AoEqp = aeqp as SEMAoEquipment;
        }
    }
}