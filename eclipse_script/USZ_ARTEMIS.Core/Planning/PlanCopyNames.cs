namespace USZ_ARTEMIS.Core.Planning
{
    public sealed class PlanCopyNames
    {
        private const int MaximumPlanIdLength = 13;

        private PlanCopyNames(string id, string name, bool wasShortened)
        {
            Id = id;
            Name = name;
            WasShortened = wasShortened;
        }

        public string Id { get; }

        public string Name { get; }

        public bool WasShortened { get; }

        public static PlanCopyNames Create(
            string originalPlanId,
            string originalPlanName,
            string fractionSuffix)
        {
            string copiedPlanId = originalPlanId + fractionSuffix;
            string copiedPlanName = originalPlanName + fractionSuffix;
            bool wasShortened = copiedPlanId.Length > MaximumPlanIdLength;

            if (wasShortened)
            {
                copiedPlanId = originalPlanId.Remove(2, 3) + fractionSuffix;

                if (originalPlanName != null && originalPlanName.Length >= 7)
                {
                    copiedPlanName = originalPlanName.Remove(4, 3) + fractionSuffix;
                }
            }

            return new PlanCopyNames(copiedPlanId, copiedPlanName, wasShortened);
        }
    }
}
