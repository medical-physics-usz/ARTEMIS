using System;
using System.Linq;
using USZ_ARTEMIS.Core.QA;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS.QA
{
    internal static class MetricsCalc
    {
        public static double GetMeanMlcOpening(Beam beam)
        {
            string machineId = beam.TreatmentUnit?.Id;
            if (string.Equals(machineId, "Edge2018", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(machineId, "TrueBeam3232", StringComparison.OrdinalIgnoreCase))
            {
                return MeanMlcOpening.CalculateSingleLayer(
                    beam.ControlPoints.Select(controlPoint => controlPoint.LeafPositions));
            }

            int openPairs = 0;
            double totalOpening = 0;
            foreach (ControlPoint controlPoint in beam.ControlPoints)
            {
                float[,] positions = controlPoint.LeafPositions;
                // Preserve the established leaf range and gap rule for these machines.
                for (int leaf = 1; leaf < positions.GetLength(1); leaf++)
                {
                    float gap = Math.Abs(positions[0, leaf] - positions[1, leaf]);
                    if (gap > 0.01)
                    {
                        totalOpening += gap;
                        openPairs++;
                    }
                }
            }

            return totalOpening / openPairs;
        }
    }
}
