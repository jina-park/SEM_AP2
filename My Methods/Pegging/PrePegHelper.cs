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
    public static partial class PrePegHelper
    {
        public static string CreateDgKey(SEMPegWipInfo pwi)
        {
            return CommonHelper.CreateKey(pwi.FlowProductID, pwi.FlowCustomerID, pwi.TargetOperID);
        }

        public static string CreateDgKey(SEMGeneralPegPart semPp)
        {
            return CommonHelper.CreateKey(semPp.Product.ProductID, semPp.CustomerID, semPp.TargetOperID);
        }



    }
}