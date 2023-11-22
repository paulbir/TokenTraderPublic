using System.Runtime.Serialization;
using SharedDataStructures.Messages;

namespace TmexConnector.Model.Public.Data
{
    [DataContract]
    public class Quote : TickerMessage
    {
        //[DataMember(EmitDefaultValue = false)]
        //public decimal? BestBid { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public double? BidIv { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public decimal? BestAsk { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public double? AskIv { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public double? StrikeIv { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public decimal? LastPrice { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public decimal? MarkPrice { get; set; }

        ////        public decimal IndexPrice { get; set; }
        //[DataMember(EmitDefaultValue = false)]
        //public decimal? OpenInterest { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public long? LastTimestamp { get; set; }

        //[DataMember(EmitDefaultValue = false)]
        //public long Timestamp { get; set; }

        public Quote(decimal b, decimal a, decimal l)
        {
            Bid = b;
            Ask = a;
            Last = l;
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}