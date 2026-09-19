using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class EngineeringDistanceApprovalLegacyConflictDispositions { public const string Unresolved = "UNRESOLVED"; public const string ResolvedByProjectDecision = "RESOLVED_BY_PROJECT_DECISION"; }
    public static class EngineeringDistanceApprovalAuthorityStates
    {
        public const string InactiveApproval="INACTIVE_APPROVAL", NotApproved="NOT_APPROVED", StaleApproval="STALE_APPROVAL", NoAuthorityPolicy="NO_AUTHORITY_POLICY", AuthorityPolicyMismatch="AUTHORITY_POLICY_MISMATCH", LegacyConflictUnresolved="LEGACY_CONFLICT_UNRESOLVED", SourceQuarantined="SOURCE_QUARANTINED", GoverningProjectConditional="GOVERNING_PROJECT_CONDITIONAL", GoverningProjectDeterministic="GOVERNING_PROJECT_DETERMINISTIC";
    }
    [DataContract] public sealed class EngineeringDistanceApprovalAuthorityPolicyDefinition
    {
        [DataMember(Order=1)] public string PolicyId { get; set; } = string.Empty;
        [DataMember(Order=2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order=3)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order=4)] public string ApprovalRecordId { get; set; } = string.Empty;
        [DataMember(Order=5)] public string ApprovalReference { get; set; } = string.Empty;
        [DataMember(Order=6)] public string AuthorityBasisReference { get; set; } = string.Empty;
        [DataMember(Order=7)] public string EnforcementMode { get; set; } = string.Empty;
        [DataMember(Order=8)] public string LegacyConflictDisposition { get; set; } = string.Empty;
        [DataMember(Order=9)] public string Rationale { get; set; } = string.Empty;
    }
    [DataContract] public sealed class EngineeringDistanceApprovalAuthorityResult
    {
        [DataMember(Order=1)] public string State { get; set; } = string.Empty;
        [DataMember(Order=2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order=3)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order=4)] public string ApprovalRecordId { get; set; } = string.Empty;
        [DataMember(Order=5,EmitDefaultValue=false)] public string? PolicyId { get; set; }
        [DataMember(Order=6,EmitDefaultValue=false)] public string? AuthorityBasisReference { get; set; }
        [DataMember(Order=7,EmitDefaultValue=false)] public string? AuthorityClass { get; set; }
        [DataMember(Order=8,EmitDefaultValue=false)] public string? EffectiveEnforcementMode { get; set; }
    }
}
