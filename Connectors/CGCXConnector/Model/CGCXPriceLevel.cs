using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace CGCXConnector.Model
{
    class CGCXPriceLevel : PriceLevel
    {
        public OrderSide Side { get; }
        public long SequenceNumber { get; }
        public int IsinId { get; }
        public decimal Last { get; }

        public CGCXPriceLevel(List<decimal> values)
        {
            //либо если неправильно распарсилось, либо если Side не Buy и не Sell
            if (values == null || values.Count < 10 || values[9] > 1)
            {
                Price = 0;
                return;
            }

            SequenceNumber = (long)values[0];
            IsinId = (int)values[7];
            Side = values[9] == 0 ? OrderSide.Buy : OrderSide.Sell;
            Price = values[6];
            Qty = values[3] == 2 ? 0 : values[8]; //если Delete, то Qty = 0

            Last = values[4];
        }
    }
}