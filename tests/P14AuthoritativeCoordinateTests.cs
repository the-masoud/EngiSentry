using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.Core;
using EngiSentry.PlotPlan;
using EngiSentry.Semantics;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// P14 focused acceptance: the authoritative equipment coordinate package contract.
    ///
    /// Every coordinate in this file is FULLY SYNTHETIC. No real, inferred, or copied pilot
    /// coordinate is used anywhere, and nothing here ever fills in a real pilot coordinate.
    /// </summary>
    internal static class P14AuthoritativeCoordinateTests
    {
        // An obviously synthetic 64-character drawing hash (never a real drawing).
        private static readonly string SyntheticDrawingSha = string.Concat(Enumerable.Repeat("0123456789ABCDEF", 4));
        private const string SyntheticDrawingName = "SYNTHETIC-P14-TEST-ONLY.dwg";

        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        public static void Run()
        {
            SyntheticPositiveAcceptance();
            StoreAndProviderAcceptance();
            PlacementIntegrationAcceptance();
            NegativeMatrix();
            Console.WriteLine("P14 authoritative coordinate package tests passed.");
        }

        // ---------------------------------------------------------------- fixtures

        private static List<AuthoritativeEquipmentCoordinateInventoryEntry> SyntheticInventory() => new List<AuthoritativeEquipmentCoordinateInventoryEntry>
        {
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:P-90A:AAAA01", EquipmentTag = "P-90A", OccurrenceIndex = 1, SourceEntityHandle = "AAAA01", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" },
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:P-90B:AAAA02", EquipmentTag = "P-90B", OccurrenceIndex = 2, SourceEntityHandle = "AAAA02", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" },
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:SU-90:AAAA03", EquipmentTag = "SU-90", OccurrenceIndex = 3, SourceEntityHandle = "AAAA03", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" }
        };

        private static AuthoritativeEquipmentCoordinateManifest SyntheticManifest() => new AuthoritativeEquipmentCoordinateManifest
        {
            Schema = AuthoritativeEquipmentCoordinateSchema.Current,
            DrawingName = SyntheticDrawingName,
            DrawingSha256 = SyntheticDrawingSha,
            CoordinateUnits = AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters,
            CoordinateBasis = AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs,
            ScheduleFileName = AuthoritativeEquipmentCoordinateSchema.ScheduleFileName,
            ProjectName = "SYNTHETIC PROJECT (TEST ONLY)",
            IssuedBy = "SYNTHETIC AUTHORITY (TEST ONLY)",
            IssuedByRole = "TEST_ONLY",
            IssuedTimestampUtc = "2026-10-01T00:00:00.000Z",
            SourceReference = "SYNTHETIC-REFERENCE-0001",
            Notes = "SYNTHETIC / TEST_ONLY — no real project data."
        };

        private const string SyntheticScheduleHeader = "OccurrenceId,EquipmentTag,OccurrenceIndex,SourceEntityHandle,Layer,CurrentPlacementStatus,CurrentPlacementReason,Easting,Northing,Elevation";

        private static string SyntheticScheduleCsv() => string.Join("\r\n", new[]
        {
            SyntheticScheduleHeader,
            "placement:equipment:P-90A:AAAA01,P-90A,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1234.5,6789.25,12.5",
            "placement:equipment:P-90B:AAAA02,P-90B,2,AAAA02,EQ-TAG,Incomplete,SYNTHETIC,2000,3000,",
            "placement:equipment:SU-90:AAAA03,SU-90,3,AAAA03,EQ-TAG,Incomplete,SYNTHETIC,500,600.125,",
            ""
        });

        private static byte[] ManifestBytes(Action<AuthoritativeEquipmentCoordinateManifest>? mutate = null)
        {
            AuthoritativeEquipmentCoordinateManifest manifest = SyntheticManifest();
            mutate?.Invoke(manifest);
            return AuthoritativeEquipmentCoordinatePackageSerializer.SerializeManifest(manifest);
        }

        private static byte[] ScheduleBytes(string? csv = null) => Encoding.UTF8.GetBytes(csv ?? SyntheticScheduleCsv());

        private static AuthoritativeEquipmentCoordinateValidationResult Validate(
            byte[]? manifest, byte[]? schedule,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory = null,
            string? drawingSha = null)
            => AuthoritativeEquipmentCoordinatePackageValidator.ValidateBytes(
                manifest, schedule, inventory ?? SyntheticInventory(), drawingSha ?? SyntheticDrawingSha);

        private static string ExpectStatus(string name, string expected, byte[]? manifest, byte[]? schedule,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory = null, string? drawingSha = null)
        {
            AuthoritativeEquipmentCoordinateValidationResult result = Validate(manifest, schedule, inventory, drawingSha);
            Assert(string.Equals(result.Status, expected, StringComparison.Ordinal),
                name + ": expected " + expected + " but got " + result.Status + " (" + string.Join(" | ", result.Messages) + ")");
            Assert(!result.IsValid, name + ": a rejected package must never be valid.");
            Assert(result.Coordinates.Count == 0, name + ": a rejected package must never expose coordinates.");
            return result.Status;
        }

        private static string TempRoot() => Path.Combine(Path.GetTempPath(), "engisentry-p14-" + Guid.NewGuid().ToString("N"));

        // ---------------------------------------------------------------- positive

        private static void SyntheticPositiveAcceptance()
        {
            AuthoritativeEquipmentCoordinateValidationResult result = Validate(ManifestBytes(), ScheduleBytes());
            Assert(result.IsValid, "the synthetic positive package must validate: " + string.Join(" | ", result.Messages));
            Assert(result.ExpectedOccurrenceCount == 3 && result.ResolvedOccurrenceCount == 3, "coverage counts were wrong.");
            Assert(result.Coordinates.Count == 3, "expected three coordinates.");
            Assert(AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(result.ManifestSha256)
                && AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(result.ScheduleSha256)
                && AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(result.PackageFingerprint),
                "EngiSentry must compute all three hashes itself.");

            AuthoritativeEquipmentCoordinate first = result.Coordinates.Single(x => x.OccurrenceId == "placement:equipment:P-90A:AAAA01");
            Assert(first.EquipmentTag == "P-90A" && first.Easting == 1234.5m && first.Northing == 6789.25m && first.Elevation == 12.5m, "coordinate values were misread.");
            Assert(first.CoordinateSourceType == AuthoritativeCoordinateSourceTypes.AuthoritativeProjectSchedule
                && first.SourceUnits == AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters
                && first.SourceCoordinateBasis == AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs
                && first.TransformType == AuthoritativeCoordinateTransformTypes.Identity,
                "coordinate provenance fields were not populated.");
            Assert(result.Coordinates.Single(x => x.OccurrenceId == "placement:equipment:P-90B:AAAA02").Elevation == null, "an absent elevation must stay absent.");

            // The fingerprint binds manifest ++ 0x00 ++ schedule, and is stable across recomputation.
            byte[] manifest = ManifestBytes(), schedule = ScheduleBytes();
            string fingerprint = AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifest, schedule);
            Assert(fingerprint == result.PackageFingerprint, "package fingerprint is not content-addressed.");
            Assert(fingerprint == AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifest, schedule), "package fingerprint is not deterministic.");

            // Allowed normalization: surrounding whitespace is trimmed; tag case follows the existing policy.
            string padded = string.Join("\r\n", new[]
            {
                SyntheticScheduleHeader,
                "  placement:equipment:P-90A:AAAA01  ,  p-90a  ,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1234.5,6789.25,12.5",
                "placement:equipment:P-90B:AAAA02,P-90B,2,AAAA02,EQ-TAG,Incomplete,SYNTHETIC,2000,3000,",
                "placement:equipment:SU-90:AAAA03,SU-90,3,AAAA03,EQ-TAG,Incomplete,SYNTHETIC,500,600.125,",
                ""
            });
            Assert(Validate(ManifestBytes(), ScheduleBytes(padded)).IsValid, "whitespace trimming and tag case normalization must be accepted.");

            // Request generation produces blanks only and never a coordinate.
            string root = TempRoot();
            try
            {
                AuthoritativeEquipmentCoordinateRequestResult request = new AuthoritativeEquipmentCoordinateRequestBuilder(root)
                    .Create(SyntheticDrawingName, SyntheticDrawingSha, SyntheticInventory(), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
                Assert(request.RowCount == 3 && request.NonEmptyOccurrenceIdCount == 3 && request.NonEmptyEquipmentTagCount == 3, "request row counts were wrong.");
                Assert(request.BlankEastingCount == 3 && request.BlankNorthingCount == 3 && request.DuplicateOccurrenceIdCount == 0 && request.MissingOccurrenceIdCount == 0, "request blank-coordinate guarantees were violated.");
                string[] lines = File.ReadAllLines(request.SchedulePath);
                Assert(lines.Length == 4 && lines[0] == SyntheticScheduleHeader, "request schedule header was wrong.");
                foreach (string line in lines.Skip(1))
                {
                    string[] fields = line.Split(',');
                    Assert(fields.Length == 10 && fields[7].Length == 0 && fields[8].Length == 0 && fields[9].Length == 0,
                        "a request row must carry blank Easting, Northing and Elevation.");
                }
                // A request template is intentionally incomplete and must fail closed until the authority completes it.
                AuthoritativeEquipmentCoordinateValidationResult template = AuthoritativeEquipmentCoordinatePackageValidator.Validate(request.PackageDirectory, SyntheticInventory(), SyntheticDrawingSha);
                Assert(!template.IsValid && template.Status == AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest,
                    "an uncompleted request template must fail closed.");
            }
            finally { TryDelete(root); }
        }

        // ---------------------------------------------------------------- store + provider

        private static void StoreAndProviderAcceptance()
        {
            string root = TempRoot();
            try
            {
                var store = new AuthoritativeEquipmentCoordinatePackageStore(root);
                var provider = new AuthoritativeEquipmentCoordinateProvider(store);
                AuthoritativeEquipmentCoordinateValidationResult validation = Validate(ManifestBytes(), ScheduleBytes());
                Assert(validation.IsValid, "fixture must validate before installation.");
                Assert(provider.ResolveForDrawing(SyntheticDrawingSha).Count == 0, "no package is active yet.");
                Assert(store.ReadActive(SyntheticDrawingSha) == null, "no active descriptor should exist yet.");

                AuthoritativeEquipmentCoordinateImportReceipt receipt = store.Install(SyntheticDrawingSha, WritePackage(validation), validation, null);
                Assert(receipt.Status == AuthoritativeEquipmentCoordinateInstallationStatuses.Installed && receipt.OccurrenceCount == 3, "installation receipt was wrong.");
                Assert(store.IsActive(SyntheticDrawingSha, validation.PackageFingerprint), "the installed package must be active.");

                IReadOnlyList<AuthoritativeEquipmentCoordinate> resolved = provider.ResolveForDrawing(SyntheticDrawingSha);
                Assert(resolved.Count == 3, "the active package must resolve three occurrences.");
                AuthoritativeEquipmentCoordinate? one = provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA01", "P-90A");
                Assert(one != null && one.Easting == 1234.5m && one.Northing == 6789.25m, "occurrence-plus-tag lookup failed.");

                // No tag-only lookup, no cross-occurrence lookup, no wrong-tag lookup.
                Assert(provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA01", "P-90B") == null, "a wrong tag must not resolve.");
                Assert(provider.Resolve(SyntheticDrawingSha, "placement:equipment:UNKNOWN:ZZZZ", "P-90A") == null, "an unknown occurrence must not resolve.");
                Assert(provider.Resolve(SyntheticDrawingSha, null, "P-90A") == null && provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA01", null) == null, "a partial key must not resolve.");
                Assert(provider.Resolve(string.Concat(Enumerable.Repeat("FEDCBA9876543210", 4)), "placement:equipment:P-90A:AAAA01", "P-90A") == null, "a different drawing must not resolve.");

                // Re-installing the identical package is idempotent and creates no new content.
                string packageDirectory = store.PackageDirectory(SyntheticDrawingSha, validation.PackageFingerprint);
                string[] before = Directory.GetFiles(packageDirectory).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                store.Install(SyntheticDrawingSha, packageDirectory, validation, validation.PackageFingerprint);
                string[] after = Directory.GetFiles(packageDirectory).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                Assert(before.SequenceEqual(after), "re-installing the same package must not rewrite its content.");
                Assert(File.Exists(store.ActivePath(SyntheticDrawingSha)), "active.json must exist.");

                // Tampering with the stored schedule must fail closed at the provider.
                string schedulePath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName);
                byte[] original = File.ReadAllBytes(schedulePath);
                File.WriteAllBytes(schedulePath, Encoding.UTF8.GetBytes(SyntheticScheduleCsv().Replace("1234.5", "9999.5")));
                Assert(provider.ResolveForDrawing(SyntheticDrawingSha).Count == 0, "a tampered stored package must yield nothing.");
                File.WriteAllBytes(schedulePath, original);
                Assert(provider.ResolveForDrawing(SyntheticDrawingSha).Count == 3, "the restored package must resolve again.");
            }
            finally { TryDelete(root); }
        }

        private static string WritePackage(AuthoritativeEquipmentCoordinateValidationResult validation)
        {
            // The validator's own request-style layout: write the exact bytes we validated to a scratch directory.
            string directory = TempRoot();
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName), ManifestBytes());
            File.WriteAllBytes(Path.Combine(directory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName), ScheduleBytes());
            return directory;
        }

        // ---------------------------------------------------------------- placement

        private static void PlacementIntegrationAcceptance()
        {
            // A deterministically owned TXT-ID pair resolves the placement with no authoritative package.
            PlotPlanModel baseline = BuildOwnedFixture();
            EquipmentPlacement baselinePlacement = baseline.EquipmentPlacements.Single();
            Assert(baselinePlacement.PlacementStatus == "Complete" && baselinePlacement.AuthoritativeCoordinate == null,
                "without an authoritative package the existing behaviour must be unchanged.");
            string occurrenceId = baselinePlacement.PlacementId;
            string tag = baselinePlacement.NormalizedTag;
            decimal deterministicEasting = baselinePlacement.Easting.SelectedValue!.Value;
            decimal deterministicNorthing = baselinePlacement.Northing.SelectedValue!.Value;

            // Agreeing authoritative coordinate: existing evidence is kept and corroborated.
            EquipmentPlacement agreed = BuildOwnedFixture(Authoritative(occurrenceId, tag, deterministicEasting, deterministicNorthing)).EquipmentPlacements.Single();
            Assert(agreed.PlacementStatus == "Complete"
                && agreed.AuthoritativeCoordinate?.PlacementStatus == AuthoritativePlacementStatuses.Resolved
                && agreed.AuthoritativeCoordinate.ConflictWithEvidenceSource == null
                && agreed.Easting.SelectedSourceHandle == "OWNED:E",
                "an agreeing authoritative coordinate must corroborate, never replace, existing evidence.");

            // Disagreeing authoritative coordinate: conflict, and nothing is silently replaced.
            EquipmentPlacement conflicted = BuildOwnedFixture(Authoritative(occurrenceId, tag, deterministicEasting + 1m, deterministicNorthing)).EquipmentPlacements.Single();
            Assert(conflicted.PlacementStatus == "Ambiguous"
                && conflicted.AuthoritativeCoordinate?.PlacementStatus == AuthoritativePlacementStatuses.Conflict
                && conflicted.AuthoritativeCoordinate.ConflictWithEvidenceSource == "TXT-ID"
                && conflicted.Easting.SelectedValue == deterministicEasting && conflicted.Easting.SelectedSourceHandle == "OWNED:E",
                "a conflicting authoritative coordinate must be surfaced and must not delete existing evidence.");

            // An unresolved placement is resolved by the authoritative coordinate.
            PlotPlanModel unresolvedPlan = BuildUnownedFixture();
            EquipmentPlacement unresolved = unresolvedPlan.EquipmentPlacements.Single();
            EquipmentPlacement resolved = BuildUnownedFixture(Authoritative(unresolved.PlacementId, unresolved.NormalizedTag, 4321.75m, 8765.25m)).EquipmentPlacements.Single();
            Assert(resolved.PlacementStatus == "Complete"
                && resolved.Easting.SelectedValue == 4321.75m && resolved.Northing.SelectedValue == 8765.25m
                && resolved.AuthoritativeCoordinate?.PlacementStatus == AuthoritativePlacementStatuses.Resolved
                && resolved.AuthoritativeCoordinate.OccurrenceId == unresolved.PlacementId,
                "an authoritative coordinate must resolve an unresolved placement and carry its provenance.");

            // A matching occurrence with a mismatched tag must never resolve.
            EquipmentPlacement mismatched = BuildUnownedFixture(Authoritative(unresolved.PlacementId, "P-99Z", 1m, 2m)).EquipmentPlacements.Single();
            Assert(mismatched.PlacementStatus == "Incomplete" && mismatched.AuthoritativeCoordinate == null,
                "a tag that does not match the occurrence must never resolve it.");

            // No package at all leaves behaviour untouched.
            Assert(BuildUnownedFixture().EquipmentPlacements.Single().PlacementStatus == "Incomplete", "null authoritative input must be a no-op.");
        }

        private static AuthoritativeEquipmentCoordinate Authoritative(string occurrenceId, string tag, decimal easting, decimal northing) => new AuthoritativeEquipmentCoordinate
        {
            OccurrenceId = occurrenceId,
            EquipmentTag = tag,
            Easting = easting,
            Northing = northing,
            DrawingSha256 = SyntheticDrawingSha,
            PackageFingerprint = new string('B', 64),
            ManifestSha256 = new string('C', 64),
            ScheduleSha256 = new string('D', 64),
            SourceUnits = AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters,
            SourceCoordinateBasis = AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs,
            IssuedBy = "SYNTHETIC AUTHORITY (TEST ONLY)",
            SourceReference = "SYNTHETIC-REFERENCE-0001",
            EvidenceSummary = "SYNTHETIC / TEST_ONLY"
        };

        private static PlotPlanModel BuildOwnedFixture(AuthoritativeEquipmentCoordinate? authoritative = null)
        {
            var snapshot = new DrawingSnapshot();
            snapshot.TextEntities.Add(new TextSnapshot { Text = "P-91A", Handle = "OWNED:TAG", Layer = "EQ-TAG", SpaceKind = "Model", Height = 1, PositionX = 0, PositionY = 0 });
            snapshot.TextEntities.Add(new TextSnapshot { Text = "X= 500", Handle = "OWNED:E", Layer = "TXT-ID", SpaceKind = "Model", PositionX = 0, PositionY = 0 });
            snapshot.TextEntities.Add(new TextSnapshot { Text = "Y= 600", Handle = "OWNED:N", Layer = "TXT-ID", SpaceKind = "Model", PositionX = 0, PositionY = 0 });
            EngineeringDrawingModel model = SemanticExtractor.Extract(snapshot);
            return PlotPlanBuilder.Build(EntityIndexer.Build(model), model, snapshot, null, null, Wrap(authoritative));
        }

        private static PlotPlanModel BuildUnownedFixture(AuthoritativeEquipmentCoordinate? authoritative = null)
        {
            var snapshot = new DrawingSnapshot();
            snapshot.TextEntities.Add(new TextSnapshot { Text = "P-92A", Handle = "SOLO:TAG", Layer = "EQ-TAG", SpaceKind = "Model", Height = 1, PositionX = 0, PositionY = 0 });
            snapshot.TextEntities.Add(new TextSnapshot { Text = "X= 700", Handle = "PAPER:E", Layer = "EQ-ID", SpaceKind = "Paper", PositionX = 0, PositionY = 0 });
            EngineeringDrawingModel model = SemanticExtractor.Extract(snapshot);
            return PlotPlanBuilder.Build(EntityIndexer.Build(model), model, snapshot, null, null, Wrap(authoritative));
        }

        private static IReadOnlyList<AuthoritativeEquipmentCoordinate>? Wrap(AuthoritativeEquipmentCoordinate? coordinate)
            => coordinate == null ? null : new List<AuthoritativeEquipmentCoordinate> { coordinate };

        // ---------------------------------------------------------------- negative matrix

        private static void NegativeMatrix()
        {
            var cases = new List<Tuple<string, string, byte[]?, byte[]?, IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>?, string?>>();

            void Add(string name, string expected, byte[]? manifest, byte[]? schedule,
                IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory = null, string? drawingSha = null)
                => cases.Add(Tuple.Create(name, expected, manifest, schedule, inventory, drawingSha));

            // Manifest: schema, structure, provenance.
            Add("P14-N01 wrong schema", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchema, ManifestBytes(m => m.Schema = "ENGISENTRY-EQUIPMENT-COORDINATES-0"), ScheduleBytes());
            Add("P14-N02 malformed manifest JSON", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, Encoding.UTF8.GetBytes("{ not json"), ScheduleBytes());
            Add("P14-N03 null manifest JSON", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, Encoding.UTF8.GetBytes("null"), ScheduleBytes());
            Add("P14-N04 missing IssuedBy", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.IssuedBy = ""), ScheduleBytes());
            Add("P14-N05 missing IssuedByRole", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.IssuedByRole = ""), ScheduleBytes());
            Add("P14-N06 missing IssuedTimestampUtc", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.IssuedTimestampUtc = ""), ScheduleBytes());
            Add("P14-N07 missing SourceReference", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.SourceReference = ""), ScheduleBytes());
            Add("P14-N08 missing ProjectName", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.ProjectName = ""), ScheduleBytes());
            Add("P14-N09 missing DrawingName", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.DrawingName = ""), ScheduleBytes());
            Add("P14-N10 missing CoordinateUnits", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.CoordinateUnits = ""), ScheduleBytes());
            Add("P14-N11 missing CoordinateBasis", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.CoordinateBasis = ""), ScheduleBytes());
            Add("P14-N12 missing ScheduleFileName", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest, ManifestBytes(m => m.ScheduleFileName = ""), ScheduleBytes());

            // Manifest: path traversal / alternate directory / unexpected name.
            Add("P14-N13 alternate directory", AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, ManifestBytes(m => m.ScheduleFileName = "sub/equipment-coordinate-schedule.csv"), ScheduleBytes());
            Add("P14-N14 parent traversal", AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, ManifestBytes(m => m.ScheduleFileName = "../equipment-coordinate-schedule.csv"), ScheduleBytes());
            Add("P14-N15 absolute path", AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, ManifestBytes(m => m.ScheduleFileName = @"C:\abs\equipment-coordinate-schedule.csv"), ScheduleBytes());
            Add("P14-N16 unexpected file name", AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, ManifestBytes(m => m.ScheduleFileName = "schedule.csv"), ScheduleBytes());
            Add("P14-N17 missing manifest bytes", AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid, null, ScheduleBytes());

            // Drawing binding.
            Add("P14-N18 wrong drawing hash", AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, ManifestBytes(m => m.DrawingSha256 = new string('9', 64)), ScheduleBytes());
            Add("P14-N19 malformed drawing hash", AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, ManifestBytes(m => m.DrawingSha256 = "not-a-hash"), ScheduleBytes());
            Add("P14-N20 no current drawing hash", AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch, ManifestBytes(), ScheduleBytes(), null, "");

            // Unit and coordinate basis.
            Add("P14-N21 unsupported units", AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedUnits, ManifestBytes(m => m.CoordinateUnits = "Millimeters"), ScheduleBytes());
            Add("P14-N22 unsupported basis", AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedCoordinateBasis, ManifestBytes(m => m.CoordinateBasis = "SITE_GRID"), ScheduleBytes());

            // Schedule structure.
            Add("P14-N23 empty schedule", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes(""));
            Add("P14-N24 missing required column", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes("OccurrenceId,EquipmentTag,Northing\r\nplacement:equipment:P-90A:AAAA01,P-90A,1\r\n"));
            Add("P14-N25 duplicate column", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes("OccurrenceId,OccurrenceId,EquipmentTag,Easting,Northing\r\n"));
            Add("P14-N26 too many fields", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes(SyntheticScheduleHeader + "\r\nplacement:equipment:P-90A:AAAA01,P-90A,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1,2,,EXTRA\r\n"));
            Add("P14-N27 empty OccurrenceId", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes(SyntheticScheduleHeader + "\r\n,P-90A,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1,2,\r\n"));
            Add("P14-N28 empty EquipmentTag", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule, ManifestBytes(), ScheduleBytes(SyntheticScheduleHeader + "\r\nplacement:equipment:P-90A:AAAA01,,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1,2,\r\n"));

            // Coordinate values.
            Add("P14-N29 non-numeric Easting", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate, ManifestBytes(), ScheduleBytes(ReplaceRow(0, "abc")));
            Add("P14-N30 NaN Northing", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate, ManifestBytes(), ScheduleBytes(ReplaceRow(1, "NaN")));
            Add("P14-N31 grouped Easting", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate, ManifestBytes(), ScheduleBytes(ReplaceRow(0, "\"1,500,000\"")));
            Add("P14-N32 Infinity Easting", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate, ManifestBytes(), ScheduleBytes(ReplaceRow(0, "Infinity")));
            Add("P14-N33 malformed Elevation", AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate, ManifestBytes(), ScheduleBytes(SyntheticScheduleCsv().Replace(",1234.5,6789.25,12.5", ",1234.5,6789.25,12.3.4")));

            // Occurrence identity and coverage.
            Add("P14-N34 duplicate occurrence", AuthoritativeEquipmentCoordinateValidationStatuses.DuplicateOccurrence, ManifestBytes(), ScheduleBytes(SyntheticScheduleCsv().Replace("placement:equipment:P-90B:AAAA02,P-90B", "placement:equipment:P-90A:AAAA01,P-90B")));
            Add("P14-N35 unknown occurrence", AuthoritativeEquipmentCoordinateValidationStatuses.OccurrenceMismatch, ManifestBytes(), ScheduleBytes(SyntheticScheduleCsv().Replace("placement:equipment:SU-90:AAAA03", "placement:equipment:XX-90:AAAA09")));
            Add("P14-N36 tag does not match occurrence", AuthoritativeEquipmentCoordinateValidationStatuses.OccurrenceMismatch, ManifestBytes(), ScheduleBytes(SyntheticScheduleCsv().Replace("placement:equipment:P-90B:AAAA02,P-90B", "placement:equipment:P-90B:AAAA02,SU-90")));
            Add("P14-N37 incomplete coverage", AuthoritativeEquipmentCoordinateValidationStatuses.IncompleteCoverage, ManifestBytes(), ScheduleBytes(string.Join("\r\n", new[] { SyntheticScheduleHeader, "placement:equipment:P-90A:AAAA01,P-90A,1,AAAA01,EQ-TAG,Incomplete,SYNTHETIC,1234.5,6789.25,12.5", "" })));
            Add("P14-N38 empty inventory", AuthoritativeEquipmentCoordinateValidationStatuses.IncompleteCoverage, ManifestBytes(), ScheduleBytes(), new List<AuthoritativeEquipmentCoordinateInventoryEntry>());

            Assert(cases.Count >= 30, "the negative matrix must contain at least 30 cases (found " + cases.Count + ").");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var statuses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var testCase in cases)
            {
                Assert(seen.Add(testCase.Item1), "duplicate negative case name: " + testCase.Item1);
                statuses.Add(ExpectStatus(testCase.Item1, testCase.Item2, testCase.Item3, testCase.Item4, testCase.Item5, testCase.Item6));
            }

            // Every named rejection status in the contract must be exercised by the matrix.
            foreach (string required in new[]
            {
                AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchema,
                AuthoritativeEquipmentCoordinateValidationStatuses.InvalidManifest,
                AuthoritativeEquipmentCoordinateValidationStatuses.DrawingMismatch,
                AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedUnits,
                AuthoritativeEquipmentCoordinateValidationStatuses.UnsupportedCoordinateBasis,
                AuthoritativeEquipmentCoordinateValidationStatuses.InvalidSchedule,
                AuthoritativeEquipmentCoordinateValidationStatuses.InvalidCoordinate,
                AuthoritativeEquipmentCoordinateValidationStatuses.OccurrenceMismatch,
                AuthoritativeEquipmentCoordinateValidationStatuses.DuplicateOccurrence,
                AuthoritativeEquipmentCoordinateValidationStatuses.IncompleteCoverage,
                AuthoritativeEquipmentCoordinateValidationStatuses.PathInvalid
            }) Assert(statuses.Contains(required), "the negative matrix never exercised status: " + required);
        }

        private static string ReplaceRow(int rowIndex, string easting)
        {
            string[] lines = SyntheticScheduleCsv().Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] fields = lines[rowIndex + 1].Split(',');
            fields[7] = easting;
            lines[rowIndex + 1] = string.Join(",", fields);
            return string.Join("\r\n", lines);
        }

        private static void TryDelete(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { /* best effort scratch cleanup */ }
        }
    }
}
