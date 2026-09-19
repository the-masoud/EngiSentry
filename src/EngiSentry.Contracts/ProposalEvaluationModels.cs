using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Proposal-layer evaluation states (Phase A architecture E.3). These map onto the existing accepted
    /// decision vocabulary rather than replacing it, and live on NEW proposal types only. They are never
    /// written into <see cref="EngineeringDistanceRuleEvaluation"/>, QaRuleResult or QaReport status, whose
    /// accepted PASS / FINDINGS / INSUFFICIENT_EVIDENCE / REVIEW_REQUIRED / FAIL set is unchanged.
    /// </summary>
    public static class ProposalConstraintState
    {
        /// <summary>Maps from <see cref="StandardDecisionStates.Pass"/>.</summary>
        public const string Satisfied = "SATISFIED";
        /// <summary>Maps from <see cref="StandardDecisionStates.Fail"/>.</summary>
        public const string Violated = "VIOLATED";
        /// <summary>Maps from <see cref="StandardDecisionStates.InsufficientEvidence"/>; never collapses to SATISFIED.</summary>
        public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";
        /// <summary>Maps from <see cref="StandardDecisionStates.NotApplicable"/>.</summary>
        public const string NotApplicable = "NOT_APPLICABLE";
        /// <summary>Maps from <see cref="StandardDecisionStates.ExpertReviewRequired"/> / SourceQuarantined / StandardConflict.</summary>
        public const string ReviewRequired = "REVIEW_REQUIRED";

        public static bool IsSupported(string value) =>
            value == Satisfied || value == Violated || value == InsufficientEvidence
            || value == NotApplicable || value == ReviewRequired;
    }

    /// <summary>
    /// The re-evaluated result of one bounded constraint at one candidate position. Phase B establishes the
    /// contract only; no re-evaluation engine is implemented in this phase.
    /// </summary>
    [DataContract]
    public sealed class PlacementProposalConstraintOutcome
    {
        [DataMember(Order = 1)] public string ConstraintId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ConstraintTreatment { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string MeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? SubjectId { get; set; }

        /// <summary>Measured distance at the candidate position, or null when the state is
        /// INSUFFICIENT_EVIDENCE / REVIEW_REQUIRED / NOT_APPLICABLE.</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public double? ActualDistanceMeters { get; set; }

        [DataMember(Order = 8)] public decimal RequiredDistanceMeters { get; set; }

        /// <summary>One of the <see cref="ProposalConstraintState"/> values.</summary>
        [DataMember(Order = 9)] public string EvaluationState { get; set; } = string.Empty;

        [DataMember(Order = 10, EmitDefaultValue = false)] public string? Reason { get; set; }

        /// <summary>Portable evidence handles backing the outcome, Ordinal-ordered.</summary>
        [DataMember(Order = 11)] public List<string> EvidenceHandles { get; set; } = new List<string>();
    }
}
