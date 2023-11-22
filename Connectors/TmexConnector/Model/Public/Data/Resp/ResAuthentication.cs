using System.Runtime.Serialization;

namespace TmexConnector.Model.Public.Data.Resp
{
    public class ResAuthentication
    {
        public bool IsAuthenticated { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public long ExpiresAt { get; set; }
    }
}