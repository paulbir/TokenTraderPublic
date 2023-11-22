using System.Collections.Generic;
using System.Globalization;
using SharedDataStructures.Messages;

namespace KucoinConnector
{
    static class MessageCreator
    {
        public static string CreateSubscribeToBookMessage(List<string> isins, int requestId)
        {
            return $"{{ \"id\": {requestId}, \"type\": \"subscribe\", \"topic\": \"/market/level2:{string.Join(',', isins)}\", \"response\": false}}";
        }

        public static string CreateSubscribeToSymbolSnapshotMessage(List<string> isins, int requestId)
        {
            return $"{{ \"id\": {requestId}, \"type\": \"subscribe\", \"topic\": \"/market/snapshot:{string.Join(',', isins)}\", \"req\": 0}}";
        }

        public static string CreatePingMessage(int requestId)
        {
            return $"{{ \"id\": {requestId}, \"type\": \"ping\"}}";
        }

        public static string CreateAddOrderMessage(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty)
        {
            return $"{{ \"clientOid\": \"{clientOrderId}\", \"side\": \"{(side == OrderSide.Buy ? "buy" : "sell")}\", \"symbol\": \"{isin}\", " +
                   $"\"price\": {price.ToString(CultureInfo.InvariantCulture)}, \"size\": {qty.ToString(CultureInfo.InvariantCulture)}}}";
        }
    }
}