using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using USZ_ARTEMIS.Core.QA;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS.QA
{
    class TargetVolumeComparison
    {
        public string Text { get; set; }
        public QaStatus Status { get; set; }
    }

    class Tools
    {

        public static string CompareAllTargetVolumes(PlanSetup adaptedPlan, PlanSetup originalPlan)
        {
            StringBuilder output = new StringBuilder();
            foreach (TargetVolumeComparison comparison in GetAllTargetVolumeComparisons(adaptedPlan, originalPlan))
            {
                output.Append(comparison.Text);
                output.Append('\n');
            }

            return output.ToString();
        }

        public static List<TargetVolumeComparison> GetAllTargetVolumeComparisons(
            PlanSetup adaptedPlan,
            PlanSetup originalPlan)
        {
            // compare all targets 
            List<TargetVolumeComparison> comparisons = new List<TargetVolumeComparison>();

            // loop over all adapted structures 
            foreach (Structure tempStrAdapted in adaptedPlan.StructureSet.Structures)
            {
                double tempVolumeAdaptedStructure = 0;
                double? tempVolumeOriginaStructure = null;

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
                            QaPercentageClassification volumeComparison =
                                QaStatusClassifier.ForTargetVolumes(
                                    tempVolumeAdaptedStructure,
                                    tempVolumeOriginaStructure,
                                    2);
                            if (!volumeComparison.DisplayedPercentage.HasValue)
                            {
                                comparisons.Add(new TargetVolumeComparison
                                {
                                    Text = tempStrAdapted.Id + " not matched or zero volume",
                                    Status = volumeComparison.Status
                                });
                            }
                            else
                            {
                                double volumeChangeAbs =
                                    tempVolumeAdaptedStructure - tempVolumeOriginaStructure.Value;
                                comparisons.Add(new TargetVolumeComparison
                                {
                                    Text = tempStrAdapted.Id + " change = " + volumeComparison.DisplayedPercentage.Value.ToString("F2") + " % (" + volumeChangeAbs.ToString("F2") + " cc)",
                                    Status = volumeComparison.Status
                                });
                            }


                        }
                    }


                }



            }

            return comparisons;

        }

        public static PlanSetup GetOriginalPlan(PlanSetup adaptedPlan)
        {
            string adaptedID = adaptedPlan.Id;

            if (string.IsNullOrEmpty(adaptedID) || adaptedID.Length < 2)
            {
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

            return outPlan;
        }

    }
}
