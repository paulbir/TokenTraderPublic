using System.Collections.Generic;

namespace SharedDataStructures.Messages
{
    public class BookMessage
    {
        public string Isin { get; protected set; }
        public long Sequence { get; protected set; }
        public List<PriceLevel> Bids { get; protected set; }
        public List<PriceLevel> Asks { get; protected set; }

        public override string ToString()
        {
            string bidsStr = "Bids:[" + string.Join('|', Bids) + "]";
            string asksStr = "Asks:[" + string.Join('|', Asks) + "]";
            return $"{Isin};{Sequence};\n{bidsStr};\n{asksStr}";
        }
    }
}