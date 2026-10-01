using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Content hashing for the coordinate package. EngiSentry computes every hash itself from the
    /// exact source bytes it read; it never trusts a hash supplied by the package. The manifest and
    /// schedule bytes are hashed separately and combined into a content-addressed package fingerprint.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateHashing
    {
        public static string ComputeSha256(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }

        /// <summary>Hashes an on-disk file using the project's shared read-only SHA-256 helper.</summary>
        public static string ComputeFileSha256(string path) => HumanEngineeringReviewDecisionFileStore.ComputeSha256(path);

        /// <summary>
        /// The package fingerprint binds the manifest bytes and the schedule bytes into one identity:
        /// SHA256(manifest bytes ++ 0x00 ++ schedule bytes). Source bytes are never rewritten.
        /// </summary>
        public static string ComputePackageFingerprint(byte[] manifestBytes, byte[] scheduleBytes)
        {
            if (manifestBytes == null) throw new ArgumentNullException(nameof(manifestBytes));
            if (scheduleBytes == null) throw new ArgumentNullException(nameof(scheduleBytes));
            var buffer = new byte[manifestBytes.Length + 1 + scheduleBytes.Length];
            Buffer.BlockCopy(manifestBytes, 0, buffer, 0, manifestBytes.Length);
            buffer[manifestBytes.Length] = 0x00;
            Buffer.BlockCopy(scheduleBytes, 0, buffer, manifestBytes.Length + 1, scheduleBytes.Length);
            return ComputeSha256(buffer);
        }

        internal static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    /// <summary>JSON (de)serialization for the manifest, matching the project's DataContract conventions.</summary>
    public static class AuthoritativeEquipmentCoordinatePackageSerializer
    {
        public static byte[] SerializeManifest(AuthoritativeEquipmentCoordinateManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            var serializer = new DataContractJsonSerializer(typeof(AuthoritativeEquipmentCoordinateManifest));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, manifest);
                return stream.ToArray();
            }
        }

        /// <summary>Reads the manifest. A malformed or non-object payload yields null (never a partial manifest).</summary>
        public static AuthoritativeEquipmentCoordinateManifest? TryDeserializeManifest(byte[] bytes)
        {
            if (bytes == null) return null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AuthoritativeEquipmentCoordinateManifest));
                using (var stream = new MemoryStream(bytes))
                {
                    object? value = serializer.ReadObject(stream);
                    return value as AuthoritativeEquipmentCoordinateManifest;
                }
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// Strict validation of an authoritative equipment coordinate package. Nothing is trusted until
    /// every check passes: the manifest schema and provenance, the unit and coordinate basis, the
    /// drawing hash binding, the schedule structure, every coordinate value, the occurrence-plus-tag
    /// binding, and full coverage of the drawing's equipment inventory.
    ///
    /// Validation is read-only: no file is written, no drawing is touched.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinatePackageValidator
    {
        private static readonly string[] RequiredColumns = { "OccurrenceId", "EquipmentTag", "Easting", "Northing" };

        /// <summary>Full package validation: path structure, then content.</summary>
        public static AuthoritativeEquipmentCoordinateValidationResult Validate(
            string? packageDirectory,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory) || !Directory.Exists(packageDirectory))
                return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "package directory does not exist");

            string manifestPath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName);
            if (!File.Exists(manifestPath))
                return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "manifest file is missing: " + AuthoritativeEquipmentCoordinateSchema.ManifestFileName);

            byte[] manifestBytes;
            try { manifestBytes = File.ReadAllBytes(manifestPath); }
            catch (Exception exception) { return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "manifest file could not be read: " + exception.Message); }

            AuthoritativeEquipmentCoordinateManifest? manifest = AuthoritativeEquipmentCoordinatePackageSerializer.TryDeserializeManifest(manifestBytes);
            if (manifest == null)
                return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, "manifest is not a readable JSON object");

            string? schedulePath = ResolveSchedulePath(packageDirectory!, manifest, out string? pathError, out string pathStatus);
            if (schedulePath == null)
                return Failure(pathStatus, pathError!);

            if (!File.Exists(schedulePath))
                return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "schedule file is missing: " + AuthoritativeEquipmentCoordinateSchema.ScheduleFileName);

            byte[] scheduleBytes;
            try { scheduleBytes = File.ReadAllBytes(schedulePath); }
            catch (Exception exception) { return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "schedule file could not be read: " + exception.Message); }

            return ValidateContent(manifest, manifestBytes, scheduleBytes, inventory, expectedDrawingSha256);
        }

        /// <summary>
        /// Content-only validation for callers that already read the bytes (and for focused tests).
        /// The manifest's ScheduleFileName is still checked, because an unexpected name is a contract
        /// violation regardless of how the bytes were obtained.
        /// </summary>
        public static AuthoritativeEquipmentCoordinateValidationResult ValidateBytes(
            byte[]? manifestBytes,
            byte[]? scheduleBytes,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            if (manifestBytes == null) return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "manifest bytes are required");
            if (scheduleBytes == null) return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, "schedule bytes are required");
            AuthoritativeEquipmentCoordinateManifest? manifest = AuthoritativeEquipmentCoordinatePackageSerializer.TryDeserializeManifest(manifestBytes);
            if (manifest == null)
                return Failure(AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, "manifest is not a readable JSON object");
            if (ResolveSchedulePath("C:\\package", manifest, out string? pathError, out string pathStatus) == null)
                return Failure(pathStatus, pathError!);
            return ValidateContent(manifest, manifestBytes, scheduleBytes, inventory, expectedDrawingSha256);
        }

        /// <summary>
        /// Re-derives the coordinates from a stored schedule without re-checking inventory coverage.
        /// Used by the provider after it has re-verified the stored package hashes. Returns empty on
        /// any structural or coordinate problem: a package that no longer parses yields no coordinates.
        /// </summary>
        public static IReadOnlyList<AuthoritativeEquipmentCoordinate> ReadScheduleCoordinates(
            byte[]? manifestBytes,
            byte[]? scheduleBytes,
            AuthoritativeEquipmentCoordinateManifest? manifest)
        {
            if (manifestBytes == null || scheduleBytes == null || manifest == null) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            ScheduleParseResult parsed = ParseSchedule(scheduleBytes);
            if (!parsed.Success) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            string manifestSha = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(manifestBytes);
            string scheduleSha = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(scheduleBytes);
            string fingerprint = AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifestBytes, scheduleBytes);
            return parsed.Rows.Select(row => BuildCoordinate(row, manifest, manifestSha, scheduleSha, fingerprint)).ToList();
        }

        private static string? ResolveSchedulePath(string packageDirectory, AuthoritativeEquipmentCoordinateManifest manifest, out string? error, out string status)
        {
            error = null;
            status = AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid;
            string claimed = (manifest.ScheduleFileName ?? string.Empty).Trim();
            // A blank ScheduleFileName is a missing required manifest field, not a path problem.
            if (string.IsNullOrEmpty(claimed)) { error = "ScheduleFileName is required"; status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest; return null; }
            if (claimed.IndexOfAny(new[] { '/', '\\' }) >= 0) { error = "ScheduleFileName must be a bare file name, not a path: " + claimed; return null; }
            if (claimed.Contains("..")) { error = "ScheduleFileName must not contain a parent-directory reference: " + claimed; return null; }
            if (!string.Equals(claimed, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName, StringComparison.Ordinal))
            { error = "unexpected schedule file name: " + claimed + " (expected " + AuthoritativeEquipmentCoordinateSchema.ScheduleFileName + ")"; return null; }
            if (Path.IsPathRooted(claimed)) { error = "ScheduleFileName must not be an absolute path: " + claimed; return null; }
            string resolved = Path.Combine(packageDirectory, claimed);
            try
            {
                string fullDirectory = Path.GetFullPath(packageDirectory).TrimEnd('\\', '/');
                string fullSchedule = Path.GetFullPath(resolved);
                string? scheduleDirectory = Path.GetDirectoryName(fullSchedule);
                if (!string.Equals(scheduleDirectory?.TrimEnd('\\', '/'), fullDirectory, StringComparison.OrdinalIgnoreCase))
                { error = "the schedule file must sit directly inside the package directory"; return null; }
                return resolved;
            }
            catch (Exception exception) { error = "schedule path could not be resolved: " + exception.Message; return null; }
        }

        private static AuthoritativeEquipmentCoordinateValidationResult ValidateContent(
            AuthoritativeEquipmentCoordinateManifest manifest,
            byte[] manifestBytes,
            byte[] scheduleBytes,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            string? expectedDrawingSha256)
        {
            var result = new AuthoritativeEquipmentCoordinateValidationResult
            {
                ManifestSha256 = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(manifestBytes),
                ScheduleSha256 = AuthoritativeEquipmentCoordinateHashing.ComputeSha256(scheduleBytes),
                PackageFingerprint = AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifestBytes, scheduleBytes),
                DrawingName = manifest.DrawingName,
                IssuedBy = manifest.IssuedBy,
                SourceReference = manifest.SourceReference
            };

            // 1. Schema.
            if (!string.Equals(manifest.Schema, AuthoritativeEquipmentCoordinateSchema.Current, StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchema, "unsupported manifest Schema: " + (manifest.Schema ?? "<null>"));

            // 2. Required fields and provenance.
            foreach (string field in AuthoritativeEquipmentCoordinateManifest.RequiredNonEmptyFields)
            {
                if (string.IsNullOrWhiteSpace(ReadField(manifest, field)))
                    return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, "manifest field is required and must not be empty: " + field);
            }
            foreach (string field in AuthoritativeEquipmentCoordinateManifest.ProvenanceFields)
            {
                if (string.IsNullOrWhiteSpace(ReadField(manifest, field)))
                    return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, "manifest provenance is incomplete: " + field);
            }

            // 3. Drawing binding.
            if (!IsSha256(expectedDrawingSha256))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, "the current drawing has no usable SHA-256 to bind against");
            if (!IsSha256(manifest.DrawingSha256))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, "manifest DrawingSha256 is not a 64-character hexadecimal SHA256");
            if (!string.Equals(manifest.DrawingSha256, expectedDrawingSha256, StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, "manifest DrawingSha256 does not match the current drawing");

            // 4. Unit basis.
            if (!string.Equals(manifest.CoordinateUnits, AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters, StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedUnits, "unsupported CoordinateUnits: " + (manifest.CoordinateUnits ?? "<null>"));

            // 5. Coordinate basis.
            if (!string.Equals(manifest.CoordinateBasis, AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs, StringComparison.Ordinal))
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedCoordinateBasis, "unsupported CoordinateBasis: " + (manifest.CoordinateBasis ?? "<null>"));

            // 6-7. Schedule structure and per-row coordinate validation.
            ScheduleParseResult parsed = ParseSchedule(scheduleBytes);
            if (!parsed.Success)
                return Fail(result, parsed.Status!, parsed.Error!);
            foreach (AuthoritativeEquipmentCoordinateScheduleRow row in parsed.Rows)
                result.Coordinates.Add(BuildCoordinate(row, manifest, result.ManifestSha256, result.ScheduleSha256, result.PackageFingerprint));

            // 8. Duplicate occurrence identity.
            var duplicate = result.Coordinates.GroupBy(x => x.OccurrenceId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.DuplicateOccurrence, "duplicate OccurrenceId in schedule: " + duplicate.Key);

            // 9. Occurrence-plus-tag binding against the drawing inventory.
            var inventoryById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AuthoritativeEquipmentCoordinateInventoryEntry entry in inventory ?? Array.Empty<AuthoritativeEquipmentCoordinateInventoryEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.OccurrenceId)) continue;
                inventoryById[entry.OccurrenceId.Trim()] = NormalizeTag(entry.EquipmentTag);
            }
            result.ExpectedOccurrenceCount = inventoryById.Count;
            result.ResolvedOccurrenceCount = result.Coordinates.Count;

            if (inventoryById.Count == 0)
                return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.IncompleteCoverage, "the drawing inventory is empty; nothing can be covered");

            foreach (AuthoritativeEquipmentCoordinate coordinate in result.Coordinates)
            {
                if (!inventoryById.TryGetValue(coordinate.OccurrenceId, out string? expectedTag))
                    return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.OccurrenceMismatch, "schedule contains an OccurrenceId that is not in the drawing inventory: " + coordinate.OccurrenceId);
                if (!string.Equals(NormalizeTag(coordinate.EquipmentTag), expectedTag, StringComparison.Ordinal))
                    return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.OccurrenceMismatch, "EquipmentTag does not match the drawing inventory for " + coordinate.OccurrenceId);
            }

            // 10. Full coverage.
            foreach (string expectedId in inventoryById.Keys)
                if (!result.Coordinates.Any(x => string.Equals(x.OccurrenceId, expectedId, StringComparison.Ordinal)))
                    return Fail(result, AuthoritativeEquipmentCoordinateValidationStatuses.IncompleteCoverage, "the schedule does not cover every equipment occurrence; missing: " + expectedId);

            result.Coordinates = result.Coordinates.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal).ToList();
            result.Status = AuthoritativeEquipmentCoordinateValidationStatuses.Valid;
            result.Messages.Add("Package is valid: " + result.ResolvedOccurrenceCount + " of " + result.ExpectedOccurrenceCount + " occurrences covered.");
            return result;
        }

        private static AuthoritativeEquipmentCoordinate BuildCoordinate(
            AuthoritativeEquipmentCoordinateScheduleRow row,
            AuthoritativeEquipmentCoordinateManifest manifest,
            string manifestSha256, string scheduleSha256, string packageFingerprint)
        {
            return new AuthoritativeEquipmentCoordinate
            {
                OccurrenceId = row.OccurrenceId,
                EquipmentTag = row.EquipmentTag,
                Easting = decimal.Parse(row.Easting.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture),
                Northing = decimal.Parse(row.Northing.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture),
                Elevation = string.IsNullOrEmpty(row.Elevation) ? (decimal?)null : decimal.Parse(row.Elevation.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture),
                DrawingSha256 = manifest.DrawingSha256,
                PackageFingerprint = packageFingerprint,
                ManifestSha256 = manifestSha256,
                ScheduleSha256 = scheduleSha256,
                SourceUnits = manifest.CoordinateUnits,
                SourceCoordinateBasis = manifest.CoordinateBasis,
                IssuedBy = manifest.IssuedBy,
                SourceReference = manifest.SourceReference,
                SourceDocumentNumber = manifest.SourceDocumentNumber,
                SourceRevision = manifest.SourceRevision,
                SourceIssueStatus = manifest.SourceIssueStatus,
                IssueDate = manifest.IssueDate,
                EvidenceSummary = "Authoritative project schedule " + manifest.SourceReference + " issued by " + manifest.IssuedBy
            };
        }

        internal sealed class ScheduleParseResult
        {
            public bool Success { get; set; }
            public string? Status { get; set; }
            public string? Error { get; set; }
            public List<AuthoritativeEquipmentCoordinateScheduleRow> Rows { get; } = new List<AuthoritativeEquipmentCoordinateScheduleRow>();
        }

        /// <summary>Structural + coordinate parsing of the schedule, shared by validation and the provider.</summary>
        internal static ScheduleParseResult ParseSchedule(byte[] scheduleBytes)
        {
            var outcome = new ScheduleParseResult();
            string text;
            try { text = DecodeSchedule(scheduleBytes); }
            catch (Exception exception) { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = exception.Message; return outcome; }

            List<List<string>> records = ParseCsv(text);
            var nonEmpty = records.Where(r => r.Any(f => !string.IsNullOrWhiteSpace(f))).ToList();
            if (nonEmpty.Count == 0)
            { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "schedule is empty"; return outcome; }

            List<string> header = nonEmpty[0].Select(x => x.Trim()).ToList();
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                if (string.IsNullOrEmpty(header[i])) continue;
                if (columnIndex.ContainsKey(header[i]))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "duplicate schedule column: " + header[i]; return outcome; }
                columnIndex[header[i]] = i;
            }
            foreach (string required in RequiredColumns)
                if (!columnIndex.ContainsKey(required))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "schedule is missing required column: " + required; return outcome; }

            for (int r = 1; r < nonEmpty.Count; r++)
            {
                List<string> record = nonEmpty[r];
                if (record.Count > header.Count)
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "schedule row " + (r + 1) + " has more fields than the header"; return outcome; }

                string occurrenceId = Field(record, columnIndex, "OccurrenceId").Trim();
                string equipmentTag = Field(record, columnIndex, "EquipmentTag").Trim();
                string easting = Field(record, columnIndex, "Easting").Trim();
                string northing = Field(record, columnIndex, "Northing").Trim();
                string elevation = Field(record, columnIndex, "Elevation").Trim();

                if (string.IsNullOrEmpty(occurrenceId))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "schedule row " + (r + 1) + " has an empty OccurrenceId"; return outcome; }
                if (string.IsNullOrEmpty(equipmentTag))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule; outcome.Error = "schedule row " + (r + 1) + " has an empty EquipmentTag"; return outcome; }

                if (!TryParseCoordinate(easting, out _))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate; outcome.Error = "schedule row " + (r + 1) + " has an invalid Easting: " + Describe(easting); return outcome; }
                if (!TryParseCoordinate(northing, out _))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate; outcome.Error = "schedule row " + (r + 1) + " has an invalid Northing: " + Describe(northing); return outcome; }
                if (!string.IsNullOrEmpty(elevation) && !TryParseCoordinate(elevation, out _))
                { outcome.Status = AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate; outcome.Error = "schedule row " + (r + 1) + " has an invalid Elevation: " + Describe(elevation); return outcome; }

                outcome.Rows.Add(new AuthoritativeEquipmentCoordinateScheduleRow
                {
                    OccurrenceId = occurrenceId,
                    EquipmentTag = equipmentTag,
                    Easting = easting,
                    Northing = northing,
                    Elevation = elevation,
                    OccurrenceIndex = FieldOrNull(record, columnIndex, "OccurrenceIndex"),
                    SourceEntityHandle = FieldOrNull(record, columnIndex, "SourceEntityHandle"),
                    Layer = FieldOrNull(record, columnIndex, "Layer"),
                    CurrentPlacementStatus = FieldOrNull(record, columnIndex, "CurrentPlacementStatus"),
                    CurrentPlacementReason = FieldOrNull(record, columnIndex, "CurrentPlacementReason")
                });
            }

            outcome.Success = true;
            return outcome;
        }

        private static AuthoritativeEquipmentCoordinateValidationResult Fail(AuthoritativeEquipmentCoordinateValidationResult result, string status, string message)
        {
            result.Status = status;
            result.Messages.Add(message);
            result.Coordinates.Clear();
            return result;
        }

        private static AuthoritativeEquipmentCoordinateValidationResult Failure(string status, string message)
        {
            var result = new AuthoritativeEquipmentCoordinateValidationResult { Status = status };
            result.Messages.Add(message);
            return result;
        }

        private static string DecodeSchedule(byte[] bytes)
        {
            string text = new UTF8Encoding(false, true).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return text;
        }

        private static string Field(List<string> record, Dictionary<string, int> index, string column)
            => index.TryGetValue(column, out int i) && i < record.Count ? record[i] : string.Empty;

        private static string? FieldOrNull(List<string> record, Dictionary<string, int> index, string column)
        {
            string value = Field(record, index, column).Trim();
            return value.Length == 0 ? null : value;
        }

        private static string NormalizeTag(string? tag) => (tag ?? string.Empty).Trim().ToUpperInvariant();

        private static string Describe(string value) => value.Length == 0 ? "<blank>" : value;

        /// <summary>Invariant decimal parsing. Group separators are rejected so "1,500,000" is never ambiguous.</summary>
        private static bool TryParseCoordinate(string text, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text.Trim();
            if (trimmed.IndexOf(',') >= 0) return false;
            const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
            return decimal.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out value);
        }

        internal static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }

        private static string? ReadField(AuthoritativeEquipmentCoordinateManifest manifest, string field)
        {
            switch (field)
            {
                case "Schema": return manifest.Schema;
                case "DrawingName": return manifest.DrawingName;
                case "DrawingSha256": return manifest.DrawingSha256;
                case "CoordinateUnits": return manifest.CoordinateUnits;
                case "CoordinateBasis": return manifest.CoordinateBasis;
                case "ScheduleFileName": return manifest.ScheduleFileName;
                case "ProjectName": return manifest.ProjectName;
                case "IssuedBy": return manifest.IssuedBy;
                case "IssuedByRole": return manifest.IssuedByRole;
                case "IssuedTimestampUtc": return manifest.IssuedTimestampUtc;
                case "SourceReference": return manifest.SourceReference;
                case "Notes": return manifest.Notes;
                default: return null;
            }
        }

        /// <summary>
        /// Deterministic CSV parsing (RFC 4180-style): quoted fields, doubled-quote escapes, commas and
        /// newlines inside quotes, and both CRLF and LF record separators.
        /// </summary>
        internal static List<List<string>> ParseCsv(string text)
        {
            var records = new List<List<string>>();
            var current = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool hasContent = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                }
                else if (c == '"' && field.Length == 0) { inQuotes = true; hasContent = true; }
                else if (c == ',') { current.Add(field.ToString()); field.Clear(); hasContent = true; }
                else if (c == '\r') { /* normalized away; the following \n terminates the record */ }
                else if (c == '\n') { current.Add(field.ToString()); field.Clear(); records.Add(current); current = new List<string>(); hasContent = false; }
                else { field.Append(c); hasContent = true; }
            }
            if (hasContent || field.Length > 0 || current.Count > 0) { current.Add(field.ToString()); records.Add(current); }
            return records;
        }
    }
}
