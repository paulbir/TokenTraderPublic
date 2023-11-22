using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class WoortonBalanceMessage : BalanceMessage
    {
        public WoortonBalanceMessage(string currency, decimal available)
        {
            Currency  = currency;
            Available = available;
            Reserved  = 0;
        }
    }
}