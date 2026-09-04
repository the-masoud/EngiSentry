using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class MeasurementTests
    {
        internal static void Run()
        {
            PlotSpatialMeasurementModel eligibility = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:complete", 0m, 0m), Placement("placement:incomplete", 1m, 1m, "Incomplete"), Placement("placement:ambiguous", 2m, 2m, "Ambiguous"), Placement("placement:missing-coordinate", 3m, 3m, "Complete", "Missing", "Resolved") }, new[] { Constraint("constraint:road", "Road", 0, 0, 10, 0) });
            Assert(eligibility.Statistics.EligibleEquipmentPlacementCount == 1 && eligibility.Statistics.EquipmentPairMeasurementCount == 0 && eligibility.Statistics.EquipmentSpatialMeasurementCount == 1, "Placement eligibility did not require Complete resolved coordinates.");

            EquipmentPlacement z = Placement("placement:z", 0m, 0m);
            EquipmentPlacement a = Placement("placement:a", 3m, 4m);
            EquipmentPlacement b = Placement("placement:b", 0m, 0m);
            SpatialCoordinateConstraint road = Constraint("constraint:road", "Road", 0, 0, 10, 0);
            SpatialCoordinateConstraint boundary = Constraint("constraint:boundary", "Boundary", 0, 0, 0, 10);
            PlotSpatialMeasurementModel measured = PlotSpatialMeasurementBuilder.Build(new[] { z, a, b }, new[] { boundary, road, new SpatialCoordinateConstraint { ConstraintId = "constraint:missing", Status = "Missing" }, new SpatialCoordinateConstraint { ConstraintId = "constraint:ambiguous", Status = "Ambiguous" } });
            Assert(measured.Statistics.EligibleEquipmentPlacementCount == 3 && measured.Statistics.EquipmentPairMeasurementCount == 3 && measured.Statistics.EquipmentSpatialMeasurementCount == 6 && measured.Statistics.ResolvedSpatialConstraintSourceCount == 2 && measured.Statistics.RoadMeasurementCount == 3 && measured.Statistics.BoundaryMeasurementCount == 3, "Measurement count invariants or subject counts failed.");
            Assert(measured.EquipmentPairMeasurements.Select(x => x.MeasurementId).SequenceEqual(measured.EquipmentPairMeasurements.Select(x => x.MeasurementId).OrderBy(x => x, StringComparer.Ordinal)) && measured.EquipmentPairMeasurements.Single(x => x.PlacementIdA == "placement:a" && x.PlacementIdB == "placement:b").Distance2D == 5d && measured.EquipmentPairMeasurements.Any(x => x.Distance2D == 0d), "Equipment pairs were not unique, ordinal, correct, and zero-safe.");
            Assert(measured.EquipmentSpatialMeasurements.Select(x => x.MeasurementId).SequenceEqual(measured.EquipmentSpatialMeasurements.Select(x => x.MeasurementId).OrderBy(x => x, StringComparer.Ordinal)), "Equipment-spatial measurement ordering was not deterministic.");
            Assert(Ids(measured) == Ids(PlotSpatialMeasurementBuilder.Build(new[] { b, z, a }, new[] { road, boundary })), "Measurement identities changed with input order.");

            EquipmentSpatialMeasurement perpendicular = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:p", 5m, 3m) }, new[] { Constraint("constraint:perpendicular", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            Assert(perpendicular.ClosestPointX == 5d && perpendicular.ClosestPointY == 0d && perpendicular.Distance2D == 3d, "Perpendicular point-to-segment measurement failed.");
            EquipmentSpatialMeasurement before = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:before", -2m, 3m) }, new[] { Constraint("constraint:before", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            EquipmentSpatialMeasurement after = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:after", 12m, 4m) }, new[] { Constraint("constraint:after", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            Assert(before.ClosestPointX == 0d && before.ClosestPointY == 0d && Math.Abs(before.Distance2D - Math.Sqrt(13)) < 0.0000001 && after.ClosestPointX == 10d && after.ClosestPointY == 0d && Math.Abs(after.Distance2D - Math.Sqrt(20)) < 0.0000001, "Segment endpoint clamping failed.");

            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { new SpatialCoordinateConstraint { ConstraintId = "constraint:invalid", Status = "Resolved", SelectedGeometryHandle = "H", SelectedGeometryLayer = "L", SelectedGeometryKind = "Line", SelectedSegmentIndex = 0 } }), "Selected candidate evidence was not required.");
            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { Constraint("constraint:zero", "Road", 1, 1, 1, 1) }), "Zero-length selected segment was not rejected.");
            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { Constraint("constraint:nonfinite", "Road", double.NaN, 0, 1, 1) }), "Non-finite selected segment was not rejected.");

            var index = new EngineeringEntityIndex();
            var plotWithoutMeasurements = new PlotPlanModel { Geometry = new PlotGeometryModel { SpatialConstraints = new List<SpatialCoordinateConstraint> { new SpatialCoordinateConstraint { ConstraintId = "spatial-constraint:boundary:B1", Status = "Ambiguous", LabelSourceHandle = "B1" } } } };
            var plotWithMeasurements = new PlotPlanModel { Geometry = plotWithoutMeasurements.Geometry, Measurements = measured };
            QaReport unprofiledBefore = QaEngine.Evaluate(index, BuiltInQaRules.All, plotWithoutMeasurements, BuiltInPlotPlanQaRules.All);
            QaReport unprofiledAfter = QaEngine.Evaluate(index, BuiltInQaRules.All, plotWithMeasurements, BuiltInPlotPlanQaRules.All);
            Assert(QaIdentity(unprofiledBefore) == QaIdentity(unprofiledAfter), "Measurements changed unprofiled QA results.");
            ProfileResolution defaults = ProfileResolver.Resolve(Catalog(), new EngineeringStandardProfile[0]);
            QaReport profiledBefore = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plotWithoutMeasurements, BuiltInPlotPlanQaRules.All, defaults);
            QaReport profiledAfter = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plotWithMeasurements, BuiltInPlotPlanQaRules.All, defaults);
            Assert(QaIdentity(profiledBefore) == QaIdentity(profiledAfter), "Measurements changed profile-aware QA results.");

            var report = new PlotPlanQaReport { PlotPlan = new PlotPlanModel { Geometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { new PlotGeometryPrimitive { SourceHandle = "raw", SourceLayer = "RAW", GeometryKind = "Line" } }, Associations = new List<GeometryAssociation> { new GeometryAssociation { AssociationId = "association:1" } }, Statistics = new PlotGeometryStatistics { SupportedPrimitiveCount = 597995 }, SpatialConstraints = new List<SpatialCoordinateConstraint> { road } }, Measurements = measured }, Qa = new QaReport { ProfileResolution = defaults, Findings = new List<QaFinding> { new QaFinding { FindingId = "finding:1", Evidence = new QaEvidence { Reason = "preserved" } } } } };
            PlotPlanQaReport compact = PlotPlanReportCompactor.Create(report);
            Assert(report.PlotPlan.Geometry.Primitives.Count == 1 && compact.PlotPlan.Geometry.Primitives.Count == 0 && compact.PlotPlan.Geometry.Statistics.SupportedPrimitiveCount == 597995 && compact.PlotPlan.Geometry.Associations.Count == 1 && compact.PlotPlan.Geometry.SpatialConstraints.Count == 1 && compact.PlotPlan.Measurements.EquipmentSpatialMeasurements.Count == measured.EquipmentSpatialMeasurements.Count && compact.Qa.Findings.Single().Evidence.Reason == "preserved" && compact.Qa.ProfileResolution != null, "Compact report did not preserve required report evidence.");
            string json;
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(PlotPlanQaReport)).WriteObject(stream, compact); json = Encoding.UTF8.GetString(stream.ToArray()); }
            Assert(json.Contains("\"Primitives\":[]") && json.Contains("\"ProfileResolution\""), "Compact artifact serialization did not preserve its contract.");
        }

        private static EquipmentPlacement Placement(string id, decimal easting, decimal northing, string status = "Complete", string eastStatus = "Resolved", string northStatus = "Resolved") => new EquipmentPlacement { PlacementId = id, EquipmentEntityId = "equipment:" + id, NormalizedTag = id, PlacementStatus = status, Easting = new CoordinateAssociation { Axis = "E", Status = eastStatus, SelectedValue = easting }, Northing = new CoordinateAssociation { Axis = "N", Status = northStatus, SelectedValue = northing } };
        private static SpatialCoordinateConstraint Constraint(string id, string subject, double startX, double startY, double endX, double endY)
        {
            var candidate = new SpatialGeometryCandidate { SourceHandle = "H-" + id, SourceLayer = "L-" + subject, GeometryKind = "Line", SegmentIndex = 0, SegmentStartX = startX, SegmentStartY = startY, SegmentEndX = endX, SegmentEndY = endY };
            return new SpatialCoordinateConstraint { ConstraintId = id, SubjectKind = subject, Axis = "E", CoordinateValue = 1m, LabelSourceHandle = "Label-" + id, Status = "Resolved", SelectedGeometryHandle = candidate.SourceHandle, SelectedGeometryLayer = candidate.SourceLayer, SelectedGeometryKind = candidate.GeometryKind, SelectedSegmentIndex = candidate.SegmentIndex, Candidates = new List<SpatialGeometryCandidate> { candidate } };
        }
        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).ToList();
        private static string Ids(PlotSpatialMeasurementModel model) => string.Join("|", model.EquipmentPairMeasurements.Select(x => x.MeasurementId)) + ";" + string.Join("|", model.EquipmentSpatialMeasurements.Select(x => x.MeasurementId));
        private static string QaIdentity(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static void Throws(Action action, string message) { bool thrown = false; try { action(); } catch (InvalidOperationException) { thrown = true; } Assert(thrown, message); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
