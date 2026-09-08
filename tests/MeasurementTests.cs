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
using EngiSentry.Semantics;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class MeasurementTests
    {
        internal static void Run()
        {
            EngineeringDrawingModel semanticUnits = SemanticExtractor.Extract(new DrawingSnapshot { InsertionUnits = "Millimeters" });
            EngineeringDrawingModel semanticWhitespaceUnits = SemanticExtractor.Extract(new DrawingSnapshot { InsertionUnits = " Meters " });
            Assert(semanticUnits.Drawing!.InsertionUnits == "Millimeters" && semanticWhitespaceUnits.Drawing!.InsertionUnits == " Meters ", "Semantic drawing did not preserve snapshot insertion units exactly.");

            var units = new[]
            {
                new { InsertionUnits = "Millimeters", CanonicalUnit = "mm", MetersPerDrawingUnit = 0.001d, DrawingDistance = 1000d, Meters = 1d },
                new { InsertionUnits = "Centimeters", CanonicalUnit = "cm", MetersPerDrawingUnit = 0.01d, DrawingDistance = 100d, Meters = 1d },
                new { InsertionUnits = "Meters", CanonicalUnit = "m", MetersPerDrawingUnit = 1d, DrawingDistance = 1d, Meters = 1d },
                new { InsertionUnits = "Kilometers", CanonicalUnit = "km", MetersPerDrawingUnit = 1000d, DrawingDistance = 1d, Meters = 1000d },
                new { InsertionUnits = "Inches", CanonicalUnit = "in", MetersPerDrawingUnit = 0.0254d, DrawingDistance = 12d, Meters = 0.3048d },
                new { InsertionUnits = "Feet", CanonicalUnit = "ft", MetersPerDrawingUnit = 0.3048d, DrawingDistance = 10d, Meters = 3.048d },
                new { InsertionUnits = "Yards", CanonicalUnit = "yd", MetersPerDrawingUnit = 0.9144d, DrawingDistance = 1d, Meters = 0.9144d },
                new { InsertionUnits = "Miles", CanonicalUnit = "mi", MetersPerDrawingUnit = 1609.344d, DrawingDistance = 1d, Meters = 1609.344d }
            };
            foreach (var expectedUnit in units)
            {
                DrawingUnitResolution resolution = DrawingUnitResolver.Resolve(expectedUnit.InsertionUnits);
                Assert(resolution.InsertionUnits == expectedUnit.InsertionUnits && resolution.CanonicalUnit == expectedUnit.CanonicalUnit && resolution.MetersPerDrawingUnit == expectedUnit.MetersPerDrawingUnit && resolution.ResolutionSource == "AutoCAD.Insunits", "Drawing-unit mapping was not exact for " + expectedUnit.InsertionUnits + ".");
                AssertAlmost(DrawingUnitResolver.DrawingUnitsToMeters(expectedUnit.DrawingDistance, resolution), expectedUnit.Meters, "Drawing-unit conversion failed for " + expectedUnit.InsertionUnits + ".");
                AssertAlmost(DrawingUnitResolver.MetersToDrawingUnits(expectedUnit.Meters, resolution), expectedUnit.DrawingDistance, "Metre conversion failed for " + expectedUnit.InsertionUnits + ".");
                Assert(DrawingUnitResolver.DrawingUnitsToMeters(0d, resolution) == 0d && DrawingUnitResolver.MetersToDrawingUnits(0d, resolution) == 0d, "Zero conversion failed for " + expectedUnit.InsertionUnits + ".");
                AssertAlmost(DrawingUnitResolver.MetersToDrawingUnits(DrawingUnitResolver.DrawingUnitsToMeters(123.456d, resolution), resolution), 123.456d, "Positive conversion round-trip failed for " + expectedUnit.InsertionUnits + ".");
            }
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve(null!), "drawing insertion units are required", "Null insertion units were not rejected.");
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve(string.Empty), "drawing insertion units are required", "Empty insertion units were not rejected.");
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve(" "), "drawing insertion units are required", "Blank insertion units were not rejected.");
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve("Undefined"), "unsupported drawing insertion units: Undefined", "Undefined insertion units were not rejected.");
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve("Unitless"), "unsupported drawing insertion units: Unitless", "Unitless insertion units were not rejected.");
            ThrowsDrawingUnit(() => DrawingUnitResolver.Resolve("meters"), "unsupported drawing insertion units: meters", "Case-mismatched insertion units were not rejected.");
            DrawingUnitResolution metreResolution = DrawingUnitResolver.Resolve("Meters");
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            {
                ThrowsDrawingUnit(() => DrawingUnitResolver.DrawingUnitsToMeters(invalid, metreResolution), "drawing distance must be finite and non-negative", "Invalid drawing distance was not rejected.");
                ThrowsDrawingUnit(() => DrawingUnitResolver.MetersToDrawingUnits(invalid, metreResolution), "metre distance must be finite and non-negative", "Invalid metre distance was not rejected.");
            }
            ThrowsArgumentNull(() => DrawingUnitResolver.DrawingUnitsToMeters(1d, null!), "Null drawing-unit resolution was not rejected.");
            ThrowsArgumentNull(() => DrawingUnitResolver.MetersToDrawingUnits(1d, null!), "Null drawing-unit resolution was not rejected for inverse conversion.");
            foreach (DrawingUnitResolution invalidResolution in new[]
            {
                new DrawingUnitResolution { InsertionUnits = "Meters", CanonicalUnit = "wrong", MetersPerDrawingUnit = 1d, ResolutionSource = "AutoCAD.Insunits" },
                new DrawingUnitResolution { InsertionUnits = "Meters", CanonicalUnit = "m", MetersPerDrawingUnit = 2d, ResolutionSource = "AutoCAD.Insunits" },
                new DrawingUnitResolution { InsertionUnits = "Meters", CanonicalUnit = "m", MetersPerDrawingUnit = 1d, ResolutionSource = "wrong" }
            })
            {
                ThrowsDrawingUnit(() => DrawingUnitResolver.DrawingUnitsToMeters(1d, invalidResolution), "drawing unit resolution is invalid: Meters", "Mutated drawing-unit resolution was accepted.");
            }

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
            Assert(measured.EquipmentPairMeasurements.Single(x => x.PlacementIdA == "placement:a" && x.PlacementIdB == "placement:b").Distance2D == 5d, "Existing Distance2D was automatically converted.");

            EquipmentSpatialMeasurement perpendicular = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:p", 5m, 3m) }, new[] { Constraint("constraint:perpendicular", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            Assert(perpendicular.ClosestPointX == 5d && perpendicular.ClosestPointY == 0d && perpendicular.Distance2D == 3d, "Perpendicular point-to-segment measurement failed.");
            EquipmentSpatialMeasurement before = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:before", -2m, 3m) }, new[] { Constraint("constraint:before", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            EquipmentSpatialMeasurement after = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:after", 12m, 4m) }, new[] { Constraint("constraint:after", "Road", 0, 0, 10, 0) }).EquipmentSpatialMeasurements.Single();
            Assert(before.ClosestPointX == 0d && before.ClosestPointY == 0d && Math.Abs(before.Distance2D - Math.Sqrt(13)) < 0.0000001 && after.ClosestPointX == 10d && after.ClosestPointY == 0d && Math.Abs(after.Distance2D - Math.Sqrt(20)) < 0.0000001, "Segment endpoint clamping failed.");

            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { new SpatialCoordinateConstraint { ConstraintId = "constraint:invalid", Status = "Resolved", SelectedGeometryHandle = "H", SelectedGeometryLayer = "L", SelectedGeometryKind = "Line", SelectedSegmentIndex = 0 } }), "Selected candidate evidence was not required.");
            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { Constraint("constraint:zero", "Road", 1, 1, 1, 1) }), "Zero-length selected segment was not rejected.");
            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:x", 0m, 0m) }, new[] { Constraint("constraint:nonfinite", "Road", double.NaN, 0, 1, 1) }), "Non-finite selected segment was not rejected.");

            RunP410QualificationTests();

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

            var compactSubject = new EngineeringSpatialSubject { SubjectId = "engineering-spatial-subject:securityfence:raw", SubjectKind = "Boundary", EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.SecurityFence, GeometrySourceHandle = "raw", GeometrySourceLayer = "RAW", GeometryKind = "Line", AssociationIds = new List<string> { "association:1" }, LabelSourceHandles = new List<string> { "label:1" } };
            var report = new PlotPlanQaReport { PlotPlan = new PlotPlanModel { Geometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { new PlotGeometryPrimitive { SourceHandle = "raw", SourceLayer = "RAW", GeometryKind = "Line" } }, Associations = new List<GeometryAssociation> { new GeometryAssociation { AssociationId = "association:1" } }, Statistics = new PlotGeometryStatistics { SupportedPrimitiveCount = 597995 }, SpatialConstraints = new List<SpatialCoordinateConstraint> { road }, EngineeringSpatialSubjects = new List<EngineeringSpatialSubject> { compactSubject } }, Measurements = measured }, Qa = new QaReport { ProfileResolution = defaults, Findings = new List<QaFinding> { new QaFinding { FindingId = "finding:1", Evidence = new QaEvidence { Reason = "preserved" } } } } };
            PlotPlanQaReport compact = PlotPlanReportCompactor.Create(report);
            Assert(report.PlotPlan.Geometry.Primitives.Count == 1 && compact.PlotPlan.Geometry.Primitives.Count == 0 && compact.PlotPlan.Geometry.EngineeringSpatialSubjects.Count == 1 && compact.PlotPlan.Geometry.EngineeringSpatialSubjects.Single().SubjectId == compactSubject.SubjectId && compact.PlotPlan.Geometry.EngineeringSpatialSubjects.Single().EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence && compact.PlotPlan.Geometry.Statistics.SupportedPrimitiveCount == 597995 && compact.PlotPlan.Geometry.Associations.Count == 1 && compact.PlotPlan.Geometry.SpatialConstraints.Count == 1 && compact.PlotPlan.Measurements.EquipmentSpatialMeasurements.Count == measured.EquipmentSpatialMeasurements.Count && compact.Qa.Findings.Single().Evidence.Reason == "preserved" && compact.Qa.ProfileResolution != null, "Compact report did not preserve required report evidence.");
            string json;
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(PlotPlanQaReport)).WriteObject(stream, compact); json = Encoding.UTF8.GetString(stream.ToArray()); }
            Assert(json.Contains("\"Primitives\":[]") && json.Contains("\"EngineeringSpatialSubjects\"") && json.Contains(compactSubject.SubjectId) && json.Contains("\"ProfileResolution\""), "Compact artifact serialization did not preserve its contract.");
        }

        private static void RunP410QualificationTests()
        {
            PlotGeometryPrimitive fence = Primitive("F", 0, 0, 10, 0);
            EngineeringDrawingModel fenceModel = new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "Fence", SourceHandle = "LF" } } };
            PlotGeometryModel resolvedFence = Geometry("Boundary", "Resolved", "LF", "F", fence);
            IReadOnlyList<EngineeringSpatialSubject> fenceSubjects = EngineeringSpatialSubjectBuilder.Build(fenceModel, resolvedFence);
            Assert(fenceSubjects.Count == 1 && fenceSubjects[0].EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence, "Fence semantic evidence did not qualify SecurityFence geometry.");
            Assert(EngineeringSpatialSubjectBuilder.Build(fenceModel, Geometry("Boundary", "Missing", "LF", "F", fence)).Count == 0 && EngineeringSpatialSubjectBuilder.Build(fenceModel, Geometry("Boundary", "Ambiguous", "LF", "F", fence)).Count == 0, "Missing or ambiguous Fence association was promoted.");
            IReadOnlyList<EngineeringSpatialSubject> unitSubjects = EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "LF" } } }, resolvedFence);
            Assert(unitSubjects.Count == 1 && unitSubjects.Single().SubjectKind == "Boundary" && unitSubjects.Single().EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary && unitSubjects.Single().GeometrySourceHandle == "F" && unitSubjects.Single().AssociationIds.SequenceEqual(new[] { "association" }) && unitSubjects.Single().LabelSourceHandles.SequenceEqual(new[] { "LF" }) && EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel(), resolvedFence).Count == 0, "UnitLimit or layer-only evidence qualification failed.");
            EngineeringDrawingModel unitModel = new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "U2" }, new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "U1" } } };
            PlotGeometryModel unitGeometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { fence }, Associations = new List<GeometryAssociation> { Association("Boundary", "Resolved", "U2", "F", "u2"), Association("Boundary", "Resolved", "U1", "F", "u1") } };
            EngineeringSpatialSubject unitMerged = EngineeringSpatialSubjectBuilder.Build(unitModel, unitGeometry).Single();
            Assert(unitMerged.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary && unitMerged.AssociationIds.SequenceEqual(new[] { "u1", "u2" }) && unitMerged.LabelSourceHandles.SequenceEqual(new[] { "U1", "U2" }) && SubjectIdentity(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = unitModel.BoundaryLabels.AsEnumerable().Reverse().ToList() }, new PlotGeometryModel { Primitives = unitGeometry.Primitives.AsEnumerable().Reverse().ToList(), Associations = unitGeometry.Associations.AsEnumerable().Reverse().ToList() })) == SubjectIdentity(new[] { unitMerged }), "UnitBoundary determinism failed.");
            Assert(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "LF" } } }, Geometry("Boundary", "Missing", "LF", "F", fence)).Count == 0 && EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "LF" } } }, Geometry("Boundary", "Ambiguous", "LF", "F", fence)).Count == 0 && EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "BatteryLimit", SourceHandle = "LF" }, new BoundaryEvidence { BoundaryType = "Boundary", SourceHandle = "LG" } } }, resolvedFence).Count == 0, "UnitBoundary fail-closed qualification failed.");
            Assert(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "Boundary", SourceHandle = "LG" } } }, Geometry("Boundary", "Resolved", "LG", "F", fence)).Count == 0, "Resolved generic Boundary was qualified as UnitBoundary.");
            Throws(() => EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "Fence", SourceHandle = "LF" }, new BoundaryEvidence { BoundaryType = "UnitLimit", SourceHandle = "LF" } } }, resolvedFence), "Incompatible SecurityFence and UnitBoundary geometry was accepted.");
            EngineeringSpatialSubject unitMeasurementSubject = Subject("Boundary", EngineeringDistanceEvidenceKeys.UnitBoundary, "F", "Layer", "Polyline", "LF");
            EquipmentSpatialMeasurement unitMeasurement = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:unit", 5m, 2m, applicability: EngineeringDistanceEvidenceKeys.Equipment) }, new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { fence }, EngineeringSpatialSubjects = new List<EngineeringSpatialSubject> { unitMeasurementSubject } }).EquipmentSpatialMeasurements.Single();
            Assert(unitMeasurement.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary && unitMeasurement.EngineeringApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment, "UnitBoundary measurement propagation failed.");
            Throws(() => EngineeringSpatialSubjectBuilder.Build(fenceModel, Geometry("Boundary", "Resolved", "LF", "missing")), "Missing selected Fence primitive was accepted.");
            PlotGeometryModel duplicate = Geometry("Boundary", "Resolved", "LF", "F", fence, Primitive("F", 0, 0, 10, 0));
            Throws(() => EngineeringSpatialSubjectBuilder.Build(fenceModel, duplicate), "Duplicate selected Fence primitive was accepted.");

            foreach (string edge in new[] { "ACCESS ROAD EDGE", "ROAD EDGE", "EDGE OF ROAD", "EDGE OF ACCESS ROAD" })
                Assert(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { RoadLabels = new List<TextEvidence> { new TextEvidence { RawText = edge, SourceHandle = "LR" } } }, Geometry("Road", "Resolved", "LR", "R", Primitive("R", 0, 0, 10, 0))).Single().EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge, "Explicit road-edge text was not qualified: " + edge);
            foreach (string centerline in new[] { "ROAD C.L", "ROAD CL", "ROAD CENTERLINE", "ROAD CENTER LINE", "ROAD CENTRELINE", "ROAD CENTRE LINE" })
                Assert(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { RoadLabels = new List<TextEvidence> { new TextEvidence { RawText = centerline, SourceHandle = "LR" } } }, Geometry("Road", "Resolved", "LR", "R", Primitive("R", 0, 0, 10, 0))).Count == 0, "Road centerline was qualified as a road edge: " + centerline);

            EngineeringDrawingModel twoLabels = new EngineeringDrawingModel { BoundaryLabels = new List<BoundaryEvidence> { new BoundaryEvidence { BoundaryType = "Fence", SourceHandle = "B2" }, new BoundaryEvidence { BoundaryType = "Fence", SourceHandle = "B1" } } };
            PlotGeometryModel twoAssociations = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { fence }, Associations = new List<GeometryAssociation> { Association("Boundary", "Resolved", "B2", "F", "a2"), Association("Boundary", "Resolved", "B1", "F", "a1") } };
            EngineeringSpatialSubject merged = EngineeringSpatialSubjectBuilder.Build(twoLabels, twoAssociations).Single();
            Assert(merged.AssociationIds.SequenceEqual(new[] { "a1", "a2" }) && merged.LabelSourceHandles.SequenceEqual(new[] { "B1", "B2" }) && merged.SubjectId == "engineering-spatial-subject:securityfence:F", "Fence corroboration was not deterministically merged.");
            Assert(SubjectIdentity(EngineeringSpatialSubjectBuilder.Build(twoLabels, twoAssociations)) == SubjectIdentity(EngineeringSpatialSubjectBuilder.Build(new EngineeringDrawingModel { BoundaryLabels = twoLabels.BoundaryLabels.AsEnumerable().Reverse().ToList() }, new PlotGeometryModel { Primitives = twoAssociations.Primitives.AsEnumerable().Reverse().ToList(), Associations = twoAssociations.Associations.AsEnumerable().Reverse().ToList() })), "Engineering subject output changed with input ordering.");

            var fullGeometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { new PlotGeometryPrimitive { SourceHandle = "P", SourceLayer = "L", GeometryKind = "Polyline", Vertices = new List<PlotGeometryVertex> { Vertex(0, 0), Vertex(0, 100), Vertex(100, 100) } } }, EngineeringSpatialSubjects = new List<EngineeringSpatialSubject> { Subject("Boundary", EngineeringDistanceEvidenceKeys.SecurityFence, "P", "L", "Polyline", "label") } };
            EquipmentSpatialMeasurement full = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:full", 50m, 98m) }, fullGeometry).EquipmentSpatialMeasurements.Single();
            Assert(full.SegmentIndex == 1 && full.Distance2D == 2d && full.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence && full.Axis == string.Empty && full.CoordinateValue == 0m && full.EngineeringApplicabilityKey == null, "Engineering measurement did not use the nearest full-primitive segment or preserve null applicability.");
            fullGeometry.Primitives[0].Closed = true;
            EquipmentSpatialMeasurement closing = PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:closing", 50m, 2m, applicability: "ProcessingEquipment") }, fullGeometry).EquipmentSpatialMeasurements.Single();
            Assert(closing.SegmentIndex == 2 && closing.EngineeringApplicabilityKey == "ProcessingEquipment", "Closed primitive segment or explicit applicability propagation failed.");
            fullGeometry.Primitives[0].Vertices.Insert(1, Vertex(0, 0));
            Assert(PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:zero", 50m, 98m) }, fullGeometry).EquipmentSpatialMeasurements.Single().Distance2D == 2d, "Zero-length engineering segment was not skipped.");
            fullGeometry.Primitives[0].Vertices = new List<PlotGeometryVertex> { Vertex(0, 0), Vertex(0, 0) };
            Throws(() => PlotSpatialMeasurementBuilder.Build(new[] { Placement("placement:allzero", 1m, 1m) }, fullGeometry), "All-zero engineering primitive was accepted.");
        }

        private static EquipmentPlacement Placement(string id, decimal easting, decimal northing, string status = "Complete", string eastStatus = "Resolved", string northStatus = "Resolved", string? applicability = null) => new EquipmentPlacement { PlacementId = id, EquipmentEntityId = "equipment:" + id, NormalizedTag = id, PlacementStatus = status, EngineeringApplicabilityKey = applicability, Easting = new CoordinateAssociation { Axis = "E", Status = eastStatus, SelectedValue = easting }, Northing = new CoordinateAssociation { Axis = "N", Status = northStatus, SelectedValue = northing } };
        private static PlotGeometryVertex Vertex(double x, double y) => new PlotGeometryVertex { X = x, Y = y, Z = 0d };
        private static PlotGeometryPrimitive Primitive(string handle, double x1, double y1, double x2, double y2) => new PlotGeometryPrimitive { SourceHandle = handle, SourceLayer = "Layer", GeometryKind = "Polyline", Vertices = new List<PlotGeometryVertex> { Vertex(x1, y1), Vertex(x2, y2) } };
        private static GeometryAssociation Association(string subjectKind, string status, string labelHandle, string geometryHandle, string id = "association") => new GeometryAssociation { AssociationId = id, SubjectKind = subjectKind, LabelSourceHandle = labelHandle, Status = status, SelectedGeometryHandle = geometryHandle };
        private static PlotGeometryModel Geometry(string subjectKind, string status, string labelHandle, string geometryHandle, params PlotGeometryPrimitive[] primitives) => new PlotGeometryModel { Primitives = primitives.ToList(), Associations = new List<GeometryAssociation> { Association(subjectKind, status, labelHandle, geometryHandle) } };
        private static EngineeringSpatialSubject Subject(string subjectKind, string basis, string handle, string layer, string kind, string label) => new EngineeringSpatialSubject { SubjectId = "engineering-spatial-subject:" + basis.ToLowerInvariant() + ":" + handle, SubjectKind = subjectKind, EngineeringMeasurementBasisKey = basis, GeometrySourceHandle = handle, GeometrySourceLayer = layer, GeometryKind = kind, AssociationIds = new List<string> { "association" }, LabelSourceHandles = new List<string> { label } };
        private static string SubjectIdentity(IEnumerable<EngineeringSpatialSubject> subjects) => string.Join("|", subjects.Select(x => x.SubjectId + ":" + string.Join(",", x.AssociationIds) + ":" + string.Join(",", x.LabelSourceHandles)));
        private static SpatialCoordinateConstraint Constraint(string id, string subject, double startX, double startY, double endX, double endY)
        {
            var candidate = new SpatialGeometryCandidate { SourceHandle = "H-" + id, SourceLayer = "L-" + subject, GeometryKind = "Line", SegmentIndex = 0, SegmentStartX = startX, SegmentStartY = startY, SegmentEndX = endX, SegmentEndY = endY };
            return new SpatialCoordinateConstraint { ConstraintId = id, SubjectKind = subject, Axis = "E", CoordinateValue = 1m, LabelSourceHandle = "Label-" + id, Status = "Resolved", SelectedGeometryHandle = candidate.SourceHandle, SelectedGeometryLayer = candidate.SourceLayer, SelectedGeometryKind = candidate.GeometryKind, SelectedSegmentIndex = candidate.SegmentIndex, Candidates = new List<SpatialGeometryCandidate> { candidate } };
        }
        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).ToList();
        private static string Ids(PlotSpatialMeasurementModel model) => string.Join("|", model.EquipmentPairMeasurements.Select(x => x.MeasurementId)) + ";" + string.Join("|", model.EquipmentSpatialMeasurements.Select(x => x.MeasurementId));
        private static string QaIdentity(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static void Throws(Action action, string message) { bool thrown = false; try { action(); } catch (InvalidOperationException) { thrown = true; } Assert(thrown, message); }
        private static void ThrowsDrawingUnit(Action action, string expectedMessage, string message) { try { action(); } catch (DrawingUnitResolutionException exception) { Assert(exception.Message == expectedMessage, message + " Actual: " + exception.Message); return; } throw new InvalidOperationException(message); }
        private static void ThrowsArgumentNull(Action action, string message) { try { action(); } catch (ArgumentNullException) { return; } throw new InvalidOperationException(message); }
        private static void AssertAlmost(double actual, double expected, string message) { Assert(Math.Abs(actual - expected) < 0.0000000001d, message + " Actual: " + actual); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
