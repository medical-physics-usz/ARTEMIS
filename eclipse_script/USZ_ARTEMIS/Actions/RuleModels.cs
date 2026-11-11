using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace USZ_ARTEMIS.Actions
{
    public enum RuleType
    {
        Expansion,
        AsymmetricExpansion,
        Subtraction,
        Addition,
        Intersection,
        SbrtRing,
        RectalWall,
        Ctv1
    }

    public class StructureRule
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleType Type { get; set; }

        public List<string> InputStructures { get; set; } = new List<string>();
        public string OutputStructure { get; set; }
        public double? MarginMm { get; set; }
        public double[] AsymmetricMarginsMm { get; set; }
    }

    public class PlanRuleSet
    {
        public string PatientId { get; set; }
        public string CourseId { get; set; }
        public string PlanId { get; set; }
        public int Version { get; set; } = 1;
        public List<StructureRule> Rules { get; set; } = new List<StructureRule>();
    }

    public sealed class RuleApplicationResult
    {
        private RuleApplicationResult(bool succeeded, bool cancelled)
        {
            Succeeded = succeeded;
            Cancelled = cancelled;
        }

        public bool Succeeded { get; }
        public bool Cancelled { get; }

        public static RuleApplicationResult Success()
        {
            return new RuleApplicationResult(true, false);
        }

        public static RuleApplicationResult Failure()
        {
            return new RuleApplicationResult(false, false);
        }

        public static RuleApplicationResult Cancel()
        {
            return new RuleApplicationResult(false, true);
        }
    }
}
