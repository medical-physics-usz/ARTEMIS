using System.Text.RegularExpressions;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS.QA
{
    class Tools
    {

        public static string CompareAllTargetVolumes(PlanSetup adaptedPlan, PlanSetup originalPlan)
        {
            // compare all targets 
            string outString = "";

            // loop over all adapted structures 
            foreach (Structure tempStrAdapted in adaptedPlan.StructureSet.Structures)
            {
                double tempVolumeAdaptedStructure = 0;
                double tempVolumeOriginaStructure = 0;

                // select only the targets
                if (tempStrAdapted.DicomType.EndsWith("TV"))
                {
                    // exclude physics structures
                    if (!tempStrAdapted.Id.Contains("PH"))
                    {
                        if (!tempStrAdapted.Id.Contains("Ph"))
                        {
                            // target found, extract volume
                            tempVolumeAdaptedStructure = tempStrAdapted.Volume;



                            // search for volume with same ID in the original plan
                            foreach (Structure tempStrOrigina in originalPlan.StructureSet.Structures)
                            {
                                if (tempStrOrigina.Id.Equals(tempStrAdapted.Id))
                                {
                                    // found, extract volume
                                    tempVolumeOriginaStructure = tempStrOrigina.Volume;

                                }

                            }

                            // calculate change and write it to string
                            if (tempVolumeAdaptedStructure == 0 || tempVolumeOriginaStructure == 0)
                            {
                                outString = outString + tempStrAdapted.Id + " not matched or zero volume\n";
                            }
                            else
                            {
                                double volumeChangePerc = 100 * (tempVolumeAdaptedStructure - tempVolumeOriginaStructure) / tempVolumeOriginaStructure;
                                double volumeChangeAbs = tempVolumeAdaptedStructure - tempVolumeOriginaStructure;
                                outString = outString + tempStrAdapted.Id + " change = " + volumeChangePerc.ToString("F2") + " % (" + volumeChangeAbs.ToString("F2") + " cc)\n";
                            }


                        }
                    }


                }



            }

            return outString;

        }

        public static PlanSetup GetOriginalPlan(PlanSetup adaptedPlan)
        {
            string adaptedID = adaptedPlan.Id;

            if (string.IsNullOrEmpty(adaptedID) || adaptedID.Length < 2)
            {
                System.Windows.Forms.MessageBox.Show("Adapted plan ID is invalid: " + adaptedID);
                return null;
            }

            // Remove the fraction/adaptation suffix: A, B, C, ...
            string baseAdaptedID = adaptedID.Substring(0, adaptedID.Length - 1);

            PlanSetup outPlan = null;

            // First try the simple case:
            // SBabc01-02_1aA -> SBabc01-02_1a
            foreach (PlanSetup tempPlan in adaptedPlan.Course.PlanSetups)
            {
                if (tempPlan.Id.Equals(baseAdaptedID))
                {
                    outPlan = tempPlan;
                    break;
                }
            }

            // If not found, handle shortened adapted IDs:
            // SB01-02_1aA should match SBxxx01-02_1a
            if (outPlan == null && baseAdaptedID.Length >= 2)
            {
                string pattern =
                    "^" +
                    Regex.Escape(baseAdaptedID.Substring(0, 2)) +
                    "[a-z]{3}" +
                    Regex.Escape(baseAdaptedID.Substring(2)) +
                    "$";

                var rxOriginalFromShortened = new Regex(pattern);

                foreach (PlanSetup tempPlan in adaptedPlan.Course.PlanSetups)
                {
                    if (rxOriginalFromShortened.IsMatch(tempPlan.Id))
                    {
                        outPlan = tempPlan;
                        break;
                    }
                }
            }

            if (outPlan == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Original plan not found!\n" +
                    "Adapted PlanID = " + adaptedID + "\n" +
                    "Base adapted PlanID = " + baseAdaptedID);
            }

            return outPlan;
        }

    }
}
