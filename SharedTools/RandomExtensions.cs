using System;

namespace SharedTools
{
    public static class RandomExtensions
    {
        /// <summary>
        ///   Generates normally distributed numbers. Each operation makes two Gaussians for the price of one, and apparently they can be cached or something for better performance, but who cares.
        /// </summary>
        /// <param name="rnd"></param>
        /// <param name = "mu">Mean of the distribution</param>
        /// <param name = "sigma">Standard deviation</param>
        /// <returns></returns>
        public static double NextGaussian(this Random rnd, double mu = 0, double sigma = 1)
        {
            double u1 = rnd.NextDouble();
            double u2 = rnd.NextDouble();

            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            double randNormal = mu + sigma * randStdNormal;

            return randNormal;
        }

        public static bool Next50PercentChoice(this Random rnd) => rnd.Next(2) == 0;
    }
}