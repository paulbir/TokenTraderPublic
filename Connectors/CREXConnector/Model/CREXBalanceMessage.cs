using SharedDataStructures.Messages;

namespace CREXConnector.Model
{
    class CREXBalanceMessage : BalanceMessage
    {
        public CREXBalanceMessage(string asset, decimal total, decimal locked)
        {
            Currency = asset;
            Available = total - locked;
            Reserved = locked;
        }
    }
}