using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using BitMEXConnector.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace BitMEXConnector
{
    public class BitMEXClient : IDataConnector
    {
        static readonly string uri = "wss://www.bitmex.com/realtime";

        public string Name { get; private set; }
        public string ExchangeName => "bitmex";
        public string PublicKey { get; private set; }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;

        List<string> isins;
        int timeoutSeconds;
        Timer timeoutTimer;
        WebSocket ws;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKey, string secretKey, string connectorName)
        {
            isins = isinsP;
            timeoutSeconds = dataTimeoutSeconds;
            PublicKey = publicKey;
            Name = connectorName;

            timeoutTimer = new Timer(timeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            string topics = string.Join(',', isins.Select(isin => $"orderBookL2:{isin},instrument:{isin}"));
            ws = new WebSocket($"{uri}?subscribe={topics}") { EnableAutoSendPing = true, AutoSendPingInterval = 1000 };

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            timeoutTimer?.Stop();
            ws?.Close();
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            timeoutTimer?.Start();
            Connected?.Invoke(this, null);
        }

        void Ws_Closed(object sender, EventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            string responseName = GetResponseName(e.Message);

            switch (responseName)
            {
                case "info":
                case "success":
                    return;
                case "status":
                    ProcessError(e.Message);
                    break;
                case "table":
                    timeoutTimer.Stop();
                    ProcessTable(e.Message);
                    timeoutTimer.Start();
                    break;
                default:
                    return;
            }
        }

        static string GetResponseName(string message)
        {
            int colonIndex = message.IndexOf(":", StringComparison.InvariantCulture);
            return message.Substring(2, colonIndex - 3);
        }

        void ProcessError(string errorMessage)
        {
            var error = JsonConvert.DeserializeObject<BitMEXErrorMessage>(errorMessage);
            ErrorOccured?.Invoke(this, error);
        }

        void ProcessTable(string tableMessage)
        {
            JObject messageObject = JObject.Parse(tableMessage);
            string tableName = (string)messageObject.SelectToken("table");

            switch (tableName)
            {
                case "orderBookL2":
                    ProcessBookL2(messageObject);
                    break;
                case "instrument":
                    ProcessInstrument(messageObject);
                    break;
            }
        }

        void ProcessBookL2(JObject messageObject)
        {
            string action = (string)messageObject.SelectToken("action");
            List<BitMEXPriceLevel> priceLevels = messageObject.SelectToken("data").ToObject<List<BitMEXPriceLevel>>();
            var book = new BitMEXBookMessage(priceLevels);

            if (action == "partial") BookSnapshotArrived?.Invoke(this, book);
            else BookUpdateArrived?.Invoke(this, book);
        }

        void ProcessInstrument(JObject messageObject)
        {
            BitMEXTickerMessage ticker = messageObject.SelectToken("data").ToObject<List<BitMEXTickerMessage>>()[0];

            TickerArrived?.Invoke(this, ticker);
        }
    }
}
