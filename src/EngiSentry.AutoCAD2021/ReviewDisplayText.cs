using System.Collections.Generic;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    // Display-layer mapping for the ES_ENGINEERREVIEW surface ONLY.
    //
    // The governed values shown by the review surface — question texts, answer choices, statuses,
    // classifications, and the backend validation reasons — are canonical tokens that must stay
    // byte-identical wherever they are stored, matched, or persisted. This class NEVER replaces a
    // canonical value: it only converts one into the text shown in the current UI language.
    //
    // When English is active every mapping is the identity of the canonical value, so the accepted
    // English surface is unchanged. When Persian is active the same canonical token is rendered
    // through the Persian resources. The control keeps holding canonical tokens in its combo items
    // and selections, and persists exactly those.
    internal static class ReviewDisplayText
    {
        // canonical question text -> resource key (the table is keyed on the canonical text because
        // the backend validation reasons embed that same canonical text in their tail)
        private static readonly Dictionary<string, string> QuestionKeys = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Road minimum understandable", "Question_criteria_road_minimum" },
            { "Plant / Security Fence minimum understandable", "Question_criteria_fence_minimum" },
            { "Unit Boundary minimum understandable", "Question_criteria_unit_boundary" },
            { "Result understandable without opening JSON", "Question_results_understandable_without_json" },
            { "Observed distance understandable", "Question_results_observed_distance" },
            { "Insufficient evidence understandable", "Question_results_insufficient_evidence" },
            { "Governance wording understandable", "Question_results_governance_wording" },
            { "Result appears technically plausible", "Question_interpretation_technically_plausible" },
            { "False positive suspected", "Question_interpretation_false_positive" },
            { "False negative suspected", "Question_interpretation_false_negative" },
            { "Ambiguous/missing drawing evidence correctly reported", "Question_interpretation_ambiguous_reported" },
            { "Blocking usability issue", "Question_usability_blocking_issue" },
            { "Issue description", "Question_usability_issue_description" },
            { "Engineer comment", "Question_usability_engineer_comment" }
        };

        // the accepted bounded answer choices
        private static readonly Dictionary<string, string> AnswerKeys = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { "Yes", "AnswerYes" },
            { "No", "AnswerNo" },
            { "N/A", "AnswerNotApplicable" },
            { "Needs review", "AnswerNeedsReview" }
        };

        private static readonly Dictionary<string, string> StatusKeys = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { ProjectEngineerReviewStatuses.Pending, "StatusPending" },
            { ProjectEngineerReviewStatuses.Completed, "StatusCompleted" },
            { ProjectEngineerReviewStatuses.Invalid, "StatusInvalid" }
        };

        private static readonly Dictionary<string, string> ClassificationKeys = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            { ProjectEngineerReviewClassifications.Pass, "Classification_PASS" },
            { ProjectEngineerReviewClassifications.UsabilityIssue, "Classification_USABILITY_ISSUE" },
            { ProjectEngineerReviewClassifications.SuspectedProductDefect, "Classification_SUSPECTED_PRODUCT_DEFECT" },
            { ProjectEngineerReviewClassifications.DrawingEvidenceLimitation, "Classification_DRAWING_EVIDENCE_LIMITATION" },
            { ProjectEngineerReviewClassifications.EngineeringReviewRequired, "Classification_ENGINEERING_REVIEW_REQUIRED" }
        };

        // the canonical reason literals the completeness gate produces, duplicated here on purpose:
        // the service stays untouched, so the display layer carries its own copy of the exact reason
        // text it matches against
        private const string ReasonReviewerNameCanonical = "Reviewer display name is required before saving.";
        private const string ReasonReviewerRoleCanonical = "Reviewer role/title is required before saving.";
        private const string ReasonClassificationRequiredCanonical = "A review classification is required before saving.";
        private const string ReasonAnswerRequiredCanonical = "Answer required: ";
        private const string ReasonAnswerVocabularyCanonical = "Answer not in the allowed vocabulary: ";
        private const string ReasonUnsupportedClassificationCanonical = "Unsupported classification: ";

        internal static string Question(ProjectEngineerReviewQuestionDefinition question)
        {
            if (question == null) return string.Empty;
            return QuestionText(question.Text);
        }

        internal static string QuestionText(string canonicalText)
        {
            if (string.IsNullOrEmpty(canonicalText) || !ReviewLocalization.IsPersian) return canonicalText ?? string.Empty;
            return QuestionKeys.TryGetValue(canonicalText, out string key) ? Resolve(key, canonicalText) : canonicalText;
        }

        internal static string Answer(string canonical)
        {
            if (string.IsNullOrEmpty(canonical) || !ReviewLocalization.IsPersian) return canonical ?? string.Empty;
            return AnswerKeys.TryGetValue(canonical, out string key) ? Resolve(key, canonical) : canonical;
        }

        internal static string Status(string canonical)
        {
            if (string.IsNullOrEmpty(canonical) || !ReviewLocalization.IsPersian) return canonical ?? string.Empty;
            return StatusKeys.TryGetValue(canonical, out string key) ? Resolve(key, canonical) : canonical;
        }

        internal static string Classification(string canonical)
        {
            if (string.IsNullOrEmpty(canonical) || !ReviewLocalization.IsPersian) return canonical ?? string.Empty;
            return ClassificationKeys.TryGetValue(canonical, out string key) ? Resolve(key, canonical) : canonical;
        }

        // The backend validation reason is canonical and is what the completeness gate returns; the
        // surface shows the same reason in the selected UI language. Prefix-bearing reasons carry a
        // canonical question text (or classification token) after the marker, which is mapped too,
        // so a Persian reason reads fully in Persian while the underlying reason the surface acts on
        // is untouched.
        internal static string Reason(string canonicalReason)
        {
            if (string.IsNullOrEmpty(canonicalReason) || !ReviewLocalization.IsPersian) return canonicalReason ?? string.Empty;
            if (canonicalReason == ReasonReviewerNameCanonical) return Resolve("ReasonReviewerName", canonicalReason);
            if (canonicalReason == ReasonReviewerRoleCanonical) return Resolve("ReasonReviewerRole", canonicalReason);
            if (canonicalReason == ReasonClassificationRequiredCanonical) return Resolve("ReasonClassificationRequired", canonicalReason);
            string tail = StripPrefix(canonicalReason, ReasonAnswerRequiredCanonical, out bool answerRequired);
            if (answerRequired) return Resolve("ReasonAnswerRequired", ReasonAnswerRequiredCanonical) + QuestionText(tail);
            tail = StripPrefix(canonicalReason, ReasonAnswerVocabularyCanonical, out bool vocabulary);
            if (vocabulary) return Resolve("ReasonAnswerVocabulary", ReasonAnswerVocabularyCanonical) + QuestionText(tail);
            tail = StripPrefix(canonicalReason, ReasonUnsupportedClassificationCanonical, out bool unsupported);
            if (unsupported) return Resolve("ReasonUnsupportedClassification", ReasonUnsupportedClassificationCanonical) + Classification(tail);
            return canonicalReason;
        }

        // English identity is guaranteed by the neutral resources, which hold the canonical value
        // itself; a missing entry must never mask the canonical text.
        private static string Resolve(string key, string canonical)
        {
            string value = ReviewStrings.Get(key);
            return string.IsNullOrEmpty(value) || value == key ? canonical : value;
        }

        private static string StripPrefix(string value, string prefix, out bool matched)
        {
            matched = value.StartsWith(prefix, System.StringComparison.Ordinal);
            return matched ? value.Substring(prefix.Length) : value;
        }
    }
}
