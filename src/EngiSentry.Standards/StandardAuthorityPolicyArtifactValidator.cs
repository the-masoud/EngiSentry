using System;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardAuthorityPolicyArtifactValidationException : Exception { public StandardAuthorityPolicyArtifactValidationException(string message, Exception? innerException = null) : base(message, innerException) { } }

    /// <summary>
    /// Verifies a <see cref="StandardAuthorityPolicyArtifact"/> as genuine signed material: well-formed, schema
    /// correct, project-scoped, content-identity correct, issued by a TRUSTED issuer/key pair, covered by a valid
    /// RSA signature, and carrying a policy whose SourceId/ClauseId/class/mode are real and consistent. This
    /// reuses the accepted trusted-issuer / RSA_PKCS1_SHA256 infrastructure exactly as
    /// <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifactValidator"/> does for approval-policy
    /// artifacts and <see cref="ProjectRuleAuthorityBindingArtifactValidator"/> for bindings.
    ///
    /// What this validator does NOT do: it grants no execution authority and never sets
    /// <c>AutoExecutionAllowed</c>. It establishes only that a standards-authority POLICY is authentic, signed,
    /// trusted, in-scope and internally consistent. The policy still has to be merged with the built-in baseline
    /// into the <see cref="StandardAuthorityPolicyRegistry"/> — where unknown sources, orphan clauses, unsupported
    /// class/mode pairings and conflicts with the built-in safe baseline are rejected — before
    /// <see cref="StandardAuthorityResolver"/> and <see cref="StandardAuthorityExecutionGate"/> ever see it.
    /// Multiple independent gates, none sufficient alone.
    /// </summary>
    public static class StandardAuthorityPolicyArtifactValidator
    {
        public const string SchemaVersion = "1.0";

        /// <summary>Validates one artifact. <paramref name="sourceRegistry"/> and <paramref name="clauseRegistry"/>
        /// are the SAME registries the runtime context builds, so a policy can only ever cite a SourceId and
        /// ClauseId the authority chain actually knows about.</summary>
        public static StandardAuthorityPolicyArtifact Validate(
            StandardAuthorityPolicyArtifact artifact,
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers,
            StandardSourceRegistry sourceRegistry,
            StandardClauseRegistry clauseRegistry)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));

            // 1. Shape and schema. Wrong schema is not policy material.
            if (artifact.SchemaVersion != SchemaVersion) Fail("standard authority policy artifact SchemaVersion is invalid");
            if (!EngineeringDistanceApprovalRecordValidator.IsSha(artifact.PolicyArtifactId)) Fail("standard authority policy artifact PolicyArtifactId is invalid");
            if (string.IsNullOrWhiteSpace(artifact.IssuerId)) Fail("standard authority policy artifact IssuerId is required");
            if (string.IsNullOrWhiteSpace(artifact.KeyId)) Fail("standard authority policy artifact KeyId is required");
            if (artifact.IssuedAtUtc.Kind != DateTimeKind.Utc || artifact.IssuedAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0) Fail("standard authority policy artifact IssuedAtUtc must be UTC whole milliseconds");
            if (artifact.RevokedAtUtc.HasValue && (artifact.RevokedAtUtc.Value.Kind != DateTimeKind.Utc || artifact.RevokedAtUtc.Value.Ticks % TimeSpan.TicksPerMillisecond != 0)) Fail("standard authority policy artifact RevokedAtUtc must be UTC whole milliseconds");
            if (artifact.SignatureAlgorithm != EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm) Fail("standard authority policy artifact SignatureAlgorithm is invalid");

            // 2. Project scope must be present and normalized, so the intake loader can compare it against the
            //    current project without ambiguity. An unscoped policy is not production material.
            if (artifact.ProjectDirectory != EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(artifact.ProjectDirectory)) Fail("standard authority policy artifact ProjectDirectory is invalid or not normalized");

            // 3. The carried policy must be structurally present and internally consistent. A policy that does not
            //    state a SourceId and an authority class/mode states no authority at all.
            StandardAuthorityPolicyDefinition policy = artifact.Policy ?? throw new StandardAuthorityPolicyArtifactValidationException("standard authority policy artifact Policy is required");
            if (string.IsNullOrWhiteSpace(policy.PolicyId)) Fail("standard authority policy artifact Policy.PolicyId is required");
            if (string.IsNullOrWhiteSpace(policy.SourceId)) Fail("standard authority policy artifact Policy.SourceId is required: " + policy.PolicyId);
            if (policy.ClauseId != null && string.IsNullOrWhiteSpace(policy.ClauseId)) Fail("standard authority policy artifact Policy.ClauseId is empty: " + policy.PolicyId);
            if (string.IsNullOrWhiteSpace(policy.AuthorityClass)) Fail("standard authority policy artifact Policy.AuthorityClass is required: " + policy.PolicyId);
            if (string.IsNullOrWhiteSpace(policy.EnforcementMode)) Fail("standard authority policy artifact Policy.EnforcementMode is required: " + policy.PolicyId);
            if (string.IsNullOrWhiteSpace(policy.Rationale)) Fail("standard authority policy artifact Policy.Rationale is required: " + policy.PolicyId);
            if (!IsAuthorityClass(policy.AuthorityClass)) Fail("standard authority policy artifact Policy.AuthorityClass is unsupported: " + policy.AuthorityClass);
            if (!IsEnforcementMode(policy.EnforcementMode)) Fail("standard authority policy artifact Policy.EnforcementMode is unsupported: " + policy.EnforcementMode);
            if (!IsValidCombination(policy.AuthorityClass, policy.EnforcementMode)) Fail("standard authority policy artifact Policy authority/enforcement combination is invalid: " + policy.AuthorityClass + " | " + policy.EnforcementMode);

            // 4. The cited SourceId must be a registered source, and — when a ClauseId is stated — that clause must
            //    exist and belong to the SAME SourceId. A policy may not point at an invented source or at another
            //    source's clause; this is the explicit, non-inferred SourceId/ClauseId relationship.
            if (!sourceRegistry.TryGet(policy.SourceId, out _)) Fail("standard authority policy artifact Policy references unknown SourceId: " + policy.SourceId);
            if (policy.ClauseId != null)
            {
                if (!clauseRegistry.TryGet(policy.ClauseId, out StandardClauseDefinition? clause) || clause == null) Fail("standard authority policy artifact Policy references unknown ClauseId: " + policy.ClauseId);
                else if (!string.Equals(clause.SourceId, policy.SourceId, StringComparison.Ordinal)) Fail("standard authority policy artifact Policy ClauseId belongs to a different SourceId: " + policy.ClauseId);
            }

            // 5. Content identity: the recorded artifact id must match the canonical id of the signed payload, so a
            //    policy cannot be altered after the fact without invalidating its id.
            string expectedId = StandardAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact);
            if (!string.Equals(artifact.PolicyArtifactId, expectedId, StringComparison.Ordinal)) Fail("standard authority policy artifact PolicyArtifactId does not match content");

            // 6. The signer must be a trusted issuer/key pair in the accepted trust store.
            byte[] signature;
            try { signature = Convert.FromBase64String(artifact.SignatureBase64); }
            catch (Exception exception) { throw new StandardAuthorityPolicyArtifactValidationException("standard authority policy artifact SignatureBase64 is invalid", exception); }
            if (!trustedIssuers.TryGet(artifact.IssuerId, artifact.KeyId, out EngineeringDistanceApprovalAuthorityTrustedKeyDefinition? trusted) || trusted == null)
            { throw new StandardAuthorityPolicyArtifactValidationException("standard authority policy artifact signer is not trusted"); }
            if (trusted.SignatureAlgorithm != artifact.SignatureAlgorithm) Fail("standard authority policy artifact signer algorithm does not match");

            // 7. RSA signature verification over the canonical unsigned payload. Tampering with any signed field —
            //    including the policy's project scope, source, clause or enforcement mode — fails here.
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(trusted.RsaModulusBase64), Exponent = Convert.FromBase64String(trusted.RsaExponentBase64) });
                    if (rsa.KeySize < 2048) Fail("trusted RSA key size is below 2048 bits");
                    if (!rsa.VerifyData(StandardAuthorityPolicyArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) Fail("standard authority policy artifact signature verification failed");
                }
            }
            catch (StandardAuthorityPolicyArtifactValidationException) { throw; }
            catch (Exception exception) { throw new StandardAuthorityPolicyArtifactValidationException("standard authority policy artifact signature verification failed", exception); }

            return StandardAuthorityPolicyArtifactCanonicalizer.CopyArtifact(artifact);
        }

        private static bool IsAuthorityClass(string value) =>
            value == StandardAuthorityClasses.GoverningProject ||
            value == StandardAuthorityClasses.ActivePrimary ||
            value == StandardAuthorityClasses.ActiveSupporting ||
            value == StandardAuthorityClasses.EngineeringGuidance ||
            value == StandardAuthorityClasses.LegacyConflict ||
            value == StandardAuthorityClasses.Quarantined;

        private static bool IsEnforcementMode(string value) =>
            value == StandardEnforcementModes.Deterministic ||
            value == StandardEnforcementModes.ConditionalReview ||
            value == StandardEnforcementModes.Advisory ||
            value == StandardEnforcementModes.NeverAutoEnforce;

        private static bool IsValidCombination(string authorityClass, string enforcementMode)
        {
            if (authorityClass == StandardAuthorityClasses.GoverningProject || authorityClass == StandardAuthorityClasses.ActivePrimary)
                return enforcementMode == StandardEnforcementModes.Deterministic || enforcementMode == StandardEnforcementModes.ConditionalReview;
            if (authorityClass == StandardAuthorityClasses.ActiveSupporting || authorityClass == StandardAuthorityClasses.EngineeringGuidance)
                return enforcementMode == StandardEnforcementModes.ConditionalReview || enforcementMode == StandardEnforcementModes.Advisory;
            return (authorityClass == StandardAuthorityClasses.LegacyConflict || authorityClass == StandardAuthorityClasses.Quarantined) && enforcementMode == StandardEnforcementModes.NeverAutoEnforce;
        }

        private static void Fail(string message) => throw new StandardAuthorityPolicyArtifactValidationException(message);
    }
}
