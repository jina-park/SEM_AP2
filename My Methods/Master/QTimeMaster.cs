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
    public static partial class QTimeMaster
    {
		internal static void SetWipStayHours(List<ILot> list)
		{
			if (list == null || list.Count == 0)
				return;

			DateTime planStartTime = ModelContext.Current.StartTime;

			foreach (SEMLot lot in list)
			{
				if (lot.QTimeInfo == null)
					continue;

				lot.QTimeInfo.SetWipStayHours(lot.LotID, planStartTime);
			}
		}

		private static void SetWipStayHours(this QTimeInfo info, string lotID, DateTime planStartTime)
		{
			if (info.HasQTime() == false)
				return;

			foreach (var it in info.List)
			{
				string fromStepID = it.FromStep.StepID;

				//주석 박진아 : WipStayHoursDict 테이블이 비어있어 QTime로직은 사용하지 않음
				//var finds = InputMart.Instance.WipStayHoursDict.FindRows(lotID, fromStepID);
				//if (finds == null)
				//	continue;

				//var find = finds.FirstOrDefault();
				//if (find == null)
				//	continue;

				//DateTime fromStepOutTime = MyHelper.Min(find.FROM_STEP_OUT_TIME, planStartTime);
				//if (fromStepOutTime == DateTime.MinValue)
				//	continue;

				//it.FromStepOutTime = fromStepOutTime;
			}
		}

		public static bool HasQTime(this QTimeInfo info)
		{
			if (info.List != null && info.List.Count > 0)
				return true;

			return false;
		}





	}
}