using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Governed bounded CHECK: evaluates a set of bounded constraints against existing accepted Plot Plan
    /// measurement evidence and produces the Phase A proposal-layer states without ever inventing a verdict.
    ///
    /// This is deliberately NOT a new evaluation engine. The accepted
    /// <see cref="EngineeringDistanceRuleEvaluator"/> remains the sole source of numerical distance verdicts,
    /// and the accepted EngiSentry.Standards.EngineeringDistanceQaIntegrator remains the sole source of QA
    /// findings. This evaluator only projects already-governed constraint states onto the bounded vocabulary,
    /// so a bounded check can be reported for the three supported relationships without touching any accepted
    /// contract or behavior.
    ///
    /// Hard invariants:
    /// - A constraint whose treatment is REVIEW_REQUIRED never becomes SATISFIED or VIOLATED.
    /// - A constraint with no resolved subject evidence is INSUFFICIENT_EVIDENCE, never SATISFIED.
    /// - A constraint outside the bounded taxonomy is NOT_APPLICABLE and is reported, never silently dropped.
    /// - No new QA finding, rule result, PASS or FAIL is created; no existing status is altered.
    /// </summary>
    public static class BoundedCheckEvaluator
    {
        /// <summary>
        /// Evaluates bounded constraints against existing accepted measurement evidence. Measurements must be
        /// the accepted <see cref="EquipmentSpatialMeasurement"/> set; they are read only, never recomputed.
        /// </summary>
        public static BoundedCheckResult Evaluate(IReadOnlyList<BoundedConstraint> constraints, PlotSpatialMeasurementModel measurements, out BoundedCheckDiagnostics diagnostics)
        {
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));
            if (measurements == null) throw new ArgumentNullException(nameof(measurements));

            var results = new List<BoundedConstraintCheck>();
            var reasons = new List<string>();

            List<EquipmentSpatialMeasurement> spatial = (measurements.EquipmentSpatialMeasurements ?? new List<EquipmentSpatialMeasurement>())
                .Where(x => x != null).ToList();

            foreach (BoundedConstraint constraint in constraints.OrderBy(x => x.ConstraintId, StringComparer.Ordinal))
            {
                if (constraint == null) continue;

                string? relationship = BoundedRelationship.TryResolve(constraint.RequiredMeasurementBasisKey);
                if (relationship == null)
                {
                    results.Add(NotApplicable(constraint, "measurement basis is outside the bounded PlantModel v1 taxonomy"));
                    reasons.Add("not-applicable:" + constraint.ConstraintId + ": basis '" + constraint.RequiredMeasurementBasisKey + "' is outside the bounded taxonomy");
                    continue;
                }

                if (constraint.ConstraintTreatment == ConstraintTreatment.ReviewRequired)
                {
                    // A review-required constraint is surfaced, never numerically evaluated.
                    results.Add(ReviewRequired(constraint, "authority is not auto-executable"));
                    reasons.Add("review-required:" + constraint.ConstraintId + ": constraint is not executable and must not produce a distance verdict");
                    continue;
                }

                List<EquipmentSpatialMeasurement> forBasis = spatial
                    .Where(x => string.Equals(x.EngineeringMeasurementBasisKey, constraint.RequiredMeasurementBasisKey, StringComparison.Ordinal))
                    .ToList();

                if (forBasis.Count == 0)
                {
                    results.Add(InsufficientEvidence(constraint, "no resolved subject evidence for basis '" + constraint.RequiredMeasurementBasisKey + "'"));
                    reasons.Add("insufficient-evidence:" + constraint.ConstraintId + ": no resolved measurement evidence for the basis");
                    continue;
                }

                // Only existing accepted measurements are compared; no distance is recomputed here.
                double observedMeters = forBasis.Min(x => x.Distance2D);
                double requiredMeters = (double)constraint.MinimumDistanceMeters;

                if (observedMeters >= requiredMeters) results.Add(Satisfied(constraint, forBasis, observedMeters));
                else results.Add(Violated(constraint, forBasis, observedMeters));
            }

            results = results.OrderBy(x => x.ConstraintId, StringComparer.Ordinal).ToList();

            diagnostics = new BoundedCheckDiagnostics
            {
                ConstraintCount = results.Count,
                SatisfiedCount = results.Count(x => x.State == ProposalConstraintState.Satisfied),
                ViolatedCount = results.Count(x => x.State == ProposalConstraintState.Violated),
                InsufficientEvidenceCount = results.Count(x => x.State == ProposalConstraintState.InsufficientEvidence),
                ReviewRequiredCount = results.Count(x => x.State == ProposalConstraintState.ReviewRequired),
                NotApplicableCount = results.Count(x => x.State == ProposalConstraintState.NotApplicable),
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return new BoundedCheckResult { Checks = results };
        }

        private static BoundedConstraintCheck Satisfied(BoundedConstraint constraint, IReadOnlyList<EquipmentSpatialMeasurement> evidence, double observedMeters)
        {
            return new BoundedConstraintCheck
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                Relationship = BoundedRelationship.TryResolve(constraint.RequiredMeasurementBasisKey),
                State = ProposalConstraintState.Satisfied,
                ObservedDistanceMeters = observedMeters,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                EvidenceHandles = EvidenceFor(evidence)
            };
        }

        private static BoundedConstraintCheck Violated(BoundedConstraint constraint, IReadOnlyList<EquipmentSpatialMeasurement> evidence, double observedMeters)
        {
            return new BoundedConstraintCheck
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                Relationship = BoundedRelationship.TryResolve(constraint.RequiredMeasurementBasisKey),
                State = ProposalConstraintState.Violated,
                ObservedDistanceMeters = observedMeters,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                EvidenceHandles = EvidenceFor(evidence)
            };
        }

        private static BoundedConstraintCheck InsufficientEvidence(BoundedConstraint constraint, string reason)
        {
            return new BoundedConstraintCheck
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                Relationship = BoundedRelationship.TryResolve(constraint.RequiredMeasurementBasisKey),
                State = ProposalConstraintState.InsufficientEvidence,
                ObservedDistanceMeters = null,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                Reason = reason
            };
        }

        private static BoundedConstraintCheck ReviewRequired(BoundedConstraint constraint, string reason)
        {
            return new BoundedConstraintCheck
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                Relationship = BoundedRelationship.TryResolve(constraint.RequiredMeasurementBasisKey),
                State = ProposalConstraintState.ReviewRequired,
                ObservedDistanceMeters = null,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                Reason = reason
            };
        }

        private static BoundedConstraintCheck NotApplicable(BoundedConstraint constraint, string reason)
        {
            return new BoundedConstraintCheck
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                Relationship = null,
                State = ProposalConstraintState.NotApplicable,
                ObservedDistanceMeters = null,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                Reason = reason
            };
        }

        private static List<string> EvidenceFor(IReadOnlyList<EquipmentSpatialMeasurement> evidence)
        {
            // Portable evidence identifiers only, ordered deterministically; never a host entity.
            return evidence
                .Where(x => x != null)
                .SelectMany(x => new[] { x.GeometrySourceHandle, x.LabelSourceHandle })
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>The bounded check outcome for one constraint.</summary>
    public sealed class BoundedConstraintCheck
    {
        public string ConstraintId { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string ConstraintTreatment { get; set; } = string.Empty;
        public string MeasurementBasisKey { get; set; } = string.Empty;
        public string? Relationship { get; set; }
        /// <summary>One of the <see cref="ProposalConstraintState"/> values.</summary>
        public string State { get; set; } = string.Empty;
        public double? ObservedDistanceMeters { get; set; }
        public decimal RequiredDistanceMeters { get; set; }
        public List<string> EvidenceHandles { get; set; } = new List<string>();
        public string? Reason { get; set; }
    }

    public sealed class BoundedCheckResult
    {
        public List<BoundedConstraintCheck> Checks { get; set; } = new List<BoundedConstraintCheck>();
    }

    /// <summary>
    /// Deterministic diagnostics for the bounded check. Counts are reported per state so that
    /// INSUFFICIENT_EVIDENCE and REVIEW_REQUIRED stay visible and are never collapsed into satisfied.
    /// </summary>
    public sealed class BoundedCheckDiagnostics
    {
        public int ConstraintCount { get; set; }
        public int SatisfiedCount { get; set; }
        public int ViolatedCount { get; set; }
        public int InsufficientEvidenceCount { get; set; }
        public int ReviewRequiredCount { get; set; }
        public int NotApplicableCount { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
