using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinTickMessage : TickerMessage
    {
        public KucoinTickMessage(string symbol, decimal buy, decimal sell, decimal lastTradedPrice)
        {
            Isin = symbol;
            Bid = buy;
            Ask = sell;
            Last = lastTradedPrice;
        }
    }
}
