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
    public partial class SHIFT_TAT
    {
        public TimeSpan GET_TAT0(Mozart.SeePlan.Pegging.PegPart pegPart, bool isRun, ref bool handled, TimeSpan prevReturnValue)
        {
            SEMGeneralPegPart pp = pegPart as SEMGeneralPegPart;
            SEMGeneralStep step = pp.CurrentStep as SEMGeneralStep;
            //SEMGeneralStep step = pp.CurrentStage.Tag as SEMGeneralStep;
            //pp.CurrentStage.Tag는 되도록 쓰지 않음

            string key = step.StepID; // 박진아 : key가 의미없지만 임시로 설정

            SEMStepTat tat;
            if (step.StepTats.TryGetValue(key, out tat))
                return tat.Tat;

            return TimeSpan.Zero;
        }

        public bool USE_TARGET_TAT0(PegPart pegPart, PegStage stage, bool isRun, ref bool handled, bool prevReturnValue)
        {
            return false;
        }

        public TimeSpan GET_TARGET_TAT0(PegTarget pegTarget, PegStage stage, bool isRun, ref bool handled, TimeSpan prevReturnValue)
        {
            SEMGeneralPegTarget target = pegTarget as SEMGeneralPegTarget;
            SEMGeneralStep step = pegTarget.PegPart.CurrentStep as SEMGeneralStep;

            // float waitTat = (float)SiteConfigHelper.GetDefaultWaitTAT().TotalMinutes;
            //[todo] 기존에 default tat 가져오는 코드가 있었으나, 0으로 설정했음
            // float runTat = (float)SiteConfigHelper.GetDefaultRunTAT().TotalMinutes;
            double runTat = 0;

            string key = CommonHelper.CreateKey(target.ProductID, target.IsMain.ToString());
            SEMStepTat tat;
            step.StepTats.TryGetValue(key, out tat);

            if (tat != null)
            {
                runTat = tat.TAT;
            }

            double time = runTat;

            return TimeSpan.FromMinutes(time);
        }
    }
}