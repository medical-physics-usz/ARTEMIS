using System;

namespace USZ_ARTEMIS.Core.Planning
{
    public static class PtvContainmentSelection
    {
        public static bool IsCandidate(string structureId, string dicomType, bool isEmpty)
        {
            return !isEmpty &&
                !string.IsNullOrWhiteSpace(structureId) &&
                structureId.StartsWith("PTV", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(dicomType) &&
                dicomType.Equals("PTV", StringComparison.OrdinalIgnoreCase);
        }
    }
}
