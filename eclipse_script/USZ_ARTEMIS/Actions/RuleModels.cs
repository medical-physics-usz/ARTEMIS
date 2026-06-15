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
        MorphologicalOpening,
        SbrtRing,
        RectalWall
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
}
