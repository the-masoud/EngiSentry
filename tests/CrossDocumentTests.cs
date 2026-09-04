using System;
using System.Linq;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.CrossDocument;
using EngiSentry.Rules;

namespace EngiSentry.Tests
{
    internal static class CrossDocumentTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void Run()
        {
            const string csv = "Tag,Description,Installation\nP-200-101A/B,\"Pump, main\",TOG 6.400\nE-200-104,,\n";
            ExternalEquipmentRegister register = ExternalEquipmentRegisterParser.ParseBytes("test.csv", "test.csv", Encoding.UTF8.GetBytes(csv));
            Assert(register.Statistics.SourceRowCount == 2 && register.Statistics.ExpandedEquipmentCount == 3, "CSV expansion statistics failed.");
            Assert(register.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "E-200-104", "P-200-101A", "P-200-101B" }), "CSV deterministic ordering failed.");
            Assert(register.Records.Single(x => x.NormalizedTag == "P-200-101A").Description == "Pump, main", "Quoted CSV field failed.");
            Assert(register.Records.Single(x => x.NormalizedTag == "P-200-101A").ExpansionMethod == "GroupedSuffixExpansion", "Grouped expansion method failed.");
            bool duplicateRejected = false;
            try { ExternalEquipmentRegisterParser.ParseBytes("d.csv", "d.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-1-1A/B,,\nP-1-1A,,\n")); } catch (ExternalRegisterValidationException) { duplicateRejected = true; }
            Assert(duplicateRejected, "Duplicate expanded identity was accepted.");
            bool invalidUtf8Rejected = false;
            try { ExternalEquipmentRegisterParser.ParseBytes("bad.csv", "bad.csv", new byte[] { 0xFF }); } catch (ExternalRegisterValidationException) { invalidUtf8Rejected = true; }
            Assert(invalidUtf8Rejected, "Invalid UTF-8 was accepted.");
            var index = new EngineeringEntityIndex { EquipmentEntities = { new EquipmentEntity { CanonicalId = "equipment:P-200-101A", NormalizedTag = "P-200-101A", HasDrawingEvidence = true, EquipmentListLinks = { new EquipmentListLink { Description = "Pump", SourceHandles = { "ABC" } } } } } };
            ExternalEquipmentRegister mismatch = ExternalEquipmentRegisterParser.ParseBytes("m.csv", "m.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-200-101A,Other,\n"));
            QaRuleEvaluation evaluation = BuiltInCrossDocumentQaRules.All.Single(x => x.RuleId == "XDOC-EQ-003").Evaluate(index, mismatch);
            Assert(evaluation.Result.FindingCount == 1 && evaluation.Findings[0].Evidence.CrossDocumentEvidence!.MismatchFields.Single() == "Description", "Cross-document mismatch rule failed.");
        }
    }
}
