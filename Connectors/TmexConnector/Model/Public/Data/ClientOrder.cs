using System;
using System.Runtime.Serialization;
using SharedDataStructures.Messages;
using TmexConnector.Model.Shared;
using TmexConnector.Model.Shared.Data;

namespace TmexConnector.Model.Public.Data
{
    [DataContract]
    public class ClientOrder : OrderMessage
    {
        //public long Id { get; set; }
        //public string ExternalId { get; set; }
        ////public long AssetId { get; set; }
        //public string Symbol { get;set; }
        //public long PortfolioId { get; set; }
        //public decimal Price { get; set; }
        //[DataMember(EmitDefaultValue = false)]
        //public decimal? StopPrice { get; set; }
        //public TradeSide Side { get; set; }
        //public long Amount { get; set; }
        //public long AmountLeft { get; set; }

        //public OrderState State { get; set; }
        //public OrderType Type { get; set; }
        //public long CreatedAt { get; set; }
        //public long Timestamp { get; set; }
        //public string StateMessage { get; set; }
        //[DataMember(EmitDefaultValue = false)]
        //public long? OrderIdRef { get; set; }

        /// <summary>
        /// Present if order has matches
        /// </summary>
        //public ClientDealInfo[] Deals { get; set; }

        public long ExchangeOrderId { get; }
        public long ClientHashId { get; }
        public OrderState State { get; }

        public ClientOrder(long id, long ex, string s, TradeSide sd, OrderState st, decimal p, decimal a, long t, ClientDealInfo[] d)
        {
            ExchangeOrderId = id;
            ClientHashId = ex;
            State = st;

            Isin = s;
            Side = sd == TradeSide.Buy ? OrderSide.Buy : OrderSide.Sell;
            Status = st.ToString();
            Price = p;
            Qty = a;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime;

            if (d == null) return;

            decimal sumTradeQty = 0;
            decimal sumTradeFee = 0;

            foreach (ClientDealInfo deal in d)
            {
                sumTradeQty += deal.Amount;
                sumTradeFee += deal.Fee;
            }

            TradeQty = sumTradeQty;
            TradeFee = sumTradeFee;
        }

        public void SetClientOrderId(string orderId) => OrderId = orderId;
    }
}