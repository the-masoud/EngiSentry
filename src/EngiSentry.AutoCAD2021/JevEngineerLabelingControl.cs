#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using EngiSentry.Intelligence;

namespace EngiSentry.AutoCAD2021
{
    internal sealed class JevEngineerLabelingControl : UserControl
    {
        private readonly JevEngineerLabelingService service = new JevEngineerLabelingService();
        private readonly ComboBox packageSelector = new ComboBox();
        private readonly Label drawingName = new Label();
        private readonly Label packageStatus = new Label();
        private readonly Label progressText = new Label();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly ComboBox filter = new ComboBox();
        private readonly DataGridView casesGrid = new DataGridView();
        private readonly TextBox evidence = new TextBox();
        private readonly DataGridView candidatesGrid = new DataGridView();
        private readonly ComboBox engineerLabel = new ComboBox();
        private readonly TextBox comment = new TextBox();
        private readonly Button previous = new Button();
        private readonly Button next = new Button();
        private readonly Button nextUnlabeled = new Button();
        private readonly Button save = new Button();
        private readonly Label message = new Label();
        private List<JevEngineerLabelingPackage> packages = new List<JevEngineerLabelingPackage>();
        private JevEngineerLabelingPackage currentPackage;
        private List<int> visibleCaseIndexes = new List<int>();
        private int currentCaseIndex = -1;
        private bool loading;

        internal JevEngineerLabelingControl()
        {
            Dock = DockStyle.Fill;
            BuildLayout();
            LoadPackages();
        }

        internal void LoadPackages()
        {
            loading = true;
            packages = service.Discover();
            packageSelector.Items.Clear();
            foreach (JevEngineerLabelingPackage package in packages) packageSelector.Items.Add(package);
            int editable = packages.FindIndex(x => !x.IsReadOnly);
            packageSelector.SelectedIndex = editable >= 0 ? editable : packages.Count > 0 ? 0 : -1;
            loading = false;
            SelectPackage(packageSelector.SelectedIndex);
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            header.Controls.Add(new Label { Text = "Jev Engineer Labeling", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) }, 0, 0);
            header.SetColumnSpan(header.GetControlFromPosition(0, 0), 4);
            header.Controls.Add(new Label { Text = "Package", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            packageSelector.Dock = DockStyle.Fill; packageSelector.DropDownStyle = ComboBoxStyle.DropDownList; packageSelector.SelectedIndexChanged += (s, e) => { if (!loading) { CaptureCurrent(); SelectPackage(packageSelector.SelectedIndex); } };
            header.Controls.Add(packageSelector, 1, 1); header.SetColumnSpan(packageSelector, 3);
            drawingName.Dock = DockStyle.Fill; drawingName.AutoEllipsis = true; drawingName.TextAlign = ContentAlignment.MiddleLeft;
            packageStatus.Dock = DockStyle.Fill; packageStatus.TextAlign = ContentAlignment.MiddleLeft;
            progressText.Dock = DockStyle.Fill; progressText.TextAlign = ContentAlignment.MiddleLeft;
            progress.Dock = DockStyle.Fill;
            header.Controls.Add(drawingName, 0, 2); header.Controls.Add(packageStatus, 1, 2); header.Controls.Add(progressText, 2, 2); header.Controls.Add(progress, 3, 2);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1000, 500), Panel1MinSize = 300, Panel2MinSize = 360, SplitterDistance = 455 };
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var filterPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            filterPanel.Controls.Add(new Label { Text = "Filter", Width = 45, TextAlign = ContentAlignment.MiddleLeft });
            filter.DropDownStyle = ComboBoxStyle.DropDownList; filter.Width = 130; filter.Items.AddRange(new object[] { "All", "Unlabeled", "Labeled" }); filter.SelectedIndex = 0; filter.SelectedIndexChanged += (s, e) => { if (!loading) { CaptureCurrent(); RefreshCases(); } };
            filterPanel.Controls.Add(filter); left.Controls.Add(filterPanel, 0, 0);
            ConfigureCasesGrid(); left.Controls.Add(casesGrid, 0, 1); split.Panel1.Controls.Add(left);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 24)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
            evidence.Dock = DockStyle.Fill; evidence.Multiline = true; evidence.ReadOnly = true; evidence.ScrollBars = ScrollBars.Vertical; evidence.BackColor = SystemColors.Window;
            right.Controls.Add(evidence, 0, 0); ConfigureCandidatesGrid(); right.Controls.Add(candidatesGrid, 0, 1);
            var edit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            edit.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); edit.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); edit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            edit.Controls.Add(new Label { Text = "Engineer Label", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            engineerLabel.Dock = DockStyle.Fill; engineerLabel.DropDownStyle = ComboBoxStyle.DropDownList; engineerLabel.Items.Add(string.Empty); engineerLabel.Items.AddRange(JevSemanticLabels.All.Cast<object>().ToArray());
            edit.Controls.Add(engineerLabel, 1, 0); edit.Controls.Add(new Label { Text = "Comment", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            comment.Dock = DockStyle.Fill; comment.Multiline = true; comment.ScrollBars = ScrollBars.Vertical; comment.MaxLength = 500; edit.Controls.Add(comment, 1, 1); right.Controls.Add(edit, 0, 2); split.Panel2.Controls.Add(right);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 2 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            previous.Text = "Previous"; next.Text = "Next"; nextUnlabeled.Text = "Next Unlabeled"; save.Text = "Save";
            previous.Click += (s, e) => Navigate(-1); next.Click += (s, e) => Navigate(1); nextUnlabeled.Click += (s, e) => NavigateUnlabeled(); save.Click += Save;
            foreach (Button button in new[] { previous, next, nextUnlabeled, save }) button.Dock = DockStyle.Fill;
            footer.Controls.Add(previous, 0, 0); footer.Controls.Add(next, 1, 0); footer.Controls.Add(nextUnlabeled, 2, 0); footer.Controls.Add(save, 4, 0);
            message.Dock = DockStyle.Fill; message.AutoEllipsis = true; message.TextAlign = ContentAlignment.MiddleLeft; footer.Controls.Add(message, 0, 1); footer.SetColumnSpan(message, 5);
            root.Controls.Add(header, 0, 0); root.Controls.Add(split, 0, 1); root.Controls.Add(footer, 0, 2); Controls.Add(root);
        }

        private void ConfigureCasesGrid()
        {
            casesGrid.Dock = DockStyle.Fill; casesGrid.ReadOnly = true; casesGrid.AllowUserToAddRows = false; casesGrid.AllowUserToDeleteRows = false; casesGrid.AllowUserToResizeRows = false; casesGrid.MultiSelect = false; casesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; casesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            foreach (string name in new[] { "#", "Subject", "Nearby Text", "Layer", "Deterministic State", "Engineer Label", "Status" }) casesGrid.Columns.Add(name, name);
            casesGrid.SelectionChanged += (s, e) => { if (!loading && casesGrid.SelectedRows.Count == 1) { CaptureCurrent(); ShowCase((int)casesGrid.SelectedRows[0].Tag); } };
        }

        private void ConfigureCandidatesGrid()
        {
            candidatesGrid.Dock = DockStyle.Fill; candidatesGrid.ReadOnly = true; candidatesGrid.AllowUserToAddRows = false; candidatesGrid.AllowUserToDeleteRows = false; candidatesGrid.AllowUserToResizeRows = false; candidatesGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            foreach (string name in new[] { "SourceHandle", "SourceLayer", "GeometryKind", "Closed", "VertexCount", "Distance" }) candidatesGrid.Columns.Add(name, name);
        }

        private void SelectPackage(int index)
        {
            currentPackage = index >= 0 && index < packages.Count ? packages[index] : null; currentCaseIndex = -1;
            drawingName.Text = currentPackage == null ? "No calibration packages found" : currentPackage.Package.DrawingName;
            message.Text = currentPackage?.ValidationError ?? string.Empty; RefreshCases();
        }

        private void RefreshCases()
        {
            loading = true; casesGrid.Rows.Clear(); visibleCaseIndexes.Clear();
            if (currentPackage != null)
            {
                string mode = filter.SelectedItem as string ?? "All";
                for (int index = 0; index < currentPackage.Package.Cases.Count; index++)
                {
                    JevCalibrationCase item = currentPackage.Package.Cases[index]; JevEngineerLabel label = currentPackage.Labels[index]; bool labeled = JevSemanticLabels.All.Contains(label.EngineerLabel);
                    if (mode == "Unlabeled" && labeled || mode == "Labeled" && !labeled) continue;
                    visibleCaseIndexes.Add(index); int row = casesGrid.Rows.Add(index + 1, Subject(item), NearbyText(item), item.LayerName ?? string.Empty, item.CurrentDeterministicState, label.EngineerLabel ?? string.Empty, labeled ? "LABELED" : "UNLABELED"); casesGrid.Rows[row].Tag = index;
                }
            }
            loading = false; UpdateHeader(); if (visibleCaseIndexes.Count > 0) SelectGridCase(visibleCaseIndexes.Contains(currentCaseIndex) ? currentCaseIndex : visibleCaseIndexes[0]); else ShowCase(-1);
        }

        private void ShowCase(int index)
        {
            currentCaseIndex = index; loading = true; candidatesGrid.Rows.Clear();
            if (currentPackage == null || index < 0 || index >= currentPackage.Package.Cases.Count) { evidence.Text = string.Empty; engineerLabel.SelectedIndex = 0; comment.Text = string.Empty; SetEditing(false); loading = false; return; }
            JevCalibrationCase item = currentPackage.Package.Cases[index]; JevEngineerLabel label = currentPackage.Labels[index]; evidence.Text = Evidence(item);
            foreach (JevCalibrationCandidate candidate in item.Candidates ?? new List<JevCalibrationCandidate>()) candidatesGrid.Rows.Add(candidate.SourceHandle, candidate.SourceLayer, candidate.GeometryKind, Value(candidate.Closed), Value(candidate.VertexCount), candidate.Distance.ToString("0.###", CultureInfo.InvariantCulture));
            engineerLabel.SelectedItem = label.EngineerLabel ?? string.Empty; if (engineerLabel.SelectedIndex < 0) engineerLabel.SelectedIndex = 0; comment.Text = label.Comment ?? string.Empty; SetEditing(!currentPackage.IsReadOnly); loading = false;
        }

        private void CaptureCurrent()
        {
            if (loading || currentPackage == null || currentPackage.IsReadOnly || currentCaseIndex < 0 || currentCaseIndex >= currentPackage.Labels.Count) return;
            currentPackage.Labels[currentCaseIndex].EngineerLabel = string.IsNullOrWhiteSpace(engineerLabel.SelectedItem as string) ? null : engineerLabel.SelectedItem as string;
            currentPackage.Labels[currentCaseIndex].Comment = string.IsNullOrEmpty(comment.Text) ? null : comment.Text;
        }

        private void Save(object sender, EventArgs e)
        {
            if (currentPackage == null) return; CaptureCurrent();
            try { service.Save(currentPackage); message.Text = "Engineer labels saved."; RefreshCases(); }
            catch (Exception exception) { message.Text = "Save failed: " + exception.Message; }
        }

        private void Navigate(int delta)
        {
            if (visibleCaseIndexes.Count == 0) return; CaptureCurrent(); int position = Math.Max(0, visibleCaseIndexes.IndexOf(currentCaseIndex)); position = Math.Max(0, Math.Min(visibleCaseIndexes.Count - 1, position + delta)); SelectGridCase(visibleCaseIndexes[position]);
        }

        private void NavigateUnlabeled()
        {
            if (currentPackage == null) return; CaptureCurrent(); int start = Math.Max(-1, currentCaseIndex); for (int offset = 1; offset <= currentPackage.Labels.Count; offset++) { int index = (start + offset) % currentPackage.Labels.Count; if (!JevSemanticLabels.All.Contains(currentPackage.Labels[index].EngineerLabel)) { if (!visibleCaseIndexes.Contains(index)) filter.SelectedIndex = 0; SelectGridCase(index); return; } }
        }

        private void SelectGridCase(int caseIndex)
        {
            foreach (DataGridViewRow row in casesGrid.Rows) if ((int)row.Tag == caseIndex) { row.Selected = true; casesGrid.CurrentCell = row.Cells[0]; ShowCase(caseIndex); return; }
        }

        private void SetEditing(bool enabled) { engineerLabel.Enabled = enabled; comment.Enabled = enabled; save.Enabled = enabled; }
        private void UpdateHeader() { packageStatus.Text = currentPackage == null ? "EMPTY" : currentPackage.Status; int total = currentPackage?.Package.Cases.Count ?? 0; int labeled = currentPackage?.LabeledCount ?? 0; progressText.Text = "Labeled " + labeled + " / " + total; progress.Maximum = Math.Max(1, total); progress.Value = Math.Min(progress.Maximum, labeled); }
        private static string Subject(JevCalibrationCase item) => !string.IsNullOrWhiteSpace(item.SubjectKind) ? item.SubjectKind! : item.GeometryKind;
        private static string NearbyText(JevCalibrationCase item) => !string.IsNullOrWhiteSpace(item.LabelRawText) ? item.LabelRawText! : (item.NearbyTextFragments ?? new List<string>()).FirstOrDefault() ?? string.Empty;
        private static string Value(object value) => value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

        private static string Evidence(JevCalibrationCase item)
        {
            var text = new StringBuilder(); Section(text, "IDENTITY"); Add(text, "EvidenceId", item.EvidenceId); Add(text, "InputProjectionSha256", item.InputProjectionSha256);
            Section(text, "DETERMINISTIC CONTEXT"); Add(text, "CurrentDeterministicState", item.CurrentDeterministicState); Add(text, "ExistingCandidateKinds", string.Join(", ", item.ExistingCandidateKinds ?? new List<string>())); Add(text, "MeasurementBasisHints", string.Join(", ", item.MeasurementBasisHints ?? new List<string>())); Add(text, "SpatialContext", string.Join(" | ", item.SpatialContext ?? new List<string>()));
            Section(text, "SOURCE EVIDENCE"); Add(text, "SourceRecordType", item.SourceRecordType); Add(text, "SourceRecordId", item.SourceRecordId); Add(text, "SubjectKind", item.SubjectKind); Add(text, "LabelRawText", item.LabelRawText); Add(text, "LabelSourceHandle", item.LabelSourceHandle); Add(text, "LabelSourceLayer", item.LabelSourceLayer); if (item.LabelPositionX.HasValue || item.LabelPositionY.HasValue || item.LabelPositionZ.HasValue) Add(text, "LabelPosition X/Y/Z", Value(item.LabelPositionX) + " / " + Value(item.LabelPositionY) + " / " + Value(item.LabelPositionZ)); Add(text, "AssociationScale", item.AssociationScale); Add(text, "Reason", item.Reason); Add(text, "SelectedGeometryHandle", item.SelectedGeometryHandle); Add(text, "SelectedDistance", item.SelectedDistance);
            Section(text, "GEOMETRY"); Add(text, "GeometryKind", item.GeometryKind); Add(text, "LayerName", item.LayerName); return text.ToString();
        }

        private static void Section(StringBuilder text, string name) { if (text.Length > 0) text.AppendLine(); text.AppendLine(name); }
        private static void Add(StringBuilder text, string name, string value) { if (!string.IsNullOrWhiteSpace(value)) text.AppendLine(name + ": " + value); }
        private static void Add(StringBuilder text, string name, double? value) { if (value.HasValue) Add(text, name, value.Value.ToString("0.###", CultureInfo.InvariantCulture)); }
    }
}
