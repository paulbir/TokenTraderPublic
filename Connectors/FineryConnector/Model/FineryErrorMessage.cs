using System.Collections.Generic;
using SharedDataStructures;
using SharedDataStructures.Messages;

namespace FineryConnector.Model
{
    class FineryErrorMessage : ErrorMessage
    {
        public FineryErrorMessage(RequestError code, string message, string description, int fineryErrorCode, bool isCritical = false)
        {
            if (!ErrorCodes.TryGetValue(fineryErrorCode, out (string errorMessage, bool isCriticalFinery) tuple))
            {
                tuple.errorMessage = "UnknownError";
                tuple.isCriticalFinery = false;
            }

            Code        = (int)code;
            Message     = message + (string.IsNullOrEmpty(message) ? "" : ";") + $"FineryErrorCode={fineryErrorCode};FineryErrorMessage={tuple.errorMessage}";
            Description = description;
            IsCritical = isCritical || tuple.isCriticalFinery;
        }

        static readonly Dictionary<int, (string, bool)> ErrorCodes = new Dictionary<int, (string, bool)>
                                                                     {
                                                                         {1, ("Not implemented", true)},
                                                                         {2, ("Not connected", true)},
                                                                         {3, ("Not authorized", true)},
                                                                         {4, ("Already authorized", true)},
                                                                         {5, ("Invalid password", true)},
                                                                         {6, ("Invalid nonce or signature", true)}, //не смогли сделать auth
                                                                         {7, ("Invalid timestamp", true)},
                                                                         {8, ("API method not available", true)},
                                                                         {9, ("API method parameter is invalid", true)},
                                                                         {10, ("Internal error", true)},
                                                                         {20, ("Invalid currency flags", true)},
                                                                         {21, ("Invalid currency price", true)},
                                                                         {22, ("Invalid currency balance step", true)},
                                                                         {23, ("Invalid currency name", true)},
                                                                         {24, ("Currency name cannot be changed", true)},
                                                                         {25, ("Currency balance step cannot be changed", true)},
                                                                         {26, ("Currency not found", true)},
                                                                         {27, ("Currency cannot be removed", true)},
                                                                         {30, ("Invalid instrument flags", true)},
                                                                         {31, ("Invalid instrument name", true)},
                                                                         {32, ("Instrument asset currency cannot be changed", true)},
                                                                         {33, ("Instrument balance currency cannot be changed", true)},
                                                                         {34, ("Instrument not found", true)},
                                                                         {35, ("Instrument cannot be removed", true)},
                                                                         {40, ("Invalid client flags", true)},
                                                                         {41, ("Invalid client taker delta ratio", true)},
                                                                         {42, ("Invalid name", true)},
                                                                         {43, ("Client type cannot be changed", true)},
                                                                         {44, ("Client already exists", true)},
                                                                         {45, ("Client not found", true)},
                                                                         {50, ("Invalid limit flags", true)},
                                                                         {51, ("Invalid limit net limit", true)},
                                                                         {52, ("Invalid limit gross limit", true)},
                                                                         {53, ("Limit not found", true)},
                                                                         {54, ("Limit clients are identical", true)},
                                                                         {55, ("Limit client types are identical", true)},
                                                                         {61, ("Invalid settlement order size", true)},
                                                                         {62, ("Invalid settlement order comment", true)},
                                                                         {63, ("Identical settlement clients", true)},
                                                                         {64, ("Settlement not found", true)},
                                                                         {65, ("Settlement order is from transaction", true)},
                                                                         {70, ("Invalid order size", true)},
                                                                         {71, ("Invalid order price", true)},
                                                                         {72, ("Invalid order flags", true)},
                                                                         {73, ("Order type not allowed", true)},
                                                                         {74, ("Client order id already in use", true)},
                                                                         {75, ("Add failed - Post-Only", true)},
                                                                         {76, ("Add failed - IOC: no orders to match", true)},
                                                                         {77, ("Add failed - FOK: not enough liquidity", true)},
                                                                         {78, ("Add failed - SMP (self-trade prevention)", true)},
                                                                         {79, ("Add failed - limits", true)},
                                                                         {80, ("Del failed - not found", false)}, //не найдена заявка для удаления. не фатально
                                                                         {90, ("Mod failed - no size after decrement", true)},
                                                                         {91, ("Mod failed - side mismatch", true)},
                                                                         {100, ("Binding already exists", true)},
                                                                         {101, ("Binding not found", true)},
                                                                         {102, ("Invalid feed name", true)},
                                                                         {103, ("Invalid feed id", true)},
                                                                         {104, ("Database out-of-sync", true)},
                                                                         {110, ("Field Required", true)},
                                                                         {111, ("Field Invalid", true)},
                                                                         {112, ("Poor Username", true)},
                                                                         {113, ("Poor Password", true)},
                                                                         {114, ("Password Change Required", true)},
                                                                         {120, ("Maximum number of keys reached", true)},
                                                                         {121, ("Key not found", true)},
                                                                         {130, ("Settlement request already exists", true)},
                                                                         {131, ("Settlement request not found", true)},
                                                                         {132, ("Invalid settlement request flags", true)},
                                                                         {133, ("Invalid settlement request counterparty", true)},
                                                                         {140, ("Invalid settlement transaction flags", true)},
                                                                         {141, ("Invalid settlement transaction amount", true)},
                                                                         {142, ("Invalid settlement transaction txId", true)},
                                                                         {143, ("Identical clients not allowed", true)},
                                                                         {144, ("Settlement transaction not found", true)},
                                                                         {160, ("Settlement order client A global net limit breached", true)},
                                                                         {161, ("Settlement order client A global gross limit breached", true)},
                                                                         {162, ("Settlement order client B global net limit breached", true)},
                                                                         {163, ("Settlement order client B global gross limit breached", true)},
                                                                         {164, ("Settlement order client A counterparty net limit breached", true)},
                                                                         {165, ("Settlement order client A counterparty gross limit breached", true)},
                                                                         {166, ("Settlement order client B counterparty net limit breached", true)},
                                                                         {167, ("Settlement order client B counterparty gross limit breached", true)}
                                                                     };
    }
}