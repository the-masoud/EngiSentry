using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Constraint treatment layer (Phase A architecture E.2). How the design engine must treat a governed
    /// rule. Treatment is metadata recorded at pack time; it is never authority-bearing and is never
    /// inferred from clause text. Authority and executability remain exclusively properties of the existing
    /// P6→P9 authority gate (<see cref="StandardRuleExecutionGateDecision"/>), so authority and
    /// optimization priority are never conflated: relabelling a rule OPTIMIZATION_OBJECTIVE cannot make it
    /// executable, and relabelling it HARD_CONSTRAINT cannot grant it authority.
    /// </summary>
    public static class ConstraintTreatment
    {
        public const string HardConstraint = "HARD_CONSTRAINT";
        public const string SafetyConstraint = "SAFETY_CONSTRAINT";
        public const string ProjectRequirement = "PROJECT_REQUIREMENT";
        public const string EngineeringPreference = "ENGINEERING_PREFERENCE";
        public const string OptimizationObjective = "OPTIMIZATION_OBJECTIVE";
        public const string ReviewRequired = "REVIEW_REQUIRED";

        public static bool IsSupported(string value) =>
            value == HardConstraint || value == SafetyConstraint || value == ProjectRequirement
            || value == EngineeringPreference || value == OptimizationObjective || value == ReviewRequired;
    }

    /// <summary>
    /// One governed constraint as evaluated against PlantModel v1: the evaluation unit for CHECK and the
    /// guard unit for SUGGEST. Composed from existing accepted types only. Phase B establishes the contract
    /// only; no constraint evaluation engine is implemented in this phase.
    /// </summary>
    [DataContract]
    public sealed class BoundedConstraint
    {
        /// <summary>Deterministic identity, e.g. bounded-constraint:&lt;RuleId&gt;.</summary>
        [DataMember(Order = 1)] public string ConstraintId { get; set; } = string.Empty;

        /// <summary>The governed source rule this constraint is projected from (read-only view).</summary>
        [DataMember(Order = 2)] public StandardsPackRule? Rule { get; set; }

        [DataMember(Order = 3)] public string ConstraintTreatment { get; set; } = string.Empty;

        /// <summary>Existing accepted measurement kinds only: EquipmentToRoad2D / EquipmentToBoundary2D.</summary>
        [DataMember(Order = 4)] public string MeasurementKind { get; set; } = string.Empty;

        [DataMember(Order = 5)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 7)] public decimal MinimumDistanceMeters { get; set; }

        /// <summary>Unit context carried in the same shape the existing evaluation already uses.</summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? UnitContext { get; set; }
    }
}
