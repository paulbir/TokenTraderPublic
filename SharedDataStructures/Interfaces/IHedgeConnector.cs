using System;
using System.Collections.Generic;
using SharedDataStructures.Messages;

namespace SharedDataStructures.Interfaces
{
    public interface IHedgeConnector : ITradeConnector
    {
        void AddHedgeOrder(string     clientOrderId, string isin, OrderSide side, decimal price, decimal qty, decimal slippagePriceFrac, int requestId);

        event EventHandler<List<LimitMessage>> LimitArrived;
    }
}