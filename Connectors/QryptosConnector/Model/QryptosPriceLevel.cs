using System.Collections.Generic;
using SharedDataStructures.Messages;
using SharedTools;

namespace QryptosConnector.Model
{
    class QryptosPriceLevel : PriceLevel
    {
        public QryptosPriceLevel(List<string> priceLevel)
        {
            Price = priceLevel[0].ToDecimal();
            Qty = priceLevel[1].ToDecimal();
        }
    }
}