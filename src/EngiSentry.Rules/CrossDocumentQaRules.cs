using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    public interface ICrossDocumentQaRule
    {
        string RuleId { get; } string Title { get; } string Category { get; } string Severity { get; } string Description { get; }
        QaRuleEvaluation Evaluate(EngineeringEntityIndex index, ExternalEquipmentRegister register);
    }
    public static class BuiltInCrossDocumentQaRules
    {
        public static IReadOnlyList<ICrossDocumentQaRule> All { get; } = new ICrossDocumentQaRule[] { new ExternalWithoutDrawingRule(), new DrawingWithoutExternalRule(), new ExternalAttributeMismatchRule() }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
    }
    internal abstract class CrossRuleBase : ICrossDocumentQaRule
    {
        public abstract string RuleId { get; } public abstract string Title { get; } public abstract string Category { get; } public abstract string Severity { get; } public abstract string Description { get; }
        public abstract QaRuleEvaluation Evaluate(EngineeringEntityIndex index, ExternalEquipmentRegister register);
        protected QaRuleEvaluation Finish(int count, IEnumerable<QaFinding> findings) { var list = findings.ToList(); return new QaRuleEvaluation(new QaRuleResult { RuleId = RuleId, Severity = Severity, EvaluatedSubjectCount = count, FindingCount = list.Count, Status = list.Count == 0 ? "PASS" : "FINDINGS" }, list); }
        protected QaFinding Finding(string id, string subjectId, string message, QaEvidence evidence, IEnumerable<string> handles) => new QaFinding { FindingId = id, RuleId = RuleId, Severity = Severity, Category = Category, SubjectType = "Equipment", SubjectId = subjectId, Message = message, Evidence = evidence, SourceHandles = handles.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList() };
        protected static Dictionary<string, EquipmentEntity> Entities(EngineeringEntityIndex index) => index.EquipmentEntities.GroupBy(x => x.NormalizedTag, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.OrderBy(e => e.CanonicalId, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
    }
    internal sealed class ExternalWithoutDrawingRule : CrossRuleBase
    {
        public override string RuleId => "XDOC-EQ-001"; public override string Title => "External Equipment Without Drawing Evidence"; public override string Category => "CrossDocumentEquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, ExternalEquipmentRegister register)
        {
            var entities = Entities(index); var findings = new List<QaFinding>();
            foreach (var record in register.Records.OrderBy(x => x.NormalizedTag, StringComparer.Ordinal))
            {
                EquipmentEntity entity; if (!entities.TryGetValue(record.NormalizedTag, out entity!) || !entity.HasDrawingEvidence)
                {
                    var evidence = new CrossDocumentEvidence { ExternalSourceRowNumber = record.SourceRowNumber, ExternalSourceTag = record.SourceTag, ExternalNormalizedTag = record.NormalizedTag, MatchedEquipmentCanonicalId = entity?.CanonicalId };
                    IEnumerable<string> handles = entity == null ? Enumerable.Empty<string>() : entity.EquipmentListLinks.SelectMany(x => x.SourceHandles);
                    findings.Add(Finding(RuleId + ":equipment:" + record.NormalizedTag, "equipment:" + record.NormalizedTag, "External Equipment Register identity has no matching DrawingTag evidence.", new QaEvidence { CrossDocumentEvidence = evidence }, handles));
                }
            }
            return Finish(register.Records.Count, findings);
        }
    }
    internal sealed class DrawingWithoutExternalRule : CrossRuleBase
    {
        public override string RuleId => "XDOC-EQ-002"; public override string Title => "Drawing Equipment Without External Register Evidence"; public override string Category => "CrossDocumentEquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, ExternalEquipmentRegister register)
        {
            var external = new HashSet<string>(register.Records.Select(x => x.NormalizedTag), StringComparer.OrdinalIgnoreCase);
            var candidates = index.EquipmentEntities.Where(x => x.HasDrawingEvidence).OrderBy(x => x.CanonicalId, StringComparer.Ordinal).ToList();
            return Finish(candidates.Count, candidates.Where(x => !external.Contains(x.NormalizedTag)).Select(x => Finding(RuleId + ":" + x.CanonicalId, x.CanonicalId, "Drawing equipment identity has no matching External Equipment Register evidence.", new QaEvidence { CrossDocumentEvidence = new CrossDocumentEvidence { MatchedEquipmentCanonicalId = x.CanonicalId } }, x.Occurrences.Where(o => o.Role == "DrawingTag").Select(o => o.SourceHandle ?? string.Empty))));
        }
    }
    internal sealed class ExternalAttributeMismatchRule : CrossRuleBase
    {
        public override string RuleId => "XDOC-EQ-003"; public override string Title => "External Equipment Attribute Mismatch"; public override string Category => "CrossDocumentEquipmentConsistency"; public override string Severity => "Warning"; public override string Description => Title;
        public override QaRuleEvaluation Evaluate(EngineeringEntityIndex index, ExternalEquipmentRegister register)
        {
            var entities = Entities(index); var findings = new List<QaFinding>();
            foreach (var record in register.Records.OrderBy(x => x.NormalizedTag, StringComparer.Ordinal))
            {
                EquipmentEntity entity; if (!entities.TryGetValue(record.NormalizedTag, out entity!) || entity.EquipmentListLinks.Count == 0) continue;
                List<EquipmentListLink> links = entity.EquipmentListLinks.OrderBy(x => string.Join(",", x.SourceHandles.OrderBy(h => h, StringComparer.Ordinal)), StringComparer.Ordinal).ToList();
                List<string> descriptions = Values(links.Select(x => x.Description)); List<string> installations = Values(links.Select(x => x.Installation)); var mismatch = new List<string>();
                if (!string.IsNullOrWhiteSpace(record.Description) && Mismatch(record.Description!, descriptions)) mismatch.Add("Description");
                if (!string.IsNullOrWhiteSpace(record.Installation) && Mismatch(record.Installation!, installations)) mismatch.Add("Installation");
                if (mismatch.Count == 0) continue;
                mismatch = mismatch.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var evidence = new CrossDocumentEvidence { ExternalSourceRowNumber = record.SourceRowNumber, ExternalSourceTag = record.SourceTag, ExternalNormalizedTag = record.NormalizedTag, MatchedEquipmentCanonicalId = entity.CanonicalId, MismatchFields = mismatch, ExternalDescription = record.Description, ExternalInstallation = record.Installation, DrawingDescription = descriptions.Count == 1 ? descriptions[0] : null, DrawingInstallation = installations.Count == 1 ? installations[0] : null };
                findings.Add(Finding(RuleId + ":" + entity.CanonicalId, entity.CanonicalId, "External Equipment Register attributes do not match the drawing Equipment List evidence.", new QaEvidence { CrossDocumentEvidence = evidence }, links.SelectMany(x => x.SourceHandles)));
            }
            return Finish(register.Records.Count, findings);
        }
        private static bool Mismatch(string value, List<string> values) => values.Count == 0 || (values.Count == 1 && !string.Equals(value.Trim(), values[0], StringComparison.OrdinalIgnoreCase));
        private static List<string> Values(IEnumerable<string?> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
