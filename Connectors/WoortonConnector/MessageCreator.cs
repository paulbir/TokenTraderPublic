using System.Globalization;

namespace WoortonConnector
{
    static class MessageCreator
    {
        public static string CreateAddOrderMessage(string clientOrderId, string isin, string side, decimal price, decimal qty, string type)
        {
            return
                $"{{\"client_request_id\":\"{clientOrderId}\", \"instrument\":\"{isin}\", \"direction\":\"{side}\", \"order_type\":\"{type}\", " +
                $"\"requested_price\":\"{price.ToString(CultureInfo.InvariantCulture)}\", \"amount\":\"{qty.ToString(CultureInfo.InvariantCulture)}\"}}";
        }

        public static string CreateExecuteMessage(string requestId, string isin, string side, decimal qty, decimal total)
        {
            return $"{{\"request_id\":\"{requestId}\", \"instrument\":\"{isin}\", \"direction\":\"{side}\", " +
                   $"\"amount\":\"{qty.ToString(CultureInfo.InvariantCulture)}\", \"total\":\"{total.ToString(CultureInfo.InvariantCulture)}\"}}";
        }
    }
}