using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceCriteriaProvenance
    {
        [DataMember(Order = 1)] public string SourceKind { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string CriteriaSettingKey { get; set; } = string.Empty;
        [DataMember(Order = 4)] public int SchemaVersion { get; set; }
        [DataMember(Order = 5)] public decimal ConfiguredMinimumDistanceMeters { get; set; }
    }
}
