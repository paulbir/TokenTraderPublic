using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryUpdateMessage : BookMessage
    {
        public FineryUpdateMessage(string isin)
        {
            Isin     = isin;
            Sequence = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public bool SetPriceLevels(OrderSide side, JArray levelsArray, decimal priceStep, decimal qtyStep)
        {
            var levels = new List<PriceLevel>();

            foreach (JToken levelToken in levelsArray)
            {
                var jLevel = (JArray)levelToken;
                if (jLevel.Count < 3) return false;

                string  action = (string)jLevel[0];
                decimal price  = (decimal)jLevel[1] * priceStep;
                decimal qty    = (decimal)jLevel[2] * qtyStep;

                PriceLevel priceLevel;
                switch (action)
                {
                    case "+":
                    case "M":
                        priceLevel = new FineryPriceLevel(price, qty, PriceLevelApplyMethod.Straight);
                        break;

                    case "-":
                        priceLevel = new FineryPriceLevel(price, 0, PriceLevelApplyMethod.Straight);
                        break;

                    case "~":
                        priceLevel = new FineryPriceLevel(price, qty, PriceLevelApplyMethod.DeleteAheadPrice);
                        break;

                    default: return false;
                }

                levels.Add(priceLevel);
            }

            if (side == OrderSide.Buy) Bids = levels;
            else Asks                       = levels;

            return true;
        }
    }
}