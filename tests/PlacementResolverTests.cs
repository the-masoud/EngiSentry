using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Core;
using EngiSentry.PlotPlan;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PlacementResolverTests
    {
        private static void Check(bool value, string caseName)
        {
            if (!value) throw new InvalidOperationException("Placement resolver: " + caseName);
        }

        internal static void Run()
        {
            var fixture = New();
            Tag(fixture, "T", "P-01A", 0, 0);
            X(fixture, "X", 100, 0, 2);
            Y(fixture, "Y", 200, 0, 3);
            PlotPlanModel inferred = Build(fixture);
            EquipmentPlacement item = inferred.EquipmentPlacements.Single();
            Check(item.ResolutionState == PlacementResolutionState.ReviewRequired && item.DrawingCandidate?.CandidateX == 100m
                && item.DrawingCandidate.CandidateY == 200m && item.DrawingCandidate.TagEntityHandle == "T"
                && item.DrawingCandidate.XEntityHandle == "X" && item.DrawingCandidate.YEntityHandle == "Y", "structured cluster and evidence");
            Check(item.PlacementStatus != "Complete" && !item.Easting.SelectedValue.HasValue && !item.Northing.SelectedValue.HasValue,
                "inference did not become trusted");
            Check(item.DrawingCandidate.CoordinatesInDrawingUnits && inferred.Measurements.EquipmentSpatialMeasurements.Count == 0,
                "millimeter basis and no normal measurement");
            fixture.model.RoadLabels.Add(new TextEvidence { SourceHandle = "ROAD-LABEL", SourceLayer = "ROAD-TEXT", RawText = "ROAD EDGE", PositionX = 5, PositionY = 2 });
            var road = new PlotGeometryCapture();
            road.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "ROAD-LABEL", SourceLayer = "ROAD-TEXT", PositionX = 5, PositionY = 2, Height = 1 });
            road.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "ROAD-LINE", SourceLayer = "ROAD", GeometryKind = "Line",
                Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = 0, Y = 0 }, new PlotGeometryVertex { X = 10, Y = 0 } } });
            PlotPlanModel withRoad = PlotPlanBuilder.Build(EntityIndexer.Build(fixture.model), fixture.model, fixture.snapshot, road);
            Check(withRoad.Measurements.EquipmentSpatialMeasurements.Count == 0
                && withRoad.Measurements.ProvisionalSpatialMeasurements.Any(x => x.SubjectKind == "Road"),
                "candidate road distance is provisional only");

            var reverse = New();
            reverse.snapshot.TextEntities.AddRange(fixture.snapshot.TextEntities.AsEnumerable().Reverse());
            reverse.model.EquipmentTags.AddRange(fixture.model.EquipmentTags);
            reverse.model.Coordinates.AddRange(fixture.model.Coordinates.AsEnumerable().Reverse());
            Check(Build(reverse).EquipmentPlacements.Single().DrawingCandidate?.XEntityHandle == "X", "enumeration determinism");

            var sharedX = SharedFixture(true);
            PlotPlanModel sharedXResult = Build(sharedX);
            Check(sharedXResult.EquipmentPlacements.Count == 2 && sharedXResult.EquipmentPlacements.All(x =>
                x.ResolutionState == PlacementResolutionState.ReviewRequired && x.DrawingCandidate?.XEntityHandle == "SHARED-X"
                && x.DrawingCandidate.EvidenceType == "SHARED_X_ORTHOGONAL_GRAPH" && x.DrawingDiagnostic.CompetingCandidateCount == 1),
                "shared X yields two review candidates with diagnostics");
            Check(sharedXResult.Measurements.EquipmentSpatialMeasurements.Count == 0 &&
                sharedXResult.EquipmentPlacements.All(x => x.PlacementStatus != "Complete"), "shared axis remains nonauthoritative");

            var sharedY = SharedFixture(false);
            PlotPlanModel sharedYResult = Build(sharedY);
            Check(sharedYResult.EquipmentPlacements.All(x => x.ResolutionState == PlacementResolutionState.ReviewRequired &&
                x.DrawingCandidate?.YEntityHandle == "SHARED-Y" && x.DrawingCandidate.EvidenceType == "SHARED_Y_ORTHOGONAL_GRAPH"),
                "shared Y yields two review candidates");
            Y(sharedY, "ALT-Y", 201, 0, -14);
            PlotPlanModel ambiguousShared = Build(sharedY);
            Check(ambiguousShared.EquipmentPlacements.All(x => x.ResolutionState == PlacementResolutionState.Unresolved &&
                x.DrawingDiagnostic.CompetingCandidateCount == 2 && x.DrawingDiagnostic.SharedAxisCandidateHandles.Count == 2),
                "competing shared axes fail closed");

            var permuted = SharedFixture(true);
            permuted.snapshot.TextEntities.Reverse(); permuted.model.Coordinates.Reverse(); permuted.model.EquipmentTags.Reverse();
            Check(Build(permuted).EquipmentPlacements.OrderBy(x => x.NormalizedTag).Select(x =>
                x.DrawingCandidate?.XEntityHandle + ":" + x.DrawingCandidate?.YEntityHandle).SequenceEqual(
                sharedXResult.EquipmentPlacements.OrderBy(x => x.NormalizedTag).Select(x =>
                x.DrawingCandidate?.XEntityHandle + ":" + x.DrawingCandidate?.YEntityHandle)), "shared graph order independence");
            X(sharedX, "FAR-X", 300, 60, -12);
            Check(Build(sharedX).EquipmentPlacements.All(x => x.DrawingCandidate?.XEntityHandle == "SHARED-X"),
                "unrelated axis excluded from group");

            var sharedRoad = SharedFixture(true);
            sharedRoad.model.RoadLabels.Add(new TextEvidence { SourceHandle = "ROAD-TEXT", SourceLayer = "ROAD-TEXT", RawText = "ROAD EDGE", PositionX = 5, PositionY = 1 });
            sharedRoad.model.BoundaryLabels.Add(new BoundaryEvidence { SourceHandle = "FENCE-TEXT", SourceLayer = "FENCE-TEXT", RawText = "SECURITY FENCE", BoundaryType = "Fence", PositionX = 5, PositionY = 2 });
            var geometry = new PlotGeometryCapture();
            geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "ROAD-TEXT", SourceLayer = "ROAD-TEXT", PositionX = 5, PositionY = 1, Height = 1 });
            geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "FENCE-TEXT", SourceLayer = "FENCE-TEXT", PositionX = 5, PositionY = 2, Height = 1 });
            geometry.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "ROAD", SourceLayer = "ROAD", GeometryKind = "Line",
                Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = 0, Y = 0 }, new PlotGeometryVertex { X = 10, Y = 0 } } });
            geometry.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "FENCE", SourceLayer = "FENCE", GeometryKind = "Line",
                Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = 0, Y = 2 }, new PlotGeometryVertex { X = 10, Y = 2 } } });
            PlotPlanModel sharedMeasured = PlotPlanBuilder.Build(EntityIndexer.Build(sharedRoad.model), sharedRoad.model, sharedRoad.snapshot, geometry);
            Check(sharedMeasured.Measurements.EquipmentSpatialMeasurements.Count == 0 &&
                sharedMeasured.Measurements.ProvisionalSpatialMeasurements.Any(x => x.SubjectKind == "Road"),
                "shared candidate distance remains provisional");

            var exact = SharedFixture(true);
            string exactId = Build(exact).EquipmentPlacements.Single(x => x.NormalizedTag == "P-01A").PlacementId;
            var exactCoordinate = new AuthoritativeEquipmentCoordinate { OccurrenceId = exactId, EquipmentTag = "P-01A", Easting = 900m, Northing = 800m };
            PlotPlanModel exactResult = Build(exact, new[] { exactCoordinate });
            Check(exactResult.EquipmentPlacements.Single(x => x.NormalizedTag == "P-01A").ResolutionState == PlacementResolutionState.Resolved &&
                exactResult.EquipmentPlacements.Single(x => x.NormalizedTag == "P-01A").DrawingCandidate == null &&
                exactResult.EquipmentPlacements.Single(x => x.NormalizedTag == "P-01A").Easting.SelectedValue == 900m,
                "exact authority overrides inference");

            var nearestOnly = New();
            Tag(nearestOnly, "T", "P-01A", 0, 0);
            X(nearestOnly, "X", 100, 1, 0);
            Y(nearestOnly, "Y", 200, 0, 1);
            nearestOnly.snapshot.TextEntities.Single(x => x.Handle == "Y").Rotation = 1;
            Check(Build(nearestOnly).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved,
                "nearest labels without coherent orientation");

            var competing = New();
            Tag(competing, "T", "P-01A", 0, 0);
            X(competing, "X1", 100, 0, 2); Y(competing, "Y1", 200, 0, 3);
            X(competing, "X2", 101, 0, -2); Y(competing, "Y2", 201, 0, -3);
            Check(Build(competing).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved,
                "competing clusters are not selected by distance or handle");

            var partial = New(); Tag(partial, "T", "P-01A", 0, 0); X(partial, "X", 100, 0, 2);
            Check(Build(partial).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved, "missing Y");
            partial.model.Coordinates.Clear(); partial.snapshot.TextEntities.RemoveAll(x => x.Handle == "X"); Y(partial, "Y", 200, 0, 3);
            Check(Build(partial).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved, "missing X");

            var unrelated = New(); Tag(unrelated, "T", "P-01A", 0, 0);
            X(unrelated, "X", 100, 0, 2); Y(unrelated, "Y", 200, 9, 3);
            Check(Build(unrelated).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved,
                "unrelated EQ-ID cluster cannot leak");

            var shared = New(); Tag(shared, "A", "TK-02", 0, 0); Tag(shared, "B", "TK-02", 0, 5);
            X(shared, "X", 100, 0, 2); Y(shared, "Y", 200, 0, 3);
            PlotPlanModel duplicates = Build(shared);
            Check(duplicates.EquipmentPlacements.Count == 2 && duplicates.EquipmentPlacements.Select(x => x.PlacementId).Distinct().Count() == 2
                && duplicates.EquipmentPlacements.All(x => x.ResolutionState == PlacementResolutionState.Unresolved),
                "duplicate occurrence and shared cluster exclusivity");

            var combined = New(); Tag(combined, "C", "P-03A/B", 0, 0);
            X(combined, "X", 100, 0, 2); Y(combined, "Y", 200, 0, 3);
            Check(Build(combined).EquipmentPlacements.All(x => x.ResolutionState != PlacementResolutionState.ReviewRequired),
                "combined annotation does not assign one pair to expanded occurrences");

            var owned = New(); Tag(owned, "ROOT:T", "P-01A", 0, 0);
            X(owned, "ROOT:X", 100, 0, 2); Y(owned, "ROOT:Y", 200, 0, 3);
            Check(Build(owned).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Resolved,
                "deterministic instantiated owner");

            string occurrence = item.PlacementId;
            var authoritative = new AuthoritativeEquipmentCoordinate { OccurrenceId = occurrence, EquipmentTag = "P-01A", Easting = 900m, Northing = 800m };
            EquipmentPlacement imported = Build(fixture, new[] { authoritative }).EquipmentPlacements.Single();
            Check(imported.ResolutionState == PlacementResolutionState.Resolved && imported.DrawingCandidate == null
                && imported.Easting.SelectedValue == 900m && imported.Northing.SelectedValue == 800m,
                "exact occurrence authoritative override");
            authoritative.OccurrenceId = occurrence + "-other";
            Check(Build(fixture, new[] { authoritative }).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.ReviewRequired,
                "authoritative package does not bind by tag alone");

            var badUnits = New("Meters"); Tag(badUnits, "T", "P-01A", 0, 0);
            X(badUnits, "X", 100, 0, 2); Y(badUnits, "Y", 200, 0, 3);
            EquipmentPlacement unitCandidate = Build(badUnits).EquipmentPlacements.Single();
            Check(unitCandidate.ResolutionState == PlacementResolutionState.ReviewRequired
                && unitCandidate.DrawingCandidate != null && !unitCandidate.DrawingCandidate.CoordinatesInDrawingUnits,
                "unproven coordinate basis retains review without measurement");

            var invalid = New(); Tag(invalid, "T", "P-01A", 0, 0);
            invalid.snapshot.TextEntities.Add(new TextSnapshot { Handle = "X", Text = "X= NaN", Layer = "EQ-ID", SpaceKind = "Model", Height = 1, PositionY = 2 });
            Y(invalid, "Y", 200, 0, 3);
            Check(Build(invalid).EquipmentPlacements.Single().ResolutionState == PlacementResolutionState.Unresolved,
                "nonfinite coordinate annotation rejected");
            Check(fixture.snapshot.TextEntities.Count == 3, "source snapshot was not mutated");
            CandidateDecisionTests();
        }

        private static void CandidateDecisionTests()
        {
            var f = SharedFixture(true);
            PlotPlanModel initial = Build(f);
            Check(initial.EquipmentPlacements.All(x => x.ResolutionState == PlacementResolutionState.ReviewRequired),
                "unapproved candidates remain review required");
            string temp = Path.Combine(Path.GetTempPath(), "EngiSentryCandidateReview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string drawingPath = Path.Combine(temp, "test.dwg");
            File.WriteAllText(drawingPath, "test-only drawing identity; no AutoCAD or engineer approval");
            string sha = HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath);
            var reviewService = new ProjectEngineerReviewService(Path.Combine(temp, "reviews"));
            var decisions = new EquipmentPlacementCandidateDecisionService(Path.Combine(temp, "resolutions"), reviewService);
            try
            {
                EquipmentPlacement a = initial.EquipmentPlacements.Single(x => x.NormalizedTag == "P-01A");
                EquipmentPlacement b = initial.EquipmentPlacements.Single(x => x.NormalizedTag == "P-01B");
                DrawingPlacementCandidateDecision accepted = decisions.Record(a, "test.dwg", drawingPath, sha,
                    "Millimeters", DrawingPlacementCandidateActions.Accept, "TEST_ONLY", "Automated fixture");
                DrawingPlacementCandidateDecision rejected = decisions.Record(b, "test.dwg", drawingPath, sha,
                    "Millimeters", DrawingPlacementCandidateActions.Reject, "TEST_ONLY", "Automated fixture");
                Check(HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath) == sha,
                    "accept and reject do not mutate drawing bytes");
                var loaded = decisions.ResolveForDrawing(sha, "Millimeters");
                Check(loaded.Count == 2 && loaded.All(x => !string.IsNullOrEmpty(x.ResponseId) &&
                    !string.IsNullOrEmpty(x.CandidateFingerprint)), "review persistence and provenance reload");
                PlotPlanModel decided = Build(f, candidateDecisions: loaded);
                EquipmentPlacement acceptedPlacement = decided.EquipmentPlacements.Single(x => x.PlacementId == a.PlacementId);
                EquipmentPlacement rejectedPlacement = decided.EquipmentPlacements.Single(x => x.PlacementId == b.PlacementId);
                Check(acceptedPlacement.ResolutionState == PlacementResolutionState.Resolved &&
                    acceptedPlacement.PlacementStatus == "Complete" && acceptedPlacement.CandidateDecision?.DecisionId == accepted.DecisionId &&
                    acceptedPlacement.Easting.SelectedRawText == DrawingPlacementCandidateActions.ConfirmedSource,
                    "exact accept resolves with truthful provenance");
                Check(rejectedPlacement.ResolutionState == PlacementResolutionState.Unresolved &&
                    rejectedPlacement.PlacementStatus != "Complete" && rejectedPlacement.CandidateDecision?.DecisionId == rejected.DecisionId,
                    "exact reject stays out of governed placement");
                Check(decided.EquipmentPlacements.Count(x => x.ResolutionState == PlacementResolutionState.Resolved) == 1,
                    "A/B occurrence isolation");

                var geometry = new PlotGeometryCapture();
                f.model.RoadLabels.Add(new TextEvidence { SourceHandle = "ROAD-TEXT", SourceLayer = "ROAD-TEXT", RawText = "ROAD EDGE", PositionX = 5, PositionY = 1 });
                f.model.BoundaryLabels.Add(new BoundaryEvidence { SourceHandle = "FENCE-TEXT", SourceLayer = "FENCE-TEXT", RawText = "FENCE", BoundaryType = "Fence", PositionX = 5, PositionY = 2 });
                geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "ROAD-TEXT", SourceLayer = "ROAD-TEXT", PositionX = 5, PositionY = 1, Height = 1 });
                geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "FENCE-TEXT", SourceLayer = "FENCE-TEXT", PositionX = 5, PositionY = 2, Height = 1 });
                geometry.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "ROAD", SourceLayer = "ROAD", GeometryKind = "Line",
                    Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = 0, Y = 0 }, new PlotGeometryVertex { X = 10, Y = 0 } } });
                geometry.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "FENCE", SourceLayer = "FENCE", GeometryKind = "Line",
                    Vertices = new List<PlotGeometryVertex> { new PlotGeometryVertex { X = 0, Y = 2 }, new PlotGeometryVertex { X = 10, Y = 2 } } });
                PlotPlanModel measured = PlotPlanBuilder.Build(EntityIndexer.Build(f.model), f.model, f.snapshot, geometry,
                    candidateDecisions: loaded);
                Check(measured.Measurements.EquipmentSpatialMeasurements.Any(x => x.PlacementId == a.PlacementId && x.SubjectKind == "Road") &&
                    measured.Measurements.EquipmentSpatialMeasurements.All(x => x.PlacementId != b.PlacementId),
                    "accepted candidate enters governed measurement; rejected does not");
                var authority = new AuthoritativeEquipmentCoordinate { OccurrenceId = a.PlacementId, EquipmentTag = "P-01A", Easting = 900m, Northing = 800m };
                PlotPlanModel higher = Build(f, new[] { authority }, loaded);
                Check(higher.EquipmentPlacements.Single(x => x.PlacementId == a.PlacementId).Easting.SelectedValue == 900m &&
                    higher.EquipmentPlacements.Single(x => x.PlacementId == a.PlacementId).CandidateDecision == null,
                    "exact authoritative provider supersedes candidate decision");
                Check(decisions.ResolveForDrawing(new string('A', 64), "Millimeters").Count == 0,
                    "wrong drawing fingerprint ignored");
                var wrongHandle = new DrawingPlacementCandidateDecision { OccurrenceId = a.PlacementId,
                    EquipmentTag = a.NormalizedTag, TagHandle = "OTHER-HANDLE", CandidateFingerprint = accepted.CandidateFingerprint,
                    CandidateX = accepted.CandidateX, CandidateY = accepted.CandidateY, XHandle = accepted.XHandle,
                    YHandle = accepted.YHandle, EvidenceType = accepted.EvidenceType, EvidenceSummary = accepted.EvidenceSummary,
                    InsertionUnits = accepted.InsertionUnits, Action = accepted.Action };
                Check(Build(f, candidateDecisions: new[] { wrongHandle }).EquipmentPlacements.Single(x => x.PlacementId == a.PlacementId)
                    .ResolutionState == PlacementResolutionState.ReviewRequired, "wrong occurrence handle ignored");

                var duplicate = SharedFixture(true);
                duplicate.model.EquipmentTags.ForEach(x => x.NormalizedTag = "P-03A");
                duplicate.snapshot.TextEntities.Where(x => x.Layer == "EQ-TAG").ToList().ForEach(x => x.Text = "P-03A");
                PlotPlanModel duplicatePlan = Build(duplicate);
                Check(duplicatePlan.EquipmentPlacements.Count == 2 &&
                    duplicatePlan.EquipmentPlacements.Select(x => x.PlacementId).Distinct().Count() == 2,
                    "duplicate canonical tag keeps exact occurrences");
                EquipmentPlacement first = duplicatePlan.EquipmentPlacements.OrderBy(x => x.PlacementId).First();
                DrawingPlacementCandidateDecision firstOnly = decisions.Record(first, "test.dwg", drawingPath, sha,
                    "Millimeters", DrawingPlacementCandidateActions.Accept, "TEST_ONLY", "Automated fixture");
                PlotPlanModel duplicateDecided = Build(duplicate, candidateDecisions: new[] { firstOnly });
                Check(duplicateDecided.EquipmentPlacements.Count(x => x.ResolutionState == PlacementResolutionState.Resolved) == 1 &&
                    duplicateDecided.EquipmentPlacements.Count(x => x.ResolutionState == PlacementResolutionState.ReviewRequired) == 1,
                    "P-03A occurrence A does not approve occurrence B");
            }
            finally { Directory.Delete(temp, true); }
        }

        private static (DrawingSnapshot snapshot, EngineeringDrawingModel model) New(string units = "Millimeters") =>
            (new DrawingSnapshot { InsertionUnits = units }, new EngineeringDrawingModel { Drawing = new SemanticDrawing { InsertionUnits = units } });

        private static (DrawingSnapshot snapshot, EngineeringDrawingModel model) SharedFixture(bool sharedX)
        {
            var f = New();
            Tag(f, "A", "P-01A", 0, 0); Tag(f, "B", "P-01B", 0, -10);
            if (sharedX)
            {
                Y(f, "OWN-A", 200, 0, -1); Y(f, "OWN-B", 190, 0, -11);
                X(f, "SHARED-X", 100, 0, -13);
            }
            else
            {
                X(f, "OWN-A", 100, 0, -1); X(f, "OWN-B", 110, 0, -11);
                Y(f, "SHARED-Y", 200, 0, -13);
            }
            return f;
        }

        private static void Tag((DrawingSnapshot snapshot, EngineeringDrawingModel model) f, string handle, string text, double x, double y)
        {
            f.snapshot.TextEntities.Add(new TextSnapshot { Handle = handle, Text = text, Layer = "EQ-TAG", SpaceKind = "Model", Height = 1, PositionX = x, PositionY = y });
            f.model.EquipmentTags.Add(new EquipmentTag { SourceHandle = handle, SourceLayer = "EQ-TAG", NormalizedTag = text });
        }
        private static void X((DrawingSnapshot snapshot, EngineeringDrawingModel model) f, string handle, decimal value, double x, double y) => Coordinate(f, handle, value, x, y, "E", "X= ");
        private static void Y((DrawingSnapshot snapshot, EngineeringDrawingModel model) f, string handle, decimal value, double x, double y) => Coordinate(f, handle, value, x, y, "N", "Y= ");
        private static void Coordinate((DrawingSnapshot snapshot, EngineeringDrawingModel model) f, string handle, decimal value, double x, double y, string axis, string prefix)
        {
            string text = prefix + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            f.snapshot.TextEntities.Add(new TextSnapshot { Handle = handle, Text = text, Layer = "EQ-ID", SpaceKind = "Model", Height = 1, PositionX = x, PositionY = y });
            f.model.Coordinates.Add(new CoordinateEvidence { SourceHandle = handle, SourceLayer = "EQ-ID", RawText = text, PositionX = x, PositionY = y, Axis = axis, Value = value, ParseStatus = "Parsed" });
        }
        private static PlotPlanModel Build((DrawingSnapshot snapshot, EngineeringDrawingModel model) f,
            IReadOnlyList<AuthoritativeEquipmentCoordinate>? coordinates = null,
            IReadOnlyList<DrawingPlacementCandidateDecision>? candidateDecisions = null) =>
            PlotPlanBuilder.Build(EntityIndexer.Build(f.model), f.model, f.snapshot,
                authoritativeCoordinates: coordinates, candidateDecisions: candidateDecisions);
    }
}
