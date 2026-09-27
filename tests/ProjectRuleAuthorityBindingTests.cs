using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;
using EngiSentry.AutoCAD2021;
using EngiSentry.AutoCAD2021.Scanning;

namespace EngiSentry.Tests
{
    /// <summary>
    /// P11.1 authority-binding acceptance tests for the structural defect where project distance rules
    /// (project.plotplan.equipment.*) and authority-chain rules (runtime.plotplan.authority.* / std.plotplan.*)
    /// live in distinct identity namespaces. The governed projection previously joined them by exact RuleId,
    /// so a project rule could never receive execution authority through the real path. The fix introduces an
    /// explicit, evidence-backed <see cref="ProjectRuleAuthorityBinding"/> that is validated against the
    /// authority chain's actual resolution. Every fixture here is synthetic and TEST_ONLY; no production
    /// catalog, source, clause, approval, authority, hash or distance is created or altered.
    ///
    /// Follows the existing harness convention (internal static Run + Assert).
    /// </summary>
    internal static class ProjectRuleAuthorityBindingTests
    {
        private const string InsertionUnits = "Meters";
        private const double MetersPerDrawingUnit = 1.0d;
        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const string FenceHandle = "fence:geometry";
        private const double EquipmentEastingMeters = 100d;

        // ---- TEST_ONLY deterministic governed authority (composed through the REAL authority chain) ----
        private const string TestSourceId = "test.governed.source.rev1";
        private const string TestClauseId = "test.governed.road.clearance";
        private const string TestPolicyId = "test.governed.policy.road";
        private const string TestAuthorityRuleId = "test.authority.governed.road.minimum-clearance";
        private const string TestSourceSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        internal static void Run()
        {
            NoBindingGrantsNothing();                 // 1
            MismatchedBindingGrantsNothing();         // 2
            StaleOrUntrustedAuthorityGrantsNothing(); // 3
            ValidDeterministicBindingGrants();        // 4
            ProductionCatalogRemainsZeroExecutable(); // 5
            SuggestProducesOneProposalOnlyWithValidBinding(); // 6
            GovernedCompatibleValueAloneGrantsNothing();      // explicit requirement
            NoHostTypedMembersOrHostReference();             // layering safety
            Console.WriteLine("P11.1 project-rule authority binding assertions passed: " + _assertions);
        }

        // ---- 1. No binding -> AutoExecutionAllowed false ----------------------------------------------

        private static void NoBindingGrantsNothing()
        {
            // Even when a DETERMINISTIC authority rule genuinely exists in the context, a project rule that
            // supplies no explicit binding must receive no execution authority: the identities are never
            // joined by naming convention or by a hard-coded mapping.
            PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
            StandardsPackRule road = ProjectRoadRule(context, authorityBindings: null);

            Assert(road != null, "The projection returned no rule.");
            Assert(!road!.AutoExecutionAllowed, "A project rule with no authority binding received execution authority.");
            Assert(road.ConstraintTreatment == ConstraintTreatment.ReviewRequired || road.ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "An unbound project rule carried an unexpected treatment: " + road.ConstraintTreatment);
            Assert(road.AuthorityClass == null, "An unbound project rule synthesized authority provenance.");
            Assert(road.Clauses.Count == 0, "An unbound project rule synthesized clause evidence.");

            // The validator itself fail-closes on an absent binding.
            ProjectRuleAuthorityBindingValidationResult absent = ProjectRuleAuthorityBindingValidator.Validate(null, context);
            Assert(!absent.IsValid, "A null binding validated as valid.");
            Assert(absent.Reasons.Count > 0, "A null binding recorded no reason.");
        }

        // ---- 2. Wrong / mismatched binding -> false ----------------------------------------------------

        private static void MismatchedBindingGrantsNothing()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();

            // 2a. The binding points at an authority rule the chain never resolved.
            AssertInvalid(With(ValidBinding(), authorityRuleId: "authority.does.not.exist"), context,
                "A binding to a non-existent authority rule granted authority.");

            // 2b. The recorded authority class contradicts the chain's resolution.
            AssertInvalid(With(ValidBinding(), authorityClass: StandardAuthorityClasses.ActivePrimary), context,
                "A binding with a mismatched AuthorityClass granted authority.");

            // 2c. The recorded enforcement mode contradicts the gate.
            AssertInvalid(With(ValidBinding(), enforcementMode: StandardEnforcementModes.ConditionalReview), context,
                "A binding with a mismatched EnforcementMode granted authority.");

            // 2d. The recorded source id contradicts the resolved clause.
            AssertInvalid(With(ValidBinding(), sourceId: "some.other.source"), context,
                "A binding with a mismatched SourceId granted authority.");

            // 2e. The recorded source hash is not the registered source hash.
            AssertInvalid(With(ValidBinding(), sourceSha256: "B".PadLeft(64, 'B')), context,
                "A binding with a mismatched SourceSha256 granted authority.");

            // 2f. The recorded policy id contradicts the resolved clause policy.
            AssertInvalid(With(ValidBinding(), policyId: "some.other.policy"), context,
                "A binding with a mismatched PolicyId granted authority.");

            // 2g. The binding cites a clause the chain did not resolve for the bound authority rule.
            AssertInvalid(With(ValidBinding(), clauseIds: new List<string> { "clause.never.resolved" }), context,
                "A binding citing an unresolved clause granted authority.");

            // 2h. A self-mapping binding is malformed.
            AssertInvalid(With(ValidBinding(), projectRuleId: TestAuthorityRuleId), context,
                "A self-mapping binding granted authority.");

            // 2i. Every mismatched binding must also leave the projected rule non-executable.
            foreach (ProjectRuleAuthorityBinding bad in MismatchedBindings())
            {
                StandardsPackRule rule = ProjectRoadRule(context, new[] { bad });
                Assert(!rule.AutoExecutionAllowed, "A mismatched binding left the projected rule executable: " + bad.AuthorityRuleId);
            }

            // 2j. A binding to a real but NON-DETERMINISTIC authority rule grants nothing.
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            string realAuthorityRule = real.ExecutionGate.Rules.First().RuleId;
            ProjectRuleAuthorityBinding nonDeterministic = new ProjectRuleAuthorityBinding
            {
                ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                AuthorityRuleId = realAuthorityRule
            };
            ProjectRuleAuthorityBindingValidationResult nonDeterministicResult = ProjectRuleAuthorityBindingValidator.Validate(nonDeterministic, real);
            Assert(!nonDeterministicResult.IsValid, "A binding to a non-deterministic authority rule validated as valid.");
            StandardsPackRule nonDeterministicRule = ProjectRoadRule(real, new[] { nonDeterministic });
            Assert(!nonDeterministicRule.AutoExecutionAllowed, "A binding to a non-deterministic authority rule left the projected rule executable.");
        }

        // ---- 3. Stale / untrusted authority -> false ----------------------------------------------------

        private static void StaleOrUntrustedAuthorityGrantsNothing()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
            ProjectRuleAuthorityBinding binding = ValidBinding();

            // 3a. A stale approval currentness verdict revokes the grant.
            var stale = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
            {
                { binding.ProjectRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Stale, Reasons = new List<string> { EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged } } }
            };
            ProjectRuleAuthorityBindingValidationResult staleResult = ProjectRuleAuthorityBindingValidator.Validate(binding, context, stale, null);
            Assert(!staleResult.IsValid, "A binding tied to a STALE approval validated as valid.");

            // A CURRENT currentness verdict does not by itself revoke.
            var current = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
            {
                { binding.ProjectRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Current } }
            };
            Assert(ProjectRuleAuthorityBindingValidator.Validate(binding, context, current, null).IsValid, "A binding tied to a CURRENT approval failed to validate.");

            // 3b. A non-deterministic P9 approval authority (untrusted for execution) revokes the grant.
            foreach (string untrusted in new[] { EngineeringDistanceApprovalAuthorityStates.NotApproved, EngineeringDistanceApprovalAuthorityStates.StaleApproval,
                                                EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional, EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy })
            {
                var untrustedMap = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
                {
                    { binding.ProjectRuleId, new EngineeringDistanceApprovalAuthorityResult { State = untrusted, ApprovalRecordId = "approval-1" } }
                };
                ProjectRuleAuthorityBindingValidationResult untrustedResult = ProjectRuleAuthorityBindingValidator.Validate(binding, context, null, untrustedMap);
                Assert(!untrustedResult.IsValid, "A binding tied to an untrusted authority state validated as valid: " + untrusted);
            }

            // A binding whose recorded approval record contradicts the resolved approval authority revokes.
            var deterministicButMismatched = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
            {
                { binding.ProjectRuleId, new EngineeringDistanceApprovalAuthorityResult { State = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic, ApprovalRecordId = "approval-other" } }
            };
            ProjectRuleAuthorityBinding withApprovalRecord = With(binding, approvalRecordId: "approval-1");
            Assert(!ProjectRuleAuthorityBindingValidator.Validate(withApprovalRecord, context, null, deterministicButMismatched).IsValid,
                "A binding whose ApprovalRecordId contradicts the resolved approval authority validated as valid.");

            // 3c. At the projection level the existing checks must also lower the grant.
            StandardsPackRule staleProjected = ProjectRoadRule(context, new[] { binding }, currentnessByRuleId: stale);
            Assert(!staleProjected.AutoExecutionAllowed, "A stale approval left the projected rule executable through the projection.");
            Assert(staleProjected.ConstraintTreatment == ConstraintTreatment.ReviewRequired, "A stale approval did not surface as review-required.");
            Assert(staleProjected.CurrentnessState == EngineeringDistanceApprovalCurrentnessStates.Stale, "The projected rule lost its currentness state.");

            var untrustedProjectedMap = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
            {
                { binding.ProjectRuleId, new EngineeringDistanceApprovalAuthorityResult { State = EngineeringDistanceApprovalAuthorityStates.NotApproved } }
            };
            StandardsPackRule untrustedProjected = ProjectRoadRule(context, new[] { binding }, approvalAuthorityByRuleId: untrustedProjectedMap);
            Assert(!untrustedProjected.AutoExecutionAllowed, "An untrusted approval left the projected rule executable through the projection.");
        }

        // ---- 4. Explicit TEST_ONLY valid deterministic authority binding -> true -------------------------

        private static void ValidDeterministicBindingGrants()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
            ProjectRuleAuthorityBinding binding = ValidBinding();

            ProjectRuleAuthorityBindingValidationResult result = ProjectRuleAuthorityBindingValidator.Validate(binding, context);
            Assert(result.IsValid, "A valid deterministic binding failed to validate: " + string.Join("; ", result.Reasons));
            Assert(result.BoundGateDecision != null && result.BoundGateDecision.AutoExecutionAllowed, "A valid binding carried no executable gate decision.");
            Assert(result.BoundGateDecision!.EffectiveEnforcementMode == StandardEnforcementModes.Deterministic, "A valid binding carried a non-deterministic mode.");
            Assert(result.AuthorityClass == StandardAuthorityClasses.GoverningProject, "A valid binding lost the governing authority class.");
            Assert(result.SourceSha256 == TestSourceSha256, "A valid binding lost the registered source hash.");
            Assert(result.Clauses.Count > 0, "A valid binding carried no clause evidence.");

            StandardsPackRule road = ProjectRoadRule(context, new[] { binding });

            Assert(road.AutoExecutionAllowed, "A valid deterministic binding did not grant execution authority to the projected rule.");
            Assert(road.EffectiveEnforcementMode == StandardEnforcementModes.Deterministic, "The projected rule lost its deterministic enforcement mode.");
            Assert(road.AuthorityClass == StandardAuthorityClasses.GoverningProject, "The projected rule lost its governing authority class.");
            Assert(road.DecisionState == null || road.DecisionState == StandardDecisionStates.Pass, "The projected rule carried an unclean decision state: " + road.DecisionState);
            Assert(!road.EngineeringReviewRequired, "A deterministic governed rule still required engineering review.");
            Assert(road.SourceSha256 == TestSourceSha256, "The projected rule lost its source hash provenance.");
            Assert(road.Clauses.Count == 1 && road.Clauses[0].ClauseId == TestClauseId, "The projected rule lost its clause evidence.");
            Assert(road.Clauses[0].AuthorityClass == StandardAuthorityClasses.GoverningProject, "The projected clause lost its authority class.");
            Assert(road.ConstraintTreatment == ConstraintTreatment.ProjectRequirement, "A governed executable project rule changed its treatment: " + road.ConstraintTreatment);

            // Only the bound rule becomes executable; the sibling fence/unit rules stay fail-closed.
            List<StandardsPackRule> all = ProjectAllRules(context, new[] { binding });
            Assert(all.Count(x => x.AutoExecutionAllowed) == 1, "More than one rule became executable under a single road binding.");
            Assert(all.Single(x => x.AutoExecutionAllowed).RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId, "The wrong rule became executable.");
        }

        // ---- 5. The real production catalog stays at 0 auto-executable ----------------------------------

        private static void ProductionCatalogRemainsZeroExecutable()
        {
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();

            // The authority gate itself grants nothing today.
            Assert(real.ExecutionGate.Rules.Count > 0, "The real authority gate has no rules.");
            Assert(real.ExecutionGate.Rules.All(g => !g.AutoExecutionAllowed), "The real authority gate granted auto-execution.");

            // The exact ES_SUGGEST production composition (no bindings supplied) yields no executable rule.
            ProjectDistanceCriteria criteria = TestCriteria(Path.Combine(Path.GetTempPath(), "EngiSentry-BindingAudit-" + Guid.NewGuid().ToString("N")));
            List<EngineeringDistanceRuleDefinition> binderRules = ProjectDistanceRuleBinder.Create(criteria).ToList();

            // The old structural defect is documented, not re-introduced: the identity sets never overlap.
            HashSet<string> gateIds = new HashSet<string>(real.ExecutionGate.Rules.Select(g => g.RuleId), StringComparer.Ordinal);
            Assert(binderRules.Count(r => gateIds.Contains(r.RuleId)) == 0, "Project rule identities now overlap the authority gate identities by naming.");

            List<StandardsPackRule> projected = StandardsPackProjectionBuilder.Build(real, binderRules).ToList();
            Assert(projected.Count > 0, "The production projection produced no rules.");
            Assert(projected.All(x => !x.AutoExecutionAllowed), "The production projection yielded an auto-executable rule with no binding.");
            Assert(projected.All(x => x.AuthorityClass == null), "The production projection synthesized authority provenance.");
            Assert(projected.All(x => x.Clauses.Count == 0), "The production projection synthesized clause evidence.");

            // Through the production-facing helper the result is identical: fail-closed.
            ProjectDistanceGovernanceAggregateResult emptyGovernance = new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = criteria.ProjectDirectory };
            List<StandardsPackRule> viaHelper = PlacementProposalGovernedRuleProjection.Build(criteria, emptyGovernance, real).ToList();
            Assert(viaHelper.All(x => !x.AutoExecutionAllowed), "The production helper yielded an auto-executable rule with no binding.");
            Assert(viaHelper.Count == projected.Count, "The production helper changed the governed rule count.");
        }

        // ---- 6. ES_SUGGEST produces ProposalCount=1 only in the valid synthetic governed case -----------

        private static void SuggestProducesOneProposalOnlyWithValidBinding()
        {
            string project = Path.Combine(Path.GetTempPath(), "EngiSentry-BindingAuditSuggest-" + Guid.NewGuid().ToString("N"));
            string approvalDirectory = Path.Combine(project, "qa", "plotplan");
            Directory.CreateDirectory(approvalDirectory);
            try
            {
                ProjectDistanceCriteria criteria = TestCriteria(project, roadMinimumMeters: 15m);
                PlotPlanModel plotPlan = PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d);

                // The real P9 loader resolves an empty approval directory to PROJECT_RULE_ONLY with no
                // authority state, exactly as production does.
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                Assert(governance.Items.All(x => x.AuthorityState == null), "The empty approval directory produced an authority state.");
                Assert(governance.Items.All(x => x.Resolution.GovernanceProvenance == null || x.Resolution.GovernanceProvenance.ApprovedMinimumDistanceMeters <= 0m),
                    "The empty approval directory produced an approved governed value.");

                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                PlotPlanModel meterPlan = plotPlan;

                // 6a. Without a binding the exact ES_SUGGEST composition yields no executable rule -> 0 proposals.
                PlacementProposalRunResult blocked = PlacementProposalRuntime.Build(
                    meterPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context), governance, out PlacementProposalRunDiagnostics blockedDiagnostics);
                Assert(blockedDiagnostics.ProposalCount == 0, "The ES_SUGGEST composition produced proposals without an authority binding.");
                Assert(blockedDiagnostics.ExecutableViolatedCount == 0, "The ES_SUGGEST composition counted an executable violation without an authority binding.");
                Assert(blockedDiagnostics.ReviewRequiredCount > 0, "The unbound composition did not surface review-required constraints.");
                Assert(blocked.Proposals.Count == 0, "The unbound composition emitted proposals.");

                // 6b. With a valid TEST_ONLY binding the same composition yields exactly one executable proposal.
                ProjectRuleAuthorityBinding binding = ValidBinding();
                PlacementProposalRunResult granted = PlacementProposalRuntime.Build(
                    meterPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, new[] { binding }), governance, out PlacementProposalRunDiagnostics grantedDiagnostics);

                Assert(grantedDiagnostics.ProposalCount == 1, "The valid governed case did not produce exactly one proposal: " + grantedDiagnostics.ProposalCount);
                Assert(grantedDiagnostics.ExecutableViolatedCount == 1, "The valid governed case did not count one executable violation.");
                Assert(grantedDiagnostics.ViolatedCount > 0, "The valid governed case recorded no violated constraint.");
                Assert(granted.Proposals.Count == 1, "The valid governed case emitted " + granted.Proposals.Count + " proposals.");

                PlacementProposal proposal = granted.Proposals[0];
                Assert(proposal.ViolatedRule != null && proposal.ViolatedRule.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                    "The proposal did not carry the governed road rule identity.");
                Assert(proposal.RequiredDistanceMeters == 15m, "The proposal lost the 15 m criterion.");
                Assert(proposal.ObservedDistanceMeters > 9.5d && proposal.ObservedDistanceMeters < 10.5d, "The proposal lost the 10 m observed distance.");
                Assert(proposal.ProposedNorthing >= MetersToDrawingUnits(15d), "The proposal did not clear the required distance.");
                Assert(proposal.EngineeringReviewRequired, "A generated proposal stopped requiring engineering review.");

                // 6c. A mismatched binding through the same composition still yields zero.
                PlacementProposalRunResult mismatched = PlacementProposalRuntime.Build(
                    meterPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, new[] { With(binding, authorityClass: StandardAuthorityClasses.ActivePrimary) }),
                    governance, out PlacementProposalRunDiagnostics mismatchedDiagnostics);
                Assert(mismatchedDiagnostics.ProposalCount == 0, "A mismatched binding produced a proposal through the ES_SUGGEST composition.");
            }
            finally
            {
                if (Directory.Exists(project)) Directory.Delete(project, true);
            }
        }

        // ---- extra. A governed-compatible value alone must NOT grant execution authority -----------------

        private static void GovernedCompatibleValueAloneGrantsNothing()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
            ProjectDistanceCriteria criteria = TestCriteria(Path.Combine(Path.GetTempPath(), "EngiSentry-BindingAuditValue-" + Guid.NewGuid().ToString("N")));

            // A fully governed-compatible resolution carrying an approved minimum distance, but NO authority
            // binding: the value must be adopted as the criterion while executability stays false.
            string roadRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId;
            var governed = new Dictionary<string, ProjectDistanceGovernedRuleResolution>(StringComparer.Ordinal)
            {
                {
                    roadRuleId,
                    new ProjectDistanceGovernedRuleResolution
                    {
                        State = ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible,
                        ProjectRuleId = roadRuleId,
                        AuthorityState = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic,
                        GovernanceProvenance = new ProjectDistanceGovernanceProvenance
                        {
                            ProjectRuleId = roadRuleId,
                            ApprovedMinimumDistanceMeters = 18m,
                            AuthorityState = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic
                        }
                    }
                }
            };

            List<EngineeringDistanceRuleDefinition> rules = ProjectDistanceRuleBinder.Create(criteria).ToList();
            List<StandardsPackRule> projected = StandardsPackProjectionBuilder.Build(context, rules, governed).ToList();
            StandardsPackRule road = projected.First(x => x.RuleId == roadRuleId);

            Assert(road.MinimumDistanceMeters == 18m, "The governed-compatible approved value was not adopted as the criterion.");
            Assert(!road.AutoExecutionAllowed, "A governed-compatible value alone granted execution authority.");
            Assert(road.DecisionState == EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic, "The governed authority state was not recorded.");
            Assert(projected.All(x => !x.AutoExecutionAllowed), "A governed-compatible value left some rule executable.");

            // Adding the valid binding on top of the same governed value DOES grant, and keeps the approved value.
            List<StandardsPackRule> withBinding = StandardsPackProjectionBuilder.Build(context, rules, governed, authorityBindings: new[] { ValidBinding() }).ToList();
            StandardsPackRule roadWithBinding = withBinding.First(x => x.RuleId == roadRuleId);
            Assert(roadWithBinding.AutoExecutionAllowed, "A valid binding did not grant authority over a governed-compatible value.");
            Assert(roadWithBinding.MinimumDistanceMeters == 18m, "The binding path lost the approved governed criterion.");
        }

        // ---- layering safety -----------------------------------------------------------------------------

        private static void NoHostTypedMembersOrHostReference()
        {
            Assert(NoHostTypedMembers(typeof(ProjectRuleAuthorityBinding)), "ProjectRuleAuthorityBinding exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(ProjectRuleAuthorityBindingValidationResult)), "The binding validation result exposes a host-typed member.");
            Assert(!typeof(ProjectRuleAuthorityBindingValidator).Assembly.GetName().Name!.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase),
                "The binding validator left the standards assembly.");
            Assert(typeof(ProjectRuleAuthorityBindingValidator).Assembly == typeof(StandardsPackProjectionBuilder).Assembly,
                "The binding validator left the accepted standards assembly.");
            Assert(ReferencesAssembly(typeof(StandardsPackProjectionBuilder).Assembly, "EngiSentry.Contracts"),
                "The standards projection no longer references the accepted contracts.");
        }

        // ---- TEST_ONLY deterministic authority context, composed through the REAL authority chain ----------

        private static PlotPlanStandardsAuthorityRuntimeContext DeterministicTestAuthorityContext()
        {
            StandardSourceDefinition source = new StandardSourceDefinition
            {
                SourceId = TestSourceId,
                DocumentNumber = "TEST-GOV-001",
                Title = "TEST_ONLY governed spacing authority",
                Issuer = "TEST_ONLY audit harness",
                RevisionOrEdition = "1",
                SourceFileName = "test-governed.pdf",
                SourceSha256 = TestSourceSha256
            };
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { source });

            StandardClauseDefinition clause = new StandardClauseDefinition
            {
                ClauseId = TestClauseId,
                SourceId = TestSourceId,
                ClauseReference = "1",
                ClauseText = "TEST_ONLY deterministic minimum equipment-to-road clearance.",
                PageNumber = 1
            };
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { clause });

            StandardAuthorityPolicyRegistry policies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[]
            {
                new StandardAuthorityPolicyDefinition
                {
                    PolicyId = TestPolicyId,
                    SourceId = TestSourceId,
                    ClauseId = TestClauseId,
                    AuthorityClass = StandardAuthorityClasses.GoverningProject,
                    EnforcementMode = StandardEnforcementModes.Deterministic,
                    Rationale = "TEST_ONLY: explicit deterministic governing authority used only by the audit harness."
                }
            });

            StandardRulePackDefinition pack = new StandardRulePackDefinition
            {
                PackId = "test.governed.pack",
                Name = "TEST_ONLY governed pack",
                Rules = new List<StandardRuleDefinition>
                {
                    new StandardRuleDefinition { RuleId = TestAuthorityRuleId, Title = "TEST_ONLY governed road clearance", ClauseIds = new List<string> { TestClauseId } }
                }
            };
            StandardRulePackResolution ruleResolution = StandardRulePackResolver.Resolve(
                StandardRulePackRegistry.Create(clauses, new[] { pack }),
                new[] { new StandardRulePackPrecedenceDefinition { PackId = "test.governed.pack", Precedence = 10 } });

            StandardAuthorityResolution authorityResolution = StandardAuthorityResolver.Resolve(ruleResolution, clauses, policies);
            StandardExecutionGateResolution gate = StandardAuthorityExecutionGate.Evaluate(authorityResolution);
            StandardAuthorityReviewProjection review = StandardAuthorityReviewProjector.Create(authorityResolution, sources, clauses);

            // The deterministic TEST_ONLY gate decision must genuinely exist and be auto-executable.
            StandardRuleExecutionGateDecision? gateDecision = gate.Rules.FirstOrDefault(x => string.Equals(x.RuleId, TestAuthorityRuleId, StringComparison.Ordinal));
            Assert(gateDecision != null && gateDecision.AutoExecutionAllowed && gateDecision.EffectiveEnforcementMode == StandardEnforcementModes.Deterministic,
                "The TEST_ONLY deterministic authority chain did not produce an auto-executable deterministic gate decision.");

            return new PlotPlanStandardsAuthorityRuntimeContext(sources, clauses, ruleResolution, policies, authorityResolution, gate, review);
        }

        /// <summary>A binding whose recorded evidence matches the TEST_ONLY deterministic chain exactly.</summary>
        private static ProjectRuleAuthorityBinding ValidBinding() => new ProjectRuleAuthorityBinding
        {
            ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
            AuthorityRuleId = TestAuthorityRuleId,
            ClauseIds = new List<string> { TestClauseId },
            PolicyId = TestPolicyId,
            SourceId = TestSourceId,
            SourceSha256 = TestSourceSha256,
            AuthorityClass = StandardAuthorityClasses.GoverningProject,
            EffectiveEnforcementMode = StandardEnforcementModes.Deterministic,
            Rationale = "TEST_ONLY: explicit evidence-backed binding for the audit harness."
        };

        /// <summary>Clones a TEST_ONLY binding with optional overrides (the contract is a class, not a record).</summary>
        private static ProjectRuleAuthorityBinding With(ProjectRuleAuthorityBinding b,
            string? projectRuleId = null, string? authorityRuleId = null, string? authorityClass = null,
            string? enforcementMode = null, string? sourceId = null, string? sourceSha256 = null,
            string? policyId = null, IList<string>? clauseIds = null, string? approvalRecordId = null)
        {
            return new ProjectRuleAuthorityBinding
            {
                ProjectRuleId = projectRuleId ?? b.ProjectRuleId,
                AuthorityRuleId = authorityRuleId ?? b.AuthorityRuleId,
                ClauseIds = clauseIds == null ? b.ClauseIds : new List<string>(clauseIds),
                PolicyId = policyId ?? b.PolicyId,
                SourceId = sourceId ?? b.SourceId,
                SourceSha256 = sourceSha256 ?? b.SourceSha256,
                AuthorityClass = authorityClass ?? b.AuthorityClass,
                EffectiveEnforcementMode = enforcementMode ?? b.EffectiveEnforcementMode,
                ApprovalRecordId = approvalRecordId ?? b.ApprovalRecordId,
                AuthorityState = b.AuthorityState,
                Rationale = b.Rationale
            };
        }

        private static IEnumerable<ProjectRuleAuthorityBinding> MismatchedBindings()
        {
            yield return With(ValidBinding(), authorityRuleId: "authority.does.not.exist");
            yield return With(ValidBinding(), authorityClass: StandardAuthorityClasses.ActivePrimary);
            yield return With(ValidBinding(), enforcementMode: StandardEnforcementModes.ConditionalReview);
            yield return With(ValidBinding(), sourceId: "some.other.source");
            yield return With(ValidBinding(), sourceSha256: "B".PadLeft(64, 'B'));
            yield return With(ValidBinding(), policyId: "some.other.policy");
            yield return With(ValidBinding(), clauseIds: new List<string> { "clause.never.resolved" });
        }

        private static void AssertInvalid(ProjectRuleAuthorityBinding binding, PlotPlanStandardsAuthorityRuntimeContext context, string message)
        {
            ProjectRuleAuthorityBindingValidationResult result = ProjectRuleAuthorityBindingValidator.Validate(binding, context);
            Assert(!result.IsValid, message + " (" + string.Join("; ", result.Reasons) + ")");
            Assert(result.Reasons.Count > 0, "An invalid binding recorded no reason.");
        }

        // ---- projection helpers -----------------------------------------------------------------------------

        private static StandardsPackRule ProjectRoadRule(PlotPlanStandardsAuthorityRuntimeContext context, IReadOnlyList<ProjectRuleAuthorityBinding>? authorityBindings,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId = null,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult>? approvalAuthorityByRuleId = null)
        {
            ProjectDistanceCriteria criteria = TestCriteria(Path.Combine(Path.GetTempPath(), "EngiSentry-BindingAudit-" + Guid.NewGuid().ToString("N")));
            return StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, currentnessByRuleId, approvalAuthorityByRuleId, null, authorityBindings)
                .First(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId);
        }

        private static List<StandardsPackRule> ProjectAllRules(PlotPlanStandardsAuthorityRuntimeContext context, IReadOnlyList<ProjectRuleAuthorityBinding>? authorityBindings)
        {
            ProjectDistanceCriteria criteria = TestCriteria(Path.Combine(Path.GetTempPath(), "EngiSentry-BindingAudit-" + Guid.NewGuid().ToString("N")));
            return StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, null, null, null, authorityBindings).ToList();
        }

        private static ProjectDistanceCriteria TestCriteria(string projectDirectory, decimal roadMinimumMeters = 15m)
        {
            return new ProjectDistanceCriteria
            {
                SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion,
                ProjectDirectory = projectDirectory,
                AccessRoadEdgeMinimumMeters = roadMinimumMeters,
                SecurityFenceMinimumMeters = 12m,
                UnitBoundaryMinimumMeters = 9m
            };
        }

        // ---- plot-plan fixture (one violated road placement) -------------------------------------------------

        private static PlotPlanModel PlotPlanWithOneViolatedRoadPlacement(double observedMeters)
        {
            PlotPlanModel model = new PlotPlanModel
            {
                Drawing = new SemanticDrawing { FileName = "synthetic.dwg", InsertionUnits = InsertionUnits },
                Geometry = FullGeometry(),
                Measurements = new PlotSpatialMeasurementModel()
            };
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurement(observedMeters));
            model.EquipmentPlacements.Add(PlacementFor(PlacementId));
            return model;
        }

        private static PlotGeometryModel FullGeometry()
        {
            var model = new PlotGeometryModel();
            model.Primitives.Add(HorizontalPrimitive(RoadHandle, 0d, -1000d, 1000d));
            model.Primitives.Add(HorizontalPrimitive(FenceHandle, 40d, -1000d, 1000d));
            return model;
        }

        private static PlotGeometryPrimitive HorizontalPrimitive(string handle, double northingMeters, double westMeters, double eastMeters)
        {
            return new PlotGeometryPrimitive
            {
                SourceHandle = handle,
                SourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                Closed = false,
                Vertices = new List<PlotGeometryVertex>
                {
                    new PlotGeometryVertex { X = MetersToDrawingUnits(westMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d },
                    new PlotGeometryVertex { X = MetersToDrawingUnits(eastMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d }
                }
            };
        }

        private static EquipmentPlacement PlacementFor(string placementId)
        {
            return new EquipmentPlacement
            {
                PlacementId = placementId,
                EquipmentEntityId = EquipmentEntityId,
                TagSourceHandle = "tag:equipment-1",
                PlacementStatus = "Complete",
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 100m },
                Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 10m }
            };
        }

        private static EquipmentSpatialMeasurement RoadMeasurement(double observedMeters)
        {
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = "measurement:road-1",
                PlacementId = PlacementId,
                EquipmentEntityId = EquipmentEntityId,
                NormalizedTag = NormalizedTag,
                EquipmentEasting = Convert.ToDecimal(MetersToDrawingUnits(EquipmentEastingMeters), CultureInfo.InvariantCulture),
                EquipmentNorthing = Convert.ToDecimal(MetersToDrawingUnits(observedMeters), CultureInfo.InvariantCulture),
                ConstraintId = "bounded-constraint:" + ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                SubjectKind = "Road",
                LabelSourceHandle = RoadHandle + ":label",
                GeometrySourceHandle = RoadHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                ClosestPointX = MetersToDrawingUnits(EquipmentEastingMeters),
                ClosestPointY = MetersToDrawingUnits(0d),
                Distance2D = MetersToDrawingUnits(observedMeters),
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
            };
        }

        private static double MetersToDrawingUnits(double meters) => meters / MetersPerDrawingUnit;

        // ---- shared layering helpers --------------------------------------------------------------------------

        private static bool NoHostTypedMembers(Type type)
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties())
                if (IsHostAssembly(property.PropertyType)) return false;
            foreach (System.Reflection.FieldInfo field in type.GetFields())
                if (IsHostAssembly(field.FieldType)) return false;
            return true;
        }

        private static bool IsHostAssembly(Type type)
        {
            string? name = type.Assembly.GetName().Name;
            return !string.IsNullOrEmpty(name) && (name.StartsWith("ac", StringComparison.Ordinal) || name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ReferencesAssembly(System.Reflection.Assembly assembly, string targetName)
        {
            return assembly.GetReferencedAssemblies().Any(r => (r.Name ?? string.Empty) == targetName);
        }

        private static int _assertions;

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
