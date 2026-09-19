using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Canonical facility element kinds supported by PlantModel v1. Bounded to the Phase B use case:
    /// Equipment, Road, Plant Fence, Unit Boundary. No speculative facility categories are admitted.
    /// SiteBoundary and BatteryLimit exist in <see cref="EngineeringDistanceEvidenceKeys"/> but carry no
    /// PlantModel v1 kind and are deliberately excluded (see Phase A architecture section C.1).
    /// </summary>
    public static class FacilityElementKind
    {
        public const string Equipment = "Equipment";
        public const string Road = "Road";
        public const string PlantFence = "PlantFence";
        public const string UnitBoundary = "UnitBoundary";

        /// <summary>Deterministically reports whether a value is a supported PlantModel v1 kind.</summary>
        public static bool IsSupported(string value) =>
            value == Equipment || value == Road || value == PlantFence || value == UnitBoundary;
    }

    /// <summary>
    /// Classification confidence vocabulary. A strict ordinal mapping onto existing accepted evidence
    /// statuses (PlacementStatus, CoordinateAssociation.Status, GeometryAssociation.Status); it is a
    /// vocabulary only and never a calibrated numeric probability. See Phase A architecture B.4.
    /// </summary>
    public static class ClassificationConfidence
    {
        public const string High = "HIGH";
        public const string Medium = "MEDIUM";
        public const string Low = "LOW";
        public const string None = "NONE";

        public static bool IsSupported(string value) =>
            value == High || value == Medium || value == Low || value == None;
    }

    /// <summary>
    /// One facility actor in the bounded PlantModel v1 domain: an Equipment, a Road, a Plant Fence, or a
    /// Unit Boundary. Composed purely from existing accepted evidence types; introduces no new geometry.
    /// </summary>
    [DataContract]
    public sealed class FacilityElement
    {
        /// <summary>Deterministic identity, e.g. facility-element:equipment:placement:&lt;PlacementId&gt;
        /// or facility-element:road:&lt;SubjectId&gt;. Never random and never time-derived.</summary>
        [DataMember(Order = 1)] public string ElementId { get; set; } = string.Empty;

        [DataMember(Order = 2)] public string FacilityElementKind { get; set; } = string.Empty;

        /// <summary>Portable source identity of the underlying drawing entity (never a host object).</summary>
        [DataMember(Order = 3)] public string SourceHandle { get; set; } = string.Empty;

        [DataMember(Order = 4, EmitDefaultValue = false)] public string? SourceLayer { get; set; }

        /// <summary>Human-readable classification label for the facility kind.</summary>
        [DataMember(Order = 5)] public string Classification { get; set; } = string.Empty;

        [DataMember(Order = 6)] public string ClassificationConfidence { get; set; } = string.Empty;

        /// <summary>Deterministic human-readable record of why this confidence was assigned.</summary>
        [DataMember(Order = 7)] public string ClassificationBasis { get; set; } = string.Empty;

        /// <summary>Portable evidence handles backing the classification, ordered by Ordinal comparison.</summary>
        [DataMember(Order = 8)] public List<string> EvidenceHandles { get; set; } = new List<string>();

        /// <summary>Resolved equipment location point. Populated for Equipment only, and only when the
        /// existing accepted pipeline already provides resolved coordinate evidence. Null otherwise.</summary>
        [DataMember(Order = 9, EmitDefaultValue = false)] public double? PositionX { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public double? PositionY { get; set; }

        /// <summary>Reference geometry identity for Road / Plant Fence / Unit Boundary elements.</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? GeometryKind { get; set; }
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? GeometrySourceHandle { get; set; }
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? GeometrySourceLayer { get; set; }

        /// <summary>Reference geometry vertices for Road / Plant Fence / Unit Boundary elements, in the
        /// existing accepted <see cref="PlotGeometryVertex"/> shape. Null for Equipment.</summary>
        [DataMember(Order = 14, EmitDefaultValue = false)] public List<PlotGeometryVertex>? Vertices { get; set; }

        [DataMember(Order = 15, EmitDefaultValue = false)] public string? EngineeringMeasurementBasisKey { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? EngineeringApplicabilityKey { get; set; }

        /// <summary>Existing accepted placement status vocabulary (Incomplete / Complete / Ambiguous) for
        /// Equipment; the association status vocabulary for reference-geometry elements.</summary>
        [DataMember(Order = 17, EmitDefaultValue = false)] public string? ResolutionStatus { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public string? ResolutionReason { get; set; }
    }

    /// <summary>
    /// Host-free, facility-oriented read-only view of one Plot Plan for the bounded use case. A projection
    /// over the existing accepted <see cref="PlotPlanModel"/>: no field is moved, renamed, re-typed, or
    /// re-computed. Non-goals include footprint, orientation, maintenance envelope, process layout, pipe
    /// routing, underground utilities and any facility category outside the bounded four.
    /// </summary>
    [DataContract]
    public sealed class PlantModelV1
    {
        /// <summary>The accepted Plot Plan this model is projected from, held by reference. Ignored on
        /// serialization so PlantModel v1 output stays bounded and can never alter the accepted
        /// PlotPlanQaReport / PlotPlanModel serialized shape.</summary>
        [IgnoreDataMember] public PlotPlanModel? Source { get; set; }

        [DataMember(Order = 1)] public List<FacilityElement> FacilityElements { get; set; } = new List<FacilityElement>();

        [DataMember(Order = 2)] public PlantModelDiagnostics Diagnostics { get; set; } = new PlantModelDiagnostics();
    }

    /// <summary>
    /// Deterministic extraction-quality diagnostics. Counts mirror the existing
    /// <see cref="PlotPlanStatistics"/> / <see cref="PlotGeometryStatistics"/> /
    /// <see cref="PlotSpatialMeasurementStatistics"/> style and are never recomputed from new evidence.
    /// Unsupported items are reported explicitly, never silently discarded.
    /// </summary>
    [DataContract]
    public sealed class PlantModelDiagnostics
    {
        [DataMember(Order = 1)] public int EquipmentElementCount { get; set; }
        [DataMember(Order = 2)] public int RoadElementCount { get; set; }
        [DataMember(Order = 3)] public int PlantFenceElementCount { get; set; }
        [DataMember(Order = 4)] public int UnitBoundaryElementCount { get; set; }
        [DataMember(Order = 5)] public int UnsupportedElementCount { get; set; }

        [DataMember(Order = 6)] public int HighConfidenceCount { get; set; }
        [DataMember(Order = 7)] public int MediumConfidenceCount { get; set; }
        [DataMember(Order = 8)] public int LowConfidenceCount { get; set; }
        [DataMember(Order = 9)] public int NoneConfidenceCount { get; set; }

        /// <summary>Equipment placements with no resolved coordinate evidence (existing
        /// Incomplete/Missing semantics).</summary>
        [DataMember(Order = 10)] public int EquipmentWithUnresolvedCoordinateCount { get; set; }

        /// <summary>Road / Plant Fence / Unit Boundary subjects whose reference geometry could not be
        /// resolved to exactly one primitive (the existing ResolvePrimitive invariant).</summary>
        [DataMember(Order = 11)] public int ReferenceGeometryUnresolvedCount { get; set; }

        /// <summary>Equipment placements whose classification the existing evidence did not prove.</summary>
        [DataMember(Order = 12)] public int AmbiguousClassificationCount { get; set; }

        /// <summary>Total resolved engineering spatial measurement evidence available in the source model,
        /// reported verbatim from <see cref="PlotSpatialMeasurementStatistics"/>.</summary>
        [DataMember(Order = 13)] public int ResolvedMeasurementEvidenceCount { get; set; }

        /// <summary>Deterministic, Ordinal-ordered reasons a usable facility element could not be produced.
        /// Each entry names the existing status vocabulary that produced it.</summary>
        [DataMember(Order = 14)] public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
