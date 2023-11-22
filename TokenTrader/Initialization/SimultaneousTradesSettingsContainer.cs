namespace TokenTrader.Initialization
{
    class SimultaneousTradesSettingsContainer : BaseSettings
    {
        public SimultaneousTradesSettings TradeModelSettings { get; set; }

        public override void Verify()
        {
            base.Verify();

            TradeModelSettings.Verify();
        }
    }
}