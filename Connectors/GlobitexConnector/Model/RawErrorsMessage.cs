using System.Collections.Generic;
using Newtonsoft.Json;

namespace GlobitexConnector.Model
{
    class RawErrorsMessage
    {
        [JsonProperty("errors")]
        public List<GlobitexErrorMessage> ErrorsList { get; set; }
    }
}