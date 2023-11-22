using SharedDataStructures.Messages;

namespace BitMEXConnector.Model
{
    class BitMEXTickerMessage : TickerMessage
    {
        public BitMEXTickerMessage(string symbol, decimal lastPrice, decimal bidPrice, decimal askPrice)
        {
            Isin = symbol;
            Bid = bidPrice;
            Ask = askPrice;
            Last = lastPrice;
        }
    }
}