using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Phase D tests: the canonical explainable SUGGEST path — deterministic candidate generation from a proven
    /// executable governed violation, re-evaluation of every candidate against all bounded constraints,
    /// deterministic ranking, and read-only PlacementProposal composition (Phase A architecture G/H/I/J).
    ///
    /// Every fixture in this class is synthetic and stays inside this class. No production catalog, source,
    /// clause, approval, authority, hash or distance is created or altered, and no company standard is named
    /// or remembered. Synthetic numerical values are permitted here only because they never reach a production
    /// pack. This class does not re-test the P6/P7/P8/P9 governance chain, the Phase C projection, or the
    /// accepted measurement pipeline itself; that coverage belongs to the existing test classes. Follows the
    /// existing harness convention (internal static Run + Assert).
    /// </summary>
    internal static class PlacementProposalTests
    {
        // Millimetres: 1 drawing unit = 0.001 m. Chosen so metre/drawing-unit conversion is exact and visible.
        private const string InsertionUnits = "Millimeters";
        private const double MetersPerDrawingUnit = 0.001d;

        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const string FenceHandle = "fence:geometry";
        private const string BoundaryHandle = "boundary:geometry";

        internal static void Run()
        {
            ContractSafetyAndLayering();
            CandidateGeneration();
            ObliqueDisplacementVector();
            RefusalConditions();
            ReEvaluation();
            Ranking();
            AllMandatoryViolationsRanking();
            ProposalComposition();
            Determinism();
            SameContextDeterminism();
        }

        // ---- 1. Contract safety and dependency layering ----------------------------------------------

        private static void ContractSafetyAndLayering()
        {
            // Phase D services live in the constraint engine, which must not reference the standards
            // projection assembly; no circular project reference is introduced by Phase D.
            Assert(NoHostReference(typeof(PlacementProposalCandidateGenerator).Assembly), "EngiSentry.Rules references a host/Autodesk assembly.");
            Assert(!ReferencesAssembly(typeof(PlacementProposalCandidateGenerator).Assembly, "EngiSentry.Standards"),
                "EngiSentry.Rules references EngiSentry.Standards, reversing the accepted dependency direction.");
            Assert(ReferencesAssembly(typeof(PlacementProposalCandidateGenerator).Assembly, "EngiSentry.PlotPlan"),
                "EngiSentry.Rules no longer references EngiSentry.PlotPlan.");

            // The accepted Phase B proposal contracts carry no host-typed members.
            Assert(NoHostTypedMembers(typeof(PlacementProposal)), "PlacementProposal exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalCandidate)), "PlacementProposalCandidate exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalRankingSummary)), "PlacementProposalRankingSummary exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalConstraintOutcome)), "PlacementProposalConstraintOutcome exposes a host-typed member.");

            // The mandatory v1 lists are non-empty by construction; the point-based limitation is carried verbatim.
            Assert(ProposalAssumptions.Defaults.Length > 0, "The default assumptions list is empty.");
            Assert(ProposalUncheckedConstraints.Defaults.Length > 0, "The default unchecked-constraints list is empty.");
            Assert(ProposalKnownLimitations.Defaults.Length > 0, "The default known-limitations list is empty.");
            Assert(ProposalKnownLimitations.Defaults.Contains(ProposalKnownLimitations.PointBasedLimitation),
                "The point-based limitation is not carried by default.");

            // A fresh candidate requires engineering review and never claims a final position.
            var fresh = new PlacementProposalCandidate();
            Assert(fresh.EngineeringReviewRequired, "A fresh candidate does not require engineering review.");
            Assert(fresh.CheckedConstraints != null && fresh.CheckedConstraints.Count == 0, "A fresh candidate is not initialized empty.");
            Assert(fresh.UncheckedConstraints != null && fresh.Assumptions != null, "A fresh candidate lost its mandatory lists.");

            // The accepted state vocabulary is closed; Phase D lives only on the new proposal types.
            Assert(ProposalConstraintState.IsSupported(ProposalConstraintState.Satisfied), "SATISFIED is not a supported state.");
            Assert(ProposalConstraintState.IsSupported(ProposalConstraintState.InsufficientEvidence), "INSUFFICIENT_EVIDENCE is not a supported state.");
            Assert(!ProposalConstraintState.IsSupported("PARTIALLY_COMPLIANT"), "An unsupported state was accepted.");

            // The v1 margin is minimum movement.
            Assert(PlacementProposalCandidateGenerator.MarginMeters == 0d, "The v1 margin is not zero (minimum movement).");
            Assert(PlacementProposalCandidateGenerator.AdditionalIncrementsMeters.Length > 0, "The incremental candidate set is empty.");
            Assert(PlacementProposalCandidateGenerator.CardinalDirections.Length == 8, "The cardinal fallback is not the 8-direction set.");
        }

        // ---- 2. Deterministic candidate generation ------------------------------------------------------

        private static void CandidateGeneration()
        {
            ViolationContext context = RoadViolationContext(minimumMeters: 15m, actualMeters: 10m);
            GenerationResult result = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics diagnostics);

            Assert(diagnostics.Refused == false && diagnostics.RefusalReason == null, "An executable violation was refused.");
            Assert(result.Candidates.Count >= 1, "No candidate was generated for an executable violation.");
            Assert(diagnostics.GeneratedCount == result.Candidates.Count, "The generation diagnostics did not count the candidates.");

            // The exact-minimum displacement candidate: 15 - 10 = 5 m of additional clearance.
            PlacementProposalCandidate exact = result.Candidates[0];
            Assert(exact.DisplacementDistance == MetersToDrawingUnits(5d), "The base candidate displacement is not the exact minimum: " + exact.DisplacementDistance.ToString(CultureInfo.InvariantCulture) + ".");
            Assert(exact.OriginalEasting == context.EquipmentEasting, "The candidate lost its original easting.");
            Assert(exact.OriginalNorthing == context.EquipmentNorthing, "The candidate lost its original northing.");
            Assert(exact.PlacementId == PlacementId, "The candidate lost its placement identity.");
            Assert(exact.EquipmentEntityId == EquipmentEntityId, "The candidate lost its equipment identity.");
            Assert(exact.NormalizedTag == NormalizedTag, "The candidate lost its normalized tag.");
            Assert(exact.GeneratingConstraintId == context.Violation.ConstraintId, "The candidate lost its generating constraint identity.");
            Assert(exact.GeneratingRuleId == context.Rule.RuleId, "The candidate lost its generating rule identity.");
            Assert(exact.EngineeringReviewRequired, "A generated candidate stopped requiring engineering review.");
            Assert(exact.UncheckedConstraints.Count > 0, "A generated candidate carried no unchecked constraints.");
            Assert(exact.Assumptions.Count > 0, "A generated candidate carried no assumptions.");

            // The direction is the unit vector from the closest point toward the equipment point.
            Assert(exact.DisplacementDirection == ExpectedDirection(context), "The base candidate direction is not the recorded-evidence direction.");
            Assert(exact.DisplacementX == exact.ProposedEasting - exact.OriginalEasting, "The candidate displacement X is inconsistent.");
            Assert(exact.DisplacementY == exact.ProposedNorthing - exact.OriginalNorthing, "The candidate displacement Y is inconsistent.");

            // The proposed point actually achieves compliance against the generating geometry.
            double proposedDistance = PointToPolylineDistance.PointToPolyline(exact.ProposedEasting, exact.ProposedNorthing, context.ViolatingGeometry!);
            Assert(DrawingUnitsToMeters(proposedDistance) >= (double)context.Rule.MinimumDistanceMeters,
                "The proposed point does not achieve compliance: " + DrawingUnitsToMeters(proposedDistance).ToString(CultureInfo.InvariantCulture) + " m.");

            // The incremental candidates move strictly further along the same direction.
            for (int i = 1; i < result.Candidates.Count; i++)
            {
                Assert(result.Candidates[i].DisplacementDistance > result.Candidates[i - 1].DisplacementDistance,
                    "The incremental candidates are not ordered by ascending displacement at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
                Assert(result.Candidates[i].DisplacementDirection == exact.DisplacementDirection,
                    "An incremental candidate changed direction at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
            }

            // Candidate identity is deterministic and derived, never random or time-based.
            Assert(result.Candidates.Select(x => x.CandidateId).Distinct(StringComparer.Ordinal).Count() == result.Candidates.Count,
                "Candidate identities are not unique.");
            Assert(result.Candidates[0].CandidateId.IndexOf(PlacementId, StringComparison.Ordinal) >= 0, "Candidate identity does not derive from the placement.");
            Assert(result.Candidates[0].CandidateId.IndexOf(context.Violation.ConstraintId, StringComparison.Ordinal) >= 0, "Candidate identity does not derive from the generating constraint.");
        }

        // ---- 2b. Exact (non-quantized) displacement vector ---------------------------------------------

        private static void ObliqueDisplacementVector()
        {
            // The equipment sits at (30, 40) m from the recorded closest point: a 3-4-5 triangle, so the exact
            // unit vector is (0.6, 0.8). That is neither axis-aligned nor at 45 degrees, so no member of the
            // 8-direction set can reproduce it. Phase A H.1 step 3 requires this exact vector; the recorded
            // direction label is only a human-readable approximation.
            ViolationContext context = ObliqueViolationContext(minimumMeters: 15m, actualMeters: 10m);
            GenerationResult result = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics diagnostics);

            Assert(diagnostics.Refused == false, "An oblique executable violation was refused.");
            PlacementProposalCandidate exact = result.Candidates[0];
            Assert(exact.DisplacementDistance == MetersToDrawingUnits(5d), "The base candidate displacement is not the exact minimum: " + exact.DisplacementDistance.ToString(CultureInfo.InvariantCulture) + ".");

            // The exact normalized components, with no quantization to the 8-direction set.
            Assert(exact.DisplacementX == MetersToDrawingUnits(3d), "The displacement X is not the exact vector component (0.6 * 5 m = 3 m): " + exact.DisplacementX.ToString(CultureInfo.InvariantCulture) + ".");
            Assert(exact.DisplacementY == MetersToDrawingUnits(4d), "The displacement Y is not the exact vector component (0.8 * 5 m = 4 m): " + exact.DisplacementY.ToString(CultureInfo.InvariantCulture) + ".");
            Assert(IsApproximately(exact.DisplacementX / exact.DisplacementDistance, 0.6d), "The displacement X is not the exact unit vector component 0.6.");
            Assert(IsApproximately(exact.DisplacementY / exact.DisplacementDistance, 0.8d), "The displacement Y is not the exact unit vector component 0.8.");

            // Proof that the vector is not reproducible by any 8-direction quantization.
            Assert(exact.DisplacementX != 0d && exact.DisplacementY != 0d, "The displacement vector is axis-aligned.");
            Assert(exact.DisplacementX != exact.DisplacementY, "The displacement vector lies on a 45-degree diagonal.");

            // The label is the approximate cardinal/intercardinal text and must not drive the geometry.
            Assert(exact.DisplacementDirection == "NE", "The human-readable direction label is not the expected approximation.");
            Assert(exact.ProposedEasting == context.EquipmentEasting + MetersToDrawingUnits(3d), "The proposed easting did not use the exact vector.");
            Assert(exact.ProposedNorthing == context.EquipmentNorthing + MetersToDrawingUnits(4d), "The proposed northing did not use the exact vector.");

            // The proposed point actually achieves compliance, measured through the accepted algorithm.
            double observed = PointToPolylineDistance.PointToPolyline(exact.ProposedEasting, exact.ProposedNorthing, context.ViolatingGeometry!);
            Assert(DrawingUnitsToMeters(observed) >= (double)context.Rule.MinimumDistanceMeters,
                "The oblique proposed point does not achieve compliance: " + DrawingUnitsToMeters(observed).ToString(CultureInfo.InvariantCulture) + " m.");

            // Every additional candidate follows the same exact vector, only further along it.
            foreach (PlacementProposalCandidate candidate in result.Candidates)
            {
                Assert(IsApproximately(candidate.DisplacementX / candidate.DisplacementDistance, 0.6d), "A candidate left the exact displacement vector.");
                Assert(IsApproximately(candidate.DisplacementY / candidate.DisplacementDistance, 0.8d), "A candidate left the exact displacement vector.");
            }
        }

        // ---- 3. Refusal conditions (Phase A H.3) ---------------------------------------------------------

        private static void RefusalConditions()
        {
            // A non-auto-executable rule never generates a candidate.
            StandardsPackRule blockedRule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m);
            blockedRule.AutoExecutionAllowed = false;
            blockedRule.DecisionState = StandardDecisionStates.ExpertReviewRequired;
            ViolationContext blockedContext = ContextWithRule(blockedRule, EngineeringDistanceEvidenceKeys.AccessRoadEdge, 10m);
            GenerationResult blocked = PlacementProposalCandidateGenerator.Generate(blockedContext, out GenerationDiagnostics blockedDiagnostics);
            Assert(blocked.Candidates.Count == 0, "A non-auto-executable rule generated a candidate.");
            Assert(blockedDiagnostics.Refused && !string.IsNullOrEmpty(blockedDiagnostics.RefusalReason), "A non-auto-executable rule was not refused with a reason.");

            // A quarantined source never generates a candidate, even when numerically violated.
            StandardsPackRule quarantinedRule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m);
            quarantinedRule.AuthorityClass = StandardAuthorityClasses.Quarantined;
            ViolationContext quarantinedContext = ContextWithRule(quarantinedRule, EngineeringDistanceEvidenceKeys.AccessRoadEdge, 10m);
            Assert(PlacementProposalCandidateGenerator.Generate(quarantinedContext, out GenerationDiagnostics qd).Candidates.Count == 0,
                "A quarantined rule generated a candidate.");

            // A review-required treatment never generates a candidate.
            StandardsPackRule reviewRule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m);
            reviewRule.ConstraintTreatment = ConstraintTreatment.ReviewRequired;
            ViolationContext reviewContext = ContextWithRule(reviewRule, EngineeringDistanceEvidenceKeys.AccessRoadEdge, 10m);
            Assert(PlacementProposalCandidateGenerator.Generate(reviewContext, out GenerationDiagnostics rd).Candidates.Count == 0,
                "A review-required rule generated a candidate.");

            // An incomplete placement never generates a candidate.
            ViolationContext incompleteContext = RoadViolationContext(15m, 10m);
            incompleteContext.PlacementStatus = "Incomplete";
            Assert(PlacementProposalCandidateGenerator.Generate(incompleteContext, out GenerationDiagnostics id_).Candidates.Count == 0,
                "An incomplete placement generated a candidate.");

            // An ambiguous placement never generates a candidate.
            ViolationContext ambiguousContext = RoadViolationContext(15m, 10m);
            ambiguousContext.PlacementStatus = "Ambiguous";
            Assert(PlacementProposalCandidateGenerator.Generate(ambiguousContext, out GenerationDiagnostics ad).Candidates.Count == 0,
                "An ambiguous placement generated a candidate.");

            // Unresolved subject geometry never generates a candidate.
            ViolationContext unresolvedContext = RoadViolationContext(15m, 10m);
            unresolvedContext.ViolatingGeometry = null;
            Assert(PlacementProposalCandidateGenerator.Generate(unresolvedContext, out GenerationDiagnostics ud).Candidates.Count == 0,
                "Unresolved geometry generated a candidate.");

            // A missing evidence handle never generates a candidate.
            ViolationContext noHandleContext = RoadViolationContext(15m, 10m);
            noHandleContext.Violation.GeometrySourceHandle = string.Empty;
            Assert(PlacementProposalCandidateGenerator.Generate(noHandleContext, out GenerationDiagnostics hd).Candidates.Count == 0,
                "A missing geometry handle generated a candidate.");

            // A degenerate direction falls back to the deterministic cardinal set rather than refusing.
            ViolationContext degenerateContext = RoadViolationContext(15m, 10m);
            degenerateContext.Violation.ClosestPointX = degenerateContext.EquipmentEasting!.Value;
            degenerateContext.Violation.ClosestPointY = degenerateContext.EquipmentNorthing!.Value;
            GenerationResult degenerate = PlacementProposalCandidateGenerator.Generate(degenerateContext, out GenerationDiagnostics dd);
            Assert(dd.Refused == false, "A degenerate direction was refused instead of falling back to the cardinal set.");
            Assert(degenerate.Candidates.Count >= 1, "The cardinal fallback produced no candidate.");
            Assert(degenerate.Candidates.All(x => PlacementProposalCandidateGenerator.CardinalDirections.Contains(x.DisplacementDirection, StringComparer.Ordinal)),
                "A degenerate-direction candidate did not use the cardinal fallback.");
        }

        // ---- 4. Re-evaluation (Phase A I) -----------------------------------------------------------------

        private static void ReEvaluation()
        {
            // Every bounded constraint is re-evaluated, not only the generating one.
            ReEvaluationContext context = FullReEvaluationContext();
            PlacementProposalCandidate candidate = SimpleCandidate();

            PlacementProposalReEvaluator.ReEvaluate(candidate, context, out ReEvaluationDiagnostics diagnostics);

            Assert(diagnostics.ConstraintCount == context.Constraints.Count, "Not all bounded constraints were re-evaluated.");
            Assert(candidate.CheckedConstraints.Count == context.Constraints.Count, "Re-evaluation did not retain every constraint outcome.");
            Assert(candidate.CheckedConstraints.Select(x => x.ConstraintId).SequenceEqual(candidate.CheckedConstraints.Select(x => x.ConstraintId).OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal),
                "Re-evaluation outcomes are not deterministically ordered.");

            // Road re-evaluation: the candidate sits 20 m from the road primitive against a 15 m criterion.
            PlacementProposalConstraintOutcome? road = candidate.CheckedConstraints.FirstOrDefault(x => x.MeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
            Assert(road != null, "The road constraint was not re-evaluated.");
            Assert(road!.EvaluationState == ProposalConstraintState.Satisfied, "A compliant road re-evaluation was not SATISFIED.");
            Assert(road.ActualDistanceMeters != null, "A satisfied road outcome reported no distance.");

            // Plant Fence re-evaluation: the candidate sits 5 m from the fence primitive against a 12 m criterion.
            PlacementProposalConstraintOutcome? fence = candidate.CheckedConstraints.FirstOrDefault(x => x.MeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence);
            Assert(fence != null, "The plant fence constraint was not re-evaluated.");
            Assert(fence!.EvaluationState == ProposalConstraintState.Violated, "A non-compliant fence re-evaluation was not VIOLATED.");
            Assert(fence.ActualDistanceMeters < (double)fence.RequiredDistanceMeters, "A violated fence outcome did not report a below-criterion distance.");

            // Unit Boundary re-evaluation: no resolved subject geometry, so INSUFFICIENT_EVIDENCE, never SATISFIED.
            PlacementProposalConstraintOutcome? boundary = candidate.CheckedConstraints.FirstOrDefault(x => x.MeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary);
            Assert(boundary != null, "The unit boundary constraint was not re-evaluated.");
            Assert(boundary!.EvaluationState == ProposalConstraintState.InsufficientEvidence, "An unresolved basis became a verdict instead of INSUFFICIENT_EVIDENCE.");
            Assert(boundary.ActualDistanceMeters == null, "An insufficient-evidence outcome reported a distance.");

            // INSUFFICIENT_EVIDENCE is counted visibly, never collapsed to zero or hidden.
            Assert(diagnostics.InsufficientEvidenceCount == 1, "The insufficient-evidence count was not retained visibly.");
            Assert(diagnostics.SatisfiedCount == 1 && diagnostics.ViolatedCount == 1, "The re-evaluation state counts are wrong.");

            // A basis outside the bounded taxonomy is NOT_APPLICABLE, never a pass.
            ReEvaluationContext outOfTaxonomyContext = new ReEvaluationContext
            {
                Constraints = new[] { OutOfTaxonomyConstraint() },
                UnitResolution = UnitResolution(),
                Geometry = FullGeometry()
            };
            PlacementProposalCandidate outOfTaxonomyCandidate = SimpleCandidate();
            PlacementProposalReEvaluator.ReEvaluate(outOfTaxonomyCandidate, outOfTaxonomyContext, out ReEvaluationDiagnostics otd);
            Assert(outOfTaxonomyCandidate.CheckedConstraints[0].EvaluationState == ProposalConstraintState.NotApplicable,
                "An out-of-taxonomy constraint was not NOT_APPLICABLE.");

            // Governance is never changed by re-evaluation: a blocked rule stays REVIEW_REQUIRED even with full evidence.
            ReEvaluationContext blockedContext = new ReEvaluationContext
            {
                Constraints = new[] { BlockedConstraint(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m) },
                UnitResolution = UnitResolution(),
                Geometry = FullGeometry()
            };
            PlacementProposalCandidate blockedCandidate = SimpleCandidate();
            PlacementProposalReEvaluator.ReEvaluate(blockedCandidate, blockedContext, out ReEvaluationDiagnostics bd);
            Assert(blockedCandidate.CheckedConstraints[0].EvaluationState == ProposalConstraintState.ReviewRequired,
                "Re-evaluation altered the governance state of a blocked rule.");
            Assert(blockedCandidate.CheckedConstraints[0].ActualDistanceMeters == null, "A review-required outcome reported a distance.");

            // Evidence handles on outcomes are subsets of the existing source handles.
            foreach (PlacementProposalConstraintOutcome outcome in candidate.CheckedConstraints)
            {
                Assert(outcome.EvidenceHandles.All(x => ExistingHandles.Contains(x, StringComparer.Ordinal)),
                    "A re-evaluation outcome carried an evidence handle that is not an existing source handle.");
            }
        }

        // ---- 5. Ranking (Phase A J) -------------------------------------------------------------------------

        private static void Ranking()
        {
            // A candidate violating a mandatory constraint must never outrank one that does not.
            PlacementProposalCandidate clean = Candidate("candidate:clean", violated: false, displacementMeters: 4d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate mandatoryViolator = Candidate("candidate:violator", violated: true, displacementMeters: 1d, treatment: ConstraintTreatment.HardConstraint);

            RankingResult result = PlacementProposalRanker.Rank(new[] { mandatoryViolator, clean }, out RankingDiagnostics diagnostics);
            Assert(result.OrderedCandidates[0].CandidateId == "candidate:clean", "A mandatory violator outranked a compliant candidate.");
            Assert(result.Summary.WinningCandidateId == "candidate:clean", "The winner is not the compliant candidate.");
            Assert(result.Summary.RejectedCandidates.Any(x => x.CandidateId == "candidate:violator"), "A mandatory violator was not recorded as rejected.");
            Assert(diagnostics.MandatoryViolationCandidateCount == 1, "The mandatory-violation count was not retained.");

            // Minimum movement is the declared objective among compliant candidates.
            PlacementProposalCandidate near = Candidate("candidate:near", violated: false, displacementMeters: 3d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate far = Candidate("candidate:far", violated: false, displacementMeters: 9d, treatment: ConstraintTreatment.HardConstraint);
            RankingResult movement = PlacementProposalRanker.Rank(new[] { far, near }, out RankingDiagnostics md);
            Assert(movement.OrderedCandidates[0].CandidateId == "candidate:near", "Minimum-movement ranking did not prefer the smaller displacement.");

            // Unresolved states stay visible and are never collapsed to zero.
            PlacementProposalCandidate unresolved = Candidate("candidate:unresolved", violated: false, displacementMeters: 2d, treatment: ConstraintTreatment.HardConstraint, unresolved: true);
            RankingResult unresolvedResult = PlacementProposalRanker.Rank(new[] { unresolved, near }, out RankingDiagnostics ud);
            Assert(unresolvedResult.Summary.InsufficientEvidenceCount > 0 || unresolvedResult.Summary.ReviewRequiredCount > 0,
                "An unresolved state was collapsed or hidden by ranking.");
            Assert(unresolvedResult.OrderedCandidates[0].CandidateId == "candidate:near", "An unresolved candidate outranked a resolved one.");

            // A non-mandatory treatment does not count as a mandatory violation.
            PlacementProposalCandidate preferenceViolator = Candidate("candidate:preference", violated: true, displacementMeters: 1d, treatment: ConstraintTreatment.EngineeringPreference);
            RankingResult preference = PlacementProposalRanker.Rank(new[] { preferenceViolator, near }, out RankingDiagnostics pd);
            Assert(preference.OrderedCandidates[0].CandidateId == "candidate:preference" || preference.OrderedCandidates[0].CandidateId == "candidate:near",
                "A preference violation was wrongly treated as a mandatory violation.");

            // Ranking changes ordering only: outcomes are untouched.
            List<PlacementProposalConstraintOutcome> before = far.CheckedConstraints.ToList();
            PlacementProposalRanker.Rank(new[] { far, near }, out RankingDiagnostics _);
            Assert(far.CheckedConstraints.SequenceEqual(before), "Ranking modified a constraint outcome.");

            // The tie-break is fully deterministic: an equal mandatory tier, equal movement and an equal
            // proposed position resolve by generating constraint id then candidate id, never by input order.
            PlacementProposalCandidate tieZ = Candidate("candidate:tie-z", violated: false, displacementMeters: 4d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate tieA = Candidate("candidate:tie-a", violated: false, displacementMeters: 4d, treatment: ConstraintTreatment.HardConstraint);
            tieZ.GeneratingConstraintId = "bounded-constraint:pd.test.same";
            tieA.GeneratingConstraintId = "bounded-constraint:pd.test.same";
            RankingResult tie = PlacementProposalRanker.Rank(new[] { tieZ, tieA }, out RankingDiagnostics td);
            Assert(tie.OrderedCandidates[0].CandidateId == "candidate:tie-a", "The tie-break did not resolve by candidate id.");
            Assert(tie.Summary.OrderedCandidateIds.SequenceEqual(new[] { "candidate:tie-a", "candidate:tie-z" }, StringComparer.Ordinal),
                "The tie-break ordering is not deterministic.");

            // At candidate-id equality, the lower generating constraint id wins.
            PlacementProposalCandidate constraintTieHigh = Candidate("candidate:tie-c", violated: false, displacementMeters: 4d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate constraintTieLow = Candidate("candidate:tie-c", violated: false, displacementMeters: 4d, treatment: ConstraintTreatment.HardConstraint);
            constraintTieHigh.GeneratingConstraintId = "bounded-constraint:pd.test.z";
            constraintTieLow.GeneratingConstraintId = "bounded-constraint:pd.test.a";
            RankingResult constraintTie = PlacementProposalRanker.Rank(new[] { constraintTieHigh, constraintTieLow }, out RankingDiagnostics ctd);
            Assert(constraintTie.OrderedCandidates[0] == constraintTieLow, "The tie-break did not resolve by generating constraint id.");

            // An empty candidate set yields an empty ranking with no winner, not an exception.
            RankingResult empty = PlacementProposalRanker.Rank(Array.Empty<PlacementProposalCandidate>(), out RankingDiagnostics ed);
            Assert(empty.OrderedCandidates.Count == 0 && empty.Summary.WinningCandidateId == null, "An empty candidate set produced a winner.");
        }

        // ---- 5b. Rejection consistency when every candidate has a mandatory violation --------------------

        private static void AllMandatoryViolationsRanking()
        {
            // No candidate is free of a mandatory violation, so none can be dropped in favour of a compliant
            // alternative: the least-violating candidate is presented with its violations visible. This is the
            // case where the previous implementation recorded the winner as also rejected.
            PlacementProposalCandidate nearViolator = Candidate("candidate:near-violator", violated: true, displacementMeters: 2d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate farViolator = Candidate("candidate:far-violator", violated: true, displacementMeters: 4d, treatment: ConstraintTreatment.SafetyConstraint);

            RankingResult allViolations = PlacementProposalRanker.Rank(new[] { farViolator, nearViolator }, out RankingDiagnostics avd);

            // Ordering still follows the full Phase A J policy: equal mandatory tier, then minimum movement.
            Assert(allViolations.OrderedCandidates.Count == 2, "A candidate was dropped when no compliant alternative existed.");
            Assert(allViolations.OrderedCandidates[0].CandidateId == "candidate:near-violator", "Minimum-movement ranking did not apply within the mandatory-violation tier.");
            Assert(allViolations.Summary.WinningCandidateId == "candidate:near-violator", "The winner is not the first presented candidate.");

            // No candidate is rejected, because none was actually dropped.
            Assert(allViolations.Summary.RejectedCandidates.Count == 0, "A candidate was rejected without being dropped.");
            Assert(avd.MandatoryViolationCandidateCount == 2, "The mandatory-violation count was not retained visibly.");
            Assert(NoRejectedCandidateIsPresented(allViolations), "A presented candidate was also recorded as rejected.");
            Assert(allViolations.Summary.WinningCandidateId == null
                || !allViolations.Summary.RejectedCandidates.Any(x => string.Equals(x.CandidateId, allViolations.Summary.WinningCandidateId, StringComparison.Ordinal)),
                "The winning candidate was also recorded as rejected.");

            // The mixed case still drops the mandatory violator, and the dropped candidate is absent from the
            // presented order and so can never be the winner.
            PlacementProposalCandidate clean = Candidate("candidate:clean-mixed", violated: false, displacementMeters: 9d, treatment: ConstraintTreatment.HardConstraint);
            PlacementProposalCandidate mandatoryViolator = Candidate("candidate:violator-mixed", violated: true, displacementMeters: 1d, treatment: ConstraintTreatment.HardConstraint);
            RankingResult mixed = PlacementProposalRanker.Rank(new[] { mandatoryViolator, clean }, out RankingDiagnostics md);

            Assert(mixed.OrderedCandidates.Count == 1, "The mandatory violator was not dropped in favour of a compliant candidate.");
            Assert(mixed.OrderedCandidates[0].CandidateId == "candidate:clean-mixed", "A mandatory violator was presented ahead of a compliant candidate.");
            Assert(mixed.Summary.WinningCandidateId == "candidate:clean-mixed", "The winner is not the compliant candidate.");
            Assert(mixed.Summary.RejectedCandidates.Any(x => x.CandidateId == "candidate:violator-mixed"), "A dropped candidate was not recorded with its reason.");
            Assert(NoRejectedCandidateIsPresented(mixed), "A dropped candidate remained in the presented order.");
            Assert(!string.IsNullOrEmpty(mixed.Summary.RejectedCandidates[0].RejectionReason), "A rejected candidate carried no reason.");
        }

        /// <summary>No candidate id appears in both the presented order and the rejected list.</summary>
        private static bool NoRejectedCandidateIsPresented(RankingResult result)
        {
            foreach (string presented in result.OrderedCandidates.Select(x => x.CandidateId))
            {
                if (result.Summary.RejectedCandidates.Any(x => string.Equals(x.CandidateId, presented, StringComparison.Ordinal))) return false;
            }
            return true;
        }

        // ---- 6. Proposal composition (Phase A G) --------------------------------------------------------------

        private static void ProposalComposition()
        {
            ViolationContext context = RoadViolationContext(minimumMeters: 15m, actualMeters: 10m);
            ReEvaluationContext reEvaluationContext = FullReEvaluationContext();

            PlacementProposal proposal = PlacementProposalGenerator.Build(context, reEvaluationContext, out ProposalDiagnostics diagnostics);

            Assert(string.IsNullOrEmpty(proposal.ProposalId) == false, "The proposal has no deterministic identity.");
            Assert(diagnostics.Refused == false, "An executable violation produced a refused proposal.");
            Assert(diagnostics.CandidateCount > 0, "The proposal diagnostics report no candidates.");

            // All G.1 fields are populated from accepted evidence.
            Assert(proposal.ViolatedRule != null, "The proposal lost its violated governed rule.");
            Assert(proposal.ObservedDistanceMeters == context.Violation.ActualDistanceMeters, "The proposal lost its observed distance.");
            Assert(proposal.RequiredDistanceMeters == context.Rule.MinimumDistanceMeters, "The proposal lost its required distance.");
            Assert(proposal.MeasurementBasisKey == context.Violation.MeasurementBasisKey, "The proposal lost its measurement basis.");
            Assert(proposal.ObservedMeasurementId == context.Violation.MeasurementId, "The proposal lost its observed measurement identity.");
            Assert(!string.IsNullOrEmpty(proposal.UnitContext), "The proposal lost its unit context.");
            Assert(proposal.UnitContext!.IndexOf(InsertionUnits, StringComparison.Ordinal) >= 0, "The proposal unit context does not carry the insertion units.");

            // Original and proposed positions plus movement distance/direction.
            Assert(proposal.OriginalEasting == context.EquipmentEasting, "The proposal lost its original position.");
            Assert(proposal.MovementDistanceMeters > 0d, "The proposal reported no movement distance.");
            Assert(!string.IsNullOrEmpty(proposal.MovementDirection), "The proposal reported no movement direction.");

            // All three relationship re-evaluations are present.
            Assert(proposal.RoadReEvaluation != null, "The proposal lost its Road re-evaluation.");
            Assert(proposal.PlantFenceReEvaluation != null, "The proposal lost its Plant Fence re-evaluation.");
            Assert(proposal.UnitBoundaryReEvaluation != null, "The proposal lost its Unit Boundary re-evaluation.");

            // Constraint partitioning: insufficient evidence is never collapsed into satisfied.
            Assert(proposal.SatisfiedConstraints.Count > 0, "The proposal carried no satisfied constraints.");
            Assert(proposal.InsufficientEvidenceConstraints.Count > 0, "The proposal collapsed insufficient evidence into satisfied.");
            Assert(!proposal.InsufficientEvidenceConstraints.Any(x => x.EvaluationState == ProposalConstraintState.Satisfied),
                "An insufficient-evidence row is labelled SATISFIED.");
            Assert(proposal.ViolatedConstraints.All(x => x.EvaluationState == ProposalConstraintState.Violated), "A violated row is mislabelled.");

            // Mandatory v1 lists are non-empty and carry the required text.
            Assert(proposal.Assumptions.Count > 0, "The proposal carried no assumptions.");
            Assert(proposal.UncheckedConstraints.Count > 0, "The proposal carried no unchecked constraints.");
            Assert(proposal.KnownLimitations.Contains(ProposalKnownLimitations.PointBasedLimitation),
                "The proposal lost the point-based known limitation.");

            // Engineering review is always required in v1.
            Assert(proposal.EngineeringReviewRequired, "The proposal stopped requiring engineering review.");
            Assert(!string.IsNullOrEmpty(proposal.EngineeringReviewReason), "The proposal carried no engineering review reason.");

            // Clause evidence is copied verbatim from the governed rule, never invented.
            Assert(proposal.ClauseEvidence.Count == context.Rule.Clauses.Count, "The proposal altered clause evidence count.");
            if (context.Rule.Clauses.Count > 0)
            {
                Assert(proposal.ClauseEvidence[0].ClauseId == context.Rule.Clauses[0].ClauseId, "The proposal altered clause evidence.");
            }

            // The refused path produces a review-required proposal with a deterministic reason.
            ViolationContext refusedContext = RoadViolationContext(15m, 10m);
            refusedContext.Rule.AutoExecutionAllowed = false;
            PlacementProposal refused = PlacementProposalGenerator.Build(refusedContext, reEvaluationContext, out ProposalDiagnostics refusedDiagnostics);
            Assert(refusedDiagnostics.Refused, "A blocked violation was not reported as refused.");
            Assert(refused.RejectedCandidates.Count == 0, "A refused proposal carried rejected candidates.");
            Assert(refused.EngineeringReviewRequired, "A refused proposal stopped requiring engineering review.");
            Assert(!string.IsNullOrEmpty(refused.EngineeringReviewReason), "A refused proposal carried no reason.");
        }

        // ---- 7. Determinism --------------------------------------------------------------------------------------

        private static void Determinism()
        {
            string first = ProposalSnapshot(RoadViolationContext(15m, 10m));
            string second = ProposalSnapshot(RoadViolationContext(15m, 10m));
            Assert(first == second, "Repeated identical input produced a different proposal.");

            // Reordered constraint input yields the identical proposal: ordering is internal, never input-dependent.
            string reversed = ProposalSnapshot(RoadViolationContext(15m, 10m), reverseConstraints: true);
            Assert(first == reversed, "Reordered input produced a different proposal.");

            // Candidate generation alone is order-invariant.
            ViolationContext context = RoadViolationContext(15m, 10m);
            List<PlacementProposalCandidate> a = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics _).Candidates;
            List<PlacementProposalCandidate> b = PlacementProposalCandidateGenerator.Generate(RoadViolationContext(15m, 10m), out GenerationDiagnostics _).Candidates;
            Assert(a.Select(x => x.CandidateId).SequenceEqual(b.Select(x => x.CandidateId), StringComparer.Ordinal), "Candidate generation is not deterministic.");

            // The proposal identity is deterministic content, not time or random derived.
            const string idPrefix = "placement-proposal:";
            string proposalId = first.Substring(0, first.IndexOf('|'));
            Assert(proposalId.StartsWith(idPrefix, StringComparison.Ordinal), "The proposal id does not carry its deterministic prefix.");
            Assert(RegexIsHex(proposalId.Substring(idPrefix.Length)), "The proposal id is not a deterministic content-derived digest.");
        }

        // ---- 7b. Same-context determinism ----------------------------------------------------------------------

        private static void SameContextDeterminism()
        {
            // The generator must not mutate its input: two calls on the SAME unchanged ViolationContext yield
            // identical candidate ids and identical output. Candidate numbering is local to each call.
            ViolationContext context = RoadViolationContext(minimumMeters: 15m, actualMeters: 10m);

            string firstSnapshot = ProposalSnapshot(context);
            string secondSnapshot = ProposalSnapshot(context);
            Assert(firstSnapshot == secondSnapshot, "Two proposal builds on the same unchanged ViolationContext produced different output.");

            GenerationResult first = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics firstDiagnostics);
            GenerationResult second = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics secondDiagnostics);

            Assert(first.Candidates.Count == second.Candidates.Count, "Repeated generation on the same context changed the candidate count.");
            Assert(first.Candidates.Select(x => x.CandidateId).SequenceEqual(second.Candidates.Select(x => x.CandidateId), StringComparer.Ordinal),
                "Repeated generation on the same context produced different candidate ids.");
            for (int i = 0; i < first.Candidates.Count; i++)
            {
                Assert(first.Candidates[i].ProposedEasting == second.Candidates[i].ProposedEasting, "Repeated generation changed a proposed easting at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
                Assert(first.Candidates[i].ProposedNorthing == second.Candidates[i].ProposedNorthing, "Repeated generation changed a proposed northing at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
                Assert(first.Candidates[i].DisplacementDistance == second.Candidates[i].DisplacementDistance, "Repeated generation changed a displacement at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
                Assert(first.Candidates[i].DisplacementX == second.Candidates[i].DisplacementX && first.Candidates[i].DisplacementY == second.Candidates[i].DisplacementY,
                    "Repeated generation changed a displacement vector at index " + i.ToString(CultureInfo.InvariantCulture) + ".");
            }
            Assert(firstDiagnostics.GeneratedCount == secondDiagnostics.GeneratedCount, "Repeated generation changed the generated count.");
            Assert(firstDiagnostics.DiagnosticReasons.SequenceEqual(secondDiagnostics.DiagnosticReasons, StringComparer.Ordinal),
                "Repeated generation changed the diagnostic reasons.");

            // A context built from identical evidence yields the same ids: numbering depends on input only.
            ViolationContext twin = RoadViolationContext(minimumMeters: 15m, actualMeters: 10m);
            GenerationResult twinResult = PlacementProposalCandidateGenerator.Generate(twin, out GenerationDiagnostics _);
            Assert(first.Candidates.Select(x => x.CandidateId).SequenceEqual(twinResult.Candidates.Select(x => x.CandidateId), StringComparer.Ordinal),
                "Identical evidence produced different candidate ids.");
        }

        // ---- Fixtures -------------------------------------------------------------------------------------------

        private static readonly string[] ExistingHandles = { RoadHandle, FenceHandle, RoadHandle + ":label", FenceHandle + ":label", BoundaryHandle };

        // Synthetic site, all dimensions in metres: the road edge runs east-west at northing 0, the plant
        // fence parallels it at northing 40. Equipment sits at easting 100. Every fixture below is expressed
        // in metres and converted through the same helper, so drawing-unit and metre values always agree.
        private const double RoadNorthingMeters = 0d;
        private const double FenceNorthingMeters = 40d;
        private const double EquipmentEastingMeters = 100d;
        private const double SpanMeters = 1000d;

        private static DrawingUnitResolution UnitResolution()
        {
            // The accepted resolver is the only unit path; the fixture resolves exactly as production does.
            return DrawingUnitResolver.Resolve(InsertionUnits);
        }

        private static double MetersToDrawingUnits(double meters) => meters / MetersPerDrawingUnit;
        private static double DrawingUnitsToMeters(double drawingUnits) => drawingUnits * MetersPerDrawingUnit;

        /// <summary>Equality within the precision of the normalized vector computation.</summary>
        private static bool IsApproximately(double value, double expected)
        {
            double tolerance = Math.Max(1d, Math.Abs(expected)) * 1e-12d;
            return Math.Abs(value - expected) <= tolerance;
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

        private static PlotGeometryModel FullGeometry()
        {
            var model = new PlotGeometryModel();
            model.Primitives.Add(HorizontalPrimitive(RoadHandle, RoadNorthingMeters, -SpanMeters, SpanMeters));
            model.Primitives.Add(HorizontalPrimitive(FenceHandle, FenceNorthingMeters, -SpanMeters, SpanMeters));

            model.EngineeringSpatialSubjects.Add(new EngineeringSpatialSubject
            {
                SubjectId = "subject:road-1",
                SubjectKind = "Road",
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                GeometrySourceHandle = RoadHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                LabelSourceHandles = new List<string> { RoadHandle + ":label" }
            });
            model.EngineeringSpatialSubjects.Add(new EngineeringSpatialSubject
            {
                SubjectId = "subject:fence-1",
                SubjectKind = "PlantFence",
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.SecurityFence,
                GeometrySourceHandle = FenceHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                LabelSourceHandles = new List<string> { FenceHandle + ":label" }
            });
            // No UnitBoundary subject: the basis is intentionally unresolved to exercise INSUFFICIENT_EVIDENCE.
            return model;
        }

        /// <summary>The governed rule: executable, project criteria, hard constraint.</summary>
        private static StandardsPackRule ExecutableRule(string basisKey, decimal minimum)
        {
            return new StandardsPackRule
            {
                RuleId = "pd.governed." + basisKey,
                PackId = "project.plotplan.criteria",
                MeasurementBasisKey = basisKey,
                ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                MinimumDistanceMeters = minimum,
                ConstraintTreatment = ConstraintTreatment.HardConstraint,
                AutoExecutionAllowed = true,
                EngineeringReviewRequired = true,
                DecisionState = StandardDecisionStates.Pass
            };
        }

        private static BoundedConstraint ConstraintFor(string basisKey, decimal minimum, StandardsPackRule? rule = null)
        {
            StandardsPackRule resolvedRule = rule ?? ExecutableRule(basisKey, minimum);
            return new BoundedConstraint
            {
                ConstraintId = "bounded-constraint:" + resolvedRule.RuleId,
                Rule = resolvedRule!,
                ConstraintTreatment = resolvedRule.ConstraintTreatment ?? ConstraintTreatment.HardConstraint,
                MeasurementKind = basisKey == EngineeringDistanceEvidenceKeys.SecurityFence || basisKey == EngineeringDistanceEvidenceKeys.UnitBoundary
                    ? "EquipmentToBoundary2D"
                    : "EquipmentToRoad2D",
                RequiredMeasurementBasisKey = basisKey,
                RequiredApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                MinimumDistanceMeters = minimum
            };
        }

        private static BoundedConstraint OutOfTaxonomyConstraint()
        {
            return ConstraintFor(EngineeringDistanceEvidenceKeys.SiteBoundary, 15m);
        }

        private static BoundedConstraint BlockedConstraint(string basisKey, decimal minimum)
        {
            StandardsPackRule rule = ExecutableRule(basisKey, minimum);
            rule.AutoExecutionAllowed = false;
            return ConstraintFor(basisKey, minimum, rule);
        }

        private static ReEvaluationContext FullReEvaluationContext()
        {
            return new ReEvaluationContext
            {
                Constraints = new[]
                {
                    ConstraintFor(EngineeringDistanceEvidenceKeys.AccessRoadEdge, 15m),
                    ConstraintFor(EngineeringDistanceEvidenceKeys.SecurityFence, 12m),
                    ConstraintFor(EngineeringDistanceEvidenceKeys.UnitBoundary, 9m)
                },
                UnitResolution = UnitResolution(),
                Geometry = FullGeometry()
            };
        }

        /// <summary>A road violation: equipment <paramref name="actualMeters"/> from the road edge against a
        /// <paramref name="minimumMeters"/> criterion. The equipment northing is placed so the recorded actual
        /// distance is geometrically true.</summary>
        private static ViolationContext RoadViolationContext(decimal minimumMeters, decimal actualMeters)
        {
            StandardsPackRule rule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, minimumMeters);
            double actualMetersValue = (double)actualMeters;

            return new ViolationContext
            {
                Violation = new EngineeringDistanceViolation
                {
                    MeasurementId = "measurement:road-1",
                    PlacementId = PlacementId,
                    EquipmentEntityId = EquipmentEntityId,
                    NormalizedTag = NormalizedTag,
                    SubjectKind = "Road",
                    ConstraintId = "bounded-constraint:" + rule.RuleId,
                    ActualDistanceDrawingUnits = MetersToDrawingUnits(actualMetersValue),
                    ActualDistanceMeters = actualMetersValue,
                    LabelSourceHandle = RoadHandle + ":label",
                    GeometrySourceHandle = RoadHandle,
                    ClosestPointX = MetersToDrawingUnits(EquipmentEastingMeters),
                    ClosestPointY = MetersToDrawingUnits(RoadNorthingMeters),
                    MeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                    ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
                },
                Rule = rule,
                EquipmentEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                EquipmentNorthing = MetersToDrawingUnits(RoadNorthingMeters + actualMetersValue),
                PlacementStatus = "Complete",
                ViolatingGeometry = HorizontalPrimitive(RoadHandle, RoadNorthingMeters, -SpanMeters, SpanMeters),
                UnitResolution = UnitResolution()
            };
        }

        /// <summary>A road violation on a 3-4-5 triangle: closest point at (0, 0), equipment at (30, 40) m, so
        /// the exact unit vector is (0.6, 0.8) — neither axis-aligned nor diagonal.</summary>
        private static ViolationContext ObliqueViolationContext(decimal minimumMeters, decimal actualMeters)
        {
            StandardsPackRule rule = ExecutableRule(EngineeringDistanceEvidenceKeys.AccessRoadEdge, minimumMeters);
            double actualMetersValue = (double)actualMeters;

            return new ViolationContext
            {
                Violation = new EngineeringDistanceViolation
                {
                    MeasurementId = "measurement:road-oblique",
                    PlacementId = PlacementId,
                    EquipmentEntityId = EquipmentEntityId,
                    NormalizedTag = NormalizedTag,
                    SubjectKind = "Road",
                    ConstraintId = "bounded-constraint:" + rule.RuleId,
                    ActualDistanceDrawingUnits = MetersToDrawingUnits(actualMetersValue),
                    ActualDistanceMeters = actualMetersValue,
                    LabelSourceHandle = RoadHandle + ":label",
                    GeometrySourceHandle = RoadHandle,
                    ClosestPointX = MetersToDrawingUnits(0d),
                    ClosestPointY = MetersToDrawingUnits(RoadNorthingMeters),
                    MeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                    ApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
                },
                Rule = rule,
                EquipmentEasting = MetersToDrawingUnits(30d),
                EquipmentNorthing = MetersToDrawingUnits(40d),
                PlacementStatus = "Complete",
                ViolatingGeometry = HorizontalPrimitive(RoadHandle, RoadNorthingMeters, -SpanMeters, SpanMeters),
                UnitResolution = UnitResolution()
            };
        }

        private static ViolationContext ContextWithRule(StandardsPackRule rule, string basisKey, decimal actualMeters)
        {
            ViolationContext context = RoadViolationContext(rule.MinimumDistanceMeters, actualMeters);
            context.Rule = rule;
            context.Violation.ConstraintId = "bounded-constraint:" + rule.RuleId;
            return context;
        }

        /// <summary>The expected direction label for the recorded closest-point evidence.</summary>
        private static string ExpectedDirection(ViolationContext context)
        {
            double dx = context.EquipmentEasting!.Value - context.Violation.ClosestPointX;
            double dy = context.EquipmentNorthing!.Value - context.Violation.ClosestPointY;
            if (dx == 0d && dy == 0d) return "E";
            double degrees = Math.Atan2(dy, dx) * 180d / Math.PI;
            if (degrees < 0d) degrees += 360d;
            int sector = (int)Math.Round(degrees / 45d) % 8;
            switch (sector)
            {
                case 0: return "E";
                case 1: return "NE";
                case 2: return "N";
                case 3: return "NW";
                case 4: return "W";
                case 5: return "SW";
                case 6: return "S";
                default: return "SE";
            }
        }

        /// <summary>A candidate 30 m north of the road edge: 30 m from the road (criterion 15 m, SATISFIED)
        /// and 10 m from the fence at northing 40 (criterion 12 m, VIOLATED).</summary>
        private static PlacementProposalCandidate SimpleCandidate()
        {
            double originalNorthing = MetersToDrawingUnits(RoadNorthingMeters + 10d);
            double proposedNorthing = MetersToDrawingUnits(RoadNorthingMeters + 30d);
            double displacement = MetersToDrawingUnits(20d);

            return new PlacementProposalCandidate
            {
                CandidateId = "proposal-candidate:placement:constraint:0",
                PlacementId = PlacementId,
                EquipmentEntityId = EquipmentEntityId,
                NormalizedTag = NormalizedTag,
                OriginalEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                OriginalNorthing = originalNorthing,
                ProposedEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                ProposedNorthing = proposedNorthing,
                DisplacementX = 0d,
                DisplacementY = displacement,
                DisplacementDistance = displacement,
                DisplacementDirection = "N",
                GeneratingConstraintId = "bounded-constraint:pd.governed.AccessRoadEdge",
                GeneratingRuleId = "pd.governed.AccessRoadEdge",
                GeometrySourceHandle = RoadHandle,
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                RejectedReasons = new List<string>(),
                EngineeringReviewRequired = true
            };
        }

        private static PlacementProposalCandidate Candidate(string candidateId, bool violated, double displacementMeters, string treatment, bool unresolved = false)
        {
            var outcome = new PlacementProposalConstraintOutcome
            {
                ConstraintId = "bounded-constraint:pd.test." + candidateId,
                RuleId = "pd.test",
                ConstraintTreatment = treatment,
                MeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                SubjectKind = "Road",
                ActualDistanceMeters = violated ? 5d : 20d,
                RequiredDistanceMeters = 15m,
                EvaluationState = unresolved ? ProposalConstraintState.InsufficientEvidence : (violated ? ProposalConstraintState.Violated : ProposalConstraintState.Satisfied),
                EvidenceHandles = new List<string> { RoadHandle }
            };

            double displacement = MetersToDrawingUnits(displacementMeters);
            double originalNorthing = MetersToDrawingUnits(RoadNorthingMeters + 10d);

            return new PlacementProposalCandidate
            {
                CandidateId = candidateId,
                PlacementId = PlacementId,
                EquipmentEntityId = EquipmentEntityId,
                NormalizedTag = NormalizedTag,
                OriginalEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                OriginalNorthing = originalNorthing,
                ProposedEasting = MetersToDrawingUnits(EquipmentEastingMeters),
                ProposedNorthing = originalNorthing + displacement,
                DisplacementX = 0d,
                DisplacementY = displacement,
                DisplacementDistance = displacement,
                DisplacementDirection = "N",
                GeneratingConstraintId = "bounded-constraint:pd.test." + candidateId,
                GeneratingRuleId = "pd.test",
                GeometrySourceHandle = RoadHandle,
                CheckedConstraints = new List<PlacementProposalConstraintOutcome> { outcome },
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                RejectedReasons = new List<string>(),
                EngineeringReviewRequired = true
            };
        }

        // ---- Determinism and snapshot helpers ------------------------------------------------------------------

        private static string ProposalSnapshot(ViolationContext context, bool reverseConstraints = false)
        {
            ReEvaluationContext reEvaluationContext = FullReEvaluationContext();
            if (reverseConstraints) reEvaluationContext.Constraints = reEvaluationContext.Constraints.Reverse().ToList();

            PlacementProposal proposal = PlacementProposalGenerator.Build(context, reEvaluationContext, out ProposalDiagnostics diagnostics);

            var builder = new StringBuilder();
            builder.Append(proposal.ProposalId).Append('|')
                .Append(proposal.ViolatedRule?.RuleId).Append('|')
                .Append(proposal.ObservedDistanceMeters.ToString("F6", CultureInfo.InvariantCulture)).Append('|')
                .Append(proposal.RequiredDistanceMeters.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(proposal.MeasurementBasisKey).Append('|')
                .Append(proposal.OriginalEasting.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                .Append(proposal.OriginalNorthing.ToString("F6", CultureInfo.InvariantCulture)).Append('|')
                .Append(proposal.ProposedEasting.ToString("F6", CultureInfo.InvariantCulture)).Append(',')
                .Append(proposal.ProposedNorthing.ToString("F6", CultureInfo.InvariantCulture)).Append('|')
                .Append(proposal.MovementDistanceMeters.ToString("F6", CultureInfo.InvariantCulture)).Append('|')
                .Append(proposal.MovementDirection).Append('|')
                .Append(proposal.RoadReEvaluation?.EvaluationState).Append('|')
                .Append(proposal.PlantFenceReEvaluation?.EvaluationState).Append('|')
                .Append(proposal.UnitBoundaryReEvaluation?.EvaluationState).Append('|')
                .Append(string.Join(",", proposal.SatisfiedConstraints.Select(x => x.ConstraintId + ":" + x.EvaluationState))).Append('|')
                .Append(string.Join(",", proposal.ViolatedConstraints.Select(x => x.ConstraintId + ":" + x.EvaluationState))).Append('|')
                .Append(string.Join(",", proposal.InsufficientEvidenceConstraints.Select(x => x.ConstraintId + ":" + x.EvaluationState))).Append('|')
                .Append(proposal.EngineeringReviewRequired).Append('|')
                .Append(proposal.EngineeringReviewReason);
            builder.Append("##diagnostics=").Append(string.Join(",", diagnostics.DiagnosticReasons));
            return builder.ToString();
        }

        private static bool RegexIsHex(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char c in value)
            {
                bool isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!isHex) return false;
            }
            return true;
        }

        // ---- Assertion helpers ---------------------------------------------------------------------------

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
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