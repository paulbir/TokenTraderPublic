using SharedDataStructures.Messages;

namespace BinanceConnector.Model
{
    class BinanceTickerMessage : TickerMessage
    {
        public BinanceTickerMessage(string s, decimal b, decimal B, decimal a, decimal A, decimal c, decimal C)
        {
            Isin = s.ToLowerInvariant();
            Bid = b;
            Ask = a;
            Last = c;
        }
    }
}