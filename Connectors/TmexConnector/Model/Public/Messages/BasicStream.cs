using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Messages
{
    [DataContract]
    public class BasicStream : IWsStream
    {
        [DataMember(Name = "rev")]
        public long Revision { get; set; }
    }
}