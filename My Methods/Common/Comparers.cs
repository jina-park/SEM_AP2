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

namespace SEM_AREA
{
    [FeatureBind()]
    public static partial class Comparers
    {
        public class PegWipInfoCompare : IComparer<SEMPegWipInfo>
        {
            public int Compare(SEMPegWipInfo x, SEMPegWipInfo y)
            {
                // 해당 매소드 수정시  PEG_WIP/SORT_WIP1도 정렬 방식이 동일하게 되도록 수정

                SEMWipInfo x_wip = x.WipInfo;
                SEMWipInfo y_wip = y.WipInfo;

                int cmp = 0;
                if (object.ReferenceEquals(x, y))
                    return 0;

                // 1. Demand Oper에 도착시간 빠른 것 우선
                cmp = x.CalcAvailDate.CompareTo(y.CalcAvailDate);
                if (cmp != 0)
                    return cmp;

                // 2.Run중인 lot 우선
                if (x_wip.WipState.ToUpper() == "RUN" && y_wip.WipState.ToUpper() != "RUN")
                    return -1;

                if (x_wip.WipState.ToUpper() != "RUN" && y_wip.WipState.ToUpper() == "RUN")
                    return 1;

                // 3. ResFixedWip 우선
                string resFixOper = InputMart.Instance.TargetOperIDForSort == "SG4090" ? "SG3910" : "SG4430";
                bool xIsResFix = x_wip.IsResFixLotArrange && x_wip.LotArrangeOperID == resFixOper;
                bool yIsResFix = y_wip.IsResFixLotArrange && y_wip.LotArrangeOperID == resFixOper;

                if (xIsResFix && yIsResFix == false)
                    return -1;

                if (xIsResFix == false && yIsResFix)
                    return 1;

                // 4. ReelLabel된것 우선
                if (x_wip.IsReelLabeled && y_wip.IsReelLabeled == false)
                    return -1;

                if (x_wip.IsReelLabeled == false && y_wip.IsReelLabeled)
                    return 1;

                // 5. 긴급도 높은 wip(UrgentPriority가 작은것)우선
                cmp = x_wip.UrgentPriority.CompareTo(y_wip.UrgentPriority);
                if (cmp != 0)
                    return cmp;

                // 6. WipArrivalTime 빠른것 우선
                cmp = x_wip.WipArrivedTime.CompareTo(y_wip.WipArrivedTime);
                if (cmp != 0)
                    return cmp;

                // 7. Wip Qty 많은 것 우선
                cmp = x_wip.UnitQty.CompareTo(y_wip.UnitQty) * -1;

                return cmp;
            }
        }

        public class DemGroupCompare : IComparer<DemandGroup>
        {
            public int Compare(DemandGroup x, DemandGroup y)
            {
                #region value
                int cmp = 0;
                SEMGeneralPegPart x_dem = x.Sample;
                SEMGeneralPegPart y_dem = y.Sample;

                if (object.ReferenceEquals(x, y))
                    return -1;
                #endregion

                cmp = x.TargetOperId.CompareTo(y.TargetOperId) * -1;
                if (cmp != 0)
                    return cmp;

                cmp = x_dem.Priority.CompareTo(y_dem.Priority);
                if (cmp != 0)
                    return cmp;

                cmp = x_dem.DueDate.CompareTo(y_dem.DueDate);
                if (cmp != 0)
                    return cmp;

                cmp = x_dem.DemandQty.CompareTo(y_dem.DemandQty) * -1;
                if (cmp != 0)
                    return cmp;

                cmp = x_dem.DemandID.CompareTo(y_dem.DemandID);
                if (cmp != 0)
                    return cmp;

                return -1;
            }
        }

        public class SEMPegPartCompare : IComparer<SEMGeneralPegPart>
        {
            public int Compare(SEMGeneralPegPart x, SEMGeneralPegPart y)
            {
                #region value
                int cmp = 0;
                //SEMGeneralPegPart x_dem = x.Sample;
                //SEMGeneralPegPart y_dem = y.Sample;

                if (object.ReferenceEquals(x, y))
                    return -1;
                #endregion

                cmp = x.TargetOperID.CompareTo(y.TargetOperID) * -1;
                if (cmp != 0)
                    return cmp;

                cmp = x.Priority.CompareTo(y.Priority);
                if (cmp != 0)
                    return cmp;

                cmp = x.DueDate.CompareTo(y.DueDate);
                if (cmp != 0)
                    return cmp;

                cmp = x.DemandQty.CompareTo(y.DemandQty) * -1;
                if (cmp != 0)
                    return cmp;

                cmp = x.DemandID.CompareTo(y.DemandID);
                if (cmp != 0)
                    return cmp;

                return -1;
            }

        }

        public class ReservedLotCompare : IComparer<SEMLot>
        {

            public int Compare(SEMLot x, SEMLot y)
            {
                SEMWipInfo xWip = x.Wip;
                SEMWipInfo yWip = y.Wip;
                
                
                // ResourceFixedLot 우선
                if (xWip.IsResFixLotArrange && yWip.IsResFixLotArrange == false)
                    return -1;

                if (xWip.IsResFixLotArrange == false && yWip.IsResFixLotArrange)
                    return 1;

                SEMEqp eqp = InputMart.Instance.CurrentDispatchingEqp;

                // Setup 하지 않는 Lot 우선
                bool xIsneedSetup = SetupMaster.IsNeedSetup(eqp, x, true);
                bool yIsneedSetup = SetupMaster.IsNeedSetup(eqp, y, true);

                if (xIsneedSetup && yIsneedSetup == false)
                    return 1;

                if (xIsneedSetup == false && yIsneedSetup)
                    return -1;

                return 0;
            }
        }


        public class ResFixLotArrangeCompare : IComparer<SEMLot>
        {

            public int Compare(SEMLot x, SEMLot y)
            {
                SEMWipInfo xWip = x.Wip;
                SEMWipInfo yWip = y.Wip;

                SEMEqp xEqp; // = InputMart.Instance.ResFixLotArrangeDic[x.LotID];
                SEMEqp yEqp; // = InputMart.Instance.ResFixLotArrangeDic[y.LotID];

                // 장비 가져오기
                if (InputMart.Instance.ResFixedLotArrangeDic.TryGetValue(x.LotID, out xEqp) == false)
                    return 1;                

                if (InputMart.Instance.ResFixedLotArrangeDic.TryGetValue(y.LotID, out yEqp) == false)
                    return -1;

                // 1.Setup 하지 않는 Lot 우선
                bool xIsneedSetup = SetupMaster.IsNeedSetup(xEqp, x, true);
                bool yIsneedSetup = SetupMaster.IsNeedSetup(yEqp, y, true);

                if (xIsneedSetup && yIsneedSetup == false)
                    return 1;

                if (xIsneedSetup == false && yIsneedSetup)
                    return -1;

                // 2.납기 늦은 Lot 우선
                var now = AoFactory.Current.NowDT;
                double xLpstGap = (now - x.GetLPST(xWip.LotArrangeOperID)).TotalDays;
                double yLpstGap = (now - y.GetLPST(yWip.LotArrangeOperID)).TotalDays;

                if (xLpstGap < yLpstGap)
                    return 1;

                if (xLpstGap > yLpstGap)
                    return -1;


                // 3.PRE_END_TIME 빠른 Lot 우선
                if (x.PreEndTime > y.PreEndTime)
                    return 1;

                if (x.PreEndTime < y.PreEndTime)
                    return -1; 


                // 4.모든 우선순위 조건이 동일
                return 0;
            }
        }
        public class JobGroupCompare : IComparer<JobConditionGroup>
        {
            public int Compare(JobConditionGroup x, JobConditionGroup y)
            {

                // 1.Step
                int xPriority= x.GetPriority();
                int yPriority = y.GetPriority();

                // priority 작은것 우선
                if (xPriority < yPriority)
                    return -1;
                else if (xPriority > yPriority)
                    return 1;
                else
                    return 0;                
            }

        }

        public static int GetPriority(this JobConditionGroup jGroup)
        {
            string stepKey = jGroup.GetStepKey();

            if (stepKey == "SG3910")
                return 1;
            else if (stepKey == "SG4140")
                return 2;
            else if (stepKey == "SG5160")
                return 3;
            else if (stepKey == "SG4430")
                return 4;
            else
                return 99;
        }

        public class AssignEqpCompare : IComparer<EqpAssignInfo>
        {
            public int Compare(EqpAssignInfo x, EqpAssignInfo y)
            {
                // 1. UrgentPriority가 큰 장비 우선 할당
                int xUrgentPriority = x.WorkStep.GetUrgentPriority();
                int yUrgentPriority = y.WorkStep.GetUrgentPriority();

                if (xUrgentPriority > yUrgentPriority)
                    return -1;

                if (xUrgentPriority < yUrgentPriority)
                    return 1;

                // 2. SETUP시간 짧은 장비 우선 할당
                double xTime = x.SetupTime;
                double yTime = y.SetupTime;

                if (xTime > yTime)
                    return 1;

                if (xTime < yTime)
                    return -1;

                // 3. Waiting시간 짧은 장비 우선 할당
                xTime = x.WaitingTime;
                yTime = y.WaitingTime;

                if (xTime > yTime)
                    return 1;

                if (xTime < yTime)
                    return -1;

                // 4. 작업 가능한 lot이 많은 것 우선
                if (x.AoEqp.ArrangedLots.Count < y.AoEqp.ArrangedLots.Count)
                    return 1;

                if (x.AoEqp.ArrangedLots.Count > y.AoEqp.ArrangedLots.Count)
                    return -1;

                return 0;
            }
        }

        public class AssignEqpCompareForUrgent : IComparer<EqpAssignInfo>
        {
            public int Compare(EqpAssignInfo x, EqpAssignInfo y)
            {
                // 1. UrgentPriority 큰 eqp 우선
                int xUrgentPriority = x.WorkStep.GetUrgentPriority();
                int yUrgentPriority = y.WorkStep.GetUrgentPriority();

                if (xUrgentPriority > yUrgentPriority)
                    return -1;

                if (xUrgentPriority < yUrgentPriority)
                    return 1;

                // 2. 투입 가능시간 빠른 것 우선
                double xTime = x.WaitingTime + x.SetupTime;
                double yTime = y.WaitingTime + y.SetupTime;

                if (xTime > yTime)
                    return 1;

                if (xTime < yTime)
                    return -1;

                // 3. 작업 가능한 lot이 많은 것 우선
                if (x.AoEqp.ArrangedLots.Count < y.AoEqp.ArrangedLots.Count)
                    return 1;

                if (x.AoEqp.ArrangedLots.Count > y.AoEqp.ArrangedLots.Count)
                    return -1;

                return 0;
            }
        }
        public static int GetDueScore(this EqpAssignInfo info)
        {
            if (info.DueState == Constants.Delay)
                return 3;
            else if (info.DueState == Constants.Normal)
                return 2;
            else if (info.DueState == Constants.Precede)
                return 1;
            else if (info.DueState == Constants.None)
                return 0;
            else
            {
                WriteLog.WriteErrorLog("알 수 없는 DueState");
                return 0;
            }
        }
    }
}