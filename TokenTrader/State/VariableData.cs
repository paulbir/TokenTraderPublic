using System;
using System.Collections.Generic;
using TokenTrader.Initialization;

namespace TokenTrader.State
{
    class VariableData
    {
        public PricesState          PricesState          { get; }
        public List<FormulaContext> BaseFormulas         { get; }
        public List<FormulaContext> PredictorFormulas    { get; }
        public DateTime             LastUpdatedTimestamp { get; set; } = DateTime.MinValue;

        public VariableData(List<FormulaContext> baseFormulas,
                            List<FormulaContext> predictorFormulas,
                            decimal              maxSpreadForReadyPricesPerc,
                            decimal              defaultValue,
                            int                  numBookLevelsToSendUdp,
                            int                  bookErrorQueueWindowMs,
                            bool                 checkBookCross)
        {
            BaseFormulas      = baseFormulas      ?? new List<FormulaContext>();
            PredictorFormulas = predictorFormulas ?? new List<FormulaContext>();

            PricesState = new PricesState(maxSpreadForReadyPricesPerc, numBookLevelsToSendUdp, bookErrorQueueWindowMs, checkBookCross, defaultValue);
        }
    }
}