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
}
