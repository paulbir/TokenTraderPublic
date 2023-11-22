using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Messages
{
    [DataContract]
    public class AssetStream : BasicStream
    {
        //[DataMember(Name = "aid")]
        //public long AssetId { get; set; }

        [DataMember(Name = "sym")]
        public string Symbol { get; set; }
    }
}