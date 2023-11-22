using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using SharedDataStructures.Exceptions;
using SharedTools;

namespace TurnoverPlanGenerator
{
    class Program
    {
        //const int TurnoverPeriodMin = 10;
        const int MinNumOfActivePeriods = 0;
        const int MaxNumOfActivePeriods = 4;
        const int ActivePeriodLastsMinutes = 90;
        const double ActivePeriodsTurnoverFrac = 0.5;
        const double ActivePeriodShapeMu = 0;
        const double ActivePeriodShapeSigma = 2.5;
        const int EmptyPeriodLastsMinutes = 30;
        //const double TurnoverSigmaFrac = 0.25;

        static void Main(string[] args)
        {
            IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();
            var settings = new Settings();
            config.Bind(settings);
            settings.Verify();

            if (settings.IsinsToPlan.Count == 0) throw new ConfigErrorsException("IsinsToPlan has to cantain at leat 1 element.");

            foreach (IsinParams isinToPlan in settings.IsinsToPlan)
            {
                Directory.CreateDirectory("plans");
                string plansPath = Path.Combine("plans", $"{isinToPlan.Isin.Replace("/", "[slash]")}_turnover_plan");
                using (var sw = new StreamWriter(plansPath))
                {
                    (int numPoints, string header) = CalcNumPointsAndMakeHeader(settings.TurnoverPeriodMins);
                    sw.WriteLine(header);

                    var rnd = new Random();

                    int impliedNumActivePeriodPoints = (int)Math.Round((double)ActivePeriodLastsMinutes / settings.TurnoverPeriodMins);
                    int numEmptyPeriodPoints = (int)Math.Round((double)EmptyPeriodLastsMinutes / settings.TurnoverPeriodMins);
                    DateTime today = DateTime.Today;
                    List<double> normalPDFPoints = NormalDistribution.PDFPoints(impliedNumActivePeriodPoints, ActivePeriodShapeMu, ActivePeriodShapeSigma);
                    int realNumActivePeriodPoints = normalPDFPoints.Count;

                    //так как получаем некоторое количество дискретных точек, то площадь под ними < 1. это занижает суммарный оборот.
                    //поэтому вводим поправочный коэффициент. будем на него точки домножать. тогда с учётом коэффициента площадь будет = 1.
                    double correctionCoef = 1 + (1 - normalPDFPoints.Sum()) / normalPDFPoints.Sum();

                    double newDayAverageTurnoverUSD = isinToPlan.DailyAverageTurnoverUSD;
                    for (int i = 0; i <= isinToPlan.PlanHorizonDays; i++)
                    {
                        List<int> dayTurnoverPoints = CreateDayTurnover(rnd,
                                                                        newDayAverageTurnoverUSD,
                                                                        settings.TurnoverSigmaFrac,
                                                                        isinToPlan.MinNumOfEmptyPeriods.Value,
                                                                        isinToPlan.MaxNumOfEmptyPeriods.Value,
                                                                        numPoints,
                                                                        realNumActivePeriodPoints,
                                                                        numEmptyPeriodPoints,
                                                                        normalPDFPoints,
                                                                        correctionCoef);

                        string line = $"{today.AddDays(i).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)};{string.Join(';', dayTurnoverPoints)}";
                        sw.WriteLine(line);

                        //если хотим трэнд, то каждый новый день домножаем на коэффициент.
                        newDayAverageTurnoverUSD *= 1 + (double)isinToPlan.DailyTurnoverTrendFrac.Value;
                    }
                }
            }
        }

        static (int, string) CalcNumPointsAndMakeHeader(int turnoverPeriodMin)
        {
            int numPoints = 0;
            var startTime = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string header = "date;";
            DateTime shiftedTime = startTime;

            while (shiftedTime.Day == startTime.Day)
            {
                shiftedTime = shiftedTime.AddMinutes(turnoverPeriodMin);
                if (shiftedTime.TimeOfDay > startTime.TimeOfDay)
                {
                    header += shiftedTime.ToString("HH:mm:ss;");
                    numPoints++;
                }
            }

            return (numPoints, header.TrimEnd(';'));
        }

        static List<int> CreateDayTurnover(Random rnd,
                                           double dailyAverageTurnoverUSD,
                                           double turnoverSigmaFrac,
                                           int minNumOfEmptyPeriods,
                                           int maxNumOfEmptyPeriods,
                                           int numPoints,
                                           int numActivePeriodPoints,
                                           int numEmptyPeriodPoints,
                                           List<double> normalPDFPoints,
                                           double correctionCoef)
        {
            int numActivePeriods = rnd.Next(MinNumOfActivePeriods, MaxNumOfActivePeriods + 1);
            int numEmptyPeriods = rnd.Next(minNumOfEmptyPeriods, maxNumOfEmptyPeriods + 1);

            double turnoverSigma = dailyAverageTurnoverUSD * turnoverSigmaFrac;
            int targerDailyTurnover = Math.Max((int)Math.Round(rnd.NextGaussian(dailyAverageTurnoverUSD, turnoverSigma)), 0);
            int targetActivePeriodTurnover = numActivePeriods == 0 ? 0 : (int)Math.Round(targerDailyTurnover * ActivePeriodsTurnoverFrac / numActivePeriods);

            double targetFlatTurnover = numActivePeriods > 0 ? targerDailyTurnover * (1 - ActivePeriodsTurnoverFrac) : targerDailyTurnover;

            int targetFlatPointTurnover =
                (int)Math.Round(targetFlatTurnover / (numPoints - numActivePeriodPoints * numActivePeriods - numEmptyPeriodPoints * numEmptyPeriods));

            List<int> specialPeriodsStartPoints =
                ChooseSpecialPeriodStartPoints(rnd, numPoints, numActivePeriods, numActivePeriodPoints, numEmptyPeriods, numEmptyPeriodPoints);

            List<int> dayTurnoverPoints = FillDayTurnoverPoints(rnd,
                                                                normalPDFPoints,
                                                                correctionCoef,
                                                                numPoints,
                                                                numActivePeriodPoints,
                                                                numEmptyPeriodPoints,
                                                                targetActivePeriodTurnover,
                                                                targetFlatPointTurnover,
                                                                turnoverSigmaFrac,
                                                                specialPeriodsStartPoints);
            //int sum = dayTurnoverPoints.Sum();
            return dayTurnoverPoints;
        }

        static List<int> ChooseSpecialPeriodStartPoints(Random rnd,
                                                        int numPoints,
                                                        int numActivePeriods,
                                                        int numActivePeriodPoints,
                                                        int numEmptyPeriods,
                                                        int numEmptyPeriodPoints)
        {
            var specialPeriodsStartPoints = new List<int>();
            if (numActivePeriods == 0 && numEmptyPeriods == 0) return specialPeriodsStartPoints;

            List<int> specialPeriosTypeIndicators = Enumerable.Repeat(1, numActivePeriods).ToList();
            specialPeriosTypeIndicators.AddRange(Enumerable.Repeat(-1, numEmptyPeriods));
            specialPeriosTypeIndicators.Shuffle();

            int sectorWidth = numPoints / (numActivePeriods + numEmptyPeriods);
            for (int i = 0; i < numActivePeriods + numEmptyPeriods; i++)
            {
                int lowerActivePeriodBound = sectorWidth * i;

                //верхняя граница отстоит от следующего сектора на ширину специального периода, чтобы не было наложения.
                int upperActivePeriodBound = lowerActivePeriodBound + sectorWidth - Math.Max(numActivePeriodPoints, numEmptyPeriodPoints);

                if (upperActivePeriodBound <= lowerActivePeriodBound)
                    throw new ConfigErrorsException("Special period length or number of special periods is too large. " + "Can't fit them into a trading day.");

                int specialPeriodStartPoint = rnd.Next(lowerActivePeriodBound, upperActivePeriodBound + 1);

                specialPeriodsStartPoints.Add(specialPeriodStartPoint * specialPeriosTypeIndicators[i]);
            }

            return specialPeriodsStartPoints;
        }

        static List<int> FillDayTurnoverPoints(Random rnd,
                                               List<double> normalPDFPoints,
                                               double correctionCoef,
                                               int numPoints,
                                               int numActivePeriodPoints,
                                               int numEmptyPeriodPoints,
                                               int targetActivePeriodTurnover,
                                               int targetFlatPointTurnover,
                                               double turnoverSigmaFrac,
                                               List<int> specialPeriodsStartPoints)
        {
            var dayTurnoverPoints = new List<int>();
            int activePeriodIndex = 0;
            for (int i = 0; i < numPoints; i++)
            {
                if (activePeriodIndex < specialPeriodsStartPoints.Count && i == Math.Abs(specialPeriodsStartPoints[activePeriodIndex]))
                {
                    if (specialPeriodsStartPoints[activePeriodIndex] > 0)
                    {
                        FillActivePeriodWithNormalPDF(rnd, normalPDFPoints, correctionCoef, targetActivePeriodTurnover, turnoverSigmaFrac, dayTurnoverPoints);
                        i += numActivePeriodPoints - 1;
                    }
                    else
                    {
                        for (int j = 0; j < numEmptyPeriodPoints; j++) dayTurnoverPoints.Add(0);
                        i += numEmptyPeriodPoints - 1;
                    }

                    activePeriodIndex++;
                }
                else
                {
                    int point = Math.Max((int)Math.Round(rnd.NextGaussian(targetFlatPointTurnover, targetFlatPointTurnover * turnoverSigmaFrac)), 0);
                    dayTurnoverPoints.Add(point);
                }
            }

            return dayTurnoverPoints;
        }

        //активный период будем делать в форме колокола. чтобы типа разгонялся, а потом затухал.
        //для этого берём функцию плотности нормального распределения.
        //если передавать в неё X с шагом 1 вокруг mu, то сумма значений будет равна площади под кривой, то есть 1.
        //соответственно каждое значение домножаем на целевой оборот, и в сумме получится как раз целевой оборот.
        static void FillActivePeriodWithNormalPDF(Random rnd,
                                                  List<double> normalPDFPoints,
                                                  double correctionCoef,
                                                  int targetActivePeriodTurnover,
                                                  double turnoverSigmaFrac,
                                                  List<int> dayTurnoverPoints)
        {
            int activePeriodTurnover = 0;
            while (activePeriodTurnover == 0) activePeriodTurnover = (int)Math.Round(rnd.NextGaussian(targetActivePeriodTurnover, turnoverSigmaFrac));

            foreach (double pdfPoint in normalPDFPoints)
            {
                int point = Math.Max((int)Math.Round(pdfPoint * activePeriodTurnover * correctionCoef), 0);
                dayTurnoverPoints.Add(point);
            }
        }
    }
}