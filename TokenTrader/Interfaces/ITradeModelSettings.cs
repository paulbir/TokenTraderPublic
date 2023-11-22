using System.Collections.Generic;

namespace TokenTrader.Interfaces
{
    interface ITradeModelSettings
    {
        decimal      MaxSpreadForReadyPricesPerc { get; set; }
        bool?        IsMarginMarket              { get; set; }
        List<string> NoBookCrossCheckVariables   { get; set; }
        void         Verify();
        void         SetDerivativeSettings();
    }
}