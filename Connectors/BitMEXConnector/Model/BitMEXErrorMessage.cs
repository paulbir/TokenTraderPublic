using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace BitMEXConnector.Model
{
    class BitMEXErrorMessage : ErrorMessage
    {
        [JsonConstructor]
        public BitMEXErrorMessage(int status, string error, Dictionary<string, string> meta, Dictionary<string, string> request)
        {
            Code = status;
            Message = error;
            Description = $"meta: {string.Join(';', meta.Select(pair => $"{pair.Key}={pair.Value}"))}\n" +
                          $"request: {string.Join(';', request.Select(pair => $"{pair.Key}={pair.Value}"))}";
        }

        public BitMEXErrorMessage(int code, string message, string description)
        {
            Code = code;
            Message = message;
            Description = description;
        }
    }
}