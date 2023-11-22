using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SharedTools;
using SuperSocket.ClientEngine;
using WebSocket4Net;
using WoortonV2Connector.Model;

namespace WoortonV2Connector
{
    public class WoortonV2Client : IHedgeConnector
    {
        readonly string wsBaseUrl   = "wss://websocket.woorton.com/";

        #if DEBUG
                readonly string restBaseUrl = "https://api.uat.woorton.com/";
        #else
                    readonly string restBaseUrl = "https://api-v2.woorton.com/";
        #endif

        string restInstrumentsBaseUrl = "https://api-v2.woorton.com/";

        readonly int orderTimeoutSeconds = 30;
        readonly int allowedConsecutiveErrors = 3;


        readonly string instrumentsUrl      = "instrument";
        readonly string balancesUrl         = "balance";
        readonly string exposureUrl         = "exposure";
        readonly string addOrderUrl         = "order";

        static readonly string testToken = "-";

        HashSet<string>                         isins;
        string                                  publicKey;
        string                                  secretKey;
        Dictionary<string, WoortonV2Instrument> instrumentByIsin;
        Dictionary<string, WoortonV2Instrument> instrumentById;

        Timer      timeoutTimer;
        WebSocket  ws;
        HttpClient httpClient;

        int numBalancesConsecutiveErrors = 0;
        int numPositionsConsecutiveErrors = 0;

        public string Name         { get; private set; }
        public string ExchangeName => "woortonv2";
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

        //Dictionary<string, WoortonV2Instrument> testInstrumentByIsin;
        //Dictionary<string, WoortonV2Instrument> testInstrumentById;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKeyP, string secretKeyP, string connectorName)
        {
            isins     = isinsP.ToHashSet();
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
            numBalancesConsecutiveErrors = 0;
            numPositionsConsecutiveErrors = 0;

            httpClient = new HttpClient();
            #if DEBUG
                        httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {testToken}");
            #else
                            httpClient.DefaultRequestHeaders.Add("Authorization", $"Token {secretKey}");
            #endif
            httpClient.BaseAddress = new Uri(restBaseUrl);

            (instrumentByIsin, instrumentById) = GetInstruments(restInstrumentsBaseUrl, secretKey);
            //(testInstrumentByIsin, testInstrumentById) = GetInstruments(restBaseUrl, testToken);
            if (instrumentByIsin == null || instrumentByIsin.Count == 0 || instrumentById == null || instrumentById.Count == 0) return;

            if (isins == null || isins.Count == 0)
            {
                Connected?.Invoke(this, null);
                return;
            }

            ws                 =  new WebSocket(wsBaseUrl) {AutoSendPingInterval = 10000, EnableAutoSendPing = true};
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
            List<PositionMessage>         positions = GetPositions();
            if (positions != null) PositionArrived?.Invoke(this, positions);

            List<WoortonV2BalanceMessage> balances  = GetBalances();
            if (balances != null) BalanceArrived?.Invoke(this, balances.Select(balance => (BalanceMessage)balance).ToList());

            if (positions == null || balances == null) return;
            List<LimitMessage> limits = MakeLimits(balances, positions);
            LimitArrived?.Invoke(this, limits);
        }

        public void AddHedgeOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, decimal slippagePriceFrac, int requestId)
        {
            if (!instrumentByIsin.TryGetValue(isin, out WoortonV2Instrument instrument))
            {
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.AddOrder, $"Isin {isin} was not found in instrument dictionary", ""));
                return;
            }

            //if (!testInstrumentByIsin.TryGetValue(isin, out WoortonV2Instrument instrument))
            //{
            //    ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.AddOrder, $"Isin {isin} was not found in instrument dictionary", ""));
            //    return;
            //}

            string sideStr = side == OrderSide.Buy ? "BUY" : "SELL";

            string addOrderRequest = MessageCreator.CreateAddOrderMessage(instrument.Id, sideStr, price, qty, "FOK", orderTimeoutSeconds);
            HttpResponseMessage addOrderResponseMessage = httpClient.PostAsync(addOrderUrl, new StringContent(addOrderRequest, Encoding.UTF8, "application/json")).Result;
            string addOrderResponse = addOrderResponseMessage.Content.ReadAsStringAsync().Result;

            WoortonV2OrderMessage order;
            try
            {
                order = JsonConvert.DeserializeObject<WoortonV2OrderMessage>(addOrderResponse);
            }
            catch (Exception ex)
            {
                string exceptionMessage = ex.MakeString();
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.AddOrder, $"AddOrderResponse could not be parsed\n{exceptionMessage}", addOrderResponse));
                return;
            }

            if (order == null)
            {
                ErrorOccured?.Invoke(this,
                                     new WoortonV2ErrorMessage(RequestError.AddOrder,
                                                               $"AddOrderResponseMessage contains no trade\n{addOrderResponseMessage.StatusCode}\n{addOrderResponseMessage.ReasonPhrase}",
                                                               addOrderResponse));
                return;
            }
            order.FillRestFields(clientOrderId, isin, side, price, qty);

            switch (order.Status)
            {
                case "FILLED":
                    foreach (WoortonV2Trade trade in order.Trades) ExecutionReportArrived?.Invoke(this, trade);
                    break;

                case "REJECTED": 
                    OrderCanceled?.Invoke(this, order);
                    break;

                default: 
                    ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.AddOrder, $"Unknown order status = {order.Status}", addOrderResponse));
                    break;
            }
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            string authMessage = MessageCreator.CreateAuthMessage(secretKey);
            ws.Send(authMessage);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            DoStop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(0, "WoortonV2 websocket exception", e.Exception.MakeString()));
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JObject messageObject = JObject.Parse(e.Message);
            string  messageType   = (string)messageObject.SelectToken("event");

            bool isSuccess = (bool)(messageObject.SelectToken("succeed") ?? messageObject.SelectToken("success"));
            if (!isSuccess)
            {
                string errorMessage = (string)(messageObject.SelectToken("message") ?? "");
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(0, errorMessage, ""));
                return;
            }

            switch (messageType)
            {
                case "price":
                {
                    timeoutTimer?.Stop();
                    ProcessSnapshot(messageObject);
                    timeoutTimer?.Start();
                    break;
                }
                case "subscribe": 
                    break;
                case "auth":
                {
                    string subscribeMessage = MessageCreator.CreateSubscribeMessage();
                    ws.Send(subscribeMessage);

                    Connected?.Invoke(this, null);

                    timeoutTimer?.Start();
                    break;
                }
            }
        }

        void ProcessSnapshot(JObject messageObject)
        {
            //string isinId = (string)messageObject.SelectToken("instrument_id");
            //if (string.IsNullOrEmpty(isinId)) return;
            //if (!instrumentById.TryGetValue(isinId, out WoortonV2Instrument instrument)) return;
            //if (!isins.Contains(instrument.Isin)) return;

            var book = messageObject.ToObject<WoortonV2BookMessage>();
            if (book == null || string.IsNullOrEmpty(book.Isin) || !isins.Contains(book.Isin)) return;
            //book.SetIsin(instrument.Isin);
            BookSnapshotArrived?.Invoke(this, book);
        }

        void DoStop()
        {
            numBalancesConsecutiveErrors = 0;
            numPositionsConsecutiveErrors = 0;
            timeoutTimer?.Stop();
            if (ws?.State == WebSocketState.Open) ws.Close();
        }

        (Dictionary<string, WoortonV2Instrument> instrumentByIsin, Dictionary<string, WoortonV2Instrument> instrumentById) GetInstruments(string restBaseUrlP, string token)
        {
            var localInstrumentByIsin = new Dictionary<string, WoortonV2Instrument>();
            var localInstrumentById = new Dictionary<string, WoortonV2Instrument>();

            var httpClientLocal = new HttpClient();
            httpClientLocal.DefaultRequestHeaders.Add("Authorization", $"Token {token}");
            httpClientLocal.BaseAddress = new Uri(restBaseUrlP);

            string instrumentsStr = "";
            try
            {
                instrumentsStr = httpClientLocal.GetStringAsync(instrumentsUrl).Result;
                var instruments = JsonConvert.DeserializeObject<List<WoortonV2Instrument>>(instrumentsStr);

                foreach (WoortonV2Instrument instrument in instruments)
                {
                    localInstrumentByIsin.TryAdd(instrument.Isin, instrument);
                    localInstrumentById.TryAdd(instrument.Id, instrument);
                }
            }
            catch (Exception e)
            {
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.Instruments, errorMsg, instrumentsStr));
                return (null, null);
            }

            if (localInstrumentByIsin.Count == 0 || localInstrumentById.Count == 0)
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.Instruments, "Got no instruments in dictionary", instrumentsStr));

            return (localInstrumentByIsin, localInstrumentById);
        }

        List<WoortonV2BalanceMessage> GetBalances()
        {
            string balanceStr = "";
            try
            {
                balanceStr = httpClient.GetStringAsync(exposureUrl).Result;
                var balances = JsonConvert.DeserializeObject<List<WoortonV2BalanceMessage>>(balanceStr);
                numBalancesConsecutiveErrors = 0;

                return balances;
            }
            catch (Exception e)
            {
                bool isCritical = IsErrorOffLimitAndIncrement(ref numBalancesConsecutiveErrors);
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.TradingBalance, errorMsg, balanceStr, isCritical));
                return null;
            }
        }

        List<PositionMessage> GetPositions()
        {
            string positionsStr = "";
            try
            {
                positionsStr = httpClient.GetStringAsync(balancesUrl).Result;
                var positions = JsonConvert.DeserializeObject<List<WoortonV2PositionMessage>>(positionsStr);
                numPositionsConsecutiveErrors = 0;

                return positions.Select(pos => (PositionMessage)pos).ToList();
            }
            catch (Exception e)
            {
                bool isCritical = IsErrorOffLimitAndIncrement(ref numPositionsConsecutiveErrors);
                string errorMsg = e.MakeString();
                ErrorOccured?.Invoke(this, new WoortonV2ErrorMessage(RequestError.Positions, errorMsg, positionsStr, isCritical));
                return null;
            }
        }

        List<LimitMessage> MakeLimits(List<WoortonV2BalanceMessage> balances, List<PositionMessage> positions)
        {
            Dictionary<string, WoortonV2BalanceMessage>  balancesByCur  = balances.ToDictionary(balance => balance.Currency, balance => balance);
            Dictionary<string, PositionMessage> positionsByCur = positions.ToDictionary(position => position.Isin, position => position);

            var limits = new List<LimitMessage>();
            foreach (KeyValuePair<string, PositionMessage> pair in positionsByCur)
            {
                string          currency = pair.Key;
                PositionMessage position = pair.Value;
                if (!balancesByCur.TryGetValue(currency, out WoortonV2BalanceMessage balance)) continue;

                limits.Add(new WoortonV2LimitMessage(currency, balance.Min, balance.Max, position.Qty));
            }

            return limits;
        }

        bool IsErrorOffLimitAndIncrement(ref int numConsecutiveErrors)
        {
            numConsecutiveErrors++;
            return numConsecutiveErrors >= allowedConsecutiveErrors;
        }
    }
}