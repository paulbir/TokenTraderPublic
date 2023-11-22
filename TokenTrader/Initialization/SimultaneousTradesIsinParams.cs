using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class SimultaneousTradesIsinParams : BaseIsinParams
    {
        public decimal MinTradeVolumeMu { get; set; }
        public decimal SmallRoundValue { get; set; }
        public decimal LargeRoundValue { get; set; }
        public decimal VWAPQty { get; set; }

        //public SimultaneousTradesIsinParams(bool? IsReverse) : base(IsReverse)
        //{

        //}

        public override void Verify()
        {
            base.Verify();

            if (MinTradeVolumeMu < 0) throw new ConfigErrorsException("MinTradeVolumeMu in IsinsToTrade was not set properly.");
            if (SmallRoundValue <= 0) throw new ConfigErrorsException("SmallRoundValue in IsinsToTrade was not set properly.");
            if (LargeRoundValue <= 0) throw new ConfigErrorsException("LargeRoundValue in IsinsToTrade was not set properly.");
            if (VWAPQty < 0) throw new ConfigErrorsException("VWAPQty in IsinsToTrade was not set properly.");
        }
    }
}