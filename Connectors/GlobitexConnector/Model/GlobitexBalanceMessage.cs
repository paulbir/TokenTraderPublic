using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class GlobitexBalanceMessage : BalanceMessage
    {
        public GlobitexBalanceMessage(string currency, decimal available, decimal reserved)
        {
            Currency = currency;
            Available = available;
            Reserved = reserved;
        }
    }
}