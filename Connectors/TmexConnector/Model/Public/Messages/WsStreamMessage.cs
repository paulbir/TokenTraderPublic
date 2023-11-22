using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Messages
{
    [DataContract]
    public class WsStreamMessage<TData, TMeta> : ApiMessage<TData>
        where TMeta : IWsStream
    {
        /// <summary>
        /// Stream definition (may contain common fields of events such as revision)
        /// </summary>
        [DataMember(Name="s")]
        public TMeta Stream { get; set; }
    }

    /// <summary>
    /// Ws streaming metadata
    /// </summary>
    public interface IWsStream
    {
        /// <summary>
        /// Stream identification
        /// </summary>
        //string Id { get; }

        /// <summary>
        /// Stream revision
        /// </summary>
        long Revision { get; }
    }
}