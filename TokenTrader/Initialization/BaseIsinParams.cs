using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class BaseIsinParams
    {
        public string Isin { get; set; }
        public string BuyMarginCurrency { get; set; }
        public string SellMarginCurrency { get; set; }
        public decimal MinQty { get; set; }
        public decimal MinStep { get; set; }
        public decimal MinOrderVolume { get; set; }
        public string ConvertToFiatIsin { get; set; }
        public decimal Leverage { get; set; }
        public decimal LotSize { get; set; }
        public bool? IsReverse { get; set; }

        public virtual void Verify()
        {
            if (string.IsNullOrEmpty(Isin)) throw new ConfigErrorsException("Isin in IsinsToTrade was not set properly.");
            if (string.IsNullOrEmpty(BuyMarginCurrency)) throw new ConfigErrorsException("BuyMarginCurrency in IsinsToTrade was not set properly.");
            if (string.IsNullOrEmpty(SellMarginCurrency)) throw new ConfigErrorsException("SellMarginCurrency in IsinsToTrade was not set properly.");
            if (MinQty <= 0) throw new ConfigErrorsException("MinQty in IsinsToTrade was not set properly.");
            if (MinStep <= 0) throw new ConfigErrorsException("MinStep in IsinsToTrade was not set properly.");
            if (MinOrderVolume < 0) throw new ConfigErrorsException("MinOrderVolume in IsinsToTrade was not set properly.");
            if (string.IsNullOrEmpty(ConvertToFiatIsin)) throw new ConfigErrorsException("ConvertToFiatIsin in IsinsToTrade was not set properly.");

            if (Leverage <= 0) throw new ConfigErrorsException("Leverage in IsinsToTrade was not set properly.");
            if (LotSize <= 0) throw new ConfigErrorsException("LotSize in IsinsToTrade was not set properly.");
            if (IsReverse == null) throw new ConfigErrorsException("IsReverse in IsinsToTrade was not set properly.");
        }

        public virtual void SetDerivativeSettings()
        {

        }
    }
}