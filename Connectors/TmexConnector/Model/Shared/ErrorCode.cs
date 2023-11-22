namespace TmexConnector.Model.Shared
{
    public enum ErrorCode
    {
        #region 0:999

        Success = 0,
        SystemError,
        /// <summary>
        /// No authorization
        /// </summary>
        NotAuthorized,
        BadRequest,
        UnknownCommand,
        InvalidCredentials,
        InvalidPayload,
        AlreadyAuthenticated,
        TooManyRequests,
        RequestFailed,
        RequestCancelled,
        RequestTimeout,
        NotEnoughData,
        RequestIsTooBig,
        PermissionDenied,
        InvalidReferralCode,
        InvalidCountryCode,
        WeakPassword,
        InvalidCaptcha,
        // New code goes here

        #endregion

        #region 1000:1999

        E1000 = 1000,

        Maintenance,
        MetricsDisabled,
        SystemOffline,
        // New code goes here

        #endregion

        #region 2000:2999

        E2000 = 2000,

        AssetNotFound,
        AssetLocked,
        AssetIsInvalid,
        AssetHasNoParent,
        InvalidSymbol,
        // New code goes here

        #endregion

        #region 3000:3999

        E3000 = 3000,

        ClientNotFound,
        PortfolioNotFound,
        PositionNotFound,
        InsufficientFunds,
        ClientDisabled,
        FailedToCreateAccount,
        InvalidPrice,
        InvalidAmount,

        OrderNotFound,
        TooManyOrders,
        DuplicateExternalId,
        AccountNotFound,
        TokenNotFound,
        TfaFailed,
        TfaTokenNotFound,
        TfaEnabled,
        TfaDisabled,
        EmailNotConfirmed,
        EmailConfirmed,
        InvalidNonce,
        ClientEnabled,
        ReportNotFound,
        ReportNotReady,
        ReportInvalid,
        ReportProcessing,
        TokenNotRenewed,
        InvalidOrderType,
        InvalidTitle,
        TitleAlreadyExists,
        PriceOutsideOfRange,
        OrderRequestExpired,
        InvalidPortfolio,
        // New code goes here

        #endregion

        #region 4000:4999

        E4000 = 4000,

        DuplicateSubscription,
        TooManySubscriptions,
        NotSubscribed,
        InvalidStream,
        InvalidRevision,
        // New code goes here

        #endregion

        #region 5000:5999

        E5000 = 5000,

        WalletsCreationFrequencyExceeded,
        WithdrawalDisabledForCurrency,
        InvalidWallet,
        DepositDisabledForCurrency,

        #endregion
    }
}