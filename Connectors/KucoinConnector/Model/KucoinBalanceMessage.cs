using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinBalanceMessage : BalanceMessage
    {
        public string Type { get; }
        public KucoinBalanceMessage(string currency, string type, decimal balance, decimal holds)
        {
            Currency = currency;
            Available = balance - holds;
            Reserved = holds;

            Type = type;
        }
    }
}
