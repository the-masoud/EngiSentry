using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringEntityIndex
    {
        [DataMember(Order = 1)] public SemanticDrawing? Drawing { get; set; }
        [DataMember(Order = 2)] public List<EquipmentEntity> EquipmentEntities { get; set; } = new List<EquipmentEntity>();
        [DataMember(Order = 3)] public List<StructureEntity> StructureEntities { get; set; } = new List<StructureEntity>();
        [DataMember(Order = 4)] public List<GroupedTagLink> GroupedTagLinks { get; set; } = new List<GroupedTagLink>();
        [DataMember(Order = 5)] public List<UnresolvedLink> UnresolvedLinks { get; set; } = new List<UnresolvedLink>();
        [DataMember(Order = 6)] public List<AmbiguousLink> AmbiguousLinks { get; set; } = new List<AmbiguousLink>();
        [DataMember(Order = 7)] public EntityIndexStatistics Statistics { get; set; } = new EntityIndexStatistics();
    }

    [DataContract]
    public sealed class EquipmentEntity
    {
        [DataMember(Order = 1)] public string CanonicalId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? EquipmentPrefix { get; set; }
        [DataMember(Order = 4)] public string? UnitNumber { get; set; }
        [DataMember(Order = 5)] public string? SequenceNumber { get; set; }
        [DataMember(Order = 6)] public string? Suffix { get; set; }
        [DataMember(Order = 7)] public List<EquipmentOccurrence> Occurrences { get; set; } = new List<EquipmentOccurrence>();
        [DataMember(Order = 8)] public List<EquipmentListLink> EquipmentListLinks { get; set; } = new List<EquipmentListLink>();
        [DataMember(Order = 9)] public bool HasDrawingEvidence { get; set; }
        [DataMember(Order = 10)] public bool HasEquipmentListEvidence { get; set; }
    }

    [DataContract]
    public sealed class EquipmentOccurrence : SemanticEvidence
    {
        [DataMember(Order = 20)] public string? RawTag { get; set; }
        [DataMember(Order = 21)] public string? NormalizedTag { get; set; }
        [DataMember(Order = 22)] public string? EquipmentPrefix { get; set; }
        [DataMember(Order = 23)] public string? UnitNumber { get; set; }
        [DataMember(Order = 24)] public string? SequenceNumber { get; set; }
        [DataMember(Order = 25)] public string? Suffix { get; set; }
        [DataMember(Order = 26)] public string Role { get; set; } = "DrawingTag";
    }

    [DataContract]
    public sealed class EquipmentListLink
    {
        [DataMember(Order = 1)] public string SourceTag { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string? Description { get; set; }
        [DataMember(Order = 3)] public string? Installation { get; set; }
        [DataMember(Order = 4)] public List<string> SourceHandles { get; set; } = new List<string>();
        [DataMember(Order = 5)] public string LinkMethod { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class StructureEntity
    {
        [DataMember(Order = 1)] public string CanonicalId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? AreaOrUnitCode { get; set; }
        [DataMember(Order = 4)] public string? TypeCode { get; set; }
        [DataMember(Order = 5)] public string? SequenceNumber { get; set; }
        [DataMember(Order = 6)] public List<StructureTag> Occurrences { get; set; } = new List<StructureTag>();
    }

    [DataContract]
    public sealed class GroupedTagLink
    {
        [DataMember(Order = 1)] public string SourceGroupedTag { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<string> SourceHandles { get; set; } = new List<string>();
        [DataMember(Order = 3)] public List<string> MemberNormalizedTags { get; set; } = new List<string>();
        [DataMember(Order = 4)] public List<string> ResolvedEntityIds { get; set; } = new List<string>();
        [DataMember(Order = 5)] public List<string> UnresolvedMemberTags { get; set; } = new List<string>();
        [DataMember(Order = 6)] public string LinkMethod { get; set; } = "GroupedSuffixExpansion";
    }

    [DataContract]
    public sealed class UnresolvedLink
    {
        [DataMember(Order = 1)] public string SourceType { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceValue { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? ExpectedTargetIdentity { get; set; }
        [DataMember(Order = 4)] public string Reason { get; set; } = string.Empty;
        [DataMember(Order = 5)] public List<string> SourceHandles { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class AmbiguousLink
    {
        [DataMember(Order = 1)] public string SourceType { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceValue { get; set; } = string.Empty;
        [DataMember(Order = 3)] public List<string> CandidateEntityIds { get; set; } = new List<string>();
        [DataMember(Order = 4)] public string Reason { get; set; } = string.Empty;
        [DataMember(Order = 5)] public List<string> SourceHandles { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class EntityIndexStatistics
    {
        [DataMember(Order = 1)] public int EquipmentEntityCount { get; set; }
        [DataMember(Order = 2)] public int StructureEntityCount { get; set; }
        [DataMember(Order = 3)] public int EquipmentEntitiesWithDrawingEvidence { get; set; }
        [DataMember(Order = 4)] public int EquipmentEntitiesWithEquipmentListEvidence { get; set; }
        [DataMember(Order = 5)] public int EquipmentEntitiesWithBoth { get; set; }
        [DataMember(Order = 6)] public int GroupedTagCount { get; set; }
        [DataMember(Order = 7)] public int GroupedMemberLinkCount { get; set; }
        [DataMember(Order = 8)] public int UnresolvedLinkCount { get; set; }
        [DataMember(Order = 9)] public int AmbiguousLinkCount { get; set; }
    }
}
