using TmexConnector.Model.Public.Data;
using TmexConnector.Model.Shared.Data;

namespace TmexConnector.Model.Public.Messages
{
    public static class WsStreams
    {
        /// <summary>
        /// <see cref="SystemEvent"/>
        /// <see cref="ApiMessageType.System"/>
        /// </summary>
        public const string System = "sys";

        /// <summary>
        /// <see cref="long"/>
        /// <see cref="ApiMessageType.Heartbeat"/>
        /// </summary>
        public const string Heartbeat = "hbt";

        /// <summary>
        /// <see cref="AccountData"/>
        /// <see cref="ApiMessageType.Account"/>
        /// </summary>
        public const string Accounts = "accounts";

        /// <summary>
        /// <see cref="ClientOrder"/>
        /// <see cref="ApiMessageType.Order"/>
        /// </summary>
        public const string Orders = "orders";
        
        /// <summary>
        /// <see cref="ClientFeeInfo"/>
        /// <see cref="ApiMessageType.ClientFee"/>
        /// </summary>
        public const string Fees = "fees";

        /// <summary>
        /// <see cref="AssetDeal"/>.
        /// <see cref="ApiMessageType.AssetDeal"/>
        /// </summary>
        public const string AssetDeals = "deals";

        /// <summary>
        /// <see cref="Quote"/>
        /// <see cref="ApiMessageType.Quote"/>
        /// </summary>
        public const string Quotes = "quote";

        /// <summary>
        /// <see cref="IndexValueUpdate"/>
        /// <see cref="ApiMessageType.IndexValue"/>
        /// </summary>
        public const string IndexValues = "index";

        /// <summary>
        /// <see cref="OrderBookLevel"/>
        /// <see cref="ApiMessageType.Book"/>
        /// </summary>
        public const string Book = "book";
        
        /// <summary>
        /// <see cref="WsSessionEvent"/>
        /// <see cref="ApiMessageType.WsSessionEvent"/>
        /// </summary>
        public const string Session = "sess";
    }
}