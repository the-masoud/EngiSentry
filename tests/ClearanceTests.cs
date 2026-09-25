using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Semantics;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class ClearanceTests
    {
        internal static void Run()
        {
            RunDistanceCoverageTests();
            RunDrawingUnitConflictGate();
            RunP102C1Presentation();
            List<KnownQaRule> catalog = Catalog();
            ProfileResolution defaults = ProfileResolver.Resolve(catalog, new EngineeringStandardProfile[0]);
            Assert(defaults.EffectiveRuleSettings.Count == 17 && defaults.EffectiveRuleSettings.Count(x => x.Enabled) == 15 && !Setting(defaults, "PP-CLEAR-001").Enabled && !Setting(defaults, "PP-CLEAR-002").Enabled, "M5.0 default rule enablement changed.");
            Assert(Setting(defaults, "PP-CLEAR-001").EffectiveSeverity == "Warning" && Setting(defaults, "PP-CLEAR-002").EffectiveSeverity == "Warning", "M5.0 default clearance severity changed.");

            Invalid(catalog, Profile("invalid-normal", "Company", new RuleSetting { RuleId = "ENGI-EQ-001", MinimumDistanceDrawingUnits = 1m }), "only for clearance");
            Invalid(catalog, Profile("invalid-zero", "Company", new RuleSetting { RuleId = "PP-CLEAR-001", MinimumDistanceDrawingUnits = 0m }), "greater than zero");
            Invalid(catalog, Profile("invalid-negative", "Company", new RuleSetting { RuleId = "PP-CLEAR-001", MinimumDistanceDrawingUnits = -1m }), "greater than zero");
            Invalid(catalog, Profile("invalid-enabled-no-threshold", "Company", new RuleSetting { RuleId = "PP-CLEAR-001", Enabled = true }), "requires MinimumDistanceDrawingUnits");

            ProfileResolution layered = ProfileResolver.Resolve(catalog, new[]
            {
                Profile("company-threshold", "Company", new RuleSetting { RuleId = "PP-CLEAR-001", Enabled = true, MinimumDistanceDrawingUnits = 100m }),
                Profile("client-threshold", "Client", new RuleSetting { RuleId = "PP-CLEAR-001", MinimumDistanceDrawingUnits = 200m }),
                Profile("project-threshold", "Project", new RuleSetting { RuleId = "PP-CLEAR-001", MinimumDistanceDrawingUnits = 300m })
            });
            Assert(Setting(layered, "PP-CLEAR-001").EffectiveMinimumDistanceDrawingUnits == 300m && Setting(layered, "PP-CLEAR-001").MinimumDistanceSourceProfileId == "project-threshold" && Setting(layered, "PP-CLEAR-001").EnabledSourceProfileId == "company-threshold", "Threshold inheritance or provenance failed.");

            PlotPlanModel plot = new PlotPlanModel
            {
                EquipmentPlacements = new List<EquipmentPlacement> { Placement("placement:A", "TAG-A", "TAG-H-A"), Placement("placement:B", "TAG-B", "TAG-H-B") },
                Measurements = new PlotSpatialMeasurementModel
                {
                    EquipmentSpatialMeasurements = new List<EquipmentSpatialMeasurement>
                    {
                        Measurement("m-boundary-z", "placement:A", "Boundary", 5m, "LB-A", "GB-A"),
                        Measurement("m-boundary-a", "placement:A", "Boundary", 5m, "LB-A2", "GB-A2"),
                        Measurement("m-boundary-b", "placement:B", "Boundary", 6m, "LB-B", "GB-B"),
                        Measurement("m-road", "placement:A", "Road", 4m, "LR-A", "GR-A"),
                        Measurement("m-road-b", "placement:B", "Road", 7m, "LR-B", "GR-B")
                    }
                }
            };
            EngineeringEntityIndex index = new EngineeringEntityIndex();
            ProfileResolution configured = ProfileResolver.Resolve(catalog, new[] { Profile("clearance", "Company", new RuleSetting { RuleId = "PP-CLEAR-001", Enabled = true, MinimumDistanceDrawingUnits = 6m }, new RuleSetting { RuleId = "PP-CLEAR-002", Enabled = true, MinimumDistanceDrawingUnits = 5m }) });
            QaReport report = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plot, ProfilePlotPlanQaRules.Create(configured), configured);
            QaRuleResult boundaryResult = report.RuleResults.Single(x => x.RuleId == "PP-CLEAR-001");
            QaRuleResult roadResult = report.RuleResults.Single(x => x.RuleId == "PP-CLEAR-002");
            Assert(boundaryResult.EvaluatedSubjectCount == 2 && boundaryResult.FindingCount == 1 && roadResult.EvaluatedSubjectCount == 2 && roadResult.FindingCount == 1 && report.RuleResults.Count == 17, "Clearance filtering, nearest selection, or one-finding-per-placement failed.");
            QaFinding boundaryFinding = report.Findings.Single(x => x.RuleId == "PP-CLEAR-001");
            Assert(boundaryFinding.FindingId == "PP-CLEAR-001:placement:A" && boundaryFinding.Evidence.ClearanceEvidence != null && boundaryFinding.Evidence.ClearanceEvidence.MeasurementId == "m-boundary-a" && boundaryFinding.Evidence.ClearanceEvidence.MinimumDistanceDrawingUnits == 6m && boundaryFinding.SourceHandles.SequenceEqual(new[] { "GB-A2", "LB-A2", "TAG-H-A" }), "Clearance evidence, ordinal tie-break, or source handles failed.");
            QaFinding roadFinding = report.Findings.Single(x => x.RuleId == "PP-CLEAR-002");
            Assert(roadFinding.FindingId == "PP-CLEAR-002:placement:A" && roadFinding.Evidence.ClearanceEvidence!.ActualDistance2D == 4d, "Road clearance finding identity or measurement failed.");

            ProfileResolution disabled = ProfileResolver.Resolve(catalog, new EngineeringStandardProfile[0]);
            QaReport disabledReport = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plot, ProfilePlotPlanQaRules.Create(disabled), disabled);
            Assert(!disabledReport.RuleResults.Any(x => x.RuleId == "PP-CLEAR-001" || x.RuleId == "PP-CLEAR-002") && disabledReport.RuleResults.Count == 15, "Disabled clearance rules were evaluated.");
            Assert(QaIdentity(QaEngine.Evaluate(index, BuiltInQaRules.All, plot, BuiltInPlotPlanQaRules.All)) == QaIdentity(QaEngine.Evaluate(index, BuiltInQaRules.All, new PlotPlanModel { Geometry = plot.Geometry }, BuiltInPlotPlanQaRules.All)), "Unprofiled QA behavior changed by M5.0 measurement inputs.");

            EngineeringDistanceRuleRegistry emptyDistanceRules = EngineeringDistanceRuleRegistry.Create(Standards(), new EngineeringDistanceRuleDefinition[0]);
            Assert(emptyDistanceRules.Rules.Count == 0, "Empty engineering distance rule registry was not valid.");

            var inputRoad = DistanceRule("std.road", "EquipmentToRoad2D", 12.5m);
            var inputBoundary = DistanceRule("std.boundary", "EquipmentToBoundary2D", 7.25m);
            EngineeringDistanceRuleRegistry distanceRules = EngineeringDistanceRuleRegistry.Create(Standards("std.boundary", "std.road"), new[] { inputRoad, inputBoundary });
            Assert(distanceRules.Rules.Select(x => x.RuleId).SequenceEqual(new[] { "std.boundary", "std.road" }) && distanceRules.Get("std.boundary").MeasurementKind == "EquipmentToBoundary2D" && distanceRules.Get("std.boundary").MinimumDistanceMeters == 7.25m && distanceRules.Get("std.road").MeasurementKind == "EquipmentToRoad2D" && distanceRules.Get("std.road").MinimumDistanceMeters == 12.5m, "Engineering distance rule ordering or values were not deterministic.");
            EngineeringDistanceRuleRegistry equivalentDistanceRules = EngineeringDistanceRuleRegistry.Create(Standards("std.road", "std.boundary"), new[] { DistanceRule("std.boundary", "EquipmentToBoundary2D", 7.25m), DistanceRule("std.road", "EquipmentToRoad2D", 12.5m) });
            Assert(string.Join("|", distanceRules.Rules.Select(x => x.RuleId + ":" + x.MeasurementKind + ":" + x.MinimumDistanceMeters)) == string.Join("|", equivalentDistanceRules.Rules.Select(x => x.RuleId + ":" + x.MeasurementKind + ":" + x.MinimumDistanceMeters)), "Engineering distance registry changed with input order.");

            Assert(EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 1m) }).Rules.Count == 1, "Exact effective P3 RuleId link was rejected.");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("STD.ROAD", "EquipmentToRoad2D", 1m) }), "engineering distance rule references unknown effective RuleId: STD.ROAD");
            Assert(EngineeringDistanceRuleRegistry.Create(Standards("std.road", "std.boundary"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 1m), DistanceRule("std.boundary", "EquipmentToBoundary2D", 1m) }).Rules.Count == 2, "Supported measurement kinds were rejected.");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "", 1m) }), "engineering distance rule MeasurementKind is required: std.road");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "equipmenttoroad2d", 1m) }), "unsupported engineering distance MeasurementKind: std.road | equipmenttoroad2d");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentPair2D", 1m) }), "unsupported engineering distance MeasurementKind: std.road | EquipmentPair2D");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 0m) }), "engineering distance rule MinimumDistanceMeters must be greater than zero: std.road");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", -1m) }), "engineering distance rule MinimumDistanceMeters must be greater than zero: std.road");

            ThrowsArgumentNull(() => EngineeringDistanceRuleRegistry.Create(null!, new EngineeringDistanceRuleDefinition[0]), "Null standards resolution was not rejected.");
            ThrowsArgumentNull(() => EngineeringDistanceRuleRegistry.Create(Standards(), null!), "Null distance-rule enumerable was not rejected.");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(new StandardRulePackResolution { EffectiveRules = null! }, new EngineeringDistanceRuleDefinition[0]), "engineering distance rule standards EffectiveRules are required");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { null! } }, new EngineeringDistanceRuleDefinition[0]), "engineering distance rule standards contains a null effective rule");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards(" "), new EngineeringDistanceRuleDefinition[0]), "engineering distance rule standards RuleId is required");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road", "std.road"), new EngineeringDistanceRuleDefinition[0]), "duplicate engineering distance rule standards RuleId: std.road");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new EngineeringDistanceRuleDefinition[] { null! }), "engineering distance rule registry contains a null rule");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule(" ", "EquipmentToRoad2D", 1m) }), "engineering distance rule RuleId is required");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("missing.rule", "EquipmentToRoad2D", 1m) }), "engineering distance rule references unknown effective RuleId: missing.rule");
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 1m), DistanceRule("std.road", "EquipmentToRoad2D", 2m) }), "duplicate engineering distance rule RuleId: std.road");

            EngineeringDistanceRuleDefinition? lookup;
            Assert(distanceRules.TryGet("std.road", out lookup) && lookup != null && lookup.RuleId == "std.road", "Exact distance-rule lookup failed.");
            Assert(!distanceRules.TryGet(null!, out lookup) && lookup == null && !distanceRules.TryGet("STD.ROAD", out lookup) && lookup == null && !distanceRules.TryGet("", out lookup) && lookup == null && !distanceRules.TryGet("   ", out lookup) && lookup == null, "Invalid distance-rule lookup did not fail closed.");
            ThrowsDistance(() => distanceRules.Get("missing"), "engineering distance rule was not found: missing");
            ThrowsDistance(() => distanceRules.Get(null!), "engineering distance rule was not found: ");

            inputRoad.RuleId = "mutated.rule";
            inputRoad.MeasurementKind = "EquipmentPair2D";
            inputRoad.MinimumDistanceMeters = 999m;
            Assert(distanceRules.Get("std.road").MeasurementKind == "EquipmentToRoad2D" && distanceRules.Get("std.road").MinimumDistanceMeters == 12.5m, "Input rule mutation affected registry state.");
            EngineeringDistanceRuleDefinition rulesCopy = distanceRules.Rules.Single(x => x.RuleId == "std.road");
            rulesCopy.MinimumDistanceMeters = 999m;
            Assert(distanceRules.TryGet("std.road", out lookup) && lookup != null, "Distance-rule lookup unexpectedly failed.");
            lookup!.MeasurementKind = "EquipmentPair2D";
            EngineeringDistanceRuleDefinition getCopy = distanceRules.Get("std.road");
            getCopy.RuleId = "mutated.get";
            Assert(distanceRules.Get("std.road").MeasurementKind == "EquipmentToRoad2D" && distanceRules.Get("std.road").MinimumDistanceMeters == 12.5m && distanceRules.Rules.Single(x => x.RuleId == "std.road").RuleId == "std.road", "Returned rule mutation affected registry state.");

            EngineeringDistanceRuleRegistry roadOneMetre = EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 1m) });
            EngineeringDistanceRuleEvaluation millimetreEvaluation = EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Millimeters", EvaluationMeasurement("m-500", "p-500", "Road", 500d), EvaluationMeasurement("m-1000", "p-1000", "Road", 1000d), EvaluationMeasurement("m-1500", "p-1500", "Road", 1500d))).Single();
            Assert(millimetreEvaluation.MinimumDistanceDrawingUnits == 1000d && millimetreEvaluation.EvaluatedSubjectCount == 3 && millimetreEvaluation.ViolationCount == 1 && millimetreEvaluation.Status == "VIOLATIONS" && millimetreEvaluation.Violations.Single().MeasurementId == "m-500" && millimetreEvaluation.Violations.Single().ActualDistanceMeters == 0.5d, "Millimetre distance evaluation was not unit-safe.");

            EngineeringDistanceRuleRegistry roadTenMetres = EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 10m) });
            EngineeringDistanceRuleEvaluation metreEvaluation = EngineeringDistanceRuleEvaluator.Evaluate(roadTenMetres, EvaluationPlot("Meters", EvaluationMeasurement("m-9", "p-9", "Road", 9d), EvaluationMeasurement("m-10", "p-10", "Road", 10d), EvaluationMeasurement("m-11", "p-11", "Road", 11d))).Single();
            Assert(metreEvaluation.MinimumDistanceDrawingUnits == 10d && metreEvaluation.ViolationCount == 1 && metreEvaluation.Violations.Single().MeasurementId == "m-9", "Metre minimum-distance equality or comparison failed.");

            EngineeringDistanceRuleEvaluation feetEvaluation = EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 3.048m) }), EvaluationPlot("Feet", EvaluationMeasurement("m-feet", "p-feet", "Road", 5d))).Single();
            Assert(feetEvaluation.MinimumDistanceDrawingUnits == 10d && feetEvaluation.Violations.Single().ActualDistanceMeters == 1.524d && feetEvaluation.Status == "VIOLATIONS", "Imperial drawing-unit conversion failed.");

            EngineeringDistanceRuleRegistry mixedRules = EngineeringDistanceRuleRegistry.Create(Standards("std.road", "std.boundary"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 5m), DistanceRule("std.boundary", "EquipmentToBoundary2D", 4m) });
            List<EngineeringDistanceRuleEvaluation> isolated = EngineeringDistanceRuleEvaluator.Evaluate(mixedRules, EvaluationPlot("Meters", EvaluationMeasurement("m-road", "p-road", "Road", 4d), EvaluationMeasurement("m-boundary", "p-boundary", "Boundary", 3d))).ToList();
            Assert(isolated.Single(x => x.RuleId == "std.road").Violations.Single().MeasurementId == "m-road" && isolated.Single(x => x.RuleId == "std.boundary").Violations.Single().MeasurementId == "m-boundary", "Road and Boundary measurements were not isolated.");

            EngineeringDistanceRuleEvaluation nearest = EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 5m) }), EvaluationPlot("Meters", EvaluationMeasurement("m-z", "p", "Road", 5d), EvaluationMeasurement("m-a", "p", "Road", 5d), EvaluationMeasurement("m-near", "p", "Road", 4d))).Single();
            Assert(nearest.EvaluatedSubjectCount == 1 && nearest.Violations.Single().MeasurementId == "m-near", "Nearest measurement did not govern a placement.");
            EngineeringDistanceRuleEvaluation tiedNearest = EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 6m) }), EvaluationPlot("Meters", EvaluationMeasurement("m-z", "p", "Road", 5d), EvaluationMeasurement("m-a", "p", "Road", 5d))).Single();
            Assert(tiedNearest.EvaluatedSubjectCount == 1 && tiedNearest.Violations.Single().MeasurementId == "m-a", "Nearest measurement tie-break was not ordinal.");

            EngineeringDistanceRuleEvaluation insufficient = EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m-boundary-only", "p", "Boundary", 0d))).Single();
            Assert(insufficient.EvaluatedSubjectCount == 0 && insufficient.ViolationCount == 0 && insufficient.Violations.Count == 0 && insufficient.Status == "INSUFFICIENT_EVIDENCE", "Missing measurement evidence incorrectly passed.");
            EngineeringDistanceRuleEvaluation pass = EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m-pass", "p", "Road", 1d))).Single();
            Assert(pass.Status == "PASS" && pass.ViolationCount == 0, "Compliant measurement did not pass.");

            EngineeringDistanceRuleRegistry qualifiedRoad = EngineeringDistanceRuleRegistry.Create(Standards("std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 5m, "AccessRoadEdge", "ProcessingEquipment") });
            EngineeringDistanceRuleEvaluation exactQualified = EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("qualified", "p-qualified", "Road", 4d, "AccessRoadEdge", "ProcessingEquipment"))).Single();
            Assert(exactQualified.Status == "VIOLATIONS" && exactQualified.EvaluatedSubjectCount == 1 && exactQualified.Violations.Single().MeasurementBasisKey == "AccessRoadEdge" && exactQualified.Violations.Single().ApplicabilityKey == "ProcessingEquipment", "Exact Road basis/applicability evidence was not evaluated.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("generic", "p", "Road", 1d, null, null))).Single().Status == "INSUFFICIENT_EVIDENCE", "Generic Road evidence was evaluated without an engineering basis.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("missing-applicability", "p", "Road", 1d, "AccessRoadEdge", null))).Single().Status == "INSUFFICIENT_EVIDENCE", "Road evidence without applicability was evaluated.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("missing-basis", "p", "Road", 1d, null, "ProcessingEquipment"))).Single().Status == "INSUFFICIENT_EVIDENCE", "Road evidence without a basis was evaluated.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("other-road-basis", "p", "Road", 1d, "UnitPeripheralRoadEdge", "ProcessingEquipment"))).Single().Status == "INSUFFICIENT_EVIDENCE", "Access Road rule consumed Unit Peripheral Road evidence.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("other-applicability", "p", "Road", 1d, "AccessRoadEdge", "Equipment"))).Single().Status == "INSUFFICIENT_EVIDENCE", "Processing Equipment rule consumed generic Equipment evidence.");
            EngineeringDistanceRuleRegistry securityFence = EngineeringDistanceRuleRegistry.Create(Standards("std.boundary"), new[] { DistanceRule("std.boundary", "EquipmentToBoundary2D", 5m, "SecurityFence", "ProcessingEquipment") });
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(securityFence, EvaluationPlot("Meters", EvaluationMeasurement("site-boundary", "p", "Boundary", 1d, "SiteBoundary", "ProcessingEquipment"))).Single().Status == "INSUFFICIENT_EVIDENCE", "Security Fence rule consumed Site Boundary evidence.");
            EngineeringDistanceRuleEvaluation qualifiedEquality = EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("qualified-equal", "p", "Road", 5d, "AccessRoadEdge", "ProcessingEquipment"))).Single();
            Assert(qualifiedEquality.Status == "PASS" && qualifiedEquality.ViolationCount == 0, "Qualified equality did not pass.");
            EngineeringDistanceRuleEvaluation qualifiedNearest = EngineeringDistanceRuleEvaluator.Evaluate(qualifiedRoad, EvaluationPlot("Meters", EvaluationMeasurement("unqualified-near", "p", "Road", 1d, "UnitPeripheralRoadEdge", "ProcessingEquipment"), EvaluationMeasurement("qualified-near", "p", "Road", 4d, "AccessRoadEdge", "ProcessingEquipment"))).Single();
            Assert(qualifiedNearest.EvaluatedSubjectCount == 1 && qualifiedNearest.Violations.Single().MeasurementId == "qualified-near", "Nearest selection used unqualified evidence.");

            EquipmentPlacement propagatedPlacement = Placement("propagated", "PROP", "handle:propagated");
            propagatedPlacement.EngineeringApplicabilityKey = "ProcessingEquipment";
            SpatialCoordinateConstraint propagatedConstraint = new SpatialCoordinateConstraint { ConstraintId = "constraint:propagated", SubjectKind = "Road", Axis = "E", Status = "Resolved", SelectedGeometryHandle = "geometry:propagated", SelectedGeometryLayer = "layer", SelectedGeometryKind = "Line", SelectedSegmentIndex = 0, EngineeringMeasurementBasisKey = "AccessRoadEdge", Candidates = new List<SpatialGeometryCandidate> { new SpatialGeometryCandidate { SourceHandle = "geometry:propagated", SourceLayer = "layer", GeometryKind = "Line", SegmentIndex = 0, SegmentStartX = 0d, SegmentStartY = 0d, SegmentEndX = 10d, SegmentEndY = 0d } } };
            EquipmentSpatialMeasurement propagatedMeasurement = PlotSpatialMeasurementBuilder.Build(new[] { propagatedPlacement }, new[] { propagatedConstraint }).EquipmentSpatialMeasurements.Single();
            Assert(propagatedMeasurement.EngineeringMeasurementBasisKey == "AccessRoadEdge" && propagatedMeasurement.EngineeringApplicabilityKey == "ProcessingEquipment", "Spatial measurement did not propagate explicit engineering keys.");

            ThrowsArgumentNull(() => EngineeringDistanceRuleEvaluator.Evaluate(null!, EvaluationPlot("Meters")), "Null distance-rule registry was not rejected.");
            ThrowsArgumentNull(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, null!), "Null plot plan was not rejected.");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, new PlotPlanModel { Drawing = null! }), "engineering distance evaluation Drawing is required");
            ThrowsDrawingUnit(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot(null!)), "drawing insertion units are required");
            ThrowsDrawingUnit(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot(" ")), "drawing insertion units are required");
            ThrowsDrawingUnit(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Undefined")), "unsupported drawing insertion units: Undefined");
            ThrowsDrawingUnit(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("meters")), "unsupported drawing insertion units: meters");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, new PlotPlanModel { Drawing = new SemanticDrawing { InsertionUnits = "Meters" }, Measurements = null! }), "engineering distance evaluation Measurements are required");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, new PlotPlanModel { Drawing = new SemanticDrawing { InsertionUnits = "Meters" }, Measurements = new PlotSpatialMeasurementModel { EquipmentSpatialMeasurements = null! } }), "engineering distance evaluation EquipmentSpatialMeasurements are required");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", new EquipmentSpatialMeasurement[] { null! })), "engineering distance evaluation contains a null spatial measurement");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement(" ", "p", "Road", 1d))), "engineering distance evaluation MeasurementId is required");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m1", " ", "Road", 1d))), "engineering distance evaluation PlacementId is required: m1");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m1", "p", " ", 1d))), "engineering distance evaluation SubjectKind is required: m1");
            foreach (double invalidDistance in new[] { double.NaN, double.PositiveInfinity, -1d }) ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m1", "p", "Road", invalidDistance))), "engineering distance evaluation Distance2D must be finite and non-negative: m1");
            ThrowsEvaluation(() => EngineeringDistanceRuleEvaluator.Evaluate(roadOneMetre, EvaluationPlot("Meters", EvaluationMeasurement("m1", "p1", "Road", 1d), EvaluationMeasurement("m1", "p2", "Road", 2d))), "duplicate engineering distance evaluation MeasurementId: m1");

            List<EquipmentSpatialMeasurement> deterministicMeasurements = new List<EquipmentSpatialMeasurement> { EvaluationMeasurement("m-z", "p-z", "Road", 2d), EvaluationMeasurement("m-a", "p-a", "Road", 1d), EvaluationMeasurement("m-boundary", "p-b", "Boundary", 1d) };
            EngineeringDistanceRuleRegistry deterministicRules = EngineeringDistanceRuleRegistry.Create(Standards("std.road", "std.boundary"), new[] { DistanceRule("std.boundary", "EquipmentToBoundary2D", 2m), DistanceRule("std.road", "EquipmentToRoad2D", 2m) });
            string deterministicIdentity = EvaluationIdentity(EngineeringDistanceRuleEvaluator.Evaluate(deterministicRules, EvaluationPlot("Meters", deterministicMeasurements.ToArray())));
            string reversedDeterministicIdentity = EvaluationIdentity(EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(Standards("std.boundary", "std.road"), new[] { DistanceRule("std.road", "EquipmentToRoad2D", 2m), DistanceRule("std.boundary", "EquipmentToBoundary2D", 2m) }), EvaluationPlot("Meters", deterministicMeasurements.AsEnumerable().Reverse().ToArray())));
            Assert(deterministicIdentity == reversedDeterministicIdentity, "Distance evaluation changed with equivalent input order.");
            List<string> beforeIds = deterministicMeasurements.Select(x => x.MeasurementId).ToList();
            List<double> beforeDistances = deterministicMeasurements.Select(x => x.Distance2D).ToList();
            PlotPlanModel immutablePlot = EvaluationPlot("Meters", deterministicMeasurements.ToArray());
            EngineeringDistanceRuleEvaluator.Evaluate(deterministicRules, immutablePlot);
            Assert(immutablePlot.Drawing!.InsertionUnits == "Meters" && immutablePlot.Measurements.EquipmentSpatialMeasurements.Select(x => x.MeasurementId).SequenceEqual(beforeIds) && immutablePlot.Measurements.EquipmentSpatialMeasurements.Select(x => x.Distance2D).SequenceEqual(beforeDistances), "Distance evaluation mutated the supplied Plot Plan model.");

            StandardSourceRegistry qaSources = QaSources();
            StandardClauseRegistry qaClauses = QaClauses(qaSources, "std.road", "std.boundary", "std.pass", "std.insufficient");
            QaReport roadQa = EngineeringDistanceQaIntegrator.Create(new[] { millimetreEvaluation }, QaStandards("std.road"), qaClauses, qaSources);
            QaRuleResult roadQaRule = roadQa.RuleResults.Single();
            QaFinding roadQaFinding = roadQa.Findings.Single();
            EngineeringDistanceEvidence roadEvidence = roadQaFinding.Evidence.EngineeringDistanceEvidence!;
            Assert(roadQaRule.RuleId == "std.road" && roadQaRule.Severity == "Warning" && roadQaRule.EvaluatedSubjectCount == 3 && roadQaRule.FindingCount == 1 && roadQaRule.Status == "FINDINGS" && roadQaFinding.FindingId == "std.road:p-500" && roadQaFinding.RuleId == "std.road" && roadQaFinding.Severity == "Warning" && roadQaFinding.Category == "EngineeringDistance" && roadQaFinding.SubjectType == "EquipmentPlacement" && roadQaFinding.SubjectId == "p-500" && roadQaFinding.Message == "Equipment placement is closer to Road geometry than the standards-backed minimum distance.", "Road engineering-distance QA projection failed.");
            Assert(roadEvidence.MinimumDistanceMeters == 1m && roadEvidence.MinimumDistanceDrawingUnits == 1000d && roadEvidence.ActualDistanceDrawingUnits == 500d && roadEvidence.ActualDistanceMeters == 0.5d && roadEvidence.InsertionUnits == "Millimeters" && roadEvidence.CanonicalUnit == "mm" && roadEvidence.MetersPerDrawingUnit == 0.001d && roadEvidence.MeasurementId == "m-500" && roadEvidence.PlacementId == "p-500" && roadEvidence.GeometrySourceHandle == "geometry:m-500", "Unit-safe structured engineering-distance evidence failed.");
            Assert(roadEvidence.RequiredMeasurementBasisKey == "AccessRoadEdge" && roadEvidence.ActualMeasurementBasisKey == "AccessRoadEdge" && roadEvidence.RequiredApplicabilityKey == "ProcessingEquipment" && roadEvidence.ActualApplicabilityKey == "ProcessingEquipment", "Engineering-distance QA evidence did not preserve exact basis/applicability keys.");
            StandardFindingProvenance roadProvenance = roadQaFinding.StandardsProvenance!;
            StandardClauseCitation roadCitation = roadProvenance.Clauses.Single();
            Assert(roadProvenance.RuleId == "std.road" && roadProvenance.RuleTitle == "Title std.road" && roadProvenance.WinningPackId == "pack.test" && roadProvenance.WinningPrecedence == 1 && roadProvenance.OverriddenPackIds.SequenceEqual(new[] { "pack.previous" }) && roadCitation.ClauseId == "clause.std.road" && roadCitation.SourceId == "source.test" && roadCitation.ClauseReference == "1" && roadCitation.ClauseText == "Text std.road" && roadCitation.Heading == "Heading" && roadCitation.PageNumber == 1 && roadCitation.DocumentNumber == "DOC-TEST" && roadCitation.SourceTitle == "Synthetic Source" && roadCitation.Issuer == "Test Issuer" && roadCitation.RevisionOrEdition == "R1" && roadCitation.SourceFileName == "synthetic.pdf" && roadCitation.SourceSha256 == new string('A', 64), "P3 standards provenance was not attached exactly.");

            EngineeringDistanceRuleEvaluation boundaryEvaluation = EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(Standards("std.boundary"), new[] { DistanceRule("std.boundary", "EquipmentToBoundary2D", 1m) }), EvaluationPlot("Meters", EvaluationMeasurement("m-boundary", "p-boundary", "Boundary", 0.5d))).Single();
            QaFinding boundaryQaFinding = EngineeringDistanceQaIntegrator.Create(new[] { boundaryEvaluation }, QaStandards("std.boundary"), qaClauses, qaSources).Findings.Single();
            Assert(boundaryQaFinding.Message == "Equipment placement is closer to Boundary geometry than the standards-backed minimum distance." && boundaryQaFinding.Category == "EngineeringDistance" && boundaryQaFinding.Evidence.EngineeringDistanceEvidence!.SubjectKind == "Boundary" && boundaryQaFinding.StandardsProvenance != null, "Boundary engineering-distance QA projection failed.");

            QaReport passQa = EngineeringDistanceQaIntegrator.Create(new[] { pass }, QaStandards("std.road"), qaClauses, qaSources);
            QaReport insufficientQa = EngineeringDistanceQaIntegrator.Create(new[] { insufficient }, QaStandards("std.road"), qaClauses, qaSources);
            Assert(passQa.RuleResults.Single().Status == "PASS" && passQa.Findings.Count == 0 && passQa.OverallStatus == "PASS" && insufficientQa.RuleResults.Single().Status == "INSUFFICIENT_EVIDENCE" && insufficientQa.Findings.Count == 0 && insufficientQa.OverallStatus == "INSUFFICIENT_EVIDENCE", "PASS or INSUFFICIENT_EVIDENCE QA projection failed.");

            EngineeringDistanceRuleEvaluation mixedPass = Evaluation("std.pass", "EquipmentToRoad2D", "PASS", 1, 0);
            EngineeringDistanceRuleEvaluation mixedInsufficient = Evaluation("std.insufficient", "EquipmentToBoundary2D", "INSUFFICIENT_EVIDENCE", 0, 0);
            QaReport mixedQa = EngineeringDistanceQaIntegrator.Create(new[] { mixedInsufficient, mixedPass, boundaryEvaluation }, QaStandards("std.boundary", "std.pass", "std.insufficient"), qaClauses, qaSources);
            Assert(mixedQa.RuleResults.Select(x => x.RuleId).SequenceEqual(new[] { "std.boundary", "std.insufficient", "std.pass" }) && mixedQa.Findings.Count == 1 && mixedQa.OverallStatus == "REVIEW_REQUIRED" && mixedQa.Statistics.RuleCount == 3 && mixedQa.Statistics.RulesPassed == 1 && mixedQa.Statistics.RulesWithFindings == 1 && mixedQa.Statistics.RulesInsufficientEvidence == 1 && mixedQa.Statistics.TotalFindings == 1 && mixedQa.Statistics.WarningFindings == 1 && mixedQa.Statistics.InfoFindings == 0 && mixedQa.Statistics.ErrorFindings == 0, "Mixed engineering-distance QA report statistics failed.");
            QaReport emptyQa = EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[0], QaStandards(), qaClauses, qaSources);
            Assert(emptyQa.RuleResults.Count == 0 && emptyQa.Findings.Count == 0 && emptyQa.OverallStatus == "PASS" && emptyQa.Statistics.RuleCount == 0 && emptyQa.Statistics.TotalFindings == 0, "Empty engineering-distance QA report failed.");

            string qaIdentity = QaDistanceIdentity(EngineeringDistanceQaIntegrator.Create(new[] { boundaryEvaluation, mixedPass }, QaStandards("std.boundary", "std.pass"), qaClauses, qaSources));
            string reversedQaIdentity = QaDistanceIdentity(EngineeringDistanceQaIntegrator.Create(new[] { mixedPass, boundaryEvaluation }, QaStandards("std.pass", "std.boundary"), qaClauses, qaSources));
            Assert(qaIdentity == reversedQaIdentity, "Engineering-distance QA projection was not deterministic.");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("STD.ROAD", "EquipmentToRoad2D", "PASS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA references unknown effective RuleId: STD.ROAD");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[] { null! }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA contains a null evaluation");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation(" ", "EquipmentToRoad2D", "PASS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA RuleId is required");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", 1, 0), Evaluation("std.road", "EquipmentToRoad2D", "PASS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "duplicate engineering distance QA RuleId: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "", "PASS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA MeasurementKind is required: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentPair2D", "PASS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "unsupported engineering distance QA MeasurementKind: std.road | EquipmentPair2D");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { new EngineeringDistanceRuleEvaluation { RuleId = "std.road", MeasurementKind = "EquipmentToRoad2D", Violations = null!, EvaluatedSubjectCount = 1, ViolationCount = 0, Status = "PASS" } }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA Violations are required: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", -1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA EvaluatedSubjectCount is invalid: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", 1, -1) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA ViolationCount is invalid: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", 1, 1) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA ViolationCount is inconsistent: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", 0, 1, Violation("m1", "p1", "Road")) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA violation count exceeds evaluated subjects: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "OTHER", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "unsupported engineering distance QA status: std.road | OTHER");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "PASS", 0, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA PASS state is inconsistent: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA VIOLATIONS state is inconsistent: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "INSUFFICIENT_EVIDENCE", 1, 0) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA INSUFFICIENT_EVIDENCE state is inconsistent: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, new EngineeringDistanceViolation[] { null! }) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA contains a null violation: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation(" ", "p", "Road")) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA violation MeasurementId is required: std.road");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("m", " ", "Road")) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA violation PlacementId is required: std.road | m");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 2, 2, Violation("m1", "p", "Road"), Violation("m2", "p", "Road")) }, QaStandards("std.road"), qaClauses, qaSources), "duplicate engineering distance QA violation PlacementId: std.road | p");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("m", "p", "Boundary")) }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA SubjectKind is inconsistent: std.road | m");
            EngineeringDistanceRuleEvaluation forgedBasis = Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("m", "p", "Road"));
            forgedBasis.Violations.Single().MeasurementBasisKey = "UnitPeripheralRoadEdge";
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { forgedBasis }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA MeasurementBasisKey is inconsistent: std.road | m");
            EngineeringDistanceRuleEvaluation forgedApplicability = Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("m", "p", "Road"));
            forgedApplicability.Violations.Single().ApplicabilityKey = "Equipment";
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new[] { forgedApplicability }, QaStandards("std.road"), qaClauses, qaSources), "engineering distance QA ApplicabilityKey is inconsistent: std.road | m");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[0], new StandardRulePackResolution { EffectiveRules = null! }, qaClauses, qaSources), "engineering distance QA standards EffectiveRules are required");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[0], new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { null! } }, qaClauses, qaSources), "engineering distance QA standards contains a null effective rule");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[0], QaStandards(" "), qaClauses, qaSources), "engineering distance QA standards RuleId is required");
            ThrowsIntegration(() => EngineeringDistanceQaIntegrator.Create(new EngineeringDistanceRuleEvaluation[0], QaStandards("std.road", "std.road"), qaClauses, qaSources), "duplicate engineering distance QA standards RuleId: std.road");

            StandardSourceDefinition[] p46SourceInputs = P46Sources();
            StandardClauseDefinition[] p46ClauseInputs = P46Clauses();
            StandardRulePackDefinition[] p46PackInputs = P46Packs();
            StandardSourceRegistry p46Sources = StandardSourceRegistry.Create(p46SourceInputs);
            StandardClauseRegistry p46Clauses = StandardClauseRegistry.Create(p46Sources, p46ClauseInputs);
            StandardRulePackRegistry p46Packs = StandardRulePackRegistry.Create(p46Clauses, p46PackInputs);
            StandardRulePackResolution p46Resolution = StandardRulePackResolver.Resolve(p46Packs, new[] { new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.access-road-clearance", Precedence = 10 }, new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.security-fence-clearance", Precedence = 10 } });
            EngineeringDistanceRuleDefinition[] p46RuleInputs = P46Rules();
            EngineeringDistanceRuleRegistry p46Rules = EngineeringDistanceRuleRegistry.Create(p46Resolution, p46RuleInputs);
            const string accessRoadRuleId = "std.plotplan.processing-equipment.access-road-edge.minimum-clearance";
            const string securityFenceRuleId = "std.plotplan.equipment.security-fence.minimum-clearance";
            Assert(p46Resolution.EffectiveRules.Select(x => x.RuleId).SequenceEqual(new[] { securityFenceRuleId, accessRoadRuleId }) && p46Resolution.EffectiveRules.All(x => x.WinningPrecedence == 10 && x.OverriddenPackIds.Count == 0), "P4.6 candidate rule packs incorrectly asserted precedence between standards.");

            EngineeringDistanceRuleEvaluation accessViolation = EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-2.5", "p46-road", "Road", 2.5d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId);
            Assert(accessViolation.MinimumDistanceMeters == 3m && accessViolation.EvaluatedSubjectCount == 1 && accessViolation.ViolationCount == 1 && accessViolation.Status == "VIOLATIONS" && accessViolation.Violations.Single().ActualDistanceMeters == 2.5d && accessViolation.Violations.Single().MeasurementBasisKey == "AccessRoadEdge" && accessViolation.Violations.Single().ApplicabilityKey == "ProcessingEquipment", "P4.6 3m Access Road candidate did not evaluate exactly.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-3", "p46-road", "Road", 3d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId).Status == "PASS", "P4.6 3m equality was a violation.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-3.5", "p46-road", "Road", 3.5d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId).Status == "PASS", "P4.6 3m above-limit evidence did not pass.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-generic", "p", "Road", 1d, null, null))).Single(x => x.RuleId == accessRoadRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 generic Road evidence was not fail-closed.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-other-basis", "p", "Road", 1d, "UnitPeripheralRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 wrong Road basis was not fail-closed.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-road-other-applicability", "p", "Road", 1d, "AccessRoadEdge", "Equipment"))).Single(x => x.RuleId == accessRoadRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 wrong Road applicability was not fail-closed.");

            EngineeringDistanceRuleEvaluation fenceViolation = EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-29", "p46-fence", "Boundary", 29d, "SecurityFence", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId);
            Assert(fenceViolation.MinimumDistanceMeters == 30m && fenceViolation.EvaluatedSubjectCount == 1 && fenceViolation.ViolationCount == 1 && fenceViolation.Status == "VIOLATIONS" && fenceViolation.Violations.Single().ActualDistanceMeters == 29d && fenceViolation.Violations.Single().MeasurementBasisKey == "SecurityFence" && fenceViolation.Violations.Single().ApplicabilityKey == "Equipment", "P4.6 30m Security Fence candidate did not evaluate exactly.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-30", "p46-fence", "Boundary", 30d, "SecurityFence", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId).Status == "PASS", "P4.6 30m equality was a violation.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-31", "p46-fence", "Boundary", 31d, "SecurityFence", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId).Status == "PASS", "P4.6 30m above-limit evidence did not pass.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-generic", "p", "Boundary", 1d, null, null))).Single(x => x.RuleId == securityFenceRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 generic Boundary evidence was not fail-closed.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-site", "p", "Boundary", 1d, "SiteBoundary", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId).Status == "INSUFFICIENT_EVIDENCE" && EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-unit", "p", "Boundary", 1d, "UnitBoundary", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 wrong Boundary basis was not fail-closed.");
            Assert(EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", EvaluationMeasurement("p46-fence-other-applicability", "p", "Boundary", 1d, "SecurityFence", "ProcessingEquipment"))).Single(x => x.RuleId == securityFenceRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.6 wrong Boundary applicability was not fail-closed.");

            EngineeringDistanceRuleEvaluation accessMillimetres = EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Millimeters", EvaluationMeasurement("p46-road-2500", "p46-road-mm", "Road", 2500d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId);
            Assert(accessMillimetres.MinimumDistanceDrawingUnits == 3000d && accessMillimetres.Violations.Single().ActualDistanceDrawingUnits == 2500d && accessMillimetres.Violations.Single().ActualDistanceMeters == 2.5d && accessMillimetres.Status == "VIOLATIONS", "P4.6 3m millimetre conversion was not unit-safe.");

            QaReport accessRealQa = EngineeringDistanceQaIntegrator.Create(new[] { accessViolation }, p46Resolution, p46Clauses, p46Sources);
            QaReport fenceRealQa = EngineeringDistanceQaIntegrator.Create(new[] { fenceViolation }, p46Resolution, p46Clauses, p46Sources);
            Assert(RealCandidateEvidence(accessRealQa.Findings.Single(), accessRoadRuleId, "candidate.project.access-road-clearance", 3m, 2.5d, "AccessRoadEdge", "ProcessingEquipment", "trc.pcd-pd-135.3.3.9.b", "ips.e-pr-190.8.2.3"), "P4.6 3m real-source QA provenance failed.");
            Assert(RealCandidateEvidence(fenceRealQa.Findings.Single(), securityFenceRuleId, "candidate.project.security-fence-clearance", 30m, 29d, "SecurityFence", "Equipment", "trc.pcd-pd-135.3.3.10.a", "ips.e-pr-190.f.11.2"), "P4.6 30m real-source QA provenance failed.");

            List<EquipmentSpatialMeasurement> p46Measurements = new List<EquipmentSpatialMeasurement> { EvaluationMeasurement("p46-fence", "p-fence", "Boundary", 29d, "SecurityFence", "Equipment"), EvaluationMeasurement("p46-road", "p-road", "Road", 2.5d, "AccessRoadEdge", "ProcessingEquipment") };
            List<EngineeringDistanceRuleEvaluation> p46Evaluations = EngineeringDistanceRuleEvaluator.Evaluate(p46Rules, EvaluationPlot("Meters", p46Measurements.ToArray())).ToList();
            QaReport p46Qa = EngineeringDistanceQaIntegrator.Create(p46Evaluations, p46Resolution, p46Clauses, p46Sources);
            StandardRulePackResolution reversedResolution = StandardRulePackResolver.Resolve(StandardRulePackRegistry.Create(StandardClauseRegistry.Create(StandardSourceRegistry.Create(P46Sources().Reverse()), P46Clauses().Reverse()), P46Packs().Reverse()), new[] { new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.security-fence-clearance", Precedence = 10 }, new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.access-road-clearance", Precedence = 10 } });
            EngineeringDistanceRuleRegistry reversedRules = EngineeringDistanceRuleRegistry.Create(reversedResolution, P46Rules().Reverse());
            List<EngineeringDistanceRuleEvaluation> reversedEvaluations = EngineeringDistanceRuleEvaluator.Evaluate(reversedRules, EvaluationPlot("Meters", p46Measurements.AsEnumerable().Reverse().ToArray())).ToList();
            QaReport reversedQa = EngineeringDistanceQaIntegrator.Create(reversedEvaluations, reversedResolution, StandardClauseRegistry.Create(StandardSourceRegistry.Create(P46Sources().Reverse()), P46Clauses().Reverse()), StandardSourceRegistry.Create(P46Sources().Reverse()));
            Assert(P46Identity(p46Resolution, p46Evaluations, p46Qa) == P46Identity(reversedResolution, reversedEvaluations, reversedQa), "P4.6 real candidate processing was not deterministic.");

            p46SourceInputs[0].Title = "changed"; p46ClauseInputs[0].ClauseText = "changed"; p46PackInputs[0].Rules[0].Title = "changed"; p46RuleInputs[0].MinimumDistanceMeters = 999m;
            Assert(accessRealQa.Findings.Single().StandardsProvenance!.Clauses.Count == 2 && p46Rules.Get(accessRoadRuleId).MinimumDistanceMeters == 3m && p46Resolution.EffectiveRules.Single(x => x.RuleId == accessRoadRuleId).Title == "Minimum Processing Equipment to Access Road Edge Clearance", "P4.6 registry/evaluation/QA snapshots retained caller-owned state.");

            StandardSourceRegistry p47Sources = StandardSourceRegistry.Create(P46Sources());
            StandardClauseRegistry p47Clauses = StandardClauseRegistry.Create(p47Sources, P46Clauses());
            List<EngineeringDistanceCandidateDefinition> builtInCandidates = EngineeringDistanceCandidateCatalog.BuiltInCandidates.ToList();
            Assert(builtInCandidates.Count == 2 && builtInCandidates.All(x => x.ApprovalStatus == "PENDING_ENGINEERING_APPROVAL"), "P4.7 built-in candidates are not exactly pending engineering approval.");
            EngineeringDistanceCandidateDefinition p47Access = builtInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition p47Fence = builtInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            Assert(p47Access.PackId == "candidate.project.access-road-clearance" && p47Access.RuleId == accessRoadRuleId && p47Access.Title == "Minimum Processing Equipment to Access Road Edge Clearance" && p47Access.MeasurementKind == "EquipmentToRoad2D" && p47Access.ProposedMinimumDistanceMeters == 3m && p47Access.RequiredMeasurementBasisKey == "AccessRoadEdge" && p47Access.RequiredApplicabilityKey == "ProcessingEquipment" && p47Access.ClauseIds.SequenceEqual(new[] { "ips.e-pr-190.8.2.3", "trc.pcd-pd-135.3.3.9.b" }), "P4.7 3m candidate identity or provenance changed.");
            Assert(p47Fence.PackId == "candidate.project.security-fence-clearance" && p47Fence.RuleId == securityFenceRuleId && p47Fence.Title == "Minimum Equipment to Security Fence Clearance" && p47Fence.MeasurementKind == "EquipmentToBoundary2D" && p47Fence.ProposedMinimumDistanceMeters == 30m && p47Fence.RequiredMeasurementBasisKey == "SecurityFence" && p47Fence.RequiredApplicabilityKey == "Equipment" && p47Fence.ClauseIds.SequenceEqual(new[] { "ips.e-pr-190.f.11.2", "trc.pcd-pd-135.3.3.10.a" }), "P4.7 30m candidate identity or provenance changed.");
            EngineeringDistanceCandidateCatalog p47Catalog = EngineeringDistanceCandidateCatalog.Create(p47Clauses, builtInCandidates);
            EngineeringDistanceCandidateCatalog reversedP47Catalog = EngineeringDistanceCandidateCatalog.Create(p47Clauses, builtInCandidates.AsEnumerable().Reverse());
            Assert(P47CatalogIdentity(p47Catalog) == P47CatalogIdentity(reversedP47Catalog), "P4.7 catalog ordering was not deterministic.");
            builtInCandidates[0].Title = "changed"; builtInCandidates[0].ClauseIds.Clear();
            EngineeringDistanceCandidateDefinition p47Output = p47Catalog.Candidates.Single(x => x.CandidateId == p47Access.CandidateId); p47Output.Title = "changed"; p47Output.ClauseIds.Clear();
            Assert(p47Catalog.Candidates.Single(x => x.CandidateId == p47Access.CandidateId).Title == "Minimum Processing Equipment to Access Road Edge Clearance" && p47Catalog.Candidates.Single(x => x.CandidateId == p47Access.CandidateId).ClauseIds.Count == 2, "P4.7 catalog did not isolate caller or output mutation.");

            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.missing", "pack.missing", "rule.missing", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "missing.clause") }), "engineering distance candidate references unknown ClauseId: candidate.missing | missing.clause");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.same", "pack.a", "rule.a", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3"), P47Candidate("candidate.same", "pack.b", "rule.b", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3") }), "duplicate engineering distance candidate CandidateId: candidate.same");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.a", "pack.same", "rule.a", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3"), P47Candidate("candidate.b", "pack.same", "rule.b", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3") }), "duplicate engineering distance candidate PackId: pack.same");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.a", "pack.a", "rule.same", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3"), P47Candidate("candidate.b", "pack.b", "rule.same", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3") }), "duplicate engineering distance candidate RuleId: rule.same");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.invalid", "pack.invalid", "rule.invalid", "EquipmentToRoad2D", 1m, "SecurityFence", "Equipment", "ips.e-pr-190.8.2.3") }), "unsupported engineering distance candidate RequiredMeasurementBasisKey: candidate.invalid | SecurityFence");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.invalid", "pack.invalid", "rule.invalid", "EquipmentToRoad2D", 1m, "AccessRoadEdge", "Unknown", "ips.e-pr-190.8.2.3") }), "unsupported engineering distance candidate RequiredApplicabilityKey: candidate.invalid | Unknown");
            ThrowsCandidate(() => EngineeringDistanceCandidateCatalog.Create(p47Clauses, new[] { P47Candidate("candidate.invalid", "pack.invalid", "rule.invalid", "EquipmentToRoad2D", 0m, "AccessRoadEdge", "Equipment", "ips.e-pr-190.8.2.3") }), "engineering distance candidate ProposedMinimumDistanceMeters must be greater than zero: candidate.invalid");

            List<StandardRulePackDefinition> p47ProjectedPacks = p47Catalog.ToStandardRulePacks().ToList();
            Assert(p47ProjectedPacks.Count == 2 && p47ProjectedPacks.All(x => x.Rules.Count == 1) && p47ProjectedPacks.Single(x => x.PackId == "candidate.project.access-road-clearance").Rules.Single().ClauseIds.SequenceEqual(new[] { "ips.e-pr-190.8.2.3", "trc.pcd-pd-135.3.3.9.b" }) && p47ProjectedPacks.Single(x => x.PackId == "candidate.project.security-fence-clearance").Rules.Single().ClauseIds.SequenceEqual(new[] { "ips.e-pr-190.f.11.2", "trc.pcd-pd-135.3.3.10.a" }), "P4.7 rule-pack projection changed real identities or clauses.");
            StandardRulePackResolution p47Resolution = StandardRulePackResolver.Resolve(StandardRulePackRegistry.Create(p47Clauses, p47ProjectedPacks), new[] { new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.access-road-clearance", Precedence = 10 }, new StandardRulePackPrecedenceDefinition { PackId = "candidate.project.security-fence-clearance", Precedence = 10 } });
            Assert(p47Resolution.EffectiveRules.Count == 2 && p47Resolution.EffectiveRules.Select(x => x.RuleId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { accessRoadRuleId, securityFenceRuleId }.OrderBy(x => x, StringComparer.Ordinal)), "P4.7 projected packs did not resolve to both distinct P3 effective rules.");

            EngineeringDistanceCandidateDefinition catalogAccess = p47Catalog.Candidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceRuleDefinition? p47Executable;
            Assert(!EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "PENDING", null), p47Resolution, out p47Executable) && p47Executable == null, "P4.7 pending decision promoted a candidate.");
            Assert(!EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "REJECTED", null), p47Resolution, out p47Executable) && p47Executable == null, "P4.7 rejected decision promoted a candidate.");
            Assert(!EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "REFERENCE_ONLY", null), p47Resolution, out p47Executable) && p47Executable == null, "P4.7 reference-only decision promoted a candidate.");
            ThrowsApproval(() => EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "APPROVED", " "), p47Resolution, out _), "engineering distance approval reference is required: " + catalogAccess.CandidateId);
            ThrowsApproval(() => EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision("candidate.wrong", "APPROVED", "TEST-1"), p47Resolution, out _), "engineering distance approval CandidateId does not match candidate: " + catalogAccess.CandidateId);
            ThrowsApproval(() => EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "APPROVED", "TEST-1"), new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition>() }, out _), "engineering distance approval requires exactly one effective RuleId: " + catalogAccess.RuleId);
            StandardRulePackResolution mismatchedP47Resolution = new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { new EffectiveStandardRuleDefinition { RuleId = catalogAccess.RuleId, ClauseIds = new List<string> { "ips.e-pr-190.8.2.3" } } } };
            ThrowsApproval(() => EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "APPROVED", "TEST-1"), mismatchedP47Resolution, out _), "engineering distance approval ClauseIds do not match effective rule: " + catalogAccess.RuleId);
            Assert(EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogAccess, P47Decision(catalogAccess.CandidateId, "APPROVED", "TEST-ACCESS-3M"), p47Resolution, out p47Executable) && p47Executable != null && p47Executable.RuleId == accessRoadRuleId && p47Executable.MinimumDistanceMeters == 3m && p47Executable.RequiredMeasurementBasisKey == "AccessRoadEdge" && p47Executable.RequiredApplicabilityKey == "ProcessingEquipment", "P4.7 approved 3m candidate did not promote exactly.");
            EngineeringDistanceCandidateDefinition catalogFence = p47Catalog.Candidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            Assert(EngineeringDistanceApprovalGate.TryCreateExecutableRule(catalogFence, P47Decision(catalogFence.CandidateId, "APPROVED", "TEST-FENCE-30M"), p47Resolution, out EngineeringDistanceRuleDefinition? p47FenceExecutable) && p47FenceExecutable != null && p47FenceExecutable.RuleId == securityFenceRuleId && p47FenceExecutable.MinimumDistanceMeters == 30m && p47FenceExecutable.RequiredMeasurementBasisKey == "SecurityFence" && p47FenceExecutable.RequiredApplicabilityKey == "Equipment", "P4.7 approved 30m candidate did not promote exactly.");
            EngineeringDistanceRuleEvaluation p47Evaluation = EngineeringDistanceRuleEvaluator.Evaluate(EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p47Executable! }), EvaluationPlot("Meters", EvaluationMeasurement("p47-road", "p47", "Road", 2.5d, "AccessRoadEdge", "ProcessingEquipment"))).Single();
            QaReport p47Qa = EngineeringDistanceQaIntegrator.Create(new[] { p47Evaluation }, p47Resolution, p47Clauses, p47Sources);
            Assert(p47Evaluation.Status == "VIOLATIONS" && p47Qa.Findings.Count == 1 && p47Qa.Findings.Single().StandardsProvenance!.RuleId == accessRoadRuleId && p47Qa.Findings.Single().Evidence.EngineeringDistanceEvidence!.MinimumDistanceMeters == 3m, "P4.7 test-only approved rule changed the P4.3/P4.4 pipeline.");

            string p48Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngiSentry-P48-" + Guid.NewGuid().ToString("N"));
            string p48ProjectA = System.IO.Path.Combine(p48Root, "ProjectA");
            string p48ProjectB = System.IO.Path.Combine(p48Root, "ProjectB");
            System.IO.Directory.CreateDirectory(p48ProjectA); System.IO.Directory.CreateDirectory(p48ProjectB);
            string p48DirectoryA = ProjectDistanceCriteriaStore.ResolveProjectDirectory(System.IO.Path.Combine(p48ProjectA, "A0.dwg"));
            string p48DirectoryACase = ProjectDistanceCriteriaStore.ResolveProjectDirectory(System.IO.Path.Combine(p48ProjectA.ToLowerInvariant(), "a0.dwg"));
            string p48DirectoryB = ProjectDistanceCriteriaStore.ResolveProjectDirectory(System.IO.Path.Combine(p48ProjectB, "A0.dwg"));
            ProjectDistanceCriteria p48Defaults = ProjectDistanceCriteriaStore.CreateDefaults(p48DirectoryA);
            Assert(p48Defaults.SchemaVersion == 2 && p48Defaults.AccessRoadEdgeMinimumMeters == ProjectDistanceCriteriaDefaults.AccessRoadEdgeMinimumMeters && p48Defaults.SecurityFenceMinimumMeters == ProjectDistanceCriteriaDefaults.SecurityFenceMinimumMeters && p48Defaults.UnitBoundaryMinimumMeters == ProjectDistanceCriteriaDefaults.UnitBoundaryMinimumMeters, "P4.13 project defaults are incorrect.");
            p48Defaults.AccessRoadEdgeMinimumMeters = 99m;
            Assert(ProjectDistanceCriteriaStore.CreateDefaults(p48DirectoryA).AccessRoadEdgeMinimumMeters == 3m, "P4.8 defaults exposed mutable candidate state.");
            string p48PathA = ProjectDistanceCriteriaStore.GetSettingsPath(p48DirectoryA, p48Root);
            string p48PathACase = ProjectDistanceCriteriaStore.GetSettingsPath(p48DirectoryACase + System.IO.Path.DirectorySeparatorChar, p48Root);
            string p48PathB = ProjectDistanceCriteriaStore.GetSettingsPath(p48DirectoryB, p48Root);
            Assert(p48DirectoryA == p48DirectoryACase && p48PathA == p48PathACase && p48PathA != p48PathB && System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileName(p48PathA), "^[A-F0-9]{64}\\.json$"), "P4.8 project directory normalization or settings-path hash was not deterministic.");
            Assert(!ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out ProjectDistanceCriteria? p48Missing) && p48Missing == null, "P4.8 missing settings were not distinguished from valid settings.");
            ProjectDistanceCriteria p48Initial = new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m };
            WriteP48Criteria(p48PathA, p48Initial);
            byte[] p48LegacyBefore = System.IO.File.ReadAllBytes(p48PathA);
            Assert(ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out ProjectDistanceCriteria? p48Loaded) && p48Loaded != null && p48Loaded.SchemaVersion == 1 && p48Loaded.ProjectDirectory == p48DirectoryA && p48Loaded.AccessRoadEdgeMinimumMeters == 3m && p48Loaded.SecurityFenceMinimumMeters == 30m && p48LegacyBefore.SequenceEqual(System.IO.File.ReadAllBytes(p48PathA)), "P4.13 legacy settings were not read-only compatible.");
            p48Loaded!.AccessRoadEdgeMinimumMeters = 99m;
            Assert(ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out ProjectDistanceCriteria? p48Reloaded) && p48Reloaded!.AccessRoadEdgeMinimumMeters == 3m, "P4.8 loaded criteria mutation changed persisted settings.");
            ProjectDistanceCriteriaStore.Save(new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m, UnitBoundaryMinimumMeters = 18m }, p48Root);
            ProjectDistanceCriteriaStore.Save(new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = p48DirectoryB, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m }, p48Root);
            Assert(ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out ProjectDistanceCriteria? p48Updated) && p48Updated!.AccessRoadEdgeMinimumMeters == 5m && p48Updated.SecurityFenceMinimumMeters == 25m && p48Updated.UnitBoundaryMinimumMeters == 18m && ProjectDistanceCriteriaStore.TryLoad(p48DirectoryB, p48Root, out ProjectDistanceCriteria? p48Other) && p48Other!.AccessRoadEdgeMinimumMeters == 3m && p48Other.SecurityFenceMinimumMeters == 30m && p48Other.UnitBoundaryMinimumMeters == 15m, "P4.13 project update or isolation failed.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 0m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = -1m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 0m }), "Project Distance Criteria Security Fence minimum must be greater than zero.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = -1m }), "Project Distance Criteria Security Fence minimum must be greater than zero.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = " ", AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria ProjectDirectory must be an absolute path.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m, UnitBoundaryMinimumMeters = 0m }), "Project Distance Criteria Unit Boundary minimum must be greater than zero.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 3, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m, UnitBoundaryMinimumMeters = 1m }), "Unsupported Project Distance Criteria schema version: 3");
            System.IO.File.WriteAllText(p48PathA, "{ malformed");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out _), "Project Distance Criteria settings are invalid: " + p48PathA);
            WriteP48Criteria(p48PathA, new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p48DirectoryA, AccessRoadEdgeMinimumMeters = 0m, SecurityFenceMinimumMeters = 30m });
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.TryLoad(p48DirectoryA, p48Root, out _), "Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            System.IO.Directory.Delete(p48Root, true);

            ProjectDistanceCriteria p49Defaults = new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m };
            IReadOnlyList<EngineeringDistanceRuleDefinition> p49DefaultRules = ProjectDistanceRuleBinder.Create(p49Defaults);
            Assert(p49DefaultRules.Count == 2 && p49DefaultRules.Single(x => x.RequiredMeasurementBasisKey == "AccessRoadEdge").MinimumDistanceMeters == 3m && p49DefaultRules.Single(x => x.RequiredMeasurementBasisKey == "SecurityFence").MinimumDistanceMeters == 30m, "P4.9 default project criteria did not create exactly two configured rules.");
            ProjectDistanceCriteria p49Criteria = new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m };
            IReadOnlyList<EngineeringDistanceRuleDefinition> p49Rules = ProjectDistanceRuleBinder.Create(p49Criteria);
            EngineeringDistanceRuleDefinition p49Road = p49Rules.Single(x => x.RequiredMeasurementBasisKey == "AccessRoadEdge");
            EngineeringDistanceRuleDefinition p49Fence = p49Rules.Single(x => x.RequiredMeasurementBasisKey == "SecurityFence");
            Assert(p49Road.MinimumDistanceMeters == 5m && p49Fence.MinimumDistanceMeters == 25m && p49Road.CriteriaProvenance!.SourceKind == "ProjectDistanceCriteria" && p49Road.CriteriaProvenance.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && p49Fence.CriteriaProvenance!.CriteriaSettingKey == "SecurityFenceMinimumMeters" && p49Road.CriteriaProvenance.ConfiguredMinimumDistanceMeters == 5m && p49Fence.CriteriaProvenance.ConfiguredMinimumDistanceMeters == 25m, "P4.9 project rule binding did not retain configured authority and provenance.");
            Assert(EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m").ProposedMinimumDistanceMeters == 3m && EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m").ProposedMinimumDistanceMeters == 30m && EngineeringDistanceCandidateCatalog.BuiltInCandidates.All(x => x.ApprovalStatus == "PENDING_ENGINEERING_APPROVAL"), "P4.9 changed P4.7 candidate values or approval status.");
            p49Criteria.AccessRoadEdgeMinimumMeters = 99m; p49Road.MinimumDistanceMeters = 99m; p49Road.CriteriaProvenance!.ProjectDirectory = "C:\\changed";
            IReadOnlyList<EngineeringDistanceRuleDefinition> p49RepeatRules = ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m });
            Assert(p49RepeatRules.Single(x => x.RequiredMeasurementBasisKey == "AccessRoadEdge").MinimumDistanceMeters == 5m && p49RepeatRules.Single(x => x.RequiredMeasurementBasisKey == "AccessRoadEdge").CriteriaProvenance!.ProjectDirectory == p49Defaults.ProjectDirectory && string.Join("|", p49RepeatRules.Select(x => x.RuleId + ":" + x.MinimumDistanceMeters)) == string.Join("|", ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m }).Select(x => x.RuleId + ":" + x.MinimumDistanceMeters)), "P4.9 binder did not isolate input/output snapshots or remain deterministic.");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(null!), "Project Distance Criteria are required.");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 3, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m, UnitBoundaryMinimumMeters = 1m }), "Unsupported Project Distance Criteria schema version: 3");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = " ", AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria ProjectDirectory must be an absolute path.");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = "relative", AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria ProjectDirectory must be an absolute path.");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 0m, SecurityFenceMinimumMeters = 1m }), "Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            ThrowsBinding(() => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 1m, SecurityFenceMinimumMeters = 0m }), "Project Distance Criteria Security Fence minimum must be greater than zero.");

            EngineeringDistanceRuleRegistry p49Registry = EngineeringDistanceRuleRegistry.Create(p47Resolution, p49RepeatRules);
            EngineeringDistanceRuleDefinition p49RegistryRoad = p49Registry.Get(accessRoadRuleId);
            Assert(p49RegistryRoad.CriteriaProvenance != null && p49RegistryRoad.CriteriaProvenance.SourceKind == "ProjectDistanceCriteria" && p49RegistryRoad.CriteriaProvenance.ProjectDirectory == p49Defaults.ProjectDirectory && p49RegistryRoad.CriteriaProvenance.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && p49RegistryRoad.CriteriaProvenance.SchemaVersion == 1 && p49RegistryRoad.CriteriaProvenance.ConfiguredMinimumDistanceMeters == 5m, "P4.9 registry did not preserve criteria provenance.");
            p49RegistryRoad.CriteriaProvenance!.ProjectDirectory = "C:\\changed";
            Assert(p49Registry.Get(accessRoadRuleId).CriteriaProvenance!.ProjectDirectory == p49Defaults.ProjectDirectory, "P4.9 registry criteria provenance was not deep copied.");
            Func<string, EngineeringDistanceRuleDefinition> p49FreshRule = basis => ProjectDistanceRuleBinder.Create(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = p49Defaults.ProjectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m }).Single(x => x.RequiredMeasurementBasisKey == basis);
            EngineeringDistanceRuleDefinition p49Invalid = p49FreshRule("AccessRoadEdge");
            p49Invalid.CriteriaProvenance!.SourceKind = "Unknown";
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "unsupported engineering distance criteria SourceKind: " + accessRoadRuleId + " | Unknown");
            p49Invalid = p49FreshRule("AccessRoadEdge"); p49Invalid.CriteriaProvenance!.SchemaVersion = 3;
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "unsupported engineering distance criteria SchemaVersion: " + accessRoadRuleId + " | 3");
            p49Invalid = p49FreshRule("AccessRoadEdge"); p49Invalid.CriteriaProvenance!.ProjectDirectory = "relative";
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "engineering distance criteria ProjectDirectory must be absolute: " + accessRoadRuleId);
            p49Invalid = p49FreshRule("AccessRoadEdge"); p49Invalid.CriteriaProvenance!.ConfiguredMinimumDistanceMeters = 0m;
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "engineering distance criteria ConfiguredMinimumDistanceMeters must be greater than zero: " + accessRoadRuleId);
            p49Invalid = p49FreshRule("AccessRoadEdge"); p49Invalid.CriteriaProvenance!.ConfiguredMinimumDistanceMeters = 6m;
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "engineering distance criteria configured minimum does not match rule minimum: " + accessRoadRuleId);
            p49Invalid = p49FreshRule("AccessRoadEdge"); p49Invalid.CriteriaProvenance!.CriteriaSettingKey = "SecurityFenceMinimumMeters";
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "engineering distance criteria setting key is incompatible with rule basis: " + accessRoadRuleId);
            p49Invalid = p49FreshRule("SecurityFence"); p49Invalid.CriteriaProvenance!.CriteriaSettingKey = "AccessRoadEdgeMinimumMeters";
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(p47Resolution, new[] { p49Invalid }), "engineering distance criteria setting key is incompatible with rule basis: " + securityFenceRuleId);

            EngineeringDistanceRuleEvaluation p49RoadViolation = EngineeringDistanceRuleEvaluator.Evaluate(p49Registry, EvaluationPlot("Meters", EvaluationMeasurement("p49-road-4", "p49-road", "Road", 4d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId);
            EngineeringDistanceRuleEvaluation p49RoadPass = EngineeringDistanceRuleEvaluator.Evaluate(p49Registry, EvaluationPlot("Meters", EvaluationMeasurement("p49-road-5", "p49-road", "Road", 5d, "AccessRoadEdge", "ProcessingEquipment"))).Single(x => x.RuleId == accessRoadRuleId);
            EngineeringDistanceRuleEvaluation p49FenceViolation = EngineeringDistanceRuleEvaluator.Evaluate(p49Registry, EvaluationPlot("Meters", EvaluationMeasurement("p49-fence-20", "p49-fence", "Boundary", 20d, "SecurityFence", "Equipment"))).Single(x => x.RuleId == securityFenceRuleId);
            Assert(p49RoadViolation.Status == "VIOLATIONS" && p49RoadViolation.MinimumDistanceMeters == 5m && p49RoadViolation.CriteriaProvenance!.ConfiguredMinimumDistanceMeters == 5m && p49RoadViolation.CriteriaProvenance.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && p49RoadPass.Status == "PASS" && p49FenceViolation.Status == "VIOLATIONS" && p49FenceViolation.MinimumDistanceMeters == 25m && p49FenceViolation.CriteriaProvenance!.CriteriaSettingKey == "SecurityFenceMinimumMeters", "P4.9 evaluator did not preserve configured thresholds, equality, or provenance.");
            QaReport p49RoadQa = EngineeringDistanceQaIntegrator.Create(new[] { p49RoadViolation }, p47Resolution, p47Clauses, p47Sources);
            QaReport p49FenceQa = EngineeringDistanceQaIntegrator.Create(new[] { p49FenceViolation }, p47Resolution, p47Clauses, p47Sources);
            QaFinding p49RoadFinding = p49RoadQa.Findings.Single(); QaFinding p49FenceFinding = p49FenceQa.Findings.Single();
            Assert(p49RoadFinding.Evidence.EngineeringDistanceEvidence!.MinimumDistanceMeters == 5m && p49RoadFinding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance != null && p49RoadFinding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && p49RoadFinding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.ConfiguredMinimumDistanceMeters == 5m && p49RoadFinding.StandardsProvenance == null && p49RoadFinding.Message.Contains("configured project minimum distance") && !p49RoadFinding.Message.Contains("standards-backed") && p49FenceFinding.Evidence.EngineeringDistanceEvidence!.MinimumDistanceMeters == 25m && p49FenceFinding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance!.CriteriaSettingKey == "SecurityFenceMinimumMeters" && p49FenceFinding.StandardsProvenance == null, "P4.9 project criteria QA provenance was not separated from standards.");
            Assert(p47Qa.Findings.Single().StandardsProvenance != null && p47Qa.Findings.Single().Evidence.EngineeringDistanceEvidence!.CriteriaProvenance == null && p47Qa.Findings.Single().Message.Contains("standards-backed minimum distance"), "P4.9 changed the P4.7 standards-backed QA path.");
            EngineeringDistanceRuleEvaluation p49MixedProject = Evaluation("project.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("p49-mixed", "p49-mixed", "Road"));
            p49MixedProject.MinimumDistanceMeters = 5m;
            p49MixedProject.CriteriaProvenance = new EngineeringDistanceCriteriaProvenance { SourceKind = "ProjectDistanceCriteria", ProjectDirectory = p49Defaults.ProjectDirectory, CriteriaSettingKey = "AccessRoadEdgeMinimumMeters", SchemaVersion = 1, ConfiguredMinimumDistanceMeters = 5m };
            EngineeringDistanceRuleEvaluation p49MixedStandard = Evaluation("std.mixed", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("p49-standard", "p49-standard", "Road"));
            StandardSourceRegistry p49MixedSources = QaSources(); StandardClauseRegistry p49MixedClauses = QaClauses(p49MixedSources, "project.road", "std.mixed");
            QaReport p49MixedQa = EngineeringDistanceQaIntegrator.Create(new[] { p49MixedProject, p49MixedStandard }, QaStandards("project.road", "std.mixed"), p49MixedClauses, p49MixedSources);
            Assert(p49MixedQa.Findings.Single(x => x.RuleId == "project.road").StandardsProvenance == null && p49MixedQa.Findings.Single(x => x.RuleId == "project.road").Evidence.EngineeringDistanceEvidence!.CriteriaProvenance != null && p49MixedQa.Findings.Single(x => x.RuleId == "std.mixed").StandardsProvenance != null && p49MixedQa.Findings.Single(x => x.RuleId == "std.mixed").Evidence.EngineeringDistanceEvidence!.CriteriaProvenance == null, "P4.9 mixed report provenance separation failed.");

            EngineeringDistanceRuleEvaluation snapshotEvaluation = Evaluation("std.road", "EquipmentToRoad2D", "VIOLATIONS", 1, 1, Violation("m-snapshot", "p-snapshot", "Road"));
            QaReport snapshotReport = EngineeringDistanceQaIntegrator.Create(new[] { snapshotEvaluation }, QaStandards("std.road"), qaClauses, qaSources);
            snapshotEvaluation.RuleId = "changed"; snapshotEvaluation.MeasurementKind = "changed"; snapshotEvaluation.Status = "PASS"; snapshotEvaluation.MinimumDistanceMeters = 999m; snapshotEvaluation.MinimumDistanceDrawingUnits = 999d; snapshotEvaluation.Violations.Single().MeasurementId = "changed"; snapshotEvaluation.Violations.Single().PlacementId = "changed"; snapshotEvaluation.Violations.Single().ActualDistanceMeters = 999d; snapshotEvaluation.Violations.Single().SubjectKind = "changed"; snapshotEvaluation.Violations.Single().LabelSourceHandle = "changed";
            Assert(snapshotReport.RuleResults.Single().RuleId == "std.road" && snapshotReport.Findings.Single().FindingId == "std.road:p-snapshot" && snapshotReport.Findings.Single().Evidence.EngineeringDistanceEvidence!.MeasurementId == "m-snapshot", "Engineering-distance QA report retained caller-owned evaluation or violation references.");

            RunP412();
            RunP413();
            RunP51();
            RunP52();
            RunP53();
            RunP54();
            RunP91();
            RunP92A();
            RunP92B();
            RunP92C1();
            RunP92C2A();
            RunP92C2B();
            RunP92C2C();
        }

        private static void RunDistanceCoverageTests()
        {
            EngineeringDistanceRuleRegistry roadRule = EngineeringDistanceRuleRegistry.Create(Standards("std.coverage.road"), new[] { DistanceRule("std.coverage.road", "EquipmentToRoad2D", 3m, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment) });
            PlotPlanModel plot = EvaluationPlot("Meters", EvaluationMeasurement("coverage-road", "coverage-complete", "Road", 4d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment));
            plot.EquipmentPlacements.Add(new EquipmentPlacement { PlacementId = "coverage-ambiguous", EquipmentEntityId = "equipment:coverage-ambiguous", PlacementStatus = "Ambiguous", EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment });
            plot.EquipmentPlacements.Add(new EquipmentPlacement { PlacementId = "coverage-incomplete", EquipmentEntityId = "equipment:coverage-incomplete", PlacementStatus = "Incomplete", EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment });
            plot.EquipmentPlacements.Add(new EquipmentPlacement { PlacementId = "coverage-other", EquipmentEntityId = "equipment:coverage-other", PlacementStatus = "Incomplete", EngineeringApplicabilityKey = "Other" });
            EngineeringDistanceRuleEvaluation partial = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, plot).Single();
            Assert(partial.Status == "INSUFFICIENT_EVIDENCE" && partial.TotalApplicablePlacementCount == 3 && partial.EvaluatedPlacementCount == 1 && partial.UnresolvedPlacementCount == 2 && partial.EvaluatedSubjectCount == 1 && partial.ViolationCount == 0, "Partial placement coverage incorrectly passed or lost counts.");
            StandardSourceRegistry sources = QaSources();
            QaReport partialQa = EngineeringDistanceQaIntegrator.Create(new[] { partial }, QaStandards("std.coverage.road"), QaClauses(sources, "std.coverage.road"), sources);
            Assert(partialQa.RuleResults.Single().Status == "INSUFFICIENT_EVIDENCE", "Partial coverage was rejected by QA projection.");
            using (var stream = new System.IO.MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(EngineeringDistanceRuleEvaluation)).WriteObject(stream, partial);
                string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                Assert(json.Contains("TotalApplicablePlacementCount") && json.Contains("EvaluatedPlacementCount") && json.Contains("UnresolvedPlacementCount"), "Coverage counts were not serialized in evidence.");
            }
            plot.Measurements.EquipmentSpatialMeasurements[0].Distance2D = 2d;
            EngineeringDistanceRuleEvaluation violated = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, plot).Single();
            Assert(violated.Status == "VIOLATIONS" && violated.ViolationCount == 1 && violated.TotalApplicablePlacementCount == 3 && violated.EvaluatedPlacementCount == 1 && violated.UnresolvedPlacementCount == 2, "Proven violation lost precedence over partial coverage.");
            plot.Measurements.EquipmentSpatialMeasurements[0].Distance2D = 4d;
            foreach (EquipmentPlacement placement in plot.EquipmentPlacements.Where(x => x.PlacementId == "coverage-ambiguous" || x.PlacementId == "coverage-incomplete"))
            {
                placement.PlacementStatus = "Complete";
                placement.Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 0m };
                placement.Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 0m };
                plot.Measurements.EquipmentSpatialMeasurements.Add(EvaluationMeasurement("measurement:" + placement.PlacementId, placement.PlacementId, "Road", 4d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment));
            }
            EngineeringDistanceRuleEvaluation complete = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, plot).Single();
            Assert(complete.Status == "PASS" && complete.TotalApplicablePlacementCount == 3 && complete.EvaluatedPlacementCount == 3 && complete.UnresolvedPlacementCount == 0, "Fully covered passing placements did not pass.");
            EngineeringDistanceRuleRegistry boundaryRule = EngineeringDistanceRuleRegistry.Create(Standards("std.coverage.boundary"), new[] { DistanceRule("std.coverage.boundary", "EquipmentToBoundary2D", 15m, EngineeringDistanceEvidenceKeys.UnitBoundary, EngineeringDistanceEvidenceKeys.Equipment) });
            EngineeringDistanceRuleEvaluation noBoundary = EngineeringDistanceRuleEvaluator.Evaluate(boundaryRule, plot).Single();
            Assert(noBoundary.Status == "INSUFFICIENT_EVIDENCE" && noBoundary.TotalApplicablePlacementCount == 3 && noBoundary.EvaluatedPlacementCount == 0 && noBoundary.UnresolvedPlacementCount == 3, "UnitBoundary without a valid subject did not remain insufficient.");

            PlotPlanModel duplicateOccurrences = EvaluationPlot("Meters",
                EvaluationMeasurement("duplicate-a-road", "duplicate-a", "Road", 4d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment),
                EvaluationMeasurement("duplicate-b-road", "duplicate-b", "Road", 4d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment));
            EquipmentPlacement duplicateA = duplicateOccurrences.EquipmentPlacements.Single(x => x.PlacementId == "duplicate-a");
            EquipmentPlacement duplicateB = duplicateOccurrences.EquipmentPlacements.Single(x => x.PlacementId == "duplicate-b");
            duplicateA.EquipmentEntityId = duplicateB.EquipmentEntityId = "equipment:canonical-duplicate";
            duplicateOccurrences.Measurements.EquipmentSpatialMeasurements.ForEach(x => x.EquipmentEntityId = "equipment:canonical-duplicate");
            duplicateA.Easting.SelectedValue = duplicateB.Easting.SelectedValue = 100m;
            duplicateA.Northing.SelectedValue = duplicateB.Northing.SelectedValue = 200m;
            EngineeringDistanceRuleEvaluation deduplicated = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, duplicateOccurrences).Single();
            Assert(deduplicated.Status == "PASS" && deduplicated.TotalApplicablePlacementCount == 1 && deduplicated.EvaluatedPlacementCount == 1 && deduplicated.UnresolvedPlacementCount == 0 && deduplicated.EvaluatedSubjectCount == 1, "Equivalent DrawingTag placements inflated canonical equipment coverage.");

            duplicateB.Easting.SelectedValue = 101m;
            EngineeringDistanceRuleEvaluation conflicting = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, duplicateOccurrences).Single();
            Assert(conflicting.Status == "INSUFFICIENT_EVIDENCE" && conflicting.TotalApplicablePlacementCount == 1 && conflicting.EvaluatedPlacementCount == 0 && conflicting.UnresolvedPlacementCount == 1 && conflicting.EvaluatedSubjectCount == 0 && conflicting.ViolationCount == 0, "Conflicting Complete placements were selected instead of leaving the canonical equipment unresolved.");

            PlotPlanModel oneCompleteOccurrence = EvaluationPlot("Meters", EvaluationMeasurement("one-complete-road", "one-complete", "Road", 4d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment));
            oneCompleteOccurrence.EquipmentPlacements.Add(new EquipmentPlacement { PlacementId = "one-incomplete", EquipmentEntityId = "equipment:one-complete", PlacementStatus = "Incomplete", EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment });
            EngineeringDistanceRuleEvaluation oneComplete = EngineeringDistanceRuleEvaluator.Evaluate(roadRule, oneCompleteOccurrence).Single();
            Assert(oneComplete.Status == "PASS" && oneComplete.TotalApplicablePlacementCount == 1 && oneComplete.EvaluatedPlacementCount == 1 && oneComplete.UnresolvedPlacementCount == 0, "An incomplete duplicate DrawingTag occurrence inflated unresolved coverage.");
        }

        private static void RunDrawingUnitConflictGate()
        {
            const string declaration = "1-ALL DIMENSIONS, ELEVATIONS AND COORDINATES ARE IN MILLIMETERS.";
            var snapshot = new DrawingSnapshot { InsertionUnits = "Meters" };
            snapshot.TextEntities.Add(new TextSnapshot { Text = declaration, Handle = "NOTE-1", Layer = "Text", SpaceKind = "Paper" });
            snapshot.TextEntities.Add(new TextSnapshot { Text = "ALL EQUIPMENT AND SUMPS DIMENSIONS ARE APPROXIMATE.", Handle = "NOTE-2", SpaceKind = "Paper" });
            SemanticDrawing drawing = SemanticExtractor.Extract(snapshot).Drawing!;
            Assert(drawing.ExplicitUnitDeclarations.Count == 1 && drawing.ExplicitUnitDeclarations[0].Unit == "Millimeters" && drawing.ExplicitUnitDeclarations[0].SourceHandle == "NOTE-1", "Explicit drawing unit note was not captured with provenance.");
            PlotPlanModel plot = EvaluationPlot("Meters", EvaluationMeasurement("unit-road", "unit-placement", "Road", 2d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment));
            plot.Drawing = drawing;
            ProjectDistanceCriteria criteria = ProjectDistanceCriteriaStore.CreateDefaults(System.IO.Path.GetTempPath());
            QaReport gated = ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base());
            Assert(gated.EngineeringDistanceSummary!.Evaluations.All(x => x.Status == "INSUFFICIENT_EVIDENCE" && x.EvaluatedSubjectCount == 0 && x.ViolationCount == 0 && x.TotalApplicablePlacementCount == 1 && x.EvaluatedPlacementCount == 0 && x.UnresolvedPlacementCount == 1 && x.MetersPerDrawingUnit == 0d && x.MinimumDistanceDrawingUnits == 0d && x.UnitConflict != null && x.UnitConflict.Insunits == "Meters" && x.UnitConflict.ExplicitDeclarations.Single().RawText == declaration && x.UnitConflict.ExplicitDeclarations.Single().SourceHandle == "NOTE-1"), "Unit conflict produced a trusted distance verdict or lost either source.");
            Assert(gated.RuleResults.Where(x => gated.EngineeringDistanceSummary.Evaluations.Any(e => e.RuleId == x.RuleId)).All(x => x.Status == "INSUFFICIENT_EVIDENCE") && gated.OverallStatus != "PASS" && gated.OverallStatus != "FAIL", "Unit conflict QA was not review-required or insufficient.");
            using (var stream = new System.IO.MemoryStream())
            {
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(QaReport)).WriteObject(stream, gated);
                string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                Assert(json.Contains("NOTE-1") && json.Contains("Millimeters") && json.Contains("Meters") && json.Contains("UnitConflict"), "Serialized QA did not retain both unit sources.");
            }
            var bounded = new BoundedConstraint { ConstraintId = "unit-gated", MeasurementKind = "EquipmentToRoad2D", MinimumDistanceMeters = 3m, RequiredMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge, RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.ProcessingEquipment };
            Assert(EngineeringDistanceRuleEvaluator.EvaluateGovernedConstraint(bounded, plot).Status == "INSUFFICIENT_EVIDENCE", "Governed distance evaluation bypassed the unit conflict gate.");
            snapshot.InsertionUnits = "Millimeters";
            plot.Drawing = SemanticExtractor.Extract(snapshot).Drawing;
            Assert(ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base()).EngineeringDistanceSummary!.Evaluations.All(x => x.UnitConflict == null), "Matching drawing unit evidence was treated as a conflict.");
            snapshot.TextEntities.Clear();
            plot.Drawing = SemanticExtractor.Extract(snapshot).Drawing;
            Assert(ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base()).EngineeringDistanceSummary!.Evaluations.All(x => x.UnitConflict == null), "Absent drawing unit declaration was treated as a conflict.");
            snapshot.TextEntities.Add(new TextSnapshot { Text = "DRAWING UNITS: FEET", Handle = "GENERIC", SpaceKind = "Model" });
            Assert(SemanticExtractor.Extract(snapshot).Drawing!.ExplicitUnitDeclarations.Single().Unit == "Feet", "Generic drawing unit declaration was missed.");
        }

        private static void RunP91()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            DateTime timestamp = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234);
            EngineeringDistanceApprovalDecision approved = new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "APR-P91-001" };
            EngineeringDistanceApprovalRecord first = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project\\", road, context.ClauseRegistry, context.SourceRegistry, approved, "P9.1 Test Approver", null, timestamp, "P9.1 deterministic approval");
            EngineeringDistanceApprovalRecord same = EngineeringDistanceApprovalRecordBuilder.Create("c:\\p91\\project", road, context.ClauseRegistry, context.SourceRegistry, approved, "P9.1 Test Approver", null, timestamp, "P9.1 deterministic approval");
            EngineeringDistanceApprovalRecord fenceRecord = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", fence, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = fence.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "APR-P91-002" }, "P9.1 Test Approver", null, timestamp, null, first.ApprovalRecordId);
            Assert(first.CandidateBinding.CandidateId == road.CandidateId && first.CandidateBinding.RuleId == road.RuleId && first.CandidateBinding.ProposedMinimumDistanceMeters == 3m && first.CandidateBinding.RequiredMeasurementBasisKey == "AccessRoadEdge" && first.CandidateBinding.RequiredApplicabilityKey == "ProcessingEquipment" && first.CandidateBinding.ClauseIds.SequenceEqual(road.ClauseIds.OrderBy(x => x, StringComparer.Ordinal)), "P9.1 road candidate binding changed.");
            Assert(first.EvidenceBindings.Select(x => x.ClauseId + "|" + x.SourceId + "|" + x.SourceSha256).SequenceEqual(new[] { "ips.e-pr-190.8.2.3|ips.e-pr-190.rev1.2009|FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152", "trc.pcd-pd-135.3.3.9.b|trc.pcd-pd-135.rev1|A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" }), "P9.1 road source evidence changed.");
            Assert(fenceRecord.CandidateBinding.CandidateId == fence.CandidateId && fenceRecord.CandidateBinding.ProposedMinimumDistanceMeters == 30m && fenceRecord.SupersedesApprovalRecordId == first.ApprovalRecordId && fenceRecord.EvidenceBindings.Select(x => x.ClauseId + "|" + x.SourceId + "|" + x.SourceSha256).SequenceEqual(new[] { "ips.e-pr-190.f.11.2|ips.e-pr-190.rev1.2009|FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152", "trc.pcd-pd-135.3.3.10.a|trc.pcd-pd-135.rev1|A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" }), "P9.1 fence source evidence changed.");
            Assert(first.ApprovalRecordId == same.ApprovalRecordId && first.CandidateBinding.CandidateDefinitionSha256 == same.CandidateBinding.CandidateDefinitionSha256 && first.DecisionTimestampUtc.Kind == DateTimeKind.Utc && first.DecisionTimestampUtc.Ticks % TimeSpan.TicksPerMillisecond == 0, "P9.1 deterministic identity or timestamp normalization failed.");
            Assert(first.EvidenceBindings.All(x => x.SourceSha256.Length == 64) && first.EvidenceBindings.SequenceEqual(first.EvidenceBindings.OrderBy(x => x.ClauseId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ThenBy(x => x.SourceSha256, StringComparer.Ordinal)), "P9.1 evidence ordering or SHA binding failed.");
            EngineeringDistanceApprovalRecord changed = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "REFERENCE_ONLY" }, "P9.1 Test Approver", null, timestamp, "P9.1 changed decision");
            Assert(changed.ApprovalRecordId != first.ApprovalRecordId, "P9.1 changed logical record did not change ApprovalRecordId.");
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngiSentry-P91-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = EngineeringDistanceApprovalRecordFileStore.Write(directory, first);
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                EngineeringDistanceApprovalRecord read = EngineeringDistanceApprovalRecordFileStore.Read(path);
                Assert(bytes.Length >= 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) && read.ApprovalRecordId == first.ApprovalRecordId && read.DecisionTimestampUtc == first.DecisionTimestampUtc && EngineeringDistanceApprovalRecordFileStore.ComputeSha256(path).Length == 64, "P9.1 persistence round-trip or BOM-free output failed.");
                bool overwrite = false; try { EngineeringDistanceApprovalRecordFileStore.Write(directory, first); } catch (System.IO.IOException) { overwrite = true; } Assert(overwrite, "P9.1 CreateNew did not reject overwrite.");
                string json = System.IO.File.ReadAllText(path);
                string fingerprintPath = System.IO.Path.Combine(directory, "candidate-fingerprint-tampered.json");
                System.IO.File.WriteAllText(fingerprintPath, json.Replace(first.CandidateBinding.CandidateDefinitionSha256, new string('0', 64)), new System.Text.UTF8Encoding(false));
                bool fingerprintTampered = false; try { EngineeringDistanceApprovalRecordFileStore.Read(fingerprintPath); } catch (EngineeringDistanceApprovalRecordValidationException) { fingerprintTampered = true; } catch (System.IO.InvalidDataException) { fingerprintTampered = true; } Assert(fingerprintTampered, "P9.1 tampered candidate fingerprint was accepted.");
                string replacementId = first.ApprovalRecordId == new string('F', 64) ? new string('E', 64) : new string('F', 64);
                string recordIdPath = System.IO.Path.Combine(directory, "approval-record-id-tampered.json");
                System.IO.File.WriteAllText(recordIdPath, json.Replace(first.ApprovalRecordId, replacementId), new System.Text.UTF8Encoding(false));
                bool recordIdTampered = false; try { EngineeringDistanceApprovalRecordFileStore.Read(recordIdPath); } catch (System.IO.InvalidDataException) { recordIdTampered = true; } Assert(recordIdTampered, "P9.1 tampered ApprovalRecordId was accepted.");
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
            bool blankApprover = false; try { EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", road, context.ClauseRegistry, context.SourceRegistry, approved, string.Empty, null, timestamp); } catch (EngineeringDistanceApprovalRecordValidationException) { blankApprover = true; } Assert(blankApprover, "P9.1 accepted a blank approver.");
            bool reviewAccepted = false; try { EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "REVIEW_ACCEPTED" }, "P9.1 Test Approver", null, timestamp); } catch (EngineeringDistanceApprovalRecordValidationException) { reviewAccepted = true; } Assert(reviewAccepted, "P9.1 accepted P8 review state as approval.");
            bool unknownStatus = false; try { EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "UNKNOWN_STATUS" }, "P9.1 Test Approver", null, timestamp); } catch (EngineeringDistanceApprovalRecordValidationException) { unknownStatus = true; } Assert(unknownStatus, "P9.1 accepted an unknown approval state.");
            bool missingReference = false; try { EngineeringDistanceApprovalRecordBuilder.Create("C:\\P91\\Project", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "APPROVED" }, "P9.1 Test Approver", null, timestamp); } catch (EngineeringDistanceApprovalRecordValidationException) { missingReference = true; } Assert(missingReference, "P9.1 APPROVED record did not require ApprovalReference.");
            first.SupersedesApprovalRecordId = first.ApprovalRecordId;
            bool self = false; try { EngineeringDistanceApprovalRecordValidator.Validate(first); } catch (EngineeringDistanceApprovalRecordValidationException) { self = true; } Assert(self, "P9.1 self-supersession was accepted.");
        }

        private static void RunP92A()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            DateTime timestamp = new DateTime(2026, 9, 9, 13, 0, 0, DateTimeKind.Utc);
            EngineeringDistanceApprovalRecord a = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92A-A" }, "Approver", null, timestamp, "A");
            EngineeringDistanceApprovalRecord b = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "REJECTED" }, "Approver", null, timestamp.AddMilliseconds(1), "B", a.ApprovalRecordId);
            EngineeringDistanceApprovalRecord c = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp.AddMilliseconds(2), "C", b.ApprovalRecordId);
            EngineeringDistanceApprovalHistoryResolution chain = EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { c, a, b });
            Assert(chain.Entries.Single(x => x.ApprovalRecordId == a.ApprovalRecordId).State == EngineeringDistanceApprovalHistoryStates.Superseded && chain.Entries.Single(x => x.ApprovalRecordId == b.ApprovalRecordId).State == EngineeringDistanceApprovalHistoryStates.Superseded && chain.Entries.Single(x => x.ApprovalRecordId == c.ApprovalRecordId).State == EngineeringDistanceApprovalHistoryStates.Active, "P9.2A append-only history chain failed.");
            EngineeringDistanceApprovalCurrentnessResult current = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "c:\\p92a\\", road, context.ClauseRegistry, context.SourceRegistry);
            Assert(current.Status == EngineeringDistanceApprovalCurrentnessStates.Current && current.Reasons.Count == 0, "P9.2A current approval was not current.");
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A-OTHER", road, context.ClauseRegistry, context.SourceRegistry).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.ProjectScopeChanged }), "P9.2A project currentness reason failed.");
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A", null, context.ClauseRegistry, context.SourceRegistry).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.CandidateMissing }), "P9.2A missing candidate reason failed.");
            EngineeringDistanceCandidateDefinition changedValue = CopyP92Candidate(road); changedValue.ProposedMinimumDistanceMeters = 4m;
            EngineeringDistanceCandidateDefinition changedRule = CopyP92Candidate(road); changedRule.RuleId = "changed.rule";
            EngineeringDistanceCandidateDefinition changedBasis = CopyP92Candidate(road); changedBasis.RequiredMeasurementBasisKey = "RoadCenterline";
            EngineeringDistanceCandidateDefinition changedApplicability = CopyP92Candidate(road); changedApplicability.RequiredApplicabilityKey = "Equipment";
            EngineeringDistanceCandidateDefinition changedClauses = CopyP92Candidate(road); changedClauses.ClauseIds = new List<string> { road.ClauseIds[0] };
            foreach (EngineeringDistanceCandidateDefinition changed in new[] { changedValue, changedRule, changedBasis, changedApplicability }) Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A", changed, context.ClauseRegistry, context.SourceRegistry).Reasons.Contains(EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged), "P9.2A candidate change was not stale.");
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A", changedClauses, context.ClauseRegistry, context.SourceRegistry).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged, EngineeringDistanceApprovalCurrentnessReasons.EvidenceBindingsChanged }), "P9.2A clause-set stale reasons were not exact or deterministic.");
            StandardSourceRegistry changedShaSources = StandardSourceRegistry.Create(context.SourceRegistry.Sources.Select(x => new StandardSourceDefinition { SourceId = x.SourceId, DocumentNumber = x.DocumentNumber, Title = x.Title, Issuer = x.Issuer, RevisionOrEdition = x.RevisionOrEdition, SourceFileName = x.SourceFileName, SourceSha256 = x.SourceId == "trc.pcd-pd-135.rev1" ? new string('B', 64) : x.SourceSha256 }));
            StandardClauseRegistry changedShaClauses = StandardClauseRegistry.Create(changedShaSources, context.ClauseRegistry.Clauses);
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A", road, changedShaClauses, changedShaSources).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.EvidenceBindingsChanged }), "P9.2A source SHA change was not stale.");
            StandardSourceRegistry changedIdSources = StandardSourceRegistry.Create(context.SourceRegistry.Sources.Select(x => new StandardSourceDefinition { SourceId = x.SourceId == "trc.pcd-pd-135.rev1" ? "trc.pcd-pd-135.changed" : x.SourceId, DocumentNumber = x.DocumentNumber, Title = x.Title, Issuer = x.Issuer, RevisionOrEdition = x.RevisionOrEdition, SourceFileName = x.SourceFileName, SourceSha256 = x.SourceSha256 }));
            StandardClauseRegistry changedIdClauses = StandardClauseRegistry.Create(changedIdSources, context.ClauseRegistry.Clauses.Select(x => new StandardClauseDefinition { ClauseId = x.ClauseId, SourceId = x.SourceId == "trc.pcd-pd-135.rev1" ? "trc.pcd-pd-135.changed" : x.SourceId, ClauseReference = x.ClauseReference, ClauseText = x.ClauseText, Heading = x.Heading, PageNumber = x.PageNumber }));
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A", road, changedIdClauses, changedIdSources).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.EvidenceBindingsChanged }), "P9.2A source ID change was not stale.");
            EngineeringDistanceApprovalRecord fenceRecord = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", fence, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = fence.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92A-F" }, "Approver", null, timestamp, "F");
            Assert(EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { fenceRecord }).Entries.Single().State == EngineeringDistanceApprovalHistoryStates.Active && EngineeringDistanceApprovalCurrentnessValidator.Evaluate(fenceRecord, "C:\\P92A", fence, context.ClauseRegistry, context.SourceRegistry).Status == EngineeringDistanceApprovalCurrentnessStates.Current && EngineeringDistanceApprovalCurrentnessValidator.Evaluate(fenceRecord, "C:\\P92A", fence, context.ClauseRegistry, context.SourceRegistry).Reasons.Count == 0, "P9.2A fence was not explicitly ACTIVE and CURRENT.");
            Assert(EngineeringDistanceApprovalCurrentnessValidator.Evaluate(a, "C:\\P92A-OTHER", changedValue, changedShaClauses, changedShaSources).Reasons.SequenceEqual(new[] { EngineeringDistanceApprovalCurrentnessReasons.ProjectScopeChanged, EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged, EngineeringDistanceApprovalCurrentnessReasons.EvidenceBindingsChanged }), "P9.2A multi-reason stale ordering failed.");
            EngineeringDistanceApprovalHistoryResolution independent = EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, fenceRecord, EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A-OTHER", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92A-P" }, "Approver", null, timestamp, "P") });
            Assert(independent.Entries.Count == 3 && independent.Entries.All(x => x.State == EngineeringDistanceApprovalHistoryStates.Active), "P9.2A independent project/candidate heads failed.");
            bool duplicate = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, a }); } catch (EngineeringDistanceApprovalHistoryValidationException) { duplicate = true; } Assert(duplicate, "P9.2A duplicate approval ID was accepted.");
            bool missing = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp, null, new string('A', 64)) }); } catch (EngineeringDistanceApprovalHistoryValidationException) { missing = true; } Assert(missing, "P9.2A missing supersession target was accepted.");
            bool crossProject = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A-OTHER", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp, null, a.ApprovalRecordId) }); } catch (EngineeringDistanceApprovalHistoryValidationException) { crossProject = true; } Assert(crossProject, "P9.2A cross-project supersession was accepted.");
            bool crossCandidate = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", fence, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = fence.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp, null, a.ApprovalRecordId) }); } catch (EngineeringDistanceApprovalHistoryValidationException) { crossCandidate = true; } Assert(crossCandidate, "P9.2A cross-candidate supersession was accepted.");
            bool branch = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, b, EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp.AddMilliseconds(3), "branch", a.ApprovalRecordId) }); } catch (EngineeringDistanceApprovalHistoryValidationException) { branch = true; } Assert(branch, "P9.2A supersession branch was accepted.");
            bool multipleHeads = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { a, EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "PENDING" }, "Approver", null, timestamp.AddMilliseconds(4), "independent") }); } catch (EngineeringDistanceApprovalHistoryValidationException) { multipleHeads = true; } Assert(multipleHeads, "P9.2A multiple active heads were accepted.");
            EngineeringDistanceApprovalRecord cycleA = new EngineeringDistanceApprovalRecord { SchemaVersion = a.SchemaVersion, ApprovalRecordId = new string('C', 64), ProjectDirectory = a.ProjectDirectory, CandidateBinding = a.CandidateBinding, Decision = a.Decision, ApproverDisplayName = a.ApproverDisplayName, DecisionTimestampUtc = a.DecisionTimestampUtc, EvidenceBindings = a.EvidenceBindings };
            EngineeringDistanceApprovalRecord cycleB = new EngineeringDistanceApprovalRecord { SchemaVersion = a.SchemaVersion, ApprovalRecordId = new string('D', 64), ProjectDirectory = a.ProjectDirectory, CandidateBinding = a.CandidateBinding, Decision = a.Decision, ApproverDisplayName = a.ApproverDisplayName, DecisionTimestampUtc = a.DecisionTimestampUtc, EvidenceBindings = a.EvidenceBindings };
            cycleA.SupersedesApprovalRecordId = cycleB.ApprovalRecordId; cycleB.SupersedesApprovalRecordId = cycleA.ApprovalRecordId;
            bool cycle = false; try { EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { cycleA, cycleB }); } catch (EngineeringDistanceApprovalHistoryValidationException) { cycle = true; } Assert(cycle, "P9.2A supersession cycle was accepted.");
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92A-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string absent = System.IO.Path.Combine(directory, "absent");
            try { Assert(EngineeringDistanceApprovalArtifactCatalog.Load(absent, "C:\\P92A").Count == 0, "P9.2A absent catalog directory did not return empty."); EngineeringDistanceApprovalRecord otherProject = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92A-OTHER", road, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = road.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92A-O" }, "Approver", null, timestamp, "O"); EngineeringDistanceApprovalRecordFileStore.Write(directory, a); EngineeringDistanceApprovalRecordFileStore.Write(directory, fenceRecord); EngineeringDistanceApprovalRecordFileStore.Write(directory, otherProject); Assert(EngineeringDistanceApprovalArtifactCatalog.Load(directory, "C:\\P92A").Select(x => x.ApprovalRecordId).SequenceEqual(new[] { a, fenceRecord }.OrderBy(x => x.CandidateBinding.CandidateId, StringComparer.Ordinal).ThenBy(x => x.ApprovalRecordId, StringComparer.Ordinal).Select(x => x.ApprovalRecordId)), "P9.2A catalog ordering/filter failed."); }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
            string duplicateDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92AD-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { string original = EngineeringDistanceApprovalRecordFileStore.Write(duplicateDirectory, a); System.IO.File.Copy(original, System.IO.Path.Combine(duplicateDirectory, "duplicate.engineering-distance.approval.json")); bool duplicateArtifact = false; try { EngineeringDistanceApprovalArtifactCatalog.Load(duplicateDirectory, "C:\\P92A"); } catch (EngineeringDistanceApprovalArtifactCatalogException) { duplicateArtifact = true; } Assert(duplicateArtifact, "P9.2A catalog accepted duplicate ApprovalRecordId."); }
            finally { if (System.IO.Directory.Exists(duplicateDirectory)) System.IO.Directory.Delete(duplicateDirectory, true); }
            string malformedDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92AM-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { System.IO.Directory.CreateDirectory(malformedDirectory); System.IO.File.WriteAllText(System.IO.Path.Combine(malformedDirectory, "malformed.engineering-distance.approval.json"), "not-json"); bool malformed = false; try { EngineeringDistanceApprovalArtifactCatalog.Load(malformedDirectory, "C:\\P92A"); } catch (EngineeringDistanceApprovalArtifactCatalogException) { malformed = true; } Assert(malformed, "P9.2A catalog silently ignored malformed approval artifact."); }
            finally { if (System.IO.Directory.Exists(malformedDirectory)) System.IO.Directory.Delete(malformedDirectory, true); }
        }

        private static EngineeringDistanceCandidateDefinition CopyP92Candidate(EngineeringDistanceCandidateDefinition source) => new EngineeringDistanceCandidateDefinition { CandidateId = source.CandidateId, PackId = source.PackId, RuleId = source.RuleId, Title = source.Title, MeasurementKind = source.MeasurementKind, ProposedMinimumDistanceMeters = source.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = source.RequiredMeasurementBasisKey, RequiredApplicabilityKey = source.RequiredApplicabilityKey, ApprovalStatus = source.ApprovalStatus, ClauseIds = source.ClauseIds.ToList() };

        private static void RunP92B()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            StandardAuthorityPolicyRegistry sourcePolicies = StandardAuthorityPolicyRegistry.Create(context.SourceRegistry, context.ClauseRegistry, new[] { new StandardAuthorityPolicyDefinition { PolicyId="p92b-pcd", SourceId="trc.pcd-pd-135.rev1", AuthorityClass=StandardAuthorityClasses.GoverningProject, EnforcementMode=StandardEnforcementModes.ConditionalReview, Rationale="test" }, new StandardAuthorityPolicyDefinition { PolicyId="p92b-ips", SourceId="ips.e-pr-190.rev1.2009", AuthorityClass=StandardAuthorityClasses.LegacyConflict, EnforcementMode=StandardEnforcementModes.NeverAutoEnforce, Rationale="test" } });
            foreach (EngineeringDistanceCandidateDefinition candidate in EngineeringDistanceCandidateCatalog.BuiltInCandidates)
            {
                EngineeringDistanceApprovalRecord approval = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92B", candidate, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId=candidate.CandidateId, DecisionStatus="APPROVED", ApprovalReference="P92B-"+candidate.CandidateId }, "Approver", null, new DateTime(2026,9,10,12,0,0,DateTimeKind.Utc));
                EngineeringDistanceApprovalHistoryEntry active = EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { approval }).Entries.Single();
                EngineeringDistanceApprovalCurrentnessResult current = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(approval,"C:\\P92B",candidate,context.ClauseRegistry,context.SourceRegistry);
                Assert(EngineeringDistanceApprovalAuthorityResolver.Resolve(approval,active,current,EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new EngineeringDistanceApprovalAuthorityPolicyDefinition[0]),context.ClauseRegistry,context.SourceRegistry,sourcePolicies).State==EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy,"P9.2B no-policy state failed.");
                EngineeringDistanceApprovalAuthorityPolicyDefinition unresolved = P92BPolicy(approval,StandardEnforcementModes.ConditionalReview,EngineeringDistanceApprovalLegacyConflictDispositions.Unresolved);
                Assert(EngineeringDistanceApprovalAuthorityResolver.Resolve(approval,active,current,EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[]{unresolved}),context.ClauseRegistry,context.SourceRegistry,sourcePolicies).State==EngineeringDistanceApprovalAuthorityStates.LegacyConflictUnresolved,"P9.2B unresolved legacy state failed.");
                EngineeringDistanceApprovalAuthorityPolicyDefinition conditional = P92BPolicy(approval,StandardEnforcementModes.ConditionalReview,EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
                EngineeringDistanceApprovalAuthorityResult conditionalResult = EngineeringDistanceApprovalAuthorityResolver.Resolve(approval,active,current,EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[]{conditional}),context.ClauseRegistry,context.SourceRegistry,sourcePolicies);
                Assert(conditionalResult.State==EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional && conditionalResult.AuthorityClass==StandardAuthorityClasses.GoverningProject && conditionalResult.EffectiveEnforcementMode==StandardEnforcementModes.ConditionalReview && conditionalResult.PolicyId==conditional.PolicyId && conditionalResult.AuthorityBasisReference==conditional.AuthorityBasisReference,"P9.2B conditional project authority provenance failed.");
                EngineeringDistanceApprovalAuthorityPolicyDefinition deterministic = P92BPolicy(approval,StandardEnforcementModes.Deterministic,EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
                EngineeringDistanceApprovalAuthorityResult deterministicResult = EngineeringDistanceApprovalAuthorityResolver.Resolve(approval,active,current,EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[]{deterministic}),context.ClauseRegistry,context.SourceRegistry,sourcePolicies);
                Assert(deterministicResult.State==EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic && deterministicResult.AuthorityClass==StandardAuthorityClasses.GoverningProject && deterministicResult.EffectiveEnforcementMode==StandardEnforcementModes.Deterministic && deterministicResult.PolicyId==deterministic.PolicyId && deterministicResult.AuthorityBasisReference==deterministic.AuthorityBasisReference,"P9.2B deterministic project authority provenance failed.");
            }

            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceApprovalRecord roadApproval = P92BApproval("C:\\P92B", road, context, "APPROVED", "P92B-C2", "Display Only", null);
            EngineeringDistanceApprovalHistoryEntry roadActive = EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { roadApproval }).Entries.Single();
            EngineeringDistanceApprovalCurrentnessResult roadCurrent = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(roadApproval, "C:\\P92B", road, context.ClauseRegistry, context.SourceRegistry);
            EngineeringDistanceApprovalAuthorityPolicyDefinition policy = P92BPolicy(roadApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
            EngineeringDistanceApprovalAuthorityPolicyRegistry registry = EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { policy });
            EngineeringDistanceApprovalAuthorityResult resolved = P92BResolve(roadApproval, roadActive, roadCurrent, registry, context, sourcePolicies);
            Assert(resolved.State == EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic && resolved.AuthorityClass == StandardAuthorityClasses.GoverningProject, "P9.2B C2 valid deterministic authority was not resolved.");

            EngineeringDistanceApprovalHistoryEntry superseded = new EngineeringDistanceApprovalHistoryEntry { ApprovalRecordId = roadApproval.ApprovalRecordId, ProjectDirectory = roadApproval.ProjectDirectory, CandidateId = roadApproval.CandidateBinding.CandidateId, State = EngineeringDistanceApprovalHistoryStates.Superseded, SupersededByApprovalRecordId = new string('B', 64) };
            EngineeringDistanceApprovalAuthorityResult supersededResult = P92BResolve(roadApproval, superseded, roadCurrent, registry, context, sourcePolicies);
            Assert(supersededResult.State == EngineeringDistanceApprovalAuthorityStates.InactiveApproval && supersededResult.AuthorityClass != StandardAuthorityClasses.GoverningProject, "P9.2B C2 superseded approval was not blocked.");
            foreach (string status in new[] { "PENDING", "REJECTED", "REFERENCE_ONLY" })
            {
                EngineeringDistanceApprovalRecord nonApproved = P92BApproval("C:\\P92B", road, context, status, null, "Approver", null);
                EngineeringDistanceApprovalAuthorityResult result = P92BResolve(nonApproved, EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { nonApproved }).Entries.Single(), EngineeringDistanceApprovalCurrentnessValidator.Evaluate(nonApproved, "C:\\P92B", road, context.ClauseRegistry, context.SourceRegistry), EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new EngineeringDistanceApprovalAuthorityPolicyDefinition[0]), context, sourcePolicies);
                Assert(result.State == EngineeringDistanceApprovalAuthorityStates.NotApproved && result.AuthorityClass != StandardAuthorityClasses.GoverningProject, "P9.2B C2 non-approved state was not blocked: " + status);
            }
            EngineeringDistanceApprovalCurrentnessResult stale = new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Stale, Reasons = new List<string> { EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged } };
            EngineeringDistanceApprovalAuthorityResult staleResult = P92BResolve(roadApproval, roadActive, stale, registry, context, sourcePolicies);
            Assert(staleResult.State == EngineeringDistanceApprovalAuthorityStates.StaleApproval && staleResult.AuthorityClass != StandardAuthorityClasses.GoverningProject, "P9.2B C2 stale approval was not blocked.");

            EngineeringDistanceApprovalAuthorityPolicyDefinition wrongProject = P92BCopyPolicy(policy); wrongProject.ProjectDirectory = "C:\\P92B-OTHER";
            EngineeringDistanceApprovalAuthorityPolicyDefinition wrongCandidate = P92BCopyPolicy(policy); wrongCandidate.CandidateId = "candidate.other";
            EngineeringDistanceApprovalAuthorityPolicyDefinition wrongRecord = P92BCopyPolicy(policy); wrongRecord.ApprovalRecordId = new string('A', 64);
            EngineeringDistanceApprovalAuthorityPolicyDefinition wrongReference = P92BCopyPolicy(policy); wrongReference.ApprovalReference = "OTHER-REFERENCE";
            Assert(P92BResolve(roadApproval, roadActive, roadCurrent, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { wrongProject }), context, sourcePolicies).State == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy, "P9.2B C2 wrong project policy was selected.");
            Assert(P92BResolve(roadApproval, roadActive, roadCurrent, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { wrongCandidate }), context, sourcePolicies).State == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy, "P9.2B C2 wrong candidate policy was selected.");
            Assert(P92BResolve(roadApproval, roadActive, roadCurrent, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { wrongRecord }), context, sourcePolicies).State == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy, "P9.2B C2 wrong approval-record policy was selected.");
            Assert(P92BResolve(roadApproval, roadActive, roadCurrent, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { wrongReference }), context, sourcePolicies).State == EngineeringDistanceApprovalAuthorityStates.AuthorityPolicyMismatch, "P9.2B C2 approval-reference mismatch was not blocked.");

            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.PolicyId = ""), "blank PolicyId");
            P92BAssertPolicyRejected(new[] { P92BCopyPolicy(policy), P92BCopyPolicy(policy, x => x.ApprovalRecordId = new string('E', 64)) }, "duplicate PolicyId");
            P92BAssertPolicyRejected(new[] { P92BCopyPolicy(policy), P92BCopyPolicy(policy, x => x.PolicyId = "duplicate") }, "duplicate exact key");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.CandidateId = ""), "blank CandidateId");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.ApprovalRecordId = "not-a-sha"), "invalid ApprovalRecordId");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.ApprovalReference = ""), "blank ApprovalReference");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.AuthorityBasisReference = ""), "blank AuthorityBasisReference");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.Rationale = ""), "blank Rationale");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.EnforcementMode = StandardEnforcementModes.Advisory), "advisory enforcement");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.EnforcementMode = StandardEnforcementModes.NeverAutoEnforce), "never-auto enforcement");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.EnforcementMode = "UNSUPPORTED"), "unsupported enforcement");
            P92BAssertPolicyRejected(P92BCopyPolicy(policy, x => x.LegacyConflictDisposition = "UNSUPPORTED"), "unsupported legacy disposition");
            Assert(EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { P92BCopyPolicy(policy, x => x.EnforcementMode = StandardEnforcementModes.ConditionalReview), P92BCopyPolicy(policy, x => { x.PolicyId = "p92b-distinct"; x.ApprovalRecordId = new string('C', 64); x.LegacyConflictDisposition = EngineeringDistanceApprovalLegacyConflictDispositions.Unresolved; }) }).Policies.Count == 2, "P9.2B C2 accepted policy values were rejected.");

            P92BAssertQuarantineHardBlock();
            EngineeringDistanceApprovalAuthorityResult displayOnly = P92BResolve(roadApproval, roadActive, roadCurrent, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new EngineeringDistanceApprovalAuthorityPolicyDefinition[0]), context, sourcePolicies);
            EngineeringDistanceApprovalRecord identityOnly = P92BApproval("C:\\P92B", road, context, "APPROVED", "P92B-C2-IDENTITY", "Other Display", "other-authenticated-identity");
            Assert(displayOnly.State == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy && P92BResolve(identityOnly, EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { identityOnly }).Entries.Single(), EngineeringDistanceApprovalCurrentnessValidator.Evaluate(identityOnly, "C:\\P92B", road, context.ClauseRegistry, context.SourceRegistry), EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new EngineeringDistanceApprovalAuthorityPolicyDefinition[0]), context, sourcePolicies).State == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy, "P9.2B C2 reviewer identity granted authority without policy.");

            string approvalId = roadApproval.ApprovalRecordId; string approvalContent = EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(roadApproval); string historyState = roadActive.State; string historyProject = roadActive.ProjectDirectory; string historyCandidate = roadActive.CandidateId; string? historySupersededBy = roadActive.SupersededByApprovalRecordId; string currentState = roadCurrent.Status; string currentReasons = string.Join("|", roadCurrent.Reasons); EngineeringDistanceApprovalAuthorityPolicyDefinition originalPolicy = P92BCopyPolicy(policy); string originalRationale = policy.Rationale;
            P92BResolve(roadApproval, roadActive, roadCurrent, registry, context, sourcePolicies);
            Assert(roadApproval.ApprovalRecordId == approvalId && EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(roadApproval) == approvalContent && roadActive.ApprovalRecordId == approvalId && roadActive.State == historyState && roadActive.ProjectDirectory == historyProject && roadActive.CandidateId == historyCandidate && roadActive.SupersededByApprovalRecordId == historySupersededBy && roadCurrent.Status == currentState && string.Join("|", roadCurrent.Reasons) == currentReasons && policy.PolicyId == originalPolicy.PolicyId && policy.ProjectDirectory == originalPolicy.ProjectDirectory && policy.CandidateId == originalPolicy.CandidateId && policy.ApprovalRecordId == originalPolicy.ApprovalRecordId && policy.ApprovalReference == originalPolicy.ApprovalReference && policy.AuthorityBasisReference == originalPolicy.AuthorityBasisReference && policy.EnforcementMode == originalPolicy.EnforcementMode && policy.LegacyConflictDisposition == originalPolicy.LegacyConflictDisposition && policy.Rationale == originalRationale, "P9.2B C2 resolver mutated caller inputs.");
            policy.Rationale = "mutated caller";
            EngineeringDistanceApprovalAuthorityPolicyDefinition registryPolicy = registry.Policies.Single();
            Assert(registryPolicy.Rationale == originalRationale, "P9.2B C2 registry did not isolate its input policy.");
            registryPolicy.Rationale = "mutated output";
            Assert(registry.Policies.Single().Rationale == originalRationale, "P9.2B C2 registry did not isolate returned policy copies.");
        }

        private static EngineeringDistanceApprovalAuthorityPolicyDefinition P92BPolicy(EngineeringDistanceApprovalRecord approval,string mode,string disposition) => new EngineeringDistanceApprovalAuthorityPolicyDefinition { PolicyId="p92b-"+approval.ApprovalRecordId, ProjectDirectory=approval.ProjectDirectory, CandidateId=approval.CandidateBinding.CandidateId, ApprovalRecordId=approval.ApprovalRecordId, ApprovalReference=approval.Decision.ApprovalReference!, AuthorityBasisReference="PROJECT-GOV-1", EnforcementMode=mode, LegacyConflictDisposition=disposition, Rationale="controlled project authority" };

        private static EngineeringDistanceApprovalRecord P92BApproval(string project, EngineeringDistanceCandidateDefinition candidate, ProjectEngineeringDistanceReferenceContext context, string status, string? reference, string displayName, string? identity) => EngineeringDistanceApprovalRecordBuilder.Create(project, candidate, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = candidate.CandidateId, DecisionStatus = status, ApprovalReference = reference }, displayName, identity, new DateTime(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc));

        private static EngineeringDistanceApprovalAuthorityResult P92BResolve(EngineeringDistanceApprovalRecord approval, EngineeringDistanceApprovalHistoryEntry history, EngineeringDistanceApprovalCurrentnessResult currentness, EngineeringDistanceApprovalAuthorityPolicyRegistry policies, ProjectEngineeringDistanceReferenceContext context, StandardAuthorityPolicyRegistry sourcePolicies) => EngineeringDistanceApprovalAuthorityResolver.Resolve(approval, history, currentness, policies, context.ClauseRegistry, context.SourceRegistry, sourcePolicies);

        private static EngineeringDistanceApprovalAuthorityPolicyDefinition P92BCopyPolicy(EngineeringDistanceApprovalAuthorityPolicyDefinition source, Action<EngineeringDistanceApprovalAuthorityPolicyDefinition>? mutate = null) { EngineeringDistanceApprovalAuthorityPolicyDefinition copy = new EngineeringDistanceApprovalAuthorityPolicyDefinition { PolicyId=source.PolicyId, ProjectDirectory=source.ProjectDirectory, CandidateId=source.CandidateId, ApprovalRecordId=source.ApprovalRecordId, ApprovalReference=source.ApprovalReference, AuthorityBasisReference=source.AuthorityBasisReference, EnforcementMode=source.EnforcementMode, LegacyConflictDisposition=source.LegacyConflictDisposition, Rationale=source.Rationale }; if (mutate != null) mutate(copy); return copy; }

        private static void P92BAssertPolicyRejected(EngineeringDistanceApprovalAuthorityPolicyDefinition policy, string scenario) => P92BAssertPolicyRejected(new[] { policy }, scenario);
        private static void P92BAssertPolicyRejected(IEnumerable<EngineeringDistanceApprovalAuthorityPolicyDefinition> policies, string scenario) { try { EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(policies); } catch (EngineeringDistanceApprovalAuthorityPolicyRegistryValidationException) { return; } throw new InvalidOperationException("P9.2B C2 policy registry accepted " + scenario + "."); }

        private static void P92BAssertQuarantineHardBlock()
        {
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "p92b.quarantined", DocumentNumber = "P92B-Q", Title = "Quarantined", Issuer = "Test", RevisionOrEdition = "1", SourceSha256 = new string('D', 64) } });
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { new StandardClauseDefinition { ClauseId = "p92b.quarantined.1", SourceId = "p92b.quarantined", ClauseReference = "1", ClauseText = "Synthetic quarantine evidence" } });
            EngineeringDistanceCandidateDefinition candidate = new EngineeringDistanceCandidateDefinition { CandidateId = "candidate.p92b.quarantined", PackId = "pack.p92b.quarantined", RuleId = "rule.p92b.quarantined", Title = "Synthetic quarantine candidate", MeasurementKind = "EquipmentToRoad2D", ProposedMinimumDistanceMeters = 1m, RequiredMeasurementBasisKey = "AccessRoadEdge", RequiredApplicabilityKey = "ProcessingEquipment", ApprovalStatus = "PENDING_ENGINEERING_APPROVAL", ClauseIds = new List<string> { "p92b.quarantined.1" } };
            EngineeringDistanceApprovalRecord approval = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92B-Q", candidate, clauses, sources, new EngineeringDistanceApprovalDecision { CandidateId = candidate.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92B-Q" }, "Approver", null, new DateTime(2026, 9, 10, 14, 0, 0, DateTimeKind.Utc));
            EngineeringDistanceApprovalHistoryEntry active = EngineeringDistanceApprovalHistoryResolver.Resolve(new[] { approval }).Entries.Single();
            EngineeringDistanceApprovalCurrentnessResult current = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(approval, "C:\\P92B-Q", candidate, clauses, sources);
            EngineeringDistanceApprovalAuthorityPolicyDefinition policy = P92BPolicy(approval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
            StandardAuthorityPolicyRegistry sourcePolicies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { new StandardAuthorityPolicyDefinition { PolicyId = "p92b-quarantine", SourceId = "p92b.quarantined", AuthorityClass = StandardAuthorityClasses.Quarantined, EnforcementMode = StandardEnforcementModes.NeverAutoEnforce, Rationale = "synthetic quarantine" } });
            EngineeringDistanceApprovalAuthorityResult result = EngineeringDistanceApprovalAuthorityResolver.Resolve(approval, active, current, EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { policy }), clauses, sources, sourcePolicies);
            Assert(result.State == EngineeringDistanceApprovalAuthorityStates.SourceQuarantined && result.AuthorityClass != StandardAuthorityClasses.GoverningProject && result.State != EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic, "P9.2B C2 project authority bypassed a quarantined source.");
        }

        private static void RunP92C2A()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceApprovalRecord approval = P92BApproval("C:\\P92C2A", road, context, "APPROVED", "P92C2A-ROAD", "Approver", null);
            EngineeringDistanceApprovalAuthorityPolicyDefinition policy = P92BPolicy(approval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
            using (System.Security.Cryptography.RSA signer = System.Security.Cryptography.RSA.Create(2048))
            using (System.Security.Cryptography.RSA untrustedSigner = System.Security.Cryptography.RSA.Create(2048))
            {
                EngineeringDistanceApprovalAuthorityTrustedKeyDefinition originalKey = P92C2ATrustedKey(signer, "issuer.p92c2a", "key.1");
                EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { originalKey });
                originalKey.IssuerId = "changed";
                Assert(trusted.TrustedKeys.Single().IssuerId == "issuer.p92c2a", "P9.2C2A trusted-key input isolation failed.");
                trusted.TryGet("issuer.p92c2a", "key.1", out EngineeringDistanceApprovalAuthorityTrustedKeyDefinition? returnedKey);
                returnedKey!.KeyId = "changed";
                Assert(trusted.TrustedKeys.Single().KeyId == "key.1", "P9.2C2A trusted-key output isolation failed.");

                EngineeringDistanceApprovalAuthorityPolicyArtifact artifact = P92C2AArtifact(signer, policy, "issuer.p92c2a", "key.1", new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc));
                EngineeringDistanceApprovalAuthorityPolicyArtifact verified = EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(artifact, trusted);
                Assert(verified.PolicyArtifactId == EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact) && verified.Policy!.PolicyId == policy.PolicyId && EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(new[] { verified.Policy }).Policies.Single().PolicyId == policy.PolicyId, "P9.2C2A valid signed policy was not accepted or projected.");
                string artifactIdentity = artifact.PolicyArtifactId + "|" + artifact.Policy!.PolicyId + "|" + artifact.SignatureBase64;
                verified.Policy!.PolicyId = "changed";
                Assert(artifactIdentity == artifact.PolicyArtifactId + "|" + artifact.Policy!.PolicyId + "|" + artifact.SignatureBase64, "P9.2C2A artifact validation mutated caller-owned content.");

                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.Policy!.PolicyId = "changed"), trusted), "PolicyId tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.Policy!.ApprovalRecordId = new string('A', 64)), trusted), "ApprovalRecordId tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.Policy!.ApprovalReference = "changed"), trusted), "ApprovalReference tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.Policy!.AuthorityBasisReference = "changed"), trusted), "AuthorityBasisReference tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.Policy!.ProjectDirectory = "C:\\OTHER"), trusted), "ProjectDirectory tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.IssuerId = "issuer.other"), trusted), "IssuerId tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.KeyId = "key.other"), trusted), "KeyId tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.SignatureAlgorithm = "OTHER"), trusted), "SignatureAlgorithm tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.SignatureBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 })), trusted), "signature tamper");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AArtifact(untrustedSigner, policy, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc), trusted), "untrusted signing key");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AArtifact(signer, policy, "issuer.unknown", "key.1", artifact.IssuedAtUtc), trusted), "unknown issuer");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AArtifact(signer, policy, "issuer.p92c2a", "key.unknown", artifact.IssuedAtUtc), trusted), "unknown key");
                EngineeringDistanceApprovalAuthorityPolicyDefinition nonNormalized = P92BCopyPolicy(policy); nonNormalized.ProjectDirectory = "C:\\P92C2A\\";
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AArtifact(signer, nonNormalized, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc), trusted), "non-normalized project");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.PolicyArtifactId = new string('A', 64)), trusted), "wrong PolicyArtifactId");
                P92C2AReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(P92C2AAlter(artifact, x => x.SignatureBase64 = string.Empty), trusted), "unsigned artifact");
                bool smallKey = false; using (System.Security.Cryptography.RSA small = System.Security.Cryptography.RSA.Create(1024)) { try { EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { P92C2ATrustedKey(small, "issuer.small", "key.1") }); } catch (EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException) { smallKey = true; } }
                bool duplicateKey = false; try { EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { P92C2ATrustedKey(signer, "issuer.p92c2a", "key.1"), P92C2ATrustedKey(signer, "issuer.p92c2a", "key.1") }); } catch (EngineeringDistanceApprovalAuthorityTrustedIssuerRegistryValidationException) { duplicateKey = true; }
                Assert(smallKey && duplicateKey, "P9.2C2A trusted-key size or duplicate validation failed.");

                string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92C2A-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    Assert(EngineeringDistanceApprovalAuthorityTrustStore.Load(System.IO.Path.Combine(root, "absent.json")).TrustedKeys.Count == 0 && EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(System.IO.Path.Combine(root, "absent"), policy.ProjectDirectory, trusted).Artifacts.Count == 0, "P9.2C2A absent trust store or catalog did not fail closed to empty.");
                    System.IO.Directory.CreateDirectory(root);
                    string malformedTrust = System.IO.Path.Combine(root, "malformed-trust.json"); System.IO.File.WriteAllText(malformedTrust, "not-json");
                    bool malformedTrustRejected = false; try { EngineeringDistanceApprovalAuthorityTrustStore.Load(malformedTrust); } catch (EngineeringDistanceApprovalAuthorityTrustStoreException) { malformedTrustRejected = true; }
                    Assert(malformedTrustRejected, "P9.2C2A malformed trust store was accepted.");

                    string catalogDirectory = System.IO.Path.Combine(root, "catalog"); System.IO.Directory.CreateDirectory(catalogDirectory);
                    EngineeringDistanceApprovalAuthorityPolicyDefinition otherPolicy = P92BCopyPolicy(policy); otherPolicy.ProjectDirectory = "C:\\P92C2A-OTHER";
                    P92C2AWrite(System.IO.Path.Combine(catalogDirectory, "other" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), P92C2AArtifact(signer, otherPolicy, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc));
                    P92C2AWrite(System.IO.Path.Combine(catalogDirectory, "valid" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), artifact);
                    EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog catalog = EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(catalogDirectory, policy.ProjectDirectory, trusted);
                    EngineeringDistanceApprovalAuthorityPolicyDefinition projected = catalog.Policies.Single(); projected.Rationale = "changed";
                    Assert(catalog.Artifacts.Count == 1 && catalog.Policies.Single().Rationale == policy.Rationale && catalog.CreatePolicyRegistry().Policies.Single().PolicyId == policy.PolicyId, "P9.2C2A verified catalog projection was not detached.");

                    string malformedDirectory = System.IO.Path.Combine(root, "malformed"); System.IO.Directory.CreateDirectory(malformedDirectory); System.IO.File.WriteAllText(System.IO.Path.Combine(malformedDirectory, "bad" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), "not-json");
                    P92C2ACatalogReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(malformedDirectory, policy.ProjectDirectory, trusted), "malformed artifact");
                    string duplicateIdDirectory = System.IO.Path.Combine(root, "duplicate-id"); System.IO.Directory.CreateDirectory(duplicateIdDirectory); P92C2AWrite(System.IO.Path.Combine(duplicateIdDirectory, "one" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), artifact); P92C2AWrite(System.IO.Path.Combine(duplicateIdDirectory, "two" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), artifact); P92C2ACatalogReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(duplicateIdDirectory, policy.ProjectDirectory, trusted), "duplicate artifact ID");
                    string duplicatePolicyDirectory = System.IO.Path.Combine(root, "duplicate-policy"); System.IO.Directory.CreateDirectory(duplicatePolicyDirectory); EngineeringDistanceApprovalAuthorityPolicyDefinition secondPolicy = P92BCopyPolicy(policy); secondPolicy.PolicyId = policy.PolicyId + "-second"; P92C2AWrite(System.IO.Path.Combine(duplicatePolicyDirectory, "one" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), artifact); P92C2AWrite(System.IO.Path.Combine(duplicatePolicyDirectory, "two" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), P92C2AArtifact(signer, secondPolicy, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc.AddMilliseconds(1))); P92C2ACatalogReject(() => EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(duplicatePolicyDirectory, policy.ProjectDirectory, trusted), "duplicate policy key");
                    string orderingDirectory = System.IO.Path.Combine(root, "ordering"); System.IO.Directory.CreateDirectory(orderingDirectory);
                    EngineeringDistanceApprovalAuthorityPolicyDefinition a = P92BCopyPolicy(policy); a.PolicyId = "policy.a"; a.CandidateId = "candidate.a"; a.ApprovalRecordId = new string('A', 64);
                    EngineeringDistanceApprovalAuthorityPolicyDefinition z = P92BCopyPolicy(policy); z.PolicyId = "policy.z"; z.CandidateId = "candidate.z"; z.ApprovalRecordId = new string('B', 64);
                    P92C2AWrite(System.IO.Path.Combine(orderingDirectory, "z" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), P92C2AArtifact(signer, z, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc.AddMilliseconds(2)));
                    P92C2AWrite(System.IO.Path.Combine(orderingDirectory, "a" + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), P92C2AArtifact(signer, a, "issuer.p92c2a", "key.1", artifact.IssuedAtUtc.AddMilliseconds(3)));
                    Assert(EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(orderingDirectory, policy.ProjectDirectory, trusted).Policies.Select(x => x.CandidateId).SequenceEqual(new[] { "candidate.a", "candidate.z" }), "P9.2C2A catalog ordering was not deterministic.");
                }
                finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
            }
        }

        private static EngineeringDistanceApprovalAuthorityTrustedKeyDefinition P92C2ATrustedKey(System.Security.Cryptography.RSA rsa, string issuerId, string keyId) { System.Security.Cryptography.RSAParameters parameters = rsa.ExportParameters(false); return new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition { IssuerId = issuerId, KeyId = keyId, SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm, RsaModulusBase64 = Convert.ToBase64String(parameters.Modulus!), RsaExponentBase64 = Convert.ToBase64String(parameters.Exponent!) }; }
        private static EngineeringDistanceApprovalAuthorityPolicyArtifact P92C2AArtifact(System.Security.Cryptography.RSA signer, EngineeringDistanceApprovalAuthorityPolicyDefinition policy, string issuerId, string keyId, DateTime issuedAtUtc) { var artifact = new EngineeringDistanceApprovalAuthorityPolicyArtifact { SchemaVersion = EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.SchemaVersion, IssuerId = issuerId, KeyId = keyId, IssuedAtUtc = new DateTime(issuedAtUtc.Ticks - issuedAtUtc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc), SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm, Policy = P92BCopyPolicy(policy) }; artifact.PolicyArtifactId = EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact); artifact.SignatureBase64 = Convert.ToBase64String(signer.SignData(EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)); return artifact; }
        private static EngineeringDistanceApprovalAuthorityPolicyArtifact P92C2AAlter(EngineeringDistanceApprovalAuthorityPolicyArtifact source, Action<EngineeringDistanceApprovalAuthorityPolicyArtifact> mutate) { var copy = new EngineeringDistanceApprovalAuthorityPolicyArtifact { SchemaVersion = source.SchemaVersion, PolicyArtifactId = source.PolicyArtifactId, IssuerId = source.IssuerId, KeyId = source.KeyId, IssuedAtUtc = source.IssuedAtUtc, SignatureAlgorithm = source.SignatureAlgorithm, Policy = P92BCopyPolicy(source.Policy!), SignatureBase64 = source.SignatureBase64 }; mutate(copy); return copy; }
        private static void P92C2AWrite(string path, EngineeringDistanceApprovalAuthorityPolicyArtifact artifact) { using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(EngineeringDistanceApprovalAuthorityPolicyArtifact)).WriteObject(stream, artifact); }
        private static void P92C2AReject(Action action, string scenario) { try { action(); } catch (EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException) { return; } throw new InvalidOperationException("P9.2C2A accepted " + scenario + "."); }
        private static void P92C2ACatalogReject(Action action, string scenario) { try { action(); } catch (EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException) { return; } throw new InvalidOperationException("P9.2C2A catalog accepted " + scenario + "."); }

        private static void RunP92C2B()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            StandardAuthorityPolicyRegistry standardPolicies = StandardAuthorityPolicyRegistry.Create(context.SourceRegistry, context.ClauseRegistry, new[] { new StandardAuthorityPolicyDefinition { PolicyId = "p92c2b-pcd", SourceId = "trc.pcd-pd-135.rev1", AuthorityClass = StandardAuthorityClasses.GoverningProject, EnforcementMode = StandardEnforcementModes.ConditionalReview, Rationale = "test" }, new StandardAuthorityPolicyDefinition { PolicyId = "p92c2b-ips", SourceId = "ips.e-pr-190.rev1.2009", AuthorityClass = StandardAuthorityClasses.LegacyConflict, EnforcementMode = StandardEnforcementModes.NeverAutoEnforce, Rationale = "test" } });
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = "C:\\P92C2B", AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
            IReadOnlyList<EngineeringDistanceRuleDefinition> rules = ProjectDistanceRuleBinder.Create(criteria);
            EngineeringDistanceApprovalRecord roadApproval = P92BApproval(criteria.ProjectDirectory, road, context, "APPROVED", "P92C2B-ROAD", "Approver", null);
            EngineeringDistanceApprovalRecord fenceApproval = P92BApproval(criteria.ProjectDirectory, fence, context, "APPROVED", "P92C2B-FENCE", "Approver", null);
            using (System.Security.Cryptography.RSA signer = System.Security.Cryptography.RSA.Create(2048))
            {
                EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { P92C2ATrustedKey(signer, "issuer.p92c2b", "key.1") });
                string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92C2B-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog verified = P92C2BCatalog(root, "valid", criteria.ProjectDirectory, trusted, signer, P92BPolicy(roadApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision), P92BPolicy(fenceApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision));
                    ProjectDistanceGovernanceAggregateResult aggregate = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence, road }, new[] { fenceApproval, roadApproval }, verified, context.ClauseRegistry, context.SourceRegistry, standardPolicies);
                    ProjectDistanceGovernanceAggregateItem roadItem = aggregate.Items.Single(x => x.CandidateId == road.CandidateId);
                    ProjectDistanceGovernanceAggregateItem fenceItem = aggregate.Items.Single(x => x.CandidateId == fence.CandidateId);
                    Assert(aggregate.ProjectDirectory == "C:\\P92C2B" && aggregate.Items.Select(x => x.CandidateId).SequenceEqual(new[] { road.CandidateId, fence.CandidateId }.OrderBy(x => x, StringComparer.Ordinal)) && roadItem.AuthorityState == EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic && roadItem.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.SemanticScopeConflict && roadItem.Resolution.State != ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible, "P9.2C2B real Road semantic-scope conflict failed.");
                    Assert(fenceItem.AuthorityState == EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic && fenceItem.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible && fenceItem.ProjectRuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId && fenceItem.Resolution.GovernanceProvenance != null && fenceItem.Resolution.GovernanceProvenance.ProjectRuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId && fenceItem.ApprovalRecordId == fenceApproval.ApprovalRecordId && fenceItem.HistoryState == EngineeringDistanceApprovalHistoryStates.Active && fenceItem.CurrentnessState == EngineeringDistanceApprovalCurrentnessStates.Current && fenceItem.VerifiedPolicyArtifactId != null && fenceItem.VerifiedPolicyIssuerId == "issuer.p92c2b" && fenceItem.VerifiedPolicyKeyId == "key.1", "P9.2C2B real Fence authority, provenance, or verified-policy trace failed.");
                    Assert(rules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary && aggregate.Items.All(x => x.ProjectRuleId != ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId), "P9.2C2B UnitBoundary entered the governed aggregate path.");

                    ProjectDistanceCriteria changedCriteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = criteria.ProjectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 25m, UnitBoundaryMinimumMeters = 15m };
                    Assert(ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, ProjectDistanceRuleBinder.Create(changedCriteria), new[] { fence }, new[] { fenceApproval }, P92C2BCatalog(root, "value", criteria.ProjectDirectory, trusted, signer, P92BPolicy(fenceApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single().Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ValueConflict, "P9.2C2B differing Fence value did not remain a conflict.");
                    EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog empty = P92C2BCatalog(root, "empty", criteria.ProjectDirectory, trusted, signer);
                    ProjectDistanceGovernanceAggregateItem noApproval = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single();
                    Assert(noApproval.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && noApproval.ApprovalRecordId == null && noApproval.HistoryState == null && noApproval.CurrentnessState == null && noApproval.AuthorityState == null && noApproval.VerifiedPolicyArtifactId == null, "P9.2C2B no-approval behavior or trace changed.");
                    ProjectDistanceGovernanceAggregateItem noPolicy = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { fenceApproval }, empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single();
                    Assert(noPolicy.AuthorityState == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy && noPolicy.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && noPolicy.VerifiedPolicyArtifactId == null, "P9.2C2B no-policy behavior changed.");
                    EngineeringDistanceApprovalRecord pending = P92BApproval(criteria.ProjectDirectory, fence, context, "PENDING", null, "Approver", null);
                    ProjectDistanceGovernanceAggregateItem pendingItem = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { pending }, empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single();
                    Assert(pendingItem.AuthorityState == EngineeringDistanceApprovalAuthorityStates.NotApproved && pendingItem.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly, "P9.2C2B pending approval was promoted.");
                    EngineeringDistanceCandidateDefinition staleCandidate = CopyP92Candidate(fence); staleCandidate.ProposedMinimumDistanceMeters = 31m;
                    ProjectDistanceGovernanceAggregateItem staleItem = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { staleCandidate }, new[] { fenceApproval }, P92C2BCatalog(root, "stale", criteria.ProjectDirectory, trusted, signer, P92BPolicy(fenceApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single();
                    Assert(staleItem.CurrentnessState == EngineeringDistanceApprovalCurrentnessStates.Stale && staleItem.AuthorityState == EngineeringDistanceApprovalAuthorityStates.StaleApproval && staleItem.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly, "P9.2C2B stale approval was promoted.");
                    ProjectDistanceGovernanceAggregateItem conditional = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { fenceApproval }, P92C2BCatalog(root, "conditional", criteria.ProjectDirectory, trusted, signer, P92BPolicy(fenceApproval, StandardEnforcementModes.ConditionalReview, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), context.ClauseRegistry, context.SourceRegistry, standardPolicies).Items.Single();
                    Assert(conditional.AuthorityState == EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional && conditional.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.GovernedConditional, "P9.2C2B conditional authority was not retained as conditional.");

                    ProjectDistanceGovernanceAggregateResult reversed = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules.Reverse(), new[] { road, fence }.Reverse(), new[] { roadApproval, fenceApproval }.Reverse(), verified, context.ClauseRegistry, context.SourceRegistry, standardPolicies);
                    Assert(P92C2BIdentity(aggregate) == P92C2BIdentity(reversed), "P9.2C2B aggregate ordering or selection depended on input order.");
                    string ruleIdentity = string.Join("|", rules.Select(x => x.RuleId + ":" + x.MinimumDistanceMeters + ":" + x.CriteriaProvenance!.ProjectDirectory)); string candidateIdentity = fence.CandidateId + "|" + fence.ProposedMinimumDistanceMeters + "|" + string.Join(",", fence.ClauseIds); string approvalIdentity = EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(fenceApproval); string artifactIdentity = string.Join("|", verified.Artifacts.Select(x => x.PolicyArtifactId));
                    fenceItem.Resolution.State = "changed";
                    Assert(ruleIdentity == string.Join("|", rules.Select(x => x.RuleId + ":" + x.MinimumDistanceMeters + ":" + x.CriteriaProvenance!.ProjectDirectory)) && candidateIdentity == fence.CandidateId + "|" + fence.ProposedMinimumDistanceMeters + "|" + string.Join(",", fence.ClauseIds) && approvalIdentity == EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(fenceApproval) && artifactIdentity == string.Join("|", verified.Artifacts.Select(x => x.PolicyArtifactId)), "P9.2C2B mutated caller-owned input evidence.");

                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(null!, rules, new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "null project");
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve("relative", rules, new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "invalid project");
                    EngineeringDistanceApprovalRecord otherApproval = P92BApproval("C:\\P92C2B-OTHER", fence, context, "APPROVED", "OTHER", "Approver", null);
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { otherApproval }, empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "other-project approval");
                    EngineeringDistanceApprovalAuthorityPolicyDefinition otherPolicy = P92BPolicy(otherApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision);
                    EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog otherCatalog = P92C2BCatalog(root, "other", otherApproval.ProjectDirectory, trusted, signer, otherPolicy);
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new EngineeringDistanceApprovalRecord[0], otherCatalog, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "other-project verified catalog");
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence, CopyP92Candidate(fence) }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "duplicate CandidateId");
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules.Where(x => x.RequiredMeasurementBasisKey != EngineeringDistanceEvidenceKeys.SecurityFence), new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "zero matching project rule");
                    EngineeringDistanceRuleDefinition duplicateMatch = P92C2BRuleCopy(rules.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence)); duplicateMatch.RuleId = "project.duplicate";
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules.Concat(new[] { duplicateMatch }), new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "multiple matching project rules");
                    EngineeringDistanceCandidateDefinition secondTarget = CopyP92Candidate(fence); secondTarget.CandidateId = "candidate.second"; secondTarget.PackId = "pack.second"; secondTarget.RuleId = "rule.second";
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence, secondTarget }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "duplicate project-rule target");
                    EngineeringDistanceRuleDefinition invalidRule = P92C2BRuleCopy(rules.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence)); invalidRule.CriteriaProvenance = null;
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, new[] { invalidRule }, new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "missing CriteriaProvenance");
                    EngineeringDistanceRuleDefinition wrongProjectRule = P92C2BRuleCopy(rules.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence)); wrongProjectRule.CriteriaProvenance!.ProjectDirectory = "C:\\P92C2B-OTHER";
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, new[] { wrongProjectRule }, new[] { fence }, new EngineeringDistanceApprovalRecord[0], empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "project-rule ProjectDirectory mismatch");
                    P92C2BAggregateReject(() => ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { fenceApproval, fenceApproval }, empty, context.ClauseRegistry, context.SourceRegistry, standardPolicies), "malformed approval history");
                }
                finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
            }
            P92C2BQuarantine();
        }

        private static void RunP92C2C()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            StandardAuthorityPolicyRegistry runtimePolicies = ProjectEngineeringDistanceAuthorityPolicyCatalog.Create(context.SourceRegistry, context.ClauseRegistry);
            StandardAuthorityPolicyRegistry plotPlanPolicies = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create().PolicyRegistry;
            Assert(runtimePolicies.Policies.Count == 2 && runtimePolicies.Policies.All(x => x.ClauseId == null) && runtimePolicies.Policies.All(x => plotPlanPolicies.Policies.Any(y => y.PolicyId == x.PolicyId && y.SourceId == x.SourceId && y.AuthorityClass == x.AuthorityClass && y.EnforcementMode == x.EnforcementMode && y.Rationale == x.Rationale)), "P9.2C2C source-wide project authority policies differ from the accepted plot-plan policies.");

            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = "C:\\P92C2C", AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
            IReadOnlyList<EngineeringDistanceRuleDefinition> rules = ProjectDistanceRuleBinder.Create(criteria);
            EngineeringDistanceApprovalRecord roadApproval = P92BApproval(criteria.ProjectDirectory, road, context, "APPROVED", "P92C2C-ROAD", "Approver", null);
            EngineeringDistanceApprovalRecord fenceApproval = P92BApproval(criteria.ProjectDirectory, fence, context, "APPROVED", "P92C2C-FENCE", "Approver", null);
            using (System.Security.Cryptography.RSA signer = System.Security.Cryptography.RSA.Create(2048))
            {
                EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { P92C2ATrustedKey(signer, "issuer.p92c2b", "key.1") });
                string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92C2C-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog deterministic = P92C2BCatalog(root, "deterministic", criteria.ProjectDirectory, trusted, signer, P92BPolicy(roadApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision), P92BPolicy(fenceApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision));
                    ProjectDistanceGovernanceAggregateResult aggregate = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, EngineeringDistanceCandidateCatalog.BuiltInCandidates, new[] { roadApproval, fenceApproval }, deterministic, context.ClauseRegistry, context.SourceRegistry, runtimePolicies);
                    Assert(aggregate.Items.Count == 2 && aggregate.Items.Single(x => x.CandidateId == road.CandidateId).Resolution.State == ProjectDistanceGovernedRuleResolutionStates.SemanticScopeConflict && aggregate.Items.Single(x => x.CandidateId == fence.CandidateId).Resolution.State == ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible && aggregate.Items.All(x => x.ProjectRuleId != ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId), "P9.2C2C governed candidate classification changed.");

                    PlotPlanModel plot = EvaluationPlot("Meters", EvaluationMeasurement("p92c2c-road", "p92c2c-road", "Road", 2d, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment), EvaluationMeasurement("p92c2c-fence", "p92c2c-fence", "Boundary", 29d, EngineeringDistanceEvidenceKeys.SecurityFence, EngineeringDistanceEvidenceKeys.Equipment), EvaluationMeasurement("p92c2c-unit", "p92c2c-unit", "Boundary", 14d, EngineeringDistanceEvidenceKeys.UnitBoundary, EngineeringDistanceEvidenceKeys.Equipment));
                    QaReport legacy = ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base());
                    QaReport governed = ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base(), aggregate);
                    Assert(legacy.EngineeringDistanceSummary!.Governance == null && P92C2CQaIdentity(legacy) == P92C2CQaIdentity(governed) && governed.EngineeringDistanceSummary?.Governance != null && governed.Findings.All(x => x.StandardsProvenance == null), "P9.2C2C governance changed schema-v2 physical QA results or generated standards QA.");
                    ProjectDistanceGovernanceAggregateResult governedGovernance = governed.EngineeringDistanceSummary?.Governance ?? throw new InvalidOperationException("P9.2C2C governed runtime summary governance is required.");
                    Assert(governedGovernance.Items.Count == 2, "P9.2C2C governed runtime item count changed.");
                    string governanceState = governedGovernance.Items.Single(x => x.CandidateId == fence.CandidateId).Resolution.State;
                    aggregate.Items.Single(x => x.CandidateId == fence.CandidateId).Resolution.State = "changed";
                    Assert(governedGovernance.Items.Single(x => x.CandidateId == fence.CandidateId).Resolution.State == governanceState, "P9.2C2C runtime summary retained mutable governance input.");
                    using (var stream = new System.IO.MemoryStream())
                    {
                        new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(QaReport)).WriteObject(stream, governed);
                        string json = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                        Assert(json.Contains("Governance") && json.Contains("GOVERNED_COMPATIBLE"), "P9.2C2C governance summary was not serialized.");
                    }

                    ProjectDistanceCriteria changedCriteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = criteria.ProjectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 25m, UnitBoundaryMinimumMeters = 15m };
                    ProjectDistanceGovernanceAggregateResult valueConflict = ProjectDistanceGovernanceAggregateResolver.Resolve(changedCriteria.ProjectDirectory, ProjectDistanceRuleBinder.Create(changedCriteria), new[] { fence }, new[] { fenceApproval }, P92C2BCatalog(root, "value", criteria.ProjectDirectory, trusted, signer, P92BPolicy(fenceApproval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), context.ClauseRegistry, context.SourceRegistry, runtimePolicies);
                    QaReport valueGoverned = ProjectEngineeringDistanceRuntime.Evaluate(changedCriteria, EvaluationPlot("Meters", EvaluationMeasurement("p92c2c-value", "p92c2c-value", "Boundary", 24d, EngineeringDistanceEvidenceKeys.SecurityFence, EngineeringDistanceEvidenceKeys.Equipment)), P412Base(), valueConflict);
                    Assert(valueGoverned.EngineeringDistanceSummary!.SecurityFenceMinimumMeters == 25m && valueGoverned.EngineeringDistanceSummary.Governance!.Items.Single().Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ValueConflict, "P9.2C2C conflicting approval changed the physical project rule or governance state.");

                    ProjectDistanceGovernanceAggregateResult conditional = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new[] { fenceApproval }, P92C2BCatalog(root, "conditional", criteria.ProjectDirectory, trusted, signer, P92BPolicy(fenceApproval, StandardEnforcementModes.ConditionalReview, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), context.ClauseRegistry, context.SourceRegistry, runtimePolicies);
                    ProjectDistanceGovernanceAggregateResult projectOnly = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, rules, new[] { fence }, new EngineeringDistanceApprovalRecord[0], P92C2BCatalog(root, "none", criteria.ProjectDirectory, trusted, signer), context.ClauseRegistry, context.SourceRegistry, runtimePolicies);
                    Assert(ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters"), P412Base(), conditional).EngineeringDistanceSummary!.Governance!.Items.Single().Resolution.State == ProjectDistanceGovernedRuleResolutionStates.GovernedConditional && ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters"), P412Base(), projectOnly).EngineeringDistanceSummary!.Governance!.Items.Single().Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly, "P9.2C2C conditional or ungoverned authority state was not preserved.");

                    ProjectDistanceGovernanceAggregateItem invalid = aggregate.Items.Single(x => x.CandidateId == fence.CandidateId);
                    ProjectDistanceGovernanceAggregateResult unitBoundary = new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = criteria.ProjectDirectory, Items = new List<ProjectDistanceGovernanceAggregateItem> { new ProjectDistanceGovernanceAggregateItem { CandidateId = invalid.CandidateId, CandidateRuleId = invalid.CandidateRuleId, ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId, Resolution = new ProjectDistanceGovernedRuleResolution { CandidateId = invalid.CandidateId, ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId } } } };
                    P92C2CRuntimeReject(() => ProjectEngineeringDistanceRuntime.Evaluate(criteria, plot, P412Base(), unitBoundary), "UnitBoundary governance");
                    P92C2CRuntimeReject(() => ProjectEngineeringDistanceRuntime.Evaluate(new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = criteria.ProjectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m }, plot, P412Base(), aggregate), "schema-v1 governance");
                }
                finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
            }
        }

        private static string P92C2CQaIdentity(QaReport report) => report.OverallStatus + "|" + string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Status + ":" + x.Severity)) + "|" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.RuleId + ":" + x.Severity));
        private static void P92C2CRuntimeReject(Action action, string scenario) { try { action(); } catch (ProjectEngineeringDistanceRuntimeException) { return; } throw new InvalidOperationException("P9.2C2C accepted " + scenario + "."); }

        private static EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog P92C2BCatalog(string root, string leaf, string project, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted, System.Security.Cryptography.RSA signer, params EngineeringDistanceApprovalAuthorityPolicyDefinition[] policies)
        {
            string directory = System.IO.Path.Combine(root, leaf); System.IO.Directory.CreateDirectory(directory);
            for (int index = 0; index < policies.Length; index++) P92C2AWrite(System.IO.Path.Combine(directory, index.ToString() + EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Suffix), P92C2AArtifact(signer, policies[index], "issuer.p92c2b", "key.1", new DateTime(2026, 9, 10, 19, 0, index, DateTimeKind.Utc)));
            return EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(directory, project, trusted);
        }

        private static string P92C2BIdentity(ProjectDistanceGovernanceAggregateResult result) => string.Join("|", result.Items.Select(x => x.CandidateId + ":" + x.ProjectRuleId + ":" + x.ApprovalRecordId + ":" + x.HistoryState + ":" + x.CurrentnessState + ":" + x.AuthorityState + ":" + x.Resolution.State + ":" + x.VerifiedPolicyArtifactId));
        private static EngineeringDistanceRuleDefinition P92C2BRuleCopy(EngineeringDistanceRuleDefinition source) => new EngineeringDistanceRuleDefinition { RuleId = source.RuleId, MeasurementKind = source.MeasurementKind, MinimumDistanceMeters = source.MinimumDistanceMeters, RequiredMeasurementBasisKey = source.RequiredMeasurementBasisKey, RequiredApplicabilityKey = source.RequiredApplicabilityKey, CriteriaProvenance = source.CriteriaProvenance == null ? null : new EngineeringDistanceCriteriaProvenance { SourceKind = source.CriteriaProvenance.SourceKind, ProjectDirectory = source.CriteriaProvenance.ProjectDirectory, CriteriaSettingKey = source.CriteriaProvenance.CriteriaSettingKey, SchemaVersion = source.CriteriaProvenance.SchemaVersion, ConfiguredMinimumDistanceMeters = source.CriteriaProvenance.ConfiguredMinimumDistanceMeters } };
        private static void P92C2BAggregateReject(Action action, string scenario) { try { action(); } catch (Exception exception) when (exception is ArgumentNullException || exception is EngineeringDistanceApprovalRecordValidationException || exception is ProjectDistanceGovernanceAggregateValidationException) { return; } throw new InvalidOperationException("P9.2C2B accepted " + scenario + "."); }

        private static void P92C2BQuarantine()
        {
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "p92c2b.quarantine", DocumentNumber = "P92C2B-Q", Title = "Quarantine", Issuer = "Test", RevisionOrEdition = "1", SourceSha256 = new string('D', 64) } });
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { new StandardClauseDefinition { ClauseId = "p92c2b.quarantine.1", SourceId = "p92c2b.quarantine", ClauseReference = "1", ClauseText = "Synthetic quarantine evidence" } });
            StandardAuthorityPolicyRegistry standards = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { new StandardAuthorityPolicyDefinition { PolicyId = "p92c2b.quarantine.policy", SourceId = "p92c2b.quarantine", AuthorityClass = StandardAuthorityClasses.Quarantined, EnforcementMode = StandardEnforcementModes.NeverAutoEnforce, Rationale = "quarantine" } });
            EngineeringDistanceCandidateDefinition candidate = new EngineeringDistanceCandidateDefinition { CandidateId = "candidate.p92c2b.quarantine", PackId = "pack.p92c2b.quarantine", RuleId = "rule.p92c2b.quarantine", Title = "Synthetic quarantine candidate", MeasurementKind = "EquipmentToBoundary2D", ProposedMinimumDistanceMeters = 1m, RequiredMeasurementBasisKey = EngineeringDistanceEvidenceKeys.SecurityFence, RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment, ApprovalStatus = "PENDING_ENGINEERING_APPROVAL", ClauseIds = new List<string> { "p92c2b.quarantine.1" } };
            EngineeringDistanceApprovalRecord approval = EngineeringDistanceApprovalRecordBuilder.Create("C:\\P92C2B-Q", candidate, clauses, sources, new EngineeringDistanceApprovalDecision { CandidateId = candidate.CandidateId, DecisionStatus = "APPROVED", ApprovalReference = "P92C2B-Q" }, "Approver", null, new DateTime(2026, 9, 10, 19, 30, 0, DateTimeKind.Utc));
            EngineeringDistanceRuleDefinition rule = new EngineeringDistanceRuleDefinition { RuleId = "project.p92c2b.quarantine", MeasurementKind = candidate.MeasurementKind, MinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey, RequiredApplicabilityKey = candidate.RequiredApplicabilityKey, CriteriaProvenance = new EngineeringDistanceCriteriaProvenance { SourceKind = "ProjectDistanceCriteria", ProjectDirectory = approval.ProjectDirectory, CriteriaSettingKey = "Synthetic", SchemaVersion = 2, ConfiguredMinimumDistanceMeters = 1m } };
            using (System.Security.Cryptography.RSA signer = System.Security.Cryptography.RSA.Create(2048))
            {
                EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[] { P92C2ATrustedKey(signer, "issuer.p92c2b", "key.1") });
                string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "P92C2BQ-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    ProjectDistanceGovernanceAggregateItem item = ProjectDistanceGovernanceAggregateResolver.Resolve(approval.ProjectDirectory, new[] { rule }, new[] { candidate }, new[] { approval }, P92C2BCatalog(root, "policies", approval.ProjectDirectory, trusted, signer, P92BPolicy(approval, StandardEnforcementModes.Deterministic, EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)), clauses, sources, standards).Items.Single();
                    Assert(item.AuthorityState == EngineeringDistanceApprovalAuthorityStates.SourceQuarantined && item.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && item.Resolution.State != ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible, "P9.2C2B quarantined authority was promoted.");
                }
                finally { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
            }
        }

        private static void RunP92C1()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = "C:\\P92C1", AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
            IReadOnlyList<EngineeringDistanceRuleDefinition> projectRules = ProjectDistanceRuleBinder.Create(criteria);
            EngineeringDistanceRuleDefinition roadRule = projectRules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId);
            EngineeringDistanceRuleDefinition fenceRule = projectRules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId);
            EngineeringDistanceRuleDefinition unitBoundaryRule = projectRules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId);
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m");
            EngineeringDistanceApprovalRecord roadApproval = P92BApproval("C:\\P92C1", road, context, "APPROVED", "P92C1-ROAD", "Approver", null);
            EngineeringDistanceApprovalRecord fenceApproval = P92BApproval("C:\\P92C1", fence, context, "APPROVED", "P92C1-FENCE", "Approver", null);
            EngineeringDistanceApprovalAuthorityResult roadDeterministic = P92CAuthority(roadApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic);
            EngineeringDistanceApprovalAuthorityResult fenceDeterministic = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic);

            ProjectDistanceGovernedRuleResolution roadResult = ProjectDistanceGovernedRuleResolver.Resolve(roadRule, road, roadApproval, roadDeterministic);
            Assert(roadRule.MeasurementKind == road.MeasurementKind && roadRule.RequiredMeasurementBasisKey == road.RequiredMeasurementBasisKey && roadRule.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment && road.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.ProcessingEquipment && roadResult.State == ProjectDistanceGovernedRuleResolutionStates.SemanticScopeConflict && roadResult.State != ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible, "P9.2C1 Road scope mismatch was not fail-closed.");

            ProjectDistanceGovernedRuleResolution fenceResult = ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, fenceDeterministic);
            ProjectDistanceGovernanceProvenance fenceGovernance = fenceResult.GovernanceProvenance ?? throw new InvalidOperationException("P9.2C1 fence governance provenance was absent.");
            Assert(fenceResult.State == ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible && fenceResult.ProjectRuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId && fenceResult.ProjectRuleId != fence.RuleId && fenceRule.MeasurementKind == fence.MeasurementKind && fenceRule.RequiredMeasurementBasisKey == fence.RequiredMeasurementBasisKey && fenceRule.RequiredApplicabilityKey == fence.RequiredApplicabilityKey && fenceGovernance.ProjectRuleId == fenceRule.RuleId && fenceGovernance.CandidateId == fence.CandidateId && fenceGovernance.CandidateRuleId == fence.RuleId && fenceGovernance.ApprovalRecordId == fenceApproval.ApprovalRecordId && fenceGovernance.PolicyId == fenceDeterministic.PolicyId && fenceGovernance.AuthorityBasisReference == fenceDeterministic.AuthorityBasisReference && fenceGovernance.AuthorityState == fenceDeterministic.State && fenceGovernance.ApprovedMinimumDistanceMeters == 30m, "P9.2C1 compatible Fence resolution or detached provenance changed.");

            EngineeringDistanceRuleDefinition changedFenceRule = P92CCloneRule(fenceRule); changedFenceRule.MinimumDistanceMeters = 25m; changedFenceRule.CriteriaProvenance!.ConfiguredMinimumDistanceMeters = 25m;
            Assert(ProjectDistanceGovernedRuleResolver.Resolve(changedFenceRule, fence, fenceApproval, fenceDeterministic).State == ProjectDistanceGovernedRuleResolutionStates.ValueConflict, "P9.2C1 differing Fence values did not fail closed.");
            EngineeringDistanceApprovalAuthorityResult fenceConditional = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional);
            Assert(ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, fenceConditional).State == ProjectDistanceGovernedRuleResolutionStates.GovernedConditional, "P9.2C1 conditional authority was not retained as non-executable review.");
            ProjectDistanceGovernedRuleResolution projectOnly = ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, null, null);
            Assert(projectOnly.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && projectOnly.GovernanceProvenance == null, "P9.2C1 no-approval project-only resolution changed.");
            foreach (string state in new[] { EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy, EngineeringDistanceApprovalAuthorityStates.StaleApproval, EngineeringDistanceApprovalAuthorityStates.SourceQuarantined })
            {
                EngineeringDistanceApprovalAuthorityResult blocked = P92CAuthority(fenceApproval, state);
                ProjectDistanceGovernedRuleResolution blockedResult = ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, blocked);
                Assert(blockedResult.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && blockedResult.AuthorityState == state && blockedResult.GovernanceProvenance == null, "P9.2C1 blocked authority was promoted: " + state);
            }
            Assert(EngineeringDistanceCandidateCatalog.BuiltInCandidates.All(x => x.RequiredMeasurementBasisKey != EngineeringDistanceEvidenceKeys.UnitBoundary) && unitBoundaryRule.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId, "P9.2C1 UnitBoundary entered governed activation.");

            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, roadApproval, P92CAuthority(roadApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic)), "approval candidate mismatch");
            EngineeringDistanceApprovalAuthorityResult candidateMismatch = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic); candidateMismatch.CandidateId = "candidate.other";
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, candidateMismatch), "authority candidate mismatch");
            EngineeringDistanceApprovalAuthorityResult recordMismatch = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic); recordMismatch.ApprovalRecordId = new string('A', 64);
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, recordMismatch), "authority record mismatch");
            EngineeringDistanceApprovalAuthorityResult projectMismatch = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic); projectMismatch.ProjectDirectory = "C:\\P92C1-OTHER";
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, projectMismatch), "authority project mismatch");
            EngineeringDistanceRuleDefinition ruleProjectMismatch = P92CCloneRule(fenceRule); ruleProjectMismatch.CriteriaProvenance!.ProjectDirectory = "C:\\P92C1-OTHER";
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(ruleProjectMismatch, fence, fenceApproval, fenceDeterministic), "project-rule project mismatch");
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, null), "approval without authority");
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, null, fenceDeterministic), "authority without approval");
            EngineeringDistanceApprovalAuthorityResult missingPolicy = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic); missingPolicy.PolicyId = null;
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, missingPolicy), "missing PolicyId");
            EngineeringDistanceApprovalAuthorityResult missingBasis = P92CAuthority(fenceApproval, EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic); missingBasis.AuthorityBasisReference = null;
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, missingBasis), "missing AuthorityBasisReference");
            EngineeringDistanceRuleDefinition missingCriteria = P92CCloneRule(fenceRule); missingCriteria.CriteriaProvenance = null;
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(missingCriteria, fence, fenceApproval, fenceDeterministic), "missing CriteriaProvenance");
            EngineeringDistanceRuleDefinition invalidCriteria = P92CCloneRule(fenceRule); invalidCriteria.CriteriaProvenance!.SourceKind = "Other";
            P92CAssertRejected(() => ProjectDistanceGovernedRuleResolver.Resolve(invalidCriteria, fence, fenceApproval, fenceDeterministic), "invalid CriteriaProvenance SourceKind");

            string ruleIdentity = fenceRule.RuleId + "|" + fenceRule.MinimumDistanceMeters + "|" + fenceRule.RequiredMeasurementBasisKey + "|" + fenceRule.RequiredApplicabilityKey; string criteriaIdentity = fenceRule.CriteriaProvenance!.SourceKind + "|" + fenceRule.CriteriaProvenance.ProjectDirectory + "|" + fenceRule.CriteriaProvenance.CriteriaSettingKey + "|" + fenceRule.CriteriaProvenance.ConfiguredMinimumDistanceMeters; string candidateIdentity = fence.CandidateId + "|" + fence.RuleId + "|" + fence.ProposedMinimumDistanceMeters + "|" + string.Join("|", fence.ClauseIds); string approvalIdentity = EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(fenceApproval); string authorityIdentity = fenceDeterministic.State + "|" + fenceDeterministic.ProjectDirectory + "|" + fenceDeterministic.CandidateId + "|" + fenceDeterministic.ApprovalRecordId + "|" + fenceDeterministic.PolicyId + "|" + fenceDeterministic.AuthorityBasisReference;
            ProjectDistanceGovernedRuleResolver.Resolve(fenceRule, fence, fenceApproval, fenceDeterministic);
            Assert(ruleIdentity == fenceRule.RuleId + "|" + fenceRule.MinimumDistanceMeters + "|" + fenceRule.RequiredMeasurementBasisKey + "|" + fenceRule.RequiredApplicabilityKey && criteriaIdentity == fenceRule.CriteriaProvenance!.SourceKind + "|" + fenceRule.CriteriaProvenance.ProjectDirectory + "|" + fenceRule.CriteriaProvenance.CriteriaSettingKey + "|" + fenceRule.CriteriaProvenance.ConfiguredMinimumDistanceMeters && candidateIdentity == fence.CandidateId + "|" + fence.RuleId + "|" + fence.ProposedMinimumDistanceMeters + "|" + string.Join("|", fence.ClauseIds) && approvalIdentity == EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(fenceApproval) && authorityIdentity == fenceDeterministic.State + "|" + fenceDeterministic.ProjectDirectory + "|" + fenceDeterministic.CandidateId + "|" + fenceDeterministic.ApprovalRecordId + "|" + fenceDeterministic.PolicyId + "|" + fenceDeterministic.AuthorityBasisReference, "P9.2C1 resolver mutated input evidence.");
        }

        private static EngineeringDistanceApprovalAuthorityResult P92CAuthority(EngineeringDistanceApprovalRecord approval, string state) => new EngineeringDistanceApprovalAuthorityResult { State = state, ProjectDirectory = approval.ProjectDirectory, CandidateId = approval.CandidateBinding.CandidateId, ApprovalRecordId = approval.ApprovalRecordId, PolicyId = "P92C1-POLICY", AuthorityBasisReference = "P92C1-BASIS" };

        private static EngineeringDistanceRuleDefinition P92CCloneRule(EngineeringDistanceRuleDefinition source) => new EngineeringDistanceRuleDefinition { RuleId = source.RuleId, MeasurementKind = source.MeasurementKind, MinimumDistanceMeters = source.MinimumDistanceMeters, RequiredMeasurementBasisKey = source.RequiredMeasurementBasisKey, RequiredApplicabilityKey = source.RequiredApplicabilityKey, CriteriaProvenance = source.CriteriaProvenance == null ? null : new EngineeringDistanceCriteriaProvenance { SourceKind = source.CriteriaProvenance.SourceKind, ProjectDirectory = source.CriteriaProvenance.ProjectDirectory, CriteriaSettingKey = source.CriteriaProvenance.CriteriaSettingKey, SchemaVersion = source.CriteriaProvenance.SchemaVersion, ConfiguredMinimumDistanceMeters = source.CriteriaProvenance.ConfiguredMinimumDistanceMeters } };

        private static void P92CAssertRejected(Action action, string scenario) { try { action(); } catch (ProjectDistanceGovernedRuleResolverValidationException) { return; } throw new InvalidOperationException("P9.2C1 resolver accepted " + scenario + "."); }

        private static void RunP54()
        {
            Assert(StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.d", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)))).ReviewItems.Count == 0, "P5.4 deterministic rule was not omitted.");
            StandardAuthorityReviewItem conditional = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.c", StandardEnforcementModes.ConditionalReview, GateClause("c", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview)))).ReviewItems.Single();
            Assert(conditional.DecisionState == StandardDecisionStates.ExpertReviewRequired && conditional.EffectiveEnforcementMode == StandardEnforcementModes.ConditionalReview && conditional.Message == "Standards authority requires engineering review; automatic deterministic evaluation is not allowed.", "P5.4 conditional projection failed.");
            StandardAuthorityReviewItem advisory = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.a", StandardEnforcementModes.Advisory, GateClause("a", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory)))).ReviewItems.Single();
            Assert(advisory.DecisionState == StandardDecisionStates.Advisory && advisory.Message == "Standards authority is advisory; automatic deterministic evaluation is not allowed.", "P5.4 advisory projection failed.");
            StandardAuthorityReviewItem quarantined = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.q", StandardEnforcementModes.NeverAutoEnforce, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)))).ReviewItems.Single();
            Assert(quarantined.DecisionState == StandardDecisionStates.SourceQuarantined && quarantined.Message == "Standards authority is quarantined; automatic deterministic evaluation is not allowed.", "P5.4 quarantine projection failed.");
            StandardAuthorityReviewItem legacy = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.l", StandardEnforcementModes.NeverAutoEnforce, GateClause("l", StandardAuthorityClasses.LegacyConflict, StandardEnforcementModes.NeverAutoEnforce)))).ReviewItems.Single();
            Assert(legacy.DecisionState == StandardDecisionStates.ExpertReviewRequired && legacy.DecisionState != StandardDecisionStates.StandardConflict, "P5.4 legacy projection inferred conflict.");

            StandardAuthorityReviewItem mixed = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.mix", StandardEnforcementModes.ConditionalReview, GateClause("z", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory, sourceId: "source.z", policyId: "policy.z", rationale: "z"), GateClause("a", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, sourceId: "source.a", policyId: "policy.a", policyClauseId: "a", rationale: "a")))).ReviewItems.Single();
            StandardClauseAuthorityResolution mixedClause = mixed.Clauses.Single(clause => clause.ClauseId == "a");
            Assert(mixed.DecisionState == StandardDecisionStates.ExpertReviewRequired && mixed.Clauses.Select(clause => clause.ClauseId).SequenceEqual(new[] { "a", "z" }) && mixedClause.SourceId == "source.a" && mixedClause.PolicyId == "policy.a" && mixedClause.PolicyClauseId == "a" && mixedClause.AuthorityClass == StandardAuthorityClasses.ActivePrimary && mixedClause.EnforcementMode == StandardEnforcementModes.Deterministic && mixedClause.Rationale == "a", "P5.4 mixed clause evidence was not preserved.");

            StandardAuthorityReviewProjection unordered = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.z", StandardEnforcementModes.Advisory, GateClause("z", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory)), GateRule("rule.d", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)), GateRule("rule.a", StandardEnforcementModes.ConditionalReview, GateClause("b", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview), GateClause("a", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory)), GateRule("rule.q", StandardEnforcementModes.NeverAutoEnforce, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce))));
            StandardAuthorityReviewProjection reversed = StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.q", StandardEnforcementModes.NeverAutoEnforce, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)), GateRule("rule.a", StandardEnforcementModes.ConditionalReview, GateClause("a", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory), GateClause("b", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview)), GateRule("rule.d", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)), GateRule("rule.z", StandardEnforcementModes.Advisory, GateClause("z", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory))));
            Assert(unordered.ReviewItems.Count == 3 && unordered.ReviewItems.Select(item => item.RuleId).SequenceEqual(new[] { "rule.a", "rule.q", "rule.z" }) && unordered.ReviewItems.Single(item => item.RuleId == "rule.a").Clauses.Select(clause => clause.ClauseId).SequenceEqual(new[] { "a", "b" }) && ReviewIdentity(unordered) == ReviewIdentity(reversed), "P5.4 projection ordering was not deterministic.");
            Assert(unordered.ReviewItems.All(item => item.DecisionState != StandardDecisionStates.Pass && item.DecisionState != StandardDecisionStates.Fail && item.DecisionState != StandardDecisionStates.StandardConflict && item.DecisionState != StandardDecisionStates.NotApplicable && item.DecisionState != StandardDecisionStates.InsufficientEvidence), "P5.4 emitted a forbidden decision state.");

            ThrowsArgumentNull(() => StandardAuthorityReviewProjector.Create(null!), "Null P5.4 authority resolution was not rejected.");
            ThrowsGate(() => StandardAuthorityReviewProjector.Create(new StandardAuthorityResolution { Rules = null! }), "standard authority execution gate Rules are required");
            ThrowsGate(() => StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.forged", StandardEnforcementModes.Deterministic, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)))), "standard authority execution gate EffectiveEnforcementMode is inconsistent: rule.forged");
            ThrowsGate(() => StandardAuthorityReviewProjector.Create(GateResolution(GateRule("rule.invalid", StandardEnforcementModes.Deterministic, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.Deterministic)))));
            Assert(StandardAuthorityReviewProjector.Create(new StandardAuthorityResolution { Rules = new List<StandardRuleAuthorityResolution>() }).ReviewItems.Count == 0, "P5.4 empty projection failed.");

            StandardAuthorityResolution mutable = GateResolution(GateRule("rule.mutable", StandardEnforcementModes.Advisory, GateClause("clause.mutable", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory, sourceId: "source.mutable", policyId: "policy.mutable", policyClauseId: "clause.mutable", rationale: "original")));
            StandardAuthorityReviewProjection immutable = StandardAuthorityReviewProjector.Create(mutable);
            StandardRuleAuthorityResolution mutableRule = mutable.Rules.Single(); StandardClauseAuthorityResolution mutableClause = mutableRule.Clauses.Single();
            mutableRule.RuleId = "changed"; mutableRule.EffectiveEnforcementMode = StandardEnforcementModes.Deterministic; mutableClause.ClauseId = "changed"; mutableClause.SourceId = "changed"; mutableClause.PolicyId = "changed"; mutableClause.PolicyClauseId = "changed"; mutableClause.AuthorityClass = StandardAuthorityClasses.ActivePrimary; mutableClause.EnforcementMode = StandardEnforcementModes.Deterministic; mutableClause.Rationale = "changed"; mutableRule.Clauses.Clear(); mutable.Rules.Clear();
            StandardAuthorityReviewItem immutableItem = immutable.ReviewItems.Single(); StandardClauseAuthorityResolution immutableClause = immutableItem.Clauses.Single();
            Assert(immutableItem.RuleId == "rule.mutable" && immutableItem.EffectiveEnforcementMode == StandardEnforcementModes.Advisory && immutableItem.DecisionState == StandardDecisionStates.Advisory && immutableClause.ClauseId == "clause.mutable" && immutableClause.SourceId == "source.mutable" && immutableClause.PolicyId == "policy.mutable" && immutableClause.PolicyClauseId == "clause.mutable" && immutableClause.AuthorityClass == StandardAuthorityClasses.EngineeringGuidance && immutableClause.EnforcementMode == StandardEnforcementModes.Advisory && immutableClause.Rationale == "original", "P5.4 projection retained caller-owned state.");
        }

        private static void RunP53()
        {
            StandardExecutionGateResolution deterministic = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.d", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic))));
            Assert(deterministic.Rules.Single().RuleId == "rule.d" && deterministic.Rules.Single().EffectiveEnforcementMode == StandardEnforcementModes.Deterministic && deterministic.Rules.Single().AutoExecutionAllowed && deterministic.Rules.Single().DecisionState == null, "P5.3 deterministic execution gate failed.");
            StandardExecutionGateResolution conditional = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.c", StandardEnforcementModes.ConditionalReview, GateClause("c", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview))));
            Assert(!conditional.Rules.Single().AutoExecutionAllowed && conditional.Rules.Single().DecisionState == StandardDecisionStates.ExpertReviewRequired, "P5.3 conditional execution gate failed.");
            StandardExecutionGateResolution advisory = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.a", StandardEnforcementModes.Advisory, GateClause("a", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory))));
            Assert(!advisory.Rules.Single().AutoExecutionAllowed && advisory.Rules.Single().DecisionState == StandardDecisionStates.Advisory, "P5.3 advisory execution gate failed.");
            StandardExecutionGateResolution mixed = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.da", StandardEnforcementModes.ConditionalReview, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), GateClause("a", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory))));
            Assert(!mixed.Rules.Single().AutoExecutionAllowed && mixed.Rules.Single().DecisionState == StandardDecisionStates.ExpertReviewRequired, "P5.3 deterministic/advisory aggregate failed.");
            StandardExecutionGateResolution quarantine = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.q", StandardEnforcementModes.NeverAutoEnforce, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce))));
            Assert(!quarantine.Rules.Single().AutoExecutionAllowed && quarantine.Rules.Single().DecisionState == StandardDecisionStates.SourceQuarantined, "P5.3 quarantine gate failed.");
            StandardExecutionGateResolution legacy = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.l", StandardEnforcementModes.NeverAutoEnforce, GateClause("l", StandardAuthorityClasses.LegacyConflict, StandardEnforcementModes.NeverAutoEnforce))));
            Assert(!legacy.Rules.Single().AutoExecutionAllowed && legacy.Rules.Single().DecisionState == StandardDecisionStates.ExpertReviewRequired && legacy.Rules.Single().DecisionState != StandardDecisionStates.StandardConflict, "P5.3 legacy conflict gate failed.");
            StandardExecutionGateResolution dominance = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.qd", StandardEnforcementModes.NeverAutoEnforce, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce))));
            Assert(dominance.Rules.Single().DecisionState == StandardDecisionStates.SourceQuarantined, "P5.3 quarantine dominance failed.");
            StandardExecutionGateResolution multiAuthority = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.multi", StandardEnforcementModes.ConditionalReview, GateClause("primary", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), GateClause("support", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory))));
            Assert(multiAuthority.Rules.Single().DecisionState == StandardDecisionStates.ExpertReviewRequired && multiAuthority.Rules.Single().DecisionState != StandardDecisionStates.StandardConflict && multiAuthority.Rules.Single().DecisionState != StandardDecisionStates.Pass && multiAuthority.Rules.Single().DecisionState != StandardDecisionStates.Fail, "P5.3 valid multisource gate inferred an unsupported conflict state.");
            StandardExecutionGateResolution policyClauseMatch = StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.policy-clause", StandardEnforcementModes.Deterministic, GateClause("pc", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, policyClauseId: "pc"))));
            Assert(policyClauseMatch.Rules.Single().AutoExecutionAllowed, "P5.3 matching PolicyClauseId was rejected.");
            StandardRuleExecutionGateDecision[] decisions = deterministic.Rules.Concat(conditional.Rules).Concat(advisory.Rules).Concat(mixed.Rules).Concat(quarantine.Rules).Concat(legacy.Rules).Concat(dominance.Rules).Concat(multiAuthority.Rules).ToArray();
            Assert(decisions.Where(decision => decision.DecisionState != null).All(decision => decision.DecisionState != StandardDecisionStates.Pass && decision.DecisionState != StandardDecisionStates.Fail && decision.DecisionState != StandardDecisionStates.NotApplicable && decision.DecisionState != StandardDecisionStates.InsufficientEvidence && decision.DecisionState != StandardDecisionStates.StandardConflict), "P5.3 gate emitted a premature decision state.");

            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.forged", StandardEnforcementModes.Deterministic, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)))), "standard authority execution gate EffectiveEnforcementMode is inconsistent: rule.forged");
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.forged-advisory", StandardEnforcementModes.ConditionalReview, GateClause("a", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.invalid-q", StandardEnforcementModes.Deterministic, GateClause("q", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.invalid-s", StandardEnforcementModes.Deterministic, GateClause("s", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Deterministic)))));
            ThrowsArgumentNull(() => StandardAuthorityExecutionGate.Evaluate(null!), "Null P5.3 authority resolution was not rejected.");
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(new StandardAuthorityResolution { Rules = null! }), "standard authority execution gate Rules are required");
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(new StandardAuthorityResolution { Rules = new List<StandardRuleAuthorityResolution> { null! } }));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(new StandardRuleAuthorityResolution { RuleId = " ", EffectiveEnforcementMode = StandardEnforcementModes.Deterministic, Clauses = new List<StandardClauseAuthorityResolution> { GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic) } })));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(new StandardRuleAuthorityResolution { RuleId = "rule.blank-mode", EffectiveEnforcementMode = " ", Clauses = new List<StandardClauseAuthorityResolution> { GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic) } })));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.duplicate", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)), GateRule("rule.duplicate", StandardEnforcementModes.Deterministic, GateClause("e", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.unsupported-mode", "deterministic", GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(new StandardRuleAuthorityResolution { RuleId = "rule.null-clauses", EffectiveEnforcementMode = StandardEnforcementModes.Deterministic, Clauses = null! })));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(new StandardRuleAuthorityResolution { RuleId = "rule.empty-clauses", EffectiveEnforcementMode = StandardEnforcementModes.Deterministic, Clauses = new List<StandardClauseAuthorityResolution>() })));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.null-clause", StandardEnforcementModes.Deterministic, new StandardClauseAuthorityResolution[] { null! }))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-clause", StandardEnforcementModes.Deterministic, GateClause(" ", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.duplicate-clause", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-source", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, sourceId: " ")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-policy", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, policyId: " ")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-policy-clause", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, policyClauseId: " ")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.bad-policy-clause", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, policyClauseId: "other")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.bad-authority", StandardEnforcementModes.Deterministic, GateClause("d", "UNKNOWN", StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.bad-enforcement", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, "UNKNOWN")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-rationale", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, rationale: " ")))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-authority", StandardEnforcementModes.Deterministic, GateClause("d", " ", StandardEnforcementModes.Deterministic)))));
            ThrowsGate(() => StandardAuthorityExecutionGate.Evaluate(GateResolution(GateRule("rule.blank-enforcement", StandardEnforcementModes.Deterministic, GateClause("d", StandardAuthorityClasses.ActivePrimary, " ")))));

            Assert(StandardAuthorityExecutionGate.Evaluate(new StandardAuthorityResolution { Rules = new List<StandardRuleAuthorityResolution>() }).Rules.Count == 0, "P5.3 empty resolution failed.");
            StandardAuthorityResolution unordered = GateResolution(GateRule("rule.z", StandardEnforcementModes.Advisory, GateClause("z", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory)), GateRule("rule.a", StandardEnforcementModes.Deterministic, GateClause("a", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic)));
            StandardExecutionGateResolution ordered = StandardAuthorityExecutionGate.Evaluate(unordered);
            StandardExecutionGateResolution reversed = StandardAuthorityExecutionGate.Evaluate(GateResolution(unordered.Rules[1], unordered.Rules[0]));
            Assert(ordered.Rules.Select(rule => rule.RuleId).SequenceEqual(new[] { "rule.a", "rule.z" }) && string.Join("|", ordered.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + rule.DecisionState)) == string.Join("|", reversed.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + rule.DecisionState)), "P5.3 gate ordering was not deterministic.");
            StandardAuthorityResolution mutable = GateResolution(GateRule("rule.mutable", StandardEnforcementModes.Deterministic, GateClause("mutable", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic, policyId: "policy.mutable", policyClauseId: "mutable", rationale: "original")));
            StandardExecutionGateResolution immutable = StandardAuthorityExecutionGate.Evaluate(mutable);
            StandardRuleAuthorityResolution mutableRule = mutable.Rules.Single();
            StandardClauseAuthorityResolution mutableClause = mutableRule.Clauses.Single();
            mutableRule.RuleId = "changed"; mutableRule.EffectiveEnforcementMode = StandardEnforcementModes.Advisory; mutableClause.AuthorityClass = StandardAuthorityClasses.EngineeringGuidance; mutableClause.EnforcementMode = StandardEnforcementModes.Advisory; mutableClause.PolicyId = "changed"; mutableClause.PolicyClauseId = "changed"; mutableClause.Rationale = "changed"; mutableRule.Clauses.Clear(); mutable.Rules.Clear();
            Assert(immutable.Rules.Count == 1 && immutable.Rules.Single().RuleId == "rule.mutable" && immutable.Rules.Single().EffectiveEnforcementMode == StandardEnforcementModes.Deterministic && immutable.Rules.Single().AutoExecutionAllowed && immutable.Rules.Single().DecisionState == null, "P5.3 gate retained caller-owned state.");
        }

        private static void RunP52()
        {
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { Source("source.project"), Source("source.primary"), Source("source.support"), Source("source.legacy"), Source("source.quarantine") });
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { Clause("clause.project", "source.project", "1"), Clause("clause.primary", "source.primary", "1"), Clause("clause.support", "source.support", "1"), Clause("clause.support2", "source.support", "2"), Clause("clause.legacy", "source.legacy", "1"), Clause("clause.quarantine", "source.quarantine", "1") });
            StandardAuthorityPolicyRegistry policies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[]
            {
                Policy("project", "source.project", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.Deterministic),
                Policy("primary", "source.primary", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic),
                Policy("support", "source.support", null, StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory),
                Policy("support2", "source.support", "clause.support2", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory),
                Policy("legacy", "source.legacy", null, StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.ConditionalReview),
                Policy("quarantine", "source.quarantine", "clause.quarantine", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)
            });
            ThrowsArgumentNull(() => StandardAuthorityResolver.Resolve(null!, clauses, policies), "Null P5.2 rule resolution was not rejected.");
            ThrowsArgumentNull(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.null-clause-registry", "clause.primary")), null!, policies), "Null P5.2 clause registry was not rejected.");
            ThrowsArgumentNull(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.null-authority-registry", "clause.primary")), clauses, null!), "Null P5.2 authority registry was not rejected.");
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(new StandardRulePackResolution { EffectiveRules = null! }, clauses, policies), "standard authority resolution EffectiveRules are required");
            StandardAuthorityResolution single = StandardAuthorityResolver.Resolve(Resolution(Rule("rule.single", "clause.primary")), clauses, policies);
            StandardClauseAuthorityResolution singleClause = single.Rules.Single().Clauses.Single();
            Assert(single.Rules.Count == 1 && single.Rules.Single().RuleId == "rule.single" && single.Rules.Single().EffectiveEnforcementMode == StandardEnforcementModes.Deterministic && singleClause.ClauseId == "clause.primary" && singleClause.SourceId == "source.primary" && singleClause.PolicyId == "primary" && singleClause.PolicyClauseId == null && singleClause.AuthorityClass == StandardAuthorityClasses.ActivePrimary && singleClause.EnforcementMode == StandardEnforcementModes.Deterministic && singleClause.Rationale == "rationale:primary", "P5.2 single deterministic resolution failed.");
            StandardAuthorityResolution fallback = StandardAuthorityResolver.Resolve(Resolution(Rule("rule.fallback", "clause.project")), clauses, policies);
            Assert(fallback.Rules.Single().Clauses.Single().PolicyClauseId == null && fallback.Rules.Single().EffectiveEnforcementMode == StandardEnforcementModes.Deterministic, "P5.2 source-wide fallback failed.");
            StandardAuthorityResolution overrideResolution = StandardAuthorityResolver.Resolve(Resolution(Rule("rule.override", "clause.quarantine")), clauses, policies);
            Assert(overrideResolution.Rules.Single().Clauses.Single().PolicyId == "quarantine" && overrideResolution.Rules.Single().Clauses.Single().PolicyClauseId == "clause.quarantine" && overrideResolution.Rules.Single().EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "P5.2 clause override failed.");
            Assert(Mode(policies, clauses, "rule.dd", "clause.primary", "clause.project") == StandardEnforcementModes.Deterministic && Mode(policies, clauses, "rule.aa", "clause.support", "clause.support2") == StandardEnforcementModes.Advisory && Mode(policies, clauses, "rule.da", "clause.primary", "clause.support") == StandardEnforcementModes.ConditionalReview && Mode(policies, clauses, "rule.dc", "clause.primary", "clause.legacy") == StandardEnforcementModes.ConditionalReview && Mode(policies, clauses, "rule.ac", "clause.support", "clause.legacy") == StandardEnforcementModes.ConditionalReview && Mode(policies, clauses, "rule.dn", "clause.primary", "clause.quarantine") == StandardEnforcementModes.NeverAutoEnforce && Mode(policies, clauses, "rule.cn", "clause.legacy", "clause.quarantine") == StandardEnforcementModes.NeverAutoEnforce, "P5.2 aggregate enforcement modes failed.");
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.missing", "clause.missing")), clauses, policies), "standard authority resolution references unknown ClauseId: rule.missing | clause.missing");
            StandardAuthorityPolicyRegistry noPolicies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new StandardAuthorityPolicyDefinition[0]);
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.nopolicy", "clause.primary")), clauses, noPolicies), "standard authority resolution has no policy: rule.nopolicy | clause.primary");
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { Rule("dup", "clause.primary"), Rule("dup", "clause.project") } }, clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.dupclause", "clause.primary", "clause.primary")), clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { null! } }, clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(new EffectiveStandardRuleDefinition { RuleId = " ", ClauseIds = new List<string> { "clause.primary" } }), clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(new EffectiveStandardRuleDefinition { RuleId = "rule.null", ClauseIds = null! }), clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(new EffectiveStandardRuleDefinition { RuleId = "rule.empty", ClauseIds = new List<string>() }), clauses, policies));
            ThrowsResolution(() => StandardAuthorityResolver.Resolve(Resolution(Rule("rule.blank", " ")), clauses, policies));
            Assert(StandardAuthorityResolver.Resolve(new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition>() }, clauses, policies).Rules.Count == 0, "P5.2 empty resolution failed.");
            StandardRulePackResolution mutable = Resolution(Rule("rule.z", "clause.support", "clause.primary"), Rule("rule.a", "clause.project"));
            StandardAuthorityResolution ordered = StandardAuthorityResolver.Resolve(mutable, clauses, policies);
            StandardAuthorityResolution reversed = StandardAuthorityResolver.Resolve(Resolution(Rule("rule.a", "clause.project"), Rule("rule.z", "clause.primary", "clause.support")), clauses, policies);
            string identity = string.Join("|", ordered.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + string.Join(",", rule.Clauses.Select(clause => clause.ClauseId))));
            Assert(identity == string.Join("|", reversed.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + string.Join(",", rule.Clauses.Select(clause => clause.ClauseId)))) && ordered.Rules.Select(rule => rule.RuleId).SequenceEqual(new[] { "rule.a", "rule.z" }), "P5.2 resolution ordering was not deterministic.");
            mutable.EffectiveRules[0].RuleId = "changed"; mutable.EffectiveRules[0].ClauseIds[0] = "changed"; mutable.EffectiveRules.Clear();
            Assert(ordered.Rules.Count == 2 && ordered.Rules.Single(rule => rule.RuleId == "rule.z").Clauses.Select(clause => clause.ClauseId).SequenceEqual(new[] { "clause.primary", "clause.support" }), "P5.2 resolution retained caller-owned state.");
        }

        private static void RunP51()
        {
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[]
            {
                Source("source.primary"), Source("source.project"), Source("source.a"), Source("source.b"), Source("source.none")
            });
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[]
            {
                Clause("clause.general", "source.project", "1"), Clause("clause.flare", "source.project", "2"), Clause("clause.a", "source.a", "1")
            });

            StandardAuthorityPolicyDefinition[] validCombinations =
            {
                Policy("govern-deterministic", "source.primary", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.Deterministic),
                Policy("govern-review", "source.project", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.ConditionalReview),
                Policy("primary-deterministic", "source.a", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic),
                Policy("primary-review", "source.b", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview),
                Policy("support-review", "source.primary", "clause.general", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.ConditionalReview),
                Policy("support-advisory", "source.project", "clause.flare", StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Advisory),
                Policy("guidance-review", "source.a", "clause.a", StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.ConditionalReview),
                Policy("guidance-advisory", "source.b", null, StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Advisory),
                Policy("legacy", "source.none", null, StandardAuthorityClasses.LegacyConflict, StandardEnforcementModes.NeverAutoEnforce),
                Policy("quarantine", "source.primary", null, StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce)
            };
            foreach (StandardAuthorityPolicyDefinition valid in validCombinations)
            {
                StandardSourceRegistry singleSource = StandardSourceRegistry.Create(new[] { Source("source.valid") });
                StandardClauseRegistry noClauses = StandardClauseRegistry.Create(singleSource, new StandardClauseDefinition[0]);
                StandardAuthorityPolicyRegistry.Create(singleSource, noClauses, new[] { Policy(valid.PolicyId, "source.valid", null, valid.AuthorityClass, valid.EnforcementMode) });
            }

            foreach (StandardAuthorityPolicyDefinition invalid in new[]
            {
                Policy("invalid-1", "source.primary", null, StandardAuthorityClasses.Quarantined, StandardEnforcementModes.Deterministic),
                Policy("invalid-2", "source.primary", null, StandardAuthorityClasses.Quarantined, StandardEnforcementModes.ConditionalReview),
                Policy("invalid-3", "source.primary", null, StandardAuthorityClasses.LegacyConflict, StandardEnforcementModes.Advisory),
                Policy("invalid-4", "source.primary", null, StandardAuthorityClasses.EngineeringGuidance, StandardEnforcementModes.Deterministic),
                Policy("invalid-5", "source.primary", null, StandardAuthorityClasses.ActiveSupporting, StandardEnforcementModes.Deterministic),
                Policy("invalid-6", "source.primary", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.NeverAutoEnforce),
                Policy("invalid-7", "source.primary", null, "active_primary", StandardEnforcementModes.Deterministic)
            }) ThrowsAuthority(() => StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { invalid }));

            StandardAuthorityPolicyDefinition primary = Policy("primary", "source.primary", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview);
            StandardAuthorityPolicyDefinition project = Policy("project", "source.project", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.Deterministic);
            StandardAuthorityPolicyDefinition flare = Policy("flare", "source.project", "clause.flare", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce);
            StandardAuthorityPolicyDefinition a = Policy("a", "source.a", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic);
            StandardAuthorityPolicyDefinition b = Policy("b", "source.b", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic);
            StandardAuthorityPolicyRegistry registry = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { flare, b, project, primary, a });
            Assert(registry.TryResolve("source.primary", null, out StandardAuthorityPolicyDefinition? resolvedPrimary) && resolvedPrimary != null && resolvedPrimary.PolicyId == "primary" && !registry.TryResolve("SOURCE.PRIMARY", null, out _), "P5.1 source-wide ordinal resolution failed.");
            Assert(registry.TryResolve("source.project", "clause.general", out StandardAuthorityPolicyDefinition? general) && general != null && general.PolicyId == "project" && registry.TryResolve("source.project", "clause.flare", out StandardAuthorityPolicyDefinition? resolvedFlare) && resolvedFlare != null && resolvedFlare.PolicyId == "flare", "P5.1 clause override resolution failed.");
            Assert(!registry.TryResolve("source.project", "clause.unknown", out _) && !registry.TryResolve("source.b", "clause.a", out _) && !registry.TryResolve("source.none", null, out _), "P5.1 unknown or mismatched policy resolution did not fail closed.");
            ThrowsAuthority(() => registry.Get("source.none", null), "standard authority policy was not found: source.none");
            ThrowsAuthority(() => registry.Get("source.project", "clause.unknown"), "standard authority policy was not found: source.project | clause.unknown");
            ThrowsAuthority(() => StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { Policy("dup", "source.primary", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), Policy("dup", "source.project", null, StandardAuthorityClasses.GoverningProject, StandardEnforcementModes.Deterministic) }));
            ThrowsAuthority(() => StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { Policy("wide-1", "source.primary", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic), Policy("wide-2", "source.primary", null, StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.ConditionalReview) }));
            ThrowsAuthority(() => StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { Policy("clause-1", "source.project", "clause.flare", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce), Policy("clause-2", "source.project", "clause.flare", StandardAuthorityClasses.Quarantined, StandardEnforcementModes.NeverAutoEnforce) }));
            ThrowsAuthority(() => StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { Policy("mismatch", "source.b", "clause.a", StandardAuthorityClasses.ActivePrimary, StandardEnforcementModes.Deterministic) }));

            StandardAuthorityPolicyRegistry reversed = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[] { a, primary, project, b, flare });
            string identity = string.Join("|", registry.Policies.Select(x => x.SourceId + ":" + (x.ClauseId ?? string.Empty) + ":" + x.PolicyId));
            Assert(identity == string.Join("|", reversed.Policies.Select(x => x.SourceId + ":" + (x.ClauseId ?? string.Empty) + ":" + x.PolicyId)) && registry.Policies.Where(x => x.SourceId == "source.project").First().ClauseId == null, "P5.1 policy ordering was not deterministic.");

            primary.Rationale = "changed"; registry.Policies.Single(x => x.PolicyId == "primary").Rationale = "changed"; registry.TryResolve("source.primary", null, out StandardAuthorityPolicyDefinition? copiedResolve); copiedResolve!.Rationale = "changed"; registry.Get("source.primary", null).Rationale = "changed";
            Assert(registry.Get("source.primary", null).Rationale == "rationale:primary", "P5.1 registry retained mutable policy state.");
        }

        private static void RunP413()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngiSentry-P413-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            string projectDirectory = ProjectDistanceCriteriaStore.ResolveProjectDirectory(System.IO.Path.Combine(root, "P413.dwg"));
            ProjectDistanceCriteria defaults = ProjectDistanceCriteriaStore.CreateDefaults(projectDirectory);
            Assert(defaults.SchemaVersion == 2 && defaults.AccessRoadEdgeMinimumMeters == 3m && defaults.SecurityFenceMinimumMeters == 30m && defaults.UnitBoundaryMinimumMeters == 15m, "P4.13 defaults are incorrect.");
            ProjectDistanceCriteria legacy = new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m };
            ProjectDistanceCriteria draft = ProjectDistanceCriteriaStore.CreateCurrentDraft(legacy);
            Assert(draft.SchemaVersion == 2 && draft.AccessRoadEdgeMinimumMeters == 5m && draft.SecurityFenceMinimumMeters == 25m && draft.UnitBoundaryMinimumMeters == 15m && legacy.SchemaVersion == 1, "P4.13 legacy draft migration failed.");
            ProjectDistanceCriteria schemaTwoInput = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 4m, SecurityFenceMinimumMeters = 40m, UnitBoundaryMinimumMeters = 18m };
            ProjectDistanceCriteria schemaTwoCopy = ProjectDistanceCriteriaStore.CreateCurrentDraft(schemaTwoInput);
            schemaTwoCopy.AccessRoadEdgeMinimumMeters = 99m; schemaTwoCopy.SecurityFenceMinimumMeters = 99m; schemaTwoCopy.UnitBoundaryMinimumMeters = 99m;
            Assert(schemaTwoInput.SchemaVersion == 2 && schemaTwoInput.AccessRoadEdgeMinimumMeters == 4m && schemaTwoInput.SecurityFenceMinimumMeters == 40m && schemaTwoInput.UnitBoundaryMinimumMeters == 18m, "P4.13 schema-2 draft copy isolation failed.");
            ThrowsCriteria(() => ProjectDistanceCriteriaStore.Save(legacy, root), "Project Distance Criteria Save requires schema version 2.");
            ProjectDistanceCriteria custom = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 4m, SecurityFenceMinimumMeters = 40m, UnitBoundaryMinimumMeters = 18m };
            ProjectDistanceCriteriaStore.Save(custom, root);
            Assert(ProjectDistanceCriteriaStore.TryLoad(projectDirectory, root, out ProjectDistanceCriteria? loaded) && loaded != null && loaded.SchemaVersion == 2 && loaded.AccessRoadEdgeMinimumMeters == 4m && loaded.SecurityFenceMinimumMeters == 40m && loaded.UnitBoundaryMinimumMeters == 18m, "P4.13 schema-2 save/load failed.");
            ThrowsDistanceCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 0m }));
            ThrowsDistanceCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = -1m }));
            ThrowsDistanceCriteria(() => ProjectDistanceCriteriaStore.ValidateAndCopy(new ProjectDistanceCriteria { SchemaVersion = 3, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m }));

            EngineeringDistanceRuleDefinition[] rules = ProjectDistanceRuleBinder.Create(defaults).ToArray();
            EngineeringDistanceRuleDefinition projectRoad = rules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId); EngineeringDistanceRuleDefinition projectPlant = rules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId); EngineeringDistanceRuleDefinition projectUnit = rules.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId);
            Assert(rules.Select(x => x.RuleId).SequenceEqual(new[] { ProjectDistanceRuleBinder.EquipmentToPlantRuleId, ProjectDistanceRuleBinder.EquipmentToRoadRuleId, ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId }) && projectRoad.MeasurementKind == "EquipmentToRoad2D" && projectRoad.MinimumDistanceMeters == 3m && projectRoad.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge && projectRoad.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment && projectPlant.MeasurementKind == "EquipmentToBoundary2D" && projectPlant.MinimumDistanceMeters == 30m && projectPlant.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence && projectPlant.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment && projectUnit.MeasurementKind == "EquipmentToBoundary2D" && projectUnit.MinimumDistanceMeters == 15m && projectUnit.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary && projectUnit.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment && projectRoad.CriteriaProvenance != null && projectPlant.CriteriaProvenance != null && projectUnit.CriteriaProvenance != null && projectRoad.CriteriaProvenance.SourceKind == "ProjectDistanceCriteria" && projectPlant.CriteriaProvenance.SourceKind == "ProjectDistanceCriteria" && projectUnit.CriteriaProvenance.SourceKind == "ProjectDistanceCriteria" && projectRoad.CriteriaProvenance.SchemaVersion == 2 && projectPlant.CriteriaProvenance.SchemaVersion == 2 && projectUnit.CriteriaProvenance.SchemaVersion == 2 && projectRoad.CriteriaProvenance.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && projectPlant.CriteriaProvenance.CriteriaSettingKey == "SecurityFenceMinimumMeters" && projectUnit.CriteriaProvenance.CriteriaSettingKey == "UnitBoundaryMinimumMeters" && projectRoad.CriteriaProvenance.ConfiguredMinimumDistanceMeters == projectRoad.MinimumDistanceMeters && projectPlant.CriteriaProvenance.ConfiguredMinimumDistanceMeters == projectPlant.MinimumDistanceMeters && projectUnit.CriteriaProvenance.ConfiguredMinimumDistanceMeters == projectUnit.MinimumDistanceMeters, "P4.13 exact project rule binding failed.");
            EngineeringDistanceRuleRegistry registry = EngineeringDistanceRuleRegistry.CreateProjectCriteria(rules);
            ThrowsDistance(() => EngineeringDistanceRuleRegistry.Create(Standards(), rules), "engineering distance rule references unknown effective RuleId: " + ProjectDistanceRuleBinder.EquipmentToPlantRuleId);
            QaReport report = ProjectEngineeringDistanceRuntime.Evaluate(defaults, EvaluationPlot("Meters", EvaluationMeasurement("p413-road", "p413-shared", "Road", 2d, "AccessRoadEdge", "Equipment"), EvaluationMeasurement("p413-plant", "p413-shared", "Boundary", 29d, "SecurityFence", "Equipment"), EvaluationMeasurement("p413-unit", "p413-shared", "Boundary", 14d, "UnitBoundary", "Equipment")), P412Base());
            Assert(registry.Rules.Count == 3 && report.EngineeringDistanceSummary != null && report.EngineeringDistanceSummary.SchemaVersion == 2 && report.EngineeringDistanceSummary.Evaluations.Count == 3 && report.Findings.Count == 3 && report.EngineeringDistanceSummary.Evaluations.All(x => x.Status == "VIOLATIONS" && x.EvaluatedSubjectCount == 1 && x.ViolationCount == 1) && report.Findings.All(x => x.StandardsProvenance == null && x.Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.SourceKind == "ProjectDistanceCriteria" && x.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.SchemaVersion == 2) && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.CriteriaSettingKey == "AccessRoadEdgeMinimumMeters" && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.ConfiguredMinimumDistanceMeters == 3m && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.CriteriaSettingKey == "SecurityFenceMinimumMeters" && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.ConfiguredMinimumDistanceMeters == 30m && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.CriteriaSettingKey == "UnitBoundaryMinimumMeters" && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).Evidence.EngineeringDistanceEvidence!.CriteriaProvenance!.ConfiguredMinimumDistanceMeters == 15m && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).Message == "Equipment placement is closer to Road Edge geometry than the configured project minimum distance." && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).Message == "Equipment placement is closer to Security Fence geometry than the configured Equipment-to-Plant minimum distance." && report.Findings.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).Message == "Equipment placement is closer to Unit Boundary geometry than the configured project minimum distance.", "P4.13 exact project-only QA integration failed.");
            QaReport equality = ProjectEngineeringDistanceRuntime.Evaluate(defaults, EvaluationPlot("Meters", EvaluationMeasurement("p413-road-equal", "p413-shared-equal", "Road", 3d, "AccessRoadEdge", "Equipment"), EvaluationMeasurement("p413-plant-equal", "p413-shared-equal", "Boundary", 30d, "SecurityFence", "Equipment"), EvaluationMeasurement("p413-unit-equal", "p413-shared-equal", "Boundary", 15d, "UnitBoundary", "Equipment")), P412Base());
            Assert(equality.Findings.Count == 0 && equality.EngineeringDistanceSummary!.Evaluations.All(x => x.Status == "PASS") && equality.EngineeringDistanceSummary.Evaluations.Select(x => x.RuleId).SequenceEqual(equality.EngineeringDistanceSummary.Evaluations.Select(x => x.RuleId).OrderBy(x => x, StringComparer.Ordinal)), "P4.13 equality or summary behavior failed.");
            QaReport noFallback = ProjectEngineeringDistanceRuntime.Evaluate(defaults, EvaluationPlot("Meters", EvaluationMeasurement("p413-wrong-fence", "p413-wrong-fence", "Boundary", 1d, "SecurityFence", "Equipment")), P412Base());
            Assert(noFallback.EngineeringDistanceSummary!.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).Status == "INSUFFICIENT_EVIDENCE" && noFallback.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).Status == "VIOLATIONS" && noFallback.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).Status == "INSUFFICIENT_EVIDENCE", "P4.13 SecurityFence basis matching fell back incorrectly.");
            QaReport unitOnly = ProjectEngineeringDistanceRuntime.Evaluate(defaults, EvaluationPlot("Meters", EvaluationMeasurement("p413-wrong-unit", "p413-wrong-unit", "Boundary", 1d, "UnitBoundary", "Equipment")), P412Base());
            Assert(unitOnly.EngineeringDistanceSummary!.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).Status == "INSUFFICIENT_EVIDENCE" && unitOnly.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).Status == "INSUFFICIENT_EVIDENCE" && unitOnly.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).Status == "VIOLATIONS", "P4.13 UnitBoundary basis matching fell back incorrectly.");
            ProjectDistanceCriteria immutableCriteria = new ProjectDistanceCriteria { SchemaVersion = 2, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
            EquipmentSpatialMeasurement immutableMeasurement = EvaluationMeasurement("p413-immutable", "p413-immutable", "Boundary", 29d, "SecurityFence", "Equipment");
            PlotPlanModel immutablePlot = EvaluationPlot("Meters", immutableMeasurement);
            QaReport immutableBaseQa = P412Base();
            int immutableRuleCount = immutableBaseQa.RuleResults.Count; int immutableFindingCount = immutableBaseQa.Findings.Count; string immutableMeasurementId = immutableMeasurement.MeasurementId; double immutableDistance = immutableMeasurement.Distance2D; string? immutableBasis = immutableMeasurement.EngineeringMeasurementBasisKey; string? immutableApplicability = immutableMeasurement.EngineeringApplicabilityKey;
            QaReport immutableResult = ProjectEngineeringDistanceRuntime.Evaluate(immutableCriteria, immutablePlot, immutableBaseQa);
            Assert(immutableCriteria.AccessRoadEdgeMinimumMeters == 3m && immutableCriteria.SecurityFenceMinimumMeters == 30m && immutableCriteria.UnitBoundaryMinimumMeters == 15m && immutableBaseQa.RuleResults.Count == immutableRuleCount && immutableBaseQa.Findings.Count == immutableFindingCount && immutableBaseQa.EngineeringDistanceSummary == null && immutableMeasurement.MeasurementId == immutableMeasurementId && immutableMeasurement.Distance2D == immutableDistance && immutableMeasurement.EngineeringMeasurementBasisKey == immutableBasis && immutableMeasurement.EngineeringApplicabilityKey == immutableApplicability, "P4.13 runtime mutated caller-owned inputs.");
            immutableCriteria.AccessRoadEdgeMinimumMeters = 99m; immutableCriteria.SecurityFenceMinimumMeters = 99m; immutableCriteria.UnitBoundaryMinimumMeters = 99m;
            Assert(immutableResult.EngineeringDistanceSummary!.AccessRoadEdgeMinimumMeters == 3m && immutableResult.EngineeringDistanceSummary.SecurityFenceMinimumMeters == 30m && immutableResult.EngineeringDistanceSummary.UnitBoundaryMinimumMeters == 15m, "P4.13 summary retained mutable criteria state.");
            System.IO.Directory.Delete(root, true);
        }

        private static void ThrowsDistanceCriteria(Action action)
        {
            try { action(); } catch (ProjectDistanceCriteriaValidationException) { return; }
            throw new InvalidOperationException("Expected Project Distance Criteria validation failure.");
        }

        private static StandardSourceDefinition Source(string sourceId) => new StandardSourceDefinition { SourceId = sourceId, DocumentNumber = "DOC-" + sourceId, Title = "Synthetic " + sourceId, Issuer = "Test", RevisionOrEdition = "R1" };
        private static StandardClauseDefinition Clause(string clauseId, string sourceId, string reference) => new StandardClauseDefinition { ClauseId = clauseId, SourceId = sourceId, ClauseReference = reference, ClauseText = "Synthetic " + clauseId };
        private static StandardAuthorityPolicyDefinition Policy(string policyId, string sourceId, string? clauseId, string authorityClass, string enforcementMode) => new StandardAuthorityPolicyDefinition { PolicyId = policyId, SourceId = sourceId, ClauseId = clauseId, AuthorityClass = authorityClass, EnforcementMode = enforcementMode, Rationale = "rationale:" + policyId };
        private static EffectiveStandardRuleDefinition Rule(string ruleId, params string[] clauseIds) => new EffectiveStandardRuleDefinition { RuleId = ruleId, ClauseIds = clauseIds.ToList() };
        private static StandardRulePackResolution Resolution(params EffectiveStandardRuleDefinition[] rules) => new StandardRulePackResolution { EffectiveRules = rules.ToList() };
        private static StandardClauseAuthorityResolution GateClause(string clauseId, string authorityClass, string enforcementMode, string sourceId = "source", string policyId = "policy", string? policyClauseId = null, string rationale = "rationale") => new StandardClauseAuthorityResolution { ClauseId = clauseId, SourceId = sourceId, PolicyId = policyId, PolicyClauseId = policyClauseId, AuthorityClass = authorityClass, EnforcementMode = enforcementMode, Rationale = rationale };
        private static StandardRuleAuthorityResolution GateRule(string ruleId, string mode, params StandardClauseAuthorityResolution[] clauses) => new StandardRuleAuthorityResolution { RuleId = ruleId, EffectiveEnforcementMode = mode, Clauses = clauses.ToList() };
        private static StandardAuthorityResolution GateResolution(params StandardRuleAuthorityResolution[] rules) => new StandardAuthorityResolution { Rules = rules.ToList() };
        private static string ReviewIdentity(StandardAuthorityReviewProjection projection) => string.Join("|", projection.ReviewItems.Select(item => item.RuleId + ":" + item.EffectiveEnforcementMode + ":" + item.DecisionState + ":" + item.Message + ":" + string.Join(",", item.Clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.PolicyId + ":" + clause.PolicyClauseId + ":" + clause.AuthorityClass + ":" + clause.EnforcementMode + ":" + clause.Rationale))));
        private static string Mode(StandardAuthorityPolicyRegistry policies, StandardClauseRegistry clauses, string ruleId, params string[] clauseIds) => StandardAuthorityResolver.Resolve(Resolution(Rule(ruleId, clauseIds)), clauses, policies).Rules.Single().EffectiveEnforcementMode;
        private static void ThrowsGate(Action action, string? message = null)
        {
            try { action(); }
            catch (StandardAuthorityExecutionGateException exception)
            {
                if (message == null || exception.Message == message) return;
                throw new InvalidOperationException("Unexpected Standard Authority Execution Gate message: " + exception.Message);
            }
            throw new InvalidOperationException("Expected Standard Authority Execution Gate failure.");
        }
        private static void ThrowsResolution(Action action, string? message = null)
        {
            try { action(); }
            catch (StandardAuthorityResolutionException exception)
            {
                if (message == null || exception.Message == message) return;
                throw new InvalidOperationException("Unexpected Standard Authority Resolution message: " + exception.Message);
            }
            throw new InvalidOperationException("Expected Standard Authority Resolution failure.");
        }
        private static void ThrowsAuthority(Action action, string? message = null)
        {
            try { action(); }
            catch (StandardAuthorityPolicyRegistryValidationException exception)
            {
                if (message == null || exception.Message == message) return;
                throw new InvalidOperationException("Unexpected Standard Authority Policy Registry validation message: " + exception.Message);
            }
            throw new InvalidOperationException("Expected Standard Authority Policy Registry validation failure.");
        }

        private static void RunP102C1Presentation()
        {
            EngineeringDistanceRunSummary summary = new EngineeringDistanceRunSummary
            {
                AccessRoadEdgeMinimumMeters = 3m,
                SecurityFenceMinimumMeters = 30m,
                UnitBoundaryMinimumMeters = 15m,
                Evaluations = new List<EngineeringDistanceRuleEvaluation>
                {
                    PresentationEvaluation(ProjectDistanceRuleBinder.EquipmentToRoadRuleId, "EquipmentToRoad2D", "PASS", "AccessRoadEdge"),
                    PresentationEvaluation(ProjectDistanceRuleBinder.EquipmentToPlantRuleId, "EquipmentToBoundary2D", "VIOLATIONS", "SecurityFence"),
                    PresentationEvaluation(ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId, "EquipmentToBoundary2D", "INSUFFICIENT_EVIDENCE", "UnitBoundary")
                },
                Governance = new ProjectDistanceGovernanceAggregateResult
                {
                    Items = new List<ProjectDistanceGovernanceAggregateItem>
                    {
                        new ProjectDistanceGovernanceAggregateItem
                        {
                            ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                            Resolution = new ProjectDistanceGovernedRuleResolution { State = "PROJECT_RULE_ONLY" }
                        }
                    }
                }
            };
            PlotSpatialMeasurementModel measurements = new PlotSpatialMeasurementModel
            {
                EquipmentSpatialMeasurements = new List<EquipmentSpatialMeasurement>
                {
                    PresentationMeasurement("road-near", "placement-road", "Road", 91.5d, "AccessRoadEdge"),
                    PresentationMeasurement("road-far", "placement-road", "Road", 100d, "AccessRoadEdge"),
                    PresentationMeasurement("fence", "placement-fence", "Boundary", 25d, "SecurityFence")
                }
            };
            IReadOnlyList<PlotPlanDistancePresentationRow> rows = PlotPlanDistancePresentation.Build(summary, measurements);
            Assert(rows.Count == 3 && rows.Select(x => x.Name).SequenceEqual(new[] { "Equipment → Road", "Equipment → Plant / Security Fence", "Equipment → Unit Boundary" }), "P10.2 distance presentation rows or friendly labels changed.");
            Assert(rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId).MinimumDistanceMeters == 3m && rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId).MinimumDistanceMeters == 30m && rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId).MinimumDistanceMeters == 15m, "P10.2 distance presentation criteria values changed.");
            PlotPlanDistancePresentationRow pass = rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId);
            PlotPlanDistancePresentationRow violations = rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId);
            PlotPlanDistancePresentationRow insufficient = rows.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId);
            Assert(pass.UnderlyingStatus == "PASS" && pass.Observed == "91.5 m (1 resolved)" && pass.Observed != "<not resolved>" && pass.Interpretation.IndexOf("Project criterion only", StringComparison.Ordinal) >= 0, "P10.2 PASS presentation did not use resolved measurement evidence.");
            Assert(violations.UnderlyingStatus == "VIOLATIONS" && violations.Observed == "25 m (1 resolved)", "P10.2 VIOLATIONS presentation did not use resolved measurement evidence.");
            Assert(insufficient.UnderlyingStatus == "INSUFFICIENT_EVIDENCE" && insufficient.Observed == "<not resolved>", "P10.2 insufficient-evidence presentation changed.");
            Assert(rows.Select(x => x.RuleId).Distinct(StringComparer.Ordinal).Count() == 3, "P10.2 produced duplicate project distance rows.");
        }

        private static EngineeringDistanceRuleEvaluation PresentationEvaluation(string ruleId, string measurementKind, string status, string basis) => new EngineeringDistanceRuleEvaluation
        {
            RuleId = ruleId,
            MeasurementKind = measurementKind,
            Status = status,
            RequiredMeasurementBasisKey = basis,
            RequiredApplicabilityKey = "Equipment",
            MetersPerDrawingUnit = 1d
        };

        private static EquipmentSpatialMeasurement PresentationMeasurement(string id, string placement, string subjectKind, double distance, string basis) => new EquipmentSpatialMeasurement
        {
            MeasurementId = id,
            PlacementId = placement,
            SubjectKind = subjectKind,
            Distance2D = distance,
            EngineeringMeasurementBasisKey = basis,
            EngineeringApplicabilityKey = "Equipment"
        };

        private static void RunP412()
        {
            const string roadRuleId = "std.plotplan.processing-equipment.access-road-edge.minimum-clearance";
            const string fenceRuleId = "std.plotplan.equipment.security-fence.minimum-clearance";
            ProjectEngineeringDistanceReferenceContext references = ProjectEngineeringDistanceReferenceCatalog.Create();
            Assert(references.SourceRegistry.Sources.Count == 2 && references.ClauseRegistry.Clauses.Count == 4 && references.Resolution.EffectiveRules.Count == 2, "P4.12 reference catalog counts are incorrect.");
            Assert(references.SourceRegistry.Sources.Select(x => x.SourceSha256).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E", "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" }), "P4.12 reference catalog source hashes are incorrect.");
            Assert(references.ClauseRegistry.Clauses.Select(x => x.ClauseId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "ips.e-pr-190.8.2.3", "ips.e-pr-190.f.11.2", "trc.pcd-pd-135.3.3.10.a", "trc.pcd-pd-135.3.3.9.b" }), "P4.12 reference catalog clause identifiers are incorrect.");
            Assert(references.Resolution.EffectiveRules.Select(x => x.RuleId).SequenceEqual(new[] { fenceRuleId, roadRuleId }) && references.Resolution.EffectiveRules.All(x => x.WinningPrecedence == 10 && x.OverriddenPackIds.Count == 0), "P4.12 reference catalog effective rules are incorrect.");

            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EngiSentry-P412-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(root);
            string projectDirectory = ProjectDistanceCriteriaStore.ResolveProjectDirectory(System.IO.Path.Combine(root, "P412.dwg"));
            ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = 1, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = 5m, SecurityFenceMinimumMeters = 25m };
            EquipmentSpatialMeasurement road = EvaluationMeasurement("p412-road", "p412-road", "Road", 4d, "AccessRoadEdge", "ProcessingEquipment");
            EquipmentSpatialMeasurement fence = EvaluationMeasurement("p412-fence", "p412-fence", "Boundary", 20d, "SecurityFence", "Equipment");
            PlotPlanModel violationsPlot = EvaluationPlot("Meters", road, fence);
            QaReport baseQa = P412Base();
            QaReport report = ProjectEngineeringDistanceRuntime.Evaluate(criteria, violationsPlot, baseQa);
            EngineeringDistanceRuleEvaluation roadEvaluation = report.EngineeringDistanceSummary!.Evaluations.Single(x => x.RuleId == roadRuleId);
            EngineeringDistanceRuleEvaluation fenceEvaluation = report.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == fenceRuleId);
            Assert(roadEvaluation.MinimumDistanceMeters == 5m && roadEvaluation.Status == "VIOLATIONS" && fenceEvaluation.MinimumDistanceMeters == 25m && fenceEvaluation.Status == "VIOLATIONS" && report.Findings.Count == 2, "P4.12 dynamic 5m/25m evaluation failed.");
            QaFinding roadFinding = report.Findings.Single(x => x.RuleId == roadRuleId);
            QaFinding fenceFinding = report.Findings.Single(x => x.RuleId == fenceRuleId);
            Assert(P412ProjectFinding(roadFinding, "AccessRoadEdgeMinimumMeters", 5m) && P412ProjectFinding(fenceFinding, "SecurityFenceMinimumMeters", 25m), "P4.12 project finding provenance failed.");
            EngineeringDistanceRunSummary summary = report.EngineeringDistanceSummary!;
            Assert(summary.SourceKind == "ProjectDistanceCriteria" && summary.ProjectDirectory == projectDirectory && summary.SchemaVersion == 1 && summary.AccessRoadEdgeMinimumMeters == 5m && summary.SecurityFenceMinimumMeters == 25m && summary.Evaluations.Count == 2 && summary.Evaluations.Select(x => x.RuleId).SequenceEqual(summary.Evaluations.Select(x => x.RuleId).OrderBy(x => x, StringComparer.Ordinal)), "P4.12 run summary failed.");
            Assert(EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.processing-equipment.access-road-edge.3m").ProposedMinimumDistanceMeters == 3m && EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == "candidate.equipment.security-fence.30m").ProposedMinimumDistanceMeters == 30m && EngineeringDistanceCandidateCatalog.BuiltInCandidates.All(x => x.ApprovalStatus == "PENDING_ENGINEERING_APPROVAL"), "P4.12 runtime mutated P4.7 candidate state.");

            QaReport exactApplicability = ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters", EvaluationMeasurement("p412-road-mismatch", "p412-road-mismatch", "Road", 4d, "AccessRoadEdge", "Equipment"), fence), P412Base());
            Assert(exactApplicability.EngineeringDistanceSummary!.Evaluations.Single(x => x.RuleId == roadRuleId).Status == "INSUFFICIENT_EVIDENCE" && exactApplicability.EngineeringDistanceSummary.Evaluations.Single(x => x.RuleId == fenceRuleId).Status == "VIOLATIONS", "P4.12 applicability matching was not exact.");

            QaReport equality = ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters", EvaluationMeasurement("p412-road-equal", "p412-road-equal", "Road", 5d, "AccessRoadEdge", "ProcessingEquipment"), EvaluationMeasurement("p412-fence-equal", "p412-fence-equal", "Boundary", 25d, "SecurityFence", "Equipment")), P412Base());
            Assert(equality.EngineeringDistanceSummary != null && equality.EngineeringDistanceSummary.AccessRoadEdgeMinimumMeters == 5m && equality.EngineeringDistanceSummary.SecurityFenceMinimumMeters == 25m && equality.EngineeringDistanceSummary.Evaluations.Count == 2 && equality.Findings.Count == 0 && equality.EngineeringDistanceSummary.Evaluations.All(x => x.Status == "PASS") && equality.OverallStatus == "PASS", "P4.12 equality behavior failed.");

            QaReport baseError = P412Base();
            baseError.Findings.Add(new QaFinding { FindingId = "p412-base-error", RuleId = "p412.base", Severity = "Error", SubjectId = "p412" });
            QaReport errorCombined = ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters", EvaluationMeasurement("p412-road-pass", "p412-road-pass", "Road", 5d, "AccessRoadEdge", "ProcessingEquipment"), EvaluationMeasurement("p412-fence-pass", "p412-fence-pass", "Boundary", 25d, "SecurityFence", "Equipment")), baseError);
            QaReport insufficientCombined = ProjectEngineeringDistanceRuntime.Evaluate(criteria, EvaluationPlot("Meters"), P412Base());
            Assert(errorCombined.OverallStatus == "FAIL" && report.OverallStatus == "REVIEW_REQUIRED" && insufficientCombined.OverallStatus == "INSUFFICIENT_EVIDENCE" && P412StatisticsAgree(errorCombined) && P412StatisticsAgree(report) && P412StatisticsAgree(insufficientCombined) && P412StatisticsAgree(equality), "P4.12 combined status or statistics failed.");

            QaReport duplicateRule = P412Base(); duplicateRule.RuleResults.Add(new QaRuleResult { RuleId = roadRuleId, Severity = "Warning", Status = "PASS" });
            ThrowsP412(() => ProjectEngineeringDistanceRuntime.Evaluate(criteria, violationsPlot, duplicateRule), "duplicate combined QA RuleId: " + roadRuleId);
            QaReport duplicateFinding = P412Base(); duplicateFinding.Findings.Add(new QaFinding { FindingId = roadRuleId + ":p412-road", RuleId = "p412.base", Severity = "Warning", SubjectId = "p412-road" });
            ThrowsP412(() => ProjectEngineeringDistanceRuntime.Evaluate(criteria, violationsPlot, duplicateFinding), "duplicate combined QA FindingId: " + roadRuleId + ":p412-road");
            QaReport doubleIntegrated = P412Base(); doubleIntegrated.EngineeringDistanceSummary = new EngineeringDistanceRunSummary();
            ThrowsP412(() => ProjectEngineeringDistanceRuntime.Evaluate(criteria, violationsPlot, doubleIntegrated), "base QA already contains engineering distance summary");

            int baseRuleCount = baseQa.RuleResults.Count;
            int baseFindingCount = baseQa.Findings.Count;
            string roadId = road.MeasurementId; double roadDistance = road.Distance2D;
            criteria.AccessRoadEdgeMinimumMeters = 99m; criteria.SecurityFenceMinimumMeters = 99m;
            Assert(baseQa.RuleResults.Count == baseRuleCount && baseQa.Findings.Count == baseFindingCount && baseQa.EngineeringDistanceSummary == null && road.MeasurementId == roadId && road.Distance2D == roadDistance && summary.AccessRoadEdgeMinimumMeters == 5m && summary.SecurityFenceMinimumMeters == 25m, "P4.12 runtime did not preserve caller-owned state.");
            System.IO.Directory.Delete(root, true);
        }

        private static QaReport P412Base() => new QaReport { RuleResults = new List<QaRuleResult> { new QaRuleResult { RuleId = "p412.base", Severity = "Info", Status = "PASS" } }, Findings = new List<QaFinding>(), Statistics = new QaStatistics() };
        private static bool P412ProjectFinding(QaFinding finding, string settingKey, decimal configuredMinimum) => finding.Evidence.EngineeringDistanceEvidence != null && finding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance != null && finding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.SourceKind == "ProjectDistanceCriteria" && finding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.CriteriaSettingKey == settingKey && finding.Evidence.EngineeringDistanceEvidence.CriteriaProvenance.ConfiguredMinimumDistanceMeters == configuredMinimum && finding.StandardsProvenance == null && finding.Message.Contains("configured project minimum distance") && !finding.Message.Contains("standards-backed minimum distance");
        private static bool P412StatisticsAgree(QaReport report) => report.Statistics.RuleCount == report.RuleResults.Count && report.Statistics.TotalFindings == report.Findings.Count && report.Statistics.InfoFindings == report.Findings.Count(x => x.Severity == "Info") && report.Statistics.WarningFindings == report.Findings.Count(x => x.Severity == "Warning") && report.Statistics.ErrorFindings == report.Findings.Count(x => x.Severity == "Error") && report.Statistics.RulesPassed == report.RuleResults.Count(x => x.Status == "PASS") && report.Statistics.RulesWithFindings == report.RuleResults.Count(x => x.Status == "FINDINGS") && report.Statistics.RulesNotApplicable == report.RuleResults.Count(x => x.Status == "NOT_APPLICABLE") && report.Statistics.RulesInsufficientEvidence == report.RuleResults.Count(x => x.Status == "INSUFFICIENT_EVIDENCE");
        private static void ThrowsP412(Action action, string expectedMessage) { try { action(); } catch (ProjectEngineeringDistanceRuntimeException exception) { Assert(exception.Message == expectedMessage, "Unexpected P4.12 runtime message: " + exception.Message); return; } throw new InvalidOperationException("Expected P4.12 runtime failure: " + expectedMessage); }

        private static EquipmentPlacement Placement(string id, string tag, string handle) => new EquipmentPlacement { PlacementId = id, EquipmentEntityId = "equipment:" + tag, NormalizedTag = tag, TagSourceHandle = handle, PlacementStatus = "Complete", Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 1m }, Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 1m } };
        private static EquipmentSpatialMeasurement Measurement(string id, string placement, string kind, decimal distance, string label, string geometry) => new EquipmentSpatialMeasurement { MeasurementId = id, PlacementId = placement, EquipmentEntityId = "equipment:" + placement, NormalizedTag = placement, SubjectKind = kind, ConstraintId = "constraint:" + id, LabelSourceHandle = label, GeometrySourceHandle = geometry, Distance2D = decimal.ToDouble(distance) };
        private static EngineeringStandardProfile Profile(string id, string level, params RuleSetting[] settings) => new EngineeringStandardProfile { ProfileId = id, Name = id, Level = level, Source = "M5.0 test fixture only", Version = "1.0", RuleSettings = settings.ToList() };
        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).Concat(new[] { new KnownQaRule { RuleId = "PP-CLEAR-001", Severity = "Warning", DefaultEnabled = false }, new KnownQaRule { RuleId = "PP-CLEAR-002", Severity = "Warning", DefaultEnabled = false } }).ToList();
        private static EffectiveRuleSetting Setting(ProfileResolution resolution, string id) => resolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == id);
        private static void Invalid(List<KnownQaRule> catalog, EngineeringStandardProfile profile, string expected) { bool thrown = false; try { ProfileResolver.Resolve(catalog, new[] { profile }); } catch (ProfileValidationException exception) { thrown = exception.Message.Contains(expected); } Assert(thrown, "Expected profile validation failure: " + expected); }
        private static string QaIdentity(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static StandardRulePackResolution Standards(params string[] ruleIds) => new StandardRulePackResolution { EffectiveRules = ruleIds.Select(x => new EffectiveStandardRuleDefinition { RuleId = x }).ToList() };
        private static EngineeringDistanceRuleDefinition DistanceRule(string ruleId, string measurementKind, decimal minimumDistanceMeters) => DistanceRule(ruleId, measurementKind, minimumDistanceMeters, measurementKind == "EquipmentToRoad2D" ? "AccessRoadEdge" : "SecurityFence", "ProcessingEquipment");
        private static EngineeringDistanceRuleDefinition DistanceRule(string ruleId, string measurementKind, decimal minimumDistanceMeters, string requiredMeasurementBasisKey, string requiredApplicabilityKey) => new EngineeringDistanceRuleDefinition { RuleId = ruleId, MeasurementKind = measurementKind, MinimumDistanceMeters = minimumDistanceMeters, RequiredMeasurementBasisKey = requiredMeasurementBasisKey, RequiredApplicabilityKey = requiredApplicabilityKey };
        private static PlotPlanModel EvaluationPlot(string insertionUnits, params EquipmentSpatialMeasurement[] measurements) => new PlotPlanModel
        {
            Drawing = new SemanticDrawing { InsertionUnits = insertionUnits },
            EquipmentPlacements = measurements.Where(x => x != null && !string.IsNullOrWhiteSpace(x.PlacementId)).GroupBy(x => x.PlacementId, StringComparer.Ordinal).Select(x => new EquipmentPlacement
            {
                PlacementId = x.Key, EquipmentEntityId = x.First().EquipmentEntityId, PlacementStatus = "Complete", EngineeringApplicabilityKey = x.First().EngineeringApplicabilityKey,
                Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 0m },
                Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 0m }
            }).ToList(),
            Measurements = new PlotSpatialMeasurementModel { EquipmentSpatialMeasurements = measurements.ToList() }
        };
        private static EquipmentSpatialMeasurement EvaluationMeasurement(string measurementId, string placementId, string subjectKind, double distance) => EvaluationMeasurement(measurementId, placementId, subjectKind, distance, subjectKind == "Road" ? "AccessRoadEdge" : "SecurityFence", "ProcessingEquipment");
        private static EquipmentSpatialMeasurement EvaluationMeasurement(string measurementId, string placementId, string subjectKind, double distance, string? engineeringMeasurementBasisKey, string? engineeringApplicabilityKey) => new EquipmentSpatialMeasurement { MeasurementId = measurementId, PlacementId = placementId, EquipmentEntityId = "equipment:" + placementId, NormalizedTag = "tag:" + placementId, SubjectKind = subjectKind, ConstraintId = "constraint:" + measurementId, LabelSourceHandle = "label:" + measurementId, GeometrySourceHandle = "geometry:" + measurementId, ClosestPointX = 1d, ClosestPointY = 2d, Distance2D = distance, EngineeringMeasurementBasisKey = engineeringMeasurementBasisKey, EngineeringApplicabilityKey = engineeringApplicabilityKey };
        private static string EvaluationIdentity(IEnumerable<EngineeringDistanceRuleEvaluation> evaluations) => string.Join("|", evaluations.Select(x => x.RuleId + ":" + x.MeasurementKind + ":" + x.MinimumDistanceMeters + ":" + x.MinimumDistanceDrawingUnits + ":" + x.InsertionUnits + ":" + x.CanonicalUnit + ":" + x.MetersPerDrawingUnit + ":" + x.EvaluatedSubjectCount + ":" + x.ViolationCount + ":" + x.Status + ":" + string.Join(",", x.Violations.Select(y => y.PlacementId + ":" + y.MeasurementId + ":" + y.ActualDistanceDrawingUnits + ":" + y.ActualDistanceMeters))));
        private static StandardSourceRegistry QaSources() => StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "source.test", DocumentNumber = "DOC-TEST", Title = "Synthetic Source", Issuer = "Test Issuer", RevisionOrEdition = "R1", SourceFileName = "synthetic.pdf", SourceSha256 = new string('A', 64) } });
        private static StandardClauseRegistry QaClauses(StandardSourceRegistry sources, params string[] ruleIds) => StandardClauseRegistry.Create(sources, ruleIds.Distinct(StringComparer.Ordinal).Select((ruleId, index) => new StandardClauseDefinition { ClauseId = "clause." + ruleId, SourceId = "source.test", ClauseReference = (index + 1).ToString(), ClauseText = "Text " + ruleId, Heading = "Heading", PageNumber = 1 }));
        private static StandardRulePackResolution QaStandards(params string[] ruleIds) => new StandardRulePackResolution { EffectiveRules = ruleIds.Select(ruleId => new EffectiveStandardRuleDefinition { RuleId = ruleId, Title = "Title " + ruleId, ClauseIds = new List<string> { "clause." + ruleId }, WinningPackId = "pack.test", WinningPrecedence = 1, OverriddenPackIds = new List<string> { "pack.previous" } }).ToList() };
        private static EngineeringDistanceRuleEvaluation Evaluation(string ruleId, string measurementKind, string status, int evaluatedSubjectCount, int violationCount, params EngineeringDistanceViolation[] violations) => new EngineeringDistanceRuleEvaluation { RuleId = ruleId, MeasurementKind = measurementKind, MinimumDistanceMeters = 1m, MinimumDistanceDrawingUnits = 1d, InsertionUnits = "Meters", CanonicalUnit = "m", MetersPerDrawingUnit = 1d, Status = status, EvaluatedSubjectCount = evaluatedSubjectCount, ViolationCount = violationCount, Violations = violations.ToList(), RequiredMeasurementBasisKey = measurementKind == "EquipmentToRoad2D" ? "AccessRoadEdge" : "SecurityFence", RequiredApplicabilityKey = "ProcessingEquipment" };
        private static EngineeringDistanceViolation Violation(string measurementId, string placementId, string subjectKind) => new EngineeringDistanceViolation { MeasurementId = measurementId, PlacementId = placementId, EquipmentEntityId = "equipment:" + placementId, NormalizedTag = "tag:" + placementId, SubjectKind = subjectKind, ConstraintId = "constraint:" + measurementId, ActualDistanceDrawingUnits = 0.5d, ActualDistanceMeters = 0.5d, LabelSourceHandle = "label:" + measurementId, GeometrySourceHandle = "geometry:" + measurementId, ClosestPointX = 1d, ClosestPointY = 2d, MeasurementBasisKey = subjectKind == "Road" ? "AccessRoadEdge" : "SecurityFence", ApplicabilityKey = "ProcessingEquipment" };
        private static string QaDistanceIdentity(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Status + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + string.Join(",", x.SourceHandles) + ":" + x.Evidence.EngineeringDistanceEvidence!.MeasurementId + ":" + x.StandardsProvenance!.RuleId + ":" + string.Join(",", x.StandardsProvenance.Clauses.Select(c => c.ClauseId))));
        private static StandardSourceDefinition[] P46Sources() => new[]
        {
            new StandardSourceDefinition { SourceId = "trc.pcd-pd-135.rev1", DocumentNumber = "PCD-PD-135", Title = "PIPING DESIGN CRITERIA", Issuer = "Tehran Raymand Consulting Engineers", RevisionOrEdition = "1", SourceFileName = "PCD-PD-135.pdf", SourceSha256 = "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" },
            new StandardSourceDefinition { SourceId = "ips.e-pr-190.rev1.2009", DocumentNumber = "IPS-E-PR-190(1)", Title = "ENGINEERING STANDARD FOR LAYOUT AND SPACING", Issuer = "Iranian Ministry of Petroleum", RevisionOrEdition = "First Revision / March 2009", SourceFileName = "IPS E-PR-190.pdf", SourceSha256 = "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" }
        };
        private static StandardClauseDefinition[] P46Clauses() => new[]
        {
            new StandardClauseDefinition { ClauseId = "trc.pcd-pd-135.3.3.9.b", SourceId = "trc.pcd-pd-135.rev1", ClauseReference = "3.3.9(b) — Roadways", Heading = "3.3.9 — Roadways", PageNumber = 16, ClauseText = "Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions." },
            new StandardClauseDefinition { ClauseId = "ips.e-pr-190.8.2.3", SourceId = "ips.e-pr-190.rev1.2009", ClauseReference = "8.2(3) — Roadways", Heading = "8.2 — Roadways", PageNumber = 22, ClauseText = "Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions." },
            new StandardClauseDefinition { ClauseId = "trc.pcd-pd-135.3.3.10.a", SourceId = "trc.pcd-pd-135.rev1", ClauseReference = "3.3.10(a) — Overall Concerns", Heading = "3.3.10 — Overall Concerns", PageNumber = 16, ClauseText = "The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m." },
            new StandardClauseDefinition { ClauseId = "ips.e-pr-190.f.11.2", SourceId = "ips.e-pr-190.rev1.2009", ClauseReference = "f.11.2 — Security fence", Heading = "f.11 — Security fence", PageNumber = 21, ClauseText = "The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m." }
        };
        private static StandardRulePackDefinition[] P46Packs() => new[]
        {
            new StandardRulePackDefinition { PackId = "candidate.project.access-road-clearance", Name = "P4.6 Access Road Candidate", Rules = new List<StandardRuleDefinition> { new StandardRuleDefinition { RuleId = "std.plotplan.processing-equipment.access-road-edge.minimum-clearance", Title = "Minimum Processing Equipment to Access Road Edge Clearance", ClauseIds = new List<string> { "ips.e-pr-190.8.2.3", "trc.pcd-pd-135.3.3.9.b" } } } },
            new StandardRulePackDefinition { PackId = "candidate.project.security-fence-clearance", Name = "P4.6 Security Fence Candidate", Rules = new List<StandardRuleDefinition> { new StandardRuleDefinition { RuleId = "std.plotplan.equipment.security-fence.minimum-clearance", Title = "Minimum Equipment to Security Fence Clearance", ClauseIds = new List<string> { "ips.e-pr-190.f.11.2", "trc.pcd-pd-135.3.3.10.a" } } } }
        };
        private static EngineeringDistanceRuleDefinition[] P46Rules() => new[]
        {
            DistanceRule("std.plotplan.processing-equipment.access-road-edge.minimum-clearance", "EquipmentToRoad2D", 3m, "AccessRoadEdge", "ProcessingEquipment"),
            DistanceRule("std.plotplan.equipment.security-fence.minimum-clearance", "EquipmentToBoundary2D", 30m, "SecurityFence", "Equipment")
        };
        private static bool RealCandidateEvidence(QaFinding finding, string ruleId, string packId, decimal minimumDistanceMeters, double actualDistanceMeters, string basis, string applicability, string pcdClauseId, string ipsClauseId)
        {
            EngineeringDistanceEvidence? evidence = finding.Evidence.EngineeringDistanceEvidence;
            StandardFindingProvenance? provenance = finding.StandardsProvenance;
            if (evidence == null || provenance == null || finding.RuleId != ruleId || provenance.RuleId != ruleId || provenance.WinningPackId != packId || provenance.WinningPrecedence != 10 || provenance.Clauses.Count != 2) return false;
            if (evidence.MinimumDistanceMeters != minimumDistanceMeters || evidence.ActualDistanceMeters != actualDistanceMeters || evidence.RequiredMeasurementBasisKey != basis || evidence.ActualMeasurementBasisKey != basis || evidence.RequiredApplicabilityKey != applicability || evidence.ActualApplicabilityKey != applicability) return false;
            StandardClauseCitation pcd = provenance.Clauses.Single(x => x.ClauseId == pcdClauseId);
            StandardClauseCitation ips = provenance.Clauses.Single(x => x.ClauseId == ipsClauseId);
            bool pcdClause = pcdClauseId == "trc.pcd-pd-135.3.3.9.b"
                ? pcd.ClauseReference == "3.3.9(b) — Roadways" && pcd.Heading == "3.3.9 — Roadways" && pcd.PageNumber == 16 && pcd.ClauseText == "Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions."
                : pcd.ClauseReference == "3.3.10(a) — Overall Concerns" && pcd.Heading == "3.3.10 — Overall Concerns" && pcd.PageNumber == 16 && pcd.ClauseText == "The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m.";
            bool ipsClause = ipsClauseId == "ips.e-pr-190.8.2.3"
                ? ips.ClauseReference == "8.2(3) — Roadways" && ips.Heading == "8.2 — Roadways" && ips.PageNumber == 22 && ips.ClauseText == "Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions."
                : ips.ClauseReference == "f.11.2 — Security fence" && ips.Heading == "f.11 — Security fence" && ips.PageNumber == 21 && ips.ClauseText == "The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m.";
            return pcd.SourceId == "trc.pcd-pd-135.rev1" && pcd.DocumentNumber == "PCD-PD-135" && pcd.RevisionOrEdition == "1" && pcd.SourceFileName == "PCD-PD-135.pdf" && pcd.SourceSha256 == "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" && pcdClause
                && ips.SourceId == "ips.e-pr-190.rev1.2009" && ips.DocumentNumber == "IPS-E-PR-190(1)" && ips.RevisionOrEdition == "First Revision / March 2009" && ips.SourceFileName == "IPS E-PR-190.pdf" && ips.SourceSha256 == "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" && ipsClause;
        }
        private static string P46Identity(StandardRulePackResolution resolution, IEnumerable<EngineeringDistanceRuleEvaluation> evaluations, QaReport report) => string.Join("|", resolution.EffectiveRules.Select(x => x.RuleId + ":" + x.WinningPackId + ":" + string.Join(",", x.ClauseIds))) + ";" + EvaluationIdentity(evaluations) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Evidence.EngineeringDistanceEvidence!.MinimumDistanceMeters + ":" + x.Evidence.EngineeringDistanceEvidence.ActualDistanceMeters + ":" + string.Join(",", x.StandardsProvenance!.Clauses.Select(c => c.ClauseId))));
        private static EngineeringDistanceCandidateDefinition P47Candidate(string candidateId, string packId, string ruleId, string measurementKind, decimal minimumDistanceMeters, string basis, string applicability, params string[] clauseIds) => new EngineeringDistanceCandidateDefinition { CandidateId = candidateId, PackId = packId, RuleId = ruleId, Title = "Candidate " + candidateId, MeasurementKind = measurementKind, ProposedMinimumDistanceMeters = minimumDistanceMeters, RequiredMeasurementBasisKey = basis, RequiredApplicabilityKey = applicability, ApprovalStatus = "PENDING_ENGINEERING_APPROVAL", ClauseIds = clauseIds.ToList() };
        private static EngineeringDistanceApprovalDecision P47Decision(string candidateId, string decisionStatus, string? approvalReference) => new EngineeringDistanceApprovalDecision { CandidateId = candidateId, DecisionStatus = decisionStatus, ApprovalReference = approvalReference };
        private static string P47CatalogIdentity(EngineeringDistanceCandidateCatalog catalog) => string.Join("|", catalog.Candidates.Select(x => x.CandidateId + ":" + x.PackId + ":" + x.RuleId + ":" + x.ProposedMinimumDistanceMeters + ":" + string.Join(",", x.ClauseIds)));
        private static void WriteP48Criteria(string path, ProjectDistanceCriteria criteria)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using (System.IO.FileStream stream = System.IO.File.Create(path)) new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ProjectDistanceCriteria)).WriteObject(stream, criteria);
        }
        private static void ThrowsCriteria(Action action, string expectedMessage) { try { action(); } catch (ProjectDistanceCriteriaValidationException exception) { Assert(exception.Message == expectedMessage, "Unexpected Project Distance Criteria message: " + exception.Message); return; } throw new InvalidOperationException("Expected Project Distance Criteria validation failure: " + expectedMessage); }
        private static void ThrowsBinding(Action action, string expectedMessage) { try { action(); } catch (ProjectDistanceRuleBindingException exception) { Assert(exception.Message == expectedMessage, "Unexpected Project Distance Rule Binding message: " + exception.Message); return; } throw new InvalidOperationException("Expected Project Distance Rule Binding failure: " + expectedMessage); }
        private static void ThrowsCandidate(Action action, string expectedMessage) { try { action(); } catch (EngineeringDistanceCandidateCatalogValidationException exception) { Assert(exception.Message == expectedMessage, "Unexpected candidate validation message: " + exception.Message); return; } throw new InvalidOperationException("Expected candidate validation failure: " + expectedMessage); }
        private static void ThrowsApproval(Action action, string expectedMessage) { try { action(); } catch (EngineeringDistanceApprovalGateException exception) { Assert(exception.Message == expectedMessage, "Unexpected approval gate message: " + exception.Message); return; } throw new InvalidOperationException("Expected approval gate failure: " + expectedMessage); }
        private static void ThrowsDistance(Action action, string expectedMessage) { try { action(); } catch (EngineeringDistanceRuleRegistryValidationException exception) { Assert(exception.Message == expectedMessage, "Unexpected distance-rule validation message: " + exception.Message); return; } throw new InvalidOperationException("Expected distance-rule validation failure: " + expectedMessage); }
        private static void ThrowsEvaluation(Action action, string expectedMessage) { try { action(); } catch (EngineeringDistanceRuleEvaluationException exception) { Assert(exception.Message == expectedMessage, "Unexpected distance-evaluation message: " + exception.Message); return; } throw new InvalidOperationException("Expected distance-evaluation failure: " + expectedMessage); }
        private static void ThrowsIntegration(Action action, string expectedMessage) { try { action(); } catch (EngineeringDistanceQaIntegrationException exception) { Assert(exception.Message == expectedMessage, "Unexpected engineering-distance QA message: " + exception.Message); return; } throw new InvalidOperationException("Expected engineering-distance QA failure: " + expectedMessage); }
        private static void ThrowsDrawingUnit(Action action, string expectedMessage) { try { action(); } catch (DrawingUnitResolutionException exception) { Assert(exception.Message == expectedMessage, "Unexpected drawing-unit message: " + exception.Message); return; } throw new InvalidOperationException("Expected drawing-unit validation failure: " + expectedMessage); }
        private static void ThrowsArgumentNull(Action action, string message) { try { action(); } catch (ArgumentNullException) { return; } throw new InvalidOperationException(message); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
