using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexBookMessage : BookMessage
    {
        public bool IsEmpty => Bids.Count == 0 && Asks.Count == 0;

        public CoinFlexBookMessage(string isin)
        {
            Isin = isin;
            Bids = new List<PriceLevel>();
            Asks = new List<PriceLevel>();
        }

        public CoinFlexBookMessage(string isin, List<RawOrderUpdate> levels, decimal scale)
        {
            Isin     = isin;
            Sequence = DateTimeOffset.UtcNow.Ticks;
            Bids     = new List<PriceLevel>();
            Asks     = new List<PriceLevel>();

            foreach (RawOrderUpdate level in levels)
            {
                if (level.QtyUnscaled > 0) Bids.Add(new CoinFlexPriceLevel(level.PriceUnscaled, level.QtyUnscaled, scale, true));
                else Asks.Add(new CoinFlexPriceLevel(level.PriceUnscaled, level.QtyUnscaled, scale, true));
            }
        }

        public CoinFlexBookMessage(string isin, OrderSide side, CoinFlexPriceLevel level)
        {
            Isin     = isin;
            Sequence = DateTimeOffset.UtcNow.Ticks;

            if (side == OrderSide.Buy)
            {
                Bids = new List<PriceLevel> {level};
                Asks = new List<PriceLevel>();
            }
            else
            {
                Bids = new List<PriceLevel>();
                Asks = new List<PriceLevel> {level};
            }
        }

        CoinFlexBookMessage(CoinFlexBookMessage other)
        {
            Isin = other.Isin;
            Sequence = other.Sequence;
            Bids = new List<PriceLevel>(other.Bids);
            Asks = new List<PriceLevel>(other.Asks);
        }

        public void AccumulateUpdate(OrderSide side, CoinFlexPriceLevel level)
        {
            Sequence = DateTimeOffset.UtcNow.Ticks;

            if (side == OrderSide.Buy) Bids.Add(level);
            else Asks.Add(level);
        }

        public void AccumulateUpdates(CoinFlexBookMessage update)
        {
            Sequence = DateTimeOffset.UtcNow.Ticks;

            Bids.AddRange(update.Bids);
            Asks.AddRange(update.Asks);
        }

        public void Clear()
        {
            Bids.Clear();
            Asks.Clear();
        }

        public CoinFlexBookMessage CreateDeepCopy() => new CoinFlexBookMessage(this);
    }
}