using System.Runtime.Serialization;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data.Resp
{
    public class ResPlacedOrder
    {
        public long ExternalId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public long OrderId { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ApiError Error { get; set; }
    }
}