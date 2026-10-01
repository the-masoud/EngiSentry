using System.Collections.Generic;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// P14.1 engineer-facing XLSX coordinate workflow.
    ///
    /// The engineer receives exactly one file — <c>EngiSentry-Equipment-Coordinates.xlsx</c> — fills in
    /// the source-document metadata and the Easting/Northing (and optionally Elevation) for every
    /// equipment occurrence, saves the SAME file, and hands it back. JSON, CSV, manual hashing and
    /// fingerprint calculation are all internal: the engineer never sees any of them.
    ///
    /// This class holds the workbook-level contract constants only. It deliberately does not reference
    /// any spreadsheet library so the contract stays dependency-free.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateWorkbookSchema
    {
        /// <summary>The only supported workbook schema version.</summary>
        public const string Current = "ENGISENTRY-EQUIPMENT-COORDINATES-XLSX-1";

        /// <summary>The single user-facing file name. The engineer gets exactly this one file.</summary>
        public const string WorkbookFileName = "EngiSentry-Equipment-Coordinates.xlsx";

        /// <summary>The internal file name under which the exact imported workbook bytes are preserved.</summary>
        public const string SourceWorkbookFileName = "source-workbook.xlsx";

        /// <summary>The only supported workbook extension. .xls, .xlsm, .csv and .json are rejected.</summary>
        public const string WorkbookExtension = ".xlsx";

        /// <summary>The visible worksheet the engineer works in.</summary>
        public const string VisibleSheetName = "Equipment Coordinates";

        /// <summary>The internal worksheet, stored very hidden, that binds the workbook to the drawing.</summary>
        public const string InternalSheetName = "_EngiSentry";

        /// <summary>The single Excel table on the visible sheet.</summary>
        public const string TableName = "EngiSentryEquipmentCoordinates";

        // ---- Visible column headers (the table column names must match these exactly). ----

        /// <summary>Generated, read-only, informational row number.</summary>
        public const string ColumnNumber = "No.";
        /// <summary>Generated, read-only equipment tag.</summary>
        public const string ColumnEquipmentTag = "Equipment Tag";
        /// <summary>Generated, read-only description, filled only when deterministic existing evidence provides it.</summary>
        public const string ColumnDescription = "Description";
        /// <summary>Editable, required.</summary>
        public const string ColumnEasting = "Easting";
        /// <summary>Editable, required.</summary>
        public const string ColumnNorthing = "Northing";
        /// <summary>Editable, optional.</summary>
        public const string ColumnElevation = "Elevation";

        // ---- Hidden, locked row-identity columns. They travel with the row if Excel reorders rows. ----

        /// <summary>Hidden, locked. The canonical occurrence identity: <c>placement:equipment:&lt;TAG&gt;:&lt;HANDLE&gt;</c>.</summary>
        public const string ColumnOccurrenceId = "_EngiSentryOccurrenceId";
        /// <summary>Hidden, locked. The handle the equipment tag was read from. Informational only.</summary>
        public const string ColumnSourceEntityHandle = "_EngiSentrySourceEntityHandle";

        // ---- Workbook-defined names for the source-document metadata. Read by name, never by position. ----

        public const string NameProjectIdentifier = "ES_ProjectIdentifier";
        public const string NameSourceDocumentNumber = "ES_SourceDocumentNumber";
        public const string NameSourceRevision = "ES_SourceRevision";
        public const string NameSourceIssueStatus = "ES_SourceIssueStatus";
        public const string NameIssuedBy = "ES_IssuedBy";
        public const string NameIssueDate = "ES_IssueDate";
        public const string NameNotes = "ES_Notes";

        /// <summary>The six source-metadata fields the engineer must complete before import.</summary>
        public static readonly IReadOnlyList<string> RequiredSourceMetadataNames = new[]
        {
            NameProjectIdentifier, NameSourceDocumentNumber, NameSourceRevision,
            NameSourceIssueStatus, NameIssuedBy, NameIssueDate
        };

        /// <summary>Every source-metadata defined name, including the optional Notes field.</summary>
        public static readonly IReadOnlyList<string> AllSourceMetadataNames = new[]
        {
            NameProjectIdentifier, NameSourceDocumentNumber, NameSourceRevision,
            NameSourceIssueStatus, NameIssuedBy, NameIssueDate, NameNotes
        };

        // ---- Internal metadata keys stored on the _EngiSentry sheet as key/value pairs. ----

        public const string InternalKeySchemaVersion = "SchemaVersion";
        public const string InternalKeyDrawingFileName = "DrawingFileName";
        public const string InternalKeyDrawingSha256 = "DrawingSha256";
        public const string InternalKeyGeneratedUtc = "GeneratedUtc";
        public const string InternalKeyExpectedOccurrenceCount = "ExpectedOccurrenceCount";
        public const string InternalKeyRequestFingerprint = "RequestFingerprint";

        /// <summary>The six internal metadata keys that must be present and non-empty on import.</summary>
        public static readonly IReadOnlyList<string> RequiredInternalKeys = new[]
        {
            InternalKeySchemaVersion, InternalKeyDrawingFileName, InternalKeyDrawingSha256,
            InternalKeyGeneratedUtc, InternalKeyExpectedOccurrenceCount, InternalKeyRequestFingerprint
        };

        /// <summary>The only accepted IssueDate form. Locale-dependent dates are never accepted.</summary>
        public const string IssueDateFormat = "yyyy-MM-dd";
    }

    /// <summary>
    /// Named workbook-import outcomes. A workbook activates a package only on <see cref="Valid"/>.
    /// The names deliberately overlap the P14 package statuses where the check is the same check.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateWorkbookStatuses
    {
        public const string Valid = "VALID";
        public const string PathInvalid = "PATH_INVALID";
        public const string UnsupportedExtension = "UNSUPPORTED_EXTENSION";
        public const string InvalidXlsx = "INVALID_XLSX";
        public const string MissingVisibleSheet = "MISSING_VISIBLE_SHEET";
        public const string MissingInternalSheet = "MISSING_INTERNAL_SHEET";
        public const string MissingTable = "MISSING_TABLE";
        public const string MissingNamedField = "MISSING_NAMED_FIELD";
        public const string UnsupportedWorkbookSchema = "UNSUPPORTED_WORKBOOK_SCHEMA";
        public const string MissingInternalMetadata = "MISSING_INTERNAL_METADATA";
        public const string MissingSourceMetadata = "MISSING_SOURCE_METADATA";
        public const string InvalidIssueDate = "INVALID_ISSUE_DATE";
        public const string DrawingMismatch = "DRAWING_MISMATCH";
        public const string RequestCountMismatch = "REQUEST_COUNT_MISMATCH";
        public const string RequestFingerprintMismatch = "REQUEST_FINGERPRINT_MISMATCH";
        public const string RowIdentityMismatch = "ROW_IDENTITY_MISMATCH";
        public const string InvalidCoordinate = "INVALID_COORDINATE";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Valid, PathInvalid, UnsupportedExtension, InvalidXlsx, MissingVisibleSheet,
            MissingInternalSheet, MissingTable, MissingNamedField, UnsupportedWorkbookSchema,
            MissingInternalMetadata, MissingSourceMetadata, InvalidIssueDate, DrawingMismatch,
            RequestCountMismatch, RequestFingerprintMismatch, RowIdentityMismatch, InvalidCoordinate
        };
    }

    /// <summary>
    /// How a generated cell must be written. <see cref="Auto"/> is the production behaviour: blank when
    /// the raw text is empty, otherwise a real numeric cell. The remaining kinds exist so the focused
    /// negative matrix can emit the exact malformed cells a hand-edited workbook would contain.
    /// </summary>
    public enum AuthoritativeEquipmentCoordinateCellKind
    {
        /// <summary>Blank when the raw text is empty; otherwise a literal numeric cell.</summary>
        Auto = 0,
        /// <summary>Always write an empty cell.</summary>
        Blank = 1,
        /// <summary>Write a literal numeric cell.</summary>
        Number = 2,
        /// <summary>Write a text cell, even when the text looks numeric.</summary>
        Text = 3,
        /// <summary>Write a formula cell. Formulas are never evaluated and never accepted on import.</summary>
        Formula = 4,
        /// <summary>Write a formula-error cell such as <c>#VALUE!</c>.</summary>
        Error = 5
    }

    /// <summary>
    /// Fault switches used only by the focused negative matrix. A production request never sets any of
    /// them: they exist so a fixture can emit a structurally damaged workbook without a hand-written
    /// binary patch, and the reader must detect each one.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookDefects
    {
        public bool OmitVisibleSheet { get; set; }
        public bool OmitInternalSheet { get; set; }
        public bool OmitTable { get; set; }
        public bool OmitNamedMetadata { get; set; }
        public bool HideVisibleSheet { get; set; }
    }

    /// <summary>One row of the generated workbook. Coordinates are raw text so nothing is coerced early.</summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookRow
    {
        public int Number { get; set; }
        public string EquipmentTag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Easting { get; set; } = string.Empty;
        public string Northing { get; set; } = string.Empty;
        public string Elevation { get; set; } = string.Empty;
        public string OccurrenceId { get; set; } = string.Empty;
        public string SourceEntityHandle { get; set; } = string.Empty;

        public AuthoritativeEquipmentCoordinateCellKind EastingKind { get; set; } = AuthoritativeEquipmentCoordinateCellKind.Auto;
        public AuthoritativeEquipmentCoordinateCellKind NorthingKind { get; set; } = AuthoritativeEquipmentCoordinateCellKind.Auto;
        public AuthoritativeEquipmentCoordinateCellKind ElevationKind { get; set; } = AuthoritativeEquipmentCoordinateCellKind.Auto;
    }

    /// <summary>The complete content of one generated workbook, in workbook order.</summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookModel
    {
        public string SchemaVersion { get; set; } = AuthoritativeEquipmentCoordinateWorkbookSchema.Current;
        public string DrawingFileName { get; set; } = string.Empty;
        public string DrawingSha256 { get; set; } = string.Empty;
        public string GeneratedUtc { get; set; } = string.Empty;
        public int ExpectedOccurrenceCount { get; set; }
        public string RequestFingerprint { get; set; } = string.Empty;

        public string ProjectIdentifier { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public string SourceRevision { get; set; } = string.Empty;
        public string SourceIssueStatus { get; set; } = string.Empty;
        public string IssuedBy { get; set; } = string.Empty;
        public string IssueDate { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;

        public List<AuthoritativeEquipmentCoordinateWorkbookRow> Rows { get; set; } = new List<AuthoritativeEquipmentCoordinateWorkbookRow>();

        public AuthoritativeEquipmentCoordinateWorkbookDefects Defects { get; set; } = new AuthoritativeEquipmentCoordinateWorkbookDefects();
    }

    /// <summary>The six required source-document metadata values, exactly as read from the named cells.</summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookMetadata
    {
        public string ProjectIdentifier { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public string SourceRevision { get; set; } = string.Empty;
        public string SourceIssueStatus { get; set; } = string.Empty;
        public string IssuedBy { get; set; } = string.Empty;
        public string IssueDate { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// The workbook-derived provenance recorded on the receipt and the active package. It is kept
    /// separate from the semantic package identity: <c>SourceWorkbookSha256</c> identifies the exact
    /// imported .xlsx file, while <c>PackageFingerprint</c> still identifies the normalized
    /// authoritative package content.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookProvenance
    {
        public string SourceWorkbookSha256 { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string SourceDocumentNumber { get; set; } = string.Empty;
        public string SourceRevision { get; set; } = string.Empty;
        public string SourceIssueStatus { get; set; } = string.Empty;
        public string IssueDate { get; set; } = string.Empty;
    }

    /// <summary>
    /// The outcome of reading and validating one engineer-facing workbook. <see cref="Validation"/> is the
    /// normalized P14 package result produced by re-running the existing P14 validator over the
    /// normalized manifest and schedule bytes, so there is exactly one authority engine.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateWorkbookImportResult
    {
        public string Status { get; set; } = AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid;
        public List<string> Messages { get; set; } = new List<string>();

        public string WorkbookPath { get; set; } = string.Empty;
        public string SourceWorkbookSha256 { get; set; } = string.Empty;

        /// <summary>The canonical request fingerprint read from the workbook's internal metadata.</summary>
        public string RequestFingerprint { get; set; } = string.Empty;

        /// <summary>
        /// The exact workbook bytes that were read and validated. The installer persists these bytes
        /// verbatim rather than re-reading the file, so a workbook edited between validation and
        /// installation can never be installed under a validated hash.
        /// </summary>
        public byte[]? SourceWorkbookBytes { get; set; }

        public AuthoritativeEquipmentCoordinateWorkbookMetadata Metadata { get; set; } = new AuthoritativeEquipmentCoordinateWorkbookMetadata();

        /// <summary>The normalized P14 package validation result. Non-null once the workbook parsed.</summary>
        public AuthoritativeEquipmentCoordinateValidationResult? Validation { get; set; }

        /// <summary>The normalized internal P14 manifest bytes, generated deterministically from the workbook.</summary>
        public byte[]? NormalizedManifestBytes { get; set; }

        /// <summary>The normalized internal P14 schedule bytes, generated deterministically from the workbook.</summary>
        public byte[]? NormalizedScheduleBytes { get; set; }

        public bool IsValid => string.Equals(Status, AuthoritativeEquipmentCoordinateWorkbookStatuses.Valid, System.StringComparison.Ordinal);
    }
}
