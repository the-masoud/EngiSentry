using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.AutoCAD2021;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Phase E integration tests: the read-only proposal runtime that wires the accepted Phase C governed
    /// CHECK to the accepted Phase D proposal generator, then emits the separate Phase A K.3 sidecar
    /// (Phase A section K, especially K.3).
    ///
    /// These tests cover only the integration surface: deterministic sidecar serialization, the
    /// FileMode.CreateNew contract, the zero-proposal governed outcome, the eligible-fixture proposal path,
    /// the PlotPlanQaReport serialization non-regression, the absence of ReviewProjection from PlotQA JSON,
    /// the absence of any DWG mutation path, and Core Console host safety. They deliberately do NOT repeat
    /// the accepted Phase D candidate/ranking/re-evaluation coverage; that belongs to
    /// <see cref="PlacementProposalTests"/>, and the governance chain itself belongs to the existing classes.
    ///
    /// Every fixture is synthetic and stays inside this class. No production catalog, source, clause,
    /// approval, authority, hash or distance is created or altered, and no company standard is named or
    /// remembered. Follows the existing harness convention (internal static Run + Assert).
    /// </summary>
    internal static class PlacementProposalRuntimeTests
    {
        // Metre insertion units: one drawing unit is one metre. The accepted bounded evaluator compares the
        // recorded 2D distance directly against the rule's metre criterion, so a metre-unit drawing keeps the
        // fixture dimensionally consistent while exercising the accepted unit resolver exactly once.
        private const string InsertionUnits = "Meters";
        private const double MetersPerDrawingUnit = 1.0d;

        // Millimetre insertion units: one drawing unit is one millimetre. The same 10 m clearance is recorded
        // as Distance2D = 10000, so the wiring — not the evaluator — must convert before the metre comparison.
        private const string MillimeterInsertionUnits = "Millimeters";
        private const double MetersPerMillimeterUnit = 0.001d;

        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const string FenceHandle = "fence:geometry";
        private const string BoundaryHandle = "boundary:geometry";

        private const double RoadNorthingMeters = 0d;
        private const double FenceNorthingMeters = 40d;
        private const double EquipmentEastingMeters = 100d;
        private const double SpanMeters = 1000d;

        private static readonly DateTime SnapshotUtc = new DateTime(2026, 9, 20, 14, 30, 5, 17, DateTimeKind.Utc);

        internal static void Run()
        {
            ContractLayering();
            ZeroProposalGovernedResult();
            ReviewRequiredYieldsNoProposal();
            EligibleFixtureProducesProposals();
            MillimeterDrawingProducesViolatedProposal();
            GovernedEvaluatorParity();
            MultiPlacementPerPlacementProposals();
            MixedViolatedAndSatisfiedPlacements();
            DuplicateMeasurementPerPlacement();
            ReorderedMeasurementInput();
            SidecarDeterminismAndCreateNew();
            MultiPlacementSidecarRoundTrip();
            PlotPlanQaReportSerializationUnchanged();
            ReviewProjectionAbsentFromPlotQaJson();
            NoDrawingMutationSurface();
            CoreConsoleHostSafety();
        }

        // ---- 1. Dependency layering and reuse of accepted types -----------------------------------------

        private static void ContractLayering()
        {
            // The runtime composes accepted services; it must not re-implement or re-route them. Its own surface
            // stays free of the host object model, so the read-only pipeline can later move out of the process.
            Assert(NoHostTypedMembers(typeof(PlacementProposalRuntime)), "The runtime exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalSnapshotWriter)), "The sidecar writer exposes a host-typed member.");
            Assert(ReferencesAssembly(typeof(PlacementProposalRuntime).Assembly, "EngiSentry.Contracts"),
                "The runtime no longer references the accepted contracts.");
            Assert(ReferencesAssembly(typeof(PlacementProposalRuntime).Assembly, "EngiSentry.Rules"),
                "The runtime no longer references the accepted constraint engine.");
            Assert(ReferencesAssembly(typeof(PlacementProposalRuntime).Assembly, "EngiSentry.Standards"),
                "The runtime no longer references the accepted standards projection.");

            // The sidecar writer lives in the same assembly as the accepted snapshot writers and reuses their
            // root resolution, so the artifact lands beside the accepted PlotQA and standards-review sidecars.
            Assert(typeof(PlacementProposalSnapshotWriter).Assembly == typeof(PlacementProposalRuntime).Assembly,
                "The sidecar writer left the accepted runtime assembly.");
            Assert(ReferencesAssembly(typeof(PlacementProposalSnapshotWriter).Assembly, "EngiSentry.Revision"),
                "The sidecar writer lost its revision-contract reference.");

            // No new proposal model was introduced: the runtime returns the accepted PlacementProposal.
            Assert(typeof(PlacementProposalRunResult).GetProperty("Proposals")!.PropertyType == typeof(List<PlacementProposal>),
                "The runtime result does not carry the accepted PlacementProposal list.");

            // No field was added to PlotPlanQaReport: the accepted DataMember set is unchanged.
            DataMemberAttribute[] reportMembers = typeof(PlotPlanQaReport).GetProperties()
                .Select(p => p.GetCustomAttribute<DataMemberAttribute>())
                .Where(a => a != null)
                .ToArray()!;
            Assert(reportMembers.Length == 3, "PlotPlanQaReport gained or lost a serialized member: " + reportMembers.Length);
            Assert(reportMembers.All(a => a!.Order >= 1 && a!.Order <= 3), "PlotPlanQaReport member ordering changed.");
        }

        // ---- 2. Governance-blocked evidence yields zero proposals ---------------------------------------

        private static void ZeroProposalGovernedResult()
        {
            // No governed rules at all: the CHECK has nothing to evaluate, so no proposal can be generated.
            PlotPlanModel plotPlan = EmptyPlotPlan();
            PlacementProposalRunResult empty = PlacementProposalRuntime.Build(plotPlan, new List<StandardsPackRule>(), null!, out PlacementProposalRunDiagnostics diagnostics);
            Assert(empty.Proposals.Count == 0, "A governance-empty run produced proposals.");
            Assert(diagnostics.ProposalCount == 0, "A governance-empty run recorded a non-zero proposal count.");
            Assert(diagnostics.ViolatedCount == 0, "A governance-empty run recorded violations.");
            Assert(diagnostics.GovernedRuleCount == 0, "A governance-empty run recorded governed rules.");
            Assert(diagnostics.GovernanceItems == 0, "A governance-empty run recorded governance items.");

            // Rules present but no measurement evidence: INSUFFICIENT_EVIDENCE, never a promoted proposal.
            PlotPlanModel noEvidence = EmptyPlotPlan();
            PlacementProposalRunResult noMeasurements = PlacementProposalRuntime.Build(noEvidence, GovernedRules(), null!, out PlacementProposalRunDiagnostics noEvidenceDiagnostics);
            Assert(noMeasurements.Proposals.Count == 0, "A run with no measurement evidence produced proposals.");
            Assert(noEvidenceDiagnostics.ProposalCount == 0, "A run with no measurement evidence recorded proposals.");
            Assert(noEvidenceDiagnostics.GovernedRuleCount > 0, "The governed rule count was not carried into diagnostics.");
        }

        // ---- 3. Review-required / non-executable rules generate no proposal ------------------------------

        private static void ReviewRequiredYieldsNoProposal()
        {
            // A measurement that would violate the criterion, but the governing rule is not auto-executable.
            PlotPlanModel plotPlan = PlotPlanWithRoadMeasurement(actualMeters: 10m);
            List<StandardsPackRule> blocked = GovernedRules(roadMinimumMeters: 15m, autoExecutionAllowed: false);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, blocked, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(result.Proposals.Count == 0, "A non-auto-executable rule generated a placement proposal.");
            Assert(diagnostics.ProposalCount == 0, "A non-auto-executable rule recorded a proposal count.");

            // The accepted Phase C CHECK degrades a non-executable rule to REVIEW_REQUIRED, so it never reaches
            // the VIOLATED state that generation consumes: the block is upstream, in the accepted evaluator.
            Assert(diagnostics.ViolatedCount == 0, "A non-auto-executable rule was counted as violated.");
            Assert(diagnostics.ExecutableViolatedCount == 0, "A non-auto-executable rule counted an executable violation.");
            Assert(diagnostics.ReviewRequiredCount > 0, "A non-auto-executable rule did not surface as review-required.");
            Assert(!diagnostics.DiagnosticReasons.Any(x => x.StartsWith("blocked:", StringComparison.Ordinal)),
                "A review-required constraint reached the runtime's blocked branch, so the accepted upstream gate was bypassed.");

            // The same evidence with an executable rule DOES reach generation: the block is the only difference.
            PlacementProposalRunResult executable = PlacementProposalRuntime.Build(plotPlan, GovernedRules(roadMinimumMeters: 15m), null!, out PlacementProposalRunDiagnostics _);
            Assert(executable.Proposals.Count > 0, "The same evidence with an executable rule produced no proposal.");
        }

        // ---- 4. Eligible controlled fixture produces proposals --------------------------------------------

        private static void EligibleFixtureProducesProposals()
        {
            // Equipment 10 m from the road edge against a 15 m criterion: a genuine executable violation.
            PlotPlanModel plotPlan = PlotPlanWithRoadMeasurement(actualMeters: 10m);
            List<StandardsPackRule> rules = GovernedRules(roadMinimumMeters: 15m);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, rules, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(result.Proposals.Count > 0, "An eligible controlled fixture produced no proposal.");
            Assert(diagnostics.ProposalCount == result.Proposals.Count, "The diagnostics count did not match the proposal set.");
            Assert(diagnostics.ViolatedCount > 0, "An eligible fixture recorded no violated constraint.");
            Assert(diagnostics.ExecutableViolatedCount > 0, "An eligible fixture recorded no executable violation.");

            // Every emitted proposal is the accepted advisory shape: review-required, never a final position.
            foreach (PlacementProposal proposal in result.Proposals)
            {
                Assert(proposal != null, "The run emitted a null proposal.");
                Assert(!string.IsNullOrEmpty(proposal!.ProposalId), "A proposal lost its deterministic identity.");
                Assert(proposal.EngineeringReviewRequired, "A generated proposal stopped requiring engineering review.");
                Assert(proposal.ViolatedRule != null && proposal.ViolatedRule!.RuleId == "pd.governed." + EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                    "A proposal did not carry the violated governed rule identity.");
                Assert(proposal.RequiredDistanceMeters == 15m, "A proposal did not carry the required distance criterion.");
                Assert(proposal.ObservedDistanceMeters > 0d, "A proposal did not carry the observed distance.");
                Assert(proposal.MovementDistanceMeters >= 0d, "A proposal carried a negative movement distance.");
                Assert(proposal.KnownLimitations != null && proposal.KnownLimitations.Count > 0, "A proposal lost its mandatory known-limitations list.");
                Assert(proposal.Assumptions != null && proposal.Assumptions.Count > 0, "A proposal lost its mandatory assumptions list.");
                Assert(proposal.UncheckedConstraints != null && proposal.UncheckedConstraints.Count > 0, "A proposal lost its mandatory unchecked-constraints list.");
            }

            // The proposed position actually satisfies the violated criterion (10 m -> at least 15 m from road).
            PlacementProposal primary = result.Proposals[0];
            Assert(primary.ProposedNorthing >= MetersToDrawingUnits(15d), "The primary proposal moved the equipment below the required clearance.");
            Assert(primary.OriginalNorthing == MetersToDrawingUnits(10d), "The proposal lost its original equipment position.");

            // Deterministic ordering: proposals are ordered by identity, independent of construction order.
            Assert(result.Proposals.Select(x => x.ProposalId).SequenceEqual(result.Proposals.Select(x => x.ProposalId).OrderBy(x => x, StringComparer.Ordinal)),
                "The runtime result is not deterministically ordered by proposal identity.");
        }

        // ---- 4b. A non-metre insertion unit keeps the CHECK dimensionally correct ----------------------------

        private static void MillimeterDrawingProducesViolatedProposal()
        {
            // Phase C acceptance caveat 15.1: the wiring supplies the unit resolution the accepted evaluator
            // deliberately does not apply. The same physical case as section 4 — 10 m of actual clearance
            // against a 15 m criterion — is recorded as Distance2D = 10000 in a millimetre drawing. Compared
            // raw, 10000 reads as 10000 m of clearance, the CHECK reports SATISFIED and the violation is
            // hidden; through the accepted resolver it resolves to 10 m and the eligible path is reached.
            const decimal requiredMeters = 15m;
            PlotPlanModel plotPlan = PlotPlanWithRoadMeasurement(actualMeters: 10m, MillimeterInsertionUnits, MetersPerMillimeterUnit);
            List<StandardsPackRule> rules = GovernedRules(roadMinimumMeters: requiredMeters);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, rules, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(diagnostics.SatisfiedCount == 0, "A 10 m clearance against a 15 m criterion was reported satisfied in a millimetre drawing.");
            Assert(diagnostics.ViolatedCount > 0, "A millimetre drawing did not surface the violated road constraint.");
            Assert(diagnostics.ExecutableViolatedCount > 0, "A millimetre drawing did not reach the executable violated path.");
            Assert(result.Proposals.Count > 0, "A millimetre-drawing violation produced no eligible proposal.");

            foreach (PlacementProposal proposal in result.Proposals)
            {
                Assert(proposal != null, "The millimetre run emitted a null proposal.");
                Assert(proposal!.RequiredDistanceMeters == requiredMeters, "A millimetre-drawing proposal lost the metre criterion.");
                // Observed distance is reported in metres regardless of the drawing unit: 10000 mm resolves to 10 m.
                Assert(proposal.ObservedDistanceMeters > 9.5d && proposal.ObservedDistanceMeters < 10.5d,
                    "A millimetre-drawing proposal carried a metre-inconsistent observed distance: " + proposal.ObservedDistanceMeters);
                Assert(proposal.EngineeringReviewRequired, "A millimetre-drawing proposal stopped requiring engineering review.");
            }

            // The proposed position still clears the criterion, expressed in the drawing's own units: the
            // accepted generator converts its displacement through the same resolution.
            PlacementProposal primary = result.Proposals[0];
            Assert(Math.Abs(primary.OriginalNorthing - MetersToDrawingUnits(10d, MetersPerMillimeterUnit)) < 1e-3d,
                "The millimetre fixture lost its original equipment position: " + primary.OriginalNorthing);
            Assert(primary.ProposedNorthing >= MetersToDrawingUnits(15d, MetersPerMillimeterUnit) - 1e-3d,
                "The millimetre proposal moved the equipment below the required clearance: " + primary.ProposedNorthing);

            // The metre fixture is unchanged and agrees with the millimetre fixture: unit conversion, not a
            // different evaluation, is the only thing that differs between the two drawings.
            PlacementProposalRunResult meterRun = PlacementProposalRuntime.Build(PlotPlanWithRoadMeasurement(actualMeters: 10m), GovernedRules(roadMinimumMeters: requiredMeters), null!, out PlacementProposalRunDiagnostics meterDiagnostics);
            Assert(meterRun.Proposals.Count == result.Proposals.Count, "The metre and millimetre fixtures diverged in proposal count.");
            Assert(meterDiagnostics.ViolatedCount == diagnostics.ViolatedCount, "The metre and millimetre fixtures diverged in violated count.");
            Assert(Math.Abs(meterRun.Proposals[0].ObservedDistanceMeters - primary.ObservedDistanceMeters) < 1e-9d,
                "The metre and millimetre fixtures diverged in observed distance.");
        }

        // ---- 4c. The governed bridge reuses the accepted evaluator's selection semantics ------------------

        private static void GovernedEvaluatorParity()
        {
            // The governed entry point must be the same implementation the registry path uses, not a copy. A
            // fixture representable through both paths is built once and evaluated both ways; the violations
            // must agree in identity, placement, distance and evidence, measure for measure.
            PlotPlanModel plotPlan = PlotPlanWithTwoPlacements(violatedMeters: 10d, satisfiedMeters: 20d);
            BoundedConstraint constraint = RoadConstraintFrom(GovernedRules(roadMinimumMeters: 15m));

            EngineeringDistanceRuleEvaluation governed = EngineeringDistanceRuleEvaluator.EvaluateGovernedConstraint(constraint, plotPlan);

            // The registry path: the same rule, carried through the accepted criteria-provenance contract.
            EngineeringDistanceRuleDefinition rule = RegistryRoadRule(15m);
            EngineeringDistanceRuleRegistry registry = EngineeringDistanceRuleRegistry.CreateProjectCriteria(new[] { rule });
            IReadOnlyList<EngineeringDistanceRuleEvaluation> registryEvaluations = EngineeringDistanceRuleEvaluator.Evaluate(registry, plotPlan);
            EngineeringDistanceRuleEvaluation accepted = registryEvaluations.First(x => x.RuleId == rule.RuleId);

            Assert(governed.RuleId == accepted.RuleId, "The governed evaluation reported a different rule identity.");
            Assert(governed.MeasurementKind == accepted.MeasurementKind, "The governed evaluation reported a different measurement kind.");
            Assert(governed.MinimumDistanceMeters == accepted.MinimumDistanceMeters, "The governed evaluation reported a different criterion.");
            Assert(governed.RequiredMeasurementBasisKey == accepted.RequiredMeasurementBasisKey, "The governed evaluation reported a different measurement basis.");
            Assert(governed.RequiredApplicabilityKey == accepted.RequiredApplicabilityKey, "The governed evaluation reported a different applicability.");
            Assert(governed.MinimumDistanceDrawingUnits == accepted.MinimumDistanceDrawingUnits, "The governed evaluation converted the criterion differently.");
            Assert(governed.MetersPerDrawingUnit == accepted.MetersPerDrawingUnit, "The governed evaluation resolved the drawing unit differently.");
            Assert(governed.EvaluatedSubjectCount == accepted.EvaluatedSubjectCount, "The governed evaluation selected a different number of governing placements.");
            Assert(governed.Status == accepted.Status, "The governed evaluation reported a different status: " + governed.Status + " vs " + accepted.Status);
            Assert(governed.ViolationCount == accepted.ViolationCount, "The governed evaluation reported a different violation count.");
            Assert(governed.Violations.Count == accepted.Violations.Count, "The governed evaluation returned a different violation list length.");

            for (int index = 0; index < accepted.Violations.Count; index++)
            {
                EngineeringDistanceViolation a = accepted.Violations[index];
                EngineeringDistanceViolation b = governed.Violations[index];
                Assert(a.MeasurementId == b.MeasurementId, "The governed evaluation selected a different measurement at position " + index + ".");
                Assert(a.PlacementId == b.PlacementId, "The governed evaluation reported a different placement at position " + index + ".");
                Assert(a.EquipmentEntityId == b.EquipmentEntityId, "The governed evaluation reported a different equipment entity at position " + index + ".");
                Assert(a.NormalizedTag == b.NormalizedTag, "The governed evaluation reported a different tag at position " + index + ".");
                Assert(a.SubjectKind == b.SubjectKind, "The governed evaluation reported a different subject kind at position " + index + ".");
                Assert(a.ActualDistanceDrawingUnits == b.ActualDistanceDrawingUnits, "The governed evaluation carried a different drawing-unit distance at position " + index + ".");
                Assert(a.ActualDistanceMeters == b.ActualDistanceMeters, "The governed evaluation carried a different metre distance at position " + index + ".");
                Assert(a.GeometrySourceHandle == b.GeometrySourceHandle, "The governed evaluation carried different geometry evidence at position " + index + ".");
                Assert(a.LabelSourceHandle == b.LabelSourceHandle, "The governed evaluation carried different label evidence at position " + index + ".");
                Assert(a.ClosestPointX == b.ClosestPointX && a.ClosestPointY == b.ClosestPointY, "The governed evaluation carried a different closest point at position " + index + ".");
                Assert(a.MeasurementBasisKey == b.MeasurementBasisKey, "The governed evaluation carried a different basis key at position " + index + ".");
                Assert(a.ApplicabilityKey == b.ApplicabilityKey, "The governed evaluation carried a different applicability key at position " + index + ".");
            }

            // Deterministic ordering is part of the accepted semantics: PlacementId, then MeasurementId.
            Assert(governed.Violations.Select(x => x.PlacementId).SequenceEqual(governed.Violations.Select(x => x.PlacementId).OrderBy(x => x, StringComparer.Ordinal)),
                "The governed violations are not ordered by placement identity.");
            Assert(governed.Violations.Select(x => x.MeasurementId).SequenceEqual(governed.Violations.Select(x => x.MeasurementId).OrderBy(x => x, StringComparer.Ordinal)),
                "The governed violations are not ordered by measurement identity.");

            // A governed constraint deliberately carries no criteria provenance — it is already governed — and
            // must not synthesize one. The registry path's provenance stays on the registry evaluation.
            Assert(governed.CriteriaProvenance == null, "The governed evaluation synthesized a criteria provenance.");
            Assert(accepted.CriteriaProvenance != null, "The registry evaluation lost its criteria provenance.");

            // The bridge must consume only the governed constraint's own fields: an unrelated rule identity on
            // the constraint produces the same selection, because the measurement evidence governs, not the id.
            BoundedConstraint relabeled = ConstraintFromRule(ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m), "an.unrelated.rule.id");
            EngineeringDistanceRuleEvaluation relabeledEvaluation = EngineeringDistanceRuleEvaluator.EvaluateGovernedConstraint(relabeled, plotPlan);
            Assert(relabeledEvaluation.Violations.Count == governed.Violations.Count,
                "The governed evaluation changed its violation count when only the rule identity changed.");
            Assert(relabeledEvaluation.Violations.Select(x => x.MeasurementId).SequenceEqual(governed.Violations.Select(x => x.MeasurementId)),
                "The governed evaluation changed its selection when only the rule identity changed.");
        }

        // ---- 4d. One rule/basis preserves every violated placement -----------------------------------------

        private static void MultiPlacementPerPlacementProposals()
        {
            // Two distinct equipment placements, both violating the same executable Road constraint. The
            // accepted evaluator selects one governing measurement per PlacementId, so both surface: the
            // previous global collapse would have produced a single proposal for the closest one only.
            PlotPlanModel plotPlan = PlotPlanWithTwoPlacements(violatedMeters: 10d, violated2Meters: 8d);
            List<StandardsPackRule> rules = GovernedRules(roadMinimumMeters: 15m);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, rules, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(result.Proposals.Count == 2, "Two violated placements did not produce exactly two proposals: " + result.Proposals.Count);
            Assert(diagnostics.ProposalCount == 2, "The diagnostics did not record the two per-placement proposals.");
            Assert(diagnostics.ExecutableViolatedCount == 2, "The diagnostics did not count the two executable violations.");

            // Both proposals are for the same rule and basis but for different placements.
            Assert(result.Proposals.Select(x => x.ViolatedRule!.RuleId).Distinct(StringComparer.Ordinal).Count() == 1,
                "The two proposals carried different violated rule identities.");
            Assert(result.Proposals.Select(x => x.ObservedMeasurementId).Distinct(StringComparer.Ordinal).Count() == 2,
                "The two proposals carried the same governing measurement, so a placement was collapsed.");

            AssertProposalCarriesPlacementEvidence(result.Proposals[0], plotPlan, "placement:equipment-1");
            AssertProposalCarriesPlacementEvidence(result.Proposals[1], plotPlan, "placement:equipment-2");

            // The further placement is the one with the smaller observed clearance.
            Assert(result.Proposals[0].ObservedDistanceMeters > result.Proposals[1].ObservedDistanceMeters,
                "The two placements were not distinguished by their observed distances.");

            // Deterministic ordering by proposal identity, independent of construction order.
            Assert(result.Proposals.Select(x => x.ProposalId).SequenceEqual(result.Proposals.Select(x => x.ProposalId).OrderBy(x => x, StringComparer.Ordinal)),
                "The per-placement proposals are not deterministically ordered.");
        }

        // ---- 4e. A satisfied placement under the same rule contributes no proposal --------------------------

        private static void MixedViolatedAndSatisfiedPlacements()
        {
            // The same executable Road constraint, one placement violating and one satisfying. Only the
            // violated placement reaches generation; the satisfied one is not averaged or merged into it.
            PlotPlanModel plotPlan = PlotPlanWithTwoPlacements(violatedMeters: 10d, satisfiedMeters: 20d);
            List<StandardsPackRule> rules = GovernedRules(roadMinimumMeters: 15m);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, rules, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(result.Proposals.Count == 1, "A violated and a satisfied placement did not produce exactly one proposal: " + result.Proposals.Count);
            Assert(diagnostics.ProposalCount == 1, "The diagnostics did not record the single per-placement proposal.");
            Assert(diagnostics.ViolatedCount > 0, "The aggregate check did not surface the violated constraint.");

            PlacementProposal proposal = result.Proposals[0];
            AssertProposalCarriesPlacementEvidence(proposal, plotPlan, "placement:equipment-1");
            Assert(proposal.ObservedDistanceMeters > 5d && proposal.ObservedDistanceMeters < 15d,
                "The proposal did not carry the violated placement's observed distance: " + proposal.ObservedDistanceMeters);

            // The satisfied placement's measurement is not the one the proposal was built from.
            EquipmentSpatialMeasurement satisfied = MeasurementForPlacement(plotPlan, "placement:equipment-2");
            Assert(proposal.ObservedMeasurementId != satisfied.MeasurementId,
                "The proposal was built from the satisfied placement's measurement.");
        }

        // ---- 4f. Multiple measurements for one placement collapse to one governing measurement --------------

        private static void DuplicateMeasurementPerPlacement()
        {
            // One placement, two recorded measurements on the same basis (e.g. two resolved road segments). The
            // accepted evaluator selects the minimum per PlacementId, so exactly one proposal results and no
            // duplicate is emitted for the second measurement.
            PlotPlanModel plotPlan = PlotPlanWithDuplicateMeasurements(governingMeters: 7d, fartherMeters: 12d);
            List<StandardsPackRule> rules = GovernedRules(roadMinimumMeters: 15m);

            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, rules, null!, out PlacementProposalRunDiagnostics diagnostics);

            Assert(result.Proposals.Count == 1, "Duplicate measurements for one placement produced " + result.Proposals.Count + " proposals instead of one.");
            Assert(diagnostics.ProposalCount == 1, "The diagnostics did not record the single collapsed proposal.");

            PlacementProposal proposal = result.Proposals[0];
            Assert(proposal.ObservedDistanceMeters > 6.5d && proposal.ObservedDistanceMeters < 7.5d,
                "The proposal did not carry the minimum governing distance: " + proposal.ObservedDistanceMeters);

            EquipmentSpatialMeasurement governing = MeasurementForPlacement(plotPlan, "placement:equipment-1");
            Assert(proposal.ObservedMeasurementId == governing.MeasurementId,
                "The proposal was not built from the minimum governing measurement.");

            // The farther measurement is recorded evidence but must not produce its own proposal.
            Assert(result.Proposals.All(x => x.ObservedMeasurementId != "measurement:road-far"),
                "The non-governing measurement produced a proposal.");
        }

        // ---- 4g. Input order does not change the proposal set ------------------------------------------------

        private static void ReorderedMeasurementInput()
        {
            // The same evidence in reverse construction order yields the same proposals in the same
            // deterministic order: selection and ordering belong to the accepted evaluator and the writer, not
            // to the order the caller happened to supply.
            const decimal requiredMeters = 15m;

            PlacementProposalRunResult first = PlacementProposalRuntime.Build(PlotPlanWithTwoPlacements(violatedMeters: 10d, violated2Meters: 8d), GovernedRules(roadMinimumMeters: requiredMeters), null!, out PlacementProposalRunDiagnostics firstDiagnostics);
            PlacementProposalRunResult reversed = PlacementProposalRuntime.Build(PlotPlanWithTwoPlacementsReversed(violatedMeters: 10d, violated2Meters: 8d), GovernedRules(roadMinimumMeters: requiredMeters), null!, out PlacementProposalRunDiagnostics reversedDiagnostics);

            Assert(first.Proposals.Count == reversed.Proposals.Count, "Reordering the measurements changed the proposal count.");
            Assert(first.Proposals.Select(x => x.ProposalId).SequenceEqual(reversed.Proposals.Select(x => x.ProposalId)),
                "Reordering the measurements changed the proposal identities or their order.");
            Assert(first.Proposals.Select(x => x.ObservedMeasurementId).SequenceEqual(reversed.Proposals.Select(x => x.ObservedMeasurementId)),
                "Reordering the measurements changed which measurements were selected.");
            Assert(Math.Abs(first.Proposals[0].ProposedNorthing - reversed.Proposals[0].ProposedNorthing) < 1e-9d
                && Math.Abs(first.Proposals[1].ProposedNorthing - reversed.Proposals[1].ProposedNorthing) < 1e-9d,
                "Reordering the measurements changed the proposed positions.");
            Assert(firstDiagnostics.ViolatedCount == reversedDiagnostics.ViolatedCount, "Reordering the measurements changed the violated count.");
        }

        // ---- 6b. A multi-placement proposal set survives the sidecar round-trip -----------------------------

        private static void MultiPlacementSidecarRoundTrip()
        {
            // The K.3 sidecar must carry every proposal identity and its placement evidence through
            // serialization, so an engineer reading the artifact sees the same per-placement set.
            PlotPlanModel plotPlan = PlotPlanWithTwoPlacements(violatedMeters: 10d, violated2Meters: 8d);
            List<PlacementProposal> proposals = PlacementProposalRuntime.Build(plotPlan, GovernedRules(roadMinimumMeters: 15m), null!, out PlacementProposalRunDiagnostics _).Proposals;
            Assert(proposals.Count == 2, "The multi-placement sidecar fixture did not produce two proposals.");

            string path = string.Empty;
            try
            {
                path = PlacementProposalSnapshotWriter.Write(proposals, "phase-e-multi", SnapshotUtc);
                List<PlacementProposal> read = PlacementProposalSnapshotWriter.Read(path);

                Assert(read != null, "Reading the multi-placement sidecar returned no list.");
                Assert(read!.Count == 2, "The sidecar round-trip lost proposals: " + read.Count);
                Assert(read.Select(x => x!.ProposalId).SequenceEqual(proposals.Select(x => x.ProposalId)),
                    "The sidecar round-trip did not preserve the per-placement proposal identities and order.");
                Assert(read.All(x => x!.EngineeringReviewRequired), "A round-tripped multi-placement proposal stopped requiring engineering review.");

                // Placement identity survives through the recorded measurement and tag, not through a host handle.
                Assert(read.Select(x => x!.ObservedMeasurementId).Distinct(StringComparer.Ordinal).Count() == 2,
                    "The sidecar round-trip collapsed the two placements' measurement identities.");
                Assert(read.Select(x => x!.OriginalNorthing).Distinct().Count() == 2,
                    "The sidecar round-trip collapsed the two placements' original positions.");

                // Reversed writer input still yields byte-identical bytes: ordering is the writer's contract.
                string reversedPath = PlacementProposalSnapshotWriter.Write(proposals.AsEnumerable().Reverse().ToList(), "phase-e-multi-rev", SnapshotUtc);
                try
                {
                    Assert(File.ReadAllText(path) == File.ReadAllText(reversedPath),
                        "The sidecar bytes changed when the caller supplied the same proposals in reverse order.");
                }
                finally { TryDelete(reversedPath); }
            }
            finally { TryDelete(path); }
        }

        // ---- 5. Sidecar determinism and the FileMode.CreateNew contract ---------------------------------

        private static void SidecarDeterminismAndCreateNew()
        {
            // The production writer resolves the artifacts root itself, so each write takes its own drawing
            // name: same evidence, distinct paths, and the bytes must still be identical.
            List<PlacementProposal> firstSet = EligibleProposals();
            string first = PlacementProposalSnapshotWriter.Write(firstSet, "phase-e-det-a", SnapshotUtc);
            string second = PlacementProposalSnapshotWriter.Write(EligibleProposals(), "phase-e-det-b", SnapshotUtc);

            // Reverse construction order: the artifact is still identical, proving ordering is the writer's job.
            string reversed = PlacementProposalSnapshotWriter.Write(EligibleProposals().AsEnumerable().Reverse().ToList(), "phase-e-det-c", SnapshotUtc);

            string emptyPath = string.Empty;
            string filteredPath = string.Empty;
            try
            {
                Assert(File.ReadAllText(first) == File.ReadAllText(second), "Identical evidence produced a different sidecar.");
                Assert(File.ReadAllText(first) == File.ReadAllText(reversed), "The sidecar changed when the caller supplied proposals in reverse order.");

                // The round-trip preserves the accepted contract, in the deterministic order.
                List<PlacementProposal> read = PlacementProposalSnapshotWriter.Read(first);
                Assert(read != null, "Reading the sidecar returned no list.");
                Assert(read!.Count == firstSet.Count, "The sidecar round-trip lost proposals.");
                Assert(read.Select(x => x.ProposalId).SequenceEqual(firstSet.Select(x => x.ProposalId).OrderBy(x => x, StringComparer.Ordinal)),
                    "The sidecar round-trip did not preserve deterministic ordering.");
                Assert(read.All(x => x!.EngineeringReviewRequired), "The round-tripped sidecar stopped requiring engineering review.");

                // The path contract: <drawing>.<timestamp>.placement-proposal.json under qa/plotplan.
                Assert(Path.GetFileName(first) == "phase-e-det-a.20260920T143005017Z.placement-proposal.json",
                    "The sidecar filename does not match the <drawing>.<timestamp>.placement-proposal.json contract: " + Path.GetFileName(first));
                Assert(Path.GetFileName(Path.GetDirectoryName(first)!) == "plotplan", "The sidecar was not written under the plotplan directory.");
                Assert(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(first)!)) == "qa", "The sidecar was not written under the qa directory.");

                // FileMode.CreateNew: a second write to the SAME path fails rather than silently overwriting.
                bool rejected;
                try { PlacementProposalSnapshotWriter.Write(EligibleProposals(), "phase-e-det-a", SnapshotUtc); rejected = false; }
                catch (IOException) { rejected = true; }
                Assert(rejected, "The sidecar overwrote an existing artifact instead of refusing with FileMode.CreateNew.");

                // Empty proposal set still produces a valid, readable artifact (zero proposals is a real result).
                emptyPath = PlacementProposalSnapshotWriter.Write(new List<PlacementProposal>(), "phase-e-det-empty", SnapshotUtc);
                List<PlacementProposal> emptyRead = PlacementProposalSnapshotWriter.Read(emptyPath);
                Assert(emptyRead != null && emptyRead!.Count == 0, "A zero-proposal sidecar did not round-trip as an empty list.");
                Assert(File.ReadAllText(emptyPath).StartsWith("[", StringComparison.Ordinal), "A zero-proposal sidecar was not serialized as a JSON array.");

                // Null proposals in the input are skipped, not written, never crash the run.
                List<PlacementProposal> withNulls = EligibleProposals();
                withNulls.Add(null!);
                filteredPath = PlacementProposalSnapshotWriter.Write(withNulls, "phase-e-det-nulls", SnapshotUtc);
                Assert(PlacementProposalSnapshotWriter.Read(filteredPath).Count == EligibleProposals().Count,
                    "A null proposal was serialized into the sidecar.");
            }
            finally
            {
                TryDelete(first);
                TryDelete(second);
                TryDelete(reversed);
                TryDelete(emptyPath);
                TryDelete(filteredPath);
            }
        }

        // ---- 6. PlotPlanQaReport serialization is unchanged ----------------------------------------------

        private static void PlotPlanQaReportSerializationUnchanged()
        {
            PlotPlanModel plotPlan = PlotPlanWithRoadMeasurement(actualMeters: 10m);
            var report = new PlotPlanQaReport { PlotPlan = plotPlan, Qa = new QaReport() };
            string json = SerializeReport(report);

            // The report carries no placement-proposal field: the sidecar is the only proposal carrier.
            Assert(json.IndexOf("placement-proposal", StringComparison.Ordinal) < 0, "PlotPlanQaReport serialized a placement-proposal reference.");
            Assert(json.IndexOf("PlacementProposal", StringComparison.Ordinal) < 0, "PlotPlanQaReport serialized a PlacementProposal member.");
            Assert(json.IndexOf("\"Proposals\"", StringComparison.Ordinal) < 0, "PlotPlanQaReport serialized a proposals collection.");
            Assert(json.StartsWith("{", StringComparison.Ordinal), "The Plot Plan QA report did not serialize as a JSON object.");

            // The accepted compactor still produces the accepted PlotQA artifact shape.
            PlotPlanQaReport compact = PlotPlanReportCompactor.Create(report);
            Assert(compact != null && compact.PlotPlan != null && compact.Qa != null, "The accepted compactor lost its members.");

            // The sidecar writer produces exactly the K.3 name, distinct from the accepted PlotQA artifact.
            const string drawing = "phase-e.dwg";
            string proposalPath = PlacementProposalSnapshotWriter.Write(new List<PlacementProposal>(), drawing, SnapshotUtc);
            try
            {
                string proposalName = Path.GetFileName(proposalPath);
                Assert(proposalName == "phase-e.dwg.20260920T143005017Z.placement-proposal.json",
                    "The proposal sidecar name does not match the K.3 contract: " + proposalName);
                Assert(!proposalName.EndsWith(".plotplan.qa.json", StringComparison.Ordinal),
                    "The proposal sidecar reused the accepted PlotQA extension.");
                Assert(proposalName != "phase-e.dwg.20260920T143005017Z.plotplan.qa.json",
                    "The proposal sidecar collides with the accepted PlotQA artifact name.");
            }
            finally { TryDelete(proposalPath); }

            // The accepted report round-trips unchanged.
            PlotPlanQaReport roundTripped = DeserializeReport(json);
            Assert(roundTripped != null, "The Plot Plan QA report did not round-trip.");
            Assert(roundTripped!.Qa != null && roundTripped.PlotPlan != null, "The Plot Plan QA report lost its members on round-trip.");
            Assert(roundTripped.ReviewProjection == null, "The round-tripped report gained a review projection.");
        }

        // ---- 7. ReviewProjection stays absent from PlotQA JSON -------------------------------------------

        private static void ReviewProjectionAbsentFromPlotQaJson()
        {
            // ReviewProjection is [IgnoreDataMember] and must remain outside the serialized JSON, even when set.
            var report = new PlotPlanQaReport
            {
                PlotPlan = new PlotPlanModel(),
                Qa = new QaReport(),
                ReviewProjection = new StandardAuthorityReviewProjection()
            };
            string json = SerializeReport(report);
            Assert(json.IndexOf("ReviewProjection", StringComparison.Ordinal) < 0, "ReviewProjection leaked into the PlotQA JSON.");
            Assert(json.IndexOf("reviewProjection", StringComparison.Ordinal) < 0, "ReviewProjection leaked into the PlotQA JSON under camel case.");
            Assert(json.IndexOf("StandardAuthorityReviewProjection", StringComparison.Ordinal) < 0, "ReviewProjection leaked into the PlotQA JSON as a type reference.");

            // The proposal sidecar is serialized independently and never through the report compactor.
            Assert(typeof(PlotPlanQaReport).GetProperty("ReviewProjection")!.GetCustomAttribute<IgnoreDataMemberAttribute>() != null,
                "ReviewProjection lost its [IgnoreDataMember] attribute.");
        }

        // ---- 8. No DWG mutation surface -------------------------------------------------------------------

        private static void NoDrawingMutationSurface()
        {
            // The runtime and the sidecar writer expose no document, transaction, entity or write API.
            Assert(!typeof(PlacementProposalRuntime).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Any(m => IsDocumentMutation(m)), "PlacementProposalRuntime exposes a document-mutation method: " +
                string.Join(",", typeof(PlacementProposalRuntime).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(IsDocumentMutation).Select(m => m.Name)));
            Assert(!typeof(PlacementProposalSnapshotWriter).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Any(m => IsDocumentMutation(m)), "PlacementProposalSnapshotWriter exposes a document-mutation method: " +
                string.Join(",", typeof(PlacementProposalSnapshotWriter).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(IsDocumentMutation).Select(m => m.Name)));

            // Neither type accepts or returns a host document type.
            Assert(NoHostTypedMembers(typeof(PlacementProposalRunResult)), "PlacementProposalRunResult exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalRunDiagnostics)), "PlacementProposalRunDiagnostics exposes a host-typed member.");

            // The accepted proposal contract itself carries no mutation capability.
            Assert(NoHostTypedMembers(typeof(PlacementProposal)), "PlacementProposal exposes a host-typed member.");

            // Building proposals from a plot plan never touches the file system: it returns an in-memory set.
            PlotPlanModel plotPlan = PlotPlanWithRoadMeasurement(actualMeters: 10m);
            PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, GovernedRules(15m), null!, out PlacementProposalRunDiagnostics _);
            Assert(result.Proposals.All(p => p!.ProposedEasting != 0d || p.ProposedNorthing != 0d || p.OriginalEasting != 0d),
                "A proposal carried a zeroed position, indicating the evidence was not carried.");
        }

        // ---- 9. The command stays Core Console safe ------------------------------------------------------

        private static void CoreConsoleHostSafety()
        {
            // The accepted host boundary is reflected by name: it is internal, so the contract is checked by
            // shape rather than by a direct call from this assembly.
            Type? boundary = typeof(PlacementProposalRuntime).Assembly.GetType("EngiSentry.AutoCAD2021.AutoCADHostBoundary");
            Assert(boundary != null, "The accepted AutoCADHostBoundary type no longer exists.");
            MethodInfo? boundaryMethod = boundary!.GetMethod("IsCoreConsole", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert(boundaryMethod != null, "The accepted AutoCADHostBoundary.IsCoreConsole boundary no longer exists.");
            Assert(boundaryMethod!.ReturnType == typeof(bool), "The accepted host boundary changed its return type.");
            Assert(boundaryMethod.GetParameters().Length == 0, "The accepted host boundary changed its parameter contract.");

            // In the test harness the process is not accoreconsole, so the boundary reports false: the palette
            // short-circuit path is reachable only inside the real console. This mirrors the accepted boundary
            // without depending on its internal accessibility.
            Assert(Process.GetCurrentProcess().ProcessName != "accoreconsole",
                "The test harness is running inside accoreconsole, so the host boundary cannot be validated out of process.");

            // The command method is registered under the canonical ES_SUGGEST name and takes no arguments.
            // The CommandMethod attribute itself cannot be read out of process: materializing it — even as
            // metadata-only CustomAttributeData — resolves its accoremgd constructor, and the harness
            // intentionally does not ship the AutoCAD runtime (references stay Copy Local = false). The
            // command's compiled contract is asserted instead; the registration string is verified inside
            // the host, where the attribute is loadable.
            MethodInfo? command = typeof(Commands).GetMethod("Suggest", BindingFlags.Public | BindingFlags.Instance);
            Assert(command != null, "The ES_SUGGEST command method was not found on the command class.");
            Assert(command!.GetParameters().Length == 0, "The ES_SUGGEST command accepted parameters, changing its command contract.");
            Assert(command.ReturnType == typeof(void), "The ES_SUGGEST command changed its return type.");
            Assert(command.DeclaringType == typeof(Commands), "The ES_SUGGEST command left the accepted command class.");
            Assert(!command.IsAbstract && !command.IsVirtual, "The ES_SUGGEST command became overridable, changing its dispatch contract.");
            Assert(!command.IsStatic, "The ES_SUGGEST command became static, breaking the AutoCAD command convention.");
            Assert(command.IsPublic, "The ES_SUGGEST command stopped being public, so AutoCAD could not dispatch it.");

            // No new UI: the palette type is unchanged in this phase, and the runtime never references it.
            Assert(typeof(PlacementProposalRuntime).Assembly.GetType("EngiSentry.AutoCAD2021.VisualResultsPalette") != null,
                "The accepted palette type left the host assembly.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalRuntime)), "The runtime gained a host-typed member.");
        }

        // ---- Fixtures --------------------------------------------------------------------------------------

        private static List<StandardsPackRule> GovernedRules(decimal roadMinimumMeters = 15m, bool autoExecutionAllowed = true)
        {
            var rules = new List<StandardsPackRule>();
            rules.Add(ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, roadMinimumMeters, autoExecutionAllowed));
            rules.Add(ExecutableRule(EngineeringDistanceEvidenceKeys.SecurityFence, 12m, autoExecutionAllowed));
            rules.Add(ExecutableRule(EngineeringDistanceEvidenceKeys.UnitBoundary, 9m, autoExecutionAllowed));
            return rules;
        }

        private static StandardsPackRule ExecutableRule(string basisKey, decimal minimum, bool autoExecutionAllowed = true)
        {
            return new StandardsPackRule
            {
                RuleId = "pd.governed." + basisKey,
                PackId = "project.plotplan.criteria",
                MeasurementBasisKey = basisKey,
                ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                MinimumDistanceMeters = minimum,
                ConstraintTreatment = ConstraintTreatment.HardConstraint,
                AutoExecutionAllowed = autoExecutionAllowed,
                EngineeringReviewRequired = true,
                DecisionState = StandardDecisionStates.Pass
            };
        }

        private static PlotPlanModel EmptyPlotPlan(string insertionUnits = InsertionUnits, double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            return new PlotPlanModel
            {
                Drawing = new SemanticDrawing { FileName = "synthetic.dwg", InsertionUnits = insertionUnits },
                Geometry = FullGeometry(metersPerDrawingUnit),
                Measurements = new PlotSpatialMeasurementModel()
            };
        }

        private static PlotPlanModel PlotPlanWithRoadMeasurement(decimal actualMeters, string insertionUnits = InsertionUnits, double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            PlotPlanModel model = EmptyPlotPlan(insertionUnits, metersPerDrawingUnit);
            model.Measurements = new PlotSpatialMeasurementModel();
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurement(actualMeters, metersPerDrawingUnit));
            model.EquipmentPlacements.Add(PlacementFor(PlacementId, "tag:equipment-1"));
            return model;
        }

        /// <summary>Two distinct equipment placements at different clearances from the same road edge.</summary>
        private static PlotPlanModel PlotPlanWithTwoPlacements(double violatedMeters, double? violated2Meters = null, double? satisfiedMeters = null)
        {
            PlotPlanModel model = EmptyPlotPlan();
            model.Measurements = new PlotSpatialMeasurementModel();
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor("placement:equipment-1", "equipment-1", "T-1001", "measurement:road-1", violatedMeters));
            double second = violated2Meters ?? satisfiedMeters ?? 0d;
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor("placement:equipment-2", "equipment-2", "T-1002", "measurement:road-2", second));
            model.EquipmentPlacements.Add(PlacementFor("placement:equipment-1", "tag:equipment-1"));
            model.EquipmentPlacements.Add(PlacementFor("placement:equipment-2", "tag:equipment-2"));
            return model;
        }

        /// <summary>The same two placements, supplied in the opposite construction order.</summary>
        private static PlotPlanModel PlotPlanWithTwoPlacementsReversed(double violatedMeters, double violated2Meters)
        {
            PlotPlanModel model = EmptyPlotPlan();
            model.Measurements = new PlotSpatialMeasurementModel();
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor("placement:equipment-2", "equipment-2", "T-1002", "measurement:road-2", violated2Meters));
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor("placement:equipment-1", "equipment-1", "T-1001", "measurement:road-1", violatedMeters));
            model.EquipmentPlacements.Add(PlacementFor("placement:equipment-2", "tag:equipment-2"));
            model.EquipmentPlacements.Add(PlacementFor("placement:equipment-1", "tag:equipment-1"));
            return model;
        }

        /// <summary>One placement carrying two measurements on the same basis; the minimum governs.</summary>
        private static PlotPlanModel PlotPlanWithDuplicateMeasurements(double governingMeters, double fartherMeters)
        {
            PlotPlanModel model = EmptyPlotPlan();
            model.Measurements = new PlotSpatialMeasurementModel();
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor(PlacementId, EquipmentEntityId, NormalizedTag, "measurement:road-near", governingMeters));
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurementFor(PlacementId, EquipmentEntityId, NormalizedTag, "measurement:road-far", fartherMeters));
            model.EquipmentPlacements.Add(PlacementFor(PlacementId, "tag:equipment-1"));
            return model;
        }

        private static EquipmentPlacement PlacementFor(string placementId, string tagHandle)
        {
            return new EquipmentPlacement
            {
                PlacementId = placementId,
                EquipmentEntityId = placementId.StartsWith("placement:", StringComparison.Ordinal) ? placementId.Substring("placement:".Length) : placementId,
                TagSourceHandle = tagHandle,
                PlacementStatus = "Complete",
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 100m },
                Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 10m }
            };
        }

        private static EquipmentSpatialMeasurement RoadMeasurement(decimal actualMeters, double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            return RoadMeasurementFor(PlacementId, EquipmentEntityId, NormalizedTag, "measurement:road-1", (double)actualMeters, metersPerDrawingUnit);
        }

        /// <summary>
        /// One synthetic spatial measurement: equipment at the given clearance from the horizontal road edge. The
        /// measurement id, placement id, equipment entity and tag are parameters so a fixture can express several
        /// distinct placements without inventing a second geometry.
        /// </summary>
        private static EquipmentSpatialMeasurement RoadMeasurementFor(string placementId, string equipmentEntityId, string normalizedTag, string measurementId, double actualMeters, double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = measurementId,
                PlacementId = placementId,
                EquipmentEntityId = equipmentEntityId,
                NormalizedTag = normalizedTag,
                EquipmentEasting = Convert.ToDecimal(MetersToDrawingUnits(EquipmentEastingMeters, metersPerDrawingUnit), CultureInfo.InvariantCulture),
                EquipmentNorthing = Convert.ToDecimal(MetersToDrawingUnits(RoadNorthingMeters + actualMeters, metersPerDrawingUnit), CultureInfo.InvariantCulture),
                ConstraintId = "bounded-constraint:pd.governed." + EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                SubjectKind = "Road",
                LabelSourceHandle = RoadHandle + ":label",
                GeometrySourceHandle = RoadHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                ClosestPointX = MetersToDrawingUnits(EquipmentEastingMeters, metersPerDrawingUnit),
                ClosestPointY = MetersToDrawingUnits(RoadNorthingMeters, metersPerDrawingUnit),
                Distance2D = MetersToDrawingUnits(actualMeters, metersPerDrawingUnit),
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
            };
        }

        /// <summary>The bounded road constraint the governed projection builds for the fixture rules.</summary>
        private static BoundedConstraint RoadConstraintFrom(List<StandardsPackRule> rules)
        {
            BoundedConstraintSet constraints = BoundedConstraintBuilder.Build(rules, out BoundedConstraintDiagnostics _);
            BoundedConstraint? constraint = constraints.Constraints.FirstOrDefault(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
            Assert(constraint != null, "The governed projection produced no bounded road constraint.");
            return constraint!;
        }

        /// <summary>The accepted registry-shaped rule that the same fixture is representable through.</summary>
        private static EngineeringDistanceRuleDefinition RegistryRoadRule(decimal minimumMeters)
        {
            return new EngineeringDistanceRuleDefinition
            {
                RuleId = "pd.governed." + EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                MeasurementKind = "EquipmentToRoad2D",
                MinimumDistanceMeters = minimumMeters,
                RequiredMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                CriteriaProvenance = new EngineeringDistanceCriteriaProvenance
                {
                    SourceKind = "ProjectDistanceCriteria",
                    ProjectDirectory = AppContext.BaseDirectory,
                    CriteriaSettingKey = "AccessRoadEdgeMinimumMeters",
                    SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion,
                    ConfiguredMinimumDistanceMeters = minimumMeters
                }
            };
        }

        /// <summary>A bounded constraint whose rule identity is deliberately unrelated to the fixture's.</summary>
        private static BoundedConstraint ConstraintFromRule(StandardsPackRule rule, string ruleId)
        {
            StandardsPackRule withId = new StandardsPackRule
            {
                RuleId = ruleId,
                PackId = rule.PackId,
                MeasurementBasisKey = rule.MeasurementBasisKey,
                ApplicabilityKey = rule.ApplicabilityKey,
                MinimumDistanceMeters = rule.MinimumDistanceMeters,
                ConstraintTreatment = rule.ConstraintTreatment,
                AutoExecutionAllowed = rule.AutoExecutionAllowed,
                EngineeringReviewRequired = rule.EngineeringReviewRequired,
                DecisionState = rule.DecisionState
            };
            return new BoundedConstraint
            {
                ConstraintId = "bounded-constraint:" + ruleId,
                Rule = withId,
                ConstraintTreatment = rule.ConstraintTreatment ?? ConstraintTreatment.HardConstraint,
                MeasurementKind = "EquipmentToRoad2D",
                RequiredMeasurementBasisKey = rule.MeasurementBasisKey ?? string.Empty,
                RequiredApplicabilityKey = rule.ApplicabilityKey ?? string.Empty,
                MinimumDistanceMeters = rule.MinimumDistanceMeters,
                UnitContext = null
            };
        }

        /// <summary>The accepted measurement one placement recorded, whichever basis it came from.</summary>
        private static EquipmentSpatialMeasurement MeasurementForPlacement(PlotPlanModel plotPlan, string placementId)
        {
            EquipmentSpatialMeasurement? measurement = plotPlan.Measurements?.EquipmentSpatialMeasurements
                .FirstOrDefault(x => x != null && string.Equals(x.PlacementId, placementId, StringComparison.Ordinal));
            Assert(measurement != null, "The fixture lost its measurement for " + placementId + ".");
            return measurement!;
        }

        /// <summary>
        /// Asserts one proposal carries its placement's accepted evidence: identity, tag, governing measurement,
        /// original position and geometry evidence. Per-placement fidelity is the property this phase protects.
        /// </summary>
        private static void AssertProposalCarriesPlacementEvidence(PlacementProposal? proposal, PlotPlanModel plotPlan, string placementId)
        {
            Assert(proposal != null, "A proposal for " + placementId + " was null.");
            EquipmentSpatialMeasurement measurement = MeasurementForPlacement(plotPlan, placementId);
            EquipmentPlacement? placement = plotPlan.EquipmentPlacements.FirstOrDefault(x => string.Equals(x.PlacementId, placementId, StringComparison.Ordinal));

            Assert(proposal!.ObservedMeasurementId == measurement.MeasurementId,
                "The proposal for " + placementId + " did not carry its governing measurement identity.");
            Assert(proposal.ViolatedRule != null && proposal.ViolatedRule!.RuleId == "pd.governed." + EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                "The proposal for " + placementId + " lost its violated rule identity.");
            Assert(proposal.MeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                "The proposal for " + placementId + " lost its measurement basis key.");
            Assert(proposal.MeasurementApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment,
                "The proposal for " + placementId + " lost its applicability key.");
            Assert(Math.Abs(proposal.OriginalNorthing - (double)measurement.EquipmentNorthing) < 1e-6d,
                "The proposal for " + placementId + " lost its original equipment position.");
            Assert(Math.Abs(proposal.OriginalEasting - (double)measurement.EquipmentEasting) < 1e-6d,
                "The proposal for " + placementId + " lost its original equipment easting.");
            Assert(placement != null && placement!.PlacementStatus == "Complete",
                "The proposal's placement status was not carried from the accepted plot plan.");
        }

        private static PlotGeometryModel FullGeometry(double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            var model = new PlotGeometryModel();
            model.Primitives.Add(HorizontalPrimitive(RoadHandle, RoadNorthingMeters, -SpanMeters, SpanMeters, metersPerDrawingUnit));
            model.Primitives.Add(HorizontalPrimitive(FenceHandle, FenceNorthingMeters, -SpanMeters, SpanMeters, metersPerDrawingUnit));
            model.Primitives.Add(HorizontalPrimitive(BoundaryHandle, FenceNorthingMeters + 20d, -SpanMeters, SpanMeters, metersPerDrawingUnit));
            return model;
        }

        private static PlotGeometryPrimitive HorizontalPrimitive(string handle, double northingMeters, double westMeters, double eastMeters, double metersPerDrawingUnit = MetersPerDrawingUnit)
        {
            return new PlotGeometryPrimitive
            {
                SourceHandle = handle,
                SourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                Closed = false,
                Vertices = new List<PlotGeometryVertex>
                {
                    new PlotGeometryVertex { X = MetersToDrawingUnits(westMeters, metersPerDrawingUnit), Y = MetersToDrawingUnits(northingMeters, metersPerDrawingUnit), Z = 0d },
                    new PlotGeometryVertex { X = MetersToDrawingUnits(eastMeters, metersPerDrawingUnit), Y = MetersToDrawingUnits(northingMeters, metersPerDrawingUnit), Z = 0d }
                }
            };
        }

        /// <summary>Two distinct synthetic proposals, built so their identity ordering is stable and
        /// independent of insertion order.</summary>
        private static List<PlacementProposal> EligibleProposals()
        {
            return new List<PlacementProposal>
            {
                SyntheticProposal("proposal:placement:constraint:zeta", 15m, 10d, 15d),
                SyntheticProposal("proposal:placement:constraint:alpha", 15m, 10d, 16d)
            };
        }

        private static PlacementProposal SyntheticProposal(string proposalId, decimal requiredMeters, double observedMeters, double proposedNorthingMeters)
        {
            return new PlacementProposal
            {
                ProposalId = proposalId,
                ViolatedRule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, requiredMeters),
                ObservedDistanceMeters = observedMeters,
                ObservedMeasurementBasis = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                ObservedMeasurementId = "measurement:road-1",
                RequiredDistanceMeters = requiredMeters,
                MeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                MeasurementApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                UnitContext = InsertionUnits,
                OriginalEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                OriginalNorthing = MetersToDrawingUnits(observedMeters),
                ProposedEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                ProposedNorthing = MetersToDrawingUnits(proposedNorthingMeters),
                MovementDistanceMeters = proposedNorthingMeters - observedMeters,
                MovementDirection = "N",
                EngineeringReviewRequired = true,
                EngineeringReviewReason = "Synthetic fixture: review is mandatory in v1.",
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                KnownLimitations = new List<string>(ProposalKnownLimitations.Defaults)
            };
        }

        // ---- Sidecar helpers ------------------------------------------------------------------------------

        /// <summary>Removes a transient sidecar the test wrote into the accepted artifacts root.</summary>
        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        // ---- Shared helpers ------------------------------------------------------------------------------------------

        private static double MetersToDrawingUnits(double meters, double metersPerDrawingUnit = MetersPerDrawingUnit) => meters / metersPerDrawingUnit;

        private static string SerializeReport(PlotPlanQaReport report)
        {
            var serializer = new DataContractJsonSerializer(typeof(PlotPlanQaReport));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, report);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static PlotPlanQaReport DeserializeReport(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(PlotPlanQaReport));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (PlotPlanQaReport)serializer.ReadObject(stream);
            }
        }

        private static bool IsDocumentMutation(MethodInfo method)
        {
            string name = method.Name;
            if (name.IndexOf("Write", StringComparison.Ordinal) >= 0) return false; // sidecar writes are file I/O, not DWG mutation
            return name.IndexOf("Document", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("Transaction", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("Modify", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("Move", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("Erase", StringComparison.Ordinal) >= 0 ||
                   name.IndexOf("Set", StringComparison.Ordinal) >= 0;
        }

        /// <summary>True when no field, property, parameter or return type of <paramref name="type"/> comes
        /// from an Autodesk assembly — the read-only contract must not touch the host object model.</summary>
        private static bool NoHostTypedMembers(Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                if (IsHostAssembly(field.FieldType)) return false;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (IsHostAssembly(property.PropertyType)) return false;
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (IsHostAssembly(method.ReturnType)) return false;
                foreach (ParameterInfo parameter in method.GetParameters())
                    if (IsHostAssembly(parameter.ParameterType)) return false;
            }
            return true;
        }

        private static bool IsHostAssembly(Type type)
        {
            string? name = type.Assembly.GetName().Name;
            return !string.IsNullOrEmpty(name) && (name.StartsWith("ac", StringComparison.Ordinal) || name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ReferencesAssembly(Assembly assembly, string targetName)
        {
            return assembly.GetReferencedAssemblies().Any(r => (r.Name ?? string.Empty) == targetName);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
