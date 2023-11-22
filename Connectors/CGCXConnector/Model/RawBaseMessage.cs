namespace CGCXConnector.Model
{
    class RawBaseMessage
    {
        public int RequestId { get; }
        public string RequestType { get; }
        public string Payload { get; }

        public RawBaseMessage(int i, string n, string o)
        {
            RequestId = i;
            RequestType = n;
            Payload = o;
        }
    }
}