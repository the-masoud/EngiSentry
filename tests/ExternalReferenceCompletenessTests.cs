using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Rules;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Focused tests for the unresolved external reference completeness gate. The gate consumes only
    /// the scanner's existing XrefSnapshot data and must never change behavior for drawings with no
    /// references or only resolved references.
    /// </summary>
    internal static class ExternalReferenceCompletenessTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static XrefSnapshot Xref(string name, bool resolved) => new XrefSnapshot
        {
            Name = name,
            RawPath = name + ".dwg",
            ResolvedPath = name + ".dwg",
            Status = resolved ? "Resolved" : "Unresolved",
            IsResolved = resolved
        };

        // A report shaped like a real QaEngine output: one passing rule, no findings, status PASS.
        private static QaReport PassingReport() => new QaReport
        {
            OverallStatus = "PASS",
            RuleResults = new List<QaRuleResult>
            {
                new QaRuleResult { RuleId = "ENGI-EQ-003", Severity = "Info", EvaluatedSubjectCount = 1, FindingCount = 0, Status = "PASS" }
            },
            Findings = new List<QaFinding>(),
            Statistics = new QaStatistics { RuleCount = 1, RulesPassed = 1, TotalFindings = 0 }
        };

        internal static void Run()
        {
            NoReferencesUnchanged();
            ResolvedReferenceUnchanged();
            UnresolvedReferenceForcesInsufficientEvidence();
            ExistingFindingsPreserved();
            RealPassReportIsPrevented();
            Console.WriteLine("Unresolved external reference completeness gate assertions passed: 5 scenarios.");
        }

        // 1. No xrefs => unchanged.
        private static void NoReferencesUnchanged()
        {
            QaReport report = PassingReport();
            QaReport result = ExternalReferenceCompletenessGate.Apply(report, new List<XrefSnapshot>());
            Assert(ReferenceEquals(result, report), "No-xref gate returned a different report instance.");
            Assert(result.OverallStatus == "PASS", "No-xref gate changed the overall status.");
            Assert(result.Findings.Count == 0 && result.RuleResults.Count == 1 && result.Statistics.RuleCount == 1 && result.Statistics.TotalFindings == 0, "No-xref gate changed the report contents.");

            QaReport nullResult = ExternalReferenceCompletenessGate.Apply(report, null);
            Assert(ReferenceEquals(nullResult, report) && nullResult.OverallStatus == "PASS" && nullResult.Findings.Count == 0, "Null-xref gate changed the report.");
            Assert(!ExternalReferenceCompletenessGate.HasUnresolvedReferences(new List<XrefSnapshot>()), "Empty xref list reported unresolved references.");
        }

        // 2. Resolved xref => unchanged.
        private static void ResolvedReferenceUnchanged()
        {
            QaReport report = PassingReport();
            QaReport result = ExternalReferenceCompletenessGate.Apply(report, new[] { Xref("A", true), Xref("B", true) });
            Assert(ReferenceEquals(result, report), "Resolved-xref gate returned a different report instance.");
            Assert(result.OverallStatus == "PASS" && result.Findings.Count == 0 && result.RuleResults.Count == 1, "Resolved-xref gate changed the report.");
            Assert(!ExternalReferenceCompletenessGate.HasUnresolvedReferences(new[] { Xref("A", true) }), "Resolved xref reported unresolved references.");
        }

        // 3. Unresolved xref => INSUFFICIENT_EVIDENCE + one completeness finding.
        private static void UnresolvedReferenceForcesInsufficientEvidence()
        {
            QaReport report = PassingReport();
            QaReport result = ExternalReferenceCompletenessGate.Apply(report, new[] { Xref("A", true), Xref("B", false), Xref("C", false) });
            Assert(ReferenceEquals(result, report), "Unresolved-xref gate returned a different report instance.");
            Assert(result.OverallStatus == "INSUFFICIENT_EVIDENCE", "Unresolved xref did not force INSUFFICIENT_EVIDENCE.");
            Assert(result.Findings.Count == 1, "Unresolved xref did not add exactly one finding.");
            QaFinding finding = result.Findings.Single();
            Assert(finding.RuleId == "ENGI-REF-001" && finding.FindingId == "ENGI-REF-001" && finding.Category == "ExternalReferenceCompleteness" && finding.Severity == "Error", "Completeness finding identity/severity is wrong.");
            Assert(finding.Evidence.Reason == "unresolved-external-reference" && finding.Evidence.SourceType == "ExternalReference" && finding.Evidence.OccurrenceCount == 2, "Completeness finding evidence did not carry the unresolved reference count.");
            Assert(finding.SourceHandles.SequenceEqual(new[] { "B", "C" }), "Completeness finding source handles were not the unresolved references in deterministic order.");
            QaRuleResult gateResult = result.RuleResults.Single(x => x.RuleId == "ENGI-REF-001");
            Assert(result.RuleResults.Count(x => x.RuleId == "ENGI-REF-001") == 1 && gateResult.Status == "INSUFFICIENT_EVIDENCE" && gateResult.EvaluatedSubjectCount == 2, "Completeness rule result is wrong.");
            Assert(result.Statistics.TotalFindings == 1 && result.Statistics.ErrorFindings == 1 && result.Statistics.RulesInsufficientEvidence == 1 && result.Statistics.RuleCount == 2, "Statistics were not recomputed after the gate.");
        }

        // 4. Existing findings preserved.
        private static void ExistingFindingsPreserved()
        {
            QaReport report = PassingReport();
            report.OverallStatus = "REVIEW_REQUIRED";
            report.Findings.Add(new QaFinding { FindingId = "ENGI-EQ-001:E-1", RuleId = "ENGI-EQ-001", Severity = "Warning", Category = "EquipmentConsistency", SubjectType = "Equipment", SubjectId = "E-1", Message = "existing warning" });
            report.RuleResults.Single().FindingCount = 1;
            report.RuleResults.Single().Status = "FINDINGS";
            report.Statistics = new QaStatistics { RuleCount = 1, RulesWithFindings = 1, TotalFindings = 1, WarningFindings = 1 };

            QaReport result = ExternalReferenceCompletenessGate.Apply(report, new[] { Xref("B", false) });
            Assert(result.OverallStatus == "INSUFFICIENT_EVIDENCE", "Unresolved xref did not override an existing REVIEW_REQUIRED status.");
            Assert(result.Findings.Count == 2, "Existing findings were not preserved alongside the completeness finding.");
            Assert(result.Findings.Any(x => x.FindingId == "ENGI-EQ-001:E-1") && result.Findings.Any(x => x.FindingId == "ENGI-REF-001"), "Preserved or completeness finding identity was lost.");
            Assert(result.Statistics.TotalFindings == 2 && result.Statistics.WarningFindings == 1 && result.Statistics.ErrorFindings == 1, "Preserved-finding statistics were not recomputed.");

            // An existing Error finding (which would normally yield FAIL) must still resolve to INSUFFICIENT_EVIDENCE.
            QaReport failing = PassingReport();
            failing.OverallStatus = "FAIL";
            failing.Findings.Add(new QaFinding { FindingId = "ENGI-IDX-001:X", RuleId = "ENGI-IDX-001", Severity = "Error", Category = "EntityIndexIntegrity", SubjectType = "EntityLink", SubjectId = "X", Message = "existing error" });
            QaReport failingResult = ExternalReferenceCompletenessGate.Apply(failing, new[] { Xref("B", false) });
            Assert(failingResult.OverallStatus == "INSUFFICIENT_EVIDENCE", "An existing Error finding overrode the gate INSUFFICIENT_EVIDENCE result.");
            Assert(failingResult.Findings.Count == 2 && failingResult.Findings.Any(x => x.FindingId == "ENGI-IDX-001:X"), "Existing Error finding was not preserved.");
        }

        // Integration: a real QaEngine report that is PASS must not survive an unresolved reference.
        private static void RealPassReportIsPrevented()
        {
            var index = new EngineeringEntityIndex();
            index.EquipmentEntities.Add(new EquipmentEntity
            {
                CanonicalId = "equipment:P-01A",
                NormalizedTag = "P-01A",
                HasDrawingEvidence = true,
                HasEquipmentListEvidence = false,
                Occurrences = new List<EquipmentOccurrence> { new EquipmentOccurrence { SourceHandle = "H1", Role = "DrawingTag", NormalizedTag = "P-01A" } }
            });

            QaReport pass = QaEngine.Evaluate(index, BuiltInQaRules.All);
            Assert(pass.OverallStatus == "PASS" && pass.Findings.Count == 0, "Test fixture did not produce the expected PASS report.");

            QaReport noRefs = ExternalReferenceCompletenessGate.Apply(QaEngine.Evaluate(index, BuiltInQaRules.All), new List<XrefSnapshot>());
            Assert(noRefs.OverallStatus == "PASS" && noRefs.Findings.Count == 0, "Host-only drawing behavior changed under the gate.");

            QaReport gated = ExternalReferenceCompletenessGate.Apply(pass, new[] { Xref("GOVERNING", false) });
            Assert(gated.OverallStatus == "INSUFFICIENT_EVIDENCE", "A PASS report survived an unresolved reference.");
            Assert(gated.Findings.Count == 1 && gated.Findings.Single().RuleId == "ENGI-REF-001", "Gated PASS report did not carry exactly the completeness finding.");
        }
    }
}
