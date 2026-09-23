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
    // Visual language is deliberately shared with the accepted dark EngiSentry Results UI:
    // same background/surface/card/muted palette, same Segoe UI + Segoe UI Semibold typography,
    // same flat dark buttons, and system-default (high-contrast) input fields for readability.
    internal sealed class ProjectEngineerReviewControl : UserControl
    {
        private const int HeaderHeight = 64;
        private const int IdentityHeight = 88;
        private const int SummarySectionHeight = 86;
        // guaranteed height for the questions area: it holds the primary working surface, so it
        // never shrinks below a few visible rows even when the host window is short.
        private const int QuestionsSectionHeight = 330;
        private const int TailHeight = 128;
        private const int FooterHeight = 72;
        private const int AnswerColumnWidth = 210;
        private const int EvidenceKeyWidth = 152;
        private const int SummaryKeyWidth = 118;
        private const int IdentityCaptionWidth = 140;
        private const int QuestionRowHeight = 26;
        private const int SectionHeaderHeight = 24;
        private const int EvidenceRowHeight = 22;

        private readonly ProjectEngineerReviewService service;
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 8000 };
        private readonly Label titleLabel = new Label();
        private readonly Label statusBadge = new Label();
        private readonly Label caseLabel = new Label();
        private readonly TextBox reviewerName = new TextBox();
        private readonly TextBox reviewerRole = new TextBox();
        private readonly Panel summaryBody = new Panel();
        private readonly Panel evidenceBody = new Panel();
        private readonly Panel questionsBody = new Panel();
        private readonly Label requiredNote = new Label();
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
            BackColor = ReviewUi.Background;
            ForeColor = ReviewUi.Text;
            Font = ReviewUi.BodyFont;
            DoubleBuffered = true;
            // the whole palette scrolls when the host window is shorter than the sections need,
            // so the footer is never lost when the window is made small
            AutoScroll = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
                Padding = new Padding(12), BackColor = ReviewUi.Background, Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, IdentityHeight));
            // the middle (summary + evidence) absorbs growth and shrinkage; the questions section
            // below keeps a guaranteed usable height so it never collapses to an unusable sliver
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, QuestionsSectionHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, TailHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));

            BuildHeader(root);
            BuildIdentity(root);
            BuildSummaryAndEvidence(root);
            BuildQuestions(root);
            BuildTail(root);
            BuildFooter(root);
            Controls.Add(root);
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Padding = new Padding(12, 6, 12, 6) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            titleLabel.Text = "Project Engineer Review";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Font = ReviewUi.TitleFont;
            titleLabel.ForeColor = Color.White;
            titleLabel.AutoEllipsis = true;
            toolTip.SetToolTip(titleLabel, titleLabel.Text);
            grid.Controls.Add(titleLabel, 0, 0);
            grid.SetRowSpan(titleLabel, 2);

            statusBadge.Dock = DockStyle.Fill;
            statusBadge.TextAlign = ContentAlignment.MiddleRight;
            statusBadge.Font = ReviewUi.BadgeFont;
            statusBadge.ForeColor = ReviewUi.Pending;
            statusBadge.BackColor = ReviewUi.Surface;
            statusBadge.BorderStyle = BorderStyle.FixedSingle;
            statusBadge.Text = "● PENDING";
            grid.Controls.Add(statusBadge, 1, 0);

            caseLabel.Dock = DockStyle.Fill;
            caseLabel.TextAlign = ContentAlignment.MiddleRight;
            caseLabel.Font = ReviewUi.BodyFont;
            caseLabel.ForeColor = ReviewUi.Muted;
            caseLabel.AutoEllipsis = true;
            grid.Controls.Add(caseLabel, 1, 1);

            host.Controls.Add(grid);
            root.Controls.Add(host, 0, 0);
        }

        private void BuildIdentity(TableLayoutPanel root)
        {
            Panel body;
            Control section = CreateSection("Engineer Identity", out body);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            reviewerName.Dock = DockStyle.Fill;
            reviewerName.MaxLength = 120;
            reviewerName.Font = ReviewUi.BodyFont;
            reviewerName.AccessibleName = "ReviewerDisplayName";
            reviewerName.TabIndex = 1;
            toolTip.SetToolTip(reviewerName, "Name as the reviewer wishes it recorded in the review evidence.");
            AddField(grid, 0, 0, "Reviewer / Display Name", reviewerName);

            reviewerRole.Dock = DockStyle.Fill;
            reviewerRole.MaxLength = 120;
            reviewerRole.Font = ReviewUi.BodyFont;
            reviewerRole.AccessibleName = "ReviewerRole";
            reviewerRole.TabIndex = 2;
            toolTip.SetToolTip(reviewerRole, "Role or title as the reviewer wishes it recorded in the review evidence.");
            AddField(grid, 1, 0, "Role / Title", reviewerRole);

            var note = new Label
            {
                Text = "Recorded as review evidence only — this is not identity verification.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            grid.Controls.Add(note, 0, 1);
            grid.SetColumnSpan(note, 2);

            body.Controls.Add(grid);
            root.Controls.Add(section, 0, 1);
        }

        private void BuildSummaryAndEvidence(TableLayoutPanel root)
        {
            // Summary and evidence are stacked full-width rather than side by side: the evidence
            // values are long (64-hex hashes) and the summary values read best with the full row,
            // so a narrow side column would collapse the value column to a few pixels.
            var middle = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Background, Margin = new Padding(0) };

            Panel evidenceHostBody;
            Control evidenceSection = CreateSection("Software Evidence", out evidenceHostBody);
            evidenceSection.Dock = DockStyle.Fill;
            evidenceBody.Dock = DockStyle.Fill;
            evidenceBody.AutoScroll = true;
            evidenceBody.BackColor = ReviewUi.Surface;
            evidenceHostBody.Controls.Add(evidenceBody);

            Panel summaryHostBody;
            Control summarySection = CreateSection("Case Summary", out summaryHostBody);
            summarySection.Dock = DockStyle.Top;
            summarySection.Height = SummarySectionHeight;
            summaryBody.Dock = DockStyle.Fill;
            summaryBody.BackColor = ReviewUi.Surface;
            summaryHostBody.Controls.Add(summaryBody);

            // docking runs last-added first, so adding the fill host first lets the summary claim
            // the top edge and the evidence host fills the remainder
            middle.Controls.Add(evidenceSection);
            middle.Controls.Add(summarySection);
            root.Controls.Add(middle, 0, 2);
        }

        private void BuildQuestions(TableLayoutPanel root)
        {
            Panel body;
            Control section = CreateSection("Review Questions", out body);
            requiredNote.Text = "Answer every required question (marked *). Nothing is stored until you choose Save Review.";
            requiredNote.Dock = DockStyle.Top;
            requiredNote.Height = 15;
            requiredNote.TextAlign = ContentAlignment.MiddleLeft;
            requiredNote.Font = ReviewUi.CaptionFont;
            requiredNote.ForeColor = ReviewUi.Muted;
            requiredNote.AutoEllipsis = true;
            questionsBody.Dock = DockStyle.Fill;
            questionsBody.AutoScroll = true;
            questionsBody.BackColor = ReviewUi.Surface;
            body.Controls.Add(questionsBody);
            body.Controls.Add(requiredNote);
            root.Controls.Add(section, 0, 3);
        }

        private void BuildTail(TableLayoutPanel root)
        {
            var tail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Background, Margin = new Padding(0) };
            tail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 316));
            tail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            Panel classificationBody;
            Control classificationSection = CreateSection("Classification", out classificationBody);
            var classificationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            classificationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            classificationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            classification.Dock = DockStyle.Fill;
            classification.DropDownStyle = ComboBoxStyle.DropDownList;
            classification.Font = ReviewUi.BodyFont;
            classification.Items.Add(string.Empty);
            classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray());
            classification.SelectedIndex = 0;
            classification.AccessibleName = "Classification";
            classification.TabIndex = 20;
            toolTip.SetToolTip(classification, "Exactly one classification is required before saving.");
            classificationLayout.Controls.Add(classification, 0, 0);
            var classificationNote = new Label
            {
                Text = "Required — choose the one classification that fits this case.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            classificationLayout.Controls.Add(classificationNote, 0, 1);
            classificationBody.Controls.Add(classificationLayout);
            tail.Controls.Add(classificationSection, 0, 0);

            Panel commentBody;
            Control commentSection = CreateSection("Engineer Comment", out commentBody);
            var commentNote = new Label
            {
                Text = "Optional — up to 1000 characters.",
                Dock = DockStyle.Top, Height = 15, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            comment.Dock = DockStyle.Fill;
            comment.Multiline = true;
            comment.ScrollBars = ScrollBars.Vertical;
            comment.MaxLength = 1000;
            comment.Font = ReviewUi.BodyFont;
            comment.AccessibleName = "EngineerComment";
            comment.TabIndex = 21;
            commentBody.Controls.Add(comment);
            commentBody.Controls.Add(commentNote);
            tail.Controls.Add(commentSection, 1, 0);

            root.Controls.Add(tail, 0, 4);
        }

        private void BuildFooter(TableLayoutPanel root)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Padding = new Padding(10, 6, 10, 6) };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            previous.Text = "Previous";
            next.Text = "Next";
            nextPending.Text = "Next Pending";
            save.Text = "Save Review";
            previous.Click += (s, e) => Navigate(-1);
            next.Click += (s, e) => Navigate(1);
            nextPending.Click += (s, e) => NavigatePending();
            save.Click += Save;
            previous.AccessibleName = previous.Text;
            next.AccessibleName = next.Text;
            nextPending.AccessibleName = nextPending.Text;
            save.AccessibleName = save.Text;
            previous.TabIndex = 30;
            next.TabIndex = 31;
            nextPending.TabIndex = 32;
            save.TabIndex = 33;
            // docking each button inside its cell makes the primary Save cell visibly the widest,
            // and gives every action a consistent, comfortable hit area
            foreach (Button button in new[] { previous, next, nextPending, save }) button.Dock = DockStyle.Fill;
            foreach (Button button in new[] { previous, next, nextPending }) StyleButton(button, false);
            StyleButton(save, true);
            toolTip.SetToolTip(previous, "Show the previous case in the review queue.");
            toolTip.SetToolTip(next, "Show the next case in the review queue.");
            toolTip.SetToolTip(nextPending, "Jump to the next case that still has no recorded response.");
            toolTip.SetToolTip(save, "Explicitly save this review. It is never saved automatically.");
            grid.Controls.Add(previous, 0, 0);
            grid.Controls.Add(next, 1, 0);
            grid.Controls.Add(nextPending, 2, 0);
            grid.Controls.Add(save, 4, 0);

            message.Dock = DockStyle.Fill;
            message.AutoEllipsis = true;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Font = ReviewUi.BodyFont;
            message.ForeColor = ReviewUi.Value;
            message.Text = string.Empty;
            message.TabIndex = 34;
            grid.Controls.Add(message, 0, 1);
            grid.SetColumnSpan(message, 5);

            host.Controls.Add(grid);
            root.Controls.Add(host, 0, 5);
        }

        private static Control CreateSection(string title, out Panel body)
        {
            var section = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = ReviewUi.Surface, Padding = new Padding(10, 6, 10, 7), Margin = new Padding(0, 0, 0, 6)
            };
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var head = new Label
            {
                Text = title.ToUpperInvariant(),
                Dock = DockStyle.Fill, Font = ReviewUi.SectionFont, ForeColor = ReviewUi.Muted,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0)
            };
            body = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface };
            section.Controls.Add(head, 0, 0);
            section.Controls.Add(body, 0, 1);
            return section;
        }

        private static void AddField(TableLayoutPanel grid, int column, int row, string caption, Control control)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 2, 8, 2) };
            var label = new Label
            {
                Text = caption, Dock = DockStyle.Left, Width = IdentityCaptionWidth,
                ForeColor = ReviewUi.Caption, Font = ReviewUi.BodyFont,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            control.Dock = DockStyle.Fill;
            host.Controls.Add(control);
            host.Controls.Add(label);
            grid.Controls.Add(host, column, row);
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Font = ReviewUi.ButtonFont;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.BorderColor = ReviewUi.Border;
            button.FlatAppearance.BorderSize = 1;
            ApplyButtonState(button, primary);
            button.EnabledChanged += (sender, args) => ApplyButtonState(button, primary);
        }

        private static void ApplyButtonState(Button button, bool primary)
        {
            if (button.Enabled)
            {
                button.BackColor = primary ? ReviewUi.Primary : ReviewUi.Card;
                button.ForeColor = Color.White;
            }
            else
            {
                button.BackColor = ReviewUi.Surface;
                button.ForeColor = ReviewUi.DisabledText;
            }
        }

        private void ShowCase(int index)
        {
            currentIndex = index;
            loading = true;
            answerControls.Clear();
            classification.SelectedIndex = 0;
            comment.Text = string.Empty;
            if (index < 0 || index >= queue.Count)
            {
                UpdateHeader(-1, null);
                ClearControls(summaryBody);
                ClearControls(evidenceBody);
                ClearControls(questionsBody);
                var empty = new Label
                {
                    Text = queue.Count == 0 ? "No reviewable cases found." : "No case selected.",
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    Font = ReviewUi.BodyFont, ForeColor = ReviewUi.Muted
                };
                summaryBody.Controls.Add(empty);
                save.Enabled = false;
                previous.Enabled = false;
                next.Enabled = false;
                nextPending.Enabled = false;
                message.Text = string.Empty;
                loading = false;
                return;
            }
            ProjectEngineerReviewRequest request = queue[index];
            string status = service.ResolveStatus(request);
            UpdateHeader(index, status);
            BuildSummary(request);
            BuildEvidence(request);
            BuildQuestions(request);
            ProjectEngineerReviewResponse existing = service.LoadResponse(request.ReviewRequestId);
            if (existing != null) ApplyResponse(existing); else ApplyPendingAnswers(request);
            previous.Enabled = index > 0;
            next.Enabled = index < queue.Count - 1;
            nextPending.Enabled = queue.Any(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending);
            save.Enabled = status != ProjectEngineerReviewStatuses.Completed;
            message.Text = status == ProjectEngineerReviewStatuses.Completed
                ? "This case is already completed — the recorded review is shown."
                : status == ProjectEngineerReviewStatuses.Invalid
                    ? "The stored response is invalid; saving will replace it."
                    : string.Empty;
            loading = false;
        }

        private void UpdateHeader(int index, string status)
        {
            if (index < 0 || index >= queue.Count)
            {
                statusBadge.Text = "● NONE";
                statusBadge.ForeColor = ReviewUi.Muted;
                caseLabel.Text = queue.Count == 0 ? "No reviewable cases found." : "No case selected.";
                toolTip.SetToolTip(statusBadge, "There is no case to display.");
                return;
            }
            caseLabel.Text = "Case " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + queue.Count.ToString(CultureInfo.InvariantCulture);
            statusBadge.Text = StatusGlyph(status) + " " + status;
            statusBadge.ForeColor = StatusColor(status);
            string detail = status == ProjectEngineerReviewStatuses.Completed
                ? "A complete, binding-valid engineer response is recorded for this case."
                : status == ProjectEngineerReviewStatuses.Invalid
                    ? "A stored response failed validation; a new save will replace it."
                    : "No engineer response is recorded yet for this case.";
            toolTip.SetToolTip(statusBadge, detail);
        }

        private static string StatusGlyph(string status)
        {
            if (status == ProjectEngineerReviewStatuses.Completed) return "✓";
            if (status == ProjectEngineerReviewStatuses.Invalid) return "✕";
            return "●";
        }

        private static Color StatusColor(string status)
        {
            if (status == ProjectEngineerReviewStatuses.Completed) return ReviewUi.Completed;
            if (status == ProjectEngineerReviewStatuses.Invalid) return ReviewUi.Invalid;
            return ReviewUi.Pending;
        }

        private void BuildSummary(ProjectEngineerReviewRequest request)
        {
            summaryBody.SuspendLayout();
            ClearControls(summaryBody);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            AddKeyValue(summaryBody, grid, 0, 0, "Drawing", request.DrawingName);
            AddKeyValue(summaryBody, grid, 1, 0, "Case", request.ReviewedCaseId);
            AddKeyValue(summaryBody, grid, 0, 1, "Operational result", request.SoftwareResult ?? "<not recorded>");
            AddKeyValue(summaryBody, grid, 1, 1, "Review reason", request.ReviewReason);
            summaryBody.Controls.Add(grid);
            summaryBody.ResumeLayout();
        }

        private void AddKeyValue(Panel owner, TableLayoutPanel grid, int column, int row, string key, string value)
        {
            var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0, 2, 10, 2) };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SummaryKeyWidth));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var keyLabel = new Label
            {
                Text = key.ToUpperInvariant(), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            var valueLabel = new Label
            {
                Text = value ?? string.Empty, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.ValueFont, ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            if (!string.IsNullOrEmpty(value)) toolTip.SetToolTip(valueLabel, value);
            pair.Controls.Add(keyLabel, 0, 0);
            pair.Controls.Add(valueLabel, 1, 0);
            grid.Controls.Add(pair, column, row);
        }

        private void BuildEvidence(ProjectEngineerReviewRequest request)
        {
            evidenceBody.SuspendLayout();
            ClearControls(evidenceBody);
            var grid = new TableLayoutPanel
            {
                // single full-width column: each row is a key/value pair panel, so the row must
                // span the whole grid width or the pair's value column collapses to a pixel.
                // Explicit height (AutoSize would shrink the width to the key column); docking
                // supplies the full panel width.
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0, BackColor = ReviewUi.Surface, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddEvidenceRow(grid, "Review request ID", request.ReviewRequestId);
            AddEvidenceRow(grid, "Request SHA-256", request.RequestSha256);
            AddEvidenceRow(grid, "Workflow source", request.WorkflowSourceType);
            AddEvidenceRow(grid, "Reviewed case", request.ReviewedCaseId);
            AddEvidenceRow(grid, "Drawing", request.DrawingName);
            if (!string.IsNullOrWhiteSpace(request.DrawingSha256)) AddEvidenceRow(grid, "Drawing SHA-256", request.DrawingSha256);
            AddEvidenceRow(grid, "Software result", request.SoftwareResult);
            AddEvidenceRow(grid, "Software state", request.SoftwareState);
            foreach (ProjectEngineerReviewEvidenceReference reference in request.EvidenceReferences ?? new List<ProjectEngineerReviewEvidenceReference>())
            {
                if (string.IsNullOrWhiteSpace(reference.Value)) continue;
                string key = !string.IsNullOrWhiteSpace(reference.Label) ? reference.Label : reference.Kind;
                AddEvidenceRow(grid, key, reference.Value);
            }
            evidenceBody.Controls.Add(grid);
            grid.Height = (grid.RowCount * EvidenceRowHeight) + 2;
            evidenceBody.ResumeLayout();
        }

        private void AddEvidenceRow(TableLayoutPanel grid, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, EvidenceRowHeight));
            var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, EvidenceKeyWidth));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var keyLabel = new Label
            {
                Text = key, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            var valueLabel = new Label
            {
                Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.ValueFont, ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            toolTip.SetToolTip(keyLabel, key);
            toolTip.SetToolTip(valueLabel, value);
            pair.Controls.Add(keyLabel, 0, 0);
            pair.Controls.Add(valueLabel, 1, 0);
            grid.Controls.Add(pair, 0, grid.RowCount - 1);
        }

        private void BuildQuestions(ProjectEngineerReviewRequest request)
        {
            questionsBody.SuspendLayout();
            ClearControls(questionsBody);
            var grid = new TableLayoutPanel
            {
                // explicit height (AutoSize would pin the grid to its preferred width and break
                // horizontal resize); docking supplies the full panel width so the question label
                // column absorbs growth and shrinkage
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, BackColor = ReviewUi.Surface, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AnswerColumnWidth));
            string currentSection = null;
            int tabIndex = 10;
            int sectionCount = 0;
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!string.Equals(currentSection, question.Section, StringComparison.Ordinal))
                {
                    currentSection = question.Section;
                    AddQuestionSectionHeader(grid, currentSection);
                    sectionCount++;
                }
                AddQuestionRow(grid, question, ref tabIndex);
            }
            questionsBody.Controls.Add(grid);
            grid.Height = (sectionCount * SectionHeaderHeight) + ((grid.RowCount - sectionCount) * QuestionRowHeight);
            questionsBody.ResumeLayout();
        }

        private void AddQuestionSectionHeader(TableLayoutPanel grid, string section)
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, SectionHeaderHeight));
            var strip = new Label
            {
                Text = section.ToUpperInvariant(),
                Dock = DockStyle.Fill, BackColor = ReviewUi.Card, ForeColor = ReviewUi.Muted,
                Font = ReviewUi.SectionFont, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true
            };
            grid.Controls.Add(strip, 0, grid.RowCount - 1);
            grid.SetColumnSpan(strip, 2);
        }

        private void AddQuestionRow(TableLayoutPanel grid, ProjectEngineerReviewQuestionDefinition question, ref int tabIndex)
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, QuestionRowHeight));
            string displayText = question.Text + (question.Required ? " *" : string.Empty);
            var label = new Label
            {
                Text = displayText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.BodyFont, ForeColor = ReviewUi.Value, AutoEllipsis = true,
                Margin = new Padding(2, 0, 8, 0)
            };
            toolTip.SetToolTip(label, question.Text);
            grid.Controls.Add(label, 0, grid.RowCount - 1);
            Control answer;
            if (question.AnswerKind == ProjectEngineerAnswerKinds.FreeText)
            {
                var box = new TextBox
                {
                    Dock = DockStyle.Fill, MaxLength = 500, Tag = question.QuestionId,
                    Font = ReviewUi.BodyFont, Margin = new Padding(0, 2, 2, 2),
                    AccessibleName = question.QuestionId, TabIndex = tabIndex++
                };
                box.TextChanged += (s, e) => { if (!loading) message.Text = string.Empty; };
                answer = box;
            }
            else
            {
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Tag = question.QuestionId,
                    Font = ReviewUi.BodyFont, Margin = new Padding(0, 2, 2, 2),
                    AccessibleName = question.QuestionId, TabIndex = tabIndex++
                };
                combo.Items.Add(string.Empty);
                foreach (string value in ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)) combo.Items.Add(value);
                combo.SelectedIndex = 0;
                combo.SelectedIndexChanged += (s, e) => { if (!loading) message.Text = string.Empty; };
                answer = combo;
            }
            grid.Controls.Add(answer, 1, grid.RowCount - 1);
            answerControls[question.QuestionId] = answer;
        }

        private static void ClearControls(Panel panel)
        {
            List<Control> children = panel.Controls.Cast<Control>().ToList();
            panel.Controls.Clear();
            foreach (Control child in children) child.Dispose();
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
                ShowCase(currentIndex);
                message.Text = "Project engineer review saved for " + request.ReviewedCaseId + ".";
            }
            catch (Exception exception) { message.Text = "Save failed: " + exception.Message; }
        }

        private static class ReviewUi
        {
            internal static readonly Color Background = Color.FromArgb(30, 31, 34);
            internal static readonly Color Surface = Color.FromArgb(38, 40, 44);
            internal static readonly Color Card = Color.FromArgb(43, 45, 50);
            internal static readonly Color Text = Color.FromArgb(235, 237, 240);
            internal static readonly Color Value = Color.FromArgb(225, 228, 232);
            internal static readonly Color Muted = Color.FromArgb(160, 166, 175);
            internal static readonly Color Caption = Color.FromArgb(168, 174, 184);
            internal static readonly Color Border = Color.FromArgb(80, 84, 92);
            internal static readonly Color Primary = Color.FromArgb(0, 105, 170);
            internal static readonly Color Pending = Color.FromArgb(255, 190, 92);
            internal static readonly Color Completed = Color.FromArgb(108, 220, 155);
            internal static readonly Color Invalid = Color.FromArgb(255, 112, 112);
            internal static readonly Color DisabledText = Color.FromArgb(120, 125, 132);

            internal static readonly Font TitleFont = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font BadgeFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font SectionFont = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font CaptionFont = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font BodyFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font ButtonFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font ValueFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }
    }
}
