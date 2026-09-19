using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException : Exception { public EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException(string message, Exception? innerException = null) : base(message, innerException) { } }

    public sealed class EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry
    {
        public const string SignatureAlgorithm = "RSA_PKCS1_SHA256";
        private readonly List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> keys;
        private EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry(List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> values) { keys = values; }
        public IReadOnlyList<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> TrustedKeys => keys.Select(Copy).ToList();

        public static EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry Create(IEnumerable<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var accepted = new List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineeringDistanceApprovalAuthorityTrustedKeyDefinition value in values)
            {
                if (value == null)
                {
                    throw new EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException("trusted issuer registry contains null key");
                }
                EngineeringDistanceApprovalAuthorityTrustedKeyDefinition copy = Copy(value);
                if (string.IsNullOrWhiteSpace(copy.IssuerId) || string.IsNullOrWhiteSpace(copy.KeyId)) Fail("trusted key IssuerId and KeyId are required");
                if (copy.SignatureAlgorithm != SignatureAlgorithm) Fail("trusted key SignatureAlgorithm is invalid");
                if (!identities.Add(copy.IssuerId + "\u001f" + copy.KeyId)) Fail("duplicate trusted key IssuerId and KeyId");
                try
                {
                    RSAParameters parameters = new RSAParameters { Modulus = Convert.FromBase64String(copy.RsaModulusBase64), Exponent = Convert.FromBase64String(copy.RsaExponentBase64) };
                    using (RSA rsa = RSA.Create()) { rsa.ImportParameters(parameters); if (rsa.KeySize < 2048) Fail("trusted RSA key size is below 2048 bits"); }
                }
                catch (EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException) { throw; }
                catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException("trusted RSA public key is invalid", exception); }
                accepted.Add(copy);
            }
            return new EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry(accepted.OrderBy(x => x.IssuerId, StringComparer.Ordinal).ThenBy(x => x.KeyId, StringComparer.Ordinal).ToList());
        }

        public bool TryGet(string issuerId, string keyId, out EngineeringDistanceApprovalAuthorityTrustedKeyDefinition? key)
        {
            key = keys.FirstOrDefault(x => x.IssuerId == issuerId && x.KeyId == keyId);
            if (key == null) return false;
            key = Copy(key);
            return true;
        }

        private static EngineeringDistanceApprovalAuthorityTrustedKeyDefinition Copy(EngineeringDistanceApprovalAuthorityTrustedKeyDefinition x) => new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition { IssuerId = x.IssuerId, KeyId = x.KeyId, SignatureAlgorithm = x.SignatureAlgorithm, RsaModulusBase64 = x.RsaModulusBase64, RsaExponentBase64 = x.RsaExponentBase64 };
        private static void Fail(string message) => throw new EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException(message);
    }
}
