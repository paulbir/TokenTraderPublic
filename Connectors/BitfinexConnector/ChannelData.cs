namespace BitfinexConnector
{
    class ChannelData
    {
        public string Isin { get; }
        public bool IsSnapshotReceived { get; set; } = false;

        public ChannelData(string isin)
        {
            Isin = isin;
        }
    }
}