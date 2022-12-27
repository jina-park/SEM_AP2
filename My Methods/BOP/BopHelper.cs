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
    public static partial class BopHelper
    {
        public static SEMStdStep FindStdStep(string stepID)
        {
            SEMStdStep stdStep = InputMart.Instance.SEMStdStep.Rows.Find(stepID);

            return stdStep;
        }
        public static SEMGeneralStep FindStep(string processID, string stepID)
        {
            SEMProcess proc = BopHelper.FindProcess(processID);

            if (proc != null)
                return proc.FindStep(stepID) as SEMGeneralStep;
            else
                return null;
        }

        public static SEMProcess FindProcess(string processID)
        {
            string key = processID;

            SEMProcess proc = InputMart.Instance.SEMProcessView.FindRows(key).FirstOrDefault();

            return proc;
        }

        internal static void AddWipProduct(SEMProduct semProduct)
        {
            List<SEMProduct> list;
            if (InputMart.Instance.WipProductList.TryGetValue(semProduct.Key, out list) == false)
                InputMart.Instance.WipProductList.Add(semProduct.Key, list = new List<SEMProduct>());

            if (list.Contains(semProduct) == false)
                list.Add(semProduct);
        }

        public static SEMProduct FindProduct(string productID)
        {
            SEMProduct prod;
            InputMart.Instance.SEMProduct.TryGetValue(productID, out prod);

            return prod;
        }



    }
}