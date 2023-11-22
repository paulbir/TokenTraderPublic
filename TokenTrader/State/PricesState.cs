using System;
using SharedDataStructures.Messages;
using TokenTrader.OrderBook;

namespace TokenTrader.State
{
    class PricesState
    {
        readonly decimal maxSpreadPerc;
        DateTime         lastUpdateTimestamp = DateTime.MinValue;

        public UnlimitedOrderBook<long> Book                       { get; }
        public decimal                  LastPrice                  { get; set; }
        public DateTime                 BookBrokenStartedTimestamp { get; private set; } = DateTime.MaxValue;
        public DateTime                 BookStuckStartedTimestamp  { get; private set; } = DateTime.MaxValue;
        public bool                     CheckBookCross             { get; }

        public bool ArePricesReady
        {
            get
            {
                decimal bid    = Book.BestBid;
                decimal ask    = Book.BestAsk;
                decimal mid    = (bid + ask) / 2;
                decimal spread = Math.Abs(ask - bid);

                bool isCrossCheckPassed = !CheckBookCross || Book.BestAsk > Book.BestBid;
                return isCrossCheckPassed && bid > 0 && ask > 0 && spread / mid * 100 <= maxSpreadPerc;
            }
        }

        public PricesState(decimal maxSpreadPerc, int numLevelsToSend, int bookErrorQueueWindowMs, bool checkBookCross, decimal defaultValue = decimal.MinValue)
        {
            this.maxSpreadPerc = maxSpreadPerc;
            CheckBookCross     = checkBookCross;
            Book               = new UnlimitedOrderBook<long>(numLevelsToSend, true, bookErrorQueueWindowMs);

            if (defaultValue == decimal.MinValue) return;

            Book.Insert(OrderSide.Buy,  defaultValue - defaultValue / 10, 1, 1);
            Book.Insert(OrderSide.Sell, defaultValue + defaultValue / 10, 1, 2);

            LastPrice = defaultValue;
        }

        public bool IsIntervalSecondsPassed(int intervalSeconds) => (DateTime.UtcNow - lastUpdateTimestamp).TotalMilliseconds > intervalSeconds;

        public override string ToString() => $"bidP-Q={Book.BestBid}-{Book.BestBidQty};askP-Q={Book.BestAsk}-{Book.BestAskQty}";

        public void SetBookBrokenStartedTimestamp()
        {
            if (BookBrokenStartedTimestamp == DateTime.MaxValue) BookBrokenStartedTimestamp = DateTime.Now;
        }

        public void ResetBookBrokenStartedTimestamp()
        {
            BookBrokenStartedTimestamp = DateTime.MaxValue;
        }

        public void UpdateLastTimestamp()
        {
            lastUpdateTimestamp = DateTime.UtcNow;
        }

        public void SetBookStuckStartedTimestamp()
        {
            if (BookStuckStartedTimestamp == DateTime.MaxValue) BookStuckStartedTimestamp = DateTime.Now;
        }

        public void ResetBookStuckStartedTimestamp()
        {
            BookStuckStartedTimestamp = DateTime.MaxValue;
        }
    }
}