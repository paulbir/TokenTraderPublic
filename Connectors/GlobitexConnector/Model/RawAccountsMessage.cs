using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace GlobitexConnector.Model
{
    class RawAccountsMessage
    {
        public Dictionary<string, List<BalanceMessage>> BalancesByAccount = new Dictionary<string, List<BalanceMessage>>();
        public string FirstAccount { get; } = "";

        public RawAccountsMessage(List<Account> accounts)
        {
            foreach (Account account in accounts)
            {
                if (FirstAccount == "") FirstAccount = account.AccountName;

                foreach (Balance balance in account.Balances)
                {
                    if (!BalancesByAccount.TryGetValue(account.AccountName, out List<BalanceMessage> balances))
                    {
                        balances = new List<BalanceMessage>();
                        BalancesByAccount.Add(account.AccountName, balances);
                    }

                    balances.Add(new GlobitexBalanceMessage(balance.Currency, balance.Available, balance.Reserved));
                }

            }
        }
    }
}