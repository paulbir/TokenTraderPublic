namespace GlobitexConnector
{
    class IsinData
    {
        public bool IsSnapshotReceived { get; set; } = false;
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Last { get; set; }
    }
}