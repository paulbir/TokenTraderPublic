using System.Runtime.Serialization;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data
{
    /// <summary>
    /// Operation is considered successful if error is null
    /// </summary>
    [DataContract]
    public class StreamSubscription
    {
        [DataMember(Name = "s")]
        public string Stream { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ApiError Error { get; set; }
    }
}