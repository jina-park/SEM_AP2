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
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using Mozart.Simulation.Engine;
using Mozart.SeePlan.Simulation;
using Mozart.SeePlan.TimeLibrary;
using Mozart.SeePlan;
using System.Globalization;

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class CommonHelper
    {
        /// <summary>
        /// Check if the string matches the given pattern
        /// </summary>
        /// <param name="text">The string value to check</param>
        /// <param name="pattern">The string value to be checked against</param>
        /// <param name="caseSensetive">Are you sure you really wanna check upper/lower cases?</param>
        /// <returns></returns>
        public static bool IsLike(this string text, string pattern, bool caseSensetive = false)
        {
            // Wild card to regular expression
            pattern = "^" + Regex.Escape(pattern).Replace("_", ".*") + "$";

            return Regex.IsMatch(text, pattern);
        }
        private static StringBuilder sBuilder = new StringBuilder(2048);
        public static string Concat(string delimiter, params string[] strArr)
        {
            sBuilder.Clear();
            foreach (string str in strArr)
            {
                if (sBuilder.Length > 0)
                    sBuilder.Append(delimiter);
                
                sBuilder.Append(str);
            }
            return sBuilder.ToString();
        }

        public static string CreateKey(params string[] strArr)
        {
            sBuilder.Clear();

            int cnt = 0;
            foreach (string str in strArr)
            {
                if (cnt++ > 0)
                    sBuilder.Append('@');

                if (str == null)
                    sBuilder.Append("");
                else
                    sBuilder.Append(str);
            }
            return sBuilder.ToString();
        }

        public static string CreateKey(IEnumerable<string> strArr)
        {
            sBuilder.Clear();

            int cnt = 0;
            foreach (string str in strArr)
            {
                if (cnt++ > 0)
                    sBuilder.Append('@');

                sBuilder.Append(str);
            }
            return sBuilder.ToString();
        }

        public static string ListToString(this IEnumerable<string> list)
        {
            if (list == null || list.Count() == 0)
                return string.Empty;

            sBuilder.Clear();
            
            int cnt = 0;
            foreach (string str in list)
            {
                if (cnt++ > 0)
                    sBuilder.Append(", ");
                
                sBuilder.Append(str);
            }

            return sBuilder.ToString();
        }
        public static string ListToString(this IEnumerable<string> list, string delimiter = " / ")
        {
            if (list == null || list.Count() == 0)
                return string.Empty;

            sBuilder.Clear();

            int cnt = 0;
            foreach (string str in list)
            {
                if (cnt++ > 0)
                    sBuilder.Append(delimiter);

                sBuilder.Append(str);
            }

            return sBuilder.ToString();
        }

        public static string DictionaryToString(this Dictionary<string,string> dic, string delimiter = ", ")
        {
            if (dic == null || dic.Count == 0)
                return string.Empty;

            sBuilder.Clear();

            int cnt = 0;

            foreach (var pair in dic)
            {
                if (cnt++ > 0)
                    sBuilder.Append(delimiter);

                sBuilder.Append(pair.Key);
                sBuilder.Append(":");
                sBuilder.Append(pair.Value);
            }

            return sBuilder.ToString();
        }

        public static string QueueToString(this SearchQueue<WorkLot> list)
        {
            if (list == null || list.Count() == 0)
                return string.Empty;

            sBuilder.Clear();

            int cnt = 0;
            foreach (var wLot in list)
            {
                if (cnt++ > 0)
                    sBuilder.Append(", ");

                sBuilder.Append(wLot.Lot.LotID);
            }

            return sBuilder.ToString();
        }
        internal static void ArrayAdd<T>(ref T[] array, T item)
        {
            if (array == null || array.Length == 0)
                array = new T[] { item };
            else
            {
                Array.Resize<T>(ref array, array.Length + 1);
                array[array.Length - 1] = item;
            }
        }
        internal static bool ArrayContains<T>(T[] array, T item)
        {
            if (array == null || array.Length == 0)
                return false;

            for (int i = 0; i < array.Length; i++)
                if (object.Equals(array[i], item))
                    return true;
            return false;
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
        public static bool Equals(string a, string b, StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase)
        {
            return string.Equals(a, b, comparisonType);
        }
        public static object GetPropValue(this object obj, string propName)
        {
            if (obj == null)
                return null;

            PropertyInfo prop = obj.GetType().GetProperties().Where(x => x.Name == propName).FirstOrDefault();

            if (prop == null)
                return obj;
            else
                return prop.GetValue(obj);
        }

        public static DateTime GetLinkerTime(this Assembly assembly, TimeZoneInfo target = null)
        {
            var filePath = assembly.Location;
            const int c_PeHeaderOffset = 60;
            const int c_LinkerTimestampOffset = 8;

            var buffer = new byte[2048];

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                stream.Read(buffer, 0, 2048);

            var offset = BitConverter.ToInt32(buffer, c_PeHeaderOffset);
            var secondsSince1970 = BitConverter.ToInt32(buffer, offset + c_LinkerTimestampOffset);
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var linkTimeUtc = epoch.AddSeconds(secondsSince1970);

            var tz = target ?? TimeZoneInfo.Local;
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(linkTimeUtc, tz);

            return localTime;
        }

        private static bool isWriteBuildInfo = true;

        public static void WriteBuildInfo()
        {
            if (isWriteBuildInfo == false)
                return;

            isWriteBuildInfo = false;

            System.Reflection.Assembly seeplan = System.Reflection.Assembly.GetAssembly(typeof(Mozart.SeePlan.ShopCalendar));
            System.Reflection.Assembly general = System.Reflection.Assembly.GetAssembly(typeof(Mozart.SeePlan.General.GeneralLibrary));
            System.Reflection.Assembly semPlanning = System.Reflection.Assembly.GetExecutingAssembly();

            Logger.MonitorInfo("  - SeePlan Lib : {0} / {1}", seeplan.GetName().Version.ToString(), seeplan.Location);
            Logger.MonitorInfo("  - General Lib : {0} / {1}", general.GetName().Version.ToString(), general.Location);
            Logger.MonitorInfo("  - SEMPlanning Lib : {0} / {1} / {2}", semPlanning.GetName().Version.ToString(), semPlanning.Location, semPlanning.GetLinkerTime());

#if DEBUG
            Logger.MonitorInfo(string.Format("  - {0} -> {1}", semPlanning.GetName().Name, "DEBUG"));
#else
            Logger.MonitorInfo(string.Format("{0} -> {1}", semPlanning.GetName().Name, "RELEASE"));
#endif
        }

        public static DateTime StartDayOfWeek(DateTime date)
        {
            int dayOfWeek = (int)date.DayOfWeek;
            if (dayOfWeek == 0)
                dayOfWeek = 7;

            return date.AddDays((int)DayOfWeek.Monday - dayOfWeek);
        }
        public static DateTime BaseDayOfWeek(DateTime date)
        {
            return StartDayOfWeek(date).AddDays(3);
        }
        public static int WeekNo(DateTime date)
        {
            DateTime baseDay = BaseDayOfWeek(ShopCalendar.SplitDate(date));

            return baseDay.Year * 100 + WeekOfYear(baseDay);
        }
        public static string GetWeekNo(DateTime date)
        {
            return WeekNo(date).ToString();
        }
        public static int WeekOfYear(DateTime date)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;

            return GetIso8601WeekOfYear(ci.Calendar, date);
        }
        public static int GetIso8601WeekOfYear(Calendar cal, DateTime time)
        {
            // Seriously cheat.  If its Monday, Tuesday or Wednesday, then it'll 
            // be the same week# as whatever Thursday, Friday or Saturday are,
            // and we always get those right
            DayOfWeek day = cal.GetDayOfWeek(time);
            if (day >= DayOfWeek.Monday && day <= DayOfWeek.Wednesday)
            {
                time = time.AddDays(3);
            }

            // Return the week of our adjusted day
            return cal.GetWeekOfYear(time, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        public static DateTime StringToDateTime(string value, bool withTime = true)
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
    }
}