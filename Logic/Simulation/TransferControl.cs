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
    public partial class TransferControl
    {
        public Time GET_TRANSFER_TIME1(Mozart.SeePlan.Simulation.IHandlingBatch hb, ref bool handled, Time prevReturnValue)
        {
            //return Time.Zero;

            return AgentHelper.TransferTime(hb.Sample as SEMLot);
        }

        public void ON_TRANSFERED0(IHandlingBatch hb, ref bool handled)
        {
            var lot = hb.Sample as SEMLot;
            lot.AfterTransferTime = AoFactory.Current.NowDT;
        }
    }
}