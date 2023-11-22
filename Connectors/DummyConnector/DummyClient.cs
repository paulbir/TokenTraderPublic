using System;
using System.Timers;
using System.Collections.Generic;
using DummyConnector.Model;
using SharedDataStructures.Interfaces;
using SharedDataStructures.Messages;

namespace DummyConnector
{
    public class DummyClient : IDataConnector
    {
        readonly int dataSendTimeoutMs = 5000;
        readonly decimal mid = 1;

        readonly Dictionary<string, long> sequenceByIsin = new Dictionary<string, long>();
        List<string> isins;
        Timer dataSendTimer;

        public string Name { get; private set; }
        public string ExchangeName => "dummy";
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
            Name = connectorName;

            foreach (string isin in isins) sequenceByIsin.Add(isin, 0);

            dataSendTimer = new Timer(dataSendTimeoutMs);
            dataSendTimer.Elapsed += DataSendTimer_Elapsed;
        }

        void DataSendTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            dataSendTimer.Stop();

            try
            {
                foreach (string isin in isins)
                {
                    var book = new DummyBookMessage(mid, isin, sequenceByIsin[isin]);
                    BookSnapshotArrived?.Invoke(this, book);

                    var ticker = new DummyTickerMessage(mid, isin);
                    TickerArrived?.Invoke(this, ticker);

                    sequenceByIsin[isin]++;
                }
            }
            finally
            {
                dataSendTimer.Start();
            }
        }

        public void Start()
        {
            Connected?.Invoke(this, null);
            dataSendTimer.Start();
        }

        public void Stop()
        {
            Disconnected?.Invoke(this, null);
            dataSendTimer.Stop();
        }
    }
}
