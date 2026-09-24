#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    // Visual language is deliberately shared with the accepted dark EngiSentry Results UI:
    // same background/surface/card/muted palette and the same Segoe UI + Segoe UI Semibold
    // typography, presented as a pinned app-style header, a single scrollable column of
    // bordered cards, and a pinned action bar with the primary Save Review action.
    //
    // Presentation only: this layer never changes the review questions, answer vocabularies,
    // classifications, persistence, governance, authority behavior, or review semantics. The
    // queue stays exactly the four governed P10.3 pilot cases, a review is still only stored on
    // an explicit Save Review, and every derived indicator below (remaining-answer counter,
    // queue progress, character count) is recomputed from state that already exists.
    internal sealed class ProjectEngineerReviewControl : UserControl
    {
        private const int HeaderHeight = 86;
        private const int FooterHeight = 78;
        private const int CardHeaderHeight = 32;
        private const int CardPadTop = 6;
        private const int CardPadBottom = 12;
        private const int AnswerColumnWidth = 216;
        private const int EvidenceKeyWidth = 172;
        private const int QuestionRowHeight = 30;
        private const int QuestionSectionHeight = 28;
        private const int EvidenceRowHeight = 24;
        // the evidence pane is audit reference material: it gets its own capped scroll region so
        // the primary review flow stays compact instead of being pushed far off-screen
        private const int EvidenceBodyHeight = 176;

        private readonly ProjectEngineerReviewService service;
        private readonly ToolTip toolTip = new ToolTip { AutoPopDelay = 8000 };
        private readonly Label titleLabel = new Label();
        private readonly Label subtitleLabel = new Label();
        private readonly BadgeLabel statusBadge = new BadgeLabel();
        private readonly Label caseLabel = new Label();
        private readonly Label progressLabel = new Label();
        private readonly ThinProgress queueProgress = new ThinProgress();
        private readonly TextBox reviewerName = new TextBox();
        private readonly TextBox reviewerRole = new TextBox();
        private readonly Panel evidenceBody = new Panel();
        private readonly Label remainingChip = new Label();
        private readonly Label evidenceCountChip = new Label();
        private readonly ComboBox classification = new ComboBox();
        private readonly TextBox comment = new TextBox();
        private readonly Label commentCount = new Label();
        private readonly Button previous = new Button();
        private readonly Button next = new Button();
        private readonly Button nextPending = new Button();
        private readonly Button save = new Button();
        private readonly SeverityMessage message = new SeverityMessage();
        private Card summaryCard;
        private TableLayoutPanel summaryGrid;
        private Card questionsCard;
        private TableLayoutPanel questionsGrid;
        private List<ProjectEngineerReviewRequest> queue = new List<ProjectEngineerReviewRequest>();
        private int currentIndex = -1;
        private bool loading;
        private Control highlighted;
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        internal ProjectEngineerReviewControl() : this(new ProjectEngineerReviewService()) { }

        internal ProjectEngineerReviewControl(ProjectEngineerReviewService reviewService)
        {
            if (reviewService == null) throw new ArgumentNullException(nameof(reviewService));
            service = reviewService;
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

            // one pinned header row, one scrolling column of cards, one pinned action bar:
            // the status/counter and the Save Review action are never scrolled out of view
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0),
                BackColor = ReviewUi.Background, Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, HeaderHeight));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));

            BuildHeader(root);
            BuildContent(root);
            BuildFooter(root);
            Controls.Add(root);
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            var host = new BorderedPanel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, BottomLine = true,
                Padding = new Padding(18, 10, 14, 10)
            };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            titleLabel.Text = "Project Engineer Review";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Font = ReviewUi.TitleFont;
            titleLabel.ForeColor = Color.White;
            titleLabel.AutoEllipsis = true;
            toolTip.SetToolTip(titleLabel, "Project Engineer Review");
            grid.Controls.Add(titleLabel, 0, 0);

            subtitleLabel.Text = "Human engineering observation evidence only";
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            subtitleLabel.Font = ReviewUi.SubtitleFont;
            subtitleLabel.ForeColor = ReviewUi.Muted;
            subtitleLabel.AutoEllipsis = true;
            toolTip.SetToolTip(subtitleLabel, "Records human engineering observation evidence only. It never creates approval, authority policy, trusted-issuer status, private-key authority, or rule activation.");
            grid.Controls.Add(subtitleLabel, 0, 1);
            grid.SetRowSpan(subtitleLabel, 2);

            statusBadge.Dock = DockStyle.Fill;
            statusBadge.Font = ReviewUi.BadgeFont;
            statusBadge.ForeColor = ReviewUi.Pending;
            statusBadge.Tint = ReviewUi.PendingTint;
            statusBadge.Text = "● PENDING";
            statusBadge.TextAlign = ContentAlignment.MiddleCenter;
            grid.Controls.Add(statusBadge, 1, 0);

            caseLabel.Dock = DockStyle.Fill;
            caseLabel.TextAlign = ContentAlignment.MiddleCenter;
            caseLabel.Font = ReviewUi.CounterFont;
            caseLabel.ForeColor = ReviewUi.Value;
            caseLabel.AutoEllipsis = true;
            grid.Controls.Add(caseLabel, 1, 1);

            var progressHost = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 2, 0, 2) };
            queueProgress.Dock = DockStyle.Fill;
            queueProgress.BackColor = ReviewUi.Surface;
            progressLabel.Dock = DockStyle.Right;
            progressLabel.Width = 76;
            progressLabel.TextAlign = ContentAlignment.MiddleRight;
            progressLabel.Font = ReviewUi.CaptionFont;
            progressLabel.ForeColor = ReviewUi.Muted;
            progressLabel.AutoEllipsis = true;
            progressHost.Controls.Add(queueProgress);
            progressHost.Controls.Add(progressLabel);
            grid.Controls.Add(progressHost, 1, 2);

            var accent = new Panel { Dock = DockStyle.Left, Width = 3, BackColor = ReviewUi.Accent, Margin = new Padding(0, 24, 14, 24) };
            host.Controls.Add(grid);
            host.Controls.Add(accent);
            root.Controls.Add(host, 0, 0);
        }

        private void BuildContent(TableLayoutPanel root)
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Background, AutoScroll = true,
                Padding = new Padding(0, 12, 0, 14)
            };

            Card identity = BuildIdentity();
            questionsCard = BuildQuestions();
            Card disposition = BuildDisposition();
            summaryCard = BuildSummary();
            Card evidence = BuildEvidence();

            // Docking claims edges in reverse add order, so the cards are added bottom-first to
            // stack top-down. The primary review flow comes first — who is reviewing, the questions,
            // then the disposition that Save Review commits — so every question fits on the first
            // screen. Case summary and software evidence are audit reference material and follow.
            evidence.Dock = DockStyle.Top;
            summaryCard.Dock = DockStyle.Top;
            disposition.Dock = DockStyle.Top;
            questionsCard.Dock = DockStyle.Top;
            identity.Dock = DockStyle.Top;
            scroll.Controls.Add(evidence);
            scroll.Controls.Add(summaryCard);
            scroll.Controls.Add(disposition);
            scroll.Controls.Add(questionsCard);
            scroll.Controls.Add(identity);
            root.Controls.Add(scroll, 0, 1);
        }

        private Card BuildIdentity()
        {
            var card = new Card("Engineer Identity", MakeChip("Required before saving", ReviewUi.Required));

            reviewerName.Dock = DockStyle.Fill;
            reviewerName.MaxLength = 120;
            reviewerName.Font = ReviewUi.BodyFont;
            reviewerName.BackColor = ReviewUi.InputBack;
            reviewerName.ForeColor = ReviewUi.InputText;
            reviewerName.AccessibleName = "ReviewerDisplayName";
            reviewerName.TabIndex = 1;
            toolTip.SetToolTip(reviewerName, "Name as the reviewer wishes it recorded in the review evidence.");

            reviewerRole.Dock = DockStyle.Fill;
            reviewerRole.MaxLength = 120;
            reviewerRole.Font = ReviewUi.BodyFont;
            reviewerRole.BackColor = ReviewUi.InputBack;
            reviewerRole.ForeColor = ReviewUi.InputText;
            reviewerRole.AccessibleName = "ReviewerRole";
            reviewerRole.TabIndex = 2;
            toolTip.SetToolTip(reviewerRole, "Role or title as the reviewer wishes it recorded in the review evidence.");

            var note = new Label
            {
                Text = "Recorded as review evidence only — this is not identity verification.",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            toolTip.SetToolTip(note, note.Text);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            grid.Controls.Add(FieldPair("Reviewer / Display Name", reviewerName), 0, 0);
            grid.Controls.Add(FieldPair("Role / Title", reviewerRole), 1, 0);
            grid.Controls.Add(note, 0, 1);
            grid.SetColumnSpan(note, 2);

            card.Body.Controls.Add(grid);
            card.Fit(66);
            return card;
        }

        private Card BuildSummary()
        {
            var card = new Card("Case Summary", null);
            summaryGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            summaryGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            summaryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            summaryGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            card.Body.Controls.Add(summaryGrid);
            card.Fit(88);
            return card;
        }

        private Card BuildEvidence()
        {
            ConfigureChip(evidenceCountChip, 112);
            var card = new Card("Software Evidence", evidenceCountChip);
            evidenceBody.Dock = DockStyle.Fill;
            evidenceBody.AutoScroll = true;
            evidenceBody.BackColor = ReviewUi.Surface;
            evidenceBody.Padding = new Padding(0, 0, 0, 2);
            card.Body.Controls.Add(evidenceBody);
            card.Fit(EvidenceBodyHeight);
            return card;
        }

        private Card BuildQuestions()
        {
            ConfigureChip(remainingChip, 192);
            var card = new Card("Review Questions", remainingChip);
            var requiredNote = new Label
            {
                Text = "Answer every required question (marked ●). Nothing is stored until you choose Save Review.",
                Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            toolTip.SetToolTip(requiredNote, requiredNote.Text);
            questionsGrid = new TableLayoutPanel
            {
                // explicit height (AutoSize would pin the grid to its preferred width and break
                // horizontal resize); docking supplies the full panel width so the question label
                // column absorbs growth and shrinkage
                Dock = DockStyle.Top, ColumnCount = 2, RowCount = 0, BackColor = ReviewUi.Surface, Margin = new Padding(0)
            };
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, AnswerColumnWidth));
            card.Body.Controls.Add(questionsGrid);
            card.Body.Controls.Add(requiredNote);
            card.Fit(18 + (4 * QuestionSectionHeight) + (14 * QuestionRowHeight));
            return card;
        }

        private Card BuildDisposition()
        {
            var card = new Card("Engineer Disposition", null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 312));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            classification.Dock = DockStyle.Top;
            classification.Height = 30;
            classification.DropDownStyle = ComboBoxStyle.DropDownList;
            classification.FlatStyle = FlatStyle.Flat;
            classification.Font = ReviewUi.BodyFont;
            classification.BackColor = ReviewUi.InputBack;
            classification.ForeColor = ReviewUi.InputText;
            classification.Items.Add(string.Empty);
            classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray());
            classification.SelectedIndex = 0;
            classification.AccessibleName = "Classification";
            classification.TabIndex = 24;
            toolTip.SetToolTip(classification, "Exactly one classification is required before saving.");
            grid.Controls.Add(FieldPair("Classification", classification, MakeChip("● Required", ReviewUi.Required)), 0, 0);

            comment.Dock = DockStyle.Fill;
            comment.Multiline = true;
            comment.ScrollBars = ScrollBars.Vertical;
            comment.MaxLength = 1000;
            comment.Font = ReviewUi.BodyFont;
            comment.BackColor = ReviewUi.InputBack;
            comment.ForeColor = ReviewUi.InputText;
            comment.AccessibleName = "EngineerComment";
            comment.TabIndex = 25;
            comment.TextChanged += (s, e) => UpdateCommentCount();
            commentCount.Font = ReviewUi.ChipFont;
            commentCount.AutoEllipsis = true;
            commentCount.TextAlign = ContentAlignment.MiddleRight;
            grid.Controls.Add(FieldPair("Engineer Comment", comment, commentCount), 1, 0);

            card.Body.Controls.Add(grid);
            card.Fit(59);
            return card;
        }

        private void BuildFooter(TableLayoutPanel root)
        {
            var host = new BorderedPanel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, TopLine = true,
                Padding = new Padding(12, 8, 12, 8)
            };
            // five columns keep the validation/status line anchored to the action bar; the primary
            // Save Review cell is the widest control in the bar
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

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
            foreach (Button button in new[] { previous, next, nextPending, save })
            {
                button.Dock = DockStyle.Fill;
                button.Margin = new Padding(1);
                button.Padding = new Padding(0);
            }
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
            root.Controls.Add(host, 0, 2);
        }

        // A labelled field: small caption above a full-width input, with an optional right-aligned
        // chip (required marker, character count) on the caption row.
        private static Panel FieldPair(string caption, Control input, Control chip)
        {
            var pair = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 0, 16, 0) };
            var captionHost = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = ReviewUi.Surface };
            var label = new Label
            {
                Text = caption.ToUpperInvariant(), Dock = DockStyle.Fill,
                ForeColor = ReviewUi.Caption, Font = ReviewUi.CaptionFont,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            captionHost.Controls.Add(label);
            if (chip != null)
            {
                chip.Dock = DockStyle.Right;
                captionHost.Controls.Add(chip);
            }
            var fieldHost = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface };
            fieldHost.Controls.Add(input);
            pair.Controls.Add(fieldHost);
            pair.Controls.Add(captionHost);
            return pair;
        }

        private static Panel FieldPair(string caption, Control input) => FieldPair(caption, input, null);

        private static Label MakeChip(string text, Color color)
        {
            return new Label
            {
                Text = text, Width = 140, AutoEllipsis = true,
                Font = ReviewUi.ChipFont, ForeColor = color,
                TextAlign = ContentAlignment.MiddleRight, BackColor = ReviewUi.Surface,
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        // Seats a live counter chip on a card header band, sized for its longest text so the
        // header caption keeps its full width and the counter never clips mid-update.
        private static Label ConfigureChip(Label chip, int width)
        {
            chip.Width = width;
            chip.Font = ReviewUi.ChipFont;
            chip.TextAlign = ContentAlignment.MiddleRight;
            chip.AutoEllipsis = true;
            chip.Margin = new Padding(0, 0, 8, 0);
            return chip;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Font = primary ? ReviewUi.PrimaryButtonFont : ReviewUi.ButtonFont;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatAppearance.BorderColor = primary ? ReviewUi.PrimaryEdge : ReviewUi.Border;
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
                button.BackColor = ReviewUi.Card;
                button.ForeColor = ReviewUi.DisabledText;
            }
        }

        private void ShowCase(int index)
        {
            currentIndex = index;
            loading = true;
            ClearHighlight();
            answerControls.Clear();
            classification.SelectedIndex = 0;
            comment.Text = string.Empty;
            UpdateQueueProgress();
            if (index < 0 || index >= queue.Count)
            {
                UpdateHeader(-1, null);
                ClearControls(evidenceBody);
                var empty = new Label
                {
                    Text = queue.Count == 0 ? "No reviewable cases found." : "No case selected.",
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    Font = ReviewUi.BodyFont, ForeColor = ReviewUi.Muted
                };
                evidenceBody.Controls.Add(empty);
                ClearControls(summaryGrid);
                ClearControls(questionsGrid);
                save.Enabled = false;
                previous.Enabled = false;
                next.Enabled = false;
                nextPending.Enabled = false;
                message.Text = string.Empty;
                message.Severity = SeverityMessage.None;
                remainingChip.Text = string.Empty;
                evidenceCountChip.Text = string.Empty;
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
            if (status == ProjectEngineerReviewStatuses.Completed)
            {
                message.Text = "This case is already completed — the recorded review is shown.";
                message.Severity = SeverityMessage.Success;
            }
            else if (status == ProjectEngineerReviewStatuses.Invalid)
            {
                message.Text = "The stored response is invalid; saving will replace it.";
                message.Severity = SeverityMessage.Warning;
            }
            else
            {
                message.Text = string.Empty;
                message.Severity = SeverityMessage.None;
            }
            UpdateRemainingRequired();
            UpdateCommentCount();
            loading = false;
        }

        private void UpdateHeader(int index, string status)
        {
            if (index < 0 || index >= queue.Count)
            {
                statusBadge.Text = "● NONE";
                statusBadge.ForeColor = ReviewUi.Muted;
                statusBadge.Tint = ReviewUi.SurfaceAlt;
                caseLabel.Text = queue.Count == 0 ? "No reviewable cases found." : "No case selected.";
                toolTip.SetToolTip(statusBadge, "There is no case to display.");
                return;
            }
            caseLabel.Text = "Case " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + queue.Count.ToString(CultureInfo.InvariantCulture);
            statusBadge.Text = StatusGlyph(status) + " " + status;
            statusBadge.ForeColor = StatusColor(status);
            statusBadge.Tint = StatusTint(status);
            string detail = status == ProjectEngineerReviewStatuses.Completed
                ? "A complete, binding-valid engineer response is recorded for this case."
                : status == ProjectEngineerReviewStatuses.Invalid
                    ? "A stored response failed validation; a new save will replace it."
                    : "No engineer response is recorded yet for this case.";
            toolTip.SetToolTip(statusBadge, detail);
        }

        private void UpdateQueueProgress()
        {
            int total = queue.Count;
            int completed = queue.Count(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Completed);
            progressLabel.Text = "Reviewed " + completed.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture);
            queueProgress.Percent = total == 0 ? 0 : (int)Math.Round(100.0 * completed / total);
            toolTip.SetToolTip(progressLabel, completed.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture) + " cases hold a complete, binding-valid engineer response.");
        }

        // live, presentation-only completeness meter: derived only from answers already entered
        private void UpdateRemainingRequired()
        {
            if (currentIndex < 0 || currentIndex >= queue.Count) { remainingChip.Text = string.Empty; return; }
            ProjectEngineerReviewRequest request = queue[currentIndex];
            int missing = 0;
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!question.Required) continue;
                if (!answerControls.TryGetValue(question.QuestionId, out Control control)) continue;
                string value = control is ComboBox combo ? (combo.SelectedIndex > 0 ? (string)combo.SelectedItem : string.Empty) : control.Text;
                if (string.IsNullOrWhiteSpace(value)) missing++;
            }
            remainingChip.Text = missing == 0
                ? "All required answers complete"
                : missing.ToString(CultureInfo.InvariantCulture) + " required answer" + (missing == 1 ? string.Empty : "s") + " remaining";
            remainingChip.ForeColor = missing == 0 ? ReviewUi.Completed : ReviewUi.Required;
            toolTip.SetToolTip(remainingChip, missing == 0
                ? "Every required question has an answer. Save Review stores the review."
                : "Required questions are still unanswered. Nothing is stored until Save Review.");
        }

        private void UpdateCommentCount()
        {
            int length = comment.TextLength;
            commentCount.Text = length.ToString(CultureInfo.InvariantCulture) + " / 1000";
            commentCount.ForeColor = length > 0 ? ReviewUi.Muted : ReviewUi.Caption;
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

        private static Color StatusTint(string status)
        {
            if (status == ProjectEngineerReviewStatuses.Completed) return ReviewUi.CompletedTint;
            if (status == ProjectEngineerReviewStatuses.Invalid) return ReviewUi.InvalidTint;
            return ReviewUi.PendingTint;
        }

        private void BuildSummary(ProjectEngineerReviewRequest request)
        {
            summaryCard.SuspendLayout();
            ClearControls(summaryGrid);
            AddSummaryPair(summaryGrid, 0, 0, "Drawing", request.DrawingName);
            AddSummaryPair(summaryGrid, 1, 0, "Case", request.ReviewedCaseId);
            AddSummaryPair(summaryGrid, 0, 1, "Operational result", request.SoftwareResult ?? "<not recorded>");
            AddSummaryPair(summaryGrid, 1, 1, "Review reason", request.ReviewReason);
            summaryCard.ResumeLayout();
        }

        private void AddSummaryPair(TableLayoutPanel grid, int column, int row, string key, string value)
        {
            var pair = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 0, 16, 0) };
            var valueLabel = new Label
            {
                Text = value ?? string.Empty, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.ValueFont, ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            var keyLabel = new Label
            {
                Text = key.ToUpperInvariant(), Dock = DockStyle.Top, Height = 15,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Caption, AutoEllipsis = true
            };
            if (!string.IsNullOrEmpty(value)) toolTip.SetToolTip(valueLabel, value);
            toolTip.SetToolTip(keyLabel, key);
            pair.Controls.Add(valueLabel);
            pair.Controls.Add(keyLabel);
            grid.Controls.Add(pair, column, row);
        }

        private void BuildEvidence(ProjectEngineerReviewRequest request)
        {
            evidenceBody.SuspendLayout();
            ClearControls(evidenceBody);
            var grid = new TableLayoutPanel
            {
                // single full-width column: each row is a key/value pair panel, so the row must
                // span the whole grid width or the pair's value column collapses to a pixel
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 0, BackColor = ReviewUi.Surface, Margin = new Padding(0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int added = 0;
            added += AddEvidenceRow(grid, "Review request ID", request.ReviewRequestId);
            added += AddEvidenceRow(grid, "Request SHA-256", request.RequestSha256);
            added += AddEvidenceRow(grid, "Workflow source", request.WorkflowSourceType);
            added += AddEvidenceRow(grid, "Reviewed case", request.ReviewedCaseId);
            added += AddEvidenceRow(grid, "Drawing", request.DrawingName);
            if (!string.IsNullOrWhiteSpace(request.DrawingSha256)) added += AddEvidenceRow(grid, "Drawing SHA-256", request.DrawingSha256);
            added += AddEvidenceRow(grid, "Software result", request.SoftwareResult);
            added += AddEvidenceRow(grid, "Software state", request.SoftwareState);
            foreach (ProjectEngineerReviewEvidenceReference reference in request.EvidenceReferences ?? new List<ProjectEngineerReviewEvidenceReference>())
            {
                if (string.IsNullOrWhiteSpace(reference.Value)) continue;
                string key = !string.IsNullOrWhiteSpace(reference.Label) ? reference.Label : reference.Kind;
                added += AddEvidenceRow(grid, key, reference.Value);
            }
            evidenceCountChip.Text = added.ToString(CultureInfo.InvariantCulture) + " evidence items";
            evidenceBody.Controls.Add(grid);
            grid.Height = (grid.RowCount * EvidenceRowHeight) + 2;
            evidenceBody.ResumeLayout();
        }

        private int AddEvidenceRow(TableLayoutPanel grid, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            // faint banding on alternate rows keeps a long audit list scannable
            bool alt = (grid.RowCount % 2) == 1;
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, EvidenceRowHeight));
            var pair = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = alt ? ReviewUi.EvidenceAlt : ReviewUi.Surface, Margin = new Padding(0) };
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, EvidenceKeyWidth));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var keyLabel = new Label
            {
                Text = key, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Caption, AutoEllipsis = true
            };
            // long hex hashes render in a monospace face so digits stay aligned and scannable
            var valueLabel = new Label
            {
                Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = IsHexHash(value) ? ReviewUi.MonoFont : ReviewUi.ValueFont,
                ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            toolTip.SetToolTip(keyLabel, key);
            toolTip.SetToolTip(valueLabel, value);
            pair.Controls.Add(keyLabel, 0, 0);
            pair.Controls.Add(valueLabel, 1, 0);
            grid.Controls.Add(pair, 0, grid.RowCount - 1);
            return 1;
        }

        private static bool IsHexHash(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }

        private void BuildQuestions(ProjectEngineerReviewRequest request)
        {
            questionsCard.SuspendLayout();
            ClearControls(questionsGrid);
            questionsGrid.RowCount = 0;
            questionsGrid.RowStyles.Clear();
            string currentSection = null;
            int tabIndex = 10;
            int sectionCount = 0;
            foreach (ProjectEngineerReviewQuestionDefinition question in request.Questions)
            {
                if (!string.Equals(currentSection, question.Section, StringComparison.Ordinal))
                {
                    currentSection = question.Section;
                    AddQuestionSectionHeader(questionsGrid, currentSection);
                    sectionCount++;
                }
                AddQuestionRow(questionsGrid, question, ref tabIndex);
            }
            int answerRows = questionsGrid.RowCount - sectionCount;
            questionsGrid.Height = (sectionCount * QuestionSectionHeight) + (answerRows * QuestionRowHeight);
            questionsCard.Fit(18 + questionsGrid.Height);
            questionsCard.ResumeLayout();
        }

        private void AddQuestionSectionHeader(TableLayoutPanel grid, string section)
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, QuestionSectionHeight));
            var strip = new Label
            {
                Text = section.ToUpperInvariant(),
                Dock = DockStyle.Fill, BackColor = ReviewUi.SurfaceAlt, ForeColor = ReviewUi.Section,
                Font = ReviewUi.SectionFont, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true
            };
            toolTip.SetToolTip(strip, section);
            grid.Controls.Add(strip, 0, grid.RowCount - 1);
            grid.SetColumnSpan(strip, 2);
        }

        private void AddQuestionRow(TableLayoutPanel grid, ProjectEngineerReviewQuestionDefinition question, ref int tabIndex)
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, QuestionRowHeight));
            // the required marker is its own colored glyph so the marker cannot be mistaken for
            // part of the question text; optional questions get a hollow marker instead
            var textHost = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Margin = new Padding(0, 0, 8, 0) };
            var marker = new Label
            {
                Text = question.Required ? "●" : "○", Dock = DockStyle.Left, Width = 16,
                Font = ReviewUi.MarkerFont, ForeColor = question.Required ? ReviewUi.Required : ReviewUi.Optional,
                TextAlign = ContentAlignment.MiddleLeft
            };
            var label = new Label
            {
                Text = question.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.BodyFont, ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            toolTip.SetToolTip(label, question.Text + (question.Required ? " (required)" : " (optional)"));
            textHost.Controls.Add(label);
            textHost.Controls.Add(marker);
            grid.Controls.Add(textHost, 0, grid.RowCount - 1);
            Control answer;
            if (question.AnswerKind == ProjectEngineerAnswerKinds.FreeText)
            {
                var box = new TextBox
                {
                    Dock = DockStyle.Fill, MaxLength = 500, Tag = question.QuestionId,
                    Font = ReviewUi.BodyFont, Margin = new Padding(0, 2, 2, 2),
                    BackColor = ReviewUi.InputBack, ForeColor = ReviewUi.InputText,
                    AccessibleName = question.QuestionId, TabIndex = tabIndex++
                };
                box.TextChanged += HandleAnswerChanged;
                answer = box;
            }
            else
            {
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Tag = question.QuestionId,
                    Font = ReviewUi.BodyFont, Margin = new Padding(0, 2, 2, 2), FlatStyle = FlatStyle.Flat,
                    BackColor = ReviewUi.InputBack, ForeColor = ReviewUi.InputText,
                    AccessibleName = question.QuestionId, TabIndex = tabIndex++
                };
                combo.Items.Add(string.Empty);
                foreach (string value in ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)) combo.Items.Add(value);
                combo.SelectedIndex = 0;
                combo.SelectedIndexChanged += HandleAnswerChanged;
                answer = combo;
            }
            grid.Controls.Add(answer, 1, grid.RowCount - 1);
            answerControls[question.QuestionId] = answer;
        }

        private void HandleAnswerChanged(object sender, EventArgs e)
        {
            if (loading) return;
            message.Text = string.Empty;
            message.Severity = SeverityMessage.None;
            UpdateRemainingRequired();
            if (sender is Control control) ClearHighlight(control);
        }

        private static void ClearControls(Control container)
        {
            List<Control> children = container.Controls.Cast<Control>().ToList();
            container.Controls.Clear();
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
            message.Severity = SeverityMessage.Info;
        }

        private void Save(object sender, EventArgs e)
        {
            if (currentIndex < 0 || currentIndex >= queue.Count) return;
            ProjectEngineerReviewRequest request = queue[currentIndex];
            Dictionary<string, string> answers = CaptureAnswers();
            string selectedClassification = classification.SelectedItem as string ?? string.Empty;
            string incomplete = ProjectEngineerReviewService.FindIncompleteReason(request, reviewerName.Text, reviewerRole.Text, answers, selectedClassification);
            if (incomplete != null)
            {
                // inline validation: the reason is mapped back to the exact field that blocked the
                // save so the engineer sees where to look, not just that something is missing
                message.Text = "Review not saved — " + incomplete;
                message.Severity = SeverityMessage.Warning;
                Highlight(MapErrorControl(request, incomplete));
                return;
            }
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
                message.Severity = SeverityMessage.Success;
            }
            catch (Exception exception)
            {
                message.Text = "Save failed: " + exception.Message;
                message.Severity = SeverityMessage.Error;
            }
        }

        // points the engineer at the exact control behind a blocked save, in the order the
        // completeness gate reports its reasons
        private Control MapErrorControl(ProjectEngineerReviewRequest request, string reason)
        {
            if (reason.StartsWith("Reviewer display name", StringComparison.Ordinal)) return reviewerName;
            if (reason.StartsWith("Reviewer role", StringComparison.Ordinal)) return reviewerRole;
            if (reason.StartsWith("A review classification", StringComparison.Ordinal) || reason.StartsWith("Unsupported classification", StringComparison.Ordinal)) return classification;
            const string answerMarker = "Answer required: ";
            const string vocabularyMarker = "Answer not in the allowed vocabulary: ";
            int index = reason.IndexOf(answerMarker, StringComparison.Ordinal);
            int markerLength = answerMarker.Length;
            if (index < 0) { index = reason.IndexOf(vocabularyMarker, StringComparison.Ordinal); markerLength = vocabularyMarker.Length; }
            if (index < 0) return null;
            string text = reason.Substring(index + markerLength);
            ProjectEngineerReviewQuestionDefinition question = request.Questions.FirstOrDefault(x => string.Equals(x.Text, text, StringComparison.Ordinal));
            if (question == null) return null;
            return answerControls.TryGetValue(question.QuestionId, out Control control) ? control : null;
        }

        // tint the offending field until the engineer edits it; the change handlers clear the tint
        private void Highlight(Control control)
        {
            ClearHighlight();
            if (control == null) return;
            highlighted = control;
            control.TextChanged += ClearHighlight;
            if (control is ComboBox combo) combo.SelectedIndexChanged += ClearHighlight;
            control.BackColor = ReviewUi.ErrorBack;
        }

        private void ClearHighlight(object sender, EventArgs e) => ClearHighlight();

        private void ClearHighlight(Control only)
        {
            if (highlighted != null && highlighted == only) ClearHighlight();
        }

        private void ClearHighlight()
        {
            if (highlighted == null) return;
            highlighted.TextChanged -= ClearHighlight;
            if (highlighted is ComboBox combo) combo.SelectedIndexChanged -= ClearHighlight;
            if (highlighted is TextBoxBase || highlighted is ComboBox) highlighted.BackColor = ReviewUi.InputBack;
            highlighted = null;
        }

        // Bordered card with an accent bar, an uppercase caption, and an optional right chip.
        private sealed class Card : Panel
        {
            private readonly Panel accent = new Panel { Width = 3, BackColor = ReviewUi.Accent };
            private readonly Label caption = new Label();
            public Panel Body { get; }

            public string Caption => caption.Text;

            public Card(string title, Control chip)
            {
                BackColor = ReviewUi.Surface;
                Padding = new Padding(1);
                Margin = new Padding(12, 0, 12, 12);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

                var head = new BorderedPanel { Dock = DockStyle.Top, Height = CardHeaderHeight, BackColor = ReviewUi.CardHead, BottomLine = true };
                accent.Dock = DockStyle.Left;
                accent.Margin = new Padding(0, 8, 10, 8);
                caption.Text = title.ToUpperInvariant();
                caption.Dock = DockStyle.Fill;
                caption.Font = ReviewUi.SectionFont;
                caption.ForeColor = ReviewUi.Section;
                caption.BackColor = ReviewUi.CardHead;
                caption.TextAlign = ContentAlignment.MiddleLeft;
                caption.AutoEllipsis = true;
                if (chip != null)
                {
                    chip.Dock = DockStyle.Right;
                    // a chip on the header band shares the band color so only its text reads as a marker
                    chip.BackColor = ReviewUi.CardHead;
                }
                // add order sets dock order (reverse of add): the caption fills whatever remains
                // after the accent claims the left edge and the chip claims the right edge
                head.Controls.Add(caption);
                if (chip != null) head.Controls.Add(chip);
                head.Controls.Add(accent);

                Body = new Panel { Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, Padding = new Padding(14, CardPadTop, 14, CardPadBottom) };
                Controls.Add(Body);
                Controls.Add(head);
            }

            // total height from the height of the card's content
            public void Fit(int contentHeight)
            {
                Height = 2 + CardHeaderHeight + CardPadTop + contentHeight + CardPadBottom;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(ReviewUi.Border, 1))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
            }
        }

        // Panel with a single hairline rule on its top and/or bottom edge, used to separate the
        // pinned header and footer from the scrolling card column.
        private sealed class BorderedPanel : Panel
        {
            public bool TopLine { get; set; }
            public bool BottomLine { get; set; }

            public BorderedPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(ReviewUi.Border, 1))
                {
                    if (TopLine) e.Graphics.DrawLine(pen, 0, 0, Width - 1, 0);
                    if (BottomLine) e.Graphics.DrawLine(pen, 0, Height - 1, Width - 1, Height - 1);
                }
            }
        }

        // Rounded status pill: tinted background with matching foreground, drawn so the badge
        // reads as a colored tag rather than a bordered label.
        private sealed class BadgeLabel : Label
        {
            public Color Tint { get; set; } = ReviewUi.SurfaceAlt;

            public BadgeLabel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
                using (var path = RoundedPath(r, 8))
                using (var brush = new SolidBrush(Tint)) g.FillPath(brush, path);
                TextRenderer.DrawText(g, Text, Font, r, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        // Thin centered progress rail for queue progress (presentation only).
        private sealed class ThinProgress : Control
        {
            private int percent;
            private const int RailHeight = 6;

            public int Percent
            {
                get => percent;
                set { percent = Math.Max(0, Math.Min(100, value)); Invalidate(); }
            }

            public ThinProgress()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int y = (Height - RailHeight) / 2;
                Rectangle track = new Rectangle(0, y, Width, RailHeight);
                using (var path = RoundedPath(track, RailHeight / 2))
                using (var trackBrush = new SolidBrush(ReviewUi.SurfaceAlt)) g.FillPath(trackBrush, path);
                if (percent > 0)
                {
                    int fill = (int)Math.Round(Width * (percent / 100.0));
                    int barWidth = Math.Max(RailHeight, Math.Min(fill, Width));
                    Rectangle bar = new Rectangle(0, y, barWidth, RailHeight);
                    using (var barPath = RoundedPath(bar, RailHeight / 2))
                    using (var barBrush = new SolidBrush(ReviewUi.Completed)) g.FillPath(barBrush, barPath);
                }
            }
        }

        // Message line that prefixes a colored severity glyph while keeping its Text pure, so the
        // inline validation reason stays machine-readable and copy-safe.
        private sealed class SeverityMessage : Label
        {
            public const int None = 0;
            public const int Info = 1;
            public const int Success = 2;
            public const int Warning = 3;
            public const int Error = 4;

            private const int GlyphBox = 20;
            private static readonly Font GlyphFont = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point);

            private int severity = None;

            public int Severity
            {
                get => severity;
                set { severity = value; Invalidate(); }
            }

            public SeverityMessage()
            {
                AutoSize = false;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, e.ClipRectangle);
                Rectangle text = new Rectangle(2, 0, Math.Max(0, Width - 4), Height);
                if (severity == None)
                {
                    TextRenderer.DrawText(g, Text, Font, text, ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }
                Color color = severity == Success ? ReviewUi.Completed : severity == Warning ? ReviewUi.Required : severity == Error ? ReviewUi.Invalid : ReviewUi.Accent;
                string glyph = severity == Success ? "✓" : severity == Warning ? "!" : severity == Error ? "✕" : "●";
                Rectangle dot = new Rectangle(2, (Height - 15) / 2, 15, 15);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, dot);
                TextRenderer.DrawText(g, glyph, GlyphFont, dot, Color.FromArgb(28, 30, 34),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                text = new Rectangle(GlyphBox, 0, Math.Max(0, Width - GlyphBox - 2), Height);
                TextRenderer.DrawText(g, Text, Font, text, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static class ReviewUi
        {
            // shared EngiSentry dark palette, kept consistent with the accepted Results UI
            internal static readonly Color Background = Color.FromArgb(30, 31, 34);
            internal static readonly Color Surface = Color.FromArgb(38, 40, 44);
            internal static readonly Color SurfaceAlt = Color.FromArgb(46, 49, 55);
            internal static readonly Color EvidenceAlt = Color.FromArgb(45, 48, 54);
            internal static readonly Color Card = Color.FromArgb(43, 45, 50);
            internal static readonly Color CardHead = Color.FromArgb(44, 47, 52);
            internal static readonly Color Text = Color.FromArgb(235, 237, 240);
            internal static readonly Color Value = Color.FromArgb(225, 228, 232);
            internal static readonly Color Muted = Color.FromArgb(160, 166, 175);
            internal static readonly Color Caption = Color.FromArgb(120, 127, 138);
            internal static readonly Color Section = Color.FromArgb(170, 176, 186);
            internal static readonly Color Border = Color.FromArgb(74, 78, 86);
            internal static readonly Color Accent = Color.FromArgb(0, 120, 200);
            internal static readonly Color Primary = Color.FromArgb(0, 118, 214);
            internal static readonly Color PrimaryEdge = Color.FromArgb(0, 140, 250);
            internal static readonly Color Pending = Color.FromArgb(255, 190, 92);
            internal static readonly Color PendingTint = Color.FromArgb(58, 48, 26);
            internal static readonly Color Completed = Color.FromArgb(108, 220, 155);
            internal static readonly Color CompletedTint = Color.FromArgb(24, 46, 34);
            internal static readonly Color Invalid = Color.FromArgb(255, 112, 112);
            internal static readonly Color InvalidTint = Color.FromArgb(58, 30, 32);
            internal static readonly Color Required = Color.FromArgb(255, 176, 59);
            internal static readonly Color Optional = Color.FromArgb(96, 103, 113);
            internal static readonly Color InputBack = Color.FromArgb(24, 26, 29);
            internal static readonly Color InputText = Color.FromArgb(235, 237, 240);
            internal static readonly Color ErrorBack = Color.FromArgb(58, 30, 32);
            internal static readonly Color DisabledText = Color.FromArgb(120, 125, 132);

            internal static readonly Font TitleFont = new Font("Segoe UI Semibold", 14.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font SubtitleFont = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font BadgeFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font CounterFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font SectionFont = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font CaptionFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font ChipFont = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font BodyFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font ButtonFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font PrimaryButtonFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            internal static readonly Font ValueFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font MarkerFont = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            internal static readonly Font MonoFont = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);
        }
    }
}
