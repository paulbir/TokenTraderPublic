using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharedDataStructures.Messages;

namespace SharedDataStructures.Interfaces
{
    public interface ITradeConnector : IDataConnector
    {
        void AddOrder(string     clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId);
        void PrepareOrder(string clientOrderId, string isin, OrderSide side, decimal price, decimal qty, int requestId);
        Task SendPreparedOrder();
        void CancelOrder(string  clientOrderId,    int    requestId);
        void ReplaceOrder(string oldClientOrderId, string newClientOrderId, decimal price, decimal qty, int requestId);
        void GetActiveOrders(int requestId);
        void GetPosAndMoney(int  requestId);

        event EventHandler<OrderMessage>          NewOrderAdded;
        event EventHandler<OrderMessage>          OrderCanceled;
        event EventHandler<OrderMessage>          OrderReplaced;
        event EventHandler<List<OrderMessage>>    ActiveOrdersListArrived;
        event EventHandler<OrderMessage>          ExecutionReportArrived;
        event EventHandler<List<BalanceMessage>>  BalanceArrived;
        event EventHandler<List<PositionMessage>> PositionArrived;
    }
}