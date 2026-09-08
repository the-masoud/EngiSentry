using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceCandidateDefinition
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PackId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Title { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string MeasurementKind { get; set; } = string.Empty;
        [DataMember(Order = 6)] public decimal ProposedMinimumDistanceMeters { get; set; }
        [DataMember(Order = 7)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string ApprovalStatus { get; set; } = string.Empty;
        [DataMember(Order = 10)] public List<string> ClauseIds { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalDecision
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string DecisionStatus { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? ApprovalReference { get; set; }
    }
}
