namespace FineryConnector.Model
{
    class IsinData
    {
        public string  Isin    { get; }
        public long    FeedId  { get; }
        public decimal QtyStep { get; }

        public IsinData(string isin, long feedId, decimal qtyStep)
        {
            Isin    = isin;
            FeedId  = feedId;
            QtyStep = qtyStep;
        }
    }
}