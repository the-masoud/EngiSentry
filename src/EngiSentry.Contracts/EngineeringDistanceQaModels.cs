using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceEvidence
    {
        [DataMember(Order = 1)] public string MeasurementId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string MeasurementKind { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string ConstraintId { get; set; } = string.Empty;
        [DataMember(Order = 8)] public decimal MinimumDistanceMeters { get; set; }
        [DataMember(Order = 9)] public double MinimumDistanceDrawingUnits { get; set; }
        [DataMember(Order = 10)] public double ActualDistanceDrawingUnits { get; set; }
        [DataMember(Order = 11)] public double ActualDistanceMeters { get; set; }
        [DataMember(Order = 12)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string CanonicalUnit { get; set; } = string.Empty;
        [DataMember(Order = 14)] public double MetersPerDrawingUnit { get; set; }
        [DataMember(Order = 15)] public string LabelSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 16)] public string GeometrySourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 17)] public double ClosestPointX { get; set; }
        [DataMember(Order = 18)] public double ClosestPointY { get; set; }
        [DataMember(Order = 19)] public string RequiredMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 20)] public string ActualMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 21)] public string RequiredApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 22)] public string ActualApplicabilityKey { get; set; } = string.Empty;
        [DataMember(Order = 23, EmitDefaultValue = false)] public EngineeringDistanceCriteriaProvenance? CriteriaProvenance { get; set; }
    }
}
