using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class ProjectDistanceCriteria
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; }
        [DataMember(Order = 2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal AccessRoadEdgeMinimumMeters { get; set; }
        [DataMember(Order = 4)] public decimal SecurityFenceMinimumMeters { get; set; }
        [DataMember(Order = 5)] public decimal UnitBoundaryMinimumMeters { get; set; }
    }

    public static class ProjectDistanceCriteriaDefaults
    {
        public const int CurrentSchemaVersion = 2;
        public const decimal AccessRoadEdgeMinimumMeters = 3m;
        public const decimal SecurityFenceMinimumMeters = 30m;
        public const decimal UnitBoundaryMinimumMeters = 15m;
    }
}
