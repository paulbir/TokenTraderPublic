using System.Collections.Generic;
using SharedDataStructures.Exceptions;

namespace TokenTrader.Initialization
{
    class BaseSettings
    {
        public string LogOutput { get; set; }
        public bool? CreateNewLogWithDate { get; set; }
        public bool? AppendLog { get; set; }
        public string TradeModel { get; set; }
        public string TradeConnector { get; set; }
        public List<ConnectorSettings> TradeConnectorsSettings { get; set; }

        public virtual void Verify()
        {
            if (string.IsNullOrEmpty(LogOutput)) throw new ConfigErrorsException("LogOutput was not set properly.");
            if (CreateNewLogWithDate == null) throw new ConfigErrorsException("CreateNewLogWithDate was not set properly.");
            if (AppendLog == null) throw new ConfigErrorsException("AppendLog was not set properly.");

            if (string.IsNullOrEmpty(TradeModel)) throw new ConfigErrorsException("TradeModel was not set properly.");

            if (string.IsNullOrEmpty(TradeConnector)) throw new ConfigErrorsException("TradeConnector was not set properly.");
            if (TradeConnectorsSettings == null || TradeConnectorsSettings.Count == 0) throw new ConfigErrorsException("TradeConnectorsSettings was not set properly.");
            foreach (ConnectorSettings connectorsSetting in TradeConnectorsSettings)
            {
                if (string.IsNullOrEmpty(connectorsSetting.PubKey)) throw new ConfigErrorsException("PubKey in TradeConnectorsSettings was not set properly.");
                if (string.IsNullOrEmpty(connectorsSetting.SecKey)) throw new ConfigErrorsException("SecKey in TradeConnectorsSettings was not set properly.");
            }
        }

        public virtual void SetDerivativeSettings()
        {

        }
    }
}
