using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Messages
{
    [DataContract]
    public class OrderBookStream : AssetStream
    {
        [DataMember(Name="ts")]
        public long Timestamp { get; set; }
    }
}