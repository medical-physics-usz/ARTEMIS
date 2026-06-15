using System.Windows;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS
{
    public partial class StartPage
    {
        private void BtnCreateRule_Click(object sender, RoutedEventArgs e)
        {
            string status = context.PlanSetup.ApprovalStatusAsString;
            if (status == "Unapproved")
            {
                string ruleType = Actions.Rules.SelectRuleType();

                if (ruleType == "Expand") { Actions.Rules.CreateExpansion(GetSelectedPlan()); }
                if (ruleType == "Subtract") { Actions.Rules.CreateSubtraction(GetSelectedPlan()); }
                if (ruleType == "Add") { Actions.Rules.CreateAddition(GetSelectedPlan()); }
                if (ruleType == "Intersect") { Actions.Rules.CreateIntersection(GetSelectedPlan()); }
                if (ruleType == "Create SBRT ring") { Actions.Rules.CreateSbrtRing(GetSelectedPlan()); }
                if (ruleType == "Create RectalWall") { Actions.Rules.CreateRectalWall(GetSelectedPlan()); }
            }
            else
            {
                MessageBox.Show($"Plan status: {status}\n\n Unapprove the plan to modify the rules", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnInspectRules_Click(object sender, RoutedEventArgs e)
        {
            Actions.Rules.ViewRules(GetSelectedPlan());
        }

        private void BtnApplyRules_Click(object sender, RoutedEventArgs e)
        {
            string status = context.PlanSetup.ApprovalStatusAsString;
            if (status == "Unapproved")
            {
                PlanSetup selectedPlan = GetSelectedPlan();
                string rulesFilePath = Actions.Rules.RetrieveRulesFile(selectedPlan);
                Actions.Rules.ApplyRules(selectedPlan, rulesFilePath);
            }
            else
            {
                MessageBox.Show($"Plan status: {status}\n\n Unapprove the plan to apply the rules", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditRules_Click(object sender, RoutedEventArgs e)
        {
            string status = context.PlanSetup.ApprovalStatusAsString;
            if (status == "Unapproved")
            {
                Actions.Rules.EditRules(GetSelectedPlan());
            }
            else
            {
                MessageBox.Show($"Plan status: {status}\n\n Unapprove the plan to modify the rules", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCreateRulesFromTemplate_Click(object sender, RoutedEventArgs e)
        {
            string status = context.PlanSetup.ApprovalStatusAsString;
            if (status == "Unapproved")
            {
                Actions.Rules.CreateRulesFromTemplate(GetSelectedPlan());
            }
            else
            {
                MessageBox.Show($"Plan status: {status}\n\n Unapprove the plan to modify the rules", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
