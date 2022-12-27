using Mozart.SeePlan.Simulation;
using Mozart.SeePlan.DataModel;
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

namespace SEM_AREA.Logic.Simulation
{
    [FeatureBind()]
    public partial class ProcessControl
    {
        public ProcTimeInfo GET_PROCESS_TIME0(Mozart.SeePlan.Simulation.AoEquipment e, IHandlingBatch hb, ref bool handled, ProcTimeInfo prevReturnValue)
        {
            SEMAoEquipment aeqp = e as SEMAoEquipment;
            SEMEqp eqp = e.Target as SEMEqp;
            SEMLot lot = hb.Sample as SEMLot;

            ProcTimeInfo time = TimeHelper.GetProcessTime(lot, eqp);
            if (aeqp.ProcessingLot == null)
            {
                aeqp.ProcessingLot = lot;
                aeqp.LotStartTime = AoFactory.Current.NowDT;
                aeqp.LotEndTime = AoFactory.Current.NowDT;
            }

            var processingMin = time.TactTime.TotalMinutes * lot.UnitQtyDouble;
            aeqp.LotEndTime = aeqp.LotEndTime.AddMinutes(processingMin);

            return time;
        }
        public void ON_ENTERED0(AoEquipment aeqp, AoProcess proc, IHandlingBatch hb, ref bool handled)
        {
            #region obsolete
            //if (InputMart.Instance.GlobalParameters.ApplyInflow2 == true)
            //{
            //    SEMLot lot = hb.ToSEMLot();

            //    //주석 박진아 : 안쓰는 것으로 보임
            //    //lot.ReserveEqp = string.Empty;


            //    InflowMaster.UpdateLot(lot, aeqp, proc, "OnEntered");
            //}
            #endregion
            
            var semEqp = aeqp.Target as SEMEqp;            
            var lot = hb.Sample as SEMLot;
            //lot.FactoryID = semEqp.FactoryID;
            //lot.FloorID = semEqp.FloorID;

            //var semWip = lot.WipInfo as SEMWipInfo;
            //semWip.FactoryID = semEqp.FactoryID;
            //semWip.FloorID = semEqp.FloorID;

        }

        public bool IS_NEED_SETUP0(AoEquipment aeqp, IHandlingBatch hb, ref bool handled, bool prevReturnValue)
        {            
            var lot = hb.Sample as SEMLot;
            var eqp = aeqp.Target as SEMEqp;
            if (eqp.EqpID == "E2100675") { }

            bool isNeedSetup = SetupMaster.IsNeedSetup(eqp, lot, true, true);

            // Last Job Condition 정보가 없는경우 eqp.StepJobConditions에 데이터가 없음, 투입하는 lot의 작업 조건을 넣어줌
            if (eqp.StepJobConditions.Count() == 0)
                eqp.UpdateJobCondition(lot);

            return isNeedSetup;

            // Update job conditions of the current resource at GET_SETUP_TIME
            // Job conditions to be updated can be retrieved from aeqp.GetSetupTime(lot, out List<SEMEqpJobCondition>)
        }

        public void ON_TRACK_IN0(AoEquipment aeqp, IHandlingBatch hb, ref bool handled)
        {
            SEMAoEquipment sEqp = aeqp as SEMAoEquipment;
            SEMLot lot = hb.Sample as SEMLot;
            sEqp.ProcessingLot = lot;
            sEqp.LotStartTime = AoFactory.Current.NowDT;
            sEqp.LotEndTime = AoFactory.Current.NowDT;
        }

        public void ON_TRACK_OUT0(AoEquipment aeqp, IHandlingBatch hb, ref bool handled)
        {
            SEMAoEquipment sEqp = aeqp as SEMAoEquipment;
            SEMLot lot = hb.Sample as SEMLot;
            SEMWipInfo wipInfo = lot.WipInfo as SEMWipInfo;

            sEqp.ProcessingLot = null;
            sEqp.LotStartTime = DateTime.MinValue;
            sEqp.LotEndTime = DateTime.MinValue;
        }

        public double GET_PROCESS_UNIT_SIZE1(AoEquipment aeqp, IHandlingBatch hb, ref bool handled, double prevReturnValue)
        {
            double unitSize = SeeplanConfiguration.Instance.LotUnitSize;

            var lot = hb.Sample as SEMLot;
            if (!aeqp.IsBatchType())
            {
                unitSize = lot.GetUnitQty();
            }

            return unitSize;
        }
    }
}