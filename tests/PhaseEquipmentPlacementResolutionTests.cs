#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.Core;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    // Focused tests for the engineer-assisted equipment placement workflow. They never run a real
    // engineer confirmation and never touch the production review/resolution roots: every review is
    // an unmistakably synthetic TEST_ONLY case written to an isolated temporary root, and every
    // engineer identity is a clearly labelled test-only reviewer.
    internal static class PhaseEquipmentPlacementResolutionTests
    {
        private static int assertions;
        private const string DrawingName = "ENGINEER-PLACEMENT-TEST.dwg";
        private static readonly string DrawingSha = new string('A', 64);
        private static readonly string OtherSha = new string('B', 64);
        private const string Units = "Millimeters";

        internal static void Run()
        {
            Assert(EquipmentPlacementResolutionService.ResolveRoot() == EquipmentPlacementResolutionService.DefaultRoot, "The resolution-root override must stay disabled outside an explicit test scope.");
            Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The review-root override must stay disabled outside an explicit test scope.");

            string reviewRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-EPR-REVIEW-" + Guid.NewGuid().ToString("N"));
            string resolutionRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-EPR-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reviewRoot);
            Directory.CreateDirectory(resolutionRoot);
            try
            {
                var reviewService = new ProjectEngineerReviewService(reviewRoot);
                var resolutionService = new EquipmentPlacementResolutionService(resolutionRoot, reviewService);

                IdentityTests();
                ValidatorTests();
                PickRuleTests();
                QueueIdentityTests();
                AppendOnlyTests(reviewService, resolutionService);
                TrustGatingTests(reviewService, resolutionService);
                PlotPlanGatingTests(reviewService, resolutionService);
                ControlTests(reviewService, resolutionService, reviewRoot);
                AuthoritySeparationTests();

                // The default production roots must never be written by any of the above.
                Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The review-root override must remain disabled after the tests.");
                Assert(EquipmentPlacementResolutionService.ResolveRoot() == EquipmentPlacementResolutionService.DefaultRoot, "The resolution-root override must remain disabled after the tests.");

                Console.WriteLine("Phase Equipment Placement Resolution focused assertions passed: " + assertions);
            }
            finally
            {
                if (Directory.Exists(reviewRoot)) Directory.Delete(reviewRoot, true);
                if (Directory.Exists(resolutionRoot)) Directory.Delete(resolutionRoot, true);
            }
        }

        // ---- identity -------------------------------------------------------------------------

        private static void IdentityTests()
        {
            EquipmentPlacementResolutionRecord record = RawRecord(DrawingSha, "equipment:P-90A", 1000m, 2000m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0));
            Assert(record.ResolutionId == EquipmentPlacementResolutionIdentity.ComputeResolutionId(record), "ResolutionId must recompute from its own content.");
            Assert(record.RecordId == EquipmentPlacementResolutionIdentity.ComputeRecordId(record), "RecordId must recompute from its own content.");

            // ResolutionId changes when any of the five bound inputs changes, and only then.
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.DrawingSha256 = OtherSha)) != record.ResolutionId, "A changed drawing hash must change the ResolutionId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.EquipmentEntityId = "equipment:P-90B")) != record.ResolutionId, "A changed equipment identity must change the ResolutionId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.Easting = 1001m)) != record.ResolutionId, "A changed easting must change the ResolutionId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.Northing = 2001m)) != record.ResolutionId, "A changed northing must change the ResolutionId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.InsertionUnits = "Meters")) != record.ResolutionId, "Changed units must change the ResolutionId.");

            // A review-envelope field is NOT part of the ResolutionId.
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.ResponseId = "RESP-2")) == record.ResolutionId, "The ResolutionId must depend only on the drawing hash, identity, coordinates, and units.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(Mutate(record, x => x.ReviewerDisplayName = "Someone else")) == record.ResolutionId, "The ResolutionId must not depend on the reviewer.");

            // Decimal scale never changes the identity: the same stored value always hashes the same.
            EquipmentPlacementResolutionRecord scaleOne = RawRecord(DrawingSha, "equipment:P-90A", 1000.0m, 2000m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0));
            EquipmentPlacementResolutionRecord scaleTwo = RawRecord(DrawingSha, "equipment:P-90A", 1000.00m, 2000m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0));
            Assert(DecimalScale(scaleOne.Easting) != DecimalScale(scaleTwo.Easting), "The scale fixture must actually differ in decimal scale.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeResolutionId(scaleOne) == EquipmentPlacementResolutionIdentity.ComputeResolutionId(scaleTwo), "Decimal scale must not change the ResolutionId.");
            Assert(EquipmentPlacementResolutionIdentity.CanonicalDecimal(1000.50m) == "1000.5" && EquipmentPlacementResolutionIdentity.CanonicalDecimal(1000m) == "1000", "Canonical decimal rendering changed.");

            // RecordId changes for every confirmation-distinguishing field.
            Assert(EquipmentPlacementResolutionIdentity.ComputeRecordId(Mutate(record, x => x.ResponseId = "RESP-2")) != record.RecordId, "A changed response identity must change the RecordId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeRecordId(Mutate(record, x => x.RequestSha256 = new string('D', 64))) != record.RecordId, "A changed request hash must change the RecordId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeRecordId(Mutate(record, x => x.ReviewerDisplayName = "Other")) != record.RecordId, "A changed reviewer must change the RecordId.");
            Assert(EquipmentPlacementResolutionIdentity.ComputeRecordId(Mutate(record, x => x.ResponseTimestampUtc = Utc(2026, 9, 25, 10, 0, 1))) != record.RecordId, "A changed timestamp must change the RecordId.");
        }

        // ---- validator ------------------------------------------------------------------------

        private static void ValidatorTests()
        {
            EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0)));
            EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.FreeSpace, null, null, "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0)));

            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, null, "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0))), "An entity pick without a proven handle must be rejected.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", null, "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0))), "An entity pick without a proven layer must be rejected.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.FreeSpace, "H1", null, "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0))), "A free-space pick must not carry a source handle.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(Mutate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0)), x => x.ResolutionId = new string('E', 64))), "A tampered ResolutionId must be rejected.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(Mutate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0)), x => x.RecordId = new string('E', 64))), "A tampered RecordId must be rejected.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(Mutate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", Utc(2026, 9, 25, 10, 0, 0)), x => x.State = "UNKNOWN")), "An unsupported state must be rejected.");
            ExpectInvalid(() => EquipmentPlacementResolutionValidator.Validate(RawRecord(DrawingSha, "equipment:P-90A", 1m, 2m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "RESP-1", "Reviewer", DateTime.SpecifyKind(Utc(2026, 9, 25, 10, 0, 0), DateTimeKind.Local))), "A non-UTC timestamp must be rejected.");
        }

        // ---- pick rules: UCS -> WCS and Model-tab gating --------------------------------------

        // Editor prompt points are always expressed in the current UCS, so a pick must be converted
        // through the current UCS transform before it is stored, and it may only be taken from the
        // true Model tab. These rules are exercised directly because the Autodesk editor cannot run
        // in the offline test host.
        private static void PickRuleTests()
        {
            var promptPoint = new EquipmentPlacementPoint(10, 20, 1);

            // A UCS rotated 90 degrees about Z and translated by (100, 200, 5).
            EquipmentPlacementUcsTransform rotatedAndTranslated = EquipmentPlacementUcsTransform.FromComponents(
                0, -1, 0,
                1, 0, 0,
                0, 0, 1,
                100, 200, 5);
            EquipmentPlacementPoint world = EquipmentPlacementPickRules.ToWorld(promptPoint, rotatedAndTranslated);
            Assert(world.X == 80 && world.Y == 210 && world.Z == 6, "A rotated and translated UCS must convert the prompt point to the correct WCS point. Actual: " + world.X + "," + world.Y + "," + world.Z);

            // The same conversion is applied on both pick paths, and the raw prompt point is never stored.
            EquipmentPlacementPick freeWorld = EquipmentPlacementPickRules.CreateFreeSpacePick(promptPoint, rotatedAndTranslated, true);
            Assert(freeWorld != null && freeWorld.PointX == 80 && freeWorld.PointY == 210 && freeWorld.PointZ == 6 && freeWorld.Source == EquipmentPlacementResolutionSources.FreeSpace && freeWorld.Handle == null, "A free-space pick must store the UCS-converted WCS point.");
            EquipmentPlacementPick entityWorld = EquipmentPlacementPickRules.CreateEntityPick(promptPoint, rotatedAndTranslated, true, true, false, "H1", "EQ-TAG");
            Assert(entityWorld != null && entityWorld.PointX == 80 && entityWorld.PointY == 210 && entityWorld.PointZ == 6 && entityWorld.Source == EquipmentPlacementResolutionSources.EntityPick && entityWorld.Handle == "H1" && entityWorld.Layer == "EQ-TAG", "An entity pick must store the UCS-converted WCS point and keep the proven handle and layer.");
            Assert(entityWorld.PointX != promptPoint.X && entityWorld.PointY != promptPoint.Y, "A prompt point must never be stored as if it were already WCS.");

            // A rotation of 180 degrees with a negative translation, checked against exact values so
            // the rotate-then-translate order is pinned.
            EquipmentPlacementUcsTransform halfTurn = EquipmentPlacementUcsTransform.FromComponents(
                -1, 0, 0,
                0, -1, 0,
                0, 0, 1,
                50, -25, 2);
            EquipmentPlacementPoint halfTurnWorld = EquipmentPlacementPickRules.ToWorld(new EquipmentPlacementPoint(7, 9, 3), halfTurn);
            Assert(halfTurnWorld.X == 43 && halfTurnWorld.Y == -34 && halfTurnWorld.Z == 5, "A 180-degree UCS rotation with a negative translation must convert the prompt point to the correct WCS point. Actual: " + halfTurnWorld.X + "," + halfTurnWorld.Y + "," + halfTurnWorld.Z);

            // A point picked in a layout is a paper-space point, never a model coordinate.
            Assert(EquipmentPlacementPickRules.CreateFreeSpacePick(promptPoint, EquipmentPlacementUcsTransform.Identity, false) == null, "A free-space pick taken in a layout (paper space) must be rejected.");

            // A model-space entity reached through a layout viewport is still not a Model-tab pick.
            Assert(EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, false, true, false, "H1", "EQ-TAG") == null, "A model-space entity picked through a layout viewport must be rejected.");
            // A paper-space entity is rejected even on the Model tab.
            Assert(EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, true, false, false, "H1", "EQ-TAG") == null, "A paper-space entity must be rejected.");
            // A viewport entity is never a placement source.
            Assert(EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, true, true, true, "H1", "EQ-TAG") == null, "A viewport entity must be rejected.");

            // A normal Model-tab pick under the WCS is unchanged.
            EquipmentPlacementPick plainFree = EquipmentPlacementPickRules.CreateFreeSpacePick(promptPoint, EquipmentPlacementUcsTransform.Identity, true);
            Assert(plainFree != null && plainFree.PointX == 10 && plainFree.PointY == 20 && plainFree.PointZ == 1 && plainFree.Source == EquipmentPlacementResolutionSources.FreeSpace && plainFree.Handle == null && plainFree.Layer == null, "A Model-tab WCS free-space pick must be stored unchanged.");
            EquipmentPlacementPick plainEntity = EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, true, true, false, "H9", "EQ-TAG");
            Assert(plainEntity != null && plainEntity.PointX == 10 && plainEntity.PointY == 20 && plainEntity.PointZ == 1 && plainEntity.Source == EquipmentPlacementResolutionSources.EntityPick && plainEntity.Handle == "H9" && plainEntity.Layer == "EQ-TAG", "A Model-tab WCS entity pick must be stored unchanged.");

            // A pick without a proven handle or layer is never an entity pick.
            Assert(EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, true, true, false, null, "EQ-TAG") == null, "An entity pick without a proven handle must be rejected.");
            Assert(EquipmentPlacementPickRules.CreateEntityPick(promptPoint, EquipmentPlacementUcsTransform.Identity, true, true, false, "H1", null) == null, "An entity pick without a proven layer must be rejected.");
        }

        // ---- queue identity: one case per equipment identity ----------------------------------

        // The engineer queue is keyed by equipment identity, never by placement occurrence. These
        // rules are exercised directly because the pilot multiplicity (several placement
        // occurrences per equipment identity) cannot be produced by a single simple fixture.
        private static void QueueIdentityTests()
        {
            // The pilot shape: 25 placement occurrences across 19 equipment identities (six
            // identities carry two occurrences each), none Complete. The queue and the command-line
            // count must both report 19 unique unresolved entities, not 25 occurrences.
            var pilot = new List<EquipmentPlacement>();
            for (int i = 0; i < 19; i++)
            {
                string id = "equipment:PILOT-" + i.ToString("D2");
                pilot.Add(QueuePlacement(id, "PILOT-" + i.ToString("D2"), "placement:" + id + ":A"));
                if (i < 6) pilot.Add(QueuePlacement(id, "PILOT-" + i.ToString("D2"), "placement:" + id + ":B"));
            }
            Assert(pilot.Count == 25, "The pilot fixture must hold 25 placement occurrences.");
            Assert(pilot.Select(x => x.EquipmentEntityId).Distinct(StringComparer.Ordinal).Count() == 19, "The pilot fixture must span 19 equipment identities.");
            List<EquipmentPlacement> queue = EquipmentPlacementResolutionQueue.Build(pilot);
            Assert(queue.Count == 19, "The queue must hold exactly one case per unresolved equipment identity. Actual: " + queue.Count);
            Assert(queue.Select(x => x.EquipmentEntityId).Distinct(StringComparer.Ordinal).Count() == queue.Count, "Every queue case must be a distinct equipment identity.");
            Assert(EquipmentPlacementResolutionQueue.CountUnresolved(pilot) == 19, "The command-line unresolved count must report unique unresolved equipment identities, not placement occurrences. Actual: " + EquipmentPlacementResolutionQueue.CountUnresolved(pilot));

            // An identity with any Complete placement is never queued, even when it also has
            // unresolved occurrences: one resolved occurrence resolves the whole identity.
            var mixed = new List<EquipmentPlacement>
            {
                QueuePlacement("equipment:P-10A", "P-10A", "placement:equipment:P-10A:A"),
                QueuePlacement("equipment:P-10A", "P-10A", "placement:equipment:P-10A:B", "Complete"),
                QueuePlacement("equipment:P-11A", "P-11A", "placement:equipment:P-11A:A"),
                QueuePlacement("equipment:P-12A", "P-12A", "placement:equipment:P-12A:A", "Complete"),
                QueuePlacement("equipment:P-12A", "P-12A", "placement:equipment:P-12A:B")
            };
            List<EquipmentPlacement> mixedQueue = EquipmentPlacementResolutionQueue.Build(mixed);
            Assert(mixedQueue.Count == 1 && mixedQueue[0].EquipmentEntityId == "equipment:P-11A", "An identity with any Complete placement must not be queued; only the fully unresolved identity remains. Actual: " + string.Join(",", mixedQueue.Select(x => x.EquipmentEntityId)));

            // The representative is deterministic: the smallest PlacementId wins regardless of the
            // order the occurrences are presented in.
            var occurrences = new List<EquipmentPlacement>
            {
                QueuePlacement("equipment:P-20A", "P-20A", "placement:equipment:P-20A:Z"),
                QueuePlacement("equipment:P-20A", "P-20A", "placement:equipment:P-20A:M"),
                QueuePlacement("equipment:P-20A", "P-20A", "placement:equipment:P-20A:A")
            };
            Assert(EquipmentPlacementResolutionQueue.Build(occurrences).Single().PlacementId == "placement:equipment:P-20A:A", "The representative must be the deterministic smallest PlacementId.");
            Assert(EquipmentPlacementResolutionQueue.Build(Enumerable.Reverse(occurrences).ToList()).Single().PlacementId == "placement:equipment:P-20A:A", "The representative must not depend on the input order.");

            // Saving a resolution clears every queue entry for the identity, not just the one shown.
            var duplicates = new List<EquipmentPlacement>
            {
                QueuePlacement("equipment:P-30A", "P-30A", "placement:equipment:P-30A:A"),
                QueuePlacement("equipment:P-30A", "P-30A", "placement:equipment:P-30A:B"),
                QueuePlacement("equipment:P-31A", "P-31A", "placement:equipment:P-31A:A")
            };
            int removed = EquipmentPlacementResolutionQueue.RemoveForEntity(duplicates, "equipment:P-30A");
            Assert(removed == 2 && duplicates.Count == 1 && duplicates[0].EquipmentEntityId == "equipment:P-31A", "Saving a resolution must remove every queue entry for that equipment identity.");
        }

        // ---- append-only store ----------------------------------------------------------------

        private static void AppendOnlyTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            EquipmentPlacementResolutionRecord first = PersistConfirmation(reviewService, "TEST-ONLY-EPR-1", "equipment:P-90A", "P-90A", 1000m, 2000m, Units, EquipmentPlacementResolutionSources.EntityPick, "H1", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 10, 0, 0), DrawingSha);
            EquipmentPlacementResolutionRecord second = PersistConfirmation(reviewService, "TEST-ONLY-EPR-1", "equipment:P-90A", "P-90A", 1000m, 2000m, Units, EquipmentPlacementResolutionSources.FreeSpace, null, null, "TEST-ONLY Reviewer", Utc(2026, 9, 25, 10, 5, 0), DrawingSha);
            Assert(first.ResolutionId == second.ResolutionId && first.RecordId != second.RecordId, "Two confirmations of the same coordinates must share a ResolutionId but have distinct RecordIds.");

            string firstPath = resolutionService.Save(first);
            string firstBytes = Convert.ToBase64String(File.ReadAllBytes(firstPath));
            string secondPath = resolutionService.Save(second);
            Assert(firstPath != secondPath && File.Exists(firstPath) && File.Exists(secondPath), "Both confirmations must persist as separate files.");
            Assert(Convert.ToBase64String(File.ReadAllBytes(firstPath)) == firstBytes, "Persisting a new confirmation must not modify the earlier confirmation.");
            Assert(resolutionService.LoadAll().Count >= 2, "Every persisted confirmation must remain loadable as history.");

            // The full path must stay within the Windows MAX_PATH limit even though both identities
            // are 64-character hashes; otherwise the store cannot write under a normal profile root.
            Assert(firstPath.Length < 260, "The record path must stay within MAX_PATH. Actual length: " + firstPath.Length);
            string productionRoot = EquipmentPlacementResolutionService.DefaultRoot;
            string productionPath = EquipmentPlacementResolutionFileStore.RecordPath(Path.Combine(productionRoot, first.ResolutionId), first);
            Assert(productionPath.Length < 260, "The production record path must stay within MAX_PATH. Actual length: " + productionPath.Length);

            bool overwriteRejected = false;
            try { resolutionService.Save(first); } catch (IOException) { overwriteRejected = true; } catch (EquipmentPlacementResolutionValidationException) { overwriteRejected = true; }
            Assert(overwriteRejected, "Re-persisting an existing confirmation must never overwrite it.");
            Assert(Convert.ToBase64String(File.ReadAllBytes(firstPath)) == firstBytes, "A rejected overwrite must leave the original confirmation untouched.");

            // The record is persisted as JSON, which stores DateTimes at millisecond precision. A
            // confirmation whose timestamp carries sub-millisecond precision (as DateTime.UtcNow does)
            // must still re-verify after the round-trip, otherwise it would be silently discarded and
            // the placement would never become Complete.
            DateTime precise = new DateTime(2026, 9, 25, 17, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
            EquipmentPlacementResolutionRecord subMillisecond = PersistConfirmation(reviewService, "TEST-ONLY-EPR-8", "equipment:P-88A", "P-88A", 1234m, 5678m, Units, EquipmentPlacementResolutionSources.EntityPick, "H8", "EQ-TAG", "TEST-ONLY Reviewer", precise, DrawingSha);
            Assert(subMillisecond.ResponseTimestampUtc.Ticks % TimeSpan.TicksPerMillisecond == 0 && subMillisecond.ResponseTimestampUtc.Kind == DateTimeKind.Utc, "A persisted confirmation timestamp must be normalized to the persisted (millisecond) precision in UTC.");
            resolutionService.Save(subMillisecond);
            Assert(resolutionService.LoadAll().Any(x => x.RecordId == subMillisecond.RecordId), "A sub-millisecond confirmation must re-verify after the JSON round-trip.");
            EquipmentPlacementResolution subMillisecondTrusted = resolutionService.ResolveTrusted(DrawingSha, "equipment:P-88A", Units);
            Assert(subMillisecondTrusted != null && subMillisecondTrusted.RecordId == subMillisecond.RecordId && subMillisecondTrusted.Easting == 1234m, "A sub-millisecond confirmation must remain trusted after the round-trip.");
        }

        // ---- trust gating ---------------------------------------------------------------------

        private static void TrustGatingTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            EquipmentPlacementResolutionRecord entityPick = PersistConfirmation(reviewService, "TEST-ONLY-EPR-2", "equipment:P-91A", "P-91A", 1111m, 2222m, Units, EquipmentPlacementResolutionSources.EntityPick, "H2", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 11, 0, 0), DrawingSha);
            resolutionService.Save(entityPick);

            EquipmentPlacementResolution trusted = resolutionService.ResolveTrusted(DrawingSha, "equipment:P-91A", Units);
            Assert(trusted != null && trusted.ResolutionId == entityPick.ResolutionId && trusted.Easting == 1111m && trusted.Northing == 2222m && trusted.Source == EquipmentPlacementResolutionSources.EntityPick && trusted.SourceHandle == "H2", "A valid ACTIVE confirmation must resolve to a trusted projection.");

            // A changed drawing hash leaves the equipment unresolved.
            Assert(resolutionService.ResolveTrusted(OtherSha, "equipment:P-91A", Units) == null, "A changed drawing hash must leave the equipment unresolved.");
            // Mismatched units leave the equipment unresolved.
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-91A", "Meters") == null, "Mismatched units must leave the equipment unresolved.");
            // Unknown equipment leaves nothing resolved.
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-99Z", Units) == null, "An unknown equipment identity must not resolve.");
            // An invalid drawing hash is not trusted.
            Assert(resolutionService.ResolveTrusted("NOT-A-HASH", "equipment:P-91A", Units) == null, "A malformed drawing hash must not resolve.");

            // A non-ACTIVE confirmation is ignored.
            EquipmentPlacementResolutionRecord superseded = PersistConfirmation(reviewService, "TEST-ONLY-EPR-3", "equipment:P-92A", "P-92A", 3333m, 4444m, Units, EquipmentPlacementResolutionSources.EntityPick, "H3", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 12, 0, 0), DrawingSha);
            superseded.State = EquipmentPlacementResolutionStates.Superseded;
            superseded.RecordId = EquipmentPlacementResolutionIdentity.ComputeRecordId(superseded);
            resolutionService.Save(superseded);
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-92A", Units) == null, "A non-ACTIVE confirmation must never resolve equipment.");

            // A tampered review binding is not trusted.
            EquipmentPlacementResolutionRecord tampered = PersistConfirmation(reviewService, "TEST-ONLY-EPR-4", "equipment:P-93A", "P-93A", 5555m, 6666m, Units, EquipmentPlacementResolutionSources.FreeSpace, null, null, "TEST-ONLY Reviewer", Utc(2026, 9, 25, 13, 0, 0), DrawingSha);
            tampered.RequestSha256 = new string('F', 64);
            tampered.ResolutionId = EquipmentPlacementResolutionIdentity.ComputeResolutionId(tampered);
            tampered.RecordId = EquipmentPlacementResolutionIdentity.ComputeRecordId(tampered);
            resolutionService.Save(tampered);
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-93A", Units) == null, "A confirmation whose request evidence hash does not bind to the persisted review must not resolve equipment.");

            // Deleting the persisted review response invalidates an otherwise valid confirmation.
            EquipmentPlacementResolutionRecord orphaned = PersistConfirmation(reviewService, "TEST-ONLY-EPR-5", "equipment:P-94A", "P-94A", 7777m, 8888m, Units, EquipmentPlacementResolutionSources.FreeSpace, null, null, "TEST-ONLY Reviewer", Utc(2026, 9, 25, 14, 0, 0), DrawingSha);
            resolutionService.Save(orphaned);
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-94A", Units) != null, "A valid confirmation must resolve before its review is removed.");
            File.Delete(reviewService.ResponsePath(orphaned.ReviewRequestId));
            Assert(resolutionService.ResolveTrusted(DrawingSha, "equipment:P-94A", Units) == null, "A confirmation whose review response is gone must leave the equipment unresolved.");

            // The most recent confirmation wins when several are ACTIVE.
            EquipmentPlacementResolutionRecord older = PersistConfirmation(reviewService, "TEST-ONLY-EPR-6", "equipment:P-95A", "P-95A", 10m, 20m, Units, EquipmentPlacementResolutionSources.EntityPick, "H6", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 15, 0, 0), DrawingSha);
            EquipmentPlacementResolutionRecord newer = PersistConfirmation(reviewService, "TEST-ONLY-EPR-6", "equipment:P-95A", "P-95A", 30m, 40m, Units, EquipmentPlacementResolutionSources.EntityPick, "H6", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 15, 5, 0), DrawingSha);
            resolutionService.Save(older);
            resolutionService.Save(newer);
            EquipmentPlacementResolution latest = resolutionService.ResolveTrusted(DrawingSha, "equipment:P-95A", Units);
            Assert(latest != null && latest.RecordId == newer.RecordId && latest.Easting == 30m, "The most recent ACTIVE confirmation must win.");

            // The drawing-scoped projection returns exactly one resolution per equipment identity.
            IReadOnlyList<EquipmentPlacementResolution> forDrawing = resolutionService.ResolveTrustedForDrawing(DrawingSha, Units);
            Assert(forDrawing.Select(x => x.EquipmentEntityId).Distinct(StringComparer.Ordinal).Count() == forDrawing.Count, "The drawing-scoped projection must hold one resolution per equipment identity.");
            Assert(forDrawing.Any(x => x.EquipmentEntityId == "equipment:P-95A" && x.Easting == 30m), "The drawing-scoped projection must carry the winning confirmation.");
            Assert(!forDrawing.Any(x => x.EquipmentEntityId == "equipment:P-92A"), "A non-ACTIVE confirmation must not appear in the drawing-scoped projection.");
            Assert(!forDrawing.Any(x => x.EquipmentEntityId == "equipment:P-94A"), "A confirmation whose review is gone must not appear in the drawing-scoped projection.");
        }

        // ---- plot-plan gating -----------------------------------------------------------------

        private static void PlotPlanGatingTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            // A trusted engineer pick makes an otherwise-unresolved placement Complete. A dedicated
            // equipment identity keeps this test independent of the confirmations saved by the
            // sibling sub-tests.
            PlotPlanModel plan = BuildUnresolvedPlan("P-89A", Units);
            EquipmentPlacement placement = plan.EquipmentPlacements.Single();
            string tag = placement.NormalizedTag;
            Assert(placement.PlacementStatus != "Complete", "The fixture placement must start unresolved.");
            EquipmentPlacementResolutionRecord record = PersistConfirmation(reviewService, "TEST-ONLY-EPR-7", placement.EquipmentEntityId, placement.NormalizedTag, 1500m, 2500m, Units, EquipmentPlacementResolutionSources.EntityPick, "H7", "EQ-TAG", "TEST-ONLY Reviewer", Utc(2026, 9, 25, 16, 0, 0), DrawingSha);
            resolutionService.Save(record);
            EquipmentPlacementResolution resolution = resolutionService.ResolveTrusted(DrawingSha, placement.EquipmentEntityId, Units);
            Assert(resolution != null, "The fixture confirmation must resolve.");

            PlotPlanModel resolved = Rebuild(plan, Units, resolution);
            EquipmentPlacement resolvedPlacement = resolved.EquipmentPlacements.Single();
            Assert(resolvedPlacement.PlacementStatus == "Complete" && resolvedPlacement.Easting.Status == "Resolved" && resolvedPlacement.Northing.Status == "Resolved" && resolvedPlacement.Easting.SelectedValue == 1500m && resolvedPlacement.Northing.SelectedValue == 2500m, "A trusted engineer pick must make the placement Complete. Actual: status=" + resolvedPlacement.PlacementStatus + " E=" + resolvedPlacement.Easting.Status + "/" + resolvedPlacement.Easting.SelectedValue + " N=" + resolvedPlacement.Northing.Status + "/" + resolvedPlacement.Northing.SelectedValue);
            Assert(resolvedPlacement.EngineerResolution != null && resolvedPlacement.EngineerResolution.ResolutionId == record.ResolutionId && resolvedPlacement.EngineerResolution.RecordId == record.RecordId && resolvedPlacement.EngineerResolution.Source == EquipmentPlacementResolutionSources.EntityPick && resolvedPlacement.EngineerResolution.SourceHandle == "H7" && resolvedPlacement.EngineerResolution.ReviewerDisplayName == "TEST-ONLY Reviewer", "The engineer resolution provenance must be carried on the placement.");
            Assert(resolved.Statistics.CompletePlacementCount == 1, "The resolved placement must count as Complete.");

            // Mismatched units never complete a placement.
            PlotPlanModel wrongUnits = Rebuild(plan, Units, resolution, unitOverride: "Meters");
            Assert(wrongUnits.EquipmentPlacements.Single().PlacementStatus != "Complete" && wrongUnits.EquipmentPlacements.Single().EngineerResolution == null, "A unit-mismatched resolution must never complete a placement.");

            // No resolution at all leaves the placement unresolved.
            PlotPlanModel unresolvedPlan = Rebuild(plan, Units, null);
            Assert(unresolvedPlan.EquipmentPlacements.Single().PlacementStatus != "Complete" && unresolvedPlan.EquipmentPlacements.Single().EngineerResolution == null, "Without a resolution the placement must stay unresolved.");

            // Deterministic evidence is never overwritten by an engineer resolution.
            var conflicting = new EquipmentPlacementResolution { ResolutionId = new string('1', 64), RecordId = new string('2', 64), EquipmentEntityId = "equipment:P-96A", NormalizedTag = "P-96A", Easting = 999m, Northing = 888m, InsertionUnits = Units, Source = EquipmentPlacementResolutionSources.FreeSpace, ReviewerDisplayName = "TEST-ONLY Reviewer", ReviewerRole = "TEST-ONLY", ResponseTimestampUtc = Utc(2026, 9, 25, 16, 30, 0) };
            PlotPlanModel deterministic = BuildCenterlineCompletePlan("P-96A", Units, new[] { conflicting });
            EquipmentPlacement stillDeterministic = deterministic.EquipmentPlacements.Single();
            Assert(stillDeterministic.PlacementStatus == "Complete" && stillDeterministic.Easting.SelectedValue == 10m && stillDeterministic.Northing.SelectedValue == 5m && stillDeterministic.EngineerResolution == null, "Deterministic evidence must never be overwritten by an engineer resolution.");

            // Unresolved equipment must not block unrelated QA.
            EngineeringEntityIndex index = EntityIndexer.Build(SemanticModel(tag, Units));
            QaReport qa = QaEngine.Evaluate(index, BuiltInQaRules.All, unresolvedPlan, BuiltInPlotPlanQaRules.All);
            Assert(qa.RuleResults.Count == 15, "Unrelated QA rules must still run while equipment is unresolved.");
            Assert(qa.RuleResults.Single(x => x.RuleId == "PP-COORD-001").FindingCount == 1, "The unresolved placement must still be reported by the incomplete-placement rule.");
            Assert(qa.OverallStatus != null, "QA must still produce an overall status while equipment is unresolved.");
        }

        // ---- control contract -----------------------------------------------------------------

        private static void ControlTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService, string reviewRoot)
        {
            PlotPlanModel plan = BuildUnresolvedPlan("P-97A", Units);
            EquipmentPlacement placement = plan.EquipmentPlacements.Single();
            int reviewsBefore = ResponseFiles(reviewRoot);
            int recordsBefore = RecordFiles(resolutionService);

            // A cancelled pick writes nothing.
            var cancelled = new EquipmentPlacementResolutionControl(plan, DrawingName, DrawingSha, Units, reviewService, resolutionService, new StubPickProvider(null));
            Assert(cancelled.UnresolvedCount == 1, "The surface must present the unresolved placement.");
            Assert(!Button(cancelled, "Save Resolution").Enabled, "Save must be disabled before a pick is captured.");
            Button(cancelled, "Pick Entity").PerformClick();
            Assert(cancelled.CurrentPick == null, "A cancelled pick must not capture anything.");
            Assert(!Button(cancelled, "Save Resolution").Enabled, "Save must stay disabled after a cancelled pick.");
            Assert(ResponseFiles(reviewRoot) == reviewsBefore && RecordFiles(resolutionService) == recordsBefore, "A cancelled pick must not write a review or a resolution.");
            cancelled.Dispose();

            // A captured pick still writes nothing until the engineer explicitly saves a complete review.
            var pick = new EquipmentPlacementPick { Source = EquipmentPlacementResolutionSources.EntityPick, PointX = 4321, PointY = 8765, PointZ = 0, Handle = "H97", Layer = "EQ-TAG" };
            var control = new EquipmentPlacementResolutionControl(plan, DrawingName, DrawingSha, Units, reviewService, resolutionService, new StubPickProvider(pick));
            Button(control, "Pick Entity").PerformClick();
            Assert(control.CurrentPick != null && control.CurrentPick.Handle == "H97", "An entity pick must capture the proven handle and WCS point.");
            Assert(!Button(control, "Save Resolution").Enabled, "Save must stay disabled until the review is complete.");
            Button(control, "Save Resolution").PerformClick();
            Assert(ResponseFiles(reviewRoot) == reviewsBefore && RecordFiles(resolutionService) == recordsBefore, "An incomplete review must not write anything.");

            Box(control, "ReviewerDisplayName").Text = "TEST-ONLY Reviewer";
            Box(control, "ReviewerRole").Text = "TEST-ONLY";
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(control, question.QuestionId);
                if (answer is ComboBox combo) combo.SelectedIndex = 1; else answer.Text = "test-only";
            }
            ComboBox classification = Combo(control, "Classification");
            classification.SelectedIndex = 1;
            Assert(Button(control, "Save Resolution").Enabled, "Save must be enabled once a pick and a complete review are present.");
            Button(control, "Save Resolution").PerformClick();
            Assert(ResponseFiles(reviewRoot) == reviewsBefore + 1 && RecordFiles(resolutionService) == recordsBefore + 1, "An explicit complete save must write exactly one review and one resolution.");
            Assert(control.UnresolvedCount == 0, "A saved resolution must leave the placement list.");

            EquipmentPlacementResolutionRecord stored = resolutionService.LoadAll().Single(x => x.EquipmentEntityId == placement.EquipmentEntityId);
            Assert(stored.Source == EquipmentPlacementResolutionSources.EntityPick && stored.SourceHandle == "H97" && stored.SourceLayer == "EQ-TAG" && stored.Easting == 4321m && stored.Northing == 8765m && stored.NormalizedTag == placement.NormalizedTag, "The saved resolution must record the exact pick, handle, layer, and coordinates.");
            Assert(stored.InsertionUnits == Units && stored.DrawingSha256 == DrawingSha && stored.DrawingName == DrawingName, "The saved resolution must bind the drawing hash, name, and units.");
            Assert(stored.State == EquipmentPlacementResolutionStates.Active, "A newly saved resolution must be ACTIVE.");
            Assert(resolutionService.ResolveTrusted(DrawingSha, placement.EquipmentEntityId, Units) != null, "The saved resolution must be trusted.");
            control.Dispose();

            // A free-space pick carries no handle and is still bound to the same identity rules.
            var freePlan = BuildUnresolvedPlan("P-98A", Units);
            var freePick = new EquipmentPlacementPick { Source = EquipmentPlacementResolutionSources.FreeSpace, PointX = 10, PointY = 20, PointZ = 0, Handle = null, Layer = null };
            var freeControl = new EquipmentPlacementResolutionControl(freePlan, DrawingName, DrawingSha, Units, reviewService, resolutionService, new StubPickProvider(freePick));
            Button(freeControl, "Pick Free Space").PerformClick();
            Assert(freeControl.CurrentPick != null && freeControl.CurrentPick.Handle == null, "A free-space pick must carry no source handle.");
            freeControl.Dispose();

            // Two unresolved occurrences of one equipment identity present as a single case, and a
            // saved resolution clears the whole identity from the queue.
            var duplicatePlan = new PlotPlanModel();
            duplicatePlan.EquipmentPlacements.Add(QueuePlacement("equipment:P-97B", "P-97B", "placement:equipment:P-97B:A"));
            duplicatePlan.EquipmentPlacements.Add(QueuePlacement("equipment:P-97B", "P-97B", "placement:equipment:P-97B:B"));
            duplicatePlan.EquipmentPlacements.Add(QueuePlacement("equipment:P-97C", "P-97C", "placement:equipment:P-97C:A"));
            var duplicatePick = new EquipmentPlacementPick { Source = EquipmentPlacementResolutionSources.EntityPick, PointX = 5000, PointY = 6000, PointZ = 0, Handle = "H97B", Layer = "EQ-TAG" };
            var duplicateControl = new EquipmentPlacementResolutionControl(duplicatePlan, DrawingName, DrawingSha, Units, reviewService, resolutionService, new StubPickProvider(duplicatePick));
            Assert(duplicateControl.UnresolvedCount == 2, "Two identities (one carrying two occurrences) must present as two cases. Actual: " + duplicateControl.UnresolvedCount);
            Button(duplicateControl, "Pick Entity").PerformClick();
            Box(duplicateControl, "ReviewerDisplayName").Text = "TEST-ONLY Reviewer";
            Box(duplicateControl, "ReviewerRole").Text = "TEST-ONLY";
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(duplicateControl, question.QuestionId);
                if (answer is ComboBox combo) combo.SelectedIndex = 1; else answer.Text = "test-only";
            }
            Combo(duplicateControl, "Classification").SelectedIndex = 1;
            Button(duplicateControl, "Save Resolution").PerformClick();
            Assert(duplicateControl.UnresolvedCount == 1, "Saving a resolution must remove the whole equipment identity from the queue. Actual: " + duplicateControl.UnresolvedCount);
            duplicateControl.Dispose();
        }

        // ---- authority separation -------------------------------------------------------------

        private static void AuthoritySeparationTests()
        {
            Assert(!typeof(EquipmentPlacementResolutionRecord).GetProperties().Any(x => x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0), "A resolution record must not carry approval data.");
            Assert(!typeof(EquipmentPlacementResolutionService).GetMethods().Any(x => x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0), "The resolution service must not expose approval or activation.");
            Assert(!typeof(EquipmentPlacementResolutionService).Assembly.GetTypes().Any(x => x.Namespace == "EngiSentry.Standards" && x.Name.StartsWith("EquipmentPlacementResolution", StringComparison.Ordinal) && x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0), "The workflow must not add its own approval types.");
            Assert(!typeof(EquipmentPlacementResolutionRecord).Assembly.GetReferencedAssemblies().Any(x => x.Name.StartsWith("Ac", StringComparison.Ordinal)), "The portable contract must not reference Autodesk assemblies.");
            Assert(HumanEngineeringReviewDecisionStates.ReviewAccepted == "REVIEW_ACCEPTED" && HumanEngineeringReviewDecisionStates.ReviewAccepted != "APPROVED", "REVIEW_ACCEPTED != APPROVED invariant changed.");

            MethodInfo command = typeof(Commands).GetMethod("PlacementResolve", BindingFlags.Public | BindingFlags.Instance);
            Assert(command != null, "The ES_PLACEMENTRESOLVE command handler is missing.");
            try
            {
                foreach (object attribute in command.GetCustomAttributes(false))
                {
                    PropertyInfo nameProperty = attribute.GetType().GetProperty("Name");
                    if (nameProperty == null) continue;
                    string commandName = nameProperty.GetValue(attribute) as string;
                    if (!string.IsNullOrEmpty(commandName)) Assert(commandName == "ES_PLACEMENTRESOLVE", "Command name changed: " + commandName);
                }
            }
            catch (FileNotFoundException)
            {
                // The Autodesk runtime attribute type is not resolvable in the offline test host.
            }
        }

        // ---- helpers --------------------------------------------------------------------------

        private static PlotPlanModel BuildUnresolvedPlan(string tag, string units)
        {
            EngineeringDrawingModel model = SemanticModel(tag, units);
            return PlotPlanBuilder.Build(EntityIndexer.Build(model), model, Snapshot(tag));
        }

        private static PlotPlanModel BuildCenterlineCompletePlan(string tag, string units, IReadOnlyList<EquipmentPlacementResolution> resolutions = null)
        {
            var snapshot = new DrawingSnapshot();
            snapshot.TextEntities.Add(new TextSnapshot { Text = tag, Handle = "CEN:TAG", Layer = "EQ-TAG", SpaceKind = "Model", Height = 1, PositionX = 10, PositionY = 5, ExtentMinX = 9, ExtentMinY = 4, ExtentMaxX = 11, ExtentMaxY = 6 });
            var model = new EngineeringDrawingModel { Drawing = new SemanticDrawing { InsertionUnits = units } };
            model.EquipmentTags.Add(new EquipmentTag { NormalizedTag = tag, SourceHandle = "CEN:TAG", SourceLayer = "EQ-TAG" });
            var capture = new PlotGeometryCapture();
            capture.Primitives.Add(Line("VERT", "EQ-CEN", 10, 0, 10, 10));
            capture.Primitives.Add(Line("HORIZ", "EQ-CEN", 10, 5, 20, 5));
            return PlotPlanBuilder.Build(EntityIndexer.Build(model), model, snapshot, capture, resolutions);
        }

        private static PlotPlanModel Rebuild(PlotPlanModel source, string units, EquipmentPlacementResolution resolution, string unitOverride = null)
        {
            string tag = source.EquipmentPlacements[0].NormalizedTag;
            EngineeringDrawingModel model = SemanticModel(tag, unitOverride ?? units);
            IReadOnlyList<EquipmentPlacementResolution> resolutions = resolution == null ? null : new[] { resolution };
            return PlotPlanBuilder.Build(EntityIndexer.Build(model), model, Snapshot(tag), null, resolutions);
        }

        private static EngineeringDrawingModel SemanticModel(string tag, string units)
        {
            var model = new EngineeringDrawingModel { Drawing = new SemanticDrawing { InsertionUnits = units } };
            model.EquipmentTags.Add(new EquipmentTag { NormalizedTag = tag, SourceHandle = "EPR-" + tag, SourceLayer = "EQ-TAG" });
            return model;
        }

        private static DrawingSnapshot Snapshot(string tag)
        {
            var snapshot = new DrawingSnapshot();
            snapshot.TextEntities.Add(new TextSnapshot { Text = tag, Handle = "EPR-" + tag, Layer = "EQ-TAG", SpaceKind = "Model", Height = 1, PositionX = 100, PositionY = 100 });
            return snapshot;
        }

        private static EquipmentPlacement QueuePlacement(string entityId, string tag, string placementId, string status = "Incomplete") =>
            new EquipmentPlacement { PlacementId = placementId, EquipmentEntityId = entityId, NormalizedTag = tag, PlacementStatus = status };

        private static PlotGeometryPrimitive Line(string handle, string layer, double x1, double y1, double x2, double y2) =>
            new PlotGeometryPrimitive { SourceHandle = handle, SourceLayer = layer, GeometryKind = "Line", Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = x1, Y = y1 }, new PlotGeometryVertex { X = x2, Y = y2 } } };

        private static EquipmentPlacementResolutionRecord PersistConfirmation(
            ProjectEngineerReviewService reviewService, string caseId, string entityId, string tag,
            decimal easting, decimal northing, string units, string source, string handle, string layer,
            string reviewer, DateTime timestamp, string sha)
        {
            var evidence = new List<ProjectEngineerReviewEvidenceReference>
            {
                new ProjectEngineerReviewEvidenceReference { Kind = "DrawingSha256", Label = "Pre/post SHA-256", Value = sha },
                new ProjectEngineerReviewEvidenceReference { Kind = "ConfirmationTimestamp", Label = "Confirmation timestamp (UTC)", Value = timestamp.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
                new ProjectEngineerReviewEvidenceReference { Kind = "EquipmentEntityId", Label = "Equipment identity", Value = entityId }
            };
            ProjectEngineerReviewRequest request = reviewService.CreateRequest(ProjectEngineerReviewService.WorkflowSourceTestOnly, DrawingName, sha, caseId, "Synthetic test-only placement resolution.", evidence, "UNRESOLVED_EQUIPMENT_PLACEMENT", "ENGINEER_CONFIRMATION", EquipmentPlacementResolutionQuestions.All);
            var response = new ProjectEngineerReviewResponse
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256,
                DrawingName = request.DrawingName,
                ReviewedCaseId = request.ReviewedCaseId,
                ReviewerDisplayName = reviewer,
                ReviewerRole = "TEST-ONLY",
                ResponseTimestampUtc = timestamp,
                Answers = EquipmentPlacementResolutionQuestions.All.Select(q => new ProjectEngineerReviewAnswer { QuestionId = q.QuestionId, AnswerText = ProjectEngineerAnswerKinds.IsBounded(q.AnswerKind) ? ProjectEngineerAnswerKinds.AllowedAnswers(q.AnswerKind)[0] : "test-only" }).ToList(),
                EngineerComment = "test-only synthetic confirmation",
                Classification = ProjectEngineerReviewClassifications.Pass
            };
            reviewService.SaveResponse(request, response);
            return EquipmentPlacementResolutionBuilder.Build(DrawingName, sha, entityId, tag, easting, northing, units, source, handle, layer, (double)easting, (double)northing, 0, request, response, timestamp);
        }

        private static EquipmentPlacementResolutionRecord RawRecord(string sha, string entityId, decimal easting, decimal northing, string units, string source, string handle, string layer, string responseId, string reviewer, DateTime timestamp)
        {
            var record = new EquipmentPlacementResolutionRecord
            {
                SchemaVersion = EquipmentPlacementResolutionSchema.Current,
                DrawingName = DrawingName,
                DrawingSha256 = sha,
                EquipmentEntityId = entityId,
                NormalizedTag = entityId.Replace("equipment:", string.Empty),
                Easting = easting,
                Northing = northing,
                InsertionUnits = units,
                Source = source,
                SourceHandle = string.IsNullOrWhiteSpace(handle) ? null : handle,
                SourceLayer = string.IsNullOrWhiteSpace(layer) ? null : layer,
                PickedPointX = (double)easting,
                PickedPointY = (double)northing,
                PickedPointZ = 0,
                ReviewedCaseId = "TEST-ONLY-CASE",
                ReviewRequestId = "PER-TESTONLY",
                RequestSha256 = new string('C', 64),
                ResponseId = responseId,
                ReviewerDisplayName = reviewer,
                ReviewerRole = "TEST-ONLY",
                ResponseTimestampUtc = timestamp,
                RecordedTimestampUtc = timestamp,
                State = EquipmentPlacementResolutionStates.Active
            };
            record.ResolutionId = EquipmentPlacementResolutionIdentity.ComputeResolutionId(record);
            record.RecordId = EquipmentPlacementResolutionIdentity.ComputeRecordId(record);
            return record;
        }

        // A copy is mutated and returned so each assertion starts from the same unmodified record.
        private static EquipmentPlacementResolutionRecord Mutate(EquipmentPlacementResolutionRecord record, Action<EquipmentPlacementResolutionRecord> change)
        {
            var copy = new EquipmentPlacementResolutionRecord
            {
                SchemaVersion = record.SchemaVersion,
                RecordId = record.RecordId,
                ResolutionId = record.ResolutionId,
                DrawingName = record.DrawingName,
                DrawingSha256 = record.DrawingSha256,
                EquipmentEntityId = record.EquipmentEntityId,
                NormalizedTag = record.NormalizedTag,
                Easting = record.Easting,
                Northing = record.Northing,
                InsertionUnits = record.InsertionUnits,
                Source = record.Source,
                SourceHandle = record.SourceHandle,
                SourceLayer = record.SourceLayer,
                PickedPointX = record.PickedPointX,
                PickedPointY = record.PickedPointY,
                PickedPointZ = record.PickedPointZ,
                ReviewedCaseId = record.ReviewedCaseId,
                ReviewRequestId = record.ReviewRequestId,
                RequestSha256 = record.RequestSha256,
                ResponseId = record.ResponseId,
                ReviewerDisplayName = record.ReviewerDisplayName,
                ReviewerRole = record.ReviewerRole,
                ResponseTimestampUtc = record.ResponseTimestampUtc,
                RecordedTimestampUtc = record.RecordedTimestampUtc,
                State = record.State
            };
            change(copy);
            return copy;
        }

        private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) => new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

        private static int DecimalScale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

        private static int ResponseFiles(string reviewRoot) => Directory.GetFiles(reviewRoot, ProjectEngineerReviewService.ResponseFileName, SearchOption.AllDirectories).Length;

        private static int RecordFiles(EquipmentPlacementResolutionService service) => EquipmentPlacementResolutionFileStore.EnumerateRecordPaths(service.Root).Count;

        private static IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls)
            {
                yield return child;
                foreach (Control grandchild in Descendants(child)) yield return grandchild;
            }
        }

        private static Button Button(Control root, string text) => Descendants(root).OfType<Button>().First(b => b.Text == text);
        private static TextBox Box(Control root, string accessibleName) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == accessibleName);
        private static ComboBox Combo(Control root, string accessibleName) => Descendants(root).OfType<ComboBox>().First(c => c.AccessibleName == accessibleName);
        private static Control AnswerControl(Control root, string questionId) => Descendants(root).First(c => (c is ComboBox || c is TextBox) && (c.Tag as string) == questionId);

        private static void ExpectInvalid(Action action, string message)
        {
            bool failed = false;
            try { action(); }
            catch (EquipmentPlacementResolutionValidationException) { failed = true; }
            Assert(failed, message);
        }

        private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }

        private sealed class StubPickProvider : IEquipmentPlacementPickProvider
        {
            private readonly EquipmentPlacementPick pick;
            internal StubPickProvider(EquipmentPlacementPick pick) { this.pick = pick; }
            public EquipmentPlacementPick PickEntity() => pick;
            public EquipmentPlacementPick PickFreeSpace() => pick;
        }
    }
}
