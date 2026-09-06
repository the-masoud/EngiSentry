using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class DrawingSnapshot
    {
        [DataMember(Order = 1)] public string? FileName { get; set; }
        [DataMember(Order = 2)] public string? FullPath { get; set; }
        [DataMember(Order = 3)] public string? DwgVersion { get; set; }
        [DataMember(Order = 4)] public string? InsertionUnits { get; set; }
        [DataMember(Order = 5)] public string? CurrentLayout { get; set; }
        [DataMember(Order = 6)] public DateTime ScanTimestampUtc { get; set; }
        [DataMember(Order = 7)] public long ScanDurationMs { get; set; }
        [DataMember(Order = 8)] public int ModelSpaceEntityCount { get; set; }
        [DataMember(Order = 9)] public int PaperSpaceEntityCount { get; set; }
        [DataMember(Order = 10)] public int BlockDefinitionEntityCount { get; set; }
        [DataMember(Order = 11)] public int BlockReferenceCount { get; set; }
        [DataMember(Order = 12)] public int TextCount { get; set; }
        [DataMember(Order = 13)] public int MTextCount { get; set; }
        [DataMember(Order = 14)] public int DimensionCount { get; set; }
        [DataMember(Order = 15)] public List<LayerSnapshot> Layers { get; set; } = new List<LayerSnapshot>();
        [DataMember(Order = 16)] public List<BlockDefinitionSnapshot> BlockDefinitions { get; set; } = new List<BlockDefinitionSnapshot>();
        [DataMember(Order = 17)] public List<BlockReferenceSnapshot> BlockReferences { get; set; } = new List<BlockReferenceSnapshot>();
        [DataMember(Order = 18)] public List<TextSnapshot> TextEntities { get; set; } = new List<TextSnapshot>();
        [DataMember(Order = 19)] public List<DimensionSnapshot> Dimensions { get; set; } = new List<DimensionSnapshot>();
        [DataMember(Order = 20)] public List<XrefSnapshot> Xrefs { get; set; } = new List<XrefSnapshot>();
        [DataMember(Order = 21)] public List<LayoutSnapshot> Layouts { get; set; } = new List<LayoutSnapshot>();
        [DataMember(Order = 22)] public List<ExternalDependencySnapshot> ExternalDependencies { get; set; } = new List<ExternalDependencySnapshot>();
        [DataMember(Order = 23)] public int DbModBefore { get; set; }
        [DataMember(Order = 24)] public int DbModAfter { get; set; }
        [DataMember(Order = 25)] public bool DrawingModifiedByScan { get; set; }
        [DataMember(Order = 26)] public List<TextSnapshot> InstantiatedTextEntities { get; set; } = new List<TextSnapshot>();
    }

    [DataContract] public sealed class LayerSnapshot { [DataMember(Order=1)] public string? Name{get;set;} [DataMember(Order=2)] public bool IsOn{get;set;} [DataMember(Order=3)] public bool IsFrozen{get;set;} [DataMember(Order=4)] public bool IsLocked{get;set;} [DataMember(Order=5)] public short Color{get;set;} [DataMember(Order=6)] public string? Linetype{get;set;} [DataMember(Order=7)] public string? Lineweight{get;set;} [DataMember(Order=8)] public bool IsPlottable{get;set;} }
    [DataContract] public sealed class BlockDefinitionSnapshot { [DataMember(Order=1)] public string? Name{get;set;} [DataMember(Order=2)] public bool IsAnonymous{get;set;} [DataMember(Order=3)] public bool IsLayout{get;set;} [DataMember(Order=4)] public bool IsXref{get;set;} [DataMember(Order=5)] public int EntityCount{get;set;} }
    [DataContract] public sealed class BlockReferenceSnapshot { [DataMember(Order=1)] public string? BlockName{get;set;} [DataMember(Order=2)] public string? Handle{get;set;} [DataMember(Order=3)] public string? ObjectType{get;set;} [DataMember(Order=4)] public string? Layer{get;set;} [DataMember(Order=5)] public double PositionX{get;set;} [DataMember(Order=6)] public double PositionY{get;set;} [DataMember(Order=7)] public double PositionZ{get;set;} [DataMember(Order=8)] public double Rotation{get;set;} [DataMember(Order=9)] public double ScaleX{get;set;} [DataMember(Order=10)] public double ScaleY{get;set;} [DataMember(Order=11)] public double ScaleZ{get;set;} [DataMember(Order=12)] public List<AttributeSnapshot> Attributes{get;set;} = new List<AttributeSnapshot>(); }
    [DataContract] public sealed class AttributeSnapshot { [DataMember(Order=1)] public string? Tag{get;set;} [DataMember(Order=2)] public string? Value{get;set;} [DataMember(Order=3)] public string? Handle{get;set;} [DataMember(Order=4)] public string? ObjectType{get;set;} [DataMember(Order=5)] public string? Layer{get;set;} }
    [DataContract] public sealed class TextSnapshot { [DataMember(Order=1)] public string? Type{get;set;} [DataMember(Order=2)] public string? Handle{get;set;} [DataMember(Order=3)] public string? ObjectType{get;set;} [DataMember(Order=4)] public string? Layer{get;set;} [DataMember(Order=5)] public string? Text{get;set;} [DataMember(Order=6)] public double PositionX{get;set;} [DataMember(Order=7)] public double PositionY{get;set;} [DataMember(Order=8)] public double PositionZ{get;set;} [DataMember(Order=9)] public double Height{get;set;} [DataMember(Order=10)] public double Rotation{get;set;} [DataMember(Order=11)] public string? Style{get;set;} [DataMember(Order=12)] public string? SpaceKind{get;set;} }
    [DataContract] public sealed class DimensionSnapshot { [DataMember(Order=1)] public string? Handle{get;set;} [DataMember(Order=2)] public string? ObjectType{get;set;} [DataMember(Order=3)] public string? Layer{get;set;} [DataMember(Order=4)] public string? DimensionStyle{get;set;} [DataMember(Order=5)] public double? Measurement{get;set;} }
    [DataContract] public sealed class XrefSnapshot { [DataMember(Order=1)] public string? Name{get;set;} [DataMember(Order=2)] public string? RawPath{get;set;} [DataMember(Order=3)] public string? ResolvedPath{get;set;} [DataMember(Order=4)] public string? Status{get;set;} [DataMember(Order=5)] public bool IsResolved{get;set;} [DataMember(Order=6)] public bool IsNested{get;set;} }
    [DataContract] public sealed class LayoutSnapshot { [DataMember(Order=1)] public string? Name{get;set;} [DataMember(Order=2)] public bool IsModel{get;set;} [DataMember(Order=3)] public int TabOrder{get;set;} }
    [DataContract] public sealed class ExternalDependencySnapshot { [DataMember(Order=1)] public string? Kind{get;set;} [DataMember(Order=2)] public string? Name{get;set;} [DataMember(Order=3)] public string? RawPath{get;set;} [DataMember(Order=4)] public string? ResolvedPath{get;set;} [DataMember(Order=5)] public string? Status{get;set;} [DataMember(Order=6)] public bool? ExistsOnDisk{get;set;} }

    [DataContract] public sealed class EngineeringDrawingModel
    {
        [DataMember(Order=1)] public SemanticDrawing? Drawing{get;set;}
        [DataMember(Order=2)] public List<EquipmentTag> EquipmentTags{get;set;} = new List<EquipmentTag>();
        [DataMember(Order=3)] public List<StructureTag> StructureTags{get;set;} = new List<StructureTag>();
        [DataMember(Order=4)] public List<CoordinateEvidence> Coordinates{get;set;} = new List<CoordinateEvidence>();
        [DataMember(Order=5)] public List<ElevationEvidence> Elevations{get;set;} = new List<ElevationEvidence>();
        [DataMember(Order=6)] public List<TextEvidence> RoadLabels{get;set;} = new List<TextEvidence>();
        [DataMember(Order=7)] public List<BoundaryEvidence> BoundaryLabels{get;set;} = new List<BoundaryEvidence>();
        [DataMember(Order=8)] public List<EquipmentListEntry> EquipmentListEntries{get;set;} = new List<EquipmentListEntry>();
        [DataMember(Order=9)] public SemanticStatistics Statistics{get;set;} = new SemanticStatistics();
    }
    [DataContract] public sealed class SemanticDrawing { [DataMember(Order=1)] public string? FileName{get;set;} [DataMember(Order=2)] public string? FullPath{get;set;} [DataMember(Order=3)] public string? DwgVersion{get;set;} [DataMember(Order=4)] public string? CurrentLayout{get;set;} }
    [DataContract] public abstract class SemanticEvidence { [DataMember(Order=1)] public string? RawText{get;set;} [DataMember(Order=2)] public string? SourceHandle{get;set;} [DataMember(Order=3)] public string? SourceObjectType{get;set;} [DataMember(Order=4)] public string? SourceLayer{get;set;} [DataMember(Order=5)] public double PositionX{get;set;} [DataMember(Order=6)] public double PositionY{get;set;} [DataMember(Order=7)] public double PositionZ{get;set;} [DataMember(Order=8)] public string ParseStatus{get;set;} = "Parsed"; }
    [DataContract] public sealed class EquipmentTag : SemanticEvidence { [DataMember(Order=20)] public string? RawTag{get;set;} [DataMember(Order=21)] public string? NormalizedTag{get;set;} [DataMember(Order=22)] public string? EquipmentPrefix{get;set;} [DataMember(Order=23)] public string? UnitNumber{get;set;} [DataMember(Order=24)] public string? SequenceNumber{get;set;} [DataMember(Order=25)] public string? Suffix{get;set;} }
    [DataContract] public sealed class StructureTag : SemanticEvidence { [DataMember(Order=20)] public string? RawTag{get;set;} [DataMember(Order=21)] public string? NormalizedTag{get;set;} [DataMember(Order=22)] public string? AreaOrUnitCode{get;set;} [DataMember(Order=23)] public string? TypeCode{get;set;} [DataMember(Order=24)] public string? SequenceNumber{get;set;} }
    [DataContract] public sealed class CoordinateEvidence : SemanticEvidence { [DataMember(Order=20)] public string? Axis{get;set;} [DataMember(Order=21)] public decimal Value{get;set;} }
    [DataContract] public sealed class ElevationEvidence : SemanticEvidence { [DataMember(Order=20)] public string? ElevationType{get;set;} [DataMember(Order=21)] public decimal Value{get;set;} }
    [DataContract] public sealed class TextEvidence : SemanticEvidence { }
    [DataContract] public sealed class BoundaryEvidence : SemanticEvidence { [DataMember(Order=20)] public string BoundaryType{get;set;} = "UnitLimit"; [DataMember(Order=21)] public string? Axis{get;set;} [DataMember(Order=22)] public decimal? CoordinateValue{get;set;} }
    [DataContract] public sealed class EquipmentListEntry { [DataMember(Order=1)] public string? Tag{get;set;} [DataMember(Order=2)] public string? Description{get;set;} [DataMember(Order=3)] public string? Installation{get;set;} [DataMember(Order=4)] public List<string> SourceHandles{get;set;} = new List<string>(); [DataMember(Order=5)] public string ParseStatus{get;set;} = "Parsed"; }
    [DataContract] public sealed class SemanticStatistics { [DataMember(Order=1)] public int EquipmentTagOccurrences{get;set;} [DataMember(Order=2)] public int UniqueEquipmentTags{get;set;} [DataMember(Order=3)] public int StructureTagOccurrences{get;set;} [DataMember(Order=4)] public int UniqueStructureTags{get;set;} [DataMember(Order=5)] public int CoordinateCount{get;set;} [DataMember(Order=6)] public int ElevationCount{get;set;} [DataMember(Order=7)] public int RoadLabelCount{get;set;} [DataMember(Order=8)] public int BoundaryLabelCount{get;set;} [DataMember(Order=9)] public int EquipmentListEntryCount{get;set;} }
}
