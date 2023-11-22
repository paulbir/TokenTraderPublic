namespace CREXConnector
{
    static class MessageCreator
    {
        public static string CreateBeginAuthRequestMessage()
        {
            return "{ \"_type\": \"begin_auth\" }";
        }

        public static string CreateAuthPasswordRequestMessage(string sessionId, string username, string password)
        {
            return $"{{ \"_type\": \"auth_password\", \"session_id\": \"{sessionId}\", \"username\": \"{username}\", " + 
                   $"\"password\": \"{password}\", \"recaptcha\": \"\" }}";
        }

        public static string CreateLoginRequestMessage(string accessToken)
        {
            return $"{{ \"_type\": \"login\", \"access_token\": \"{accessToken}\" }}";
        }

        public static string CreateRenewTokenRequestMessage(string refreshToken)
        {
            return $"{{ \"_type\": \"renew_token\", \"refresh_token\": \"{refreshToken}\" }}";
        }

        public static string CreateRenewLoginRequestMessage(string accessToken)
        {
            return $"{{ \"_type\": \"renew_login\", \"access_token\": \"{accessToken}\" }}";
        }

        public static string CreateOrderBookSnapshotRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"get_orderbook\", \"instrument\": \"{isin}\", \"depth\": 10000 }}";
        }

        public static string CreateOrderBookSubscriptionRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"orderbook_subscription\", \"instrument\": \"{isin}\", \"action\": \"ADD\" }}";
        }

        public static string CreateGetPositionsRequestMessage(int requestId)
        {
            return $"{{ \"_type\": \"get_positions\", \"req_id\": \"{requestId}\" }}";
        }

        public static string CreateGetActiveOrdersRequestMessage(string isin, string requestId)
        {
            return $"{{ \"_type\": \"get_orders\", \"instrument\": \"{isin}\", \"status\": [\"ACTIVE\"], \"req_id\": \"{requestId}\" }}";
        }

        public static string CreateTickerSubscriptionRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"ticker_subscription\", \"instrument\": \"{isin}\", \"action\": \"ADD\" }}";
        }

        public static string CreateGetInstrumentsRequestMessage()
        {
            return "{ \"_type\": \"get_instruments\" }";
        }

        public static string CreateOrderSubscriptionRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"orders_subscription\", \"instrument\": \"{isin}\", \"action\": \"ADD\" }}";
        }

        public static string CreatePlaceLimitOrderMessage(string clientOrderId, string isin, string price, string qty, string priceCurrency, string side, int requestId)
        {
            return $"{{ \"_type\": \"place_lmt_order\", \"client_transaction_id\": \"{clientOrderId}\", \"instrument\": \"{isin}\", \"price\": {price}, " + 
                   $"\"size\": {qty}, \"price_currency\": \"{priceCurrency}\", \"direction\": \"{side}\", \"req_id\": \"{requestId}\" }}";
        }

        public static string CreateTradeSubscriptionRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"trades_subscription\", \"instrument\": \"{isin}\", \"action\": \"ADD\" }}";
        }

        public static string CreateTransactionsSubscriptionRequestMessage(string isin)
        {
            return $"{{ \"_type\": \"transactions_subscription\", \"instrument\": \"{isin}\", \"action\": \"ADD\" }}";
        }

        public static string CreateKillOrderMessage(long orderId, string clientOrderId, string isin, int requestId)
        {
            return $"{{ \"_type\": \"kill_order\", \"order_id\": {orderId}, \"client_transaction_id\": \"{clientOrderId}\", \"instrument\": \"{isin}\", \"req_id\": \"{requestId}\" }}";
        }
    }
}