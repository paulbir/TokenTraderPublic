using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using BitfinexConnector.Model;
using Newtonsoft.Json;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;
using SuperSocket.ClientEngine;
using WebSocket4Net;

namespace BitfinexConnector
{
    public class BitfinexClient : IDataConnector
    {
        static readonly string uri = "wss://api.bitfinex.com/ws/2";

        readonly Dictionary<int, ChannelData> tickerIsinsByChannelId = new Dictionary<int, ChannelData>();
        readonly Dictionary<int, ChannelData> bookIsinsByChannelId = new Dictionary<int, ChannelData>();
        //readonly Dictionary<int, ChannelData> tradeIsinsByChannelId = new Dictionary<int, ChannelData>();
        List<string> isins;
        int numBookLevels;
        int timeoutSeconds;
        Timer timeoutTimer;
        WebSocket ws;

        public string Name { get; private set; }
        public string ExchangeName => "bitfinex";
        public string PublicKey { get; private set; }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler<ErrorMessage> ErrorOccured;
        public event EventHandler<BookMessage> BookUpdateArrived;
        public event EventHandler<BookMessage> BookSnapshotArrived;
        public event EventHandler<TickerMessage> TickerArrived;

        public void Init(List<string> isinsP, int dataTimeoutSeconds, string publicKey, string secretKey, string connectorName)
        {
            isins = isinsP;
            PublicKey = publicKey;
            numBookLevels = 25;
            Name = connectorName;
            timeoutSeconds = dataTimeoutSeconds;
            timeoutTimer = new Timer(timeoutSeconds * 1000);
            timeoutTimer.Elapsed += TimeoutTimer_Elapsed;
        }

        public void Start()
        {
            tickerIsinsByChannelId.Clear();
            bookIsinsByChannelId.Clear();
            //tradeIsinsByChannelId.Clear();

            ws = new WebSocket(uri) {EnableAutoSendPing = true, AutoSendPingInterval = 1000};

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
            tickerIsinsByChannelId.Clear();
        }

        void TimeoutTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_Opened(object sender, EventArgs e)
        {
            foreach (string isin in isins)
            {
                string tickerRequest = $"{{\"event\": \"subscribe\", \"channel\": \"ticker\", \"symbol\": \"{isin}\"}}";
                ws.Send(tickerRequest);

                string bookRequest =
                    $"{{\"event\": \"subscribe\", \"channel\": \"book\", \"pair\": \"{isin}\", \"prec\": \"P0\", \"frequency\": \"F0\", \"len\": {numBookLevels}}}";
                ws.Send(bookRequest);

                //string tradeRequest = $"{{\"event\": \"subscribe\", \"channel\": \"trades\", \"symbol\": \"{isin}\"}}";
                //ws.Send(tradeRequest);
            }

            timeoutTimer?.Start();
            Connected?.Invoke(this, null);
        }

        void Ws_Closed(object sender, EventArgs eventArgs)
        {
            timeoutTimer?.Stop();
            Disconnected?.Invoke(this, null);
        }

        void Ws_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            string responseName = GetResponseName(e.Message);

            switch (responseName)
            {
                case "info":
                    return;
                case "error":
                    ProcessError(e.Message);
                    break;
                case "subscribed":
                    ProcessSubscribed(e.Message);
                    break;
                case "data":
                    timeoutTimer.Stop();
                    ProcessData(e.Message);
                    timeoutTimer.Start();
                    break;
                default:
                    return;
            }
        }

        void Ws_Error(object sender, ErrorEventArgs e)
        {
            throw e.Exception;
        }

        void ProcessError(string errorMessage)
        {
            var error = JsonConvert.DeserializeObject<BitfinexErrorMessage>(errorMessage);

            ErrorOccured?.Invoke(this, error);
        }

        void ProcessSubscribed(string subscribedMessage)
        {
            var channelMessage = JsonConvert.DeserializeObject<BitfinexChannel>(subscribedMessage);

            if (channelMessage.Channel == "ticker") TryAddChannelToDictionary(channelMessage, tickerIsinsByChannelId);
            if (channelMessage.Channel == "book") TryAddChannelToDictionary(channelMessage, bookIsinsByChannelId);
            //else if (channelMessage.Channel == "trades") TryAddChannelToDictionary(channelMessage, tradeIsinsByChannelId);
        }

        void TryAddChannelToDictionary(BitfinexChannel channelMessage, Dictionary<int, ChannelData> channelDataById)
        {
            if (channelDataById.ContainsKey(channelMessage.ChannelId))
            {
                throw new ArgumentException($"{channelMessage.Channel} channelDataById dictionary already contains isin " +
                                            $"{channelDataById[channelMessage.ChannelId].Isin} for channel id {channelMessage.ChannelId}.");
            }

            channelDataById.Add(channelMessage.ChannelId, new ChannelData(channelMessage.Pair));
        }

        void ProcessData(string dataMessage)
        {
            int channelId = GetChannelId(dataMessage);
            string payload = GetPayload(dataMessage, removeLastBracket: true);

            if (payload == "\"hb\"") return;
           
            if (bookIsinsByChannelId.TryGetValue(channelId, out ChannelData channelData))
            {
                if (!channelData.IsSnapshotReceived)
                {
                    channelData.IsSnapshotReceived = true;
                    ProcessBookSnapshot(payload, channelData);
                }
                else ProcessBookUpdate(payload, channelData);
            }
            if (tickerIsinsByChannelId.TryGetValue(channelId, out channelData)) ProcessTicker(payload, channelData.Isin);

            //else if (tradeIsinsByChannelId.TryGetValue(channelId, out channelData))
            //{
            //    if (!channelData.IsSnapshotReceived)
            //    {
            //        channelData.IsSnapshotReceived = true;
            //        return;
            //    }

            //    ProcessTrade(payload, channelData);
            //}
        }

        void ProcessTicker(string payload, string isin)
        {
            List<decimal> rawTickerMessage;
            try
            {
                rawTickerMessage = JsonConvert.DeserializeObject<List<decimal>>(payload);
            }
            catch (Exception e)
            {
                ErrorOccured?.Invoke(this, new BitfinexErrorMessage("ticker", "", "", isin, $"payload={payload};message={e.Message}", -1));
                return;
            }

            var tickerMessage = new BitfinexTickerMessage(rawTickerMessage[0], rawTickerMessage[2], rawTickerMessage[6]);
            tickerMessage.SetIsin(isin);
            TickerArrived?.Invoke(this, tickerMessage);
        }

        void ProcessBookSnapshot(string payload, ChannelData channelData)
        {
            var rawPriceLevels = JsonConvert.DeserializeObject<List<List<decimal>>>(payload);
            List<BitfinexPriceLevel> priceLevels =
                rawPriceLevels.Select(priceLevel => new BitfinexPriceLevel(priceLevel[0], (int)priceLevel[1], priceLevel[2])).ToList();

            BookSnapshotArrived?.Invoke(this, new BitfinexBookSnapshotMessage(channelData.Isin, priceLevels));
        }

        void ProcessBookUpdate(string payload, ChannelData channelData)
        {
            var rawUpdateLevel = JsonConvert.DeserializeObject<List<decimal>>(payload);
            var updateLevel = new BitfinexPriceLevel(rawUpdateLevel[0], (int)rawUpdateLevel[1], rawUpdateLevel[2]);

            BookUpdateArrived?.Invoke(this, new BitfinexBookUpdateMessage(channelData.Isin, updateLevel));
        }

        //void ProcessTrade(string payload, ChannelData channelData)
        //{
        //    string tradeType = GetTradeType(payload);

        //    if (tradeType != "te") return;

        //    string innerTradePayload = GetPayload(payload, removeLastBracket: false);

        //    BitfinexTradeMessage trade;
        //    try
        //    {
        //        var rawTrade = JsonConvert.DeserializeObject<List<decimal>>(innerTradePayload);
        //        trade = new BitfinexTradeMessage(channelData.Isin, (long)rawTrade[0], (long)rawTrade[1], rawTrade[2], rawTrade[3]);
        //    }
        //    catch (Exception e)
        //    {
        //        ExceptionRaised?.Invoke(this, e);
        //        return;
        //    }

        //    NewTradeArrived?.Invoke(this, trade);
        //}

        static string GetResponseName(string message)
        {
            if (message[0] == '[' && message[message.Length - 1] == ']') return "data";

            int colonAfterEventIndex = message.IndexOf("event", StringComparison.InvariantCulture) + 8;
            int nextCommaIndex = message.IndexOf(",", colonAfterEventIndex, StringComparison.InvariantCulture);
            return message.Substring(colonAfterEventIndex, nextCommaIndex - colonAfterEventIndex - 1);
        }

        static int GetChannelId(string message)
        {
            int commaIndex = message.IndexOf(",", 0, StringComparison.InvariantCulture);
            string channelIdStr = message.Substring(1, commaIndex - 1);
            return Convert.ToInt32(channelIdStr);
        }

        static string GetPayload(string message, bool removeLastBracket)
        {
            int commaIndex = message.IndexOf(",", 0, StringComparison.InvariantCulture);
            int lengthDecrese = removeLastBracket ? 2 : 1;
            return message.Substring(commaIndex + 1, message.Length - commaIndex - lengthDecrese);
        }

        //static string GetTradeType(string message)
        //{
        //    return message.Substring(1, 2);
        //}
    }
}