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
    public class DateTimeRange
    {
        public DateTimeRange(DateTime start, DateTime end)
        {
            this.Start = start;
            this.End = end;
        }
        public static DateTime Min(DateTime d1, DateTime d2)
        {
            return d1> d2? d2 : d1;
        }
        public static DateTime Max(DateTime d1, DateTime d2)
        {
            return d1 > d2 ? d1 : d2;
        }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool IsOverlapping(DateTimeRange other)
        {
            return this.Start < other.End && this.End > other.Start;
        }
        public DateTimeRange GetOverlapping(DateTimeRange other)
        {
            if (this.IsOverlapping(other) == false)
                return null;

            DateTime start = DateTimeRange.Max(this.Start, other.Start);
            DateTime end = DateTimeRange.Min(this.End, other.End);
            DateTimeRange dtr = new DateTimeRange(start, end);

            return dtr;
        }
        public IEnumerable<DateTimeRange> GetCompelementsOf(DateTimeRange other)
        {
            if (this.IsOverlapping(other) == false)
                return null;

            List<DateTimeRange> dtrs = new List<DateTimeRange>();
            if (this.Start != other.Start)
            {
                DateTime start = DateTimeRange.Min(this.Start, other.Start);
                DateTime end = DateTimeRange.Max(this.Start, other.Start);
                DateTimeRange dtr = new DateTimeRange(start, end);
                dtrs.Add(dtr);
            }
            
            if (this.End != other.End)
            {
                DateTime start = DateTimeRange.Min(this.End, other.End);
                DateTime end = DateTimeRange.Max(this.End, other.End);
                DateTimeRange dtr = new DateTimeRange(start, end);
                dtrs.Add(dtr);
            }

            return dtrs;
        }
    }
}