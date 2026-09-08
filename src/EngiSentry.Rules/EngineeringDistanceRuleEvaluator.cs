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

            DrawingUnitResolution unitResolution = DrawingUnitResolver.Resolve(plotPlan.Drawing.InsertionUnits!);
            List<EquipmentSpatialMeasurement> measurements = ValidateMeasurements(plotPlan);

            return ruleRegistry.Rules
                .OrderBy(x => x.RuleId, StringComparer.Ordinal)
                .Select(rule => EvaluateRule(rule, measurements, unitResolution))
                .ToList();
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

        private static EngineeringDistanceRuleEvaluation EvaluateRule(EngineeringDistanceRuleDefinition rule, IEnumerable<EquipmentSpatialMeasurement> measurements, DrawingUnitResolution unitResolution)
        {
            string subjectKind = rule.MeasurementKind == "EquipmentToRoad2D" ? "Road" : "Boundary";
            double minimumDistanceDrawingUnits = DrawingUnitResolver.MetersToDrawingUnits(decimal.ToDouble(rule.MinimumDistanceMeters), unitResolution);
            List<EquipmentSpatialMeasurement> governing = measurements
                .Where(x => string.Equals(x.SubjectKind, subjectKind, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringMeasurementBasisKey, rule.RequiredMeasurementBasisKey, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringApplicabilityKey, rule.RequiredApplicabilityKey, StringComparison.Ordinal))
                .GroupBy(x => x.PlacementId, StringComparer.Ordinal)
                .Select(x => x.OrderBy(y => y.Distance2D).ThenBy(y => y.MeasurementId, StringComparer.Ordinal).First())
                .OrderBy(x => x.PlacementId, StringComparer.Ordinal)
                .ThenBy(x => x.MeasurementId, StringComparer.Ordinal)
                .ToList();

            var evaluation = new EngineeringDistanceRuleEvaluation
            {
                RuleId = rule.RuleId,
                MeasurementKind = rule.MeasurementKind,
                MinimumDistanceMeters = rule.MinimumDistanceMeters,
                MinimumDistanceDrawingUnits = minimumDistanceDrawingUnits,
                InsertionUnits = unitResolution.InsertionUnits,
                CanonicalUnit = unitResolution.CanonicalUnit,
                MetersPerDrawingUnit = unitResolution.MetersPerDrawingUnit,
                RequiredMeasurementBasisKey = rule.RequiredMeasurementBasisKey,
                RequiredApplicabilityKey = rule.RequiredApplicabilityKey,
                CriteriaProvenance = CopyCriteriaProvenance(rule.CriteriaProvenance),
                EvaluatedSubjectCount = governing.Count
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
            evaluation.Status = governing.Count == 0 ? "INSUFFICIENT_EVIDENCE" : evaluation.ViolationCount == 0 ? "PASS" : "VIOLATIONS";
            return evaluation;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

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
