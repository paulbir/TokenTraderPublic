using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class RawBalances
    {
        public List<BalanceMessage> Balances { get; }

        public RawBalances(List<OceanBalanceMessage> balances)
        {
            Balances = new List<BalanceMessage>();
            foreach (OceanBalanceMessage balanceMessage in balances) Balances.Add(balanceMessage);
        }
    }
}