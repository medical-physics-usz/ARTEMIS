using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.Tools
{
    class ExtractData
    {

        public static string GetPatientID(ScriptContext context)
        {
            string PatientID = context.Patient.Id.ToString();
            return PatientID;

        }

        public static string GetPatientName(ScriptContext context)
        {
            Patient Patient = context.Patient;
            string PatientName = context.Patient.LastName + ", " + context.Patient.FirstName;
            return PatientName;
        }

        public static string GetOncologist(ScriptContext context)
        {
            string Oncologist = context.Patient.PrimaryOncologistId.ToString();
            return Oncologist;

        }
        public static string GetPhysicist(ScriptContext context)
        {
            string Physicist = context.CurrentUser.Id.ToString();
            return Physicist;

        }
        public static string GetCourse(ScriptContext context, PlanSetup selectedPlan)
        {
            string Course = selectedPlan.Course.Id.ToString();
            return Course;

        }

        public static string GetPlan(ScriptContext context, PlanSetup selectedPlan)
        {
            string Plan = selectedPlan.Id.ToString();
            return Plan;

        }

        public static string GetStructureSet(ScriptContext context, PlanSetup selectedPlan)
        {
            string StructureSet = selectedPlan.StructureSet.ToString();
            return StructureSet;

        }

        public static string GetImageDevice(ScriptContext context, PlanSetup selectedPlan)
        {
            string ImageDevice = selectedPlan.StructureSet.Image.Series.ImagingDeviceModel.ToString();
            return ImageDevice;

        }

        public static string GetEnergy(ScriptContext context, PlanSetup selectedPlan)
        {
            List<Beam> inBeams = selectedPlan.Beams.ToList();
            List<Beam> therapyBeams = new List<Beam>();
            foreach (Beam beam in inBeams) { if (!beam.IsSetupField) { therapyBeams.Add(beam); } } // inline list as done in every function in MetricsCalc

            string Energy = therapyBeams[0].EnergyModeDisplayName;
            return Energy;

        }

        public static string GetTreatmentMachine(ScriptContext context, PlanSetup selectedPlan)
        {
            List<Beam> inBeams = selectedPlan.Beams.ToList();
            List<Beam> therapyBeams = new List<Beam>();
            foreach (Beam beam in inBeams) { if (!beam.IsSetupField) { therapyBeams.Add(beam); } } // inline list as done in every function in MetricsCalc

            string TreatmentMachine = therapyBeams[0].TreatmentUnit.Id.ToString();
            return TreatmentMachine;
        }

        public static string GetNumberOfFractions(ScriptContext context, PlanSetup selectedPlan)
        {
            string NumberOfFractions = selectedPlan.NumberOfFractions.ToString();
            return NumberOfFractions;

        }

        public static double GetPrescribedDose(ScriptContext context, PlanSetup selectedPlan)
        {
            double PrescribedDose = selectedPlan.TotalDose.Dose;
            return PrescribedDose;

        }

        public static string GetDosePerFraction(ScriptContext context, PlanSetup selectedPlan)
        {
            string DosePerFraction = selectedPlan.DosePerFraction.Dose.ToString();
            return DosePerFraction;

        }

        public static string GetNormalizationMode(ScriptContext context, PlanSetup selectedPlan)
        {
            string NormalizationMode = selectedPlan.PlanNormalizationMethod.ToString();
            return NormalizationMode;

        }
        public static string GetNormalizationValue(ScriptContext context, PlanSetup selectedPlan)
        {
            string NormalizationValue = selectedPlan.PlanNormalizationValue.ToString("0.000");
            return NormalizationValue;

        }

        public static double GetTotalMu(PlanSetup selectedPlan)
        {
            // loop over the treatment beams and get the MU
            double outMU = 0;

            foreach (Beam tempBeam in selectedPlan.Beams)
            {
                if (!tempBeam.IsSetupField)
                {
                    outMU = outMU + tempBeam.Meterset.Value;
                }
            }
            return outMU;

        }

        public static Structure FindPTV(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = selectedPlan.StructureSet.Structures.First(s => s.Id == selectedPlan.TargetVolumeID);
            string PTVID = PTV.Name.ToString();
            return PTV;
        }
        public static double GetPTVDmean(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double ptvDmean_percent_orgPlan = selectedPlan.GetDVHCumulativeData(PTV, DoseValuePresentation.Relative, VolumePresentation.Relative, 0.1).MeanDose.Dose; //meandose [%]
                return ptvDmean_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }

        public static double GetPTVDmax(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double ptvDmax_percent_orgPlan = selectedPlan.GetDVHCumulativeData(PTV, DoseValuePresentation.Relative, VolumePresentation.AbsoluteCm3, 0.1).MaxDose.Dose; ; //meandose [%]
                return ptvDmax_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }

        public static double GetPTVD03(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double PTV_D03cc_percent_orgPlan = selectedPlan.GetDoseAtVolume(PTV, 0.03, VolumePresentation.AbsoluteCm3, DoseValuePresentation.Relative).Dose;
                return PTV_D03cc_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }

        public static double GetPTVD2(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double PTV_D2_percent_orgPlan = selectedPlan.GetDoseAtVolume(PTV, 2, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose;
                return PTV_D2_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }

        public static double GetPTVD95(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double PTV_D95_percent_orgPlan = selectedPlan.GetDoseAtVolume(PTV, 95, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose;
                return PTV_D95_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }

        public static double GetPTVD98(ScriptContext context, PlanSetup selectedPlan)
        {
            Structure PTV = FindPTV(context, selectedPlan);
            if (PTV != null & selectedPlan.IsDoseValid)
            {
                double PTV_D98_percent_orgPlan = selectedPlan.GetDoseAtVolume(PTV, 98, VolumePresentation.Relative, DoseValuePresentation.Relative).Dose;
                return PTV_D98_percent_orgPlan;
            }
            else if (PTV != null)
            {
                return 0.0;//MessageBox.Show("PTV not found in the original plan, check if there is a structure assigned as Target Structure and if there is dose calculated in original plan");
            }
            return 0.0;
        }


    }
}
