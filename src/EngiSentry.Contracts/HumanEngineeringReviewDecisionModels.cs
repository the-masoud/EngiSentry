using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class HumanEngineeringReviewDecisionStates
    {
        public const string ReviewAccepted = "REVIEW_ACCEPTED";
        public const string ReviewRejected = "REVIEW_REJECTED";
        public const string NeedsClarification = "NEEDS_CLARIFICATION";
    }

    [DataContract]
    public sealed class HumanEngineeringReviewEvidenceBinding
    {
        [DataMember(Order = 1)] public string ClauseId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SourceSha256 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class HumanEngineeringReviewDecisionRecord
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public string DecisionRecordId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 4)] public DateTime ScanTimestampUtc { get; set; }
        [DataMember(Order = 5)] public string ReviewPackageSha256 { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ReviewPackageSchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string AuthorityDecisionStateAtReview { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string EffectiveEnforcementModeAtReview { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string HumanReviewState { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string ReviewerDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? AuthenticatedReviewerIdentity { get; set; }
        [DataMember(Order = 13)] public DateTime DecisionTimestampUtc { get; set; }
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? Comment { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? SupersedesDecisionRecordId { get; set; }
        [DataMember(Order = 16)] public List<HumanEngineeringReviewEvidenceBinding> EvidenceBindings { get; set; } = new List<HumanEngineeringReviewEvidenceBinding>();
    }
}
