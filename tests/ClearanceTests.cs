using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class ClearanceTests
    {
        internal static void Run()
        {
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
        }

        private static EquipmentPlacement Placement(string id, string tag, string handle) => new EquipmentPlacement { PlacementId = id, EquipmentEntityId = "equipment:" + tag, NormalizedTag = tag, TagSourceHandle = handle, PlacementStatus = "Complete", Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 1m }, Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 1m } };
        private static EquipmentSpatialMeasurement Measurement(string id, string placement, string kind, decimal distance, string label, string geometry) => new EquipmentSpatialMeasurement { MeasurementId = id, PlacementId = placement, EquipmentEntityId = "equipment:" + placement, NormalizedTag = placement, SubjectKind = kind, ConstraintId = "constraint:" + id, LabelSourceHandle = label, GeometrySourceHandle = geometry, Distance2D = decimal.ToDouble(distance) };
        private static EngineeringStandardProfile Profile(string id, string level, params RuleSetting[] settings) => new EngineeringStandardProfile { ProfileId = id, Name = id, Level = level, Source = "M5.0 test fixture only", Version = "1.0", RuleSettings = settings.ToList() };
        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).Concat(new[] { new KnownQaRule { RuleId = "PP-CLEAR-001", Severity = "Warning", DefaultEnabled = false }, new KnownQaRule { RuleId = "PP-CLEAR-002", Severity = "Warning", DefaultEnabled = false } }).ToList();
        private static EffectiveRuleSetting Setting(ProfileResolution resolution, string id) => resolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == id);
        private static void Invalid(List<KnownQaRule> catalog, EngineeringStandardProfile profile, string expected) { bool thrown = false; try { ProfileResolver.Resolve(catalog, new[] { profile }); } catch (ProfileValidationException exception) { thrown = exception.Message.Contains(expected); } Assert(thrown, "Expected profile validation failure: " + expected); }
        private static string QaIdentity(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
