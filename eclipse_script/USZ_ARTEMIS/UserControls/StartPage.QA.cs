using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.Core.QA;
using USZ_ARTEMIS.QA;
using USZ_ARTEMIS.Tools;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private const string QaOkPrefix = "\u2713 ";
        private const string QaWarningPrefix = "\u26A0 ";
        private const string QaErrorPrefix = "\u2717 ";

        private void BtnComparePlans_Click(object sender, RoutedEventArgs e)
        {
            txtOutputQA.Document.Blocks.Clear();
            PlanSetup selectedPlan = GetSelectedPlan();
            if (selectedPlan == null)
            {
                MessageBox.Show("Open a plan to compare.", "Compare plans",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PlanSetup originalPlan = QA.Tools.GetOriginalPlan(selectedPlan) ?? SelectBasePlan(selectedPlan);
            if (originalPlan == null)
                return;

            if (selectedPlan.PrimaryReferencePoint == null || originalPlan.PrimaryReferencePoint == null ||
                selectedPlan.StructureSet == null || originalPlan.StructureSet == null)
            {
                MessageBox.Show(
                    $"Both plans ({selectedPlan.Id} and {originalPlan.Id}) need a primary reference point and a structure set.",
                    "Compare plans", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FlowDocument outputQA = new FlowDocument
            {
                PagePadding = new Thickness(4)
            };
            AddQaOutputLine(outputQA, $"Plan {selectedPlan.Id} compared with base plan {originalPlan.Id}");

            bool isDosePerFractionIdentical =
                selectedPlan.DosePerFraction.ValueAsString.Equals(originalPlan.DosePerFraction.ValueAsString);
            if (isDosePerFractionIdentical)
            {
                AddQaStatusLine(
                    outputQA,
                    "Dose/fx is identical",
                    QaStatusClassifier.ForMatch(isDosePerFractionIdentical));
            }
            else
            {
                AddQaStatusLine(
                    outputQA,
                    "Dose/fx is different!",
                    QaStatusClassifier.ForMatch(isDosePerFractionIdentical));
            }
            AddQaOutputSeparator(outputQA);

            bool isReferencePointIdentical =
                selectedPlan.PrimaryReferencePoint.Id.Equals(originalPlan.PrimaryReferencePoint.Id);
            if (isReferencePointIdentical)
            {
                AddQaStatusLine(
                    outputQA,
                    "Ref. point is identical",
                    QaStatusClassifier.ForMatch(isReferencePointIdentical));
            }
            else
            {
                AddQaStatusLine(
                    outputQA,
                    "Ref. point is different!",
                    QaStatusClassifier.ForMatch(isReferencePointIdentical));
            }
            AddQaOutputSeparator(outputQA);

            List<Beam> therapyBeamsSelected = selectedPlan.Beams.Where(beam => !beam.IsSetupField).ToList();
            List<Beam> therapyBeamsOriginal = originalPlan.Beams.Where(beam => !beam.IsSetupField).ToList();

            var beamPairs = BeamPairing.ById(therapyBeamsSelected, therapyBeamsOriginal, beam => beam.Id).ToList();
            AddMmoComparison(outputQA, beamPairs);
            AddQaOutputSeparator(outputQA);

            AddSas10Comparison(outputQA, beamPairs);
            AddQaOutputSeparator(outputQA);

            foreach (TargetVolumeComparison comparison in QA.Tools.GetAllTargetVolumeComparisons(selectedPlan, originalPlan))
            {
                AddQaStatusLine(
                    outputQA,
                    comparison.Text,
                    comparison.Status);
            }
            AddQaOutputSeparator(outputQA);

            double totalMuSelected = 0;
            double totalMuOriginal = 0;
            foreach (Beam beam in therapyBeamsSelected)
            {
                totalMuSelected += beam.Meterset.Value;
            }

            int numberOfTherapyBeamsOriginal = therapyBeamsOriginal.Count;
            for (int iBeam = 0; iBeam < numberOfTherapyBeamsOriginal; iBeam++)
            {
                totalMuOriginal += therapyBeamsOriginal[iBeam].Meterset.Value;
            }

            double muChangePerc = 100 * (totalMuSelected - totalMuOriginal) / totalMuOriginal;
            double displayedMuChangePerc =
                QaStatusClassifier.RoundPercentageForDisplay(muChangePerc, 1);
            AddQaStatusLine(
                outputQA,
                "MUs Change = " + displayedMuChangePerc.ToString("F1") + " %",
                QaStatusClassifier.ForSymmetricChange(displayedMuChangePerc));
            AddQaOutputSeparator(outputQA);

            txtOutputQA.Document = outputQA;
        }

        private static void AddMmoComparison(FlowDocument document, IReadOnlyList<BeamPair<Beam>> beamPairs)
        {
            foreach (BeamPair<Beam> pair in beamPairs)
            {
                string selectedText = DescribeMmo(pair.Selected, out double? selectedMmo);
                string referenceText = DescribeMmo(pair.Reference, out double? referenceMmo);
                double? displayedChange = null;
                string changeText = "n/a";
                if (selectedMmo.HasValue && referenceMmo.HasValue && Math.Abs(referenceMmo.Value) >= 1e-9)
                {
                    double change = 100.0 * (selectedMmo.Value - referenceMmo.Value) / referenceMmo.Value;
                    displayedChange = QaStatusClassifier.RoundPercentageForDisplay(change, 1);
                    string sign = displayedChange.Value >= 0 ? "+" : "";
                    changeText = $"{sign}{displayedChange:F1}%";
                }

                AddQaStatusLine(document,
                    $"MMO {pair.Id} = {selectedText} (reference {referenceText}; {changeText})",
                    QaStatusClassifier.ForMmoChange(displayedChange));
            }
        }

        private static string DescribeMmo(IReadOnlyList<Beam> beamsWithSameId, out double? mmo)
        {
            mmo = null;
            string unavailable = DescribeUnavailableBeam(beamsWithSameId.Count);
            if (unavailable != null)
                return unavailable;

            double opening = MetricsCalc.GetMeanMlcOpening(beamsWithSameId[0]);
            if (double.IsNaN(opening) || double.IsInfinity(opening))
                return "n/a (unavailable MLC opening)";

            mmo = opening;
            return $"{opening:F1} mm";
        }

        private static void AddSas10Comparison(FlowDocument document, IReadOnlyList<BeamPair<Beam>> beamPairs)
        {
            if (beamPairs.Count == 0)
            {
                AddQaStatusLine(document, "SAS10 = n/a (no treatment beams)", QaStatus.Warning);
                return;
            }

            foreach (BeamPair<Beam> pair in beamPairs)
            {
                string selectedText = DescribeSas10(pair.Selected, out double? selectedPercentage);
                string originalText = DescribeSas10(pair.Reference, out double? originalPercentage);
                string line = $"SAS10 {pair.Id} = {selectedText} (reference {originalText}";
                if (selectedPercentage.HasValue && originalPercentage.HasValue)
                {
                    double change = QaStatusClassifier.RoundPercentageForDisplay(
                        selectedPercentage.Value - originalPercentage.Value, 1);
                    AddQaOutputLine(document, $"{line}; {change:+0.0;-0.0;0.0} pp)");
                }
                else
                {
                    AddQaStatusLine(document, line + ")", QaStatus.Warning);
                }
            }
        }

        private static string DescribeUnavailableBeam(int count)
        {
            if (count == 0)
                return "n/a (beam ID absent)";
            if (count > 1)
                return "n/a (duplicate beam ID)";
            return null;
        }

        private static string DescribeSas10(IReadOnlyList<Beam> beamsWithSameId, out double? percentage)
        {
            percentage = null;
            string unavailable = DescribeUnavailableBeam(beamsWithSameId.Count);
            if (unavailable != null)
                return unavailable;

            SmallApertureScoreResult score = SmallApertureScore.Calculate(
                beamsWithSameId[0].ControlPoints.Select(controlPoint => controlPoint.LeafPositions));
            percentage = score.Percentage;
            switch (score.Status)
            {
                case SmallApertureScoreStatus.Available:
                    return $"{percentage:F1}%";
                case SmallApertureScoreStatus.NoMlc:
                    return "n/a (no MLC)";
                case SmallApertureScoreStatus.NoControlPoints:
                    return "n/a (no control points)";
                case SmallApertureScoreStatus.NoOpenLeafPairs:
                    return "n/a (no open MLC leaf pairs)";
                default:
                    return "n/a (invalid MLC leaf positions)";
            }
        }

        private PlanSetup SelectBasePlan(PlanSetup selectedPlan)
        {
            Course course = selectedPlan.Course;
            var candidates = course.PlanSetups
                .Where(plan => !string.Equals(plan.Id, selectedPlan.Id, StringComparison.Ordinal))
                .OrderBy(plan => plan.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidates.Count == 0)
            {
                MessageBox.Show($"Course {course.Id} has no other plans to compare with {selectedPlan.Id}.",
                    "Select base plan", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var owner = ResolveShownOwnerWindow();
            if (owner != null)
                new WindowInteropHelper(owner).EnsureHandle();

            var dialog = new Window
            {
                Title = "Select base plan",
                Owner = owner,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Width = 420,
                Height = 340,
                MinWidth = 340,
                MinHeight = 260
            };
            var prompt = new TextBlock
            {
                Text = $"No original plan was found automatically for {selectedPlan.Id}. " +
                       $"Select a base plan from course {course.Id}.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var plans = new ListBox { ItemsSource = candidates, DisplayMemberPath = nameof(PlanSetup.Id) };
            var compare = new Button { Content = "Compare", MinWidth = 80, IsDefault = true, IsEnabled = false };
            plans.SelectionChanged += (sender, args) => compare.IsEnabled = plans.SelectedItem != null;
            compare.Click += (sender, args) => dialog.DialogResult = true;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttons.Children.Add(compare);
            buttons.Children.Add(new Button
            {
                Content = "Cancel", MinWidth = 80, IsCancel = true, Margin = new Thickness(8, 0, 0, 0)
            });

            var content = new DockPanel { Margin = new Thickness(12) };
            DockPanel.SetDock(prompt, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            content.Children.Add(prompt);
            content.Children.Add(buttons);
            content.Children.Add(plans);
            dialog.Content = content;

            return dialog.ShowDialog() == true ? (PlanSetup)plans.SelectedItem : null;
        }

        private static void AddQaStatusLine(FlowDocument document, string text, QaStatus status)
        {
            switch (status)
            {
                case QaStatus.Acceptable:
                    AddQaOutputLine(document, QaOkPrefix + text, Brushes.Green);
                    break;
                case QaStatus.Warning:
                    AddQaOutputLine(document, QaWarningPrefix + text, Brushes.DarkOrange);
                    break;
                case QaStatus.Error:
                    AddQaOutputLine(document, QaErrorPrefix + text, Brushes.Red);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        private static void AddQaOutputLine(FlowDocument document, string text, Brush foreground = null)
        {
            Run run = new Run(text);
            if (foreground != null)
            {
                run.Foreground = foreground;
            }

            document.Blocks.Add(new Paragraph(run)
            {
                Margin = new Thickness(0)
            });
        }

        private static void AddQaOutputSeparator(FlowDocument document)
        {
            AddQaOutputLine(document, "________________");
            document.Blocks.Add(new Paragraph
            {
                Margin = new Thickness(0),
                FontSize = 6
            });
        }

        private void CbMachine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateQaSendLabel();
        }

        private void UpdateQaSendLabel()
        {
            // SelectionChanged can fire before the QA buttons are created by XAML.
            if (btnSendPlan != null)
                btnSendPlan.Content = DoseCheckLauncher.IsAccelaMachine(GetSelectedMachineId())
                    ? "Send to DoseCHECK"
                    : "Send to SciMoCa";
        }

        private async void BtnPerformQA_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetQaPlan(out PlanSetup plan, out bool useDoseCheck))
                return;

            if (useDoseCheck)
            {
                await RunDoseCheckAsync(plan, waterPsqa: true);
                return;
            }

            PerformEclipseWaterQa();
        }

        private void PerformEclipseWaterQa()
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

        private async void BtnSendPlan_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetQaPlan(out PlanSetup plan, out bool useDoseCheck))
                return;

            if (useDoseCheck)
            {
                await RunDoseCheckAsync(plan, waterPsqa: false);
                return;
            }

            try
            {
                if (!plan.IsDoseValid || string.IsNullOrWhiteSpace(plan.Dose?.UID))
                    throw new InvalidOperationException("Calculate the dose of the open plan before sending it.");

                Scimoca.SendToScimoca(context.Patient.Id, plan.Id, plan.UID, plan.Dose.UID, Environment.UserName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Send to SciMoCa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool TryGetQaPlan(out PlanSetup plan, out bool useDoseCheck)
        {
            plan = GetSelectedPlan();
            useDoseCheck = false;
            try
            {
                if (plan == null || context.Patient == null)
                    throw new InvalidOperationException("Open the treatment plan in Eclipse before running QA.");

                useDoseCheck = DoseCheckLauncher.UseDoseCheck(GetSelectedMachineId(), plan.Beams
                    .Where(beam => !beam.IsSetupField)
                    .Select(beam => beam.TreatmentUnit?.Id));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Treatment Machine", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private async Task RunDoseCheckAsync(PlanSetup plan, bool waterPsqa)
        {
            string title = waterPsqa ? "DoseCHECK water PSQA" : "Send to DoseCHECK";
            string executable = waterPsqa ? AppPaths.DoseCheckPsqaExecutablePath : AppPaths.DoseCheckExecutablePath;
            string workingDirectory = waterPsqa ? AppPaths.DoseCheckPsqaWorkingDirectory : AppPaths.DoseCheckWorkingDirectory;
            btnSendPlan.IsEnabled = false;
            btnPerformQA.IsEnabled = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(AppPaths.ConfigurationLoadError))
                    throw new InvalidOperationException(AppPaths.ConfigurationLoadError);
                if (!plan.IsDoseValid)
                    throw new InvalidOperationException("Calculate and save the dose of the open plan before exporting it.");

                var startInfo = DoseCheckLauncher.CreateStartInfo(executable, workingDirectory,
                    context.Patient.Id, plan.Id, plan.UID, plan.Dose?.UID, Environment.UserName, waterPsqa);
                int exitCode = await ExternalProcessLauncher.RunAsync(startInfo);
                if (exitCode != 0)
                    throw new InvalidOperationException(
                        $"The exporter exited with code {exitCode}. Verify that the plan and dose were saved in Eclipse.\n" +
                        $"Review the exporter log in {Path.Combine(Path.GetDirectoryName(executable), "logs")} " +
                        "and check DoseCHECK for partial delivery before retrying.");

                MessageBox.Show(
                    "The DoseCHECK DICOM listener accepted the export. Check DoseCHECK for the result.",
                    title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSendPlan.IsEnabled = true;
                btnPerformQA.IsEnabled = true;
            }
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
