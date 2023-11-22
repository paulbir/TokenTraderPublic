namespace KucoinConnector
{
    class BookSnapshotData
    {
        public bool GotSnapshot { get; private set; }
        public long Sequence { get; private set; }

        public BookSnapshotData(bool gotSnapshot, long sequence)
        {
            GotSnapshot = gotSnapshot;
            Sequence = sequence;
        }

        public void SetSnapshot(long sequence)
        {
            GotSnapshot = true;
            Sequence = sequence;
        }
    }
}