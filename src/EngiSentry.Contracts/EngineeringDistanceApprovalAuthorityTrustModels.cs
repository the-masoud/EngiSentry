using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceApprovalAuthorityTrustStoreDocument
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> TrustedKeys { get; set; } = new List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition>();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalAuthorityTrustedKeyDefinition
    {
        [DataMember(Order = 1)] public string IssuerId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string KeyId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SignatureAlgorithm { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string RsaModulusBase64 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string RsaExponentBase64 { get; set; } = string.Empty;
    }
}
