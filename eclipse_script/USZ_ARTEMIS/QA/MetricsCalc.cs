using System;
using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS.QA
{
    internal class MetricsCalc
    {




        // Calculate the mean MLC opening
        public static List<double> MMO(IEnumerable<Beam> Beams)
        {
            var retList = new List<Double>();

            // Convert the beams to a list and then keep only the therapy fields (exclude setup fields)
            List<Beam> inBeams = Beams.ToList();
            List<Beam> therapyBeams = new List<Beam>();
            foreach (Beam beam in inBeams)
            {
                if (!beam.IsSetupField)
                {
                    therapyBeams.Add(beam);
                }
            }
            int NTherapyBeams = therapyBeams.Count();

            // MMO for each beam
            List<double> MmoTherapyBeams = new List<double>();
            for (int iBeam = 0; iBeam < NTherapyBeams; iBeam++)
            {
                MmoTherapyBeams.Add(0);
            }


            // Go over the control points to calculate the mean MLC opening
            // Loop over the beams
            for (int iBeam = 0; iBeam < NTherapyBeams; iBeam++)
            {
                // Get the control points collection
                ControlPointCollection temp_ControlPoints = null;
                temp_ControlPoints = therapyBeams[iBeam].ControlPoints;

                int NLeafsPairsOpen = 0;
                double LeafPairsOpenings = 0;

                // Loop over the control points
                for (int iControlPoint = 0; iControlPoint < temp_ControlPoints.Count; iControlPoint++)
                {
                    // Get the control point
                    ControlPoint temp_ControlPoint = temp_ControlPoints[iControlPoint];
                    int temp_NLeafs = temp_ControlPoint.LeafPositions.GetLength(1);

                    // Perform calculations
                    for (int iLeaf = 1; iLeaf < temp_NLeafs; iLeaf++)
                    {
                        if (Math.Abs(temp_ControlPoint.LeafPositions[0, iLeaf] - temp_ControlPoint.LeafPositions[1, iLeaf]) > 0.01) // exclude leaf pairs that are closed
                        {
                            LeafPairsOpenings = LeafPairsOpenings + Math.Abs(temp_ControlPoint.LeafPositions[0, iLeaf] - temp_ControlPoint.LeafPositions[1, iLeaf]); // sum up the openings
                            NLeafsPairsOpen++; // keep track of how many openings we have
                        }
                    }

                }


                // Average the leaf openings for this beam
                MmoTherapyBeams[iBeam] = LeafPairsOpenings / NLeafsPairsOpen;

            }

            // Calculate the total mean MLC opening scaling on MUs delivered per beam (inverse beacuse high MU high risk, low opening high risk)
            double MMO_Total = 0;
            double MU_Total = 0;
            for (int iBeam = 0; iBeam < NTherapyBeams; iBeam++)
            {
                MMO_Total = MMO_Total + MmoTherapyBeams[iBeam] * (1 / therapyBeams[iBeam].Meterset.Value);
                MU_Total = MU_Total + therapyBeams[iBeam].Meterset.Value;
            }

            MMO_Total = MMO_Total * MU_Total; // normalize beams per total MUs after weighting per MUs delivered per beam


            // Prepare the list to be returned
            // Pos 0 = number of beams
            retList.Add(NTherapyBeams);

            // Pos 1 ... n = metric values for the single beams
            for (int iBeam = 0; iBeam < NTherapyBeams; iBeam++)
            {
                retList.Add(MmoTherapyBeams[iBeam]);
            }

            // Pos n+1 = average of the metric over all the beams
            retList.Add(MMO_Total);

            return retList;


        }



    }
}
