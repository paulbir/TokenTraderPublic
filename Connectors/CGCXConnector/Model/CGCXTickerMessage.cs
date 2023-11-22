using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXTickerMessage : TickerMessage
    {
        public int IsinId { get; }
        public DateTime EndTimestamp { get; }

        public CGCXTickerMessage(List<decimal> values)
        {
            if (values == null || values.Count < 9)
            {
                Bid = 0;
                Ask = 0;
                Last = 0;
                return;
            }

            IsinId = (int)values[8];
            Bid = values[6];
            Ask = values[7];
            Last = values[4];
            EndTimestamp = DateTimeOffset.FromUnixTimeMilliseconds((long)values[0]).UtcDateTime;
        }

        public CGCXTickerMessage(string isin, decimal bid, decimal ask, decimal last)
        {
            Isin = isin;
            Bid = bid;
            Ask = ask;
            Last = last;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}