using SharedDataStructures.Messages;
using SharedTools;

namespace QryptosConnector.Model
{
    class QryptosBalanceMessage : BalanceMessage
    {
        public QryptosBalanceMessage(string currency, string balance, string free_balance)
        {
            decimal totalBalance = balance.ToDecimal();
            decimal freeBalance = free_balance?.ToDecimal() ?? -1;

            Currency = currency;
            Available = free_balance == null ? totalBalance : freeBalance;
            Reserved = free_balance == null ? 0 : totalBalance - freeBalance;
        }
    }
}