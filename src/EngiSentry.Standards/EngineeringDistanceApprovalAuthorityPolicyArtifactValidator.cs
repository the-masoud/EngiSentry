using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException : Exception { public EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException(string message, Exception? innerException = null) : base(message, innerException) { } }
    public static class EngineeringDistanceApprovalAuthorityPolicyArtifactValidator
    {
        public const string SchemaVersion = "1.0";
        public static EngineeringDistanceApprovalAuthorityPolicyArtifact Validate(EngineeringDistanceApprovalAuthorityPolicyArtifact artifact, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));
            if (artifact.SchemaVersion != SchemaVersion) Fail("policy artifact SchemaVersion is invalid");
            if (!EngineeringDistanceApprovalRecordValidator.IsSha(artifact.PolicyArtifactId)) Fail("policy artifact PolicyArtifactId is invalid");
            if (string.IsNullOrWhiteSpace(artifact.IssuerId)) Fail("policy artifact IssuerId is required");
            if (string.IsNullOrWhiteSpace(artifact.KeyId)) Fail("policy artifact KeyId is required");
            if (artifact.IssuedAtUtc.Kind != DateTimeKind.Utc || artifact.IssuedAtUtc.Ticks % TimeSpan.TicksPerMillisecond != 0) Fail("policy artifact IssuedAtUtc must be UTC whole milliseconds");
            if (artifact.SignatureAlgorithm != EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm) Fail("policy artifact SignatureAlgorithm is invalid");
            EngineeringDistanceApprovalAuthorityPolicyDefinition policy = artifact.Policy ?? throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException("policy artifact Policy is required");
            if (policy.ProjectDirectory != EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(policy.ProjectDirectory)) Fail("policy artifact Policy.ProjectDirectory is invalid or not normalized");
            try { EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.Copy(policy) }); }
            catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException("policy artifact Policy is invalid", exception); }
            string expectedId = EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact);
            if (!string.Equals(artifact.PolicyArtifactId, expectedId, StringComparison.Ordinal)) Fail("policy artifact PolicyArtifactId does not match content");
            byte[] signature;
            try { signature = Convert.FromBase64String(artifact.SignatureBase64); }
            catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException("policy artifact SignatureBase64 is invalid", exception); }
            if (!trustedIssuers.TryGet(artifact.IssuerId, artifact.KeyId, out EngineeringDistanceApprovalAuthorityTrustedKeyDefinition? trusted) || trusted == null)
            {
                throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException("policy artifact signer is not trusted");
            }
            if (trusted.SignatureAlgorithm != artifact.SignatureAlgorithm) Fail("policy artifact signer algorithm does not match");
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(trusted.RsaModulusBase64), Exponent = Convert.FromBase64String(trusted.RsaExponentBase64) });
                    if (rsa.KeySize < 2048) Fail("trusted RSA key size is below 2048 bits");
                    if (!rsa.VerifyData(EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) Fail("policy artifact signature verification failed");
                }
            }
            catch (EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException) { throw; }
            catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException("policy artifact signature verification failed", exception); }
            return CopyArtifact(artifact);
        }

        internal static EngineeringDistanceApprovalAuthorityPolicyArtifact CopyArtifact(EngineeringDistanceApprovalAuthorityPolicyArtifact x) => new EngineeringDistanceApprovalAuthorityPolicyArtifact { SchemaVersion = x.SchemaVersion, PolicyArtifactId = x.PolicyArtifactId, IssuerId = x.IssuerId, KeyId = x.KeyId, IssuedAtUtc = x.IssuedAtUtc, SignatureAlgorithm = x.SignatureAlgorithm, Policy = x.Policy == null ? null : EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.Copy(x.Policy), SignatureBase64 = x.SignatureBase64 };
        private static void Fail(string message) => throw new EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException(message);
    }
}
