using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// P14 authoritative equipment coordinate package contract. A package is exactly two files:
    /// <c>equipment-coordinate-manifest.json</c> and <c>equipment-coordinate-schedule.csv</c>. The
    /// manifest declares the drawing identity, the unit basis, and the coordinate basis; the schedule
    /// carries one row per equipment occurrence. EngiSentry never invents or infers a coordinate: a
    /// coordinate is trusted only when it arrives through a fully valid, drawing-bound package.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateSchema
    {
        public const string Current = "ENGISENTRY-EQUIPMENT-COORDINATES-1";

        public const string ManifestFileName = "equipment-coordinate-manifest.json";
        public const string ScheduleFileName = "equipment-coordinate-schedule.csv";
        public const string ReceiptFileName = "import-receipt.json";
        public const string ActiveFileName = "active.json";

        /// <summary>The only supported coordinate unit for an authoritative package.</summary>
        public const string CoordinateUnitsMeters = "Meters";

        /// <summary>The only supported coordinate basis: the pilot drawing's own WCS, untransformed.</summary>
        public const string CoordinateBasisPilotWcs = "PILOT_WCS";
    }

    /// <summary>Named strict-validation outcomes. A package activates only on <see cref="Valid"/>.</summary>
    public static class AuthoritativeEquipmentCoordinateValidationStatuses
    {
        public const string Valid = "VALID";
        public const string InvalidSchema = "INVALID_SCHEMA";
        public const string InvalidManifest = "INVALID_MANIFEST";
        public const string DrawingMismatch = "DRAWING_MISMATCH";
        public const string UnsupportedUnits = "UNSUPPORTED_UNITS";
        public const string UnsupportedCoordinateBasis = "UNSUPPORTED_COORDINATE_BASIS";
        public const string InvalidSchedule = "INVALID_SCHEDULE";
        public const string InvalidCoordinate = "INVALID_COORDINATE";
        public const string OccurrenceMismatch = "OCCURRENCE_MISMATCH";
        public const string DuplicateOccurrence = "DUPLICATE_OCCURRENCE";
        public const string IncompleteCoverage = "INCOMPLETE_COVERAGE";
        public const string PathInvalid = "PATH_INVALID";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Valid, InvalidSchema, InvalidManifest, DrawingMismatch, UnsupportedUnits,
            UnsupportedCoordinateBasis, InvalidSchedule, InvalidCoordinate, OccurrenceMismatch,
            DuplicateOccurrence, IncompleteCoverage, PathInvalid
        };
    }

    /// <summary>Outcome of an installation attempt. Only <see cref="Installed"/> activates a new package.</summary>
    public static class AuthoritativeEquipmentCoordinateInstallationStatuses
    {
        public const string Installed = "INSTALLED";
        public const string AlreadyActive = "ALREADY_ACTIVE";
        public const string Rejected = "REJECTED";
        public const string Declined = "DECLINED";
    }

    /// <summary>The single coordinate source type this phase produces.</summary>
    public static class AuthoritativeCoordinateSourceTypes
    {
        public const string AuthoritativeProjectSchedule = "AUTHORITATIVE_PROJECT_SCHEDULE";
    }

    /// <summary>The only supported transform: identity (no CRS change, no fitted transform).</summary>
    public static class AuthoritativeCoordinateTransformTypes
    {
        public const string Identity = "IDENTITY";
    }

    /// <summary>Placement-level authoritative evidence statuses.</summary>
    public static class AuthoritativePlacementStatuses
    {
        public const string Resolved = "RESOLVED";
        public const string Conflict = "COORDINATE_EVIDENCE_CONFLICT";
    }

    /// <summary>
    /// The package manifest. Exactly twelve fields are required. Eleven must be non-empty; only
    /// <see cref="Notes"/> may be empty (but its key must still be present). The provenance fields
    /// (<see cref="IssuedBy"/>, <see cref="IssuedByRole"/>, <see cref="IssuedTimestampUtc"/>,
    /// <see cref="SourceReference"/>) identify who issued the coordinates; EngiSentry records them
    /// verbatim and never claims cryptographic proof of them.
    /// </summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinateManifest
    {
        [DataMember(Order = 1)] public string Schema { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string CoordinateUnits { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string CoordinateBasis { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ScheduleFileName { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string ProjectName { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string IssuedBy { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string IssuedByRole { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string IssuedTimestampUtc { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string SourceReference { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string Notes { get; set; } = string.Empty;

        // P14.1 engineer-facing XLSX provenance. Every member is optional and omitted when unset, so a
        // package that did not originate from a workbook serializes to exactly the same bytes as before.
        // These carry the human source-document metadata the engineer typed into the workbook.
        //
        // SourceWorkbookSha256 is deliberately NOT part of the manifest: the manifest is the semantic
        // normalized package, and PackageFingerprint is derived from it. The exact .xlsx file identity is
        // recorded on the receipt and the active package instead, so re-saving the same content in Excel
        // never changes the semantic package identity.

        [DataMember(Order = 14, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? SourceRevision { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? SourceIssueStatus { get; set; }
        /// <summary>The source document issue date, exactly <c>YYYY-MM-DD</c>.</summary>
        [DataMember(Order = 17, EmitDefaultValue = false)] public string? IssueDate { get; set; }

        /// <summary>The eleven manifest fields that must be non-empty.</summary>
        public static readonly IReadOnlyList<string> RequiredNonEmptyFields = new[]
        {
            "Schema", "DrawingName", "DrawingSha256", "CoordinateUnits", "CoordinateBasis",
            "ScheduleFileName", "ProjectName", "IssuedBy", "IssuedByRole", "IssuedTimestampUtc",
            "SourceReference"
        };

        /// <summary>The four provenance fields that must be present for the package to be trusted.</summary>
        public static readonly IReadOnlyList<string> ProvenanceFields = new[]
        {
            "IssuedBy", "IssuedByRole", "IssuedTimestampUtc", "SourceReference"
        };
    }

    /// <summary>
    /// A single schedule row as read from the CSV. Values are kept as raw text so validation can
    /// reject malformed input rather than silently coercing it.
    /// </summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinateScheduleRow
    {
        [DataMember(Order = 1)] public string OccurrenceId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EquipmentTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Easting { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Northing { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string Elevation { get; set; } = string.Empty;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? OccurrenceIndex { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? SourceEntityHandle { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? Layer { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? CurrentPlacementStatus { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? CurrentPlacementReason { get; set; }
    }

    /// <summary>
    /// A resolved authoritative coordinate for one equipment occurrence. Both the occurrence identity
    /// and the equipment tag are carried so a consumer must match both; the tag alone is never enough.
    /// </summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinate
    {
        [DataMember(Order = 1)] public string OccurrenceId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EquipmentTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal Easting { get; set; }
        [DataMember(Order = 4)] public decimal Northing { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public decimal? Elevation { get; set; }

        [DataMember(Order = 6)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string PackageFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string ManifestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string ScheduleSha256 { get; set; } = string.Empty;

        [DataMember(Order = 10)] public string CoordinateSourceType { get; set; } = AuthoritativeCoordinateSourceTypes.AuthoritativeProjectSchedule;
        [DataMember(Order = 11)] public string SourceUnits { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string SourceCoordinateBasis { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string TransformType { get; set; } = AuthoritativeCoordinateTransformTypes.Identity;
        [DataMember(Order = 14)] public string IssuedBy { get; set; } = string.Empty;
        [DataMember(Order = 15)] public string SourceReference { get; set; } = string.Empty;
        [DataMember(Order = 16)] public string EvidenceSummary { get; set; } = string.Empty;

        // P14.1 provenance carried through from the workbook the coordinate was normalized out of.

        [DataMember(Order = 17, EmitDefaultValue = false)] public string? SourceWorkbookSha256 { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 19, EmitDefaultValue = false)] public string? SourceRevision { get; set; }
        [DataMember(Order = 20, EmitDefaultValue = false)] public string? SourceIssueStatus { get; set; }
        [DataMember(Order = 21, EmitDefaultValue = false)] public string? IssueDate { get; set; }
    }

    /// <summary>The structured result of strict package validation. Nothing activates unless <see cref="IsValid"/>.</summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinateValidationResult
    {
        [DataMember(Order = 1)] public string Status { get; set; } = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest;
        [DataMember(Order = 2)] public List<string> Messages { get; set; } = new List<string>();
        [DataMember(Order = 3)] public string ManifestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string ScheduleSha256 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string PackageFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 6)] public List<AuthoritativeEquipmentCoordinate> Coordinates { get; set; } = new List<AuthoritativeEquipmentCoordinate>();
        [DataMember(Order = 7)] public int ExpectedOccurrenceCount { get; set; }
        [DataMember(Order = 8)] public int ResolvedOccurrenceCount { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? DrawingName { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? IssuedBy { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? SourceReference { get; set; }

        [IgnoreDataMember] public bool IsValid => string.Equals(Status, AuthoritativeEquipmentCoordinateValidationStatuses.Valid, System.StringComparison.Ordinal);
    }

    /// <summary>The persisted receipt for an activated package. Historical packages keep their receipt.</summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinateImportReceipt
    {
        [DataMember(Order = 1)] public string Schema { get; set; } = AuthoritativeEquipmentCoordinateSchema.Current;
        [DataMember(Order = 2)] public string Status { get; set; } = AuthoritativeEquipmentCoordinateInstallationStatuses.Installed;
        [DataMember(Order = 3)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ManifestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ScheduleSha256 { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string PackageFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string ImportedTimestampUtc { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string IssuedBy { get; set; } = string.Empty;
        [DataMember(Order = 10)] public string SourceReference { get; set; } = string.Empty;
        [DataMember(Order = 11)] public int OccurrenceCount { get; set; }
        [DataMember(Order = 12)] public string PackageDirectory { get; set; } = string.Empty;
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? PreviousPackageFingerprint { get; set; }

        // P14.1: present only when the package was normalized from an engineer-facing XLSX workbook.
        // SourceWorkbookSha256 identifies the exact .xlsx file bytes; PackageFingerprint still identifies
        // the semantic normalized authoritative package. The two are deliberately distinct.

        [DataMember(Order = 14, EmitDefaultValue = false)] public string? SourceWorkbookSha256 { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? RequestFingerprint { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 17, EmitDefaultValue = false)] public string? SourceRevision { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public string? SourceIssueStatus { get; set; }
        [DataMember(Order = 19, EmitDefaultValue = false)] public string? IssueDate { get; set; }
    }

    /// <summary>The single active package for one drawing. Written atomically; older packages are never deleted.</summary>
    [DataContract]
    public sealed class AuthoritativeEquipmentCoordinateActivePackage
    {
        [DataMember(Order = 1)] public string Schema { get; set; } = AuthoritativeEquipmentCoordinateSchema.Current;
        [DataMember(Order = 2)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string PackageFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string ManifestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ScheduleSha256 { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string PackageDirectory { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string ActivatedTimestampUtc { get; set; } = string.Empty;
        [DataMember(Order = 8)] public int OccurrenceCount { get; set; }
        [DataMember(Order = 9)] public string IssuedBy { get; set; } = string.Empty;

        // P14.1: present only when the active package was normalized from an engineer-facing XLSX workbook.

        [DataMember(Order = 10, EmitDefaultValue = false)] public string? SourceWorkbookSha256 { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? RequestFingerprint { get; set; }
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? SourceRevision { get; set; }
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? SourceIssueStatus { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? IssueDate { get; set; }
    }

    /// <summary>
    /// One occurrence of the current drawing's equipment inventory. <see cref="OccurrenceId"/> is the
    /// existing stable canonical occurrence identity (<c>EquipmentPlacement.PlacementId</c>); the tag
    /// is carried alongside so validation can require an exact occurrence-plus-tag binding. The
    /// remaining fields are informational context for the authority and never affect validation.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateInventoryEntry
    {
        public string OccurrenceId { get; set; } = string.Empty;
        public string EquipmentTag { get; set; } = string.Empty;
        public int OccurrenceIndex { get; set; }
        public string SourceEntityHandle { get; set; } = string.Empty;
        public string Layer { get; set; } = string.Empty;
        public string CurrentPlacementStatus { get; set; } = string.Empty;
        public string CurrentPlacementReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Placement-level provenance for an authoritative coordinate. This must survive into QA
    /// evidence so any downstream consumer can see exactly which package resolved the placement.
    /// </summary>
    [DataContract]
    public sealed class AuthoritativeCoordinatePlacementEvidence
    {
        [DataMember(Order = 1)] public string PlacementStatus { get; set; } = AuthoritativePlacementStatuses.Resolved;
        [DataMember(Order = 2)] public string CoordinateSourceType { get; set; } = AuthoritativeCoordinateSourceTypes.AuthoritativeProjectSchedule;
        [DataMember(Order = 3)] public string SourceUnits { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string SourceCoordinateBasis { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string TransformType { get; set; } = AuthoritativeCoordinateTransformTypes.Identity;
        [DataMember(Order = 6)] public string OccurrenceId { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string EquipmentTag { get; set; } = string.Empty;
        [DataMember(Order = 8, EmitDefaultValue = false)] public decimal? Easting { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public decimal? Northing { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public decimal? Elevation { get; set; }
        [DataMember(Order = 11)] public string PackageFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string ManifestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string ScheduleSha256 { get; set; } = string.Empty;
        [DataMember(Order = 14)] public string IssuedBy { get; set; } = string.Empty;
        [DataMember(Order = 15)] public string SourceReference { get; set; } = string.Empty;
        [DataMember(Order = 16)] public string EvidenceSummary { get; set; } = string.Empty;
        [DataMember(Order = 17, EmitDefaultValue = false)] public string? ConflictWithEvidenceSource { get; set; }

        // P14.1 provenance required by the placement contract. SourceX/Y/Z are the coordinate exactly as
        // it arrived from the authority; FinalWcsX/Y/Z are the values actually placed in the drawing.
        // P14.1 performs no coordinate transform, so the two are equal and TransformType is IDENTITY.

        [DataMember(Order = 18, EmitDefaultValue = false)] public string? SourceWorkbookSha256 { get; set; }
        [DataMember(Order = 19, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 20, EmitDefaultValue = false)] public string? SourceRevision { get; set; }
        [DataMember(Order = 21, EmitDefaultValue = false)] public string? SourceIssueStatus { get; set; }
        [DataMember(Order = 22, EmitDefaultValue = false)] public string? IssueDate { get; set; }
        [DataMember(Order = 23, EmitDefaultValue = false)] public decimal? SourceX { get; set; }
        [DataMember(Order = 24, EmitDefaultValue = false)] public decimal? SourceY { get; set; }
        [DataMember(Order = 25, EmitDefaultValue = false)] public decimal? SourceZ { get; set; }
        [DataMember(Order = 26, EmitDefaultValue = false)] public decimal? FinalWcsX { get; set; }
        [DataMember(Order = 27, EmitDefaultValue = false)] public decimal? FinalWcsY { get; set; }
        [DataMember(Order = 28, EmitDefaultValue = false)] public decimal? FinalWcsZ { get; set; }
    }
}
