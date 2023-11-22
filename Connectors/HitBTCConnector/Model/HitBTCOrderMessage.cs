using System;
using SharedDataStructures.Messages;
using SharedTools;

namespace HitBTCConnector.Model
{
    class HitBTCOrderMessage : OrderMessage
    {
        public string ReportType { get; }

        public HitBTCOrderMessage(string clientOrderId,
                                  string symbol,
                                  string side,
                                  string status,
                                  string quantity,
                                  string price,
                                  DateTime updatedAt,
                                  string reportType,
                                  string tradeQuantity,
                                  string tradeFee)
        {
            OrderId = clientOrderId;
            Isin = symbol;
            Side = side == "buy" ? OrderSide.Buy : OrderSide.Sell;
            Status = status;
            Price = price.ToDecimal();
            Qty = quantity.ToDecimal();
            Timestamp = updatedAt.ToLocalTime();
            ReportType = reportType;
            TradeQty = string.IsNullOrEmpty(tradeQuantity) || string.IsNullOrWhiteSpace(tradeQuantity) ? -1 : tradeQuantity.ToDecimal();
            TradeFee = string.IsNullOrEmpty(tradeFee) || string.IsNullOrWhiteSpace(tradeFee) ? -1 : tradeFee.ToDecimal();
        }
    }
}