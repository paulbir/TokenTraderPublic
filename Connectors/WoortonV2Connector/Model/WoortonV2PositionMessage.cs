using SharedDataStructures.Messages;

namespace WoortonV2Connector.Model
{
    class WoortonV2PositionMessage : PositionMessage
    {
        public WoortonV2PositionMessage(string currency_id, decimal amount)
        {
            Isin = currency_id;
            Qty  = amount;
        }
    }
}