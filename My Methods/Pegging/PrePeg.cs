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
    public static partial class PrePeg
    {
        static public List<DemandGroup> InitDemGroupList = new List<DemandGroup>(); // 정적
        static public HashedSet<string> DemGroupKeyList = new HashedSet<string>();

        // 동적 Obj
        static public HashedSet<SEMPegWipInfo> AvailWips = new HashedSet<SEMPegWipInfo>();
        static public HashedSet<DemandGroup> DemGroups = new HashedSet<DemandGroup>(); // 전체 target
        static public List<DemandGroup> TargetDemGroups = new List<DemandGroup>(); // 해당 phase의 target
        //static public DateTime TargetDueDate = DateTime.MinValue;
        static public double TargetPriority = double.MinValue;
        static public string TargetOperId = string.Empty;

        public static void MakeDemandGroup(SEMGeneralPegPart semPp)
        {
            string key = PrePegHelper.CreateDgKey(semPp);
            semPp.DemKey = key;

            if (DemGroupKeyList.Contains(key) == true)
            {
                DemandGroup dg = InitDemGroupList.Where(r => r.Key == key).FirstOrDefault();
                dg.AddDemandList(semPp);
            }
            else
            {
                DemandGroup dg = new DemandGroup(semPp);
                InitDemGroupList.Add(dg);
                DemGroupKeyList.Add(key);
            }
        }


    }
}