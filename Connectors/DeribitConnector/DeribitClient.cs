using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Timers;
using DeribitConnector.Model;
using Newtonsoft.Json.Linq;
using SharedDataStructures.Exceptions;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using SuperSocket.ClientEngine.Proxy;
using WebSocket4Net;

namespace DeribitConnector
{
    public class DeribitClient : IDataConnector
    {
        static readonly string uri = "wss://www.deribit.com/ws/api/v2/";

        public string Name { get; private set; }
        public string ExchangeName => "deribit";
        public string PublicKey { get; private set; }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;

        string       secretKey;

        List<string> isins;
        int          timeoutSeconds;
        Timer        timeoutTimer;
        WebSocket    ws;

        readonly HashSet<string> channelsToSubscribe = new HashSet<string>();
        bool                     authed;
        bool                     subscribed;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKey, string secretKey, string connectorName)
        {
            isins          = isinsP;
            timeoutSeconds = dataTimeoutSeconds;
            PublicKey      = publicKey;
            this.secretKey = secretKey;
            Name           = connectorName;

            timeoutTimer = new Timer(timeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            //var proxy = new IPEndPoint(IPAddress.Loopback, 1080);
            ws = new WebSocket(uri, sslProtocols: SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls/*, httpConnectProxy: proxy*/);

            ws.Opened += Ws_Opened;
            ws.Closed += Ws_Closed;
            ws.Error += Ws_Error;
            ws.MessageReceived += Ws_MessageReceived;
            ws.Open();
        }

        public void Stop()
        {
            subscribed = false;
            authed     = false;

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
            string authMessage = CreateAuthMessage(0, PublicKey, secretKey);
            ws.Send(authMessage);
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
            string message = e.Message;
            JObject messageObject = JObject.Parse(message);

            if (!authed)
            {
                ProcessAuth(messageObject);
                return;
            }

            if (!subscribed)
            {
                if (!messageObject.ContainsKey("result")) return;
                ProcessSubscriptionResult(messageObject);
                return;
            }

            JToken paramsToken = messageObject.SelectToken("params");
            string channel = (string)paramsToken.SelectToken("channel");
            string[] channelTokens = channel.Split('.');

            timeoutTimer?.Stop();
            if (channelTokens[0] == "book") ProcessBook(paramsToken, message);
            else if (channelTokens[0] == "ticker") ProcessTicker(paramsToken, message);
            timeoutTimer?.Start();
        }

        void ProcessAuth(JObject messageObject)
        {
            if (messageObject.TryGetValue("error", out JToken errorToken))
            {
                var authError = errorToken.ToObject<DeribitErrorMessage>();
                ErrorOccured?.Invoke(this, authError);
                return;
            }

            if (!messageObject.TryGetValue("result", out JToken authResult) || !(authResult is JObject authObj) || !authObj.ContainsKey("access_token")) return;

            authed = true;

            Connected?.Invoke(this, null);

            string subscriptionsMessage = CreateSubscriptions(1);
            ws.Send(subscriptionsMessage);
        }

        void ProcessSubscriptionResult(JObject messageObject)
        {
            var subscribedChannels = messageObject.SelectToken("result").ToObject<List<string>>();
            var subscribedChannelsSet = new HashSet<string>(subscribedChannels);
            channelsToSubscribe.ExceptWith(subscribedChannelsSet);

            if (channelsToSubscribe.Count > 0)
                ErrorOccured?.Invoke(this, new DeribitErrorMessage(0, $"Couldn't subscribe to these channels: {string.Join(";", channelsToSubscribe)}", ""));

            subscribed = true;

            timeoutTimer?.Start();
        }

        void ProcessBook(JToken paramsToken, string message)
        {
            JToken dataToken = paramsToken.SelectToken("data");

            if (dataToken.SelectToken("prev_change_id") == null) //snapshot
            {
                var snapshot = dataToken.ToObject<DeribitBookMessage>();
                BookSnapshotArrived?.Invoke(this, snapshot);
            }
            else //update
            {
                var update = dataToken.ToObject<DeribitBookMessage>();
                BookUpdateArrived?.Invoke(this, update);
            }
        }

        void ProcessTicker(JToken paramsToken, string message)
        {
            var ticker = paramsToken.SelectToken("data").ToObject<DeribitTickerMessage>();
            TickerArrived?.Invoke(this, ticker);
        }

        static string CreateAuthMessage(int id, string publicKey, string secretKey)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string signature = CreateSignature(timestamp, timestamp, secretKey);

            return $"{{\"method\":\"public/auth\", \"id\":{id}, " +
                   $"\"params\": {{ \"grant_type\":\"client_signature\", \"client_id\":\"{publicKey}\", \"timestamp\":\"{timestamp}\", " +
                   $"\"signature\":\"{signature}\", \"nonce\":\"{timestamp}\", \"data\":\"\" }} }}";
        }

        static string CreateSignature(long timestamp, long nonce, string secretKey)
        {
            string stringToHash = $"{timestamp}\n{nonce}\n";
            byte[] messageBytes = Encoding.UTF8.GetBytes(stringToHash);
            byte[] secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

            using (var hmac = new HMACSHA256(secretKeyBytes))
            {
                byte[] hash = hmac.ComputeHash(messageBytes);
                string hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return hashString;
            }
        }

        string CreateSubscriptions(int id)
        {
            var sb = new StringBuilder();
            foreach (string isin in isins)
            {
                sb.Append($"\"book.{isin}.raw\", \"ticker.{isin}.100ms\",");
                channelsToSubscribe.Add($"book.{isin}.raw");
                channelsToSubscribe.Add($"ticker.{isin}.100ms");
            }

            return $"{{\"method\":\"public/subscribe\", \"id\":{id}, \"params\": {{ \"channels\": [{sb.ToString().TrimEnd(',')}] }} }}";
        }
    }
}
