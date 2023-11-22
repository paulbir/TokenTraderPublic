namespace TokenTrader.OrderBook
{
    public enum BookErrors
    {
        InsertDuplicatePrice,
        InsertDuplicateId,
        UpdateNoId,
        DeleteNoId,
        DeleteNoPrice,
        DeleteNoMatchedPrice,
        MatchDownEmptyInitial,
        MatchDownEmptyLoop,
        DeleteAheadEmptyInitial,
        DeleteAheadEmptyLoop,
        MatchedPriceLevel
    }
}