using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class PlotPlanModel
    {
        [DataMember(Order = 1)] public SemanticDrawing? Drawing { get; set; }
        [DataMember(Order = 2)] public List<EquipmentPlacement> EquipmentPlacements { get; set; } = new List<EquipmentPlacement>();
        [DataMember(Order = 3)] public PlotPlanStatistics Statistics { get; set; } = new PlotPlanStatistics();
        [DataMember(Order = 4)] public PlotGeometryModel Geometry { get; set; } = new PlotGeometryModel();
    }

    [DataContract]
    public sealed class EquipmentPlacement
    {
        [DataMember(Order = 1)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string TagSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string? TagSourceLayer { get; set; }
        [DataMember(Order = 6)] public double TagPositionX { get; set; }
        [DataMember(Order = 7)] public double TagPositionY { get; set; }
        [DataMember(Order = 8)] public double TagPositionZ { get; set; }
        [DataMember(Order = 9)] public double TagTextHeight { get; set; }
        [DataMember(Order = 10)] public double TagRotation { get; set; }
        [DataMember(Order = 11)] public double AssociationScale { get; set; }
        [DataMember(Order = 12)] public CoordinateAssociation Easting { get; set; } = new CoordinateAssociation { Axis = "E" };
        [DataMember(Order = 13)] public CoordinateAssociation Northing { get; set; } = new CoordinateAssociation { Axis = "N" };
        [DataMember(Order = 14)] public string PlacementStatus { get; set; } = "Incomplete";
    }

    [DataContract]
    public sealed class CoordinateAssociation
    {
        [DataMember(Order = 1)] public string Axis { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Status { get; set; } = "Missing";
        [DataMember(Order = 3)] public string? SelectedSourceHandle { get; set; }
        [DataMember(Order = 4)] public decimal? SelectedValue { get; set; }
        [DataMember(Order = 5)] public string? SelectedRawText { get; set; }
        [DataMember(Order = 6)] public string? SelectedSourceLayer { get; set; }
        [DataMember(Order = 7)] public double? SelectedDistance { get; set; }
        [DataMember(Order = 8)] public List<CoordinateCandidate> Candidates { get; set; } = new List<CoordinateCandidate>();
    }

    [DataContract]
    public sealed class CoordinateCandidate
    {
        [DataMember(Order = 1)] public string SourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string? RawText { get; set; }
        [DataMember(Order = 3)] public string? SourceLayer { get; set; }
        [DataMember(Order = 4)] public decimal Value { get; set; }
        [DataMember(Order = 5)] public double PositionX { get; set; }
        [DataMember(Order = 6)] public double PositionY { get; set; }
        [DataMember(Order = 7)] public double PositionZ { get; set; }
        [DataMember(Order = 8)] public double Distance { get; set; }
    }

    [DataContract]
    public sealed class PlotPlanStatistics
    {
        [DataMember(Order = 1)] public int EquipmentPlacementCount { get; set; }
        [DataMember(Order = 2)] public int CompletePlacementCount { get; set; }
        [DataMember(Order = 3)] public int IncompletePlacementCount { get; set; }
        [DataMember(Order = 4)] public int AmbiguousPlacementCount { get; set; }
        [DataMember(Order = 5)] public int ResolvedEastingCount { get; set; }
        [DataMember(Order = 6)] public int ResolvedNorthingCount { get; set; }
    }

    [DataContract]
    public sealed class PlotPlanQaReport
    {
        [DataMember(Order = 1)] public PlotPlanModel PlotPlan { get; set; } = new PlotPlanModel();
        [DataMember(Order = 2)] public QaReport Qa { get; set; } = new QaReport();
    }

    [DataContract]
    public sealed class PlotGeometryCapture
    {
        [DataMember(Order = 1)] public List<PlotGeometryPrimitive> Primitives { get; set; } = new List<PlotGeometryPrimitive>();
        [DataMember(Order = 2)] public List<PlotTextSourceRecord> ModelSpaceTextSources { get; set; } = new List<PlotTextSourceRecord>();
    }
    [DataContract]
    public sealed class PlotTextSourceRecord
    {
        [DataMember(Order = 1)] public string SourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string? SourceLayer { get; set; }
        [DataMember(Order = 3)] public double PositionX { get; set; }
        [DataMember(Order = 4)] public double PositionY { get; set; }
        [DataMember(Order = 5)] public double PositionZ { get; set; }
        [DataMember(Order = 6)] public double Height { get; set; }
    }
    [DataContract]
    public sealed class PlotGeometryPrimitive
    {
        [DataMember(Order = 1)] public string SourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceLayer { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string GeometryKind { get; set; } = string.Empty;
        [DataMember(Order = 4)] public bool Closed { get; set; }
        [DataMember(Order = 5)] public List<PlotGeometryVertex> Vertices { get; set; } = new List<PlotGeometryVertex>();
    }
    [DataContract]
    public sealed class PlotGeometryVertex { [DataMember(Order = 1)] public double X { get; set; } [DataMember(Order = 2)] public double Y { get; set; } [DataMember(Order = 3)] public double Z { get; set; } }
    [DataContract]
    public sealed class PlotGeometryModel
    {
        [DataMember(Order = 1)] public List<PlotGeometryPrimitive> Primitives { get; set; } = new List<PlotGeometryPrimitive>();
        [DataMember(Order = 2)] public List<GeometryAssociation> Associations { get; set; } = new List<GeometryAssociation>();
        [DataMember(Order = 3)] public PlotGeometryStatistics Statistics { get; set; } = new PlotGeometryStatistics();
    }
    [DataContract]
    public sealed class GeometryAssociation
    {
        [DataMember(Order = 1)] public string AssociationId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? LabelRawText { get; set; }
        [DataMember(Order = 4)] public string LabelSourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string? LabelSourceLayer { get; set; }
        [DataMember(Order = 6)] public double LabelPositionX { get; set; }
        [DataMember(Order = 7)] public double LabelPositionY { get; set; }
        [DataMember(Order = 8)] public double LabelPositionZ { get; set; }
        [DataMember(Order = 9)] public double AssociationScale { get; set; }
        [DataMember(Order = 10)] public string Status { get; set; } = "Missing";
        [DataMember(Order = 11)] public string Reason { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string? SelectedGeometryHandle { get; set; }
        [DataMember(Order = 13)] public double? SelectedDistance { get; set; }
        [DataMember(Order = 14)] public List<GeometryCandidate> Candidates { get; set; } = new List<GeometryCandidate>();
    }
    [DataContract]
    public sealed class GeometryCandidate
    {
        [DataMember(Order = 1)] public string SourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceLayer { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string GeometryKind { get; set; } = string.Empty;
        [DataMember(Order = 4)] public bool Closed { get; set; }
        [DataMember(Order = 5)] public int VertexCount { get; set; }
        [DataMember(Order = 6)] public double Distance { get; set; }
    }
    [DataContract]
    public sealed class PlotGeometryStatistics
    {
        [DataMember(Order = 1)] public int SupportedPrimitiveCount { get; set; }
        [DataMember(Order = 2)] public int GeometryAssociationCount { get; set; }
        [DataMember(Order = 3)] public int RoadAssociationCount { get; set; }
        [DataMember(Order = 4)] public int BoundaryAssociationCount { get; set; }
        [DataMember(Order = 5)] public int ResolvedAssociationCount { get; set; }
        [DataMember(Order = 6)] public int MissingAssociationCount { get; set; }
        [DataMember(Order = 7)] public int AmbiguousAssociationCount { get; set; }
    }
}
