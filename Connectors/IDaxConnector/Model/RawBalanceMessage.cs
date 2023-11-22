using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class RawBalanceMessage
    {
        public bool Success { get; }
        public string Message { get; }
        public List<BalanceMessage> Balances { get; }

        public RawBalanceMessage(List<IDaxBalanceMessage> data, bool success, string message)
        {
            Balances = data?.Select(balance => (BalanceMessage)balance).ToList();
            Success = success;
            Message = message;
        }
    }
}