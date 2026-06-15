using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using USZ_ARTEMIS.StructureCreation;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private Window ResolveShownOwnerWindow()
        {
            var owner = Window.GetWindow(this);
            if (owner != null) return owner;

            owner = System.Windows.Application.Current?.Windows?.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? System.Windows.Application.Current?.Windows?.OfType<Window>().FirstOrDefault(w => w.IsVisible);

            return owner;
        }

        private void BtnAIRStruct_Click(object sender, RoutedEventArgs e)
        {
            var plan = GetSelectedPlan();
            List<string> warnings;

            try
            {
                List<Structure> airStructures = StructureCreator.CreateAirStructures(plan.StructureSet, false, out warnings);
                if (warnings != null && warnings.Count > 0)
                {
                    foreach (string warning in warnings)
                    {
                        MessageBox.Show(warning, "Warning message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                if (airStructures != null && airStructures.Count > 0)
                {
                    MessageBox.Show("New Air override structure created");
                }
                else
                {
                    MessageBox.Show("No air found!");
                }
            }
            catch (Exception excp)
            {
                MessageBox.Show(excp.Message, "Error message", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnHDStruct_Click(object sender, RoutedEventArgs e)
        {
            List<string> warnings;
            var plan = GetSelectedPlan();
            List<Structure> ptvs = SelectPtvForRing(plan);

            if (ptvs == null)
            {
                MessageBox.Show("Please select a PTV");
                return;
            }

            try
            {
                StructureCreator.CreateHighDensityStructures(plan.StructureSet, false, out warnings, ptvs);
                if (warnings != null && warnings.Count > 0)
                {
                    foreach (string warning in warnings)
                    {
                        MessageBox.Show(warning, "Warning message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("No high density areas found inside body!");
                }
            }
            catch (Exception excp)
            {
                MessageBox.Show(excp.Message, "Error message", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private List<Structure> SelectPtvForRing(PlanSetup plan)
        {
            var allPtvs = plan.StructureSet.Structures
                .Where(s =>
                    !string.IsNullOrEmpty(s.DicomType) &&
                    s.DicomType.Equals("PTV", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Id)
                .ToList();

            if (allPtvs.Count == 0)
            {
                MessageBox.Show("No PTV structures were found in this plan.", "Select PTV",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return new List<Structure>();
            }

            var selectedPtv = new List<Structure>();
            var owner = ResolveShownOwnerWindow();
            if (owner != null)
            {
                new WindowInteropHelper(owner).EnsureHandle();
            }

            var dialog = new Window
            {
                Title = "Select the PTV",
                Width = 400,
                Height = 300,
                ResizeMode = ResizeMode.NoResize
            };

            if (owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var listBox = new ListBox
            {
                SelectionMode = SelectionMode.Single,
                DisplayMemberPath = "Id"
            };
            foreach (var ptv in allPtvs)
            {
                listBox.Items.Add(ptv);
            }

            Grid.SetRow(listBox, 0);
            grid.Children.Add(listBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(5, 0, 5, 0)
            };
            okButton.Click += (s, args) =>
            {
                if (listBox.SelectedItem == null)
                {
                    MessageBox.Show("Please select a PTV.", "Select PTV",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                selectedPtv.Add((Structure)listBox.SelectedItem);
                dialog.DialogResult = true;
                dialog.Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                Margin = new Thickness(5, 0, 5, 0)
            };
            cancelButton.Click += (s, args) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            bool? result = dialog.ShowDialog();
            if (result != true || selectedPtv.Count == 0)
            {
                return new List<Structure>();
            }

            return selectedPtv;
        }

        private void Btn2cmRing_Click(object sender, RoutedEventArgs e)
        {
            var plan = GetSelectedPlan();
            if (plan == null)
            {
                MessageBox.Show("No plan selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            List<Structure> ptvs = SelectPtvForRing(plan);
            if (ptvs == null || ptvs.Count == 0)
            {
                return;
            }

            List<string> warnings;
            try
            {
                StructureCreator.CreatePTVRing(plan.StructureSet, ptvs, false, out warnings, 20, 0);

                if (warnings == null || warnings.Count == 0)
                {
                    MessageBox.Show("PTV ring structure created for selected PTV(s).",
                        "2 cm ring",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    foreach (string warning in warnings)
                    {
                        MessageBox.Show(warning, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception excp)
            {
                MessageBox.Show(excp.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCouchStruct_Click(object sender, RoutedEventArgs e)
        {
            const string selectedCouchModel = "Exact_IGRT_Couch_Top_medium";
            List<string> warnings;

            try
            {
                StructureCreator.CreateCouchStructures(GetSelectedPlan().StructureSet, selectedCouchModel, true, out warnings);
                if (warnings == null || warnings.Count == 0)
                {
                    MessageBox.Show("New couch structures created");
                }
                else
                {
                    foreach (string warning in warnings)
                    {
                        MessageBox.Show(warning, "Warning message", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception excp)
            {
                MessageBox.Show(excp.Message, "Error message", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
