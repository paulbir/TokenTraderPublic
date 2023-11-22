using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WoortonV2Connector
{
    static class MessageCreator
    {
        public static string CreateAuthMessage(string token)
        {
            return $"{{\"event\":\"auth\", \"type\":\"oauth2\", \"token\":\"{token}\"}}";
        }

        public static string CreateSubscribeMessage()
        {
            return $"{{\"event\":\"subscribe\", \"channels\":\"price\"}}";
        }

        public static string CreateAddOrderMessage(string isin, string side, decimal price, decimal qty, string type, int orderTimeoutSeconds)
        {
            return
                $"{{\"instrument\":\"{isin}\", \"side\":\"{side}\", \"order_type\":\"{type}\", \"valid_until\":\"{DateTime.UtcNow.AddSeconds(orderTimeoutSeconds):s}Z\", " +
                $"\"price\":\"{price.ToString(CultureInfo.InvariantCulture)}\", \"quantity\":\"{qty.ToString(CultureInfo.InvariantCulture)}\"}}";
        }
    }
}
