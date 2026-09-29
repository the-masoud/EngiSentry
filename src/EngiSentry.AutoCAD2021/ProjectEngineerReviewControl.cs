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
        private const int AnswerColumnWidth = 216;
        private const int EvidenceKeyWidth = 172;
        private const int QuestionRowHeight = 30;
        private const int QuestionSectionHeight = 28;
        private const int EvidenceRowHeight = 24;
        // the evidence pane is audit reference material: it gets its own capped scroll region so
        // the primary review flow stays compact instead of being pushed far off-screen
        private const int EvidenceBodyHeight = 176;

        private readonly ProjectEngineerReviewService service;
        private ToolTip toolTip;
        private Label titleLabel;
        private Label subtitleLabel;
        private BadgeLabel statusBadge;
        private Label caseLabel;
        private Label progressLabel;
        private ThinProgress queueProgress;
        private TextBox reviewerName;
        private TextBox reviewerRole;
        private Panel evidenceBody;
        private Label remainingChip;
        private Label evidenceCountChip;
        private ComboBox classification;
        private TextBox comment;
        private Label commentCount;
        private Button previous;
        private Button next;
        private Button nextPending;
        private Button save;
        private LinkLabel languageLink;
        private SeverityMessage message;
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
            CreateControls();
            BuildLayout();
            LoadQueue();
        }

        // (Re)creates the control instances so a UI-language switch can rebuild the whole surface
        // with the current culture's resources, fonts, and layout direction. Review state (queue,
        // current case, unsaved entries) is preserved across the rebuild by Rebuild().
        private void CreateControls()
        {
            toolTip = new ToolTip { AutoPopDelay = 8000 };
            titleLabel = new Label();
            subtitleLabel = new Label();
            statusBadge = new BadgeLabel();
            caseLabel = new Label();
            progressLabel = new Label();
            queueProgress = new ThinProgress();
            reviewerName = new TextBox();
            reviewerRole = new TextBox();
            evidenceBody = new Panel();
            remainingChip = new Label();
            evidenceCountChip = new Label();
            classification = new ComboBox();
            comment = new TextBox();
            commentCount = new Label();
            previous = new Button();
            next = new Button();
            nextPending = new Button();
            save = new Button();
            languageLink = new LinkLabel();
            message = new SeverityMessage();
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

            // Standard WinForms right-to-left support: the whole surface is mirrored for Persian so
            // docking, column order, and text alignment flip automatically. Latin identifiers and
            // hashes stay LTR inside the mirrored surface.
            RightToLeft = ReviewLocalization.IsPersian ? RightToLeft.Yes : RightToLeft.No;

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

        // Rebuilds the surface after a UI-language change: the current case and any unsaved entries
        // are captured first, the control tree is recreated with the new culture's resources, fonts
        // and direction, then the case is re-shown and the captured entries restored. Nothing is
        // persisted by the rebuild — the review still stores only on an explicit Save Review.
        internal void Rebuild()
        {
            string draftName = reviewerName.Text;
            string draftRole = reviewerRole.Text;
            string draftClassification = classification.SelectedItem as string ?? string.Empty;
            string draftComment = comment.Text;
            Dictionary<string, string> draftAnswers = CaptureAnswers();
            int restore = currentIndex;

            SuspendLayout();
            try
            {
                ClearControls(this);
                CreateControls();
                BuildLayout();
            }
            finally { ResumeLayout(); }

            ShowCase(restore);

            // restore the engineer's unsaved screen entries onto the fresh controls
            loading = true;
            try
            {
                reviewerName.Text = draftName;
                reviewerRole.Text = draftRole;
                classification.SelectedItem = draftClassification;
                if (classification.SelectedIndex < 0) classification.SelectedIndex = 0;
                comment.Text = draftComment;
                foreach (KeyValuePair<string, string> pair in draftAnswers)
                {
                    if (!answerControls.TryGetValue(pair.Key, out Control control)) continue;
                    if (control is ComboBox combo) combo.SelectedItem = pair.Value;
                    else control.Text = pair.Value;
                    if (control is ComboBox c && c.SelectedIndex < 0) c.SelectedIndex = 0;
                }
            }
            finally { loading = false; }
            UpdateRemainingRequired();
            UpdateCommentCount();
        }

        private void SwitchLanguage()
        {
            // defer the rebuild until the link's click message has completed, so the control that
            // raised the event is never disposed from inside its own handler
            if (IsHandleCreated) BeginInvoke((Action)SwitchLanguageDeferred);
            else SwitchLanguageDeferred();
        }

        private void SwitchLanguageDeferred()
        {
            ReviewLocalization.Toggle();
            Rebuild();
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            var host = new BorderedPanel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, BottomLine = true,
                Padding = new Padding(18, 10, 14, 10)
            };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            titleLabel.Text = ReviewStrings.Get("AppTitle");
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.Font = ReviewUi.TitleFont;
            titleLabel.ForeColor = Color.White;
            titleLabel.AutoEllipsis = true;
            toolTip.SetToolTip(titleLabel, ReviewStrings.Get("AppTitle"));
            grid.Controls.Add(titleLabel, 0, 0);

            subtitleLabel.Text = ReviewStrings.Get("AppSubtitle");
            subtitleLabel.Dock = DockStyle.Fill;
            subtitleLabel.TextAlign = ContentAlignment.MiddleLeft;
            subtitleLabel.Font = ReviewUi.SubtitleFont;
            subtitleLabel.ForeColor = ReviewUi.Muted;
            subtitleLabel.AutoEllipsis = true;
            toolTip.SetToolTip(subtitleLabel, ReviewStrings.Get("AppSubtitleToolTip"));
            grid.Controls.Add(subtitleLabel, 0, 1);
            grid.SetRowSpan(subtitleLabel, 2);

            // the language link names the language the engineer can switch to; it never names the
            // current one, so it always reads as an available action
            languageLink.Text = ReviewLocalization.SwitchLabel;
            languageLink.Dock = DockStyle.Fill;
            languageLink.TextAlign = ContentAlignment.MiddleCenter;
            languageLink.Font = ReviewUi.CaptionFont;
            languageLink.ForeColor = ReviewUi.Value;
            languageLink.LinkColor = ReviewUi.Value;
            languageLink.ActiveLinkColor = ReviewUi.Value;
            languageLink.VisitedLinkColor = ReviewUi.Value;
            languageLink.LinkBehavior = LinkBehavior.HoverUnderline;
            languageLink.AccessibleName = "UiLanguage";
            languageLink.TabIndex = 3;
            languageLink.LinkClicked += (s, e) => SwitchLanguage();
            toolTip.SetToolTip(languageLink, ReviewStrings.Get("LanguageSwitchToolTip"));
            grid.Controls.Add(languageLink, 2, 1);
            grid.SetRowSpan(languageLink, 2);

            statusBadge.Dock = DockStyle.Fill;
            statusBadge.Font = ReviewUi.BadgeFont;
            statusBadge.ForeColor = ReviewUi.Pending;
            statusBadge.Tint = ReviewUi.PendingTint;
            statusBadge.Text = ReviewStrings.Get("StatusPendingBadge");
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
            var card = new Card(ReviewStrings.Get("SectionIdentity"), ReviewStyle.Chip(ReviewStrings.Get("ChipRequiredBeforeSaving"), ReviewUi.Required));

            reviewerName.Dock = DockStyle.Fill;
            reviewerName.MaxLength = 120;
            reviewerName.Font = ReviewUi.BodyFont;
            reviewerName.BackColor = ReviewUi.InputBack;
            reviewerName.ForeColor = ReviewUi.InputText;
            reviewerName.AccessibleName = "ReviewerDisplayName";
            reviewerName.TabIndex = 1;
            toolTip.SetToolTip(reviewerName, ReviewStrings.Get("ReviewerNameToolTip"));

            reviewerRole.Dock = DockStyle.Fill;
            reviewerRole.MaxLength = 120;
            reviewerRole.Font = ReviewUi.BodyFont;
            reviewerRole.BackColor = ReviewUi.InputBack;
            reviewerRole.ForeColor = ReviewUi.InputText;
            reviewerRole.AccessibleName = "ReviewerRole";
            reviewerRole.TabIndex = 2;
            toolTip.SetToolTip(reviewerRole, ReviewStrings.Get("ReviewerRoleToolTip"));

            var note = new Label
            {
                Text = ReviewStrings.Get("IdentityNote"),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, AutoEllipsis = true
            };
            toolTip.SetToolTip(note, note.Text);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("FieldReviewerName"), reviewerName), 0, 0);
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("FieldReviewerRole"), reviewerRole), 1, 0);
            grid.Controls.Add(note, 0, 1);
            grid.SetColumnSpan(note, 2);

            card.Body.Controls.Add(grid);
            card.Fit(66);
            return card;
        }

        private Card BuildSummary()
        {
            var card = new Card(ReviewStrings.Get("SectionSummary"), null);
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
            ReviewStyle.ConfigureChip(evidenceCountChip, 112);
            var card = new Card(ReviewStrings.Get("SectionEvidence"), evidenceCountChip);
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
            ReviewStyle.ConfigureChip(remainingChip, 192);
            var card = new Card(ReviewStrings.Get("SectionQuestions"), remainingChip);
            var requiredNote = new Label
            {
                Text = ReviewStrings.Get("RequiredNote"),
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
            var card = new Card(ReviewStrings.Get("SectionDisposition"), null);
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
            // the combo holds canonical classification tokens and persists exactly those; only the
            // painted entry is localized
            classification.DrawMode = DrawMode.OwnerDrawFixed;
            classification.DrawItem += DrawClassificationItem;
            toolTip.SetToolTip(classification, ReviewStrings.Get("ClassificationToolTip"));
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("FieldClassification"), classification, ReviewStyle.Chip(ReviewStrings.Get("ChipRequired"), ReviewUi.Required)), 0, 0);

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
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("FieldComment"), comment, commentCount), 1, 0);

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

            previous.Text = ReviewStrings.Get("ButtonPrevious");
            next.Text = ReviewStrings.Get("ButtonNext");
            nextPending.Text = ReviewStrings.Get("ButtonNextPending");
            save.Text = ReviewStrings.Get("ButtonSave");
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
            foreach (Button button in new[] { previous, next, nextPending }) ReviewStyle.StyleButton(button, false);
            ReviewStyle.StyleButton(save, true);
            toolTip.SetToolTip(previous, ReviewStrings.Get("PreviousToolTip"));
            toolTip.SetToolTip(next, ReviewStrings.Get("NextToolTip"));
            toolTip.SetToolTip(nextPending, ReviewStrings.Get("NextPendingToolTip"));
            toolTip.SetToolTip(save, ReviewStrings.Get("SaveToolTip"));
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

        // Owner-draw for the answer/classification combos: the item objects stay canonical so the
        // selection, CaptureAnswers, and persistence are byte-identical; only the painted text runs
        // through the display layer so Persian shows Persian choice text and English shows the
        // canonical vocabulary.
        private static void DrawAnswerItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            if (e.Index < 0 || e.Index >= combo.Items.Count) return;
            string canonical = (string)combo.Items[e.Index];
            ReviewStyle.PaintComboItem(combo, e, ReviewDisplayText.Answer(canonical));
        }

        private static void DrawClassificationItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            if (e.Index < 0 || e.Index >= combo.Items.Count) return;
            string canonical = (string)combo.Items[e.Index];
            ReviewStyle.PaintComboItem(combo, e, ReviewDisplayText.Classification(canonical));
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
                    Text = queue.Count == 0 ? ReviewStrings.Get("NoReviewableCases") : ReviewStrings.Get("NoCaseSelected"),
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
                message.Text = ReviewStrings.Get("MsgCompleted");
                message.Severity = SeverityMessage.Success;
            }
            else if (status == ProjectEngineerReviewStatuses.Invalid)
            {
                message.Text = ReviewStrings.Get("MsgInvalid");
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
                statusBadge.Text = ReviewStrings.Get("StatusNone");
                statusBadge.ForeColor = ReviewUi.Muted;
                statusBadge.Tint = ReviewUi.SurfaceAlt;
                caseLabel.Text = queue.Count == 0 ? ReviewStrings.Get("NoReviewableCases") : ReviewStrings.Get("NoCaseSelected");
                toolTip.SetToolTip(statusBadge, ReviewStrings.Get("NoCaseToDisplayToolTip"));
                return;
            }
            caseLabel.Text = ReviewStrings.Format("CaseOf", (index + 1).ToString(CultureInfo.InvariantCulture), queue.Count.ToString(CultureInfo.InvariantCulture));
            statusBadge.Text = StatusGlyph(status) + " " + ReviewDisplayText.Status(status);
            statusBadge.ForeColor = StatusColor(status);
            statusBadge.Tint = StatusTint(status);
            string detail = status == ProjectEngineerReviewStatuses.Completed
                ? ReviewStrings.Get("StatusCompletedDetail")
                : status == ProjectEngineerReviewStatuses.Invalid
                    ? ReviewStrings.Get("StatusInvalidDetail")
                    : ReviewStrings.Get("StatusPendingDetail");
            toolTip.SetToolTip(statusBadge, detail);
        }

        private void UpdateQueueProgress()
        {
            int total = queue.Count;
            int completed = queue.Count(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Completed);
            progressLabel.Text = ReviewStrings.Format("ReviewedOf", completed.ToString(CultureInfo.InvariantCulture), total.ToString(CultureInfo.InvariantCulture));
            queueProgress.Percent = total == 0 ? 0 : (int)Math.Round(100.0 * completed / total);
            toolTip.SetToolTip(progressLabel, ReviewStrings.Format("ReviewedToolTip", completed.ToString(CultureInfo.InvariantCulture), total.ToString(CultureInfo.InvariantCulture)));
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
                ? ReviewStrings.Get("RemainingAllComplete")
                : ReviewStrings.Format(missing == 1 ? "RemainingSingular" : "RemainingPlural", missing.ToString(CultureInfo.InvariantCulture));
            remainingChip.ForeColor = missing == 0 ? ReviewUi.Completed : ReviewUi.Required;
            toolTip.SetToolTip(remainingChip, missing == 0
                ? ReviewStrings.Get("RemainingCompleteToolTip")
                : ReviewStrings.Get("RemainingPendingToolTip"));
        }

        private void UpdateCommentCount()
        {
            int length = comment.TextLength;
            commentCount.Text = ReviewStrings.Format("CommentCountFormat", length.ToString(CultureInfo.InvariantCulture));
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
            AddSummaryPair(summaryGrid, 0, 0, ReviewStrings.Get("KeyDrawing"), request.DrawingName);
            AddSummaryPair(summaryGrid, 1, 0, ReviewStrings.Get("KeyCase"), request.ReviewedCaseId);
            AddSummaryPair(summaryGrid, 0, 1, ReviewStrings.Get("KeyOperationalResult"), request.SoftwareResult ?? ReviewStrings.Get("ValueNotRecorded"));
            AddSummaryPair(summaryGrid, 1, 1, ReviewStrings.Get("KeyReviewReason"), request.ReviewReason);
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
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceReviewRequestId"), request.ReviewRequestId);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceRequestSha256"), request.RequestSha256);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceWorkflowSource"), request.WorkflowSourceType);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceReviewedCase"), request.ReviewedCaseId);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceDrawing"), request.DrawingName);
            if (!string.IsNullOrWhiteSpace(request.DrawingSha256)) added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceDrawingSha256"), request.DrawingSha256);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceSoftwareResult"), request.SoftwareResult);
            added += AddEvidenceRow(grid, ReviewStrings.Get("EvidenceSoftwareState"), request.SoftwareState);
            foreach (ProjectEngineerReviewEvidenceReference reference in request.EvidenceReferences ?? new List<ProjectEngineerReviewEvidenceReference>())
            {
                if (string.IsNullOrWhiteSpace(reference.Value)) continue;
                string key = !string.IsNullOrWhiteSpace(reference.Label) ? reference.Label : reference.Kind;
                added += AddEvidenceRow(grid, key, reference.Value);
            }
            evidenceCountChip.Text = ReviewStrings.Format("EvidenceCountFormat", added.ToString(CultureInfo.InvariantCulture));
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

        // The four question sections are governed upstream; the surface may present their titles
        // in the selected UI language while the underlying section identifiers stay unchanged.
        private static readonly Dictionary<string, string> QuestionSectionKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { ProjectEngineerReviewQuestions.SectionCriteriaUx, "SectionCriteriaUx" },
            { ProjectEngineerReviewQuestions.SectionResultsUx, "SectionResultsUx" },
            { ProjectEngineerReviewQuestions.SectionEngineeringInterpretation, "SectionEngineeringInterpretation" },
            { ProjectEngineerReviewQuestions.SectionUsability, "SectionUsability" }
        };

        private static string LocalizeSection(string section)
        {
            return QuestionSectionKeys.TryGetValue(section, out string key) ? ReviewStrings.Get(key) : section;
        }

        private void AddQuestionSectionHeader(TableLayoutPanel grid, string section)
        {
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, QuestionSectionHeight));
            string label = LocalizeSection(section);
            var strip = new Label
            {
                Text = label.ToUpperInvariant(),
                Dock = DockStyle.Fill, BackColor = ReviewUi.SurfaceAlt, ForeColor = ReviewUi.Section,
                Font = ReviewUi.SectionFont, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0), Margin = new Padding(0, 3, 0, 3), AutoEllipsis = true
            };
            toolTip.SetToolTip(strip, label);
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
                Text = ReviewDisplayText.Question(question), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.BodyFont, ForeColor = ReviewUi.Value, AutoEllipsis = true
            };
            toolTip.SetToolTip(label, ReviewDisplayText.Question(question) + ReviewStrings.Get(question.Required ? "RequiredSuffix" : "OptionalSuffix"));
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
                // items stay canonical so selection and persistence are byte-identical; only the
                // painted text is localized
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem += DrawAnswerItem;
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
            message.Text = ReviewStrings.Get("MsgNoPending");
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
                // save so the engineer sees where to look, not just that something is missing. The
                // canonical reason drives field mapping; the displayed text is localized.
                message.Text = ReviewStrings.Get("MsgNotSavedPrefix") + ReviewDisplayText.Reason(incomplete);
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
                message.Text = ReviewStrings.Format("MsgSavedFor", request.ReviewedCaseId);
                message.Severity = SeverityMessage.Success;
            }
            catch (Exception exception)
            {
                message.Text = ReviewStrings.Format("MsgSaveFailed", exception.Message);
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
                using (var path = ReviewStyle.RoundedPath(track, RailHeight / 2))
                using (var trackBrush = new SolidBrush(ReviewUi.SurfaceAlt)) g.FillPath(trackBrush, path);
                if (percent > 0)
                {
                    int fill = (int)Math.Round(Width * (percent / 100.0));
                    int barWidth = Math.Max(RailHeight, Math.Min(fill, Width));
                    // progress grows from the leading edge: left for LTR, right for RTL
                    Rectangle bar = RightToLeft == RightToLeft.Yes
                        ? new Rectangle(Width - barWidth, y, barWidth, RailHeight)
                        : new Rectangle(0, y, barWidth, RailHeight);
                    using (var barPath = ReviewStyle.RoundedPath(bar, RailHeight / 2))
                    using (var barBrush = new SolidBrush(ReviewUi.Completed)) g.FillPath(barBrush, barPath);
                }
            }
        }

        // The shared EngiSentry dark palette, fonts and presentation primitives live in ReviewUi.cs
        // and ReviewUiControls.cs so every engineer-facing surface (Results, project-engineer review,
        // placement resolution) renders from one definition.
    }
}
