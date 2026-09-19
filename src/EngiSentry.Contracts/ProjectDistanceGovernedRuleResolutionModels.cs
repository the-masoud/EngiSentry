using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class ProjectDistanceGovernedRuleResolutionStates
    {
        public const string ProjectRuleOnly = "PROJECT_RULE_ONLY";
        public const string GovernedConditional = "GOVERNED_CONDITIONAL";
        public const string GovernedCompatible = "GOVERNED_COMPATIBLE";
        public const string ValueConflict = "VALUE_CONFLICT";
        public const string SemanticScopeConflict = "SEMANTIC_SCOPE_CONFLICT";
    }

    [DataContract]
    public sealed class ProjectDistanceGovernanceProvenance
    {
        [DataMember(Order = 1)] public string ProjectRuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string CandidateRuleId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string ApprovalRecordId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string PolicyId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string AuthorityBasisReference { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string AuthorityState { get; set; } = string.Empty;
        [DataMember(Order = 8)] public decimal ApprovedMinimumDistanceMeters { get; set; }
    }

    [DataContract]
    public sealed class ProjectDistanceGovernedRuleResolution
    {
        [DataMember(Order = 1)] public string State { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ProjectRuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string AuthorityState { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public ProjectDistanceGovernanceProvenance? GovernanceProvenance { get; set; }
    }
}
