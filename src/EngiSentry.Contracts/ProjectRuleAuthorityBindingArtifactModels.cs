using System;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// The signed intake artifact for a <see cref="ProjectRuleAuthorityBinding"/>. This is the MINIMUM production
    /// material that can lawfully establish the project↔authority rule mapping: an approval record proves a project
    /// candidate was approved, an authority-policy artifact proves a deterministic policy exists, and a trusted-issuer
    /// entry proves a signer is trusted — but NONE of them names WHICH authority-chain rule governs WHICH project
    /// rule. That mapping lives in two deliberately distinct identity namespaces and can never be inferred, derived
    /// from naming similarity, or hard-coded. It must be stated explicitly and then signed by a trusted issuer, so the
    /// mapping itself is verified material rather than a guess.
    ///
    /// The artifact reuses the existing trusted-issuer / RSA_PKCS1_SHA256 signature infrastructure verbatim (the same
    /// <see cref="EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry"/> trust store and the same canonical
    /// unsigned-payload/id/signature convention as <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifact"/>).
    /// Signing the artifact binds the issuer and key to the exact binding bytes, so the recorded ProjectDirectory,
    /// ProjectRuleId, AuthorityRuleId and every evidence field are tamper-evident.
    ///
    /// Hard invariants:
    /// - Production authority never exists until a VALID, SIGNED, TRUSTED, IN-SCOPE, NON-REVOKED artifact is loaded by
    ///   <c>ProjectRuleAuthorityBindingCatalog</c>. Absent, malformed, unsigned, tampered, untrusted, out-of-scope or
    ///   revoked material yields no binding at all (fail-closed: 0 auto-executable rules).
    /// - Signing and trust alone still grant nothing: the carried binding must additionally be validated field-by-field
    ///   against the authority chain's ACTUAL resolution by <see cref="ProjectRuleAuthorityBindingValidator"/> before it
    ///   can execute anything.
    /// </summary>
    [DataContract]
    public sealed class ProjectRuleAuthorityBindingArtifact
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string BindingArtifactId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string IssuerId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string KeyId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public DateTime IssuedAtUtc { get; set; }
        [DataMember(Order = 6)] public string SignatureAlgorithm { get; set; } = string.Empty;
        [DataMember(Order = 7)] public ProjectRuleAuthorityBinding? Binding { get; set; }
        /// <summary>When non-null, the trusted issuer has revoked this binding. A revoked artifact is dropped by the
        /// intake loader, so its project rule returns to fail-closed. Revocation is part of the signed payload and can
        /// therefore only be issued by a trusted signer.</summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public DateTime? RevokedAtUtc { get; set; }
        [DataMember(Order = 9)] public string SignatureBase64 { get; set; } = string.Empty;
    }
}
