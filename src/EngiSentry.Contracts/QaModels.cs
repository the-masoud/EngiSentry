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
        [DataMember(Order = 7, EmitDefaultValue = false)] public ProfileResolution? ProfileResolution { get; set; }
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
        [DataMember(Order = 15)] public int? OccurrenceCount { get; set; }
        [DataMember(Order = 16)] public int? DistinctSourceRowCount { get; set; }
        [DataMember(Order = 17)] public List<string> ConflictFields { get; set; } = new List<string>();
        [DataMember(Order = 18)] public List<QaEquipmentListSourceRow> SourceRows { get; set; } = new List<QaEquipmentListSourceRow>();
        [DataMember(Order = 19)] public List<QaOccurrenceEvidence> Occurrences { get; set; } = new List<QaOccurrenceEvidence>();
        [DataMember(Order = 20)] public string? PlacementId { get; set; }
        [DataMember(Order = 21)] public string? EquipmentEntityId { get; set; }
        [DataMember(Order = 22)] public string? TagSourceHandle { get; set; }
        [DataMember(Order = 23)] public List<CoordinateAssociation> CoordinateAssociations { get; set; } = new List<CoordinateAssociation>();
        [DataMember(Order = 24)] public List<ResolvedCoordinatePairEvidence> ResolvedCoordinatePairs { get; set; } = new List<ResolvedCoordinatePairEvidence>();
        [DataMember(Order = 25)] public GeometryAssociation? GeometryAssociation { get; set; }
        [DataMember(Order = 26)] public SpatialCoordinateConstraint? SpatialCoordinateConstraint { get; set; }
        [DataMember(Order = 27, EmitDefaultValue = false)] public ClearanceEvidence? ClearanceEvidence { get; set; }
        [DataMember(Order = 28, EmitDefaultValue = false)] public CrossDocumentEvidence? CrossDocumentEvidence { get; set; }
    }

    [DataContract]
    public sealed class ClearanceEvidence
    {
        [DataMember(Order = 1)] public string MeasurementId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ConstraintId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public decimal MinimumDistanceDrawingUnits { get; set; }
        [DataMember(Order = 8)] public double ActualDistance2D { get; set; }
        [DataMember(Order = 9)] public string LabelSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string GeometrySourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 11)] public double ClosestPointX { get; set; }
        [DataMember(Order = 12)] public double ClosestPointY { get; set; }
    }

    [DataContract]
    public sealed class QaEquipmentListSourceRow
    {
        [DataMember(Order = 1)] public string SourceTag { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string? Description { get; set; }
        [DataMember(Order = 3)] public string? Installation { get; set; }
        [DataMember(Order = 4)] public List<string> SourceHandles { get; set; } = new List<string>();
        [DataMember(Order = 5)] public string LinkMethod { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class QaOccurrenceEvidence
    {
        [DataMember(Order = 1)] public string SourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string? SourceLayer { get; set; }
        [DataMember(Order = 3)] public double PositionX { get; set; }
        [DataMember(Order = 4)] public double PositionY { get; set; }
        [DataMember(Order = 5)] public double PositionZ { get; set; }
    }

    [DataContract]
    public sealed class ResolvedCoordinatePairEvidence
    {
        [DataMember(Order = 1)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string TagSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal EastingValue { get; set; }
        [DataMember(Order = 4)] public string EastingSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 5)] public decimal NorthingValue { get; set; }
        [DataMember(Order = 6)] public string NorthingSourceHandle { get; set; } = string.Empty;
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
