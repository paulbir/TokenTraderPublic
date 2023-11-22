using SharedDataStructures.Messages;

namespace DummyConnector.Model
{
    class DummyTickerMessage : TickerMessage
    {
        public DummyTickerMessage(decimal mid, string isin)
        {
            Isin = isin;
            Bid = mid - 1;
            Ask = mid + 1;
        }
    }
}