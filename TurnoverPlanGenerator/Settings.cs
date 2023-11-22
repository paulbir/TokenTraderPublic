using System.Collections.Generic;
using SharedDataStructures.Exceptions;

namespace TurnoverPlanGenerator
{
    class Settings
    {
        public List<IsinParams> IsinsToPlan { get; set; }
        public int TurnoverPeriodMins { get; set; }
        public double TurnoverSigmaFrac { get; set; }

        public void Verify()
        {
            if (IsinsToPlan == null || IsinsToPlan.Count == 0) throw new ConfigErrorsException("IsinsToPlan was not set properly.");
            foreach (IsinParams isinToPlan in IsinsToPlan)
            {
                if (string.IsNullOrEmpty(isinToPlan.Isin)) throw new ConfigErrorsException("Isin in IsinsToPlan was not set properly.");
                if (isinToPlan.DailyAverageTurnoverUSD <= 0) throw new ConfigErrorsException("DailyAverageTurnoverUSD in IsinsToPlan was not set properly.");
                if (isinToPlan.PlanHorizonDays <= 0) throw new ConfigErrorsException("PlanHorizonDays in IsinsToPlan was not set properly.");
                if (isinToPlan.MinNumOfEmptyPeriods == null) throw new ConfigErrorsException("MinNumOfEmptyPeriods in IsinsToPlan was not set properly.");
                if (isinToPlan.MaxNumOfEmptyPeriods == null) throw new ConfigErrorsException("MaxNumOfEmptyPeriods in IsinsToPlan was not set properly.");
                if (isinToPlan.DailyTurnoverTrendFrac == null) throw new ConfigErrorsException("DailyTurnoverTrendFrac in IsinsToPlan was not set properly.");
            }

            if (TurnoverPeriodMins <= 0) throw new ConfigErrorsException("TurnoverPeriodMins was not set properly.");
            if (TurnoverSigmaFrac <= 0) throw new ConfigErrorsException("TurnoverSigmaFrac was not set properly.");
        }
    }
}