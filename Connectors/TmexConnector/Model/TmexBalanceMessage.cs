using SharedDataStructures.Messages;

namespace TmexConnector.Model
{
    class TmexBalanceMessage : BalanceMessage
    {
        public TmexBalanceMessage(string currency, decimal available, decimal reserved)
        {
            Currency = currency;
            Available = available;
            Reserved = reserved;
        }
    }
}