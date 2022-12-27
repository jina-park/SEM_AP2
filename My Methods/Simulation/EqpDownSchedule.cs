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

namespace SEM_AREA
{
    [FeatureBind()]
    public class EqpDownSchedule : PMSchedule
    {
        #region ctor
        public EqpDownSchedule(DateTime startTime, int duration, EqpDownType downType): base(startTime, duration)
        {
            this.DownType = downType;
        }
        #endregion

        public EqpDownType DownType { get; set; }
    }
    public enum EqpDownType
    {
        PM = 0,
        HOLIDAY = 1,
        CAPADOWN = 2
    }
}