using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaBalanceMessage : BalanceMessage
    {
        public XenaBalanceMessage(string currency, decimal available, decimal reserved)
        {
            Currency = currency;
            Available = available;
            Reserved = reserved;
        }

        public void UpdateBalance(decimal available, decimal reserved)
        {
            Available = available;
            Reserved = reserved;
        }
    }
}