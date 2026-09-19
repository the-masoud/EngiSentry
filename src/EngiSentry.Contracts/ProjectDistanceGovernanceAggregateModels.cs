using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class ProjectDistanceGovernanceAggregateResult
    {
        [DataMember(Order = 1)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<ProjectDistanceGovernanceAggregateItem> Items { get; set; } = new List<ProjectDistanceGovernanceAggregateItem>();
    }

    [DataContract]
    public sealed class ProjectDistanceGovernanceAggregateItem
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string CandidateRuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProjectRuleId { get; set; } = string.Empty;
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? ApprovalRecordId { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? HistoryState { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? CurrentnessState { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? AuthorityState { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? VerifiedPolicyArtifactId { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? VerifiedPolicyIssuerId { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? VerifiedPolicyKeyId { get; set; }
        [DataMember(Order = 11)] public ProjectDistanceGovernedRuleResolution Resolution { get; set; } = new ProjectDistanceGovernedRuleResolution();
    }
}
