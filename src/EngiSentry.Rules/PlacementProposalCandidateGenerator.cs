using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Phase D — candidate generation (Phase A architecture H). Deterministic normal-displacement generation
    /// from exactly one proven governed violation. No optimizer, no search, no objective function, no random
    /// sampling and no iteration toward global optimality: the strategy is a closed-form displacement along the
    /// direction that increases clearance, plus a small fixed set of additional candidates along the same
    /// direction at declared increments so the ranker has something to compare and refusal reasons are
    /// exercised.
    ///
    /// Inputs are accepted evidence only: the violation comes from the accepted
    /// <see cref="EngineeringDistanceRuleEvaluator"/> (or the Phase C governed CHECK path that wraps it), the
    /// equipment point is the existing resolved placement coordinate, and the violating geometry is the
    /// existing <see cref="PlotGeometryPrimitive"/> identified by the recorded evidence handle, evaluated
    /// through the accepted <see cref="PointToPolylineDistance"/> semantics. No geometry is invented: no
    /// footprint, orientation or extent is created to make a candidate fit.
    ///
    /// Hard invariants (Phase A H.2/H.3):
    /// - No candidate is generated when the governing rule is not auto-executable, or is quarantined,
    ///   unresolved, legacy-conflicting or review-required; the refusal is recorded as a deterministic reason.
    /// - No candidate is generated when the equipment placement is Incomplete or Ambiguous, when the subject
    ///   geometry is unresolved, when the displacement direction is degenerate and no cardinal candidate
    ///   achieves compliance, or when a required evidence handle is missing.
    /// - Identical inputs yield an identical candidate set, identical order and identical IDs. Ordering is by
    ///   displacement, then ProposedEasting, then ProposedNorthing, then GeneratingConstraintId, then
    ///   CandidateId, with StringComparer.Ordinal for strings.
    /// - No DWG access. Generation consumes already-built model evidence only and never writes anything.
    /// </summary>
    public static class PlacementProposalCandidateGenerator
    {
        /// <summary>v1 margin: minimum movement. A governed margin parameter may be added later.</summary>
        public const double MarginMeters = 0d;

        /// <summary>
        /// The fixed set of additional increments along the displacement direction, in metres. A declared
        /// constant list, not a search: it gives the ranker a small deterministic population and exercises
        /// rejection reasons. Phase A H.1 step 5.
        /// </summary>
        public static readonly double[] AdditionalIncrementsMeters = { 1d, 2d, 5d };

        /// <summary>The deterministic ordered cardinal fallback used when the displacement direction is degenerate.</summary>
        public static readonly string[] CardinalDirections = { "E", "N", "NE", "SE", "S", "W", "NW", "SW" };

        /// <summary>
        /// Generates the deterministic candidate set for one governing violation. Returns an empty set with a
        /// recorded refusal reason when Phase A H.3 refuses generation; an empty set is never silently
        /// presented as a proposal.
        /// </summary>
        public static GenerationResult Generate(ViolationContext context, out GenerationDiagnostics diagnostics)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var candidates = new List<PlacementProposalCandidate>();
            var reasons = new List<string>();

            string? refusal = RefusalReason(context);
            if (refusal != null)
            {
                diagnostics = new GenerationDiagnostics
                {
                    ViolationRuleId = context.Rule.RuleId,
                    GeneratedCount = 0,
                    Refused = true,
                    RefusalReason = refusal,
                    DiagnosticReasons = new List<string> { refusal }
                };
                return new GenerationResult { Candidates = candidates, RefusalReason = refusal };
            }

            PlacementProposalCandidate baseCandidate = GenerateBaseCandidate(context, reasons);
            candidates.Add(baseCandidate);

            // The fixed incremental set: same direction, declared increments. Constant list, not a search.
            foreach (double increment in AdditionalIncrementsMeters)
            {
                candidates.Add(GenerateIncrementalCandidate(context, increment));
            }

            // Degenerate direction: the deterministic cardinal fallback, each retained or rejected with a reason.
            if (context.IsDegenerateDirection)
            {
                foreach (string direction in CardinalDirections)
                {
                    PlacementProposalCandidate? cardinal = GenerateCardinalCandidate(context, direction, reasons);
                    if (cardinal != null) candidates.Add(cardinal);
                }
            }

            candidates = OrderCandidates(candidates);
            AssignCandidateIds(context, candidates);

            diagnostics = new GenerationDiagnostics
            {
                ViolationRuleId = context.Rule.RuleId,
                GeneratedCount = candidates.Count,
                Refused = false,
                RefusalReason = null,
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };

            return new GenerationResult { Candidates = candidates, RefusalReason = null };
        }

        /// <summary>
        /// Phase A H.3 refusal conditions. A candidate is not generated, and the proposal carries
        /// REVIEW_REQUIRED, when the governing rule is not auto-executable, when the placement is unresolved,
        /// when the geometry is unresolved, or when a required evidence handle is missing. The degenerate
        /// direction is not a refusal here: it triggers the cardinal fallback, and becomes a refusal only when
        /// no cardinal candidate achieves compliance.
        /// </summary>
        private static string? RefusalReason(ViolationContext context)
        {
            StandardsPackRule rule = context.Rule;
            EngineeringDistanceViolation violation = context.Violation;

            if (!rule.AutoExecutionAllowed)
                return "refusal:" + rule.RuleId + ": the governing rule is not auto-executable (decision state " + (rule.DecisionState ?? "none") + ")";
            if (rule.ConstraintTreatment == ConstraintTreatment.ReviewRequired)
                return "refusal:" + rule.RuleId + ": the governing rule is review-required";
            if (rule.AuthorityClass == StandardAuthorityClasses.Quarantined)
                return "refusal:" + rule.RuleId + ": the governing source is quarantined";
            if (rule.AuthorityClass == StandardAuthorityClasses.LegacyConflict)
                return "refusal:" + rule.RuleId + ": the governing source is a legacy conflict";
            if (string.IsNullOrEmpty(violation.GeometrySourceHandle))
                return "refusal:" + rule.RuleId + ": the violation carries no geometry evidence handle";
            if (string.IsNullOrEmpty(violation.MeasurementId))
                return "refusal:" + rule.RuleId + ": the violation carries no measurement identity";
            if (context.PlacementStatus != PlacementStatusComplete)
                return "refusal:" + rule.RuleId + ": the equipment placement is " + context.PlacementStatus + ", not Complete";
            if (context.ViolatingGeometry == null)
                return "refusal:" + rule.RuleId + ": the violating geometry is unresolved for handle " + violation.GeometrySourceHandle;
            if (!context.EquipmentEasting.HasValue || !context.EquipmentNorthing.HasValue)
                return "refusal:" + rule.RuleId + ": the equipment placement has no resolved coordinate evidence";
            if (violation.ActualDistanceMeters >= (double)rule.MinimumDistanceMeters)
                return "refusal:" + rule.RuleId + ": the recorded violation is no longer numerical (" + violation.ActualDistanceMeters.ToString(CultureInfo.InvariantCulture) + " >= " + rule.MinimumDistanceMeters.ToString(CultureInfo.InvariantCulture) + ")";

            return null;
        }

        private static PlacementProposalCandidate GenerateBaseCandidate(ViolationContext context, List<string> reasons)
        {
            double displacementMeters = (double)context.Rule.MinimumDistanceMeters - context.Violation.ActualDistanceMeters + MarginMeters;
            (double unitX, double unitY) = context.UnitDisplacement();
            return BuildCandidate(context, displacementMeters, unitX, unitY, context.DisplacementDirection, reasons);
        }

        private static PlacementProposalCandidate GenerateIncrementalCandidate(ViolationContext context, double incrementMeters)
        {
            double displacementMeters = (double)context.Rule.MinimumDistanceMeters - context.Violation.ActualDistanceMeters + incrementMeters;
            (double unitX, double unitY) = context.UnitDisplacement();
            return BuildCandidate(context, displacementMeters, unitX, unitY, context.DisplacementDirection, null);
        }

        /// <summary>
        /// The deterministic cardinal fallback (Phase A H.1 step 3). Each cardinal direction is evaluated and
        /// retained only when it achieves compliance against the generating constraint; otherwise it is
        /// rejected with a recorded reason, so the fallback never silently drops an attempt.
        /// </summary>
        private static PlacementProposalCandidate? GenerateCardinalCandidate(ViolationContext context, string direction, List<string> reasons)
        {
            (double dx, double dy) = UnitVector(direction);
            double displacementDrawingUnits = DrawingUnitResolver.MetersToDrawingUnits((double)context.Rule.MinimumDistanceMeters - context.Violation.ActualDistanceMeters + MarginMeters, context.UnitResolution);
            double proposedX = context.EquipmentEasting!.Value + dx * displacementDrawingUnits;
            double proposedY = context.EquipmentNorthing!.Value + dy * displacementDrawingUnits;

            double distance = PointToPolylineDistance.PointToPolyline(proposedX, proposedY, context.ViolatingGeometry!);
            double distanceMeters = DrawingUnitResolver.DrawingUnitsToMeters(distance, context.UnitResolution);

            if (distanceMeters < (double)context.Rule.MinimumDistanceMeters)
            {
                reasons.Add("rejected-cardinal:" + context.Rule.RuleId + ":" + direction + ": cardinal candidate does not achieve compliance (" + distanceMeters.ToString(CultureInfo.InvariantCulture) + " m < " + context.Rule.MinimumDistanceMeters.ToString(CultureInfo.InvariantCulture) + " m)");
                return null;
            }

            return AssembleCandidate(context, proposedX, proposedY, displacementDrawingUnits, direction, "cardinal-fallback:" + direction);
        }

        /// <summary>
        /// Builds one normal-displacement candidate. Geometry uses the exact normalized vector from the
        /// recorded closest point toward the equipment point (Phase A H.1 step 3); <paramref name="directionLabel"/>
        /// is only the human-readable approximation recorded on the candidate and never drives the position.
        /// </summary>
        private static PlacementProposalCandidate BuildCandidate(ViolationContext context, double displacementMeters, double unitX, double unitY, string directionLabel, List<string>? reasons)
        {
            double displacementDrawingUnits = DrawingUnitResolver.MetersToDrawingUnits(displacementMeters, context.UnitResolution);
            double proposedX = context.EquipmentEasting!.Value + unitX * displacementDrawingUnits;
            double proposedY = context.EquipmentNorthing!.Value + unitY * displacementDrawingUnits;
            return AssembleCandidate(context, proposedX, proposedY, displacementDrawingUnits, directionLabel, "normal-displacement");
        }

        /// <summary>
        /// Numbers the ordered candidate set. The counter is local to this call, so the input context is never
        /// mutated and two calls on the same unchanged context yield identical ids (Phase A H.2 determinism).
        /// </summary>
        private static void AssignCandidateIds(ViolationContext context, List<PlacementProposalCandidate> candidates)
        {
            for (int index = 0; index < candidates.Count; index++)
            {
                candidates[index].CandidateId = "proposal-candidate:" + context.Violation.PlacementId + ":" + context.Violation.ConstraintId + ":" + index.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static PlacementProposalCandidate AssembleCandidate(ViolationContext context, double proposedX, double proposedY, double displacementDrawingUnits, string direction, string generationStrategy)
        {
            EngineeringDistanceViolation violation = context.Violation;

            return new PlacementProposalCandidate
            {
                CandidateId = string.Empty,
                PlacementId = violation.PlacementId,
                EquipmentEntityId = violation.EquipmentEntityId,
                NormalizedTag = violation.NormalizedTag,
                OriginalEasting = context.EquipmentEasting!.Value,
                OriginalNorthing = context.EquipmentNorthing!.Value,
                ProposedEasting = proposedX,
                ProposedNorthing = proposedY,
                DisplacementX = proposedX - context.EquipmentEasting!.Value,
                DisplacementY = proposedY - context.EquipmentNorthing!.Value,
                DisplacementDistance = displacementDrawingUnits,
                DisplacementDirection = direction,
                GeneratingConstraintId = violation.ConstraintId,
                GeneratingRuleId = context.Rule.RuleId,
                LabelSourceHandle = violation.LabelSourceHandle,
                GeometrySourceHandle = violation.GeometrySourceHandle,
                GeometrySourceLayer = context.ViolatingGeometry!.SourceLayer,
                GeometryKind = context.ViolatingGeometry!.GeometryKind,
                UncheckedConstraints = new List<string>(ProposalUncheckedConstraints.Defaults),
                Assumptions = new List<string>(ProposalAssumptions.Defaults),
                RejectedReasons = new List<string>(),
                EngineeringReviewRequired = true
            };
        }

        /// <summary>The cardinal unit vector for one label. Used only by the degenerate-direction fallback
        /// (Phase A H.1 step 3); normal-case geometry uses <see cref="ViolationContext.UnitDisplacement"/>.</summary>
        private static (double dx, double dy) UnitVector(string direction)
        {
            switch (direction)
            {
                case "E": return (1d, 0d);
                case "N": return (0d, 1d);
                case "S": return (0d, -1d);
                case "W": return (-1d, 0d);
                case "NE": return (InverseSqrt2, InverseSqrt2);
                case "NW": return (-InverseSqrt2, InverseSqrt2);
                case "SE": return (InverseSqrt2, -InverseSqrt2);
                case "SW": return (-InverseSqrt2, -InverseSqrt2);
                default: return (1d, 0d);
            }
        }

        private const double InverseSqrt2 = 0.7071067811865476d;
        private const string PlacementStatusComplete = "Complete";

        /// <summary>
        /// Deterministic candidate ordering (Phase A H.2): displacement, then ProposedEasting, then
        /// ProposedNorthing, then GeneratingConstraintId, then CandidateId, Ordinal for strings.
        /// </summary>
        private static List<PlacementProposalCandidate> OrderCandidates(List<PlacementProposalCandidate> candidates)
        {
            return candidates
                .OrderBy(x => x.DisplacementDistance)
                .ThenBy(x => x.ProposedEasting)
                .ThenBy(x => x.ProposedNorthing)
                .ThenBy(x => x.GeneratingConstraintId, StringComparer.Ordinal)
                .ThenBy(x => x.CandidateId, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// The accepted evidence for one governing violation, assembled by the caller from existing accepted
    /// sources only. Every field is read-only evidence; the generator never resolves authority, recomputes a
    /// measurement, or invents geometry.
    /// </summary>
    public sealed class ViolationContext
    {
        /// <summary>The proven violation from the accepted engineering distance evaluation.</summary>
        public EngineeringDistanceViolation Violation { get; set; } = new EngineeringDistanceViolation();

        /// <summary>The governed rule the violation was produced for.</summary>
        public StandardsPackRule Rule { get; set; } = new StandardsPackRule();

        /// <summary>The existing resolved equipment placement coordinate, in drawing units.</summary>
        public double? EquipmentEasting { get; set; }
        public double? EquipmentNorthing { get; set; }

        /// <summary>The accepted placement status vocabulary value (Incomplete / Complete / Ambiguous).</summary>
        public string PlacementStatus { get; set; } = "Incomplete";

        /// <summary>The violating geometry, resolved from the recorded evidence handle. Null when unresolved.</summary>
        public PlotGeometryPrimitive? ViolatingGeometry { get; set; }

        /// <summary>The accepted drawing unit resolution; all metre/drawing-unit conversion goes through it.</summary>
        public DrawingUnitResolution UnitResolution { get; set; } = new DrawingUnitResolution();

        /// <summary>
        /// The displacement direction that increases clearance, as a human-readable approximation of the
        /// recorded closest-point evidence. This label is presentation only: normal-case geometry uses
        /// <see cref="UnitDisplacement"/> verbatim and is never quantized to this set. When the equipment point
        /// coincides with the closest point the direction is degenerate and the generator falls back to the
        /// deterministic cardinal set.
        /// </summary>
        public string DisplacementDirection
        {
            get
            {
                if (!EquipmentEasting.HasValue || !EquipmentNorthing.HasValue) return "E";
                double dx = EquipmentEasting.Value - Violation.ClosestPointX;
                double dy = EquipmentNorthing.Value - Violation.ClosestPointY;
                if (dx == 0d && dy == 0d) return "E";
                return CardinalLabel(dx, dy);
            }
        }

        /// <summary>
        /// The exact unit vector from the recorded closest point on the violating geometry toward the equipment
        /// point (Phase A H.1 step 3): the direction that increases clearance, derived from the same evidence as
        /// the measurement and normalized without quantization. Degenerate zero-length evidence yields the
        /// cardinal east vector, matching the first entry of the cardinal fallback set.
        /// </summary>
        public (double X, double Y) UnitDisplacement()
        {
            if (!EquipmentEasting.HasValue || !EquipmentNorthing.HasValue) return (1d, 0d);
            double dx = EquipmentEasting.Value - Violation.ClosestPointX;
            double dy = EquipmentNorthing.Value - Violation.ClosestPointY;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0d) return (1d, 0d);
            return (dx / length, dy / length);
        }

        /// <summary>True when the equipment point coincides with the recorded closest point.</summary>
        public bool IsDegenerateDirection
        {
            get
            {
                if (!EquipmentEasting.HasValue || !EquipmentNorthing.HasValue) return false;
                return EquipmentEasting.Value == Violation.ClosestPointX && EquipmentNorthing.Value == Violation.ClosestPointY;
            }
        }

        private static string CardinalLabel(double dx, double dy)
        {
            double angle = Math.Atan2(dy, dx);
            double degrees = angle * 180d / Math.PI;
            if (degrees < 0d) degrees += 360d;
            double sector = Math.Round(degrees / 45d) % 8d;
            switch ((int)sector)
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
    }

    /// <summary>The deterministic candidate set for one governing violation.</summary>
    public sealed class GenerationResult
    {
        public List<PlacementProposalCandidate> Candidates { get; set; } = new List<PlacementProposalCandidate>();
        /// <summary>Non-null when Phase A H.3 refused generation; the proposal must carry REVIEW_REQUIRED.</summary>
        public string? RefusalReason { get; set; }
    }

    /// <summary>Deterministic generation diagnostics. Refusals and rejections are reported, never dropped.</summary>
    public sealed class GenerationDiagnostics
    {
        public string ViolationRuleId { get; set; } = string.Empty;
        public int GeneratedCount { get; set; }
        public bool Refused { get; set; }
        public string? RefusalReason { get; set; }
        public List<string> DiagnosticReasons { get; set; } = new List<string>();
    }
}
