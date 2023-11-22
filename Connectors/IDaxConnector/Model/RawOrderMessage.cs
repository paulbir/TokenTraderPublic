using SharedDataStructures.Messages;

namespace IDaxConnector.Model
{
    class RawOrderMessage
    {
        public bool Success { get; }
        public string Message { get; }
        public OrderMessage Order { get; }

        public RawOrderMessage(object data, bool success, string message)
        {
            Order = data as IDaxOrderMessage;
            Success = success;
            Message = message;
        }
    }
}