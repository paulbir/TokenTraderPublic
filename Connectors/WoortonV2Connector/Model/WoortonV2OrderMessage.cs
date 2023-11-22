using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2OrderMessage : OrderMessage
    {
        public List<WoortonV2Trade> Trades { get; }

        public WoortonV2OrderMessage(DateTime created, string status, List<WoortonV2Trade> trades)
        {
            Timestamp = created;
            Status    = status;
            Trades    = trades ?? new List<WoortonV2Trade>();
        }

        public void FillRestFields(string orderId, string isin, OrderSide side, decimal price, decimal qty)
        {
            OrderId = orderId;
            Isin    = isin;
            Side    = side;
            Price   = price;
            Qty     = qty;

            foreach (WoortonV2Trade trade in Trades) trade.SetOrderFields(isin, orderId, Timestamp, qty);
        }
    }
}