using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Phase D — candidate ranking (Phase A architecture J). Deterministic, order-only ranking of already
    /// computed outcomes. No optimization score, no objective function, no relaxation and no trade-off search.
    ///
    /// Policy applied in order (Phase A J.1):
    /// 1. Mandatory/governed constraint validity first: ascending count of VIOLATED outcomes among constraints
    ///    whose treatment is HARD_CONSTRAINT or SAFETY_CONSTRAINT. A candidate with a violated mandatory
    ///    constraint can never outrank one with none, regardless of any other attribute.
    /// 2. Unresolved and review-required states stay explicitly visible: INSUFFICIENT_EVIDENCE and
    ///    REVIEW_REQUIRED counts are carried as first-class secondary attributes, never penalized into silence
    ///    and never collapsed to zero.
    /// 3. Then the declared objective — minimum movement: ascending DisplacementDistance.
    /// 4. Deterministic tie-break: DisplacementDistance, ProposedEasting, ProposedNorthing,
    ///    GeneratingConstraintId, CandidateId, with StringComparer.Ordinal for strings.
    ///
    /// Hard prohibitions (Phase A J.2): an optimization score must not override a violated mandatory
    /// constraint; INSUFFICIENT_EVIDENCE must not be hidden; a candidate with any REVIEW_REQUIRED outcome is
    /// labelled as requiring engineering review even if it ranks first; ranking never changes a measurement,
    /// an evidence handle, a governance state or a constraint state — it only orders already-computed outcomes,
    /// and drops a candidate only in favour of a candidate with no mandatory violation.
    /// </summary>
    public static class PlacementProposalRanker
    {
        /// <summary>The treatments whose violations constitute a mandatory-constraint failure for ranking.</summary>
        private static readonly HashSet<string> MandatoryTreatments = new HashSet<string>(StringComparer.Ordinal)
        {
            ConstraintTreatment.HardConstraint,
            ConstraintTreatment.SafetyConstraint
        };

        /// <summary>
        /// Ranks the re-evaluated candidates and produces the deterministic summary that makes the ranking
        /// auditable from the artifact alone. The returned candidates are the same instances, re-ordered; no
        /// measurement, evidence, governance or constraint state is modified.
        ///
        /// A candidate that violates a mandatory constraint is <b>dropped</b> — moved out of the ordered result
        /// and recorded in <see cref="PlacementProposalRankingSummary.RejectedCandidates"/> with a reason — only
        /// when the ranked set also contains candidates with no mandatory violation, i.e. only in favour of a
        /// compliant alternative. When every candidate has a mandatory violation nothing is dropped: the
        /// least-violating candidate is presented with its violations visible, so a rejected candidate and the
        /// winning candidate can never be the same candidate.
        /// </summary>
        public static RankingResult Rank(IReadOnlyList<PlacementProposalCandidate> candidates, out RankingDiagnostics diagnostics)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            List<PlacementProposalCandidate> ordered = candidates
                .Where(x => x != null)
                .OrderBy(MandatoryViolationCount)
                .ThenBy(x => UnresolvedCount(x))
                .ThenBy(x => x.DisplacementDistance)
                .ThenBy(x => x.ProposedEasting)
                .ThenBy(x => x.ProposedNorthing)
                .ThenBy(x => x.GeneratingConstraintId, StringComparer.Ordinal)
                .ThenBy(x => x.CandidateId, StringComparer.Ordinal)
                .ToList();

            List<PlacementProposalCandidate> presented = ordered.Count == 0 || MandatoryViolationCount(ordered[0]) == 0
                ? ordered.Where(x => MandatoryViolationCount(x) == 0).ToList()
                : ordered;

            var rejected = new List<PlacementProposalRejectedCandidate>();
            var reasons = new List<string>();

            // Only candidates actually dropped for this explicit reason are recorded as rejected. Because the
            // ordering above puts mandatory violators last, the dropped set is exactly the tail of `ordered`
            // and can never contain the first presented candidate.
            foreach (PlacementProposalCandidate candidate in ordered.Where(x => !presented.Contains(x)))
            {
                int mandatoryViolations = MandatoryViolationCount(candidate);
                string reason = "mandatory-violation:" + candidate.CandidateId + ": " + mandatoryViolations.ToString(CultureInfo.InvariantCulture) + " violated HARD_CONSTRAINT/SAFETY_CONSTRAINT outcome(s); dropped in favour of a candidate with no mandatory violation";
                rejected.Add(new PlacementProposalRejectedCandidate { CandidateId = candidate.CandidateId, RejectionReason = reason });
                reasons.Add(reason);
            }

            string? winningCandidateId = presented.Count > 0 ? presented[0].CandidateId : null;

            diagnostics = new RankingDiagnostics
            {
                CandidateCount = presented.Count,
                WinningCandidateId = winningCandidateId,
                MandatoryViolationCandidateCount = ordered.Count(x => MandatoryViolationCount(x) > 0),
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return new RankingResult
            {
                OrderedCandidates = presented,
                Summary = new PlacementProposalRankingSummary
                {
                    OrderedCandidateIds = presented.Select(x => x.CandidateId).ToList(),
                    WinningCandidateId = winningCandidateId,
                    SatisfiedCount = ordered.Sum(x => x.CheckedConstraints.Count(c => c != null && c.EvaluationState == ProposalConstraintState.Satisfied)),
                    ViolatedCount = ordered.Sum(x => x.CheckedConstraints.Count(c => c != null && c.EvaluationState == ProposalConstraintState.Violated)),
                    InsufficientEvidenceCount = ordered.Sum(x => x.CheckedConstraints.Count(c => c != null && c.EvaluationState == ProposalConstraintState.InsufficientEvidence)),
                    ReviewRequiredCount = ordered.Sum(x => x.CheckedConstraints.Count(c => c != null && c.EvaluationState == ProposalConstraintState.ReviewRequired)),
                    NotApplicableCount = ordered.Sum(x => x.CheckedConstraints.Count(c => c != null && c.EvaluationState == ProposalConstraintState.NotApplicable)),
                    RejectedCandidates = rejected
                }
            };
        }

        /// <summary>
        /// Ascending mandatory-violation count. Structural, not a score: it is compared before any other
        /// attribute, so a candidate with a violated mandatory constraint can never outrank one without.
        /// </summary>
        private static int MandatoryViolationCount(PlacementProposalCandidate candidate)
        {
            return candidate.CheckedConstraints.Count(c => c != null
                && c.EvaluationState == ProposalConstraintState.Violated
                && MandatoryTreatments.Contains(c.ConstraintTreatment));
        }

        /// <summary>
        /// Unresolved states stay visible as a secondary attribute. Never zeroed and never hidden: a candidate
        /// carrying INSUFFICIENT_EVIDENCE or REVIEW_REQUIRED is ranked after resolved ones at the same
        /// mandatory tier, but its count remains first-class on the summary.
        /// </summary>
        private static int UnresolvedCount(PlacementProposalCandidate candidate)
        {
            return candidate.CheckedConstraints.Count(c => c != null
                && (c.EvaluationState == ProposalConstraintState.InsufficientEvidence
                    || c.EvaluationState == ProposalConstraintState.ReviewRequired));
        }
    }

    /// <summary>The deterministic ranking result: the ordered candidates plus the auditable summary.</summary>
    public sealed class RankingResult
    {
        public List<PlacementProposalCandidate> OrderedCandidates { get; set; } = new List<PlacementProposalCandidate>();
        public PlacementProposalRankingSummary Summary { get; set; } = new PlacementProposalRankingSummary();
    }

    /// <summary>Deterministic ranking diagnostics. Rejections and mandatory failures are reported, never dropped.</summary>
    public sealed class RankingDiagnostics
    {
        public int CandidateCount { get; set; }
        public string? WinningCandidateId { get; set; }
        public int MandatoryViolationCandidateCount { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
