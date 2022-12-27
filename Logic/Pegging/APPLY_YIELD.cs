using SEM_AREA.Outputs;
using Mozart.SeePlan.Pegging;
using SEM_AREA.Persists;
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
    public partial class APPLY_YIELD
    {
        public double GET_YIELD0(Mozart.SeePlan.Pegging.PegPart pegPart, ref bool handled, double prevReturnValue)
        {
            return 1;

            var prodID = (pegPart as SEMGeneralPegPart).Product.ProductID;
            var stepID = (pegPart as SEMGeneralPegPart).CurrentStep.StepID; //(pegPart.CurrentStage.Tag as SEMGeneralStep).StepID;
            string key = CommonHelper.CreateKey(prodID, stepID);
                   
            SEMYield sYield;
            InputMart.Instance.SEMYield.TryGetValue(key, out sYield);

            double yield = sYield.Yield;

            if (yield > 1 || yield <= 0)
                return 1;
            else
                return yield;
    }
    }
}