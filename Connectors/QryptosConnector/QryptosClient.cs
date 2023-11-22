using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Jose;
using Newtonsoft.Json;
using PusherClient;
using QryptosConnector.Model;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;

namespace QryptosConnector
{
    public class QryptosClient : ITradeConnector
    {
        readonly string baseUrl = "https://api.liquid.com";
        readonly string cryptoAccountsUrl = "/crypto_accounts";
        readonly string productsUrl = "/products";
        readonly string ordersUrl = "/orders";
        readonly string balanceUrl = "/accounts/balance";
        readonly string freeBalanceUrls = "/accounts/";

        readonly Encoding encoding = Encoding.ASCII;
        
        Pusher pusher;
        List<string> isins;
        string publicKey;
        string secretKey;
        System.Timers.Timer timeoutTimer;
        string tradingAccountId = "";
        HttpWebRequest preparedOrder;
        string preparedClientOrderId;

        readonly ConcurrentMap<long, string> clientIdByOrderId = new ConcurrentMap<long, string>();
        readonly Dictionary<string, BookData> bookDatasByIsin = new Dictionary<string, BookData>();
        Dictionary<string, Product> productsByIsin;

        readonly object ordersLocker = new object();

        public string Name { get; private set; }
        public string ExchangeName => "qryptos";
        public string PublicKey { get; private set; }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;
        public event EventHandler<OrderMessage> NewOrderAdded;
        public event EventHandler<OrderMessage> OrderCanceled;
        public event EventHandler<OrderMessage> OrderReplaced;
        public event EventHandler<List<OrderMessage>> ActiveOrdersListArrived;
        public event EventHandler<OrderMessage> ExecutionReportArrived;
        public event EventHandler<List<BalanceMessage>> BalanceArrived;
        public event EventHandler<List<PositionMessage>> PositionArrived;

        //StreamWriter sw;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins = isinsP;
            publicKey = publicKeyP;
            PublicKey = publicKeyP;
            secretKey = secretKeyP;
            Name = connectorName;

            //sw = new StreamWriter($"{publicKeyP}.txt") {AutoFlush = true};

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer = new System.Timers.Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {            
            //pusher = new Pusher("333c165e73b3b55f8e4b");
            pusher = new Pusher("-");
            pusher.ConnectionStateChanged += Pusher_ConnectionStateChanged;
            pusher.Connected += Pusher_Connected;
            pusher.Error += Pusher_Error;

            SetProducts();
            SetTradingAccount();

            //AddOrder("myorder", "CRPTBTC", OrderSide.Buy, 0.0000571m, 3000, 1);

            pusher.UnbindAll();
            bookDatasByIsin.Clear();

            var quotedCurrencies = new HashSet<string>(productsByIsin.Values.Select(product => product.QuotedCurrency));
            foreach (string quotedCurrency in quotedCurrencies)
            {
                Channel ordersChannel = pusher.Subscribe($"user_{tradingAccountId}_account_{quotedCurrency.ToLowerInvariant()}_orders");
                ordersChannel.Bind("updated", ParseOrder);
            }

            if (isins != null)
            {
                foreach (string isin in isins)
                {
                    if (!productsByIsin.TryGetValue(isin, out _)) throw new ExecutionFlowException($"Isin {isin} was not found in products.");

                    Channel bookChannelBuy = pusher.Subscribe($"price_ladders_cash_{isin.ToLowerInvariant()}_buy");
                    Channel bookChannelSell = pusher.Subscribe($"price_ladders_cash_{isin.ToLowerInvariant()}_sell");
                    bookChannelBuy.Bind("updated", ParseOrderBookBidsData);
                    bookChannelSell.Bind("updated", ParseOrderBookAsksData);

                    bookDatasByIsin.Add(isin, new BookData());
                }
            }

            var state = pusher.Connect();
        }

        public void Stop()
        {
            //sw.Close();
            timeoutTimer?.Close();
            pusher?.UnbindAll();
            pusher?.Disconnect();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);

            if (!productsByIsin.TryGetValue(isin, out Product product))
                throw new ExecutionFlowException($"Isin {isin} was not found in products.");

            string addOrderMessage = MessageCreator.CreateAddOrderMessage(product.Id, sideStr, priceStr, qtyStr);
            var newOrder = SendRequest<QryptosOrderMessage>("POST", true, ordersUrl, (int)RequestError.AddOrder, addOrderMessage);
            if (newOrder == null) return;

            if (!clientIdByOrderId.TryAdd(newOrder.ExchangeOrderId, clientOrderId))
            {
                var error = new QryptosErrorMessage((int)RequestError.AddOrder,
                                                    $"Map already contains orderId={newOrder.ExchangeOrderId} or clientOrderId={clientOrderId}",
                                                    "");
                ErrorOccured?.Invoke(this, error);
            }

            newOrder.SetOrderId(clientOrderId);
            NewOrderAdded?.Invoke(this, newOrder);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr = qty.ToString(CultureInfo.InvariantCulture);

            if (!productsByIsin.TryGetValue(isin, out Product product))
                throw new ExecutionFlowException($"Isin {isin} was not found in products.");

            string addOrderMessage = MessageCreator.CreateAddOrderMessage(product.Id, sideStr, priceStr, qtyStr);

            preparedOrder = CreateHttpWebRequest("POST", ordersUrl, true, addOrderMessage);
            preparedClientOrderId = clientOrderId;
        }

        public Task SendPreparedOrder()
        {
            return Task.Run(() =>
                            {
                                if (preparedOrder == null || string.IsNullOrEmpty(preparedClientOrderId)) return;

                                string newOrderResponseString = GetResponse(preparedOrder, ordersUrl, (int)RequestError.AddOrder);
                                if (newOrderResponseString == null) return;

                                var newOrder = JsonConvert.DeserializeObject<QryptosOrderMessage>(newOrderResponseString);
                                newOrder.SetOrderId(preparedClientOrderId);

                                if (!clientIdByOrderId.TryAdd(newOrder.ExchangeOrderId, preparedClientOrderId))
                                {
                                    var error = new QryptosErrorMessage((int)RequestError.AddOrder,
                                                                        $"Map already contains orderId={newOrder.ExchangeOrderId} or clientOrderId={preparedClientOrderId}",
                                                                        "");
                                    ErrorOccured?.Invoke(this, error);
                                }

                                NewOrderAdded?.Invoke(this, newOrder);

                                preparedOrder = null;
                                preparedClientOrderId = null;

                                //sw.WriteLine($"{DateTime.UtcNow:s};ADDORDER;{newOrder.OrderId};{newOrder.ExchangeOrderId};{newOrder.Isin};{newOrder.Side};{newOrder.Price};{newOrder.Qty}");
                            });
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            string orderIdStr = clientIdByOrderId.Reverse.TryGetValue(clientOrderId, out long orderId) ? orderId.ToString() : clientOrderId;

            var cancelledOrder = SendRequest<QryptosOrderMessage>("PUT", true, $"{ordersUrl}/{orderIdStr}/cancel", (int)RequestError.CancelOrder);
            if (cancelledOrder == null) return;

            cancelledOrder.SetOrderId(clientOrderId);
            OrderCanceled?.Invoke(this, cancelledOrder);

            clientIdByOrderId.Remove(orderId, clientOrderId);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            var rawLiveOrders = SendRequest<RawActiveOrders>("GET", true, ordersUrl, (int)RequestError.ActiveOrders, null, "status=live", "limit=1000");
            var rawPartiallyFilledOrders =
                SendRequest<RawActiveOrders>("GET", true, ordersUrl, (int)RequestError.ActiveOrders, null, "status=partially_filled", "limit=1000");
            if (rawLiveOrders?.Orders == null && rawPartiallyFilledOrders?.Orders == null) return;

            var activeOrders = new List<QryptosOrderMessage>();
            if (rawLiveOrders?.Orders?.Count > 0) activeOrders.AddRange(rawLiveOrders.Orders);
            if (rawPartiallyFilledOrders?.Orders?.Count > 0) activeOrders.AddRange(rawPartiallyFilledOrders.Orders);

            foreach (QryptosOrderMessage activeOrder in activeOrders)
            {
                if (clientIdByOrderId.Forward.TryGetValue(activeOrder.ExchangeOrderId, out string clientOrderId)) activeOrder.SetOrderId(clientOrderId);
                else activeOrder.SetOrderId(activeOrder.ExchangeOrderId.ToString());
            }

            ActiveOrdersListArrived?.Invoke(this, activeOrders.Select(order => (OrderMessage)order).ToList());
        }

        public void GetPosAndMoney(int requestId)
        {
            var totalBalances = SendRequest<List<QryptosBalanceMessage>>("GET", true, balanceUrl, (int)RequestError.TradingBalance);
            if (totalBalances == null) return;

            var freeBalances = new List<BalanceMessage>();
            foreach (QryptosBalanceMessage totalBalance in totalBalances)
            {
                if (totalBalance.Available == 0) continue;

                var freeBalance = SendRequest<QryptosBalanceMessage>("GET", true, freeBalanceUrls + totalBalance.Currency, (int)RequestError.TradingBalance);
                if (freeBalance == null) continue;

                freeBalances.Add(freeBalance);
            }

            BalanceArrived?.Invoke(this, freeBalances);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Pusher_ConnectionStateChanged(object sender, ConnectionState state)
        {
            if (state == ConnectionState.Disconnected || state == ConnectionState.DisconnectionFailed || state == ConnectionState.ConnectionFailed)
            {
                Disconnected?.Invoke(this, null);
                timeoutTimer?.Stop();
            }
        }

        void Pusher_Connected(object sender)  
        {
            Connected?.Invoke(this, null);
            timeoutTimer?.Start();

            if (isins == null) return;

            foreach (string isin in isins)
            {
                if (!productsByIsin.TryGetValue(isin, out Product product)) throw new ExecutionFlowException($"Isin {isin} was not found in products.");
                GetAndSendBookSnapshot(product);
            }
        }

        void Pusher_Error(object sender, PusherException exception)
        {
            exception.Data["tokenId"] = publicKey;
            throw exception;
        }

        void ParseOrder(string channel, string message)
        {
            lock (ordersLocker)
            {
                var order = JsonConvert.DeserializeObject<QryptosOrderMessage>(message);

                //эвент о новой или снятой заявке уже был вызван сразу после отправки по результатам ответа
                if (order.Status == "live" || order.Status == "cancelled") return;

                if (!clientIdByOrderId.Forward.TryGetValue(order.ExchangeOrderId, out string clientOrderId)) return;
                order.SetOrderId(clientOrderId);

                switch (order.Status)
                {
                    case "filled":
                        clientIdByOrderId.Remove(order.ExchangeOrderId, clientOrderId);
                        ExecutionReportArrived?.Invoke(this, order);
                        break;

                    case "partially_filled":
                        ExecutionReportArrived?.Invoke(this, order);
                        break;
                }

                //sw.WriteLine($"{DateTime.UtcNow:s};EXECUTION;{clientOrderId};{message}");
            }
        }

        void ParseOrderBookBidsData(string channel, string message)
        {
            timeoutTimer?.Stop();

            ProcessPriceLevels(channel, message, OrderSide.Buy);

            timeoutTimer?.Start();
        }

        void ParseOrderBookAsksData(string channel, string message)
        {
            timeoutTimer?.Stop();

            ProcessPriceLevels(channel, message, OrderSide.Sell);

            timeoutTimer?.Start();
        }

        void GetAndSendBookSnapshot(Product product)
        {
            var snapshot = SendRequest<QryptosBookMessage>("GET", false, $"{productsUrl}/{product.Id}/price_levels", 0, null, "full=1");
            if (snapshot == null) throw new RequestFailedException($"Couldn't get order book for isin {product.Isin} for tokenId={publicKey}.");

            snapshot.SetIsin(product.Isin);
            BookSnapshotArrived?.Invoke(this, snapshot);
        }

        void SetProducts()
        {
            var productsList = SendRequest<List<Product>>("GET", false, productsUrl, 0);
            if (productsList == null) throw new RequestFailedException($"Couldn't get products list for tokenId={publicKey}.");

            productsByIsin = productsList.ToDictionary(keySelector: prod => prod.Isin, elementSelector: prod => prod);
        }

        void SetTradingAccount()
        {
            var accounts = SendRequest<List<Account>>("GET", true, cryptoAccountsUrl, 0);
            if (accounts == null || accounts.Count == 0) throw new RequestFailedException($"Couldn't get crypto accounts for tokenId={publicKey}.");

            string pusherChannel = accounts[0].PusherChannel;
            if (pusherChannel == null) throw new RequestFailedException($"PusherChannel is null for account for tokenId={publicKey}.");

            string[] tokens = pusherChannel.Split('_');
            if (tokens.Length < 4) throw new RequestFailedException($"Wrong PusherChannel={pusherChannel} for tokenId={publicKey}.");

            tradingAccountId = tokens[1];
        }

        void ProcessPriceLevels(string channel, string message, OrderSide side)
        {
            List<QryptosPriceLevel> priceLevels = null;
            try
            {
                priceLevels = JsonConvert.DeserializeObject<List<List<string>>>(message).Select(level => new QryptosPriceLevel(level)).ToList();
            }
            catch (Exception ex)
            {
                ErrorOccured?.Invoke(this, new QryptosErrorMessage(0, ex.Message, message));
            }

            if (priceLevels == null) return;

            string[] tokens = channel.Split('_');
            if (tokens.Length < 5) return;

            string isin = tokens[3].ToUpperInvariant();
            if (!bookDatasByIsin.TryGetValue(isin, out BookData bookData)) return;

            if (side == OrderSide.Buy) bookData.Bids = priceLevels;
            else bookData.Asks = priceLevels;

            if (bookData.CanSend)
            {
                BookSnapshotArrived?.Invoke(this, new QryptosBookMessage(isin, bookData.Bids, bookData.Asks));
                bookData.ClearPrices();
            }
        }

        T SendRequest<T>(string method, bool needSign, string command, int possibleErrorCode, string body = null, params object[] parameters)
        {
            string relativeUrl = CreateRelativeUrl(command, parameters);
            string jsonString = QueryString(method, relativeUrl, needSign, body, possibleErrorCode);

            if (jsonString == null) return default(T);

            T output;
            try
            {
                output = JsonConvert.DeserializeObject<T>(jsonString);
            }
            catch (Exception ex)
            {
                ex.Data["Request"] = relativeUrl;
                ex.Data["Response"] = jsonString;
                throw;
            }

            return output;
        }

        static string CreateRelativeUrl(string command, object[] parameters)
        {
            string relativeUrl = command;
            if (parameters.Length != 0)
            {
                relativeUrl += "?" + string.Join("&", parameters);
            }

            return relativeUrl;
        }

        string QueryString(string method, string relativeUrl, bool needSign, string body, int possibleErrorCode)
        {
            HttpWebRequest request = CreateHttpWebRequest(method, relativeUrl, needSign, body);
            return GetResponse(request, relativeUrl, possibleErrorCode);
        }

        HttpWebRequest CreateHttpWebRequest(string method, string relativeUrl, bool needSign, string body)
        {
            HttpWebRequest request = WebRequest.CreateHttp($"{baseUrl}{relativeUrl}");
            request.Method = method;
            request.Timeout = Timeout.Infinite;

            if (body != null)
            {
                request.ContentType = "application/json";
                using (Stream stream = request.GetRequestStream())
                {
                    byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }

            request.Headers["X-Quoine-API-Version"] = "2";
            if (needSign)
            {
                string signature = MakeSignature(relativeUrl);
                request.Headers["X-Quoine-Auth"] = signature;
            }

            return request;
        }

        string MakeSignature(string relativeUrl)
        {
            long nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();// + 10628607;

            var payload = new Dictionary<string, string> {{"path", relativeUrl}, {"nonce", nonce.ToString()}, {"token_id", publicKey}};

            string signature = JWT.Encode(payload, Encoding.UTF8.GetBytes(secretKey), JwsAlgorithm.HS256);

            return signature;
        }

        string GetResponse(HttpWebRequest request, string relativeUrl, int possibleErrorCode)
        {
            WebResponse response;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    ex.Data["Request"] = relativeUrl;
                    Stream stream = exResponse?.GetResponseStream();
                    if (stream == null) throw;

                    using (var sr = new StreamReader(stream))
                    {
                        string responseString = sr.ReadToEnd();
                        ex.Data["Response"] = responseString;

                        RawError errorsObject;
                        try 
                        {
                            errorsObject = JsonConvert.DeserializeObject<RawError>(responseString);
                        }
                        catch (Exception jex)
                        {
                            jex.Data["Request"] = relativeUrl;
                            jex.Data["Response"] = responseString;
                            throw;
                        }

                        if (!ProcessError(errorsObject, possibleErrorCode)) throw;
                        return null;
                    }
                }
            }

            using (var sr = new StreamReader(response.GetResponseStream(), encoding))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        bool ProcessError(RawError error, int possibleErrorCode)
        {
            if (string.IsNullOrEmpty(error.Message))
            {
                ErrorOccured?.Invoke(this, new QryptosErrorMessage(possibleErrorCode, error.Message, ""));
                return true;
            }

            if (error.Errors != null && error.Errors.Count > 0)
            {
                ErrorOccured?.Invoke(this,
                                     error.Errors.Count == 1
                                         ? new QryptosErrorMessage(possibleErrorCode, error.Errors[0], "")
                                         : new QryptosErrorMessage(possibleErrorCode, string.Join(';', error.Errors), ""));

                return true;
            }

            return false;
        }
    }
}