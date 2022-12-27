using Mozart.SeePlan.Pegging;
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

namespace SEM_AREA.Logic.Pegging
{
    [FeatureBind()]
    public partial class BUILD_INPLAN
    {
        public PegPart BUILD_IN_PLAN0(PegPart pegPart, ref bool handled, PegPart prevReturnValue)
        {
            var mp = pegPart as MergedPegPart;
            var pegParts = mp.Items.OfType<SEMGeneralPegPart>();

            var dic = new DoubleDictionary<SEMProduct, IComparable, InTarget>();
            foreach (var pp in pegParts)
            {
                var product = pp.Product as SEMProduct;
                if (!dic.TryGetValue(product, out Dictionary<IComparable, InTarget> planDic))
                    dic.Add(product, planDic = new Dictionary<IComparable, InTarget>());

                foreach (var pt in pp.PegTargetList)
                {
                    var moPlan = pt.MoPlan as SEMGeneralMoPlan;
                    var key = Tuple.Create(pt.DueDate, mp);
                    if (!planDic.TryGetValue(key, out InTarget it))
                    {
                        it = new InTarget();
                        it.TargetDate = pt.DueDate;
                        it.Product = ((pp as SEMGeneralPegPart).Product) as SEMProduct;
                        it.MoPlan = moPlan;

                        planDic.Add(key, it);
                    }

                    it.TargetQty += Convert.ToInt32(pt.Qty);
                }
            }

            var targets = InputMart.Instance.InTarget;
            foreach (var planDic in dic.Values)
                foreach (var it in planDic.Values)
                    targets.Rows.Add(it);

            return pegPart;
        }
    }
}