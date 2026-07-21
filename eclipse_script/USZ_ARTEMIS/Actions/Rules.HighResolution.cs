using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Forms;
using EsapiImage = VMS.TPS.Common.Model.API.Image;
using Point = System.Windows.Point;
using WpfGeometry = System.Windows.Media.Geometry;
using USZ_ARTEMIS.Core.Geometry;
using USZ_ARTEMIS.Core.Rules;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.Actions
{
    partial class Rules
    {
        private const double HausdorffSampleSpacingMm = 0.5;

        private sealed class StructureContourSnapshot
        {
            public StructureContourSnapshot(IDictionary<int, ContourSlice> slices)
            {
                Slices = slices;
            }

            public IDictionary<int, ContourSlice> Slices { get; }
        }

        private sealed class ConvertedStructureMetrics
        {
            public ConvertedStructureMetrics(string structureId, double diceSorensen, double hausdorffMm)
            {
                StructureId = structureId;
                DiceSorensen = diceSorensen;
                HausdorffMm = hausdorffMm;
            }

            public string StructureId { get; }
            public double DiceSorensen { get; }
            public double HausdorffMm { get; }
        }

        private static bool PrepareRuleStructuresForHighResolution(
            PlanSetup targetPlan,
            ICollection<Structure> structuresScheduledForDeletion)
        {
            StructureSet structureSet = targetPlan.StructureSet;
            EsapiImage image = structureSet.Image;
            var candidates = structureSet.Structures
                .Where(structure =>
                    RuleStructureResolutionPolicy.RequiresHighResolution(structure.Id) &&
                    !structuresScheduledForDeletion.Contains(structure))
                .OrderBy(structure => structure.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var alreadyHighResolution = new List<string>();
            var skippedApproved = new List<string>();
            var failures = new List<string>();
            var snapshots = new Dictionary<Structure, StructureContourSnapshot>();
            var toConvert = new List<Structure>();
            var converted = new List<ConvertedStructureMetrics>();

            foreach (Structure structure in candidates)
            {
                if (structure.IsHighResolution)
                {
                    alreadyHighResolution.Add(structure.Id);
                    continue;
                }

                if (structure.IsApproved)
                {
                    skippedApproved.Add(structure.Id);
                    continue;
                }

                if (!structure.CanConvertToHighResolution())
                {
                    failures.Add($"{structure.Id}: ESAPI reports that conversion is not possible.");
                    continue;
                }

                try
                {
                    snapshots.Add(structure, CaptureContours(structure, image));
                    toConvert.Add(structure);
                }
                catch (Exception ex)
                {
                    failures.Add($"{structure.Id}: could not capture contours before conversion ({ex.Message}).");
                }
            }

            if (failures.Count > 0)
            {
                ShowHighResolutionConversionReport(
                    alreadyHighResolution,
                    skippedApproved,
                    converted,
                    failures);
                return false;
            }

            foreach (Structure structure in toConvert)
            {
                try
                {
                    structure.ConvertToHighResolution();
                }
                catch (Exception ex)
                {
                    failures.Add($"{structure.Id}: conversion failed ({ex.Message}).");
                    break;
                }

                if (!structure.IsHighResolution)
                {
                    failures.Add(
                        $"{structure.Id}: conversion returned without error, but the structure is not high resolution.");
                    break;
                }

                try
                {
                    StructureContourSnapshot after = CaptureContours(structure, image);
                    converted.Add(CompareContours(structure.Id, snapshots[structure], after));
                }
                catch (Exception ex)
                {
                    failures.Add(
                        $"{structure.Id}: conversion completed, but contour comparison failed ({ex.Message}).");
                    break;
                }
            }

            if (converted.Count > 0 || skippedApproved.Count > 0 || failures.Count > 0)
            {
                ShowHighResolutionConversionReport(
                    alreadyHighResolution,
                    skippedApproved,
                    converted,
                    failures);
            }

            return failures.Count == 0;
        }

        private static StructureContourSnapshot CaptureContours(Structure structure, EsapiImage image)
        {
            var slices = new Dictionary<int, ContourSlice>();

            for (int plane = 0; plane < image.ZSize; plane++)
            {
                VVector[][] planeContours = structure.GetContoursOnImagePlane(plane);
                if (planeContours == null || planeContours.Length == 0)
                {
                    continue;
                }

                var contours = new List<IReadOnlyList<ContourPoint2D>>();
                double? slicePosition = null;

                foreach (VVector[] contour in planeContours)
                {
                    if (contour == null || contour.Length < 2)
                    {
                        continue;
                    }

                    var points = new List<ContourPoint2D>(contour.Length);
                    foreach (VVector point in contour)
                    {
                        VVector relative = point - image.Origin;
                        points.Add(new ContourPoint2D(
                            Dot(relative, image.XDirection),
                            Dot(relative, image.YDirection)));

                        if (!slicePosition.HasValue)
                        {
                            slicePosition = Dot(relative, image.ZDirection);
                        }
                    }

                    contours.Add(points);
                }

                if (contours.Count > 0)
                {
                    slices.Add(
                        plane,
                        new ContourSlice(slicePosition ?? plane * image.ZRes, contours));
                }
            }

            return new StructureContourSnapshot(slices);
        }

        private static ConvertedStructureMetrics CompareContours(
            string structureId,
            StructureContourSnapshot before,
            StructureContourSnapshot after)
        {
            var allPlanes = before.Slices.Keys
                .Union(after.Slices.Keys)
                .OrderBy(plane => plane)
                .ToList();

            double beforeArea = 0.0;
            double afterArea = 0.0;
            double intersectionArea = 0.0;

            foreach (int plane in allPlanes)
            {
                PathGeometry beforeGeometry = CreateSliceGeometry(GetSlice(before, plane));
                PathGeometry afterGeometry = CreateSliceGeometry(GetSlice(after, plane));

                beforeArea += beforeGeometry.GetArea();
                afterArea += afterGeometry.GetArea();
                intersectionArea += WpfGeometry.Combine(
                    beforeGeometry,
                    afterGeometry,
                    GeometryCombineMode.Intersect,
                    null).GetArea();
            }

            double diceSorensen = OverlapMetrics.CalculateDiceSorensen(
                beforeArea,
                afterArea,
                intersectionArea);

            double hausdorffMm = ContourHausdorff.CalculateSymmetric(
                before.Slices.Values.OrderBy(slice => slice.Z).ToList(),
                after.Slices.Values.OrderBy(slice => slice.Z).ToList(),
                HausdorffSampleSpacingMm);

            if (double.IsInfinity(hausdorffMm) || double.IsNaN(hausdorffMm))
            {
                throw new InvalidOperationException("Hausdorff distance is undefined because only one contour is empty.");
            }

            return new ConvertedStructureMetrics(structureId, diceSorensen, hausdorffMm);
        }

        private static ContourSlice GetSlice(StructureContourSnapshot snapshot, int plane)
        {
            ContourSlice slice;
            return snapshot.Slices.TryGetValue(plane, out slice) ? slice : null;
        }

        private static PathGeometry CreateSliceGeometry(ContourSlice slice)
        {
            var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
            if (slice == null)
            {
                return geometry;
            }

            foreach (IReadOnlyList<ContourPoint2D> contour in slice.Contours)
            {
                if (contour == null || contour.Count < 3)
                {
                    continue;
                }

                var figure = new PathFigure
                {
                    StartPoint = new Point(contour[0].X, contour[0].Y),
                    IsClosed = true,
                    IsFilled = true
                };

                var remainingPoints = new PointCollection();
                for (int i = 1; i < contour.Count; i++)
                {
                    remainingPoints.Add(new Point(contour[i].X, contour[i].Y));
                }

                figure.Segments.Add(new PolyLineSegment(remainingPoints, true));
                geometry.Figures.Add(figure);
            }

            return geometry;
        }

        private static double Dot(VVector first, VVector second)
        {
            return first.x * second.x + first.y * second.y + first.z * second.z;
        }

        private static void ShowHighResolutionConversionReport(
            IReadOnlyCollection<string> alreadyHighResolution,
            IReadOnlyCollection<string> skippedApproved,
            IReadOnlyCollection<ConvertedStructureMetrics> converted,
            IReadOnlyCollection<string> failures)
        {
            var report = new StringBuilder();
            report.AppendLine("High-resolution check before applying rules");
            report.AppendLine();

            if (converted.Count > 0)
            {
                report.AppendLine("Converted structures:");
                foreach (ConvertedStructureMetrics result in converted)
                {
                    report.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "- {0}: Dice-Sorensen = {1:F4}; Hausdorff = {2:F2} mm",
                        result.StructureId,
                        result.DiceSorensen,
                        result.HausdorffMm));
                }

                report.AppendLine(
                    "Dice uses slice-wise contour overlap; Hausdorff is the symmetric 3D contour-boundary distance " +
                    $"sampled at no more than {HausdorffSampleSpacingMm:F1} mm spacing.");
                report.AppendLine();
            }

            if (skippedApproved.Count > 0)
            {
                report.AppendLine("Skipped because approved:");
                foreach (string structureId in skippedApproved)
                {
                    report.AppendLine("- " + structureId);
                }

                report.AppendLine();
            }

            report.AppendLine($"Already high resolution: {alreadyHighResolution.Count}");

            if (failures.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Failures:");
                foreach (string failure in failures)
                {
                    report.AppendLine("- " + failure);
                }

                report.AppendLine();
                report.AppendLine("Rules were not applied because the high-resolution preparation did not complete.");
                report.AppendLine(
                    "Any conversion that completed before the failure has already modified the current structure set and was not rolled back.");
            }

            MessageBox.Show(
                report.ToString(),
                "High-resolution structure report",
                MessageBoxButtons.OK,
                failures.Count > 0 ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
    }
}
