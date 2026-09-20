using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Maps governed executable <see cref="StandardsPackRule"/> instances into Phase B
    /// <see cref="BoundedConstraint"/> instances for the bounded PlantModel v1 use case. Only the three
    /// supported relationships are produced: Equipment → Road, Equipment → Plant Fence and Equipment →
    /// Unit Boundary.
    ///
    /// Constraint treatment is recorded from the governed projection and stays strictly independent of source
    /// authority: a treatment value can never make a non-executable rule executable. Only a rule the existing
    /// authority chain has already marked auto-executable may carry an executing treatment; every other rule
    /// degrades to REVIEW_REQUIRED so it is surfaced rather than evaluated as a real engineering criterion.
    /// </summary>
    public static class BoundedConstraintBuilder
    {
        /// <summary>The accepted measurement kinds that back bounded constraints.</summary>
        private static readonly HashSet<string> SupportedMeasurementKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "EquipmentToRoad2D",
            "EquipmentToBoundary2D"
        };

        /// <summary>
        /// Builds bounded constraints from governed pack rules. Rules outside the bounded taxonomy, rules with
        /// an unsupported measurement kind, and rules whose evidence is unresolved are reported in the
        /// diagnostics rather than silently dropped or promoted.
        /// </summary>
        public static BoundedConstraintSet Build(IReadOnlyList<StandardsPackRule> rules, out BoundedConstraintDiagnostics diagnostics)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            var constraints = new List<BoundedConstraint>();
            var reasons = new List<string>();

            foreach (StandardsPackRule rule in rules.OrderBy(x => x.RuleId, StringComparer.Ordinal))
            {
                if (rule == null) continue;

                string? relationship = BoundedRelationship.TryResolve(rule.MeasurementBasisKey);
                if (relationship == null)
                {
                    reasons.Add("unsupported-relationship:" + rule.RuleId + ": measurement basis '" + rule.MeasurementBasisKey + "' is outside the bounded PlantModel v1 taxonomy");
                    continue;
                }

                if (!TryResolveMeasurementKind(rule.MeasurementBasisKey, out string measurementKind))
                {
                    reasons.Add("unsupported-measurement-kind:" + rule.RuleId + ": no accepted measurement kind for basis '" + rule.MeasurementBasisKey + "'");
                    continue;
                }

                if (!IsExecutableEvidence(rule))
                {
                    // A rule with no authority is not a constraint that can be evaluated. It is retained as a
                    // review-required constraint so the governance block stays visible and is never converted
                    // into a normal SATISFIED/VIOLATED outcome.
                    constraints.Add(BuildReviewRequiredConstraint(rule, relationship, measurementKind, "authority is not auto-executable"));
                    reasons.Add("non-executable-authority:" + rule.RuleId + ": " + (rule.DecisionState ?? "no execution authority"));
                    continue;
                }

                if (rule.MinimumDistanceMeters <= 0m)
                {
                    constraints.Add(BuildReviewRequiredConstraint(rule, relationship, measurementKind, "criterion is absent"));
                    reasons.Add("missing-criterion:" + rule.RuleId + ": MinimumDistanceMeters is not populated");
                    continue;
                }

                constraints.Add(new BoundedConstraint
                {
                    ConstraintId = "bounded-constraint:" + rule.RuleId,
                    Rule = rule,
                    ConstraintTreatment = ResolveTreatment(rule),
                    MeasurementKind = measurementKind,
                    RequiredMeasurementBasisKey = rule.MeasurementBasisKey ?? string.Empty,
                    RequiredApplicabilityKey = rule.ApplicabilityKey ?? string.Empty,
                    MinimumDistanceMeters = rule.MinimumDistanceMeters,
                    UnitContext = null
                });
            }

            constraints = constraints.OrderBy(x => x.ConstraintId, StringComparer.Ordinal).ToList();

            diagnostics = new BoundedConstraintDiagnostics
            {
                ConstraintCount = constraints.Count,
                ExecutableConstraintCount = constraints.Count(x => x.ConstraintTreatment != ConstraintTreatment.ReviewRequired),
                ReviewRequiredConstraintCount = constraints.Count(x => x.ConstraintTreatment == ConstraintTreatment.ReviewRequired),
                UnsupportedRelationshipCount = reasons.Count(x => x.StartsWith("unsupported-relationship:", StringComparison.Ordinal)),
                UnsupportedMeasurementKindCount = reasons.Count(x => x.StartsWith("unsupported-measurement-kind:", StringComparison.Ordinal)),
                NonExecutableAuthorityCount = reasons.Count(x => x.StartsWith("non-executable-authority:", StringComparison.Ordinal)),
                MissingCriterionCount = reasons.Count(x => x.StartsWith("missing-criterion:", StringComparison.Ordinal)),
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return new BoundedConstraintSet { Constraints = constraints };
        }

        /// <summary>
        /// An executing treatment is only ever recorded for a rule the existing authority chain already marked
        /// auto-executable. Treatment can never promote a non-executable rule.
        /// </summary>
        private static bool IsExecutableEvidence(StandardsPackRule rule)
        {
            if (!rule.AutoExecutionAllowed) return false;
            if (string.IsNullOrEmpty(rule.RuleId)) return false;
            return true;
        }

        /// <summary>
        /// Resolves the treatment recorded on the governed rule, validated against the authority state. A rule
        /// the gate did not authorize never carries an executing treatment regardless of its recorded value.
        /// Non-mandatory treatments (OPTIMIZATION_OBJECTIVE, ENGINEERING_PREFERENCE) are never allowed to carry
        /// an executing treatment either, because an executing treatment implies a numerical SATISFIED/VIOLATED
        /// verdict that a preference or objective must not produce.
        /// </summary>
        private static string ResolveTreatment(StandardsPackRule rule)
        {
            string recorded = rule.ConstraintTreatment ?? string.Empty;
            string treatment = string.IsNullOrEmpty(recorded) ? ConstraintTreatment.ReviewRequired : recorded;
            if (!rule.AutoExecutionAllowed) return ConstraintTreatment.ReviewRequired;
            if (!ConstraintTreatment.IsSupported(treatment)) return ConstraintTreatment.HardConstraint;
            if (treatment == ConstraintTreatment.OptimizationObjective) return ConstraintTreatment.ReviewRequired;
            if (treatment == ConstraintTreatment.EngineeringPreference) return ConstraintTreatment.ReviewRequired;

            return treatment;
        }

        private static bool TryResolveMeasurementKind(string? measurementBasisKey, out string measurementKind)
        {
            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge
                || measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge)
            {
                measurementKind = "EquipmentToRoad2D";
                return true;
            }

            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence
                || measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary)
            {
                measurementKind = "EquipmentToBoundary2D";
                return true;
            }

            measurementKind = string.Empty;
            return false;
        }

        private static BoundedConstraint BuildReviewRequiredConstraint(StandardsPackRule rule, string relationship, string measurementKind, string reason)
        {
            return new BoundedConstraint
            {
                ConstraintId = "bounded-constraint:" + rule.RuleId,
                Rule = rule,
                // REVIEW_REQUIRED keeps the governance block visible and prevents numerical evaluation.
                ConstraintTreatment = ConstraintTreatment.ReviewRequired,
                MeasurementKind = measurementKind,
                RequiredMeasurementBasisKey = rule.MeasurementBasisKey ?? string.Empty,
                RequiredApplicabilityKey = rule.ApplicabilityKey ?? string.Empty,
                MinimumDistanceMeters = rule.MinimumDistanceMeters,
                UnitContext = "review-required: " + reason
            };
        }
    }

    /// <summary>The deterministic set of bounded constraints produced for one governed pack projection.</summary>
    public sealed class BoundedConstraintSet
    {
        public List<BoundedConstraint> Constraints { get; set; } = new List<BoundedConstraint>();
    }

    /// <summary>
    /// Deterministic diagnostics for bounded constraint construction. Unsupported and non-executable rules are
    /// counted and explained rather than silently discarded.
    /// </summary>
    public sealed class BoundedConstraintDiagnostics
    {
        public int ConstraintCount { get; set; }
        public int ExecutableConstraintCount { get; set; }
        public int ReviewRequiredConstraintCount { get; set; }
        public int UnsupportedRelationshipCount { get; set; }
        public int UnsupportedMeasurementKindCount { get; set; }
        public int NonExecutableAuthorityCount { get; set; }
        public int MissingCriterionCount { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
