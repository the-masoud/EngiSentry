using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EngiSentry.Contracts;
using EngiSentry.Intelligence;

namespace EngiSentry.Tests
{
    internal static class PhaseJevAdvisoryTests
    {
        internal static void Run()
        {
            PlotPlanModel plan = BuildPlan();
            string modelBefore = Json(plan);
            List<SemanticAmbiguityCase> cases = JevSemanticAmbiguityCaseBuilder.Build(plan);
            Assert(cases.Count == 2 && cases.All(x => x.CurrentDeterministicState == "Ambiguous"), "Resolved semantic evidence was sent.");
            Assert(cases.Any(x => x.ExistingCandidateKinds.Contains(JevSemanticLabels.AccessRoadEdge)), "Ambiguous Road evidence is absent.");
            Assert(cases.Any(x => x.ExistingCandidateKinds.Contains(JevSemanticLabels.SecurityFence)) && cases.Any(x => x.ExistingCandidateKinds.Contains(JevSemanticLabels.UnitBoundary)), "Ambiguous Boundary evidence is incomplete.");

            JevSystemOneRequest request = JevSemanticAssistPayloadBuilder.Build(cases);
            string requestJson = Json(request);
            foreach (string forbidden in new[] { "ProjectDirectory", "drawing path", "drawing filename", "standards clause", "approval", "policy", "trust", "test-only-not-a-secret", "Authorization" })
                Assert(requestJson.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) < 0, "Sensitive data entered outbound request.");
            Assert(request.Questions.Values.All(x => x.Type == "choice" && x.Criteria.Keys.OrderBy(v => v, StringComparer.Ordinal).SequenceEqual(JevSemanticLabels.All.OrderBy(v => v, StringComparer.Ordinal))), "Choice labels changed.");

            CountingClient validClient = new CountingClient(Answer());
            JevSemanticAmbiguityClassifier classifier = new JevSemanticAmbiguityClassifier(validClient);
            JevSemanticAdvisorySidecar valid = classifier.Classify(cases, JevSemanticAssistPolicy.ForTests(true, true));
            Assert(validClient.CallCount == 1 && valid.Results.All(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.Classified && x.Choice == JevSemanticLabels.Unknown && x.ChoiceProbabilities.Count == 5 && x.Confidence == 0.5d && x.ErrorCategory == null), "Valid Choice response was not preserved.");
            Assert(Json(plan) == modelBefore && valid.AdvisoryOnly && !valid.DeterministicModelModified && !valid.GovernanceModified && !valid.RuleExecutionModified && !valid.PlacementProposalModified && !valid.DwgModified, "Advisory classification mutated deterministic state.");
            Assert(Json(valid).IndexOf("test-only-not-a-secret", StringComparison.Ordinal) < 0 && Json(valid).IndexOf("Authorization", StringComparison.OrdinalIgnoreCase) < 0, "Secret entered advisory sidecar.");
            Assert(classifier.Classify(cases.AsEnumerable().Reverse().ToList(), JevSemanticAssistPolicy.ForTests(true, true)).Results.Select(x => x.EvidenceId).SequenceEqual(valid.Results.Select(x => x.EvidenceId)), "Input order changed advisory ordering.");

            Assert(classifier.Classify(cases, JevSemanticAssistPolicy.ForTests(false, true)).Results.All(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.Disabled), "Disabled opt-in is not safe.");
            Assert(classifier.Classify(cases, JevSemanticAssistPolicy.ForTests(true, false)).Results.All(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.NotConfigured), "Missing key is not safe.");
            int calls = validClient.CallCount;
            Assert(classifier.Classify(new List<SemanticAmbiguityCase>(), JevSemanticAssistPolicy.ForTests(true, true)).Results.Count == 0 && validClient.CallCount == calls, "No ambiguities invoked transport.");

            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = "BAD", Probabilities = Distribution(JevSemanticLabels.Unknown), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = new Dictionary<string, double>(), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown, double.NaN), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown, double.PositiveInfinity), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown, -0.1d), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown, 1.1d), Confidence = .5d });
            AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown, .8d), Confidence = .5d });
            foreach (double confidence in new[] { double.NaN, double.PositiveInfinity, -0.1d, 1.1d }) AssertInvalid(cases, new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown), Confidence = confidence });

            foreach (string category in new[] { "timeout", "network", "http-401", "http-429", "http-529", "http-5xx" }) AssertUnavailable(cases, category);
            AssertHttpCategory(request, new ThrowingHandler(), "timeout");
            AssertHttpCategory(request, new StatusHandler(HttpStatusCode.Unauthorized), "http-401");
            AssertHttpCategory(request, new StatusHandler((HttpStatusCode)429), "http-429");
            AssertHttpCategory(request, new StatusHandler((HttpStatusCode)529), "http-529");
            AssertHttpCategory(request, new StatusHandler(HttpStatusCode.InternalServerError), "http-5xx");
            AssertCoreConsoleNetworkBlock();
            AssertTransportShape(cases);
            Assert(typeof(JevSemanticAssistResult).GetProperty("AutoExecutionAllowed") == null && typeof(JevSemanticAssistResult).GetProperty("PlacementProposal") == null, "Advisory result exposes deterministic activation surfaces.");
        }

        private static PlotPlanModel BuildPlan()
        {
            PlotPlanModel plan = new PlotPlanModel();
            plan.Geometry.Associations.Add(new GeometryAssociation { AssociationId = "road-ambiguous", SubjectKind = "Road", Status = "Ambiguous", LabelRawText = "ACCESS ROAD", Candidates = new List<GeometryCandidate> { new GeometryCandidate { GeometryKind = "Polyline", SourceLayer = "ROAD-EDGE" } } });
            plan.Geometry.Associations.Add(new GeometryAssociation { AssociationId = "road-resolved", SubjectKind = "Road", Status = "Resolved" });
            plan.Geometry.SpatialConstraints.Add(new SpatialCoordinateConstraint { ConstraintId = "boundary-ambiguous", SubjectKind = "Boundary", Status = "Ambiguous", LabelRawText = "FENCE", Candidates = new List<SpatialGeometryCandidate> { new SpatialGeometryCandidate { GeometryKind = "Line", SourceLayer = "FENCE" } } });
            return plan;
        }

        private static Dictionary<string, double> Distribution(string choice, double value = 1d) => JevSemanticLabels.All.ToDictionary(label => label, label => label == choice ? value : 0d, StringComparer.Ordinal);
        private static JevChoiceAnswer Answer() => new JevChoiceAnswer { Type = "choice", Choice = JevSemanticLabels.Unknown, Probabilities = Distribution(JevSemanticLabels.Unknown), Confidence = .5d };
        private static void AssertInvalid(List<SemanticAmbiguityCase> cases, JevChoiceAnswer answer) => Assert(new JevSemanticAmbiguityClassifier(new CountingClient(answer)).Classify(cases, JevSemanticAssistPolicy.ForTests(true, true)).Results.All(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.InvalidResponse && x.ErrorCategory == "invalid-response"), "Invalid response was accepted.");
        private static void AssertUnavailable(List<SemanticAmbiguityCase> cases, string category) => Assert(new JevSemanticAmbiguityClassifier(new FailureClient(category)).Classify(cases, JevSemanticAssistPolicy.ForTests(true, true)).Results.All(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.Unavailable && x.ErrorCategory == category), "Transport category was lost.");

        private static void AssertHttpCategory(JevSystemOneRequest request, HttpMessageHandler handler, string expected)
        {
            try { new TypeSafeJevClient(new HttpClient(handler)).DecideAsync(request, "test-only-not-a-secret").GetAwaiter().GetResult(); throw new InvalidOperationException("Expected transport failure."); }
            catch (JevTransportException exception) { Assert(exception.Category == expected && exception.Message.IndexOf("test-only-not-a-secret", StringComparison.Ordinal) < 0, "HTTP category is unsafe or incorrect."); }
        }

        private static void AssertCoreConsoleNetworkBlock()
        {
            Type? boundary = typeof(EngiSentry.AutoCAD2021.Commands).Assembly.GetType("EngiSentry.AutoCAD2021.AutoCADHostBoundary");
            MethodInfo? method = boundary?.GetMethod("IsJevNetworkAllowed", BindingFlags.Static | BindingFlags.NonPublic);
            Assert(method != null && !(bool)method.Invoke(null, new object[] { true })! && (bool)method.Invoke(null, new object[] { false })!, "Core Console network boundary is incorrect.");
        }

        private static void AssertTransportShape(List<SemanticAmbiguityCase> cases)
        {
            SemanticAmbiguityCase item = cases[0];
            JevSystemOneRequest request = JevSemanticAssistPayloadBuilder.Build(new List<SemanticAmbiguityCase> { item });
            ShapeHandler handler = new ShapeHandler(item.EvidenceId);
            JevSystemOneResponse response = new TypeSafeJevClient(new HttpClient(handler)).DecideAsync(request, "test-only-not-a-secret").GetAwaiter().GetResult();
            Assert(handler.Body.Contains("\"state\":{") && handler.Body.Contains("\"questions\":{") && handler.Body.Contains("\"criteria\":{"), "TypeSafe maps were not serialized as JSON objects.");
            Assert(response.Model == "fake-jev" && response.Answers.ContainsKey(item.EvidenceId), "TypeSafe response map was not parsed as a JSON object.");
            Assert(handler.Body.IndexOf("test-only-not-a-secret", StringComparison.Ordinal) < 0, "API key entered TypeSafe request body.");
        }

        private static string Json(object value)
        {
            using (MemoryStream stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); }
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private sealed class CountingClient : IJevSemanticDecisionClient
        {
            internal int CallCount;
            private readonly JevChoiceAnswer answer;
            internal CountingClient(JevChoiceAnswer answer) { this.answer = answer; }
            public Task<JevSystemOneResponse> DecideAsync(JevSystemOneRequest request, string apiKey) { CallCount++; return Task.FromResult(new JevSystemOneResponse { Model = "fake-jev", RequestId = "synthetic", Answers = request.Questions.Keys.ToDictionary(key => key, key => answer) }); }
        }

        private sealed class FailureClient : IJevSemanticDecisionClient
        {
            private readonly string category;
            internal FailureClient(string category) { this.category = category; }
            public Task<JevSystemOneResponse> DecideAsync(JevSystemOneRequest request, string apiKey) => throw new JevTransportException(category);
        }

        private sealed class StatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode status;
            internal StatusHandler(HttpStatusCode status) { this.status = status; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(status));
        }

        private sealed class ShapeHandler : HttpMessageHandler
        {
            private readonly string evidenceId;
            internal string Body { get; private set; } = string.Empty;
            internal ShapeHandler(string evidenceId) { this.evidenceId = evidenceId; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string responseJson = "{\"model\":\"fake-jev\",\"answers\":{\"" + evidenceId + "\":{\"type\":\"choice\",\"choice\":\"UNKNOWN\",\"probabilities\":{\"ACCESS_ROAD_EDGE\":0,\"SECURITY_FENCE\":0,\"UNIT_BOUNDARY\":0,\"UNRELATED\":0,\"UNKNOWN\":1},\"confidence\":1}}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson, Encoding.UTF8, "application/json") });
            }
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new TaskCanceledException();
        }
    }
}
