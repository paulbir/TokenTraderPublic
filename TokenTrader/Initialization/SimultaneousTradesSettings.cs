using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Exceptions;
using TokenTrader.Interfaces;

namespace TokenTrader.Initialization
{
    class SimultaneousTradesSettings : ITradeModelSettings
    {
        public decimal                            MaxSpreadToChooseMidPricePerc      { get; set; }
        public int                                MinBasePriceOffsetFromBestMinsteps { get; set; }
        public int                                MaxTradeDelaySec                   { get; set; }
        public bool?                              HoldBuyOrder                       { get; set; }
        public bool?                              HoldSellOrder                      { get; set; }
        public bool?                              SafeBuySideMode                    { get; set; }
        public bool?                              UseVWAP                            { get; set; }
        public bool?                              EnableSlowDelayMode                { get; set; }
        public bool?                              EnableHighVolumeMode               { get; set; }
        public decimal                            BaseQtySigmaFrac                   { get; set; }
        public bool?                              SlowDownOnNarrowSpread             { get; set; }
        public List<SimultaneousTradesIsinParams> IsinsToTrade                       { get; set; }

        public decimal      MaxSpreadForReadyPricesPerc { get; set; }
        public bool?        IsMarginMarket              { get; set; }
        public List<string> NoBookCrossCheckVariables   { get; set; }
        public int          StopOnStuckBookTimeoutSec   { get; set; }

        public void Verify()
        {
            if (MaxSpreadToChooseMidPricePerc      <= 0) throw new ConfigErrorsException("MaxSpreadToChooseMidPricePerc was not set properly.");
            if (MinBasePriceOffsetFromBestMinsteps <= 0) throw new ConfigErrorsException("MinBasePriceOffsetFromBestMinsteps was not set properly.");
            if (MaxTradeDelaySec                   <= 0) throw new ConfigErrorsException("MaxTradeDelaySec was not set properly.");
            if (HoldBuyOrder                       == null) throw new ConfigErrorsException("HoldBuyOrder was not set properly.");
            if (HoldSellOrder                      == null) throw new ConfigErrorsException("HoldSellOrder was not set properly.");
            if (SafeBuySideMode                    == null) throw new ConfigErrorsException("SafeBuySideMode was not set properly.");
            if (UseVWAP                            == null) throw new ConfigErrorsException("UseVWAP was not set properly.");
            if (EnableSlowDelayMode                == null) throw new ConfigErrorsException("EnableSlowDelayMode was not set properly.");
            if (EnableHighVolumeMode               == null) throw new ConfigErrorsException("EnableHighVolumeMode was not set properly.");
            if (BaseQtySigmaFrac                   <= 0) throw new ConfigErrorsException("BaseQtySigmaFrac was not set properly.");
            if (SlowDownOnNarrowSpread             == null) throw new ConfigErrorsException("SlowDownOnNarrowSpread was not set properly.");

            if (MaxSpreadForReadyPricesPerc <= 0) throw new ConfigErrorsException("MaxSpreadForReadyPricesPerc was not set properly.");
            if (IsMarginMarket              == null) throw new ConfigErrorsException("IsMarginMarket was not set properly.");
            if (NoBookCrossCheckVariables == null || NoBookCrossCheckVariables.Any(string.IsNullOrEmpty))
                throw new ConfigErrorsException("NoBookCrossCheckVariables was not set properly.");
            if (StopOnStuckBookTimeoutSec <= 0) throw new ConfigErrorsException("StopOnStuckBookTimeoutSec was not set properly.");  
            if (IsinsToTrade == null || IsinsToTrade.Count == 0) throw new ConfigErrorsException("IsinsToTrade was not set properly.");
            foreach (SimultaneousTradesIsinParams isinToTrade in IsinsToTrade) isinToTrade.Verify();
        }

        public void SetDerivativeSettings() { }
    }
}