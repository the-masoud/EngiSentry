using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    internal sealed class ProjectDistanceCriteriaDialog : Form
    {
        private readonly string projectDirectory;
        private readonly TextBox accessRoad;
        private readonly TextBox securityFence;
        private readonly TextBox unitBoundary;

        private ProjectDistanceCriteriaDialog(string projectDirectory, ProjectDistanceCriteria criteria)
        {
            this.projectDirectory = projectDirectory;
            Text = "EngiSentry — Project Distance Criteria";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(570, 255);

            var projectLabel = new Label { Left = 16, Top = 16, Width = 80, Text = "Project:" };
            var projectValue = new Label { Left = 100, Top = 16, Width = 450, Height = 36, Text = projectDirectory, AutoEllipsis = true };
            var roadLabel = new Label { Left = 16, Top = 68, Width = 315, Text = "Equipment → Road" };
            accessRoad = new TextBox { Left = 340, Top = 64, Width = 120, Text = criteria.AccessRoadEdgeMinimumMeters.ToString(CultureInfo.CurrentCulture) };
            var roadUnit = new Label { Left = 470, Top = 68, Width = 30, Text = "m" };
            var fenceLabel = new Label { Left = 16, Top = 103, Width = 315, Text = "Equipment → Plant / Security Fence" };
            securityFence = new TextBox { Left = 340, Top = 99, Width = 120, Text = criteria.SecurityFenceMinimumMeters.ToString(CultureInfo.CurrentCulture) };
            var fenceUnit = new Label { Left = 470, Top = 103, Width = 30, Text = "m" };
            var unitLabel = new Label { Left = 16, Top = 138, Width = 315, Text = "Equipment → Unit Boundary" };
            unitBoundary = new TextBox { Left = 340, Top = 134, Width = 120, Text = criteria.UnitBoundaryMinimumMeters.ToString(CultureInfo.CurrentCulture) };
            var unit = new Label { Left = 470, Top = 138, Width = 30, Text = "m" };
            var note = new Label { Left = 16, Top = 166, Width = 535, Height = 40, Text = "Project criteria are stored outside the DWG.\r\nConfirm them to continue, or edit them to match the governing project requirements." };
            var save = new Button { Left = 330, Top = 216, Width = 120, Text = "Save & Continue", DialogResult = DialogResult.None };
            var cancel = new Button { Left = 460, Top = 216, Width = 90, Text = "Cancel", DialogResult = DialogResult.Cancel };
            save.Click += SaveClicked;
            Controls.AddRange(new Control[] { projectLabel, projectValue, roadLabel, accessRoad, roadUnit, fenceLabel, securityFence, fenceUnit, unitLabel, unitBoundary, unit, note, save, cancel });
            CancelButton = cancel;
        }

        internal ProjectDistanceCriteria? Result { get; private set; }

        internal static ProjectDistanceCriteria? Show(string projectDirectory, ProjectDistanceCriteria criteria)
        {
            using (var dialog = new ProjectDistanceCriteriaDialog(projectDirectory, criteria)) return dialog.ShowDialog() == DialogResult.OK ? dialog.Result : null;
        }

        private void SaveClicked(object? sender, EventArgs eventArgs)
        {
            if (!decimal.TryParse(accessRoad.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal road) || road <= 0m || !decimal.TryParse(securityFence.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal fence) || fence <= 0m || !decimal.TryParse(unitBoundary.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal unit) || unit <= 0m)
            {
                MessageBox.Show(this, "Enter positive decimal meter values for all criteria.", "EngiSentry — Project Distance Criteria", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = projectDirectory, AccessRoadEdgeMinimumMeters = road, SecurityFenceMinimumMeters = fence, UnitBoundaryMinimumMeters = unit };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
