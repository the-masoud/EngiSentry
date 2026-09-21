using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EngiSentry.Intelligence
{
    public sealed class JevCalibrationBenchmarkExecutionResult
    {
        public string Status { get; set; } = string.Empty;
        public JevCalibrationBenchmarkReport? Report { get; set; }
        public bool TransportCalled { get; set; }
    }

    public sealed class JevCalibrationBenchmarkExecutionService
    {
        public JevCalibrationBenchmarkExecutionResult Execute(
            string packageDirectory,
            JevCalibrationPackage package,
            IReadOnlyList<JevEngineerLabel> labels,
            IReadOnlyList<SemanticAmbiguityCase> cases,
            JevSemanticAssistPolicy policy,
            Func<IJevSemanticDecisionClient> clientFactory)
        {
            string casesHash = JevCalibrationBenchmarkStore.HashFile(Path.Combine(packageDirectory, "calibration-cases.json"));
            string labelsHash = JevCalibrationBenchmarkStore.HashFile(Path.Combine(packageDirectory, "engineer-labels.json"));
            try
            {
                JevCalibrationReportCheck existing = JevCalibrationBenchmarkStore.Check(packageDirectory, package.CalibrationPackageId, casesHash, labelsHash);
                if (existing.Report != null) return new JevCalibrationBenchmarkExecutionResult { Status = "EXISTING_REPORT_MATCH", Report = existing.Report };
            }
            catch (InvalidOperationException exception) when (exception.Message == JevCalibrationStates.BenchmarkInputChanged)
            {
                return new JevCalibrationBenchmarkExecutionResult { Status = JevCalibrationStates.BenchmarkInputChanged };
            }

            if (!policy.Enabled) return new JevCalibrationBenchmarkExecutionResult { Status = JevSemanticAdvisoryStates.Disabled };
            if (!policy.HasApiKey || string.IsNullOrWhiteSpace(policy.ApiKey)) return new JevCalibrationBenchmarkExecutionResult { Status = JevSemanticAdvisoryStates.NotConfigured };

            Dictionary<string, SemanticAmbiguityCase> current = cases.GroupBy(x => x.EvidenceId, StringComparer.Ordinal).Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
            List<SemanticAmbiguityCase> labeledCases = new List<SemanticAmbiguityCase>();
            foreach (JevEngineerLabel label in labels.OrderBy(x => x.EvidenceId, StringComparer.Ordinal))
            {
                if (!current.TryGetValue(label.EvidenceId, out SemanticAmbiguityCase? item)) throw new InvalidOperationException(JevCalibrationStates.PackageContentMismatch);
                labeledCases.Add(item);
            }

            IJevSemanticDecisionClient client = clientFactory();
            JevSemanticAdvisorySidecar advisory = new JevSemanticAmbiguityClassifier(client).Classify(labeledCases, policy);
            JevCalibrationBenchmarkReport report = JevCalibrationBenchmarkRunner.Create(package, labels, advisory, casesHash, labelsHash);
            report = JevCalibrationBenchmarkStore.ReadOrWrite(packageDirectory, report);
            return new JevCalibrationBenchmarkExecutionResult { Status = JevCalibrationStates.BenchmarkComplete, Report = report, TransportCalled = true };
        }
    }
}
