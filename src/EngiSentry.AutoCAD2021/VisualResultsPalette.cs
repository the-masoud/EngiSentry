#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    internal static class VisualResultsPalette
    {
        private static PaletteSet palette;
        private static VisualResultsControl control;

        public static void ShowHome(string command, Document document, string drawing, string status, string detail)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "EngiSentry",
                Drawing = drawing,
                Status = status,
                StatusDetail = detail,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Mode", "Read-only"),
                    new VisualMetric("DWG changes", "None"),
                    new VisualMetric("Output", "Not applicable")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Command", command),
                    new VisualDetail("Connection", detail)
                }
            });
        }

        public static void ShowScan(Document document, DrawingSnapshot snapshot, string outputPath)
        {
            int unresolved = snapshot.Xrefs.Count(x => !x.IsResolved);
            Publish(new VisualResultsData
            {
                Command = "ES_SCAN",
                Title = "Drawing Scan",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Read-only scan completed",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Model entities", snapshot.ModelSpaceEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Paper entities", snapshot.PaperSpaceEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Blocks", snapshot.BlockDefinitions.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unresolved Xrefs", unresolved.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Block references", snapshot.BlockReferenceCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Xrefs", snapshot.Xrefs.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Scan duration", snapshot.ScanDurationMs.ToString(CultureInfo.InvariantCulture) + " ms"),
                    new VisualDetail("DBMOD guard", snapshot.DrawingModifiedByScan ? "Changed" : "Unchanged"),
                    new VisualDetail("Snapshot", outputPath)
                }
            });
        }

        public static void ShowSemantic(Document document, DrawingSnapshot snapshot, EngineeringDrawingModel model, string outputPath)
        {
            SemanticStatistics s = model.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_SEMANTIC",
                Title = "Semantic Extraction",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Semantic model built",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment tags", s.EquipmentTagOccurrences.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unique tags", s.UniqueEquipmentTags.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Structure tags", s.StructureTagOccurrences.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Coordinates", s.CoordinateCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Elevations", s.ElevationCount.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Road labels", s.RoadLabelCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Boundary labels", s.BoundaryLabelCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Equipment list rows", s.EquipmentListEntryCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Semantic model", outputPath),
                    new VisualDetail("DBMOD guard", "Unchanged")
                }
            });
        }

        public static void ShowIndex(Document document, DrawingSnapshot snapshot, EngineeringEntityIndex index, string outputPath)
        {
            EntityIndexStatistics s = index.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_INDEX",
                Title = "Entity Index",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Entity index built",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment entities", s.EquipmentEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Structure entities", s.StructureEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("With both sources", s.EquipmentEntitiesWithBoth.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Grouped tags", s.GroupedTagCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unresolved links", s.UnresolvedLinkCount.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Drawing evidence", s.EquipmentEntitiesWithDrawingEvidence.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Equipment-list evidence", s.EquipmentEntitiesWithEquipmentListEvidence.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Grouped member links", s.GroupedMemberLinkCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Ambiguous links", s.AmbiguousLinkCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Entity index", outputPath)
                }
            });
        }

        public static void ShowQa(Document document, DrawingSnapshot snapshot, QaReport report, string outputPath, string command, string title, string note)
        {
            QaStatistics s = report.Statistics;
            Publish(new VisualResultsData
            {
                Command = command,
                Title = title,
                Drawing = snapshot.FileName,
                Status = report.OverallStatus,
                StatusDetail = "QA evaluation completed",
                OutputPath = outputPath,
                Document = document,
                Note = note,
                Metrics = QaMetrics(s),
                QaFindings = report.Findings
            });
        }

        public static void ShowPlotQa(Document document, DrawingSnapshot snapshot, PlotPlanModel plotPlan, QaReport report, string outputPath)
        {
            PlotPlanStatistics p = plotPlan.Statistics;
            PlotGeometryStatistics g = plotPlan.Geometry.Statistics;
            QaStatistics q = report.Statistics;
            List<VisualMetric> metrics = new List<VisualMetric>
            {
                new VisualMetric("Rules", q.RuleCount.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Findings", q.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Warnings", q.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Errors", q.ErrorFindings.ToString(CultureInfo.InvariantCulture))
            };
            Publish(new VisualResultsData
            {
                Command = "ES_PLOTQA",
                Title = "Plot Plan QA",
                Drawing = snapshot.FileName,
                Status = report.OverallStatus,
                StatusDetail = "Plot-plan QA evaluation completed",
                OutputPath = outputPath,
                Document = document,
                Note = "Placements: " + p.EquipmentPlacementCount.ToString(CultureInfo.InvariantCulture) +
                       " | Incomplete: " + p.IncompletePlacementCount.ToString(CultureInfo.InvariantCulture) +
                       " | Geometry gaps: " + g.MissingAssociationCount.ToString(CultureInfo.InvariantCulture) +
                       " | Spatial gaps: " + g.MissingSpatialConstraintCount.ToString(CultureInfo.InvariantCulture),
                Metrics = metrics,
                QaFindings = report.Findings
            });
        }

        public static void ShowProfileQa(Document document, DrawingSnapshot snapshot, PlotPlanModel plotPlan, QaReport report, ProfileResolution resolution, string outputPath)
        {
            string chain = resolution == null || resolution.AppliedProfileIds.Count == 0
                ? "None"
                : string.Join(" -> ", resolution.AppliedProfileIds);
            ShowPlotQa(document, snapshot, plotPlan, report, outputPath);
            UpdateNote("Applied profile chain: " + chain);
            UpdateCommand("ES_PROFILEQA", "Profile QA");
        }

        public static void ShowCrossQa(Document document, DrawingSnapshot snapshot, ExternalEquipmentRegister register, QaReport report, string outputPath)
        {
            QaStatistics s = report.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_CROSSQA",
                Title = "Cross-Document QA",
                Drawing = snapshot.FileName,
                Status = report.OverallStatus,
                StatusDetail = "External register comparison completed",
                OutputPath = outputPath,
                Document = document,
                Note = "External register: " + register.SourceFileName + " | SHA256: " + register.SourceSha256,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Source rows", register.Statistics.SourceRowCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Expanded identities", register.Statistics.ExpandedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Findings", s.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Warnings", s.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Errors", s.ErrorFindings.ToString(CultureInfo.InvariantCulture))
                },
                QaFindings = report.Findings
            });
        }

        public static void ShowRevisionBaseline(Document document, DrawingSnapshot snapshot, RevisionSnapshot revision, string outputPath)
        {
            Publish(new VisualResultsData
            {
                Command = "ES_REVBASELINE",
                Title = "Revision Baseline",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Read-only baseline created",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment", revision.Statistics.EquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Mode", "Read-only"),
                    new VisualMetric("DWG changes", "None")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Baseline", outputPath),
                    new VisualDetail("Drawing", snapshot.FileName),
                    new VisualDetail("DBMOD guard", "Unchanged")
                }
            });
        }

        public static void ShowRevision(Document document, DrawingSnapshot snapshot, RevisionComparisonReport report, string outputPath)
        {
            RevisionComparisonStatistics s = report.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_REVISION",
                Title = "Revision Comparison",
                Drawing = snapshot.FileName,
                Status = s.TotalChangeCount == 0 ? "UNCHANGED" : "CHANGES FOUND",
                StatusDetail = s.TotalChangeCount == 0 ? "No engineering changes detected" : "Baseline comparison completed",
                OutputPath = outputPath,
                Document = document,
                Note = s.TotalChangeCount == 0
                    ? "Baseline and current drawing match for all tracked revision evidence."
                    : "Review the detected engineering changes below.",
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Baseline equipment", s.BaselineEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Current equipment", s.CurrentEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Added", s.AddedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Removed", s.RemovedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Coordinate changes", s.CoordinateChangedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Attribute changes", s.AttributeChangedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Total changes", s.TotalChangeCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unchanged", s.UnchangedEquipmentCount.ToString(CultureInfo.InvariantCulture))
                },
                RevisionChanges = report.Changes
            });
        }

        public static void ShowFailure(string command, Document document, string drawing, string message)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "Command Failed",
                Drawing = drawing,
                Status = "FAILED",
                StatusDetail = message,
                Document = document,
                Note = "No drawing changes were made.",
                Details = new List<VisualDetail> { new VisualDetail("Reason", message) }
            });
        }

        public static void ShowCancelled(string command, Document document, string drawing, string message)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "Command Cancelled",
                Drawing = drawing,
                Status = "CANCELLED",
                StatusDetail = message,
                Document = document,
                Note = "No drawing changes were made.",
                Details = new List<VisualDetail> { new VisualDetail("State", message) }
            });
        }

        private static List<VisualMetric> QaMetrics(QaStatistics s)
        {
            return new List<VisualMetric>
            {
                new VisualMetric("Rules", s.RuleCount.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Findings", s.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Warnings", s.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Errors", s.ErrorFindings.ToString(CultureInfo.InvariantCulture))
            };
        }

        private static void Publish(VisualResultsData data)
        {
            try
            {
                if (palette == null)
                {
                    control = new VisualResultsControl();
                    palette = new PaletteSet("EngiSentry Results");
                    palette.Add("Results", control);
                    palette.Size = new Size(700, 650);
                }
                control.SetResult(data);
                EnsureUsablePaletteSize(data);
                palette.Visible = true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("EngiSentry visual results error: " + exception);
            }
        }

        private static void EnsureUsablePaletteSize(VisualResultsData data)
        {
            bool hasTable = (data.QaFindings != null && data.QaFindings.Count > 0) ||
                            (data.RevisionChanges != null && data.RevisionChanges.Count > 0);
            int metricCount = data.Metrics == null ? 0 : data.Metrics.Count;
            int metricRows = Math.Max(1, (metricCount + 1) / 2);
            int rowCount = data.QaFindings != null && data.QaFindings.Count > 0
                ? data.QaFindings.Count
                : data.RevisionChanges != null && data.RevisionChanges.Count > 0
                    ? data.RevisionChanges.Count
                    : data.Details == null ? 0 : data.Details.Count;

            int visibleRows = Math.Max(3, Math.Min(rowCount, hasTable ? 12 : 8));
            int preferredWidth = hasTable ? 900 : 680;
            int metricsHeight = metricRows * (hasTable ? 44 : 51) + 12;
            int gridHeight = 28 + visibleRows * 25;
            if (string.Equals(data.Command, "ES_REVISION", StringComparison.OrdinalIgnoreCase) && rowCount > 0 && rowCount <= 8) gridHeight += 14;
            int preferredHeight = Math.Max(380, Math.Min(760, 54 + metricsHeight + 26 + 17 + gridHeight + 38 + 24));

            Size preferredSize = new Size(preferredWidth, preferredHeight);
            palette.MinimumSize = preferredSize;
            Size currentSize = palette.Size;
            palette.Size = new Size(Math.Max(currentSize.Width, preferredWidth), Math.Max(currentSize.Height, preferredHeight));
        }

        private static void UpdateNote(string note)
        {
            if (control != null) control.SetNote(note);
        }

        private static void UpdateCommand(string command, string title)
        {
            if (control != null) control.SetCommandAndTitle(command, title);
        }
    }

    internal sealed class VisualResultsData
    {
        public string Command { get; set; }
        public string Title { get; set; }
        public string Drawing { get; set; }
        public string Status { get; set; }
        public string StatusDetail { get; set; }
        public string OutputPath { get; set; }
        public string Note { get; set; }
        public Document Document { get; set; }
        public List<VisualMetric> Metrics { get; set; } = new List<VisualMetric>();
        public List<VisualDetail> Details { get; set; } = new List<VisualDetail>();
        public List<QaFinding> QaFindings { get; set; } = new List<QaFinding>();
        public List<RevisionChangeRecord> RevisionChanges { get; set; } = new List<RevisionChangeRecord>();
    }

    internal sealed class VisualMetric
    {
        public VisualMetric(string label, string value) { Label = label; Value = value; }
        public string Label { get; private set; }
        public string Value { get; private set; }
    }

    internal sealed class VisualDetail
    {
        public VisualDetail(string label, string value) { Label = label; Value = value ?? string.Empty; }
        public string Label { get; private set; }
        public string Value { get; private set; }
    }

    internal sealed class VisualResultsControl : UserControl
    {
        private readonly Label titleLabel = new Label();
        private readonly Label commandLabel = new Label();
        private readonly Label drawingLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label noteLabel = new Label();
        private readonly FlowLayoutPanel metricsPanel = new FlowLayoutPanel();
        private readonly DataGridView detailGrid = new DataGridView();
        private readonly Label gridLabel = new Label();
        private readonly Button openOutputButton = new Button();
        private readonly Button zoomEvidenceButton = new Button();
        private readonly Panel bodyPanel = new Panel();
        private VisualResultsData current;

        public VisualResultsControl()
        {
            BackColor = Color.FromArgb(30, 31, 34);
            ForeColor = Color.FromArgb(235, 237, 240);
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Dock = DockStyle.Fill;
            BuildLayout();
        }

        public void SetResult(VisualResultsData data)
        {
            current = data;
            titleLabel.Text = data.Title ?? "EngiSentry Results";
            commandLabel.Text = data.Command ?? string.Empty;
            drawingLabel.Text = "Drawing: " + (string.IsNullOrWhiteSpace(data.Drawing) ? "<none>" : data.Drawing);
            statusLabel.Text = string.IsNullOrWhiteSpace(data.StatusDetail) ? data.Status : data.Status + "  |  " + data.StatusDetail;
            statusLabel.ForeColor = StatusColor(data.Status);
            noteLabel.Text = data.Note ?? string.Empty;
            noteLabel.Visible = !string.IsNullOrWhiteSpace(data.Note);
            openOutputButton.Enabled = !string.IsNullOrWhiteSpace(data.OutputPath);
            zoomEvidenceButton.Visible = data.QaFindings != null && data.QaFindings.Count > 0;
            BuildMetrics(data.Metrics);
            BuildGrid(data);
        }

        public void SetNote(string note)
        {
            noteLabel.Text = note ?? string.Empty;
            noteLabel.Visible = !string.IsNullOrWhiteSpace(note);
        }

        public void SetCommandAndTitle(string command, string title)
        {
            commandLabel.Text = command;
            titleLabel.Text = title;
        }

        private void BuildLayout()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(38, 40, 44), Padding = new Padding(10, 5, 10, 4) };
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 20;
            titleLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 12.5F, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            commandLabel.Dock = DockStyle.Right;
            commandLabel.Width = 115;
            commandLabel.TextAlign = ContentAlignment.TopRight;
            commandLabel.ForeColor = Color.FromArgb(160, 166, 175);
            drawingLabel.Dock = DockStyle.Fill;
            drawingLabel.AutoEllipsis = true;
            drawingLabel.ForeColor = Color.FromArgb(190, 195, 202);
            header.Controls.Add(drawingLabel);
            header.Controls.Add(commandLabel);
            header.Controls.Add(titleLabel);

            metricsPanel.Dock = DockStyle.Top;
            metricsPanel.Height = 104;
            metricsPanel.Padding = new Padding(8, 6, 8, 6);
            metricsPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            metricsPanel.WrapContents = true;
            metricsPanel.AutoScroll = false;
            metricsPanel.BackColor = Color.FromArgb(30, 31, 34);
            metricsPanel.SizeChanged += (sender, args) => { if (current != null) BuildMetrics(current.Metrics); };

            Panel statusPanel = new Panel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(10, 3, 10, 3), BackColor = Color.FromArgb(30, 31, 34) };
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Height = 19;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            noteLabel.Dock = DockStyle.Bottom;
            noteLabel.Height = 19;
            noteLabel.AutoEllipsis = true;
            noteLabel.TextAlign = ContentAlignment.MiddleLeft;
            noteLabel.ForeColor = Color.FromArgb(160, 166, 175);
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Controls.Add(noteLabel);

            bodyPanel.Dock = DockStyle.Fill;
            bodyPanel.Padding = new Padding(10, 2, 10, 8);
            gridLabel.Dock = DockStyle.Top;
            gridLabel.Height = 17;
            gridLabel.Text = "DETAILS";
            gridLabel.ForeColor = Color.FromArgb(160, 166, 175);
            gridLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            detailGrid.Dock = DockStyle.Fill;
            ConfigureGrid();
            bodyPanel.Controls.Add(detailGrid);
            bodyPanel.Controls.Add(gridLabel);

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(6, 3, 6, 4), BackColor = Color.FromArgb(38, 40, 44), FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
            openOutputButton.Text = "Open Output Folder";
            openOutputButton.AutoSize = true;
            openOutputButton.FlatStyle = FlatStyle.Flat;
            openOutputButton.FlatAppearance.BorderColor = Color.FromArgb(80, 84, 92);
            openOutputButton.ForeColor = Color.White;
            openOutputButton.BackColor = Color.FromArgb(52, 55, 61);
            openOutputButton.Click += OpenOutputFolder;
            zoomEvidenceButton.Text = "Zoom To Evidence";
            zoomEvidenceButton.AutoSize = true;
            zoomEvidenceButton.FlatStyle = FlatStyle.Flat;
            zoomEvidenceButton.FlatAppearance.BorderColor = Color.FromArgb(80, 84, 92);
            zoomEvidenceButton.ForeColor = Color.White;
            zoomEvidenceButton.BackColor = Color.FromArgb(52, 55, 61);
            zoomEvidenceButton.Enabled = false;
            zoomEvidenceButton.Click += ZoomToEvidence;
            footer.Controls.Add(zoomEvidenceButton);
            footer.Controls.Add(openOutputButton);

            Controls.Add(bodyPanel);
            Controls.Add(footer);
            Controls.Add(statusPanel);
            Controls.Add(metricsPanel);
            Controls.Add(header);
        }

        private void ConfigureGrid()
        {
            detailGrid.AllowUserToAddRows = false;
            detailGrid.AllowUserToDeleteRows = false;
            detailGrid.AllowUserToResizeRows = false;
            detailGrid.BackgroundColor = Color.FromArgb(30, 31, 34);
            detailGrid.BorderStyle = BorderStyle.None;
            detailGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            detailGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            detailGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(52, 55, 61), ForeColor = Color.FromArgb(230, 233, 237), Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold) };
            detailGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 31, 34), ForeColor = Color.FromArgb(225, 228, 232), SelectionBackColor = Color.FromArgb(55, 75, 95), SelectionForeColor = Color.White, WrapMode = DataGridViewTriState.False };
            detailGrid.EnableHeadersVisualStyles = false;
            detailGrid.MultiSelect = false;
            detailGrid.ReadOnly = true;
            detailGrid.RowHeadersVisible = false;
            detailGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            detailGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            detailGrid.RowTemplate.Height = 25;
            detailGrid.ColumnHeadersHeight = 26;
            detailGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            detailGrid.SelectionChanged += DetailGridSelectionChanged;
        }

        private void BuildMetrics(IEnumerable<VisualMetric> metrics)
        {
            metricsPanel.SuspendLayout();
            metricsPanel.Controls.Clear();
            List<VisualMetric> items = (metrics ?? Enumerable.Empty<VisualMetric>()).ToList();
            bool hasTable = current != null &&
                ((current.QaFindings != null && current.QaFindings.Count > 0) ||
                 (current.RevisionChanges != null && current.RevisionChanges.Count > 0));
            int available = Math.Max(180, metricsPanel.ClientSize.Width - metricsPanel.Padding.Horizontal - 2);
            int columns = 2;
            int gap = 5;
            int cardWidth = Math.Max(74, (available - gap * 2) / 2);
            int cardHeight = hasTable ? 40 : 48;
            int rows = Math.Max(1, (items.Count + columns - 1) / columns);
            metricsPanel.Height = metricsPanel.Padding.Vertical + rows * (cardHeight + 4);
            foreach (VisualMetric metric in items)
            {
                Panel card = new Panel { Width = cardWidth, Height = cardHeight, Margin = new Padding(0, 0, gap, 4), BackColor = Color.FromArgb(43, 45, 50), Padding = new Padding(8, 5, 7, 4) };
                Label value = new Label { Dock = DockStyle.Top, Height = hasTable ? 18 : 22, Text = metric.Value, ForeColor = Color.White, Font = new System.Drawing.Font("Segoe UI Semibold", hasTable ? 10.5F : 12.5F, FontStyle.Bold), AutoEllipsis = true };
                Label label = new Label { Dock = DockStyle.Fill, Text = metric.Label.ToUpperInvariant(), ForeColor = Color.FromArgb(168, 174, 184), Font = new System.Drawing.Font("Segoe UI", 7F, FontStyle.Regular), AutoEllipsis = true };
                card.Controls.Add(label);
                card.Controls.Add(value);
                metricsPanel.Controls.Add(card);
            }
            metricsPanel.ResumeLayout();
        }

        private void BuildGrid(VisualResultsData data)
        {
            detailGrid.Columns.Clear();
            detailGrid.Rows.Clear();
            detailGrid.ScrollBars = ScrollBars.Vertical;
            if (data.QaFindings != null && data.QaFindings.Count > 0)
            {
                gridLabel.Text = "FINDINGS";
                AddColumn("Severity", 82);
                AddColumn("Rule", 105);
                AddColumn("Tag / Subject", 145);
                AddColumn("Message", 420);
                foreach (QaFinding finding in data.QaFindings)
                {
                    int row = detailGrid.Rows.Add(finding.Severity, finding.RuleId, Subject(finding), finding.Message);
                    detailGrid.Rows[row].Tag = finding;
                }
            }
            else if (data.RevisionChanges != null && data.RevisionChanges.Count > 0)
            {
                gridLabel.Text = "CHANGES";
                detailGrid.ScrollBars = data.RevisionChanges.Count <= 8 ? ScrollBars.None : ScrollBars.Vertical;
                AddColumn("Change", 145);
                AddColumn("Equipment Tag", 150);
                AddColumn("Details", 520);
                foreach (RevisionChangeRecord change in data.RevisionChanges)
                {
                    int row = detailGrid.Rows.Add(RevisionChangeLabel(change), change.NormalizedTag, RevisionChangeDetail(change));
                    DataGridViewRow gridRow = detailGrid.Rows[row];
                    gridRow.Height = 25;
                    gridRow.Cells[0].Style.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
                    gridRow.Cells[0].Style.ForeColor = RevisionChangeColor(change);
                }
                detailGrid.ClearSelection();
            }
            else if (string.Equals(data.Command, "ES_REVISION", StringComparison.OrdinalIgnoreCase))
            {
                gridLabel.Text = "CHANGES";
                AddColumn("Result", 710);
                detailGrid.ScrollBars = ScrollBars.None;
                int row = detailGrid.Rows.Add("No engineering changes detected between the active baseline and current drawing.");
                detailGrid.Rows[row].Height = 28;
                detailGrid.ClearSelection();
            }
            else
            {
                gridLabel.Text = "DETAILS";
                AddColumn("Metric", 190);
                AddColumn("Value", 520);
                foreach (VisualDetail detail in data.Details ?? new List<VisualDetail>()) detailGrid.Rows.Add(detail.Label, detail.Value);
            }
            zoomEvidenceButton.Enabled = false;
        }

        private static string RevisionChangeLabel(RevisionChangeRecord change)
        {
            switch (change == null ? string.Empty : change.ChangeKind)
            {
                case "EquipmentAdded": return "ADDED";
                case "EquipmentRemoved": return "REMOVED";
                case "EquipmentCoordinateEvidenceChanged": return "COORDINATE CHANGED";
                case "EquipmentAttributeEvidenceChanged": return "ATTRIBUTE CHANGED";
                default: return string.IsNullOrWhiteSpace(change?.ChangeKind) ? "CHANGED" : change.ChangeKind;
            }
        }

        private static Color RevisionChangeColor(RevisionChangeRecord change)
        {
            switch (change == null ? string.Empty : change.ChangeKind)
            {
                case "EquipmentAdded": return Color.FromArgb(112, 214, 154);
                case "EquipmentRemoved": return Color.FromArgb(238, 139, 139);
                case "EquipmentCoordinateEvidenceChanged": return Color.FromArgb(105, 190, 245);
                case "EquipmentAttributeEvidenceChanged": return Color.FromArgb(238, 193, 106);
                default: return Color.White;
            }
        }

        private static string RevisionChangeDetail(RevisionChangeRecord change)
        {
            if (change == null) return string.Empty;
            switch (change.ChangeKind)
            {
                case "EquipmentAdded":
                    return "New equipment detected in the current drawing.";
                case "EquipmentRemoved":
                    return "Present in the baseline but not found in the current drawing.";
                case "EquipmentCoordinateEvidenceChanged":
                    return "Coordinates: " + RevisionCoordinates(change.Baseline) + "  ->  " + RevisionCoordinates(change.Current);
                case "EquipmentAttributeEvidenceChanged":
                    return RevisionAttributeDetail(change);
                default:
                    return string.Join(", ", change.ChangedFields ?? new List<string>());
            }
        }

        private static string RevisionCoordinates(RevisionEquipmentRecord record)
        {
            if (record == null || record.CompleteCoordinatePairs == null || record.CompleteCoordinatePairs.Count == 0) return "not resolved";
            return string.Join("; ", record.CompleteCoordinatePairs.Select(x =>
                "E " + x.Easting.ToString(CultureInfo.InvariantCulture) + " / N " + x.Northing.ToString(CultureInfo.InvariantCulture)));
        }

        private static string RevisionAttributeDetail(RevisionChangeRecord change)
        {
            var parts = new List<string>();
            foreach (string field in change.ChangedFields ?? new List<string>())
            {
                if (string.Equals(field, "Description", StringComparison.Ordinal))
                    parts.Add("Description: " + RevisionValues(change.Baseline?.DescriptionValues) + "  ->  " + RevisionValues(change.Current?.DescriptionValues));
                else if (string.Equals(field, "Installation", StringComparison.Ordinal))
                    parts.Add("Installation: " + RevisionValues(change.Baseline?.InstallationValues) + "  ->  " + RevisionValues(change.Current?.InstallationValues));
                else
                    parts.Add(field + " changed");
            }
            return parts.Count == 0 ? "Tracked equipment attributes changed." : string.Join(" | ", parts);
        }

        private static string RevisionValues(IEnumerable<string> values)
        {
            if (values == null) return "none";
            List<string> items = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return items.Count == 0 ? "none" : string.Join("; ", items);
        }

        private void AddColumn(string name, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name, MinimumWidth = 55, FillWeight = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            detailGrid.Columns.Add(column);
        }

        private void DetailGridSelectionChanged(object sender, EventArgs e)
        {
            DataGridViewRow row = detailGrid.CurrentRow;
            QaFinding finding = row == null ? null : row.Tag as QaFinding;
            zoomEvidenceButton.Enabled = finding != null && FirstHandle(finding) != null && current != null && current.Document != null;
        }

        private void OpenOutputFolder(object sender, EventArgs e)
        {
            try
            {
                if (current == null || string.IsNullOrWhiteSpace(current.OutputPath)) return;
                string folder = Directory.Exists(current.OutputPath) ? current.OutputPath : Path.GetDirectoryName(current.OutputPath);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    SetNote("Output folder is not available: " + current.OutputPath);
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = false });
            }
            catch (Exception exception)
            {
                SetNote("Could not open output folder: " + exception.Message);
            }
        }

        private void ZoomToEvidence(object sender, EventArgs e)
        {
            try
            {
                DataGridViewRow row = detailGrid.CurrentRow;
                QaFinding finding = row == null ? null : row.Tag as QaFinding;
                string handleText = finding == null ? null : FirstHandle(finding);
                Document document = current == null ? null : current.Document;
                if (document == null || string.IsNullOrWhiteSpace(handleText))
                {
                    SetNote("No valid source handle is available for this finding.");
                    return;
                }
                long handleValue;
                if (!long.TryParse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handleValue))
                {
                    SetNote("The finding source handle is invalid.");
                    return;
                }
                ObjectId objectId = document.Database.GetObjectId(false, new Handle(handleValue), 0);
                Extents3d extents;
                using (Transaction transaction = document.TransactionManager.StartTransaction())
                {
                    DBObject dbObject = transaction.GetObject(objectId, OpenMode.ForRead, false);
                    Entity entity = dbObject as Entity;
                    if (entity == null) throw new InvalidOperationException("The source object is not drawable.");
                    extents = entity.GeometricExtents;
                }
                document.Editor.SetImpliedSelection(new[] { objectId });
                using (ViewTableRecord view = document.Editor.GetCurrentView())
                {
                    double width = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, 1.0);
                    double height = Math.Max(extents.MaxPoint.Y - extents.MinPoint.Y, 1.0);
                    view.CenterPoint = new Point2d((extents.MinPoint.X + extents.MaxPoint.X) / 2.0, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);
                    view.Width = width * 1.6;
                    view.Height = height * 1.6;
                    document.Editor.SetCurrentView(view);
                }
                SetNote("Evidence selected and view centered on handle " + handleText + ".");
            }
            catch (Exception exception)
            {
                SetNote("Evidence could not be resolved: " + exception.Message);
            }
        }

        private static string Subject(QaFinding finding)
        {
            if (!string.IsNullOrWhiteSpace(finding.Evidence == null ? null : finding.Evidence.NormalizedTag)) return finding.Evidence.NormalizedTag;
            if (!string.IsNullOrWhiteSpace(finding.SubjectId)) return finding.SubjectId;
            return finding.SubjectType;
        }

        private static string FirstHandle(QaFinding finding)
        {
            IEnumerable<string> handles = (finding.SourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.DrawingTagSourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.EquipmentListSourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.Occurrences == null ? Enumerable.Empty<string>() : finding.Evidence.Occurrences.Select(x => x.SourceHandle))
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : new[] { finding.Evidence.TagSourceHandle, finding.Evidence.GeometryAssociation == null ? null : finding.Evidence.GeometryAssociation.LabelSourceHandle, finding.Evidence.GeometryAssociation == null ? null : finding.Evidence.GeometryAssociation.SelectedGeometryHandle, finding.Evidence.SpatialCoordinateConstraint == null ? null : finding.Evidence.SpatialCoordinateConstraint.LabelSourceHandle, finding.Evidence.SpatialCoordinateConstraint == null ? null : finding.Evidence.SpatialCoordinateConstraint.SelectedGeometryHandle });
            return handles.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        private static Color StatusColor(string status)
        {
            string value = (status ?? string.Empty).ToUpperInvariant();
            if (value.Contains("FAIL") || value.Contains("ERROR")) return Color.FromArgb(255, 112, 112);
            if (value.Contains("WARN") || value.Contains("CHANGE")) return Color.FromArgb(255, 190, 92);
            if (value.Contains("CANCEL")) return Color.FromArgb(175, 181, 190);
            return Color.FromArgb(108, 220, 155);
        }
    }
}
