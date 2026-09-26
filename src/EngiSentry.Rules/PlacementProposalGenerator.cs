using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Phase D — proposal composition (Phase A architecture G). Composes the candidate generator, the
    /// re-evaluator and the ranker into one read-only <see cref="PlacementProposal"/>: the explainable
    /// CHECK+SUGGEST explanation for one governing violation.
    ///
    /// The primary flow is exactly Phase A: governed executable violation → deterministic candidate generation
    /// → re-evaluate every candidate against all bounded constraints → deterministic ranking →
    /// PlacementProposal. No phase of this is an optimizer, a search or a general solver.
    ///
    /// The proposal is read-only evidence. No field can express a drawing mutation: the type carries no host
    /// entity and no write capability, and the generator never opens a document or writes anything.
    /// Engineering review is always required in v1; a first-ranked candidate is advisory evidence for the
    /// engineer, never an instruction to move equipment.
    /// </summary>
    public static class PlacementProposalGenerator
    {
        public const string GeneratorIdentity = "EngiSentry.PlacementProposalGenerator/1";
        private const string EngineeringReviewReason =
            "Phase D is advisory evidence: candidates are point-based, no equipment footprint, orientation or " +
            "extent is evaluated, and no general optimization is performed. Engineering review is required before " +
            "any position is adopted.";

        /// <summary>
        /// Builds the placement proposal for one governing violation. All inputs are accepted evidence: the
        /// violation context, the bounded constraint set (all supported constraints, not only the generating
        /// one), the accepted plot geometry and the accepted drawing unit resolution.
        /// </summary>
        public static PlacementProposal Build(ViolationContext context, ReEvaluationContext reEvaluationContext, out ProposalDiagnostics diagnostics)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (reEvaluationContext == null) throw new ArgumentNullException(nameof(reEvaluationContext));

            var reasons = new List<string>();

            GenerationResult generation = PlacementProposalCandidateGenerator.Generate(context, out GenerationDiagnostics generationDiagnostics);
            if (generationDiagnostics.Refused)
            {
                reasons.Add(generationDiagnostics.RefusalReason ?? "refusal: generation was refused");
                diagnostics = RefusedDiagnostics(context, reasons);
                return RefusedProposal(context, generationDiagnostics.RefusalReason);
            }

            // Every generated candidate is re-evaluated against ALL bounded constraints, then ranked.
            List<PlacementProposalCandidate> reEvaluated = new List<PlacementProposalCandidate>();
            foreach (PlacementProposalCandidate candidate in generation.Candidates)
            {
                reEvaluated.Add(PlacementProposalReEvaluator.ReEvaluate(candidate, reEvaluationContext, out ReEvaluationDiagnostics reEvaluationDiagnostics));
                reasons.AddRange(reEvaluationDiagnostics.DiagnosticReasons);
            }

            RankingResult ranking = PlacementProposalRanker.Rank(reEvaluated, out RankingDiagnostics rankingDiagnostics);
            reasons.AddRange(rankingDiagnostics.DiagnosticReasons);

            PlacementProposal proposal = ComposeProposal(context, reEvaluationContext, ranking, generation.Candidates);

            diagnostics = new ProposalDiagnostics
            {
                ViolationRuleId = context.Rule.RuleId,
                CandidateCount = ranking.OrderedCandidates.Count,
                RankedCount = ranking.Summary.OrderedCandidateIds.Count,
                Refused = false,
                RefusalReason = null,
                RejectedCandidateCount = ranking.Summary.RejectedCandidates.Count,
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return proposal;
        }

        /// <summary>
        /// The refused proposal (Phase A H.3): no candidate is presented, review is required, and the
        /// deterministic refusal reason is carried so the engineer is never left with an empty result.
        /// </summary>
        private static PlacementProposal RefusedProposal(ViolationContext context, string? refusalReason)
        {
            return new PlacementProposal
            {
                ProposalId = DeterministicProposalId(context, Array.Empty<PlacementProposalCandidate>()),
                PlacementId = context.Violation.PlacementId,
                EquipmentEntityId = context.Violation.EquipmentEntityId,
                NormalizedTag = context.Violation.NormalizedTag,
                InsertionUnits = context.UnitResolution.InsertionUnits,
                GeneratorIdentity = GeneratorIdentity,
                ViolatedRule = context.Rule,
                ObservedDistanceMeters = context.Violation.ActualDistanceMeters,
                ObservedMeasurementBasis = context.Violation.MeasurementBasisKey,
                ObservedMeasurementId = context.Violation.MeasurementId,
                RequiredDistanceMeters = context.Rule.MinimumDistanceMeters,
                MeasurementBasisKey = context.Violation.MeasurementBasisKey,
                MeasurementApplicabilityKey = context.Violation.ApplicabilityKey,
                UnitContext = UnitContextText(context.UnitResolution),
                OriginalEasting = context.EquipmentEasting ?? 0d,
                OriginalNorthing = context.EquipmentNorthing ?? 0d,
                ClauseEvidence = ClauseEvidenceFor(context.Rule),
                RejectedCandidates = new List<PlacementProposalCandidate>(),
                SatisfiedConstraints = new List<PlacementProposalConstraintOutcome>(),
                ViolatedConstraints = new List<PlacementProposalConstraintOutcome>(),
                InsufficientEvidenceConstraints = new List<PlacementProposalConstraintOutcome>(),
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                EngineeringReviewRequired = true,
                EngineeringReviewReason = refusalReason ?? "generation was refused; engineering review is required",
                KnownLimitations = new List<string>(ProposalKnownLimitations.Defaults)
            };
        }

        private static PlacementProposal ComposeProposal(ViolationContext context, ReEvaluationContext reEvaluationContext, RankingResult ranking, IReadOnlyList<PlacementProposalCandidate> generated)
        {
            PlacementProposalCandidate? winner = ranking.OrderedCandidates.FirstOrDefault();

            // Rejected candidates: those the ranker dropped for a mandatory violation, each with its reason.
            // Dropped candidates are absent from the ranked order, so they are resolved from the generated set.
            List<PlacementProposalCandidate> rejected = generated
                .Where(x => ranking.Summary.RejectedCandidates.Any(r => r != null && string.Equals(r.CandidateId, x.CandidateId, StringComparison.Ordinal)))
                .ToList();

            List<PlacementProposalConstraintOutcome> allOutcomes = (winner?.CheckedConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Where(x => x != null)
                .OrderBy(x => x.ConstraintId, StringComparer.Ordinal)
                .ToList();

            return new PlacementProposal
            {
                ProposalId = DeterministicProposalId(context, ranking.OrderedCandidates),
                PlacementId = winner?.PlacementId ?? context.Violation.PlacementId,
                EquipmentEntityId = winner?.EquipmentEntityId ?? context.Violation.EquipmentEntityId,
                NormalizedTag = winner?.NormalizedTag ?? context.Violation.NormalizedTag,
                InsertionUnits = context.UnitResolution.InsertionUnits,
                GeneratorIdentity = GeneratorIdentity,
                ViolatedRule = context.Rule,
                ObservedDistanceMeters = context.Violation.ActualDistanceMeters,
                ObservedMeasurementBasis = context.Violation.MeasurementBasisKey,
                ObservedMeasurementId = context.Violation.MeasurementId,
                RequiredDistanceMeters = context.Rule.MinimumDistanceMeters,
                MeasurementBasisKey = context.Violation.MeasurementBasisKey,
                MeasurementApplicabilityKey = context.Violation.ApplicabilityKey,
                UnitContext = UnitContextText(context.UnitResolution),
                ClauseEvidence = ClauseEvidenceFor(context.Rule),
                OriginalEasting = winner?.OriginalEasting ?? context.EquipmentEasting ?? 0d,
                OriginalNorthing = winner?.OriginalNorthing ?? context.EquipmentNorthing ?? 0d,
                ProposedEasting = winner?.ProposedEasting ?? context.EquipmentEasting ?? 0d,
                ProposedNorthing = winner?.ProposedNorthing ?? context.EquipmentNorthing ?? 0d,
                MovementDistanceMeters = winner != null
                    ? DrawingUnitResolver.DrawingUnitsToMeters(winner.DisplacementDistance, context.UnitResolution)
                    : 0d,
                MovementDirection = winner?.DisplacementDirection,
                RoadReEvaluation = OutcomeForBasis(allOutcomes, reEvaluationContext, EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge),
                PlantFenceReEvaluation = OutcomeForBasis(allOutcomes, reEvaluationContext, EngineeringDistanceEvidenceKeys.SecurityFence),
                UnitBoundaryReEvaluation = OutcomeForBasis(allOutcomes, reEvaluationContext, EngineeringDistanceEvidenceKeys.UnitBoundary),
                SatisfiedConstraints = allOutcomes.Where(x => x.EvaluationState == ProposalConstraintState.Satisfied).ToList(),
                ViolatedConstraints = allOutcomes.Where(x => x.EvaluationState == ProposalConstraintState.Violated).ToList(),
                // Never collapsed into satisfied: insufficient evidence stays a first-class list.
                InsufficientEvidenceConstraints = allOutcomes.Where(x => x.EvaluationState == ProposalConstraintState.InsufficientEvidence).ToList(),
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                RejectedCandidates = rejected,
                EngineeringReviewRequired = true,
                EngineeringReviewReason = EngineeringReviewReason,
                KnownLimitations = new List<string>(ProposalKnownLimitations.Defaults)
            };
        }

        /// <summary>
        /// Selects the re-evaluation outcome for one relationship, preferring the generating basis when the
        /// relationship has two accepted bases (Road: access road edge and unit peripheral road edge).
        /// </summary>
        private static PlacementProposalConstraintOutcome? OutcomeForBasis(IReadOnlyList<PlacementProposalConstraintOutcome> outcomes, ReEvaluationContext context, params string[] basisKeys)
        {
            foreach (string basisKey in basisKeys.OrderBy(x => x, StringComparer.Ordinal))
            {
                PlacementProposalConstraintOutcome? outcome = outcomes.FirstOrDefault(x => string.Equals(x.MeasurementBasisKey, basisKey, StringComparison.Ordinal));
                if (outcome != null) return outcome;
            }
            return null;
        }

        /// <summary>
        /// Deterministic identity from PlacementId + generating constraint + candidate set. Never random and
        /// never time-derived, so the same evidence always yields the same proposal id.
        /// </summary>
        private static string DeterministicProposalId(ViolationContext context, IReadOnlyList<PlacementProposalCandidate> candidates)
        {
            string candidateKeys = string.Join(",", candidates.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(x => x.CandidateId));
            string raw = context.Violation.PlacementId + "|" + context.Violation.ConstraintId + "|" + candidateKeys;
            string sha = Sha256Text(raw);
            return "placement-proposal:" + sha;
        }

        private static string UnitContextText(DrawingUnitResolution resolution)
        {
            return resolution.InsertionUnits + " (" + resolution.CanonicalUnit + ", " +
                resolution.MetersPerDrawingUnit.ToString(CultureInfo.InvariantCulture) + " m per drawing unit)";
        }

        private static List<StandardClauseCitation> ClauseEvidenceFor(StandardsPackRule rule)
        {
            // Clause evidence is copied verbatim from the governed rule's own clause resolutions; nothing is
            // invented and no source, page or hash is synthesized.
            if (rule?.Clauses == null) return new List<StandardClauseCitation>();

            return rule.Clauses
                .Where(x => x != null)
                .Select(clause => new StandardClauseCitation
                {
                    ClauseId = clause.ClauseId,
                    SourceId = clause.SourceId,
                    ClauseReference = clause.ClauseReference ?? string.Empty,
                    ClauseText = clause.ClauseText ?? string.Empty,
                    Heading = clause.ClauseHeading,
                    PageNumber = clause.PageNumber,
                    DocumentNumber = clause.SourceDocumentNumber ?? string.Empty,
                    SourceTitle = clause.SourceTitle ?? string.Empty,
                    Issuer = clause.SourceIssuer ?? string.Empty,
                    RevisionOrEdition = clause.SourceRevisionOrEdition,
                    SourceFileName = clause.SourceFileName ?? string.Empty,
                    SourceSha256 = clause.SourceSha256
                })
                .OrderBy(x => x.ClauseId, StringComparer.Ordinal)
                .ToList();
        }

        private static ProposalDiagnostics RefusedDiagnostics(ViolationContext context, List<string> reasons)
        {
            return new ProposalDiagnostics
            {
                ViolationRuleId = context.Rule.RuleId,
                CandidateCount = 0,
                RankedCount = 0,
                Refused = true,
                RefusalReason = reasons.FirstOrDefault(),
                RejectedCandidateCount = 0,
                DiagnosticReasons = reasons
            };
        }

        private static string Sha256Text(string value)
        {
            // Deterministic content addressing, consistent with the discipline already enforced by the
            // accepted review-package builders. No time or random component.
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
                var builder = new System.Text.StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }

    /// <summary>Deterministic proposal diagnostics. Refusals, rejections and reasons are reported, never dropped.</summary>
    public sealed class ProposalDiagnostics
    {
        public string ViolationRuleId { get; set; } = string.Empty;
        public int CandidateCount { get; set; }
        public int RankedCount { get; set; }
        public bool Refused { get; set; }
        public string? RefusalReason { get; set; }
        public int RejectedCandidateCount { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
