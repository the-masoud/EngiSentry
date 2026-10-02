using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>Occurrence-scoped candidate decisions in the existing external placement-resolution root.</summary>
    public sealed class EquipmentPlacementCandidateDecisionService
    {
        private const string QuestionId = "placement.candidate-decision";
        private readonly string root;
        private readonly ProjectEngineerReviewService reviews;
        public EquipmentPlacementCandidateDecisionService() : this(EquipmentPlacementResolutionService.ResolveRoot(), new ProjectEngineerReviewService()) { }
        public EquipmentPlacementCandidateDecisionService(string root, ProjectEngineerReviewService reviews)
        { this.root = root ?? throw new ArgumentNullException(nameof(root)); this.reviews = reviews ?? throw new ArgumentNullException(nameof(reviews)); }

        public DrawingPlacementCandidateDecision Record(EquipmentPlacement placement, string drawingName, string drawingPath,
            string drawingSha256, string insertionUnits, string action, string reviewerName, string reviewerRole, string? comment = null)
        {
            if (placement?.DrawingCandidate == null || placement.ResolutionState != PlacementResolutionState.ReviewRequired)
                throw new InvalidOperationException("Only one current REVIEW_REQUIRED candidate can be reviewed.");
            if (action != DrawingPlacementCandidateActions.Accept && action != DrawingPlacementCandidateActions.Reject)
                throw new ArgumentException("ACCEPT or REJECT is required", nameof(action));
            if (string.IsNullOrWhiteSpace(reviewerName) || string.IsNullOrWhiteSpace(reviewerRole))
                throw new InvalidOperationException("Reviewer name and role are required.");
            if (action == DrawingPlacementCandidateActions.Accept && !placement.DrawingCandidate.CoordinatesInDrawingUnits)
                throw new InvalidOperationException("Candidate coordinate basis is not proven for governed measurement.");
            if (!string.Equals(HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath), drawingSha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The drawing changed after the candidate was loaded.");
            DrawingPlacementCandidate candidate = placement.DrawingCandidate;
            string fingerprint = DrawingPlacementCandidateDecisionIdentity.CandidateFingerprint(placement, candidate);
            DateTime now = EquipmentPlacementResolutionIdentity.NormalizeTimestamp(DateTime.UtcNow);
            var evidence = new List<ProjectEngineerReviewEvidenceReference>
            {
                Evidence("OccurrenceId", placement.PlacementId), Evidence("EquipmentTag", placement.NormalizedTag),
                Evidence("TagHandle", placement.TagSourceHandle), Evidence("CandidateFingerprint", fingerprint),
                Evidence("CandidateX", EquipmentPlacementResolutionIdentity.CanonicalDecimal(candidate.CandidateX)),
                Evidence("CandidateY", EquipmentPlacementResolutionIdentity.CanonicalDecimal(candidate.CandidateY)),
                Evidence("XHandle", candidate.XEntityHandle), Evidence("YHandle", candidate.YEntityHandle),
                Evidence("EvidenceType", candidate.EvidenceType), Evidence("EvidenceSummary", candidate.EvidenceSummary),
                Evidence("InsertionUnits", insertionUnits), Evidence("Decision", action),
                Evidence("DecisionTimestampUtc", now.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            };
            var questions = new[] { new ProjectEngineerReviewQuestionDefinition { QuestionId = QuestionId,
                Section = "Placement candidate", Text = "Accept this exact occurrence and candidate?",
                AnswerKind = ProjectEngineerAnswerKinds.YesNo } };
            ProjectEngineerReviewRequest request = reviews.CreateRequest(
                ProjectEngineerReviewService.WorkflowSourceEquipmentPlacement, drawingName, drawingSha256,
                placement.PlacementId, "Exact-occurrence DWG candidate review", evidence,
                "DRAWING_CANDIDATE_REVIEW", action, questions);
            var response = new ProjectEngineerReviewResponse
            {
                ReviewRequestId = request.ReviewRequestId, RequestSha256 = request.RequestSha256,
                DrawingName = drawingName, ReviewedCaseId = placement.PlacementId,
                ReviewerDisplayName = reviewerName.Trim(), ReviewerRole = reviewerRole.Trim(),
                ResponseTimestampUtc = now, EngineerComment = comment,
                Classification = ProjectEngineerReviewClassifications.EngineeringReviewRequired,
                Answers = new List<ProjectEngineerReviewAnswer> { new ProjectEngineerReviewAnswer {
                    QuestionId = QuestionId, AnswerText = action == DrawingPlacementCandidateActions.Accept ? "Yes" : "No" } }
            };
            reviews.SaveResponse(request, response);
            var record = new DrawingPlacementCandidateDecision
            {
                DrawingName = drawingName, DrawingSha256 = drawingSha256, OccurrenceId = placement.PlacementId,
                EquipmentTag = placement.NormalizedTag, TagHandle = placement.TagSourceHandle,
                CandidateFingerprint = fingerprint, CandidateX = candidate.CandidateX, CandidateY = candidate.CandidateY,
                XHandle = candidate.XEntityHandle, YHandle = candidate.YEntityHandle,
                EvidenceType = candidate.EvidenceType, EvidenceSummary = candidate.EvidenceSummary,
                InsertionUnits = insertionUnits, Action = action, ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256, ResponseId = response.ResponseId!,
                ReviewerDisplayName = response.ReviewerDisplayName, ReviewerRole = response.ReviewerRole,
                ResponseTimestampUtc = response.ResponseTimestampUtc, EngineerComment = comment
            };
            record.DecisionId = DrawingPlacementCandidateDecisionIdentity.DecisionId(record);
            Validate(record);
            string directory = Path.Combine(root, "candidate-decisions");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, record.DecisionId + ".ecd.json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                new DataContractJsonSerializer(typeof(DrawingPlacementCandidateDecision)).WriteObject(stream, record);
            return record;
        }

        public IReadOnlyList<DrawingPlacementCandidateDecision> ResolveForDrawing(string drawingSha256, string insertionUnits)
        {
            if (!EquipmentPlacementResolutionValidator.IsSha256(drawingSha256) || string.IsNullOrWhiteSpace(insertionUnits))
                return Array.Empty<DrawingPlacementCandidateDecision>();
            string directory = Path.Combine(root, "candidate-decisions");
            if (!Directory.Exists(directory)) return Array.Empty<DrawingPlacementCandidateDecision>();
            var records = new List<DrawingPlacementCandidateDecision>();
            foreach (string path in Directory.GetFiles(directory, "*.ecd.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    DrawingPlacementCandidateDecision record;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        record = (DrawingPlacementCandidateDecision)new DataContractJsonSerializer(typeof(DrawingPlacementCandidateDecision)).ReadObject(stream)!;
                    Validate(record);
                    if (record.DecisionId + ".ecd.json" != Path.GetFileName(path) ||
                        record.DrawingSha256 != drawingSha256 || record.InsertionUnits != insertionUnits || !ReviewBindingValid(record)) continue;
                    records.Add(record);
                }
                catch (Exception ex) when (ex is IOException || ex is System.Runtime.Serialization.SerializationException || ex is InvalidOperationException) { }
            }
            return records.GroupBy(x => x.OccurrenceId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.ResponseTimestampUtc).ThenByDescending(x => x.DecisionId, StringComparer.Ordinal).First())
                .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal).ToList();
        }

        private bool ReviewBindingValid(DrawingPlacementCandidateDecision record)
        {
            try
            {
                ProjectEngineerReviewRequest? request = reviews.LoadRequest(record.ReviewRequestId);
                ProjectEngineerReviewResponse? response = reviews.LoadResponse(record.ReviewRequestId);
                return request != null && response != null && request.DrawingSha256 == record.DrawingSha256
                    && request.ReviewedCaseId == record.OccurrenceId && request.RequestSha256 == record.RequestSha256
                    && response.ResponseId == record.ResponseId && response.ReviewerDisplayName == record.ReviewerDisplayName
                    && response.ReviewerRole == record.ReviewerRole && response.ResponseTimestampUtc == record.ResponseTimestampUtc
                    && response.Answers.Count == 1 && response.Answers[0].QuestionId == QuestionId
                    && response.Answers[0].AnswerText == (record.Action == DrawingPlacementCandidateActions.Accept ? "Yes" : "No")
                    && request.EvidenceReferences.Any(x => x.Kind == "CandidateFingerprint" && x.Value == record.CandidateFingerprint);
            }
            catch (Exception) { return false; }
        }

        private static ProjectEngineerReviewEvidenceReference Evidence(string kind, string value) =>
            new ProjectEngineerReviewEvidenceReference { Kind = kind, Label = kind, Value = value };

        private static void Validate(DrawingPlacementCandidateDecision record)
        {
            if (record == null || record.SchemaVersion != "EPR-CANDIDATE-1" ||
                !EquipmentPlacementResolutionValidator.IsSha256(record.DrawingSha256) ||
                !EquipmentPlacementResolutionValidator.IsSha256(record.CandidateFingerprint) ||
                string.IsNullOrWhiteSpace(record.OccurrenceId) || string.IsNullOrWhiteSpace(record.TagHandle) ||
                string.IsNullOrWhiteSpace(record.XHandle) || string.IsNullOrWhiteSpace(record.YHandle) ||
                string.IsNullOrWhiteSpace(record.EquipmentTag) || string.IsNullOrWhiteSpace(record.InsertionUnits) ||
                string.IsNullOrWhiteSpace(record.EvidenceType) || string.IsNullOrWhiteSpace(record.EvidenceSummary) ||
                (record.Action != DrawingPlacementCandidateActions.Accept && record.Action != DrawingPlacementCandidateActions.Reject) ||
                record.ResponseTimestampUtc.Kind != DateTimeKind.Utc ||
                string.IsNullOrWhiteSpace(record.ReviewerDisplayName) || string.IsNullOrWhiteSpace(record.ReviewerRole) ||
                !EquipmentPlacementResolutionValidator.IsSha256(record.RequestSha256) ||
                string.IsNullOrWhiteSpace(record.ReviewRequestId) || string.IsNullOrWhiteSpace(record.ResponseId) ||
                record.DecisionId != DrawingPlacementCandidateDecisionIdentity.DecisionId(record))
                throw new InvalidOperationException("Invalid exact-occurrence candidate decision.");
        }
    }
}
