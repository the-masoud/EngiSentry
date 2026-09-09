using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.AutoCAD2021.Scanning;
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

            StandardSourceRegistry emptyRegistry = StandardSourceRegistry.Create(Array.Empty<StandardSourceDefinition>());
            Assert(emptyRegistry.Sources.Count == 0, "Empty standard source registry was rejected.");
            var unsortedSources = new[] { Source("source.c"), Source("source.a"), Source("source.b") };
            StandardSourceRegistry orderedRegistry = StandardSourceRegistry.Create(unsortedSources);
            StandardSourceRegistry reorderedRegistry = StandardSourceRegistry.Create(new[] { Source("source.b"), Source("source.c"), Source("source.a") });
            Assert(orderedRegistry.Sources.Select(source => source.SourceId).SequenceEqual(new[] { "source.a", "source.b", "source.c" }) && SourceMetadata(orderedRegistry.Sources) == SourceMetadata(reorderedRegistry.Sources), "Standard source registry ordering was not deterministic.");

            Assert(orderedRegistry.TryGet("source.b", out StandardSourceDefinition? sourceB) && sourceB != null && SourceMetadata(new[] { sourceB }) == SourceMetadata(new[] { Source("source.b") }), "Exact standard source lookup failed.");
            Assert(!orderedRegistry.TryGet("SOURCE.B", out _) && !orderedRegistry.TryGet("", out _) && !orderedRegistry.TryGet("   ", out _), "Standard source lookup was not exact.");
            Assert(orderedRegistry.Get("source.b").SourceId == "source.b", "Standard source Get failed.");
            RegistryInvalid(() => orderedRegistry.Get("missing"), "standard source was not found: missing");
            RegistryInvalid(() => orderedRegistry.Get(null!), "standard source was not found: ");

            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { Source("source.a"), Source("source.a") }), "duplicate standard source SourceId: source.a");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = " ", DocumentNumber = "DOC", Title = "Title", Issuer = "Issuer" } }), "standard source SourceId is required");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "source.a", DocumentNumber = "", Title = "Title", Issuer = "Issuer" } }), "standard source DocumentNumber is required: source.a");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "source.a", DocumentNumber = "DOC", Title = " ", Issuer = "Issuer" } }), "standard source Title is required: source.a");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { new StandardSourceDefinition { SourceId = "source.a", DocumentNumber = "DOC", Title = "Title", Issuer = "" } }), "standard source Issuer is required: source.a");
            bool nullEnumerableRejected = false;
            try { StandardSourceRegistry.Create(null!); } catch (ArgumentNullException) { nullEnumerableRejected = true; }
            Assert(nullEnumerableRejected, "Null standard source enumerable was not rejected.");
            RegistryInvalid(() => StandardSourceRegistry.Create(new StandardSourceDefinition[] { null! }), "standard source registry contains a null source");

            string lowercaseHash = new string('a', 64);
            StandardSourceRegistry hashRegistry = StandardSourceRegistry.Create(new[] { Source("source.a", sourceSha256: lowercaseHash) });
            Assert(hashRegistry.Get("source.a").SourceSha256 == lowercaseHash.ToUpperInvariant(), "Standard source SHA256 was not normalized.");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { Source("source.a", sourceSha256: "ABC") }), "standard source SHA256 is invalid: source.a");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { Source("source.a", sourceSha256: new string('g', 64)) }), "standard source SHA256 is invalid: source.a");

            StandardSourceDefinition isolatedInput = Source("source.a", revisionOrEdition: "R1", sourceFileName: "source.pdf", sourceSha256: lowercaseHash);
            StandardSourceRegistry isolatedRegistry = StandardSourceRegistry.Create(new[] { isolatedInput });
            isolatedInput.Title = "Changed"; isolatedInput.Issuer = "Changed"; isolatedInput.RevisionOrEdition = "Changed"; isolatedInput.SourceFileName = "changed.pdf"; isolatedInput.SourceSha256 = new string('b', 64);
            StandardSourceDefinition isolatedStored = isolatedRegistry.Get("source.a");
            Assert(isolatedStored.Title == "Title source.a" && isolatedStored.Issuer == "Issuer source.a" && isolatedStored.RevisionOrEdition == "R1" && isolatedStored.SourceFileName == "source.pdf" && isolatedStored.SourceSha256 == lowercaseHash.ToUpperInvariant(), "Input mutation changed registry state.");
            StandardSourceDefinition fromSources = isolatedRegistry.Sources.Single(); fromSources.Title = "Changed";
            Assert(isolatedRegistry.Get("source.a").Title == "Title source.a", "Sources exposed registry-owned state.");
            Assert(isolatedRegistry.TryGet("source.a", out StandardSourceDefinition? fromTryGet) && fromTryGet != null, "TryGet snapshot was unavailable.");
            fromTryGet!.Issuer = "Changed";
            Assert(isolatedRegistry.Get("source.a").Issuer == "Issuer source.a", "TryGet exposed registry-owned state.");

            StandardSourceRegistry optionalRegistry = StandardSourceRegistry.Create(new[] { Source("source.a") });
            StandardSourceDefinition optionalSource = optionalRegistry.Get("source.a");
            Assert(optionalSource.RevisionOrEdition == null && optionalSource.SourceFileName == null && optionalSource.SourceSha256 == null, "Optional standard source metadata was rejected.");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { Source("source.a", revisionOrEdition: " ") }), "standard source RevisionOrEdition is empty: source.a");
            RegistryInvalid(() => StandardSourceRegistry.Create(new[] { Source("source.a", sourceFileName: " ") }), "standard source SourceFileName is empty: source.a");

            StandardSourceRegistry clauseSources = StandardSourceRegistry.Create(new[] { Source("source.a"), Source("source.b") });
            StandardClauseRegistry emptyClauseRegistry = StandardClauseRegistry.Create(StandardSourceRegistry.Create(Array.Empty<StandardSourceDefinition>()), Array.Empty<StandardClauseDefinition>());
            Assert(emptyClauseRegistry.Clauses.Count == 0, "Empty standard clause registry was rejected.");
            var unsortedClauses = new[] { Clause("clause.c", "source.a", "3.0"), Clause("clause.a", "source.a", "1.0"), Clause("clause.b", "source.b", "2.0") };
            StandardClauseRegistry orderedClauses = StandardClauseRegistry.Create(clauseSources, unsortedClauses);
            StandardClauseRegistry reorderedClauses = StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.b", "source.b", "2.0"), Clause("clause.c", "source.a", "3.0"), Clause("clause.a", "source.a", "1.0") });
            Assert(orderedClauses.Clauses.Select(clause => clause.ClauseId).SequenceEqual(new[] { "clause.a", "clause.b", "clause.c" }) && ClauseMetadata(orderedClauses.Clauses) == ClauseMetadata(reorderedClauses.Clauses), "Standard clause registry ordering was not deterministic.");

            Assert(orderedClauses.TryGet("clause.a", out StandardClauseDefinition? clauseA) && clauseA != null && clauseA.ClauseId == "clause.a", "Exact standard clause lookup failed.");
            Assert(!orderedClauses.TryGet("CLAUSE.A", out _) && !orderedClauses.TryGet("", out _) && !orderedClauses.TryGet("   ", out _), "Standard clause lookup was not exact.");
            Assert(orderedClauses.Get("clause.a").ClauseId == "clause.a", "Standard clause Get failed.");
            ClauseRegistryInvalid(() => orderedClauses.Get("missing"), "standard clause was not found: missing");
            ClauseRegistryInvalid(() => orderedClauses.Get(null!), "standard clause was not found: ");

            bool nullSourceRegistryRejected = false;
            try { StandardClauseRegistry.Create(null!, Array.Empty<StandardClauseDefinition>()); } catch (ArgumentNullException) { nullSourceRegistryRejected = true; }
            Assert(nullSourceRegistryRejected, "Null standard source registry was not rejected.");
            bool nullClauseEnumerableRejected = false;
            try { StandardClauseRegistry.Create(clauseSources, null!); } catch (ArgumentNullException) { nullClauseEnumerableRejected = true; }
            Assert(nullClauseEnumerableRejected, "Null standard clause enumerable was not rejected.");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new StandardClauseDefinition[] { null! }), "standard clause registry contains a null clause");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", "SOURCE.A") }), "standard clause references unknown SourceId: SOURCE.A");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { new StandardClauseDefinition { ClauseId = " ", SourceId = "source.a", ClauseReference = "1", ClauseText = "text" } }), "standard clause ClauseId is required");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { new StandardClauseDefinition { ClauseId = "clause.a", SourceId = " ", ClauseReference = "1", ClauseText = "text" } }), "standard clause SourceId is required: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { new StandardClauseDefinition { ClauseId = "clause.a", SourceId = "source.a", ClauseReference = " ", ClauseText = "text" } }), "standard clause ClauseReference is required: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { new StandardClauseDefinition { ClauseId = "clause.a", SourceId = "source.a", ClauseReference = "1", ClauseText = " " } }), "standard clause ClauseText is required: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", heading: " ") }), "standard clause Heading is empty: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", pageNumber: 0) }), "standard clause PageNumber must be greater than zero: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", pageNumber: -1) }), "standard clause PageNumber must be greater than zero: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a"), Clause("clause.a", "source.b", "2") }), "duplicate standard clause ClauseId: clause.a");
            ClauseRegistryInvalid(() => StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", "source.a", "1"), Clause("clause.b", "source.a", "1") }), "duplicate standard clause reference: source.a | 1");
            Assert(StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.a", "source.a", "1"), Clause("clause.b", "source.b", "1") }).Clauses.Count == 2, "Different sources could not reuse a clause reference.");

            StandardClauseDefinition isolatedClauseInput = Clause("clause.a", "source.a", "Table 3 Note 2", "  Synthetic Text With Case  ", "Section A", 7);
            StandardClauseRegistry isolatedClauseRegistry = StandardClauseRegistry.Create(clauseSources, new[] { isolatedClauseInput });
            isolatedClauseInput.SourceId = "source.b"; isolatedClauseInput.ClauseReference = "Changed"; isolatedClauseInput.ClauseText = "Changed"; isolatedClauseInput.Heading = "Changed"; isolatedClauseInput.PageNumber = 8;
            StandardClauseDefinition isolatedClauseStored = isolatedClauseRegistry.Get("clause.a");
            Assert(isolatedClauseStored.SourceId == "source.a" && isolatedClauseStored.ClauseReference == "Table 3 Note 2" && isolatedClauseStored.ClauseText == "  Synthetic Text With Case  " && isolatedClauseStored.Heading == "Section A" && isolatedClauseStored.PageNumber == 7, "Clause input mutation changed registry state.");
            StandardClauseDefinition fromClauses = isolatedClauseRegistry.Clauses.Single(); fromClauses.ClauseText = "Changed";
            Assert(isolatedClauseRegistry.Get("clause.a").ClauseText == "  Synthetic Text With Case  ", "Clauses exposed registry-owned state.");
            Assert(isolatedClauseRegistry.TryGet("clause.a", out StandardClauseDefinition? fromClauseTryGet) && fromClauseTryGet != null, "Clause TryGet snapshot was unavailable.");
            fromClauseTryGet!.Heading = "Changed";
            StandardClauseDefinition fromClauseGet = isolatedClauseRegistry.Get("clause.a"); fromClauseGet.PageNumber = 99;
            Assert(isolatedClauseRegistry.Get("clause.a").Heading == "Section A" && isolatedClauseRegistry.Get("clause.a").PageNumber == 7, "Clause lookup exposed registry-owned state.");
            StandardClauseDefinition optionalClause = StandardClauseRegistry.Create(clauseSources, new[] { Clause("clause.optional") }).Get("clause.optional");
            Assert(optionalClause.Heading == null && optionalClause.PageNumber == null, "Optional standard clause metadata was rejected.");

            StandardClauseRegistry ruleClauses = StandardClauseRegistry.Create(
                clauseSources,
                new[]
                {
                    Clause("clause.a", "source.a", "1.0"),
                    Clause("clause.b", "source.a", "2.0"),
                    Clause("clause.c", "source.a", "3.0")
                });
            StandardRulePackRegistry emptyPackRegistry = StandardRulePackRegistry.Create(ruleClauses, Array.Empty<StandardRulePackDefinition>());
            Assert(emptyPackRegistry.Packs.Count == 0, "Empty standard rule pack registry was rejected.");
            var unsortedPacks = new[]
            {
                Pack("pack.c", "Pack C", Rule("rule.a", "Rule A", "clause.a")),
                Pack("pack.a", "Pack A", Rule("rule.c", "Rule C", "clause.c", "clause.a", "clause.b"), Rule("rule.a", "Rule A", "clause.a"), Rule("rule.b", "Rule B", "clause.b")),
                Pack("pack.b", "Pack B", Rule("rule.a", "Rule A", "clause.a"))
            };
            StandardRulePackRegistry orderedPacks = StandardRulePackRegistry.Create(ruleClauses, unsortedPacks);
            StandardRulePackRegistry reorderedPacks = StandardRulePackRegistry.Create(ruleClauses, new[]
            {
                Pack("pack.b", "Pack B", Rule("rule.a", "Rule A", "clause.a")),
                Pack("pack.a", "Pack A", Rule("rule.b", "Rule B", "clause.b"), Rule("rule.a", "Rule A", "clause.a"), Rule("rule.c", "Rule C", "clause.b", "clause.c", "clause.a")),
                Pack("pack.c", "Pack C", Rule("rule.a", "Rule A", "clause.a"))
            });
            Assert(orderedPacks.Packs.Select(pack => pack.PackId).SequenceEqual(new[] { "pack.a", "pack.b", "pack.c" }) && orderedPacks.Get("pack.a").Rules.Select(rule => rule.RuleId).SequenceEqual(new[] { "rule.a", "rule.b", "rule.c" }) && orderedPacks.Get("pack.a").Rules.Single(rule => rule.RuleId == "rule.c").ClauseIds.SequenceEqual(new[] { "clause.a", "clause.b", "clause.c" }) && PackMetadata(orderedPacks.Packs) == PackMetadata(reorderedPacks.Packs), "Standard rule pack registry ordering was not deterministic.");

            Assert(orderedPacks.TryGet("pack.a", out StandardRulePackDefinition? packA) && packA != null && packA.PackId == "pack.a", "Exact standard rule pack lookup failed.");
            Assert(!orderedPacks.TryGet("PACK.A", out _) && !orderedPacks.TryGet("", out _) && !orderedPacks.TryGet("   ", out _), "Standard rule pack lookup was not exact.");
            Assert(orderedPacks.Get("pack.a").PackId == "pack.a", "Standard rule pack Get failed.");
            PackRegistryInvalid(() => orderedPacks.Get("missing"), "standard rule pack was not found: missing");
            PackRegistryInvalid(() => orderedPacks.Get(null!), "standard rule pack was not found: ");

            bool nullClauseRegistryRejected = false;
            try { StandardRulePackRegistry.Create(null!, Array.Empty<StandardRulePackDefinition>()); } catch (ArgumentNullException) { nullClauseRegistryRejected = true; }
            Assert(nullClauseRegistryRejected, "Null standard clause registry was not rejected.");
            bool nullPackEnumerableRejected = false;
            try { StandardRulePackRegistry.Create(ruleClauses, null!); } catch (ArgumentNullException) { nullPackEnumerableRejected = true; }
            Assert(nullPackEnumerableRejected, "Null standard rule pack enumerable was not rejected.");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new StandardRulePackDefinition[] { null! }), "standard rule pack registry contains a null pack");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { new StandardRulePackDefinition { PackId = " ", Name = "Pack", Rules = new List<StandardRuleDefinition> { Rule("rule.a", "Rule A", "clause.a") } } }), "standard rule pack PackId is required");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { new StandardRulePackDefinition { PackId = "pack.a", Name = " ", Rules = new List<StandardRuleDefinition> { Rule("rule.a", "Rule A", "clause.a") } } }), "standard rule pack Name is required: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { new StandardRulePackDefinition { PackId = "pack.a", Name = "Pack", Rules = null! } }), "standard rule pack Rules is required: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { new StandardRulePackDefinition { PackId = "pack.a", Name = "Pack", Rules = new List<StandardRuleDefinition>() } }), "standard rule pack contains no rules: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { new StandardRulePackDefinition { PackId = "pack.a", Name = "Pack", Rules = new List<StandardRuleDefinition> { null! } } }), "standard rule pack contains a null rule: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", new StandardRuleDefinition { RuleId = " ", Title = "Rule", ClauseIds = new List<string> { "clause.a" } }) }), "standard rule RuleId is required: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", new StandardRuleDefinition { RuleId = "rule.a", Title = " ", ClauseIds = new List<string> { "clause.a" } }) }), "standard rule Title is required: pack.a | rule.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", new StandardRuleDefinition { RuleId = "rule.a", Title = "Rule", ClauseIds = null! }) }), "standard rule ClauseIds is required: pack.a | rule.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", new StandardRuleDefinition { RuleId = "rule.a", Title = "Rule", ClauseIds = new List<string>() }) }), "standard rule requires at least one ClauseId: pack.a | rule.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", " ")) }), "standard rule ClauseId is required: pack.a | rule.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", "missing.clause")) }), "standard rule references unknown ClauseId: missing.clause");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", "CLAUSE.A")) }), "standard rule references unknown ClauseId: CLAUSE.A");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", "clause.a")), Pack("pack.a", "Pack", Rule("rule.b", "Rule", "clause.b")) }), "duplicate standard rule pack PackId: pack.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", "clause.a"), Rule("rule.a", "Rule", "clause.b")) }), "duplicate standard rule RuleId in pack: pack.a | rule.a");
            PackRegistryInvalid(() => StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack", Rule("rule.a", "Rule", "clause.a", "clause.a")) }), "duplicate standard rule ClauseId: pack.a | rule.a | clause.a");
            Assert(StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack A", Rule("rule.same", "Rule", "clause.a"), Rule("rule.other", "Rule", "clause.a")), Pack("pack.b", "Pack B", Rule("rule.same", "Rule", "clause.a")) }).Packs.Count == 2, "Cross-pack rule identity or clause reuse was rejected.");

            StandardRuleDefinition isolatedRule = Rule("rule.a", "  Rule Title With Case  ", "clause.a");
            StandardRulePackDefinition isolatedPack = Pack("pack.a", "  Pack Name With Case  ", isolatedRule);
            StandardRulePackRegistry isolatedPackRegistry = StandardRulePackRegistry.Create(ruleClauses, new[] { isolatedPack });
            isolatedPack.PackId = "pack.changed"; isolatedPack.Name = "Changed"; isolatedPack.Rules.Clear(); isolatedRule.RuleId = "rule.changed"; isolatedRule.Title = "Changed"; isolatedRule.ClauseIds[0] = "clause.b";
            StandardRulePackDefinition isolatedPackStored = isolatedPackRegistry.Get("pack.a");
            Assert(isolatedPackStored.Name == "  Pack Name With Case  " && isolatedPackStored.Rules.Single().RuleId == "rule.a" && isolatedPackStored.Rules.Single().Title == "  Rule Title With Case  " && isolatedPackStored.Rules.Single().ClauseIds.Single() == "clause.a", "Rule pack input mutation changed registry state.");
            StandardRulePackDefinition fromPacks = isolatedPackRegistry.Packs.Single(); fromPacks.Rules.Single().ClauseIds[0] = "clause.b"; fromPacks.Rules.Clear();
            Assert(isolatedPackRegistry.Get("pack.a").Rules.Single().ClauseIds.Single() == "clause.a", "Packs exposed registry-owned state.");
            Assert(isolatedPackRegistry.TryGet("pack.a", out StandardRulePackDefinition? fromPackTryGet) && fromPackTryGet != null, "Rule pack TryGet snapshot was unavailable.");
            fromPackTryGet!.Rules.Single().Title = "Changed";
            StandardRulePackDefinition fromPackGet = isolatedPackRegistry.Get("pack.a"); fromPackGet.Rules.Single().ClauseIds.Clear();
            Assert(isolatedPackRegistry.Get("pack.a").Rules.Single().Title == "  Rule Title With Case  " && isolatedPackRegistry.Get("pack.a").Rules.Single().ClauseIds.Single() == "clause.a", "Rule pack lookup exposed registry-owned state.");

            StandardRulePackRegistry emptyPrecedencePacks = StandardRulePackRegistry.Create(ruleClauses, Array.Empty<StandardRulePackDefinition>());
            StandardRulePackResolution emptyResolution = StandardRulePackResolver.Resolve(emptyPrecedencePacks, Array.Empty<StandardRulePackPrecedenceDefinition>());
            Assert(emptyResolution.AppliedPrecedence.Count == 0 && emptyResolution.EffectiveRules.Count == 0, "Empty rule pack resolution was rejected.");
            bool nullRulePackRegistryRejected = false;
            try { StandardRulePackResolver.Resolve(null!, Array.Empty<StandardRulePackPrecedenceDefinition>()); } catch (ArgumentNullException) { nullRulePackRegistryRejected = true; }
            Assert(nullRulePackRegistryRejected, "Null rule pack registry was not rejected.");
            bool nullPrecedenceRejected = false;
            try { StandardRulePackResolver.Resolve(emptyPrecedencePacks, null!); } catch (ArgumentNullException) { nullPrecedenceRejected = true; }
            Assert(nullPrecedenceRejected, "Null precedence enumerable was not rejected.");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(emptyPrecedencePacks, new StandardRulePackPrecedenceDefinition[] { null! }), "standard rule precedence contains a null entry");

            StandardRulePackRegistry precedencePackA = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "Pack A", Rule("rule.a", "Rule A", "clause.a")) });
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { new StandardRulePackPrecedenceDefinition { PackId = " ", Precedence = 1 } }), "standard rule precedence PackId is required");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { Precedence("pack.a", 0) }), "standard rule precedence must be greater than zero: pack.a");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { Precedence("pack.a", -1) }), "standard rule precedence must be greater than zero: pack.a");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { Precedence("missing.pack", 1) }), "standard rule precedence references unknown PackId: missing.pack");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { Precedence("PACK.A", 1) }), "standard rule precedence references unknown PackId: PACK.A");
            ResolverInvalid(() => StandardRulePackResolver.Resolve(precedencePackA, new[] { Precedence("pack.a", 1), Precedence("pack.a", 2) }), "duplicate standard rule precedence PackId: pack.a");
            StandardRulePackRegistry missingPrecedencePacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.b", "Pack B", Rule("rule.b", "Rule B", "clause.b")), Pack("pack.a", "Pack A", Rule("rule.a", "Rule A", "clause.a")) });
            ResolverInvalid(() => StandardRulePackResolver.Resolve(missingPrecedencePacks, new[] { Precedence("pack.b", 1) }), "standard rule precedence is missing for PackId: pack.a");

            StandardRulePackResolution uniqueResolution = StandardRulePackResolver.Resolve(missingPrecedencePacks, new[] { Precedence("pack.a", 10), Precedence("pack.b", 20) });
            Assert(uniqueResolution.EffectiveRules.Select(rule => rule.RuleId).SequenceEqual(new[] { "rule.a", "rule.b" }) && uniqueResolution.EffectiveRules.Single(rule => rule.RuleId == "rule.a").WinningPackId == "pack.a" && uniqueResolution.EffectiveRules.Single(rule => rule.RuleId == "rule.b").WinningPrecedence == 20 && uniqueResolution.EffectiveRules.All(rule => rule.OverriddenPackIds.Count == 0), "Unique rule resolution failed.");

            StandardRulePackRegistry winnerPacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.low", "Low", Rule("rule.same", "Low title", "clause.a")), Pack("pack.high", "High", Rule("rule.same", "High title", "clause.b")) });
            StandardRulePackResolution winnerResolution = StandardRulePackResolver.Resolve(winnerPacks, new[] { Precedence("pack.low", 10), Precedence("pack.high", 20) });
            EffectiveStandardRuleDefinition winningRule = winnerResolution.EffectiveRules.Single();
            Assert(winningRule.WinningPackId == "pack.high" && winningRule.WinningPrecedence == 20 && winningRule.Title == "High title" && winningRule.ClauseIds.SequenceEqual(new[] { "clause.b" }) && winningRule.OverriddenPackIds.SequenceEqual(new[] { "pack.low" }), "Precedence winner merged or lost rule provenance.");

            StandardRulePackRegistry multipleLoserPacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.c", "C", Rule("rule.same", "C", "clause.c")), Pack("pack.b", "B", Rule("rule.same", "B", "clause.b")), Pack("pack.a", "A", Rule("rule.same", "A", "clause.a")) });
            StandardRulePackResolution multipleLoserResolution = StandardRulePackResolver.Resolve(multipleLoserPacks, new[] { Precedence("pack.c", 10), Precedence("pack.b", 20), Precedence("pack.a", 30) });
            Assert(multipleLoserResolution.EffectiveRules.Single().WinningPackId == "pack.a" && multipleLoserResolution.EffectiveRules.Single().OverriddenPackIds.SequenceEqual(new[] { "pack.b", "pack.c" }), "Losing pack precedence ordering failed.");
            StandardRulePackResolution lowerTieResolution = StandardRulePackResolver.Resolve(multipleLoserPacks, new[] { Precedence("pack.a", 30), Precedence("pack.b", 20), Precedence("pack.c", 20) });
            Assert(lowerTieResolution.EffectiveRules.Single().WinningPackId == "pack.a", "Lower precedence tie was incorrectly rejected.");
            StandardRulePackRegistry topTiePacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "A", Rule("rule.same", "A", "clause.a")), Pack("pack.b", "B", Rule("rule.same", "B", "clause.b")) });
            ResolverInvalid(() => StandardRulePackResolver.Resolve(topTiePacks, new[] { Precedence("pack.a", 20), Precedence("pack.b", 20) }), "unresolved standard rule conflict: rule.same | pack.a,pack.b");
            StandardRulePackRegistry identicalTopTiePacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "A", Rule("rule.same", "Same", "clause.a")), Pack("pack.b", "B", Rule("rule.same", "Same", "clause.a")) });
            ResolverInvalid(() => StandardRulePackResolver.Resolve(identicalTopTiePacks, new[] { Precedence("pack.b", 20), Precedence("pack.a", 20) }), "unresolved standard rule conflict: rule.same | pack.a,pack.b");
            StandardRulePackRegistry noConflictTiePacks = StandardRulePackRegistry.Create(ruleClauses, new[] { Pack("pack.a", "A", Rule("rule.same", "A", "clause.a")), Pack("pack.b", "B", Rule("RULE.SAME", "B", "clause.b")) });
            Assert(StandardRulePackResolver.Resolve(noConflictTiePacks, new[] { Precedence("pack.a", 20), Precedence("pack.b", 20) }).EffectiveRules.Count == 2, "Case-distinct RuleIds or equal precedence without conflict was rejected.");

            StandardRulePackResolution deterministicResolution = StandardRulePackResolver.Resolve(orderedPacks, new[] { Precedence("pack.c", 10), Precedence("pack.a", 30), Precedence("pack.b", 20) });
            StandardRulePackResolution reorderedResolution = StandardRulePackResolver.Resolve(orderedPacks, new[] { Precedence("pack.b", 20), Precedence("pack.c", 10), Precedence("pack.a", 30) });
            Assert(ResolutionMetadata(deterministicResolution) == ResolutionMetadata(reorderedResolution) && deterministicResolution.AppliedPrecedence.Select(entry => entry.PackId).SequenceEqual(new[] { "pack.a", "pack.b", "pack.c" }) && deterministicResolution.EffectiveRules.Select(rule => rule.RuleId).SequenceEqual(deterministicResolution.EffectiveRules.Select(rule => rule.RuleId).OrderBy(ruleId => ruleId, StringComparer.Ordinal)), "Rule pack resolution output ordering was not deterministic.");

            var mutablePrecedence = new[] { Precedence("pack.low", 10), Precedence("pack.high", 20) };
            StandardRulePackResolution mutableResolution = StandardRulePackResolver.Resolve(winnerPacks, mutablePrecedence);
            mutablePrecedence[0].PackId = "changed"; mutablePrecedence[1].Precedence = 1;
            Assert(mutableResolution.AppliedPrecedence.Single(entry => entry.PackId == "pack.high").Precedence == 20, "Caller precedence mutation changed resolution state.");
            mutableResolution.AppliedPrecedence.Clear(); mutableResolution.EffectiveRules.Single().ClauseIds.Clear(); mutableResolution.EffectiveRules.Single().OverriddenPackIds.Clear();
            StandardRulePackResolution freshResolution = StandardRulePackResolver.Resolve(winnerPacks, new[] { Precedence("pack.low", 10), Precedence("pack.high", 20) });
            Assert(freshResolution.AppliedPrecedence.Count == 2 && freshResolution.EffectiveRules.Single().ClauseIds.SequenceEqual(new[] { "clause.b" }) && freshResolution.EffectiveRules.Single().OverriddenPackIds.SequenceEqual(new[] { "pack.low" }), "Returned resolution mutation affected a later Resolve.");

            string provenanceHash = new string('c', 64);
            StandardSourceRegistry provenanceSources = StandardSourceRegistry.Create(new[] { Source("source.a", "R1", "source-a.pdf", provenanceHash) });
            StandardClauseRegistry provenanceClauses = StandardClauseRegistry.Create(provenanceSources, new[]
            {
                Clause("clause.a", "source.a", "1.0", "Clause A text", "Heading A", 1),
                Clause("clause.b", "source.a", "2.0", "Clause B text", "Heading B", 2),
                Clause("clause.c", "source.a", "3.0", "Clause C text", "Heading C", 3)
            });
            StandardRulePackResolution traceResolution = Resolution(EffectiveRule("rule.a", "Rule A", "pack.a", 20, new[] { "pack.low" }, "clause.a"));
            QaFinding unmatchedFinding = Finding("qa.unmatched");
            QaReport unmatchedReport = new QaReport { Findings = new List<QaFinding> { unmatchedFinding } };
            string unmatchedIdentity = FindingIdentity(unmatchedFinding);
            StandardFindingProvenanceEnricher.Apply(unmatchedReport, traceResolution, provenanceClauses, provenanceSources);
            Assert(unmatchedFinding.StandardsProvenance == null && FindingIdentity(unmatchedFinding) == unmatchedIdentity, "Unmatched finding was changed by provenance enrichment.");

            QaFinding traceFinding = Finding("rule.a");
            QaReport traceReport = new QaReport { Findings = new List<QaFinding> { traceFinding } };
            string traceIdentity = FindingIdentity(traceFinding);
            StandardFindingProvenanceEnricher.Apply(traceReport, traceResolution, provenanceClauses, provenanceSources);
            StandardFindingProvenance trace = traceFinding.StandardsProvenance!;
            StandardClauseCitation traceCitation = trace.Clauses.Single();
            Assert(trace.RuleId == "rule.a" && trace.RuleTitle == "Rule A" && trace.WinningPackId == "pack.a" && trace.WinningPrecedence == 20 && trace.OverriddenPackIds.SequenceEqual(new[] { "pack.low" }) && traceCitation.ClauseId == "clause.a" && traceCitation.SourceId == "source.a" && traceCitation.ClauseReference == "1.0" && traceCitation.ClauseText == "Clause A text" && traceCitation.Heading == "Heading A" && traceCitation.PageNumber == 1 && traceCitation.DocumentNumber == "DOC source.a" && traceCitation.SourceTitle == "Title source.a" && traceCitation.Issuer == "Issuer source.a" && traceCitation.RevisionOrEdition == "R1" && traceCitation.SourceFileName == "source-a.pdf" && traceCitation.SourceSha256 == provenanceHash.ToUpperInvariant() && FindingIdentity(traceFinding) == traceIdentity, "Complete standards traceability or finding invariants failed.");

            StandardRulePackResolution multiClauseResolution = Resolution(EffectiveRule("rule.a", "Rule A", "pack.a", 20, new[] { "pack.low" }, "clause.c", "clause.a", "clause.b"));
            QaFinding firstMatchedFinding = Finding("rule.a"); QaFinding secondMatchedFinding = Finding("rule.a");
            QaReport multipleFindingReport = new QaReport { Findings = new List<QaFinding> { firstMatchedFinding, secondMatchedFinding } };
            StandardFindingProvenanceEnricher.Apply(multipleFindingReport, multiClauseResolution, provenanceClauses, provenanceSources);
            Assert(firstMatchedFinding.StandardsProvenance!.Clauses.Select(citation => citation.ClauseId).SequenceEqual(new[] { "clause.a", "clause.b", "clause.c" }) && firstMatchedFinding.StandardsProvenance.Clauses.Select(citation => citation.DocumentNumber).All(value => value == "DOC source.a"), "Clause citation ordering or source metadata failed.");
            firstMatchedFinding.StandardsProvenance.RuleTitle = "Changed"; firstMatchedFinding.StandardsProvenance.Clauses.Clear(); firstMatchedFinding.StandardsProvenance.OverriddenPackIds.Clear();
            Assert(secondMatchedFinding.StandardsProvenance!.RuleTitle == "Rule A" && secondMatchedFinding.StandardsProvenance.Clauses.Count == 3 && secondMatchedFinding.StandardsProvenance.OverriddenPackIds.SequenceEqual(new[] { "pack.low" }), "Multiple findings shared mutable provenance.");
            QaReport exactMatchReport = new QaReport { Findings = new List<QaFinding> { Finding("rule.a"), Finding("RULE.A") } };
            StandardFindingProvenanceEnricher.Apply(exactMatchReport, traceResolution, provenanceClauses, provenanceSources);
            Assert(exactMatchReport.Findings[0].StandardsProvenance != null && exactMatchReport.Findings[1].StandardsProvenance == null, "Provenance RuleId matching was not exact ordinal.");

            QaReport unknownClauseReport = new QaReport { Findings = new List<QaFinding> { Finding("rule.a") } };
            string unknownClauseIdentity = FindingIdentity(unknownClauseReport.Findings.Single());
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(unknownClauseReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 1, Array.Empty<string>(), "missing.clause")), provenanceClauses, provenanceSources), "standard finding provenance references unknown ClauseId: missing.clause");
            Assert(unknownClauseReport.Findings.Single().StandardsProvenance == null && FindingIdentity(unknownClauseReport.Findings.Single()) == unknownClauseIdentity, "Unknown clause failure mutated the report.");
            StandardSourceRegistry wrongProvenanceSources = StandardSourceRegistry.Create(new[] { Source("source.b") });
            QaReport unknownSourceReport = new QaReport { Findings = new List<QaFinding> { Finding("rule.a") } };
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(unknownSourceReport, traceResolution, provenanceClauses, wrongProvenanceSources), "standard finding provenance references unknown SourceId: source.a");

            QaReport validationReport = new QaReport { Findings = new List<QaFinding> { Finding("rule.a") } };
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, new StandardRulePackResolution { EffectiveRules = null! }, provenanceClauses, provenanceSources), "standard finding provenance EffectiveRules is required");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { null! } }, provenanceClauses, provenanceSources), "standard finding provenance contains a null effective rule");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule(" ", "Rule", "pack.a", 1, Array.Empty<string>(), "clause.a")), provenanceClauses, provenanceSources), "standard finding provenance RuleId is required");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", " ", 1, Array.Empty<string>(), "clause.a")), provenanceClauses, provenanceSources), "standard finding provenance WinningPackId is required: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 0, Array.Empty<string>(), "clause.a")), provenanceClauses, provenanceSources), "standard finding provenance WinningPrecedence must be greater than zero: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { new EffectiveStandardRuleDefinition { RuleId = "rule.a", WinningPackId = "pack.a", WinningPrecedence = 1, ClauseIds = null!, OverriddenPackIds = new List<string>() } } }, provenanceClauses, provenanceSources), "standard finding provenance ClauseIds is required: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 1, Array.Empty<string>())), provenanceClauses, provenanceSources), "standard finding provenance requires at least one ClauseId: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 1, Array.Empty<string>(), " ")), provenanceClauses, provenanceSources), "standard finding provenance ClauseId is required: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, new StandardRulePackResolution { EffectiveRules = new List<EffectiveStandardRuleDefinition> { new EffectiveStandardRuleDefinition { RuleId = "rule.a", WinningPackId = "pack.a", WinningPrecedence = 1, ClauseIds = new List<string> { "clause.a" }, OverriddenPackIds = null! } } }, provenanceClauses, provenanceSources), "standard finding provenance OverriddenPackIds is required: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 1, Array.Empty<string>(), "clause.a"), EffectiveRule("rule.a", "Rule", "pack.b", 2, Array.Empty<string>(), "clause.b")), provenanceClauses, provenanceSources), "duplicate standard finding provenance RuleId: rule.a");
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(validationReport, Resolution(EffectiveRule("rule.a", "Rule", "pack.a", 1, Array.Empty<string>(), "clause.a", "clause.a")), provenanceClauses, provenanceSources), "duplicate standard finding provenance ClauseId: rule.a | clause.a");

            QaFinding partialFirst = Finding("rule.a"); partialFirst.StandardsProvenance = Provenance("old.first");
            QaFinding partialSecond = Finding("rule.b"); partialSecond.StandardsProvenance = Provenance("old.second");
            QaReport partialReport = new QaReport { Findings = new List<QaFinding> { partialFirst, partialSecond } };
            ProvenanceInvalid(() => StandardFindingProvenanceEnricher.Apply(partialReport, Resolution(EffectiveRule("rule.a", "Rule A", "pack.a", 1, Array.Empty<string>(), "clause.a"), EffectiveRule("rule.b", "Rule B", "pack.a", 1, Array.Empty<string>(), "missing.clause")), provenanceClauses, provenanceSources), "standard finding provenance references unknown ClauseId: missing.clause");
            Assert(partialFirst.StandardsProvenance!.RuleId == "old.first" && partialSecond.StandardsProvenance!.RuleId == "old.second", "Provenance failure partially mutated findings.");
            QaFinding staleFinding = Finding("qa.unmatched"); staleFinding.StandardsProvenance = Provenance("old");
            QaReport staleReport = new QaReport { Findings = new List<QaFinding> { staleFinding } };
            StandardFindingProvenanceEnricher.Apply(staleReport, traceResolution, provenanceClauses, provenanceSources);
            Assert(staleFinding.StandardsProvenance == null, "Stale unmatched provenance was not cleared.");
            QaReport idempotentReport = new QaReport { Findings = new List<QaFinding> { Finding("rule.a") } };
            StandardFindingProvenanceEnricher.Apply(idempotentReport, multiClauseResolution, provenanceClauses, provenanceSources);
            string provenanceIdentity = ProvenanceIdentity(idempotentReport.Findings.Single().StandardsProvenance!);
            StandardFindingProvenanceEnricher.Apply(idempotentReport, multiClauseResolution, provenanceClauses, provenanceSources);
            Assert(ProvenanceIdentity(idempotentReport.Findings.Single().StandardsProvenance!) == provenanceIdentity, "Provenance enrichment was not idempotent.");

            const string pcdHash = "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E";
            const string eprHash = "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152";
            const string gsfHash = "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B";
            const string pcdText = "Road and access ways shall offer easy access for mobile equipment during construction and maintenance, fire fighting and emergency escape in a fire situation.";
            const string eprText = "This Standard Specification covers the basic requirements of the plant layout and spacing of oil & gas refineries, petrochemical and similar chemical plants to ensure safety and fire prevention together with ease of operation and maintenance.";
            const string gsfText = "هدف از تدوین این استاندارد، تعیین حداقل الزامات ایمنی برای جانمایی و تعیین فاصله‌گذاری تجهیزات و تأسیسات فرایندی صنعت نفت در بخش خشکی است.";
            StandardSourceRegistry realSources = StandardSourceRegistry.Create(new[]
            {
                new StandardSourceDefinition { SourceId = "trc.pcd-pd-135.rev1", DocumentNumber = "PCD-PD-135", Title = "PIPING DESIGN CRITERIA", Issuer = "Tehran Raymand Consulting Engineers", RevisionOrEdition = "1", SourceFileName = "PCD-PD-135.pdf", SourceSha256 = pcdHash },
                new StandardSourceDefinition { SourceId = "ips.e-pr-190.rev1.2009", DocumentNumber = "IPS-E-PR-190(1)", Title = "ENGINEERING STANDARD FOR LAYOUT AND SPACING", Issuer = "Iranian Ministry of Petroleum", RevisionOrEdition = "First Revision / March 2009", SourceFileName = "IPS E-PR-190.pdf", SourceSha256 = eprHash },
                new StandardSourceDefinition { SourceId = "ips.g-sf-150.rev1.2023", DocumentNumber = "IPS-G-SF-150(1):2023 / INSO 23591:1402", Title = "Petroleum industry – Requirements for spacing and layout of industry and equipment", Issuer = "Iranian Ministry of Petroleum", RevisionOrEdition = "First Edition / 1402 / 2023", SourceFileName = "IPS G-SF-150-23591.pdf", SourceSha256 = gsfHash }
            });
            StandardClauseRegistry realClauses = StandardClauseRegistry.Create(realSources, new[]
            {
                Clause("trc.pcd-pd-135.3.3.9.a", "trc.pcd-pd-135.rev1", "3.3.9(a) — Roadways", pcdText, pageNumber: 15),
                Clause("ips.e-pr-190.1.1", "ips.e-pr-190.rev1.2009", "1.1", eprText, pageNumber: 12),
                Clause("ips.g-sf-150.1.scope", "ips.g-sf-150.rev1.2023", "1 — هدف و دامنه کاربرد", gsfText, pageNumber: 9)
            });
            Assert(realSources.Sources.Count == 3 && realSources.Get("trc.pcd-pd-135.rev1").SourceFileName == "PCD-PD-135.pdf" && realSources.Get("trc.pcd-pd-135.rev1").SourceSha256 == pcdHash && realSources.Get("ips.e-pr-190.rev1.2009").SourceFileName == "IPS E-PR-190.pdf" && realSources.Get("ips.e-pr-190.rev1.2009").SourceSha256 == eprHash && realSources.Get("ips.g-sf-150.rev1.2023").SourceFileName == "IPS G-SF-150-23591.pdf" && realSources.Get("ips.g-sf-150.rev1.2023").SourceSha256 == gsfHash, "Real standard source identity or SHA256 provenance failed.");
            Assert(realClauses.Clauses.Count == 3 && realClauses.Get("trc.pcd-pd-135.3.3.9.a").SourceId == "trc.pcd-pd-135.rev1" && realClauses.Get("trc.pcd-pd-135.3.3.9.a").ClauseReference == "3.3.9(a) — Roadways" && realClauses.Get("trc.pcd-pd-135.3.3.9.a").ClauseText == pcdText && realClauses.Get("trc.pcd-pd-135.3.3.9.a").PageNumber == 15 && realClauses.Get("ips.e-pr-190.1.1").SourceId == "ips.e-pr-190.rev1.2009" && realClauses.Get("ips.e-pr-190.1.1").ClauseReference == "1.1" && realClauses.Get("ips.e-pr-190.1.1").ClauseText == eprText && realClauses.Get("ips.e-pr-190.1.1").PageNumber == 12 && realClauses.Get("ips.g-sf-150.1.scope").SourceId == "ips.g-sf-150.rev1.2023" && realClauses.Get("ips.g-sf-150.1.scope").ClauseReference == "1 — هدف و دامنه کاربرد" && realClauses.Get("ips.g-sf-150.1.scope").ClauseText == gsfText && realClauses.Get("ips.g-sf-150.1.scope").PageNumber == 9, "Real standard clause identity, source link, text, or page provenance failed.");
            StandardRulePackRegistry acceptancePacks = StandardRulePackRegistry.Create(realClauses, new[]
            {
                Pack("acceptance.ips-e-pr-190", "P3.6 acceptance E-PR-190", Rule("acceptance.layout-spacing-basis", "P3.6 acceptance layout spacing basis", "ips.e-pr-190.1.1")),
                Pack("acceptance.ips-g-sf-150", "P3.6 acceptance G-SF-150", Rule("acceptance.layout-spacing-basis", "P3.6 acceptance layout spacing basis", "ips.g-sf-150.1.scope"))
            });
            StandardRulePackResolution acceptanceResolution = StandardRulePackResolver.Resolve(acceptancePacks, new[] { Precedence("acceptance.ips-e-pr-190", 10), Precedence("acceptance.ips-g-sf-150", 20) });
            EffectiveStandardRuleDefinition acceptanceRule = acceptanceResolution.EffectiveRules.Single();
            Assert(acceptanceResolution.EffectiveRules.Count == 1 && acceptanceRule.RuleId == "acceptance.layout-spacing-basis" && acceptanceRule.WinningPackId == "acceptance.ips-g-sf-150" && acceptanceRule.WinningPrecedence == 20 && acceptanceRule.OverriddenPackIds.SequenceEqual(new[] { "acceptance.ips-e-pr-190" }) && acceptanceRule.ClauseIds.SequenceEqual(new[] { "ips.g-sf-150.1.scope" }), "Real-source P3.6 acceptance precedence did not resolve as required.");
            QaFinding acceptanceFinding = Finding("acceptance.layout-spacing-basis");
            StandardFindingProvenanceEnricher.Apply(new QaReport { Findings = new List<QaFinding> { acceptanceFinding } }, acceptanceResolution, realClauses, realSources);
            StandardFindingProvenance acceptanceProvenance = acceptanceFinding.StandardsProvenance!;
            StandardClauseCitation acceptanceCitation = acceptanceProvenance.Clauses.Single();
            Assert(acceptanceProvenance.RuleId == "acceptance.layout-spacing-basis" && acceptanceProvenance.WinningPackId == "acceptance.ips-g-sf-150" && acceptanceProvenance.WinningPrecedence == 20 && acceptanceProvenance.OverriddenPackIds.SequenceEqual(new[] { "acceptance.ips-e-pr-190" }) && acceptanceProvenance.Clauses.Count == 1 && acceptanceCitation.ClauseId == "ips.g-sf-150.1.scope" && acceptanceCitation.SourceId == "ips.g-sf-150.rev1.2023" && acceptanceCitation.ClauseReference == "1 — هدف و دامنه کاربرد" && acceptanceCitation.ClauseText == gsfText && acceptanceCitation.PageNumber == 9 && acceptanceCitation.DocumentNumber == "IPS-G-SF-150(1):2023 / INSO 23591:1402" && acceptanceCitation.SourceTitle == "Petroleum industry – Requirements for spacing and layout of industry and equipment" && acceptanceCitation.Issuer == "Iranian Ministry of Petroleum" && acceptanceCitation.RevisionOrEdition == "First Edition / 1402 / 2023" && acceptanceCitation.SourceFileName == "IPS G-SF-150-23591.pdf" && acceptanceCitation.SourceSha256 == gsfHash, "Real-source finding provenance acceptance failed.");
            RunP55(realSources, realClauses);
            RunP61();
            RunP62();
            RunP63();
            RunP64();
            RunP71();
            RunP72();
        }

        private static void RunP55(StandardSourceRegistry realSources, StandardClauseRegistry p36Clauses)
        {
            const string pcd = "trc.pcd-pd-135.rev1";
            const string flareB = "As a preliminary assumption, the flare stack should be located at least 90 meters for HP/MP flare and 50 meters form LP flare from other facilities. After preliminary calculation by Safety/Process Departments, this assumption will be replaced by a more precise pre-calculated data and finally after vendor selection, the final data will be attained. Common roads and piping system, which serves something other than flare system, shall not cross this area.";
            StandardClauseDefinition[] flareClauses =
            {
                Clause("trc.pcd-pd-135.3.3.7.a", pcd, "3.3.7(a) — Flare and Burn-pit Area", "Flare and burn pit shall be located at the end of the site with sufficient distance to prevent personnel hazard.", pageNumber: 14),
                Clause("trc.pcd-pd-135.3.3.7.b", pcd, "3.3.7(b) — Flare and Burn-pit Area", "Flaring of process units that generate combustible by products (gas or vapor), in the when required, sufficient space must be left between the flare and the nearest adjacent equipment to keep the radiant heat flux below allowable limits.", pageNumber: 14),
                Clause("trc.pcd-pd-135.3.3.7.c", pcd, "3.3.7(c) — Flare and Burn-pit Area", "Ignited flares are sources of ignition and should not be located down-wind of sources of flammable gas (or should be located far enough from them), except where flare are high enough to avoid any risk. Conversely, the risk of flame-out where the flares become sources of fuel (i.e. release source) shall be considered.", pageNumber: 15),
                Clause("trc.pcd-pd-135.3.4.13.a", pcd, "3.4.13(a) — Flare", "There should be a totally clear area surrounding the flare stack. The size of this area is dependent on heat intensity requirements which depend upon the height of the flare and relieving quantity.", pageNumber: 24),
                Clause("trc.pcd-pd-135.3.4.13.b", pcd, "3.4.13(b) — Flare", flareB, pageNumber: 24),
                Clause("trc.pcd-pd-135.3.4.13.c", pcd, "3.4.13(c) — Flare", "The ignition panels shall be located outside of the clear area around the stacks.", pageNumber: 24)
            };
            StandardClauseRegistry p55Clauses = StandardClauseRegistry.Create(realSources, p36Clauses.Clauses.Concat(flareClauses));
            Assert(p36Clauses.Clauses.Count == 3 && p55Clauses.Clauses.Count == 9 && p55Clauses.Get("trc.pcd-pd-135.3.4.13.b").ClauseText == flareB && flareB.Contains("90 meters") && flareB.Contains("50 meters"), "P5.5 flare evidence or P3.6 isolation failed.");
            ThrowsAuthorityPolicy(() => PlotPlanStandardsAuthorityPolicyCatalog.Create(realSources, p36Clauses));

            StandardAuthorityPolicyRegistry policies = PlotPlanStandardsAuthorityPolicyCatalog.Create(realSources, p55Clauses);
            string[] flareIds = flareClauses.Select(clause => clause.ClauseId).ToArray();
            Assert(policies.Policies.Count == 9 && policies.Policies.Count(policy => policy.ClauseId == null) == 3 && policies.Policies.Count(policy => policy.ClauseId != null) == 6 && policies.Policies.Select(policy => policy.PolicyId).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(new[] { "real.ips-e-pr-190.rev1.2009.default", "real.ips-g-sf-150.rev1.2023.default", "real.pcd-pd-135.rev1.default", "real.pcd-pd-135.rev1.flare.3.3.7.a", "real.pcd-pd-135.rev1.flare.3.3.7.b", "real.pcd-pd-135.rev1.flare.3.3.7.c", "real.pcd-pd-135.rev1.flare.3.4.13.a", "real.pcd-pd-135.rev1.flare.3.4.13.b", "real.pcd-pd-135.rev1.flare.3.4.13.c" }), "P5.5 catalog policy exactness failed.");
            Assert(policies.Policies.Select(policy => policy.SourceId + ":" + (policy.ClauseId ?? string.Empty) + ":" + policy.PolicyId).SequenceEqual(policies.Policies.OrderBy(policy => policy.SourceId, StringComparer.Ordinal).ThenBy(policy => policy.ClauseId == null ? 0 : 1).ThenBy(policy => policy.ClauseId, StringComparer.Ordinal).ThenBy(policy => policy.PolicyId, StringComparer.Ordinal).Select(policy => policy.SourceId + ":" + (policy.ClauseId ?? string.Empty) + ":" + policy.PolicyId)), "P5.5 policy ordering failed.");
            Assert(policies.TryResolve(pcd, "trc.pcd-pd-135.3.3.9.a", out StandardAuthorityPolicyDefinition? general) && general != null && general.PolicyId == "real.pcd-pd-135.rev1.default" && general.ClauseId == null && general.AuthorityClass == StandardAuthorityClasses.GoverningProject && general.EnforcementMode == StandardEnforcementModes.ConditionalReview, "P5.5 PCD general fallback failed.");
            foreach (string clauseId in flareIds)
            {
                Assert(policies.TryResolve(pcd, clauseId, out StandardAuthorityPolicyDefinition? flare) && flare != null && flare.PolicyId == "real.pcd-pd-135.rev1.flare." + clauseId.Substring("trc.pcd-pd-135.".Length) && flare.ClauseId == clauseId && flare.AuthorityClass == StandardAuthorityClasses.Quarantined && flare.EnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "P5.5 flare override failed: " + clauseId);
            }
            Assert(policies.TryResolve("ips.e-pr-190.rev1.2009", "ips.e-pr-190.1.1", out StandardAuthorityPolicyDefinition? legacy) && legacy != null && legacy.ClauseId == null && legacy.AuthorityClass == StandardAuthorityClasses.LegacyConflict && legacy.EnforcementMode == StandardEnforcementModes.NeverAutoEnforce && policies.TryResolve("ips.g-sf-150.rev1.2023", "ips.g-sf-150.1.scope", out StandardAuthorityPolicyDefinition? primary) && primary != null && primary.ClauseId == null && primary.AuthorityClass == StandardAuthorityClasses.ActivePrimary && primary.EnforcementMode == StandardEnforcementModes.ConditionalReview, "P5.5 IPS source-wide policies failed.");

            StandardRulePackDefinition pack = Pack("acceptance.p55.real-authority", "P5.5 Real Standards Authority Acceptance", Rule("acceptance.p55.pcd.general", "PCD general", "trc.pcd-pd-135.3.3.9.a"), Rule("acceptance.p55.pcd.flare", "PCD flare", "trc.pcd-pd-135.3.4.13.b"), Rule("acceptance.p55.epr.legacy", "EPR legacy", "ips.e-pr-190.1.1"), Rule("acceptance.p55.gsf.primary", "GSF primary", "ips.g-sf-150.1.scope"));
            StandardRulePackResolution resolution = StandardRulePackResolver.Resolve(StandardRulePackRegistry.Create(p55Clauses, new[] { pack }), new[] { Precedence(pack.PackId, 10) });
            StandardAuthorityResolution authority = StandardAuthorityResolver.Resolve(resolution, p55Clauses, policies);
            StandardExecutionGateResolution gate = StandardAuthorityExecutionGate.Evaluate(authority);
            StandardAuthorityReviewProjection review = StandardAuthorityReviewProjector.Create(authority);
            Assert(authority.Rules.Count == 4 && authority.Rules.Single(rule => rule.RuleId == "acceptance.p55.pcd.general").EffectiveEnforcementMode == StandardEnforcementModes.ConditionalReview && authority.Rules.Single(rule => rule.RuleId == "acceptance.p55.pcd.flare").EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce && authority.Rules.Single(rule => rule.RuleId == "acceptance.p55.epr.legacy").EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce && authority.Rules.Single(rule => rule.RuleId == "acceptance.p55.gsf.primary").EffectiveEnforcementMode == StandardEnforcementModes.ConditionalReview && authority.Rules.Single(rule => rule.RuleId == "acceptance.p55.pcd.flare").Clauses.Single().PolicyId == "real.pcd-pd-135.rev1.flare.3.4.13.b", "P5.5 authority resolution failed.");
            Assert(gate.Rules.Count == 4 && gate.Rules.All(rule => !rule.AutoExecutionAllowed) && gate.Rules.Single(rule => rule.RuleId == "acceptance.p55.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && gate.Rules.Single(rule => rule.RuleId == "acceptance.p55.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && gate.Rules.Single(rule => rule.RuleId == "acceptance.p55.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && gate.Rules.Single(rule => rule.RuleId == "acceptance.p55.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired && gate.Rules.All(rule => rule.DecisionState != StandardDecisionStates.StandardConflict && rule.DecisionState != StandardDecisionStates.Pass && rule.DecisionState != StandardDecisionStates.Fail && rule.DecisionState != StandardDecisionStates.NotApplicable && rule.DecisionState != StandardDecisionStates.InsufficientEvidence), "P5.5 execution gate failed.");
            Assert(review.ReviewItems.Count == 4 && review.ReviewItems.Select(item => item.RuleId).SequenceEqual(review.ReviewItems.Select(item => item.RuleId).OrderBy(value => value, StringComparer.Ordinal)) && review.ReviewItems.All(item => item.Clauses.Count == 1 && item.DecisionState != StandardDecisionStates.Pass && item.DecisionState != StandardDecisionStates.Fail && item.DecisionState != StandardDecisionStates.StandardConflict && item.DecisionState != StandardDecisionStates.NotApplicable && item.DecisionState != StandardDecisionStates.InsufficientEvidence) && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.general").Clauses.Single().ClauseId == "trc.pcd-pd-135.3.3.9.a" && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.flare").Clauses.Single().SourceId == pcd && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.flare").Clauses.Single().ClauseId == "trc.pcd-pd-135.3.4.13.b" && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.flare").Clauses.Single().AuthorityClass == StandardAuthorityClasses.Quarantined && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.pcd.flare").Clauses.Single().EnforcementMode == StandardEnforcementModes.NeverAutoEnforce && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.epr.legacy").Clauses.Single().ClauseId == "ips.e-pr-190.1.1" && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired && review.ReviewItems.Single(item => item.RuleId == "acceptance.p55.gsf.primary").Clauses.Single().ClauseId == "ips.g-sf-150.1.scope", "P5.5 review projection failed.");
            StandardSourceDefinition pcdSource = realSources.Get(pcd); StandardSourceDefinition eprSource = realSources.Get("ips.e-pr-190.rev1.2009"); StandardSourceDefinition gsfSource = realSources.Get("ips.g-sf-150.rev1.2023");
            Assert(pcdSource.SourceId == pcd && pcdSource.DocumentNumber == "PCD-PD-135" && pcdSource.Title == "PIPING DESIGN CRITERIA" && pcdSource.Issuer == "Tehran Raymand Consulting Engineers" && pcdSource.RevisionOrEdition == "1" && pcdSource.SourceFileName == "PCD-PD-135.pdf" && pcdSource.SourceSha256 == "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" && eprSource.SourceId == "ips.e-pr-190.rev1.2009" && eprSource.DocumentNumber == "IPS-E-PR-190(1)" && eprSource.Title == "ENGINEERING STANDARD FOR LAYOUT AND SPACING" && eprSource.Issuer == "Iranian Ministry of Petroleum" && eprSource.RevisionOrEdition == "First Revision / March 2009" && eprSource.SourceFileName == "IPS E-PR-190.pdf" && eprSource.SourceSha256 == "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" && gsfSource.SourceId == "ips.g-sf-150.rev1.2023" && gsfSource.DocumentNumber == "IPS-G-SF-150(1):2023 / INSO 23591:1402" && gsfSource.Title == "Petroleum industry – Requirements for spacing and layout of industry and equipment" && gsfSource.Issuer == "Iranian Ministry of Petroleum" && gsfSource.RevisionOrEdition == "First Edition / 1402 / 2023" && gsfSource.SourceFileName == "IPS G-SF-150-23591.pdf" && gsfSource.SourceSha256 == "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B", "P5.5 changed P3.6 source metadata.");

            StandardRulePackDefinition reversePack = Pack(pack.PackId, pack.Name, pack.Rules.AsEnumerable().Reverse().Select(rule => Rule(rule.RuleId, rule.Title, rule.ClauseIds.ToArray())).ToArray());
            StandardAuthorityResolution reverseAuthority = StandardAuthorityResolver.Resolve(StandardRulePackResolver.Resolve(StandardRulePackRegistry.Create(p55Clauses, new[] { reversePack }), new[] { Precedence(reversePack.PackId, 10) }), p55Clauses, policies);
            Assert(AuthorityIdentity(authority) == AuthorityIdentity(reverseAuthority) && GateIdentity(gate) == GateIdentity(StandardAuthorityExecutionGate.Evaluate(reverseAuthority)) && ReviewIdentity(review) == ReviewIdentity(StandardAuthorityReviewProjector.Create(reverseAuthority)), "P5.5 real pipeline ordering was not deterministic.");
        }

        private static void RunP61()
        {
            PlotPlanStandardsAuthorityRuntimeContext first = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            PlotPlanStandardsAuthorityRuntimeContext second = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            Assert(first.SourceRegistry.Sources.Count == 3 && first.ClauseRegistry.Clauses.Count == 9 && first.PolicyRegistry.Policies.Count == 9 && first.RuleResolution.EffectiveRules.Count == 4 && first.AuthorityResolution.Rules.Count == 4 && first.ExecutionGate.Rules.Count == 4 && first.ReviewProjection.ReviewItems.Count == 4, "P6.1 runtime authority context counts failed.");

            Assert(first.SourceRegistry.Get("trc.pcd-pd-135.rev1").SourceSha256 == "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" && first.SourceRegistry.Get("ips.e-pr-190.rev1.2009").SourceSha256 == "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" && first.SourceRegistry.Get("ips.g-sf-150.rev1.2023").SourceSha256 == "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B" && first.SourceRegistry.Sources.Select(source => source.SourceId).SequenceEqual(new[] { "ips.e-pr-190.rev1.2009", "ips.g-sf-150.rev1.2023", "trc.pcd-pd-135.rev1" }), "P6.1 runtime authority source identity failed.");
            string[] clauseIds = { "trc.pcd-pd-135.3.3.9.a", "ips.e-pr-190.1.1", "ips.g-sf-150.1.scope", "trc.pcd-pd-135.3.3.7.a", "trc.pcd-pd-135.3.3.7.b", "trc.pcd-pd-135.3.3.7.c", "trc.pcd-pd-135.3.4.13.a", "trc.pcd-pd-135.3.4.13.b", "trc.pcd-pd-135.3.4.13.c" };
            Assert(clauseIds.All(clauseId => first.ClauseRegistry.TryGet(clauseId, out _)) && first.ClauseRegistry.Get("trc.pcd-pd-135.3.3.9.a").ClauseReference == "3.3.9(a) — Roadways" && first.ClauseRegistry.Get("ips.e-pr-190.1.1").ClauseReference == "1.1" && first.ClauseRegistry.Get("ips.g-sf-150.1.scope").ClauseReference == "1 — هدف و دامنه کاربرد" && first.ClauseRegistry.Get("trc.pcd-pd-135.3.4.13.b").ClauseText.Contains("90 meters") && first.ClauseRegistry.Get("trc.pcd-pd-135.3.4.13.b").ClauseText.Contains("50 meters"), "P6.1 runtime authority clause identity failed.");

            string[] ruleIds = { "runtime.plotplan.authority.epr.legacy", "runtime.plotplan.authority.gsf.primary", "runtime.plotplan.authority.pcd.flare", "runtime.plotplan.authority.pcd.general" };
            Assert(first.RuleResolution.EffectiveRules.Select(rule => rule.RuleId).SequenceEqual(ruleIds) && first.RuleResolution.EffectiveRules.All(rule => rule.WinningPackId == "runtime.plotplan.real-standards-authority" && rule.WinningPrecedence == 10 && rule.OverriddenPackIds.Count == 0) && first.RuleResolution.EffectiveRules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").ClauseIds.Single() == "trc.pcd-pd-135.3.3.9.a" && first.RuleResolution.EffectiveRules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").ClauseIds.Single() == "trc.pcd-pd-135.3.4.13.b" && first.RuleResolution.EffectiveRules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").ClauseIds.Single() == "ips.e-pr-190.1.1" && first.RuleResolution.EffectiveRules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").ClauseIds.Single() == "ips.g-sf-150.1.scope", "P6.1 runtime authority rule mapping failed.");

            Assert(first.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").EffectiveEnforcementMode == StandardEnforcementModes.ConditionalReview && first.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce && first.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce && first.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").EffectiveEnforcementMode == StandardEnforcementModes.ConditionalReview && first.AuthorityResolution.Rules.SelectMany(rule => rule.Clauses).All(clause => !string.IsNullOrEmpty(clause.SourceId) && !string.IsNullOrEmpty(clause.ClauseId) && !string.IsNullOrEmpty(clause.PolicyId) && !string.IsNullOrEmpty(clause.AuthorityClass) && !string.IsNullOrEmpty(clause.EnforcementMode) && !string.IsNullOrEmpty(clause.Rationale)), "P6.1 runtime authority resolution failed.");

            Assert(first.ExecutionGate.Rules.All(rule => !rule.AutoExecutionAllowed && rule.DecisionState != StandardDecisionStates.Pass && rule.DecisionState != StandardDecisionStates.Fail && rule.DecisionState != StandardDecisionStates.StandardConflict && rule.DecisionState != StandardDecisionStates.NotApplicable && rule.DecisionState != StandardDecisionStates.InsufficientEvidence) && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired, "P6.1 runtime authority execution gate failed.");
            Assert(first.ReviewProjection.ReviewItems.Select(item => item.RuleId).SequenceEqual(ruleIds) && first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().ClauseId == "trc.pcd-pd-135.3.4.13.b" && first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().AuthorityClass == StandardAuthorityClasses.Quarantined && first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().EnforcementMode == StandardEnforcementModes.NeverAutoEnforce && first.ReviewProjection.ReviewItems.Where(item => item.RuleId != "runtime.plotplan.authority.pcd.flare").All(item => item.DecisionState == StandardDecisionStates.ExpertReviewRequired), "P6.1 runtime authority review projection failed.");

            string firstIdentity = ResolutionMetadata(first.RuleResolution) + ";" + AuthorityIdentity(first.AuthorityResolution) + ";" + GateIdentity(first.ExecutionGate) + ";" + ReviewIdentity(first.ReviewProjection);
            string secondIdentity = ResolutionMetadata(second.RuleResolution) + ";" + AuthorityIdentity(second.AuthorityResolution) + ";" + GateIdentity(second.ExecutionGate) + ";" + ReviewIdentity(second.ReviewProjection);
            Assert(firstIdentity == secondIdentity, "P6.1 repeated runtime context construction was not deterministic.");
            first.RuleResolution.EffectiveRules[0].RuleId = "changed";
            first.AuthorityResolution.Rules[0].RuleId = "changed";
            first.ExecutionGate.Rules[0].RuleId = "changed";
            first.ReviewProjection.ReviewItems[0].RuleId = "changed";
            PlotPlanStandardsAuthorityRuntimeContext third = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            string thirdIdentity = ResolutionMetadata(third.RuleResolution) + ";" + AuthorityIdentity(third.AuthorityResolution) + ";" + GateIdentity(third.ExecutionGate) + ";" + ReviewIdentity(third.ReviewProjection);
            Assert(thirdIdentity == secondIdentity, "P6.1 caller mutation leaked into a fresh runtime authority context.");
        }

        private static void RunP62()
        {
            PlotPlanQaReport empty = new PlotPlanQaReport { PlotPlan = new PlotPlanModel(), Qa = new QaReport() };
            Assert(empty.StandardsAuthority == null && !Json(empty).Contains("\"StandardsAuthority\""), "P6.2 null authority output was not backward-compatible.");

            PlotPlanStandardsAuthorityRuntimeContext context = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            var qa = new QaReport
            {
                OverallStatus = "SyntheticOverall",
                Statistics = new QaStatistics { RuleCount = 7, TotalFindings = 1 },
                RuleResults = new List<QaRuleResult> { new QaRuleResult { RuleId = "synthetic.rule", Severity = "Warning", FindingCount = 1, Status = "Synthetic" } },
                Findings = new List<QaFinding> { new QaFinding { FindingId = "synthetic.finding", RuleId = "synthetic.rule", Severity = "Warning", Category = "Synthetic", SubjectType = "Synthetic", SubjectId = "subject", Message = "Synthetic" } }
            };
            var report = new PlotPlanQaReport
            {
                PlotPlan = new PlotPlanModel { Geometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { new PlotGeometryPrimitive() } } },
                Qa = qa,
                StandardsAuthority = new PlotPlanStandardsAuthorityReport { AuthorityResolution = context.AuthorityResolution, ExecutionGate = context.ExecutionGate }
            };
            PlotPlanStandardsAuthorityReport authority = report.StandardsAuthority!;
            string[] ruleIds = { "runtime.plotplan.authority.epr.legacy", "runtime.plotplan.authority.gsf.primary", "runtime.plotplan.authority.pcd.flare", "runtime.plotplan.authority.pcd.general" };
            Assert(authority.AuthorityResolution.Rules.Count == 4 && authority.ExecutionGate.Rules.Count == 4 && authority.ExecutionGate.Rules.Count(rule => rule.AutoExecutionAllowed) == 0 && authority.AuthorityResolution.Rules.Select(rule => rule.RuleId).SequenceEqual(ruleIds) && authority.ExecutionGate.Rules.Select(rule => rule.RuleId).SequenceEqual(ruleIds), "P6.2 authority output counts or ordering failed.");
            Assert(authority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").Clauses.Single().ClauseId == "trc.pcd-pd-135.3.3.9.a" && authority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().ClauseId == "trc.pcd-pd-135.3.4.13.b" && authority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").Clauses.Single().ClauseId == "ips.e-pr-190.1.1" && authority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").Clauses.Single().ClauseId == "ips.g-sf-150.1.scope", "P6.2 clause mappings were not preserved.");
            StandardClauseAuthorityResolution flare = authority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single();
            Assert(flare.ClauseId == "trc.pcd-pd-135.3.4.13.b" && flare.SourceId == "trc.pcd-pd-135.rev1" && flare.PolicyId == "real.pcd-pd-135.rev1.flare.3.4.13.b" && flare.PolicyClauseId == "trc.pcd-pd-135.3.4.13.b" && flare.AuthorityClass == StandardAuthorityClasses.Quarantined && flare.EnforcementMode == StandardEnforcementModes.NeverAutoEnforce && !string.IsNullOrEmpty(flare.Rationale), "P6.2 flare clause authority evidence was not preserved.");
            Assert(authority.ExecutionGate.Rules.All(rule => !rule.AutoExecutionAllowed && rule.DecisionState != StandardDecisionStates.Pass && rule.DecisionState != StandardDecisionStates.Fail && rule.DecisionState != StandardDecisionStates.StandardConflict && rule.DecisionState != StandardDecisionStates.NotApplicable && rule.DecisionState != StandardDecisionStates.InsufficientEvidence) && authority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && authority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && authority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && authority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired, "P6.2 authority execution gate states failed.");

            string json = Json(report);
            Assert(json.Contains("\"StandardsAuthority\"") && json.Contains("\"AuthorityResolution\"") && json.Contains("\"ExecutionGate\"") && json.Contains("runtime.plotplan.authority.pcd.flare") && json.Contains("trc.pcd-pd-135.3.4.13.b") && json.Contains(StandardDecisionStates.SourceQuarantined) && json.Contains(StandardAuthorityClasses.Quarantined) && json.Contains(StandardEnforcementModes.NeverAutoEnforce) && !json.Contains("\"ReviewProjection\""), "P6.2 authority JSON contract failed.");

            PlotPlanQaReport compacted = PlotPlanReportCompactor.Create(report);
            Assert(compacted.StandardsAuthority != null && compacted.StandardsAuthority.AuthorityResolution.Rules.Count == 4 && compacted.StandardsAuthority.ExecutionGate.Rules.Count == 4 && compacted.StandardsAuthority.AuthorityResolution.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().ClauseId == flare.ClauseId && compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && compacted.PlotPlan.Geometry.Primitives.Count == 0, "P6.2 compactor authority preservation or primitive compaction failed.");
            Assert(compacted.Qa.OverallStatus == qa.OverallStatus && compacted.Qa.Statistics.RuleCount == qa.Statistics.RuleCount && compacted.Qa.Statistics.TotalFindings == qa.Statistics.TotalFindings && compacted.Qa.RuleResults.SequenceEqual(qa.RuleResults) && compacted.Qa.Findings.SequenceEqual(qa.Findings) && new PlotPlanQaReport().StandardsAuthority == null, "P6.2 authority output changed QA semantics or auto-populated itself.");
        }

        private static void RunP63()
        {
            var report = new PlotPlanQaReport
            {
                PlotPlan = new PlotPlanModel { Geometry = new PlotGeometryModel { Primitives = new List<PlotGeometryPrimitive> { new PlotGeometryPrimitive() } } },
                Qa = new QaReport { OverallStatus = "SyntheticOverall", Statistics = new QaStatistics { RuleCount = 2, TotalFindings = 1 }, RuleResults = new List<QaRuleResult> { new QaRuleResult { RuleId = "synthetic.rule", FindingCount = 1, Status = "Synthetic" } }, Findings = new List<QaFinding> { new QaFinding { FindingId = "synthetic.finding", RuleId = "synthetic.rule" } } }
            };
            report = PlotPlanReportCompactor.CreatePlotPlanQaReport(report.PlotPlan, report.Qa);
            PlotPlanQaReport compacted = PlotPlanReportCompactor.Create(report);
            string json = Json(compacted);
            Assert(compacted.StandardsAuthority != null && compacted.StandardsAuthority.AuthorityResolution.Rules.Count == 4 && compacted.StandardsAuthority.ExecutionGate.Rules.Count == 4 && compacted.StandardsAuthority.ExecutionGate.Rules.Count(rule => rule.AutoExecutionAllowed) == 0 && compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired && compacted.PlotPlan.Geometry.Primitives.Count == 0 && compacted.Qa.OverallStatus == report.Qa.OverallStatus && compacted.Qa.Statistics.RuleCount == report.Qa.Statistics.RuleCount && compacted.Qa.Statistics.TotalFindings == report.Qa.Statistics.TotalFindings && compacted.Qa.RuleResults.SequenceEqual(report.Qa.RuleResults) && compacted.Qa.Findings.SequenceEqual(report.Qa.Findings) && json.Contains("\"StandardsAuthority\"") && json.Contains("\"AuthorityResolution\"") && json.Contains("\"ExecutionGate\"") && !json.Contains("\"ReviewProjection\"") && !json.Contains("PASS") && !json.Contains("FAIL"), "P6.3 ES_PLOTQA authority report integration contract failed.");
        }

        private static void RunP64()
        {
            string[] ruleIds =
            {
                "runtime.plotplan.authority.epr.legacy",
                "runtime.plotplan.authority.gsf.primary",
                "runtime.plotplan.authority.pcd.flare",
                "runtime.plotplan.authority.pcd.general"
            };
            PlotPlanQaReport report = PlotPlanReportCompactor.CreatePlotPlanQaReport(
                new PlotPlanModel(),
                new QaReport { OverallStatus = "SyntheticOverall" });
            PlotPlanQaReport compacted = PlotPlanReportCompactor.Create(report);
            string json = Json(compacted);
            Assert(report.ReviewProjection != null && report.ReviewProjection.ReviewItems.Count == 4 &&
                   compacted.ReviewProjection != null && compacted.ReviewProjection.ReviewItems.Count == 4 &&
                   compacted.ReviewProjection.ReviewItems.Select(item => item.RuleId).SequenceEqual(ruleIds) &&
                   compacted.StandardsAuthority != null && compacted.StandardsAuthority.AuthorityResolution.Rules.Count == 4 &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Count == 4 &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.All(rule => !rule.AutoExecutionAllowed) &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Select(rule => rule.RuleId).SequenceEqual(ruleIds) &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired &&
                   compacted.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired &&
                   compacted.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined &&
                   compacted.Qa.Findings.Count == report.Qa.Findings.Count &&
                   compacted.Qa.RuleResults.Count == report.Qa.RuleResults.Count &&
                   !json.Contains("\"ReviewProjection\"") && !json.Contains("\"PASS\"") && !json.Contains("\"FAIL\""),
                   "P6.4 engineering review exposure contract failed.");

            string authorityIdentity = AuthorityIdentity(report.StandardsAuthority!.AuthorityResolution);
            string gateIdentity = GateIdentity(report.StandardsAuthority.ExecutionGate);
            report.ReviewProjection!.ReviewItems[0].RuleId = "mutated-review-only";
            Assert(AuthorityIdentity(report.StandardsAuthority.AuthorityResolution) == authorityIdentity &&
                   GateIdentity(report.StandardsAuthority.ExecutionGate) == gateIdentity,
                   "P6.5 review projection mutation contaminated authority or gate state.");
        }

        private static void RunP71()
        {
            PlotPlanStandardsAuthorityRuntimeContext first = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            PlotPlanStandardsAuthorityRuntimeContext second = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            string[] sourceIds = { "ips.e-pr-190.rev1.2009", "ips.g-sf-150.rev1.2023", "trc.pcd-pd-135.rev1" };
            string[] sourceHashes =
            {
                "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152",
                "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B",
                "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E"
            };
            Assert(first.SourceRegistry.Sources.Count == 3 && first.SourceRegistry.Sources.Select(source => source.SourceId).SequenceEqual(sourceIds) && first.SourceRegistry.Sources.Select(source => source.SourceSha256).SequenceEqual(sourceHashes), "P7.1 source identity or hash evidence was not stable.");

            StandardAuthorityReviewProjection review = first.ReviewProjection;
            Assert(review.ReviewItems.Count == 4 && review.ReviewItems.Select(item => item.RuleId).SequenceEqual(new[]
            {
                "runtime.plotplan.authority.epr.legacy",
                "runtime.plotplan.authority.gsf.primary",
                "runtime.plotplan.authority.pcd.flare",
                "runtime.plotplan.authority.pcd.general"
            }) && review.ReviewItems.All(item => item.Clauses.Count > 0 && item.Clauses.All(clause =>
                !string.IsNullOrEmpty(clause.ClauseId) &&
                !string.IsNullOrEmpty(clause.SourceId) &&
                !string.IsNullOrEmpty(clause.ClauseReference) &&
                !string.IsNullOrEmpty(clause.ClauseText) &&
                !string.IsNullOrEmpty(clause.SourceDocumentNumber) &&
                !string.IsNullOrEmpty(clause.SourceTitle) &&
                !string.IsNullOrEmpty(clause.SourceIssuer) &&
                !string.IsNullOrEmpty(clause.SourceRevisionOrEdition) &&
                !string.IsNullOrEmpty(clause.SourceFileName) &&
                !string.IsNullOrEmpty(clause.SourceSha256))), "P7.1 review items did not trace to complete source and clause evidence.");

            StandardClauseAuthorityResolution flare = review.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single();
            Assert(flare.ClauseId == "trc.pcd-pd-135.3.4.13.b" && flare.SourceId == "trc.pcd-pd-135.rev1" && flare.ClauseReference == "3.4.13(b) — Flare" && flare.ClauseText != null && flare.ClauseText.Contains("90 meters") && flare.PageNumber == 24 && flare.SourceRevisionOrEdition == "1" && flare.SourceFileName == "PCD-PD-135.pdf" && flare.SourceSha256 == "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E", "P7.1 flare clause traceability was not preserved.");

            Assert(first.AuthorityResolution.Rules.Count == 4 && first.ExecutionGate.Rules.Count == 4 && first.ReviewProjection.ReviewItems.Count == 4 && first.ExecutionGate.Rules.Count(rule => rule.AutoExecutionAllowed) == 0 && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired, "P7.1 authority safety counts or states changed.");

            PlotPlanQaReport report = PlotPlanReportCompactor.CreatePlotPlanQaReport(new PlotPlanModel(), new QaReport());
            string json = Json(report);
            Assert(report.ReviewProjection != null && report.Qa.Findings.Count == 0 && report.Qa.RuleResults.Count == 0 && !json.Contains("\"ReviewProjection\""), "P7.1 authority integration changed QA findings, rule results, or serialization.");

            StandardAuthorityReviewProjection withoutRegistries = StandardAuthorityReviewProjector.Create(first.AuthorityResolution, null, null);
            StandardClauseAuthorityResolution unavailable = withoutRegistries.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single();
            Assert(unavailable.SourceDocumentNumber == null && unavailable.SourceTitle == null && unavailable.SourceIssuer == null && unavailable.SourceRevisionOrEdition == null && unavailable.SourceFileName == null && unavailable.SourceSha256 == null && unavailable.ClauseReference == null && unavailable.ClauseText == null && unavailable.ClauseHeading == null && unavailable.PageNumber == null, "P7.1 unavailable provenance metadata was fabricated.");

            string firstIdentity = string.Join("|", review.ReviewItems.Select(item => item.RuleId + ":" + string.Join(",", item.Clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.ClauseReference + ":" + clause.PageNumber + ":" + clause.SourceSha256))));
            string secondIdentity = string.Join("|", second.ReviewProjection.ReviewItems.Select(item => item.RuleId + ":" + string.Join(",", item.Clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.ClauseReference + ":" + clause.PageNumber + ":" + clause.SourceSha256))));
            Assert(firstIdentity == secondIdentity, "P7.1 repeated authority traceability construction was not deterministic.");
            flare.ClauseText = "mutated";
            flare.SourceSha256 = "mutated";
            StandardAuthorityReviewItem? secondFlareItem = second.ReviewProjection.ReviewItems.SingleOrDefault(item => item.RuleId == "runtime.plotplan.authority.pcd.flare");
            Assert(secondFlareItem != null && secondFlareItem.Clauses.Single().ClauseText != "mutated" && secondFlareItem.Clauses.Single().SourceSha256 != "mutated", "P7.1 review traceability mutation leaked across contexts.");
        }

        private static void RunP72()
        {
            DateTime timestamp = new DateTime(2026, 9, 8, 19, 49, 8, 658, DateTimeKind.Utc);
            PlotPlanQaReport firstReport = PlotPlanReportCompactor.CreatePlotPlanQaReport(new PlotPlanModel(), new QaReport());
            PlotPlanQaReport secondReport = PlotPlanReportCompactor.CreatePlotPlanQaReport(new PlotPlanModel(), new QaReport());
            StandardAuthorityReviewPackage first = StandardAuthorityReviewPackageBuilder.Create(firstReport, "PLOTPLAN-A0-E-FINAL.dwg", timestamp);
            StandardAuthorityReviewPackage second = StandardAuthorityReviewPackageBuilder.Create(secondReport, "PLOTPLAN-A0-E-FINAL.dwg", timestamp);
            string firstJson = Json(first);
            string secondJson = Json(second);

            Assert(first.SchemaVersion == "1.0" && first.DrawingName == "PLOTPLAN-A0-E-FINAL.dwg" && first.ScanTimestampUtc == timestamp && first.StandardsAuthority.AuthorityResolution.Rules.Count == 4 && first.StandardsAuthority.ExecutionGate.Rules.Count == 4 && first.ReviewProjection.ReviewItems.Count == 4 && first.StandardsAuthority.ExecutionGate.Rules.Count(rule => rule.AutoExecutionAllowed) == 0, "P7.2 package contract or counts failed.");
            Assert(first.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.epr.legacy").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.gsf.primary").DecisionState == StandardDecisionStates.ExpertReviewRequired && first.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && first.StandardsAuthority.ExecutionGate.Rules.Single(rule => rule.RuleId == "runtime.plotplan.authority.pcd.general").DecisionState == StandardDecisionStates.ExpertReviewRequired, "P7.2 package gate states changed.");

            string[] ruleIds = { "runtime.plotplan.authority.epr.legacy", "runtime.plotplan.authority.gsf.primary", "runtime.plotplan.authority.pcd.flare", "runtime.plotplan.authority.pcd.general" };
            Assert(first.ReviewProjection.ReviewItems.Select(item => item.RuleId).SequenceEqual(ruleIds) && first.StandardsAuthority.AuthorityResolution.Rules.Select(rule => rule.RuleId).SequenceEqual(ruleIds) && first.StandardsAuthority.ExecutionGate.Rules.Select(rule => rule.RuleId).SequenceEqual(ruleIds) && first.ReviewProjection.ReviewItems.All(item => item.Clauses.SequenceEqual(item.Clauses.OrderBy(clause => clause.ClauseId, StringComparer.Ordinal))), "P7.2 package ordering was not deterministic.");
            Assert(first.ReviewProjection.ReviewItems.All(item => item.Clauses.Count > 0 && item.Clauses.All(clause => !string.IsNullOrEmpty(clause.ClauseId) && !string.IsNullOrEmpty(clause.SourceId) && !string.IsNullOrEmpty(clause.SourceSha256))), "P7.2 review item traceability was incomplete.");
            string[] acceptedSourceIds = { "ips.e-pr-190.rev1.2009", "ips.g-sf-150.rev1.2023", "trc.pcd-pd-135.rev1" };
            string[] acceptedSourceHashes = { "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152", "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B", "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" };
            Assert(acceptedSourceIds.Select((sourceId, index) => first.ReviewProjection.ReviewItems.SelectMany(item => item.Clauses).Where(clause => clause.SourceId == sourceId).ToList()).Select((clauses, index) => clauses.Count > 0 && clauses.All(clause => clause.SourceSha256 == acceptedSourceHashes[index])).All(valid => valid), "P7.2 accepted source identities were not preserved.");
            Assert(first.StandardsAuthority.AuthorityResolution.Rules.All(rule => rule.Clauses.SequenceEqual(rule.Clauses.OrderBy(clause => clause.ClauseId, StringComparer.Ordinal))), "P7.2 authority clause ordering was not deterministic.");

            StandardClauseAuthorityResolution flare = first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single();
            Assert(flare.ClauseId == "trc.pcd-pd-135.3.4.13.b" && flare.SourceId == "trc.pcd-pd-135.rev1" && flare.SourceSha256 == "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" && flare.PageNumber == 24 && flare.ClauseText != null && flare.ClauseText.Contains("90 meters"), "P7.2 package traceability or flare evidence failed.");
            Assert(firstJson == secondJson && firstReport.Qa.Findings.Count == 0 && firstReport.Qa.RuleResults.Count == 0 && !Json(firstReport).Contains("\"ReviewProjection\"") && !Json(firstReport).Contains("StandardAuthorityReviewPackage") && firstJson.Contains("\"ReviewProjection\"") && firstJson.Contains("trc.pcd-pd-135.3.4.13.b") && firstJson.Contains("90 meters"), "P7.2 serialization or QA safety boundaries failed.");

            first.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState = "mutated";
            flare.ClauseText = "mutated";
            flare.SourceSha256 = "mutated";
            Assert(firstReport.ReviewProjection!.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").DecisionState == StandardDecisionStates.SourceQuarantined && firstReport.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().ClauseText != "mutated" && second.ReviewProjection.ReviewItems.Single(item => item.RuleId == "runtime.plotplan.authority.pcd.flare").Clauses.Single().SourceSha256 != "mutated", "P7.2 package mutation isolation failed.");
        }

        private static List<KnownQaRule> Catalog() => BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
        private static EngineeringStandardProfile Profile(string id, string level, params RuleSetting[] settings) => new EngineeringStandardProfile { ProfileId = id, Name = id + " profile", Level = level, Source = "M3.0 test fixture only", Version = "1.0", RuleSettings = settings.ToList() };
        private static void Invalid(List<KnownQaRule> catalog, IEnumerable<EngineeringStandardProfile> profiles, string expected) { bool thrown = false; try { ProfileResolver.Resolve(catalog, profiles); } catch (ProfileValidationException exception) { thrown = exception.Message.Contains(expected); } Assert(thrown, "Expected profile validation failure: " + expected); }
        private static string Serialize(ProfileResolution value) => string.Join("|", value.AppliedProfileIds) + ";" + string.Join("|", value.EffectiveRuleSettings.Select(x => x.EffectiveRuleId + ":" + x.Enabled + ":" + x.EffectiveSeverity + ":" + x.EnabledSourceProfileId + ":" + x.SeveritySourceProfileId));
        private static string Json<T>(T value) { var serializer = new DataContractJsonSerializer(typeof(T)); using (var stream = new MemoryStream()) { serializer.WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); } }
        private static string Comparable(QaReport report) => string.Join("|", report.RuleResults.Select(x => x.RuleId + ":" + x.Severity + ":" + x.FindingCount)) + ";" + string.Join("|", report.Findings.Select(x => x.FindingId + ":" + x.Severity));
        private static StandardSourceDefinition Source(string sourceId, string? revisionOrEdition = null, string? sourceFileName = null, string? sourceSha256 = null) => new StandardSourceDefinition { SourceId = sourceId, DocumentNumber = "DOC " + sourceId, Title = "Title " + sourceId, Issuer = "Issuer " + sourceId, RevisionOrEdition = revisionOrEdition, SourceFileName = sourceFileName, SourceSha256 = sourceSha256 };
        private static string SourceMetadata(IEnumerable<StandardSourceDefinition> sources) => string.Join("|", sources.Select(source => source.SourceId + ":" + source.DocumentNumber + ":" + source.Title + ":" + source.Issuer + ":" + source.RevisionOrEdition + ":" + source.SourceFileName + ":" + source.SourceSha256));
        private static void RegistryInvalid(Action action, string expectedMessage) { bool thrown = false; try { action(); } catch (StandardSourceRegistryValidationException exception) { thrown = exception.Message == expectedMessage; } Assert(thrown, "Expected standard source registry validation failure: " + expectedMessage); }
        private static StandardClauseDefinition Clause(string clauseId, string sourceId = "source.a", string clauseReference = "1.0", string clauseText = "Synthetic clause text", string? heading = null, int? pageNumber = null) => new StandardClauseDefinition { ClauseId = clauseId, SourceId = sourceId, ClauseReference = clauseReference, ClauseText = clauseText, Heading = heading, PageNumber = pageNumber };
        private static string ClauseMetadata(IEnumerable<StandardClauseDefinition> clauses) => string.Join("|", clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.ClauseReference + ":" + clause.ClauseText + ":" + clause.Heading + ":" + clause.PageNumber));
        private static void ClauseRegistryInvalid(Action action, string expectedMessage) { bool thrown = false; try { action(); } catch (StandardClauseRegistryValidationException exception) { thrown = exception.Message == expectedMessage; } Assert(thrown, "Expected standard clause registry validation failure: " + expectedMessage); }
        private static StandardRuleDefinition Rule(string ruleId, string title, params string[] clauseIds) => new StandardRuleDefinition { RuleId = ruleId, Title = title, ClauseIds = clauseIds.ToList() };
        private static StandardRulePackDefinition Pack(string packId, string name, params StandardRuleDefinition[] rules) => new StandardRulePackDefinition { PackId = packId, Name = name, Rules = rules.ToList() };
        private static string PackMetadata(IEnumerable<StandardRulePackDefinition> packs) => string.Join("|", packs.Select(pack => pack.PackId + ":" + pack.Name + ":" + string.Join(",", pack.Rules.Select(rule => rule.RuleId + ":" + rule.Title + ":" + string.Join(",", rule.ClauseIds)))));
        private static void PackRegistryInvalid(Action action, string expectedMessage) { bool thrown = false; try { action(); } catch (StandardRulePackRegistryValidationException exception) { thrown = exception.Message == expectedMessage; } Assert(thrown, "Expected standard rule pack registry validation failure: " + expectedMessage); }
        private static StandardRulePackPrecedenceDefinition Precedence(string packId, int precedence) => new StandardRulePackPrecedenceDefinition { PackId = packId, Precedence = precedence };
        private static string ResolutionMetadata(StandardRulePackResolution resolution) => string.Join("|", resolution.AppliedPrecedence.Select(entry => entry.PackId + ":" + entry.Precedence)) + ";" + string.Join("|", resolution.EffectiveRules.Select(rule => rule.RuleId + ":" + rule.Title + ":" + string.Join(",", rule.ClauseIds) + ":" + rule.WinningPackId + ":" + rule.WinningPrecedence + ":" + string.Join(",", rule.OverriddenPackIds)));
        private static void ResolverInvalid(Action action, string expectedMessage) { bool thrown = false; try { action(); } catch (StandardRulePackResolutionException exception) { thrown = exception.Message == expectedMessage; } Assert(thrown, "Expected standard rule pack resolution failure: " + expectedMessage); }
        private static EffectiveStandardRuleDefinition EffectiveRule(string ruleId, string title, string packId, int precedence, string[] overriddenPackIds, params string[] clauseIds) => new EffectiveStandardRuleDefinition { RuleId = ruleId, Title = title, WinningPackId = packId, WinningPrecedence = precedence, OverriddenPackIds = overriddenPackIds.ToList(), ClauseIds = clauseIds.ToList() };
        private static StandardRulePackResolution Resolution(params EffectiveStandardRuleDefinition[] rules) => new StandardRulePackResolution { EffectiveRules = rules.ToList() };
        private static QaFinding Finding(string ruleId) => new QaFinding { FindingId = "finding:" + ruleId, RuleId = ruleId, Severity = "Warning", Category = "Synthetic", SubjectType = "SyntheticSubject", SubjectId = "subject:" + ruleId, Message = "Synthetic message", Evidence = new QaEvidence { Reason = "Synthetic evidence" }, SourceHandles = new List<string> { "H1" } };
        private static StandardFindingProvenance Provenance(string ruleId) => new StandardFindingProvenance { RuleId = ruleId, RuleTitle = "Old", WinningPackId = "old.pack", WinningPrecedence = 1, OverriddenPackIds = new List<string> { "old.low" }, Clauses = new List<StandardClauseCitation> { new StandardClauseCitation { ClauseId = "old.clause" } } };
        private static string FindingIdentity(QaFinding finding) => finding.FindingId + ":" + finding.RuleId + ":" + finding.Severity + ":" + finding.Category + ":" + finding.SubjectType + ":" + finding.SubjectId + ":" + finding.Message + ":" + finding.Evidence.Reason + ":" + string.Join(",", finding.SourceHandles);
        private static string ProvenanceIdentity(StandardFindingProvenance provenance) => provenance.RuleId + ":" + provenance.RuleTitle + ":" + provenance.WinningPackId + ":" + provenance.WinningPrecedence + ":" + string.Join(",", provenance.OverriddenPackIds) + ":" + string.Join(",", provenance.Clauses.Select(citation => citation.ClauseId + ":" + citation.SourceId + ":" + citation.ClauseReference + ":" + citation.ClauseText));
        private static string AuthorityIdentity(StandardAuthorityResolution resolution) => string.Join("|", resolution.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + string.Join(",", rule.Clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.PolicyId + ":" + clause.PolicyClauseId + ":" + clause.AuthorityClass + ":" + clause.EnforcementMode + ":" + clause.Rationale))));
        private static string GateIdentity(StandardExecutionGateResolution resolution) => string.Join("|", resolution.Rules.Select(rule => rule.RuleId + ":" + rule.EffectiveEnforcementMode + ":" + rule.AutoExecutionAllowed + ":" + rule.DecisionState));
        private static string ReviewIdentity(StandardAuthorityReviewProjection projection) => string.Join("|", projection.ReviewItems.Select(item => item.RuleId + ":" + item.EffectiveEnforcementMode + ":" + item.DecisionState + ":" + item.Message + ":" + string.Join(",", item.Clauses.Select(clause => clause.ClauseId + ":" + clause.SourceId + ":" + clause.PolicyId + ":" + clause.PolicyClauseId + ":" + clause.AuthorityClass + ":" + clause.EnforcementMode + ":" + clause.Rationale))));
        private static void ThrowsAuthorityPolicy(Action action) { try { action(); } catch (StandardAuthorityPolicyRegistryValidationException) { return; } throw new InvalidOperationException("Expected standard authority policy registry validation failure."); }
        private static void ProvenanceInvalid(Action action, string expectedMessage) { bool thrown = false; try { action(); } catch (StandardFindingProvenanceException exception) { thrown = exception.Message == expectedMessage; } Assert(thrown, "Expected standard finding provenance failure: " + expectedMessage); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
