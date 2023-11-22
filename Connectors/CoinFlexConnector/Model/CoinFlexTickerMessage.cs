using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace CoinFlexConnector.Model
{
    class CoinFlexTickerMessage : TickerMessage
    {
        public CoinFlexTickerMessage(decimal bid, decimal ask, decimal last, string isin)
        {
            Bid = bid;
            Ask = ask;
            Last = last;
            Isin = isin;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }

        public void UpdateBid(decimal bid) => Bid = bid;
        public void UpdateAsk(decimal ask) => Ask = ask;
        public void UpdateLast(decimal last) => Last = last;

        public TickerMessage MakeDeepCopy() => new CoinFlexTickerMessage(Bid, Ask, Last, Isin);

        public void SetPriceQty(decimal scale)
        {
            Bid /= scale;
            Ask /= scale;
            Last /= scale;
        }
    }
}