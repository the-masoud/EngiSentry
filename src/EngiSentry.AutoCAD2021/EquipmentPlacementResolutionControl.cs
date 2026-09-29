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
    /// cards are Equipment / Proposal, Placement Selection, Engineer Review, Decision, Reviewer and
    /// a collapsed-by-default Technical Details section, so every field the engineer must fill is
    /// explicitly labelled and the long identifiers stay out of the primary flow.
    ///
    /// The surface is bilingual: every engineer-facing string is read from the shared
    /// ReviewStrings resources (English neutral / Persian fa), the language can be switched live
    /// from the header, and the choice is persisted per Windows user by ReviewUiPreferences. The
    /// governed values (question texts, answer vocabularies, classifications, dispositions) keep
    /// their exact upstream tokens for storage, matching and persistence; only the shown text runs
    /// through ReviewDisplayText. Technical values (hashes, IDs, coordinates) always stay LTR.
    /// </summary>
    internal sealed class EquipmentPlacementResolutionControl : UserControl
    {
        // Base (96 DPI) metrics; every absolute size is passed through Scale() so the surface keeps
        // its proportions when AutoCAD hosts the palette on a 125% / 150% display. Fonts are set in
        // points, so without this the rows would stay pixel-fixed while the text grew.
        private const int HeaderHeightBase = 86;
        private const int FooterHeightBase = 104;
        private const int QuestionRowHeightBase = 30;
        private const int InfoRowBase = 22;
        private const int InfoCaptionWidthBase = 156;
        private const int DecisionBodyBase = 96;
        private const int ReviewerBodyBase = 132;
        private const int ListColumnBase = 300;
        private const int AnswerColumnBase = 150;
        private const int ButtonHeightBase = 28;
        private const int PickButtonRowBase = 34;
        private const int TechnicalToggleRowBase = 30;
        private const int TechnicalBodyBase = 236;

        private readonly string drawingName;
        private readonly string drawingPath;
        private readonly string drawingSha256;
        private readonly string insertionUnits;
        private readonly ProjectEngineerReviewService reviewService;
        private readonly EquipmentPlacementResolutionService resolutionService;
        private readonly PlacementProposalDispositionService dispositionService;
        private readonly IEquipmentPlacementPickProvider pickProvider;
        private readonly List<PlacementProposal> proposals;

        private ListBox placementList;
        private Label tagLabel;
        private TableLayoutPanel summaryGrid;
        private TableLayoutPanel pickGrid;
        private Button pickEntity;
        private Button pickFreeSpace;
        private Button clearPick;
        private TextBox reviewerName;
        private TextBox reviewerRole;
        private ComboBox classification;
        private ComboBox proposalDisposition;
        private TextBox comment;
        private Button save;
        private Button technicalToggle;
        private Panel technicalBody;
        private TableLayoutPanel technicalGrid;
        private SeverityMessage message;
        private Label whyLabel;
        private Label header;
        private Label subHeader;
        private BadgeLabel countBadge;
        private LinkLabel languageLink;
        private TableLayoutPanel questionsGrid;
        private Card workItemCard;
        private Card pickCard;
        private Card technicalCard;
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        // the Disposition field is only meaningful for a generated proposal, so the whole labelled
        // field (caption + combo) is shown/hidden together rather than leaving a stray caption
        private Panel dispositionField;

        private List<EquipmentPlacement> unresolved = new List<EquipmentPlacement>();
        private EquipmentPlacement current;
        private PlacementProposal currentProposal;
        private EquipmentPlacementPick pick;
        private bool loading;
        private bool technicalExpanded;
        private string lastResolutionId;
        private string lastDispositionId;

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
            CreateControls();
            BuildLayout();
            RefreshPlacements();
        }

        internal int UnresolvedCount => unresolved.Count;
        internal EquipmentPlacementPick CurrentPick => pick;

        // ---- control construction -------------------------------------------------------------

        // (Re)creates every control so a UI-language switch can rebuild the whole surface with the
        // current culture's resources, fonts and layout direction. Engineering state is preserved
        // across a rebuild by Rebuild().
        private void CreateControls()
        {
            placementList = new ListBox();
            tagLabel = new Label();
            summaryGrid = new TableLayoutPanel();
            pickGrid = new TableLayoutPanel();
            pickEntity = new Button();
            pickFreeSpace = new Button();
            clearPick = new Button();
            reviewerName = new TextBox();
            reviewerRole = new TextBox();
            classification = new ComboBox();
            proposalDisposition = new ComboBox();
            comment = new TextBox();
            save = new Button();
            technicalToggle = new Button();
            technicalBody = new Panel();
            technicalGrid = new TableLayoutPanel();
            message = new SeverityMessage();
            whyLabel = new Label();
            header = new Label();
            subHeader = new Label();
            countBadge = new BadgeLabel();
            languageLink = new LinkLabel();
            questionsGrid = new TableLayoutPanel();
            dispositionField = null;
        }

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

            // Standard WinForms right-to-left support: the whole surface is mirrored for Persian so
            // docking, column order and text alignment flip automatically. Latin identifiers, hashes
            // and coordinates stay LTR inside the mirrored surface.
            RightToLeft = ReviewLocalization.IsPersian ? RightToLeft.Yes : RightToLeft.No;

            // one pinned header row, one scrolling column of cards, one pinned action bar: the
            // status line and the primary action can never be scrolled out of view, and the card
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

        // Rebuilds the surface after a UI-language change: the current selection, the captured pick,
        // any unsaved entries and the technical-details state are captured first, the control tree is
        // recreated with the new culture's resources, fonts and direction, then everything is
        // restored. Nothing is persisted by the rebuild — a resolution still stores only on an
        // explicit Record Decision.
        internal void Rebuild()
        {
            int restoreIndex = placementList.SelectedIndex;
            EquipmentPlacementPick draftPick = pick;
            string draftName = reviewerName.Text;
            string draftRole = reviewerRole.Text;
            string draftComment = comment.Text;
            string draftClassification = classification.SelectedItem as string ?? string.Empty;
            string draftDisposition = proposalDisposition.SelectedItem as string ?? string.Empty;
            Dictionary<string, string> draftAnswers = CaptureAnswers();
            bool draftTechnical = technicalExpanded;

            SuspendLayout();
            try
            {
                ClearControls(this);
                CreateControls();
                BuildLayout();
            }
            finally { ResumeLayout(); }

            loading = true;
            try
            {
                RefreshPlacements();
                if (restoreIndex >= 0 && restoreIndex < placementList.Items.Count) placementList.SelectedIndex = restoreIndex;
            }
            finally { loading = false; }

            // restore the engineer's unsaved screen entries onto the fresh controls
            loading = true;
            try
            {
                pick = draftPick;
                reviewerName.Text = draftName;
                reviewerRole.Text = draftRole;
                comment.Text = draftComment;
                classification.SelectedItem = draftClassification;
                if (classification.SelectedIndex < 0) classification.SelectedIndex = 0;
                proposalDisposition.SelectedItem = draftDisposition;
                if (proposalDisposition.SelectedIndex < 0) proposalDisposition.SelectedIndex = 0;
                foreach (KeyValuePair<string, string> pair in draftAnswers)
                {
                    if (!answerControls.TryGetValue(pair.Key, out Control control)) continue;
                    if (control is ComboBox combo) combo.SelectedItem = pair.Value;
                    else control.Text = pair.Value;
                    if (control is ComboBox c && c.SelectedIndex < 0) c.SelectedIndex = 0;
                }
            }
            finally { loading = false; }

            SetTechnicalExpanded(draftTechnical);
            UpdatePickInfo();
            UpdateSummary();
            UpdateTechnicalDetails();
            UpdateSaveState();
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
            ReviewUiPreferences.Save(ReviewLocalization.Current);
            Rebuild();
        }

        private void BuildHeader(TableLayoutPanel root)
        {
            var host = new BorderedPanel
            {
                Dock = DockStyle.Fill, BackColor = ReviewUi.Surface, BottomLine = true,
                Padding = new Padding(18, 10, 14, 10)
            };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(150)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(170)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            header.Dock = DockStyle.Fill;
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Font = ReviewUi.TitleFont;
            header.ForeColor = Color.White;
            header.BackColor = ReviewUi.Surface;
            header.AutoEllipsis = true;
            header.Text = ReviewStrings.Get("Placement_AppTitle");
            grid.Controls.Add(header, 0, 0);

            // drawing name and unit symbols are never localized; the full SHA-256 lives in the
            // collapsed Technical Details section instead of dominating the header
            subHeader.Dock = DockStyle.Fill;
            subHeader.TextAlign = ContentAlignment.MiddleLeft;
            subHeader.Font = ReviewUi.SubtitleFont;
            subHeader.ForeColor = ReviewUi.Muted;
            subHeader.BackColor = ReviewUi.Surface;
            subHeader.AutoEllipsis = true;
            subHeader.Text = ReviewStrings.Get("Placement_HeaderDrawing") + ": " + drawingName
                + "   |   " + ReviewStrings.Get("Placement_HeaderUnits") + ": "
                + (string.IsNullOrEmpty(insertionUnits) ? ReviewStrings.Get("Placement_ValueUnresolved") : insertionUnits);
            grid.Controls.Add(subHeader, 0, 1);

            // the language selector names the language the engineer can switch to, so it always reads
            // as an available action and never needs Windows or AutoCAD to be reconfigured
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
            languageLink.LinkClicked += (s, e) => SwitchLanguage();
            var tooltip = new ToolTip { AutoPopDelay = 8000 };
            tooltip.SetToolTip(languageLink, ReviewStrings.Get("Placement_LanguageToolTip"));
            grid.Controls.Add(languageLink, 1, 0);
            grid.SetRowSpan(languageLink, 2);

            countBadge.Dock = DockStyle.Fill;
            countBadge.Font = ReviewUi.BadgeFont;
            countBadge.TextAlign = ContentAlignment.MiddleCenter;
            countBadge.BackColor = ReviewUi.Surface;
            countBadge.ForeColor = ReviewUi.Pending;
            countBadge.Tint = ReviewUi.PendingTint;
            countBadge.Text = string.Empty;
            grid.Controls.Add(countBadge, 2, 0);
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

            workItemCard = BuildWorkItemCard();
            pickCard = BuildPickCard();
            Card questions = BuildQuestionsCard();
            Card decision = BuildDecisionCard();
            Card reviewer = BuildReviewerCard();
            technicalCard = BuildTechnicalCard();

            // Docking claims edges in reverse add order, so the cards are added bottom-first to stack
            // top-down in workflow order: choose the case, capture the pick, answer the questions,
            // record the decision, identify the reviewer, then the collapsed technical reference.
            technicalCard.Dock = DockStyle.Top;
            reviewer.Dock = DockStyle.Top;
            decision.Dock = DockStyle.Top;
            questions.Dock = DockStyle.Top;
            pickCard.Dock = DockStyle.Top;
            workItemCard.Dock = DockStyle.Top;
            scroll.Controls.Add(technicalCard);
            scroll.Controls.Add(reviewer);
            scroll.Controls.Add(decision);
            scroll.Controls.Add(questions);
            scroll.Controls.Add(pickCard);
            scroll.Controls.Add(workItemCard);
            root.Controls.Add(scroll, 0, 1);
        }

        // ---- cards ----------------------------------------------------------------------------

        // B. Equipment / Proposal: the queue on the left, the selected item's summary on the right.
        private Card BuildWorkItemCard()
        {
            var card = new Card(ReviewStrings.Get("Placement_SectionEquipment"), null);
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
            // the queue holds tags and identities: technical values that always read left-to-right
            placementList.RightToLeft = RightToLeft.No;
            placementList.SelectedIndexChanged += (s, e) => { if (!loading) SelectWorkItem(placementList.SelectedIndex); };
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldQueue"), placementList, null, false), 0, 0);

            tagLabel.Dock = DockStyle.Top;
            tagLabel.Height = Scale(26);
            tagLabel.TextAlign = ContentAlignment.MiddleLeft;
            tagLabel.Font = ReviewUi.ValueFont;
            tagLabel.ForeColor = ReviewUi.Text;
            tagLabel.BackColor = ReviewUi.Surface;
            tagLabel.AutoEllipsis = true;

            summaryGrid.Dock = DockStyle.Fill;
            summaryGrid.BackColor = ReviewUi.Surface;
            summaryGrid.Margin = new Padding(0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(26)));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(tagLabel, 0, 0);
            right.Controls.Add(summaryGrid, 0, 1);
            grid.Controls.Add(right, 1, 0);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(158));
            return card;
        }

        // C. Placement Selection: the captured pick evidence plus the explicit pick actions.
        private Card BuildPickCard()
        {
            var card = new Card(ReviewStrings.Get("Placement_SectionSelection"), null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(PickButtonRowBase)));

            pickGrid.Dock = DockStyle.Fill;
            pickGrid.BackColor = ReviewUi.Surface;
            pickGrid.Margin = new Padding(0);
            grid.Controls.Add(pickGrid, 0, 0);

            var pickButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = ReviewUi.Surface, Margin = new Padding(0), Padding = new Padding(0) };
            pickEntity.Text = ReviewStrings.Get("Placement_ButtonPickEquipment");
            pickFreeSpace.Text = ReviewStrings.Get("Placement_ButtonPickFreeSpace");
            clearPick.Text = ReviewStrings.Get("Placement_ButtonClear");
            pickEntity.AccessibleName = "PickEntity"; pickFreeSpace.AccessibleName = "PickFreeSpace"; clearPick.AccessibleName = "ClearPick";
            pickEntity.Width = Scale(180); pickFreeSpace.Width = Scale(180); clearPick.Width = Scale(150);
            pickEntity.Height = Scale(ButtonHeightBase); pickFreeSpace.Height = Scale(ButtonHeightBase); clearPick.Height = Scale(ButtonHeightBase);
            pickEntity.Margin = new Padding(0, 0, 8, 0); pickFreeSpace.Margin = new Padding(0, 0, 8, 0); clearPick.Margin = new Padding(0, 0, 0, 0);
            ReviewStyle.StyleButton(pickEntity, false);
            ReviewStyle.StyleButton(pickFreeSpace, false);
            ReviewStyle.StyleButton(clearPick, false);
            pickEntity.Click += (s, e) => DoPick(true);
            pickFreeSpace.Click += (s, e) => DoPick(false);
            clearPick.Click += (s, e) => { pick = null; UpdatePickInfo(); UpdateSummary(); UpdateTechnicalDetails(); UpdateSaveState(); ShowMessage(ReviewStrings.Get("Placement_StatusPickCleared"), false); };
            pickButtons.Controls.Add(pickEntity); pickButtons.Controls.Add(pickFreeSpace); pickButtons.Controls.Add(clearPick);
            grid.Controls.Add(pickButtons, 0, 1);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(172));
            return card;
        }

        // D. Engineer review questions.
        private Card BuildQuestionsCard()
        {
            var card = new Card(ReviewStrings.Get("Placement_SectionReview"), null);
            var requiredNote = new Label
            {
                Text = ReviewStrings.Get("Placement_RequiredNote"),
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
            var card = new Card(ReviewStrings.Get("Placement_SectionDecision"), null);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            classification.AccessibleName = "Classification";
            classification.Items.Add(string.Empty);
            classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray());
            classification.SelectedIndex = 0;
            classification.SelectedIndexChanged += (s, e) => UpdateSaveState();
            StyleLocalizedCombo(classification, ReviewDisplayText.Classification);
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldClassification"), classification, ReviewStyle.Chip(ReviewStrings.Get("Placement_ChipRequired"), ReviewUi.Required), false), 0, 0);

            proposalDisposition.AccessibleName = "PlacementProposalDisposition";
            proposalDisposition.Items.Add(string.Empty);
            proposalDisposition.Items.AddRange(new object[] { PlacementProposalDispositions.Approved, PlacementProposalDispositions.Rejected, PlacementProposalDispositions.Modified, PlacementProposalDispositions.Cancelled });
            proposalDisposition.SelectedIndex = 0;
            proposalDisposition.SelectedIndexChanged += (s, e) => UpdateSaveState();
            StyleLocalizedCombo(proposalDisposition, ReviewDisplayText.Disposition);
            dispositionField = ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldDisposition"), proposalDisposition, ReviewStyle.Chip(ReviewStrings.Get("Placement_ChipProposalOnly"), ReviewUi.Optional), false);
            dispositionField.Visible = false;
            grid.Controls.Add(dispositionField, 1, 0);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(DecisionBodyBase));
            return card;
        }

        // F. Reviewer: who is recording the decision, and their comment.
        private Card BuildReviewerCard()
        {
            var card = new Card(ReviewStrings.Get("Placement_SectionReviewer"), null);
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
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldReviewerName"), reviewerName, ReviewStyle.Chip(ReviewStrings.Get("Placement_ChipRequired"), ReviewUi.Required), false), 0, 0);

            reviewerRole.Dock = DockStyle.Fill;
            reviewerRole.MaxLength = 120;
            reviewerRole.AccessibleName = "ReviewerRole";
            reviewerRole.TextChanged += (s, e) => UpdateSaveState();
            ReviewStyle.StyleTextBox(reviewerRole, ReviewUi.BodyFont, false);
            grid.Controls.Add(ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldReviewerRole"), reviewerRole, ReviewStyle.Chip(ReviewStrings.Get("Placement_ChipRequired"), ReviewUi.Required), false), 1, 0);

            comment.Dock = DockStyle.Fill;
            comment.AccessibleName = "EngineerComment";
            ReviewStyle.StyleTextBox(comment, ReviewUi.BodyFont, true);
            comment.ScrollBars = ScrollBars.Vertical;
            var commentField = ReviewStyle.FieldPair(ReviewStrings.Get("Placement_FieldComment"), comment, null, false);
            grid.Controls.Add(commentField, 0, 1);
            grid.SetColumnSpan(commentField, 2);

            card.Body.Controls.Add(grid);
            card.Fit(Scale(ReviewerBodyBase));
            return card;
        }

        // Technical Details: collapsed by default so the long identities, hashes and fingerprints
        // never dominate the engineer flow. Fully accessible, selectable and always LTR.
        private Card BuildTechnicalCard()
        {
            var card = new Card(ReviewStrings.Get("Placement_SectionTechnical"), null);
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(TechnicalToggleRowBase)));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            technicalToggle.Dock = DockStyle.Left;
            technicalToggle.Width = Scale(220);
            technicalToggle.Height = Scale(ButtonHeightBase);
            technicalToggle.AccessibleName = "TechnicalToggle";
            technicalToggle.Text = ReviewStrings.Get("Placement_ButtonTechnicalShow");
            ReviewStyle.StyleButton(technicalToggle, false);
            technicalToggle.Click += (s, e) => SetTechnicalExpanded(!technicalExpanded);
            stack.Controls.Add(technicalToggle, 0, 0);

            technicalBody.Dock = DockStyle.Fill;
            technicalBody.BackColor = ReviewUi.Surface;
            technicalBody.AutoScroll = true;
            technicalGrid.Dock = DockStyle.Top;
            technicalGrid.BackColor = ReviewUi.Surface;
            technicalGrid.Margin = new Padding(0);
            technicalBody.Controls.Add(technicalGrid);
            stack.Controls.Add(technicalBody, 0, 1);

            card.Body.Controls.Add(stack);
            technicalExpanded = false;
            technicalBody.Visible = false;
            card.Fit(Scale(TechnicalToggleRowBase));
            return card;
        }

        private void SetTechnicalExpanded(bool expanded)
        {
            technicalExpanded = expanded;
            if (technicalBody == null) return;
            technicalBody.Visible = expanded;
            if (technicalToggle != null)
                technicalToggle.Text = ReviewStrings.Get(expanded ? "Placement_ButtonTechnicalHide" : "Placement_ButtonTechnicalShow");
            if (technicalCard != null)
                technicalCard.Fit(Scale(TechnicalToggleRowBase) + (expanded ? Scale(TechnicalBodyBase) : 0));
            if (expanded) UpdateTechnicalDetails();
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
                    Text = (question.Required ? "* " : "  ") + ReviewDisplayText.Question(question),
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
                    combo.Items.Add(string.Empty);
                    foreach (string value in ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)) combo.Items.Add(value);
                    combo.SelectedIndex = 0;
                    combo.SelectedIndexChanged += (s, e) => UpdateSaveState();
                    // the combo holds the canonical answer tokens and persists exactly those; only the
                    // painted entry is localized
                    StyleLocalizedCombo(combo, ReviewDisplayText.Answer);
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
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = ReviewUi.Surface, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(220)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(34)));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(20)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var hint = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Muted, BackColor = ReviewUi.Surface, AutoEllipsis = true,
                Text = ReviewStrings.Get("Placement_Hint")
            };
            grid.Controls.Add(hint, 0, 0);

            save.Text = ReviewStrings.Get("Placement_ButtonRecord");
            save.Dock = DockStyle.Fill;
            save.Margin = new Padding(1);
            save.Padding = new Padding(0);
            save.AccessibleName = "SaveResolution";
            save.Click += (s, e) => SaveResolution();
            ReviewStyle.StyleButton(save, true);
            grid.Controls.Add(save, 1, 0);

            // one concise helper that always reflects the actual missing requirement, derived from the
            // same validation state the primary action uses — no second validation engine
            whyLabel.Dock = DockStyle.Fill;
            whyLabel.AutoEllipsis = true;
            whyLabel.TextAlign = ContentAlignment.MiddleLeft;
            whyLabel.Font = ReviewUi.CaptionFont;
            whyLabel.ForeColor = ReviewUi.Muted;
            whyLabel.BackColor = ReviewUi.Surface;
            whyLabel.AccessibleName = "MissingRequirement";
            whyLabel.Text = string.Empty;
            grid.Controls.Add(whyLabel, 0, 1);
            grid.SetColumnSpan(whyLabel, 2);

            message.Dock = DockStyle.Fill;
            message.AutoEllipsis = true;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Font = ReviewUi.BodyFont;
            message.ForeColor = ReviewUi.Value;
            message.BackColor = ReviewUi.Surface;
            message.Text = string.Empty;
            message.Severity = SeverityMessage.None;
            grid.Controls.Add(message, 0, 2);
            grid.SetColumnSpan(message, 2);

            host.Controls.Add(grid);
            root.Controls.Add(host, 0, 2);
        }

        // ---- localized combo styling ----------------------------------------------------------

        // Drop-down styling with owner-drawn items: the item objects stay canonical (so selection,
        // capture and persistence are byte-identical); only the painted entry runs through the
        // display layer. This keeps the closed combo and its drop-down readable on the dark surface.
        private static void StyleLocalizedCombo(ComboBox combo, Func<string, string> display)
        {
            ReviewStyle.StyleCombo(combo, ReviewUi.BodyFont);
            // StyleCombo already hooks the raw painter; replace its effect with the localized one by
            // painting after it (the localized text is drawn on top of the same background)
            combo.DrawItem += (sender, e) =>
            {
                if (e.Index < 0 || e.Index >= combo.Items.Count) return;
                ReviewStyle.PaintComboItem(combo, e, display(combo.Items[e.Index] as string));
            };
        }

        // ---- grid helpers ---------------------------------------------------------------------

        private void InitGrid(TableLayoutPanel grid)
        {
            grid.SuspendLayout();
            var children = grid.Controls.Cast<Control>().ToList();
            grid.Controls.Clear();
            foreach (Control child in children) child.Dispose();
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            grid.RowCount = 0;
            grid.ColumnCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Scale(InfoCaptionWidthBase)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ResumeLayout();
        }

        // A labelled read-only value: a localized caption beside a technical value that always reads
        // left-to-right and stays selectable/copyable.
        private void AddInfoRow(TableLayoutPanel grid, string caption, string value)
        {
            int row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(InfoRowBase)));
            var captionLabel = new Label
            {
                Text = caption ?? string.Empty, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Font = ReviewUi.CaptionFont, ForeColor = ReviewUi.Caption, BackColor = grid.BackColor,
                AutoEllipsis = true, Margin = new Padding(0, 0, 8, 0)
            };
            grid.Controls.Add(captionLabel, 0, row);
            grid.Controls.Add(ValueControl(value), 1, row);
        }

        private static TextBox ValueControl(string value)
        {
            return new TextBox
            {
                Text = value ?? string.Empty,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Multiline = false,
                Dock = DockStyle.Fill,
                Font = ReviewUi.MonoFont,
                BackColor = ReviewUi.Surface,
                ForeColor = ReviewUi.Value,
                // technical values never mirror, even inside the RTL Persian surface
                RightToLeft = RightToLeft.No,
                AccessibleName = "TechnicalValue",
                TabStop = false,
                // a single-line TextBox derives its height from the font; the mono face is short, so
                // a minimum keeps every value field at a usable, readable height
                MinimumSize = new Size(40, 18),
                Margin = new Padding(0, 2, 0, 2)
            };
        }

        private static void ClearControls(Control container)
        {
            List<Control> children = container.Controls.Cast<Control>().ToList();
            container.Controls.Clear();
            foreach (Control child in children) child.Dispose();
        }

        // ---- state ----------------------------------------------------------------------------

        private void RefreshPlacements()
        {
            loading = true;
            placementList.Items.Clear();
            foreach (EquipmentPlacement placement in unresolved) placementList.Items.Add(QueueLabel(placement));
            foreach (PlacementProposal proposal in proposals)
            {
                // the queue line is a governed reference line: the disposition keeps its exact
                // canonical token so the accepted P11.1 surface (and its acceptance evidence) is
                // unchanged; only the leading marker is presented in the selected UI language
                string state = dispositionService.GetStatus(proposal).Disposition;
                placementList.Items.Add(ReviewStrings.Get("Placement_ListProposal") + " | " + proposal.NormalizedTag + " | " + state + " | " + proposal.ProposalId);
            }
            loading = false;
            UpdateCountBadge();
            if (placementList.Items.Count > 0) placementList.SelectedIndex = 0; else SelectWorkItem(-1);
        }

        private void UpdateCountBadge()
        {
            int open = placementList.Items.Count;
            countBadge.Text = open == 1
                ? ReviewStrings.Get("Placement_HeaderOpenItemOne")
                : ReviewStrings.Format("Placement_HeaderOpenItemMany", open.ToString(CultureInfo.InvariantCulture));
            countBadge.ForeColor = open == 0 ? ReviewUi.Completed : ReviewUi.Pending;
            countBadge.Tint = open == 0 ? ReviewUi.CompletedTint : ReviewUi.PendingTint;
        }

        private static string QueueLabel(EquipmentPlacement placement) =>
            placement.NormalizedTag + "  |  " + placement.EquipmentEntityId + "  |  " + placement.PlacementStatus;

        private void SelectWorkItem(int index)
        {
            current = index >= 0 && index < unresolved.Count ? unresolved[index] : null;
            currentProposal = index >= unresolved.Count && index < unresolved.Count + proposals.Count ? proposals[index - unresolved.Count] : null;
            pick = null;
            proposalDisposition.SelectedIndex = 0;
            proposalDisposition.Visible = currentProposal != null;
            if (dispositionField != null) dispositionField.Visible = currentProposal != null;
            UpdatePickInfo();
            UpdateSummary();
            UpdateTechnicalDetails();
            UpdateSaveState();
        }

        private void UpdateSummary()
        {
            if (summaryGrid == null) return;
            InitGrid(summaryGrid);
            if (current != null)
            {
                tagLabel.Text = current.NormalizedTag;
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldReviewStatus"), current.PlacementStatus);
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldOriginal"), PlacementLocation(current));
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldProposed"), pick == null ? ReviewStrings.Get("Placement_ValueNone") : PickPoint(pick));
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldReviewRequired"), ReviewStrings.Get("Placement_ValueYes"));
            }
            else if (currentProposal != null)
            {
                PlacementProposalDispositionStatus status = dispositionService.GetStatus(currentProposal);
                tagLabel.Text = currentProposal.NormalizedTag;
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldReviewStatus"), ReviewDisplayText.Disposition(status.Disposition));
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldOriginal"), Point(currentProposal.OriginalEasting, currentProposal.OriginalNorthing));
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldProposed"), Point(currentProposal.ProposedEasting, currentProposal.ProposedNorthing));
                AddInfoRow(summaryGrid, ReviewStrings.Get("Placement_FieldReviewRequired"),
                    ReviewStrings.Get(currentProposal.EngineeringReviewRequired ? "Placement_ValueYes" : "Placement_ValueNo"));
            }
            else
            {
                tagLabel.Text = ReviewStrings.Get("Placement_ValueNotSelected");
            }
            if (workItemCard != null) workItemCard.Fit(Scale(30) + Scale(summaryGrid.RowCount * InfoRowBase));
        }

        private void UpdatePickInfo()
        {
            if (pickGrid == null) return;
            InitGrid(pickGrid);
            if (pick == null)
            {
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_FieldPickEvidence"), ReviewStrings.Get("Placement_PickNone"));
            }
            else
            {
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechPickSource"), pick.Source);
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechPickPoint"), PickPoint(pick));
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechHandle"), string.IsNullOrEmpty(pick.Handle) ? ReviewStrings.Get("Placement_ValueFreeSpace") : pick.Handle);
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechLayer"), string.IsNullOrEmpty(pick.Layer) ? ReviewStrings.Get("Placement_ValueFreeSpace") : pick.Layer);
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechStoredEasting"), Stored(EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointX)));
                AddInfoRow(pickGrid, ReviewStrings.Get("Placement_TechStoredNorthing"), Stored(EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointY)));
            }
            if (pickCard != null) pickCard.Fit(Scale(pickGrid.RowCount * InfoRowBase) + Scale(PickButtonRowBase) + Scale(8));
        }

        private void UpdateTechnicalDetails()
        {
            if (technicalGrid == null) return;
            InitGrid(technicalGrid);
            AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechDrawingSha"), drawingSha256);
            AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechDrawing"), drawingName);
            AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechUnits"), string.IsNullOrEmpty(insertionUnits) ? ReviewStrings.Get("Placement_ValueUnresolved") : insertionUnits);
            if (current != null)
            {
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechPlacementId"), current.PlacementId);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechEntityId"), current.EquipmentEntityId);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechPlacementStatus"), current.PlacementStatus);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechEastingStatus"), current.Easting?.Status);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechNorthingStatus"), current.Northing?.Status);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechCenterline"), current.CenterlineEvidence?.Status ?? ReviewStrings.Get("Placement_ValueNone"));
            }
            if (currentProposal != null)
            {
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechProposalId"), currentProposal.ProposalId);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechFingerprint"), currentProposal.ProposalFingerprint);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechEntityId"), currentProposal.EquipmentEntityId);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechGenerator"), currentProposal.GeneratorIdentity);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechOriginalPoint"), Point(currentProposal.OriginalEasting, currentProposal.OriginalNorthing));
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechProposedPoint"), Point(currentProposal.ProposedEasting, currentProposal.ProposedNorthing));
            }
            if (pick != null)
            {
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechPickSource"), pick.Source);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechPickPoint"), PickPoint(pick));
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechHandle"), string.IsNullOrEmpty(pick.Handle) ? ReviewStrings.Get("Placement_ValueFreeSpace") : pick.Handle);
                AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechLayer"), string.IsNullOrEmpty(pick.Layer) ? ReviewStrings.Get("Placement_ValueFreeSpace") : pick.Layer);
            }
            AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechResolutionId"), string.IsNullOrEmpty(lastResolutionId) ? ReviewStrings.Get("Placement_ValueNone") : lastResolutionId);
            AddInfoRow(technicalGrid, ReviewStrings.Get("Placement_TechDispositionId"), string.IsNullOrEmpty(lastDispositionId) ? ReviewStrings.Get("Placement_ValueNone") : lastDispositionId);
            technicalGrid.Height = Scale(technicalGrid.RowCount * InfoRowBase);
        }

        private static string PlacementLocation(EquipmentPlacement placement) =>
            "E/N: " + (placement.Easting?.Status ?? "?") + " / " + (placement.Northing?.Status ?? "?");

        private static string Point(double easting, double northing) =>
            easting.ToString("R", CultureInfo.InvariantCulture) + ", " + northing.ToString("R", CultureInfo.InvariantCulture);

        private static string PickPoint(EquipmentPlacementPick captured) =>
            EquipmentPlacementPickFormatting.Format(captured.PointX) + ", " + EquipmentPlacementPickFormatting.Format(captured.PointY);

        private static string Stored(decimal? value) => value.HasValue ? EquipmentPlacementResolutionIdentity.CanonicalDecimal(value.Value) : ReviewStrings.Get("Placement_ValueUnresolved");

        private void DoPick(bool entity)
        {
            if (current == null && (currentProposal == null || proposalDisposition.SelectedItem as string != PlacementProposalDispositions.Modified))
            {
                ShowMessage(ReviewStrings.Get("Placement_ErrorSelectFirst"), true);
                return;
            }
            EquipmentPlacementPick result;
            try { result = entity ? pickProvider.PickEntity() : pickProvider.PickFreeSpace(); }
            catch (Exception exception) { ShowMessage(ReviewStrings.Format("Placement_ErrorPickFailed", exception.Message), true); return; }
            if (result == null) { ShowMessage(ReviewStrings.Get("Placement_ErrorPickCancelled"), true); return; }
            if (EquipmentPlacementPickFormatting.ToStoredDecimal(result.PointX) == null || EquipmentPlacementPickFormatting.ToStoredDecimal(result.PointY) == null)
            {
                ShowMessage(ReviewStrings.Get("Placement_ErrorPickRange"), true);
                pick = null;
                UpdatePickInfo();
                UpdateSummary();
                UpdateTechnicalDetails();
                UpdateSaveState();
                return;
            }
            pick = result;
            UpdatePickInfo();
            UpdateSummary();
            UpdateTechnicalDetails();
            UpdateSaveState();
            ShowMessage(ReviewStrings.Get("Placement_StatusPickCaptured"), false);
        }

        private void UpdateSaveState()
        {
            // the readiness rule is unchanged from the accepted behaviour: it is only surfaced more
            // clearly (the primary action stays readable in both states, and the reason it is
            // disabled is stated inline)
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
            if (whyLabel != null)
            {
                whyLabel.Text = ready ? ReviewStrings.Get("Placement_WhyReady") : MissingRequirement();
                whyLabel.ForeColor = ready ? ReviewUi.Completed : ReviewUi.Muted;
            }
        }

        // The first requirement that is still missing, derived from the same state the readiness rule
        // above reads. It is a helper message only — it never gates anything on its own.
        private string MissingRequirement()
        {
            if (current == null && currentProposal == null) return ReviewStrings.Get("Placement_WhySelect");
            if (string.IsNullOrWhiteSpace(reviewerName.Text)) return ReviewStrings.Get("Placement_WhyReviewerName");
            if (string.IsNullOrWhiteSpace(reviewerRole.Text)) return ReviewStrings.Get("Placement_WhyReviewerRole");
            if (string.IsNullOrWhiteSpace(classification.SelectedItem as string)) return ReviewStrings.Get("Placement_WhyClassification");
            if (current != null)
            {
                if (pick == null) return ReviewStrings.Get("Placement_WhyPick");
            }
            else
            {
                string action = proposalDisposition.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(action)) return ReviewStrings.Get("Placement_WhyDisposition");
                bool needsPick = action == PlacementProposalDispositions.Modified;
                if (needsPick != (pick != null)) return ReviewStrings.Get("Placement_WhyPick");
                if (string.IsNullOrWhiteSpace(comment.Text)) return ReviewStrings.Get("Placement_WhyComment");
                if (action == PlacementProposalDispositions.Approved
                    && (!EquipmentPlacementPickFormatting.ToStoredDecimal(currentProposal.ProposedEasting).HasValue || !EquipmentPlacementPickFormatting.ToStoredDecimal(currentProposal.ProposedNorthing).HasValue))
                    return ReviewStrings.Get("Placement_WhyProposalRange");
            }
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                if (!question.Required) continue;
                if (!answerControls.TryGetValue(question.QuestionId, out Control control)) return ReviewStrings.Get("Placement_WhyAnswers");
                string value = control is ComboBox combo ? (combo.SelectedIndex > 0 ? (string)combo.SelectedItem : string.Empty) : control.Text;
                if (string.IsNullOrWhiteSpace(value)) return ReviewStrings.Get("Placement_WhyAnswers");
            }
            return ReviewStrings.Get("Placement_WhyReady");
        }

        private void SaveResolution()
        {
            if (currentProposal != null) { SaveProposalDisposition(); return; }
            if (current == null) { ShowMessage(ReviewStrings.Get("Placement_ErrorNoSelection"), true); return; }
            if (pick == null) { ShowMessage(ReviewStrings.Get("Placement_ErrorNoPick"), true); return; }
            decimal? easting = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointX);
            decimal? northing = EquipmentPlacementPickFormatting.ToStoredDecimal(pick.PointY);
            if (!easting.HasValue || !northing.HasValue) { ShowMessage(ReviewStrings.Get("Placement_ErrorPickRange"), true); return; }
            if (string.IsNullOrWhiteSpace(drawingSha256)) { ShowMessage(ReviewStrings.Get("Placement_ErrorNoHash"), true); return; }
            if (string.IsNullOrWhiteSpace(insertionUnits)) { ShowMessage(ReviewStrings.Get("Placement_ErrorNoUnits"), true); return; }

            EquipmentPlacement target = current;
            DateTime responseTimestamp = DateTime.UtcNow;
            ProjectEngineerReviewRequest request = BuildRequest(target, pick, easting.Value, northing.Value, responseTimestamp);
            Dictionary<string, string> answers = CaptureAnswers();
            string incomplete = ProjectEngineerReviewService.FindIncompleteReason(request, reviewerName.Text, reviewerRole.Text, answers, classification.SelectedItem as string);
            if (incomplete != null) { ShowMessage(ReviewStrings.Format("Placement_ErrorNotSaved", ReviewDisplayText.Reason(incomplete)), true); return; }

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
                lastResolutionId = record.ResolutionId;
            }
            catch (Exception exception)
            {
                ShowMessage(ReviewStrings.Format("Placement_ErrorNotSaved", exception.Message), true);
                return;
            }

            // A saved resolution resolves the whole equipment identity, so every queue entry for
            // that identity leaves the queue — not just the one that was displayed.
            EquipmentPlacementResolutionQueue.RemoveForEntity(unresolved, target.EquipmentEntityId);
            RefreshPlacements();
            UpdateTechnicalDetails();
            ShowMessage(ReviewStrings.Format("Placement_StatusResolutionSaved", target.NormalizedTag), false);
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
                lastDispositionId = disposition.RecordId;
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
                    lastResolutionId = resolution.ResolutionId;
                }
                UpdateSummary();
                UpdateTechnicalDetails();
                UpdateSaveState();
                ShowMessage(ReviewStrings.Format("Placement_StatusDecisionRecorded", action), false);
            }
            catch (Exception exception) { ShowMessage(ReviewStrings.Format("Placement_ErrorDispositionNotRecorded", exception.Message), true); }
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
    }
}
