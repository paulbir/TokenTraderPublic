using System;
using System.Security.Cryptography;
using System.Text;

namespace FineryConnector
{
    static class MessageCreator
    {
        public static string CreateAuthMessage(string publicKey, string secretKey)
        {
            long   timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long   nonce     = timestamp;
            string content   = $"{{\\\"nonce\\\":{nonce}, \\\"timestamp\\\":{timestamp}}}";
            string contentForSignature = $"{{\"nonce\":{nonce}, \"timestamp\":{timestamp}}}";
            string signature = CreateSignature(secretKey, contentForSignature);

            return $"{{\"event\":\"auth\", \"content\":\"{content}\", \"key\":\"{publicKey}\", \"signature\":\"{signature}\"}}";
        }

        public static string CreateInstrumentsRequestMessage(int requestId)
        {
            return CreateRequestMessage("instruments", null, requestId);
        }

        public static string CreatePositionsRequestMessage(int requestId)
        {
            return CreateRequestMessage("positions", null, requestId);
        }        

        public static string CreateLimitsRequestMessage(int requestId)
        {
            return CreateRequestMessage("limits", null, requestId);
        }   

        public static string CreateBindToPositionsMessage()
        {
            return CreateBindUnbindMessage("P", true);
        }

        public static string CreateBindToIsinMessage(long feedId, string bookStreamName)
        {
            return CreateBindUnbindMessage(bookStreamName, true, feedId);
        }

        public static string CreateAddOrderRequestMessage(string isin, long clientOrderId, long price, long qty, string side, int requestId)
        {
            string payload =
                $"{{\"instrument\": \"{isin}\", \"clientOrderId\": {clientOrderId}, \"price\": {price}, \"size\": {qty}, \"side\": \"{side}\", \"type\": \"limitIOC\", \"cod\": true}}";
            return CreateRequestMessage("add", payload, requestId);
        }

        public static string CreateCancelOrderRequestMessage(long clientOrderId, int requestId)
        {
            string payload = $"{{\"clientOrderId\": {clientOrderId}}}";
            return CreateRequestMessage("del", payload, requestId);
        }

        static string CreateBindUnbindMessage(string feed, bool isBind, long feedId = -1)
        {
            return $"{{\"event\":\"{(isBind ? "" : "un")}bind\", \"feed\":\"{feed}\"{(feedId != -1 ? $", \"feedId\":{feedId}" : "")}}}";
        }

        static string CreateRequestMessage(string method, string payload, int requestId)
        {
            string request = $"{{\"event\":\"request\", \"reqId\": {requestId}, \"method\": \"{method}\"";
            if (!string.IsNullOrEmpty(payload)) request += $", \"content\": {payload}}}";
            else request                                += "}";

            return request;
        }

        static string CreateSignature(string secretKey, string payload)
        {
            byte[] payloadBytes   = Encoding.UTF8.GetBytes(payload);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            byte[] hashBytes;
            using (var encoder = new HMACSHA384(secretKeyBytes)) { hashBytes = encoder.ComputeHash(payloadBytes); }

            string hashString = Convert.ToBase64String(hashBytes);
            return hashString;
        }
    }
}