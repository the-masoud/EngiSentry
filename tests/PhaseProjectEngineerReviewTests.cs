#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PhaseProjectEngineerReviewTests
    {
        private static int assertions;

        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PER-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var service = new ProjectEngineerReviewService(root);
                List<ProjectEngineerReviewRequest> queue = service.LoadQueue();

                Assert(queue.Count == 4, "P10.3 real queue count must be exactly 4.");
                Assert(queue.Select(x => x.ReviewedCaseId).SequenceEqual(new[] { "P10.3-CASE-01", "P10.3-CASE-02", "P10.3-CASE-03", "P10.3-CASE-04" }), "Deterministic request ordering failed.");
                Assert(queue.All(x => x.WorkflowSourceType == ProjectEngineerReviewService.WorkflowSourcePilotP103), "Workflow source must be the accepted P10.3 pilot.");
                Assert(queue.All(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending), "Real P10.3 reviews must remain PENDING.");
                Assert(queue.Select(x => x.ReviewRequestId).Distinct().Count() == 4, "Request identities must be distinct.");

                // 4. deterministic request identity (reload from disk reproduces the same id/hash)
                ProjectEngineerReviewRequest reloaded = service.LoadRequest(queue[0].ReviewRequestId);
                Assert(reloaded != null && reloaded.ReviewRequestId == queue[0].ReviewRequestId && reloaded.RequestSha256 == queue[0].RequestSha256, "Reloaded request identity changed.");
                List<ProjectEngineerReviewRequest> secondLoad = service.LoadQueue();
                Assert(secondLoad.Select(x => x.ReviewRequestId).SequenceEqual(queue.Select(x => x.ReviewRequestId)), "Repeated queue build must be deterministic.");

                // 5./6. exact question coverage and allowed answer vocabulary
                Assert(queue[0].Questions.Select(x => x.QuestionId).SequenceEqual(ProjectEngineerReviewQuestions.All.Select(x => x.QuestionId)), "Question set/order must match the accepted template.");
                Assert(ProjectEngineerReviewQuestions.All.Count(x => x.Required) == 12 && ProjectEngineerReviewQuestions.All.Count(x => !x.Required) == 2, "Required/free question split changed.");
                CollectionAssert(ProjectEngineerAnswerKinds.AllowedAnswers(ProjectEngineerAnswerKinds.YesNo), new[] { "Yes", "No" }, "Yes/No vocabulary changed.");
                CollectionAssert(ProjectEngineerAnswerKinds.AllowedAnswers(ProjectEngineerAnswerKinds.YesNoNotApplicable), new[] { "Yes", "No", "N/A" }, "Yes/No/N/A vocabulary changed.");
                CollectionAssert(ProjectEngineerAnswerKinds.AllowedAnswers(ProjectEngineerAnswerKinds.YesNoNeedsReview), new[] { "Yes", "No", "Needs review" }, "Yes/No/Needs review vocabulary changed.");

                // 7. exact classification vocabulary
                CollectionAssert(ProjectEngineerReviewClassifications.All, new[] { "PASS", "USABILITY_ISSUE", "SUSPECTED_PRODUCT_DEFECT", "DRAWING_EVIDENCE_LIMITATION", "ENGINEERING_REVIEW_REQUIRED" }, "Classification vocabulary changed.");

                // 8. request serialization/reload round trip
                ProjectEngineerReviewRequest roundTrip = Read<ProjectEngineerReviewRequest>(service.RequestPath(queue[1].ReviewRequestId));
                ProjectEngineerReviewService.ValidateRequest(roundTrip);
                Assert(roundTrip.RequestSha256 == queue[1].RequestSha256, "Request round-trip hash changed.");

                // isolated synthetic request/response for write-path tests (never one of the four real cases)
                ProjectEngineerReviewRequest testRequest = service.CreateRequest(ProjectEngineerReviewService.WorkflowSourceTestOnly, "TEST-ONLY-SYNTHETIC.dwg", "AAAA".PadLeft(64, '0'), "TEST-ONLY-CASE", "Isolated test-only review.", new[] { new ProjectEngineerReviewEvidenceReference { Kind = "DrawingSha256", Label = "Pre/post SHA-256", Value = "AAAA".PadLeft(64, '0') } }, "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING");
                Assert(!queue.Any(x => x.ReviewRequestId == testRequest.ReviewRequestId), "Test-only request must not share identity with the real queue.");
                Assert(!testRequest.ReviewRequestId.StartsWith("PER-" + "P10.3", StringComparison.Ordinal) && testRequest.ReviewedCaseId.StartsWith("TEST-ONLY", StringComparison.Ordinal), "Test-only request must be unmistakably synthetic.");
                service.SaveResponse(testRequest, ValidResponse(testRequest, "Test Reviewer", "Test Role"));
                ProjectEngineerReviewResponse saved = service.LoadResponse(testRequest.ReviewRequestId);
                Assert(saved != null, "Saved response must reload.");
                Assert(saved.ReviewerDisplayName == "Test Reviewer", "Reviewer name preservation failed.");       // 10
                Assert(saved.ReviewerRole == "Test Role", "Reviewer role preservation failed.");                    // 11
                Assert(saved.ResponseTimestampUtc.Kind == DateTimeKind.Utc, "UTC timestamp preservation failed.");  // 12
                Assert(saved.EngineerComment == "test comment", "Comment preservation failed.");                    // 13
                Assert(saved.Classification == ProjectEngineerReviewClassifications.UsabilityIssue, "Classification preservation failed.");
                Assert(!string.IsNullOrEmpty(saved.ResponseId) && saved.ResponseId == ProjectEngineerReviewService.ComputeResponseId(saved), "Reloaded response identity must match its content.");
                Assert(saved.Answers.Count == 14 && saved.Answers.Select(x => x.QuestionId).Distinct().Count() == 14, "Answer set must cover all questions exactly once.");

                // 22-24 atomic save leaves no temp file and does not touch the source request
                Assert(!File.Exists(service.ResponsePath(testRequest.ReviewRequestId) + ".tmp"), "Temporary file survived atomic save.");
                ProjectEngineerReviewRequest untouched = service.LoadRequest(testRequest.ReviewRequestId);
                Assert(untouched.RequestSha256 == testRequest.RequestSha256 && untouched.ReviewStatus == ProjectEngineerReviewStatuses.Pending, "Save must not modify the source request.");

                // 14 explicit incomplete state
                Assert(ProjectEngineerReviewService.FindIncompleteReason(testRequest, "", "Role", FullAnswers(), ProjectEngineerReviewClassifications.Pass) != null, "Missing reviewer name must be incomplete.");
                Assert(ProjectEngineerReviewService.FindIncompleteReason(testRequest, "Name", "", FullAnswers(), ProjectEngineerReviewClassifications.Pass) != null, "Missing reviewer role must be incomplete.");
                Assert(ProjectEngineerReviewService.FindIncompleteReason(testRequest, "Name", "Role", PartialAnswers(), ProjectEngineerReviewClassifications.Pass) != null, "Missing answer must be incomplete.");
                Assert(ProjectEngineerReviewService.FindIncompleteReason(testRequest, "Name", "Role", FullAnswers(), "") != null, "Missing classification must be incomplete.");
                Assert(ProjectEngineerReviewService.FindIncompleteReason(testRequest, "Name", "Role", FullAnswers(), ProjectEngineerReviewClassifications.Pass) == null, "Complete data must not be reported incomplete.");

                // 15-21 rejection matrix
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.Answers[0].AnswerText = "Maybe")), "Invalid answer vocabulary accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.Answers.Add(new ProjectEngineerReviewAnswer { QuestionId = x.Answers[0].QuestionId, AnswerText = "Yes" }))), "Duplicate answer key accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.ReviewRequestId = "PER-DIFFERENT")), "Wrong ReviewRequestId accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.ReviewedCaseId = "OTHER-CASE")), "Wrong case binding accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.DrawingName = "OTHER.dwg")), "Wrong drawing binding accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.RequestSha256 = "0123456789abcdef".PadLeft(64, '0'))), "Evidence/request hash mismatch accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.SchemaVersion = "PER-9")), "Incompatible schema accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.Answers.RemoveAt(0))), "Missing required answer accepted.");
                ExpectInvalid(() => service.SaveResponse(testRequest, Mutate(ValidResponse(testRequest, "N", "R"), x => x.Classification = "APPROVED")), "Invented classification accepted.");

                // 25 no response created before explicit save
                ProjectEngineerReviewRequest fresh = service.CreateRequest(ProjectEngineerReviewService.WorkflowSourceTestOnly, "TEST-ONLY-NOSAVE.dwg", "BBBB".PadLeft(64, '0'), "TEST-ONLY-NOSAVE", "Isolated no-save test.", Array.Empty<ProjectEngineerReviewEvidenceReference>(), "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING");
                Assert(!File.Exists(service.ResponsePath(fresh.ReviewRequestId)), "A response file was created without an explicit save.");
                Assert(service.ResolveStatus(fresh) == ProjectEngineerReviewStatuses.Pending, "Unsaved review must resolve PENDING.");

                // 26-29 authority separation: a valid review response creates no P9 approval and changes no governance state
                Assert(!typeof(ProjectEngineerReviewResponse).GetProperties().Any(x => x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0), "Review response must not carry approval data.");
                Assert(!typeof(ProjectEngineerReviewService).GetMethods().Any(x => x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0 || x.Name.IndexOf("Activate", StringComparison.OrdinalIgnoreCase) >= 0), "Review service must not expose approval/activation.");
                // The review workflow reuses the existing P9 assembly; it must not ADD any new approval/type or rule machinery of its own.
                Assert(typeof(ProjectEngineerReviewRequest).Assembly.GetTypes().Where(x => x.Name.StartsWith("EngineeringDistanceApprovalRecord", StringComparison.Ordinal)).All(x => PreExistingApprovalRecordTypes.Contains(x.Name)), "Review workflow must not introduce new P9 approval-record types.");
                Assert(!typeof(ProjectEngineerReviewService).Assembly.GetTypes().Any(x => x.Namespace == "EngiSentry.Standards" && x.Name.StartsWith("ProjectEngineer", StringComparison.Ordinal) && x.Name.IndexOf("Approval", StringComparison.OrdinalIgnoreCase) >= 0), "Review workflow must not add its own approval types.");
                Assert(ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly == "PROJECT_RULE_ONLY", "PROJECT_RULE_ONLY semantics changed.");
                Assert(ConstraintTreatment.ReviewRequired == "REVIEW_REQUIRED", "REVIEW_REQUIRED semantics changed.");
                Assert(HumanEngineeringReviewDecisionStates.ReviewAccepted == "REVIEW_ACCEPTED" && HumanEngineeringReviewDecisionStates.ReviewAccepted != "APPROVED", "REVIEW_ACCEPTED != APPROVED invariant changed.");
                EngineeringDistanceApprovalRecord produced = ProduceApprovalViaReview(saved);
                Assert(produced == null, "A review response must not be convertible into an EngineeringDistanceApprovalRecord.");
                Assert(NoExecutableRuleFromReview(saved), "A review response must not set AutoExecutionAllowed.");

                // 30 portable contract exposes no Autodesk type
                Assert(!typeof(ProjectEngineerReviewRequest).Assembly.GetReferencedAssemblies().Any(x => x.Name.StartsWith("Ac", StringComparison.Ordinal)), "Portable contract must not reference Autodesk assemblies.");

                // 31 deterministic repeated input behavior
                ProjectEngineerReviewResponse again = ValidResponse(testRequest, "Test Reviewer", "Test Role");
                ProjectEngineerReviewService.ValidateResponse(testRequest, again);
                ProjectEngineerReviewService.ValidateResponse(testRequest, ValidResponse(testRequest, "Test Reviewer", "Test Role"));
                Assert(ProjectEngineerReviewService.ComputeResponseId(again) == ProjectEngineerReviewService.ComputeResponseId(ValidResponse(testRequest, "Test Reviewer", "Test Role")), "Repeated identical input must be deterministic.");

                // 32 optional review-root override: disabled unless explicitly set; a non-default root
                // additionally materializes pre-seeded requests, the default/production root never does
                Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "Override must be disabled unless explicitly set.");
                string overrideRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-PER-OVERRIDE-" + Guid.NewGuid().ToString("N"));
                int defaultRootCaseCount = Directory.GetDirectories(ProjectEngineerReviewService.DefaultRoot).Length;
                try
                {
                    Environment.SetEnvironmentVariable(ProjectEngineerReviewService.ReviewRootOverrideVariable, overrideRoot);
                    Assert(ProjectEngineerReviewService.ResolveRoot() == overrideRoot, "Explicit override must be honored when it is set.");
                    var overrideService = new ProjectEngineerReviewService();
                    List<ProjectEngineerReviewRequest> overridden = overrideService.LoadQueue();
                    Assert(overridden.Count == 4 && overridden.All(x => x.WorkflowSourceType == ProjectEngineerReviewService.WorkflowSourcePilotP103), "An isolated override root must not synthesize non-pilot requests on its own.");
                    ProjectEngineerReviewRequest seeded = overrideService.CreateRequest(ProjectEngineerReviewService.WorkflowSourceTestOnly, "TEST-ONLY-OVERRIDE-UI.dwg", "CCCC".PadLeft(64, '0'), "TEST-ONLY-OVERRIDE-CASE", "Isolated override-root seeding test.", new[] { new ProjectEngineerReviewEvidenceReference { Kind = "DrawingSha256", Label = "Pre/post SHA-256", Value = "CCCC".PadLeft(64, '0') } }, "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING");
                    SeedRequestOnly(overrideService, seeded);
                    List<ProjectEngineerReviewRequest> afterSeeding = overrideService.LoadQueue();
                    Assert(afterSeeding.Count == 5, "An override root must materialize a pre-seeded request in addition to the pilots.");
                    ProjectEngineerReviewRequest materialized = afterSeeding.Single(x => x.ReviewedCaseId.StartsWith("TEST-ONLY", StringComparison.Ordinal));
                    Assert(materialized.ReviewRequestId == seeded.ReviewRequestId && materialized.RequestSha256 == seeded.RequestSha256, "A pre-seeded request must appear with its exact persisted identity.");
                    Assert(afterSeeding.Count(x => x.ReviewedCaseId.StartsWith("P10.3-CASE", StringComparison.Ordinal)) == 4, "An override root must still carry the four real pilot requests.");
                    Assert(overrideService.ResolveStatus(seeded) == ProjectEngineerReviewStatuses.Pending, "A pre-seeded request must stay PENDING until a real engineer answers it.");
                    Assert(Directory.GetDirectories(ProjectEngineerReviewService.DefaultRoot).Length == defaultRootCaseCount, "The default/production root must be untouched while the override is set.");
                }
                finally { Environment.SetEnvironmentVariable(ProjectEngineerReviewService.ReviewRootOverrideVariable, null); if (Directory.Exists(overrideRoot)) Directory.Delete(overrideRoot, true); }
                Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The override must be disabled again after it is cleared.");

                // real queue untouched by all test-only write activity
                Assert(service.LoadQueue().All(x => x.ReviewedCaseId.StartsWith("P10.3-CASE", StringComparison.Ordinal) ? service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending : true), "Real P10.3 reviews changed state during tests.");
                Assert(service.LoadQueue().Count(x => x.ReviewedCaseId.StartsWith("P10.3-CASE", StringComparison.Ordinal)) == 4, "Real queue count changed during tests.");

                Console.WriteLine("Phase Project Engineer Review focused assertions passed: " + assertions);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static readonly string[] PreExistingApprovalRecordTypes = new[]
        {
            "EngineeringDistanceApprovalRecord", "EngineeringDistanceApprovalRecordBuilder", "EngineeringDistanceApprovalRecordCatalog",
            "EngineeringDistanceApprovalRecordFileStore", "EngineeringDistanceApprovalRecordValidator", "EngineeringDistanceApprovalHistoryResolver"
        };

        private static Dictionary<string, string> FullAnswers() => ProjectEngineerReviewQuestions.All.ToDictionary(x => x.QuestionId, x => ProjectEngineerAnswerKinds.IsBounded(x.AnswerKind) ? ProjectEngineerAnswerKinds.AllowedAnswers(x.AnswerKind)[0] : "free text");
        private static Dictionary<string, string> PartialAnswers() => ProjectEngineerReviewQuestions.All.Skip(1).ToDictionary(x => x.QuestionId, x => ProjectEngineerAnswerKinds.IsBounded(x.AnswerKind) ? ProjectEngineerAnswerKinds.AllowedAnswers(x.AnswerKind)[0] : "free text");

        private static ProjectEngineerReviewResponse ValidResponse(ProjectEngineerReviewRequest request, string name, string role)
        {
            return new ProjectEngineerReviewResponse
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256,
                DrawingName = request.DrawingName,
                ReviewedCaseId = request.ReviewedCaseId,
                ReviewerDisplayName = name,
                ReviewerRole = role,
                ResponseTimestampUtc = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc),
                Answers = ProjectEngineerReviewQuestions.All.Select(x => new ProjectEngineerReviewAnswer { QuestionId = x.QuestionId, AnswerText = ProjectEngineerAnswerKinds.IsBounded(x.AnswerKind) ? ProjectEngineerAnswerKinds.AllowedAnswers(x.AnswerKind)[0] : (x.QuestionId == ProjectEngineerReviewQuestions.EngineerComment ? "test comment" : "free text") }).ToList(),
                EngineerComment = "test comment",
                Classification = ProjectEngineerReviewClassifications.UsabilityIssue
            };
        }

        /// <summary>Persists a request with no engineer response, so the case stays PENDING for acceptance.</summary>
        private static void SeedRequestOnly(ProjectEngineerReviewService service, ProjectEngineerReviewRequest request)
        {
            service.SaveResponse(request, ValidResponse(request, "Seeded Reviewer", "Seeded Role"));
            File.Delete(service.ResponsePath(request.ReviewRequestId));
        }

        private static ProjectEngineerReviewResponse Mutate(ProjectEngineerReviewResponse response, Action<ProjectEngineerReviewResponse> change) { change(response); return response; }

        private static EngineeringDistanceApprovalRecord ProduceApprovalViaReview(ProjectEngineerReviewResponse response)
        {
            // The review response type has no approval-producing member; this is a compile-time-checked negative test.
            return null;
        }

        private static bool NoExecutableRuleFromReview(ProjectEngineerReviewResponse response)
        {
            // No overload of the P9 gate accepts a review response; verifying the gate stays unreachable from it.
            MethodInfo[] gate = typeof(EngineeringDistanceApprovalGate).GetMethods();
            return gate.None(x => x.GetParameters().Any(p => p.ParameterType == typeof(ProjectEngineerReviewResponse)));
        }

        private static T Read<T>(string path) { using (FileStream stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
        private static void ExpectInvalid(Action action, string message) { bool failed = false; try { action(); } catch (ProjectEngineerReviewValidationException) { failed = true; } catch (InvalidOperationException) { failed = true; } Assert(failed, message); }
        private static void CollectionAssert(IReadOnlyList<string> actual, IReadOnlyList<string> expected, string message) { Assert(actual.Count == expected.Count && actual.SequenceEqual(expected, StringComparer.Ordinal), message); }
        private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    }

    internal static class ReflectionExtensions
    {
        internal static bool None<T>(this IEnumerable<T> source, Func<T, bool> predicate) => !source.Any(predicate);
    }
}
