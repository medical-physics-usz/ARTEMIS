using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.QA;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private void BtnComparePlans_Click(object sender, RoutedEventArgs e)
        {
            PlanSetup selectedPlan = GetSelectedPlan();
            PlanSetup originalPlan = QA.Tools.GetOriginalPlan(selectedPlan);

            string outputQA = string.Empty;

            if (selectedPlan.DosePerFraction.ValueAsString.Equals(originalPlan.DosePerFraction.ValueAsString))
            {
                outputQA += "Dose/fx is identical\n";
            }
            else
            {
                outputQA += "ERROR! - Dose/fx is different!\n";
            }
            outputQA += "________________________________\n\n";

            if (selectedPlan.PrimaryReferencePoint.Id.Equals(originalPlan.PrimaryReferencePoint.Id))
            {
                outputQA += "Ref. point is identical\n";
            }
            else
            {
                outputQA += "ERROR! - Ref. point is different!\n";
            }
            outputQA += "________________________________\n\n";

            List<Beam> inBeamsSelected = selectedPlan.Beams.ToList();
            List<Beam> therapyBeamsSelected = new List<Beam>();
            foreach (Beam beam in inBeamsSelected)
            {
                if (!beam.IsSetupField)
                {
                    therapyBeamsSelected.Add(beam);
                }
            }

            int numberOfTherapyBeamsSelected = therapyBeamsSelected.Count;

            List<double> adaptedMmos = new List<double>();
            int counterSelected = 0;
            foreach (double mmo in MetricsCalc.MMO(selectedPlan.Beams))
            {
                if (counterSelected != 0 && counterSelected <= numberOfTherapyBeamsSelected)
                {
                    adaptedMmos.Add(mmo);
                }
                counterSelected++;
            }

            List<double> originalMmos = new List<double>();
            int counterOriginal = 0;
            foreach (double mmo in MetricsCalc.MMO(originalPlan.Beams))
            {
                if (counterOriginal != 0 && counterOriginal <= numberOfTherapyBeamsSelected)
                {
                    originalMmos.Add(mmo);
                }
                counterOriginal++;
            }

            int numberOfMmoPairs = Math.Min(adaptedMmos.Count, originalMmos.Count);
            for (int i = 0; i < numberOfMmoPairs; i++)
            {
                double adaptedMmo = adaptedMmos[i];
                double originalMmo = originalMmos[i];

                if (Math.Abs(originalMmo) < 1e-9)
                {
                    outputQA += "MMO = " + adaptedMmo.ToString("F1") +
                                " mm (reference " + originalMmo.ToString("F1") +
                                " mm; n/a)\n";
                }
                else
                {
                    double changePerc = 100.0 * (adaptedMmo - originalMmo) / originalMmo;
                    string sign = changePerc >= 0 ? "+" : "";

                    outputQA += "MMO = " + adaptedMmo.ToString("F1") +
                                " mm (reference " + originalMmo.ToString("F1") +
                                " mm; " + sign + changePerc.ToString("F1") + "%)\n";
                }
            }
            outputQA += "________________________________\n\n";

            outputQA += QA.Tools.CompareAllTargetVolumes(selectedPlan, originalPlan);
            outputQA += "________________________________\n\n";

            double totalMuSelected = 0;
            double totalMuOriginal = 0;
            for (int iBeam = 0; iBeam < numberOfTherapyBeamsSelected; iBeam++)
            {
                totalMuSelected += therapyBeamsSelected[iBeam].Meterset.Value;
            }

            List<Beam> inBeamsOriginal = originalPlan.Beams.ToList();
            List<Beam> therapyBeamsOriginal = new List<Beam>();
            foreach (Beam beam in inBeamsOriginal)
            {
                if (!beam.IsSetupField)
                {
                    therapyBeamsOriginal.Add(beam);
                }
            }

            int numberOfTherapyBeamsOriginal = therapyBeamsOriginal.Count;
            for (int iBeam = 0; iBeam < numberOfTherapyBeamsOriginal; iBeam++)
            {
                totalMuOriginal += therapyBeamsOriginal[iBeam].Meterset.Value;
            }

            double muChangePerc = 100 * (totalMuSelected - totalMuOriginal) / totalMuOriginal;
            outputQA += "MUs Change = " + muChangePerc.ToString("F1") + " % \n";
            outputQA += "________________________________\n\n";

            txtOutputQA.Text = outputQA;
        }

        private void BtnPerformQA_Click(object sender, RoutedEventArgs e)
        {
            Course qaCourse = Tools.Actions.CreateQaCourse(context, GetSelectedCourse());
            qaCourse.CopyPlanSetup(GetSelectedPlan());
            StructureSet qaStructureSetWater = Tools.Actions.CreateQaStructureSet(context, GetSelectedPlan(), "QW");
            PlanSetup qaPlanWater = Tools.Actions.CreateQaPlan(context, GetSelectedCourse(), GetSelectedPlan(), "QW");

            Tools.Actions.OverrideBody(
                context,
                GetSelectedCourse(),
                GetSelectedPlan(),
                "QW",
                qaStructureSetWater.Structures.FirstOrDefault(s => s.DicomType == "EXTERNAL"));

            Tools.Actions.RecalculatePlan(context, GetSelectedPlan(), qaPlanWater);
            Tools.Actions.RenameCopyPlan(context, GetSelectedPlan(), GetSelectedCourse());
            SyntheticCT.CreatePDF(context, GetSelectedPlan(), qaPlanWater);
        }

        private void txtOutputQA_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void BtnSendToSciMoCa_Click(object sender, RoutedEventArgs e)
        {
            string patientId = context.Patient.Id;
            string planSetupId = context.PlanSetup.Id;
            string planSetupUID = context.PlanSetup.UID;
            string planDoseUID = context.PlanSetup.Dose.UID;
            string username = Environment.UserName;

            Scimoca.SendToScimoca(patientId, planSetupId, planSetupUID, planDoseUID, username);
        }

        private void BtnPerformRegCheck_Click(object sender, RoutedEventArgs e)
        {
            string exePath = AppPaths.RegistrationCheckExecutablePath;

            if (!File.Exists(exePath))
            {
                MessageBox.Show($"Executable not found:\n{exePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath)
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start:\n{exePath}\n\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
