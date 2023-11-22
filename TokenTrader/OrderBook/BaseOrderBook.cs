using System.Collections.Generic;
using SharedDataStructures.Messages;
using TokenTrader.DataStructures;

namespace TokenTrader.OrderBook
{
    public abstract class BaseOrderBook<TOrderId>
    {
        protected const decimal TruncCoef = 10 * 1000 * 1000 * 1000m;

        public abstract decimal BestBid { get; }
        public abstract decimal BestAsk { get; }
        public abstract decimal BestBidQty { get; }
        public abstract decimal BestAskQty { get; }
        public abstract string BidsString { get; }
        public abstract string AsksString { get; }

        public decimal PrevSentBestAsk { get; private set; }
        public decimal PrevSentBestBid { get; private set; }
        public decimal PrevSentVwapAsk { get; private set; }
        public decimal PrevSentVwapBid { get; private set; }
        public string PrevSentBookStr { get; private set; } = "";

        public abstract decimal GetOneSideVwap(OrderSide side, decimal vwapQty);

        public TimeCircularQueue<BookErrors> ErrorsTimeQ { get; protected set; } 

        public void SetPrevSentBest()
        {
            PrevSentBestBid = BestBid;
            PrevSentBestAsk = BestAsk;
        }

        public void SetPrevSentVwap(decimal prevSentVwapBid, decimal prevSentVwapAsk)
        {
            PrevSentVwapBid = prevSentVwapBid;
            PrevSentVwapAsk = prevSentVwapAsk;
        }

        public void SetPrevSentBookStr(string prevSentBookStr)
        {
            PrevSentBookStr = prevSentBookStr;
        }

        protected void BaseClear()
        {
            PrevSentBestAsk = 0;
            PrevSentBestBid = 0;
            PrevSentVwapAsk = 0;
            PrevSentVwapBid = 0;
            PrevSentBookStr = "";
        }

        protected decimal GetOneSideVwapInternal(OrderSide side, decimal vwapQty, IEnumerable<Order<TOrderId>> book)
        {
            decimal sumQty = 0;
            decimal volume = 0;

            foreach (Order<TOrderId> order in book)
            {
                if (sumQty >= vwapQty) break;

                decimal curQty   = order.Qty;
                decimal curPrice = order.Price;

                if (sumQty + curQty > vwapQty) curQty = vwapQty - sumQty;

                volume += curPrice * curQty;
                sumQty += curQty;
            }

            return sumQty == 0 ? 0 : volume / sumQty;
        }
    }
}