using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Project Engineer Review contracts. These are deliberately separate from the P8
    /// <see cref="HumanEngineeringReviewDecisionRecord"/> / P9 approval machinery: a project
    /// engineer review records human observation evidence only and never creates approval,
    /// authority policy, trusted-issuer status, or rule activation.
    /// </summary>
    public static class ProjectEngineerReviewSchema
    {
        public const string Current = "PER-1";
    }

    public static class ProjectEngineerReviewStatuses
    {
        public const string Pending = "PENDING";
        public const string Completed = "COMPLETED";
        public const string Invalid = "INVALID";
    }

    public static class ProjectEngineerReviewClassifications
    {
        public const string Pass = "PASS";
        public const string UsabilityIssue = "USABILITY_ISSUE";
        public const string SuspectedProductDefect = "SUSPECTED_PRODUCT_DEFECT";
        public const string DrawingEvidenceLimitation = "DRAWING_EVIDENCE_LIMITATION";
        public const string EngineeringReviewRequired = "ENGINEERING_REVIEW_REQUIRED";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Pass, UsabilityIssue, SuspectedProductDefect, DrawingEvidenceLimitation, EngineeringReviewRequired
        };
    }

    /// <summary>
    /// Answer vocabulary kinds. These map one-to-one onto the accepted P10.3 observation
    /// template; no vocabulary is broadened.
    /// </summary>
    public static class ProjectEngineerAnswerKinds
    {
        public const string YesNo = "YES_NO";
        public const string YesNoNotApplicable = "YES_NO_NOT_APPLICABLE";
        public const string YesNoNeedsReview = "YES_NO_NEEDS_REVIEW";
        public const string FreeText = "FREE_TEXT";

        public static readonly IReadOnlyList<string> All = new[] { YesNo, YesNoNotApplicable, YesNoNeedsReview, FreeText };

        public static IReadOnlyList<string> AllowedAnswers(string answerKind)
        {
            if (answerKind == YesNo) return new[] { "Yes", "No" };
            if (answerKind == YesNoNotApplicable) return new[] { "Yes", "No", "N/A" };
            if (answerKind == YesNoNeedsReview) return new[] { "Yes", "No", "Needs review" };
            return Array.Empty<string>();
        }

        public static bool IsBounded(string answerKind) => answerKind != FreeText;
    }

    public sealed class ProjectEngineerReviewQuestionDefinition
    {
        public string QuestionId { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string AnswerKind { get; set; } = ProjectEngineerAnswerKinds.YesNo;
        public bool Required { get; set; } = true;
    }

    [DataContract]
    public sealed class ProjectEngineerReviewEvidenceReference
    {
        [DataMember(Order = 1)] public string Kind { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Label { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string? Value { get; set; }
    }

    [DataContract]
    public sealed class ProjectEngineerReviewRequest
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = ProjectEngineerReviewSchema.Current;
        [DataMember(Order = 2)] public string ReviewRequestId { get; set; } = string.Empty;
        /// <summary>
        /// Canonical SHA-256 over the request with this field blank. Binding carried by every
        /// response so a stale or mismatched response is rejected rather than silently accepted.
        /// </summary>
        [DataMember(Order = 3)] public string RequestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string WorkflowSourceType { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? DrawingSha256 { get; set; }
        [DataMember(Order = 7)] public string ReviewedCaseId { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string ReviewStatus { get; set; } = ProjectEngineerReviewStatuses.Pending;
        [DataMember(Order = 9)] public string ReviewReason { get; set; } = string.Empty;
        [DataMember(Order = 10)] public List<ProjectEngineerReviewEvidenceReference> EvidenceReferences { get; set; } = new List<ProjectEngineerReviewEvidenceReference>();
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? SoftwareResult { get; set; }
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SoftwareState { get; set; }
        [DataMember(Order = 13)] public List<ProjectEngineerReviewQuestionDefinition> Questions { get; set; } = new List<ProjectEngineerReviewQuestionDefinition>();
    }

    [DataContract]
    public sealed class ProjectEngineerReviewAnswer
    {
        [DataMember(Order = 1)] public string QuestionId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string AnswerText { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class ProjectEngineerReviewResponse
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = ProjectEngineerReviewSchema.Current;
        [DataMember(Order = 2)] public string ReviewRequestId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string RequestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? ResponseId { get; set; }
        [DataMember(Order = 5)] public string ReviewerDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ReviewerRole { get; set; } = string.Empty;
        [DataMember(Order = 7)] public DateTime ResponseTimestampUtc { get; set; }
        [DataMember(Order = 8)] public List<ProjectEngineerReviewAnswer> Answers { get; set; } = new List<ProjectEngineerReviewAnswer>();
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? EngineerComment { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? Classification { get; set; }
        /// <summary>Binding copies used to reject a response presented against the wrong request.</summary>
        [DataMember(Order = 11)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string ReviewedCaseId { get; set; } = string.Empty;
    }
}
