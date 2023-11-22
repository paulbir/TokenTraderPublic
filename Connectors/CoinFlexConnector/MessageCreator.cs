using Org.BouncyCastle.Asn1.Ntt;

namespace CoinFlexConnector
{
    static class MessageCreator
    {
        public static string CreateSubscribeWatchOrdersMessage(int tag, long baseId, long counterId)
        {
            return $"{{\"tag\": {tag}, \"method\": \"WatchOrders\", \"base\": {baseId}, \"counter\": {counterId}, \"watch\": true}}";
        }

        public static string CreateSubscribeWatchTickerMessage(int tag, long baseId, long counterId)
        {
            return $"{{\"tag\": {tag}, \"method\": \"WatchTicker\", \"base\": {baseId}, \"counter\": {counterId}, \"watch\": true}}";
        }

        public static string CreateAuthenticateMessage(int tag, long userId, string cookie, string clientNonce, string r, string s)
        {
            return $"{{\"tag\": {tag}, \"method\": \"Authenticate\", \"user_id\": {userId}, \"cookie\": \"{cookie}\", \"nonce\": \"{clientNonce}\", " + 
                   $"\"signature\": [\"{r}\", \"{s}\"]}}";
        }

        public static string CreateBalancesMessage(int tag)
        {
            return $"{{\"tag\": {tag}, \"method\": \"GetBalances\"}}";
        }

        public static string CreateGetOrdersMessage(int tag)
        {
            return $"{{\"tag\": {tag}, \"method\": \"GetOrders\"}}";
        }

        public static string CreateCancelAllOrdersMessage(int tag)
        {
            return $"{{\"tag\": {tag}, \"method\": \"CancelAllOrders\"}}";
        }

        public static string CreatePlaceOrderMessage(int tag, long tonce, long baseId, long counterId, long qty, long price)
        {
            return $"{{\"tag\": {tag}, \"method\": \"PlaceOrder\", \"tonce\": {tonce}, \"base\": {baseId}, \"counter\": {counterId}, \"quantity\": {qty}, " +
                   $"\"price\": {price}, \"persist\": false}}";
            //return $"{{\"tag\": {tag}, \"method\": \"PlaceOrder\", \"base\": {baseId}, \"counter\": {counterId}, \"quantity\": {qty}, " +
            //       $"\"price\": {price}, \"persist\": false}}";
        }

        public static string CreateCancelOrderMessage(int tag, long tonce)
        {
            return $"{{\"tag\": {tag}, \"method\": \"CancelOrder\", \"tonce\": {tonce}}}";
            //return $"{{\"tag\": {tag}, \"method\": \"PlaceOrder\", \"base\": {baseId}, \"counter\": {counterId}, \"quantity\": {qty}, " +
            //       $"\"price\": {price}, \"persist\": false}}";
        }
    }
}
