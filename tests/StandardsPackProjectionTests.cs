using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Phase C tests: the governed bounded CHECK path, PlantModel v1 → existing P6/P7/P8/P9 governance →
    /// StandardsPackRule → BoundedConstraint → accepted EngineeringDistanceRuleEvaluator semantics →
    /// read-only CHECK result, scoped to Equipment → Road, Equipment → Plant Fence and Equipment →
    /// Unit Boundary.
    ///
    /// Every fixture in this class is synthetic and stays inside this test class. No production catalog,
    /// source, clause, approval, authority, hash or distance is created or altered, and no company standard
    /// is named or remembered. Synthetic numerical values are permitted here only because they never reach a
    /// production pack. Follows the existing harness convention (internal static Run + Assert).
    /// </summary>
    internal static class StandardsPackProjectionTests
    {
        private const string ProjectDirectory = @"C:\PROJECT";
        private const string ProjectClauseId = "trc.pcd-pd-135.3.3.9.a";
        private const string ProjectSourceId = "trc.pcd-pd-135.rev1";
        private const string ProjectSourceSha256 = "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E";

        internal static void Run()
        {
            ContractSafetyAndLayering();
            ProjectionPreservesProvenance();
            GovernanceStaysBlocked();
            ProjectRuleOnlyPath();
            BoundedRelationshipMapping();
            CheckStateBehavior();
            Determinism();
            NonRegressionSafety();
        }

        // ---- 1. Contract safety and dependency layering ---------------------------------------------

        private static void ContractSafetyAndLayering()
        {
            // New Phase C contract types carry no host-typed members.
            Assert(NoHostTypedMembers(typeof(BoundedConstraint)), "BoundedConstraint exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(BoundedConstraintSet)), "BoundedConstraintSet exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(BoundedConstraintDiagnostics)), "BoundedConstraintDiagnostics exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(BoundedConstraintCheck)), "BoundedConstraintCheck exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(BoundedCheckResult)), "BoundedCheckResult exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(BoundedCheckDiagnostics)), "BoundedCheckDiagnostics exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(StandardsPackGroup)), "StandardsPackGroup exposes a host-typed member.");

            // Neither Phase C assembly may reference a host/Autodesk assembly.
            Assert(NoHostReference(typeof(BoundedCheckEvaluator).Assembly), "EngiSentry.Rules references a host/Autodesk assembly.");
            Assert(NoHostReference(typeof(StandardsPackProjectionBuilder).Assembly), "EngiSentry.Standards references a host/Autodesk assembly.");

            // Dependency direction: the constraint engine never references the standards projection, and the
            // projection never references the constraint engine, so no circular project reference exists.
            Assert(!ReferencesAssembly(typeof(BoundedCheckEvaluator).Assembly, "EngiSentry.Standards"),
                "EngiSentry.Rules references EngiSentry.Standards, reversing the accepted dependency direction.");
            Assert(!ReferencesAssembly(typeof(StandardsPackProjectionBuilder).Assembly, "EngiSentry.Rules"),
                "EngiSentry.Standards references EngiSentry.Rules, creating a circular project reference.");
            Assert(ReferencesAssembly(typeof(StandardsPackProjectionBuilder).Assembly, "EngiSentry.Contracts"),
                "EngiSentry.Standards no longer references EngiSentry.Contracts.");
            Assert(ReferencesAssembly(typeof(BoundedCheckEvaluator).Assembly, "EngiSentry.Contracts"),
                "EngiSentry.Rules no longer references EngiSentry.Contracts.");
        }

        // ---- 2. Governed projection preserves provenance --------------------------------------------

        private static void ProjectionPreservesProvenance()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();

            // The accepted authority/gate/review baseline is what the projection consumes; Phase C does not
            // alter it, and the projection must not add execution authority that the gate did not grant.
            Assert(context.ExecutionGate.Rules.Count == 4, "The accepted authority gate no longer exposes its 4 rule decisions.");
            Assert(context.ExecutionGate.Rules.All(g => !g.AutoExecutionAllowed),
                "The accepted authority gate now allows auto-execution, changing existing governance behavior.");
            Assert(context.AuthorityResolution.Rules.Count == 4, "The accepted authority resolution no longer exposes its 4 rule resolutions.");

            IReadOnlyList<EngineeringDistanceRuleDefinition> authorityRules = context.RuleResolution.EffectiveRules
                .OrderBy(e => e.RuleId, StringComparer.Ordinal)
                .Select(e => SyntheticRuleDefinition(e.RuleId, EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m))
                .ToList();

            IReadOnlyList<StandardsPackRule> projected = StandardsPackProjectionBuilder.Build(context, authorityRules);

            Assert(projected.Count == authorityRules.Count, "The governed projection dropped or duplicated authority rules.");
            Assert(projected.Select(x => x.RuleId).SequenceEqual(authorityRules.Select(x => x.RuleId), StringComparer.Ordinal),
                "The governed projection is not deterministically ordered by RuleId.");

            foreach (StandardsPackRule rule in projected)
            {
                StandardRuleAuthorityResolution authority = context.AuthorityResolution.Rules
                    .First(x => string.Equals(x.RuleId, rule.RuleId, StringComparison.Ordinal));

                // Clause and source-hash provenance flow through verbatim from the authority resolution.
                Assert(rule.Clauses.Count == authority.Clauses.Count, "Clause provenance was altered for rule " + rule.RuleId + ".");
                Assert(rule.SourceSha256 == (authority.Clauses.Count > 0 ? authority.Clauses[0].SourceSha256 : null),
                    "Source hash provenance was altered for rule " + rule.RuleId + ".");

                // Company is metadata only and must never be synthesized when no registered pack name exists.
                Assert(rule.Company == null, "Company provenance was synthesized for rule " + rule.RuleId + ".");

                // Nothing in the accepted chain is auto-executable today, so nothing it projects may become so.
                Assert(!rule.AutoExecutionAllowed, "A projected authority rule became auto-executable: " + rule.RuleId + ".");
                Assert(rule.EngineeringReviewRequired, "A projected authority rule stopped requiring engineering review: " + rule.RuleId + ".");
                Assert(rule.MinimumDistanceMeters == 15m, "The governed projection altered the criterion for rule " + rule.RuleId + ".");
            }

            // A quarantined source in the accepted chain must surface as review-required, never executable.
            StandardsPackRule quarantined = projected.FirstOrDefault(x => x.AuthorityClass == StandardAuthorityClasses.Quarantined);
            Assert(quarantined != null, "The accepted authority chain no longer contains a quarantined source.");
            Assert(quarantined!.ConstraintTreatment == ConstraintTreatment.ReviewRequired,
                "A quarantined source was not projected as review-required: " + quarantined.RuleId + ".");
            Assert(BoundedRelationship.IsSupported(quarantined!.MeasurementBasisKey),
                "A quarantined rule lost its bounded measurement basis: " + quarantined.RuleId + ".");

            // Pack grouping keeps pack identity explicit and rules deterministically ordered within a group.
            IReadOnlyList<StandardsPackGroup> groups = StandardsPackPackCatalog.Group(projected);
            Assert(groups.Count >= 1, "Pack grouping produced no groups.");
            foreach (StandardsPackGroup group in groups)
            {
                Assert(!string.IsNullOrEmpty(group.PackId), "A pack group lost its pack identity.");
                Assert(group.Rules.Count > 0, "A pack group is empty.");
                Assert(group.Rules.Select(x => x.RuleId).SequenceEqual(group.Rules.Select(x => x.RuleId).OrderBy(y => y, StringComparer.Ordinal), StringComparer.Ordinal),
                    "Pack group rules are not deterministically ordered.");
            }

            // The bounded projection reports only bounded rules; nothing is silently repurposed.
            Assert(StandardsPackPackCatalog.BoundedRules(projected).Count == projected.Count,
                "The bounded projection did not surface every bounded relationship rule.");
        }

        // ---- 3. Governance stays blocked -------------------------------------------------------------

        private static void GovernanceStaysBlocked()
        {
            string road = EngineeringDistanceEvidenceKeys.AccessRoadEdge;
            // Evidence is present for every case below, so any block is caused by governance, not by
            // missing measurements.
            PlotSpatialMeasurementModel measurements = Measurements((road, 30.0, "road-1"));

            AssertBlocked(NonExecutableRule("missing-authority", road, 15m, decisionState: null), measurements,
                "A rule with no authority produced a CHECK verdict.");
            AssertBlocked(NonExecutableRule("expert-review-required", road, 15m, decisionState: StandardDecisionStates.ExpertReviewRequired), measurements,
                "An expert-review-required rule produced a CHECK verdict.");
            AssertBlocked(NonExecutableRule("quarantined-source", road, 15m, decisionState: StandardDecisionStates.SourceQuarantined), measurements,
                "A quarantined source produced a CHECK verdict.");
            AssertBlocked(NonExecutableRule("stale-approval", road, 15m, decisionState: EngineeringDistanceApprovalAuthorityStates.StaleApproval), measurements,
                "A stale approval produced a CHECK verdict.");
            // A P8 human-review acceptance is not a P9 engineering approval and must never execute.
            AssertBlocked(NonExecutableRule("p8-review-accepted", road, 15m, decisionState: "REVIEW_ACCEPTED"), measurements,
                "A P8 REVIEW_ACCEPTED state substituted for P9 approval and produced a CHECK verdict.");

            // The existing P9 resolver physically refuses an unknown/P8-only authority state rather than
            // silently promoting it to governing authority.
            string projectRuleId = "gov.p8.rejected";
            EngineeringDistanceRuleDefinition projectRule = SyntheticRuleDefinition(projectRuleId, road, 15m);
            EngineeringDistanceCandidateDefinition candidate = SyntheticCandidate("cand-p8", projectRuleId, road, 15m);
            EngineeringDistanceApprovalRecord approval = SyntheticApprovalRecord("ap-p8", candidate);
            AssertThrows<ProjectDistanceGovernedRuleResolverValidationException>(() =>
                Resolve(projectRule, candidate, approval, "REVIEW_ACCEPTED"),
                "A P8 REVIEW_ACCEPTED state was accepted as a governing authority state.");

            // A reviewed-but-not-approved P9 state resolves to PROJECT_RULE_ONLY with no governed provenance.
            ProjectDistanceGovernedRuleResolution notApproved = Resolve(projectRule, candidate, approval, EngineeringDistanceApprovalAuthorityStates.NotApproved);
            Assert(notApproved.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly,
                "A NOT_APPROVED authority produced a governed state instead of PROJECT_RULE_ONLY.");
            Assert(notApproved.GovernanceProvenance == null, "A NOT_APPROVED authority supplied governed provenance.");

            // Stale and quarantined authority states are likewise blocked, never promoted.
            Assert(Resolve(projectRule, candidate, approval, EngineeringDistanceApprovalAuthorityStates.StaleApproval).State
                == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly, "A STALE_APPROVAL state was promoted out of PROJECT_RULE_ONLY.");
            Assert(Resolve(projectRule, candidate, approval, EngineeringDistanceApprovalAuthorityStates.SourceQuarantined).State
                == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly, "A SOURCE_QUARANTINED state was promoted out of PROJECT_RULE_ONLY.");
        }

        // ---- 4. PROJECT_RULE_ONLY path through existing P9 governance --------------------------------

        private static void ProjectRuleOnlyPath()
        {
            PlotPlanStandardsAuthorityRuntimeContext context = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            string road = EngineeringDistanceEvidenceKeys.AccessRoadEdge;
            string projectRuleId = "pc.equipment.road";

            // 4a. A project criterion with no approval and no authority stays PROJECT_RULE_ONLY and carries
            //     no governed provenance: project provenance, explicitly never a company standard.
            EngineeringDistanceRuleDefinition projectRule = SyntheticRuleDefinition(projectRuleId, road, 15m);
            EngineeringDistanceCandidateDefinition candidate = SyntheticCandidate("cand-project", projectRuleId, road, 15m);
            ProjectDistanceGovernedRuleResolution unresolved = ProjectDistanceGovernedRuleResolver.Resolve(projectRule, candidate, null, null);
            Assert(unresolved.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly,
                "An unapproved project rule did not resolve to PROJECT_RULE_ONLY.");
            Assert(unresolved.GovernanceProvenance == null, "An unapproved project rule gained governed provenance.");
            Assert(string.IsNullOrEmpty(unresolved.AuthorityState), "An unapproved project rule gained an authority state.");

            // 4b. A genuine P9 GOVERNING_PROJECT_DETERMINISTIC approval supplies provenance and the approved
            //     criterion; the projection carries it through without inventing a distance.
            EngineeringDistanceApprovalRecord deterministicApproval = SyntheticApprovalRecord("ap-deterministic", candidate);
            ProjectDistanceGovernedRuleResolution approved = Resolve(projectRule, candidate, deterministicApproval,
                EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic);
            Assert(approved.State == ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible,
                "A matching P9 deterministic approval did not resolve to GOVERNED_COMPATIBLE.");
            Assert(approved.GovernanceProvenance != null, "A matching P9 deterministic approval supplied no governed provenance.");
            Assert(approved.GovernanceProvenance!.ApprovedMinimumDistanceMeters == 15m,
                "The governed provenance did not carry the approved criterion.");
            Assert(approved.GovernanceProvenance!.AuthorityState == EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic,
                "The governed provenance lost its authority state.");

            var governedResolutions = new Dictionary<string, ProjectDistanceGovernedRuleResolution> { { projectRuleId, approved } };
            IReadOnlyList<StandardsPackRule> projected = StandardsPackProjectionBuilder.Build(context, new[] { projectRule }, governedResolutions);
            StandardsPackRule row = projected.First(x => x.RuleId == projectRuleId);
            Assert(row.MinimumDistanceMeters == 15m, "The approved governed criterion was not projected.");
            Assert(row.ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "A PROJECT_RULE_ONLY rule lost its PROJECT_REQUIREMENT treatment.");
            Assert(row.Company == null, "A project rule was presented with company provenance.");
            Assert(row.PackId == null, "A project rule was presented with company pack identity.");
            // Even with an approved criterion, execution authority still comes only from the authority chain.
            Assert(!row.AutoExecutionAllowed, "An approved project rule gained execution authority from the projection.");

            // 4c. A conditional governing approval supplies a criterion that differs from the project rule's
            //     configured value; the projection adopts the approved value rather than inventing one.
            EngineeringDistanceRuleDefinition divergentRule = SyntheticRuleDefinition("pc.equipment.divergent", road, 12m);
            EngineeringDistanceCandidateDefinition divergentCandidate = SyntheticCandidate("cand-divergent", "pc.equipment.divergent", road, 18m);
            EngineeringDistanceApprovalRecord conditionalApproval = SyntheticApprovalRecord("ap-conditional", divergentCandidate);
            ProjectDistanceGovernedRuleResolution conditional = Resolve(divergentRule, divergentCandidate, conditionalApproval,
                EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional);
            Assert(conditional.State == ProjectDistanceGovernedRuleResolutionStates.GovernedConditional,
                "A conditional governing approval did not resolve to GOVERNED_CONDITIONAL.");
            Assert(conditional.GovernanceProvenance!.ApprovedMinimumDistanceMeters == 18m,
                "The conditional governed provenance did not carry the approved criterion.");
            var conditionalResolutions = new Dictionary<string, ProjectDistanceGovernedRuleResolution> { { divergentRule.RuleId, conditional } };
            StandardsPackRule conditionalRow = StandardsPackProjectionBuilder.Build(context, new[] { divergentRule }, conditionalResolutions)
                .First(x => x.RuleId == divergentRule.RuleId);
            Assert(conditionalRow.MinimumDistanceMeters == 18m,
                "The projection kept the configured value instead of the approved governed criterion.");
            Assert(conditionalRow.ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "A conditionally governed project rule lost its PROJECT_REQUIREMENT treatment.");

            // 4d. PROJECT_RULE_ONLY provenance stays explicit in the pack catalog: project criteria are
            //     grouped under the project-criteria pack, never under a company-standard pack.
            IReadOnlyList<StandardsPackRule> bounded = StandardsPackPackCatalog.BoundedRules(projected);
            Assert(bounded.Count == 1, "The bounded projection did not surface the project rule.");
            StandardsPackGroup group = StandardsPackPackCatalog.Group(bounded)[0];
            Assert(group.PackId == PackIds.ProjectCriteria,
                "A project rule was not grouped under the explicit project-criteria pack.");
            Assert(group.Rules[0].ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "A project rule lost its PROJECT_REQUIREMENT treatment during grouping.");

            // 4e. When the existing authority chain does mark a project rule auto-executable, the bounded
            //     CHECK executes and still reports PROJECT_REQUIREMENT provenance.
            StandardsPackRule executable = ExecutableRule("pc.equipment.executable", road, 15m);
            Assert(executable.ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "An executable project rule lost its PROJECT_REQUIREMENT provenance.");
            PlotSpatialMeasurementModel satisfied = Measurements((road, 20.0, "road-1"));
            BoundedConstraintSet set = BoundedConstraintBuilder.Build(new[] { executable }, out BoundedConstraintDiagnostics cd);
            Assert(set.Constraints.Count == 1, "An executable project rule produced no bounded constraint.");
            Assert(set.Constraints[0].ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "An executable project rule changed treatment during constraint construction.");
            Assert(cd.ExecutableConstraintCount == 1, "The diagnostics did not count the executable project rule.");
            BoundedCheckResult result = BoundedCheckEvaluator.Evaluate(set.Constraints, satisfied, out BoundedCheckDiagnostics ed);
            Assert(result.Checks.Count == 1, "An executable project rule produced no CHECK result.");
            Assert(result.Checks[0].State == ProposalConstraintState.Satisfied,
                "An executable PROJECT_RULE_ONLY rule did not produce a CHECK verdict.");
            Assert(result.Checks[0].ConstraintTreatment == ConstraintTreatment.ProjectRequirement,
                "The CHECK result lost the PROJECT_REQUIREMENT provenance.");
        }

        // ---- 5. Bounded relationship mapping ----------------------------------------------------------

        private static void BoundedRelationshipMapping()
        {
            // The bounded vocabulary is closed and explicit.
            Assert(BoundedRelationship.EquipmentToRoad == "EquipmentToRoad", "The EquipmentToRoad relationship constant changed.");
            Assert(BoundedRelationship.EquipmentToPlantFence == "EquipmentToPlantFence", "The EquipmentToPlantFence relationship constant changed.");
            Assert(BoundedRelationship.EquipmentToUnitBoundary == "EquipmentToUnitBoundary", "The EquipmentToUnitBoundary relationship constant changed.");

            // Both accepted road bases resolve to Equipment → Road.
            Assert(BoundedRelationship.TryResolve(EngineeringDistanceEvidenceKeys.AccessRoadEdge) == BoundedRelationship.EquipmentToRoad,
                "AccessRoadEdge did not map to EquipmentToRoad.");
            Assert(BoundedRelationship.TryResolve(EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge) == BoundedRelationship.EquipmentToRoad,
                "UnitPeripheralRoadEdge did not map to EquipmentToRoad.");
            Assert(BoundedRelationship.TryResolve(EngineeringDistanceEvidenceKeys.SecurityFence) == BoundedRelationship.EquipmentToPlantFence,
                "SecurityFence did not map to EquipmentToPlantFence.");
            Assert(BoundedRelationship.TryResolve(EngineeringDistanceEvidenceKeys.UnitBoundary) == BoundedRelationship.EquipmentToUnitBoundary,
                "UnitBoundary did not map to EquipmentToUnitBoundary.");

            Assert(BoundedRelationship.IsSupported(EngineeringDistanceEvidenceKeys.AccessRoadEdge), "AccessRoadEdge is not a bounded basis.");
            Assert(BoundedRelationship.IsSupported(EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge), "UnitPeripheralRoadEdge is not a bounded basis.");
            Assert(BoundedRelationship.IsSupported(EngineeringDistanceEvidenceKeys.SecurityFence), "SecurityFence is not a bounded basis.");
            Assert(BoundedRelationship.IsSupported(EngineeringDistanceEvidenceKeys.UnitBoundary), "UnitBoundary is not a bounded basis.");

            // Site boundary is an accepted measurement basis but is outside the bounded taxonomy; a reversed
            // relationship is not representable at all. Both must stay outside the bounded check.
            Assert(BoundedRelationship.TryResolve(EngineeringDistanceEvidenceKeys.SiteBoundary) == null,
                "SiteBoundary was folded into the bounded taxonomy.");
            Assert(!BoundedRelationship.IsSupported(EngineeringDistanceEvidenceKeys.SiteBoundary),
                "SiteBoundary was reported as a bounded relationship.");
            Assert(BoundedRelationship.TryResolve("RoadToEquipment") == null, "A reversed relationship was resolved.");
            Assert(BoundedRelationship.TryResolve("PlantFenceToEquipment") == null, "A reversed relationship was resolved.");
            Assert(BoundedRelationship.TryResolve(string.Empty) == null, "An empty basis resolved to a relationship.");
            Assert(BoundedRelationship.TryResolve(null) == null, "A null basis resolved to a relationship.");

            // End-to-end: each supported relationship maps through to a CHECK with the right relationship.
            AssertMapping(BoundedRelationship.EquipmentToRoad, EngineeringDistanceEvidenceKeys.AccessRoadEdge, "road-1");
            AssertMapping(BoundedRelationship.EquipmentToPlantFence, EngineeringDistanceEvidenceKeys.SecurityFence, "fence-1");
            AssertMapping(BoundedRelationship.EquipmentToUnitBoundary, EngineeringDistanceEvidenceKeys.UnitBoundary, "unit-1");

            // An unsupported relationship never reaches the CHECK: it is reported, not executed.
            StandardsPackRule siteRule = ExecutableRule("pc.site.boundary", EngineeringDistanceEvidenceKeys.SiteBoundary, 15m);
            BoundedConstraintSet siteSet = BoundedConstraintBuilder.Build(new[] { siteRule }, out BoundedConstraintDiagnostics siteDiagnostics);
            Assert(siteSet.Constraints.Count == 0, "An unsupported relationship produced a bounded constraint.");
            Assert(siteDiagnostics.UnsupportedRelationshipCount == 1, "An unsupported relationship was not counted in the diagnostics.");
            Assert(siteDiagnostics.DiagnosticReasons.Any(x => x.StartsWith("unsupported-relationship:", StringComparison.Ordinal)),
                "An unsupported relationship left no diagnostic reason.");

            // Fed directly to the evaluator, an out-of-taxonomy constraint is NOT_APPLICABLE and reported.
            BoundedConstraint outOfTaxonomy = new BoundedConstraint
            {
                ConstraintId = "bounded-constraint:out-of-taxonomy",
                Rule = siteRule,
                ConstraintTreatment = ConstraintTreatment.ProjectRequirement,
                RequiredMeasurementBasisKey = EngineeringDistanceEvidenceKeys.SiteBoundary,
                MinimumDistanceMeters = 15m
            };
            BoundedCheckResult siteCheck = BoundedCheckEvaluator.Evaluate(
                new[] { outOfTaxonomy }, Measurements((EngineeringDistanceEvidenceKeys.SiteBoundary, 5.0, "site-1")), out BoundedCheckDiagnostics siteCheckDiagnostics);
            Assert(siteCheck.Checks.Count == 1, "An out-of-taxonomy constraint vanished from the CHECK.");
            Assert(siteCheck.Checks[0].State == ProposalConstraintState.NotApplicable,
                "An out-of-taxonomy constraint produced a verdict instead of NOT_APPLICABLE.");
            Assert(siteCheck.Checks[0].Relationship == null, "An out-of-taxonomy constraint reported a relationship.");
            Assert(siteCheckDiagnostics.NotApplicableCount == 1, "The diagnostics did not count the not-applicable constraint.");
            Assert(siteCheckDiagnostics.SatisfiedCount == 0 && siteCheckDiagnostics.ViolatedCount == 0,
                "An out-of-taxonomy constraint contributed a numerical verdict.");
        }

        // ---- 6. CHECK state behavior ------------------------------------------------------------------

        private static void CheckStateBehavior()
        {
            string road = EngineeringDistanceEvidenceKeys.AccessRoadEdge;

            // Satisfied: observed strictly above the criterion.
            Assert(CheckState(ExecutableRule("r.satisfied", road, 15m), Measurements((road, 20.0, "road-1"))) == ProposalConstraintState.Satisfied,
                "A satisfied CHECK was not reported as SATISFIED.");
            // Violated: observed strictly below the criterion.
            Assert(CheckState(ExecutableRule("r.violated", road, 15m), Measurements((road, 10.0, "road-1"))) == ProposalConstraintState.Violated,
                "A violated CHECK was not reported as VIOLATED.");
            // Exact threshold: observed == criterion satisfies the constraint (>=), matching the accepted
            // PlotPlan measurement semantics. The boundary is not a violation.
            Assert(CheckState(ExecutableRule("r.exact", road, 15m), Measurements((road, 15.0, "road-1"))) == ProposalConstraintState.Satisfied,
                "An exact-threshold CHECK was not reported as SATISFIED.");
            // The minimum observed distance for the basis is the value compared.
            Assert(CheckState(ExecutableRule("r.minimum", road, 12m), Measurements((road, 30.0, "road-1"), (road, 10.0, "road-2"))) == ProposalConstraintState.Violated,
                "The CHECK did not compare the minimum observed distance for the basis.");

            // Satisfied and violated checks carry the observed distance and the portable evidence handles.
            BoundedCheckResult evidenceCheck = Check(ExecutableRule("r.evidence", road, 15m), Measurements((road, 20.0, "road-1")));
            Assert(evidenceCheck.Checks[0].ObservedDistanceMeters == 20.0, "A satisfied CHECK did not report the observed distance.");
            Assert(evidenceCheck.Checks[0].RequiredDistanceMeters == 15m, "A satisfied CHECK did not report the required distance.");
            Assert(evidenceCheck.Checks[0].EvidenceHandles.Count > 0, "A satisfied CHECK reported no evidence handles.");
            Assert(evidenceCheck.Checks[0].EvidenceHandles.SequenceEqual(evidenceCheck.Checks[0].EvidenceHandles.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal),
                "Evidence handles are not deterministically ordered.");

            // Insufficient evidence: no resolved measurement for the basis, so no verdict is invented.
            BoundedCheckResult empty = Check(ExecutableRule("r.insufficient", road, 15m), new PlotSpatialMeasurementModel());
            Assert(empty.Checks[0].State == ProposalConstraintState.InsufficientEvidence,
                "A CHECK with no evidence produced a verdict instead of INSUFFICIENT_EVIDENCE.");
            Assert(empty.Checks[0].ObservedDistanceMeters == null, "An insufficient-evidence CHECK reported an observed distance.");
            Assert(empty.Checks[0].Reason != null, "An insufficient-evidence CHECK reported no reason.");

            // Non-executable governance: a review-required rule never yields a numerical verdict, even when
            // full evidence is present.
            BoundedCheckResult blocked = Check(NonExecutableRule("r.nonexecutable", road, 15m, null), Measurements((road, 20.0, "road-1")));
            Assert(blocked.Checks[0].State == ProposalConstraintState.ReviewRequired,
                "A non-executable rule produced a verdict instead of REVIEW_REQUIRED.");
            Assert(blocked.Checks[0].ObservedDistanceMeters == null, "A non-executable CHECK reported an observed distance.");
        }

        // ---- 7. Determinism ---------------------------------------------------------------------------

        private static void Determinism()
        {
            string road = EngineeringDistanceEvidenceKeys.AccessRoadEdge;
            string fence = EngineeringDistanceEvidenceKeys.SecurityFence;

            StandardsPackRule[] rules =
            {
                ExecutableRule("pc.equipment.alpha", road, 15m),
                ExecutableRule("pc.equipment.bravo", road, 25m),
                ExecutableRule("pc.equipment.charlie", fence, 10m)
            };
            (string, double, string)[] evidence =
            {
                (road, 20.0, "road-a"),
                (road, 12.0, "road-b"),
                (fence, 30.0, "fence-a")
            };

            string first = Snapshot(rules, evidence);
            string second = Snapshot(rules, evidence);
            Assert(first == second, "Repeated identical input produced a different bounded CHECK.");

            // Reordered input must yield the identical snapshot: ordering is internal, never input-dependent.
            string reversed = Snapshot(Reverse(rules), Reverse(evidence));
            Assert(first == reversed, "Reordered input produced a different bounded CHECK.");

            // Pack grouping and bounded-rule selection are order-independent too.
            string firstGroup = GroupSnapshot(rules);
            string reversedGroup = GroupSnapshot(Reverse(rules));
            Assert(firstGroup == reversedGroup, "Reordered input produced a different pack grouping.");

            string firstBounded = BoundedSnapshot(rules);
            string reversedBounded = BoundedSnapshot(Reverse(rules));
            Assert(firstBounded == reversedBounded, "Reordered input produced a different bounded rule selection.");
        }

        // ---- 8. Non-regression safety ------------------------------------------------------------------

        private static void NonRegressionSafety()
        {
            // Accepted contract defaults are unchanged.
            var fresh = new StandardsPackRule();
            Assert(string.IsNullOrEmpty(fresh.RuleId), "A fresh StandardsPackRule claims a RuleId.");
            Assert(fresh.MinimumDistanceMeters == 0m, "A fresh StandardsPackRule carries a distance.");
            Assert(!fresh.AutoExecutionAllowed, "A fresh StandardsPackRule is auto-executable.");
            Assert(fresh.EngineeringReviewRequired, "A fresh StandardsPackRule does not require engineering review.");
            Assert(fresh.Clauses != null && fresh.Clauses.Count == 0, "A fresh StandardsPackRule cites clauses.");

            // The accepted registry contract still accepts and deterministically orders project criteria.
            EngineeringDistanceRuleRegistry registry = EngineeringDistanceRuleRegistry.CreateProjectCriteria(new[]
            {
                SyntheticRuleDefinition("pc.z.last", EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m),
                SyntheticRuleDefinition("pc.a.first", EngineeringDistanceEvidenceKeys.SecurityFence, 10m)
            });
            Assert(registry.Rules.Count == 2, "The accepted registry lost project criteria rules.");
            Assert(registry.Rules[0].RuleId == "pc.a.first" && registry.Rules[1].RuleId == "pc.z.last",
                "The accepted registry lost its deterministic ordering.");
            Assert(registry.TryGet("pc.a.first", out EngineeringDistanceRuleDefinition? found) && found != null,
                "The accepted registry lost rule lookup.");
            Assert(!registry.TryGet("pc.missing", out EngineeringDistanceRuleDefinition? missing) && missing == null,
                "The accepted registry resolved a missing rule.");

            // The accepted governance baseline is unchanged by the Phase C projection.
            PlotPlanStandardsAuthorityRuntimeContext context = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            Assert(context.ExecutionGate.Rules.Count(g => g.AutoExecutionAllowed) == 0,
                "Phase C changed the accepted auto-executable count.");
            Assert(context.ExecutionGate.Rules.Count == 4 && context.AuthorityResolution.Rules.Count == 4,
                "Phase C changed the accepted authority/gate cardinality.");
            Assert(context.ReviewProjection != null, "The accepted review projection was lost.");

            // Projecting an empty rule set yields an empty pack, never a fabricated criterion.
            IReadOnlyList<StandardsPackRule> emptyProjection = StandardsPackProjectionBuilder.Build(context, Array.Empty<EngineeringDistanceRuleDefinition>());
            Assert(emptyProjection.Count == 0, "An empty rule set produced governed pack rules.");
            Assert(StandardsPackPackCatalog.Group(null!).Count == 0, "A null rule set produced pack groups.");
            Assert(StandardsPackPackCatalog.BoundedRules(null!).Count == 0, "A null rule set produced bounded rules.");
            BoundedConstraintSet emptySet = BoundedConstraintBuilder.Build(Array.Empty<StandardsPackRule>(), out BoundedConstraintDiagnostics emptyDiagnostics);
            Assert(emptySet.Constraints.Count == 0 && emptyDiagnostics.ConstraintCount == 0, "An empty rule set produced bounded constraints.");
            BoundedCheckResult emptyCheck = BoundedCheckEvaluator.Evaluate(emptySet.Constraints, new PlotSpatialMeasurementModel(), out BoundedCheckDiagnostics emptyCheckDiagnostics);
            Assert(emptyCheck.Checks.Count == 0 && emptyCheckDiagnostics.ConstraintCount == 0, "An empty constraint set produced CHECK results.");

            // No Phase C assembly leaks a host reference.
            Assert(NoHostReference(typeof(BoundedCheckEvaluator).Assembly), "EngiSentry.Rules references a host/Autodesk assembly.");
            Assert(NoHostReference(typeof(StandardsPackProjectionBuilder).Assembly), "EngiSentry.Standards references a host/Autodesk assembly.");
        }

        // ---- Fixtures and helpers -----------------------------------------------------------------------

        private static string MeasurementKindFor(string basisKey)
        {
            return basisKey == EngineeringDistanceEvidenceKeys.SecurityFence || basisKey == EngineeringDistanceEvidenceKeys.UnitBoundary
                ? "EquipmentToBoundary2D"
                : "EquipmentToRoad2D";
        }

        private static string CriteriaSettingKeyFor(string basisKey)
        {
            if (basisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge) return "AccessRoadEdgeMinimumMeters";
            if (basisKey == EngineeringDistanceEvidenceKeys.SecurityFence) return "SecurityFenceMinimumMeters";
            if (basisKey == EngineeringDistanceEvidenceKeys.UnitBoundary) return "UnitBoundaryMinimumMeters";
            return basisKey + "MinimumMeters";
        }

        private static EngineeringDistanceRuleDefinition SyntheticRuleDefinition(string ruleId, string basisKey, decimal minimum)
        {
            return new EngineeringDistanceRuleDefinition
            {
                RuleId = ruleId,
                MeasurementKind = MeasurementKindFor(basisKey),
                MinimumDistanceMeters = minimum,
                RequiredMeasurementBasisKey = basisKey,
                RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                CriteriaProvenance = new EngineeringDistanceCriteriaProvenance
                {
                    SourceKind = "ProjectDistanceCriteria",
                    ProjectDirectory = ProjectDirectory,
                    CriteriaSettingKey = CriteriaSettingKeyFor(basisKey),
                    SchemaVersion = 1,
                    ConfiguredMinimumDistanceMeters = minimum
                }
            };
        }

        /// <summary>A governed pack rule that is not auto-executable and therefore must not produce a verdict.</summary>
        private static StandardsPackRule NonExecutableRule(string ruleId, string basisKey, decimal minimum, string? decisionState)
        {
            return new StandardsPackRule
            {
                RuleId = ruleId,
                PackId = PackIds.ProjectCriteria,
                MeasurementBasisKey = basisKey,
                ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                MinimumDistanceMeters = minimum,
                ConstraintTreatment = ConstraintTreatment.ProjectRequirement,
                AutoExecutionAllowed = false,
                EngineeringReviewRequired = true,
                DecisionState = decisionState
            };
        }

        /// <summary>A governed pack rule the authority chain has marked auto-executable; provenance stays PROJECT_RULE_ONLY.</summary>
        private static StandardsPackRule ExecutableRule(string ruleId, string basisKey, decimal minimum)
        {
            return new StandardsPackRule
            {
                RuleId = ruleId,
                PackId = PackIds.ProjectCriteria,
                MeasurementBasisKey = basisKey,
                ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                MinimumDistanceMeters = minimum,
                ConstraintTreatment = ConstraintTreatment.ProjectRequirement,
                AutoExecutionAllowed = true,
                EngineeringReviewRequired = false,
                DecisionState = StandardDecisionStates.Pass
            };
        }

        private static EngineeringDistanceCandidateDefinition SyntheticCandidate(string candidateId, string ruleId, string basisKey, decimal proposed)
        {
            return new EngineeringDistanceCandidateDefinition
            {
                CandidateId = candidateId,
                PackId = PackIds.ProjectCriteria,
                RuleId = ruleId,
                Title = "Synthetic Phase C test candidate",
                MeasurementKind = MeasurementKindFor(basisKey),
                ProposedMinimumDistanceMeters = proposed,
                RequiredMeasurementBasisKey = basisKey,
                RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                ApprovalStatus = "PENDING",
                ClauseIds = new List<string> { ProjectClauseId }
            };
        }

        /// <summary>A well-formed P9 approval record, validated by the existing P9 record validator.</summary>
        private static EngineeringDistanceApprovalRecord SyntheticApprovalRecord(string approvalRecordId, EngineeringDistanceCandidateDefinition candidate)
        {
            var binding = new EngineeringDistanceCandidateBinding
            {
                CandidateId = candidate.CandidateId,
                PackId = candidate.PackId,
                RuleId = candidate.RuleId,
                MeasurementKind = candidate.MeasurementKind,
                ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters,
                RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey,
                RequiredApplicabilityKey = candidate.RequiredApplicabilityKey,
                ClauseIds = candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                CandidateDefinitionSha256 = string.Empty
            };
            binding.CandidateDefinitionSha256 = EngineeringDistanceApprovalRecordBuilder.ComputeCandidateDefinitionSha256(binding);

            var record = new EngineeringDistanceApprovalRecord
            {
                SchemaVersion = "1.0",
                ApprovalRecordId = string.Empty,
                ProjectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(ProjectDirectory),
                CandidateBinding = binding,
                Decision = new EngineeringDistanceApprovalDecision
                {
                    CandidateId = candidate.CandidateId,
                    DecisionStatus = "APPROVED",
                    ApprovalReference = "P9-APPROVAL-" + approvalRecordId
                },
                ApproverDisplayName = "Synthetic Phase C Test Approver",
                DecisionTimestampUtc = new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc),
                EvidenceBindings = new List<EngineeringDistanceApprovalEvidenceBinding>
                {
                    new EngineeringDistanceApprovalEvidenceBinding { ClauseId = ProjectClauseId, SourceId = ProjectSourceId, SourceSha256 = ProjectSourceSha256 }
                }
            };
            record.ApprovalRecordId = EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(record);

            // The fixture must be a legitimate P9 artifact: the existing validator accepts it unchanged.
            EngineeringDistanceApprovalRecordValidator.Validate(record);
            return record;
        }

        /// <summary>Resolves a project rule through the existing P9 governed resolver with a synthetic authority result.</summary>
        private static ProjectDistanceGovernedRuleResolution Resolve(EngineeringDistanceRuleDefinition projectRule, EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceApprovalRecord approval, string authorityState)
        {
            var authority = new EngineeringDistanceApprovalAuthorityResult
            {
                State = authorityState,
                ProjectDirectory = approval.ProjectDirectory,
                CandidateId = candidate.CandidateId,
                ApprovalRecordId = approval.ApprovalRecordId,
                PolicyId = "policy.plotplan.synthetic",
                AuthorityBasisReference = "p9.synthetic.authority"
            };
            return ProjectDistanceGovernedRuleResolver.Resolve(projectRule, candidate, approval, authority);
        }

        private static PlotSpatialMeasurementModel Measurements(params (string BasisKey, double Distance, string Handle)[] evidence)
        {
            var model = new PlotSpatialMeasurementModel();
            foreach ((string BasisKey, double Distance, string Handle) item in evidence)
            {
                model.EquipmentSpatialMeasurements.Add(new EquipmentSpatialMeasurement
                {
                    MeasurementId = "measurement:" + item.Handle,
                    PlacementId = "placement:equipment-1",
                    EquipmentEntityId = "equipment-1",
                    NormalizedTag = "T-1001",
                    ConstraintId = string.Empty,
                    SubjectKind = string.Empty,
                    Axis = string.Empty,
                    CoordinateValue = 0m,
                    LabelSourceHandle = item.Handle + ":label",
                    GeometrySourceHandle = item.Handle + ":geometry",
                    GeometrySourceLayer = "Synthetic",
                    GeometryKind = "Polyline",
                    Distance2D = item.Distance,
                    EngineeringMeasurementBasisKey = item.BasisKey,
                    EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
                });
            }
            return model;
        }

        // ---- CHECK helpers ------------------------------------------------------------------------------

        private static BoundedCheckResult Check(StandardsPackRule rule, PlotSpatialMeasurementModel measurements)
        {
            return Check(new[] { rule }, measurements, out BoundedCheckDiagnostics _, out BoundedConstraintDiagnostics _);
        }

        private static string CheckState(StandardsPackRule rule, PlotSpatialMeasurementModel measurements)
        {
            return Check(new[] { rule }, measurements, out BoundedCheckDiagnostics _, out BoundedConstraintDiagnostics _).Checks[0].State;
        }

        private static BoundedCheckResult Check(IReadOnlyList<StandardsPackRule> rules, PlotSpatialMeasurementModel measurements, out BoundedCheckDiagnostics checkDiagnostics, out BoundedConstraintDiagnostics constraintDiagnostics)
        {
            BoundedConstraintSet set = BoundedConstraintBuilder.Build(rules, out constraintDiagnostics);
            return BoundedCheckEvaluator.Evaluate(set.Constraints, measurements, out checkDiagnostics);
        }

        private static void AssertBlocked(StandardsPackRule rule, PlotSpatialMeasurementModel measurements, string message)
        {
            Assert(!rule.AutoExecutionAllowed, "A blocked rule must not be auto-executable: " + rule.RuleId + ".");

            BoundedConstraintSet set = BoundedConstraintBuilder.Build(new[] { rule }, out BoundedConstraintDiagnostics constraintDiagnostics);
            Assert(constraintDiagnostics.ReviewRequiredConstraintCount == 1, "A blocked rule was not counted as review-required: " + rule.RuleId + ".");
            Assert(constraintDiagnostics.ExecutableConstraintCount == 0, "A blocked rule was counted as executable: " + rule.RuleId + ".");

            BoundedCheckResult result = BoundedCheckEvaluator.Evaluate(set.Constraints, measurements, out BoundedCheckDiagnostics checkDiagnostics);
            BoundedConstraintCheck? check = result.Checks.FirstOrDefault();
            Assert(check != null, "A blocked rule vanished from the bounded CHECK: " + rule.RuleId + ".");
            Assert(check!.State == ProposalConstraintState.ReviewRequired, message + " (" + rule.RuleId + " -> " + check!.State + ")");
            Assert(check!.ObservedDistanceMeters == null, "A blocked rule reported an observed distance: " + rule.RuleId + ".");
            Assert(check!.Relationship != null, "A blocked bounded rule lost its relationship: " + rule.RuleId + ".");
            Assert(checkDiagnostics.ReviewRequiredCount == 1, "The CHECK diagnostics did not count the blocked rule: " + rule.RuleId + ".");
            Assert(checkDiagnostics.SatisfiedCount == 0 && checkDiagnostics.ViolatedCount == 0,
                "A blocked rule contributed a numerical verdict: " + rule.RuleId + ".");
        }

        private static void AssertMapping(string expectedRelationship, string basisKey, string handle)
        {
            StandardsPackRule rule = ExecutableRule("pc.mapping." + expectedRelationship.ToLowerInvariant(), basisKey, 15m);
            BoundedCheckResult result = Check(rule, Measurements((basisKey, 20.0, handle)));
            BoundedConstraintCheck check = result.Checks[0];
            Assert(check.Relationship == expectedRelationship, "Basis " + basisKey + " mapped to " + (check.Relationship ?? "null") + " instead of " + expectedRelationship + ".");
            Assert(check.MeasurementBasisKey == basisKey, "The CHECK lost its measurement basis key.");
            Assert(check.State == ProposalConstraintState.Satisfied, "A mapping CHECK did not evaluate the evidence.");
        }

        // ---- Determinism helpers -------------------------------------------------------------------------

        private static string Snapshot(IReadOnlyList<StandardsPackRule> rules, params (string, double, string)[] evidence)
        {
            BoundedCheckResult result = Check(rules, Measurements(evidence), out BoundedCheckDiagnostics checkDiagnostics, out BoundedConstraintDiagnostics constraintDiagnostics);

            var builder = new StringBuilder();
            foreach (BoundedConstraintCheck check in result.Checks)
            {
                builder.Append(check.ConstraintId).Append('|')
                    .Append(check.State).Append('|')
                    .Append(check.ObservedDistanceMeters.HasValue ? check.ObservedDistanceMeters.Value.ToString("F6", CultureInfo.InvariantCulture) : "null").Append('|')
                    .Append(check.RequiredDistanceMeters.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(check.Relationship ?? "null").Append('|')
                    .Append(string.Join(",", check.EvidenceHandles));
                builder.Append(';');
            }
            builder.Append("##constraints=").Append(string.Join(",", constraintDiagnostics.DiagnosticReasons));
            builder.Append("##checks=").Append(string.Join(",", checkDiagnostics.DiagnosticReasons));
            return builder.ToString();
        }

        private static string GroupSnapshot(IReadOnlyList<StandardsPackRule> rules)
        {
            var builder = new StringBuilder();
            foreach (StandardsPackGroup group in StandardsPackPackCatalog.Group(rules))
            {
                builder.Append(group.PackId).Append(':').Append(string.Join(",", group.Rules.Select(x => x.RuleId)));
                builder.Append(';');
            }
            return builder.ToString();
        }

        private static string BoundedSnapshot(IReadOnlyList<StandardsPackRule> rules)
        {
            return string.Join(",", StandardsPackPackCatalog.BoundedRules(rules).Select(x => x.RuleId));
        }

        private static T[] Reverse<T>(T[] values)
        {
            var reversed = new T[values.Length];
            for (int i = 0; i < values.Length; i++) reversed[i] = values[values.Length - 1 - i];
            return reversed;
        }

        // ---- Assertion helpers ---------------------------------------------------------------------------

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(message + " (unexpected exception type: " + ex.GetType().Name + ")", ex);
            }
            throw new InvalidOperationException(message + " (no exception was thrown).");
        }

        private static bool NoHostReference(Assembly assembly)
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string name = reference.Name ?? string.Empty;
                if (name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)
                    || name.IndexOf("acdbmgd", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("acmgd", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ReferencesAssembly(Assembly assembly, string simpleName)
        {
            return assembly.GetReferencedAssemblies().Any(r => string.Equals(r.Name, simpleName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool NoHostTypedMembers(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                string propertyTypeName = property.PropertyType.Namespace ?? string.Empty;
                if (propertyTypeName.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)) return false;
                string name = property.Name;
                if (name == "ObjectId" || name == "Document" || name == "Database" || name == "Transaction" || name == "Handle" || name == "Polyline") return false;
            }
            return true;
        }
    }
}
