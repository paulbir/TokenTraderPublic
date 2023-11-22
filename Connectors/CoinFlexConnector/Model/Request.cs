namespace CoinFlexConnector.Model
{
    enum RequestType
    {
        Authentication = 1,
        Orders = 2,
        Ticker = 3,
        Balances = 4,
        ActiveOrders = 5,
        AddOrder = 6,
        CancelOrder = 7
    }

    class Request
    {
        public string Isin { get; }
        public RequestType RequestType { get; }
        public string ClientOrderId { get; }
        public bool IsRequestToThrowOnError =>
            RequestType == RequestType.Authentication || RequestType == RequestType.Orders || RequestType == RequestType.Ticker;

        public Request(string isin, RequestType requestType)
        {
            Isin = isin;
            RequestType = requestType;
        }

        public Request(RequestType requestType)
        {
            RequestType = requestType;
        }

        public Request(RequestType requestType, string clientOrderId)
        {
            ClientOrderId = clientOrderId;
            RequestType = requestType;
        }
    }
}