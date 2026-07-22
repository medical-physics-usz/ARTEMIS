using System;

namespace USZ_ARTEMIS.Core.Rules
{
    public static class RuleStructureResolutionPolicy
    {
        private static readonly string[] ExactStructureIds =
        {
            "Bowel",
            "Sigma",
            "Rectum",
            "Bladder"
        };

        private static readonly string[] StructureIdPrefixes =
        {
            "GTV",
            "ITV",
            "PTV"
        };

        public static bool RequiresHighResolution(string structureId)
        {
            if (string.IsNullOrWhiteSpace(structureId))
            {
                return false;
            }

            foreach (string exactId in ExactStructureIds)
            {
                if (structureId.Equals(exactId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (string prefix in StructureIdPrefixes)
            {
                if (structureId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
