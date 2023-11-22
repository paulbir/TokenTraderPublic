using System.Runtime.Serialization;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data
{
    public class SystemEvent
    {
        public SystemStatus Status { get; set; }

        public long Timestamp { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ApiError Error { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool AssetsChanged { get; set; }

        public string Message { get; set; }
    }
}