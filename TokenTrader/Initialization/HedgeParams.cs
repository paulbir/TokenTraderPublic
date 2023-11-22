using System.Collections.Generic;
using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class HedgeParams
    {
        public string          HedgeWithIsin                       { get; set; }
        public string          HedgeWithPubKey                     { get; set; }
        public decimal         TradeToHedgeCoef                    { get; set; }
        public decimal         HedgeMinQty                         { get; set; }
        public decimal         HedgeMinStep                        { get; set; }
        public decimal         HedgeSlippagePricePerc              { get; set; }
        public bool?           StopOnHedgeCancel                   { get; set; }
        public Dictionary<string, decimal> LimitsToStopOnExposureExceeding { get; set; }

        public void Verify()
        {
            if (string.IsNullOrEmpty(HedgeWithIsin)) throw new ConfigErrorsException("HedgeWithIsin in Hedge was not set properly.");
            if (string.IsNullOrEmpty(HedgeWithPubKey)) throw new ConfigErrorsException("HedgeWithPubKey in Hedge was not set properly.");
            if (TradeToHedgeCoef       <= 0) throw new ConfigErrorsException("TradeToHedgeCoef in Hedge was not set properly.");
            if (HedgeMinQty            <= 0) throw new ConfigErrorsException("HedgeMinQty in Hedge was not set properly.");
            if (HedgeMinStep           <= 0) throw new ConfigErrorsException("HedgeMinStep in Hedge was not set properly.");
            if (HedgeSlippagePricePerc <= 0) throw new ConfigErrorsException("HedgeSlippagePricePerc in Hedge was not set properly.");
            if (StopOnHedgeCancel      == null) throw new ConfigErrorsException("StopOnHedgeCancel in Hedge was not set properly.");
            if (LimitsToStopOnExposureExceeding == null)
                throw new ConfigErrorsException("LimitsToStopOnExposureExceeding in Hedge was not set properly.");
            foreach (decimal exposureLimitTolerance in LimitsToStopOnExposureExceeding.Values)
            {
                if (exposureLimitTolerance < 0)
                    throw new ConfigErrorsException("exposureLimitTolerance in CurrenciesToStopOnExposureExceeding was not set properly.");
            }
        }
    }
}