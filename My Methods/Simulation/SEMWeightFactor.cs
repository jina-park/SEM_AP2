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
using Mozart.SeePlan.DataModel;

namespace SEM_AREA
{
    [FeatureBind()]
    public partial class SEMWeightFactor : WeightFactor
    {
        public string OrigCriteria { get; set; }
        public bool IsAllowFilter { get; set; }

        public SEMWeightFactor(string name, double weightFactor, float sequence, FactorType type, OrderType orderType, string criteria, bool isAllowFilter)
            : base(name, weightFactor, sequence, type, orderType)
        {
            this.OrigCriteria = criteria;
            this.IsAllowFilter = IsAllowFilter;

            if (criteria == null)
                return;

            string[] splitCriteria = criteria.Split(';');
            splitCriteria.ForEach(x => x.Trim());
            base.Criteria = splitCriteria;
        }
    }
}