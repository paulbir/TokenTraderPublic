using System.Collections.Generic;
using QryptosConnector.Model;

namespace QryptosConnector
{
    class BookData
    {
        public List<QryptosPriceLevel> Bids { get; set; }
        public List<QryptosPriceLevel> Asks { get; set; }

        public bool CanSend => Bids != null && Asks != null;

        public void ClearPrices()
        {
            Bids = null;
            Asks = null;
        }
    }
}