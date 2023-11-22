using SharedDataStructures.Messages;

namespace TmexConnector.Model
{
    class TmexPositionMessage : PositionMessage
    {
        public TmexPositionMessage(string isin, decimal qty)
        {
            Isin = isin;
            Qty = qty;
        }
    }
}