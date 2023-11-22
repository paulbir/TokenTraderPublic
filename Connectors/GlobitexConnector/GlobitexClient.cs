using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using GlobitexConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;
using Timer = System.Timers.Timer;

namespace GlobitexConnector
{
    public class GlobitexClient : ITradeConnector
    {
        const           int      executionReportTimeoutMs = 1000;
        static readonly Encoding Encoding                 = Encoding.ASCII;
        static readonly string   wsBaseUrl                = "wss://stream.globitex.com/market-data";

        readonly string restEndPoint = "https://api.globitex.com";
        readonly string apiV1Path    = "/api/1/";

        readonly string apiV2Path = "/api/2/";

        //readonly string restPath = "/api/1/";
        //readonly string restBaseUrl;

        readonly string                                             accountsBalanceUrl = "payment/accounts";
        readonly string                                             addOrderUrl        = "trading/new_order";
        readonly string                                             activeOrdersUrl    = "trading/orders/active";
        readonly string                                             cancelOrderUrl     = "trading/cancel_order";
        readonly string                                             orderStateUrl      = "trading/order";
        readonly string                                             myTradesUrl        = "trading/trades";
        readonly Dictionary<string, IsinData>                       isinDatas          = new Dictionary<string, IsinData>();
        readonly Dictionary<string, GlobitexExecutionReportMessage> sentOrders         = new Dictionary<string, GlobitexExecutionReportMessage>();
        readonly HashSet<long>                                      processedTrades    = new HashSet<long>();
        readonly object                                             requestLocker      = new object();

        long           lastTradesRequestTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        WebSocket      ws;
        string         publicKey;
        string         secretKey;
        Timer          timeoutTimer;
        Timer          executionReportTimer;
        string         tradingAccount = "";
        HttpWebRequest preparedOrder;
        string         preparedBody;
        long           prevnonce;

        public string Name         { get; private set; }
        public string ExchangeName => "globitex";
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

        //Timer testTradeTimer;

        public void Init(List<string> isins, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorNameP)
        {
            if (isins?.Count > 0)
                foreach (string isin in isins)
                    isinDatas.Add(isin, new IsinData());

            string[] publicKeyTokens = publicKeyP.Split('_');
            publicKey      = publicKeyTokens[0];
            secretKey      = secretKeyP;
            PublicKey      = publicKeyP;
            Name           = connectorNameP;
            tradingAccount = publicKeyTokens[1];

            if (!string.IsNullOrEmpty(publicKeyP) && !string.IsNullOrEmpty(secretKeyP))
            {
                executionReportTimer         =  new Timer(executionReportTimeoutMs);
                executionReportTimer.Elapsed += ExecutionReportTimer_Elapsed;
            }

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer         =  new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            if (!string.IsNullOrEmpty(PublicKey) && !string.IsNullOrEmpty(secretKey)) executionReportTimer?.Start();

            //testTradeTimer         =  new Timer(60000);
            //testTradeTimer.Elapsed += TestTradeTimer_Elapsed;
            //testTradeTimer.Start();

            if (isinDatas.Count == 0)
            {
                //timeoutTimer?.Start();
                Connected?.Invoke(this, null);
                return;
            }

            ws = new WebSocket(wsBaseUrl) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};

            ws.Opened          += Ws_Opened;
            ws.Closed          += Ws_Closed;
            ws.Error           += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();

            GetAndSendExecutionForOrder();
        }

        //private void TestTradeTimer_Elapsed(object sender, ElapsedEventArgs e)
        //{
        //    testTradeTimer.Stop();

        //    var trade = new GlobitexExecutionReportMessage("testTrade1", "BTCEUR", OrderSide.Buy, "Filled", 39600, 0.01m, 0, DateTime.UtcNow, "");
        //    trade.UpdateOnTrade(39600, 0.01m, 0);
        //    ExecutionReportArrived?.Invoke(this, trade);
        //}

        public void Stop()
        {
            DoStop();
        }

        public void AddOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr  = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr   = qty.ToString(CultureInfo.InvariantCulture);

            var newOrderResponse = SendRequest<RawExecutionReport<GlobitexExecutionReportMessage>>("POST",
                                                                                                   apiV1Path,
                                                                                                   addOrderUrl,
                                                                                                   "",
                                                                                                   RequestError.AddOrder,
                                                                                                   true,
                                                                                                   $"clientOrderId={clientOrderId}",
                                                                                                   $"account={tradingAccount}",
                                                                                                   $"symbol={isin}",
                                                                                                   $"side={sideStr}",
                                                                                                   $"quantity={qtyStr}",
                                                                                                   $"price={priceStr}",
                                                                                                   "type=limit");
            if (newOrderResponse == null) return;

            ProcessNewOrder(newOrderResponse);
        }

        public void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId)
        {
            string sideStr  = side == OrderSide.Buy ? "buy" : "sell";
            string priceStr = price.ToString(CultureInfo.InvariantCulture);
            string qtyStr   = qty.ToString(CultureInfo.InvariantCulture);

            preparedBody = CreateBody(new object[]
                                      {
                                          $"clientOrderId={clientOrderId}", $"account={tradingAccount}", $"symbol={isin}", $"side={sideStr}",
                                          $"quantity={qtyStr}", $"price={priceStr}", "type=limit"
                                      });
            preparedOrder = CreateHttpWebRequest("POST", apiV1Path, addOrderUrl, preparedBody);
        }

        public Task SendPreparedOrder()
        {
            return Task.Run(() =>
                            {
                                if (preparedOrder == null || preparedBody == null) return;

                                string newOrderMessageString = GetResponse(preparedOrder, preparedBody, "", RequestError.AddOrder, true);

                                try
                                {
                                    var newOrderResponse =
                                        JsonConvert.DeserializeObject<RawExecutionReport<GlobitexExecutionReportMessage>>(newOrderMessageString);
                                    ProcessNewOrder(newOrderResponse);
                                }
                                catch (Exception e)
                                {
                                    e.Data["Response"] = newOrderMessageString;
                                    throw;
                                }
                                finally
                                {
                                    preparedOrder = null;
                                    preparedBody  = null;
                                }
                            });
        }

        public void CancelOrder(string clientOrderId, int requestId)
        {
            //пробуем снять два раза, если пришли null ответы или пришёл Internal error.
            if (!DoTryCancelOrder(clientOrderId, false)) DoTryCancelOrder(clientOrderId, true);
        }

        public void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId)
        {
            throw new NotImplementedException();
        }

        public void GetActiveOrders(int requestId)
        {
            var activeOrdersMessage =
                SendRequest<RawOrdersMessage>("GET", apiV2Path, activeOrdersUrl, "", RequestError.ActiveOrders, true, $"account={tradingAccount}");

            if (activeOrdersMessage == null) return;

            ActiveOrdersListArrived?.Invoke(this, activeOrdersMessage.ActiveOrders);
        }

        public void GetPosAndMoney(int requestId)
        {
            var accounts = SendRequest<RawAccountsMessage>("GET", apiV1Path, accountsBalanceUrl, "", RequestError.TradingBalance);

            if (accounts?.BalancesByAccount == null || accounts.BalancesByAccount.Count == 0) return;

            //один раз установим торговый акаунт, с которого будем торговать
            //if (tradingAccount == "")
            //{
            //    tradingAccount = accounts.FirstAccount;
            //    if (accounts.BalancesByAccount.Count > 1)
            //    {
            //        var error = new GlobitexErrorMessage((int)RequestError.TradingBalance,
            //                                             $"Choosing first account for trading: {tradingAccount}.",
            //                                             $"Login {publicKey} contains more than one account: {string.Join(';', accounts.BalancesByAccount.Keys)}");
            //        ErrorOccured?.Invoke(this, error);
            //    }

            //    //не нужно отправлять балансы, потому что первый вызов делаем сам коннектор. пришедшие балансы будут неожиданными.
            //    return;
            //}

            if (!accounts.BalancesByAccount.TryGetValue(tradingAccount, out List<BalanceMessage> balances))
            {
                throw new RequestFailedException($"Couldn't find trading account {tradingAccount} " +
                                                 $"among received accounts {string.Join(';', accounts.BalancesByAccount.Keys)}");
            }

            BalanceArrived?.Invoke(this, balances);
        }

        void ExecutionReportTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            executionReportTimer.Stop();

            try { GetAndSendExecutionForOrder(); }
            finally { executionReportTimer.Start(); }
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void DoStop()
        {
            executionReportTimer?.Stop();
            timeoutTimer?.Stop();
            SetAllIsinsNotReceivedSnapshot();
            sentOrders.Clear();
            processedTrades.Clear();

            if (ws?.State == WebSocketState.Open) ws.Close();
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            timeoutTimer?.Start();
            Connected?.Invoke(this, null);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            DoStop();
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            timeoutTimer?.Stop();

            //приходят только стаканы
            var                 rawBook  = JsonConvert.DeserializeObject<RawBookMessage>(e.Message);
            GlobitexBookMessage snapshot = rawBook.Snapshot;
            GlobitexBookMessage update   = rawBook.Update;

            if (snapshot != null && isinDatas.TryGetValue(snapshot.Isin, out IsinData isinData))
            {
                isinData.IsSnapshotReceived = true;
                BookSnapshotArrived?.Invoke(this, snapshot);

                isinData.Bid = snapshot.BestBid;
                isinData.Ask = snapshot.BestAsk;
            }

            if (update != null && isinDatas.TryGetValue(update.Isin, out isinData) && isinData.IsSnapshotReceived)
            {
                BookUpdateArrived?.Invoke(this, update);

                if (update.BestBid > 0) isinData.Bid  = update.BestBid;
                if (update.BestAsk > 0) isinData.Ask  = update.BestAsk;
                if (update.Last    > 0) isinData.Last = update.Last;

                if (isinData.Last > 0) TickerArrived?.Invoke(this, new GlobitexTickerMessage(isinData.Bid, isinData.Ask, isinData.Last));
            }

            timeoutTimer?.Start();
        }

        void ProcessNewOrder(RawExecutionReport<GlobitexExecutionReportMessage> newOrderResponse)
        {
            if (newOrderResponse?.ExecutionReport != null && newOrderResponse.ExecutionReport.Status != "rejected")
                NewOrderAdded?.Invoke(this, newOrderResponse.ExecutionReport);
            else
            {
                if (newOrderResponse?.Reject != null)
                {
                    ErrorOccured?.Invoke(this,
                                         new GlobitexErrorMessage((int)RequestError.AddOrder,
                                                                  $"Order add rejected with message: {newOrderResponse.Reject}",
                                                                  newOrderResponse.Reject.ClientOrderId));
                }
                else if (newOrderResponse?.ExecutionReport?.Status == "rejected")
                {
                    ErrorOccured?.Invoke(this,
                                         new GlobitexErrorMessage((int)RequestError.AddOrder,
                                                                  $"Order add rejected for order: {newOrderResponse.ExecutionReport}",
                                                                  newOrderResponse.ExecutionReport.OrderId));
                }

                return;
            }

            sentOrders.TryAdd(newOrderResponse.ExecutionReport.OrderId, newOrderResponse.ExecutionReport.CreateDeepCopy());
        }

        void GetAndSendExecutionForOrder()
        {
            var myTradesMessage = SendRequest<RawMyTradesMessage>("GET",
                                                                  apiV1Path,
                                                                  myTradesUrl,
                                                                  "",
                                                                  RequestError.Executions,
                                                                  true,
                                                                  "by=ts",
                                                                  "startIndex=0",
                                                                  "maxResults=1000",
                                                                  $"account={tradingAccount}",
                                                                  $"from={lastTradesRequestTimestamp}");

            if (myTradesMessage == null) return;

            lastTradesRequestTimestamp = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromSeconds(1)).ToUnixTimeMilliseconds();

            foreach (GlobitexTradeMessage myTrade in myTradesMessage.MyTrades)
            {
                if (processedTrades.Contains(myTrade.TradeId)) continue;
                if (!sentOrders.TryGetValue(myTrade.ClientOrderId, out GlobitexExecutionReportMessage executedOrder)) continue;

                //создаём новый объект каждый раз, потому что иначе возможно проблемы, когда мы поменяем количество в сделке в старом объекте. 
                //пользователь объекта может увидеть только последнее значение количества.
                GlobitexExecutionReportMessage executedOrderToSend = executedOrder.CreateDeepCopy();
                executedOrder.UpdateOnTrade(myTrade.TradePrice, myTrade.TradeQty, myTrade.TradeFee);
                executedOrderToSend.UpdateOnTrade(myTrade.TradePrice, myTrade.TradeQty, myTrade.TradeFee);

                ExecutionReportArrived?.Invoke(this, executedOrderToSend);
                if (executedOrder.RemainingQty == 0) sentOrders.Remove(myTrade.ClientOrderId);

                processedTrades.Add(myTrade.TradeId);
            }
        }

        bool DoTryCancelOrder(string clientOrderId, bool shouldStopOnError)
        {
            var cancelOrderResponse = SendRequest<RawExecutionReport<GlobitexExecutionReportMessage>>("POST",
                                                                                                      apiV2Path,
                                                                                                      cancelOrderUrl,
                                                                                                      clientOrderId,
                                                                                                      RequestError.CancelOrder,
                                                                                                      shouldStopOnError,
                                                                                                      $"clientOrderId={clientOrderId}",
                                                                                                      $"account={tradingAccount}");

            if (cancelOrderResponse == null) return false; //ответ на Cancel - null. попробуем ещё раз

            if (cancelOrderResponse.ExecutionReport != null) //есть Execution Report, значит заявка снялась. закончили
            {
                OrderCanceled?.Invoke(this, cancelOrderResponse.ExecutionReport);
                return true;
            }

            if (cancelOrderResponse.Reject != null)
            {
                bool isCritical = cancelOrderResponse.Reject.ToString().Contains("Internal error");
                ErrorOccured?.Invoke(this,
                                     new GlobitexErrorMessage((int)RequestError.CancelOrder,
                                                              $"GLOBITEX Error\nOrder cancel rejected with message: {cancelOrderResponse.Reject}",
                                                              cancelOrderResponse.Reject.ClientOrderId,
                                                              isCritical && shouldStopOnError));

                //пробуем ещё раз, когда функция возвращает false, что означает, что Cancel не получился.
                //и если в reject есть критическая ошибка (например Internal error), то isCritical=true, и возвращем false
                return !isCritical;
            }

            //странный случай, когда cancelOrderResponse не null, а ExecutionReport=null и Reject=null
            return false;
        }

        void SetAllIsinsNotReceivedSnapshot()
        {
            foreach (IsinData isinData in isinDatas.Values) isinData.IsSnapshotReceived = false;
        }

        T SendRequest<T>(string          method,
                         string          apiPath,
                         string          commandPath,
                         string          clientOrderId,
                         RequestError    possibleError,
                         bool            shouldStopOnError = true,
                         params object[] parameters)
        {
            string body = CreateBody(parameters);
            string jsonString;
            lock (requestLocker) jsonString = QueryString(method, apiPath, commandPath, body, possibleError, clientOrderId, shouldStopOnError);

            if (string.IsNullOrEmpty(jsonString)) return default;

            T output;

            try { output = JsonConvert.DeserializeObject<T>(jsonString); }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new GlobitexErrorMessage((int)possibleError, e.MakeString(), jsonString, true));
                return default;
            }

            return output;
        }

        static string CreateBody(object[] parameters)
        {
            return parameters.Length != 0 ? string.Join("&", parameters) : null;
        }

        string QueryString(string       method,
                           string       apiPath,
                           string       commandPath,
                           string       body,
                           RequestError possibleError,
                           string       clientOrderId,
                           bool         shouldStopOnError)
        {
            HttpWebRequest request = CreateHttpWebRequest(method, apiPath, commandPath, body);
            return GetResponse(request, $"{commandPath}?{body}", clientOrderId, possibleError, shouldStopOnError);
        }

        HttpWebRequest CreateHttpWebRequest(string method, string apiPath, string commandPath, string body)
        {
            long nonce                    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (nonce <= prevnonce) nonce = prevnonce + 1;
            prevnonce = nonce;

            //Console.WriteLine(nonce);

            string relativeUrl = string.IsNullOrEmpty(body) ? commandPath : $"{commandPath}?{body}";
            string signature   = MakeSignature(nonce, apiPath, relativeUrl, publicKey, secretKey);

            string requestUrl;
            if (method == "GET" && !string.IsNullOrEmpty(body)) requestUrl = $"{restEndPoint}{apiPath}{commandPath}?{Uri.EscapeUriString(body)}";
            else requestUrl                                                = $"{restEndPoint}{apiPath}{commandPath}";

            HttpWebRequest request = WebRequest.CreateHttp(requestUrl);
            request.Method                 = method;
            request.Timeout                = Timeout.Infinite;
            request.Headers["X-API-Key"]   = publicKey;
            request.Headers["X-Nonce"]     = nonce.ToString();
            request.Headers["X-Signature"] = signature;

            if (body != null && method != "GET")
            {
                request.ContentType = "application/x-www-form-urlencoded";
                using (Stream stream = request.GetRequestStream())
                {
                    byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }
            }

            return request;
        }

        string GetResponse(HttpWebRequest request, string relativeUrl, string clientOrderId, RequestError possibleError, bool shouldStopOnError)
        {
            WebResponse response = null;
            try { response = request.GetResponse(); }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    Stream exResponseStream;
                    if (exResponse == null || (exResponseStream = exResponse.GetResponseStream()) == null)
                    {
                        ErrorOccured?.Invoke(this,
                                             new GlobitexErrorMessage((int)possibleError,
                                                                      $"GLOBITEX Error\n{ex.Message}",
                                                                      "HTTP exception, but response stream is NULL",
                                                                      shouldStopOnError && possibleError == RequestError.CancelOrder));
                        return "";
                    }
                    using (var sr = new StreamReader(exResponseStream))
                    {
                        string responseString = sr.ReadToEnd();

                        RawErrorsMessage errorsObject;
                        try { errorsObject = JsonConvert.DeserializeObject<RawErrorsMessage>(responseString); }
                        catch (Exception e)
                        {
                            ErrorOccured?.Invoke(this,
                                                 new GlobitexErrorMessage((int)possibleError,
                                                                          "GLOBITEX Error\n" + e.MakeString(),
                                                                          responseString,
                                                                          shouldStopOnError && possibleError == RequestError.CancelOrder));
                            return "";
                        }

                        GlobitexErrorMessage error = errorsObject.ErrorsList[0];

                        //for (int i = 0; i < errorsObject.ErrorsList.Count; i++)
                        //{
                        //    ex.Data["Code" + i] = errorsObject.ErrorsList[i].Code;
                        //    ex.Data["Message" + i] = errorsObject.ErrorsList[i].Message;
                        //    ex.Data["Description" + i] = errorsObject.ErrorsList[i].Description;
                        //}

                        ErrorOccured?.Invoke(this,
                                             new GlobitexErrorMessage((int)possibleError,
                                                                      "GLOBITEX Error\n" + error.Message,
                                                                      clientOrderId                      != "" ? clientOrderId : error.Description,
                                                                      shouldStopOnError && possibleError == RequestError.CancelOrder));

                        return "";
                    }
                }
            }

            using (var sr = new StreamReader(response.GetResponseStream() ?? throw new InvalidOperationException($"{relativeUrl} response is null."), Encoding))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        static string MakeSignature(long nonce, string apiPath, string relativeUrl, string publicKey, string secretKey)
        {
            string uri            = apiPath + relativeUrl;
            string message        = $"{publicKey}&{nonce}{uri}";
            byte[] messageBytes   = Encoding.UTF8.GetBytes(message);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            byte[] hashBytes;
            using (var encoder = new HMACSHA512(secretKeyBytes)) { hashBytes = encoder.ComputeHash(messageBytes); }

            string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return hashString;
        }
    }
}