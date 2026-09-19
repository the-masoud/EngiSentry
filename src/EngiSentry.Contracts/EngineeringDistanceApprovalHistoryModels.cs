using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class EngineeringDistanceApprovalHistoryStates
    {
        public const string Active = "ACTIVE";
        public const string Superseded = "SUPERSEDED";
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHistoryEntry
    {
        [DataMember(Order = 1)] public string ApprovalRecordId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ProjectDirectory { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string State { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? SupersededByApprovalRecordId { get; set; }
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalHistoryResolution
    {
        [DataMember(Order = 1)] public List<EngineeringDistanceApprovalHistoryEntry> Entries { get; set; } = new List<EngineeringDistanceApprovalHistoryEntry>();
    }
}
