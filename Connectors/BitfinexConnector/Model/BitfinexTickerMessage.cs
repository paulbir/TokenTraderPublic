using SharedDataStructures.Messages;

namespace BitfinexConnector.Model
{
    class BitfinexTickerMessage : TickerMessage
    {
        public BitfinexTickerMessage(decimal bid, decimal ask, decimal last)
        {
            Bid = bid;
            Ask = ask;
            Last = last;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}