using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace BitfinexConnector.Model
{
    class BitfinexBookUpdateMessage : BookMessage
    {
        public BitfinexBookUpdateMessage(string isin, BitfinexPriceLevel updateLevel)
        {
            Isin = isin;
            if (updateLevel.Side == OrderSide.Buy)
            {
                Bids = new List<PriceLevel>{ updateLevel };
                Asks = new List<PriceLevel>();
            }
            else
            {
                Bids = new List<PriceLevel>();
                Asks = new List<PriceLevel> { updateLevel };
            }
        }
    }
}