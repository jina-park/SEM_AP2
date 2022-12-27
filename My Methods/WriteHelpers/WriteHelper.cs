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
    public static partial class WriteHelper
    {
        static Dictionary<string, string> ErrCheckList = new Dictionary<string, string>();
        public static void WriteIf(string key,
            ErrCategory category,
            ErrLevel level,
            string lotID,
            string productID,
            string processID,
            string eqpID,
            string stepID,
            string reason,
            string detail
            )
        {
            if (ErrCheckList.ContainsKey(key))
                return;

            ErrCheckList.Add(key, key);

            AddRow(category,
                   level,
                   lotID,
                   productID,
                   processID,
                   eqpID,
                   stepID,
                   reason,
                   detail);
        }

        private static void AddRow(ErrCategory category,
            ErrLevel level,
            string lotID,
            string productID,
            string processID,
            string eqpID,
            string stepID,
            string reason,
            string detail
          )
        {
            Outputs.ErrorHistory item = new ErrorHistory();

            item.VERSION_NO = ModelContext.Current.VersionNo;

            item.ERR_CATEGORY = category.ToString();
            item.ERR_LEVEL = level.ToString();
            item.LOT_ID = lotID;
            item.PRODUCT_ID = productID;
            item.PROCESS_ID = processID;
            item.EQP_ID = eqpID;
            item.STEP_ID = stepID;
            item.ERR_REASON = reason;
            item.REASON_DETAIL = detail;

            OutputMart.Instance.ErrorHistory.Add(item);
        }

    }
}