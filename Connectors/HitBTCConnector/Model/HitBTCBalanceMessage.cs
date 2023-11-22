using SharedDataStructures.Messages;
using SharedTools;

namespace HitBTCConnector.Model
{
    class HitBTCBalanceMessage : BalanceMessage
    {
        public HitBTCBalanceMessage(string currency, string available, string reserved)
        {
            Currency = currency;
            Available = available.ToDecimal();
            Reserved = reserved.ToDecimal();
        }
    }
}