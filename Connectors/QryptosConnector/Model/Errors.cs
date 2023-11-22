using System.Collections.Generic;
using Newtonsoft.Json;

namespace QryptosConnector.Model
{
    class Errors
    {
        [JsonProperty("user")]
        public List<string> ErrorsList { get; set; }
    }
}