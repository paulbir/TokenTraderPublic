using System;

namespace TokenTrader.State
{
    class UsedAddOrderPriceData
    {
        public decimal Bid       { get; }
        public decimal Ask       { get; }
        public decimal DealShift { get; }

        public override string ToString() => $"undBid={Bid};undAsk={Ask};dS={DealShift}";

        public UsedAddOrderPriceData(decimal bid, decimal ask, decimal dealShift, decimal minStep)
        {
            Bid       = Math.Round(bid / minStep) * minStep;
            Ask       = Math.Round(ask / minStep) * minStep;
            DealShift = Math.Round(dealShift / minStep) * minStep;
        }
    }
}