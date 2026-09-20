using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Phase D — candidate re-evaluation (Phase A architecture I). Every proposed point is re-evaluated
    /// against ALL currently supported bounded constraints — Road, Plant Fence and Unit Boundary — using the
    /// existing accepted measurement semantics. A candidate is never presented as compliant merely because it
    /// resolves the original violation.
    ///
    /// Distance mathematics is the accepted <see cref="PointToPolylineDistance"/> helper, which reproduces the
    /// accepted <c>PlotSpatialMeasurementBuilder.BuildEngineeringSpatial</c> algorithm exactly (Closed-aware
    /// segment iteration, projection parameter clamped to [0,1], minimum with lowest-distance-then-lowest-index
    /// tie-break). No second point-to-geometry algorithm exists here. Selection follows the accepted
    /// <see cref="EngineeringDistanceRuleEvaluator"/> rule: the governing subject per basis is the
    /// minimum-distance subject, compared against the rule minimum — applied to the candidate point instead of
    /// the original placement point.
    ///
    /// Hard invariants (Phase A I.3):
    /// - INSUFFICIENT_EVIDENCE never becomes SATISFIED. A basis with no resolved subject geometry is reported
    ///   as INSUFFICIENT_EVIDENCE with zero resolved subjects, never as a pass.
    /// - Governance state is never changed by re-evaluation: a non-executable, quarantined, legacy-conflicting
    ///   or review-required constraint stays REVIEW_REQUIRED regardless of any measured distance.
    /// - Re-evaluation creates no measurement, evidence or authority; it only reads geometry that already
    ///   exists and records the outcome on the candidate.
    /// - No DWG access: geometry comes from the already-built model only.
    /// </summary>
    public static class PlacementProposalReEvaluator
    {
        /// <summary>
        /// Re-evaluates one candidate against every bounded constraint. All bounded constraints are
        /// re-evaluated, including any constraint that was not the generating one, and all outcomes are
        /// retained on the candidate's CheckedConstraints in deterministic order.
        /// </summary>
        public static PlacementProposalCandidate ReEvaluate(PlacementProposalCandidate candidate, ReEvaluationContext context, out ReEvaluationDiagnostics diagnostics)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var outcomes = new List<PlacementProposalConstraintOutcome>();
            var reasons = new List<string>();

            foreach (BoundedConstraint constraint in context.Constraints.OrderBy(x => x.ConstraintId, StringComparer.Ordinal))
            {
                if (constraint == null) continue;

                outcomes.Add(EvaluateConstraint(candidate, constraint, context, reasons));
            }

            candidate.CheckedConstraints = outcomes.OrderBy(x => x.ConstraintId, StringComparer.Ordinal).ToList();

            diagnostics = new ReEvaluationDiagnostics
            {
                CandidateId = candidate.CandidateId,
                ConstraintCount = outcomes.Count,
                SatisfiedCount = outcomes.Count(x => x.EvaluationState == ProposalConstraintState.Satisfied),
                ViolatedCount = outcomes.Count(x => x.EvaluationState == ProposalConstraintState.Violated),
                InsufficientEvidenceCount = outcomes.Count(x => x.EvaluationState == ProposalConstraintState.InsufficientEvidence),
                ReviewRequiredCount = outcomes.Count(x => x.EvaluationState == ProposalConstraintState.ReviewRequired),
                NotApplicableCount = outcomes.Count(x => x.EvaluationState == ProposalConstraintState.NotApplicable),
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return candidate;
        }

        /// <summary>
        /// Evaluates one bounded constraint at the candidate point, applying the accepted selection rule and
        /// the accepted unit conversions.
        /// </summary>
        private static PlacementProposalConstraintOutcome EvaluateConstraint(PlacementProposalCandidate candidate, BoundedConstraint constraint, ReEvaluationContext context, List<string> reasons)
        {
            string constraintId = constraint.ConstraintId;
            string basisKey = constraint.RequiredMeasurementBasisKey;

            // A constraint outside the bounded taxonomy is not re-evaluated as a pass/fail.
            if (!BoundedRelationship.IsSupported(basisKey))
            {
                reasons.Add("not-applicable:" + constraintId + ": measurement basis '" + basisKey + "' is outside the bounded taxonomy");
                return Outcome(constraint, candidate, ProposalConstraintState.NotApplicable, null, "measurement basis is outside the bounded PlantModel v1 taxonomy");
            }

            // Governance is never changed by re-evaluation: a blocked rule stays blocked regardless of distance.
            StandardsPackRule? rule = constraint.Rule;
            if (rule != null && (!rule.AutoExecutionAllowed || rule.ConstraintTreatment == ConstraintTreatment.ReviewRequired
                || rule.AuthorityClass == StandardAuthorityClasses.Quarantined || rule.AuthorityClass == StandardAuthorityClasses.LegacyConflict))
            {
                reasons.Add("review-required:" + constraintId + ": the governing rule is not auto-executable; re-evaluation does not alter governance");
                return Outcome(constraint, candidate, ProposalConstraintState.ReviewRequired, null, "the governing rule is not auto-executable; governance is unchanged by re-evaluation");
            }

            // The governing subject for the basis is the minimum-distance subject, as in the accepted evaluator.
            List<SubjectGeometry> subjects = context.SubjectsFor(basisKey);
            if (subjects.Count == 0)
            {
                reasons.Add("insufficient-evidence:" + constraintId + ": no resolved subject geometry for basis '" + basisKey + "'");
                return Outcome(constraint, candidate, ProposalConstraintState.InsufficientEvidence, null, "no resolved subject geometry for basis '" + basisKey + "'");
            }

            SubjectGeometry governing = subjects[0];
            double governingDistance = PointToPolylineDistance.PointToPolyline(candidate.ProposedEasting, candidate.ProposedNorthing, governing.Primitive);

            foreach (SubjectGeometry subject in subjects.Skip(1))
            {
                double distance = PointToPolylineDistance.PointToPolyline(candidate.ProposedEasting, candidate.ProposedNorthing, subject.Primitive);
                if (distance < governingDistance)
                {
                    governing = subject;
                    governingDistance = distance;
                }
            }

            double observedMeters = DrawingUnitResolver.DrawingUnitsToMeters(governingDistance, context.UnitResolution);

            string state = observedMeters >= (double)constraint.MinimumDistanceMeters
                ? ProposalConstraintState.Satisfied
                : ProposalConstraintState.Violated;

            return Outcome(constraint, candidate, state, observedMeters, null, governing.Subject);
        }

        private static PlacementProposalConstraintOutcome Outcome(BoundedConstraint constraint, PlacementProposalCandidate candidate, string state, double? actualDistanceMeters, string? reason, EngineeringSpatialSubject? subject = null, double? distanceDrawingUnits = null)
        {
            var handles = new List<string>();
            if (subject != null)
            {
                AddHandle(handles, subject.GeometrySourceHandle);
                foreach (string labelHandle in subject.LabelSourceHandles) AddHandle(handles, labelHandle);
            }
            AddHandle(handles, candidate.GeometrySourceHandle);

            return new PlacementProposalConstraintOutcome
            {
                ConstraintId = constraint.ConstraintId,
                RuleId = constraint.Rule?.RuleId ?? string.Empty,
                ConstraintTreatment = constraint.ConstraintTreatment,
                MeasurementBasisKey = constraint.RequiredMeasurementBasisKey,
                SubjectKind = subject?.SubjectKind ?? string.Empty,
                SubjectId = subject?.SubjectId,
                ActualDistanceMeters = actualDistanceMeters,
                RequiredDistanceMeters = constraint.MinimumDistanceMeters,
                EvaluationState = state,
                Reason = reason,
                EvidenceHandles = handles.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
        }

        /// <summary>Collects a portable evidence handle, skipping empty values so no fabricated handle is recorded.</summary>
        private static void AddHandle(List<string> handles, string? handle)
        {
            if (!string.IsNullOrEmpty(handle)) handles.Add(handle!);
        }

        /// <summary>
        /// One resolved subject geometry for a measurement basis, pairing the accepted subject record with the
        /// accepted primitive its geometry handle resolves to. Both are accepted evidence; re-evaluation never
        /// creates geometry.
        /// </summary>
        public sealed class SubjectGeometry
        {
            public SubjectGeometry(EngineeringSpatialSubject subject, PlotGeometryPrimitive primitive, double distance)
            {
                Subject = subject;
                Primitive = primitive;
                Distance = distance;
            }

            public EngineeringSpatialSubject Subject { get; }
            public PlotGeometryPrimitive Primitive { get; }
            /// <summary>Distance from the evaluated point to this subject's primitive, in drawing units.</summary>
            public double Distance { get; }
        }
    }

    /// <summary>
    /// The accepted bounded constraint set and the resolved subject geometry for each basis. Callers supply
    /// existing accepted evidence only; the context never resolves authority and never invents geometry.
    /// </summary>
    public sealed class ReEvaluationContext
    {
        /// <summary>All bounded constraints to re-evaluate, including non-generating ones.</summary>
        public IReadOnlyList<BoundedConstraint> Constraints { get; set; } = Array.Empty<BoundedConstraint>();

        /// <summary>The accepted drawing unit resolution; all conversions go through it.</summary>
        public DrawingUnitResolution UnitResolution { get; set; } = new DrawingUnitResolution();

        /// <summary>The accepted plot geometry; subjects are resolved against it by handle.</summary>
        public PlotGeometryModel? Geometry { get; set; }

        /// <summary>
        /// Resolves the subject geometry for a measurement basis, ordered deterministically. Subjects whose
        /// primitive is unresolved are excluded and counted as absent evidence, so an unresolvable basis
        /// yields INSUFFICIENT_EVIDENCE rather than a fabricated pass.
        /// </summary>
        public List<PlacementProposalReEvaluator.SubjectGeometry> SubjectsFor(string basisKey)
        {
            var resolved = new List<PlacementProposalReEvaluator.SubjectGeometry>();
            if (Geometry == null) return resolved;

            Dictionary<string, PlotGeometryPrimitive> primitiveByHandle = (Geometry.Primitives ?? new List<PlotGeometryPrimitive>())
                .Where(x => x != null && !string.IsNullOrEmpty(x.SourceHandle))
                .ToDictionary(x => x.SourceHandle!, x => x, StringComparer.Ordinal);

            foreach (EngineeringSpatialSubject subject in (Geometry.EngineeringSpatialSubjects ?? new List<EngineeringSpatialSubject>())
                .Where(x => x != null && string.Equals(x.EngineeringMeasurementBasisKey, basisKey, StringComparison.Ordinal))
                .OrderBy(x => x.SubjectId, StringComparer.Ordinal))
            {
                if (!primitiveByHandle.TryGetValue(subject.GeometrySourceHandle, out PlotGeometryPrimitive? primitive) || primitive == null) continue;
                resolved.Add(new PlacementProposalReEvaluator.SubjectGeometry(subject, primitive, 0d));
            }

            return resolved;
        }
    }

    /// <summary>Deterministic re-evaluation diagnostics. Every state is counted so none is silently collapsed.</summary>
    public sealed class ReEvaluationDiagnostics
    {
        public string CandidateId { get; set; } = string.Empty;
        public int ConstraintCount { get; set; }
        public int SatisfiedCount { get; set; }
        public int ViolatedCount { get; set; }
        public int InsufficientEvidenceCount { get; set; }
        public int ReviewRequiredCount { get; set; }
        public int NotApplicableCount { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
