using System.Runtime.Serialization;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Messages
{
    /// <summary>
    /// Base message class containing only type of message
    /// </summary>
    [DataContract]
    public class ApiMessage
    {
        /// <summary>
        /// Message data type
        /// </summary>
        [DataMember(Name="t", Order = 0, IsRequired = true)]
        public ApiMessageType Type { get; set; }

        /// <summary>
        /// Client-side custom value to track responses.
        /// Default request id (0) in websocket request means that response is not required.
        /// HTTP API ignores this field and does not send it.
        /// </summary>
        [DataMember(Name = "r", Order = 1, IsRequired = false)]
        public long Reference { get; set; }

        /// <summary>
        /// Error field is separated from data field to simplify common errors parsing.
        /// If Type is error message type than this field must be used.
        /// Data field will be null.
        /// </summary>
        [DataMember(Name="err", EmitDefaultValue = false, IsRequired = false)]
//        [JsonProperty(PropertyName = "err")]
        public ApiError Error { get; set; }

        public bool ShouldSerializeReference() => Reference != 0;
        
        /// <summary>
        /// Create error message
        /// </summary>
        public static ApiMessage MakeError(ApiError error, long reference = 0)
        {
            return new ApiMessage {Type = ApiMessageType.Error, Error = error, Reference = reference};
        }

        /// <summary>
        /// Create data message
        /// </summary>
        public static ApiMessage<T> Make<T>(ApiMessageType type, long reference, params T[] data)
        {
            return Make<T>(type, reference, null, data);
        }

        /// <summary>
        /// Create partial data message
        /// </summary>
        public static ApiMessage<T> Make<T>(ApiMessageType type, long reference, TmexPartial partial, params T[] data)
        {
            return new ApiMessage<T> {Type = type, Reference = reference, Data = data, Partial = partial};
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Universal container for API messages.
    /// </summary>
    /// <typeparam name="TData">class defined by Type field</typeparam>
    [DataContract]
    public class ApiMessage<TData> : ApiMessage
    {
        /// <summary>
        /// Collection of data
        /// </summary>
        [DataMember(Name="d", EmitDefaultValue = false, IsRequired = false)]
        public TData[] Data { get; set; }
        
        /// <summary>
        /// In case of HTTP response pagination or ws stream framing.
        /// </summary>
        [DataMember(Name="p", EmitDefaultValue = false, IsRequired = false)]
        public TmexPartial Partial { get; set; }
    }
    
    /// <summary>
    /// Pagination
    /// </summary>
    [DataContract]
    public class TmexPartial
    {
        /// <summary>
        /// Page number
        /// </summary>
        [DataMember(Name="num")]
        public int Page { get; set; }

        /// <summary>
        /// Amount of pages
        /// </summary>
        [DataMember(Name="cnt")]
        public int Count { get; set; }
    }
}