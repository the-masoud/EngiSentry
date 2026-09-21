using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffFile
    {
        [DataMember(Order = 1)] public string RelativePath { get; set; } = string.Empty;
        [DataMember(Order = 2)] public long Length { get; set; }
        [DataMember(Order = 3)] public string Sha256 { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffCandidate
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal CandidateValueMeters { get; set; }
        [DataMember(Order = 4)] public decimal ConfiguredCriterionMeters { get; set; }
        [DataMember(Order = 5)] public string CandidateDefinitionSha256 { get; set; } = string.Empty;
        [DataMember(Order = 6)] public List<string> ClauseIds { get; set; } = new List<string>();
        [DataMember(Order = 7)] public List<string> SourceIds { get; set; } = new List<string>();
        [DataMember(Order = 8)] public List<string> SourceSha256 { get; set; } = new List<string>();
        [DataMember(Order = 9)] public int ApprovalArtifactCount { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? ActiveApprovalRecordId { get; set; }
        [DataMember(Order = 11)] public string ApprovalCurrentness { get; set; } = string.Empty;
        [DataMember(Order = 12)] public bool TrustedIssuerPresent { get; set; }
        [DataMember(Order = 13)] public bool VerifiedAuthorityPolicyPresent { get; set; }
        [DataMember(Order = 14)] public string GovernanceState { get; set; } = string.Empty;
        [DataMember(Order = 15)] public bool AutoExecutionAllowed { get; set; }
        [DataMember(Order = 16)] public List<string> BlockingReasons { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffSafetyFlags
    {
        [DataMember(Order = 1)] public bool IsApproval { get; set; }
        [DataMember(Order = 2)] public bool ContainsSignedAuthorityPolicy { get; set; }
        [DataMember(Order = 3)] public bool ContainsTrustedIssuer { get; set; }
        [DataMember(Order = 4)] public bool GrantsAuthority { get; set; }
        [DataMember(Order = 5)] public bool ActivatesRules { get; set; }
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffReadinessSnapshot
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public List<EngineeringDistanceApprovalHandoffCandidate> Candidates { get; set; } = new List<EngineeringDistanceApprovalHandoffCandidate>();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffManifest
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public string PackageId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? DrawingIdentity { get; set; }
        [DataMember(Order = 6)] public DateTime CreatedAtUtc { get; set; }
        [DataMember(Order = 7)] public string RoadCandidateId { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string FenceCandidateId { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string RoadApprovalRequestId { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string FenceApprovalRequestId { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string RoadPolicyRequestId { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string FencePolicyRequestId { get; set; } = string.Empty;
        [DataMember(Order = 13)] public EngineeringDistanceApprovalHandoffCandidate Road { get; set; } = new EngineeringDistanceApprovalHandoffCandidate();
        [DataMember(Order = 14)] public EngineeringDistanceApprovalHandoffCandidate Fence { get; set; } = new EngineeringDistanceApprovalHandoffCandidate();
        [DataMember(Order = 15)] public List<EngineeringDistanceApprovalHandoffFile> Files { get; set; } = new List<EngineeringDistanceApprovalHandoffFile>();
        [DataMember(Order = 16)] public EngineeringDistanceApprovalHandoffSafetyFlags Safety { get; set; } = new EngineeringDistanceApprovalHandoffSafetyFlags();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHandoffVerificationResult
    {
        [DataMember(Order = 1)] public string Status { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<string> Diagnostics { get; set; } = new List<string>();
        [DataMember(Order = 3)] public bool PayloadHashesValid { get; set; }
        [DataMember(Order = 4)] public bool ProductionAuthorityMaterialPresent { get; set; }
    }
}
