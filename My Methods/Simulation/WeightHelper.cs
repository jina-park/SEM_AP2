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
using Mozart.SeePlan.DataModel;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class WeightHelper
    {
        public static double MaxLotQty = 0;

        public static SEMWeightPreset GetSafeWeightPreset(string presetID)
        {
            if (MyHelper.IsEmptyID(presetID))
                return null;

            SEMWeightPreset preset;
            if (InputMart.Instance.SEMWeightPreset.TryGetValue(presetID, out preset) == false)
            {
                preset = new SEMWeightPreset(presetID);
                InputMart.Instance.SEMWeightPreset.Add(presetID, preset);
            }

            return preset;
        }

        public static void WriteWeightPresetLog()
        {
            HashSet<SEMWeightPreset> list = new HashSet<SEMWeightPreset>();

            var eqps = AoFactory.Current.Equipments.Values;
            foreach (var eqp in eqps)
            {
                var preset = eqp.Preset as SEMWeightPreset;
                if (preset == null)
                    continue;

                list.Add(preset);
            }

            foreach (var preset in list)
            {
                WriteWeightPresetLog(preset);
            }
        }

        private static void WriteWeightPresetLog(SEMWeightPreset preset)
        {
            if (preset == null)
                return;

            string presetID = preset.Name;
            string mapPresetID = preset.MapPresetID;

            foreach (WeightFactor factor in preset.FactorList)
            {
                //Outputs.WEIGHT_PRESET_LOG row = new WEIGHT_PRESET_LOG();

                ////row.VERSION_NO = ModelContext.Current.VersionNo;
                //row.PRESET_ID = presetID;
                //row.MAP_PRESET_ID = mapPresetID;
                //row.FACTOR_ID = factor.Name;
                //row.FACTOR_TYPE = factor.Type.ToString();
                //row.FACTOR_WEIGHT = factor.Factor;
                //row.FACTOR_NAME = Constants.NULL_ID;
                //row.SEQUENCE = (int)factor.Sequence;
                //row.ORDER_TYPE = factor.OrderType.ToString();
                //string criteria = string.Empty;
                ////foreach (var a in factor.Criteria)
                ////    criteria = criteria + ";" + a;
                //row.CRITERIA = criteria;
                //row.ALLOW_FILTER = "";//주석 박진아 : 추후 개발 MyHelper.ToStringYN(factor.IsAllowFilter);

                //OutputMart.Instance.WEIGHT_PRESET_LOG.Add(row);
            }
        }





    }
}