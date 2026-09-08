using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class StandardAuthorityClasses
    {
        public const string GoverningProject = "GOVERNING_PROJECT";
        public const string ActivePrimary = "ACTIVE_PRIMARY";
        public const string ActiveSupporting = "ACTIVE_SUPPORTING";
        public const string EngineeringGuidance = "ENGINEERING_GUIDANCE";
        public const string LegacyConflict = "LEGACY_CONFLICT";
        public const string Quarantined = "QUARANTINED";
    }

    public static class StandardEnforcementModes
    {
        public const string Deterministic = "DETERMINISTIC";
        public const string ConditionalReview = "CONDITIONAL_REVIEW";
        public const string Advisory = "ADVISORY";
        public const string NeverAutoEnforce = "NEVER_AUTO_ENFORCE";
    }

    public static class StandardDecisionStates
    {
        public const string Pass = "PASS";
        public const string Fail = "FAIL";
        public const string Advisory = "ADVISORY";
        public const string ExpertReviewRequired = "EXPERT_REVIEW_REQUIRED";
        public const string StandardConflict = "STANDARD_CONFLICT";
        public const string NotApplicable = "NOT_APPLICABLE";
        public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";
        public const string SourceQuarantined = "SOURCE_QUARANTINED";
    }

    [DataContract]
    public sealed class StandardAuthorityPolicyDefinition
    {
        [DataMember(Order = 1)]
        public string PolicyId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string SourceId { get; set; } = string.Empty;

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public string? ClauseId { get; set; }

        [DataMember(Order = 4)]
        public string AuthorityClass { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string EnforcementMode { get; set; } = string.Empty;

        [DataMember(Order = 6)]
        public string Rationale { get; set; } = string.Empty;
    }
}
