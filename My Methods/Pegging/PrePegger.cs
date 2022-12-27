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
    public class PrePegger
    {
        public static void InitPrePeg()
        {
            int idx = 0;
            Dictionary<string, List<DemandGroup>> initDemGroupDic = PrePeg.InitDemGroupList.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.ToList());

            #region pegWipInfo Init
            foreach (SEMPegWipInfo pwi in InputMart.Instance.SEMPegWipInfo.Rows)
            {
                PegWipInfoManager.PwiValidate(pwi, initDemGroupDic);

                if (pwi.IsUsable)
                    PrePeg.AvailWips.Add(pwi);

                //pegWipInfoObject Write
                pwi.PegWipIdx = pwi.WipInfo.LotID + "_" + idx++;
                WriteLog.WritePegWipInfo(pwi);
            }
            #endregion

            #region DemandGroup Init

            // DemandGroup Write
            foreach (DemandGroup dg in PrePeg.InitDemGroupList)
            {
                dg.DemList.AddRange(dg.InitDemandList);
                dg.DemList.Sort(new Comparers.SEMPegPartCompare());
                WriteLog.WriteDemandGroupLog(dg);
            }

            // Pegging 대상 Demand
            PrePeg.DemGroups.AddRange(PrePeg.InitDemGroupList.Where(r => r.AvailWipList.Count() > 0 && r.ToTalQty > 0));

            // Wip이 없는 DemandGroup 및 Demand 삭제
            var invalidDemandGroups = PrePeg.InitDemGroupList.Where(r => r.AvailWipList.Count() == 0);
            foreach (var demandGroup in invalidDemandGroups)
            {
                foreach (var demand in demandGroup.DemList)
                {
                    WriteLog.WritePegValidationDemand_UNPEG(demand, "NO_WIP", "");
                    demand.UnpegReason = "NO_WIP";
                }

                // DemandGroup을 삭제
                PrePeg.DemGroups.Remove(demandGroup);
            }


            // Product의 자재코드가 없는 DemandGroup 및 Demand 삭제
            foreach (var demandGroup in PrePeg.InitDemGroupList)
            {
                foreach (var demand in demandGroup.DemList)
                {
                    if (demand.TargetOperID == Constants.TAPING_OPER_ID && demand.SemProduct.MaterialList.Count == 0)
                    {
                        // Product의 자재코드가 없음
                        WriteLog.WritePegValidationDemand_UNPEG(demand, "NO_MATERIAL_CODE", "");
                        PrePeg.DemGroups.Remove(demandGroup);
                        demand.UnpegReason = "NO_MATERIAL_CODE";
                        continue;
                    }
                }
            }
            #endregion

            #region Target Demand Init
            // Target Demand 설정을 위한 셋팅
            //PrePeg.TargetDueDate = InputMart.Instance.DemDueDateList.FirstOrDefault();
            PrePeg.TargetOperId = Constants.SORTING_OPER_ID;

            var tarDemGroups = PrePeg.DemGroups.Where(x => x.Sample.TargetOperID == PrePeg.TargetOperId).FirstOrDefault();
            if (tarDemGroups != null)
            {
                //PrePeg.TargetPriority = PrePeg.DemGroups.Where(x => x.Sample.TargetOperID == PrePeg.TargetOperId).First().Sample.Priority;
                PrePeg.TargetPriority = tarDemGroups.Sample.Priority;
            }
            #endregion

            #region Same Lot Init

            #endregion
        }

        private static int pegCnt = 0;

        public static void DoPrePeg()
        {
            bool pegMore = true;

            // phase : duedate @ OperTarget
            int phase = 0;

            while (pegMore && phase < 100000)
            {
                if (PrePeg.AvailWips.Count() == 0)
                    break;

                bool hasDemList = SetTargetDemList(phase == 0);
                if (hasDemList == false)
                    break;

                // TargetDemandGroups 설정 : duedate가 같은 DemandGroup들
                List<DemandGroup> targetDemandGroups = PrePeg.TargetDemGroups.ToList();
                targetDemandGroups.Sort(new Comparers.DemGroupCompare());
                PrePeg.TargetDemGroups.Clear();
                PrePeg.TargetDemGroups.AddRange(targetDemandGroups);

                //phase
                bool phaseMore = true;
                int seq = 0;
                while (phaseMore && seq < 100000 && PrePeg.AvailWips.Count() != 0)
                {
                    seq++;
                    int priority = 0;
                    for (int i = 0; i < PrePeg.TargetDemGroups.Count(); i++)
                    {
                        bool isPegged = false;
                        bool isFiltered = false;
                        HashedSet<string> filterReason = new HashedSet<string>();

                        DemandGroup selectedGroup = PrePeg.TargetDemGroups.ToList()[i];
                        
                        InputMart.Instance.TargetOperIDForSort = selectedGroup.TargetOperId;

                        List<SEMPegWipInfo> targetAvailWips = new List<SEMPegWipInfo>(selectedGroup.AvailWipList);
                        //List<SEMPegWipInfo> targetAvailWips = GetTargetAvailWips(selectedGroup);

                        //targetAvailWips.AddRange(selectedGroup.AvailWipList.ToArray());
                        targetAvailWips.QuickSort(new Comparers.PegWipInfoCompare()); // wip Sort

                        for (int j = 0; j < targetAvailWips.Count(); j++)
                        {
                            SEMPegWipInfo selectedWip = targetAvailWips[j];
                            bool filter = FliterPegWip(selectedWip, selectedGroup.Sample, ref filterReason);
                            if (filter == true)
                            {
                                isFiltered = true;
                                continue;
                            }

                            #region peg
                            isPegged = true;
                            targetAvailWips.Remove(selectedWip);
                            j--;

                            bool isFinishPhase = DoPrePeg(selectedWip, selectedGroup, phase, seq, ref priority);
                            if (isFinishPhase)
                            {
                                // DemandGroup내 targetDue인 Demand를 모두 pegging 완료하여 다음 Due인 Demand까지 차감한 DemandGroup은 TargetDemandGroups에서 삭제
                                RemoveTargetDemGroups(selectedGroup, ref i);
                            }
                            #endregion

                            if (pegCnt % 100 == 0)
                                Logger.MonitorInfo($"{pegCnt.ToString("D7")}==> Do Prepeg Wip : {selectedWip.WipInfo.LotID}\t\tDemand ID : {selectedGroup.Key}\t\t{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ffffff")}");

                            pegCnt++;

                            break;
                        }

                        if (isPegged == false)
                        {
                            if (isFiltered)
                            {
                                // Demand에 pegging 가능한 wip이 있었으나 Filper로직으로 filter됨
                                foreach (var pp in selectedGroup.DemList)
                                {
                                    WriteLog.WritePegValidationDemand_UNPEG(pp, "ALL_WIPS_FILTERED", "PEG_WIP_FILTER_LOG 참조");
                                    pp.UnpegReason = "ALL_WIPS_FILTERED";
                                    pp.UnpegFilterReason = filterReason.ListToString();
                                }
                            }
                            else
                            {
                                // Demand에 Pegging 가능한 Wip이 있었으나 다른 demand에 pegging 되어서 더 이상 없음(WIP 수량 부족)
                                foreach (var pp in selectedGroup.DemList)
                                {
                                    WriteLog.WritePegValidationDemand_UNPEG(pp, "NOT_ENOUGH_WIP", "");
                                    pp.UnpegReason = "NOT_ENOUGH_WIP";
                                }
                            }

                            // Pegging되지 않은것도 prepeg로그에 찍음
                            WriteLog.WritePrePegLog(selectedGroup, selectedGroup.Sample, null, selectedGroup.Sample.TargetQty, 0, 0, 0, selectedGroup.Sample.TargetQty, phase, seq, 0, false, false, false);
                            
                            // Pegging되지 않으면 Demand Group내 다른 demand도 pegging 할 wip이 없으므로 삭제한다.
                            RemoveTargetDemGroups(selectedGroup, ref i);
                            PrePeg.DemGroups.Remove(selectedGroup);
                        }
                        else if (isPegged == true)
                        {
                            if (selectedGroup.AvailWipList.Count() == 0)
                            {
                                // Demand에 Pegging 가능한 Wip이 있었으나 다른 demand에 pegging 되어서 더 이상 없음(WIP 수량 부족)
                                foreach (var pp in selectedGroup.DemList)
                                {
                                    WriteLog.WritePegValidationDemand_UNPEG(pp, "NOT_ENOUGH_WIP", "");
                                    pp.UnpegReason = "NOT_ENOUGH_WIP";
                                }
                                RemoveTargetDemGroups(selectedGroup, ref i);
                                PrePeg.DemGroups.Remove(selectedGroup);

                            }
                            else if (selectedGroup.DemList.Count() == 0)
                            {
                                // pegging이 완료되어 DemnadGroup을 TargetDemandGroup에서 삭제
                                RemoveTargetDemGroups(selectedGroup, ref i);
                            }
                            else
                            {
                                // 해당 DemandGroup은 Pegging을 계속함
                            }
                        }
                    }

                    if (PrePeg.TargetDemGroups.Count() == 0)
                    {
                        phaseMore = false;
                    }
                }

                phase++;
            }
        }

        public static bool SetTargetDemList(bool isFirstGroup)
        {
            bool hasList = false;
            List<DemandGroup> res = new List<DemandGroup>();

            bool needChange = true;

            if (isFirstGroup)
            {
                //var tmp = PrePeg.DemGroups.Where(r => r.Sample.DueDate == PrePeg.TargetDueDate && r.TargetOperId == PrePeg.TargetOperId).ToList();
                var tmp = PrePeg.DemGroups.Where(r => r.Sample.Priority == PrePeg.TargetPriority && r.TargetOperId == PrePeg.TargetOperId).ToList();
                if (tmp != null && tmp.Count() != 0)
                {
                    res.AddRange(tmp);
                    needChange = false;
                }
            }

            int idx = 0;
            while (needChange && idx < 10000)
            {
                idx++;

                var targetList = PrePeg.DemGroups.Where(x => x.Sample.TargetOperID == PrePeg.TargetOperId);
                if (targetList.Count() > 0)
                {
                    PrePeg.TargetPriority = targetList.OrderBy(x => x.Sample.Priority).First().Sample.Priority;
                }
                else 
                {
                    if (PrePeg.TargetOperId == Constants.TAPING_OPER_ID)
                    {
                        PrePeg.TargetOperId = Constants.OI_OPER_ID;
                        PrePeg.TargetPriority = PrePeg.DemGroups.Where(x => x.Sample.TargetOperID == PrePeg.TargetOperId).First().Sample.Priority;
                    }
                    else if (PrePeg.TargetOperId == Constants.OI_OPER_ID)
                    {
                        break;
                    }
                    else if (PrePeg.TargetOperId == Constants.SORTING_OPER_ID)
                    {
                        break;
                    }
                }

                var tmp = PrePeg.DemGroups.Where(r => r.Sample.Priority == PrePeg.TargetPriority && r.TargetOperId == PrePeg.TargetOperId);
                if (tmp != null && tmp.Count() != 0)
                {
                    res.AddRange(tmp.ToList());
                    needChange = false;
                }
            }

            PrePeg.TargetDemGroups.Clear();

            if (res != null && res.Count() != 0)
            {
                PrePeg.TargetDemGroups.AddRange(res);
                hasList = true;
            }

            return hasList;
        }

        //public static List<SEMPegWipInfo> GetTargetAvailWips(DemandGroup demandGroup)
        //{
        //    List<SEMPegWipInfo> result = new List<SEMPegWipInfo>();

        //    SEMGeneralPegPart pp = demandGroup.Sample;

        //    if(pp.TargetOperID == Constants.TAPING_OPER_ID)
        //    {
        //        var a = PrePeg.AvailWips.Where(x => x.EndCustomerList.Contains(pp.CustomerID));

        //        result.AddRange(a);
        //    }

        //    result.AddRange(demandGroup.AvailWipList);

        //    return result;

        //}

        public static bool FliterPegWip(SEMPegWipInfo pegWip, SEMGeneralPegPart pp, ref HashedSet<string> filterReason)
        {
            bool isFilterWip = false;
            string reason = string.Empty;

            // pegging된 wip filter
            isFilterWip = IsPeggedWip(pegWip, pp);
            if (isFilterWip)
            {
                filterReason.Add("PEGGED_WIP");
                return true;
            }


            // 사용 불가능한 wip filter
            isFilterWip = IsInavailableWip(pegWip, pp);
            if (isFilterWip)
            {
                filterReason.Add("INVAILD_WIP");
                return true;
            }


            // EndCustomer 제약
            isFilterWip = IsEndCustomerFilter(pegWip, pp);
            if (isFilterWip)
            {
                filterReason.Add("NOT_MATCH_END_CUSTOMER_ID");
                return true;
            }

            // 추가 제약은 여기에 추가


            // 자재제약            
            isFilterWip = IsTapingMaterialFilter(pegWip, pp, ref reason);
            if (isFilterWip)
            {
                filterReason.Add(reason);
                return true;
            }

            // IsArrangeFilter()에서 pegging할 wip을 확정하고 Wip의 자재코드를 셋팅하기 때문에 FliterPegWip()의 가장 마지막에 위치해야함
            // Arrange 제약
            if (GlobalParameters.Instance.PegOnlyArranged == true)
            {
                isFilterWip = IsArrangeFilter(pegWip, pp, ref reason);
                if (isFilterWip)
                {
                    filterReason.Add(reason);
                    return true;
                }
            }

            return false;
        }

        public static bool IsPeggedWip(SEMPegWipInfo pegWip, SEMGeneralPegPart pp)
        {
            // 이 조건문은 타지 않으나 오류 방지를 위해 작성
            if (pegWip.IsPegged == true || pegWip.PlanWip.IsPrePegged)
            {
                if (PrePeg.AvailWips.Contains(pegWip) == true)
                    PrePeg.AvailWips.Remove(pegWip);

                WriteLog.WritePegWipFilterLog(pegWip, pp, false,"ETC", "PEGGED_WIP", "");
                pegWip.PlanWip.UnpegReason.Add("PEGGED_WIP");
                return true;
            }

            return false;
        }

        public static bool IsInavailableWip(SEMPegWipInfo pegWip, SEMGeneralPegPart pp)
        {
            // 이 조건문은 타지 않으나 오류 방지를 위해 작성
            if (PrePeg.AvailWips.Contains(pegWip) == false)
            {
                WriteLog.WritePegWipFilterLog(pegWip, pp, false, "ETC", "NOT_AVAIL_WIP", "");
                pegWip.PlanWip.UnpegReason.Add("NOT_AVAIL_WIP");
                return true;
            }

            return false;
        }

        public static bool IsEndCustomerFilter(SEMPegWipInfo pegWip, SEMGeneralPegPart pp)
        {
            if (pp.TargetOperID == Constants.TAPING_OPER_ID && pegWip.WipInfo.IsEndCustomerCheck)
            {
                if (pegWip.EndCustomerList.Contains(pp.EndCustomerID) == false)
                {
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "END_CUSTOMER_ID", "NOT_MATCH_END_CUSTOMER_ID", $"DEMAND:{pp.EndCustomerID} / WIP:{pegWip.EndCustomerList.ListToString()}");
                    pegWip.PlanWip.UnpegReason.Add("NOT_MATCH_END_CUSTOMER_ID");
                    pegWip.PlanWip.UnpegDetailReason.Add($"DEMAND_END_CUSTOMER_ID : {pp.EndCustomerID} / WIP_END_CUSTOMER_ID : {pegWip.EndCustomerList.ListToString()} ");
                    return true;
                }
            }

            return false;
        }

        public static bool IsTapingMaterialFilter(SEMPegWipInfo pegWip, SEMGeneralPegPart pp, ref string reason)
        {
            // Bulk Demand는 자재제약 적용하지 않음
            if (pp.TargetOperID == Constants.OI_OPER_ID)
                return false;

            // SG4090이전 Wip(BulkWip)은 자재제약 적용하지 않음  (4090의 OperSeq가 특정되지않아 이렇게 개발함)
            if (pegWip.WipInfo.InitialSEMStep.StdStep.StepType == "B" && pegWip.WipInfo.InitialSEMStep.StepID != Constants.OI_OPER_ID)
                return false;

            // SG4430 이후의 Wip은 자재제약 적용하지 않음
            if (pegWip.WipInfo.InitialSEMStep.Sequence > InputMart.Instance.TapingProcessingOperSeq[pegWip.FlowProductID])
                return false;

            // SG4430의 RUN재공은 자재제약 적용하지 않음
            if (pegWip.WipInfo.InitialSEMStep.Sequence == InputMart.Instance.TapingProcessingOperSeq[pegWip.FlowProductID]
                && pegWip.WipInfo.WipState == Constants.Run)
                return false;


            // Demand의 Product의 자재 코드 유무 확인
            if (pp.SemProduct.MaterialList.Count == 0)
            {
                // Peg Init때 자재코드없는 demand 모두 걸러서 이 조건문은 타지않음
                WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL", "NO_MATERIAL_CODE", " "); 
                pegWip.PlanWip.UnpegReason.Add("DEMAND_HAS_NO_MATERIAL_CODE");
                reason = "DEMAND_HAS_NO_MATERIAL_CODE";
                //pegWip.PlanWip.UnpegDetailReason.Add($"WIP_END_CUSTOMER_ID : {pegWip.EndCustomerList.ListToString()} / DEMAND_END_CUSTOMER_ID : {pp.EndCustomerID}");
                return true;
            }

            // Wip의 자재코드 유무 확인
            if (pegWip.WipInfo.FromTapingMatCond == null)
            {
                WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL", "NO_WIP_MATERIAL_CODE", "WIP_JOB_CONDITION 참조");
                pegWip.PlanWip.UnpegReason.Add("NO_WIP_MATERIAL_CODE");
                reason = "NO_WIP_MATERIAL_CODE";
                return true;
            }

            // Demand와 Wip의 자재코드 일치 여부
            string currentWipMatCode = pegWip.FromTapingMatCond; // Wip의 현재 자재코드 = from 자재코드
            if (pegWip.WipInfo.InitialStep.StepType == "B" || pegWip.WipInfo.InitialStep.StepType == "W")
            {
                // Bulk, 창고 Wip은 From, To 자재코드 둘다 확인
                if (pp.SemProduct.MaterialList.Where(x => x.Item1 == currentWipMatCode || x.Item2 == currentWipMatCode).Count() == 0)
                {
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL", "NOT_MATCH_FROM_TO_MATERIAL_CODE", $"WIP : {currentWipMatCode} // DEMAND : (TAPING_MATERIAL_ALT 참조 - CHIP_SIZE:{pp.SemProduct.ChipSize} / THICKNESS:{pp.SemProduct.Thickness} / TAPING_TYPE:{pp.SemProduct.TapingType} / CARRIER_TYPE:{pp.SemProduct.CarrierType})");
                    pegWip.PlanWip.UnpegReason.Add("NOT_MATCH_FROM_TO_MATERIAL_CODE");
                    pegWip.PlanWip.UnpegDetailReason.Add($"WIP_MATERIAL_CODE : {currentWipMatCode} / CHIP_SIZE@TAPING_TYPE@THICKNESS@CARRIER_TYPE : {pp.SemProduct.ChipSize}@{pp.SemProduct.TapingType}@{pp.SemProduct.Thickness}@{pp.SemProduct.CarrierType}");
                    reason = "NOT_MATCH_FROM_TO_MATERIAL_CODE";
                    return true;
                }
            }
            else if (pegWip.WipInfo.InitialStep.StepType == "T")
            {
                // Taping Wip은 To 자재코드만 확인
                if (pp.SemProduct.MaterialList.Where(x => x.Item2 == currentWipMatCode).Count() == 0)
                {
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL", "NOT_MATCH_TO_MATERIAL_CODE", $"WIP:{currentWipMatCode} // DEMAND : (TAPING_MATERIAL_ALT 참조 - CHIP_SIZE:{pp.SemProduct.ChipSize}/THICKNESS:{pp.SemProduct.Thickness}/TAPING_TYPE:{pp.SemProduct.TapingType}/CARRIER_TYPE:{pp.SemProduct.CarrierType})");
                    pegWip.PlanWip.UnpegReason.Add("NOT_MATCH_TO_MATERIAL_CODE");
                    pegWip.PlanWip.UnpegDetailReason.Add($"WIP_MATERIAL_CODE : {currentWipMatCode} / CHIP_SIZE@TAPING_TYPE@THICKNESS@CARRIER_TYPE : {pp.SemProduct.ChipSize}@{pp.SemProduct.TapingType}@{pp.SemProduct.Thickness}@{pp.SemProduct.CarrierType}");
                    reason = "NOT_MATCH_TO_MATERIAL_CODE";
                    return true;
                }
            }

            return false;
        }

        public static bool IsArrangeFilter(SEMPegWipInfo pegWip, SEMGeneralPegPart pp, ref string reason)
        {
            // SG4430(TAPING의 ProcessingOper)의 Sequence
            int seq4430;

            // Arrange 제약 적용하지 않는 wip, Arrange를 찾지 않기 때문에 자재코드도 확정하지 않는다.
            if (InputMart.Instance.TapingProcessingOperSeq.TryGetValue(pegWip.FlowProductID, out seq4430))
            {
                // SG4430 이후의 Wip은 Arrange 제약 적용하지 않음
                if (pegWip.WipInfo.InitialSEMStep.Sequence > seq4430)
                    return false;

                // SG4430의 RUN재공은 Arrange 제약 적용하지 않음
                if (pegWip.WipInfo.InitialSEMStep.Sequence == seq4430 && pegWip.WipInfo.WipState == Constants.Run)
                    return false;
            }
            
            // Tapiping Demand에 Pegging되는 경우 Wip의 자재코드 확정
            if (pp.TargetOperID == Constants.TAPING_OPER_ID)
            {
                if(pegWip.FromTapingMatCond == null)
                {
                    pegWip.PlanWip.UnpegReason.Add("NO_WIP_MATERIAL_CODE");
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL_ALT", "NO_WIP_MATERIAL_CODE.", $"TAPING_MATERIAL_ALT 참조 // CHIP_SIZE:{pp.SemProduct.ChipSize}/THICKNESS:{pp.SemProduct.Thickness}/TAPING_TYPE:{pp.SemProduct.TapingType}/CARRIER_TYPE:{pp.SemProduct.CarrierType}");
                    reason = "NO_WIP_MATERIAL_CODE";
                    return true;
                }

                string toTapingMatCond = WipHelper.GetToTapingMatCond(pegWip, pp.SemProduct);
                if (toTapingMatCond == string.Empty)
                {
                    pegWip.PlanWip.UnpegReason.Add("NO_TO_MATERIAL_CODE");
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "TAPING_MATERIAL_ALT", "NO_TO_MATERIAL_CODE", $"TAPING_MATERIAL_ALT 참조 // CHIP_SIZE:{pp.SemProduct.ChipSize}/THICKNESS:{pp.SemProduct.Thickness}/TAPING_TYPE:{pp.SemProduct.TapingType}/CARRIER_TYPE:{pp.SemProduct.CarrierType}");
                    reason = "NO_TO_MATERIAL_CODE";
                    return true;
                }
                pegWip.WipInfo.ToTapingMatCond = toTapingMatCond;
            }

            bool hasArrange = true;


            // processingOper별로 Arrange 유무 확인
            foreach (var processingOper in pegWip.ProcessingOpers)
            {
                // wip을 임시로 흘렸을때 지나온 processing oper와 그 당시 productID
                string operID = processingOper.Key;
                string productID = processingOper.Value.ProductID;
                
                string noArrangeReason = string.Empty;

                int arrCount = ArrangeHelper2.GetArrange(pegWip, operID, productID, ref noArrangeReason, "PEG").Count();
                if (arrCount == 0) // Arrange가 없음
                {
                    // PEG_WIP_FILTER_LOG 작성
                    WriteLog.WritePegWipFilterLog(pegWip, pp, false, "ARRANGE", "NO_ARRANGE", $"OPER_ID : {operID} PRODUCT_ID : {productID}  ARRANGE_LOG 참조 ");

                    if(noArrangeReason == "불가" || noArrangeReason == "All Condition Sets blocked")
                    {
                        // 찐 NoArrange
                        pegWip.PlanWip.UnpegReason.Add("NO_ARRANGE");
                        reason += "NO_ARRANGE";
                    }
                    else
                    {
                        // MasterData 오류
                        if (reason == "MASTER_DATA_NO_ARRANGE")
                            continue;
                        pegWip.PlanWip.UnpegReason.Add("MASTER_DATA_NO_ARRANGE");
                        reason += "MASTER_DATA_NO_ARRANGE";
                    }

                    // UNPEG_HISTORY에 기록될 NO_ARRANGE_REASON
                    pegWip.PlanWip.NoArrangeOpers.Add(operID);

                    // arrange가 없어서 filter 가 확정됐지만 다음 Processing Oper의 Arrnage 유무를 확인하여 로그를 보고싶을 때 셋팅
                    if (InputMart.Instance.GlobalParameters.WriteDetailArrangeLog == false)
                        return true;

                    hasArrange = false;
                }
            }

            if (hasArrange == false)
            {
                // Arrange가 없으면 확정한 자재코드를 다시 되돌린다.
                pegWip.WipInfo.ToTapingMatCond = string.Empty;
                pegWip.FP_I_TAPING_MATERIAL_ALT = null;
                
                // Filter wip
                return true;
            }

            // Not Filter wip
            return false;
        }

        public static bool DoPrePeg(SEMPegWipInfo selectedWip, DemandGroup selectedGroup,
           int phase, int seq, ref int pegSeq)
        {
            #region Set Peg
            bool needRemove = false;
            PrePeg.AvailWips.RemoveWhere(r => r.PlanWip.LotID == selectedWip.PlanWip.LotID);
            selectedWip.IsPrePegged = true;
            selectedGroup.AddPeggedWipList(selectedWip);

            // 수율이 적용된 Pegging 할 수량
            double yieldedQty;

            double yield = selectedWip.AccumYield;
            
            // Yield 적용 (차감된 수량은 PlanWip Qty에 셋팅)
            //if (InputMart.Instance.GlobalParameters.UseYield)
            //{
            //    double yieldGap = selectedWip.PlanWip.Qty - selectedWip.FlowQty;
            //    if (yieldGap > 0)
            //    {
            //        // 수율 관련 Log작성
            //        WriteLog.WritePegValidationWipYiled(selectedWip, selectedGroup.GetSample(selectedWip).DemandID, "YIELD", yieldGap);
            //        WriteLog.WriteWipYieldLog(selectedWip.WipInfo, selectedGroup.GetSample(selectedWip).DemandID, yieldGap);
            //        //WriteLog.WriteUnpegHistory(selectedWip, yieldGap); // 주석 : 수율로 탈락한 lot은 unpegHistory에 기록하지 않음

            //        // Wip수량 깎기
            //        selectedWip.PlanWip.Qty = selectedWip.FlowQty;
            //        selectedWip.WipInfo.SplitLotsRemaninQty -= yieldGap;
            //    }
            //}

            // Peggig 할 수량
            yieldedQty = selectedWip.PlanWip.QtyForPrePeg * yield;

            // MultiLotProductChange의 경우 Peggig 할 수량
            if (selectedWip.IsMultiLotProdChange)
            {
                if (selectedWip.WipInfo.LotSplitDic.Count() == 1)
                {
                    selectedWip.FlowQty = selectedWip.WipInfo.SplitLotsRemaninQty;
                    yieldedQty = selectedWip.WipInfo.SplitLotsRemaninQty; // split lot중 마지막 lot은 잔량을 모두 가져간다.
                }
                else
                {
                    selectedWip.WipInfo.SplitLotsRemaninQty -= selectedWip.FlowQty;
                    yieldedQty = selectedWip.FlowQty;
                }

                // SplitLot이 pegging되면 Dic에서 삭제
                selectedWip.WipInfo.LotSplitDic.Remove(selectedWip.FlowProductID);
            }


            // SpillOverPegging : Wip수량이 Demand 수량보다 많을 때 다음 주차 Demand에도 추가 pegging
            bool isSpillOverPeg = false;

            // OverPegging: Wip수량이 Demand 수량보다 많고 마지막 Demand인 경우 wip 전량을 Pegging
            bool isOverPeg = false;
            #endregion


            // Pegging 가능 수량
            double availPegWipQty = yieldedQty;

            // pegging 할 Wip수량이 0이 될때 까지 demList에 있는 demand들에 pegging
            while (availPegWipQty > 0)
            {                
                // pegging된 수량 초기화
                double pegWipQty = 0;
                double pegDemQty = 0;

                // Demand 선택
                SEMGeneralPegPart selectedPp = selectedGroup.GetSample(selectedWip);

                // Demand 수량
                double targetQty = selectedPp.TargetQty;

                if (Math.Abs((selectedPp.TargetQty - availPegWipQty)) < 1) // Target 수량과 wip수량이 비슷하면 (차이가 1이하)
                {
                    // wip 전체 수량 == demand 수량 천체 pegging
                    pegDemQty = selectedPp.TargetQty;
                    pegWipQty = selectedWip.PlanWip.QtyForPrePeg;

                    availPegWipQty = 0;

                    selectedGroup.DemList.Remove(selectedPp);
                }
                else if (selectedPp.TargetQty > availPegWipQty) // Target 수량보다 wip수량이 적으면 
                {
                    // wip 전체 수량 pegging
                    pegDemQty = availPegWipQty;
                    pegWipQty = selectedWip.PlanWip.QtyForPrePeg;

                    availPegWipQty = 0;
                }
                else if (selectedPp.TargetQty < availPegWipQty) // Target 수량보다 wip수량이 더 많고,
                {
                    if (IsOverPegging(selectedWip, selectedGroup, selectedPp)) // 마지막 demand면, MultiLotProdChange면
                    {
                        // OverPegging
                        pegDemQty = selectedPp.TargetQty;
                        pegWipQty = selectedWip.PlanWip.QtyForPrePeg;

                        availPegWipQty = 0;

                        isOverPeg = true;
                    }
                    else if (selectedGroup.DemList.Count() > 1) // DemList에 Demand가 추가로 더 있으면
                    {
                        pegDemQty = selectedPp.TargetQty;
                        pegWipQty = selectedPp.TargetQty / yield;

                        availPegWipQty -= pegDemQty;
                    }
                    else
                    {
                        availPegWipQty = 0;

                        WriteLog.WriteErrorLog($"PrePeg 로직 오류 LOT_ID : {selectedWip.LotID} DEMAND_ID : {selectedPp.DemandID}");
                    }

                    selectedGroup.DemList.Remove(selectedPp);

                    // target Due에 대한 Demand가 채워졌으므로 더이상 해당 Phase의 대상이 아님
                    needRemove = true; //PrePeg.TargetDemandGroup에서 지움
                }

                // Target 수량, Wip 수량 조정
                selectedPp.TargetQty -= pegDemQty;
                selectedWip.PlanWip.QtyForPrePeg -= pegWipQty;

                // Pegging 수량
                Tuple<double, double> tpPegQty = new Tuple<double, double>(pegDemQty, pegWipQty);

                // Wip과 Demand 정보 매핑
                selectedWip.PlanWip.PegWipInfo = selectedWip;

                selectedPp.PrePeggedWip.Add(selectedWip.PlanWip, new Tuple<double, double>(pegDemQty, pegWipQty));
                if (selectedPp.TargetOperID == Constants.SORTING_OPER_ID)
                    selectedWip.SortingPeggingPart.Add(selectedPp);
                else if (selectedPp.TargetOperID == Constants.OI_OPER_ID)
                    selectedWip.BulkPeggingPart.Add(selectedPp);
                else if (selectedPp.TargetOperID == Constants.TAPING_OPER_ID)
                    selectedWip.TapingPeggingPart.Add(selectedPp);

                pegSeq++;

                // DemandGroup이 Pegging 완료되면 list에서 삭제
                if (selectedGroup.DemList.Count() == 0)
                {
                    PrePeg.DemGroups.Remove(selectedGroup);
                }

                // PrePeg 로그 작성
                WriteLog.WritePrePegLog(selectedGroup, selectedPp, selectedWip, targetQty, pegWipQty, pegDemQty, selectedWip.PlanWip.QtyForPrePeg, selectedPp.TargetQty, phase, seq, pegSeq, true, isSpillOverPeg, isOverPeg);

                // PegValidation 로그 작성
                //double qty = isOverPeg ? targetQty : PeggedQty; // overpegging의 경우 pegging된 wip수량이아닌 demand 수량을 기록하기위함
                WriteLog.WritePegValidationDemand_PEG(selectedPp, selectedWip, pegDemQty);

                // 반복문을 추가로 돌면 SplitOverPeg
                isSpillOverPeg = true;
            }

            return needRemove;
        }

        public static bool IsOverPegging(SEMPegWipInfo selectedWip, DemandGroup demandGroup, SEMGeneralPegPart pp)
        {
            // Demand Group 내 마지막 Demand
            if (demandGroup.DemList.Count() == 1)
                return true;

            // EndCustomer를 고려했을때 Demand Group내 마지막 Demand
            if (selectedWip.WipInfo.IsEndCustomerCheck
                && demandGroup.DemList.Where(x => x.EndCustomerID == pp.EndCustomerID).Count() == 1)
                return true;

            // MultiLotProductChange Lot
            if (selectedWip.WipInfo.IsMultiLotProdChange)
                return true;

            return false;
        }

        public static bool RemoveTargetDemGroups(DemandGroup demGroup, ref int i)
        {
            bool res = false;
            if (PrePeg.TargetDemGroups.Contains(demGroup) == true)
            {
                res = true;
                PrePeg.TargetDemGroups.Remove(demGroup);
                i--;
            }

            return res;
        }

        public static void SetSemOperTarget(SEMPegWipInfo pwi, string operType = "OPER")
        {
            //if (pwi.SemOperTargets.Where(r => r.OperId == pwi.FlowStepID).FirstOrDefault() != null)
            //    return;

            SEMOperTarget sot = new SEMOperTarget();

            sot.LotId = pwi.WipInfo.LotID;
            sot.RouteId = pwi.FlowRouteId;
            sot.OperId = pwi.FlowStepID;
            sot.OperSeq = pwi.FlowStep.Sequence;
            sot.PlanType = operType;
            sot.SEMGeneralStep = pwi.FlowStep;

            sot.ProductId = pwi.FlowProductID;
            sot.SiteId = pwi.SiteID;
            sot.CustomerId = pwi.FlowCustomerID;
            sot.EndCustomerIDs = pwi.EndCustomerList.ListToString();
            
            if(operType == "OPER")
            {
                sot.LeadTime = pwi.FlowTat;

                sot.OperYield = pwi.FlowYield;
                sot.IsSpecialYield = pwi.IsSpecialYield;
                sot.CalcYield = pwi.CalcYield;
                //현재 pwi.FlowQty는 step out 시점의 수량이 저장되어있으므로 in시점의 수량을 저장하기 위해 수율을 나누어줌
                sot.Qty = pwi.FlowQty / pwi.FlowYield; //Math.Ceiling(pwi.FlowQty / pwi.FlowYield);

                sot.TotalTAT = pwi.TotalTat;
                sot.OperTat = pwi.FlowTat;
                sot.CalcTat = pwi.CalcTat;
                sot.CalcTotalTat = pwi.TotalTat - pwi.FlowTat;
            }
            else
            {
                //sot.LeadTime = operType == "BT" && pwi.WipInfo.IsSmallLot ? GlobalParameters.Instance.SmallLotStockLeadTime * 1440 : 0;
                sot.LeadTime = 0;
                sot.OperYield = 1;
                sot.CalcYield = 1;
                //현재 pwi.FlowQty는 step out 시점의 수량이 저장되어있으므로 in시점의 수량을 저장하기 위해 수율을 나누어줌
                sot.Qty = pwi.FlowQty; //Math.Ceiling(pwi.FlowQty / pwi.FlowYield);

                sot.TotalTAT = pwi.TotalTat;
                sot.OperTat = 0;
                sot.CalcTat = 0;
                sot.CalcTotalTat = pwi.TotalTat - pwi.FlowTat;

                if (pwi.FromProdID != string.Empty)
                {
                    sot.FromProdID = pwi.FromProdID;
                    sot.FromCustomerID = pwi.FromCustID;
                    sot.ModelChange = true;
                }

                // From 정보 초기화
                pwi.FromProdID = string.Empty;
                pwi.FromCustID = string.Empty;
            }

            pwi.SemOperTargets.Add(sot);
        }

        public static void OnAfterPrePeg()
        {
            WriteLotLpst();
        }

        public static void WriteLotLpst()
        {
            foreach (var row in InputMart.Instance.LotLpstDic)
            {
                row.Value.QTY = Math.Round(row.Value.QTY, 5);
                row.Value.PEGGING_QTY = Math.Round(row.Value.PEGGING_QTY, 5);

                OutputMart.Instance.LOT_LPST.Add(row.Value);
            }
        }


    }
}