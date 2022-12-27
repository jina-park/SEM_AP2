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
    public static partial class FabEquipmentExt
    {
		public static string GetCurrentRunMode(this SEMAoEquipment eqp)
		{
			if (eqp == null)
				return null;

			//주석 박진아 : parallel chamber 주석 처리
			//if (eqp.IsParallelChamber)
			//{
			//	var subEqp = eqp.TriggerSubEqp;
			//	if (subEqp != null)
			//		return subEqp.SubEqpGroup.CurrentRunMode;
			//}

			return null;
		}
	}
}