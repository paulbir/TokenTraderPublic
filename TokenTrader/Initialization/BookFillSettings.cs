using System.Collections.Generic;
using System.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using TokenTrader.Interfaces;

namespace TokenTrader.Initialization
{
    class BookFillSettings : ITradeModelSettings
    {
        public decimal                  MaxSpreadForReadyPricesPerc { get; set; }
        public bool?                    IsMarginMarket              { get; set; }
        public List<string>             NoBookCrossCheckVariables   { get; set; }
        public int                      StopOnStuckBookTimeoutSec   { get; set; }
        public List<BookFillIsinParams> IsinsToTrade                { get; set; }
        public Dictionary<string, string> VariableSubstMap //костыль. не знаю как сразу создать map
        {
            get => null;
            set => VariableSubstRealMap = new ConcurrentMap<string, string>(value);
        }
        public ConcurrentMap<string, string> VariableSubstRealMap { get; private set; }
        public bool?                         UseUdp               { get; set; }
        public int                           UDPListenPort        { get; set; }
        public int                           UDPSendPort          { get; set; }
        public bool?                         CheckPricesMatch     { get; set; }
        public string                        InstanceName         { get; set; }

        public void Verify()
        {
            if (MaxSpreadForReadyPricesPerc <= 0) throw new ConfigErrorsException("MaxSpreadForReadyPricesPerc was not set properly.");
            if (IsMarginMarket == null) throw new ConfigErrorsException("IsMarginMarket was not set properly.");
            if (NoBookCrossCheckVariables == null || NoBookCrossCheckVariables.Any(string.IsNullOrEmpty))
                throw new ConfigErrorsException("NoBookCrossCheckVariables was not set properly.");
            if (StopOnStuckBookTimeoutSec <= 0) throw new ConfigErrorsException("StopOnStuckBookTimeoutSec was not set properly.");            
            if (IsinsToTrade          == null || IsinsToTrade.Count == 0) throw new ConfigErrorsException("IsinsToTrade was not set properly.");
            if (VariableSubstRealMap == null) throw new ConfigErrorsException("VariableSubstMap was not set properly.");
            foreach (BookFillIsinParams isinToTrade in IsinsToTrade) isinToTrade.Verify();
            if (UseUdp           == null) throw new ConfigErrorsException("UseUdp was not set properly.");
            if (UDPListenPort    <= 255) throw new ConfigErrorsException("UDPListenPort was not set properly.");
            if (UDPSendPort    <= 255) throw new ConfigErrorsException("UDPSendPort was not set properly.");
            if (CheckPricesMatch == null) throw new ConfigErrorsException("CheckPricesMatch was not set properly.");
            if (string.IsNullOrEmpty(InstanceName)) throw new ConfigErrorsException("InstanceName was not set properly.");
        }

        public void SetDerivativeSettings()
        {
            foreach (BookFillIsinParams isinToTrade in IsinsToTrade) isinToTrade.SetDerivativeSettings();
        }
    }
}