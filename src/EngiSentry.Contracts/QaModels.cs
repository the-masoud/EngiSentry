using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class QaReport
    {
        [DataMember(Order = 1)] public SemanticDrawing? Drawing { get; set; }
        [DataMember(Order = 2)] public string ExecutionStatus { get; set; } = "Complete";
        [DataMember(Order = 3)] public string OverallStatus { get; set; } = "PASS";
        [DataMember(Order = 4)] public List<QaRuleResult> RuleResults { get; set; } = new List<QaRuleResult>();
        [DataMember(Order = 5)] public List<QaFinding> Findings { get; set; } = new List<QaFinding>();
        [DataMember(Order = 6)] public QaStatistics Statistics { get; set; } = new QaStatistics();
    }

    [DataContract]
    public sealed class QaRuleResult
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Severity { get; set; } = string.Empty;
        [DataMember(Order = 3)] public int EvaluatedSubjectCount { get; set; }
        [DataMember(Order = 4)] public int FindingCount { get; set; }
        [DataMember(Order = 5)] public string Status { get; set; } = "PASS";
    }

    [DataContract]
    public sealed class QaFinding
    {
        [DataMember(Order = 1)] public string FindingId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Severity { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Category { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SubjectType { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string SubjectId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string Message { get; set; } = string.Empty;
        [DataMember(Order = 8)] public QaEvidence Evidence { get; set; } = new QaEvidence();
        [DataMember(Order = 9)] public List<string> SourceHandles { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class QaEvidence
    {
        [DataMember(Order = 1)] public string? CanonicalId { get; set; }
        [DataMember(Order = 2)] public string? NormalizedTag { get; set; }
        [DataMember(Order = 3)] public bool? HasDrawingEvidence { get; set; }
        [DataMember(Order = 4)] public bool? HasEquipmentListEvidence { get; set; }
        [DataMember(Order = 5)] public List<string> DrawingTagSourceHandles { get; set; } = new List<string>();
        [DataMember(Order = 6)] public List<string> EquipmentListSourceHandles { get; set; } = new List<string>();
        [DataMember(Order = 7)] public string? Description { get; set; }
        [DataMember(Order = 8)] public string? Installation { get; set; }
        [DataMember(Order = 9)] public string? EquipmentListLinkMethod { get; set; }
        [DataMember(Order = 10)] public string? SourceType { get; set; }
        [DataMember(Order = 11)] public string? SourceValue { get; set; }
        [DataMember(Order = 12)] public string? ExpectedTargetIdentity { get; set; }
        [DataMember(Order = 13)] public string? Reason { get; set; }
        [DataMember(Order = 14)] public List<string> CandidateEntityIds { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class QaStatistics
    {
        [DataMember(Order = 1)] public int RuleCount { get; set; }
        [DataMember(Order = 2)] public int RulesPassed { get; set; }
        [DataMember(Order = 3)] public int RulesWithFindings { get; set; }
        [DataMember(Order = 4)] public int TotalFindings { get; set; }
        [DataMember(Order = 5)] public int InfoFindings { get; set; }
        [DataMember(Order = 6)] public int WarningFindings { get; set; }
        [DataMember(Order = 7)] public int ErrorFindings { get; set; }
    }
}
