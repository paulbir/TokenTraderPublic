using System;
using System.Collections.Generic;

namespace SharedTools
{
    public static class NormalDistribution
    {
        public static double PDF(double xSigmas, double mu, double sigma)
        {
            double twiceSigmaSquared = 2 * sigma * sigma;
            return 1 / 
                   Math.Sqrt(Math.PI * twiceSigmaSquared) * 
                   Math.Exp(-1 * (xSigmas - mu) * (xSigmas - mu) / twiceSigmaSquared);
        }

        public static List<double> PDFPoints(int numPoints, double mu, double sigma)
        {
            int halfPoints = (numPoints - 1) / 2;
            var points = new List<double>();

            for (int i = -1 * halfPoints; i <= halfPoints; i++) points.Add(PDF(i, mu, sigma));

            return points;
        }
    }
}
