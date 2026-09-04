using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    public static class ProfilePlotPlanQaRules
    {
        public static IReadOnlyList<IPlotPlanQaRule> Create(ProfileResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            EffectiveRuleSetting boundary = resolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "PP-CLEAR-001");
            EffectiveRuleSetting road = resolution.EffectiveRuleSettings.Single(x => x.EffectiveRuleId == "PP-CLEAR-002");
            return BuiltInPlotPlanQaRules.All.Concat(new IPlotPlanQaRule[]
            {
                new MinimumBoundaryClearanceRule(boundary.EffectiveMinimumDistanceDrawingUnits ?? 0m),
                new MinimumRoadClearanceRule(road.EffectiveMinimumDistanceDrawingUnits ?? 0m)
            }).OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
        }
    }

    internal abstract class MinimumClearanceRuleBase : IPlotPlanQaRule
    {
        private readonly decimal minimumDistance;
        protected MinimumClearanceRuleBase(decimal minimumDistance) { this.minimumDistance = minimumDistance; }
        public abstract string RuleId { get; }
        public abstract string Title { get; }
        public string Category => "PlotPlanClearance";
        public string Severity => "Warning";
        public string Description => Title;
        protected abstract string SubjectKind { get; }
        protected abstract string Message { get; }

        public QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            double threshold = decimal.ToDouble(minimumDistance);
            var placementHandles = plotPlan.EquipmentPlacements.ToDictionary(x => x.PlacementId, StringComparer.Ordinal);
            var nearest = plotPlan.Measurements.EquipmentSpatialMeasurements
                .Where(x => string.Equals(x.SubjectKind, SubjectKind, StringComparison.Ordinal))
                .GroupBy(x => x.PlacementId, StringComparer.Ordinal)
                .Select(group => group.OrderBy(x => x.Distance2D).ThenBy(x => x.MeasurementId, StringComparer.Ordinal).First())
                .OrderBy(x => x.PlacementId, StringComparer.Ordinal)
                .ToList();
            var findings = new List<QaFinding>();
            foreach (EquipmentSpatialMeasurement measurement in nearest)
            {
                if (!(measurement.Distance2D < threshold)) continue;
                EquipmentPlacement placement;
                placementHandles.TryGetValue(measurement.PlacementId, out placement);
                var evidence = new ClearanceEvidence
                {
                    MeasurementId = measurement.MeasurementId,
                    PlacementId = measurement.PlacementId,
                    EquipmentEntityId = measurement.EquipmentEntityId,
                    NormalizedTag = measurement.NormalizedTag,
                    SubjectKind = measurement.SubjectKind,
                    ConstraintId = measurement.ConstraintId,
                    MinimumDistanceDrawingUnits = minimumDistance,
                    ActualDistance2D = measurement.Distance2D,
                    LabelSourceHandle = measurement.LabelSourceHandle,
                    GeometrySourceHandle = measurement.GeometrySourceHandle,
                    ClosestPointX = measurement.ClosestPointX,
                    ClosestPointY = measurement.ClosestPointY
                };
                findings.Add(new QaFinding
                {
                    FindingId = RuleId + ":" + measurement.PlacementId,
                    RuleId = RuleId,
                    Severity = Severity,
                    Category = Category,
                    SubjectType = "EquipmentPlacement",
                    SubjectId = measurement.PlacementId,
                    Message = Message,
                    Evidence = new QaEvidence { ClearanceEvidence = evidence },
                    SourceHandles = new[] { placement == null ? string.Empty : placement.TagSourceHandle, measurement.LabelSourceHandle, measurement.GeometrySourceHandle }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList()
                });
            }
            return new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = nearest.Select(x => x.PlacementId).Distinct(StringComparer.Ordinal).Count(), FindingCount = findings.Count, Status = findings.Count == 0 ? "PASS" : "FINDINGS" }, findings);
        }
    }

    internal sealed class MinimumBoundaryClearanceRule : MinimumClearanceRuleBase
    {
        public MinimumBoundaryClearanceRule(decimal minimumDistance) : base(minimumDistance) { }
        public override string RuleId => "PP-CLEAR-001";
        public override string Title => "Minimum Equipment-to-Boundary Clearance";
        protected override string SubjectKind => "Boundary";
        protected override string Message => "Equipment placement is closer to resolved Boundary geometry than the configured minimum distance.";
    }

    internal sealed class MinimumRoadClearanceRule : MinimumClearanceRuleBase
    {
        public MinimumRoadClearanceRule(decimal minimumDistance) : base(minimumDistance) { }
        public override string RuleId => "PP-CLEAR-002";
        public override string Title => "Minimum Equipment-to-Road Centerline Clearance";
        protected override string SubjectKind => "Road";
        protected override string Message => "Equipment placement is closer to resolved Road geometry than the configured minimum distance.";
    }
}
