using System;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectRuleAuthorityBindingArtifactValidationException : Exception { public ProjectRuleAuthorityBindingArtifactValidationException(string message, Exception? innerException = null) : base(message, innerException) { } }

    /// <summary>
    /// Verifies a <see cref="ProjectRuleAuthorityBindingArtifact"/> as genuine signed material: well-formed, schema
    /// correct, content-identity correct, issued by a TRUSTED issuer/key pair, and covered by a valid RSA signature.
    /// This reuses the accepted trusted-issuer / RSA_PKCS1_SHA256 infrastructure exactly as
    /// <see cref="EngineeringDistanceApprovalAuthorityPolicyArtifactValidator"/> does for policy artifacts.
    ///
    /// What this validator does NOT do: it grants no execution authority. It establishes only that the binding is
    /// authentic, signed, trusted and in-scope. The carried binding still has to be validated field-by-field against
    /// the authority chain's ACTUAL resolution by <see cref="ProjectRuleAuthorityBindingValidator"/> before it can
    /// execute anything. Two independent gates, neither sufficient alone.
    /// </summary>
    public static class ProjectRuleAuthorityBindingArtifactValidator
    {
        public const string SchemaVersion = "1.0";

        public static ProjectRuleAuthorityBindingArtifact Validate(ProjectRuleAuthorityBindingArtifact artifact, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));

            // 1. Shape and schema. Wrong schema is not binding material.
            if (artifact.SchemaVersion != SchemaVersion) Fail("binding artifact SchemaVersion is invalid");
            if (!EngineeringDistanceApprovalRecordValidator.IsSha(artifact.BindingArtifactId)) Fail("binding artifact BindingArtifactId is invalid");
            if (string.IsNullOrWhiteSpace(artifact.IssuerId)) Fail("binding artifact IssuerId is required");
            if (string.IsNullOrWhiteSpace(artifact.KeyId)) Fail("binding artifact KeyId is required");
            if (artifact.IssuedAtUtc.Kind != DateTimeKind.Utc || artifact.IssuedAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0) Fail("binding artifact IssuedAtUtc must be UTC whole milliseconds");
            if (artifact.RevokedAtUtc.HasValue && (artifact.RevokedAtUtc.Value.Kind != DateTimeKind.Utc || artifact.RevokedAtUtc.Value.Ticks % TimeSpan.TicksPerMillisecond != 0)) Fail("binding artifact RevokedAtUtc must be UTC whole milliseconds");
            if (artifact.SignatureAlgorithm != EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm) Fail("binding artifact SignatureAlgorithm is invalid");

            // 2. The carried binding must be structurally present and well-formed. A binding that does not state BOTH
            //    identities states no mapping at all and cannot be signed into authority.
            ProjectRuleAuthorityBinding binding = artifact.Binding ?? throw new ProjectRuleAuthorityBindingArtifactValidationException("binding artifact Binding is required");
            if (string.IsNullOrWhiteSpace(binding.ProjectRuleId)) Fail("binding artifact Binding.ProjectRuleId is required");
            if (string.IsNullOrWhiteSpace(binding.AuthorityRuleId)) Fail("binding artifact Binding.AuthorityRuleId is required");
            if (string.Equals(binding.ProjectRuleId, binding.AuthorityRuleId, StringComparison.Ordinal)) Fail("binding artifact Binding must not map a rule to itself");

            // 3. Project scope must be present and normalized, so the intake loader can compare it against the current
            //    project without ambiguity. An unscoped binding is not production material.
            string projectDirectory = binding.ProjectDirectory ?? string.Empty;
            if (string.IsNullOrWhiteSpace(projectDirectory)) Fail("binding artifact Binding.ProjectDirectory is required");
            else if (projectDirectory != EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory)) Fail("binding artifact Binding.ProjectDirectory is invalid or not normalized");

            // 4. Content identity: the recorded artifact id must match the canonical id of the signed payload, so a
            //    binding cannot be altered after the fact without invalidating its id.
            string expectedId = ProjectRuleAuthorityBindingArtifactCanonicalizer.ComputeBindingArtifactId(artifact);
            if (!string.Equals(artifact.BindingArtifactId, expectedId, StringComparison.Ordinal)) Fail("binding artifact BindingArtifactId does not match content");

            // 5. The signer must be a trusted issuer/key pair in the accepted trust store.
            byte[] signature;
            try { signature = Convert.FromBase64String(artifact.SignatureBase64); }
            catch (Exception exception) { throw new ProjectRuleAuthorityBindingArtifactValidationException("binding artifact SignatureBase64 is invalid", exception); }
            if (!trustedIssuers.TryGet(artifact.IssuerId, artifact.KeyId, out EngineeringDistanceApprovalAuthorityTrustedKeyDefinition? trusted) || trusted == null)
            { throw new ProjectRuleAuthorityBindingArtifactValidationException("binding artifact signer is not trusted"); }
            if (trusted.SignatureAlgorithm != artifact.SignatureAlgorithm) Fail("binding artifact signer algorithm does not match");

            // 6. RSA signature verification over the canonical unsigned payload. Tampering with any signed field —
            //    including the binding's project scope or authority rule id — fails here.
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(trusted.RsaModulusBase64), Exponent = Convert.FromBase64String(trusted.RsaExponentBase64) });
                    if (rsa.KeySize < 2048) Fail("trusted RSA key size is below 2048 bits");
                    if (!rsa.VerifyData(ProjectRuleAuthorityBindingArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) Fail("binding artifact signature verification failed");
                }
            }
            catch (ProjectRuleAuthorityBindingArtifactValidationException) { throw; }
            catch (Exception exception) { throw new ProjectRuleAuthorityBindingArtifactValidationException("binding artifact signature verification failed", exception); }

            return ProjectRuleAuthorityBindingArtifactCanonicalizer.CopyArtifact(artifact);
        }

        private static void Fail(string message) => throw new ProjectRuleAuthorityBindingArtifactValidationException(message);
    }
}
