using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class ProfileTests
    {
        internal static void Run()
        {
            List<KnownQaRule> catalog = Catalog();
            ProfileResolution defaults = ProfileResolver.Resolve(catalog, new EngineeringStandardProfile[0]);
            Assert(defaults.EffectiveRuleSettings.Count == 15 && defaults.EffectiveRuleSettings.All(x => x.Enabled), "BuiltIn defaults did not enable all 15 rules.");
            Assert(defaults.AppliedProfileIds.SequenceEqual(new[] { ProfileResolver.BuiltInProfileId }) && catalog.All(rule => defaults.EffectiveRuleSettings.Single(setting => setting.EffectiveRuleId == rule.RuleId).EffectiveSeverity == rule.Severity) && defaults.EffectiveRuleSettings.All(setting => setting.EnabledSourceProfileId == ProfileResolver.BuiltInProfileId && setting.SeveritySourceProfileId == ProfileResolver.BuiltInProfileId), "BuiltIn severities or provenance changed.");

            EngineeringStandardProfile company = Profile("company", "Company", new RuleSetting { RuleId = "ENGI-EQ-003", Enabled = false }, new RuleSetting { RuleId = "PP-SPAT-002", SeverityOverride = "Error" });
            ProfileResolution companyResolution = ProfileResolver.Resolve(catalog, new[] { company });
            Assert(!companyResolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "ENGI-EQ-003").Enabled && companyResolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "ENGI-EQ-003").EnabledSourceProfileId == "company", "Company enabled override failed.");
            Assert(companyResolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "PP-SPAT-002").EffectiveSeverity == "Error" && companyResolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "PP-SPAT-002").SeveritySourceProfileId == "company", "Company severity override failed.");

            EngineeringStandardProfile client = Profile("client", "Client", new RuleSetting { RuleId = "PP-SPAT-002", SeverityOverride = "Warning" });
            EngineeringStandardProfile project = Profile("project", "Project", new RuleSetting { RuleId = "PP-SPAT-002", SeverityOverride = "Info" });
            ProfileResolution layered = ProfileResolver.Resolve(catalog, new[] { project, company, client });
            EffectiveRuleSetting spatial = layered.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "PP-SPAT-002");
            Assert(spatial.EffectiveSeverity == "Info" && spatial.SeveritySourceProfileId == "project" && !layered.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "ENGI-EQ-003").Enabled && layered.AppliedProfileIds.SequenceEqual(new[] { ProfileResolver.BuiltInProfileId, "company", "client", "project" }), "Layered precedence or omitted inheritance failed.");
            Assert(Serialize(ProfileResolver.Resolve(catalog, new[] { company, client, project })) == Serialize(layered), "Input profile order changed deterministic resolution.");

            Invalid(catalog, new[] { company, Profile("company", "Client", new RuleSetting { RuleId = "ENGI-EQ-003", Enabled = true }) }, "Duplicate ProfileId");
            Invalid(catalog, new[] { company, Profile("company2", "Company", new RuleSetting { RuleId = "ENGI-EQ-003", Enabled = true }) }, "Duplicate profile Level");
            Invalid(catalog, new[] { Profile("dup-rule", "Company", new RuleSetting { RuleId = "ENGI-EQ-003", Enabled = true }, new RuleSetting { RuleId = "ENGI-EQ-003", SeverityOverride = "Warning" }) }, "Duplicate RuleId");
            Invalid(catalog, new[] { Profile("unknown", "Company", new RuleSetting { RuleId = "UNKNOWN", Enabled = true }) }, "Unknown RuleId");
            Invalid(catalog, new[] { Profile("builtin", "BuiltIn", new RuleSetting { RuleId = "ENGI-EQ-003", Enabled = true }) }, "BuiltIn");
            Invalid(catalog, new[] { Profile("severity", "Company", new RuleSetting { RuleId = "ENGI-EQ-003", SeverityOverride = "Critical" }) }, "SeverityOverride");
            Invalid(catalog, new[] { Profile("noop", "Company", new RuleSetting { RuleId = "ENGI-EQ-003" }) }, "RuleSetting");

            EngineeringEntityIndex index = new EngineeringEntityIndex();
            index.EquipmentEntities.Add(new EquipmentEntity { CanonicalId = "equipment:X-200-101", NormalizedTag = "X-200-101", HasEquipmentListEvidence = true, EquipmentListLinks = new List<EquipmentListLink> { new EquipmentListLink { SourceTag = "X-200-101", SourceHandles = new List<string> { "L1" } } } });
            PlotPlanModel plot = new PlotPlanModel { Geometry = new PlotGeometryModel { SpatialConstraints = new List<SpatialCoordinateConstraint> { new SpatialCoordinateConstraint { ConstraintId = "spatial-constraint:boundary:B1", Status = "Ambiguous", LabelSourceHandle = "B1" } } } };
            QaReport unprofiled = QaEngine.Evaluate(index, BuiltInQaRules.All, plot, BuiltInPlotPlanQaRules.All);
            QaReport defaultProfiled = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plot, BuiltInPlotPlanQaRules.All, defaults);
            Assert(Comparable(unprofiled) == Comparable(defaultProfiled) && defaultProfiled.ProfileResolution != null, "Profile-aware defaults changed unprofiled behavior.");
            QaReport configured = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plot, BuiltInPlotPlanQaRules.All, companyResolution);
            Assert(configured.RuleResults.All(x => x.RuleId != "ENGI-EQ-003") && configured.RuleResults.Count == 14, "Disabled rule was evaluated.");
            QaFinding before = unprofiled.Findings.Single(x => x.RuleId == "PP-SPAT-002"); QaFinding after = configured.Findings.Single(x => x.RuleId == "PP-SPAT-002");
            Assert(configured.RuleResults.Single(x => x.RuleId == "PP-SPAT-002").Severity == "Error" && after.Severity == "Error" && after.FindingId == before.FindingId && after.Evidence.SpatialCoordinateConstraint != null && after.Evidence.SpatialCoordinateConstraint.ConstraintId == before.Evidence.SpatialCoordinateConstraint!.ConstraintId, "Severity override changed more than severity.");
            Assert(configured.Findings.SequenceEqual(configured.Findings.OrderBy(x => x.RuleId, StringComparer.Ordinal).ThenBy(x => x.SubjectId, StringComparer.Ordinal).ThenBy(x => x.FindingId, StringComparer.Ordinal)), "Profile finding ordering was not deterministic.");
        }

        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
        private static EngineeringStandardProfile Profile(string id, string level, params RuleSetting[] settings) => new EngineeringStandardProfile { ProfileId = id, Name = id + " profile", Level = level, Source = "M3.0 test fixture only", Version = "1.0", RuleSettings = settings.ToList() };
        private static void Invalid(List<KnownQaRule> catalog, IEnumerable<EngineeringStandardProfile> profiles, string expected) { bool thrown = false; try { ProfileResolver.Resolve(catalog, profiles); } catch (ProfileValidationException exception) { thrown = exception.Message.Contains(expected); } Assert(thrown, "Expected profile validation failure: " + expected); }
        private static string Serialize(ProfileResolution value) => string.Join("|", value.AppliedProfileIds) + ";" + string.Join("|", value.EffectiveRuleSettings.Select(x => x.EffectiveRuleId + ":" + x.Enabled + ":" + x.EffectiveSeverity + ":" + x.EnabledSourceProfileId + ":" + x.SeveritySourceProfileId));
        private static string Comparable(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
