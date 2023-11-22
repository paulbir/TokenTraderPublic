using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OceanConnector.Model;
using SharedDataStructures;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SharedTools.Interfaces;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace OceanConnector
{
    public class OceanClient : IHedgeConnector
    {
        readonly char   isinSplitChar                  = '_';
        readonly double secondsToShiftSendingTimestamp = 2;

        //readonly string wsBaseUrl = "wss://gateway-sandbox.oneworldservices.com";
        readonly string wsBaseUrl   = "wss://gateway.oneworldservices.com";

        readonly string restBaseUrl = "https://gateway-sandbox.oneworldservices.com";

        //readonly string restBaseUrl = "https://gateway.oneworldservices.com";

        readonly string balancesUrl     = "/v1/balance";
        readonly string addOrderUrl     = "/v1/order";
        readonly string tradeHistoryUrl = "/v1/history";
        readonly string timeUrl         = "/v1/time";

        List<string> isins;
        string       secretKey;
        Timer        timeoutTimer;
        WebSocket    ws;
        HttpClient   httpClient;

        byte[] secretKeyBytes;

        string wsProdPublicKey = "-";
        string wsProdSecretKey = "-";
        byte[] wsProdSecretKeyBytes;

        public string Name         { get; private set; }
        public string ExchangeName => "ocean";
        public string PublicKey    { get; private set; }

        public event EventHandler                        Connected;
        public event EventHandler                        Disconnected;
        public event EventHandler<ErrorMessage>          ErrorOccured;
        public event EventHandler<BookMessage>           BookUpdateArrived;
        public event EventHandler<BookMessage>           BookSnapshotArrived;
        public event EventHandler<TickerMessage>         TickerArrived;
        public event EventHandler<OrderMessage>          NewOrderAdded;
        public event EventHandler<OrderMessage>          OrderCanceled;
        public event EventHandler<OrderMessage>          OrderReplaced;
        public event EventHandler<List<OrderMessage>>    ActiveOrdersListArrived;
        public event EventHandler<OrderMessage>          ExecutionReportArrived;
        public event EventHandler<List<BalanceMessage>>  BalanceArrived;
        public event EventHandler<List<PositionMessage>> PositionArrived;
        public event EventHandler<List<LimitMessage>>    LimitArrived;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins     = isinsP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            Name = connectorName;

            //wsProdPublicKey = PublicKey;
            //wsProdSecretKey = secretKey;

            secretKeyBytes       = Encoding.UTF8.GetBytes(secretKey);
            wsProdSecretKeyBytes = Encoding.UTF8.GetBytes(wsProdSecretKey);

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer         =  new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("X-API-Key", PublicKey);
            httpClient.BaseAddress = new Uri(restBaseUrl);

            if (isins == null || isins.Count == 0)
            {
                Connected?.Invoke(this, null);
                return;
            }

            ws                 =  new WebSocket(wsBaseUrl + $"/v1/stream?apiKey={wsProdPublicKey}");
            ws.Opened          += Ws_Opened;
            ws.Closed          += Ws_Closed;
            ws.Error           += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;

            ws.Open();
        }

        public void Stop()
        {
            DoStop();
        }

        public void AddHedgeOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, decimal slippagePriceFrac, int requestId)
        {
            //var time = SendRequest<RawTime>(HttpMethod.Get, timeUrl, RequestError.AddOrder);

            RawOrderResponse rawOrderResponse = SendOrder(isin, side, price, qty, slippagePriceFrac, out DateTime sendingTimestamp);
            if (rawOrderResponse == null) return;

            //var time2 = SendRequest<RawTime>(HttpMethod.Get, timeUrl, RequestError.AddOrder);
            //Console.WriteLine("server: " + time2.ServerTime);
            //Console.WriteLine("my: " + sendingTimestamp);
            GetTrades(clientOrderId, sendingTimestamp, rawOrderResponse);
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public async Task SendPreparedOrder()
        {
            throw new NotImplementedException();
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            throw new NotImplementedException();
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            ActiveOrdersListArrived?.Invoke(this, new List<OrderMessage>());
        }

        public void GetPosAndMoney(int requestId)
        {
            var      rawTime      = SendRequest<RawTime>(HttpMethod.Get, timeUrl, RequestError.TradingBalance);
            DateTime localTime    = DateTime.UtcNow;
            double   diffMs        = (localTime - rawTime.ServerTime).TotalMilliseconds;
            string   timeMessage = $"Ocean GetPosAndMoney TIME. ServerTime={rawTime.ServerTime:HH:mm:ss.ffff};LocalTime={localTime:HH:mm:ss.ffff};diffMs={diffMs}";
            ErrorOccured?.Invoke(this, new OceanErrorMessage(RequestError.TradingBalance, timeMessage, "", -1));

            var      rawBalances = SendRequest<RawBalances>(HttpMethod.Get, balancesUrl, RequestError.TradingBalance);

            var limits = new List<LimitMessage>();
            foreach (BalanceMessage balance in rawBalances.Balances)
            {
                limits.Add(new OceanLimitMessage($"{balance.Currency}_STUB_LIMIT", 0, balance.Available, balance.Available * 1000));
            }

            PositionArrived?.Invoke(this, new List<PositionMessage>());
            BalanceArrived?.Invoke(this, rawBalances.Balances);
            LimitArrived?.Invoke(this, limits);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
        }

        void DoStop()
        {
            timeoutTimer?.Stop();

            if (ws?.State == WebSocketState.Open) ws.Close();
        }

        RawOrderResponse SendOrder(string isin, OrderSide side, decimal price, decimal qty, decimal slippagePriceFrac, out DateTime sendingTimestamp)
        {
            string[] tokens             = isin.Split(isinSplitChar);
            string   tradingCurrency    = tokens[0];
            string   settlementCurrency = tokens[1];

            if (slippagePriceFrac != 0) //если проскальзывание не нулевое, то значит цена передана уже с учётом проскальзывания. надо восстановить её назад.
            {
                decimal minStep = CalcMinstep(price);

                if (side == OrderSide.Buy) price /= 1 + slippagePriceFrac;
                else price                       /= 1 - slippagePriceFrac;

                price = Math.Round(price / minStep) * minStep;
            }

            string sideStr = side == OrderSide.Buy ? "buy" : "sell";

            string addOrderMessage = $"{{\"trading\": \"{tradingCurrency}\", \"settlement\": \"{settlementCurrency}\", \"side\": \"{sideStr}\", "           +
                                     $"\"size\": {qty.ToString(CultureInfo.InvariantCulture)}, \"price\": {price.ToString(CultureInfo.InvariantCulture)}, " +
                                     $"\"discretion\": {slippagePriceFrac.ToString(CultureInfo.InvariantCulture)}}}";

            //Console.WriteLine(addOrderMessage);
            //Console.WriteLine($"{side};{price};{qty};{slippagePriceFrac}");
            sendingTimestamp = DateTime.UtcNow;
            var rawOrderResponse = SendRequest<RawOrderResponse>(HttpMethod.Post, addOrderUrl, RequestError.AddOrder, addOrderMessage);
            return rawOrderResponse;
        }

        void GetTrades(string clientOrderId, DateTime sendingTimestamp, RawOrderResponse rawOrderResponse)
        {
            DateTime priorToSendingTimestamp = sendingTimestamp.Subtract(TimeSpan.FromSeconds(secondsToShiftSendingTimestamp));
            var rawTradeHistory = SendRequest<RawTradeHistory>(HttpMethod.Get,
                                                               tradeHistoryUrl,
                                                               RequestError.AddOrder,
                                                               parameters: $"start={priorToSendingTimestamp:O}&limit=100");
            foreach (OceanOrderMessage trade in rawTradeHistory.TradeHistory)
            {
                if (trade.ExchangeOrderId != rawOrderResponse.OrderID) continue;

                trade.SetClientOrderId(clientOrderId);
                trade.SetIsin(isinSplitChar);

                ExecutionReportArrived?.Invoke(this, trade);
            }
        }

        HttpRequestMessage CreateHttpRequest(HttpMethod method, string urlPath, string requestBody, string parameters)
        {
            string signature = CreateSignature(secretKeyBytes, PublicKey, urlPath, requestBody, out string nonceStr);

            string uri = restBaseUrl + urlPath + (string.IsNullOrEmpty(parameters) ? "" : $"?{parameters}");

            //Console.WriteLine(uri);
            var request = new HttpRequestMessage {RequestUri = new Uri(uri), Method = method};
            request.Headers.Add("X-API-Key",   PublicKey);
            request.Headers.Add("X-Nonce",     nonceStr);
            request.Headers.Add("X-Signature", signature);

            if (!string.IsNullOrEmpty(requestBody)) request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            return request;
        }

        T SendRequest<T>(HttpMethod method, string urlPath, RequestError possibleError, string requestBody = null, string parameters = null)
        {
            HttpRequestMessage request = CreateHttpRequest(method, urlPath, requestBody, parameters);

            string jsonString;
            try
            {
                HttpResponseMessage response = httpClient.SendAsync(request).Result;
                jsonString = response.Content.ReadAsStringAsync().Result;

                //Console.WriteLine(jsonString);
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new OceanErrorMessage(possibleError, e.MakeString(), ""));
                return default;
            }

            if (jsonString.Contains("errorCode"))
            {
                JObject responseObject = JObject.Parse(jsonString);
                if (TryProcessRequestResponseError(responseObject, possibleError)) return default;
            }

            T output;
            try { output = JsonConvert.DeserializeObject<T>(jsonString); }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new OceanErrorMessage(possibleError, e.MakeString(), jsonString, 0, "", true));
                return default;
            }

            return output;
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            string signature   = CreateSignature(wsProdSecretKeyBytes, wsProdPublicKey, "/v1/stream", "{\"event\":\"auth\"}", out string nonceStr);
            string authMessage = $"{{\"event\": \"auth\", \"nonce\": \"{nonceStr}\", \"signature\": \"{signature}\"}}";
            ws.Send(authMessage);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            ErrorOccured?.Invoke(this, new OceanErrorMessage(0, "Ocean websocket exception", e.Exception.MakeString(), 0, "", true));
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JObject messageObject = JObject.Parse(e.Message);

            if (TryProcessRequestResponseError(messageObject)) return;

            if (messageObject.TryGetValue("channel", out JToken channelToken) && (string)channelToken == "price") ProcessPrice(messageObject);
            if (messageObject.TryGetValue("authenticated", out JToken authenticatedToken))
            {
                if ((bool)authenticatedToken)
                {
                    Connected?.Invoke(this, null);
                    Subscribe();
                }
                else ErrorOccured?.Invoke(this, new OceanErrorMessage(0, "Websocket authentication failed", ""));
            }
        }

        static string CreateSignature(byte[] secretKeyBytes, string publicKey, string urlPath, string requestBody, out string nonceStr)
        {
            long nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000_000;
            nonceStr = nonce.ToString();

            string message      = publicKey + nonceStr + urlPath + requestBody;
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            byte[] hashBytes;
            using (var encoder = new HMACSHA512(secretKeyBytes)) { hashBytes = encoder.ComputeHash(messageBytes); }

            string hashString = BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            return hashString;
        }

        bool TryProcessRequestResponseError(JObject messageObject, RequestError possibleError = 0)
        {
            if (!messageObject.TryGetValue("errorCode", out JToken errorCodeToken)) return false;

            int errorCode = (int)errorCodeToken;

            //string errorMessage = messageObject.TryGetValue("errorMessage", out JToken errorMessageToken) ? (string)errorMessageToken : "";
            string traceID = messageObject.TryGetValue("traceID", out JToken traceIDToken) ? (string)traceIDToken : "";

            ErrorOccured?.Invoke(this, new OceanErrorMessage(possibleError, "", "", errorCode, traceID));

            return true;
        }

        void Subscribe()
        {
            if (isins == null) return;

            foreach (string isin in isins)
            {
                string[] tokens             = isin.Split(isinSplitChar);

                if (tokens.Length < 2)
                {
                    ErrorOccured?.Invoke(this, new OceanErrorMessage(0, $"Wrong isin format {isin}.", "", isCritical: true));
                    return;
                }

                string   tradingCurrency    = tokens[0];
                string   settlementCurrency = tokens[1];

                string subscribeMessage =
                    $"{{\"event\": \"subscribe\", \"channel\": \"price\", \"trading\": \"{tradingCurrency}\", \"settlement\": \"{settlementCurrency}\"}}";
                ws.Send(subscribeMessage);
            }
        }

        void ProcessPrice(JObject messageObject)
        {
            if (!messageObject.TryGetValue("trading",    out JToken tradingCurrencyToken)    ||
                !messageObject.TryGetValue("settlement", out JToken settlementCurrencyToken) ||
                !messageObject.TryGetValue("payload",    out JToken payloadToken)) return;

            var payload = (JObject)payloadToken;
            if (!payload.TryGetValue("priceAsk", out JToken askToken) || !payload.TryGetValue("priceBid", out JToken bidToken)) return;

            string  isin = $"{(string)tradingCurrencyToken}{isinSplitChar}{settlementCurrencyToken}";
            decimal bid  = (decimal)bidToken;
            decimal ask  = (decimal)askToken;
            var     book = new OceanBookMessage(isin, bid, ask);

            BookSnapshotArrived?.Invoke(this, book);
        }

        static decimal CalcMinstep(decimal price)
        {
            string priceStr                             = price.ToString(CultureInfo.InvariantCulture);
            int    numDecimalPlaces                     = priceStr.Substring(priceStr.IndexOf(".", StringComparison.InvariantCulture) + 1).Length;
            if (numDecimalPlaces == 0) numDecimalPlaces = 1;
            decimal minStep                             = 1 / (decimal)Math.Pow(10, numDecimalPlaces);
            return minStep;
        }
    }
}