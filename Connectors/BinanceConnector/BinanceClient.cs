using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Timers;
using BinanceConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using WebSocket4Net;
using ErrorEventArgs = SuperSocket.ClientEngine.ErrorEventArgs;

namespace BinanceConnector
{
    public class BinanceClient : IDataConnector
    {
        static readonly string uri = "wss://stream.binance.com:9443";
        static readonly string snapshotUrl = @"https://www.binance.com/api/v1/depth?symbol={0}&limit=10000";

        public string Name { get; private set; }
        public string ExchangeName => "binance";
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

        readonly Dictionary<string, bool> updatedIsins = new Dictionary<string, bool>();
        bool allIsinsUpdated;

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
            foreach (string isin in isins) updatedIsins[isin] = false;
            allIsinsUpdated = false;

            string streams = "";
            foreach (string isinType in isins)
            {
                string[] tokens = isinType.Split('.');
                if (tokens.Length < 2) throw new ConfigErrorsException($"Wrong Binance isin format: {isinType}.");

                string isin = tokens[0];
                string bookType = tokens[1];
                streams += $"{isin}@ticker/{isin}@{bookType}/";
            }
            streams = streams.TrimEnd('/');

            string connectionString = $"{uri}/stream?streams={streams}";

            ws = new WebSocket(connectionString);

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
            string[] streamName = GetStreamName(e.Message).Split('@');
            string stream = streamName[1];
            string payload = GetPayload(e.Message);

            timeoutTimer.Stop();

            switch (stream)
            {
                case "bookTicker":
                    ProcessBookTicker(payload);
                    break;

                case "depth":
                    ProcessDepth(payload);
                    break;

                case "ticker":
                    ProcessTicker(payload);
                    break;
            }

            timeoutTimer.Start();
        }

        void ProcessBookTicker(string payload)
        {
            BinanceBookTickerMessage book;

            try
            {
                book = JsonConvert.DeserializeObject<BinanceBookTickerMessage>(payload);
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new BinanceErrorMessage(0, $"Unable to convert Binance bookTicker to book. Ex: {e.Message}", payload));
                return;
            }

            if (!allIsinsUpdated && !updatedIsins[book.Isin])
            {
                updatedIsins[book.Isin] = true;
                if (updatedIsins.Values.All(updated => updated)) allIsinsUpdated = true;
                BookSnapshotArrived?.Invoke(this, book);
            }
            else BookUpdateArrived?.Invoke(this, book);
        }

        void ProcessDepth(string payload)
        {
            BinanceBookUpdateMessage book;

            try
            {
                book = JsonConvert.DeserializeObject<BinanceBookUpdateMessage>(payload);
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new BinanceErrorMessage(0, $"Unable to convert Binance depth Update to book. Ex: {e.Message}", payload));
                return;
            }

            if (!allIsinsUpdated && !updatedIsins[book.Isin])
            {
                updatedIsins[book.Isin] = true;
                if (updatedIsins.Values.All(updated => updated)) allIsinsUpdated = true;
                GetSnapshot(book.Isin);
            }

            BookUpdateArrived?.Invoke(this, book);
        }

        void ProcessTicker(string payload)
        {
            BinanceTickerMessage ticker;

            try
            {
                ticker = JsonConvert.DeserializeObject<BinanceTickerMessage>(payload);
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new BinanceErrorMessage(0, $"Unable to convert Binance ticker json object. Ex: {e.Message}", payload));
                return;
            }

            TickerArrived?.Invoke(this, ticker);
        }

        void GetSnapshot(string isin)
        {
            string requestStr = string.Format(snapshotUrl, isin.ToUpperInvariant());
            string response = QueryString(requestStr);
            if (string.IsNullOrEmpty(response)) return;

            BinanceBookSnapshotMessage snapshot;

            try
            {
                snapshot = JsonConvert.DeserializeObject<BinanceBookSnapshotMessage>(response);
                snapshot.SetIsin(isin + ".depth");
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new BinanceErrorMessage(0, $"Unable to convert Binance depth Snapshot to book. Ex: {e.Message}", response));
                return;
            }

            BookSnapshotArrived?.Invoke(this, snapshot);
        }

        string QueryString(string requestStr)
        {
            HttpWebRequest request = WebRequest.CreateHttp(requestStr);
            WebResponse response;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                using (var exResponse = (HttpWebResponse)ex.Response)
                {
                    using (var sr = new StreamReader(exResponse.GetResponseStream()))
                    {
                        string responseString = sr.ReadToEnd();
                        ex.Data["ResponseString"] = responseString;
                        throw;
                    }
                }
            }

            using (var sr = new StreamReader(response.GetResponseStream(), Encoding.ASCII))
            {
                string responseString = sr.ReadToEnd();
                return responseString;
            }
        }

        static string GetStreamName(string message)
        {
            int colonIndex = message.IndexOf(":", StringComparison.InvariantCulture);
            int commaIndex = message.IndexOf(",", StringComparison.InvariantCulture);
            return message.Substring(colonIndex + 2, commaIndex - colonIndex - 3);
        }

        static string GetPayload(string message)
        {
            int dataIndex = message.IndexOf("data", StringComparison.InvariantCulture);
            return message.Substring(dataIndex + 6, message.Length - dataIndex - 7);
        }
    }
}
