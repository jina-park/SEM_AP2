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
	public static partial class SEMGeneralStepExt
	{
		public static SEMStepTat GetTat(this SEMGeneralStep step, string productID, bool isMain)
		{
			string key = step.StepID; // 박진아 : key가 의미없지만 임시로 설정

			if (step.StepTats == null || step.StepTats.Count == 0)
				return null;

			SEMStepTat tat;
			step.StepTats.TryGetValue(key, out tat);

			return tat;
		}


		public static string GetTatKey(string productID, bool isMain)
		{
			return MyHelper.CreateKey(productID, isMain.ToString());
		}
	}
}