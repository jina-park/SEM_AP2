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

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class SEMPegWipInfoExt
    {
        private static Dictionary<SEMPegWipInfo, Dictionary<string, SEMOperTarget>> semOperTargetDic = new Dictionary<SEMPegWipInfo, Dictionary<string, SEMOperTarget>>();
        public static SEMOperTarget GetOperTarget(this SEMPegWipInfo pwi, string operID)
        {
            Dictionary<string, SEMOperTarget> operTargetDic = null;
            if (semOperTargetDic.TryGetValue(pwi, out operTargetDic) == false)
            {
                operTargetDic = pwi.SemOperTargets.ToDictionary(x => x.OperId, x => x);
                semOperTargetDic.Add(pwi, operTargetDic);
            }

            SEMOperTarget retVal = null;
            operTargetDic.TryGetValue(operID, out retVal);

            return retVal;
        }
    }
}