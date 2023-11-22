using SharedDataStructures.Messages;

namespace KucoinConnector.Model
{
    class KucoinBookUpdateMessage : BookMessage
    {
        public long SequenceStart { get; }

        public KucoinBookUpdateMessage(long sequenceStart, long sequenceEnd, KucoinRawBookUpdateLevels changes)
        {
            Sequence = sequenceEnd;
            Bids = changes.Bids;
            Asks = changes.Asks;

            SequenceStart = sequenceStart;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}