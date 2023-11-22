using System;
using System.Collections.Generic;
using System.Timers;
using BitstampConnector.Model;
using Newtonsoft.Json.Linq;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace BitstampConnector
{
    public class BitstampClient : IDataConnector
    {
        readonly string uri         = "wss://ws.bitstamp.net";
        readonly string snapshotUrl = "https://www.bitstamp.net/api/v2/order_book/";

        List<string> isins;
        int          timeoutSeconds;
        Timer        timeoutTimer;
        WebSocket    ws;

        readonly Dictionary<string, BitstampTickerMessage> tickerMessages = new Dictionary<string, BitstampTickerMessage>();

        public string Name         { get; private set; }
        public string ExchangeName => "bitstamp";
        public string PublicKey    { get; private set; }

        public event EventHandler                Connected;
        public event EventHandler                Disconnected;
        public event EventHandler<ErrorMessage>  ErrorOccured;
        public event EventHandler<BookMessage>   BookUpdateArrived;
        public event EventHandler<BookMessage>   BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKey, string secretKey, string connectorName)
        {
            isins     = isinsP;
            PublicKey = publicKey;
            Name      = connectorName;

            timeoutSeconds       =  dataTimeoutSeconds;
            timeoutTimer         =  new Timer(timeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            if (isins != null && isins.Count > 0)
                foreach (string isin in isins)
                    tickerMessages.Add(isin, new BitstampTickerMessage(isin));

            ws = new WebSocket(uri) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};

            ws.Opened          += Ws_Opened;
            ws.Closed          += Ws_Closed;
            ws.Error           += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            DoStop();
            ws?.Close();
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            DoStop();
            ws?.Close();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            foreach (string isin in isins)
            {
                string tradesRequest = $"{{\"event\": \"bts:subscribe\", \"data\": {{\"channel\": \"live_trades_{isin}\"}} }}";
                ws.Send(tradesRequest);

                string bookRequest = $"{{\"event\": \"bts:subscribe\", \"data\": {{\"channel\": \"order_book_{isin}\"}} }}";
                ws.Send(bookRequest);
            }

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
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            JObject messageObject = JObject.Parse(e.Message);

            string eventType = (string)messageObject.SelectToken("event");
            if (eventType != "data" && eventType != "trade") return;

            timeoutTimer?.Stop();

            string   channel = (string)messageObject.SelectToken("channel");
            string[] tokens  = channel.Split('_');

            string isin = tokens[2];
            if (tokens[0] == "order") ProcessBook(isin, messageObject);
            else ProcessTrade(isin, messageObject);

            timeoutTimer?.Start();
        }

        void ProcessBook(string isin, JObject messageObject)
        {
            var book = messageObject.SelectToken("data").ToObject<BitstampBookMessage>();
            book.SetIsin(isin);

            BookSnapshotArrived?.Invoke(this, book);

            if (!tickerMessages.TryGetValue(isin, out BitstampTickerMessage ticker)) return;
            ticker.SetBests(book.BestBid, book.BestAsk);

            if (ticker.IsSet) TickerArrived?.Invoke(this, ticker.CreateDeepCopy());
        }

        void ProcessTrade(string isin, JObject messageObject)
        {
            decimal last = (decimal)messageObject.SelectToken("data").SelectToken("price");

            if (!tickerMessages.TryGetValue(isin, out BitstampTickerMessage ticker)) return;
            ticker.SetLast(last);

            if (ticker.IsSet) TickerArrived?.Invoke(this, ticker.CreateDeepCopy());
        }

        void DoStop()
        {
            timeoutTimer?.Stop();
            tickerMessages.Clear();
        }
    }
}