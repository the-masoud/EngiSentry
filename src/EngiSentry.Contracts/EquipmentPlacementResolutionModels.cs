using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Engineer-assisted equipment placement resolution contracts.
    ///
    /// These record a single human engineering observation: an engineer explicitly picked the
    /// placement of an unresolved equipment identity. They never create production approval,
    /// authority policy, trusted-issuer status, private-key authority, or automatic rule
    /// activation; those remain the exclusive domain of the existing P8/P9 mechanisms. The
    /// resolution is bound to the exact drawing hash, equipment identity, tag, stored
    /// coordinates, insertion units, and the governed project-engineer review envelope.
    /// </summary>
    public static class EquipmentPlacementResolutionSchema
    {
        public const string Current = "EPR-1";
    }

    /// <summary>The two accepted engineer pick paths. No snapping, inference, or nearest-entity search is used.</summary>
    public static class EquipmentPlacementResolutionSources
    {
        /// <summary>An explicit model-space entity pick: the picked WCS point plus the proven handle and layer.</summary>
        public const string EntityPick = "ENTITY_PICK";
        /// <summary>An explicit free-space point: the picked WCS point only, with no source handle.</summary>
        public const string FreeSpace = "FREE_SPACE";

        public static readonly IReadOnlyList<string> All = new[] { EntityPick, FreeSpace };
    }

    public static class EquipmentPlacementResolutionStates
    {
        public const string Active = "ACTIVE";
        public const string Superseded = "SUPERSEDED";

        public static readonly IReadOnlyList<string> All = new[] { Active, Superseded };
    }

    /// <summary>
    /// One append-only engineer confirmation. Prior confirmations are never overwritten: a new
    /// confirmation produces a new <see cref="RecordId"/> and a new record, while every earlier
    /// record remains on disk as history.
    /// </summary>
    [DataContract]
    public sealed class EquipmentPlacementResolutionRecord
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = EquipmentPlacementResolutionSchema.Current;
        /// <summary>Distinct per confirmation: SHA-256 over ResolutionId + RequestSha256 + ResponseId + reviewer + UTC timestamp.</summary>
        [DataMember(Order = 2)] public string RecordId { get; set; } = string.Empty;
        /// <summary>SHA-256 over DrawingSha256 + EquipmentEntityId + exact stored Easting + Northing + InsertionUnits only.</summary>
        [DataMember(Order = 3)] public string ResolutionId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 8)] public decimal Easting { get; set; }
        [DataMember(Order = 9)] public decimal Northing { get; set; }
        [DataMember(Order = 10)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string Source { get; set; } = string.Empty;
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SourceHandle { get; set; }
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? SourceLayer { get; set; }
        [DataMember(Order = 14)] public double PickedPointX { get; set; }
        [DataMember(Order = 15)] public double PickedPointY { get; set; }
        [DataMember(Order = 16)] public double PickedPointZ { get; set; }
        [DataMember(Order = 17)] public string ReviewedCaseId { get; set; } = string.Empty;
        [DataMember(Order = 18)] public string ReviewRequestId { get; set; } = string.Empty;
        [DataMember(Order = 19)] public string RequestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 20)] public string ResponseId { get; set; } = string.Empty;
        [DataMember(Order = 21)] public string ReviewerDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 22)] public string ReviewerRole { get; set; } = string.Empty;
        [DataMember(Order = 23)] public DateTime ResponseTimestampUtc { get; set; }
        [DataMember(Order = 24)] public DateTime RecordedTimestampUtc { get; set; }
        [DataMember(Order = 25)] public string State { get; set; } = EquipmentPlacementResolutionStates.Active;
    }

    /// <summary>
    /// A trusted, already-validated resolution projection. Only the resolution service produces
    /// these, after verifying the record identity, the ACTIVE state, the drawing-hash binding,
    /// the insertion-unit validity, and the governed review envelope.
    /// </summary>
    public sealed class EquipmentPlacementResolution
    {
        public string ResolutionId { get; set; } = string.Empty;
        public string RecordId { get; set; } = string.Empty;
        public string EquipmentEntityId { get; set; } = string.Empty;
        public string NormalizedTag { get; set; } = string.Empty;
        public decimal Easting { get; set; }
        public decimal Northing { get; set; }
        public string InsertionUnits { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? SourceHandle { get; set; }
        public string? SourceLayer { get; set; }
        public string ReviewerDisplayName { get; set; } = string.Empty;
        public string ReviewerRole { get; set; } = string.Empty;
        public DateTime ResponseTimestampUtc { get; set; }
    }

    /// <summary>Provenance carried on a placement that was made Complete by an explicit engineer pick.</summary>
    [DataContract]
    public sealed class EquipmentPlacementEngineerResolution
    {
        [DataMember(Order = 1)] public string ResolutionId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RecordId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Source { get; set; } = string.Empty;
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? SourceHandle { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? SourceLayer { get; set; }
        [DataMember(Order = 6)] public string ReviewerDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string ReviewerRole { get; set; } = string.Empty;
        [DataMember(Order = 8)] public DateTime ResponseTimestampUtc { get; set; }
    }
}
