using System.Collections.Generic;
using Newtonsoft.Json;
using SharedDataStructures.Messages;

namespace WoortonConnector.Model
{
    class RawPosNBalanceMessage
    {
        [JsonProperty("USD")]
        public decimal UsdBalance { get; set; }

        [JsonProperty("EUR")]
        public decimal EurBalance { get; set; }

        [JsonProperty("GBP")]
        public decimal GbpBalance { get; set; }

        [JsonProperty("CHF")]
        public decimal ChfBalance { get; set; }

        [JsonProperty("CAD")]
        public decimal CadBalance { get; set; }

        [JsonProperty("UST")]
        public decimal UstBalance { get; set; }

        [JsonProperty("ETH")]
        public decimal EthBalance { get; set; }

        [JsonProperty("BTC")]
        public decimal BtcBalance { get; set; }

        [JsonProperty("LTC")]
        public decimal LtcBalance { get; set; }

        [JsonProperty("BCH")]
        public decimal BchBalance { get; set; }

        [JsonProperty("XRP")]
        public decimal XrpBalance { get; set; }

        [JsonProperty("EOS")]
        public decimal EosBalance { get; set; }

        public List<BalanceMessage> MakeBalancesToSend()
        {
            return new List<BalanceMessage>
                   {
                       new WoortonBalanceMessage("USD", UsdBalance),
                       new WoortonBalanceMessage("EUR", EurBalance),
                       new WoortonBalanceMessage("GBP", GbpBalance),
                       new WoortonBalanceMessage("CHF", ChfBalance),
                       new WoortonBalanceMessage("CAD", CadBalance),
                       new WoortonBalanceMessage("UST", UstBalance),
                       new WoortonBalanceMessage("ETH", EthBalance),
                       new WoortonBalanceMessage("BTC", BtcBalance),
                       new WoortonBalanceMessage("LTC", LtcBalance),
                       new WoortonBalanceMessage("BCH", BchBalance),
                       new WoortonBalanceMessage("XRP", XrpBalance),
                       new WoortonBalanceMessage("EOS", EosBalance)
                   };
        }

        public List<PositionMessage> MakePositionsToSend()
        {
            return new List<PositionMessage>
                   {
                       new WoortonPositionMessage("USD", UsdBalance),
                       new WoortonPositionMessage("EUR", EurBalance),
                       new WoortonPositionMessage("GBP", GbpBalance),
                       new WoortonPositionMessage("CHF", ChfBalance),
                       new WoortonPositionMessage("CAD", CadBalance),
                       new WoortonPositionMessage("UST", UstBalance),
                       new WoortonPositionMessage("ETH", EthBalance),
                       new WoortonPositionMessage("BTC", BtcBalance),
                       new WoortonPositionMessage("LTC", LtcBalance),
                       new WoortonPositionMessage("BCH", BchBalance),
                       new WoortonPositionMessage("XRP", XrpBalance),
                       new WoortonPositionMessage("EOS", EosBalance)
                   };
        }
    }
}