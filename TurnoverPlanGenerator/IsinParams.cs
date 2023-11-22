namespace TurnoverPlanGenerator
{
    class IsinParams
    {
        public string Isin { get; set; }
        public int DailyAverageTurnoverUSD { get; set; }
        public int PlanHorizonDays { get; set; }
        public int? MinNumOfEmptyPeriods { get; set; }
        public int? MaxNumOfEmptyPeriods { get; set; }
        public decimal? DailyTurnoverTrendFrac { get; set; }
    }
}