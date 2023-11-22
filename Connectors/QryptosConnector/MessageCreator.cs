namespace QryptosConnector
{
    static class MessageCreator
    {
        public static string CreateAddOrderMessage(int isinId, string side, string price, string qty)
        {
            return $"{{\"order\":" +
                   $"{{\"order_type\":\"limit\",\"product_id\":{isinId},\"side\":\"{side}\",\"quantity\":\"{qty}\",\"price\":\"{price}\"}}}}";
        }
    }
}