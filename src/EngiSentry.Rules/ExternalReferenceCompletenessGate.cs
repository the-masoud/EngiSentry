using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    /// <summary>
    /// Fail-closed completeness gate for drawings that reference unresolved external files.
    ///
    /// It uses only the scanner's existing <see cref="XrefSnapshot"/> data. It never opens, loads,
    /// resolves, detaches or traverses an external reference, and it never touches the scanner or
    /// the host-geometry extraction pipeline. The gate runs on an already-completed QA report.
    ///
    /// When at least one external reference is unresolved the gate preserves every existing finding,
    /// adds exactly one evidence/completeness finding, and forces the report to
    /// <c>INSUFFICIENT_EVIDENCE</c> so engineering compliance can never report PASS on a drawing whose
    /// governing content may live in an unresolved reference. Resolved references or no references
    /// leave the report untouched (same instance, same status, same findings).
    /// </summary>
    public static class ExternalReferenceCompletenessGate
    {
        public const string RuleId = "ENGI-REF-001";
        public const string FindingId = RuleId;
        public const string Category = "ExternalReferenceCompleteness";
        public const string Severity = "Error";
        public const string Status = "INSUFFICIENT_EVIDENCE";
        public const string Reason = "unresolved-external-reference";

        /// <summary>Unresolved external references, deterministically ordered. Empty when there are no xrefs or all are resolved.</summary>
        public static IReadOnlyList<XrefSnapshot> Unresolved(IEnumerable<XrefSnapshot>? xrefs)
        {
            return (xrefs ?? Enumerable.Empty<XrefSnapshot>())
                .Where(x => x != null && !x.IsResolved)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .ThenBy(x => x.RawPath, StringComparer.Ordinal)
                .ToList();
        }

        public static bool HasUnresolvedReferences(IEnumerable<XrefSnapshot>? xrefs) => Unresolved(xrefs).Count > 0;

        /// <summary>
        /// Applies the completeness gate to a completed QA report.
        /// - No external references, or all resolved: the report is returned unchanged.
        /// - Any unresolved external reference: every existing finding is preserved, exactly one
        ///   completeness finding is appended, statistics are recomputed, and the overall status is
        ///   forced to INSUFFICIENT_EVIDENCE (never PASS).
        /// </summary>
        public static QaReport Apply(QaReport report, IEnumerable<XrefSnapshot>? xrefs)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            List<XrefSnapshot> unresolved = Unresolved(xrefs).ToList();
            if (unresolved.Count == 0) return report;

            report.Findings.Add(new QaFinding
            {
                FindingId = FindingId,
                RuleId = RuleId,
                Severity = Severity,
                Category = Category,
                SubjectType = "ExternalReference",
                SubjectId = RuleId,
                Message = "Drawing has " + unresolved.Count + " unresolved external reference(s); engineering compliance cannot be certified from host geometry alone.",
                Evidence = new QaEvidence
                {
                    SourceType = "ExternalReference",
                    Reason = Reason,
                    OccurrenceCount = unresolved.Count
                },
                SourceHandles = unresolved
                    .Select(x => x.Name ?? x.RawPath ?? string.Empty)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList()
            });

            report.RuleResults.Add(new QaRuleResult
            {
                RuleId = RuleId,
                Severity = Severity,
                EvaluatedSubjectCount = unresolved.Count,
                FindingCount = 1,
                Status = Status
            });

            report.RuleResults = report.RuleResults.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            report.Findings = report.Findings
                .OrderBy(x => x.RuleId, StringComparer.Ordinal)
                .ThenBy(x => x.SubjectId, StringComparer.Ordinal)
                .ThenBy(x => x.FindingId, StringComparer.Ordinal)
                .ToList();
            report.Statistics = Statistics(report.RuleResults, report.Findings);
            report.OverallStatus = Status;
            return report;
        }

        private static QaStatistics Statistics(List<QaRuleResult> rules, List<QaFinding> findings) => new QaStatistics
        {
            RuleCount = rules.Count,
            RulesPassed = rules.Count(x => x.Status == "PASS"),
            RulesWithFindings = rules.Count(x => x.Status == "FINDINGS"),
            RulesNotApplicable = rules.Count(x => x.Status == "NOT_APPLICABLE"),
            RulesInsufficientEvidence = rules.Count(x => x.Status == "INSUFFICIENT_EVIDENCE"),
            TotalFindings = findings.Count,
            InfoFindings = findings.Count(x => x.Severity == "Info"),
            WarningFindings = findings.Count(x => x.Severity == "Warning"),
            ErrorFindings = findings.Count(x => x.Severity == "Error")
        };
    }
}
