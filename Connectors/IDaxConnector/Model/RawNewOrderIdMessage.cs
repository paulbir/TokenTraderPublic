namespace IDaxConnector.Model
{
    class RawNewOrderIdMessage
    {
        public bool Success { get; }
        public string Message { get; }
        public string OrderId { get; }

        public RawNewOrderIdMessage(string data, bool success, string message)
        {
            OrderId = data;
            Success = success;
            Message = message;
        }
    }
}