using System.Collections.Generic;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace USZ_ARTEMIS.Tools
{
    public class EvalClinicalGoals
    {
        //public int num;
        public string structureId;
        public string priority;
        public string objective;
        public double actualValue;
        public string evaluationResult;
        public double actualValueqa;
        public string evaluationResultqa;
        public double difference;
        public double percDiff;

        public EvalClinicalGoals(string structureId, string priority, string objective, double actualValue, string evaluationResult)
        {
            //this.num = num;
            this.structureId = structureId;
            this.priority = priority;
            this.objective = objective;
            this.actualValue = actualValue;
            this.evaluationResult = evaluationResult;
            this.actualValueqa = 0;
            this.evaluationResultqa = null;
            this.difference = 0;
            this.percDiff = 0;
        }

        public static List<EvalClinicalGoals> GetClinicalGoals(ScriptContext context, PlanSetup orgPlan, PlanSetup qaPlan)
        {
            List<EvalClinicalGoals> EvalCGList = new List<EvalClinicalGoals>();
            var CG_org = orgPlan.GetClinicalGoals();
            var CG_qa = qaPlan.GetClinicalGoals();
            List<ClinicalGoal> goals_orgPlan = null;
            List<ClinicalGoal> goals_qaPlan = null;
            if (CG_org != null & CG_qa != null)
            {
                goals_orgPlan = orgPlan.GetClinicalGoals().ToList();
                goals_qaPlan = qaPlan.GetClinicalGoals().ToList();
            }
            else
            {
                return null; //interrupt execution of the method
            }


            // Create a list of actual values of clinical goals and store actual value as a double
            string structureID = null;
            string priority = null;
            string objective = null;
            double actualValue = 0.0;
            string evaluationResult = null;
            double actualValueqa = 0.0;
            string evaluationResultqa = null;

            //List<double> ClinicalGoalActualValues = new List<double>();
            //var EvalCGList= new List<USZ_sCT_PSQA.Tools.EvalClinicalGoals>();

            // number clinical goal, unit, actualvalue, actualvalue qa, difference, difference%

            //loop clinical goals if exist and not N/A, extract all info clinical goals
            foreach (ClinicalGoal goal_orgPlan in goals_orgPlan)
            {
                if (goals_orgPlan != null)
                {
                    structureID = goal_orgPlan.StructureId;
                    priority = goal_orgPlan.Priority.ToString();
                    objective = goal_orgPlan.ObjectiveAsString;
                    actualValue = goal_orgPlan.ActualValue;
                    evaluationResult = goal_orgPlan.EvaluationResult.ToString();

                    EvalCGList.Add(new EvalClinicalGoals(structureID, priority, objective, actualValue, evaluationResult));
                }
            }

            // clinicalgoals

            //actualValueqa = goal_qaPlan.ActualValue;
            //unit_qa = goal_qaPlan.ObjectiveAsString;
            //evaluationResultqa = goal_qaPlan.EvaluationResult.ToString();

            int index_qa = 0;
            foreach (ClinicalGoal goal_qaPlan in goals_qaPlan)
            {
                if (goals_qaPlan != null)
                {
                    actualValueqa = goal_qaPlan.ActualValue;
                    EvalCGList[index_qa].actualValueqa = actualValueqa;
                    EvalCGList[index_qa].evaluationResultqa = evaluationResultqa;
                    index_qa++;
                }
            }

            double difference = 0.0;
            for (int i = 0; i < EvalCGList.Count; i++)
            {
                difference = EvalCGList[i].actualValueqa - EvalCGList[i].actualValue; //actualvalueqa - actualvalue
                EvalCGList[i].difference = difference;
            }


            double prescribedDose = USZ_ARTEMIS.Tools.ExtractData.GetPrescribedDose(context, orgPlan);

            for (int i = 0; i < EvalCGList.Count; i++)
            {
                if (EvalCGList[i].objective.EndsWith("%"))
                {
                    EvalCGList[i].percDiff = EvalCGList[i].difference;
                }


                else //if (unit.EndsWith("Gy") || unit.EndsWith("cc") ||  unit.EndsWith("cm3"))
                {
                    double difference_percent = ((EvalCGList[i].difference) / prescribedDose) * 100;

                    EvalCGList[i].percDiff = difference_percent;
                }
            }

            return EvalCGList;
        }
    }
}
