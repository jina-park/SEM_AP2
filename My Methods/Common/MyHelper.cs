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
    public static partial class MyHelper
    {
        public static bool IsEmptyID(string text)
        {
            return Mozart.SeePlan.StringUtility.IsEmptyID(text);
        }

        public static DateTime? DbNullDateTime(this DateTime date)
        {
            if (date == DateTime.MinValue || date == DateTime.MaxValue)
                return null;

            return date;
        }

        public static string ToStringYN(this bool yn)
        {
            return yn ? "Y" : "N";
        }
        public static bool ToBoolYN(this string yn)
        {
            if (MyHelper.IsEmptyID(yn))
                return false;  // defalut

            if (string.Equals(MyHelper.Trim(yn), "Y", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else
                return false;
        }

        public static string Trim(string text)
        {
            if (text == null)
                return null;

            return text.Trim();
        }

        public static T ToEnum<T>(string name, T defaultValue) where T : struct, IConvertible
        {
            try
            {
                T result;

                if (Enum.TryParse<T>(name, true, out result) == false)
                    return defaultValue;

                return result;
            }
            catch
            {
                return defaultValue;
            }
        }
        public static string CreateKey(params string[] strArr)
        {
            string sValue = null;
            foreach (string str in strArr)
            {
                if (sValue == null)
                    sValue = str;
                else
                    sValue += '@' + str;
            }

            return sValue ?? string.Empty;
        }
    }
}