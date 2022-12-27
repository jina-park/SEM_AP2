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
using Mozart.SeePlan.Simulation;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class ResHelper
    {
        #region Obsolete
        internal static SEMAoEquipment GetFabAoEquipment(string eqpID)
        {
            var aeqp = AoFactory.Current.GetEquipment(eqpID);

            if (aeqp == null)
                return null;

            return aeqp.ToSEMAoEquipment();
        }
        #endregion
    }
}