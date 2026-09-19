using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceCandidateBinding
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PackId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string MeasurementKind { get; set; } = string.Empty;
        [DataMember(Order = 5)] public decimal ProposedMinimumDistanceMeters { get; set; }
        [DataMember(Order = 6)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 8)] public List<string> ClauseIds { get; set; } = new List<string>();
        [DataMember(Order = 9)] public string CandidateDefinitionSha256 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalEvidenceBinding
    {
        [DataMember(Order = 1)] public string ClauseId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SourceSha256 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalRecord
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ApprovalRecordId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 4)] public EngineeringDistanceCandidateBinding CandidateBinding { get; set; } = new EngineeringDistanceCandidateBinding();
        [DataMember(Order = 5)] public EngineeringDistanceApprovalDecision Decision { get; set; } = new EngineeringDistanceApprovalDecision();
        [DataMember(Order = 6)] public string ApproverDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? AuthenticatedApproverIdentity { get; set; }
        [DataMember(Order = 8)] public DateTime DecisionTimestampUtc { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? Comment { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? SupersedesApprovalRecordId { get; set; }
        [DataMember(Order = 11)] public List<EngineeringDistanceApprovalEvidenceBinding> EvidenceBindings { get; set; } = new List<EngineeringDistanceApprovalEvidenceBinding>();
    }
}
