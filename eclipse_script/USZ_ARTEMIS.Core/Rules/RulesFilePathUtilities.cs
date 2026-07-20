namespace USZ_ARTEMIS.Core.Rules
{
    public static class RulesFilePathUtilities
    {
        public static string CreateFileName(string patientId, string courseId, string rulesSourcePlanId)
        {
            return $"{patientId}_{courseId}_{rulesSourcePlanId}.json";
        }
    }
}
