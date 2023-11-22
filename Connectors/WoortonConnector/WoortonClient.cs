using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using WebSocket4Net;
using WoortonConnector.Model;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace WoortonConnector
{
    public class WoortonClient : IHedgeConnector
    {
        readonly string wsBaseUrl = "wss://socket.woorton.com/";
        //readonly string wsBaseUrl = "wss://sandbox.socket.woorton.com/";
        #if DEBUG
            readonly string restBaseUrl = "https://api-sandbox.woorton.com/api/v1/";
        #else
            readonly string restBaseUrl = "https://api.woorton.com/api/v1/";
        #endif
        readonly string balancesUrl = "exposures";
        readonly string positionsUrl = "balances";
        readonly string addOrderUrl = " orders";

        static readonly string testToken = "-";
        //static readonly string prodToken = "-";

        List<string> isins;
        string       publicKey;
        string       secretKey;

        Timer     timeoutTimer;
        WebSocket ws;
        HttpClient httpClient;

        public string Name { get; private set; }
        public string ExchangeName => "woorton";
        public string PublicKey { get; private set; }

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
        public event EventHandler<List<LimitMessage>> LimitArrived;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins     = isinsP;
            publicKey = publicKeyP;
            secretKey = secretKeyP;
            PublicKey = publicKeyP;

            Name = connectorName;

            if (dataTimeoutSeconds <= 0) return;
            timeoutTimer         =  new Timer(dataTimeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            httpClient = new HttpClient();
            #if DEBUG
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {testToken}");
            #else
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {secretKey}");
            #endif
            httpClient.BaseAddress = new Uri(restBaseUrl);

            if (isins == null || isins.Count == 0)
            {
                Connected?.Invoke(this, null);
                return;
            }

            //ws = new WebSocket(wsBaseUrl,
            //                   customHeaderItems: new List<KeyValuePair<string, string>>
            //                                      {
            //                                          new KeyValuePair<string, string>("Authorization", $"Bearer {secretKey}")
            //                                      });
            ws = new WebSocket(wsBaseUrl + secretKey);
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
            string sideStr = side == OrderSide.Buy ? "buy" : "sell";

            string addOrderRequest = MessageCreator.CreateAddOrderMessage(clientOrderId, isin, sideStr, price, qty, "FOK");
            HttpResponseMessage addOrderResponseMessage = httpClient.PostAsync(addOrderUrl, new StringContent(addOrderRequest, Encoding.UTF8, "application/json")).Result;
            string addOrderResponse = addOrderResponseMessage.Content.ReadAsStringAsync().Result;

            WootonOrderMessage order;
            try { order = JsonConvert.DeserializeObject<WootonOrderMessage>(addOrderResponse); }
            catch(Exception e)
            {
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.AddOrder, errorMsg, addOrderResponse));
                return;
            }

            if (order == null)
            {
                ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.AddOrder, "Deserialized to null order object.", addOrderResponse));
                return;
            }

            if (order.Errors != null && order.Errors.Count > 0)
            {
                ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.AddOrder, order.FlattenedErrors, ""));
                return;
            }

            //если была отмена, иногда приходит null в цене
            if (order.Price <= 0) order.UpdatePrice(price);

            if (order.Status == "executed")
            {
                ExecutionReportArrived?.Invoke(this, order);
                //SendPositions();
            }
            else if (order.Status == "canceled") OrderCanceled?.Invoke(this, order);
            else ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.AddOrder, $"Unknown order status: {order.Status}", order.ToString()));
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
            List<PositionMessage> positions = GetPositions();
            if (positions != null) PositionArrived?.Invoke(this, positions);

            List<BalanceMessage> balances = GetBalances();
            if (balances != null) BalanceArrived?.Invoke(this, balances);

            if (positions == null || balances == null) return;
            List<LimitMessage> limits = MakeLimits(balances, positions);
            LimitArrived?.Invoke(this, limits);
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            foreach (string isin in isins)
            {
                string subscribeMessage = $"{{\"event\":\"subscribe\", \"instrument\":\"{isin}\"}}";
                ws.Send(subscribeMessage);
            }

            Connected?.Invoke(this, null);

            timeoutTimer?.Start();
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            ErrorOccured?.Invoke(this, new WoortonErrorMessage(0, "Woorton websocket exception", e.Exception.MakeString()));
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JObject messageObject = JObject.Parse(e.Message);
            string messageType = (string)messageObject.SelectToken("event");

            switch (messageType) 
            {
                case "price": 
                {
                    timeoutTimer?.Stop();
                    var book = messageObject.ToObject<WoortonBookMessage>();
                    BookSnapshotArrived?.Invoke(this, book);
                    timeoutTimer?.Start();
                    break;
                }                
                case "subscribe": 
                {
                    string status = (string)messageObject.SelectToken("status");
                    if (status == "success") return;

                    var error = messageObject.ToObject<RawWsError>();
                    throw new ConfigErrorsException($"ErrorCode={error.ErrorCode}. Messages: {error.Message}.");
                }

            }
        }

        void DoStop()
        {
            timeoutTimer?.Stop();
            if (ws?.State == WebSocketState.Open) ws.Close();
        }

        List<BalanceMessage> GetBalances()
        {
            string balanceStr;
            try { balanceStr = httpClient.GetStringAsync(balancesUrl).Result; }
            catch (Exception e)
            {
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.TradingBalance, errorMsg, ""));
                return null;
            }

            var rawBalances = JsonConvert.DeserializeObject<RawPosNBalanceMessage>(balanceStr);
            return rawBalances.MakeBalancesToSend();
        }

        List<PositionMessage> GetPositions()
        {
            string positionsStr;
            try
            {
                positionsStr = httpClient.GetStringAsync(positionsUrl).Result;
            }
            catch (Exception e)
            {
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonErrorMessage(RequestError.Positions, errorMsg, ""));
                return null;
            }

            var rawPositions = JsonConvert.DeserializeObject<RawPosNBalanceMessage>(positionsStr);
            return rawPositions.MakePositionsToSend();
        }

        List<LimitMessage> MakeLimits(List<BalanceMessage> balances, List<PositionMessage> positions)
        {
            Dictionary<string, BalanceMessage> balancesByCur = balances.ToDictionary(balance => balance.Currency, balance => balance);
            Dictionary<string, PositionMessage> positionsByCur = positions.ToDictionary(position => position.Isin, position => position);

            var limits = new List<LimitMessage>();
            foreach (KeyValuePair<string, PositionMessage> pair in positionsByCur)
            {
                string currency = pair.Key;
                PositionMessage position = pair.Value;
                if (!balancesByCur.TryGetValue(currency, out BalanceMessage balance)) continue;

                limits.Add(new WoortonLimitMessage(currency, -1 * balance.Available, balance.Available, position.Qty));
            }

            return limits;
        }
    }
}
