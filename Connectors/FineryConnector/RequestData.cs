using SharedDataStructures;

namespace FineryConnector
{
    class RequestData
    {
        public RequestError RequestType              { get; }
        public string       ClientOrderId            { get; }
        public bool         ShouldSendOrderIdOnError => RequestType == RequestError.CancelOrder;

        public RequestData(RequestError requestType)
        {
            RequestType = requestType;
        }

        public RequestData(RequestError requestType, string clientOrderId)
        {
            RequestType   = requestType;
            ClientOrderId = clientOrderId;
        }
    }
}