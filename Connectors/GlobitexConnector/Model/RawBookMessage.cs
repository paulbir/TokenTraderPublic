using Newtonsoft.Json;

namespace GlobitexConnector.Model
{
    class RawBookMessage
    {
        [JsonProperty("MarketDataSnapshotFullRefresh")]
        public GlobitexBookMessage Snapshot { get; set; }

        [JsonProperty("MarketDataIncrementalRefresh")]
        public GlobitexBookMessage Update { get; set; }
    }
}