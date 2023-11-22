using System;
using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanOrderMessage : OrderMessage
    {
        public string ExchangeOrderId    { get; }
        public string TradingCurrency    { get; }
        public string SettlementCurrency { get; }

        public OceanOrderMessage(string   orderID,
                                 string   trading,
                                 string   settlement,
                                 string   side,
                                 decimal  size,
                                 decimal  price,
                                 decimal  filled,
                                 string   status,
                                 DateTime createdAt)
        {
            ExchangeOrderId    = orderID;
            TradingCurrency    = trading;
            SettlementCurrency = settlement;
            Side               = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Qty                = size;
            Price              = price;
            TradeQty           = filled;
            Status             = status;
            Timestamp          = createdAt;
        }

        public void SetClientOrderId(string clientOrderId)
        {
            OrderId = clientOrderId;
        }

        public void SetIsin(char isinSplitChar)
        {
            Isin = $"{TradingCurrency}{isinSplitChar}{SettlementCurrency}";
        }
    }
}