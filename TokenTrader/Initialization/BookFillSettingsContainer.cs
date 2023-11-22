using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class BookFillSettingsContainer : BaseSettings
    {
        public BookFillSettings TradeModelSettings { get; set; }

        public string HedgeConnector { get; set; }
        public List<ConnectorSettings> HedgeConnectorsSettings { get; set; }

        public override void Verify()
        {
            base.Verify();

            TradeModelSettings.Verify();

            if (TradeModelSettings.IsinsToTrade.Any(isinToTrade => isinToTrade.UseHedge.HasValue && isinToTrade.UseHedge.Value))
            {
                if (string.IsNullOrEmpty(HedgeConnector)) throw new ConfigErrorsException("HedgeConnector was not set properly.");
                if (HedgeConnectorsSettings == null || HedgeConnectorsSettings.Count == 0) throw new ConfigErrorsException("HedgeConnectorsSettings was not set properly.");
                foreach (ConnectorSettings connectorsSetting in HedgeConnectorsSettings)
                {
                    if (string.IsNullOrEmpty(connectorsSetting.PubKey)) throw new ConfigErrorsException("PubKey in HedgeConnectorsSettings was not set properly.");
                    if (string.IsNullOrEmpty(connectorsSetting.SecKey)) throw new ConfigErrorsException("SecKey in HedgeConnectorsSettings was not set properly.");
                }
            }
        }

        public override void SetDerivativeSettings()
        {
            base.SetDerivativeSettings();
            TradeModelSettings.SetDerivativeSettings();
        }
    }
}