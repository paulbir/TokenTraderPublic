using System.Collections.Generic;
using System.Linq;
using IDaxConnector.Model;
using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class IDaxBookMessage : BookMessage
    {
        public bool Success { get; }
        public string Message { get; }

        public IDaxBookMessage(List<IDaxPriceLevel> data, bool success, string message)
        {
            Sequence = 0;
            Bids = data?.Where(level => level.Side == OrderSide.Buy).Select(level => (PriceLevel)level).ToList();
            Asks = data?.Where(level => level.Side == OrderSide.Sell).Select(level => (PriceLevel)level).ToList();

            Success = success;
            Message = message;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}