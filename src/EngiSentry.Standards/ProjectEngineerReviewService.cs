using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectEngineerReviewValidationException : Exception
    {
        public ProjectEngineerReviewValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Builds and persists governed project-engineer review requests and responses. This records
    /// human engineering observation evidence only. It never creates an approval record, an
    /// authority policy, a trusted-issuer entry, a private key, or an executable/activated rule;
    /// those remain the exclusive domain of the existing P8/P9 mechanisms.
    /// </summary>
    public sealed class ProjectEngineerReviewService
    {
        public const string WorkflowSourcePilotP103 = "P10.3-SUPERVISED-ENGINEER-PILOT";
        public const string WorkflowSourceTestOnly = "TEST_ONLY";

        public const string RequestFileName = "review-request.json";
        public const string ResponseFileName = "review-response.json";

        /// <summary>
        /// Optional review-root override, used only for runtime/test acceptance. It follows the
        /// existing <c>ENGISENTRY_*</c> environment-variable convention. When it is unset or blank
        /// the default root is used, so production behavior is unchanged: the override is disabled
        /// unless it is explicitly set. It changes which isolated folder requests/responses are
        /// read from and written to; it never changes queue or governance semantics on its own.
        /// </summary>
        public const string ReviewRootOverrideVariable = "ENGISENTRY_ENGINEERING_REVIEW_ROOT";

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "engineering-review");

        /// <summary>Returns the explicitly set review-root override, or null when it is disabled.</summary>
        public static string? ResolveRootOverride()
        {
            string? value = Environment.GetEnvironmentVariable(ReviewRootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>The default root, unless an explicit override is set.</summary>
        public static string ResolveRoot() => ResolveRootOverride() ?? DefaultRoot;

        private readonly string root;
        public ProjectEngineerReviewService() : this(ResolveRoot()) { }
        public ProjectEngineerReviewService(string rootOverride) { root = rootOverride ?? throw new ArgumentNullException(nameof(rootOverride)); }

        /// <summary>
        /// Deterministic identity for a review request: canonical SHA-256 over the request content
        /// with the identity fields blanked, mirroring the accepted decision-record identity pattern.
        /// </summary>
        public static string ComputeRequestSha256(ProjectEngineerReviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ProjectEngineerReviewRequest identity = CopyForIdentity(request);
            return HexSha256(Serialize(identity));
        }

        public static string ComputeResponseId(ProjectEngineerReviewResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            ProjectEngineerReviewResponse identity = CopyForIdentity(response);
            return HexSha256(Serialize(identity));
        }

        /// <summary>Builds a review request from explicit evidence. Nothing is inferred or synthesized.</summary>
        public ProjectEngineerReviewRequest CreateRequest(string workflowSourceType, string drawingName, string? drawingSha256, string reviewedCaseId, string reviewReason, IReadOnlyList<ProjectEngineerReviewEvidenceReference> evidence, string? softwareResult, string? softwareState)
        {
            if (string.IsNullOrWhiteSpace(workflowSourceType)) throw new ArgumentException("workflowSourceType is required", nameof(workflowSourceType));
            if (string.IsNullOrWhiteSpace(drawingName)) throw new ArgumentException("drawingName is required", nameof(drawingName));
            if (string.IsNullOrWhiteSpace(reviewedCaseId)) throw new ArgumentException("reviewedCaseId is required", nameof(reviewedCaseId));
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            var request = new ProjectEngineerReviewRequest
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                WorkflowSourceType = workflowSourceType,
                DrawingName = drawingName,
                DrawingSha256 = string.IsNullOrWhiteSpace(drawingSha256) ? null : drawingSha256,
                ReviewedCaseId = reviewedCaseId,
                ReviewStatus = ProjectEngineerReviewStatuses.Pending,
                ReviewReason = reviewReason ?? string.Empty,
                EvidenceReferences = evidence.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Label ?? string.Empty, StringComparer.Ordinal).ToList(),
                SoftwareResult = string.IsNullOrWhiteSpace(softwareResult) ? null : softwareResult,
                SoftwareState = string.IsNullOrWhiteSpace(softwareState) ? null : softwareState,
                Questions = ProjectEngineerReviewQuestions.All.ToList()
            };
            request.RequestSha256 = ComputeRequestSha256(request);
            request.ReviewRequestId = "PER-" + request.RequestSha256.Substring(0, 16);
            ValidateRequest(request);
            return request;
        }

        /// <summary>
        /// The four accepted P10.3 pilot cases as deterministic pending review requests, built only
        /// from evidence already recorded in the accepted pilot record. The operational pilots are
        /// not re-run and their evidence is not altered.
        /// </summary>
        public List<ProjectEngineerReviewRequest> BuildPilotRequests() => new List<ProjectEngineerReviewRequest>
        {
            CreateRequest(WorkflowSourcePilotP103, "A5-E-FINAL.dwg", "5F31F64DE4A6E183324E8B55088912D8FE238C8C9B25605170947FB9994E5256", "P10.3-CASE-01",
                "Supervised P10.3 pilot review of the A5 generalization fixture results surface.",
                new[]
                {
                    Evidence("DrawingSha256", "Pre/post SHA-256", "5F31F64DE4A6E183324E8B55088912D8FE238C8C9B25605170947FB9994E5256"),
                    Evidence("OperationalResult", "Operational execution", "PASS"),
                    Evidence("EngineeringReviewStatus", "Engineering review", "PENDING"),
                    Evidence("RecordedClassification", "Recorded classification", "USABILITY_ISSUE"),
                    Evidence("RecordedObservation", "Recorded defect", "Engineering Review status text clipping/truncation in the input/status area."),
                    Evidence("D1RuntimeRecheck", "D1 runtime recheck", "PASS / RESOLVED; latest accepted recheck artifact timestamp 20260912T072346104Z")
                },
                "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING"),
            CreateRequest(WorkflowSourcePilotP103, "PLOTPLAN-A0-E-FINAL-P92C2C-VERIFY.dwg", "3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4", "P10.3-CASE-02",
                "Supervised P10.3 pilot review of the protected Plot Plan A0 verification results surface.",
                new[]
                {
                    Evidence("DrawingSha256", "Pre/post SHA-256", "3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4"),
                    Evidence("OperationalResult", "Operational execution", "PASS"),
                    Evidence("EngineeringReviewStatus", "Engineering review", "PENDING"),
                    Evidence("ArtifactTimestamp", "Accepted artifact timestamp", "20260912T073851521Z")
                },
                "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING"),
            CreateRequest(WorkflowSourcePilotP103, "PLOTPLAN-A1-E-FINAL.dwg", "BC317D2492FA22728EF4BAF4086FDEFBF3ADFCF384989E89894ED1E1A5DEA24F", "P10.3-CASE-03",
                "Supervised P10.3 pilot review of the Plot Plan A1 revision fixture results surface.",
                new[]
                {
                    Evidence("DrawingSha256", "Pre/post SHA-256", "BC317D2492FA22728EF4BAF4086FDEFBF3ADFCF384989E89894ED1E1A5DEA24F"),
                    Evidence("OperationalResult", "Operational execution", "PASS"),
                    Evidence("EngineeringReviewStatus", "Engineering review", "PENDING"),
                    Evidence("ArtifactTimestamp", "Accepted artifact timestamp", "20260912T080049972Z")
                },
                "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING"),
            CreateRequest(WorkflowSourcePilotP103, "INS-PD-141-Attchments  1~4.runtime-test.dwg", "B1D1B99054C08AF00BAFF7FC6CDD0A8EDF3EFD35774F733A3942F2490142246B", "P10.3-CASE-04",
                "Supervised P10.3 pilot review of the INS-PD-141 runtime fixture results surface.",
                new[]
                {
                    Evidence("DrawingSha256", "Pre/post SHA-256", "B1D1B99054C08AF00BAFF7FC6CDD0A8EDF3EFD35774F733A3942F2490142246B"),
                    Evidence("OperationalResult", "Operational execution", "PASS"),
                    Evidence("EngineeringReviewStatus", "Engineering review", "PENDING"),
                    Evidence("ArtifactTimestamp", "Accepted artifact timestamp", "20260912T091527105Z")
                },
                "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING")
        };

        /// <summary>
        /// Materializes the review queue: persists any not-yet-persisted request definitions and
        /// attaches the current persisted response/status. Requests are never overwritten.
        /// </summary>
        public List<ProjectEngineerReviewRequest> LoadQueue()
        {
            List<ProjectEngineerReviewRequest> requests = BuildPilotRequests();
            foreach (ProjectEngineerReviewRequest request in requests) EnsureRequestPersisted(request);
            IncludeSeededRequests(requests);
            return requests.OrderBy(x => x.ReviewedCaseId, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Only a non-default (explicitly overridden) review root additionally materializes
        /// requests already persisted under it, for example a pre-seeded test-only case used for
        /// runtime acceptance. The production default root never sets the override and holds only
        /// the four P10.3 pilot requests, so the real queue is never extended and production queue
        /// semantics are unchanged. Only previously persisted, hash-valid requests are considered.
        /// </summary>
        private void IncludeSeededRequests(List<ProjectEngineerReviewRequest> requests)
        {
            if (string.Equals(root, DefaultRoot, StringComparison.Ordinal) || !Directory.Exists(root)) return;
            foreach (string directory in Directory.GetDirectories(root))
            {
                string path = Path.Combine(directory, RequestFileName);
                if (!File.Exists(path)) continue;
                ProjectEngineerReviewRequest persisted = Read<ProjectEngineerReviewRequest>(path);
                ValidateRequest(persisted);
                if (!requests.Any(x => x.ReviewRequestId == persisted.ReviewRequestId)) requests.Add(persisted);
            }
        }

        /// <summary>Reloads a persisted request exactly as recorded, or null when absent.</summary>
        public ProjectEngineerReviewRequest? LoadRequest(string reviewRequestId)
        {
            string path = RequestPath(reviewRequestId);
            if (!File.Exists(path)) return null;
            ProjectEngineerReviewRequest request = Read<ProjectEngineerReviewRequest>(path);
            ValidateRequest(request);
            return request;
        }

        /// <summary>Loads a persisted response and revalidates it against its request; null when none exists.</summary>
        public ProjectEngineerReviewResponse? LoadResponse(string reviewRequestId)
        {
            string path = ResponsePath(reviewRequestId);
            if (!File.Exists(path)) return null;
            ProjectEngineerReviewResponse response = Read<ProjectEngineerReviewResponse>(path);
            ProjectEngineerReviewRequest? request = LoadRequest(reviewRequestId);
            if (request == null) throw new ProjectEngineerReviewValidationException("review response exists without its request: " + reviewRequestId);
            ValidateResponse(request, response);
            return response;
        }

        /// <summary>
        /// Atomically persists a validated response. The request is never modified, an invalid
        /// existing response is never silently overwritten, and no file is written until this is
        /// explicitly called.
        /// </summary>
        public ProjectEngineerReviewResponse SaveResponse(ProjectEngineerReviewRequest request, ProjectEngineerReviewResponse response)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.ResponseTimestampUtc.Kind != DateTimeKind.Utc) response.ResponseTimestampUtc = response.ResponseTimestampUtc.ToUniversalTime();
            response.ResponseId = ComputeResponseId(response);
            ValidateResponse(request, response);

            EnsureRequestPersisted(request);
            string directory = RequestDirectory(request.ReviewRequestId);
            Directory.CreateDirectory(directory);
            string target = ResponsePath(request.ReviewRequestId);
            string temporary = target + ".tmp";
            if (File.Exists(temporary)) throw new InvalidOperationException("A previous review response temporary file exists.");
            try
            {
                Write(temporary, response);
                ProjectEngineerReviewResponse verified = Read<ProjectEngineerReviewResponse>(temporary);
                ValidateResponse(request, verified);
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }

            ProjectEngineerReviewRequest untouched = Read<ProjectEngineerReviewRequest>(RequestPath(request.ReviewRequestId));
            ValidateRequest(untouched);
            if (untouched.RequestSha256 != request.RequestSha256) throw new InvalidOperationException("review request was modified by response save.");
            return response;
        }

        /// <summary>Determines whether the request has a complete, binding-valid persisted response.</summary>
        public string ResolveStatus(ProjectEngineerReviewRequest request)
        {
            string path = ResponsePath(request.ReviewRequestId);
            if (!File.Exists(path)) return ProjectEngineerReviewStatuses.Pending;
            try { ValidateResponse(request, Read<ProjectEngineerReviewResponse>(path)); return ProjectEngineerReviewStatuses.Completed; }
            catch (ProjectEngineerReviewValidationException) { return ProjectEngineerReviewStatuses.Invalid; }
        }

        public string RequestDirectory(string reviewRequestId) => Path.Combine(root, reviewRequestId);
        public string RequestPath(string reviewRequestId) => Path.Combine(RequestDirectory(reviewRequestId), RequestFileName);
        public string ResponsePath(string reviewRequestId) => Path.Combine(RequestDirectory(reviewRequestId), ResponseFileName);

        public static void ValidateRequest(ProjectEngineerReviewRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.SchemaVersion != ProjectEngineerReviewSchema.Current) throw new ProjectEngineerReviewValidationException("unsupported review request SchemaVersion: " + request.SchemaVersion);
            if (string.IsNullOrWhiteSpace(request.ReviewRequestId)) throw new ProjectEngineerReviewValidationException("ReviewRequestId is required");
            if (string.IsNullOrWhiteSpace(request.WorkflowSourceType)) throw new ProjectEngineerReviewValidationException("WorkflowSourceType is required");
            if (string.IsNullOrWhiteSpace(request.DrawingName)) throw new ProjectEngineerReviewValidationException("DrawingName is required");
            if (string.IsNullOrWhiteSpace(request.ReviewedCaseId)) throw new ProjectEngineerReviewValidationException("ReviewedCaseId is required");
            if (!string.IsNullOrWhiteSpace(request.DrawingSha256) && !IsSha256(request.DrawingSha256)) throw new ProjectEngineerReviewValidationException("DrawingSha256 must be a 64-character hexadecimal SHA256");
            if (request.Questions == null || request.Questions.Count == 0) throw new ProjectEngineerReviewValidationException("review request Questions are required");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (question == null || string.IsNullOrWhiteSpace(question.QuestionId)) throw new ProjectEngineerReviewValidationException("review request contains a question without an id");
                if (!ids.Add(question.QuestionId)) throw new ProjectEngineerReviewValidationException("duplicate review request QuestionId: " + question.QuestionId);
                if (!ProjectEngineerAnswerKinds.All.Contains(question.AnswerKind)) throw new ProjectEngineerReviewValidationException("unsupported review question AnswerKind: " + question.AnswerKind);
            }
            if (!string.Equals(request.RequestSha256, ComputeRequestSha256(request), StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review request RequestSha256 does not match its content");
        }

        /// <summary>
        /// Rejects any response that does not bind to the request it claims to answer. A mismatched
        /// or incomplete response never becomes a valid review.
        /// </summary>
        public static void ValidateResponse(ProjectEngineerReviewRequest request, ProjectEngineerReviewResponse response)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (response.SchemaVersion != ProjectEngineerReviewSchema.Current) throw new ProjectEngineerReviewValidationException("incompatible review response SchemaVersion: " + response.SchemaVersion);
            if (!string.Equals(response.ReviewRequestId, request.ReviewRequestId, StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review response ReviewRequestId does not match request");
            if (!string.Equals(response.RequestSha256, request.RequestSha256, StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review response evidence/request hash does not match request");
            if (!string.Equals(response.DrawingName, request.DrawingName, StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review response drawing binding does not match request");
            if (!string.Equals(response.ReviewedCaseId, request.ReviewedCaseId, StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review response case binding does not match request");
            if (string.IsNullOrWhiteSpace(response.ReviewerDisplayName)) throw new ProjectEngineerReviewValidationException("reviewer display name is required");
            if (string.IsNullOrWhiteSpace(response.ReviewerRole)) throw new ProjectEngineerReviewValidationException("reviewer role is required");
            if (response.ResponseTimestampUtc.Kind != DateTimeKind.Utc) throw new ProjectEngineerReviewValidationException("review response timestamp must be UTC");
            if (response.Answers == null) throw new ProjectEngineerReviewValidationException("review response Answers are required");

            Dictionary<string, ProjectEngineerReviewQuestionDefinition> questions = request.Questions.ToDictionary(x => x.QuestionId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectEngineerReviewAnswer answer in response.Answers)
            {
                if (answer == null || string.IsNullOrWhiteSpace(answer.QuestionId)) throw new ProjectEngineerReviewValidationException("review response contains an answer without a question id");
                if (!seen.Add(answer.QuestionId)) throw new ProjectEngineerReviewValidationException("duplicate review response answer: " + answer.QuestionId);
                if (!questions.TryGetValue(answer.QuestionId, out ProjectEngineerReviewQuestionDefinition question)) throw new ProjectEngineerReviewValidationException("review response answers an unknown question: " + answer.QuestionId);
                if (ProjectEngineerAnswerKinds.IsBounded(question.AnswerKind))
                {
                    if (string.IsNullOrWhiteSpace(answer.AnswerText)) throw new ProjectEngineerReviewValidationException("required answer is missing for question: " + answer.QuestionId);
                    if (!ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind).Contains(answer.AnswerText)) throw new ProjectEngineerReviewValidationException("invalid answer vocabulary for question " + answer.QuestionId + ": " + answer.AnswerText);
                }
            }
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (question.Required && !seen.Contains(question.QuestionId)) throw new ProjectEngineerReviewValidationException("required review question is unanswered: " + question.QuestionId);
            }
            if (string.IsNullOrWhiteSpace(response.Classification)) throw new ProjectEngineerReviewValidationException("review classification is required");
            if (!ProjectEngineerReviewClassifications.All.Contains(response.Classification)) throw new ProjectEngineerReviewValidationException("unsupported review classification: " + response.Classification);
            // A ResponseId, once assigned, must bind to the response content; before assignment it is empty.
            if (!string.IsNullOrEmpty(response.ResponseId) && !string.Equals(response.ResponseId, ComputeResponseId(response), StringComparison.Ordinal)) throw new ProjectEngineerReviewValidationException("review response ResponseId does not match its content");
        }

        /// <summary>
        /// Completeness gate used by the UI: true only when reviewer identity, all required bounded
        /// answers, and a classification are present. Missing data keeps the review incomplete.
        /// </summary>
        public static string? FindIncompleteReason(ProjectEngineerReviewRequest request, string reviewerDisplayName, string reviewerRole, IReadOnlyDictionary<string, string> answers, string? classification)
        {
            if (string.IsNullOrWhiteSpace(reviewerDisplayName)) return "Reviewer display name is required before saving.";
            if (string.IsNullOrWhiteSpace(reviewerRole)) return "Reviewer role/title is required before saving.";
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!question.Required) continue;
                if (!answers.TryGetValue(question.QuestionId, out string value) || string.IsNullOrWhiteSpace(value)) return "Answer required: " + question.Text;
                if (ProjectEngineerAnswerKinds.IsBounded(question.AnswerKind) && !ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind).Contains(value)) return "Answer not in the allowed vocabulary: " + question.Text;
            }
            if (string.IsNullOrWhiteSpace(classification)) return "A review classification is required before saving.";
            if (!ProjectEngineerReviewClassifications.All.Contains(classification)) return "Unsupported classification: " + classification;
            return null;
        }

        private void EnsureRequestPersisted(ProjectEngineerReviewRequest request)
        {
            string path = RequestPath(request.ReviewRequestId);
            if (File.Exists(path))
            {
                ProjectEngineerReviewRequest existing = Read<ProjectEngineerReviewRequest>(path);
                ValidateRequest(existing);
                if (existing.RequestSha256 != request.RequestSha256) throw new ProjectEngineerReviewValidationException("persisted review request evidence does not match recorded evidence for " + request.ReviewedCaseId);
                return;
            }
            string directory = RequestDirectory(request.ReviewRequestId);
            Directory.CreateDirectory(directory);
            Write(path, request);
        }

        private static ProjectEngineerReviewEvidenceReference Evidence(string kind, string label, string value) => new ProjectEngineerReviewEvidenceReference { Kind = kind, Label = label, Value = value };

        private static ProjectEngineerReviewRequest CopyForIdentity(ProjectEngineerReviewRequest source) => new ProjectEngineerReviewRequest
        {
            SchemaVersion = source.SchemaVersion,
            ReviewRequestId = string.Empty,
            RequestSha256 = string.Empty,
            WorkflowSourceType = source.WorkflowSourceType,
            DrawingName = source.DrawingName,
            DrawingSha256 = source.DrawingSha256,
            ReviewedCaseId = source.ReviewedCaseId,
            ReviewStatus = source.ReviewStatus,
            ReviewReason = source.ReviewReason,
            EvidenceReferences = (source.EvidenceReferences ?? new List<ProjectEngineerReviewEvidenceReference>()).Select(x => new ProjectEngineerReviewEvidenceReference { Kind = x.Kind, Label = x.Label, Value = x.Value }).ToList(),
            SoftwareResult = source.SoftwareResult,
            SoftwareState = source.SoftwareState,
            Questions = (source.Questions ?? new List<ProjectEngineerReviewQuestionDefinition>()).Select(x => new ProjectEngineerReviewQuestionDefinition { QuestionId = x.QuestionId, Section = x.Section, Text = x.Text, AnswerKind = x.AnswerKind, Required = x.Required }).ToList()
        };

        private static ProjectEngineerReviewResponse CopyForIdentity(ProjectEngineerReviewResponse source) => new ProjectEngineerReviewResponse
        {
            SchemaVersion = source.SchemaVersion,
            ReviewRequestId = source.ReviewRequestId,
            RequestSha256 = source.RequestSha256,
            ResponseId = string.Empty,
            ReviewerDisplayName = source.ReviewerDisplayName,
            ReviewerRole = source.ReviewerRole,
            ResponseTimestampUtc = source.ResponseTimestampUtc,
            Answers = (source.Answers ?? new List<ProjectEngineerReviewAnswer>()).Select(x => new ProjectEngineerReviewAnswer { QuestionId = x.QuestionId, AnswerText = x.AnswerText }).ToList(),
            EngineerComment = source.EngineerComment,
            Classification = source.Classification,
            DrawingName = source.DrawingName,
            ReviewedCaseId = source.ReviewedCaseId
        };

        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }

        private static string HexSha256(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty); }
        private static byte[] Serialize<T>(T value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); return stream.ToArray(); } }
        private static void Write<T>(string path, T value) { using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); }
        private static T Read<T>(string path) { using (FileStream stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
    }
}
