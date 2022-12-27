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
using Mozart.SeePlan;
using Mozart.SeePlan.Pegging;
using Mozart.SeePlan.Simulation;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class ConvertHelper
    {
        public static string GetWeekNoFromDate(DateTime dueDate)
        {
            return dueDate.GetIso8601WeekOfYear().ToString();
        }

        public static DateTime ConvertStringToDateTime(string strDate)
        {
            strDate = RemoveSpace(strDate);  //value.Trim();

            if (strDate == null || strDate == "00000000000000")
                return DateTime.MinValue;
            string formatStr;
            if (strDate.Length == 14)
            {
                formatStr = strDate.Substring(0, 4) + "/" + strDate.Substring(4, 2) + "/" + strDate.Substring(6, 2)
                            + " " + strDate.Substring(8, 2) + ":" + strDate.Substring(10, 2) + ":" + strDate.Substring(12, 2);
            }
            else if (strDate.Length == 8)
            {
                formatStr = strDate.Substring(0, 4) + "/" + strDate.Substring(4, 2) + "/" + strDate.Substring(6, 2) + " 00:00:00";
                
            }
            else
            {
                // err : input data 오류
                WriteLog.WriteErrorLog($"INPUT DATA 오류, String 타입의 날짜는 14자리(년월일시분초) 또는 8자리(년월일)여야 합니다.");

                return DateTime.MinValue;
            }

            return formatStr.ToDateTime();
        }

        static public DateTime StringToDateTime(string value, bool withTime = true)
        {
            if (value == null || value == "00000000000000")
                return DateTime.MinValue;

            value = RemoveSpace(value);  //value.Trim();
            int length = value.Length;

            if (length < 8)
                return DateTime.MinValue;

            int year = 0;
            int month = 0;
            int day = 0;
            int hour = 0;
            int minute = 0;
            int second = 0;

            try
            {
                year = int.Parse(value.Substring(0, 4));
                month = int.Parse(value.Substring(4, 2));
                day = int.Parse(value.Substring(6, 2));

                if (withTime)
                {
                    int t = 8;

                    if (length >= 10)
                    {
                        if (value[8] == ' ')
                            t++;

                        hour = int.Parse(value.Substring(t + 0, 2));
                    }

                    if (length >= 12)
                    {
                        if (value[8] == ' ')
                            t++;

                        minute = int.Parse(value.Substring(t + 2, 2));
                    }

                    if (length >= 14)
                    {
                        second = int.Parse(value.Substring(t + 4, 2));
                    }
                }
            }
            catch
            {

            }

            return new DateTime(year, month, day, hour, minute, second);
        }
        public static string RemoveSpace(string text)
        {
            if (text == null)
                return null;

            return text.Replace(" ", "");
        }

        internal static SEMGeneralPegPart ToSemPegPart(this PegPart pegPart)
        {
            return pegPart as SEMGeneralPegPart;
        }

        internal static SEMGeneralPegTarget ToSemPegTarget(this PegTarget pegTarget)
        {
            return pegTarget as SEMGeneralPegTarget;
        }

        internal static SEMPlanWip ToSemPlanWip(this IMaterial m)
        {
            return m as SEMPlanWip;
        }

        internal static EntityState GetEntityState(string wipState)
        {

            if (wipState.ToUpper() == "WAIT")
                return EntityState.WAIT;

            if (wipState.ToUpper() == "RUN")
                return EntityState.RUN;

            if (string.Compare(wipState.ToUpper(), "HOLD", true) == 0)
                return EntityState.HOLD;

            return EntityState.WAIT;
        }

        public static string DateTimeToString(this DateTime dt, bool isTime = true)
        {
            string result = string.Empty;

            if (isTime)
            {
                result = dt.Year.ToString("D4")
                        + dt.Month.ToString("D2")
                        + dt.Day.ToString("D2")
                        + dt.Hour.ToString("D2")
                        + dt.Minute.ToString("D2")
                        + dt.Second.ToString("D2");
            }
            else
            {
                result = dt.Year.ToString("D4")
                        + dt.Month.ToString("D2")
                        + dt.Day.ToString("D2");
            }
            return result;
        }

        public static string DateTimeToString(this DateTime? dt, bool isTime = true)
        {
            if (dt == null)
                return string.Empty;
            else
                return dt.GetValueOrDefault().DateTimeToString(isTime);
        }
    }
}