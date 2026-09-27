using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Derives the canonical unsigned bytes a <see cref="StandardAuthorityPolicyArtifact"/> signature covers, and
    /// the content-derived artifact identity. This mirrors the accepted
    /// <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer"/> and
    /// <see cref="ProjectRuleAuthorityBindingArtifactCanonicalizer"/> conventions exactly, so the policy artifact
    /// inherits the same tamper-evidence: the issuer, key, issuance time, algorithm, revocation time, project
    /// scope and the entire carried policy are all inside the signed envelope. Any change to the policy —
    /// including retargeting its ProjectDirectory, swapping its SourceId or ClauseId, or changing its
    /// EnforcementMode — changes the payload bytes and invalidates the signature.
    /// </summary>
    public static class StandardAuthorityPolicyArtifactCanonicalizer
    {
        [DataContract] private sealed class UnsignedPayload
        {
            [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string IssuerId { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string KeyId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public DateTime IssuedAtUtc { get; set; }
            [DataMember(Order = 5)] public string SignatureAlgorithm { get; set; } = string.Empty;
            [DataMember(Order = 6)] public string ProjectDirectory { get; set; } = string.Empty;
            [DataMember(Order = 7)] public StandardAuthorityPolicyDefinition Policy { get; set; } = new StandardAuthorityPolicyDefinition();
            [DataMember(Order = 8, EmitDefaultValue = false)] public DateTime? RevokedAtUtc { get; set; }
        }

        public static byte[] CreateUnsignedPayloadBytes(StandardAuthorityPolicyArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            StandardAuthorityPolicyDefinition policy = artifact.Policy ?? throw new ArgumentException("Policy is required", nameof(artifact));
            var payload = new UnsignedPayload
            {
                SchemaVersion = artifact.SchemaVersion,
                IssuerId = artifact.IssuerId,
                KeyId = artifact.KeyId,
                IssuedAtUtc = artifact.IssuedAtUtc,
                SignatureAlgorithm = artifact.SignatureAlgorithm,
                ProjectDirectory = artifact.ProjectDirectory,
                Policy = Copy(policy),
                RevokedAtUtc = artifact.RevokedAtUtc
            };
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(UnsignedPayload)).WriteObject(stream, payload); return stream.ToArray(); }
        }

        public static string ComputePolicyArtifactId(StandardAuthorityPolicyArtifact artifact)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(CreateUnsignedPayloadBytes(artifact))).Replace("-", string.Empty);
        }

        internal static StandardAuthorityPolicyDefinition Copy(StandardAuthorityPolicyDefinition x) => new StandardAuthorityPolicyDefinition
        {
            PolicyId = x.PolicyId,
            SourceId = x.SourceId,
            ClauseId = x.ClauseId,
            AuthorityClass = x.AuthorityClass,
            EnforcementMode = x.EnforcementMode,
            Rationale = x.Rationale
        };

        internal static StandardAuthorityPolicyArtifact CopyArtifact(StandardAuthorityPolicyArtifact x) => new StandardAuthorityPolicyArtifact
        {
            SchemaVersion = x.SchemaVersion,
            PolicyArtifactId = x.PolicyArtifactId,
            ProjectDirectory = x.ProjectDirectory,
            IssuerId = x.IssuerId,
            KeyId = x.KeyId,
            IssuedAtUtc = x.IssuedAtUtc,
            RevokedAtUtc = x.RevokedAtUtc,
            SignatureAlgorithm = x.SignatureAlgorithm,
            Policy = x.Policy == null ? null : Copy(x.Policy),
            SignatureBase64 = x.SignatureBase64
        };
    }
}
