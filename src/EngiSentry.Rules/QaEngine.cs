using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    public interface IQaRule
    {
        string RuleId { get; }
        string Title { get; }
        string Category { get; }
        string Severity { get; }
        string Description { get; }
        QaRuleEvaluation Evaluate(EngineeringEntityIndex index);
    }

    public sealed class QaRuleEvaluation
    {
        public QaRuleResult Result { get; }
        public IReadOnlyList<QaFinding> Findings { get; }
        public QaRuleEvaluation(QaRuleResult result, IEnumerable<QaFinding> findings) { Result = result; Findings = findings.ToList(); }
    }

    public static class BuiltInQaRules
    {
        public static IReadOnlyList<IQaRule> All { get; } = new IQaRule[] { new EquipmentListOnlyRule(), new DrawingOnlyRule(), new MultipleDrawingTagRule(), new MultipleEquipmentListRowsRule(), new ConflictingEquipmentListValuesRule(), new UnresolvedLinkRule(), new AmbiguousLinkRule(), new MultipleStructureTagRule() }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
    }

    public static class QaEngine
    {
        public static QaReport Evaluate(EngineeringEntityIndex index, IEnumerable<IQaRule> rules)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            return Finalize(index, rules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index)));
        }

        public static QaReport Evaluate(EngineeringEntityIndex index, IEnumerable<IQaRule> rules, PlotPlanModel plotPlan, IEnumerable<IPlotPlanQaRule> plotPlanRules)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (plotPlanRules == null) throw new ArgumentNullException(nameof(plotPlanRules));
            return Finalize(index, rules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index)).Concat(plotPlanRules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index, plotPlan))));
        }

        public static QaReport EvaluateWithExternalRegister(EngineeringEntityIndex index, IEnumerable<IQaRule> rules, PlotPlanModel plotPlan, IEnumerable<IPlotPlanQaRule> plotPlanRules, ExternalEquipmentRegister register, IEnumerable<ICrossDocumentQaRule> crossRules)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (crossRules == null) throw new ArgumentNullException(nameof(crossRules));
            return Finalize(index, rules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index)).Concat(plotPlanRules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index, plotPlan))).Concat(crossRules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(x => x.Evaluate(index, register))));
        }

        public static QaReport EvaluateWithProfile(EngineeringEntityIndex index, IEnumerable<IQaRule> rules, PlotPlanModel plotPlan, IEnumerable<IPlotPlanQaRule> plotPlanRules, ProfileResolution resolution)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            if (plotPlanRules == null) throw new ArgumentNullException(nameof(plotPlanRules));
            if (resolution == null || resolution.ValidationStatus != "VALID") throw new ArgumentException("A valid profile resolution is required.", nameof(resolution));
            var settings = resolution.EffectiveRuleSettings.ToDictionary(x => x.EffectiveRuleId, StringComparer.Ordinal);
            var catalog = rules.Select(x => x.RuleId).Concat(plotPlanRules.Select(x => x.RuleId)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (catalog.Distinct(StringComparer.Ordinal).Count() != catalog.Count || catalog.Any(x => !settings.ContainsKey(x)) || settings.Count != catalog.Count) throw new ArgumentException("Profile resolution does not match the QA rule catalog.", nameof(resolution));
            IEnumerable<QaRuleEvaluation> evaluations = rules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Where(x => settings[x.RuleId].Enabled).Select(x => ApplySeverity(x.Evaluate(index), settings[x.RuleId].EffectiveSeverity))
                .Concat(plotPlanRules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Where(x => settings[x.RuleId].Enabled).Select(x => ApplySeverity(x.Evaluate(index, plotPlan), settings[x.RuleId].EffectiveSeverity)));
            return Finalize(index, evaluations, resolution);
        }

        private static QaRuleEvaluation ApplySeverity(QaRuleEvaluation evaluation, string severity)
        {
            evaluation.Result.Severity = severity;
            foreach (QaFinding finding in evaluation.Findings) finding.Severity = severity;
            return evaluation;
        }

        private static QaReport Finalize(EngineeringEntityIndex index, IEnumerable<QaRuleEvaluation> evaluations, ProfileResolution? profileResolution = null)
        {
            var report = new QaReport { Drawing = index.Drawing, ExecutionStatus = "Complete", ProfileResolution = profileResolution };
            foreach (QaRuleEvaluation evaluation in evaluations)
            {
                report.RuleResults.Add(evaluation.Result);
                report.Findings.AddRange(evaluation.Findings);
            }
            report.RuleResults = report.RuleResults.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            report.Findings = report.Findings.OrderBy(x => x.RuleId, StringComparer.Ordinal).ThenBy(x => x.SubjectId, StringComparer.Ordinal).ThenBy(x => x.FindingId, StringComparer.Ordinal).ToList();
            foreach (QaFinding finding in report.Findings)
            {
                finding.SourceHandles = Sorted(finding.SourceHandles);
                finding.Evidence.DrawingTagSourceHandles = Sorted(finding.Evidence.DrawingTagSourceHandles);
                finding.Evidence.EquipmentListSourceHandles = Sorted(finding.Evidence.EquipmentListSourceHandles);
                finding.Evidence.CandidateEntityIds = Sorted(finding.Evidence.CandidateEntityIds);
                finding.Evidence.ConflictFields = Sorted(finding.Evidence.ConflictFields);
                finding.Evidence.SourceRows = finding.Evidence.SourceRows.OrderBy(x => EquipmentListRowIdentity.Key(x.SourceHandles), StringComparer.Ordinal).ToList();
                finding.Evidence.Occurrences = finding.Evidence.Occurrences.OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
            }
            bool noEngineeringSubjects = index.EquipmentEntities.Count == 0 && index.StructureEntities.Count == 0 && index.UnresolvedLinks.Count == 0 && index.AmbiguousLinks.Count == 0;
            if (report.Findings.Count == 0 && noEngineeringSubjects)
            {
                foreach (QaRuleResult result in report.RuleResults.Where(x => x.EvaluatedSubjectCount == 0)) result.Status = "INSUFFICIENT_EVIDENCE";
                report.OverallStatus = "INSUFFICIENT_EVIDENCE";
            }
            else report.OverallStatus = report.Findings.Count == 0 ? "PASS" : report.Findings.Any(x => x.Severity == "Error") ? "FAIL" : "REVIEW_REQUIRED";
            report.Statistics = new QaStatistics { RuleCount = report.RuleResults.Count, RulesPassed = report.RuleResults.Count(x => x.Status == "PASS"), RulesWithFindings = report.RuleResults.Count(x => x.Status == "FINDINGS"), RulesNotApplicable = report.RuleResults.Count(x => x.Status == "NOT_APPLICABLE"), RulesInsufficientEvidence = report.RuleResults.Count(x => x.Status == "INSUFFICIENT_EVIDENCE"), TotalFindings = report.Findings.Count, InfoFindings = report.Findings.Count(x => x.Severity == "Info"), WarningFindings = report.Findings.Count(x => x.Severity == "Warning"), ErrorFindings = report.Findings.Count(x => x.Severity == "Error") };
            return report;
        }
        private static List<string> Sorted(IEnumerable<string> values) => values.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    internal abstract class QaRuleBase : IQaRule
    {
        public abstract string RuleId { get; } public abstract string Title { get; } public abstract string Category { get; } public abstract string Severity { get; } public abstract string Description { get; }
        public abstract QaRuleEvaluation Evaluate(EngineeringEntityIndex index);
        protected QaRuleEvaluation Finish(int count, IEnumerable<QaFinding> findings) { var list = findings.ToList(); return new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = count, FindingCount = list.Count, Status = list.Count == 0 ? "PASS" : "FINDINGS" }, list); }
        protected QaRuleEvaluation NotApplicable() => new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = 0, FindingCount = 0, Status = "NOT_APPLICABLE" }, Array.Empty<QaFinding>());
        protected QaFinding Finding(string id, string subjectType, string subjectId, string message, QaEvidence evidence, IEnumerable<string> handles) => new QaFinding { FindingId = id, RuleId = RuleId, Severity = Severity, Category = Category, SubjectType = subjectType, SubjectId = subjectId, Message = message, Evidence = evidence, SourceHandles = handles.ToList() };
    }

    internal sealed class EquipmentListOnlyRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-001"; public override string Title => "Equipment List Evidence Without Drawing Evidence"; public override string Category => "EquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => index.EquipmentEntities.Any(x => x.HasEquipmentListEvidence) ? Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Where(x => x.HasEquipmentListEvidence && !x.HasDrawingEvidence).Select(x => Finding(RuleId + ":" + x.CanonicalId, "Equipment", x.CanonicalId, "Equipment identity has Equipment List evidence but no DrawingTag evidence.", Evidence(x), x.EquipmentListLinks.SelectMany(l => l.SourceHandles)))) : NotApplicable();
        private static QaEvidence Evidence(EquipmentEntity x) => new QaEvidence { CanonicalId = x.CanonicalId, NormalizedTag = x.NormalizedTag, HasDrawingEvidence = x.HasDrawingEvidence, HasEquipmentListEvidence = x.HasEquipmentListEvidence, DrawingTagSourceHandles = x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty).ToList(), EquipmentListSourceHandles = x.EquipmentListLinks.SelectMany(l => l.SourceHandles).ToList(), Description = x.EquipmentListLinks.Select(l => l.Description).FirstOrDefault(v => v != null), Installation = x.EquipmentListLinks.Select(l => l.Installation).FirstOrDefault(v => v != null), EquipmentListLinkMethod = x.EquipmentListLinks.Select(l => l.LinkMethod).FirstOrDefault() };
    }

    internal sealed class DrawingOnlyRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-002"; public override string Title => "Drawing Evidence Without Equipment List Evidence"; public override string Category => "EquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => index.EquipmentEntities.Any(x => x.HasEquipmentListEvidence) ? Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Where(x => x.HasDrawingEvidence && !x.HasEquipmentListEvidence).Select(x => Finding(RuleId + ":" + x.CanonicalId, "Equipment", x.CanonicalId, "Equipment identity has DrawingTag evidence but no Equipment List evidence.", new QaEvidence { CanonicalId = x.CanonicalId, NormalizedTag = x.NormalizedTag, HasDrawingEvidence = x.HasDrawingEvidence, HasEquipmentListEvidence = x.HasEquipmentListEvidence, DrawingTagSourceHandles = x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty).ToList() }, x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty)))) : NotApplicable();
    }

    internal sealed class UnresolvedLinkRule : QaRuleBase
    {
        public override string RuleId => "ENGI-IDX-001"; public override string Title => "Unresolved Entity Link"; public override string Category => "EntityIndexIntegrity"; public override string Severity => "Error"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => Finish(index.UnresolvedLinks.Count, index.UnresolvedLinks.Select(x => { string key = StableIdentity.StableKey(x.SourceType, x.SourceValue, x.SourceHandles); return Finding(RuleId + ":" + key, "EntityLink", key, "Entity link could not be resolved deterministically.", new QaEvidence { SourceType = x.SourceType, SourceValue = x.SourceValue, ExpectedTargetIdentity = x.ExpectedTargetIdentity, Reason = x.Reason }, x.SourceHandles); }));
    }

    internal sealed class AmbiguousLinkRule : QaRuleBase
    {
        public override string RuleId => "ENGI-IDX-002"; public override string Title => "Ambiguous Entity Link"; public override string Category => "EntityIndexIntegrity"; public override string Severity => "Error"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => Finish(index.AmbiguousLinks.Count, index.AmbiguousLinks.Select(x => { string key = StableIdentity.StableKey(x.SourceType, x.SourceValue, x.SourceHandles); return Finding(RuleId + ":" + key, "EntityLink", key, "Entity link has multiple deterministic candidates.", new QaEvidence { SourceType = x.SourceType, SourceValue = x.SourceValue, Reason = x.Reason, CandidateEntityIds = x.CandidateEntityIds.ToList() }, x.SourceHandles); }));
    }

    internal sealed class MultipleDrawingTagRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-003"; public override string Title => "Multiple DrawingTag Evidence"; public override string Category => "EquipmentEvidence"; public override string Severity => "Info"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index)
        {
            var findings = new List<QaFinding>();
            foreach (EquipmentEntity entity in index.EquipmentEntities)
            {
                List<EquipmentOccurrence> occurrences = DrawingOccurrences(entity);
                if (occurrences.Count <= 1) continue;
                findings.Add(Finding(RuleId + ":" + entity.CanonicalId, "Equipment", entity.CanonicalId, "Equipment identity has multiple DrawingTag evidence occurrences.", new QaEvidence { CanonicalId = entity.CanonicalId, NormalizedTag = entity.NormalizedTag, OccurrenceCount = occurrences.Count, DrawingTagSourceHandles = occurrences.Select(o => o.SourceHandle!).ToList(), Occurrences = occurrences.Select(o => new QaOccurrenceEvidence { SourceHandle = o.SourceHandle!, SourceLayer = o.SourceLayer, PositionX = o.PositionX, PositionY = o.PositionY, PositionZ = o.PositionZ }).ToList() }, occurrences.Select(o => o.SourceHandle!)));
            }
            return Finish(index.EquipmentEntities.Count, findings);
        }
        private static List<EquipmentOccurrence> DrawingOccurrences(EquipmentEntity entity) => entity.Occurrences.Where(x => x.Role == "DrawingTag" && !string.IsNullOrEmpty(x.SourceHandle)).GroupBy(x => x.SourceHandle, StringComparer.Ordinal).Select(x => x.OrderBy(y => y.SourceLayer, StringComparer.Ordinal).First()).OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
    }

    internal sealed class MultipleEquipmentListRowsRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-004"; public override string Title => "Multiple Equipment List Source Rows"; public override string Category => "EquipmentEvidence"; public override string Severity => "Info"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => index.EquipmentEntities.Any(x => x.HasEquipmentListEvidence) ? Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Select(x => new { Entity = x, Rows = EquipmentListRowIdentity.DistinctRows(x.EquipmentListLinks) }).Where(x => x.Rows.Count > 1).Select(x => Finding(RuleId + ":" + x.Entity.CanonicalId, "Equipment", x.Entity.CanonicalId, "Equipment identity is referenced by multiple distinct Equipment List source rows.", new QaEvidence { CanonicalId = x.Entity.CanonicalId, NormalizedTag = x.Entity.NormalizedTag, DistinctSourceRowCount = x.Rows.Count, SourceRows = x.Rows.Select(EquipmentListRowIdentity.ToEvidence).ToList() }, x.Rows.SelectMany(r => r.SourceHandles)))) : NotApplicable();
    }

    internal sealed class ConflictingEquipmentListValuesRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-005"; public override string Title => "Conflicting Equipment List Values"; public override string Category => "EquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => index.EquipmentEntities.Any(x => x.HasEquipmentListEvidence) ? Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Select(x => new { Entity = x, Rows = EquipmentListRowIdentity.DistinctRows(x.EquipmentListLinks) }).Select(x => new { x.Entity, x.Rows, Fields = ConflictFields(x.Rows) }).Where(x => x.Fields.Count > 0).Select(x => Finding(RuleId + ":" + x.Entity.CanonicalId, "Equipment", x.Entity.CanonicalId, "Equipment identity has conflicting values across Equipment List source rows.", new QaEvidence { CanonicalId = x.Entity.CanonicalId, NormalizedTag = x.Entity.NormalizedTag, ConflictFields = x.Fields, SourceRows = x.Rows.Select(EquipmentListRowIdentity.ToEvidence).ToList() }, x.Rows.SelectMany(r => r.SourceHandles)))) : NotApplicable();
        private static List<string> ConflictFields(List<EquipmentListLink> rows) { var fields = new List<string>(); if (rows.Count > 1 && Values(rows.Select(x => x.Description)).Count > 1) fields.Add("Description"); if (rows.Count > 1 && Values(rows.Select(x => x.Installation)).Count > 1) fields.Add("Installation"); return fields.OrderBy(x => x, StringComparer.Ordinal).ToList(); }
        private static List<string> Values(IEnumerable<string?> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.Ordinal).ToList();
    }

    internal sealed class MultipleStructureTagRule : QaRuleBase
    {
        public override string RuleId => "ENGI-STR-001"; public override string Title => "Multiple StructureTag Evidence"; public override string Category => "StructureEvidence"; public override string Severity => "Info"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => Finish(index.StructureEntities.Count, index.StructureEntities.Select(x => new { Entity = x, Occurrences = x.Occurrences.Where(o => !string.IsNullOrEmpty(o.SourceHandle)).GroupBy(o => o.SourceHandle, StringComparer.Ordinal).Select(o => o.OrderBy(y => y.SourceLayer, StringComparer.Ordinal).First()).OrderBy(o => o.SourceHandle, StringComparer.Ordinal).ToList() }).Where(x => x.Occurrences.Count > 1).Select(x => Finding(RuleId + ":" + x.Entity.CanonicalId, "Structure", x.Entity.CanonicalId, "Structure identity has multiple source evidence occurrences.", new QaEvidence { CanonicalId = x.Entity.CanonicalId, NormalizedTag = x.Entity.NormalizedTag, OccurrenceCount = x.Occurrences.Count, Occurrences = x.Occurrences.Select(o => new QaOccurrenceEvidence { SourceHandle = o.SourceHandle ?? string.Empty, SourceLayer = o.SourceLayer, PositionX = o.PositionX, PositionY = o.PositionY, PositionZ = o.PositionZ }).ToList() }, x.Occurrences.Select(o => o.SourceHandle ?? string.Empty))));
    }

    internal static class EquipmentListRowIdentity
    {
        internal static List<EquipmentListLink> DistinctRows(IEnumerable<EquipmentListLink> links) => links.Where(x => Usable(x.SourceHandles)).GroupBy(x => Key(x.SourceHandles), StringComparer.Ordinal).Select(x => x.OrderBy(y => y.SourceTag, StringComparer.Ordinal).ThenBy(y => y.Description, StringComparer.Ordinal).ThenBy(y => y.Installation, StringComparer.Ordinal).First()).OrderBy(x => Key(x.SourceHandles), StringComparer.Ordinal).ToList();
        internal static string Key(IEnumerable<string> handles) => string.Join(",", handles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        internal static bool Usable(IEnumerable<string> handles) => handles.Any(x => !string.IsNullOrWhiteSpace(x));
        internal static QaEquipmentListSourceRow ToEvidence(EquipmentListLink link) => new QaEquipmentListSourceRow { SourceTag = link.SourceTag, Description = link.Description, Installation = link.Installation, SourceHandles = link.SourceHandles.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(), LinkMethod = link.LinkMethod };
    }

    internal static class StableIdentity
    {
        internal static string StableKey(string? sourceType, string? sourceValue, IEnumerable<string> handles) => (sourceType ?? string.Empty) + ":" + (sourceValue ?? string.Empty) + ":" + string.Join(",", handles.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal));
    }
}
