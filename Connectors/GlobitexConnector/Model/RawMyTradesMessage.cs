using System.Collections.Generic;

namespace GlobitexConnector.Model
{
    class RawMyTradesMessage
    {
        public List<GlobitexTradeMessage> MyTrades { get; }

        public RawMyTradesMessage(List<GlobitexTradeMessage> trades)
        {
            MyTrades = trades ?? new List<GlobitexTradeMessage>();
        }
    }
}