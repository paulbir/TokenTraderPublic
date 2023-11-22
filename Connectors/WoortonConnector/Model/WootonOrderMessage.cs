using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WootonOrderMessage : OrderMessage
    {
        public List<RawRFQError> Errors          { get; }
        public string            FlattenedErrors => string.Join(';', Errors);

        public WootonOrderMessage(string            client_request_id,
                                  decimal           amount,
                                  decimal?          price,
                                  string            direction,
                                  string            instrument,
                                  string            state,
                                  DateTime          created_at,
                                  List<RawRFQError> errors)
        {
            OrderId   = client_request_id;
            Isin      = instrument;
            Side      = direction == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status    = state;
            Price     = price ?? -1;
            Qty       = amount;
            Timestamp = created_at;
            TradeQty  = amount;
            TradeFee  = 0;
            Errors    = errors;
        }

        public void UpdatePrice(decimal price) => Price = price;
    }
}