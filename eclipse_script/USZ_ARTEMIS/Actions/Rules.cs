using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.Core.Rules;
using USZ_ARTEMIS.StructureCreation;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Data;
using System.Drawing.Printing;


namespace USZ_ARTEMIS.Actions
{
    partial class Rules
    {
        private sealed class PlanSelectionItem
        {
            public PlanSelectionItem(PlanSetup plan, string displayText)
            {
                Plan = plan;
                DisplayText = displayText;
            }

            public PlanSetup Plan { get; }
            public string DisplayText { get; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private enum ExistingRulesAction
        {
            Overwrite,
            Append,
            Cancel
        }

        // ----------------- PATH / LOAD / SAVE (JSON) -----------------

        private static string GetPreferredRulesFilePath(PlanSetup selectedPlan)
        {
            // Canonical naming convention for rules files:
            // [PatientID]_[CourseID]_[PlanID].json
            string patientId = selectedPlan.Course.Patient.Id;
            string courseId = selectedPlan.Course.Id;
            string planId = selectedPlan.Id;
            return Path.Combine(AppPaths.RulesFolder, RulesFilePathUtilities.CreateFileName(patientId, courseId, planId));
        }

        public static string RetrieveRulesFile(PlanSetup selectedPlan)
        {
            return GetPreferredRulesFilePath(selectedPlan);
        }

        private static PlanRuleSet CreateEmptyRuleSet(PlanSetup selectedPlan)
        {
            string patientId = selectedPlan.Course.Patient.Id;
            string planId = selectedPlan.Id;
            string courseId = selectedPlan.Course.Id;

            return new PlanRuleSet
            {
                PatientId = patientId,
                CourseId = courseId,
                PlanId = planId,
                Version = 1
            };
        }

        private static void UpdateRuleSetMetadata(PlanSetup selectedPlan, PlanRuleSet ruleSet)
        {
            if (ruleSet == null)
            {
                return;
            }

            ruleSet.PatientId = selectedPlan.Course.Patient.Id;
            ruleSet.CourseId = selectedPlan.Course.Id;
            ruleSet.PlanId = selectedPlan.Id;
        }

        private static PlanRuleSet LoadRulesFromPath(string path, PlanSetup selectedPlan)
        {
            if (!File.Exists(path))
            {
                return CreateEmptyRuleSet(selectedPlan);
            }

            string json = File.ReadAllText(path);
            var ruleSet = JsonConvert.DeserializeObject<PlanRuleSet>(json);

            if (ruleSet == null)
            {
                ruleSet = CreateEmptyRuleSet(selectedPlan);
            }

            UpdateRuleSetMetadata(selectedPlan, ruleSet);
            return ruleSet;
        }

        private static PlanRuleSet LoadRulesForPlan(PlanSetup selectedPlan)
        {
            return LoadRulesFromPath(RetrieveRulesFile(selectedPlan), selectedPlan);
        }

        private static void SaveRulesForPlan(PlanSetup selectedPlan, PlanRuleSet ruleSet)
        {
            string path = GetPreferredRulesFilePath(selectedPlan);
            SaveRulesToPath(path, selectedPlan, ruleSet);
        }

        private static void SaveRulesToPath(string path, PlanSetup selectedPlan, PlanRuleSet ruleSet)
        {
            UpdateRuleSetMetadata(selectedPlan, ruleSet);
            string json = JsonConvert.SerializeObject(ruleSet, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        private static void OpenRulesFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{AppPaths.RulesFolder}\"",
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    "Could not open the Rules folder.\n\n" +
                    GetRulesPathDiagnostics(null) + "\n\n" + ex.Message,
                    "Open folder failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string GetRulesPathDiagnostics(string expectedPath)
        {
            string configurationPath = string.IsNullOrWhiteSpace(AppPaths.ConfigurationSourcePath)
                ? "(none found)"
                : AppPaths.ConfigurationSourcePath;
            string configurationError = string.IsNullOrWhiteSpace(AppPaths.ConfigurationLoadError)
                ? "(none)"
                : AppPaths.ConfigurationLoadError;

            string folderStatus;
            try
            {
                FileAttributes attributes = File.GetAttributes(AppPaths.RulesFolder);
                folderStatus = (attributes & FileAttributes.Directory) == FileAttributes.Directory
                    ? "Accessible directory"
                    : "Path exists but is not a directory";
            }
            catch (Exception ex)
            {
                folderStatus = $"Not accessible: {ex.GetType().Name}: {ex.Message}";
            }

            var lines = new List<string>
            {
                $"Configuration file: {configurationPath}",
                $"Configuration error: {configurationError}",
                $"Resolved rules folder: {AppPaths.RulesFolder}",
                $"Rules folder status: {folderStatus}"
            };

            if (!string.IsNullOrWhiteSpace(expectedPath))
            {
                lines.Add($"Expected full path: {expectedPath}");
            }

            return string.Join("\n", lines);
        }

        private static PlanSetup PromptForRulesSourcePlan(PlanSetup selectedPlan, string missingPath, string actionLabel)
        {
            var coursePlans = selectedPlan.Course.PlanSetups
                .OrderBy(plan => plan.Id)
                .ToList();

            if (coursePlans.Count == 0)
            {
                return null;
            }

            PlanSetup chosenPlan = null;
            string guessedBasePlanId = PlanIdUtilities.GuessBasePlanId(selectedPlan.Id);

            using (var popupForm = new Form())
            {
                popupForm.Text = "Rules file not found";
                popupForm.Width = 900;
                popupForm.Height = 390;
                popupForm.StartPosition = FormStartPosition.CenterScreen;
                popupForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                popupForm.MaximizeBox = false;
                popupForm.MinimizeBox = false;

                var diagnosticsText = new TextBox
                {
                    Left = 20,
                    Top = 20,
                    Width = 840,
                    Height = 225,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    WordWrap = false,
                    TabStop = false,
                    Text =
                        $"No rules file was found for plan '{selectedPlan.Id}'.\n" +
                        GetRulesPathDiagnostics(missingPath) + "\n\n" +
                        $"Select the base plan in course '{selectedPlan.Course.Id}' to {actionLabel} its rules."
                };

                var comboBox = new ComboBox
                {
                    Left = 20,
                    Top = 255,
                    Width = 840,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };

                foreach (var plan in coursePlans)
                {
                    string labelText = string.IsNullOrWhiteSpace(plan.Name) || string.Equals(plan.Name, plan.Id, StringComparison.Ordinal)
                        ? plan.Id
                        : $"{plan.Id} ({plan.Name})";

                    if (plan.UID == selectedPlan.UID)
                    {
                        labelText += " [current]";
                    }

                    comboBox.Items.Add(new PlanSelectionItem(plan, labelText));
                }

                int selectedIndex = coursePlans.FindIndex(plan =>
                    !string.Equals(plan.UID, selectedPlan.UID, StringComparison.Ordinal) &&
                    string.Equals(plan.Id, guessedBasePlanId, StringComparison.OrdinalIgnoreCase));

                if (selectedIndex < 0)
                {
                    selectedIndex = coursePlans.FindIndex(plan => !string.Equals(plan.UID, selectedPlan.UID, StringComparison.Ordinal));
                }

                comboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

                var usePlanButton = new Button
                {
                    Text = "Use selected plan",
                    Width = 140,
                    Left = 230,
                    Top = 295
                };

                usePlanButton.Click += (sender, e) =>
                {
                    chosenPlan = (comboBox.SelectedItem as PlanSelectionItem)?.Plan;
                    if (chosenPlan == null)
                    {
                        MessageBox.Show("Please select a plan.", "Rules file not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    popupForm.DialogResult = DialogResult.OK;
                    popupForm.Close();
                };

                var openFolderButton = new Button
                {
                    Text = "Open rules folder",
                    Width = 140,
                    Left = 380,
                    Top = 295
                };

                openFolderButton.Click += (sender, e) => OpenRulesFolder();

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Width = 100,
                    Left = 530,
                    Top = 295
                };

                cancelButton.Click += (sender, e) =>
                {
                    popupForm.DialogResult = DialogResult.Cancel;
                    popupForm.Close();
                };

                popupForm.Controls.Add(diagnosticsText);
                popupForm.Controls.Add(comboBox);
                popupForm.Controls.Add(usePlanButton);
                popupForm.Controls.Add(openFolderButton);
                popupForm.Controls.Add(cancelButton);
                popupForm.AcceptButton = usePlanButton;
                popupForm.CancelButton = cancelButton;
                popupForm.ShowDialog();
            }

            return chosenPlan;
        }

        private static string ResolveRulesFilePath(PlanSetup selectedPlan, string initialPath, string actionLabel)
        {
            string path = initialPath;

            while (!File.Exists(path))
            {
                var sourcePlan = PromptForRulesSourcePlan(selectedPlan, path, actionLabel);
                if (sourcePlan == null)
                {
                    return null;
                }

                path = RetrieveRulesFile(sourcePlan);
                if (File.Exists(path))
                {
                    return path;
                }

                MessageBox.Show(
                    $"No rules file was found for plan '{sourcePlan.Id}'.\n\nPlease choose another plan or open the Rules folder.",
                    "Rules file not found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            return path;
        }

        // ----------------- UI HELPERS -----------------

        public static string SelectRuleType()
        {
            string selectedOption = null;

            Form popupForm = new Form();
            popupForm.Text = "Select the rule type";
            popupForm.Width = 300;
            popupForm.Height = 150;

            ComboBox comboBox = new ComboBox();
            comboBox.Items.AddRange(new string[] { "Expand", "Subtract", "Add", "Intersect", "Create SBRT ring", "Create RectalWall" });
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Location = new System.Drawing.Point(30, 20);
            comboBox.Width = 200;
            comboBox.SelectedIndex = 0;

            Button continueButton = new Button();
            continueButton.Text = "Continue";
            continueButton.Location = new System.Drawing.Point(100, 60);
            continueButton.Width = 100;

            continueButton.Click += (sender, e) =>
            {
                selectedOption = comboBox.SelectedItem.ToString();
                popupForm.Close();
            };

            popupForm.Controls.Add(comboBox);
            popupForm.Controls.Add(continueButton);

            popupForm.ShowDialog();

            return selectedOption;
        }

        public static string SelectStructure(PlanSetup SelectedPlan, string labelInstruction)
            => SelectStructure(SelectedPlan, labelInstruction, false);

        public static string SelectStructure(PlanSetup SelectedPlan, string labelInstruction, bool allowCustomName)
        {
            string selectedOption = null;

            using (Form popupForm = new Form())
            {
                popupForm.Text = labelInstruction;
                popupForm.Width = 420;
                popupForm.Height = 210;
                popupForm.StartPosition = FormStartPosition.CenterScreen;
                popupForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                popupForm.MaximizeBox = false;
                popupForm.MinimizeBox = false;

                Label hintLabel = new Label
                {
                    Left = 20,
                    Top = 16,
                    Width = 360,
                    Height = 40,
                    AutoSize = false,
                    Text = allowCustomName
                        ? "Select an existing structure or type a new output structure name."
                        : "Select a structure."
                };

                ComboBox comboBox = new ComboBox();

                foreach (Structure tempStructure in SelectedPlan.StructureSet.Structures)
                {
                    comboBox.Items.Add(tempStructure.Id);
                }

                comboBox.DropDownStyle = allowCustomName ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
                if (allowCustomName)
                {
                    comboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
                    comboBox.AutoCompleteSource = AutoCompleteSource.ListItems;
                }
                else
                {
                    comboBox.AutoCompleteMode = AutoCompleteMode.None;
                    comboBox.AutoCompleteSource = AutoCompleteSource.None;
                }
                comboBox.Location = new System.Drawing.Point(20, 62);
                comboBox.Width = 360;
                comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;

                Button continueButton = new Button();
                continueButton.Text = "Continue";
                continueButton.Location = new System.Drawing.Point(145, 130);
                continueButton.Width = 110;

                continueButton.Click += (sender, e) =>
                {
                    string rawValue = allowCustomName
                        ? comboBox.Text
                        : comboBox.SelectedItem?.ToString();

                    string candidate = (rawValue ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        MessageBox.Show(
                            allowCustomName
                                ? "Please select an existing structure or enter a new name."
                                : "Please select a structure.",
                            "Missing structure",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        return;
                    }

                    selectedOption = candidate;
                    popupForm.Close();
                };

                popupForm.Controls.Add(hintLabel);
                popupForm.Controls.Add(comboBox);
                popupForm.Controls.Add(continueButton);
                popupForm.AcceptButton = continueButton;

                popupForm.ShowDialog();
            }

            return selectedOption;
        }

        // Multi-select helper: returns a list of selected structure IDs
        public static List<string> SelectMultipleStructures(PlanSetup SelectedPlan, string labelInstruction)
        {
            var selected = new List<string>();

            using (Form popupForm = new Form())
            {
                popupForm.Text = labelInstruction;
                popupForm.Width = 400;
                popupForm.Height = 300;

                ListBox listBox = new ListBox();
                listBox.SelectionMode = SelectionMode.MultiExtended;
                listBox.Location = new Point(20, 20);
                listBox.Size = new Size(340, 200);

                foreach (Structure tempStructure in SelectedPlan.StructureSet.Structures)
                {
                    listBox.Items.Add(tempStructure.Id);
                }

                Button okButton = new Button();
                okButton.Text = "OK";
                okButton.Location = new Point(150, 230);
                okButton.Width = 80;

                okButton.Click += (sender, e) =>
                {
                    foreach (var item in listBox.SelectedItems)
                    {
                        selected.Add(item.ToString());
                    }
                    popupForm.Close();
                };

                popupForm.Controls.Add(listBox);
                popupForm.Controls.Add(okButton);

                popupForm.ShowDialog();
            }

            return selected;
        }

        private static bool SelectLymphGtv(
            PlanSetup plan,
            out List<string> selectedGtvIds,
            out string gtvId,
            out string ptvId,
            out List<string> ptvInputs)
        {
            selectedGtvIds = new List<string>();
            ptvInputs = new List<string>();
            gtvId = null;
            ptvId = null;

            var structures = plan.StructureSet.Structures;

            // Candidates: primarily by DicomType == GTV, otherwise by name containing "GTV"
            var gtvCandidates = structures
                .Where(s =>
                    (!string.IsNullOrEmpty(s.DicomType) &&
                     s.DicomType.Equals("GTV", StringComparison.OrdinalIgnoreCase))
                    || s.Id.IndexOf("GTV", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.Id)
                .ToList();

            if (gtvCandidates.Count == 0)
            {
                MessageBox.Show("No GTV structures were found in this plan.");
                return false;
            }

            var picked = new List<string>();

            using (Form popupForm = new Form())
            {
                popupForm.Text = "Select GTV(s) for lymph node template";
                popupForm.Width = 420;
                popupForm.Height = 320;
                popupForm.StartPosition = FormStartPosition.CenterScreen;

                Label lblInfo = new Label
                {
                    Text = "Select one or more GTVs:",
                    AutoSize = true,
                    Location = new Point(20, 10)
                };

                ListBox listBox = new ListBox
                {
                    SelectionMode = SelectionMode.MultiExtended,
                    Location = new Point(20, 30),
                    Size = new Size(360, 200)
                };

                foreach (var s in gtvCandidates)
                    listBox.Items.Add(s.Id);

                Button okButton = new Button
                {
                    Text = "OK",
                    Location = new Point(150, 240),
                    Width = 100
                };

                okButton.Click += (sender, e) =>
                {
                    picked.Clear();
                    foreach (var item in listBox.SelectedItems)
                        picked.Add(item.ToString());

                    if (picked.Count == 0)
                    {
                        MessageBox.Show("Please select at least one GTV.");
                        return;
                    }

                    popupForm.DialogResult = DialogResult.OK;
                    popupForm.Close();
                };

                popupForm.Controls.Add(lblInfo);
                popupForm.Controls.Add(listBox);
                popupForm.Controls.Add(okButton);
                popupForm.AcceptButton = okButton;

                if (popupForm.ShowDialog() != DialogResult.OK)
                    return false;
            }

            // Assign to out params *after* dialog closes
            selectedGtvIds = picked.ToList();

            if (selectedGtvIds.Count == 1)
            {
                gtvId = selectedGtvIds[0];
            }
            else
            {
                // Multiple GTVs → ask for combined GTV name
                string defaultName = "GTV1_Vx_1a_Ph";
                string combinedName = Interaction.InputBox(
                    "Multiple GTVs selected. Enter name for the combined GTV (sum of selected GTVs):",
                    "Combined GTV name",
                    defaultName);

                if (string.IsNullOrWhiteSpace(combinedName))
                {
                    MessageBox.Show("No combined GTV name entered. Aborting template creation.");
                    return false;
                }

                gtvId = combinedName.Trim();
            }

            // Deduce PTV from chosen/combined GTV name
            ptvId = MapGtvToPtvId(gtvId);

            if (string.IsNullOrWhiteSpace(ptvId))
            {
                MessageBox.Show(
                    "Warning: Could not deduce a PTV name from '" + gtvId +
                    "'.\nPlease verify the structure names.",
                    "PTV name warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                // Fallback to the old behavior as a last resort
                ptvId = gtvId.Replace("GTV", "PTV");
            }

            // For multiple selection: expose which PTVs should be unioned by the caller
            if (selectedGtvIds.Count > 1)
            {
                ptvInputs = selectedGtvIds
                    .Select(MapGtvToPtvId)
                    .ToList();
            }

            return true;
        }

        private static string MapGtvToPtvId(string gtv)
        {
            if (string.IsNullOrWhiteSpace(gtv))
                return null;

            // Treat "delimiter" as start/end or any NON-alphanumeric char (so '_' works as delimiter)
            // Examples:
            //   "GTV1_V1_1a"   -> "PTV1_V1_1a"
            //   "GTVn_V2_1a"   -> "PTV1_V2_1a"
            //   "GTVp_V3_1a"   -> "PTV1_V3_1a"
            // Also works if "GTV1" appears later in the string, preceded by a delimiter.

            // 1) GTV<digits> -> PTV<digits>
            var rxNum = new Regex(@"(^|[^A-Za-z0-9])GTV(?<n>\d+)(?=$|[^A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (rxNum.IsMatch(gtv))
            {
                return rxNum.Replace(gtv, m => $"{m.Groups[1].Value}PTV{m.Groups["n"].Value}");
            }

            // 2) GTVp / GTVn -> PTV1
            var rxPn = new Regex(@"(^|[^A-Za-z0-9])GTV(?<s>[pn])(?=$|[^A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (rxPn.IsMatch(gtv))
            {
                return rxPn.Replace(gtv, m => $"{m.Groups[1].Value}PTV1");
            }

            // 3) Fallback: replace any remaining "GTV" token (legacy behavior)
            if (gtv.IndexOf("GTV", StringComparison.OrdinalIgnoreCase) >= 0)
                return Regex.Replace(gtv, "GTV", "PTV", RegexOptions.IgnoreCase);

            return null;
        }



        private static string SelectCTV1(PlanSetup plan)
        {
            var structures = plan.StructureSet.Structures;

            // Prefer IDs that look like CTV
            var candidates = structures
                .Where(s =>
                    s.Id.IndexOf("CTV1", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(s => s.Id)
                .ToList();

            // Fallback: if no obvious candidates, show all structures
            if (candidates.Count == 0)
            {
                candidates = structures.OrderBy(s => s.Id).ToList();
            }

            if (candidates.Count == 0)
            {
                MessageBox.Show("No structures were found in this plan.");
                return null;
            }

            string selectedId = null;

            using (Form popupForm = new Form())
            {
                popupForm.Text = "Select CTV1";
                popupForm.Width = 420;
                popupForm.Height = 160;
                popupForm.StartPosition = FormStartPosition.CenterScreen;

                Label lblInfo = new Label
                {
                    Text = "CTV1 structure:",
                    AutoSize = true,
                    Location = new Point(20, 20)
                };

                ComboBox comboBox = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(20, 45),
                    Width = 360
                };

                foreach (var s in candidates)
                    comboBox.Items.Add(s.Id);

                if (comboBox.Items.Count > 0)
                    comboBox.SelectedIndex = 0;

                Button okButton = new Button
                {
                    Text = "OK",
                    Location = new Point(160, 80),
                    Width = 80
                };

                okButton.Click += (sender, e) =>
                {
                    if (comboBox.SelectedItem == null)
                    {
                        MessageBox.Show("Please select a structure.");
                        return;
                    }

                    selectedId = comboBox.SelectedItem.ToString();
                    popupForm.DialogResult = DialogResult.OK;
                    popupForm.Close();
                };

                popupForm.Controls.Add(lblInfo);
                popupForm.Controls.Add(comboBox);
                popupForm.Controls.Add(okButton);
                popupForm.AcceptButton = okButton;

                if (popupForm.ShowDialog() != DialogResult.OK)
                {
                    return null;
                }
            }

            return selectedId;
        }

        private static ExistingRulesAction AskHowToHandleExistingRules(string rulesFilePath, int existingRuleCount)
        {
            ExistingRulesAction action = ExistingRulesAction.Cancel;

            using (Form popupForm = new Form())
            {
                popupForm.Text = "Existing rules found";
                popupForm.Width = 560;
                popupForm.Height = 230;
                popupForm.StartPosition = FormStartPosition.CenterScreen;
                popupForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                popupForm.MaximizeBox = false;
                popupForm.MinimizeBox = false;

                Label label = new Label
                {
                    AutoSize = false,
                    Location = new Point(20, 20),
                    Size = new Size(510, 95),
                    Text =
                        "Rules already exist for this plan.\n\n" +
                        // $"Existing rules: {existingRuleCount}\n" +
                        $"File: {rulesFilePath}\n\n" +
                        "Choose how to proceed:"
                };

                Button overwriteButton = new Button
                {
                    Text = "Overwrite",
                    Width = 100,
                    Location = new Point(90, 130)
                };
                overwriteButton.Click += (sender, e) =>
                {
                    action = ExistingRulesAction.Overwrite;
                    popupForm.DialogResult = DialogResult.OK;
                    popupForm.Close();
                };

                Button appendButton = new Button
                {
                    Text = "Append",
                    Width = 100,
                    Location = new Point(220, 130)
                };
                appendButton.Click += (sender, e) =>
                {
                    action = ExistingRulesAction.Append;
                    popupForm.DialogResult = DialogResult.OK;
                    popupForm.Close();
                };

                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    Width = 100,
                    Location = new Point(350, 130),
                    DialogResult = DialogResult.Cancel
                };
                cancelButton.Click += (sender, e) =>
                {
                    action = ExistingRulesAction.Cancel;
                    popupForm.Close();
                };

                popupForm.Controls.Add(label);
                popupForm.Controls.Add(overwriteButton);
                popupForm.Controls.Add(appendButton);
                popupForm.Controls.Add(cancelButton);
                popupForm.AcceptButton = appendButton;
                popupForm.CancelButton = cancelButton;

                popupForm.ShowDialog();
            }

            return action;
        }

        // ----------------- CREATE RULES (JSON) -----------------

        public static void CreateExpansion(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            string structureIn = SelectStructure(SelectedPlan, "Structure to expand");
            if (string.IsNullOrEmpty(structureIn)) return;

            string marginStr = Interaction.InputBox("Expansion in mm:", "Expansion margin", "3");
            if (!double.TryParse(marginStr, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out double marginMm))
            {
                MessageBox.Show("Invalid margin.");
                return;
            }

            string structureOut = SelectStructure(SelectedPlan, "Output structure", true);
            if (string.IsNullOrEmpty(structureOut)) return;

            var rule = new StructureRule
            {
                Type = RuleType.Expansion,
                InputStructures = new List<string> { structureIn },
                OutputStructure = structureOut,
                MarginMm = marginMm
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        public static void CreateAsymmetricExpansion(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            string structureIn = SelectStructure(SelectedPlan, "Structure to asymmetrically expand");
            if (string.IsNullOrEmpty(structureIn)) return;

            string structureOut = SelectStructure(SelectedPlan, "Output structure", true);
            if (string.IsNullOrEmpty(structureOut)) return;

            string marginStr = Interaction.InputBox(
                "Enter 6 asymmetric margins in mm (X-, Y-, Z-, X+, Y+, Z+), separated by spaces, commas, or semicolons:",
                "Asymmetric expansion margins",
                "0 5 0 0 0 0");

            if (string.IsNullOrWhiteSpace(marginStr))
                return;

            var parts = marginStr.Split(new[] { ' ', ';', ',', '\t' },
                                        StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6)
            {
                MessageBox.Show("Please enter exactly 6 numeric values.");
                return;
            }

            var margins = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (!double.TryParse(
                        parts[i],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out margins[i]))
                {
                    MessageBox.Show($"Invalid margin value at position {i + 1}: '{parts[i]}'");
                    return;
                }
            }

            var rule = new StructureRule
            {
                Type = RuleType.AsymmetricExpansion,
                InputStructures = new List<string> { structureIn },
                OutputStructure = structureOut,
                AsymmetricMarginsMm = margins
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }


        public static void CreateSubtraction(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            // Base structure (to be cropped)
            string baseStructure = SelectStructure(SelectedPlan, "Structure to crop");
            if (string.IsNullOrEmpty(baseStructure)) return;

            // One or more cropping structures
            var croppingStructures = SelectMultipleStructures(SelectedPlan, "Cropping structures (select one or more)");
            if (croppingStructures == null || croppingStructures.Count == 0)
            {
                MessageBox.Show("No cropping structures selected.");
                return;
            }

            string structureOut = SelectStructure(SelectedPlan, "Output structure", true);
            if (string.IsNullOrEmpty(structureOut)) return;

            var inputs = new List<string> { baseStructure };
            inputs.AddRange(croppingStructures);

            var rule = new StructureRule
            {
                Type = RuleType.Subtraction,
                InputStructures = inputs,
                OutputStructure = structureOut
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        public static void CreateAddition(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            var inputs = SelectMultipleStructures(SelectedPlan, "Structures to add (select two or more)");
            if (inputs == null || inputs.Count < 2)
            {
                MessageBox.Show("Please select at least two structures to add.");
                return;
            }

            string structureOut = SelectStructure(SelectedPlan, "Output structure", true);
            if (string.IsNullOrEmpty(structureOut)) return;

            var rule = new StructureRule
            {
                Type = RuleType.Addition,
                InputStructures = inputs,
                OutputStructure = structureOut
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        public static void CreateIntersection(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            var inputs = SelectMultipleStructures(SelectedPlan, "Structures to intersect (select two or more)");
            if (inputs == null || inputs.Count < 2)
            {
                MessageBox.Show("Please select at least two structures to intersect.");
                return;
            }

            string structureOut = SelectStructure(SelectedPlan, "Output structure", true);
            if (string.IsNullOrEmpty(structureOut)) return;

            var rule = new StructureRule
            {
                Type = RuleType.Intersection,
                InputStructures = inputs,
                OutputStructure = structureOut
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        public static void CreateSbrtRing(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            string ptv = SelectStructure(SelectedPlan, "Select PTV");
            if (string.IsNullOrEmpty(ptv)) return;

            string itv = SelectStructure(SelectedPlan, "Select GTV/CTV/ITV");
            if (string.IsNullOrEmpty(itv)) return;

            string output = ptv + "_Ph";

            var rule = new StructureRule
            {
                Type = RuleType.SbrtRing,
                InputStructures = new List<string> { ptv, itv },
                OutputStructure = output
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        public static void CreateRectalWall(PlanSetup SelectedPlan)
        {
            var ruleSet = LoadRulesForPlan(SelectedPlan);

            var rule = new StructureRule
            {
                Type = RuleType.RectalWall,
                InputStructures = new List<string>(),
                OutputStructure = "RectalWall_Ph"
            };

            ruleSet.Rules.Add(rule);
            SaveRulesForPlan(SelectedPlan, ruleSet);
        }

        // ----------------- VIEW / DELETE / EDIT -----------------


        private static void ShowWideText(string title, string text, int width = 900, int height = 600)
        {
            using (var form = new System.Windows.Forms.Form
            {
                Text = title,
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen,
                Size = new System.Drawing.Size(width, height),
                MinimumSize = new System.Drawing.Size(500, 300),
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable,
                MaximizeBox = true,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true
            })
            using (var tb = new System.Windows.Forms.TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = System.Windows.Forms.DockStyle.Fill,
                ScrollBars = System.Windows.Forms.ScrollBars.Both,
                WordWrap = false, // important: keeps it wide instead of wrapping
                Font = new System.Drawing.Font(FontFamily.GenericMonospace, 12f),
                Text = text
            })
            using (var ok = new System.Windows.Forms.Button
            {
                Text = "OK",
                Dock = System.Windows.Forms.DockStyle.Bottom,
                Height = 34,
                DialogResult = System.Windows.Forms.DialogResult.OK
            })
            {
                form.AcceptButton = ok;
                form.Controls.Add(tb);
                form.Controls.Add(ok);
                form.ShowDialog();
            }
        }


        public static void ViewRules(PlanSetup SelectedPlan)
        {
            string path = ResolveRulesFilePath(SelectedPlan, RetrieveRulesFile(SelectedPlan), "view");
            if (path == null)
            {
                return;
            }

            var ruleSet = LoadRulesFromPath(path, SelectedPlan);

            if (ruleSet.Rules.Count == 0)
            {
                MessageBox.Show("The selected rules file contains no rules.", "Rules", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string outMessage = string.Join(
                "\r\n",
                ruleSet.Rules.Select((rule, index) => $"{index + 1:00}. {DescribeRule(rule)}"));

            ShowWideText("List of rules", outMessage);
        }

        public static void EditRules(PlanSetup SelectedPlan)
        {
            string rulesFile = ResolveRulesFilePath(SelectedPlan, RetrieveRulesFile(SelectedPlan), "edit");
            if (rulesFile == null)
            {
                return;
            }

            var ruleSet = LoadRulesFromPath(rulesFile, SelectedPlan);
            ShowRulesManager(SelectedPlan, rulesFile, ruleSet);
        }

        private static string DescribeRule(StructureRule rule)
        {
            if (rule == null)
            {
                return "(invalid rule)";
            }

            switch (rule.Type)
            {
                case RuleType.Expansion:
                    {
                        string input = rule.InputStructures?.FirstOrDefault() ?? "?";
                        string marginText = rule.MarginMm.HasValue ? rule.MarginMm.Value.ToString("0.###") : "?";
                        return $"{rule.OutputStructure} = {input} + {marginText}mm";
                    }
                case RuleType.AsymmetricExpansion:
                    {
                        string input = rule.InputStructures?.FirstOrDefault() ?? "?";
                        string marginsText = (rule.AsymmetricMarginsMm != null && rule.AsymmetricMarginsMm.Length == 6)
                            ? string.Join("/", rule.AsymmetricMarginsMm.Select(m => m.ToString("0.###")))
                            : "?";
                        return $"{rule.OutputStructure} = {input} + asym({marginsText}) mm (X-/Y-/Z-/X+/Y+/Z+)";
                    }
                case RuleType.MorphologicalOpening:
                    {
                        string input = rule.InputStructures?.FirstOrDefault() ?? "?";
                        string marginText = rule.MarginMm.HasValue ? rule.MarginMm.Value.ToString("0.###") : "?";
                        return $"{rule.OutputStructure} = {input} opening with {marginText}mm";
                    }
                case RuleType.Subtraction:
                    {
                        if (rule.InputStructures != null && rule.InputStructures.Count >= 2)
                        {
                            string baseStr = rule.InputStructures[0];
                            string rest = string.Join(" - ", rule.InputStructures.Skip(1));
                            return $"{rule.OutputStructure} = {baseStr} - {rest}";
                        }

                        return $"{rule.OutputStructure} = subtraction (invalid inputs)";
                    }
                case RuleType.Addition:
                    return $"{rule.OutputStructure} = {string.Join(" + ", rule.InputStructures ?? new List<string>())}";
                case RuleType.Intersection:
                    return $"{rule.OutputStructure} = {string.Join(" AND ", rule.InputStructures ?? new List<string>())}";
                case RuleType.SbrtRing:
                    {
                        if (rule.InputStructures != null && rule.InputStructures.Count == 2)
                        {
                            return $"{rule.OutputStructure} = SBRT ring between {rule.InputStructures[0]} and {rule.InputStructures[1]}";
                        }

                        return $"{rule.OutputStructure} = SBRT ring (invalid inputs)";
                    }
                case RuleType.RectalWall:
                    return "RectalWall_Ph = Automatic generation";
                default:
                    return $"{rule.OutputStructure} = {rule.Type}";
            }
        }

        private static StructureRule CloneRule(StructureRule rule)
        {
            if (rule == null)
            {
                return new StructureRule();
            }

            return new StructureRule
            {
                Type = rule.Type,
                InputStructures = rule.InputStructures?.ToList() ?? new List<string>(),
                OutputStructure = rule.OutputStructure,
                MarginMm = rule.MarginMm,
                AsymmetricMarginsMm = rule.AsymmetricMarginsMm?.ToArray()
            };
        }

        private static void ShowRulesManager(PlanSetup selectedPlan, string rulesFile, PlanRuleSet ruleSet)
        {
            var workingRules = (ruleSet.Rules ?? new List<StructureRule>())
                .Select(CloneRule)
                .ToList();

            using (var popupForm = new Form())
            {
                popupForm.Text = "Manage automation rules";
                popupForm.Width = 1150;
                popupForm.Height = 680;
                popupForm.StartPosition = FormStartPosition.CenterScreen;
                popupForm.MinimumSize = new Size(900, 500);
                popupForm.FormBorderStyle = FormBorderStyle.Sizable;
                popupForm.MaximizeBox = true;
                popupForm.MinimizeBox = false;
                popupForm.ShowInTaskbar = false;
                popupForm.TopMost = true;

                var fileLabel = new Label
                {
                    Left = 20,
                    Top = 18,
                    Width = 1090,
                    Height = 34,
                    AutoSize = false,
                    Text = $"Rules file: {rulesFile}"
                };

                var listBox = new ListBox
                {
                    Left = 20,
                    Top = 58,
                    Width = 860,
                    Height = 550,
                    HorizontalScrollbar = true,
                    Font = new Font(FontFamily.GenericMonospace, 10f),
                    IntegralHeight = false
                };

                var moveUpButton = new Button
                {
                    Text = "Move up",
                    Width = 220,
                    Left = 900,
                    Top = 58
                };

                var moveDownButton = new Button
                {
                    Text = "Move down",
                    Width = 220,
                    Left = 900,
                    Top = 98
                };

                var deleteButton = new Button
                {
                    Text = "Delete selected",
                    Width = 220,
                    Left = 900,
                    Top = 150
                };

                var inspectButton = new Button
                {
                    Text = "Inspect selected",
                    Width = 220,
                    Left = 900,
                    Top = 190
                };

                var saveButton = new Button
                {
                    Text = "Save changes",
                    Width = 220,
                    Left = 900,
                    Top = 536
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    Width = 220,
                    Left = 900,
                    Top = 576,
                    DialogResult = DialogResult.Cancel
                };

                Action updateButtons = () =>
                {
                    int selectedIndex = listBox.SelectedIndex;
                    bool hasSelection = selectedIndex >= 0 && selectedIndex < workingRules.Count;

                    moveUpButton.Enabled = hasSelection && selectedIndex > 0;
                    moveDownButton.Enabled = hasSelection && selectedIndex < workingRules.Count - 1;
                    deleteButton.Enabled = hasSelection;
                    inspectButton.Enabled = hasSelection;
                };

                Action<int> refreshList = preferredIndex =>
                {
                    listBox.Items.Clear();
                    for (int i = 0; i < workingRules.Count; i++)
                    {
                        listBox.Items.Add($"{i + 1:00}. {DescribeRule(workingRules[i])}");
                    }

                    if (workingRules.Count > 0)
                    {
                        int index = preferredIndex;
                        if (index < 0) index = 0;
                        if (index >= workingRules.Count) index = workingRules.Count - 1;
                        listBox.SelectedIndex = index;
                    }

                    updateButtons();
                };

                listBox.SelectedIndexChanged += (sender, e) => updateButtons();

                moveUpButton.Click += (sender, e) =>
                {
                    int index = listBox.SelectedIndex;
                    if (index <= 0 || index >= workingRules.Count)
                    {
                        return;
                    }

                    var tmp = workingRules[index - 1];
                    workingRules[index - 1] = workingRules[index];
                    workingRules[index] = tmp;
                    refreshList(index - 1);
                };

                moveDownButton.Click += (sender, e) =>
                {
                    int index = listBox.SelectedIndex;
                    if (index < 0 || index >= workingRules.Count - 1)
                    {
                        return;
                    }

                    var tmp = workingRules[index + 1];
                    workingRules[index + 1] = workingRules[index];
                    workingRules[index] = tmp;
                    refreshList(index + 1);
                };

                deleteButton.Click += (sender, e) =>
                {
                    int index = listBox.SelectedIndex;
                    if (index < 0 || index >= workingRules.Count)
                    {
                        return;
                    }

                    var confirmation = MessageBox.Show(
                        "Delete the selected rule?\n\n" + DescribeRule(workingRules[index]),
                        "Delete rule",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2);

                    if (confirmation != DialogResult.Yes)
                    {
                        return;
                    }

                    workingRules.RemoveAt(index);
                    refreshList(index);
                };

                inspectButton.Click += (sender, e) =>
                {
                    int index = listBox.SelectedIndex;
                    if (index < 0 || index >= workingRules.Count)
                    {
                        return;
                    }

                    string details = JsonConvert.SerializeObject(workingRules[index], Formatting.Indented);
                    ShowWideText($"Rule {index + 1} details", details);
                };

                saveButton.Click += (sender, e) =>
                {
                    try
                    {
                        ruleSet.Rules = workingRules.Select(CloneRule).ToList();
                        SaveRulesToPath(rulesFile, selectedPlan, ruleSet);

                        MessageBox.Show(
                            "Rules were saved.\n\nYou can now apply them in the new order.",
                            "Rules saved",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        popupForm.DialogResult = DialogResult.OK;
                        popupForm.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "Could not save rules.\n\n" + ex.Message,
                            "Save failed",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                };

                cancelButton.Click += (sender, e) =>
                {
                    popupForm.DialogResult = DialogResult.Cancel;
                    popupForm.Close();
                };

                popupForm.Controls.Add(fileLabel);
                popupForm.Controls.Add(listBox);
                popupForm.Controls.Add(moveUpButton);
                popupForm.Controls.Add(moveDownButton);
                popupForm.Controls.Add(deleteButton);
                popupForm.Controls.Add(inspectButton);
                popupForm.Controls.Add(saveButton);
                popupForm.Controls.Add(cancelButton);
                popupForm.AcceptButton = saveButton;
                popupForm.CancelButton = cancelButton;

                refreshList(0);
                popupForm.ShowDialog();
            }
        }

        private static string BuildCropId(string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId))
                return baseId;

            // If baseId already ends with "_Ph", strip it so we don't end up with "..._Ph_crop_Ph"
            string core = baseId.EndsWith("_Ph", StringComparison.OrdinalIgnoreCase)
                ? baseId.Substring(0, baseId.Length - 3)
                : baseId;

            return core + "_crop_Ph";
        }

        private static bool AddLymphNodeTargetsToRuleSet(
            PlanSetup plan,
            PlanRuleSet ruleSet,
            double ptvMarginMm,
            out string gtvId,
            out string ptvId)
        {
            gtvId = null;
            ptvId = null;

            // Reuse your existing selection logic
            if (!SelectLymphGtv(plan, out var selectedGtvIds, out gtvId, out ptvId, out _))
                return false;

            // Map each selected GTV -> PTV (handles GTVn/GTVp -> PTV1 etc.)
            var ptvInputs = selectedGtvIds
                .Select(MapGtvToPtvId)
                .ToList();

            // Safety: mapping failed
            if (ptvInputs.Any(string.IsNullOrWhiteSpace))
            {
                MessageBox.Show("Could not map one or more selected GTV IDs to PTV IDs. Please check naming.");
                return false;
            }

            // Safety: collisions after mapping (e.g. GTVp + GTVn both -> PTV1 with same suffix)
            var dup = ptvInputs
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (dup.Count > 0)
            {
                MessageBox.Show(
                    "PTV ID collision after mapping (e.g. GTVp/GTVn both map to PTV1 with same suffix):\n" +
                    string.Join("\n", dup) +
                    "\n\nRename the GTVs (or use numeric GTVs) so each mapped PTV ID is unique.",
                    "PTV mapping collision",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            if (selectedGtvIds.Count == 1)
            {
                // Single GTV -> create PTV = GTV + margin
                ruleSet.Rules.Add(new StructureRule
                {
                    Type = RuleType.Expansion,
                    InputStructures = new List<string> { gtvId },
                    OutputStructure = ptvId,
                    MarginMm = ptvMarginMm
                });
                return true;
            }

            // Multiple GTVs:
            // 1) Union selected GTVs -> combined GTV (gtvId)
            ruleSet.Rules.Add(new StructureRule
            {
                Type = RuleType.Addition,
                InputStructures = selectedGtvIds,
                OutputStructure = gtvId
            });

            // 2) Expand each selected GTV -> its corresponding PTV
            for (int i = 0; i < selectedGtvIds.Count; i++)
            {
                ruleSet.Rules.Add(new StructureRule
                {
                    Type = RuleType.Expansion,
                    InputStructures = new List<string> { selectedGtvIds[i] },
                    OutputStructure = ptvInputs[i],
                    MarginMm = ptvMarginMm
                });
            }

            // 3) Union expanded PTVs -> combined PTV (ptvId)
            ruleSet.Rules.Add(new StructureRule
            {
                Type = RuleType.Addition,
                InputStructures = ptvInputs,
                OutputStructure = ptvId
            });

            return true;
        }

        public static void CreateRulesFromTemplate(PlanSetup selectedPlan)
        {
            if (selectedPlan == null)
            {
                MessageBox.Show("No plan selected.");
                return;
            }

            string selectedTemplate = null;

            // Simple popup with a dropdown (ComboBox)
            Form popupForm = new Form();
            popupForm.Text = "Select rule template";
            popupForm.Width = 420;
            popupForm.Height = 150;
            popupForm.StartPosition = FormStartPosition.CenterScreen;

            Label label = new Label();
            label.Text = "Template:";
            label.AutoSize = true;
            label.Location = new Point(20, 24);

            ComboBox comboBox = new ComboBox();
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Location = new Point(100, 20);
            comboBox.Width = 280;
            comboBox.Items.AddRange(new string[]
            {
        "Prostate 2 dose levels",
        "Prostate 3 dose levels",
        "Lymph node(s) 2 x 5 Gy",
        "Lymph node(s) 5 x 7 Gy"
            });
            comboBox.SelectedIndex = 0;

            Button continueButton = new Button();
            continueButton.Text = "Continue";
            continueButton.Location = new Point(160, 60);
            continueButton.Width = 100;

            continueButton.Click += (sender, e) =>
            {
                selectedTemplate = comboBox.SelectedItem?.ToString();
                popupForm.Close();
            };

            popupForm.Controls.Add(label);
            popupForm.Controls.Add(comboBox);
            popupForm.Controls.Add(continueButton);

            popupForm.ShowDialog();

            // User cancelled or closed the window
            if (string.IsNullOrEmpty(selectedTemplate))
                return;

            var ruleSet = LoadRulesForPlan(selectedPlan);
            bool overwriteExistingRules = true;
            if (ruleSet.Rules != null && ruleSet.Rules.Count > 0)
            {
                string rulesFilePath = RetrieveRulesFile(selectedPlan);
                var action = AskHowToHandleExistingRules(rulesFilePath, ruleSet.Rules.Count);

                if (action == ExistingRulesAction.Cancel)
                    return;

                overwriteExistingRules = action == ExistingRulesAction.Overwrite;
            }

            if (overwriteExistingRules)
                ruleSet.Rules.Clear();

            switch (selectedTemplate)
            {
                case "Prostate 2 dose levels":
                    {
                        // Select CTV1
                        string ctv1Id = SelectCTV1(selectedPlan);
                        string ptv1Id = ctv1Id.Replace("CTV1", "PTV1");

                        // Bladder crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Bladder", ctv1Id },
                            OutputStructure = "Bladder"
                        });

                        // Rectum crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Rectum", ctv1Id },
                            OutputStructure = "Rectum"
                        });

                        // Sigma crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Sigma", ctv1Id },
                            OutputStructure = "Sigma"
                        });

                        // Generate rectal wall
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.RectalWall,
                            OutputStructure = "RectalWall_Ph"
                        });

                        // Generate OAR structures for CTV and PTV crops
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Bowel", "Sigma", "Rectum", "Bladder" },
                            OutputStructure = "Bowel+Sigma+Rectum+Bladder_Ph"
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Bowel", "Sigma" },
                            OutputStructure = "Bowel+Sigma_Ph"
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Bowel+Sigma+Rectum+Bladder_Ph" },
                            OutputStructure = "Bowel+Sigma+Rectum+Bladder+3mm_Ph",
                            MarginMm = 3.0
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Bowel+Sigma_Ph" },
                            OutputStructure = "Bowel+Sigma+3mm_Ph",
                            MarginMm = 3.0
                        });

                        // Generate PTV1
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.AsymmetricExpansion,
                            InputStructures = new List<string> { ctv1Id },
                            OutputStructure = ptv1Id,
                            AsymmetricMarginsMm = new double[] { 5.0, 5.0, 5.0, 5.0, 3.0, 5.0 }
                        });

                        // CTV1 crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ctv1Id, "Bowel+Sigma+Rectum+Bladder+3mm_Ph" },
                            OutputStructure = ctv1Id + "_crop_Ph"
                        });

                        // PTV1 crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv1Id, "Bowel+Sigma+3mm_Ph" },
                            OutputStructure = ptv1Id + "_crop_Ph"
                        });

                        // PTV1-CTV1 generation
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv1Id, ctv1Id },
                            OutputStructure = ptv1Id + "-" + ctv1Id + "_Ph"
                        });

                        break;
                    }

                case "Prostate 3 dose levels":
                    {
                        // Select CTV1
                        string ctv1Id = SelectCTV1(selectedPlan);
                        string ptv1Id = ctv1Id.Replace("CTV1", "PTV1");
                        string ctv2Id = ctv1Id.Replace("CTV1", "CTV2");
                        string ptv2Id = ctv2Id.Replace("CTV2", "PTV2");

                        // Bladder crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Bladder", ctv1Id },
                            OutputStructure = "Bladder"
                        });

                        // Rectum crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Rectum", ctv1Id },
                            OutputStructure = "Rectum"
                        });

                        // Sigma crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { "Sigma", ctv1Id },
                            OutputStructure = "Sigma"
                        });

                        // Generate rectal wall
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.RectalWall,
                            OutputStructure = "RectalWall_Ph"
                        });

                        // Generate OAR structures for CTV and PTV crops
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Bowel", "Sigma", "Rectum", "Bladder" },
                            OutputStructure = "Bowel+Sigma+Rectum+Bladder_Ph"
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Bowel", "Sigma" },
                            OutputStructure = "Bowel+Sigma_Ph"
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Bowel+Sigma+Rectum+Bladder_Ph" },
                            OutputStructure = "Bowel+Sigma+Rectum+Bladder+3mm_Ph",
                            MarginMm = 3.0
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Bowel+Sigma_Ph" },
                            OutputStructure = "Bowel+Sigma+3mm_Ph",
                            MarginMm = 3.0
                        });

                        // Generate PTV1
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.AsymmetricExpansion,
                            InputStructures = new List<string> { ctv1Id },
                            OutputStructure = ptv1Id,
                            AsymmetricMarginsMm = new double[] { 5.0, 5.0, 5.0, 5.0, 3.0, 5.0 }
                        });

                        // Generate PTV2
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.AsymmetricExpansion,
                            InputStructures = new List<string> { ctv2Id },
                            OutputStructure = ptv2Id,
                            AsymmetricMarginsMm = new double[] { 5.0, 5.0, 5.0, 5.0, 3.0, 5.0 }
                        });

                        // CTV1 crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ctv1Id, "Bowel+Sigma+Rectum+Bladder+3mm_Ph" },
                            OutputStructure = ctv1Id + "_crop_Ph"
                        });

                        // PTV1 crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv1Id, "Bowel+Sigma+3mm_Ph" },
                            OutputStructure = ptv1Id + "_crop_Ph"
                        });

                        // PTV1-CTV1 generation
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv1Id, ctv1Id },
                            OutputStructure = ptv1Id + "-" + ctv1Id + "_Ph"
                        });

                        // PTV2 crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv2Id, "Bowel+Sigma_Ph" },
                            OutputStructure = ptv2Id + "_crop_Ph"
                        });

                        // PTV2-PTV1 generation
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptv2Id, ptv1Id },
                            OutputStructure = ptv2Id + "-" + ptv1Id + "_Ph"
                        });

                        /*// PTV2 - PTV1 clean up
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.MorphologicalOpening,
                            InputStructures = new List<string> { ptv2Id + "-" + ptv1Id + "_Ph" },
                            OutputStructure = ptv2Id + "-" + ptv1Id + "_Ph_clean",
                            MarginMm = 3.0
                        });*/

                        break;
                    }

                case "Lymph node(s) 2 x 5 Gy":
                    {
                        if (!AddLymphNodeTargetsToRuleSet(selectedPlan, ruleSet, ptvMarginMm: 5.0, out string gtvId, out string ptvId))
                            return;

                        // Bowel, sigma, and rectum union
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Bowel", "Sigma", "Rectum" },
                            OutputStructure = "Bowel+Sigma+Rectum_Ph"
                        });

                        // 2 mm expansion of bowel, sigma, and rectum
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Bowel+Sigma+Rectum_Ph" },
                            OutputStructure = "Bowel+Sigma+Rectum+2mm_Ph",
                            MarginMm = 2.0
                        });

                        // GTV crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { gtvId, "Bowel+Sigma+Rectum+2mm_Ph" },
                            OutputStructure = gtvId.Substring(0, gtvId.Length - 3) + "_crop_Ph"
                        });

                        // PTV crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptvId, "Bowel+Sigma+Rectum_Ph" },
                            OutputStructure = ptvId.Substring(0, ptvId.Length - 3) + "_crop_Ph"
                        });

                        break;
                    }

                case "Lymph node(s) 5 x 7 Gy":
                    {
                        if (!AddLymphNodeTargetsToRuleSet(selectedPlan, ruleSet, ptvMarginMm: 5.0, out string gtvId, out string ptvId))
                            return;

                        // Sigma and bowel union
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Addition,
                            InputStructures = new List<string> { "Sigma", "Bowel" },
                            OutputStructure = "Sigma+Bowel_Ph"
                        });

                        // 2 mm and 4 mm expansions of sigma and bowel
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Sigma+Bowel_Ph" },
                            OutputStructure = "Sigma+Bowel+2mm_Ph",
                            MarginMm = 2.0
                        });
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Sigma+Bowel_Ph" },
                            OutputStructure = "Sigma+Bowel+4mm_Ph",
                            MarginMm = 4.0
                        });

                        // 2 mm expansion of rectum
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Expansion,
                            InputStructures = new List<string> { "Rectum" },
                            OutputStructure = "Rectum+2mm_Ph",
                            MarginMm = 2.0
                        });

                        // GTV crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { gtvId, "Sigma+Bowel+4mm_Ph", "Rectum+2mm_Ph" },
                            OutputStructure = gtvId.Substring(0, gtvId.Length - 3) + "_crop_Ph"
                        });

                        // PTV crop
                        ruleSet.Rules.Add(new StructureRule
                        {
                            Type = RuleType.Subtraction,
                            InputStructures = new List<string> { ptvId, "Sigma+Bowel+2mm_Ph", "Rectum" },
                            OutputStructure = ptvId.Substring(0, ptvId.Length - 3) + "_crop_Ph"
                        });

                        break;
                    }

                default:
                    MessageBox.Show("Unknown template: " + selectedTemplate);
                    return;
            }

            SaveRulesForPlan(selectedPlan, ruleSet);
            MessageBox.Show(
                overwriteExistingRules
                    ? "Rules from template '" + selectedTemplate + "' were created (existing rules were overwritten)."
                    : "Rules from template '" + selectedTemplate + "' were appended to existing rules.");
        }

    }
}
