using System.Collections.Generic;
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace OceanConnector.Model
{
    class OceanErrorMessage : ErrorMessage
    {
        static readonly Dictionary<int, (string, bool)> ErrorCodes = new Dictionary<int, (string, bool)>
                                                                     {
                                                                         {1001, ("Invalid or missing API Key", true)},
                                                                         {1002, ("Nonce is missing in HTTP header", true)},
                                                                         {1003, ("Nonce is out of order", true)},
                                                                         {1004, ("Signature is missing in HTTP header", true)},
                                                                         {1005, ("Malformed signature", true)},
                                                                         {2001, ("Parameter missing", true)},
                                                                         {2002, ("Invalid parameter format or value", true)},
                                                                         {2003, ("Malformed body", true)},
                                                                         {3001, ("Quote has expired", true)},
                                                                         {3002, ("Quote not found", true)},
                                                                         {3003, ("Not enough funds to trade", true)},
                                                                         {3004, ("Trade size higher than quoted size", true)},
                                                                         {3005, ("Quote rejected due price volatility", true)},
                                                                         {3006, ("Order not found", true)},
                                                                         {3007, ("Minimum trading value not reached", true)},
                                                                         {3008, ("Price out of discretion range", true)},
                                                                         {9001, ("Internal server error", true)},
                                                                         {9002, ("Error communicating with third party system", true)}
                                                                     };

        public OceanErrorMessage(RequestError myErrorCode,
                                 string       message,
                                 string       myDescription,
                                 int          oceanErrorCode = 0,
                                 string       traceID        = "",
                                 bool         isCritical     = false)
        {
            if (!ErrorCodes.TryGetValue(oceanErrorCode, out (string oceanErrorDescription, bool isCriticalOcean) tuple))
            {
                tuple.oceanErrorDescription = "UnknownError";
                if (oceanErrorCode != -1) tuple.isCriticalOcean = true;
            }

            Code = (int)myErrorCode;
            Message = message                                                                                                             +
                      (string.IsNullOrEmpty(message) ? "" : ";")                                                                          +
                      (oceanErrorCode != 0 ? $"OceanErrorCode={oceanErrorCode};OceanErrorDescription={tuple.oceanErrorDescription}" : "") +
                      (string.IsNullOrEmpty(traceID) ? "" : $";TraceID={traceID}");
            Description = myDescription;
            IsCritical  = isCritical || tuple.isCriticalOcean;
        }
    }
}