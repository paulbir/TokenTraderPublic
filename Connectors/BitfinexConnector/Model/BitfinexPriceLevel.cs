using System;
using SharedDataStructures.Messages;

namespace BitfinexConnector.Model
{
    class BitfinexPriceLevel : PriceLevel
    {
        public OrderSide Side { get; set; }

        public BitfinexPriceLevel(decimal price, int count, decimal qty)
        {
            Price = price;
            Qty = count == 0 ? 0 : Math.Abs(qty);
            Side = qty > 0 ? OrderSide.Buy : OrderSide.Sell;
        }
    }
}