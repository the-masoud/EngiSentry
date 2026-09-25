using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;

namespace EngiSentry.Rules
{
    public sealed class EngineeringDistanceRuleEvaluationException : Exception
    {
        public EngineeringDistanceRuleEvaluationException(string message) : base(message) { }
    }

    public static class EngineeringDistanceRuleEvaluator
    {
        public static IReadOnlyList<EngineeringDistanceRuleEvaluation> Evaluate(EngineeringDistanceRuleRegistry ruleRegistry, PlotPlanModel plotPlan)
        {
            if (ruleRegistry == null) throw new ArgumentNullException(nameof(ruleRegistry));
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (plotPlan.Drawing == null) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation Drawing is required");

            DrawingUnitConflictEvidence? conflict = UnitConflict(plotPlan.Drawing);
            if (conflict != null)
                return ruleRegistry.Rules.OrderBy(x => x.RuleId, StringComparer.Ordinal)
                    .Select(rule => ConflictEvaluation(rule.RuleId, rule.MeasurementKind, rule.MinimumDistanceMeters, rule.RequiredMeasurementBasisKey, rule.RequiredApplicabilityKey, rule.CriteriaProvenance, conflict, ApplicableEntityCount(ApplicablePlacements(plotPlan, rule.RequiredApplicabilityKey)))).ToList();
            DrawingUnitResolution unitResolution = DrawingUnitResolver.Resolve(plotPlan.Drawing.InsertionUnits!);
            List<EquipmentSpatialMeasurement> measurements = ValidateMeasurements(plotPlan);

            return ruleRegistry.Rules
                .OrderBy(x => x.RuleId, StringComparer.Ordinal)
                .Select(rule => EvaluateRule(rule, plotPlan, measurements, unitResolution))
                .ToList();
        }

        /// <summary>
        /// Phase E governed bridge: evaluates ONE already-governed <see cref="BoundedConstraint"/> against the
        /// accepted plot plan and returns the accepted per-placement violation vocabulary for it. This exists so
        /// the proposal runtime never collapses one rule/basis to a single global measurement: every violated
        /// <see cref="EngineeringDistanceViolation.PlacementId"/> survives as its own proposal.
        ///
        /// This is deliberately NOT a second evaluation implementation. It feeds the exact same
        /// <see cref="EvaluateRuleCore"/> the registry path uses, so the accepted selection semantics — GroupBy
        /// PlacementId, minimum Distance2D per placement, MeasurementId ordinal tie-break, measurement-basis and
        /// applicability filtering, <see cref="DrawingUnitResolver"/> conversion — are shared, not copied.
        ///
        /// Nothing is resolved or synthesized here. The constraint is already governed by the accepted Phase C
        /// projection, so no registry, no criteria provenance, no project directory, source, clause, approval,
        /// authority, pack or rule value is created or required. The evaluation records
        /// <see cref="EngineeringDistanceRuleEvaluation.CriteriaProvenance"/> as null, because a bounded
        /// constraint carries no criteria provenance and inventing one would fabricate governance evidence.
        /// </summary>
        public static EngineeringDistanceRuleEvaluation EvaluateGovernedConstraint(BoundedConstraint constraint, PlotPlanModel plotPlan)
        {
            if (constraint == null) throw new ArgumentNullException(nameof(constraint));
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (plotPlan.Drawing == null) throw new EngineeringDistanceRuleEvaluationException("governed engineering distance evaluation Drawing is required");

            DrawingUnitConflictEvidence? conflict = UnitConflict(plotPlan.Drawing);
            if (conflict != null)
                return ConflictEvaluation(constraint.Rule?.RuleId ?? constraint.ConstraintId, constraint.MeasurementKind, constraint.MinimumDistanceMeters, constraint.RequiredMeasurementBasisKey, constraint.RequiredApplicabilityKey, null, conflict, ApplicableEntityCount(ApplicablePlacements(plotPlan, constraint.RequiredApplicabilityKey)));
            DrawingUnitResolution unitResolution = DrawingUnitResolver.Resolve(plotPlan.Drawing.InsertionUnits!);
            List<EquipmentSpatialMeasurement> measurements = ValidateMeasurements(plotPlan);

            return EvaluateRuleCore(
                constraint.Rule?.RuleId ?? constraint.ConstraintId,
                constraint.MeasurementKind,
                constraint.MinimumDistanceMeters,
                constraint.RequiredMeasurementBasisKey,
                constraint.RequiredApplicabilityKey,
                null,
                ApplicablePlacements(plotPlan, constraint.RequiredApplicabilityKey),
                measurements,
                unitResolution);
        }

        private static List<EquipmentSpatialMeasurement> ValidateMeasurements(PlotPlanModel plotPlan)
        {
            if (plotPlan.Measurements == null) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation Measurements are required");
            if (plotPlan.Measurements.EquipmentSpatialMeasurements == null) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation EquipmentSpatialMeasurements are required");

            var validated = new List<EquipmentSpatialMeasurement>();
            var measurementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentSpatialMeasurement measurement in plotPlan.Measurements.EquipmentSpatialMeasurements)
            {
                if (measurement == null) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation contains a null spatial measurement");
                if (string.IsNullOrWhiteSpace(measurement.MeasurementId)) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation MeasurementId is required");
                if (string.IsNullOrWhiteSpace(measurement.PlacementId)) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation PlacementId is required: " + measurement.MeasurementId);
                if (string.IsNullOrWhiteSpace(measurement.SubjectKind)) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation SubjectKind is required: " + measurement.MeasurementId);
                if (!IsFinite(measurement.Distance2D) || measurement.Distance2D < 0d) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation Distance2D must be finite and non-negative: " + measurement.MeasurementId);
                if (!measurementIds.Add(measurement.MeasurementId)) throw new EngineeringDistanceRuleEvaluationException("duplicate engineering distance evaluation MeasurementId: " + measurement.MeasurementId);
                validated.Add(measurement);
            }

            return validated;
        }

        /// <summary>
        /// The one accepted rule-evaluation implementation. Both public entry points — the registry path and the
        /// Phase E governed bridge — reach this same core, so no selection semantics are duplicated. The
        /// parameters are exactly the fields a governed bounded constraint already carries; provenance is passed
        /// through verbatim and is null for the governed path.
        /// </summary>
        private static EngineeringDistanceRuleEvaluation EvaluateRuleCore(
            string ruleId,
            string measurementKind,
            decimal minimumDistanceMeters,
            string requiredMeasurementBasisKey,
            string requiredApplicabilityKey,
            EngineeringDistanceCriteriaProvenance? criteriaProvenance,
            IReadOnlyList<EquipmentPlacement> applicablePlacements,
            IEnumerable<EquipmentSpatialMeasurement> measurements,
            DrawingUnitResolution unitResolution)
        {
            string subjectKind = measurementKind == "EquipmentToRoad2D" ? "Road" : "Boundary";
            double minimumDistanceDrawingUnits = DrawingUnitResolver.MetersToDrawingUnits(decimal.ToDouble(minimumDistanceMeters), unitResolution);
            List<EquipmentPlacement> canonicalRepresentatives = CanonicalRepresentatives(applicablePlacements);
            var evaluableIds = canonicalRepresentatives.ToDictionary(x => x.PlacementId, x => x.EquipmentEntityId, StringComparer.Ordinal);
            List<EquipmentSpatialMeasurement> governing = measurements
                .Where(x => string.Equals(x.SubjectKind, subjectKind, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringMeasurementBasisKey, requiredMeasurementBasisKey, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringApplicabilityKey, requiredApplicabilityKey, StringComparison.Ordinal)
                    && evaluableIds.TryGetValue(x.PlacementId, out string? entityId)
                    && string.Equals(x.EquipmentEntityId, entityId, StringComparison.Ordinal))
                .GroupBy(x => x.EquipmentEntityId, StringComparer.Ordinal)
                .Select(x => x.OrderBy(y => y.PlacementId, StringComparer.Ordinal).ThenBy(y => y.Distance2D).ThenBy(y => y.MeasurementId, StringComparer.Ordinal).First())
                .OrderBy(x => x.EquipmentEntityId, StringComparer.Ordinal)
                .ThenBy(x => x.MeasurementId, StringComparer.Ordinal)
                .ToList();

            var evaluation = new EngineeringDistanceRuleEvaluation
            {
                RuleId = ruleId,
                MeasurementKind = measurementKind,
                MinimumDistanceMeters = minimumDistanceMeters,
                MinimumDistanceDrawingUnits = minimumDistanceDrawingUnits,
                InsertionUnits = unitResolution.InsertionUnits,
                CanonicalUnit = unitResolution.CanonicalUnit,
                MetersPerDrawingUnit = unitResolution.MetersPerDrawingUnit,
                RequiredMeasurementBasisKey = requiredMeasurementBasisKey,
                RequiredApplicabilityKey = requiredApplicabilityKey,
                CriteriaProvenance = CopyCriteriaProvenance(criteriaProvenance),
                EvaluatedSubjectCount = governing.Count,
                TotalApplicablePlacementCount = ApplicableEntityCount(applicablePlacements),
                EvaluatedPlacementCount = governing.Count,
                UnresolvedPlacementCount = ApplicableEntityCount(applicablePlacements) - governing.Count
            };

            foreach (EquipmentSpatialMeasurement measurement in governing)
            {
                if (measurement.Distance2D < minimumDistanceDrawingUnits)
                {
                    evaluation.Violations.Add(new EngineeringDistanceViolation
                    {
                        MeasurementId = measurement.MeasurementId,
                        PlacementId = measurement.PlacementId,
                        EquipmentEntityId = measurement.EquipmentEntityId,
                        NormalizedTag = measurement.NormalizedTag,
                        SubjectKind = measurement.SubjectKind,
                        ConstraintId = measurement.ConstraintId,
                        ActualDistanceDrawingUnits = measurement.Distance2D,
                        ActualDistanceMeters = DrawingUnitResolver.DrawingUnitsToMeters(measurement.Distance2D, unitResolution),
                        LabelSourceHandle = measurement.LabelSourceHandle,
                        GeometrySourceHandle = measurement.GeometrySourceHandle,
                        ClosestPointX = measurement.ClosestPointX,
                        ClosestPointY = measurement.ClosestPointY,
                        MeasurementBasisKey = measurement.EngineeringMeasurementBasisKey ?? string.Empty,
                        ApplicabilityKey = measurement.EngineeringApplicabilityKey ?? string.Empty
                    });
                }
            }

            evaluation.Violations = evaluation.Violations.OrderBy(x => x.PlacementId, StringComparer.Ordinal).ThenBy(x => x.MeasurementId, StringComparer.Ordinal).ToList();
            evaluation.ViolationCount = evaluation.Violations.Count;
            evaluation.Status = evaluation.ViolationCount > 0 ? "VIOLATIONS" : evaluation.TotalApplicablePlacementCount > 0 && evaluation.UnresolvedPlacementCount == 0 ? "PASS" : "INSUFFICIENT_EVIDENCE";
            return evaluation;
        }

        /// <summary>Registry path adapter: delegates to the shared core, carrying the rule's own provenance.</summary>
        private static EngineeringDistanceRuleEvaluation EvaluateRule(EngineeringDistanceRuleDefinition rule, PlotPlanModel plotPlan, IEnumerable<EquipmentSpatialMeasurement> measurements, DrawingUnitResolution unitResolution)
        {
            return EvaluateRuleCore(rule.RuleId, rule.MeasurementKind, rule.MinimumDistanceMeters,
                rule.RequiredMeasurementBasisKey, rule.RequiredApplicabilityKey, rule.CriteriaProvenance,
                ApplicablePlacements(plotPlan, rule.RequiredApplicabilityKey),
                measurements, unitResolution);
        }

        private static List<EquipmentPlacement> ApplicablePlacements(PlotPlanModel plotPlan, string key)
        {
            if (plotPlan.EquipmentPlacements == null) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation EquipmentPlacements are required");
            List<EquipmentPlacement> applicable = plotPlan.EquipmentPlacements.Where(x => x != null && string.Equals(x.EngineeringApplicabilityKey, key, StringComparison.Ordinal)).ToList();
            if (applicable.Any(x => string.IsNullOrWhiteSpace(x.PlacementId)) || applicable.GroupBy(x => x.PlacementId, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation applicable PlacementIds must be unique and nonempty");
            if (applicable.Any(x => string.IsNullOrWhiteSpace(x.EquipmentEntityId))) throw new EngineeringDistanceRuleEvaluationException("engineering distance evaluation applicable EquipmentEntityIds must be canonical, unique identities");
            return applicable;
        }

        private static int ApplicableEntityCount(IEnumerable<EquipmentPlacement> applicablePlacements) => applicablePlacements.Select(x => x.EquipmentEntityId).Distinct(StringComparer.Ordinal).Count();

        private static List<EquipmentPlacement> CanonicalRepresentatives(IReadOnlyList<EquipmentPlacement> applicablePlacements)
        {
            var representatives = new List<EquipmentPlacement>();
            foreach (IGrouping<string, EquipmentPlacement> entity in applicablePlacements.GroupBy(x => x.EquipmentEntityId, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                List<EquipmentPlacement> complete = entity.Where(IsEvaluablePlacement).OrderBy(x => x.PlacementId, StringComparer.Ordinal).ToList();
                if (complete.Count == 0) continue;
                decimal easting = complete[0].Easting.SelectedValue!.Value;
                decimal northing = complete[0].Northing.SelectedValue!.Value;
                if (complete.Any(x => x.Easting.SelectedValue!.Value != easting || x.Northing.SelectedValue!.Value != northing)) continue;
                representatives.Add(complete[0]);
            }
            return representatives;
        }

        private static bool IsEvaluablePlacement(EquipmentPlacement placement) =>
            placement.PlacementStatus == "Complete" && placement.Easting?.Status == "Resolved" && placement.Northing?.Status == "Resolved"
            && placement.Easting.SelectedValue.HasValue && placement.Northing.SelectedValue.HasValue;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static DrawingUnitConflictEvidence? UnitConflict(SemanticDrawing drawing)
        {
            List<DrawingUnitDeclaration> declarations = drawing.ExplicitUnitDeclarations ?? new List<DrawingUnitDeclaration>();
            if (!declarations.Any(x => !string.Equals(x.Unit, drawing.InsertionUnits, StringComparison.Ordinal))) return null;
            return new DrawingUnitConflictEvidence
            {
                Insunits = drawing.InsertionUnits ?? string.Empty,
                ExplicitDeclarations = declarations.Select(x => new DrawingUnitDeclaration { Unit = x.Unit, RawText = x.RawText, SourceHandle = x.SourceHandle, SourceLayer = x.SourceLayer, SpaceKind = x.SpaceKind }).ToList()
            };
        }

        private static EngineeringDistanceRuleEvaluation ConflictEvaluation(string ruleId, string measurementKind, decimal minimumDistanceMeters, string requiredMeasurementBasisKey, string requiredApplicabilityKey, EngineeringDistanceCriteriaProvenance? criteriaProvenance, DrawingUnitConflictEvidence conflict, int applicableCount)
        {
            return new EngineeringDistanceRuleEvaluation
            {
                RuleId = ruleId, MeasurementKind = measurementKind, MinimumDistanceMeters = minimumDistanceMeters,
                InsertionUnits = conflict.Insunits, RequiredMeasurementBasisKey = requiredMeasurementBasisKey,
                RequiredApplicabilityKey = requiredApplicabilityKey, CriteriaProvenance = CopyCriteriaProvenance(criteriaProvenance),
                Status = "INSUFFICIENT_EVIDENCE", UnitConflict = conflict,
                TotalApplicablePlacementCount = applicableCount, UnresolvedPlacementCount = applicableCount
            };
        }

        private static EngineeringDistanceCriteriaProvenance? CopyCriteriaProvenance(EngineeringDistanceCriteriaProvenance? provenance) => provenance == null ? null : new EngineeringDistanceCriteriaProvenance
        {
            SourceKind = provenance.SourceKind,
            ProjectDirectory = provenance.ProjectDirectory,
            CriteriaSettingKey = provenance.CriteriaSettingKey,
            SchemaVersion = provenance.SchemaVersion,
            ConfiguredMinimumDistanceMeters = provenance.ConfiguredMinimumDistanceMeters
        };
    }
}
