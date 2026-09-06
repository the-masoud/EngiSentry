using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    public interface IPlotPlanQaRule
    {
        string RuleId { get; } string Title { get; } string Category { get; } string Severity { get; } string Description { get; }
        QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan);
    }
    public static class BuiltInPlotPlanQaRules
    {
        public static IReadOnlyList<IPlotPlanQaRule> All { get; } = new IPlotPlanQaRule[] { new IncompletePlacementRule(), new AmbiguousPlacementRule(), new ConflictingPlacementRule(), new MissingGeometryRule(), new AmbiguousGeometryRule(), new MissingSpatialRule(), new AmbiguousSpatialRule() }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
    }
    internal abstract class PlotPlanRuleBase : IPlotPlanQaRule
    {
        public abstract string RuleId { get; } public abstract string Title { get; } public abstract string Category { get; } public abstract string Severity { get; } public abstract string Description { get; }
        public abstract QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan);
        protected QaRuleEvaluation Finish(int count, IEnumerable<QaFinding> findings) { var list = findings.ToList(); return new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = count, FindingCount = list.Count, Status = list.Count == 0 ? "PASS" : "FINDINGS" }, list); }
        protected QaRuleEvaluation NotApplicable() => new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = 0, FindingCount = 0, Status = "NOT_APPLICABLE" }, Array.Empty<QaFinding>());
        protected QaFinding Finding(string id, string type, string subject, string message, QaEvidence evidence, IEnumerable<string> handles) => new QaFinding { FindingId = id, RuleId = RuleId, Severity = Severity, Category = Category, SubjectType = type, SubjectId = subject, Message = message, Evidence = evidence, SourceHandles = handles.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList() };
        protected static QaEvidence Evidence(EquipmentPlacement p) => new QaEvidence { PlacementId = p.PlacementId, EquipmentEntityId = p.EquipmentEntityId, NormalizedTag = p.NormalizedTag, TagSourceHandle = p.TagSourceHandle, CoordinateAssociations = new List<CoordinateAssociation> { p.Easting, p.Northing } };
        protected static IEnumerable<string> Handles(EquipmentPlacement p, bool candidates) => new[] { p.TagSourceHandle }.Concat(new[] { p.Easting, p.Northing }.SelectMany(a => candidates ? a.Candidates.Select(c => c.SourceHandle) : new[] { a.SelectedSourceHandle ?? string.Empty }));
    }
    internal sealed class IncompletePlacementRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-COORD-001"; public override string Title => "Incomplete Equipment Placement"; public override string Category => "PlotPlanPlacement"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan) => plotPlan.EquipmentPlacements.Count == 0 ? NotApplicable() : Finish(plotPlan.EquipmentPlacements.Count, plotPlan.EquipmentPlacements.Where(p => p.PlacementStatus == "Incomplete").Select(p => Finding(RuleId + ":" + p.PlacementId, "EquipmentPlacement", p.PlacementId, "Equipment placement is incomplete because one or more coordinates could not be resolved.", Evidence(p), Handles(p, false))));
    }
    internal sealed class AmbiguousPlacementRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-COORD-002"; public override string Title => "Ambiguous Equipment Placement"; public override string Category => "PlotPlanPlacement"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan) => plotPlan.EquipmentPlacements.Count == 0 ? NotApplicable() : Finish(plotPlan.EquipmentPlacements.Count, plotPlan.EquipmentPlacements.Where(p => p.PlacementStatus == "Ambiguous").Select(p => Finding(RuleId + ":" + p.PlacementId, "EquipmentPlacement", p.PlacementId, "Equipment placement has ambiguous coordinate evidence.", Evidence(p), Handles(p, true))));
    }
    internal sealed class ConflictingPlacementRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-COORD-003"; public override string Title => "Conflicting Complete Equipment Placements"; public override string Category => "PlotPlanPlacement"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan)
        {
            var findings = new List<QaFinding>();
            foreach (EquipmentEntity entity in index.EquipmentEntities.OrderBy(x => x.CanonicalId, StringComparer.Ordinal))
            {
                List<EquipmentPlacement> complete = plotPlan.EquipmentPlacements.Where(p => p.EquipmentEntityId == entity.CanonicalId && p.PlacementStatus == "Complete" && p.Easting.SelectedValue.HasValue && p.Northing.SelectedValue.HasValue).OrderBy(p => p.PlacementId, StringComparer.Ordinal).ToList();
                var pairs = complete.GroupBy(p => p.Easting.SelectedValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + p.Northing.SelectedValue!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal).ToList();
                if (pairs.Count <= 1) continue;
                var evidence = new QaEvidence { CanonicalId = entity.CanonicalId, NormalizedTag = entity.NormalizedTag, ResolvedCoordinatePairs = complete.Select(p => new ResolvedCoordinatePairEvidence { PlacementId = p.PlacementId, TagSourceHandle = p.TagSourceHandle, EastingValue = p.Easting.SelectedValue!.Value, EastingSourceHandle = p.Easting.SelectedSourceHandle!, NorthingValue = p.Northing.SelectedValue!.Value, NorthingSourceHandle = p.Northing.SelectedSourceHandle! }).ToList() };
                findings.Add(Finding(RuleId + ":" + entity.CanonicalId, "Equipment", entity.CanonicalId, "Equipment identity has conflicting complete plot-plan coordinate placements.", evidence, evidence.ResolvedCoordinatePairs.SelectMany(p => new[] { p.TagSourceHandle, p.EastingSourceHandle, p.NorthingSourceHandle })));
            }
            return Finish(index.EquipmentEntities.Count, findings);
        }
    }
    internal sealed class MissingGeometryRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-GEO-001"; public override string Title => "Missing Plot Geometry Association"; public override string Category => "PlotPlanGeometry"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan) => plotPlan.Geometry.Associations.Count == 0 ? NotApplicable() : Finish(plotPlan.Geometry.Associations.Count, plotPlan.Geometry.Associations.Where(x => x.Status == "Missing").Select(x => Finding(RuleId + ":" + x.AssociationId, "PlotGeometryLabel", x.AssociationId, "Plot geometry label has no resolved supported geometry association under the M2.1 association rules.", new QaEvidence { GeometryAssociation = x }, new[] { x.LabelSourceHandle })));
    }
    internal sealed class AmbiguousGeometryRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-GEO-002"; public override string Title => "Ambiguous Plot Geometry Association"; public override string Category => "PlotPlanGeometry"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan) => plotPlan.Geometry.Associations.Count == 0 ? NotApplicable() : Finish(plotPlan.Geometry.Associations.Count, plotPlan.Geometry.Associations.Where(x => x.Status == "Ambiguous").Select(x => Finding(RuleId + ":" + x.AssociationId, "PlotGeometryLabel", x.AssociationId, "Plot geometry label has ambiguous supported geometry association evidence.", new QaEvidence { GeometryAssociation = x }, new[] { x.LabelSourceHandle }.Concat(x.Candidates.Select(c => c.SourceHandle)))));
    }

    internal sealed class MissingSpatialRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-SPAT-001"; public override string Title => "Missing Explicit Spatial Geometry Match"; public override string Category => "PlotPlanSpatialConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan) => plotPlan.Geometry.SpatialConstraints.Count == 0 ? NotApplicable() : Finish(plotPlan.Geometry.SpatialConstraints.Count, plotPlan.Geometry.SpatialConstraints.Where(x => x.Status == "Missing").Select(x => Finding(RuleId + ":" + x.ConstraintId, "SpatialCoordinateConstraint", x.ConstraintId, "Explicit Plot Plan coordinate annotation has no resolved supported local geometry match.", new QaEvidence { SpatialCoordinateConstraint = x }, new[] { x.LabelSourceHandle })));
    }

    internal sealed class AmbiguousSpatialRule : PlotPlanRuleBase
    {
        public override string RuleId => "PP-SPAT-002"; public override string Title => "Ambiguous Explicit Spatial Geometry Match"; public override string Category => "PlotPlanSpatialConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, PlotPlanModel plotPlan)
        {
                        if (plotPlan.Geometry.SpatialConstraints.Count == 0) return NotApplicable();
            IEnumerable<QaFinding> findings = plotPlan.Geometry.SpatialConstraints.Where(x => x.Status == "Ambiguous").Select(x => Finding(RuleId + ":" + x.ConstraintId, "SpatialCoordinateConstraint", x.ConstraintId, "Explicit Plot Plan coordinate annotation has multiple plausible supported geometry matches.", new QaEvidence { SpatialCoordinateConstraint = x }, new[] { x.LabelSourceHandle }.Concat(x.Candidates.Select(c => c.SourceHandle))));
            return Finish(plotPlan.Geometry.SpatialConstraints.Count, findings);
        }
    }
}
