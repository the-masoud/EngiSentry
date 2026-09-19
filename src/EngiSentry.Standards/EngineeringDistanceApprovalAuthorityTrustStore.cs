using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalAuthorityTrustStoreException : Exception { public EngineeringDistanceApprovalAuthorityTrustStoreException(string message, Exception? innerException = null) : base(message, innerException) { } }
    public static class EngineeringDistanceApprovalAuthorityTrustStore
    {
        public const string SchemaVersion = "1.0";
        public static string CanonicalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngiSentry", "authority-trust", "trusted-issuers.json");
        public static EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry LoadCanonical() => Load(CanonicalPath);
        public static EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry Load(string path)
        {
            if (!File.Exists(path)) return EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition[0]);
            try
            {
                EngineeringDistanceApprovalAuthorityTrustStoreDocument document;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) document = (EngineeringDistanceApprovalAuthorityTrustStoreDocument)new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalAuthorityTrustStoreDocument)).ReadObject(stream)!;
                if (document == null || document.SchemaVersion != SchemaVersion || document.TrustedKeys == null) throw new EngineeringDistanceApprovalAuthorityTrustStoreException("authority trust store is invalid");
                return EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(document.TrustedKeys);
            }
            catch (EngineeringDistanceApprovalAuthorityTrustStoreException) { throw; }
            catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityTrustStoreException("authority trust store is invalid", exception); }
        }
    }
}
