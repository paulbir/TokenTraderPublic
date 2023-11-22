namespace TmexConnector
{
    static class MessageCreator
    {
        public static string CreateAuthenticateMessage(string publicKey, string payload, string signature, long nonce)
        {
            return $"{{\"Key\":\"{publicKey}\", \"Payload\":\"{payload}\", \"Signature\":{signature}, \"Nonce\":\"{nonce}\"}}";
        }
    }
}