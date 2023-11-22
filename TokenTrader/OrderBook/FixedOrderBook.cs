using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using SharedDataStructures.Messages;

namespace TokenTrader.OrderBook
{
    public class FixedOrderBook<TOrderId> : BaseOrderBook<TOrderId>
    {
        readonly List<Order<TOrderId>> asks;
        readonly List<Order<TOrderId>> bids;
        public readonly int NumLevels;

        public override decimal BestBid => bids[0].Price;
        public override decimal BestAsk => asks[0].Price;
        public override decimal BestBidQty => bids[0].Qty;
        public override decimal BestAskQty => asks[0].Qty;
        public override string BidsString
        {
            get
            {
                List<Order<TOrderId>> bidsToSend = bids.TakeWhile(bid => bid.Qty > 0).ToList();
                return $"{bidsToSend.Count};{string.Join(";", bidsToSend)}";
            }
        }

        public override string AsksString
        {
            get
            {
                List<Order<TOrderId>> asksToSend = asks.TakeWhile(ask => ask.Qty > 0).ToList();
                return $"{asksToSend.Count};{string.Join(";", asksToSend)}";
            }
        }

        public bool BidsUpdated { get; private set; }
        public bool AsksUpdated { get; private set; }

        public FixedOrderBook(int numLevels)
        {
            Contract.Requires(numLevels > 0, "numLevels has to be > 0");

            NumLevels = numLevels;

            bids = new List<Order<TOrderId>>(numLevels);
            for (int i = 0; i < numLevels; i++) bids.Add(new Order<TOrderId>());

            asks = new List<Order<TOrderId>>(numLevels);
            for (int i = 0; i < numLevels; i++) asks.Add(new Order<TOrderId>());
        }

        public void Update(OrderSide side, int row, decimal price, decimal qty)
        {
            Contract.Requires(price >= 0, "price has to be >= 0");
            Contract.Requires(qty >= 0, "qty has to be >= 0");
            Contract.Requires(row >= 0, "row has to be >= 0");
            Contract.Requires(side == OrderSide.Buy && row < NumLevels || side == OrderSide.Sell && row < NumLevels,
                              "row number exceeds total number of rows in order book");

            List<Order<TOrderId>> book = null;
            book = side == OrderSide.Buy ? bids : asks;

            Contract.Assume(book != null);
            book[row].SetAll(price, qty, default);
            TryMarkAsStopLevel(book, row + 1);
        }

        public override decimal GetOneSideVwap(OrderSide side, decimal vwapQty)
        {
            Contract.Requires(vwapQty > 0, "vwapQty has to be > 0");

            List<Order<TOrderId>> book = null;

            if (side == OrderSide.Buy) book = bids;
            else if (side == OrderSide.Sell) book = asks;

            Contract.Assume(book != null);
            decimal vwap = GetOneSideVwapInternal(side, vwapQty, book);

            return Math.Truncate(vwap * TruncCoef) / TruncCoef;
        }

        public void TrySetFalseUpdated(bool bidsUpdated, bool asksUpdated)
        {
            if (!BidsUpdated) BidsUpdated = bidsUpdated;
            if (!AsksUpdated) AsksUpdated = asksUpdated;
        }

        public void ResetUpdatedFlags()
        {
            BidsUpdated = false;
            AsksUpdated = false;
        }

        void TryMarkAsStopLevel(List<Order<TOrderId>> book, int row)
        {
            if (row < NumLevels) book[row].SetAll(0, 0, default);
        }
    }
}