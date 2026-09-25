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
    /// <summary>
    /// Engineer-assisted equipment placement surface. It presents only unresolved equipment
    /// placements and resolves them exclusively through an explicit engineer pick (a model-space
    /// entity pick, or an explicit free-space point). It reuses the accepted project-engineer
    /// review service for the review envelope and persists an append-only resolution record.
    ///
    /// Presentation only: the surface never infers a location, never snaps, never searches for a
    /// nearest entity, and never writes anything unless the engineer explicitly saves a complete,
    /// confirmed resolution. Cancelling or failing a pick writes nothing.
    /// </summary>
    internal sealed class EquipmentPlacementResolutionControl : UserControl
    {
        private readonly string drawingName;
        private readonly string drawingSha256;
        private readonly string insertionUnits;
        private readonly ProjectEngineerReviewService reviewService;
        private readonly EquipmentPlacementResolutionService resolutionService;
        private readonly IEquipmentPlacementPickProvider pickProvider;

        private readonly ListBox placementList = new ListBox();
        private readonly TextBox selectionInfo = new TextBox();
        private readonly TextBox pickInfo = new TextBox();
        private readonly Button pickEntity = new Button();
        private readonly Button pickFreeSpace = new Button();
        private readonly Button clearPick = new Button();
        private readonly TextBox reviewerName = new TextBox();
        private readonly TextBox reviewerRole = new TextBox();
        private readonly ComboBox classification = new ComboBox();
        private readonly TextBox comment = new TextBox();
        private readonly Button save = new Button();
        private readonly Label message = new Label();
        private readonly Label header = new Label();
        private readonly TableLayoutPanel questionsGrid = new TableLayoutPanel();
        private readonly Dictionary<string, Control> answerControls = new Dictionary<string, Control>(StringComparer.Ordinal);

        private List<EquipmentPlacement> unresolved = new List<EquipmentPlacement>();
        private EquipmentPlacement current;
        private EquipmentPlacementPick pick;
        private bool loading;

        internal EquipmentPlacementResolutionControl(
            PlotPlanModel plotPlan,
            string drawingName,
            string drawingSha256,
            string insertionUnits,
            ProjectEngineerReviewService reviewService,
            EquipmentPlacementResolutionService resolutionService,
            IEquipmentPlacementPickProvider pickProvider)
        {
            if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan));
            this.drawingName = drawingName ?? string.Empty;
            this.drawingSha256 = drawingSha256 ?? string.Empty;
            this.insertionUnits = insertionUnits ?? string.Empty;
            this.reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
            this.resolutionService = resolutionService ?? throw new ArgumentNullException(nameof(resolutionService));
            this.pickProvider = pickProvider ?? throw new ArgumentNullException(nameof(pickProvider));
            unresolved = (plotPlan.EquipmentPlacements ?? new List<EquipmentPlacement>())
                .Where(x => x != null && !string.Equals(x.PlacementStatus, "Complete", StringComparison.Ordinal))
                .OrderBy(x => x.NormalizedTag, StringComparer.Ordinal)
                .ThenBy(x => x.PlacementId, StringComparer.Ordinal)
                .ToList();
            Dock = DockStyle.Fill;
            BuildLayout();
            RefreshPlacements();
        }

        internal int UnresolvedCount => unresolved.Count;
        internal EquipmentPlacementPick CurrentPick => pick;

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

            header.Dock = DockStyle.Fill;
            header.TextAlign = ContentAlignment.MiddleLeft;
            header.Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            header.Text = "Engineer-Assisted Equipment Placement Resolution";
            var subHeader = new Label { Dock = DockStyle.Bottom, Height = 20, TextAlign = ContentAlignment.MiddleLeft, Text = "Drawing: " + drawingName + "   |   Units: " + (string.IsNullOrEmpty(insertionUnits) ? "<unresolved>" : insertionUnits) + "   |   SHA256: " + Short(drawingSha256) };
            var headerHost = new Panel { Dock = DockStyle.Fill };
            headerHost.Controls.Add(header);
            headerHost.Controls.Add(subHeader);
            root.Controls.Add(headerHost, 0, 0);

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            placementList.Dock = DockStyle.Fill;
            placementList.IntegralHeight = false;
            placementList.SelectedIndexChanged += (s, e) => { if (!loading) SelectPlacement(placementList.SelectedIndex); };
            var leftHost = new Panel { Dock = DockStyle.Fill };
            leftHost.Controls.Add(placementList);
            leftHost.Controls.Add(new Label { Dock = DockStyle.Top, Height = 20, Text = "Unresolved equipment placements", TextAlign = ContentAlignment.MiddleLeft });
            split.Controls.Add(leftHost, 0, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));

            selectionInfo.Dock = DockStyle.Fill; selectionInfo.Multiline = true; selectionInfo.ReadOnly = true; selectionInfo.ScrollBars = ScrollBars.Vertical; selectionInfo.BackColor = SystemColors.Window;
            right.Controls.Add(selectionInfo, 0, 0);

            pickInfo.Dock = DockStyle.Fill; pickInfo.Multiline = true; pickInfo.ReadOnly = true; pickInfo.ScrollBars = ScrollBars.Vertical; pickInfo.BackColor = SystemColors.Window;
            var pickButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false };
            pickEntity.Text = "Pick Entity"; pickFreeSpace.Text = "Pick Free Space"; clearPick.Text = "Clear Pick";
            pickEntity.Width = 100; pickFreeSpace.Width = 120; clearPick.Width = 90;
            pickEntity.Click += (s, e) => DoPick(true);
            pickFreeSpace.Click += (s, e) => DoPick(false);
            clearPick.Click += (s, e) => { pick = null; UpdatePickInfo(); UpdateSaveState(); };
            pickButtons.Controls.Add(pickEntity); pickButtons.Controls.Add(pickFreeSpace); pickButtons.Controls.Add(clearPick);
            var pickHost = new Panel { Dock = DockStyle.Fill };
            pickHost.Controls.Add(pickInfo);
            pickHost.Controls.Add(pickButtons);
            right.Controls.Add(pickHost, 0, 1);

            var questions = BuildQuestions();
            right.Controls.Add(questions, 0, 2);

            var review = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            review.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); review.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            review.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); review.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); review.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            review.Controls.Add(new Label { Text = "Reviewer", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            reviewerName.Dock = DockStyle.Fill; reviewerName.AccessibleName = "ReviewerDisplayName"; reviewerName.TextChanged += (s, e) => UpdateSaveState();
            review.Controls.Add(reviewerName, 1, 0);
            review.Controls.Add(new Label { Text = "Role / title", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            reviewerRole.Dock = DockStyle.Fill; reviewerRole.AccessibleName = "ReviewerRole"; reviewerRole.TextChanged += (s, e) => UpdateSaveState();
            review.Controls.Add(reviewerRole, 1, 1);
            var commentHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            commentHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); commentHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            commentHost.Controls.Add(new Label { Text = "Engineer comment", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            comment.Dock = DockStyle.Fill; comment.Multiline = true; comment.ScrollBars = ScrollBars.Vertical; comment.AccessibleName = "EngineerComment";
            commentHost.Controls.Add(comment, 0, 1);
            review.Controls.Add(commentHost, 1, 2);
            right.Controls.Add(review, 0, 3);

            split.Controls.Add(right, 1, 0);
            root.Controls.Add(split, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            classification.Dock = DockStyle.Fill; classification.DropDownStyle = ComboBoxStyle.DropDownList; classification.AccessibleName = "Classification";
            classification.Items.Add(string.Empty); classification.Items.AddRange(ProjectEngineerReviewClassifications.All.Cast<object>().ToArray()); classification.SelectedIndex = 0;
            classification.SelectedIndexChanged += (s, e) => UpdateSaveState();
            footer.Controls.Add(classification, 0, 0);
            save.Text = "Save Resolution"; save.Dock = DockStyle.Fill; save.Click += (s, e) => SaveResolution();
            footer.Controls.Add(save, 2, 0);
            message.Dock = DockStyle.Fill; message.TextAlign = ContentAlignment.MiddleLeft; message.AutoEllipsis = true;
            footer.Controls.Add(message, 0, 1); footer.SetColumnSpan(message, 3);
            root.Controls.Add(footer, 0, 2);

            Controls.Add(root);
        }

        private Control BuildQuestions()
        {
            questionsGrid.Dock = DockStyle.Fill;
            questionsGrid.ColumnCount = 2;
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            questionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            questionsGrid.RowCount = EquipmentPlacementResolutionQuestions.All.Count;
            for (int i = 0; i < EquipmentPlacementResolutionQuestions.All.Count; i++)
            {
                ProjectEngineerReviewQuestionDefinition question = EquipmentPlacementResolutionQuestions.All[i];
                questionsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
                questionsGrid.Controls.Add(new Label { Text = (question.Required ? "* " : "  ") + question.Text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
                Control answer;
                if (question.AnswerKind == ProjectEngineerAnswerKinds.FreeText)
                {
                    var box = new TextBox { Dock = DockStyle.Fill, MaxLength = 500, Tag = question.QuestionId, AccessibleName = question.QuestionId };
                    box.TextChanged += (s, e) => UpdateSaveState();
                    answer = box;
                }
                else
                {
                    var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Tag = question.QuestionId, AccessibleName = question.QuestionId };
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

        private void RefreshPlacements()
        {
            loading = true;
            placementList.Items.Clear();
            foreach (EquipmentPlacement placement in unresolved) placementList.Items.Add(Label(placement));
            loading = false;
            if (unresolved.Count > 0) placementList.SelectedIndex = 0; else SelectPlacement(-1);
        }

        private static string Label(EquipmentPlacement placement) =>
            placement.NormalizedTag + "  |  " + placement.EquipmentEntityId + "  |  " + placement.PlacementStatus;

        private void SelectPlacement(int index)
        {
            current = index >= 0 && index < unresolved.Count ? unresolved[index] : null;
            pick = null;
            selectionInfo.Text = current == null ? "No unresolved equipment placement is selected." : Describe(current);
            UpdatePickInfo();
            UpdateSaveState();
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
            if (current == null) { ShowMessage("Select an unresolved equipment placement first.", true); return; }
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
            bool ready = current != null && pick != null
                && !string.IsNullOrWhiteSpace(reviewerName.Text)
                && !string.IsNullOrWhiteSpace(reviewerRole.Text)
                && !string.IsNullOrWhiteSpace(classification.SelectedItem as string);
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

            unresolved.Remove(target);
            RefreshPlacements();
            ShowMessage("Resolution saved for " + target.NormalizedTag + ". A fresh plot-plan QA will mark it Complete.", false);
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
            message.ForeColor = warning ? Color.FromArgb(176, 64, 48) : Color.FromArgb(32, 110, 68);
        }

        private static string Short(string value) => string.IsNullOrEmpty(value) || value.Length <= 16 ? value : value.Substring(0, 16) + "...";
    }
}
