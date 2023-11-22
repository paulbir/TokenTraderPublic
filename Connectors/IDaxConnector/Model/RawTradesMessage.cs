using System.Collections.Generic;

namespace IDaxConnector.Model
{
    class RawTradesMessage
    {
        public bool Success { get; }
        public string Message { get; }
        public List<IDaxTradeMessage> Trades { get; }

        public RawTradesMessage(List<IDaxTradeMessage> data, bool success, string message)
        {
            Trades = data;
            Success = success;
            Message = message;
        }
    }
}