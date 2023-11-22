using System.Collections.Generic;

namespace QryptosConnector.Model
{
    class RawError
    {
        public string Message { get; }
        public List<string> Errors { get; }

        public RawError(string message, Errors errors)
        {
            Message = message ?? "";
            Errors = errors?.ErrorsList ?? new List<string>();
        }
    }
}