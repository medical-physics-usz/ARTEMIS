using System;
using System.Collections.Generic;

namespace USZ_ARTEMIS.Core.QA
{
    public enum SmallApertureScoreStatus
    {
        InvalidLeafPositions,
        NoControlPoints,
        NoMlc,
        NoOpenLeafPairs,
        Available
    }

    public readonly struct SmallApertureScoreResult
    {
        internal SmallApertureScoreResult(SmallApertureScoreStatus status, double? percentage = null)
        {
            Status = status;
            Percentage = percentage;
        }

        public SmallApertureScoreStatus Status { get; }
        public double? Percentage { get; }
    }

    public static class SmallApertureScore
    {
        private const double SmallGapMm = 10.0;

        public static SmallApertureScoreResult Calculate(IEnumerable<float[,]> controlPointLeafPositions)
        {
            int? leafPairCount = null;
            long openPairs = 0;
            long smallOpenPairs = 0;

            foreach (float[,] positions in controlPointLeafPositions)
            {
                if (positions == null)
                    return new SmallApertureScoreResult(SmallApertureScoreStatus.InvalidLeafPositions);

                int banks = positions.GetLength(0);
                int pairs = positions.GetLength(1);
                // ESAPI reports a [0, 0] array at each control point for beams without an MLC.
                bool noMlc = banks == 0 && pairs == 0;
                if ((!noMlc && (banks != 2 || pairs == 0)) ||
                    (leafPairCount.HasValue && leafPairCount.Value != pairs))
                {
                    return new SmallApertureScoreResult(SmallApertureScoreStatus.InvalidLeafPositions);
                }

                leafPairCount = pairs;
                for (int leaf = 0; leaf < pairs; leaf++)
                {
                    double gap = (double)positions[1, leaf] - positions[0, leaf];
                    if (double.IsNaN(gap) || double.IsInfinity(gap))
                        return new SmallApertureScoreResult(SmallApertureScoreStatus.InvalidLeafPositions);

                    if (gap <= 0)
                        continue;

                    openPairs++;
                    if (gap < SmallGapMm)
                        smallOpenPairs++;
                }
            }

            if (!leafPairCount.HasValue)
                return new SmallApertureScoreResult(SmallApertureScoreStatus.NoControlPoints);
            if (leafPairCount.Value == 0)
                return new SmallApertureScoreResult(SmallApertureScoreStatus.NoMlc);
            if (openPairs == 0)
                return new SmallApertureScoreResult(SmallApertureScoreStatus.NoOpenLeafPairs);

            return new SmallApertureScoreResult(SmallApertureScoreStatus.Available, 100.0 * smallOpenPairs / openPairs);
        }
    }
}
