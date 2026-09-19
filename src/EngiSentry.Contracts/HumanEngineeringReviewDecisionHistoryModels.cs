using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class HumanEngineeringReviewDecisionHistoryStates
    {
        public const string Active = "ACTIVE";
        public const string Superseded = "SUPERSEDED";
    }

    [DataContract]
    public sealed class HumanEngineeringReviewDecisionHistoryEntry
    {
        [DataMember(Order = 1)] public string DecisionRecordId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string State { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? SupersededByDecisionRecordId { get; set; }
    }

    [DataContract]
    public sealed class HumanEngineeringReviewDecisionHistoryResolution
    {
        [DataMember(Order = 1)] public List<HumanEngineeringReviewDecisionHistoryEntry> Entries { get; set; } = new List<HumanEngineeringReviewDecisionHistoryEntry>();
    }
}
