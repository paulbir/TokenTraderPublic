namespace DutyFlyConnector
{
    static class MessageCreator
    {
        public static string CreateAuthorizeMessage(string authToken, string consumerId, int requestId)
        {
            return $"{{ \"method\": \"authorize\", \"params\": {{ \"token\": \"{authToken}\", \"consumerId\": \"{consumerId}\" }}, \"id\": {requestId} }}";
        }

        public static string CreateSubscribeBookMessage(string isin, int requestId)
        {
            return $"{{ \"method\": \"subscribeBook\", \"params\": {{ \"symbol\": \"{isin}\" }}, \"id\": {requestId} }}";
        }

        public static string CreateSubscribeTradesMessage(string isin, int requestId)
        {
            return $"{{ \"method\": \"subscribeTrades\", \"params\": {{ \"symbol\": \"{isin}\", \"limit\": 1 }}, \"id\": {requestId} }}";
        }

        public static string CreateGetBalancesMessage(int requestId)
        {
            return $"{{ \"method\": \"balances\", \"params\": {{ \"includeSummary\": \"true\" }}, \"id\": {requestId} }}";
        }
    }
}