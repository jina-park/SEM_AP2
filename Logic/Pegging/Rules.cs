using Mozart.SeePlan.Pegging;
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

namespace SEM_AREA.Logic.Pegging
{
    [FeatureBind()]
    public partial class Rules
    {
        public PegPart ON_END_INITIALIZE(PegPart pegPart)
        {
            //prepeg
            PrePegger.InitPrePeg();
            
            PrePegger.DoPrePeg();

            PrePegger.OnAfterPrePeg();
            //WriteLog.WriteSemDemand(pegPart);

            return pegPart;
        }
    }
}