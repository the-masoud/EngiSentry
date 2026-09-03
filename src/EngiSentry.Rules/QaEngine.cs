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
        public static IReadOnlyList<IQaRule> All { get; } = new IQaRule[] { new EquipmentListOnlyRule(), new DrawingOnlyRule(), new UnresolvedLinkRule(), new AmbiguousLinkRule() }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
    }

    public static class QaEngine
    {
        public static QaReport Evaluate(EngineeringEntityIndex index, IEnumerable<IQaRule> rules)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var report = new QaReport { Drawing = index.Drawing, ExecutionStatus = "Complete" };
            foreach (IQaRule rule in rules.OrderBy(x => x.RuleId, StringComparer.Ordinal))
            {
                QaRuleEvaluation evaluation = rule.Evaluate(index);
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
            }
            report.OverallStatus = report.Findings.Count == 0 ? "PASS" : report.Findings.Any(x => x.Severity == "Error") ? "FAIL" : "REVIEW_REQUIRED";
            report.Statistics = new QaStatistics { RuleCount = report.RuleResults.Count, RulesPassed = report.RuleResults.Count(x => x.Status == "PASS"), RulesWithFindings = report.RuleResults.Count(x => x.Status == "FINDINGS"), TotalFindings = report.Findings.Count, InfoFindings = report.Findings.Count(x => x.Severity == "Info"), WarningFindings = report.Findings.Count(x => x.Severity == "Warning"), ErrorFindings = report.Findings.Count(x => x.Severity == "Error") };
            return report;
        }
        private static List<string> Sorted(IEnumerable<string> values) => values.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    internal abstract class QaRuleBase : IQaRule
    {
        public abstract string RuleId { get; } public abstract string Title { get; } public abstract string Category { get; } public abstract string Severity { get; } public abstract string Description { get; }
        public abstract QaRuleEvaluation Evaluate(EngineeringEntityIndex index);
        protected QaRuleEvaluation Finish(int count, IEnumerable<QaFinding> findings) { var list = findings.ToList(); return new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = count, FindingCount = list.Count, Status = list.Count == 0 ? "PASS" : "FINDINGS" }, list); }
        protected QaFinding Finding(string id, string subjectType, string subjectId, string message, QaEvidence evidence, IEnumerable<string> handles) => new QaFinding { FindingId = id, RuleId = RuleId, Severity = Severity, Category = Category, SubjectType = subjectType, SubjectId = subjectId, Message = message, Evidence = evidence, SourceHandles = handles.ToList() };
    }

    internal sealed class EquipmentListOnlyRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-001"; public override string Title => "Equipment List Evidence Without Drawing Evidence"; public override string Category => "EquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Where(x => x.HasEquipmentListEvidence && !x.HasDrawingEvidence).Select(x => Finding(RuleId + ":" + x.CanonicalId, "Equipment", x.CanonicalId, "Equipment identity has Equipment List evidence but no DrawingTag evidence.", Evidence(x), x.EquipmentListLinks.SelectMany(l => l.SourceHandles))));
        private static QaEvidence Evidence(EquipmentEntity x) => new QaEvidence { CanonicalId = x.CanonicalId, NormalizedTag = x.NormalizedTag, HasDrawingEvidence = x.HasDrawingEvidence, HasEquipmentListEvidence = x.HasEquipmentListEvidence, DrawingTagSourceHandles = x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty).ToList(), EquipmentListSourceHandles = x.EquipmentListLinks.SelectMany(l => l.SourceHandles).ToList(), Description = x.EquipmentListLinks.Select(l => l.Description).FirstOrDefault(v => v != null), Installation = x.EquipmentListLinks.Select(l => l.Installation).FirstOrDefault(v => v != null), EquipmentListLinkMethod = x.EquipmentListLinks.Select(l => l.LinkMethod).FirstOrDefault() };
    }

    internal sealed class DrawingOnlyRule : QaRuleBase
    {
        public override string RuleId => "ENGI-EQ-002"; public override string Title => "Drawing Evidence Without Equipment List Evidence"; public override string Category => "EquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index) => Finish(index.EquipmentEntities.Count, index.EquipmentEntities.Where(x => x.HasDrawingEvidence && !x.HasEquipmentListEvidence).Select(x => Finding(RuleId + ":" + x.CanonicalId, "Equipment", x.CanonicalId, "Equipment identity has DrawingTag evidence but no Equipment List evidence.", new QaEvidence { CanonicalId = x.CanonicalId, NormalizedTag = x.NormalizedTag, HasDrawingEvidence = x.HasDrawingEvidence, HasEquipmentListEvidence = x.HasEquipmentListEvidence, DrawingTagSourceHandles = x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty).ToList() }, x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty))));
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

    internal static class StableIdentity
    {
        internal static string StableKey(string? sourceType, string? sourceValue, IEnumerable<string> handles) => (sourceType ?? string.Empty) + ":" + (sourceValue ?? string.Empty) + ":" + string.Join(",", handles.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal));
    }
}
