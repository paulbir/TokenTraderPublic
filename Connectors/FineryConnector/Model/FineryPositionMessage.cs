using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryPositionMessage : PositionMessage
    {
        public FineryPositionMessage(string currency, decimal qty)
        {
            Isin = currency;
            Qty  = qty;
        }

        public void AddQty(decimal qty)
        {
            Qty += qty;
        }
    }
}