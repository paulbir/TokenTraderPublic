using System;
using System.Collections.Generic;
using System.Text;

namespace SharedDataStructures.Messages
{
    public class TickerMessage
    {
        public string Isin { get; protected set; }
        public decimal Bid { get; protected set; }
        public decimal Ask { get; protected set; }
        public decimal Last { get; protected set; }
    }
}
