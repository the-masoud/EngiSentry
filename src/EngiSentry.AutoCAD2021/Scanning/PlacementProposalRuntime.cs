using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021.Scanning
{
    /// <summary>
    /// Phase E — the read-only composition that wires the accepted Phase C governed CHECK to the accepted
    /// Phase D proposal generator. It consumes already-built evidence (the plot plan, the governed pack
    /// projection and the governed aggregate) and produces zero or more <see cref="PlacementProposal"/>
    /// instances, one per violated equipment placement.
    ///
    /// This class composes accepted services only. It is not another governance resolver, another distance
    /// algorithm or another proposal model: the accepted <see cref="EngineeringDistanceRuleEvaluator"/> is the
    /// sole source of per-placement violations, and this wiring maps each accepted
    /// <see cref="EngineeringDistanceViolation"/> onto a <see cref="ViolationContext"/> and calls
    /// <see cref="PlacementProposalGenerator.Build"/>. No phase of it invents a rule, an authority, a distance,
    /// a clause or an approval, and no measurement is selected or grouped here — the evaluator's
    /// GroupBy-PlacementId semantics are reused, never duplicated.
    ///
    /// Per-placement fidelity: one governed rule/basis does not collapse to one global measurement. The
    /// accepted evaluator selects the governing measurement per PlacementId, so two violated placements under
    /// the same constraint become two proposals and a satisfied placement under the same constraint contributes
    /// none. The bounded CHECK is retained alongside purely for aggregate diagnostics; its single global row is
    /// never used to choose which placements receive a proposal.
    ///
    /// Unit consistency (Phase C acceptance caveat 15.1): the accepted
    /// <see cref="BoundedCheckEvaluator"/> compares the recorded <see cref="EquipmentSpatialMeasurement.Distance2D"/>
    /// against the rule's metre criterion and expressly does not convert units itself. The caveat assigns that
    /// responsibility to this wiring, so that CHECK is presented with a metre-consistent projection built through
    /// the accepted <see cref="DrawingUnitResolver"/>. The projection is read-only and local to the aggregate
    /// CHECK: the governed per-placement evaluation converts through the same resolver inside the accepted
    /// evaluator, and the original drawing-unit model remains the sole source of
    /// <c>ActualDistanceDrawingUnits</c>.
    ///
    /// Hard invariants:
    /// - A placement that is not VIOLATED never reaches generation; REVIEW_REQUIRED and INSUFFICIENT_EVIDENCE are
    ///   surfaced as diagnostics, never promoted to a proposal.
    /// - A violated constraint whose governing rule is not auto-executable produces no proposal. The bounded
    ///   constraint builder already degrades such rules to REVIEW_REQUIRED; this guard is repeated here so the
    ///   property cannot depend on caller assembly order.
    /// - Zero proposals is a legitimate outcome, not a failure: when governance blocks every executable basis,
    ///   the accepted pipeline yields no violation and nothing is generated.
    /// - No DWG access. The whole pipeline reads the already-built model; the only output is the proposal set.
    /// </summary>
    public static class PlacementProposalRuntime
    {
        /// <summary>
        /// Builds the proposal set for one drawing from the accepted evidence chain. Every argument is
        /// accepted evidence that the caller has already resolved; nothing is re-resolved here.
        /// </summary>
        public static PlacementProposalRunResult Build(
            PlotPlanModel plotPlan,
            IReadOnlyList<StandardsPackRule> governedRules,
            ProjectDistanceGovernanceAggregateResult governance,
            out PlacementProposalRunDiagnostics diagnostics)
        {
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (governedRules == null) throw new ArgumentNullException(nameof(governedRules));

            var reasons = new List<string>();

            // Phase C acceptance caveat 15.1: the accepted bounded evaluator compares the recorded Distance2D
            // against the rule's metre criterion and never converts units itself, so the CHECK is only
            // dimensionally correct when the wiring supplies the unit resolution. The resolver is applied once
            // here and the same resolution is reused for every metre conversion below.
            DrawingUnitResolution unitResolution = DrawingUnitResolver.Resolve(plotPlan.Drawing?.InsertionUnits ?? string.Empty);

            // Phase C governed CHECK: bounded constraints from the governed projection. The bounded evaluator is
            // retained only for the aggregate CHECK diagnostics — its single global violation row is never used
            // to choose which equipment placements receive a proposal.
            BoundedConstraintSet constraints = BoundedConstraintBuilder.Build(governedRules, out BoundedConstraintDiagnostics constraintDiagnostics);
            reasons.AddRange(constraintDiagnostics.DiagnosticReasons ?? Enumerable.Empty<string>());

            BoundedCheckResult check = BoundedCheckEvaluator.Evaluate(constraints.Constraints, MeasurementsInMeters(plotPlan.Measurements, unitResolution), out BoundedCheckDiagnostics checkDiagnostics);
            reasons.AddRange(checkDiagnostics.DiagnosticReasons ?? Enumerable.Empty<string>());

            Dictionary<string, StandardsPackRule> rulesById = governedRules
                .Where(x => x != null && !string.IsNullOrEmpty(x.RuleId))
                .ToDictionary(x => x.RuleId!, x => x, StringComparer.Ordinal);

            var proposals = new List<PlacementProposal>();
            int executableViolatedCount = 0;

            // The accepted measurements model is required for governed evaluation. Its absence is reported and
            // yields no proposals, rather than propagating the accepted evaluator's argument failure.
            if (plotPlan.Measurements == null)
            {
                reasons.Add("insufficient-evidence: the accepted measurements model is absent");
            }
            else
            {
                // The accepted engineering distance evaluator is the sole source of per-placement violations. A
                // governed bounded constraint is evaluated through the same implementation the registry path
                // uses, so one rule/basis stays one violation per PlacementId instead of collapsing to a single
                // global measurement.
                foreach (BoundedConstraint constraint in constraints.Constraints
                    .Where(x => x != null && x.ConstraintTreatment != ConstraintTreatment.ReviewRequired)
                    .OrderBy(x => x.ConstraintId, StringComparer.Ordinal))
                {
                    if (!rulesById.TryGetValue(constraint.Rule?.RuleId ?? string.Empty, out StandardsPackRule? rule) || rule == null)
                    {
                        reasons.Add("skipped:" + constraint.ConstraintId + ": no governed rule projects the constraint");
                        continue;
                    }

                    // REVIEW_REQUIRED and non-auto-executable rules must generate no proposal. The bounded
                    // constraint builder already enforces this; repeated here so the property is local.
                    if (IsGovernanceBlocked(rule))
                    {
                        reasons.Add("blocked:" + rule.RuleId + ": governing rule is " + (rule.ConstraintTreatment ?? ConstraintTreatment.ReviewRequired) + " with auto-execution " + (rule.AutoExecutionAllowed ? "allowed" : "blocked"));
                        continue;
                    }

                    EngineeringDistanceRuleEvaluation evaluation = EngineeringDistanceRuleEvaluator.EvaluateGovernedConstraint(constraint, plotPlan);

                    if (evaluation.Violations.Count == 0)
                    {
                        reasons.Add("no-violation:" + constraint.ConstraintId + ": governed evaluation status is " + evaluation.Status);
                        continue;
                    }

                    executableViolatedCount += evaluation.Violations.Count;

                    ReEvaluationContext reEvaluationContext = new ReEvaluationContext
                    {
                        Constraints = constraints.Constraints,
                        UnitResolution = unitResolution,
                        Geometry = plotPlan.Geometry
                    };

                    foreach (EngineeringDistanceViolation violation in evaluation.Violations)
                    {
                        ViolationContext context = BuildViolationContext(plotPlan, constraint, rule, violation, unitResolution, out List<string> contextReasons);
                        reasons.AddRange(contextReasons);

                        PlacementProposal proposal = PlacementProposalGenerator.Build(context, reEvaluationContext, out ProposalDiagnostics proposalDiagnostics);
                        reasons.AddRange(proposalDiagnostics.DiagnosticReasons ?? Enumerable.Empty<string>());
                        proposals.Add(proposal);
                    }
                }
            }

            diagnostics = new PlacementProposalRunDiagnostics
            {
                GovernedRuleCount = governedRules.Count,
                BoundedConstraintCount = checkDiagnostics.ConstraintCount,
                SatisfiedCount = checkDiagnostics.SatisfiedCount,
                ViolatedCount = checkDiagnostics.ViolatedCount,
                InsufficientEvidenceCount = checkDiagnostics.InsufficientEvidenceCount,
                ReviewRequiredCount = checkDiagnostics.ReviewRequiredCount,
                NotApplicableCount = checkDiagnostics.NotApplicableCount,
                ExecutableViolatedCount = executableViolatedCount,
                ProposalCount = proposals.Count,
                GovernanceItems = (governance?.Items ?? new List<ProjectDistanceGovernanceAggregateItem>()).Count,
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return new PlacementProposalRunResult { Proposals = proposals.OrderBy(x => x.ProposalId, StringComparer.Ordinal).ToList() };
        }

        /// <summary>
        /// The governing rule is blocked when the authority chain has not allowed auto-execution, or when the
        /// recorded treatment is review-required. Either condition alone forbids a proposal.
        /// </summary>
        private static bool IsGovernanceBlocked(StandardsPackRule rule)
        {
            if (!rule.AutoExecutionAllowed) return true;
            if (rule.ConstraintTreatment == ConstraintTreatment.ReviewRequired) return true;
            if (rule.AuthorityClass == StandardAuthorityClasses.Quarantined) return true;
            if (rule.AuthorityClass == StandardAuthorityClasses.LegacyConflict) return true;
            return false;
        }

        /// <summary>
        /// Phase C acceptance caveat 15.1 in wiring form: a read-only projection of the accepted measurements
        /// whose spatial <see cref="EquipmentSpatialMeasurement.Distance2D"/> is expressed in metres through the
        /// accepted <see cref="DrawingUnitResolver"/>, so the accepted bounded evaluator's metre criterion is
        /// compared against a like-dimensioned observed distance for any supported insertion unit.
        ///
        /// Only the field that evaluator compares is converted. Pair measurements and statistics are carried by
        /// reference because the evaluator reads neither, and the original drawing-unit model stays the sole
        /// source of <c>ActualDistanceDrawingUnits</c>. No distance is recomputed and no measurement is
        /// created: each projected value is the accepted recorded distance in the drawing's canonical unit.
        /// </summary>
        private static PlotSpatialMeasurementModel MeasurementsInMeters(PlotSpatialMeasurementModel? measurements, DrawingUnitResolution unitResolution)
        {
            var projected = new PlotSpatialMeasurementModel
            {
                EquipmentPairMeasurements = measurements?.EquipmentPairMeasurements ?? new List<EquipmentPairMeasurement>(),
                Statistics = measurements?.Statistics ?? new PlotSpatialMeasurementStatistics()
            };

            foreach (EquipmentSpatialMeasurement measurement in (measurements?.EquipmentSpatialMeasurements ?? new List<EquipmentSpatialMeasurement>()).Where(x => x != null))
            {
                projected.EquipmentSpatialMeasurements.Add(MeasurementInMeters(measurement, unitResolution));
            }

            return projected;
        }

        /// <summary>One accepted measurement carried verbatim, with only the compared distance converted to
        /// metres through the accepted resolver.</summary>
        private static EquipmentSpatialMeasurement MeasurementInMeters(EquipmentSpatialMeasurement measurement, DrawingUnitResolution unitResolution)
        {
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = measurement.MeasurementId,
                PlacementId = measurement.PlacementId,
                EquipmentEntityId = measurement.EquipmentEntityId,
                NormalizedTag = measurement.NormalizedTag,
                EquipmentEasting = measurement.EquipmentEasting,
                EquipmentNorthing = measurement.EquipmentNorthing,
                ConstraintId = measurement.ConstraintId,
                SubjectKind = measurement.SubjectKind,
                Axis = measurement.Axis,
                CoordinateValue = measurement.CoordinateValue,
                LabelSourceHandle = measurement.LabelSourceHandle,
                GeometrySourceHandle = measurement.GeometrySourceHandle,
                GeometrySourceLayer = measurement.GeometrySourceLayer,
                GeometryKind = measurement.GeometryKind,
                SegmentIndex = measurement.SegmentIndex,
                SegmentStartX = measurement.SegmentStartX,
                SegmentStartY = measurement.SegmentStartY,
                SegmentEndX = measurement.SegmentEndX,
                SegmentEndY = measurement.SegmentEndY,
                ClosestPointX = measurement.ClosestPointX,
                ClosestPointY = measurement.ClosestPointY,
                Distance2D = DrawingUnitResolver.DrawingUnitsToMeters(measurement.Distance2D, unitResolution),
                EngineeringMeasurementBasisKey = measurement.EngineeringMeasurementBasisKey,
                EngineeringApplicabilityKey = measurement.EngineeringApplicabilityKey
            };
        }

        /// <summary>
        /// Builds the Phase D violation context for one accepted per-placement violation. The violation itself
        /// is never recomputed: the accepted evaluator already selected the governing measurement, recorded the
        /// distances and constructed the evidence handles. What this wiring adds is only the accepted plot plan
        /// evidence the generator needs and the violation vocabulary does not carry — the equipment position, the
        /// placement status, the resolved geometry primitive and the unit resolution.
        ///
        /// The constraint identity on the context's violation is the governed bounded constraint, since that is
        /// the constraint the proposal addresses; every measured field stays the accepted evaluator's value.
        /// </summary>
        private static ViolationContext BuildViolationContext(PlotPlanModel plotPlan, BoundedConstraint constraint, StandardsPackRule rule, EngineeringDistanceViolation violation, DrawingUnitResolution unitResolution, out List<string> reasons)
        {
            reasons = new List<string>();

            PlotGeometryPrimitive? violatingGeometry = (plotPlan.Geometry?.Primitives ?? new List<PlotGeometryPrimitive>())
                .FirstOrDefault(x => x != null && string.Equals(x.SourceHandle, violation.GeometrySourceHandle, StringComparison.Ordinal));

            if (violatingGeometry == null)
            {
                reasons.Add("missing-geometry:" + constraint.ConstraintId + ": handle " + violation.GeometrySourceHandle + " did not resolve to an accepted primitive");
            }

            // The equipment position comes from the accepted measurement the evaluator selected, matched by
            // measurement identity so the position and the violation can never disagree.
            EquipmentSpatialMeasurement? evidence = (plotPlan.Measurements?.EquipmentSpatialMeasurements ?? new List<EquipmentSpatialMeasurement>())
                .FirstOrDefault(x => x != null && string.Equals(x.MeasurementId, violation.MeasurementId, StringComparison.Ordinal));

            double equipmentEasting = evidence != null ? Convert.ToDouble(evidence.EquipmentEasting, CultureInfo.InvariantCulture) : 0d;
            double equipmentNorthing = evidence != null ? Convert.ToDouble(evidence.EquipmentNorthing, CultureInfo.InvariantCulture) : 0d;

            if (evidence == null)
            {
                reasons.Add("missing-position:" + constraint.ConstraintId + ": measurement " + violation.MeasurementId + " is not present in the accepted measurements");
            }

            string placementStatus = (plotPlan.EquipmentPlacements ?? new List<EquipmentPlacement>())
                .Where(x => x != null && string.Equals(x.PlacementId, violation.PlacementId, StringComparison.Ordinal))
                .Select(x => x.PlacementStatus)
                .FirstOrDefault() ?? "Incomplete";

            return new ViolationContext
            {
                Violation = new EngineeringDistanceViolation
                {
                    MeasurementId = violation.MeasurementId,
                    PlacementId = violation.PlacementId,
                    EquipmentEntityId = violation.EquipmentEntityId,
                    NormalizedTag = violation.NormalizedTag,
                    SubjectKind = violation.SubjectKind,
                    ConstraintId = constraint.ConstraintId,
                    ActualDistanceDrawingUnits = violation.ActualDistanceDrawingUnits,
                    ActualDistanceMeters = violation.ActualDistanceMeters,
                    LabelSourceHandle = violation.LabelSourceHandle,
                    GeometrySourceHandle = violation.GeometrySourceHandle,
                    ClosestPointX = violation.ClosestPointX,
                    ClosestPointY = violation.ClosestPointY,
                    MeasurementBasisKey = violation.MeasurementBasisKey,
                    ApplicabilityKey = violation.ApplicabilityKey
                },
                Rule = rule,
                EquipmentEasting = equipmentEasting,
                EquipmentNorthing = equipmentNorthing,
                PlacementStatus = placementStatus,
                ViolatingGeometry = violatingGeometry,
                UnitResolution = unitResolution
            };
        }
    }

    /// <summary>The read-only result of one proposal run: zero or more proposals, deterministically ordered.</summary>
    public sealed class PlacementProposalRunResult
    {
        public List<PlacementProposal> Proposals { get; set; } = new List<PlacementProposal>();
    }

    /// <summary>
    /// Deterministic run diagnostics. Every CHECK state is counted so REVIEW_REQUIRED and
    /// INSUFFICIENT_EVIDENCE stay visible and are never collapsed into a proposal count.
    /// </summary>
    public sealed class PlacementProposalRunDiagnostics
    {
        public int GovernedRuleCount { get; set; }
        public int BoundedConstraintCount { get; set; }
        public int SatisfiedCount { get; set; }
        public int ViolatedCount { get; set; }
        public int InsufficientEvidenceCount { get; set; }
        public int ReviewRequiredCount { get; set; }
        public int NotApplicableCount { get; set; }
        public int ExecutableViolatedCount { get; set; }
        public int ProposalCount { get; set; }
        public int GovernanceItems { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
