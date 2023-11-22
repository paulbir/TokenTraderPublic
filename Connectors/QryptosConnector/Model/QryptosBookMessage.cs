using System.Collections.Generic;
using System.Linq;
using SharedDataStructures.Messages;
using Newtonsoft.Json;

namespace QryptosConnector.Model
{
    class QryptosBookMessage : BookMessage
    {
        public QryptosBookMessage(string isin, List<QryptosPriceLevel> bids, List<QryptosPriceLevel> asks)
        {
            Isin = isin;
            Sequence = 0;
            Bids = bids.Select(bid => (PriceLevel)bid).ToList();
            Asks = asks.Select(ask => (PriceLevel)ask).ToList();
        }

        [JsonConstructor]
        public QryptosBookMessage(List<List<string>> buy_price_levels, List<List<string>> sell_price_levels)
        {
            Sequence = 0;
            Bids = buy_price_levels.Select(level => (PriceLevel)(new QryptosPriceLevel(level))).ToList();
            Asks = sell_price_levels.Select(level => (PriceLevel)(new QryptosPriceLevel(level))).ToList();
        }

        public void SetIsin(string isin)
        {
            Isin = isin;
        }
    }
}