using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace SharedDataStructures.Interfaces
{
    public interface IDataConnector
    {
        string Name { get; }
        string ExchangeName { get; }
        string PublicKey { get; }
        void Init(List<string> isins, int dataTimeoutSeconds, string publicKey, string secretKey, string connectorName);
        void Start();
        void Stop();

        event EventHandler Connected;
        event EventHandler Disconnected;
        event EventHandler<ErrorMessage> ErrorOccured;
        event EventHandler<BookMessage> BookUpdateArrived;
        event EventHandler<BookMessage> BookSnapshotArrived;
        event EventHandler<TickerMessage> TickerArrived;
    }
}
