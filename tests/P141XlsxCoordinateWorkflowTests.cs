using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Core;
using EngiSentry.PlotPlan;
using EngiSentry.Semantics;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// P14.1 focused acceptance: the engineer-facing XLSX coordinate workflow.
    ///
    /// Every coordinate, tag, document number and hash in this file is FULLY SYNTHETIC. No real,
    /// inferred or copied pilot coordinate is used anywhere, and nothing here ever fills in a real
    /// pilot coordinate. All synthetic evidence is marked SYNTHETIC / TEST_ONLY.
    /// </summary>
    internal static class P141XlsxCoordinateWorkflowTests
    {
        private static readonly string SyntheticDrawingSha = string.Concat(Enumerable.Repeat("0123456789ABCDEF", 4));
        private const string SyntheticDrawingName = "SYNTHETIC-P141-TEST-ONLY.dwg";
        private static readonly DateTime SyntheticUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        public static void Run()
        {
            RequestGenerationAcceptance();
            WorkbookRoundTripAcceptance();
            SemanticIdentityAcceptance();
            StoreAndProviderAcceptance();
            NestedStorePathAcceptance();
            PlacementProvenanceAcceptance();
            NegativeMatrix();
            Console.WriteLine("P14.1 engineer-facing XLSX coordinate workflow tests passed.");
        }

        // ---------------------------------------------------------------- fixtures

        /// <summary>
        /// Four synthetic occurrences with only THREE distinct tags: P-90A appears twice with two
        /// different handles. Occurrence identity must therefore be the occurrence id, never the tag.
        /// </summary>
        private static List<AuthoritativeEquipmentCoordinateInventoryEntry> SyntheticInventory() => new List<AuthoritativeEquipmentCoordinateInventoryEntry>
        {
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:P-90A:AAAA01", EquipmentTag = "P-90A", OccurrenceIndex = 1, SourceEntityHandle = "AAAA01", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" },
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:P-90A:AAAA02", EquipmentTag = "P-90A", OccurrenceIndex = 2, SourceEntityHandle = "AAAA02", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" },
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:P-90B:AAAA03", EquipmentTag = "P-90B", OccurrenceIndex = 3, SourceEntityHandle = "AAAA03", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" },
            new AuthoritativeEquipmentCoordinateInventoryEntry { OccurrenceId = "placement:equipment:SU-90:AAAA04", EquipmentTag = "SU-90", OccurrenceIndex = 4, SourceEntityHandle = "AAAA04", Layer = "EQ-TAG", CurrentPlacementStatus = "Incomplete", CurrentPlacementReason = "SYNTHETIC" }
        };

        private sealed class SyntheticPoint
        {
            public string OccurrenceId = string.Empty;
            public decimal Easting;
            public decimal Northing;
            public decimal? Elevation;
        }

        private static readonly SyntheticPoint[] SyntheticPoints =
        {
            new SyntheticPoint { OccurrenceId = "placement:equipment:P-90A:AAAA01", Easting = 101.25m, Northing = 201.5m, Elevation = 12.5m },
            new SyntheticPoint { OccurrenceId = "placement:equipment:P-90A:AAAA02", Easting = 102.75m, Northing = 202.125m, Elevation = null },
            new SyntheticPoint { OccurrenceId = "placement:equipment:P-90B:AAAA03", Easting = 300m, Northing = 400m, Elevation = null },
            new SyntheticPoint { OccurrenceId = "placement:equipment:SU-90:AAAA04", Easting = 500.5m, Northing = 600.75m, Elevation = 7m }
        };

        private static AuthoritativeEquipmentCoordinateWorkbookModel Model(
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory = null,
            bool fillMetadata = true, bool fillCoordinates = true)
        {
            AuthoritativeEquipmentCoordinateWorkbookModel model = AuthoritativeEquipmentCoordinateRequestBuilder.BuildWorkbookModel(
                SyntheticDrawingName, SyntheticDrawingSha, inventory ?? SyntheticInventory(), SyntheticUtc);
            if (fillMetadata) FillMetadata(model);
            if (fillCoordinates) FillCoordinates(model);
            return model;
        }

        private static void FillMetadata(AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            model.ProjectIdentifier = "SYNTHETIC PROJECT (TEST ONLY)";
            model.SourceDocumentNumber = "SYNTHETIC-DOC-0001";
            model.SourceRevision = "A1";
            model.SourceIssueStatus = "Issued for Construction";
            model.IssuedBy = "SYNTHETIC AUTHORITY (TEST ONLY)";
            model.IssueDate = "2026-10-01";
            model.Notes = "SYNTHETIC / TEST_ONLY";
        }

        private static void FillCoordinates(AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            foreach (AuthoritativeEquipmentCoordinateWorkbookRow row in model.Rows)
            {
                SyntheticPoint? point = SyntheticPoints.FirstOrDefault(p => p.OccurrenceId == row.OccurrenceId);
                if (point == null) continue;
                row.Easting = point.Easting.ToString(System.Globalization.CultureInfo.InvariantCulture);
                row.Northing = point.Northing.ToString(System.Globalization.CultureInfo.InvariantCulture);
                row.Elevation = point.Elevation.HasValue ? point.Elevation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
            }
        }

        private static byte[] WorkbookBytes(
            Action<AuthoritativeEquipmentCoordinateWorkbookModel>? mutate = null,
            IReadOnlyList<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory = null)
        {
            AuthoritativeEquipmentCoordinateWorkbookModel model = Model(inventory);
            mutate?.Invoke(model);
            return AuthoritativeEquipmentCoordinateWorkbookWriter.WriteToBytes(model);
        }

        /// <summary>
        /// Recomputes the request fingerprint after an identity change. This models an attacker who has
        /// the generator: the fingerprint is self-consistent, but the rows still must match the live
        /// drawing inventory, so the identity binding is what actually rejects the workbook.
        /// </summary>
        private static void ResyncFingerprint(AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            model.ExpectedOccurrenceCount = model.Rows.Count;
            model.RequestFingerprint = AuthoritativeEquipmentCoordinateRequestFingerprint.Compute(model.SchemaVersion, SyntheticDrawingSha, model.Rows);
        }

        private static string TempRoot() => Path.Combine(Path.GetTempPath(), "engisentry-p141-" + Guid.NewGuid().ToString("N"));

        private static void TryDelete(string directory)
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (Exception) { /* best effort */ }
        }

        private static AuthoritativeEquipmentCoordinateWorkbookProvenance Provenance(AuthoritativeEquipmentCoordinateWorkbookImportResult read) => new AuthoritativeEquipmentCoordinateWorkbookProvenance
        {
            SourceWorkbookSha256 = read.SourceWorkbookSha256,
            RequestFingerprint = read.RequestFingerprint,
            SourceDocumentNumber = read.Metadata.SourceDocumentNumber,
            SourceRevision = read.Metadata.SourceRevision,
            SourceIssueStatus = read.Metadata.SourceIssueStatus,
            IssueDate = read.Metadata.IssueDate
        };

        // ---------------------------------------------------------------- request generation

        private static void RequestGenerationAcceptance()
        {
            string root = TempRoot();
            try
            {
                AuthoritativeEquipmentCoordinateRequestResult request = new AuthoritativeEquipmentCoordinateRequestBuilder(root)
                    .CreateWorkbook(SyntheticDrawingName, SyntheticDrawingSha, SyntheticInventory(), SyntheticUtc);

                // Exactly ONE file is exposed to the engineer, and it is the workbook.
                string[] files = Directory.GetFiles(request.PackageDirectory, "*", SearchOption.AllDirectories);
                Assert(files.Length == 1, "the engineer must receive exactly one file, found " + files.Length + ".");
                Assert(string.Equals(Path.GetFileName(files[0]), AuthoritativeEquipmentCoordinateWorkbookSchema.WorkbookFileName, StringComparison.Ordinal),
                    "the single exposed file must be " + AuthoritativeEquipmentCoordinateWorkbookSchema.WorkbookFileName + ".");
                Assert(!files.Any(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    || f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)),
                    "no JSON or CSV may be exposed to the engineer.");

                Assert(request.RowCount == 4, "expected four workbook rows.");
                Assert(request.NonEmptyEquipmentTagCount == 4, "expected four equipment tags.");
                Assert(request.NonEmptyOccurrenceIdCount == 4, "expected four occurrence ids.");
                Assert(request.BlankEastingCount == 4 && request.BlankNorthingCount == 4 && request.BlankElevationCount == 4,
                    "every coordinate cell must be blank in a request.");
                Assert(request.DuplicateOccurrenceIdCount == 0 && request.MissingOccurrenceIdCount == 0,
                    "the request must have no duplicate and no missing occurrence id.");
                Assert(AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(request.RequestFingerprint),
                    "the request fingerprint must be a 64-character SHA256.");
                Assert(string.Equals(request.WorkbookPath,
                    Path.Combine(root, AuthoritativeEquipmentCoordinateWorkbookSchema.WorkbookFileName), StringComparison.Ordinal),
                    "the request must use the one stable engineer-facing workbook path.");
                byte[] original = File.ReadAllBytes(request.WorkbookPath);
                bool refusedExisting = false;
                try
                {
                    new AuthoritativeEquipmentCoordinateRequestBuilder(root)
                        .CreateWorkbook(SyntheticDrawingName, SyntheticDrawingSha, SyntheticInventory(), SyntheticUtc);
                }
                catch (IOException) { refusedExisting = true; }
                Assert(refusedExisting && File.ReadAllBytes(request.WorkbookPath).SequenceEqual(original),
                    "a second request must fail without overwriting the existing workbook.");
            }
            finally { TryDelete(root); }
        }

        // ---------------------------------------------------------------- round trip

        private static void WorkbookRoundTripAcceptance()
        {
            AuthoritativeEquipmentCoordinateWorkbookImportResult read = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                WorkbookBytes(), SyntheticInventory(), SyntheticDrawingSha);

            Assert(read.IsValid, "the synthetic positive workbook must validate: " + string.Join(" | ", read.Messages));
            Assert(read.Validation != null && read.Validation.IsValid, "the normalized P14 package must be valid.");
            Assert(read.Validation!.Coordinates.Count == 4, "expected four normalized coordinates.");
            Assert(read.Validation.ExpectedOccurrenceCount == 4 && read.Validation.ResolvedOccurrenceCount == 4, "coverage counts were wrong.");

            AuthoritativeEquipmentCoordinate first = read.Validation.Coordinates.Single(x => x.OccurrenceId == "placement:equipment:P-90A:AAAA01");
            Assert(first.EquipmentTag == "P-90A" && first.Easting == 101.25m && first.Northing == 201.5m && first.Elevation == 12.5m,
                "coordinate values were misread from the workbook.");

            AuthoritativeEquipmentCoordinate second = read.Validation.Coordinates.Single(x => x.OccurrenceId == "placement:equipment:P-90A:AAAA02");
            Assert(second.Easting == 102.75m && second.Northing == 202.125m && second.Elevation == null,
                "a blank Elevation must stay blank, and the duplicate tag must bind to its own occurrence.");

            Assert(AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(read.SourceWorkbookSha256), "the workbook hash must be computed by EngiSentry.");
            Assert(AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(read.Validation.PackageFingerprint), "the package fingerprint must be computed by EngiSentry.");
            Assert(!string.Equals(read.SourceWorkbookSha256, read.Validation.PackageFingerprint, StringComparison.Ordinal),
                "the workbook file hash and the semantic package fingerprint must be distinct identities.");

            Assert(first.SourceDocumentNumber == "SYNTHETIC-DOC-0001" && first.SourceRevision == "A1"
                && first.SourceIssueStatus == "Issued for Construction" && first.IssueDate == "2026-10-01",
                "the source-document provenance must survive normalization.");

            // Normalization must be byte-stable: the same workbook always yields the same package.
            AuthoritativeEquipmentCoordinateWorkbookImportResult again = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                WorkbookBytes(), SyntheticInventory(), SyntheticDrawingSha);
            Assert(string.Equals(again.Validation!.PackageFingerprint, read.Validation.PackageFingerprint, StringComparison.Ordinal),
                "normalization must be deterministic.");
        }

        // ---------------------------------------------------------------- semantic identity

        private static void SemanticIdentityAcceptance()
        {
            // Changing only a value the fingerprint deliberately excludes (GeneratedUtc) changes the
            // exact file bytes but must NOT change the semantic package identity.
            byte[] baselineBytes = WorkbookBytes();
            byte[] touchedBytes = WorkbookBytes(m => m.GeneratedUtc = "2099-01-01T00:00:00.000Z");

            Assert(!baselineBytes.SequenceEqual(touchedBytes), "the fixture must actually change the workbook bytes.");

            AuthoritativeEquipmentCoordinateWorkbookImportResult baseline = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                baselineBytes, SyntheticInventory(), SyntheticDrawingSha);
            AuthoritativeEquipmentCoordinateWorkbookImportResult touched = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                touchedBytes, SyntheticInventory(), SyntheticDrawingSha);

            Assert(baseline.IsValid && touched.IsValid, "both workbooks must be valid.");
            Assert(!string.Equals(baseline.SourceWorkbookSha256, touched.SourceWorkbookSha256, StringComparison.Ordinal),
                "different workbook bytes must have different file identities.");
            Assert(string.Equals(baseline.Validation!.PackageFingerprint, touched.Validation!.PackageFingerprint, StringComparison.Ordinal),
                "the semantic package identity must not depend on the exact workbook bytes.");

            // Changing a coordinate DOES change the authoritative package identity.
            byte[] movedBytes = WorkbookBytes(m => m.Rows[0].Easting = "999.125");
            AuthoritativeEquipmentCoordinateWorkbookImportResult moved = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                movedBytes, SyntheticInventory(), SyntheticDrawingSha);
            Assert(moved.IsValid, "the moved-coordinate workbook must still be structurally valid.");
            Assert(!string.Equals(moved.Validation!.PackageFingerprint, baseline.Validation.PackageFingerprint, StringComparison.Ordinal),
                "a different authoritative coordinate must produce a different package identity.");

            // The fingerprint must be insensitive to row order but sensitive to the identity set.
            byte[] reorderedBytes = WorkbookBytes(m =>
            {
                AuthoritativeEquipmentCoordinateWorkbookRow swap = m.Rows[0];
                m.Rows[0] = m.Rows[3];
                m.Rows[3] = swap;
                ResyncFingerprint(m);
            });
            AuthoritativeEquipmentCoordinateWorkbookImportResult reordered = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                reorderedBytes, SyntheticInventory(), SyntheticDrawingSha);
            Assert(reordered.IsValid, "reordering rows must remain valid: " + string.Join(" | ", reordered.Messages));
            Assert(string.Equals(reordered.Validation!.PackageFingerprint, baseline.Validation.PackageFingerprint, StringComparison.Ordinal),
                "row order must never affect the authoritative package identity.");
        }

        // ---------------------------------------------------------------- store and provider

        private static void StoreAndProviderAcceptance()
        {
            string root = TempRoot();
            try
            {
                var store = new AuthoritativeEquipmentCoordinatePackageStore(root);
                var provider = new AuthoritativeEquipmentCoordinateProvider(store);
                Assert(provider.InspectStatus(SyntheticDrawingSha, null, false).State
                    == AuthoritativeEquipmentCoordinateStatusState.NoActivePackage,
                    "status must clearly distinguish an empty coordinate store.");
                byte[] workbook = WorkbookBytes();
                AuthoritativeEquipmentCoordinateWorkbookImportResult read = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    workbook, SyntheticInventory(), SyntheticDrawingSha);
                Assert(read.IsValid && read.Validation != null && read.SourceWorkbookBytes != null, "the fixture workbook must validate.");

                AuthoritativeEquipmentCoordinateImportReceipt receipt = store.InstallWorkbook(
                    SyntheticDrawingSha, read.NormalizedManifestBytes!, read.NormalizedScheduleBytes!,
                    read.SourceWorkbookBytes!, Provenance(read), read.Validation!, null);

                Assert(string.Equals(receipt.SourceWorkbookSha256, read.SourceWorkbookSha256, StringComparison.Ordinal),
                    "the receipt must record the exact workbook file identity.");
                Assert(!string.Equals(receipt.PackageFingerprint, receipt.SourceWorkbookSha256, StringComparison.Ordinal),
                    "the receipt must keep the two identities distinct.");
                Assert(receipt.SourceDocumentNumber == "SYNTHETIC-DOC-0001" && receipt.SourceRevision == "A1"
                    && receipt.SourceIssueStatus == "Issued for Construction" && receipt.IssueDate == "2026-10-01",
                    "the receipt must record the source-document metadata.");

                string preservedWorkbook = Path.Combine(receipt.PackageDirectory, AuthoritativeEquipmentCoordinateWorkbookSchema.SourceWorkbookFileName);
                Assert(File.Exists(preservedWorkbook), "the exact imported workbook must be preserved as source-workbook.xlsx.");
                Assert(File.ReadAllBytes(preservedWorkbook).SequenceEqual(workbook), "the preserved workbook bytes must be identical to the imported bytes.");
                Assert(string.Equals(HumanEngineeringReviewDecisionFileStore.ComputeSha256(preservedWorkbook), read.SourceWorkbookSha256, StringComparison.Ordinal),
                    "the preserved workbook hash must match the recorded SourceWorkbookSha256.");
                Assert(store.IsActive(SyntheticDrawingSha, receipt.PackageFingerprint), "the installed package must become active.");

                Assert(provider.InspectStatus(SyntheticDrawingSha, read.RequestFingerprint, false).State
                    == AuthoritativeEquipmentCoordinateStatusState.ActiveValid,
                    "the active workbook must be valid for its drawing and occurrence request.");
                AuthoritativeEquipmentCoordinateStatusSnapshot wrongDrawing = provider.InspectStatus(
                    new string('A', 64), read.RequestFingerprint, false);
                Assert(wrongDrawing.State == AuthoritativeEquipmentCoordinateStatusState.ActiveMismatch
                    && wrongDrawing.ActivePackage == null,
                    "an active package for another drawing must be reported as mismatched.");
                Assert(provider.InspectStatus(SyntheticDrawingSha, new string('F', 64), false).State
                    == AuthoritativeEquipmentCoordinateStatusState.ActiveMismatch,
                    "an active package must reject a different live occurrence fingerprint.");
                Assert(provider.InspectStatus(SyntheticDrawingSha, read.RequestFingerprint, true).State
                    == AuthoritativeEquipmentCoordinateStatusState.ActiveMismatch,
                    "an unsaved modified drawing must not be reported as bound to the on-disk package.");
                Assert(provider.GetActive(SyntheticDrawingSha)?.SourceDocumentNumber == "SYNTHETIC-DOC-0001"
                    && provider.GetActive(SyntheticDrawingSha)?.SourceWorkbookSha256 == read.SourceWorkbookSha256,
                    "status provenance must come from the active package.");
                IReadOnlyList<AuthoritativeEquipmentCoordinate> resolved = provider.ResolveForDrawing(SyntheticDrawingSha);
                Assert(resolved.Count == 4, "the provider must resolve every coordinate of the active package.");
                Assert(resolved.All(x => string.Equals(x.SourceWorkbookSha256, read.SourceWorkbookSha256, StringComparison.Ordinal)),
                    "every resolved coordinate must carry the workbook file identity.");

                // Identity is the occurrence id, never the tag alone.
                AuthoritativeEquipmentCoordinate? first = provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA01", "P-90A");
                AuthoritativeEquipmentCoordinate? second = provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA02", "P-90A");
                Assert(first != null && second != null, "both duplicated-tag occurrences must resolve independently.");
                Assert(first!.Easting == 101.25m && second!.Easting == 102.75m,
                    "a duplicated tag must resolve to its own occurrence, never to the first match.");

                Assert(provider.Resolve(SyntheticDrawingSha, null, "P-90A") == null, "a tag-only lookup must never resolve.");
                Assert(provider.Resolve(SyntheticDrawingSha, "placement:equipment:P-90A:AAAA01", "P-90B") == null,
                    "a mismatched tag must never resolve an occurrence.");
                Assert(provider.Resolve(new string('0', 64), "placement:equipment:P-90A:AAAA01", "P-90A") == null,
                    "another drawing must never see this package.");

                AuthoritativeEquipmentCoordinateWorkbookImportResult invalid = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    WorkbookBytes(m => m.Rows[0].Northing = string.Empty), SyntheticInventory(), SyntheticDrawingSha);
                Assert(!invalid.IsValid && invalid.Validation == null,
                    "a missing Northing must be rejected before installation.");
                Assert(store.ReadActive(SyntheticDrawingSha)?.PackageFingerprint == receipt.PackageFingerprint
                    && store.ReadActive(SyntheticDrawingSha)?.SourceWorkbookSha256 == receipt.SourceWorkbookSha256,
                    "a rejected later workbook must preserve the previously active package and workbook identities.");

                byte[] resavedBytes = WorkbookBytes(m => m.GeneratedUtc = "2099-01-01T00:00:00.000Z");
                AuthoritativeEquipmentCoordinateWorkbookImportResult resaved = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    resavedBytes, SyntheticInventory(), SyntheticDrawingSha);
                Assert(resaved.IsValid && resaved.Validation!.PackageFingerprint == receipt.PackageFingerprint
                    && resaved.SourceWorkbookSha256 != receipt.SourceWorkbookSha256,
                    "the collision fixture must have the same semantic package and different workbook bytes.");
                bool refusedConflictingSource = false;
                try
                {
                    store.InstallWorkbook(SyntheticDrawingSha, resaved.NormalizedManifestBytes!, resaved.NormalizedScheduleBytes!,
                        resaved.SourceWorkbookBytes!, Provenance(resaved), resaved.Validation!, receipt.PackageFingerprint);
                }
                catch (InvalidOperationException) { refusedConflictingSource = true; }
                Assert(refusedConflictingSource && store.ReadActive(SyntheticDrawingSha)?.SourceWorkbookSha256 == receipt.SourceWorkbookSha256
                    && File.ReadAllBytes(preservedWorkbook).SequenceEqual(workbook),
                    "a semantic duplicate from different XLSX bytes must not replace active provenance or stored source bytes.");
            }
            finally { TryDelete(root); }
        }

        private static void NestedStorePathAcceptance()
        {
            string baseRoot = Path.Combine(Path.GetTempPath(), "es-path-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(baseRoot, new string('n', Math.Max(1, 120 - baseRoot.Length - 1)));
            Assert(root.Length >= 113, "the path regression root must be at least as nested as the recovery store root.");
            try
            {
                byte[] workbook = WorkbookBytes();
                AuthoritativeEquipmentCoordinateWorkbookImportResult read = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    workbook, SyntheticInventory(), SyntheticDrawingSha);
                Assert(read.IsValid && read.Validation != null, "the nested-path workbook fixture must validate.");
                var store = new AuthoritativeEquipmentCoordinatePackageStore(root);
                AuthoritativeEquipmentCoordinateImportReceipt receipt = store.InstallWorkbook(
                    SyntheticDrawingSha, read.NormalizedManifestBytes!, read.NormalizedScheduleBytes!,
                    read.SourceWorkbookBytes!, Provenance(read), read.Validation!, null);
                Assert(receipt.PackageDirectory == Path.Combine(root, "p", receipt.PackageFingerprint),
                    "the compact package directory must retain the full content fingerprint without repeating the drawing SHA.");
                Assert(store.ReadActive(SyntheticDrawingSha)?.PackageFingerprint == read.Validation.PackageFingerprint,
                    "the nested store must activate the exact validated package identity.");
                string sourcePath = Path.Combine(receipt.PackageDirectory, AuthoritativeEquipmentCoordinateWorkbookSchema.SourceWorkbookFileName);
                Assert(File.ReadAllBytes(sourcePath).SequenceEqual(workbook)
                    && receipt.SourceWorkbookSha256 == read.SourceWorkbookSha256,
                    "the nested store must retain the exact workbook bytes and independent source identity.");
                var reopened = new AuthoritativeEquipmentCoordinateProvider(new AuthoritativeEquipmentCoordinatePackageStore(root));
                Assert(reopened.ResolveForDrawing(SyntheticDrawingSha).Count == 4
                    && reopened.InspectStatus(SyntheticDrawingSha, read.RequestFingerprint, false).State
                        == AuthoritativeEquipmentCoordinateStatusState.ActiveValid,
                    "the provider must reopen all active occurrences from the nested store.");
                AuthoritativeEquipmentCoordinateWorkbookImportResult invalid = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    WorkbookBytes(m => m.Rows[0].Northing = string.Empty), SyntheticInventory(), SyntheticDrawingSha);
                Assert(!invalid.IsValid && store.ReadActive(SyntheticDrawingSha)?.PackageFingerprint == receipt.PackageFingerprint
                    && store.ReadActive(SyntheticDrawingSha)?.SourceWorkbookSha256 == receipt.SourceWorkbookSha256,
                    "a later invalid workbook must preserve the nested store's active package.");
            }
            finally { TryDelete(baseRoot); }

            string unusableBase = Path.Combine(Path.GetTempPath(), "es-too-long-" + Guid.NewGuid().ToString("N"));
            string unusable = Path.Combine(unusableBase, new string('x', Math.Max(1, 180 - unusableBase.Length - 1)));
            byte[] candidate = WorkbookBytes();
            AuthoritativeEquipmentCoordinateWorkbookImportResult valid = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                candidate, SyntheticInventory(), SyntheticDrawingSha);
            Assert(valid.IsValid && valid.Validation != null, "the unusable-path fixture must be valid before store activation.");
            bool clearFailure = false;
            try
            {
                new AuthoritativeEquipmentCoordinatePackageStore(unusable).InstallWorkbook(
                    SyntheticDrawingSha, valid.NormalizedManifestBytes!, valid.NormalizedScheduleBytes!,
                    valid.SourceWorkbookBytes!, Provenance(valid), valid.Validation!, null);
            }
            catch (InvalidOperationException exception)
            {
                clearFailure = exception.Message.Contains("configured authoritative coordinate store root is too long");
            }
            Assert(clearFailure && !Directory.Exists(unusableBase),
                "an unusable store path must fail clearly before writing any package or active pointer.");
        }

        // ---------------------------------------------------------------- placement provenance

        private static void PlacementProvenanceAcceptance()
        {
            string root = TempRoot();
            try
            {
                PlotPlanModel probe = BuildUnownedFixture();
                EquipmentPlacement target = probe.EquipmentPlacements.Single();
                string occurrenceId = target.PlacementId;
                string tag = target.NormalizedTag;

                var inventory = new List<AuthoritativeEquipmentCoordinateInventoryEntry>
                {
                    new AuthoritativeEquipmentCoordinateInventoryEntry
                    {
                        OccurrenceId = occurrenceId, EquipmentTag = tag, OccurrenceIndex = 1,
                        SourceEntityHandle = "SOLO:TAG", Layer = "EQ-TAG",
                        CurrentPlacementStatus = target.PlacementStatus, CurrentPlacementReason = "SYNTHETIC"
                    }
                };

                AuthoritativeEquipmentCoordinateWorkbookModel model = Model(inventory);
                model.Rows[0].Easting = "4321.75";
                model.Rows[0].Northing = "8765.25";
                byte[] workbook = AuthoritativeEquipmentCoordinateWorkbookWriter.WriteToBytes(model);

                AuthoritativeEquipmentCoordinateWorkbookImportResult read = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    workbook, inventory, SyntheticDrawingSha);
                Assert(read.IsValid, "the single-occurrence workbook must validate: " + string.Join(" | ", read.Messages));

                var store = new AuthoritativeEquipmentCoordinatePackageStore(root);
                store.InstallWorkbook(SyntheticDrawingSha, read.NormalizedManifestBytes!, read.NormalizedScheduleBytes!,
                    read.SourceWorkbookBytes!, Provenance(read), read.Validation!, null);

                AuthoritativeEquipmentCoordinate? coordinate = new AuthoritativeEquipmentCoordinateProvider(store)
                    .Resolve(SyntheticDrawingSha, occurrenceId, tag);
                Assert(coordinate != null, "the XLSX-derived coordinate must resolve.");

                EquipmentPlacement resolved = BuildUnownedFixture(coordinate).EquipmentPlacements.Single();
                Assert(resolved.PlacementStatus == "Complete", "an unresolved placement must be resolved by a valid authoritative coordinate.");
                Assert(resolved.Easting.SelectedValue == 4321.75m && resolved.Northing.SelectedValue == 8765.25m,
                    "the authoritative coordinate must be the value that gets placed.");

                AuthoritativeCoordinatePlacementEvidence? evidence = resolved.AuthoritativeCoordinate;
                Assert(evidence != null, "the placement must carry authoritative evidence.");
                Assert(evidence!.PlacementStatus == AuthoritativePlacementStatuses.Resolved, "the evidence must be RESOLVED.");
                Assert(evidence.OccurrenceId == occurrenceId && evidence.EquipmentTag == tag, "the evidence must carry the occurrence identity and tag.");
                Assert(string.Equals(evidence.SourceWorkbookSha256, read.SourceWorkbookSha256, StringComparison.Ordinal),
                    "the evidence must carry the workbook file identity.");
                Assert(AuthoritativeEquipmentCoordinatePackageValidator.IsSha256(evidence.PackageFingerprint),
                    "the evidence must carry the semantic package fingerprint.");
                Assert(evidence.SourceDocumentNumber == "SYNTHETIC-DOC-0001" && evidence.SourceRevision == "A1"
                    && evidence.SourceIssueStatus == "Issued for Construction" && evidence.IssuedBy == "SYNTHETIC AUTHORITY (TEST ONLY)"
                    && evidence.IssueDate == "2026-10-01",
                    "the evidence must carry the full source-document provenance.");
                Assert(evidence.SourceX == 4321.75m && evidence.SourceY == 8765.25m && evidence.SourceZ == null,
                    "the evidence must carry the source coordinate.");
                Assert(evidence.FinalWcsX == 4321.75m && evidence.FinalWcsY == 8765.25m && evidence.FinalWcsZ == null,
                    "the evidence must carry the placed WCS coordinate.");
                Assert(evidence.SourceUnits == AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters
                    && evidence.SourceCoordinateBasis == AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs
                    && evidence.TransformType == AuthoritativeCoordinateTransformTypes.Identity,
                    "the evidence must record the unit basis, coordinate basis and identity transform.");
            }
            finally { TryDelete(root); }
        }

        // ---------------------------------------------------------------- negative matrix

        private sealed class NegativeCase
        {
            public string Name = string.Empty;
            public string Expected = string.Empty;
            public Action<AuthoritativeEquipmentCoordinateWorkbookModel>? Mutate;
            public string? DrawingSha;
        }

        private static void NegativeMatrix()
        {
            var cases = new List<NegativeCase>
            {
                // WORKBOOK structure
                new NegativeCase { Name = "P141-N01 missing visible sheet", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingVisibleSheet, Mutate = m => m.Defects.OmitVisibleSheet = true },
                new NegativeCase { Name = "P141-N02 visible sheet hidden", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingVisibleSheet, Mutate = m => m.Defects.HideVisibleSheet = true },
                new NegativeCase { Name = "P141-N03 missing internal sheet", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalSheet, Mutate = m => m.Defects.OmitInternalSheet = true },
                new NegativeCase { Name = "P141-N04 missing equipment table", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingTable, Mutate = m => m.Defects.OmitTable = true },
                new NegativeCase { Name = "P141-N05 missing named metadata field", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingNamedField, Mutate = m => m.Defects.OmitNamedMetadata = true },
                new NegativeCase { Name = "P141-N06 unsupported workbook schema", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedWorkbookSchema, Mutate = m => m.SchemaVersion = "ENGISENTRY-EQUIPMENT-COORDINATES-XLSX-99" },
                new NegativeCase { Name = "P141-N07 missing internal metadata", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingInternalMetadata, Mutate = m => m.RequestFingerprint = string.Empty },

                // DRAWING binding
                new NegativeCase { Name = "P141-N08 drawing revision mismatch", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.DrawingMismatch, DrawingSha = new string('A', 64) },
                new NegativeCase { Name = "P141-N09 current drawing has no SHA", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.DrawingMismatch, DrawingSha = string.Empty },

                // REQUEST binding
                new NegativeCase { Name = "P141-N10 expected occurrence count mismatch", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RequestCountMismatch, Mutate = m => m.ExpectedOccurrenceCount = 5 },
                new NegativeCase { Name = "P141-N11 request fingerprint mismatch", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RequestFingerprintMismatch, Mutate = m => m.RequestFingerprint = new string('F', 64) },

                // ROWS identity binding
                new NegativeCase { Name = "P141-N12 unknown occurrence id", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch, Mutate = m => { m.Rows[0].OccurrenceId = "placement:equipment:ZZ-99:BBBB99"; ResyncFingerprint(m); } },
                new NegativeCase { Name = "P141-N13 duplicate occurrence id", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch, Mutate = m => { m.Rows[1].OccurrenceId = m.Rows[0].OccurrenceId; ResyncFingerprint(m); } },
                new NegativeCase { Name = "P141-N14 missing occurrence coverage", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch, Mutate = m => { m.Rows.RemoveAt(3); ResyncFingerprint(m); } },
                new NegativeCase { Name = "P141-N15 tag does not match occurrence", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch, Mutate = m => { m.Rows[0].EquipmentTag = "P-99Z"; ResyncFingerprint(m); } },
                new NegativeCase { Name = "P141-N16 empty occurrence identity", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.RowIdentityMismatch, Mutate = m => { m.Rows[0].OccurrenceId = string.Empty; ResyncFingerprint(m); } },

                // SOURCE METADATA
                new NegativeCase { Name = "P141-N17 blank project identifier", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.ProjectIdentifier = string.Empty },
                new NegativeCase { Name = "P141-N18 blank source document number", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.SourceDocumentNumber = string.Empty },
                new NegativeCase { Name = "P141-N19 blank source revision", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.SourceRevision = string.Empty },
                new NegativeCase { Name = "P141-N20 blank source issue status", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.SourceIssueStatus = string.Empty },
                new NegativeCase { Name = "P141-N21 blank issued by", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.IssuedBy = string.Empty },
                new NegativeCase { Name = "P141-N22 blank issue date", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.MissingSourceMetadata, Mutate = m => m.IssueDate = string.Empty },
                new NegativeCase { Name = "P141-N23 locale-ambiguous issue date", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidIssueDate, Mutate = m => m.IssueDate = "10/01/2026" },
                new NegativeCase { Name = "P141-N24 impossible issue date", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidIssueDate, Mutate = m => m.IssueDate = "2026-02-31" },
                new NegativeCase { Name = "P141-N24a padded issue date", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidIssueDate, Mutate = m => m.IssueDate = " 2026-10-01" },

                // COORDINATES
                new NegativeCase { Name = "P141-N25 blank Easting", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].Easting = string.Empty },
                new NegativeCase { Name = "P141-N26 blank Northing", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].Northing = string.Empty },
                new NegativeCase { Name = "P141-N27 Easting as text", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].EastingKind = AuthoritativeEquipmentCoordinateCellKind.Text },
                new NegativeCase { Name = "P141-N28 Easting as formula", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].EastingKind = AuthoritativeEquipmentCoordinateCellKind.Formula },
                new NegativeCase { Name = "P141-N29 Easting as formula error", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].EastingKind = AuthoritativeEquipmentCoordinateCellKind.Error },
                new NegativeCase { Name = "P141-N30 Northing as text", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[1].NorthingKind = AuthoritativeEquipmentCoordinateCellKind.Text },
                new NegativeCase { Name = "P141-N31 Elevation as text", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].ElevationKind = AuthoritativeEquipmentCoordinateCellKind.Text },
                new NegativeCase { Name = "P141-N32 Elevation as formula", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].ElevationKind = AuthoritativeEquipmentCoordinateCellKind.Formula },
                new NegativeCase { Name = "P141-N32a NaN Easting", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].Easting = "NaN" },
                new NegativeCase { Name = "P141-N32b Infinity Northing", Expected = AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidCoordinate, Mutate = m => m.Rows[0].Northing = "Infinity" }
            };

            foreach (NegativeCase testCase in cases)
            {
                AuthoritativeEquipmentCoordinateWorkbookImportResult result = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    WorkbookBytes(testCase.Mutate), SyntheticInventory(), testCase.DrawingSha ?? SyntheticDrawingSha);

                Assert(string.Equals(result.Status, testCase.Expected, StringComparison.Ordinal),
                    testCase.Name + ": expected " + testCase.Expected + " but got " + result.Status + " (" + string.Join(" | ", result.Messages) + ")");
                Assert(!result.IsValid, testCase.Name + ": a rejected workbook must never be valid.");
                Assert(result.Validation == null, testCase.Name + ": a rejected workbook must never expose a normalized package.");
            }

            // Path-level rejections, exercised through the real file path overload.
            string root = TempRoot();
            try
            {
                byte[] valid = WorkbookBytes();

                string xls = Path.Combine(root, "workbook.xls");
                Directory.CreateDirectory(root);
                File.WriteAllBytes(xls, valid);
                ExpectPathStatus("P141-N33 legacy .xls extension", AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension, xls);

                string csv = Path.Combine(root, "workbook.csv");
                File.WriteAllBytes(csv, valid);
                ExpectPathStatus("P141-N34 .csv extension", AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension, csv);

                string json = Path.Combine(root, "workbook.json");
                File.WriteAllBytes(json, valid);
                ExpectPathStatus("P141-N35 .json extension", AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension, json);

                string macro = Path.Combine(root, "workbook.xlsm");
                File.WriteAllBytes(macro, valid);
                ExpectPathStatus("P141-N36 macro-enabled .xlsm extension", AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension, macro);

                ExpectPathStatus("P141-N37 directory instead of a workbook", AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid, root);
                ExpectPathStatus("P141-N38 missing workbook", AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid, Path.Combine(root, "does-not-exist.xlsx"));

                string garbage = Path.Combine(root, "garbage.xlsx");
                File.WriteAllBytes(garbage, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });
                ExpectPathStatus("P141-N39 not a valid XLSX container", AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx, garbage);

                string empty = Path.Combine(root, "empty.xlsx");
                File.WriteAllBytes(empty, Array.Empty<byte>());
                ExpectPathStatus("P141-N40 empty workbook file", AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx, empty);
            }
            finally { TryDelete(root); }

            // Every named status must be reachable, and the valid status must be its own outcome.
            var observed = new HashSet<string>(StringComparer.Ordinal);
            foreach (NegativeCase testCase in cases)
                observed.Add(AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                    WorkbookBytes(testCase.Mutate), SyntheticInventory(), testCase.DrawingSha ?? SyntheticDrawingSha).Status);
            observed.Add(AuthoritativeEquipmentCoordinateWorkbookStatuses.UnsupportedExtension);
            observed.Add(AuthoritativeEquipmentCoordinateWorkbookStatuses.PathInvalid);
            observed.Add(AuthoritativeEquipmentCoordinateWorkbookStatuses.InvalidXlsx);
            observed.Add(AuthoritativeEquipmentCoordinateWorkbookStatuses.Valid);

            foreach (string status in AuthoritativeEquipmentCoordinateWorkbookStatuses.All)
                Assert(observed.Contains(status), "no negative case exercises the status: " + status);
        }

        private static void ExpectPathStatus(string name, string expected, string path)
        {
            AuthoritativeEquipmentCoordinateWorkbookImportResult result = AuthoritativeEquipmentCoordinateWorkbookReader.Read(
                path, SyntheticInventory(), SyntheticDrawingSha);
            Assert(string.Equals(result.Status, expected, StringComparison.Ordinal),
                name + ": expected " + expected + " but got " + result.Status + " (" + string.Join(" | ", result.Messages) + ")");
            Assert(!result.IsValid && result.Validation == null, name + ": a rejected workbook must never expose a normalized package.");
        }

        // ---------------------------------------------------------------- placement fixtures

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
    }
}
