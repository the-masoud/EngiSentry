#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    internal sealed class ProjectEngineerReviewControl : UserControl
    {
        private readonly ProjectEngineerReviewService service;
        private readonly TextBox reviewerName = new TextBox();
        private readonly TextBox reviewerRole = new TextBox();
        private readonly Label caseSummary = new Label();
        private readonly TextBox evidence = new TextBox();
        private readonly Panel questionsPanel = new Panel();
        private readonly ComboBox classification = new ComboBox();
        private readonly TextBox comment = new TextBox();
        private readonly Button previous = new Button();
        private readonly Button next = new Button();
        private readonly Button nextPending = new Button();
        private readonly Button save = new Button();
        private readonly Label message = new Label();
        private List<ProjectEngineerReviewRequest> queue = new List<ProjectEngineerReviewRequest>();
        private int currentIndex = -1;
        private bool loading;
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        internal ProjectEngineerReviewControl() : this(new ProjectEngineerReviewService()) { }

        internal ProjectEngineerReviewControl(ProjectEngineerReviewService reviewService)
        {
            service = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
            Dock = DockStyle.Fill;
            BuildLayout();
            LoadQueue();
        }

        internal int QueueCount => queue.Count;

        internal void LoadQueue()
        {
            loading = true;
            queue = service.LoadQueue();
            loading = false;
            if (queue.Count > 0) ShowCase(0); else ShowCase(-1);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(new Label { Text = "Project Engineer Review", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, 0, 0);
            header.SetColumnSpan(header.GetControlFromPosition(0, 0), 2);
            var reviewerRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            for (int i = 0; i < 4; i++) reviewerRow.ColumnStyles.Add(i % 2 == 0 ? new ColumnStyle(SizeType.Absolute, 110) : new ColumnStyle(SizeType.Percent, 50));
            reviewerRow.Controls.Add(new Label { Text = "Reviewer name", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            reviewerName.Dock = DockStyle.Fill; reviewerName.MaxLength = 120;
            reviewerRow.Controls.Add(reviewerName, 1, 0);
            reviewerRow.Controls.Add(new Label { Text = "Role / title", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            reviewerRole.Dock = DockStyle.Fill; reviewerRole.MaxLength = 120;
            reviewerRow.Controls.Add(reviewerRole, 3, 0);
            header.Controls.Add(reviewerRow, 0, 1); header.SetColumnSpan(reviewerRow, 2);

            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            caseSummary.Dock = DockStyle.Fill; caseSummary.AutoEllipsis = true; caseSummary.TextAlign = ContentAlignment.TopLeft;
            summary.Controls.Add(caseSummary, 0, 0);
            evidence.Dock = DockStyle.Fill; evidence.Multiline = true; evidence.ReadOnly = true; evidence.ScrollBars = ScrollBars.Vertical; evidence.BackColor = SystemColors.Window;
            summary.Controls.Add(evidence, 0, 1);
            root.Controls.Add(header, 0, 0); root.Controls.Add(summary, 0, 1);

            var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            middle.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); middle.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            middle.Controls.Add(new Label { Text = "Questions (all required answers must be completed before Save)", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            questionsPanel.Dock = DockStyle.Fill; questionsPanel.AutoScroll = true; questionsPanel.BackColor = SystemColors.Window;
            middle.Controls.Add(questionsPanel, 0, 1);
            var tail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            tail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); tail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tail.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); tail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tail.Controls.Add(new Label { Text = "Classification", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            classification.Dock = DockStyle.Fill; classification.DropDownStyle = ComboBoxStyle.DropDownList; classification.Items.Add(string.Empty); classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray());
            tail.Controls.Add(classification, 1, 0);
            tail.Controls.Add(new Label { Text = "Comment", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            comment.Dock = DockStyle.Fill; comment.Multiline = true; comment.ScrollBars = ScrollBars.Vertical; comment.MaxLength = 1000;
            tail.Controls.Add(comment, 1, 1);
            middle.Controls.Add(tail, 0, 2);
            root.Controls.Add(middle, 0, 2);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            previous.Text = "Previous"; next.Text = "Next"; nextPending.Text = "Next Pending"; save.Text = "Save";
            previous.Click += (s, e) => Navigate(-1); next.Click += (s, e) => Navigate(1); nextPending.Click += (s, e) => NavigatePending(); save.Click += Save;
            foreach (Button button in new[] { previous, next, nextPending, save }) button.Dock = DockStyle.Fill;
            footer.Controls.Add(previous, 0, 0); footer.Controls.Add(next, 1, 0); footer.Controls.Add(nextPending, 2, 0); footer.Controls.Add(save, 4, 0);
            message.Dock = DockStyle.Fill; message.AutoEllipsis = true; message.TextAlign = ContentAlignment.MiddleLeft; footer.Controls.Add(message, 0, 1); footer.SetColumnSpan(message, 5);
            root.Controls.Add(footer, 0, 3); Controls.Add(root);
        }

        private void ShowCase(int index)
        {
            currentIndex = index; loading = true; answerControls.Clear(); questionsPanel.Controls.Clear();
            classification.SelectedIndex = 0; comment.Text = string.Empty;
            if (index < 0 || index >= queue.Count)
            {
                caseSummary.Text = queue.Count == 0 ? "No reviewable cases found." : "No case selected.";
                evidence.Text = string.Empty; save.Enabled = false; previous.Enabled = false; next.Enabled = false; nextPending.Enabled = false;
                loading = false; return;
            }
            ProjectEngineerReviewRequest request = queue[index];
            string status = service.ResolveStatus(request);
            caseSummary.Text = "Case " + (index + 1) + " / " + queue.Count + "  |  " + status + "  |  " + request.ReviewedCaseId + Environment.NewLine
                + "Drawing: " + request.DrawingName + "  |  Operational result: " + (request.SoftwareResult ?? "<not recorded>") + "  |  " + request.ReviewReason;
            evidence.Text = BuildEvidence(request);
            BuildQuestions(request);
            ProjectEngineerReviewResponse existing = service.LoadResponse(request.ReviewRequestId);
            if (existing != null) ApplyResponse(existing); else ApplyPendingAnswers(request);
            previous.Enabled = index > 0; next.Enabled = index < queue.Count - 1; nextPending.Enabled = queue.Any(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending);
            save.Enabled = status != ProjectEngineerReviewStatuses.Completed;
            message.Text = status == ProjectEngineerReviewStatuses.Completed ? "This case is already completed." : status == ProjectEngineerReviewStatuses.Invalid ? "Stored response is invalid; saving will replace it." : string.Empty;
            loading = false;
        }

        private static string BuildEvidence(ProjectEngineerReviewRequest request)
        {
            var text = new StringBuilder();
            text.AppendLine("IDENTITY");
            text.AppendLine("ReviewRequestId: " + request.ReviewRequestId);
            text.AppendLine("RequestSha256: " + request.RequestSha256);
            text.AppendLine("WorkflowSource: " + request.WorkflowSourceType);
            text.AppendLine("ReviewedCaseId: " + request.ReviewedCaseId);
            text.AppendLine("Drawing: " + request.DrawingName);
            if (!string.IsNullOrWhiteSpace(request.DrawingSha256)) text.AppendLine("DrawingSha256: " + request.DrawingSha256);
            text.AppendLine("SoftwareResult: " + (request.SoftwareResult ?? "<unavailable>"));
            text.AppendLine("SoftwareState: " + (request.SoftwareState ?? "<unavailable>"));
            text.AppendLine("EVIDENCE");
            foreach (ProjectEngineerReviewEvidenceReference reference in request.EvidenceReferences ?? new List<ProjectEngineerReviewEvidenceReference>())
                if (!string.IsNullOrWhiteSpace(reference.Value)) text.AppendLine(reference.Kind + " | " + reference.Label + ": " + reference.Value);
            return text.ToString();
        }

        private void BuildQuestions(ProjectEngineerReviewRequest request)
        {
            questionsPanel.SuspendLayout(); questionsPanel.Controls.Clear();
            int y = 6; string currentSection = null;
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!string.Equals(currentSection, question.Section, StringComparison.Ordinal))
                {
                    currentSection = question.Section;
                    var sectionLabel = new Label { Text = currentSection, Left = 6, Top = y, Width = questionsPanel.Width - 24, Height = 20, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
                    questionsPanel.Controls.Add(sectionLabel); y += sectionLabel.Height + 2;
                }
                var label = new Label { Text = question.Text + (question.Required ? " *" : string.Empty), Left = 6, Top = y + 3, Width = 330, Height = 20, AutoEllipsis = true };
                questionsPanel.Controls.Add(label);
                Control answer;
                if (question.AnswerKind == ProjectEngineerAnswerKinds.FreeText)
                {
                    var box = new TextBox { Left = 342, Top = y, Width = Math.Max(200, questionsPanel.Width - 360), Height = 22, MaxLength = 500, Tag = question.QuestionId };
                    box.TextChanged += (s, e) => { if (!loading) message.Text = string.Empty; };
                    answer = box;
                }
                else
                {
                    var combo = new ComboBox { Left = 342, Top = y, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, Tag = question.QuestionId };
                    combo.Items.Add(string.Empty);
                    foreach (string value in ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)) combo.Items.Add(value);
                    combo.SelectedIndex = 0;
                    combo.SelectedIndexChanged += (s, e) => { if (!loading) message.Text = string.Empty; };
                    answer = combo;
                }
                questionsPanel.Controls.Add(answer); answerControls[question.QuestionId] = answer;
                y += answer.Height + 4;
            }
            questionsPanel.ResumeLayout();
        }

        private Dictionary<string, string> CaptureAnswers()
        {
            var answers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Control> pair in answerControls)
            {
                if (pair.Value is ComboBox combo) answers[pair.Key] = combo.SelectedIndex > 0 ? (string)combo.SelectedItem : string.Empty;
                else answers[pair.Key] = pair.Value.Text;
            }
            return answers;
        }

        private void ApplyPendingAnswers(ProjectEngineerReviewRequest request)
        {
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!answerControls.TryGetValue(question.QuestionId, out Control control)) continue;
                if (control is ComboBox combo) combo.SelectedIndex = 0; else control.Text = string.Empty;
            }
        }

        private void ApplyResponse(ProjectEngineerReviewResponse response)
        {
            reviewerName.Text = response.ReviewerDisplayName;
            reviewerRole.Text = response.ReviewerRole;
            classification.SelectedItem = response.Classification as string ?? string.Empty;
            if (classification.SelectedIndex < 0) classification.SelectedIndex = 0;
            comment.Text = response.EngineerComment ?? string.Empty;
            foreach (ProjectEngineerReviewAnswer answer in response.Answers ?? new List<ProjectEngineerReviewAnswer>())
            {
                if (!answerControls.TryGetValue(answer.QuestionId, out Control control)) continue;
                if (control is ComboBox combo) combo.SelectedItem = answer.AnswerText as string ?? string.Empty; else control.Text = answer.AnswerText ?? string.Empty;
                if (control is ComboBox c && c.SelectedIndex < 0) c.SelectedIndex = 0;
            }
        }

        private void Navigate(int delta)
        {
            if (queue.Count == 0) return;
            int target = Math.Max(0, Math.Min(queue.Count - 1, currentIndex + delta));
            ShowCase(target);
        }

        private void NavigatePending()
        {
            for (int offset = 1; offset <= queue.Count; offset++)
            {
                int target = (currentIndex + offset) % queue.Count;
                if (service.ResolveStatus(queue[target]) == ProjectEngineerReviewStatuses.Pending) { ShowCase(target); return; }
            }
            message.Text = "No pending cases remain.";
        }

        private void Save(object sender, EventArgs e)
        {
            if (currentIndex < 0 || currentIndex >= queue.Count) return;
            ProjectEngineerReviewRequest request = queue[currentIndex];
            Dictionary<string, string> answers = CaptureAnswers();
            string selectedClassification = classification.SelectedItem as string ?? string.Empty;
            string incomplete = ProjectEngineerReviewService.FindIncompleteReason(request, reviewerName.Text, reviewerRole.Text, answers, selectedClassification);
            if (incomplete != null) { message.Text = "Review not saved — " + incomplete; return; }
            var response = new ProjectEngineerReviewResponse
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256,
                DrawingName = request.DrawingName,
                ReviewedCaseId = request.ReviewedCaseId,
                ReviewerDisplayName = reviewerName.Text.Trim(),
                ReviewerRole = reviewerRole.Text.Trim(),
                ResponseTimestampUtc = DateTime.UtcNow,
                Answers = answers.Select(x => new ProjectEngineerReviewAnswer { QuestionId = x.Key, AnswerText = x.Value }).ToList(),
                EngineerComment = string.IsNullOrEmpty(comment.Text) ? null : comment.Text,
                Classification = selectedClassification
            };
            try
            {
                service.SaveResponse(request, response);
                message.Text = "Project engineer review saved for " + request.ReviewedCaseId + ".";
                ShowCase(currentIndex);
            }
            catch (Exception exception) { message.Text = "Save failed: " + exception.Message; }
        }
    }
}
