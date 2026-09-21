using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EngiSentry.Intelligence
{
    public sealed class JevSemanticAmbiguityClassifier
    {
        private readonly IJevSemanticDecisionClient client;

        public JevSemanticAmbiguityClassifier(IJevSemanticDecisionClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public JevSemanticAdvisorySidecar Classify(IReadOnlyList<SemanticAmbiguityCase> cases, JevSemanticAssistPolicy policy)
        {
            List<SemanticAmbiguityCase> ordered = (cases ?? new List<SemanticAmbiguityCase>()).OrderBy(x => x.EvidenceId, StringComparer.Ordinal).ToList();
            if (ordered.Count == 0) return Sidecar(ordered, JevSemanticAdvisoryStates.NoAmbiguities);
            if (!policy.Enabled) return Sidecar(ordered, JevSemanticAdvisoryStates.Disabled);
            if (!policy.HasApiKey || string.IsNullOrWhiteSpace(policy.ApiKey)) return Sidecar(ordered, JevSemanticAdvisoryStates.NotConfigured);

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                JevSystemOneResponse response = client.DecideAsync(JevSemanticAssistPayloadBuilder.Build(ordered), policy.ApiKey!).GetAwaiter().GetResult();
                stopwatch.Stop();
                return Parse(ordered, response, stopwatch.ElapsedMilliseconds);
            }
            catch (JevInvalidResponseException) { return Sidecar(ordered, JevSemanticAdvisoryStates.InvalidResponse, stopwatch.ElapsedMilliseconds, "invalid-response"); }
            catch (JevTransportException exception) { return Sidecar(ordered, JevSemanticAdvisoryStates.Unavailable, stopwatch.ElapsedMilliseconds, exception.Category); }
            catch (Exception) { return Sidecar(ordered, JevSemanticAdvisoryStates.Unavailable, stopwatch.ElapsedMilliseconds, "network"); }
        }

        private static JevSemanticAdvisorySidecar Parse(List<SemanticAmbiguityCase> cases, JevSystemOneResponse response, long elapsed)
        {
            if (string.IsNullOrWhiteSpace(response.Model) || response.Answers.Count != cases.Count)
                return Sidecar(cases, JevSemanticAdvisoryStates.InvalidResponse, elapsed, "invalid-response");

            JevSemanticAdvisorySidecar sidecar = new JevSemanticAdvisorySidecar();
            foreach (SemanticAmbiguityCase item in cases)
            {
                if (!response.Answers.TryGetValue(item.EvidenceId, out JevChoiceAnswer? answer) || !Valid(answer))
                    return Sidecar(cases, JevSemanticAdvisoryStates.InvalidResponse, elapsed, "invalid-response");
                sidecar.Results.Add(new JevSemanticAssistResult
                {
                    EvidenceId = item.EvidenceId,
                    AdvisoryStatus = JevSemanticAdvisoryStates.Classified,
                    Choice = answer.Choice,
                    ChoiceProbabilities = answer.Probabilities.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new JevChoiceProbability { Label = x.Key, Probability = x.Value }).ToList(),
                    Confidence = answer.Confidence,
                    Model = response.Model,
                    RequestId = response.RequestId,
                    LatencyMilliseconds = elapsed,
                    InputProjectionSha256 = JevSemanticAssistPayloadBuilder.ComputeSha256(item)
                });
            }
            return sidecar;
        }

        private static bool Valid(JevChoiceAnswer answer)
        {
            if (answer.Type != "choice" || !JevSemanticLabels.All.Contains(answer.Choice) || answer.Probabilities == null || answer.Probabilities.Count != JevSemanticLabels.All.Length || answer.Probabilities.Keys.Except(JevSemanticLabels.All).Any()) return false;
            if (answer.Probabilities.Values.Any(x => double.IsNaN(x) || double.IsInfinity(x) || x < 0d || x > 1d)) return false;
            if (Math.Abs(answer.Probabilities.Values.Sum() - 1d) > 0.000001d) return false;
            return !double.IsNaN(answer.Confidence) && !double.IsInfinity(answer.Confidence) && answer.Confidence >= 0d && answer.Confidence <= 1d && answer.Probabilities.ContainsKey(answer.Choice);
        }

        private static JevSemanticAdvisorySidecar Sidecar(IEnumerable<SemanticAmbiguityCase> cases, string status, long latency = 0, string? errorCategory = null)
        {
            return new JevSemanticAdvisorySidecar
            {
                Results = cases.Select(item => new JevSemanticAssistResult { EvidenceId = item.EvidenceId, AdvisoryStatus = status, LatencyMilliseconds = latency, ErrorCategory = errorCategory, InputProjectionSha256 = JevSemanticAssistPayloadBuilder.ComputeSha256(item) }).OrderBy(x => x.EvidenceId, StringComparer.Ordinal).ToList()
            };
        }
    }
}
