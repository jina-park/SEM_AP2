using Mozart.SeePlan.Simulation;
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
using Mozart.Simulation.Engine;

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class FilterControl
    {
        public IHandlingBatch[] CHECK_RESERVATION0(DispatchingAgent da, AoEquipment aeqp, ref bool handled, IHandlingBatch[] prevReturnValue)
        {
#if false // 2022-03-22 ResFixLotArrange 로직은 더이상 Reservation에서 구현 하지 않고 LOCATE_FOR_DISPATCH3, SORT_ONLY_LOT_ARRANGE_WIPS에서 구현됨 
            SEMAoEquipment seqp = aeqp as SEMAoEquipment;

            // 예약 여부 확인 
            if (seqp.ReservedLotList.Count == 0)
                return prevReturnValue;

            // 현재 epq 캐싱 (compare에 eqp를 넘기는 방법을 몰라서)
            InputMart.Instance.CurrentDispatchingEqp = seqp.Target as SEMEqp;

            // 예약 Lot을 Dispatching 우선순위 대로 정렬
            seqp.ReservedLotList.Sort(new Comparers.ReservedLotCompare());

            // List에서 가장 우선순위 높은 Lot선택
            var selectedLot = seqp.ReservedLotList.First();

            // 선택된 lot 예약리스트에서 삭제 후 리턴
            seqp.ReservedLotList.Remove(selectedLot);
            IHandlingBatch[] result = new IHandlingBatch[] { selectedLot };
            return result;           
# endif
            return prevReturnValue;
        }

        public IList<IHandlingBatch> DO_FILTER1(AoEquipment e, IList<IHandlingBatch> wips, IDispatchContext ctx, ref bool handled, IList<IHandlingBatch> prevReturnValue)
        {
            var aeqp = e as SEMAoEquipment;

            var filterControl = DispatchFilterControl.Instance;

            filterControl.SetFilterContext(e, wips, ctx);

            var lots = new List<SEMLot>();
            wips.ForEach(x => lots.Add(x.Sample as SEMLot));

            // 동일 job Group 내 모든 wip을 대상으로 dispatching
            var candidateLots = lots.GetCandidateWips(aeqp);

            for (int i = candidateLots.Count - 1; i >= 0; i--)
            {
                var lot = candidateLots[i];

                SEMGeneralStep step = lot.CurrentStep as SEMGeneralStep;

                if (lot.IsSameLotFilter(aeqp))
                {
                    candidateLots.RemoveAt(i);
                    continue;
                }

                //if (lot.IsResFixLotFilter(aeqp))
                //{
                //    candidateLots.RemoveAt(i);
                //    continue;
                //}

                if (step.IsProcessing == false)
                {
                    candidateLots.RemoveAt(i);
                    continue;
                }

                if (lot.CurrentPlan.LoadedResource != null)
                {
                    candidateLots.RemoveAt(i);
                    continue;
                }

                filterControl.SetLotCondition(e, lot, ctx);

                if (filterControl.CheckSecondResouce(e, lot, ctx) == false)
                {
                    candidateLots.RemoveAt(i);
                    continue;
                }

                if (filterControl.CheckSetupCrew(e, lot, ctx) == false)
                {
                    candidateLots.RemoveAt(i);
                    continue;
                }

                var filterKey = filterControl.GetFilterSetKey(e, lot, ctx);
                if (string.IsNullOrEmpty(filterKey))
                {
                    if (filterControl.IsLoadable(e, lot, ctx) == false)
                    {
                        candidateLots.RemoveAt(i);
                        continue;
                    }
                }
                else
                {
                    if (AoFactory.Current.Filters.Filter(filterKey, lot, AoFactory.Current.NowDT, e, ctx))
                    {
                        candidateLots.RemoveAt(i);
                        continue;
                    }
                }
            }

            List<IHandlingBatch> result = new List<IHandlingBatch>();
            candidateLots.ForEach(x => result.Add(x));

            if (result.IsNullOrEmpty()) { }
            return result;
        }       
    }
}