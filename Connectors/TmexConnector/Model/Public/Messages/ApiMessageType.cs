using TmexConnector.Model.Public.Data;
using TmexConnector.Model.Public.Data.Req;
using TmexConnector.Model.Shared.Data;

namespace TmexConnector.Model.Public.Messages
{
    /// <summary>
    /// Message type specified in message envelope.
    /// NOTE: Do not change the order of elements and do not remove any, only add new at the end.
    /// </summary>
    public enum ApiMessageType : int
    {
        /// <summary>
        /// Error message type.
        /// Data will be null.
        /// </summary>
        Error = 0,

        /// <summary>
        /// Server time.
        /// <see cref="long"/>
        /// </summary>
        Heartbeat = 1,

        /// <summary>
        /// <see cref="SystemEvent"/>
        /// </summary>
        System = 2,

        
        CmdPing = 3,

        /// <summary>
        /// <see cref="ReqApiKeyLogin"/>
        /// </summary>
        CmdAuthenticate = 4,

        /// <summary>
        /// Stream of format: STREAMNAME[.SUBSTREAM], where SUBSTREAM is an asset symbol
        /// for assets subscriptions.
        /// <see cref="string"/>
        /// </summary>
        CmdSubscribe = 5,

        /// <summary>
        /// Stream of format: STREAMNAME[.SUBSTREAM], where SUBSTREAM is an asset symbol
        /// for assets subscriptions.
        /// <see cref="string"/>
        /// </summary>
        CmdUnsubscribe = 6,

        /// <summary>
        /// <see cref="ReqPlaceOrder"/>
        /// </summary>
        CmdPlaceOrder = 7,

        /// <summary>
        /// <see cref="ReqCancelOrder"/>
        /// </summary>
        CmdCancelOrder = 8,
        
        /// <summary>
        /// <see cref="Data.WebsocketPath"/>
        /// </summary>
        WebsocketPath = 9,

        /// <summary>
        /// <see cref="RegistrationResult"/>
        /// </summary>
        Registration = 10,

        /// <summary>
        /// <see cref="Data.JwtToken"/>
        /// </summary>
        JwtToken = 11,

        /// <summary>
        /// <see cref="AssetList"/>
        /// </summary>
        AssetsList = 12,

        /// <summary>
        /// <see cref="Data.ClientProfile"/>
        /// </summary>
        ClientProfile = 13,

        /// <summary>
        /// <see cref="Data.ApiKey"/>
        /// </summary>
        ApiKey = 14,

        /// <summary>
        /// <see cref="long"/>
        /// </summary>
        Time = 15,

        /// <summary>
        /// <see cref="AccountData"/>
        /// </summary>
        Account = 16,

        /// <summary>
        /// <see cref="ClientOrder"/>
        /// </summary>
        Order = 17,

        /// <summary>
        /// <see cref="Data.Quote"/>
        /// </summary>
        Quote = 18,

        /// <summary>
        /// <see cref="IndexValueUpdate"/>
        /// </summary>
        IndexValue = 19,

        /// <summary>
        /// <see cref="Shared.Data.AssetDeal"/>
        /// </summary>
        AssetDeal = 20,

        /// <summary>
        /// <see cref="Shared.Data.ClientDeal"/>
        /// </summary>
        ClientDeal = 21,

        /// <summary>
        /// <see cref="OrderBookLevel"/>
        /// </summary>
        Book = 22,

        /// <summary>
        /// Any <see cref="bool"/> result
        /// </summary>
        Boolean = 23,

        /// <summary>
        /// <see cref="WalletData"/>
        /// </summary>
        Wallet = 24,
        
        /// <summary>
        /// <see cref="Deposit"/>
        /// </summary>
        ClientDeposit = 25,

        /// <summary>
        /// <see cref="string"/> - hash of transaction
        /// </summary>
        WithdrawTransaction = 26,

        /// <summary>
        /// <see cref="ReqPlaceOrder"/>
        /// </summary>
        CmdGetMarginChange = 27,

        /// <summary>
        /// <see cref="Withdrawal"/>
        /// </summary>
        ClientWithdrawal = 28,

        /// <summary>
        /// <see cref="TradeableAssetDescription"/>
        /// </summary>
        AssetDetails = 29,
        
        /// <summary>
        /// Obsolete
        /// </summary>
        //ClientClaims = 30,

        /// <summary>
        /// <see cref="TfaData"/>
        /// </summary>
        Tfa = 31,

        /// <summary>
        /// <see cref="bool"/>
        /// </summary>
        CmdGetAssets = 32,

        /// <summary>
        /// <see cref="LoginEventData"/>
        /// </summary>
        ClientLogins = 33,

        /// <summary>
        /// <see cref="Report"/>
        /// </summary>
        ClientReports = 34,

        /// <summary>
        /// </summary>
        CmdAuthUpdate = 35,
        
        /// <summary>
        /// <see cref="Data.WsSessionEvent"/>
        /// </summary>
        WsSessionEvent = 36,

        /// <summary>
        /// <see cref="LiquidationFundState"/>
        /// </summary>
        LiquidationFundState = 37,
        
        /// <summary>
        /// <see cref="VolatilityCurveVars"/>
        /// </summary>
        VolatilityCurveVars = 38,
        
        /// <summary>
        /// <see cref="ClientFeeInfo"/>
        /// </summary>
        ClientFee = 39,

        /// <summary>
        /// <see cref="Data.ClientApplicationState"/>
        /// </summary>
        ClientApplicationState = 40,
        
        /// <summary>
        /// <see cref="ReqMoveOrder"/>
        /// </summary>
        CmdMoveOrder = 41,
    }
}