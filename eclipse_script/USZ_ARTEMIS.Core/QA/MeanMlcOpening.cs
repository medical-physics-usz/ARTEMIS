using System.Collections.Generic;

namespace USZ_ARTEMIS.Core.QA
{
    public static class MeanMlcOpening
    {
        private const double ClosedGapToleranceMm = 0.01;

        public static double CalculateSingleLayer(IEnumerable<float[,]> controlPointLeafPositions)
        {
            int? leafPairCount = null;
            long openPairs = 0;
            double totalGap = 0;

            foreach (float[,] positions in controlPointLeafPositions)
            {
                if (positions == null || positions.GetLength(0) != 2)
                    return double.NaN;

                int pairs = positions.GetLength(1);
                if (leafPairCount.HasValue && leafPairCount.Value != pairs)
                    return double.NaN;

                leafPairCount = pairs;
                for (int leaf = 0; leaf < pairs; leaf++)
                {
                    double gap = (double)positions[1, leaf] - positions[0, leaf];
                    if (double.IsNaN(gap) || double.IsInfinity(gap))
                        return double.NaN;

                    if (gap > ClosedGapToleranceMm)
                    {
                        totalGap += gap;
                        openPairs++;
                    }
                }
            }

            return openPairs == 0 ? double.NaN : totalGap / openPairs;
        }
    }
}
