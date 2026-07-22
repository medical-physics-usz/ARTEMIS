using System;

namespace USZ_ARTEMIS.Core.Geometry
{
    public static class OverlapMetrics
    {
        public static double CalculateDiceSorensen(
            double firstMeasure,
            double secondMeasure,
            double intersectionMeasure)
        {
            ValidateMeasure(firstMeasure, nameof(firstMeasure));
            ValidateMeasure(secondMeasure, nameof(secondMeasure));
            ValidateMeasure(intersectionMeasure, nameof(intersectionMeasure));

            double denominator = firstMeasure + secondMeasure;
            if (denominator <= double.Epsilon)
            {
                return 1.0;
            }

            double dice = 2.0 * intersectionMeasure / denominator;
            return Math.Max(0.0, Math.Min(1.0, dice));
        }

        private static void ValidateMeasure(double measure, string parameterName)
        {
            if (measure < 0.0 || double.IsNaN(measure) || double.IsInfinity(measure))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
