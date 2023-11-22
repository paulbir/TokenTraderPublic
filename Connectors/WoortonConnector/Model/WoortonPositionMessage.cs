using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WoortonPositionMessage : PositionMessage
    {
        public WoortonPositionMessage(string isin, decimal qty)
        {
            Isin = isin;
            Qty  = qty;
        }
    }
}