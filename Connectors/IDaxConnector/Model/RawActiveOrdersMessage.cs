using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    // ReSharper disable once InconsistentNaming
    class RawActiveOrdersMessage
    {
        public bool Success { get; }
        public string Message { get; }
        public List<OrderMessage> Orders { get; }

        public RawActiveOrdersMessage(List<IDaxOrderMessage> data, bool success, string message)
        {
            Success = success;
            Message = message;
            Orders = data?.Select(order => (OrderMessage)order).ToList();
        }
    }
}