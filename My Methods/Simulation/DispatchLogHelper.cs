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
using System.Text;
using Mozart.SeePlan;

namespace SEM_AREA
{
	[FeatureBind()]
	public static partial class DispatchLogHelper
	{
		public static string GetDispatchWipLog(SEMEqp targeEqp, EntityDispatchInfo info, SEMLot lot, Mozart.SeePlan.DataModel.WeightPreset wp)
		{
			var slot = lot as SEMLot;

			StringBuilder sb = new StringBuilder();

			SetDefaultLotInfo(sb, slot);

			if (wp != null)
			{
				foreach (var factor in wp.FactorList)
				{
					var vdata = slot.WeightInfo.GetValueData(factor);

					sb.Append("/");

					if (string.IsNullOrEmpty(vdata.Description))
						sb.Append(vdata.Value);
					else
						sb.Append(vdata.Value + "@" + vdata.Description);
				}
			}

			return sb.ToString();
		}

		private static void SetDefaultLotInfo(StringBuilder sb, SEMLot lot)
		{
			var sb2 = GetDefaultLotInfo(lot.LotID,
										lot.CurrentProductID,
										"",//lot.CurrentProductVersion ?? lot.Wip.ProductVersion,
										lot.CurrentStepID,
										lot.UnitQtyDouble.ToString(),
										"",//lot.ProductionType,
										lot.LogDetail
										);

			sb.Append(sb2);
		}

		private static StringBuilder GetDefaultLotInfo(string lotID, string productID, string productVersion,
	string stepID, string unitQty, string productionType, string logDetail)
		{
			StringBuilder sb = new StringBuilder();

			sb.Append(lotID);
			sb.AppendFormat("/{0}", productID);
			//sb.AppendFormat("/{0}", productVersion);
			sb.AppendFormat("/{0}", stepID);
			sb.AppendFormat("/{0}", unitQty);
			//sb.AppendFormat("/{0}", productionType);
			sb.AppendFormat("/{0}", logDetail);

			return sb;
		}

		internal static void AddDispatchInfo(SEMAoEquipment eqp, IList<IHandlingBatch> lotList, IHandlingBatch[] selected, Mozart.SeePlan.DataModel.WeightPreset preset)
		{
			// 주석 박진아 :ApplyLotGroupDispatching 구현 후 수정
			//if (InputMart.Instance.GlobalParameters.ApplyLotGroupDispatching)
			//{
			//	bool isAllWrite = false;
			//	if (isAllWrite)
			//	{
			//		AddAllDispatchInfo(eqp, lotList, selected, preset);
			//	}
			//	else
			//	{
			//		AddGroupDispatchInfo(eqp, lotList, selected, preset);
			//	}
			//}
			//else
			//{
			eqp.EqpDispatchInfo.AddDispatchInfo(lotList, selected, preset);
			//}
		}

		public static void WriteDispatchLog(SEMAoEquipment eqp, EqpDispatchInfo info)
		{
			if (eqp == null || info == null)
				return;

			DateTime dispatchTime = info.DispatchTime;
			if (dispatchTime == DateTime.MinValue || dispatchTime == DateTime.MaxValue)
				return;

			string dispatchTimeStr = DateUtility.DbToString(dispatchTime);
			var last = eqp.LastPlan as SEMPlanInfo;

			string eqpID = eqp.EqpID;

			var table = OutputMart.Instance.EQP_DISPATCHING_LOG;
			var row = new EQP_DISPATCHING_LOG();
			//parent EqpID 존재시 

			row = new EQP_DISPATCHING_LOG();

			row.EQP_ID = eqpID;
			row.DISPATCHING_TIME = dispatchTimeStr;

			table.Add(row);


			SEMEqp targetEqp = eqp.Target as SEMEqp;

			//row.VERSION_NO = ModelContext.Current.VersionNo;

			//row.FACTORY_ID = targetEqp.FactoryID;
			//row.SHOP_ID = targetEqp.ShopID;
			row.SITE_ID = "E101";
			row.EQP_GROUP = targetEqp.ResGroup;

			//row.SUB_EQP_ID = subEqpID;

			if (last != null)
			{
				StringBuilder sb = GetDefaultLotInfo(last.LotID,
													 last.ProductID,
													 "",//last.ProductVersion,
													 last.StepID,
													 last.UnitQty.ToString(),
													 "",//last.ProductionType,
													 Constants.NULL_ID);
				row.LAST_WIP = sb.ToString();
			}

			row.SELECTED_WIP = info.SelectedWipLog;
			row.DISPATCH_WIP_LOG = ParseDispatchWipLog(info);

			int filteredWipCnt = 0;
			row.FILTERED_WIP_LOG = ParseFilteredInfo(info, ref filteredWipCnt);

			row.FILTERED_WIP_CNT = filteredWipCnt;
			row.SELECTED_WIP_CNT = string.IsNullOrWhiteSpace(info.SelectedWipLog) ? 0 : info.SelectedWipLog.Split(';').Length;
			row.INIT_WIP_CNT = row.FILTERED_WIP_CNT + info.Batches.Count;

			if (targetEqp.Preset != null)
				row.PRESET_ID = targetEqp.Preset.Name;
		}

		private static string ParseDispatchWipLog(EqpDispatchInfo info)
		{
			StringBuilder dsb = new StringBuilder();
			foreach (var di in info.Batches)
			{
				if (dsb.Length > 0)
					dsb.Append(";");

				dsb.Append(di.Log);
			}

			return dsb.ToString();
		}

		private static string ParseFilteredInfo(EqpDispatchInfo info, ref int filteredWipCnt)
		{
			if (info.FilterInfos.Count == 0)
				return string.Empty;

			StringBuilder result = new StringBuilder();

			foreach (KeyValuePair<string, EntityFilterInfo> filtered in info.FilterInfos)
			{
				EntityFilterInfo filterInfo = filtered.Value;

				filteredWipCnt += filterInfo.FilterWips.Count;

				StringBuilder fsb = new StringBuilder();

				fsb.Append(filterInfo.Reason);
				fsb.Append(':');

				bool first = true;

				foreach (SEMLot fw in filterInfo.FilterWips)
				{
					StringBuilder sb = new StringBuilder();

					if (!first)
						sb.Append(";");
					else
						first = false;

					SetDefaultLotInfo(sb, fw);

					fsb.Append(sb);
				}

				fsb.Append("\t");

				result.Append(fsb);
			}

			return result.ToString();
		}

	}
}