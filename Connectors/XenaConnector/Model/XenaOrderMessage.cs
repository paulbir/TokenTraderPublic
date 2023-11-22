using System;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace XenaConnector.Model
{
    class XenaOrderMessage : OrderMessage
    {
        [JsonProperty(Tags.ClOrdId)]
        public string ClientOrderId { get; set; }

        [JsonProperty(Tags.OrigClOrdID)]
        public string OrigClientOrderId { get; set; }

        [JsonProperty(Tags.Symbol)]
        public string IsinRaw { get; set; }

        [JsonProperty(Tags.Side)]
        public int SideRaw { get; set; }

        [JsonProperty(Tags.OrdStatus)]
        public string StatusRaw { get; set; }

        [JsonProperty(Tags.Price)]
        public decimal PriceRaw { get; set; }

        [JsonProperty(Tags.OrderQty)]
        public decimal QtyRaw { get; set; }

        [JsonProperty(Tags.TransactTime)]
        public long TimestampRaw { get; set; }

        [JsonProperty(Tags.LastQty)]
        public decimal TradeQtyRaw { get; set; }

        [JsonProperty(Tags.Commission)]
        public decimal TradeFeeRaw { get; set; }

        [JsonProperty(Tags.RejectText)]
        public string RejectText { get; set; }

        [JsonProperty(Tags.ExecType)]
        public string ExecType { get; set; }

        [JsonProperty(Tags.Account)]
        public int AccountId { get; set; }

        public XenaOrderStatuses EnumStatus;
        public XenaExecTypes EnumExecType;

        public void SetBase()
        {
            Isin = IsinRaw;
            Side = SideRaw == 1 ? OrderSide.Buy : OrderSide.Sell;

            switch (StatusRaw)
            {
                case "0":
                    Status = "new";
                    EnumStatus = XenaOrderStatuses.New;
                    OrderId = ClientOrderId;
                    break;

                case "1":
                    Status = "partially_filled";
                    EnumStatus = XenaOrderStatuses.PartiallyFilled;
                    OrderId = ClientOrderId;
                    break;

                case "2":
                    Status = "filled";
                    EnumStatus = XenaOrderStatuses.Filled;
                    OrderId = ClientOrderId;
                    break;

                case "4":
                    Status = "cancelled";
                    EnumStatus = XenaOrderStatuses.Cancelled;
                    OrderId = OrigClientOrderId;
                    break;

                case "8":
                    Status = "rejected";
                    EnumStatus = XenaOrderStatuses.Rejected;
                    OrderId = ClientOrderId;
                    break;

                default:
                    Status = $"unknown-{StatusRaw}";
                    EnumStatus = XenaOrderStatuses.Unknown;
                    OrderId = ClientOrderId;
                    break;
            }

            switch (ExecType)
            {
                case "A":
                    EnumExecType = XenaExecTypes.PendingNew;
                    break;

                case "0":
                    EnumExecType = XenaExecTypes.New;
                    break;

                case "F":
                    EnumExecType = XenaExecTypes.Trade;
                    break;

                case "6":
                    EnumExecType = XenaExecTypes.PendingCancel;
                    break;

                case "4":
                    EnumExecType = XenaExecTypes.Cancelled;
                    break;

                case "E":
                    EnumExecType = XenaExecTypes.PendingReplace;
                    break;

                case "5":
                    EnumExecType = XenaExecTypes.Replaced;
                    break;

                case "8":
                    EnumExecType = XenaExecTypes.Rejected;
                    break;

                case "9":
                    EnumExecType = XenaExecTypes.Suspended;
                    break;
            }

            Price = PriceRaw;
            Qty = QtyRaw;
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(TimestampRaw / 1000_000).UtcDateTime;
            TradeQty = TradeQtyRaw;
            TradeFee = TradeFeeRaw;
        }
    }
}