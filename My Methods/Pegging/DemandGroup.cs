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
    public class DemandGroup
    {
        public string Key { get; set; } /// Site @ Product @ customer @ TargetOperId
        public string SiteId { get; set; }
        public string ProductId { get; set; }
        public string CustomerId { get; set; }
        public string TargetOperId { get; set; }
        public HashedSet<SEMGeneralPegPart> InitDemandList { get; set; } //정적(초기)
        public List<SEMGeneralPegPart> DemList { get; set; } // 동적
        //public List<SEMPegWipInfo> AvailWipList { get; set; } // 동적
        public List<SEMPegWipInfo> AvailWipList
        {
            get
            {
                return PrePeg.AvailWips.Where(r => r.DemKey == this.Key).ToList();
                //return PrePeg.AvailWips.Where(r => r.DemKeyList.Contains(this.Key)).ToList();
            }
        } // 동적
        public HashedSet<SEMPegWipInfo> PeggedWipList { get; set; } // 동적
        public HashedSet<SEMPegWipInfo> LinkPeggedWipList { get; set; } // TapingDemand에 Pegging되면서 Bulk로 연동된 Lot List
        public bool IsTargetDem { get; set; }

        public double SampleQty
        {
            get
            {
                return this.DemList.Count() > 0 ? this.DemList[0].TargetQty : 0;
            }
        }

        public double ToTalQty
        {
            get
            {
                return this.DemList.Sum(r => r.TargetQty);
            }
        }

        public SEMGeneralPegPart Sample
        {
            get
            {
                return this.DemList.Count() > 0 ? this.DemList[0] : null;
            }
        }

        public  SEMGeneralPegPart GetSample(SEMPegWipInfo pwi)
        {
            if (pwi.WipInfo.IsEndCustomerCheck)
            {
                foreach (var pp in this.DemList)
                {
                    if (pwi.EndCustomerList.Contains(pp.EndCustomerID))
                        return pp;
                }
            }
            else
            {
                return this.Sample;
            }

            return null;
        }

        public DemandGroup(SEMGeneralPegPart semPp)
        {
            string siteId = semPp.SiteID;
            string prodId = semPp.Product.ProductID;
            string custId = semPp.CustomerID;
            string targetOperId = semPp.TargetOperID;

            this.Key = CommonHelper.CreateKey(prodId, custId, targetOperId);
            //this.Key = CommonHelper.CreateKey(prodId, semPp.DemandCustomerID, targetOperId);
            this.SiteId = siteId;
            this.ProductId = prodId;
            this.CustomerId = custId;
            this.TargetOperId = targetOperId;

            this.InitDemandList = new HashedSet<SEMGeneralPegPart>();
            this.InitDemandList.Add(semPp);

            this.DemList = new List<SEMGeneralPegPart>();
            this.PeggedWipList = new HashedSet<SEMPegWipInfo>();
            this.LinkPeggedWipList = new HashedSet<SEMPegWipInfo>();
            this.IsTargetDem = true;
        }

        public void AddDemandList(SEMGeneralPegPart semPp)
        {
            if (this.InitDemandList.Contains(semPp) == false)
                this.InitDemandList.Add(semPp);

            //this.InitDemandList = new HashedSet<SEMBCPPegPart>(this.InitDemandList.ToList().OrderBy(r => r.DueDate));
        }

        public void AddPeggedWipList(SEMPegWipInfo pwi)
        {
            if (this.PeggedWipList.Contains(pwi) == false)
                this.PeggedWipList.Add(pwi);
        }

        public void AddLinkPeggedWipList(SEMPegWipInfo pwi)
        {
            if (this.LinkPeggedWipList.Contains(pwi) == false)
                this.LinkPeggedWipList.Add(pwi);
        }
    }
}