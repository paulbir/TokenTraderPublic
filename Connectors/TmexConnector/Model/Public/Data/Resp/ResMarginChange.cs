using System.Runtime.Serialization;
using TmexConnector.Model.Shared;

namespace TmexConnector.Model.Public.Data.Resp
{
    public class ResMarginChange
    {
        public decimal Value { get; set; }
        public bool IsPossible { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public ApiError Error { get; set; }
    }
}