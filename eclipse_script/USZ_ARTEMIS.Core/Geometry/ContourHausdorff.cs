using System;
using System.Collections.Generic;
using System.Linq;

namespace USZ_ARTEMIS.Core.Geometry
{
    public struct ContourPoint2D
    {
        public ContourPoint2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    public sealed class ContourSlice
    {
        public ContourSlice(double z, IReadOnlyList<IReadOnlyList<ContourPoint2D>> contours)
        {
            Z = z;
            Contours = contours ?? throw new ArgumentNullException(nameof(contours));
        }

        public double Z { get; }
        public IReadOnlyList<IReadOnlyList<ContourPoint2D>> Contours { get; }
    }

    public static class ContourHausdorff
    {
        public static double CalculateSymmetric(
            IReadOnlyList<ContourSlice> first,
            IReadOnlyList<ContourSlice> second,
            double maximumSampleSpacingMm)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            if (maximumSampleSpacingMm <= 0 || double.IsNaN(maximumSampleSpacingMm))
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSampleSpacingMm));
            }

            var firstSegments = BuildSlices(first);
            var secondSegments = BuildSlices(second);

            if (firstSegments.Count == 0 && secondSegments.Count == 0)
            {
                return 0.0;
            }

            if (firstSegments.Count == 0 || secondSegments.Count == 0)
            {
                return double.PositiveInfinity;
            }

            double firstToSecond = CalculateDirected(firstSegments, secondSegments, maximumSampleSpacingMm);
            double secondToFirst = CalculateDirected(secondSegments, firstSegments, maximumSampleSpacingMm);
            return Math.Max(firstToSecond, secondToFirst);
        }

        private static List<SliceSegments> BuildSlices(IReadOnlyList<ContourSlice> slices)
        {
            var result = new List<SliceSegments>();

            foreach (ContourSlice slice in slices)
            {
                var segments = new List<Segment2D>();
                foreach (IReadOnlyList<ContourPoint2D> contour in slice.Contours)
                {
                    if (contour == null || contour.Count < 2)
                    {
                        continue;
                    }

                    for (int i = 0; i < contour.Count; i++)
                    {
                        ContourPoint2D start = contour[i];
                        ContourPoint2D end = contour[(i + 1) % contour.Count];
                        segments.Add(new Segment2D(start, end));
                    }
                }

                if (segments.Count > 0)
                {
                    result.Add(new SliceSegments(slice.Z, segments));
                }
            }

            return result.OrderBy(slice => slice.Z).ToList();
        }

        private static double CalculateDirected(
            IReadOnlyList<SliceSegments> source,
            IReadOnlyList<SliceSegments> target,
            double maximumSampleSpacingMm)
        {
            double maximumDistanceSquared = 0.0;

            foreach (SliceSegments sourceSlice in source)
            {
                foreach (Segment2D sourceSegment in sourceSlice.Segments)
                {
                    double length = sourceSegment.Length;
                    int sampleCount = Math.Max(1, (int)Math.Ceiling(length / maximumSampleSpacingMm));

                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        double fraction = (double)sampleIndex / sampleCount;
                        ContourPoint2D point = sourceSegment.Interpolate(fraction);
                        double distanceSquared = FindNearestDistanceSquared(point, sourceSlice.Z, target);
                        maximumDistanceSquared = Math.Max(maximumDistanceSquared, distanceSquared);
                    }
                }
            }

            return Math.Sqrt(maximumDistanceSquared);
        }

        private static double FindNearestDistanceSquared(
            ContourPoint2D point,
            double z,
            IReadOnlyList<SliceSegments> target)
        {
            double best = double.PositiveInfinity;
            int right = FindFirstSliceAtOrAbove(target, z);
            int left = right - 1;

            while (left >= 0 || right < target.Count)
            {
                bool useLeft = right >= target.Count ||
                    (left >= 0 &&
                     Math.Abs(z - target[left].Z) <= Math.Abs(z - target[right].Z));
                SliceSegments targetSlice = useLeft ? target[left--] : target[right++];
                double zDifference = z - targetSlice.Z;
                double zDistanceSquared = zDifference * zDifference;
                if (zDistanceSquared >= best)
                {
                    break;
                }

                foreach (Segment2D segment in targetSlice.Segments)
                {
                    double distanceSquared = zDistanceSquared + segment.DistanceSquaredTo(point);
                    if (distanceSquared < best)
                    {
                        best = distanceSquared;
                    }
                }
            }

            return best;
        }

        private static int FindFirstSliceAtOrAbove(IReadOnlyList<SliceSegments> slices, double z)
        {
            int low = 0;
            int high = slices.Count;

            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (slices[middle].Z < z)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private sealed class SliceSegments
        {
            public SliceSegments(double z, IReadOnlyList<Segment2D> segments)
            {
                Z = z;
                Segments = segments;
            }

            public double Z { get; }
            public IReadOnlyList<Segment2D> Segments { get; }
        }

        private struct Segment2D
        {
            public Segment2D(ContourPoint2D start, ContourPoint2D end)
            {
                Start = start;
                End = end;
            }

            public ContourPoint2D Start { get; }
            public ContourPoint2D End { get; }

            public double Length
            {
                get
                {
                    double x = End.X - Start.X;
                    double y = End.Y - Start.Y;
                    return Math.Sqrt(x * x + y * y);
                }
            }

            public ContourPoint2D Interpolate(double fraction)
            {
                return new ContourPoint2D(
                    Start.X + (End.X - Start.X) * fraction,
                    Start.Y + (End.Y - Start.Y) * fraction);
            }

            public double DistanceSquaredTo(ContourPoint2D point)
            {
                double segmentX = End.X - Start.X;
                double segmentY = End.Y - Start.Y;
                double lengthSquared = segmentX * segmentX + segmentY * segmentY;

                if (lengthSquared == 0.0)
                {
                    return DistanceSquared(point, Start);
                }

                double projection =
                    ((point.X - Start.X) * segmentX + (point.Y - Start.Y) * segmentY) /
                    lengthSquared;
                projection = Math.Max(0.0, Math.Min(1.0, projection));

                var nearest = new ContourPoint2D(
                    Start.X + projection * segmentX,
                    Start.Y + projection * segmentY);
                return DistanceSquared(point, nearest);
            }

            private static double DistanceSquared(ContourPoint2D first, ContourPoint2D second)
            {
                double x = first.X - second.X;
                double y = first.Y - second.Y;
                return x * x + y * y;
            }
        }
    }
}
