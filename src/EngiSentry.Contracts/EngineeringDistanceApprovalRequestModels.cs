using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceApprovalRequestEvidenceBinding
    {
        [DataMember(Order = 1)] public string ClauseId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SourceSha256 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalRequest
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public string RequestId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string PackId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string MeasurementKind { get; set; } = string.Empty;
        [DataMember(Order = 8)] public decimal ProposedMinimumDistanceMeters { get; set; }
        [DataMember(Order = 9)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string CandidateDefinitionSha256 { get; set; } = string.Empty;
        [DataMember(Order = 12)] public List<string> ClauseIds { get; set; } = new List<string>();
        [DataMember(Order = 13)] public List<EngineeringDistanceApprovalRequestEvidenceBinding> EvidenceBindings { get; set; } = new List<EngineeringDistanceApprovalRequestEvidenceBinding>();
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? GeneratedFromDrawing { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? GeneratedFromProjectIdentity { get; set; }
        [DataMember(Order = 16)] public DateTime CreatedAtUtc { get; set; }
        [DataMember(Order = 17)] public bool IsApproval { get; set; }
        [DataMember(Order = 18)] public bool GrantsAuthority { get; set; }
        [DataMember(Order = 19)] public bool AutoExecutionAllowed { get; set; }
    }

    [DataContract]
    public sealed class EngineeringDistanceAuthorityPolicyRequest
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public string RequestId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ExpectedApprovalRecordId { get; set; } = "REQUIRED_AFTER_EXTERNAL_APPROVAL";
        [DataMember(Order = 7)] public string ExpectedApprovalReference { get; set; } = "REQUIRED_FROM_AUTHORIZED_APPROVAL";
        [DataMember(Order = 8)] public string AuthorityBasisReference { get; set; } = "REQUIRED_FROM_AUTHORIZED_AUTHORITY";
        [DataMember(Order = 9)] public string RequestedEnforcementMode { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string LegacyConflictDisposition { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string Rationale { get; set; } = string.Empty;
        [DataMember(Order = 12)] public bool IsSigned { get; set; }
        [DataMember(Order = 13)] public bool IsTrusted { get; set; }
        [DataMember(Order = 14)] public bool IsActive { get; set; }
        [DataMember(Order = 15)] public bool GrantsAuthority { get; set; }
    }
}
