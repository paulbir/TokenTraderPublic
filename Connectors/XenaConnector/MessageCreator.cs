using System;
using XenaConnector.Model;

namespace XenaConnector
{
    static class MessageCreator
    {
        public static string CreateLogonMessage(string publicKey, long nonce, string payload, string signature)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.Logon}\", \"{Tags.Username}\":\"{publicKey}\", " + 
                   $"\"{Tags.SendingTime}\":{nonce}, \"{Tags.RawData}\":\"{payload}\", \"{Tags.Password}\":\"{signature}\"}}";
        }

        public static string CreateSubscribeToBookMessage(string isin)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.MktDataRequest}\", \"{Tags.MDStreamID}\":\"DOM:{isin}:aggregated\", \"{Tags.SubscriptionRequestType}\":1}}";
        }

        public static string CreateSubscribeToMarketTrades(string isin)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.MktDataRequest}\", \"{Tags.MDStreamID}\":\"trades:{isin}\", \"{Tags.SubscriptionRequestType}\":1}}";
        }

        public static string CreateAccountStatusReportRequestMessage(int accountId)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.AccountStatusReportRequest}\", \"{Tags.Account}\":{accountId}}}";
        }

        public static string CreateOrdersMassStatusRequestMessage(int accountId)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.OrderMassStatusRequest}\", \"{Tags.Account}\":{accountId}}}";
        }

        public static string CreateAddOrderMessage(string clientOrderId,
                                                   string isin,
                                                   string side,
                                                   string price,
                                                   string qty,
                                                   int accountId,
                                                   long timestamp,
                                                   int positionId,
                                                   bool isMarginMaket,
                                                   bool isMarginCloseMode)
        {

            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.NewOrder}\", \"{Tags.ClOrdId}\":\"{clientOrderId}\", \"{Tags.Symbol}\":\"{isin}\", " +
                   $"\"{Tags.Side}\":{side}, \"{Tags.SettlType}\":\"1\", \"{Tags.TransactTime}\":{timestamp}, " +
                   $"\"{Tags.OrderQty}\":\"{qty}\", \"{Tags.OrdType}\":\"2\", \"{Tags.Price}\":\"{price}\", " +
                   $"\"{Tags.Account}\":{accountId}, \"{Tags.TimeInForce}\":1" + 
                   (isMarginMaket ? $", \"{Tags.ExecInst}\":[\"0\"]" : "") +
                   (isMarginCloseMode ? $", \"{Tags.PositionEffect}\":\"C\", \"{Tags.PositionId}\":{positionId}" : "") +
                   "}";
        }

        public static string CreateCancelOrderMessage(string clientOrderId, string isin, int accountId)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.OrderCancelRequest}\", \"{Tags.Account}\":{accountId}, \"{Tags.OrigClOrdID}\":\"{clientOrderId}\", " +
                   $"\"{Tags.Symbol}\":\"{isin}\", \"{Tags.ClOrdId}\":\"{now.Ticks}\", " + 
                   $"\"{Tags.TransactTime}\":{now.ToUnixTimeMilliseconds() * 1000_000}}}";
        }

        public static string CreateHeartbeatMessage()
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.HeartBeat}\"}}";
        }

        public static string CreatePositionMaintenanceRequestMessage(int accountId, string isin)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.PositionMaintenanceRequest}\", \"{Tags.PosTransType}\":20, \"{Tags.PosMaintAction}\":2, " + 
                   $"\"{Tags.Symbol}\":\"{isin}\", \"{Tags.Account}\":{accountId}}}";
        }

        public static string CreatePositionReportRequestMessage(int accountId)
        {
            return $"{{\"{Tags.MsgType}\":\"{MsgTypes.PositionReportRequest}\", \"{Tags.Account}\":{accountId}}}";
        }
    }
}