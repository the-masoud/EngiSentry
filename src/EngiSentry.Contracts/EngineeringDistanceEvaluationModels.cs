using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceRuleEvaluation
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string MeasurementKind { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal MinimumDistanceMeters { get; set; }
        [DataMember(Order = 4)] public double MinimumDistanceDrawingUnits { get; set; }
        [DataMember(Order = 5)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string CanonicalUnit { get; set; } = string.Empty;
        [DataMember(Order = 7)] public double MetersPerDrawingUnit { get; set; }
        [DataMember(Order = 8)] public int EvaluatedSubjectCount { get; set; }
        [DataMember(Order = 9)] public int ViolationCount { get; set; }
        [DataMember(Order = 10)] public string Status { get; set; } = string.Empty;
        [DataMember(Order = 11)] public List<EngineeringDistanceViolation> Violations { get; set; } = new List<EngineeringDistanceViolation>();
        [DataMember(Order = 12)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 14, EmitDefaultValue = false)] public EngineeringDistanceCriteriaProvenance? CriteriaProvenance { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public DrawingUnitConflictEvidence? UnitConflict { get; set; }
    }

    [DataContract]
    public sealed class DrawingUnitConflictEvidence
    {
        [DataMember(Order = 1)] public string Insunits { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<DrawingUnitDeclaration> ExplicitDeclarations { get; set; } = new List<DrawingUnitDeclaration>();
    }

    [DataContract]
    public sealed class EngineeringDistanceViolation
    {
        [DataMember(Order = 1)] public string MeasurementId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ConstraintId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public double ActualDistanceDrawingUnits { get; set; }
        [DataMember(Order = 8)] public double ActualDistanceMeters { get; set; }
        [DataMember(Order = 9)] public string LabelSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string GeometrySourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 11)] public double ClosestPointX { get; set; }
        [DataMember(Order = 12)] public double ClosestPointY { get; set; }
        [DataMember(Order = 13)] public string MeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 14)] public string ApplicabilityKey { get; set; } = string.Empty;
    }
}
