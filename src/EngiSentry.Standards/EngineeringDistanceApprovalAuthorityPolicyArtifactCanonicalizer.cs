using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer
    {
        [DataContract] private sealed class UnsignedPayload
        {
            [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string IssuerId { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string KeyId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public DateTime IssuedAtUtc { get; set; }
            [DataMember(Order = 5)] public string SignatureAlgorithm { get; set; } = string.Empty;
            [DataMember(Order = 6)] public EngineeringDistanceApprovalAuthorityPolicyDefinition Policy { get; set; } = new EngineeringDistanceApprovalAuthorityPolicyDefinition();
        }

        public static byte[] CreateUnsignedPayloadBytes(EngineeringDistanceApprovalAuthorityPolicyArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            EngineeringDistanceApprovalAuthorityPolicyDefinition policy = artifact.Policy ?? throw new ArgumentException("Policy is required", nameof(artifact));
            var payload = new UnsignedPayload { SchemaVersion = artifact.SchemaVersion, IssuerId = artifact.IssuerId, KeyId = artifact.KeyId, IssuedAtUtc = artifact.IssuedAtUtc, SignatureAlgorithm = artifact.SignatureAlgorithm, Policy = Copy(policy) };
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(UnsignedPayload)).WriteObject(stream, payload); return stream.ToArray(); }
        }
        public static string ComputePolicyArtifactId(EngineeringDistanceApprovalAuthorityPolicyArtifact artifact) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(CreateUnsignedPayloadBytes(artifact))).Replace("-", string.Empty); }
        internal static EngineeringDistanceApprovalAuthorityPolicyDefinition Copy(EngineeringDistanceApprovalAuthorityPolicyDefinition x) => new EngineeringDistanceApprovalAuthorityPolicyDefinition { PolicyId = x.PolicyId, ProjectDirectory = x.ProjectDirectory, CandidateId = x.CandidateId, ApprovalRecordId = x.ApprovalRecordId, ApprovalReference = x.ApprovalReference, AuthorityBasisReference = x.AuthorityBasisReference, EnforcementMode = x.EnforcementMode, LegacyConflictDisposition = x.LegacyConflictDisposition, Rationale = x.Rationale };
    }
}
