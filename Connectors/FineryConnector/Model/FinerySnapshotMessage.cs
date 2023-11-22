using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FinerySnapshotMessage : BookMessage
    {
        public FinerySnapshotMessage(string isin)
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
                if (jLevel.Count < 2) return false;

                decimal price  = (decimal)jLevel[0] * priceStep;
                decimal qty    = (decimal)jLevel[1] * qtyStep;

                PriceLevel priceLevel = new FineryPriceLevel(price, qty, PriceLevelApplyMethod.Straight);

                levels.Add(priceLevel);
            }

            if (side == OrderSide.Buy) Bids = levels;
            else Asks                       = levels;

            return true;
        }
    }
}