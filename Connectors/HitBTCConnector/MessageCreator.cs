namespace HitBTCConnector
{
    static class MessageCreator
    {
        public static string CreateLoginMessage(string publicKey, string secureKey)
        {
            return $"{{ \"method\": \"login\", \"params\": {{ \"algo\": \"BASIC\", \"pKey\": \"{publicKey}\", \"sKey\": \"{secureKey}\" }} }}";
        }

        public static string CreateSubscribeToBookMessage(string isin, int requestId)
        {
            return $"{{ \"method\": \"subscribeOrderbook\", \"params\": {{ \"symbol\": \"{isin}\" }},  \"id\": {requestId} }}";
        }

        public static string CreateSubscribeToTickerMessage(string isin, int requestId)
        {
            return $"{{ \"method\": \"subscribeTicker\", \"params\": {{ \"symbol\": \"{isin}\" }},  \"id\": {requestId} }}";
        }

        public static string CreateSubscribeToReportsMessage()
        {
            return "{ \"method\": \"subscribeReports\", \"params\": {} }";
        }

        public static string CreateAddOrderMessage(string clientOrderId, string isin, string side, string price, string qty, int requestId)
        {
            return $"{{ \"method\": \"newOrder\", \"params\": " + 
                   $"{{ \"clientOrderId\": \"{clientOrderId}\" , \"symbol\": \"{isin}\", \"side\": \"{side}\", \"price\": \"{price}\", \"quantity\": \"{qty}\" }},  " +
                   $"\"id\": {requestId} }}";
        }

        public static string CreateCancelOrderMessage(string clientOrderId, int requestId)
        {
            return $"{{ \"method\": \"cancelOrder\", \"params\": {{ \"clientOrderId\": \"{clientOrderId}\" }},  \"id\": {requestId} }}";
        }

        public static string CreateReplaceOrderMessage(string oldClientOrderId, string newClientOrderId, string price, string qty, int requestId)
        {
            return $"{{ \"method\": \"cancelReplaceOrder\", \"params\": " +
                   $"{{ \"clientOrderId\": \"{oldClientOrderId}\" , \"requestClientId\": \"{newClientOrderId}\", \"price\": \"{price}\", \"quantity\": \"{qty}\" }},  " +
                   $"\"id\": {requestId} }}";
        }

        public static string CreateGetActiveOrdersMessage(int requestId)
        {
            return $"{{ \"method\": \"getOrders\", \"params\": {{ }},  \"id\": {requestId} }}";
        }

        public static string CreateGetTradingBalanceMessage(int requestId)
        {
            return $"{{ \"method\": \"getTradingBalance\", \"params\": {{ }},  \"id\": {requestId} }}";
        }
    }
}