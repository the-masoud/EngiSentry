using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// The exact accepted P10.3 supervised-engineer-pilot observation template, expressed as
    /// bounded review questions. No question is added, broadened, or narrowed here: the answer
    /// vocabularies are the ones the accepted template already implies (Yes/No, Yes/No/N/A,
    /// Yes/No/Needs review) and the free-text items the template leaves blank.
    /// </summary>
    public static class ProjectEngineerReviewQuestions
    {
        public const string SectionCriteriaUx = "Criteria UX";
        public const string SectionResultsUx = "Results UX";
        public const string SectionEngineeringInterpretation = "Engineering interpretation";
        public const string SectionUsability = "Usability";

        public const string IssueDescription = "usability.issue-description";
        public const string EngineerComment = "usability.engineer-comment";

        public static IReadOnlyList<ProjectEngineerReviewQuestionDefinition> All { get; } = new ProjectEngineerReviewQuestionDefinition[]
        {
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "criteria.road-minimum", Section = SectionCriteriaUx, Text = "Road minimum understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "criteria.fence-minimum", Section = SectionCriteriaUx, Text = "Plant / Security Fence minimum understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "criteria.unit-boundary", Section = SectionCriteriaUx, Text = "Unit Boundary minimum understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "results.understandable-without-json", Section = SectionResultsUx, Text = "Result understandable without opening JSON", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "results.observed-distance", Section = SectionResultsUx, Text = "Observed distance understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNoNotApplicable },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "results.insufficient-evidence", Section = SectionResultsUx, Text = "Insufficient evidence understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNoNotApplicable },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "results.governance-wording", Section = SectionResultsUx, Text = "Governance wording understandable", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "interpretation.technically-plausible", Section = SectionEngineeringInterpretation, Text = "Result appears technically plausible", AnswerKind = ProjectEngineerAnswerKinds.YesNoNeedsReview },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "interpretation.false-positive", Section = SectionEngineeringInterpretation, Text = "False positive suspected", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "interpretation.false-negative", Section = SectionEngineeringInterpretation, Text = "False negative suspected", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "interpretation.ambiguous-reported", Section = SectionEngineeringInterpretation, Text = "Ambiguous/missing drawing evidence correctly reported", AnswerKind = ProjectEngineerAnswerKinds.YesNoNotApplicable },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = "usability.blocking-issue", Section = SectionUsability, Text = "Blocking usability issue", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = IssueDescription, Section = SectionUsability, Text = "Issue description", AnswerKind = ProjectEngineerAnswerKinds.FreeText, Required = false },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = EngineerComment, Section = SectionUsability, Text = "Engineer comment", AnswerKind = ProjectEngineerAnswerKinds.FreeText, Required = false }
        };
    }
}
