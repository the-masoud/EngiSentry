using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Reads, strictly validates and normalizes one engineer-facing workbook.
    ///
    /// The pipeline is exactly the accepted P14 pipeline with a different front end:
    /// <code>
    /// XLSX workbook -> workbook parser/validator -> normalized P14 manifest + schedule bytes
    ///               -> existing P14 validator -> existing P14 store -> existing provider -> ES_PLOTQA
    /// </code>
    /// There is no second authority engine. Everything the engineer supplies is re-validated from the
    /// workbook bytes; nothing is trusted because a cell is locked or a defined name exists.
    ///
    /// Reading is entirely read-only: no file is written and no drawing is touched.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateWorkbookReader
    {
        /// <summary>
        /// Reads and validates the workbook at <paramref name="workbookPath"/> against the current
        /// drawing's equipment inventory and drawing SHA-256.
        /// </summary>
        public static AuthoritativeEquipmentCoordinateWorkbookImportResult Read(
            string? workbookPath,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            var result = new AuthoritativeEquipmentCoordinateWorkbookImportResult
            {
                WorkbookPath = workbookPath ?? string.Empty
            };

            if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid,
                    "the workbook path does not exist or is not a file: " + (workbookPath ?? "<null>"));

            if (Directory.Exists(workbookPath))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid,
                    "the supplied path is a directory, not a workbook file");

            string extension = Path.GetExtension(workbookPath!);
            if (!string.Equals(extension, AuthoritativeEquipmentCoordinateWorkbookSchema.WorkbookExtension, StringComparison.OrdinalIgnoreCase))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension,
                    "only " + AuthoritativeEquipmentCoordinateWorkbookSchema.WorkbookExtension + " workbooks are accepted, not '" + extension + "'");

            byte[] bytes;
            try { bytes = File.ReadAllBytes(workbookPath!); }
            catch (Exception exception)
            {
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid,
                    "the workbook could not be read: " + exception.Message);
            }

            result.SourceWorkbookSha256 = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(bytes);
            result.SourceWorkbookBytes = bytes;
            return Read(bytes, result, inventory, expectedDrawingSha256);
        }

        /// <summary>Reads and validates workbook bytes already in memory (used by focused tests).</summary>
        public static AuthoritativeEquipmentCoordinateWorkbookImportResult Read(
            byte[] workbookBytes,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            var result = new AuthoritativeEquipmentCoordinateWorkbookImportResult();
            if (workbookBytes == null)
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid, "workbook bytes are required");
            result.SourceWorkbookSha256 = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(workbookBytes);
            result.SourceWorkbookBytes = workbookBytes;
            return Read(workbookBytes, result, inventory, expectedDrawingSha256);
        }

        private static AuthoritativeEquipmentCoordinateWorkbookImportResult Read(
            byte[] workbookBytes,
            AuthoritativeEquipmentCoordinateWorkbookImportResult result,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            // ---- 1. WORKBOOK: container, sheets, table, named fields, schema, internal metadata. ----

            List<RawRow> rawRows;
            Dictionary<string, string> internalMetadata;
            AuthoritativeEquipmentCoordinateWorkbookMetadata metadata;

            try
            {
                using (var stream = new MemoryStream(workbookBytes, false))
                using (SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false))
                {
                    WorkbookPart? workbookPart = document.WorkbookPart;
                    if (workbookPart?.Workbook == null)
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx,
                            "the file is not a readable .xlsx workbook");

                    List<Sheet> sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList() ?? new List<Sheet>();

                    List<Sheet> visibleMatches = sheets
                        .Where(s => string.Equals(s.Name?.Value, AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName, StringComparison.Ordinal))
                        .ToList();
                    if (visibleMatches.Count != 1)
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingVisibleSheet,
                            "expected exactly one sheet named '" + AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName
                            + "' but found " + visibleMatches.Count);
                    Sheet visibleSheet = visibleMatches[0];
                    if (visibleSheet.State != null
                        && (visibleSheet.State.Value == SheetStateValues.Hidden || visibleSheet.State.Value == SheetStateValues.VeryHidden))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingVisibleSheet,
                            "the '" + AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName + "' sheet is hidden; it must be visible");

                    List<Sheet> internalMatches = sheets
                        .Where(s => string.Equals(s.Name?.Value, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalSheetName, StringComparison.Ordinal))
                        .ToList();
                    if (internalMatches.Count != 1)
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalSheet,
                            "expected exactly one internal sheet named '" + AuthoritativeEquipmentCoordinateWorkbookSchema.InternalSheetName
                            + "' but found " + internalMatches.Count);
                    SheetStateValues? internalState = internalMatches[0].State?.Value;
                    if (internalState == null || internalState.Value != SheetStateValues.VeryHidden)
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalSheet,
                            "the '" + AuthoritativeEquipmentCoordinateWorkbookSchema.InternalSheetName + "' sheet must be very hidden");

                    if (!TryGetWorksheet(workbookPart, visibleSheet, out WorksheetPart? visiblePart, out Worksheet? visibleWorksheet))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx,
                            "the '" + AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName + "' sheet could not be opened");
                    if (!TryGetWorksheet(workbookPart, internalMatches[0], out _, out Worksheet? internalWorksheet))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx,
                            "the '" + AuthoritativeEquipmentCoordinateWorkbookSchema.InternalSheetName + "' sheet could not be opened");

                    Table? table = visiblePart!.TableDefinitionParts
                        .Select(p => p.Table)
                        .FirstOrDefault(t => t != null && string.Equals(t.Name?.Value, AuthoritativeEquipmentCoordinateWorkbookSchema.TableName, StringComparison.Ordinal));
                    if (table == null)
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingTable,
                            "the required Excel table '" + AuthoritativeEquipmentCoordinateWorkbookSchema.TableName + "' is missing");
                    if (!TryParseRange(table.Reference?.Value, out int startColumn, out int startRow, out int endColumn, out int endRow))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingTable,
                            "the table reference is missing or malformed: " + (table.Reference?.Value ?? "<null>"));

                    // Columns are located by table-column NAME, never by visual position.
                    var columnIndex = new Dictionary<string, int>(StringComparer.Ordinal);
                    var tableColumns = table.TableColumns?.Elements<TableColumn>().ToList() ?? new List<TableColumn>();
                    for (int i = 0; i < tableColumns.Count; i++)
                    {
                        string? name = tableColumns[i].Name?.Value;
                        if (string.IsNullOrEmpty(name) || columnIndex.ContainsKey(name!)) continue;
                        columnIndex[name!] = startColumn + i;
                    }
                    foreach (string required in new[]
                    {
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNumber,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEquipmentTag,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnDescription,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEasting,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNorthing,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnElevation,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnOccurrenceId,
                        AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnSourceEntityHandle
                    })
                    {
                        if (!columnIndex.ContainsKey(required))
                            return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingTable,
                                "the equipment table is missing the required column: " + required);
                    }

                    SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

                    // Internal metadata: read as key/value pairs by KEY, never by position.
                    internalMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (Row row in RowsOf(internalWorksheet))
                    {
                        uint internalRow = row.RowIndex?.Value ?? 0U;
                        string key = ReadCell(FindCell(internalWorksheet, internalRow, 0), sharedStrings).Text.Trim();
                        if (key.Length == 0) continue;
                        string value = ReadCell(FindCell(internalWorksheet, internalRow, 1), sharedStrings).Text.Trim();
                        internalMetadata[key] = value;
                    }

                    foreach (string key in AuthoritativeEquipmentCoordinateWorkbookSchema.RequiredInternalKeys)
                    {
                        if (!internalMetadata.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                            return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalMetadata,
                                "the internal workbook metadata is missing or empty: " + key);
                    }

                    if (!string.Equals(internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeySchemaVersion],
                        AuthoritativeEquipmentCoordinateWorkbookSchema.Current, StringComparison.Ordinal))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedWorkbookSchema,
                            "unsupported workbook SchemaVersion: "
                            + internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeySchemaVersion]);

                    result.RequestFingerprint = internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyRequestFingerprint];

                    // Source metadata: read by defined NAME, never by visual position.
                    List<DefinedName> definedNames = workbookPart.Workbook.DefinedNames?.Elements<DefinedName>().ToList() ?? new List<DefinedName>();
                    metadata = new AuthoritativeEquipmentCoordinateWorkbookMetadata();
                    foreach (KeyValuePair<string, string> field in new[]
                    {
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameProjectIdentifier, "ProjectIdentifier"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceDocumentNumber, "SourceDocumentNumber"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceRevision, "SourceRevision"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceIssueStatus, "SourceIssueStatus"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameIssuedBy, "IssuedBy"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameIssueDate, "IssueDate"),
                        new KeyValuePair<string, string>(AuthoritativeEquipmentCoordinateWorkbookSchema.NameNotes, "Notes")
                    })
                    {
                        DefinedName? defined = definedNames.FirstOrDefault(d => string.Equals(d.Name?.Value, field.Key, StringComparison.Ordinal));
                        if (defined == null)
                            return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingNamedField,
                                "the workbook-defined name is missing: " + field.Key);
                        string text = ResolveNamedCellText(defined, visibleWorksheet, sharedStrings);
                        switch (field.Value)
                        {
                            case "ProjectIdentifier": metadata.ProjectIdentifier = text; break;
                            case "SourceDocumentNumber": metadata.SourceDocumentNumber = text; break;
                            case "SourceRevision": metadata.SourceRevision = text; break;
                            case "SourceIssueStatus": metadata.SourceIssueStatus = text; break;
                            case "IssuedBy": metadata.IssuedBy = text; break;
                            case "IssueDate": metadata.IssueDate = text; break;
                            case "Notes": metadata.Notes = text; break;
                        }
                    }

                    // ---- Data rows, read from the table range. ----
                    rawRows = new List<RawRow>();
                    for (uint excelRow = (uint)(startRow + 1); excelRow <= (uint)endRow; excelRow++)
                    {
                        var raw = new RawRow { ExcelRow = excelRow };
                        raw.OccurrenceId = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnOccurrenceId]), sharedStrings);
                        raw.SourceEntityHandle = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnSourceEntityHandle]), sharedStrings);
                        raw.EquipmentTag = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEquipmentTag]), sharedStrings);
                        raw.Easting = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEasting]), sharedStrings);
                        raw.Northing = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNorthing]), sharedStrings);
                        raw.Elevation = ReadCell(FindCell(visibleWorksheet, excelRow, columnIndex[AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnElevation]), sharedStrings);
                        rawRows.Add(raw);
                    }
                }
            }
            catch (Exception exception)
            {
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx,
                    "the workbook could not be parsed: " + exception.GetType().Name + ": " + exception.Message);
            }

            result.Metadata = metadata;

            // Rows that carry no identity and no coordinate at all are empty filler rows.
            List<RawRow> rows = rawRows
                .Where(r => r.OccurrenceId.Kind != CellKind.Blank || r.EquipmentTag.Kind != CellKind.Blank
                    || r.Easting.Kind != CellKind.Blank || r.Northing.Kind != CellKind.Blank)
                .ToList();

            // ---- 2. DRAWING: the workbook must be bound to the current drawing revision. ----
            if (!AuthoritativeEquipmentCoordinatePackageStore.IsSha256(expectedDrawingSha256))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.DrawingMismatch,
                    "the current drawing has no usable SHA-256 to bind against");
            string workbookDrawingSha = internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyDrawingSha256];
            if (!AuthoritativeEquipmentCoordinatePackageStore.IsSha256(workbookDrawingSha))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.DrawingMismatch,
                    "the internal DrawingSha256 is not a 64-character hexadecimal SHA256");
            if (!string.Equals(workbookDrawingSha, expectedDrawingSha256, StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.DrawingMismatch,
                    "the workbook was generated for a different drawing revision");

            // ---- 3. REQUEST: the request must be the one this drawing produced. ----
            if (!int.TryParse(internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyExpectedOccurrenceCount],
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int expectedCount))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalMetadata,
                    "the internal ExpectedOccurrenceCount is not an integer");
            if (expectedCount != rows.Count)
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RequestCountMismatch,
                    "the workbook declares " + expectedCount + " expected occurrences but carries " + rows.Count + " data rows");

            string recomputedFingerprint = AuthoritativeEquipmentCoordinateRequestFingerprint.Compute(
                internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeySchemaVersion],
                workbookDrawingSha,
                rows.Select(r => new KeyValuePair<string, string>(r.OccurrenceId.Text, r.EquipmentTag.Text)));
            if (!string.Equals(recomputedFingerprint,
                internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyRequestFingerprint], StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RequestFingerprintMismatch,
                    "the recomputed RequestFingerprint does not match the workbook; the row identity set was changed");

            // ---- 4. ROWS: occurrence-plus-tag identity binding against the live inventory. ----
            var inventoryById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AuthoritativeEquipmentCoordinateInventoryEntry entry in inventory ?? Array.Empty<AuthoritativeEquipmentCoordinateInventoryEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.OccurrenceId)) continue;
                inventoryById[entry.OccurrenceId.Trim()] = NormalizeTag(entry.EquipmentTag);
            }
            if (inventoryById.Count == 0)
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                    "the drawing inventory is empty; nothing can be covered");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (RawRow row in rows)
            {
                string occurrenceId = row.OccurrenceId.Text.Trim();
                if (occurrenceId.Length == 0)
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                        "workbook row " + row.ExcelRow + " has an empty occurrence identity");
                if (!inventoryById.TryGetValue(occurrenceId, out string? expectedTag))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                        "workbook row " + row.ExcelRow + " carries an occurrence identity that is not in the drawing inventory: " + occurrenceId);
                if (!seen.Add(occurrenceId))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                        "the workbook contains a duplicate occurrence identity: " + occurrenceId);
                if (!string.Equals(NormalizeTag(row.EquipmentTag.Text), expectedTag, StringComparison.Ordinal))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                        "the equipment tag does not match the drawing inventory for " + occurrenceId);
            }
            foreach (string expectedId in inventoryById.Keys)
                if (!seen.Contains(expectedId))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch,
                        "the workbook does not cover every equipment occurrence; missing: " + expectedId);

            // ---- 5. SOURCE METADATA: six required non-empty fields and an unambiguous issue date. ----
            foreach (string value in new[]
            {
                metadata.ProjectIdentifier, metadata.SourceDocumentNumber, metadata.SourceRevision,
                metadata.SourceIssueStatus, metadata.IssuedBy, metadata.IssueDate
            })
            {
                if (string.IsNullOrWhiteSpace(value))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata,
                        "a required source-document field is empty; complete all six fields before importing");
            }
            if (!Regex.IsMatch(metadata.IssueDate, @"^\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)
                || !DateTime.TryParseExact(metadata.IssueDate,
                AuthoritativeEquipmentCoordinateWorkbookSchema.IssueDateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidIssueDate,
                    "IssueDate must be exactly " + AuthoritativeEquipmentCoordinateWorkbookSchema.IssueDateFormat + ", not '" + metadata.IssueDate + "'");

            // ---- 6. COORDINATES: literal, finite numerics only. Formulas are never evaluated. ----
            var parsed = new List<ParsedCoordinate>(rows.Count);
            foreach (RawRow row in rows)
            {
                if (!TryReadCoordinate(row.Easting, out decimal easting))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate,
                        "workbook row " + row.ExcelRow + " has an invalid Easting: " + Describe(row.Easting));
                if (!TryReadCoordinate(row.Northing, out decimal northing))
                    return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate,
                        "workbook row " + row.ExcelRow + " has an invalid Northing: " + Describe(row.Northing));

                decimal? elevation = null;
                if (row.Elevation.Kind != CellKind.Blank)
                {
                    if (!TryReadCoordinate(row.Elevation, out decimal value))
                        return Fail(result, AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate,
                            "workbook row " + row.ExcelRow + " has an invalid Elevation: " + Describe(row.Elevation));
                    elevation = value;
                }

                parsed.Add(new ParsedCoordinate
                {
                    OccurrenceId = row.OccurrenceId.Text.Trim(),
                    EquipmentTag = row.EquipmentTag.Text.Trim(),
                    Easting = easting,
                    Northing = northing,
                    Elevation = elevation
                });
            }

            // ---- Normalize into the existing P14 package model, then run the existing P14 validator. ----
            byte[] manifestBytes = AuthoritativeEquipmentCoordinatePackageSerializer.SerializeManifest(
                BuildNormalizedManifest(metadata, internalMetadata));
            byte[] scheduleBytes = AuthoritativeEquipmentCoordinateWorkbookWriter.Utf8NoBom.GetBytes(
                BuildNormalizedSchedule(parsed, inventoryById, inventory!));

            AuthoritativeEquipmentCoordinateValidationResult validation =
                AuthoritativeEquipmentCoordinatePackageValidator.ValidateBytes(
                    manifestBytes, scheduleBytes, inventory!, expectedDrawingSha256);

            result.Validation = validation;
            result.NormalizedManifestBytes = manifestBytes;
            result.NormalizedScheduleBytes = scheduleBytes;

            if (!validation.IsValid)
            {
                // Defence in depth: the normalized package must satisfy the accepted P14 contract exactly.
                result.Status = validation.Status;
                result.Messages.AddRange(validation.Messages);
                return result;
            }

            result.Status = AuthoritativeEquipmentCoordinateWorkbookStatuses.Valid;
            result.Messages.Add("Workbook is valid: " + validation.ResolvedOccurrenceCount + " of "
                + validation.ExpectedOccurrenceCount + " occurrences covered.");
            return result;
        }

        // ---------------------------------------------------------------- normalization

        private static AuthoritativeEquipmentCoordinateManifest BuildNormalizedManifest(
            AuthoritativeEquipmentCoordinateWorkbookMetadata metadata,
            Dictionary<string, string> internalMetadata)
        {
            // The normalized manifest is the SEMANTIC package: it carries what the authority issued, not
            // the exact file bytes it arrived in. SourceWorkbookSha256 is therefore recorded on the
            // receipt and the active package, never here, so that PackageFingerprint stays a semantic
            // identity and re-saving the same content in Excel never changes it.
            string notes = "Normalized from the engineer-facing workbook.";
            if (!string.IsNullOrWhiteSpace(metadata.Notes)) notes += " Notes=" + metadata.Notes;

            return new AuthoritativeEquipmentCoordinateManifest
            {
                Schema = AuthoritativeEquipmentCoordinateSchema.Current,
                DrawingName = internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyDrawingFileName],
                DrawingSha256 = internalMetadata[AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyDrawingSha256],
                CoordinateUnits = AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters,
                CoordinateBasis = AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs,
                ScheduleFileName = AuthoritativeEquipmentCoordinateSchema.ScheduleFileName,
                ProjectName = metadata.ProjectIdentifier,
                IssuedBy = metadata.IssuedBy,
                IssuedByRole = metadata.SourceIssueStatus,
                IssuedTimestampUtc = metadata.IssueDate,
                SourceReference = metadata.SourceDocumentNumber,
                Notes = notes,
                SourceDocumentNumber = metadata.SourceDocumentNumber,
                SourceRevision = metadata.SourceRevision,
                SourceIssueStatus = metadata.SourceIssueStatus,
                IssueDate = metadata.IssueDate
            };
        }

        private static string BuildNormalizedSchedule(
            List<ParsedCoordinate> parsed,
            Dictionary<string, string> inventoryById,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory)
        {
            var byId = new Dictionary<string, AuthoritativeEquipmentCoordinateInventoryEntry>(StringComparer.Ordinal);
            foreach (AuthoritativeEquipmentCoordinateInventoryEntry entry in inventory ?? Array.Empty<AuthoritativeEquipmentCoordinateInventoryEntry>())
                if (entry != null && !string.IsNullOrWhiteSpace(entry.OccurrenceId)) byId[entry.OccurrenceId.Trim()] = entry;

            var builder = new StringBuilder();
            builder.Append(string.Join(",", AuthoritativeEquipmentCoordinateRequestBuilder.ScheduleHeader));
            builder.Append("\r\n");

            foreach (ParsedCoordinate coordinate in parsed.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
            {
                byId.TryGetValue(coordinate.OccurrenceId, out AuthoritativeEquipmentCoordinateInventoryEntry? entry);
                var fields = new[]
                {
                    coordinate.OccurrenceId,
                    coordinate.EquipmentTag,
                    entry == null ? string.Empty : entry.OccurrenceIndex.ToString(CultureInfo.InvariantCulture),
                    entry?.SourceEntityHandle ?? string.Empty,
                    entry?.Layer ?? string.Empty,
                    entry?.CurrentPlacementStatus ?? string.Empty,
                    entry?.CurrentPlacementReason ?? string.Empty,
                    Format(coordinate.Easting),
                    Format(coordinate.Northing),
                    coordinate.Elevation.HasValue ? Format(coordinate.Elevation.Value) : string.Empty
                };
                builder.Append(string.Join(",", fields.Select(AuthoritativeEquipmentCoordinateRequestBuilder.EscapeCsv)));
                builder.Append("\r\n");
            }
            return builder.ToString();
        }

        /// <summary>Invariant, exponent-free, value-preserving decimal text.</summary>
        private static string Format(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- workbook plumbing

        private enum CellKind { Blank, Number, Text, Formula, Error, Other }

        private readonly struct CellRead
        {
            public CellRead(CellKind kind, string text) { Kind = kind; Text = text; }
            public CellKind Kind { get; }
            public string Text { get; }
        }

        private sealed class RawRow
        {
            public uint ExcelRow;
            public CellRead OccurrenceId;
            public CellRead SourceEntityHandle;
            public CellRead EquipmentTag;
            public CellRead Easting;
            public CellRead Northing;
            public CellRead Elevation;
        }

        private sealed class ParsedCoordinate
        {
            public string OccurrenceId = string.Empty;
            public string EquipmentTag = string.Empty;
            public decimal Easting;
            public decimal Northing;
            public decimal? Elevation;
        }

        private static bool TryGetWorksheet(WorkbookPart workbookPart, Sheet sheet, out WorksheetPart? part, out Worksheet? worksheet)
        {
            part = null;
            worksheet = null;
            string? id = sheet.Id?.Value;
            if (string.IsNullOrEmpty(id)) return false;
            try { part = workbookPart.GetPartById(id!) as WorksheetPart; }
            catch (Exception) { return false; }
            if (part == null) return false;
            worksheet = part.Worksheet;
            return worksheet != null;
        }

        private static IEnumerable<Row> RowsOf(Worksheet? worksheet)
            => worksheet?.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>();

        private static Cell? FindCell(Worksheet? worksheet, uint rowIndex, int columnIndex)
        {
            Row? row = RowsOf(worksheet).FirstOrDefault(r => (r.RowIndex?.Value ?? 0U) == rowIndex);
            if (row == null) return null;
            int position = 0;
            foreach (Cell cell in row.Elements<Cell>())
            {
                string? reference = cell.CellReference?.Value;
                if (!string.IsNullOrEmpty(reference) && TrySplitCellReference(reference!, out int column, out uint referenceRow))
                {
                    if (referenceRow != 0U && referenceRow != rowIndex) { position++; continue; }
                    if (column == columnIndex) return cell;
                }
                else if (position == columnIndex) return cell;
                position++;
            }
            return null;
        }

        private static CellRead ReadCell(Cell? cell, SharedStringTable? sharedStrings)
        {
            if (cell == null) return new CellRead(CellKind.Blank, string.Empty);
            if (cell.GetFirstChild<CellFormula>() != null) return new CellRead(CellKind.Formula, string.Empty);

            CellValues? dataType = cell.DataType?.Value;
            string raw = cell.GetFirstChild<CellValue>()?.InnerText ?? string.Empty;

            if (dataType == CellValues.SharedString)
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || sharedStrings == null)
                    return new CellRead(CellKind.Text, string.Empty);
                SharedStringItem? item = sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(index);
                return new CellRead(CellKind.Text, item?.InnerText ?? string.Empty);
            }
            if (dataType == CellValues.InlineString)
                return new CellRead(CellKind.Text, cell.GetFirstChild<InlineString>()?.InnerText ?? string.Empty);
            if (dataType == CellValues.String)
                return new CellRead(CellKind.Text, raw);
            if (dataType == CellValues.Error)
                return new CellRead(CellKind.Error, raw);
            if (dataType == CellValues.Boolean)
                return new CellRead(CellKind.Other, raw);

            return raw.Length == 0 ? new CellRead(CellKind.Blank, string.Empty) : new CellRead(CellKind.Number, raw);
        }

        private static string ResolveNamedCellText(DefinedName defined, Worksheet? worksheet, SharedStringTable? sharedStrings)
        {
            string formula = defined.InnerText ?? string.Empty;
            int bang = formula.LastIndexOf('!');
            string cellPart = bang >= 0 ? formula.Substring(bang + 1) : formula;
            if (!TrySplitCellReference(cellPart, out int column, out uint row)) return string.Empty;
            return ReadCell(FindCell(worksheet, row, column), sharedStrings).Text;
        }

        /// <summary>Literal finite numeric cells only. Text, formulas, errors and blanks are all rejected.</summary>
        private static bool TryReadCoordinate(CellRead cell, out decimal value)
        {
            value = 0M;
            if (cell.Kind != CellKind.Number) return false;
            string text = cell.Text.Trim();
            if (text.Length == 0 || text.IndexOf(',') >= 0) return false;
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
                | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
            return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out value);
        }

        private static string Describe(CellRead cell)
        {
            switch (cell.Kind)
            {
                case CellKind.Blank: return "<blank>";
                case CellKind.Formula: return "<formula>";
                case CellKind.Error: return "<formula error>";
                case CellKind.Text: return "'" + cell.Text + "' (text, not a number)";
                case CellKind.Other: return "<non-numeric cell>";
                default: return "'" + cell.Text + "'";
            }
        }

        private static bool TrySplitCellReference(string reference, out int columnIndex, out uint rowIndex)
        {
            columnIndex = 0;
            rowIndex = 0U;
            if (string.IsNullOrEmpty(reference)) return false;
            string text = reference.Replace("$", string.Empty).Trim();
            int split = 0;
            while (split < text.Length && char.IsLetter(text[split])) split++;
            if (split == 0 || split == text.Length) return false;
            string letters = text.Substring(0, split).ToUpperInvariant();
            string digits = text.Substring(split);
            int column = 0;
            foreach (char c in letters)
            {
                if (c < 'A' || c > 'Z') return false;
                column = column * 26 + (c - 'A' + 1);
            }
            if (!uint.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out rowIndex)) return false;
            columnIndex = column - 1;
            return true;
        }

        private static bool TryParseRange(string? reference, out int startColumn, out int startRow, out int endColumn, out int endRow)
        {
            startColumn = startRow = endColumn = endRow = 0;
            if (string.IsNullOrWhiteSpace(reference)) return false;
            string[] parts = reference!.Split(':');
            if (parts.Length != 2) return false;
            if (!TrySplitCellReference(parts[0], out startColumn, out uint startRowValue)) return false;
            if (!TrySplitCellReference(parts[1], out endColumn, out uint endRowValue)) return false;
            startRow = (int)startRowValue;
            endRow = (int)endRowValue;
            return startRow > 0 && endRow >= startRow;
        }

        private static string NormalizeTag(string? tag) => (tag ?? string.Empty).Trim().ToUpperInvariant();

        private static AuthoritativeEquipmentCoordinateWorkbookImportResult Fail(
            AuthoritativeEquipmentCoordinateWorkbookImportResult result, string status, string message)
        {
            result.Status = status;
            result.Messages.Add(message);
            result.Validation = null;
            result.NormalizedManifestBytes = null;
            result.NormalizedScheduleBytes = null;
            return result;
        }
    }
}
