using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceRunSummary
    {
        [DataMember(Order = 1)] public string SourceKind { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 3)] public int SchemaVersion { get; set; }
        [DataMember(Order = 4)] public decimal AccessRoadEdgeMinimumMeters { get; set; }
        [DataMember(Order = 5)] public decimal SecurityFenceMinimumMeters { get; set; }
        [DataMember(Order = 6)] public List<EngineeringDistanceRuleEvaluation> Evaluations { get; set; } = new List<EngineeringDistanceRuleEvaluation>();
        [DataMember(Order = 7)] public decimal UnitBoundaryMinimumMeters { get; set; }
    }
}
