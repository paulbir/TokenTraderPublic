namespace CGCXConnector
{
    static class MessageCreator
    {
        public static string CreateWebLoginMessage(string username, string password, int requestId)
        {
            string payload = $"{{\\\"UserName\\\":\\\"{username}\\\", \\\"Password\\\":\\\"{password}\\\"}}";
            return CreateMessage(0, requestId, "WebAuthenticateUser", payload);
        }

        public static string CreateLoginMessage(string publicKey, int userId, long nonce, string signature, int requestId)
        {
            string payload = $"{{\\\"APIKey\\\":\\\"{publicKey}\\\", \\\"UserId\\\":\\\"{userId}\\\", " +
                             $"\\\"Nonce\\\":\\\"{nonce}\\\", \\\"Signature\\\":\\\"{signature}\\\"}}";
            return CreateMessage(0, requestId, "AuthenticateUser", payload);
        }

        public static string CreateGetUserInfoMessage(int requestId)
        {
            string payload = $"{{}}";
            return CreateMessage(0, requestId, "GetUserInfo", payload);
        }

        public static string CreateGetInstrumentsMessage(int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1}}";
            return CreateMessage(0, requestId, "GetInstruments", payload);
        }

        public static string CreateSubscribeToBookMessage(string isin, int requestId)
        {
            string payload = $"{{ \\\"OMSId\\\":1, \\\"Symbol\\\":\\\"{isin}\\\", \\\"Depth\\\":100 }}";
            return CreateMessage(0, requestId, "SubscribeLevel2", payload);
        }

        public static string CreateUnsubscribeFromBookMessage(string isinId, int requestId)
        {
            string payload = $"{{ \\\"OMSId\\\":1, \\\"InstrumentId\\\":\\\"{isinId}\\\" }}";
            return CreateMessage(0, requestId, "UnsubscribeLevel2", payload);
        }

        public static string CreateSubscribeToTickerMessage(int isinId, int requestId)
        {
            string payload = $"{{ \\\"OMSId\\\":1, \\\"InstrumentId\\\":{isinId}, \\\"Interval\\\":60, \\\"IncludeLastCount\\\":0 }}";
            return CreateMessage(0, requestId, "SubscribeTicker", payload);
        }

        public static string CreateSubscribeToReportsMessage(int accountId, int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}}}";
            return CreateMessage(0, requestId, "SubscribeAccountEvents", payload);
        }

        public static string CreateAddOrderMessage(int accountId, long clientOrderId, int isinId, int side, string price, string qty, int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}, \\\"ClientOrderId\\\":{clientOrderId}, \\\"Quantity\\\":{qty}, " +
                             $"\\\"DisplayQuantity\\\":0, \\\"UseDisplayQuantity\\\":false, \\\"LimitPrice\\\":{price}, \\\"OrderIdOCO\\\":0, " +
                             $"\\\"OrderType\\\":2, \\\"PegPriceType\\\":1, \\\"InstrumentId\\\":{isinId}, \\\"TrailingAmount\\\":0, " +
                             $"\\\"LimitOﬀset\\\":0, \\\"Side\\\":{side}, \\\"StopPrice\\\":0, \\\"TimeInForce\\\":1}}";

            return CreateMessage(0, requestId, "SendOrder", payload);
        }

        public static string CreateCancelOrderMessage(int accountId, long clientOrderId, long orderId, int requestId)
        {
            //, \\\"OrderId\\\":{orderId}
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}, \\\"ClientOrderId\\\":{clientOrderId}, \\\"OrderId\\\":{orderId}}}";
            return CreateMessage(0, requestId, "CancelOrder", payload);
        }

        public static string CreateReplaceOrderMessage(int accountId,
                                                       long oldOrderId,
                                                       long newClientOrderId,
                                                       int isinId,
                                                       int side,
                                                       string price,
                                                       string qty,
                                                       int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"OrderIdToReplace\\\":{oldOrderId}, \\\"ClientOrderId\\\":{newClientOrderId}, " +
                             $"\\\"OrderType\\\":2, \\\"Side\\\":{side}, \\\"AccountId\\\":{accountId}, \\\"InstrumentId\\\":{isinId}, " +
                             $"\\\"TrailingAmount\\\":0, \\\"LimitOﬀset\\\":0, \\\"DisplayQuantity\\\":0, \\\"LimitPrice\\\":{price}, " +
                             $"\\\"StopPrice\\\":0, \\\"PegPriceType\\\":1, \\\"TimeInForce\\\":1, \\\"OrderIdOCO\\\":0, \\\"Quantity\\\":{qty}}}";
            return CreateMessage(0, requestId, "CancelReplaceOrder", payload);
        }

        public static string CreateGetActiveOrdersMessage(int accountId, int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}}}";
            return CreateMessage(0, requestId, "GetOpenOrders", payload);
        }

        public static string CreateGetTradingBalanceMessage(int accountId, int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}}}";
            return CreateMessage(0, requestId, "GetAccountPositions", payload);
        }

        public static string CreateGetProductsMessage(int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1}}";
            return CreateMessage(0, requestId, "GetProducts", payload);
        }

        public static string CreateWithdrawTicketMessage(int accountId, int productId, decimal amount, string address, int requestId)
        {
            string payload = $"{{\\\"OMSId\\\":1, \\\"AccountId\\\":{accountId}, \\\"ProductId\\\":{productId}, \\\"Amount\\\":{amount}, " +
                             $"\\\"TemplateType\\\":\\\"ToExternalBitcoinAddress\\\", " +
                             $"\\\"TemplateForm\\\":\\\"{{\\\"TemplateType\\\":\\\"ToExternalBitcoinAddress\\\", " +
                                                        $"\\\"Comment\\\":\\\"ApiWithdraw\\\", " + 
                                                        $"\\\"ExternalAddress\\\":\\\"{address}\\\"}}\\\"}}";
            return CreateMessage(0, requestId, "CreateWithdrawTicket", payload);
        }

        static string CreateMessage(int messageType, int requestId, string function, string payload)
        {
            return $"{{\"m\":{messageType}, \"i\":{requestId}, \"n\":\"{function}\", \"o\":\"{payload}\"}}";
        }
    }
}