#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using EngiSentry.Contracts;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// Engineer-assisted equipment placement surface. It presents only unresolved equipment
    /// placements and resolves them exclusively through an explicit engineer pick (a model-space
    /// entity pick, or an explicit free-space point). It reuses the accepted project-engineer
    /// review service for the review envelope and persists an append-only resolution record.
    ///
    /// Presentation only: the surface never infers a location, never snaps, never searches for a
    /// nearest entity, and never writes anything unless the engineer explicitly saves a complete,
    /// confirmed resolution. Cancelling or failing a pick writes nothing.
    ///
    /// The layout follows the shared EngiSentry dark visual language (ReviewUi + ReviewUiControls,
    /// the same primitives the accepted project-engineer review surface uses): a pinned header, a
    /// single scrolling column of bordered cards in workflow order, and a pinned action bar. The
    /// cards are Work item / proposal, Placement pick, Engineer review questions, Decision and
    /// Reviewer, so every field the engineer must fill is explicitly labelled.
    /// </summary>
    internal sealed class EquipmentPlacementResolutionControl : UserControl
    {
        // Base (96 DPI) metrics; every absolute size is passed through Scale() so the surface keeps
        // its proportions when AutoCAD hosts the palette on a 125% / 150% display. Fonts are set in
        // points, so without this the rows would stay pixel-fixed while the text grew.
        private const int HeaderHeightBase = 86;
        private const int FooterHeightBase = 78;
        private const int QuestionRowHeightBase = 30;
        private const int WorkItemBodyBase = 210;
        private const int PickBodyBase = 168;
        private const int DecisionBodyBase = 96;
        private const int ReviewerBodyBase = 132;
        private const int ListColumnBase = 300;
        private const int AnswerColumnBase = 150;
        private const int ButtonHeightBase = 28;
        private const int PickButtonRowBase = 34;

        private readonly string drawingName;
        private readonly string drawingPath;
        private readonly string drawingSha256;
        private readonly string insertionUnits;
        private readonly ProjectEngineerReviewService reviewService;
        private readonly EquipmentPlacementResolutionService resolutionService;
        private readonly PlacementProposalDispositionService dispositionService;
        private readonly IEquipmentPlacementPickProvider pickProvider;
        private readonly List<PlacementProposal> proposals;

        private readonly ListBox placementList = new ListBox();
        private readonly TextBox selectionInfo = new TextBox();
        private readonly TextBox pickInfo = new TextBox();
        private readonly Button pickEntity = new Button();
        private readonly Button pickFreeSpace = new Button();
        private readonly Button clearPick = new Button();
        private readonly TextBox reviewerName = new TextBox();
        private readonly TextBox reviewerRole = new TextBox();
        private readonly ComboBox classification = new ComboBox();
        private readonly ComboBox proposalDisposition = new ComboBox();
        private readonly TextBox comment = new TextBox();
        private readonly Button save = new Button();
        private readonly SeverityMessage message = new SeverityMessage();
        private readonly Label header = new Label();
        private readonly BadgeLabel countBadge = new BadgeLabel();
        private readonly TableLayoutPanel questionsGrid = new TableLayoutPanel();
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        // the Disposition field is only meaningful for a generated proposal, so the whole labelled
        // field (caption + combo) is shown/hidden together rather than leaving a stray caption
        private Panel dispositionField;

        private List<EquipmentPlacement> unresolved = new List<EquipmentPlacement>();
        private EquipmentPlacement current;
        private PlacementProposal currentProposal;
        private EquipmentPlacementPick pick;
        private bool loading;

        internal EquipmentPlacementResolutionControl(
            PlotPlanModel plotPlan, string drawingName, string drawingSha256, string insertionUnits,
            ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService,
            IEquipmentPlacementPickProvider pickProvider)
            : this(plotPlan, drawingName, drawingSha256, insertionUnits, reviewService, resolutionService, pickProvider, null, null)
        {
        }

        internal EquipmentPlacementResolutionControl(
            PlotPlanModel plotPlan,
            string drawingName,
            string drawingSha256,
            string insertionUnits,
            ProjectEngineerReviewService reviewService,
            EquipmentPlacementResolutionService resolutionService,
            IEquipmentPlacementPickProvider pickProvider,
            IReadOnlyList<PlacementProposal> proposals = null,
            string drawingPath = null)
        {
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            this.drawingName = drawingName ?? string.Empty;
            this.drawingPath = string.IsNullOrWhiteSpace(drawingPath) ? drawingName : drawingPath;
            this.drawingSha256 = drawingSha256 ?? string.Empty;
            this.insertionUnits = insertionUnits ?? string.Empty;
            this.reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
            this.resolutionService = resolutionService ?? throw new ArgumentNullException(nameof(resolutionService));
            dispositionService = resolutionService.DispositionService;
            this.pickProvider = pickProvider ?? throw new ArgumentNullException(nameof(pickProvider));
            this.proposals = (proposals ?? Array.Empty<PlacementProposal>()).Where(x => x != null && x.EngineeringReviewRequired
                && string.Equals(x.SourceDrawingSha256, drawingSha256, StringComparison.Ordinal)
                && string.Equals(x.ProposalFingerprint, PlacementProposalFingerprint.Compute(x), StringComparison.Ordinal))
                .OrderBy(x => x.ProposalId, StringComparer.Ordinal).ToList();
            // The queue is keyed by equipment identity: exactly one case per EquipmentEntityId, and
            // an identity whose placement is already Complete is never queued. Placement
            // occurrences are never presented as separate cases.
            unresolved = EquipmentPlacementResolutionQueue.Build(plotPlan.EquipmentPlacements);
            Dock = DockStyle.Fill;
            BuildLayout();
            RefreshPlacements();
        }

        internal int UnresolvedCount => unresolved.Count;
        internal EquipmentPlacementPick CurrentPick => pick;

        // ---- layout ---------------------------------------------------------------------------

        private int Scale(int value)
        {
            int dpi = DeviceDpi;
            if (dpi <= 0) dpi = 96;
            return Math.Max(1, (int)Math.Round(value * dpi / 96.0));
        }

        private void BuildLayout()
        {
            BackColor = ReviewUi.Background;
            ForeColor = ReviewUi.Text;
            Font = ReviewUi.BodyFont;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            // one pinned header row, one scrolling column of cards, one pinned action bar: the
            // status line and the Save action can never be scrolled out of view, and the card
            // column scrolls instead of clipping when the palette is short.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0),
                BackColor = ReviewUi.Background, Margin = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(HeaderHeightBase)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(FooterHeightBase)));

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
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            header.Dock = DockStyle.Fill;
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Font = ReviewUi.TitleFont;
            header.ForeColor = Color.White;
            header.BackColor = ReviewUi.Surface;
            header.AutoEllipsis = true;
            header.Text = "Equipment Placement Resolution";
            grid.Controls.Add(header, 0, 0);

            var subHeader = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.SubtitleFont, ForeColor = ReviewUi.Muted, BackColor = ReviewUi.Surface, AutoEllipsis = true,
                Text = "Drawing: " + drawingName + "   |   Units: " + (string.IsNullOrEmpty(insertionUnits) ? "<unresolved>" : insertionUnits) + "   |   SHA256: " + Short(drawingSha256)
            };
            grid.Controls.Add(subHeader, 0, 1);

            countBadge.Dock = DockStyle.Fill;
            countBadge.Font = ReviewUi.BadgeFont;
            countBadge.TextAlign = ContentAlignment.MiddleCenter;
            countBadge.BackColor = ReviewUi.Surface;
            countBadge.ForeColor = ReviewUi.Pending;
            countBadge.Tint = ReviewUi.PendingTint;
            countBadge.Text = string.Empty;
            grid.Controls.Add(countBadge, 1, 0);
            grid.SetRowSpan(countBadge, 2);

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

            Card workItem = BuildWorkItemCard();
            Card pickCard = BuildPickCard();
            Card questions = BuildQuestionsCard();
            Card decision = BuildDecisionCard();
            Card reviewer = BuildReviewerCard();

            // Docking claims edges in reverse add order, so the cards are added bottom-first to stack
            // top-down in workflow order: choose the case, capture the pick, answer the questions,
            // record the decision, identify the reviewer.
            reviewer.Dock = DockStyle.Top;
            decision.Dock = DockStyle.Top;
            questions.Dock = DockStyle.Top;
            pickCard.Dock = DockStyle.Top;
            workItem.Dock = DockStyle.Top;
            scroll.Controls.Add(reviewer);
            scroll.Controls.Add(decision);
            scroll.Controls.Add(questions);
            scroll.Controls.Add(pickCard);
            scroll.Controls.Add(workItem);
            root.Controls.Add(scroll, 0, 1);
        }

        // ---- cards ----------------------------------------------------------------------------

        // B. Work item / proposal: the queue on the left, the selected item's detail on the right.
        private Card BuildWorkItemCard()
        {
            var card = new Card("Work item / proposal", null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(ListColumnBase)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            placementList.Dock = DockStyle.Fill;
            placementList.IntegralHeight = false;
            placementList.BorderStyle = BorderStyle.FixedSingle;
            placementList.BackColor = ReviewUi.InputBack;
            placementList.ForeColor = ReviewUi.InputText;
            placementList.Font = ReviewUi.BodyFont;
            placementList.AccessibleName = "WorkItemList";
            placementList.SelectedIndexChanged += (s, e) => { if (!loading) SelectWorkItem(placementList.SelectedIndex); };
            grid.Controls.Add(ReviewStyle.FieldPair("Unresolved equipment / proposals", placementList, null, false), 0, 0);

            selectionInfo.Dock = DockStyle.Fill;
            selectionInfo.ReadOnly = true;
            selectionInfo.AccessibleName = "SelectedItemDetail";
            ReviewStyle.StyleTextBox(selectionInfo, ReviewUi.MonoFont, true);
            // long proposal ids and 64-char SHA-256 values must stay readable without wrapping
            selectionInfo.WordWrap = false;
            selectionInfo.ScrollBars = ScrollBars.Both;
            grid.Controls.Add(ReviewStyle.FieldPair("Selected item detail", selectionInfo, null, false), 1, 0);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(WorkItemBodyBase));
            return card;
        }

        // C. Placement pick: the captured pick evidence plus the explicit pick actions.
        private Card BuildPickCard()
        {
            var card = new Card("Placement pick", null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(PickButtonRowBase)));

            pickInfo.Dock = DockStyle.Fill;
            pickInfo.ReadOnly = true;
            pickInfo.AccessibleName = "CapturedPickEvidence";
            ReviewStyle.StyleTextBox(pickInfo, ReviewUi.MonoFont, true);
            pickInfo.WordWrap = false;
            pickInfo.ScrollBars = ScrollBars.Both;
            grid.Controls.Add(ReviewStyle.FieldPair("Captured pick evidence", pickInfo, null, false), 0, 0);

            var pickButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = ReviewUi.Surface, Margin = new Padding(0), Padding = new Padding(0) };
            pickEntity.Text = "Pick Entity"; pickFreeSpace.Text = "Pick Free Space"; clearPick.Text = "Clear Pick";
            pickEntity.AccessibleName = "PickEntity"; pickFreeSpace.AccessibleName = "PickFreeSpace"; clearPick.AccessibleName = "ClearPick";
            pickEntity.Width = Scale(112); pickFreeSpace.Width = Scale(140); clearPick.Width = Scale(100);
            pickEntity.Height = Scale(ButtonHeightBase); pickFreeSpace.Height = Scale(ButtonHeightBase); clearPick.Height = Scale(ButtonHeightBase);
            pickEntity.Margin = new Padding(0, 0, 8, 0); pickFreeSpace.Margin = new Padding(0, 0, 8, 0); clearPick.Margin = new Padding(0, 0, 0, 0);
            ReviewStyle.StyleButton(pickEntity, false);
            ReviewStyle.StyleButton(pickFreeSpace, false);
            ReviewStyle.StyleButton(clearPick, false);
            pickEntity.Click += (s, e) => DoPick(true);
            pickFreeSpace.Click += (s, e) => DoPick(false);
            clearPick.Click += (s, e) => { pick = null; UpdatePickInfo(); UpdateSaveState(); };
            pickButtons.Controls.Add(pickEntity); pickButtons.Controls.Add(pickFreeSpace); pickButtons.Controls.Add(clearPick);
            grid.Controls.Add(pickButtons, 0, 1);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(PickBodyBase));
            return card;
        }

        // D. Engineer review questions.
        private Card BuildQuestionsCard()
        {
            var card = new Card("Engineer review questions", null);
            var requiredNote = new Label
            {
                Text = "Fields marked * are required before a resolution can be saved.",
                Dock = DockStyle.Top, Height = 18, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, BackColor = ReviewUi.Surface, AutoEllipsis = true
            };
            card.Body.Controls.Add(BuildQuestions());
            card.Body.Controls.Add(requiredNote);
            card.Fit(18 + (EquipmentPlacementResolutionQuestions.All.Count * Scale(QuestionRowHeightBase)));
            return card;
        }

        // E. Decision: Classification and Disposition are each given an explicit visible caption.
        private Card BuildDecisionCard()
        {
            var card = new Card("Decision", null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            classification.AccessibleName = "Classification";
            classification.Items.Add(string.Empty);
            classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray());
            classification.SelectedIndex = 0;
            classification.SelectedIndexChanged += (s, e) => UpdateSaveState();
            ReviewStyle.StyleCombo(classification, ReviewUi.BodyFont);
            grid.Controls.Add(ReviewStyle.FieldPair("Classification", classification, ReviewStyle.Chip("REQUIRED", ReviewUi.Required), false), 0, 0);

            proposalDisposition.AccessibleName = "PlacementProposalDisposition";
            proposalDisposition.Items.Add(string.Empty);
            proposalDisposition.Items.AddRange(new object[] { PlacementProposalDispositions.Approved, PlacementProposalDispositions.Rejected, PlacementProposalDispositions.Modified, PlacementProposalDispositions.Cancelled });
            proposalDisposition.SelectedIndex = 0;
            proposalDisposition.SelectedIndexChanged += (s, e) => UpdateSaveState();
            ReviewStyle.StyleCombo(proposalDisposition, ReviewUi.BodyFont);
            dispositionField = ReviewStyle.FieldPair("Disposition", proposalDisposition, ReviewStyle.Chip("PROPOSAL ONLY", ReviewUi.Optional), false);
            dispositionField.Visible = false;
            grid.Controls.Add(dispositionField, 1, 0);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(DecisionBodyBase));
            return card;
        }

        // F. Reviewer: who is recording the resolution, and their comment.
        private Card BuildReviewerCard()
        {
            var card = new Card("Reviewer", null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            reviewerName.Dock = DockStyle.Fill;
            reviewerName.MaxLength = 120;
            reviewerName.AccessibleName = "ReviewerDisplayName";
            reviewerName.TextChanged += (s, e) => UpdateSaveState();
            ReviewStyle.StyleTextBox(reviewerName, ReviewUi.BodyFont, false);
            grid.Controls.Add(ReviewStyle.FieldPair("Reviewer name", reviewerName, ReviewStyle.Chip("REQUIRED", ReviewUi.Required), false), 0, 0);

            reviewerRole.Dock = DockStyle.Fill;
            reviewerRole.MaxLength = 120;
            reviewerRole.AccessibleName = "ReviewerRole";
            reviewerRole.TextChanged += (s, e) => UpdateSaveState();
            ReviewStyle.StyleTextBox(reviewerRole, ReviewUi.BodyFont, false);
            grid.Controls.Add(ReviewStyle.FieldPair("Role / title", reviewerRole, ReviewStyle.Chip("REQUIRED", ReviewUi.Required), false), 1, 0);

            comment.Dock = DockStyle.Fill;
            comment.AccessibleName = "EngineerComment";
            ReviewStyle.StyleTextBox(comment, ReviewUi.BodyFont, true);
            comment.ScrollBars = ScrollBars.Vertical;
            var commentField = ReviewStyle.FieldPair("Engineer comment", comment, null, false);
            grid.Controls.Add(commentField, 0, 1);
            grid.SetColumnSpan(commentField, 2);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(ReviewerBodyBase));
            return card;
        }

        private Control BuildQuestions()
        {
            questionsGrid.Dock = DockStyle.Fill;
            questionsGrid.BackColor = ReviewUi.Surface;
            questionsGrid.Margin = new Padding(0);
            questionsGrid.ColumnCount = 2;
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(AnswerColumnBase)));
            questionsGrid.RowCount = EquipmentPlacementResolutionQuestions.All.Count;
            for (int i = 0; i < EquipmentPlacementResolutionQuestions.All.Count; i++)
            {
                ProjectEngineerReviewQuestionDefinition question = EquipmentPlacementResolutionQuestions.All[i];
                questionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(QuestionRowHeightBase)));
                questionsGrid.Controls.Add(new Label
                {
                    Text = (question.Required ? "* " : "  ") + question.Text,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = ReviewUi.BodyFont,
                    ForeColor = question.Required ? ReviewUi.Text : ReviewUi.Muted,
                    BackColor = ReviewUi.Surface,
                    AutoEllipsis = true
                }, 0, i);
                Control answer;
                if (question.AnswerKind == ProjectEngineerAnswerKinds.FreeText)
                {
                    var box = new TextBox { Dock = DockStyle.Fill, MaxLength = 500, Tag = question.QuestionId, AccessibleName = question.QuestionId };
                    ReviewStyle.StyleTextBox(box, ReviewUi.BodyFont, false);
                    box.TextChanged += (s, e) => UpdateSaveState();
                    answer = box;
                }
                else
                {
                    var combo = new ComboBox { Dock = DockStyle.Fill, Tag = question.QuestionId, AccessibleName = question.QuestionId };
                    ReviewStyle.StyleCombo(combo, ReviewUi.BodyFont);
                    combo.Items.Add(string.Empty);
                    foreach (string value in ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)) combo.Items.Add(value);
                    combo.SelectedIndex = 0;
                    combo.SelectedIndexChanged += (s, e) => UpdateSaveState();
                    answer = combo;
                }
                questionsGrid.Controls.Add(answer, 1, i);
                answerControls[question.QuestionId] = answer;
            }
            return questionsGrid;
        }

        // G. Actions / status.
        private void BuildFooter(TableLayoutPanel root)
        {
            var host = new BorderedPanel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, TopLine = true,
                Padding = new Padding(12, 8, 12, 8)
            };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var hint = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, BackColor = ReviewUi.Surface, AutoEllipsis = true,
                Text = "Save is enabled only when the pick, the reviewer and the required answers are complete."
            };
            grid.Controls.Add(hint, 0, 0);

            save.Text = "Save Resolution";
            save.Dock = DockStyle.Fill;
            save.Margin = new Padding(1);
            save.Padding = new Padding(0);
            save.AccessibleName = "SaveResolution";
            save.Click += (s, e) => SaveResolution();
            ReviewStyle.StyleButton(save, true);
            grid.Controls.Add(save, 1, 0);

            message.Dock = DockStyle.Fill;
            message.AutoEllipsis = true;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Font = ReviewUi.BodyFont;
            message.ForeColor = ReviewUi.Value;
            message.BackColor = ReviewUi.Surface;
            message.Text = string.Empty;
            message.Severity = SeverityMessage.None;
            grid.Controls.Add(message, 0, 1);
            grid.SetColumnSpan(message, 2);

            host.Controls.Add(grid);
            root.Controls.Add(host, 0, 2);
        }

        // ---- state ----------------------------------------------------------------------------

        private void RefreshPlacements()
        {
            loading = true;
            placementList.Items.Clear();
            foreach (EquipmentPlacement placement in unresolved) placementList.Items.Add(Label(placement));
            foreach (PlacementProposal proposal in proposals)
            {
                string state = dispositionService.GetStatus(proposal).Disposition;
                placementList.Items.Add("PROPOSAL | " + proposal.NormalizedTag + " | " + state + " | " + proposal.ProposalId);
            }
            loading = false;
            UpdateCountBadge();
            if (placementList.Items.Count > 0) placementList.SelectedIndex = 0; else SelectWorkItem(-1);
        }

        private void UpdateCountBadge()
        {
            int open = placementList.Items.Count;
            countBadge.Text = open == 1 ? "1 OPEN ITEM" : open + " OPEN ITEMS";
            countBadge.ForeColor = open == 0 ? ReviewUi.Completed : ReviewUi.Pending;
            countBadge.Tint = open == 0 ? ReviewUi.CompletedTint : ReviewUi.PendingTint;
        }

        private static string Label(EquipmentPlacement placement) =>
            placement.NormalizedTag + "  |  " + placement.EquipmentEntityId + "  |  " + placement.PlacementStatus;

        private void SelectWorkItem(int index)
        {
            current = index >= 0 && index < unresolved.Count ? unresolved[index] : null;
            currentProposal = index >= unresolved.Count && index < unresolved.Count + proposals.Count ? proposals[index - unresolved.Count] : null;
            pick = null;
            if (current != null) selectionInfo.Text = Describe(current);
            else if (currentProposal != null) selectionInfo.Text = DescribeProposal(currentProposal, dispositionService.GetStatus(currentProposal));
            else selectionInfo.Text = "No unresolved equipment placement or generated proposal is selected.";
            proposalDisposition.SelectedIndex = 0;
            proposalDisposition.Visible = currentProposal != null;
            if (dispositionField != null) dispositionField.Visible = currentProposal != null;
            save.Text = currentProposal == null ? "Save Resolution" : "Record Disposition";
            UpdatePickInfo();
            UpdateSaveState();
        }

        private static string DescribeProposal(PlacementProposal proposal, PlacementProposalDispositionStatus status)
        {
            var text = new StringBuilder();
            text.AppendLine("Generated proposal: " + proposal.ProposalId);
            text.AppendLine("Disposition: " + status.Disposition);
            text.AppendLine("Equipment: " + proposal.NormalizedTag + " | " + proposal.EquipmentEntityId);
            text.AppendLine("Original point: " + proposal.OriginalEasting.ToString("R", CultureInfo.InvariantCulture) + ", " + proposal.OriginalNorthing.ToString("R", CultureInfo.InvariantCulture));
            text.AppendLine("Proposed point: " + proposal.ProposedEasting.ToString("R", CultureInfo.InvariantCulture) + ", " + proposal.ProposedNorthing.ToString("R", CultureInfo.InvariantCulture));
            text.AppendLine("Generator: " + proposal.GeneratorIdentity + " | Units: " + proposal.InsertionUnits);
            text.AppendLine("Proposal fingerprint: " + proposal.ProposalFingerprint);
            text.AppendLine("Review required: " + (proposal.EngineeringReviewRequired ? "YES" : "NO"));
            return text.ToString();
        }

        private static string Describe(EquipmentPlacement placement)
        {
            var text = new StringBuilder();
            text.AppendLine("Tag: " + placement.NormalizedTag);
            text.AppendLine("Equipment identity: " + placement.EquipmentEntityId);
            text.AppendLine("Placement: " + placement.PlacementId);
            text.AppendLine("Status: " + placement.PlacementStatus);
            text.AppendLine("Easting: " + placement.Easting.Status + "  |  Northing: " + placement.Northing.Status);
            text.AppendLine("Centerline evidence: " + (placement.CenterlineEvidence?.Status ?? "<none>"));
            text.AppendLine("Proximity candidates retained: E=" + (placement.Easting.Candidates?.Count ?? 0) + " N=" + (placement.Northing.Candidates?.Count ?? 0));
            return text.ToString();
        }

        private void DoPick(bool entity)
        {
            if (current == null && (currentProposal == null || proposalDisposition.SelectedItem as string != PlacementProposalDispositions.Modified)) { ShowMessage("Select an unresolved placement or choose MODIFIED for a proposal first.", true); return; }
            EquipmentPlacementPick result;
            try { result = entity ? pickProvider.PickEntity() : pickProvider.PickFreeSpace(); }
            catch (Exception exception) { ShowMessage("Pick failed: " + exception.Message, true); return; }
            if (result == null) { ShowMessage("Pick cancelled or invalid. Nothing was written.", true); return; }
            if (EquipmentPlacementPickFormatting.ToStoredDecimal(result.PointX) == null || EquipmentPlacementPickFormatting.ToStoredDecimal(result.PointY) == null)
            {
                ShowMessage("Picked point is outside the supported coordinate range. Nothing was written.", true);
                pick = null;
                UpdatePickInfo();
                UpdateSaveState();
                return;
            }
            pick = result;
            UpdatePickInfo();
            UpdateSaveState();
            ShowMessage("Pick captured. Complete the review and save.", false);
        }

        private void UpdatePickInfo()
        {
            if (pick == null) { pickInfo.Text = "No pick captured yet. Pick an entity or a free-space point."; return; }
            var text = new StringBuilder();
            text.AppendLine("Source: " + pick.Source);
            text.AppendLine("WCS point: " + EquipmentPlacementPickFormatting.Format(pick.PointX) + ", " + EquipmentPlacementPickFormatting.Format(pick.PointY) + ", " + EquipmentPlacementPickFormatting.Format(pick.PointZ));
            text.AppendLine("Proven handle: " + (string.IsNullOrEmpty(pick.Handle) ? "<none (free space)>" : pick.Handle));
            text.AppendLine("Proven layer: " + (string.IsNullOrEmpty(pick.Layer) ? "<none (free space)>" : pick.Layer));
            text.Append("Stored easting/northing: " + Stored(EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointX)) + " / " + Stored(EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointY)));
            pickInfo.Text = text.ToString();
        }

        private static string Stored(decimal? value) => value.HasValue ? EquipmentPlacementResolutionIdentity.CanonicalDecimal(value.Value) : "<out of range>";

        private void UpdateSaveState()
        {
            bool ready = (current != null ? pick != null : currentProposal != null && !string.IsNullOrWhiteSpace(proposalDisposition.SelectedItem as string))
                && !string.IsNullOrWhiteSpace(reviewerName.Text)
                && !string.IsNullOrWhiteSpace(reviewerRole.Text)
                && !string.IsNullOrWhiteSpace(classification.SelectedItem as string);
            if (ready && currentProposal != null)
            {
                string action = proposalDisposition.SelectedItem as string;
                bool needsPick = action == PlacementProposalDispositions.Modified;
                if (needsPick != (pick != null)) ready = false;
                if (string.IsNullOrWhiteSpace(comment.Text)) ready = false;
                if ((action == PlacementProposalDispositions.Rejected || action == PlacementProposalDispositions.Cancelled) && string.IsNullOrWhiteSpace(comment.Text)) ready = false;
                if (action == PlacementProposalDispositions.Approved
                    && (!EquipmentPlacementPickFormatting.ToStoredDecimal(currentProposal.ProposedEasting).HasValue || !EquipmentPlacementPickFormatting.ToStoredDecimal(currentProposal.ProposedNorthing).HasValue)) ready = false;
            }
            if (ready)
            {
                foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
                {
                    if (!question.Required) continue;
                    if (!answerControls.TryGetValue(question.QuestionId, out Control control)) { ready = false; break; }
                    string value = control is ComboBox combo ? (combo.SelectedIndex > 0 ? (string)combo.SelectedItem : string.Empty) : control.Text;
                    if (string.IsNullOrWhiteSpace(value)) { ready = false; break; }
                }
            }
            save.Enabled = ready;
        }

        private void SaveResolution()
        {
            if (currentProposal != null) { SaveProposalDisposition(); return; }
            if (current == null) { ShowMessage("Select an unresolved equipment placement first.", true); return; }
            if (pick == null) { ShowMessage("Capture an explicit engineer pick before saving.", true); return; }
            decimal? easting = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointX);
            decimal? northing = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointY);
            if (!easting.HasValue || !northing.HasValue) { ShowMessage("The picked point is outside the supported coordinate range. Nothing was written.", true); return; }
            if (string.IsNullOrWhiteSpace(drawingSha256)) { ShowMessage("The drawing hash is unavailable; save the drawing and re-run the command.", true); return; }
            if (string.IsNullOrWhiteSpace(insertionUnits)) { ShowMessage("The drawing insertion units are unresolved; a resolution cannot be recorded.", true); return; }

            EquipmentPlacement target = current;
            DateTime responseTimestamp = DateTime.UtcNow;
            ProjectEngineerReviewRequest request = BuildRequest(target, pick, easting.Value, northing.Value, responseTimestamp);
            Dictionary<string, string> answers = CaptureAnswers();
            string incomplete = ProjectEngineerReviewService.FindIncompleteReason(request, reviewerName.Text, reviewerRole.Text, answers, classification.SelectedItem as string);
            if (incomplete != null) { ShowMessage("Not saved: " + incomplete, true); return; }

            var response = new ProjectEngineerReviewResponse
            {
                SchemaVersion = ProjectEngineerReviewSchema.Current,
                ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256,
                DrawingName = request.DrawingName,
                ReviewedCaseId = request.ReviewedCaseId,
                ReviewerDisplayName = reviewerName.Text.Trim(),
                ReviewerRole = reviewerRole.Text.Trim(),
                ResponseTimestampUtc = responseTimestamp,
                Answers = answers.Select(x => new ProjectEngineerReviewAnswer { QuestionId = x.Key, AnswerText = x.Value }).ToList(),
                EngineerComment = string.IsNullOrWhiteSpace(comment.Text) ? null : comment.Text,
                Classification = classification.SelectedItem as string
            };
            try
            {
                reviewService.SaveResponse(request, response);
                EquipmentPlacementResolutionRecord record = EquipmentPlacementResolutionBuilder.Build(
                    drawingName, drawingSha256, target.EquipmentEntityId, target.NormalizedTag,
                    easting.Value, northing.Value, insertionUnits,
                    pick.Source, pick.Handle, pick.Layer, pick.PointX, pick.PointY, pick.PointZ,
                    request, response, DateTime.UtcNow);
                resolutionService.Save(record);
            }
            catch (Exception exception)
            {
                ShowMessage("Not saved: " + exception.Message, true);
                return;
            }

            // A saved resolution resolves the whole equipment identity, so every queue entry for
            // that identity leaves the queue — not just the one that was displayed.
            EquipmentPlacementResolutionQueue.RemoveForEntity(unresolved, target.EquipmentEntityId);
            RefreshPlacements();
            ShowMessage("Resolution saved for " + target.NormalizedTag + ". A fresh plot-plan QA will mark it Complete.", false);
        }

        private void SaveProposalDisposition()
        {
            PlacementProposal proposal = currentProposal;
            if (proposal == null) return;
            string action = proposalDisposition.SelectedItem as string;
            string latestDrawingSha = drawingSha256;
            try
            {
                latestDrawingSha = HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath);
                if (!string.Equals(latestDrawingSha, drawingSha256, StringComparison.Ordinal)) throw new InvalidOperationException("The drawing changed after this proposal was loaded; rerun ES_SUGGEST and ES_PLACEMENTRESOLVE.");
                PlacementProposal latest = PlacementProposalSnapshotWriter.ReadCurrentForDrawing(drawingName, latestDrawingSha)
                    .SingleOrDefault(x => string.Equals(x.ProposalId, proposal.ProposalId, StringComparison.Ordinal));
                if (latest == null || !string.Equals(latest.ProposalFingerprint, proposal.ProposalFingerprint, StringComparison.Ordinal))
                    throw new InvalidOperationException("The proposal is stale or has changed; reload the current proposal before recording a disposition.");

                PlacementProposalSelectedPlacement selected = null;
                EquipmentPlacementPick resolutionPick = null;
                if (action == PlacementProposalDispositions.Approved)
                {
                    decimal easting = EquipmentPlacementPickFormatting.ToStoredDecimal(proposal.ProposedEasting).Value;
                    decimal northing = EquipmentPlacementPickFormatting.ToStoredDecimal(proposal.ProposedNorthing).Value;
                    selected = BuildSelectedPlacement(proposal, easting, northing, EquipmentPlacementResolutionSources.ProposalApproval, null, null, proposal.ProposedEasting, proposal.ProposedNorthing, 0d);
                    resolutionPick = new EquipmentPlacementPick { Source = selected.Source, PointX = selected.PickedPointX, PointY = selected.PickedPointY, PointZ = selected.PickedPointZ };
                }
                else if (action == PlacementProposalDispositions.Modified)
                {
                    decimal easting = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointX).Value;
                    decimal northing = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointY).Value;
                    selected = BuildSelectedPlacement(proposal, easting, northing, pick.Source, pick.Handle, pick.Layer, pick.PointX, pick.PointY, pick.PointZ);
                    resolutionPick = pick;
                }

                DateTime responseTimestamp = DateTime.UtcNow;
                ProjectEngineerReviewRequest request = BuildProposalRequest(proposal, action, responseTimestamp);
                Dictionary<string, string> answers = CaptureAnswers();
                string incomplete = ProjectEngineerReviewService.FindIncompleteReason(request, reviewerName.Text, reviewerRole.Text, answers, classification.SelectedItem as string);
                if (incomplete != null) throw new InvalidOperationException(incomplete);
                var response = new ProjectEngineerReviewResponse
                {
                    SchemaVersion = ProjectEngineerReviewSchema.Current,
                    ReviewRequestId = request.ReviewRequestId,
                    RequestSha256 = request.RequestSha256,
                    DrawingName = request.DrawingName,
                    ReviewedCaseId = request.ReviewedCaseId,
                    ReviewerDisplayName = reviewerName.Text.Trim(),
                    ReviewerRole = reviewerRole.Text.Trim(),
                    ResponseTimestampUtc = responseTimestamp,
                    Answers = answers.Select(x => new ProjectEngineerReviewAnswer { QuestionId = x.Key, AnswerText = x.Value }).ToList(),
                    EngineerComment = comment.Text.Trim(),
                    Classification = classification.SelectedItem as string
                };
                reviewService.SaveResponse(request, response);
                PlacementProposalDispositionStatus currentStatus = dispositionService.GetStatus(proposal);
                PlacementProposalDispositionRecord disposition = dispositionService.Record(
                    proposal, proposal.ProposalFingerprint, latest.ProposalFingerprint, action, selected,
                    comment.Text.Trim(), reviewerName.Text.Trim(), reviewerRole.Text.Trim(), currentStatus.CurrentRecordId,
                    request, response, DateTime.UtcNow);
                if (action == PlacementProposalDispositions.Approved || action == PlacementProposalDispositions.Modified)
                {
                    EquipmentPlacementResolutionRecord resolution = EquipmentPlacementResolutionBuilder.Build(
                        drawingName, drawingSha256, proposal.EquipmentEntityId, proposal.NormalizedTag,
                        selected.Easting, selected.Northing, proposal.InsertionUnits,
                        resolutionPick.Source, resolutionPick.Handle, resolutionPick.Layer,
                        resolutionPick.PointX, resolutionPick.PointY, resolutionPick.PointZ,
                        request, response, DateTime.UtcNow,
                        proposal.ProposalId, proposal.ProposalFingerprint, disposition.RecordId, selected.PlacementFingerprint);
                    PlacementProposalDispositionService.BindResolutionToDisposition(resolution, disposition);
                    resolutionService.Save(resolution);
                }
                selectionInfo.Text = DescribeProposal(proposal, dispositionService.GetStatus(proposal));
                UpdateSaveState();
                ShowMessage("Engineer disposition recorded: " + action + ".", false);
            }
            catch (Exception exception) { ShowMessage("Disposition not recorded: " + exception.Message, true); }
        }

        private ProjectEngineerReviewRequest BuildProposalRequest(PlacementProposal proposal, string action, DateTime timestampUtc)
        {
            var evidence = new List<ProjectEngineerReviewEvidenceReference>
            {
                new ProjectEngineerReviewEvidenceReference { Kind = "PlacementProposalId", Label = "Generated proposal identity", Value = proposal.ProposalId },
                new ProjectEngineerReviewEvidenceReference { Kind = "PlacementProposalFingerprint", Label = "Immutable proposal fingerprint", Value = proposal.ProposalFingerprint },
                new ProjectEngineerReviewEvidenceReference { Kind = "PlacementId", Label = "Equipment placement identity", Value = proposal.PlacementId },
                new ProjectEngineerReviewEvidenceReference { Kind = "EquipmentEntityId", Label = "Equipment identity", Value = proposal.EquipmentEntityId },
                new ProjectEngineerReviewEvidenceReference { Kind = "OriginalPoint", Label = "Original equipment point", Value = proposal.OriginalEasting.ToString("R", CultureInfo.InvariantCulture) + ", " + proposal.OriginalNorthing.ToString("R", CultureInfo.InvariantCulture) },
                new ProjectEngineerReviewEvidenceReference { Kind = "ProposedPoint", Label = "Generated proposed point", Value = proposal.ProposedEasting.ToString("R", CultureInfo.InvariantCulture) + ", " + proposal.ProposedNorthing.ToString("R", CultureInfo.InvariantCulture) },
                new ProjectEngineerReviewEvidenceReference { Kind = "GeneratorIdentity", Label = "Deterministic generator", Value = proposal.GeneratorIdentity },
                new ProjectEngineerReviewEvidenceReference { Kind = "EngineerDisposition", Label = "Engineer disposition", Value = action },
                new ProjectEngineerReviewEvidenceReference { Kind = "DispositionTimestamp", Label = "Decision capture time (UTC)", Value = timestampUtc.ToString("O", CultureInfo.InvariantCulture) }
            };
            return reviewService.CreateRequest(ProjectEngineerReviewService.WorkflowSourcePlacementProposal, drawingName, drawingSha256,
                "P11.1-" + proposal.ProposalId, "Engineer disposition of generated placement proposal " + proposal.ProposalId,
                evidence, "PLACEMENT_PROPOSAL_REVIEW", action, EquipmentPlacementResolutionQuestions.All);
        }

        private static PlacementProposalSelectedPlacement BuildSelectedPlacement(PlacementProposal proposal, decimal easting, decimal northing, string source, string sourceHandle, string sourceLayer, double pointX, double pointY, double pointZ)
        {
            var selected = new PlacementProposalSelectedPlacement
            {
                EquipmentEntityId = proposal.EquipmentEntityId,
                NormalizedTag = proposal.NormalizedTag,
                Easting = easting,
                Northing = northing,
                InsertionUnits = proposal.InsertionUnits,
                Source = source,
                SourceHandle = sourceHandle,
                SourceLayer = sourceLayer,
                PickedPointX = pointX,
                PickedPointY = pointY,
                PickedPointZ = pointZ
            };
            selected.PlacementFingerprint = PlacementProposalDispositionService.ComputeSelectedPlacementFingerprint(selected);
            return selected;
        }

        private ProjectEngineerReviewRequest BuildRequest(EquipmentPlacement target, EquipmentPlacementPick captured, decimal easting, decimal northing, DateTime responseTimestamp)
        {
            // The confirmation timestamp is part of the review evidence, so every confirmation is a
            // distinct request/response. A new confirmation therefore never replaces an earlier one.
            var evidence = new List<ProjectEngineerReviewEvidenceReference>
            {
                new ProjectEngineerReviewEvidenceReference { Kind = "DrawingSha256", Label = "Pre/post SHA-256", Value = drawingSha256 },
                new ProjectEngineerReviewEvidenceReference { Kind = "ConfirmationTimestamp", Label = "Confirmation timestamp (UTC)", Value = responseTimestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) },
                new ProjectEngineerReviewEvidenceReference { Kind = "EquipmentEntityId", Label = "Equipment identity", Value = target.EquipmentEntityId },
                new ProjectEngineerReviewEvidenceReference { Kind = "EquipmentTag", Label = "Equipment tag", Value = target.NormalizedTag },
                new ProjectEngineerReviewEvidenceReference { Kind = "PickSource", Label = "Pick source", Value = captured.Source },
                new ProjectEngineerReviewEvidenceReference { Kind = "PickedPoint", Label = "Picked WCS point", Value = EquipmentPlacementPickFormatting.Format(captured.PointX) + ", " + EquipmentPlacementPickFormatting.Format(captured.PointY) + ", " + EquipmentPlacementPickFormatting.Format(captured.PointZ) },
                new ProjectEngineerReviewEvidenceReference { Kind = "Easting", Label = "Stored easting", Value = EquipmentPlacementResolutionIdentity.CanonicalDecimal(easting) },
                new ProjectEngineerReviewEvidenceReference { Kind = "Northing", Label = "Stored northing", Value = EquipmentPlacementResolutionIdentity.CanonicalDecimal(northing) },
                new ProjectEngineerReviewEvidenceReference { Kind = "InsertionUnits", Label = "Drawing units", Value = insertionUnits }
            };
            if (!string.IsNullOrWhiteSpace(captured.Handle)) evidence.Add(new ProjectEngineerReviewEvidenceReference { Kind = "SourceHandle", Label = "Proven handle", Value = captured.Handle });
            if (!string.IsNullOrWhiteSpace(captured.Layer)) evidence.Add(new ProjectEngineerReviewEvidenceReference { Kind = "SourceLayer", Label = "Proven layer", Value = captured.Layer });

            return reviewService.CreateRequest(
                ProjectEngineerReviewService.WorkflowSourceEquipmentPlacement,
                drawingName,
                drawingSha256,
                "EQUIP-" + target.EquipmentEntityId,
                "Engineer-assisted placement resolution for " + target.NormalizedTag + " (" + target.EquipmentEntityId + ").",
                evidence,
                "UNRESOLVED_EQUIPMENT_PLACEMENT",
                "ENGINEER_CONFIRMATION",
                EquipmentPlacementResolutionQuestions.All);
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

        private void ShowMessage(string text, bool warning)
        {
            message.Text = text;
            // normal workflow feedback is an inline status line, never a modal dialog; the severity
            // only selects the readable glyph/color, the message text stays machine-readable
            message.Severity = warning ? SeverityMessage.Warning : SeverityMessage.Success;
        }

        private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length <= 16 ? value : value.Substring(0, 16) + "...";
    }
}
