using System;
using System.Globalization;
using SharedDataStructures.Messages;
using SharedTools;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxTradeMessage
    {
        
        readonly int intSide;
        public string Isin { get; }
        public DateTime Timestamp { get; }
        public OrderSide Side { get; }
        public decimal Price { get; }
        public decimal Qty { get; }

        public string StringHash => $"{Isin};{Price};{Qty}";

        public IDaxTradeMessage(string pairName, string time, int orderSide, decimal price, decimal qty)
        {
            Isin = pairName;
            Timestamp = DateTime.Parse(time);
            intSide = orderSide;
            Side = orderSide == 1 ? OrderSide.Buy : OrderSide.Sell;
            Price = price.Normalize();
            Qty = qty.Normalize();
        }


        public IDaxTradeMessage CreateOppositeSideTradeMessage()
        {
            int oppositeSide = Side == OrderSide.Buy ? 2 : 1;
            return new IDaxTradeMessage(Isin, Timestamp.ToString(CultureInfo.InvariantCulture), oppositeSide, Price, Qty);
        }

        public override string ToString()
        {
            return $"{Isin};{intSide};{Price};{Qty};{Timestamp}";
        }
    }
}