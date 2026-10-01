using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>The outcome of generating a blank coordinate request package.</summary>
    public sealed class AuthoritativeEquipmentCoordinateRequestResult
    {
        public string PackageDirectory { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public string SchedulePath { get; set; } = string.Empty;
        public string DrawingName { get; set; } = string.Empty;
        public string DrawingSha256 { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public int NonEmptyOccurrenceIdCount { get; set; }
        public int NonEmptyEquipmentTagCount { get; set; }
        public int BlankEastingCount { get; set; }
        public int BlankNorthingCount { get; set; }
        public int DuplicateOccurrenceIdCount { get; set; }
        public int MissingOccurrenceIdCount { get; set; }
    }

    /// <summary>
    /// Generates a blank authoritative coordinate request package for a drawing: a manifest template
    /// and a schedule with exactly one row per equipment occurrence, every coordinate left blank.
    ///
    /// This is a request, never an answer. EngiSentry never invents or infers a coordinate, so the
    /// request contains no coordinate values at all — the project authority fills them in, then the
    /// completed package is imported with <c>ES_COORDIMPORT</c>. Generation is pure file I/O: no
    /// drawing is modified, no modal dialog is shown.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateRequestBuilder
    {
        public const string RootOverrideVariable = "ENGISENTRY_COORDINATE_REQUEST_ROOT";

        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EngiSentry", "coordinate-requests");

        public static string? ResolveRootOverride()
        {
            string? value = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string ResolveRoot() => ResolveRootOverride() ?? DefaultRoot;

        /// <summary>The schedule header: the four contract columns, the optional elevation, then informational context.</summary>
        internal static readonly string[] ScheduleHeader =
        {
            "OccurrenceId", "EquipmentTag", "OccurrenceIndex", "SourceEntityHandle", "Layer",
            "CurrentPlacementStatus", "CurrentPlacementReason", "Easting", "Northing", "Elevation"
        };

        private readonly string root;

        public AuthoritativeEquipmentCoordinateRequestBuilder() : this(ResolveRoot()) { }

        public AuthoritativeEquipmentCoordinateRequestBuilder(string root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public string Root => root;

        /// <summary>
        /// Writes the blank request package under <c>&lt;root&gt;\&lt;DrawingSha256&gt;\&lt;UTC-timestamp&gt;\</c>
        /// and returns a summary of what was written.
        /// </summary>
        public AuthoritativeEquipmentCoordinateRequestResult Create(
            string? drawingName,
            string drawingSha256,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> inventory,
            DateTime utcNow)
        {
            if (!AuthoritativeEquipmentCoordinatePackageStore.IsSha256(drawingSha256))
                throw new ArgumentException("drawingSha256 must be a 64-character hexadecimal SHA256", nameof(drawingSha256));

            var entries = (inventory ?? Array.Empty<AuthoritativeEquipmentCoordinateInventoryEntry>())
                .Where(x => x != null)
                .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
                .ToList();

            string timestamp = utcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string drawingDirectory = Path.Combine(root, drawingSha256);
            string packageDirectory = Path.Combine(drawingDirectory, timestamp);
            for (int suffix = 1; Directory.Exists(packageDirectory); suffix++)
                packageDirectory = Path.Combine(drawingDirectory, timestamp + "-" + suffix.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(packageDirectory);

            var manifest = new AuthoritativeEquipmentCoordinateManifest
            {
                Schema = AuthoritativeEquipmentCoordinateSchema.Current,
                DrawingName = drawingName ?? string.Empty,
                DrawingSha256 = drawingSha256,
                CoordinateUnits = AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters,
                CoordinateBasis = AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs,
                ScheduleFileName = AuthoritativeEquipmentCoordinateSchema.ScheduleFileName,
                ProjectName = string.Empty,
                IssuedBy = string.Empty,
                IssuedByRole = string.Empty,
                IssuedTimestampUtc = string.Empty,
                SourceReference = string.Empty,
                Notes = "TEMPLATE — the project authority must complete ProjectName, IssuedBy, IssuedByRole, "
                    + "IssuedTimestampUtc and SourceReference, then fill Easting and Northing for every row, "
                    + "before this package is imported with ES_COORDIMPORT."
            };

            string manifestPath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName);
            string schedulePath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName);
            File.WriteAllBytes(manifestPath, AuthoritativeEquipmentCoordinatePackageSerializer.SerializeManifest(manifest));
            File.WriteAllBytes(schedulePath, Encoding.UTF8.GetBytes(BuildScheduleCsv(entries)));

            return new AuthoritativeEquipmentCoordinateRequestResult
            {
                PackageDirectory = packageDirectory,
                ManifestPath = manifestPath,
                SchedulePath = schedulePath,
                DrawingName = drawingName ?? string.Empty,
                DrawingSha256 = drawingSha256,
                RowCount = entries.Count,
                NonEmptyOccurrenceIdCount = entries.Count(x => !string.IsNullOrWhiteSpace(x.OccurrenceId)),
                NonEmptyEquipmentTagCount = entries.Count(x => !string.IsNullOrWhiteSpace(x.EquipmentTag)),
                BlankEastingCount = entries.Count,   // every Easting is blank by construction
                BlankNorthingCount = entries.Count,  // every Northing is blank by construction
                DuplicateOccurrenceIdCount = entries.GroupBy(x => x.OccurrenceId, StringComparer.Ordinal).Count(g => g.Count() > 1),
                MissingOccurrenceIdCount = entries.Count(x => string.IsNullOrWhiteSpace(x.OccurrenceId))
            };
        }

        internal static string BuildScheduleCsv(IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry> entries)
        {
            var builder = new StringBuilder();
            builder.Append(string.Join(",", ScheduleHeader));
            builder.Append("\r\n");
            foreach (AuthoritativeEquipmentCoordinateInventoryEntry entry in entries)
            {
                var fields = new[]
                {
                    entry.OccurrenceId,
                    entry.EquipmentTag,
                    entry.OccurrenceIndex.ToString(CultureInfo.InvariantCulture),
                    entry.SourceEntityHandle,
                    entry.Layer,
                    entry.CurrentPlacementStatus,
                    entry.CurrentPlacementReason,
                    string.Empty, // Easting — intentionally blank
                    string.Empty, // Northing — intentionally blank
                    string.Empty  // Elevation — intentionally blank
                };
                builder.Append(string.Join(",", fields.Select(EscapeCsv)));
                builder.Append("\r\n");
            }
            return builder.ToString();
        }

        private static string EscapeCsv(string? value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
