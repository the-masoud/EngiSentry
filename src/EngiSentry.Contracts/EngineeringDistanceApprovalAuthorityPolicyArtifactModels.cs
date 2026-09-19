using System;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceApprovalAuthorityPolicyArtifact
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PolicyArtifactId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string IssuerId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string KeyId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public DateTime IssuedAtUtc { get; set; }
        [DataMember(Order = 6)] public string SignatureAlgorithm { get; set; } = string.Empty;
        [DataMember(Order = 7)] public EngineeringDistanceApprovalAuthorityPolicyDefinition? Policy { get; set; }
        [DataMember(Order = 8)] public string SignatureBase64 { get; set; } = string.Empty;
    }
}
