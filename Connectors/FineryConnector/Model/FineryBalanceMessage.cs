using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryBalanceMessage : BalanceMessage
    {
        public FineryBalanceMessage(string currency, decimal available, decimal reserved)
        {
            Currency  = currency;
            Available = available;
            Reserved  = reserved;
        }

        public void Update(decimal available, decimal reserved)
        {
            Available = available;
            Reserved = reserved;
        }
    }
}