using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class EngineeringDistanceApprovalQuarantineStates
    {
        public const string NoReturnedArtifacts = "NO_RETURNED_ARTIFACTS";
        public const string Quarantined = "QUARANTINED";
        public const string Rejected = "REJECTED";
        public const string PendingTrust = "PENDING_TRUST";
        public const string ReadyForAuthorizedInstallation = "READY_FOR_AUTHORIZED_INSTALLATION";
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalQuarantineFile
    {
        [DataMember(Order = 1)] public string OriginalFileName { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RelativePath { get; set; } = string.Empty;
        [DataMember(Order = 3)] public long Length { get; set; }
        [DataMember(Order = 4)] public string Sha256 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ArtifactType { get; set; } = string.Empty;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? CandidateId { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? RuleId { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? ApprovalRecordId { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? PolicyArtifactId { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? IssuerId { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? KeyId { get; set; }
        [DataMember(Order = 12)] public string ValidationStatus { get; set; } = string.Empty;
        [DataMember(Order = 13)] public List<string> Diagnostics { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalQuarantineManifest
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public string QuarantineId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SourcePackageId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string SourcePackageDirectory { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ExpectedProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 6)] public DateTime IntakeTimestampUtc { get; set; }
        [DataMember(Order = 7)] public List<EngineeringDistanceApprovalQuarantineFile> Files { get; set; } = new List<EngineeringDistanceApprovalQuarantineFile>();
        [DataMember(Order = 8)] public string HandoffVerificationStatus { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string OverallStatus { get; set; } = EngineeringDistanceApprovalQuarantineStates.Rejected;
        [DataMember(Order = 10)] public List<string> Diagnostics { get; set; } = new List<string>();
        [DataMember(Order = 11)] public string HandoffDirectory { get; set; } = string.Empty;
        [DataMember(Order = 12)] public EngineeringDistanceApprovalQuarantineCandidateResult Road { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
        [DataMember(Order = 13)] public EngineeringDistanceApprovalQuarantineCandidateResult Fence { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalQuarantineResult
    {
        [DataMember(Order = 1)] public string Status { get; set; } = EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts;
        [DataMember(Order = 2)] public string SourcePackageId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string QuarantineId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string QuarantineDirectory { get; set; } = string.Empty;
        [DataMember(Order = 5)] public int ReturnedFileCount { get; set; }
        [DataMember(Order = 6)] public int QuarantinedFileCount { get; set; }
        [DataMember(Order = 7)] public List<EngineeringDistanceApprovalQuarantineFile> Files { get; set; } = new List<EngineeringDistanceApprovalQuarantineFile>();
        [DataMember(Order = 8)] public List<string> Diagnostics { get; set; } = new List<string>();
        [DataMember(Order = 9)] public bool ProductionInstallationPerformed { get; set; }
        [DataMember(Order = 10)] public bool RuleActivationPerformed { get; set; }
        [DataMember(Order = 11)] public EngineeringDistanceApprovalQuarantineCandidateResult Road { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
        [DataMember(Order = 12)] public EngineeringDistanceApprovalQuarantineCandidateResult Fence { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalQuarantineCandidateResult
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public bool ApprovalPresent { get; set; }
        [DataMember(Order = 4)] public string ApprovalValidationStatus { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? ApprovalRecordId { get; set; }
        [DataMember(Order = 6)] public bool PolicyPresent { get; set; }
        [DataMember(Order = 7)] public string PolicyValidationStatus { get; set; } = string.Empty;
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? PolicyArtifactId { get; set; }
        [DataMember(Order = 9)] public string TrustStatus { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string CandidateState { get; set; } = EngineeringDistanceApprovalQuarantineStates.Quarantined;
        [DataMember(Order = 11)] public List<string> Diagnostics { get; set; } = new List<string>();
    }
}
