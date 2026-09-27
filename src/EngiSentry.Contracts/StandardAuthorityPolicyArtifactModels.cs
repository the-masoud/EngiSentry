using System;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// The signed intake artifact for a <see cref="StandardAuthorityPolicyDefinition"/>. This is the MINIMUM
    /// production material that can lawfully establish a standards-authority POLICY: the built-in policy catalog
    /// is the safe baseline and resolves every source/clause to CONDITIONAL_REVIEW or NEVER_AUTO_ENFORCE, so a
    /// DETERMINISTIC gate can only ever come from explicit, verified, signed material. An approval record, a
    /// distance value, a governed-compatible result or a trusted-issuer entry alone names no policy over a
    /// SourceId/ClauseId and grants no enforcement mode. That policy must be stated explicitly and then signed
    /// by a trusted issuer, so the policy itself is verified material rather than a guess or a default.
    ///
    /// The artifact reuses the existing trusted-issuer / RSA_PKCS1_SHA256 signature infrastructure verbatim (the
    /// same <see cref="EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry"/> trust store and the same
    /// canonical unsigned-payload/id/signature convention as
    /// <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifact"/> and
    /// <see cref="ProjectRuleAuthorityBindingArtifact"/>). Signing binds the issuer and key to the exact policy
    /// bytes, so the recorded ProjectDirectory, SourceId, ClauseId, AuthorityClass, EnforcementMode and
    /// Rationale are all tamper-evident.
    ///
    /// Hard invariants:
    /// - A standards-authority policy never carries execution authority until a VALID, SIGNED, TRUSTED,
    ///   IN-SCOPE, NON-REVOKED artifact is loaded by <c>StandardAuthorityPolicyArtifactCatalog</c>. Absent,
    ///   malformed, unsigned, tampered, untrusted, out-of-scope or revoked material yields no external policy at
    ///   all (fail-closed: the built-in baseline alone produces 0 deterministic gates).
    /// - The artifact NEVER sets <c>AutoExecutionAllowed</c> and never bypasses the resolver or the execution
    ///   gate. It only contributes policies into the normal
    ///   authority-policy → <c>StandardAuthorityResolver</c> → <c>StandardAuthorityExecutionGate</c> path, which
    ///   remains the sole place executability is decided. NEVER_AUTO_ENFORCE, QUARANTINED and LEGACY_CONFLICT
    ///   authority stay non-executable there exactly as before.
    /// - Signing and trust alone still grant nothing: the carried policy must additionally survive the
    ///   <c>StandardAuthorityPolicyRegistry</c> validation (unknown SourceId, ClauseId not belonging to
    ///   SourceId, unsupported class/mode and conflicts with the built-in baseline are all rejected) before the
    ///   resolver ever sees it.
    /// </summary>
    [DataContract]
    public sealed class StandardAuthorityPolicyArtifact
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PolicyArtifactId { get; set; } = string.Empty;
        /// <summary>The absolute project directory this policy is scoped to (normalized upper-cased form). A
        /// policy is authoritative only inside its own project scope; the intake loader compares this against the
        /// current project before the policy is ever handed to the registry. It is inside the signed payload, so
        /// it cannot be retargeted without invalidating the signature.</summary>
        [DataMember(Order = 3)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string IssuerId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string KeyId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public DateTime IssuedAtUtc { get; set; }
        /// <summary>When non-null, the trusted issuer has revoked this policy. A revoked artifact is dropped by
        /// the intake loader, so its clause/source returns to the built-in fail-closed baseline. Revocation is
        /// part of the signed payload and can therefore only be issued by a trusted signer.</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public DateTime? RevokedAtUtc { get; set; }
        [DataMember(Order = 8)] public string SignatureAlgorithm { get; set; } = string.Empty;
        [DataMember(Order = 9)] public StandardAuthorityPolicyDefinition? Policy { get; set; }
        [DataMember(Order = 10)] public string SignatureBase64 { get; set; } = string.Empty;
    }
}
