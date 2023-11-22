using SharedDataStructures.Messages;

namespace DutyFlyConnector.Model
{
    class DutyFlyBalanceMessage : BalanceMessage
    {
        public DutyFlyBalanceMessage(string currency, decimal available, decimal total)
        {
            Currency = currency;
            Available = available;
            Reserved = total - available;
        }
    }
}