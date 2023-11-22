using System;
using SharedDataStructures.Messages;
using SharedTools;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxOrderMessage : OrderMessage
    {
        //readonly int intSide;

        public string StringHash => $"{Isin};{Price};{Qty}";

        public IDaxOrderMessage(string orderId, int orderSide, string pairName, decimal price, decimal total, decimal filledQty, string time)
        {
            OrderId = orderId;
            Isin = pairName;
            //intSide = orderSide;
            Side = orderSide == 1 ? OrderSide.Buy : OrderSide.Sell;
            Status = "active";
            Price = price.Normalize();
            Qty = total.Normalize();
            Timestamp = DateTime.Parse(time);
            TradeQty = filledQty <= 0 ? 0 : filledQty;
            TradeFee = -0.002m * price * total;
        }

        public void IncreaseTradeQty(decimal qty)
        {
            TradeQty += qty;
        }
    }
}