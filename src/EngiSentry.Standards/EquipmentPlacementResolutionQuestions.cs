using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// The bounded confirmation template for the engineer-assisted equipment placement workflow.
    /// It reuses the accepted project-engineer answer vocabularies; no new vocabulary is added.
    /// </summary>
    public static class EquipmentPlacementResolutionQuestions
    {
        public const string SectionPlacement = "Placement confirmation";
        public const string SectionNotes = "Review notes";

        public const string PointMatchesEquipment = "placement.point-matches-equipment";
        public const string AxesCorrect = "placement.axes-correct";
        public const string UnitsConfirmed = "placement.units-confirmed";
        public const string EngineerComment = "placement.engineer-comment";

        public static IReadOnlyList<ProjectEngineerReviewQuestionDefinition> All { get; } = new ProjectEngineerReviewQuestionDefinition[]
        {
            new ProjectEngineerReviewQuestionDefinition { QuestionId = PointMatchesEquipment, Section = SectionPlacement, Text = "Picked point is the equipment location", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = AxesCorrect, Section = SectionPlacement, Text = "Easting/Northing axes are correct", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = UnitsConfirmed, Section = SectionPlacement, Text = "Drawing units confirmed for this coordinate", AnswerKind = ProjectEngineerAnswerKinds.YesNo },
            new ProjectEngineerReviewQuestionDefinition { QuestionId = EngineerComment, Section = SectionNotes, Text = "Engineer comment", AnswerKind = ProjectEngineerAnswerKinds.FreeText, Required = false }
        };
    }
}
