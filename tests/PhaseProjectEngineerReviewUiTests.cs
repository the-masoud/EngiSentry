#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    // UI-contract tests for the ES_ENGINEERREVIEW review surface. They construct the real review
    // control against an ISOLATED test-only review root so the production P10.3 root is never
    // read or written, and they never answer a real P10.3 case: the only save exercised here is a
    // single unmistakably synthetic TEST_ONLY case.
    internal static class PhaseProjectEngineerReviewUiTests
    {
        private static int assertions;

        internal static void Run()
        {
            Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The review-root override must stay disabled outside an explicit test scope.");

            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PER-UI-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var service = new ProjectEngineerReviewService(root);

                // 1. the command handler still exists on the plugin command class
                MethodInfo command = typeof(EngiSentry.AutoCAD2021.Commands).GetMethod("EngineerReview", BindingFlags.Public | BindingFlags.Instance);
                Assert(command != null, "The ES_ENGINEERREVIEW command handler is missing.");
                try
                {
                    foreach (object attribute in command.GetCustomAttributes(false))
                    {
                        PropertyInfo nameProperty = attribute.GetType().GetProperty("Name");
                        if (nameProperty == null) continue;
                        string commandName = nameProperty.GetValue(attribute) as string;
                        if (!string.IsNullOrEmpty(commandName)) Assert(commandName == "ES_ENGINEERREVIEW", "Command name changed: " + commandName);
                    }
                }
                catch (FileNotFoundException)
                {
                    // The Autodesk runtime attribute type is not resolvable in the offline test host;
                    // the command name is verified again during the AutoCAD runtime acceptance.
                }

                // 2. the control constructs against an isolated root and shows the four real pilots
                var control = new EngiSentry.AutoCAD2021.ProjectEngineerReviewControl(service);
                Assert(control.QueueCount == 4, "The review queue shown by the control must be exactly the four pilot cases.");
                Assert(service.LoadQueue().All(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending), "Opening the UI must not complete any review.");
                Assert(ResponseFiles(root).Count == 0, "Constructing the UI must not write any response.");

                // 3. control surface contract: four navigation/save buttons with stable labels
                List<Button> buttons = Descendants(control).OfType<Button>().ToList();
                Assert(buttons.Count == 4, "The action bar must contain exactly four buttons.");
                CollectionAssert(buttons.Select(b => b.Text).ToList(), new List<string> { "Previous", "Next", "Next Pending", "Save Review" }, "Action bar labels changed.");
                Button previous = buttons.Single(b => b.Text == "Previous");
                Button next = buttons.Single(b => b.Text == "Next");
                Button nextPending = buttons.Single(b => b.Text == "Next Pending");
                Button save = buttons.Single(b => b.Text == "Save Review");
                Assert(!previous.Enabled, "Previous must be disabled on the first case.");
                Assert(next.Enabled && nextPending.Enabled && save.Enabled, "Next / Next Pending / Save must be enabled on a pending case.");
                Assert(save.BackColor != next.BackColor, "Save Review must be visually distinct from navigation buttons.");

                // 4. one answer control per question, with the exact accepted vocabularies
                List<Control> answers = ProjectEngineerReviewQuestions.All.Select(q => AnswerControl(control, q.QuestionId)).ToList();
                Assert(answers.Count == 14 && answers.Distinct().Count() == 14, "Every one of the 14 questions needs its own answer control.");
                Assert(answers.OfType<TextBox>().Count() == 2, "Exactly the two free-text questions may use a text box.");
                Assert(answers.OfType<ComboBox>().Count() == 12, "Exactly the 12 bounded questions may use a combo box.");
                foreach (ProjectEngineerReviewQuestionDefinition question in ProjectEngineerReviewQuestions.All)
                {
                    Control answer = AnswerControl(control, question.QuestionId);
                    if (answer is ComboBox combo)
                    {
                        List<string> items = combo.Items.Cast<string>().ToList();
                        List<string> expected = new List<string> { string.Empty }.Concat(ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)).ToList();
                        CollectionAssert(items, expected, "Answer vocabulary changed for " + question.QuestionId + ".");
                    }
                }
                List<string> vocabulary = answers.OfType<ComboBox>().SelectMany(c => c.Items.Cast<string>()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                CollectionAssert(vocabulary, new List<string> { string.Empty, "N/A", "Needs review", "No", "Yes" }.OrderBy(x => x, StringComparer.Ordinal).ToList(), "Answer vocabulary set changed.");

                // 5. classification: exactly the accepted vocabulary, presented in its own combo
                List<ComboBox> allCombos = Descendants(control).OfType<ComboBox>().ToList();
                Assert(allCombos.Count == 13, "The surface must hold exactly the 12 answer combos plus the classification combo.");
                ComboBox classification = allCombos.Single(c => c.Items.Cast<string>().Contains(ProjectEngineerReviewClassifications.EngineeringReviewRequired));
                List<string> expectedClassification = new List<string> { string.Empty }.Concat(ProjectEngineerReviewClassifications.All).ToList();
                CollectionAssert(classification.Items.Cast<string>().ToList(), expectedClassification, "Classification vocabulary changed.");

                // 6. no favorable answer is ever preselected
                foreach (ComboBox combo in allCombos) Assert(combo.SelectedIndex == 0 && (string)combo.SelectedItem == string.Empty, "No answer or classification may be preselected.");
                foreach (TextBox box in answers.OfType<TextBox>()) Assert(string.IsNullOrEmpty(box.Text), "No free-text answer may be preselected.");
                Assert(string.IsNullOrEmpty(CommentBox(control).Text), "The engineer comment must start empty.");
                Assert(string.IsNullOrEmpty(ReviewerNameBox(control).Text) && string.IsNullOrEmpty(ReviewerRoleBox(control).Text), "Reviewer identity must start empty.");

                // 7. header status and case counter
                Assert(StatusBadge(control).Text.EndsWith(" " + ProjectEngineerReviewStatuses.Pending, StringComparison.Ordinal), "The header must show the current PENDING status.");
                Assert(CaseLabel(control).Text == "Case 1 of 4", "The header must show the case counter.");

                // 8. evidence stays complete but is presented as readable key/value content, not a raw dump
                Assert(Descendants(control).OfType<TextBox>().All(t => !t.ReadOnly), "Evidence must be presented as a readable key/value area, not a raw text dump.");
                Label requestHashLabel = HashLabels(control).First();
                string expectedHash = service.LoadQueue()[0].RequestSha256;
                Assert(requestHashLabel.Text == expectedHash, "The request hash must remain part of the presented evidence.");
                Assert(requestHashLabel.AutoEllipsis, "Long hash values must be presented as truncation-ready text, not as dominant content.");
                Assert(ToolTipText(control, requestHashLabel) == expectedHash, "A truncated evidence value must expose its full value in the tooltip.");

                // 9. navigation never writes a response and never completes a case
                next.PerformClick();
                Assert(CaseLabel(control).Text == "Case 2 of 4", "Next must advance the case counter.");
                previous.PerformClick();
                Assert(CaseLabel(control).Text == "Case 1 of 4", "Previous must move back.");
                nextPending.PerformClick();
                Assert(CaseLabel(control).Text == "Case 2 of 4", "Next Pending must move to the next pending case.");
                previous.PerformClick();
                Assert(ResponseFiles(root).Count == 0, "Navigating the UI must not write any response.");
                Assert(service.LoadQueue().All(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending), "Navigating the UI must not complete any review.");

                // 10. explicit save: incomplete input is blocked and writes nothing
                save.PerformClick();
                Assert(Message(control).Text.StartsWith("Review not saved", StringComparison.Ordinal), "An incomplete save must report a readable validation reason. Actual: " + Message(control).Text);
                Assert(ResponseFiles(root).Count == 0, "An incomplete save must not write a response.");

                // 11. explicit save on a single unmistakably synthetic TEST_ONLY case only
                ProjectEngineerReviewRequest synthetic = SeedSyntheticCase(service);
                control.LoadQueue();
                Assert(control.QueueCount == 5, "The isolated root must add exactly one synthetic case to the four pilots.");
                for (int i = 0; i < 4; i++) next.PerformClick();
                Assert(CaseLabel(control).Text == "Case 5 of 5", "Navigation must reach the synthetic case.");

                ReviewerNameBox(control).Text = "Test Reviewer";
                ReviewerRoleBox(control).Text = "Test Role";
                foreach (ProjectEngineerReviewQuestionDefinition question in ProjectEngineerReviewQuestions.All)
                {
                    Control answer = AnswerControl(control, question.QuestionId);
                    if (answer is ComboBox combo) combo.SelectedIndex = 1;
                    else answer.Text = "test-only free text";
                }
                classification.SelectedIndex = 1;
                CommentBox(control).Text = "test-only comment";
                save.PerformClick();
                Assert(Message(control).Text.Contains("saved"), "A valid explicit save must confirm success. Actual: " + Message(control).Text);
                Assert(ResponseFiles(root).Count == 1, "Exactly one response must be written by one explicit save.");
                Assert(service.ResolveStatus(synthetic) == ProjectEngineerReviewStatuses.Completed, "The synthetic case must resolve COMPLETED after the save.");
                Assert(service.LoadQueue().Where(x => x.WorkflowSourceType == ProjectEngineerReviewService.WorkflowSourcePilotP103).All(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending), "The four real P10.3 cases must stay PENDING.");
                Assert(!save.Enabled, "A completed case must disable further saving.");
                ProjectEngineerReviewResponse stored = service.LoadResponse(synthetic.ReviewRequestId);
                Assert(stored.ReviewerDisplayName == "Test Reviewer" && stored.ReviewerRole == "Test Role", "Reviewer identity must be preserved by the UI save.");
                Assert(stored.Classification == ProjectEngineerReviewClassifications.Pass, "Classification must be preserved by the UI save.");
                Assert(stored.Answers.Count == 14 && stored.Answers.Select(x => x.QuestionId).Distinct().Count() == 14, "The saved response must answer all 14 questions exactly once.");
                Assert(stored.EngineerComment == "test-only comment", "The engineer comment must be preserved by the UI save.");
                Assert(stored.RequestSha256 == synthetic.RequestSha256, "The saved response must remain bound to its request evidence.");

                // 12. layout sanity at the accepted window size: no sibling overlaps, no degenerate fields
                control.Size = new Size(1000, 960);
                PerformLayout(control);
                Assert(NoSiblingOverlaps(control), "No two controls may overlap in the review panel.");
                Assert(Descendants(control).OfType<TextBox>().All(t => t.Width >= 40 && t.Height >= 16), "Every text field must remain usable at the accepted size.");
                Assert(Descendants(control).OfType<ComboBox>().All(c => c.Width >= 40 && c.Height >= 16), "Every answer field must remain usable at the accepted size.");
                Assert(Descendants(control).OfType<Button>().All(b => b.Width >= 60 && b.Height >= 20), "Every action button must remain usable at the accepted size.");

                Console.WriteLine("Phase Project Engineer Review UI assertions passed: " + assertions);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static ProjectEngineerReviewRequest SeedSyntheticCase(ProjectEngineerReviewService service)
        {
            ProjectEngineerReviewRequest seeded = service.CreateRequest(ProjectEngineerReviewService.WorkflowSourceTestOnly, "TEST-ONLY-UI.dwg", "DDDD".PadLeft(64, '0'), "TEST-ONLY-UI-CASE", "Isolated UI test-only review. Never a real P10.3 pilot case.", new[] { new ProjectEngineerReviewEvidenceReference { Kind = "DrawingSha256", Label = "Synthetic pre/post SHA-256", Value = "DDDD".PadLeft(64, '0') } }, "OPERATIONAL_PASS", "ENGINEERING_REVIEW_PENDING");
            // persist the request only, so the case stays PENDING until the real UI Save path runs
            var vehicle = new ProjectEngineerReviewResponse
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                ReviewRequestId = seeded.ReviewRequestId,
                RequestSha256 = seeded.RequestSha256,
                DrawingName = seeded.DrawingName,
                ReviewedCaseId = seeded.ReviewedCaseId,
                ReviewerDisplayName = "Seeded Test-Only Reviewer",
                ReviewerRole = "UI Test",
                ResponseTimestampUtc = DateTime.UtcNow,
                Answers = ProjectEngineerReviewQuestions.All.Select(q => new ProjectEngineerReviewAnswer { QuestionId = q.QuestionId, AnswerText = ProjectEngineerAnswerKinds.IsBounded(q.AnswerKind) ? ProjectEngineerAnswerKinds.AllowedAnswers(q.AnswerKind)[0] : "seeded" }).ToList(),
                EngineerComment = null,
                Classification = ProjectEngineerReviewClassifications.Pass
            };
            service.SaveResponse(seeded, vehicle);
            File.Delete(service.ResponsePath(seeded.ReviewRequestId));
            return seeded;
        }

        private static List<string> ResponseFiles(string root) => Directory.GetFiles(root, "review-response.json", SearchOption.AllDirectories).ToList();

        private static IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls)
            {
                yield return child;
                foreach (Control grandchild in Descendants(child)) yield return grandchild;
            }
        }

        private static Control AnswerControl(Control root, string questionId) => Descendants(root).First(c => (c is ComboBox || c is TextBox) && (c.Tag as string) == questionId);

        private static TextBox ReviewerNameBox(Control root) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == "ReviewerDisplayName");
        private static TextBox ReviewerRoleBox(Control root) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == "ReviewerRole");
        private static TextBox CommentBox(Control root) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == "EngineerComment");

        private static Label StatusBadge(Control root)
        {
            string[] statuses = { ProjectEngineerReviewStatuses.Pending, ProjectEngineerReviewStatuses.Completed, ProjectEngineerReviewStatuses.Invalid };
            return Descendants(root).OfType<Label>().First(l => statuses.Any(s => l.Text.EndsWith(" " + s, StringComparison.Ordinal)));
        }

        private static Label CaseLabel(Control root) => Descendants(root).OfType<Label>().First(l => l.Text.StartsWith("Case ", StringComparison.Ordinal));

        private static Label Message(Control root) => Descendants(root).OfType<Label>().First(l => l.Parent is TableLayoutPanel grid && grid.ColumnCount == 5);

        private static List<Label> HashLabels(Control root) => Descendants(root).OfType<Label>().Where(l => l.Text.Length == 64 && l.Text.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))).ToList();

        private static string ToolTipText(Control control, Control target)
        {
            FieldInfo field = typeof(EngiSentry.AutoCAD2021.ProjectEngineerReviewControl).GetField("toolTip", BindingFlags.NonPublic | BindingFlags.Instance);
            return (field?.GetValue(control) as ToolTip)?.GetToolTip(target);
        }

        private static void PerformLayout(Control control)
        {
            control.PerformLayout();
            foreach (Control child in Descendants(control))
                if (child.Controls.Count > 0) child.PerformLayout();
        }

        private static bool NoSiblingOverlaps(Control control)
        {
            foreach (Control container in new[] { control }.Concat(Descendants(control)))
            {
                List<Control> siblings = container.Controls.Cast<Control>().Where(c => c.Width > 0 && c.Height > 0).ToList();
                for (int i = 0; i < siblings.Count; i++)
                    for (int j = i + 1; j < siblings.Count; j++)
                        if (siblings[i].Bounds.IntersectsWith(siblings[j].Bounds)) return false;
            }
            return true;
        }

        private static void CollectionAssert(IList<string> actual, IList<string> expected, string message) { Assert(actual.Count == expected.Count && actual.SequenceEqual(expected, StringComparer.Ordinal), message); }
        private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    }
}
