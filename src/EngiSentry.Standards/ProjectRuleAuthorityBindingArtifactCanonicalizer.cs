using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Derives the canonical unsigned bytes a <see cref="ProjectRuleAuthorityBindingArtifact"/> signature covers, and
    /// the content-derived artifact identity. This mirrors the accepted
    /// <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer"/> convention exactly, so the binding
    /// artifact inherits the same tamper-evidence: the issuer, key, issuance time, algorithm, revocation time and the
    /// entire carried binding are all inside the signed envelope. Any change to the binding — including retargeting its
    /// ProjectDirectory or swapping the AuthorityRuleId — changes the payload bytes and invalidates the signature.
    /// </summary>
    public static class ProjectRuleAuthorityBindingArtifactCanonicalizer
    {
        [DataContract] private sealed class UnsignedPayload
        {
            [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
            [DataMember(Order = 2)] public string IssuerId { get; set; } = string.Empty;
            [DataMember(Order = 3)] public string KeyId { get; set; } = string.Empty;
            [DataMember(Order = 4)] public DateTime IssuedAtUtc { get; set; }
            [DataMember(Order = 5)] public string SignatureAlgorithm { get; set; } = string.Empty;
            [DataMember(Order = 6)] public ProjectRuleAuthorityBinding Binding { get; set; } = new ProjectRuleAuthorityBinding();
            [DataMember(Order = 7, EmitDefaultValue = false)] public DateTime? RevokedAtUtc { get; set; }
        }

        public static byte[] CreateUnsignedPayloadBytes(ProjectRuleAuthorityBindingArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            ProjectRuleAuthorityBinding binding = artifact.Binding ?? throw new ArgumentException("Binding is required", nameof(artifact));
            var payload = new UnsignedPayload
            {
                SchemaVersion = artifact.SchemaVersion,
                IssuerId = artifact.IssuerId,
                KeyId = artifact.KeyId,
                IssuedAtUtc = artifact.IssuedAtUtc,
                SignatureAlgorithm = artifact.SignatureAlgorithm,
                Binding = Copy(binding),
                RevokedAtUtc = artifact.RevokedAtUtc
            };
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(UnsignedPayload)).WriteObject(stream, payload); return stream.ToArray(); }
        }

        public static string ComputeBindingArtifactId(ProjectRuleAuthorityBindingArtifact artifact)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(CreateUnsignedPayloadBytes(artifact))).Replace("-", string.Empty);
        }

        internal static ProjectRuleAuthorityBinding Copy(ProjectRuleAuthorityBinding x) => new ProjectRuleAuthorityBinding
        {
            ProjectRuleId = x.ProjectRuleId,
            AuthorityRuleId = x.AuthorityRuleId,
            ClauseIds = x.ClauseIds == null ? new List<string>() : new List<string>(x.ClauseIds),
            PolicyId = x.PolicyId,
            SourceId = x.SourceId,
            SourceSha256 = x.SourceSha256,
            AuthorityClass = x.AuthorityClass,
            EffectiveEnforcementMode = x.EffectiveEnforcementMode,
            ApprovalRecordId = x.ApprovalRecordId,
            AuthorityState = x.AuthorityState,
            Rationale = x.Rationale,
            ProjectDirectory = x.ProjectDirectory
        };

        internal static ProjectRuleAuthorityBindingArtifact CopyArtifact(ProjectRuleAuthorityBindingArtifact x) => new ProjectRuleAuthorityBindingArtifact
        {
            SchemaVersion = x.SchemaVersion,
            BindingArtifactId = x.BindingArtifactId,
            IssuerId = x.IssuerId,
            KeyId = x.KeyId,
            IssuedAtUtc = x.IssuedAtUtc,
            SignatureAlgorithm = x.SignatureAlgorithm,
            Binding = x.Binding == null ? null : Copy(x.Binding),
            RevokedAtUtc = x.RevokedAtUtc,
            SignatureBase64 = x.SignatureBase64
        };
    }
}
