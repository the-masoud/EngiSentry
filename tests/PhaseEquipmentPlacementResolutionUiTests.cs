#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    // Focused UI-contract tests for the ES_PLACEMENTRESOLVE engineer surface. They assert the
    // structure and readability contract of the bilingual surface (sections, explicit labels, dark
    // theme, actions, inline status, layout integrity, long-value access) and prove the rework did
    // not change behaviour: a cancelled pick still writes nothing and a complete explicit save still
    // writes exactly one review plus one resolution.
    //
    // The tests are deliberately structural, not pixel- or RGB-literal: readability is asserted as a
    // light-on-dark contrast property, never as a hardcoded colour value. Every review and resolution
    // is written to an isolated temporary root under an unmistakably TEST-ONLY reviewer identity.
    internal static class PhaseEquipmentPlacementResolutionUiTests
    {
        private static int assertions;
        private const string DrawingName = "P11.2-UI-TEST.dwg";
        private static readonly string DrawingSha = new string('C', 64);
        private const string Units = "Millimeters";

        internal static void Run()
        {
            string reviewRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112-REVIEW-" + Guid.NewGuid().ToString("N"));
            string resolutionRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112-RES-" + Guid.NewGuid().ToString("N"));
            string dispositionRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112-DISP-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(reviewRoot);
            Directory.CreateDirectory(resolutionRoot);
            Directory.CreateDirectory(dispositionRoot);
            try
            {
                var reviewService = new ProjectEngineerReviewService(reviewRoot);
                var dispositionService = new PlacementProposalDispositionService(dispositionRoot);
                var resolutionService = new EquipmentPlacementResolutionService(resolutionRoot, reviewService, dispositionService);

                SectionContractTests(reviewService, resolutionService);
                ExplicitLabelTests(reviewService, resolutionService);
                DarkThemeReadabilityTests(reviewService, resolutionService);
                ActionContractTests(reviewService, resolutionService);
                StatusMessageTests(reviewService, resolutionService);
                LayoutIntegrityTests(reviewService, resolutionService);
                LongValueAccessTests(reviewService, resolutionService);
                BehaviorPreservationTests(reviewService, resolutionService, reviewRoot);

                Console.WriteLine("Phase Equipment Placement Resolution UI assertions passed: " + assertions);
            }
            finally
            {
                if (Directory.Exists(reviewRoot)) Directory.Delete(reviewRoot, true);
                if (Directory.Exists(resolutionRoot)) Directory.Delete(resolutionRoot, true);
                if (Directory.Exists(dispositionRoot)) Directory.Delete(dispositionRoot, true);
            }
        }

        // 1. The surface is composed of the required labelled sections, in workflow order.
        private static void SectionContractTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            control.Size = new Size(1000, 960);
            PerformLayout(control);
            // visual order is the docking order, not the z-order, so it is read from the laid-out bounds
            List<string> captions = Descendants(control).OfType<Card>().OrderBy(c => c.Top).Select(c => c.Caption).ToList();
            CollectionAssert(captions, new List<string>
            {
                "EQUIPMENT / PROPOSAL", "PLACEMENT SELECTION", "ENGINEER REVIEW", "DECISION", "REVIEWER", "TECHNICAL DETAILS"
            }, "The surface must present the required sections as labelled cards in workflow order.");
            Assert(Descendants(control).OfType<Label>().Any(l => l.Text == "Equipment Placement Resolution"), "The header title is missing.");
            Assert(Descendants(control).OfType<Label>().Any(l => l.Text.Contains(DrawingName) && l.Text.Contains(Units)), "The header must carry the drawing identity and units.");
            Assert(Descendants(control).OfType<Button>().Any(b => b.Text == "Record Decision"), "The pinned action bar must carry the primary record action.");
            // the full SHA-256 must not dominate the header; it lives in the technical details
            Assert(!Descendants(control).OfType<Label>().Any(l => l.Text.Contains(DrawingSha)), "The full SHA-256 must not be shown in the primary header.");
            control.Dispose();
        }

        // 2. Classification and Disposition each carry their own explicit, visible caption.
        private static void ExplicitLabelTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            ComboBox classification = Combo(control, "Classification");
            Assert(classification.Visible, "The Classification field must be visible.");
            string classificationCaption = FieldCaption(classification);
            Assert(string.Equals(classificationCaption, "Classification", StringComparison.OrdinalIgnoreCase),
                "The Classification combo must carry an explicit visible 'Classification' label. Actual: " + (classificationCaption ?? "<none>"));
            Assert(FieldCaptionLabel(classification).Visible, "The Classification caption must be visible, not merely present.");
            control.Dispose();

            // The Disposition field is proposal-only, so it must appear (with its own caption) exactly
            // when a generated proposal is selected — never as an unlabelled orphan combo.
            PlacementProposal proposal = BuildProposal();
            var withProposal = NewControl(reviewService, resolutionService, new StubPickProvider(null), new[] { proposal });
            ListBox list = Descendants(withProposal).OfType<ListBox>().Single();
            Assert(list.Items.Count == 2, "The queue must present the unresolved placement and the generated proposal. Actual: " + list.Items.Count);
            list.SelectedIndex = 1;
            ComboBox disposition = Combo(withProposal, "PlacementProposalDisposition");
            Assert(disposition.Visible, "Selecting a generated proposal must reveal the Disposition field.");
            string dispositionCaption = FieldCaption(disposition);
            Assert(string.Equals(dispositionCaption, "Disposition", StringComparison.OrdinalIgnoreCase),
                "The Disposition combo must carry an explicit visible 'Disposition' label. Actual: " + (dispositionCaption ?? "<none>"));
            Assert(!string.Equals(classificationCaption, dispositionCaption, StringComparison.OrdinalIgnoreCase),
                "Classification and Disposition must be distinguishable by their captions.");
            withProposal.Dispose();
        }

        // 3. Dark-theme readability: nothing renders as a light block, and every input is light-on-dark.
        private static void DarkThemeReadabilityTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            Assert(control.BackColor.GetBrightness() < 0.5f, "The surface root must use the dark palette background.");

            foreach (Control c in Descendants(control))
            {
                // buttons and the status pill are asserted separately: the primary action is
                // intentionally a bright accent, and the pill paints its own tint
                if (!c.Visible || c is Button || c is BadgeLabel) continue;
                Assert(c.BackColor.GetBrightness() < 0.6f, "A control must not render as a light block on the dark surface: " + Describe(c));
            }

            foreach (Control input in Descendants(control).Where(c => c is TextBox || c is ComboBox || c is ListBox))
            {
                Assert(input.BackColor != SystemColors.Window, "A field must not fall back to the light system window colour: " + Describe(input));
                Assert(input.ForeColor != SystemColors.ControlText, "A field must not fall back to the system control text colour: " + Describe(input));
                Assert(input.ForeColor.GetBrightness() - input.BackColor.GetBrightness() > 0.3f, "A field must keep readable light-on-dark contrast: " + Describe(input));
            }

            foreach (Label label in Descendants(control).OfType<Label>())
            {
                Assert(label.ForeColor != SystemColors.ControlText, "A label must not fall back to the system control text colour: " + Describe(label));
                Assert(label.ForeColor.GetBrightness() > 0.4f, "A label must stay legible on the dark surface: " + Describe(label));
            }

            // no favourable answer is ever preselected
            foreach (ComboBox combo in Descendants(control).OfType<ComboBox>())
                Assert(combo.SelectedIndex == 0 && (string)combo.SelectedItem == string.Empty, "No answer or decision may be preselected: " + Describe(combo));
            control.Dispose();
        }

        // 4. Actions are readable, distinct, and stay legible when disabled.
        private static void ActionContractTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            List<Button> buttons = Descendants(control).OfType<Button>().ToList();
            CollectionAssert(buttons.Select(b => b.Text).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                new List<string> { "Clear Selection", "Pick Equipment", "Pick Free-Space Point", "Record Decision", "Show Technical Details" },
                "The action labels must stay exactly the accepted set.");

            Button save = buttons.Single(b => b.Text == "Record Decision");
            Button pick = buttons.Single(b => b.Text == "Pick Equipment");
            Assert(save.Font.Bold && !pick.Font.Bold, "The primary record action must carry the primary emphasis font.");
            Assert(buttons.All(b => b.FlatStyle == FlatStyle.Flat), "Every action must use the flat dark-theme button style.");

            Assert(!save.Enabled, "Record Decision must start disabled before a pick is captured.");
            Assert(save.ForeColor.GetBrightness() > 0.15f, "A disabled action must stay legible instead of fading into the surface.");
            Assert(save.BackColor.GetBrightness() < 0.6f, "A disabled action must stay on the dark surface.");

            save.Enabled = true;
            Assert(save.BackColor == ReviewUi.Primary, "An enabled primary action must use the primary accent surface.");
            Assert(save.BackColor != pick.BackColor, "The primary record action must be visually distinct from the secondary pick actions.");
            save.Enabled = false;
            Assert(save.BackColor != ReviewUi.Primary, "A disabled primary action must drop the primary accent surface.");
            control.Dispose();
        }

        // 5. Normal workflow feedback is an inline status line, never a modal dialog.
        private static void StatusMessageTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            List<SeverityMessage> messages = Descendants(control).OfType<SeverityMessage>().ToList();
            Assert(messages.Count == 1, "Normal workflow feedback must use exactly one inline status line.");
            SeverityMessage status = messages[0];
            Assert(status.Text == string.Empty, "The status line must start empty.");
            Assert(status.Severity == SeverityMessage.None, "The status line must start without a severity claim.");
            Assert(status.Parent is TableLayoutPanel, "The status line must sit in the pinned action bar, not float in the card column.");

            // a cancelled pick reports inline; the flow never needs a modal dialog
            Button(control, "Pick Equipment").PerformClick();
            Assert(status.Text.Length > 0, "A cancelled pick must report an inline status message.");
            Assert(status.Severity == SeverityMessage.Warning, "A cancelled pick must be reported as a warning.");
            Assert(status.BackColor.GetBrightness() < 0.6f, "The status line must stay on the dark surface.");
            control.Dispose();
        }

        // 6. Layout integrity: no overlap, no degenerate fields, and a scrollable column.
        private static void LayoutIntegrityTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            control.Size = new Size(1000, 960);
            PerformLayout(control);
            string overlap = FirstOverlap(control);
            Assert(overlap == null, "No two controls may overlap in the placement surface. " + overlap);
            // only the fields the engineer can actually see take part in the size contract; the
            // technical-details fields are collapsed by default and are asserted once expanded below
            string smallField = Descendants(control).OfType<TextBox>().Where(t => t.Visible && (t.Width < 40 || t.Height < 16)).Select(Describe).FirstOrDefault();
            Assert(smallField == null, "Every visible text field must stay usable at the accepted size. Offender: " + smallField);
            string smallCombo = Descendants(control).OfType<ComboBox>().Where(c => c.Visible && (c.Width < 40 || c.Height < 16)).Select(Describe).FirstOrDefault();
            Assert(smallCombo == null, "Every visible decision/answer field must stay usable at the accepted size. Offender: " + smallCombo);
            Assert(Descendants(control).OfType<Button>().All(b => b.Width >= 60 && b.Height >= 20), "Every action must stay usable at the accepted size.");

            // one row per question, each with its own answer control, and never overlapping
            TableLayoutPanel questions = Descendants(control).OfType<TableLayoutPanel>()
                .Single(t => t.Controls.OfType<Control>().Count(c => c.Tag as string != null) == EquipmentPlacementResolutionQuestions.All.Count);
            Assert(questions.RowCount == EquipmentPlacementResolutionQuestions.All.Count, "Every question must own a dedicated row.");
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(control, question.QuestionId);
                Assert(answer.Height >= 16 && answer.Width >= 40, "A question answer control must stay usable: " + question.QuestionId);
            }

            // expanding the collapsed technical details must lay out its value fields at a usable size
            Button(control, "Show Technical Details").PerformClick();
            PerformLayout(control);
            Assert(Descendants(control).OfType<TextBox>().Where(t => t.AccessibleName == "TechnicalValue")
                .All(t => t.Visible && t.Width >= 40 && t.Height >= 16), "Technical value fields must stay usable once the section is expanded.");

            // shrinking the palette must degrade into scrolling, never into overlap or clipping
            Panel scroller = Descendants(control).OfType<Panel>().FirstOrDefault(p => p.AutoScroll);
            Assert(scroller != null, "The card column must be scrollable so no section becomes unreachable when the palette is short.");
            control.Size = new Size(700, 520);
            PerformLayout(control);
            string smallOverlap = FirstOverlap(control);
            Assert(smallOverlap == null, "The surface must stay overlap-free when the palette is small. " + smallOverlap);
            control.Size = new Size(1000, 960);
            PerformLayout(control);
            string restoredOverlap = FirstOverlap(control);
            Assert(restoredOverlap == null, "The surface must stay overlap-free after resizing back. " + restoredOverlap);
            control.Dispose();
        }

        // 7. Long identities and hashes stay readable in full inside the collapsed technical details.
        private static void LongValueAccessTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            PlacementProposal proposal = BuildProposal();
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(null), new[] { proposal });
            Descendants(control).OfType<ListBox>().Single().SelectedIndex = 1;

            Panel technicalBody = TechnicalBody(control);
            Assert(!technicalBody.Visible, "Technical details must be collapsed by default.");
            Button(control, "Show Technical Details").PerformClick();
            Assert(technicalBody.Visible, "The technical details toggle must reveal the section.");

            List<TextBox> values = Descendants(control).OfType<TextBox>().Where(t => t.AccessibleName == "TechnicalValue").ToList();
            Assert(values.Count > 0, "Technical details must present labelled technical values.");
            Assert(values.All(t => t.ReadOnly), "Technical values must stay read-only evidence.");
            Assert(values.All(t => t.RightToLeft == RightToLeft.No), "Technical values must always stay left-to-right.");
            Assert(values.Any(t => t.Text == proposal.ProposalId), "The full proposal identity must be readable in the technical details.");
            Assert(values.Any(t => t.Text == proposal.ProposalFingerprint), "The full proposal fingerprint must be readable in the technical details.");
            Assert(values.Any(t => t.Text == DrawingSha), "The drawing SHA-256 must be readable in the technical details.");
            control.Dispose();
        }

        // 8. Behaviour preservation: the rework changed presentation only.
        private static void BehaviorPreservationTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService, string reviewRoot)
        {
            int reviewsBefore = ResponseFiles(reviewRoot);
            int recordsBefore = RecordFiles(resolutionService);

            var cancelled = NewControl(reviewService, resolutionService, new StubPickProvider(null));
            Assert(cancelled.UnresolvedCount == 1, "The surface must present the unresolved placement.");
            Assert(!Button(cancelled, "Record Decision").Enabled, "Record Decision must be disabled before a pick is captured.");
            Button(cancelled, "Pick Equipment").PerformClick();
            Assert(cancelled.CurrentPick == null, "A cancelled pick must not capture anything.");
            Assert(ResponseFiles(reviewRoot) == reviewsBefore && RecordFiles(resolutionService) == recordsBefore, "A cancelled pick must not write a review or a resolution.");
            cancelled.Dispose();

            var pick = new EquipmentPlacementPick { Source = EquipmentPlacementResolutionSources.EntityPick, PointX = 4321, PointY = 8765, PointZ = 0, Handle = "H97", Layer = "EQ-TAG" };
            var control = NewControl(reviewService, resolutionService, new StubPickProvider(pick));
            Button(control, "Pick Equipment").PerformClick();
            Assert(control.CurrentPick != null && control.CurrentPick.Handle == "H97", "An entity pick must capture the proven handle and WCS point.");
            Button(control, "Record Decision").PerformClick();
            Assert(ResponseFiles(reviewRoot) == reviewsBefore && RecordFiles(resolutionService) == recordsBefore, "An incomplete review must not write anything.");

            Box(control, "ReviewerDisplayName").Text = "TEST-ONLY Reviewer";
            Box(control, "ReviewerRole").Text = "TEST-ONLY";
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(control, question.QuestionId);
                if (answer is ComboBox combo) combo.SelectedIndex = 1; else answer.Text = "test-only";
            }
            Combo(control, "Classification").SelectedIndex = 1;
            Assert(Button(control, "Record Decision").Enabled, "Record Decision must be enabled once a pick and a complete review are present.");
            Button(control, "Record Decision").PerformClick();
            Assert(ResponseFiles(reviewRoot) == reviewsBefore + 1 && RecordFiles(resolutionService) == recordsBefore + 1, "An explicit complete save must write exactly one review and one resolution.");
            Assert(control.UnresolvedCount == 0, "A saved resolution must leave the placement list.");
            control.Dispose();
        }

        // ---- helpers --------------------------------------------------------------------------

        private static EquipmentPlacementResolutionControl NewControl(
            ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService,
            IEquipmentPlacementPickProvider pickProvider, IReadOnlyList<PlacementProposal> proposals = null)
        {
            return new EquipmentPlacementResolutionControl(UnresolvedPlan(), DrawingName, DrawingSha, Units, reviewService, resolutionService, pickProvider, proposals, DrawingName);
        }

        private static PlotPlanModel UnresolvedPlan()
        {
            var plan = new PlotPlanModel();
            plan.EquipmentPlacements.Add(new EquipmentPlacement
            {
                PlacementId = "placement:equipment:P-97A:A",
                EquipmentEntityId = "equipment:P-97A",
                NormalizedTag = "P-97A",
                PlacementStatus = "Incomplete"
            });
            return plan;
        }

        // A structurally valid generated proposal bound to this drawing: the control only queues a
        // proposal whose review flag, drawing binding and recomputed fingerprint all agree.
        private static PlacementProposal BuildProposal()
        {
            var proposal = new PlacementProposal
            {
                ProposalId = "PP-" + new string('7', 48),
                PlacementId = "placement:equipment:P-97A:A",
                EquipmentEntityId = "equipment:P-97A",
                NormalizedTag = "P-97A",
                SourceDrawingSha256 = DrawingSha,
                InsertionUnits = Units,
                GeneratorIdentity = "TEST-ONLY-GENERATOR",
                OriginalEasting = 1000, OriginalNorthing = 2000,
                ProposedEasting = 1100, ProposedNorthing = 2100,
                EngineeringReviewRequired = true
            };
            proposal.ProposalFingerprint = PlacementProposalFingerprint.Compute(proposal);
            return proposal;
        }

        private static Panel TechnicalBody(Control root)
        {
            TableLayoutPanel grid = Descendants(root).OfType<TableLayoutPanel>()
                .First(t => t.Controls.OfType<Control>().Any(c => c.AccessibleName == "TechnicalValue"));
            return grid.Parent as Panel;
        }

        private static string FieldCaption(Control input)
        {
            Label label = FieldCaptionLabel(input);
            return label?.Text;
        }

        private static Label FieldCaptionLabel(Control input)
        {
            Control pair = input.Parent?.Parent;
            if (pair == null) return null;
            Panel captionHost = pair.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Top);
            return captionHost?.Controls.OfType<Label>().FirstOrDefault();
        }

        private static string Describe(Control control)
        {
            string name = string.IsNullOrEmpty(control.AccessibleName) ? control.GetType().Name : control.GetType().Name + "[" + control.AccessibleName + "]";
            return name + " back=" + control.BackColor + " fore=" + control.ForeColor;
        }

        private static int ResponseFiles(string reviewRoot) => Directory.GetFiles(reviewRoot, ProjectEngineerReviewService.ResponseFileName, SearchOption.AllDirectories).Length;
        private static int RecordFiles(EquipmentPlacementResolutionService service) => EquipmentPlacementResolutionFileStore.EnumerateRecordPaths(service.Root).Count;

        private static IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls)
            {
                yield return child;
                foreach (Control grandchild in Descendants(child)) yield return grandchild;
            }
        }

        private static Button Button(Control root, string text) => Descendants(root).OfType<Button>().First(b => b.Text == text);
        private static TextBox Box(Control root, string accessibleName) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == accessibleName);
        private static ComboBox Combo(Control root, string accessibleName) => Descendants(root).OfType<ComboBox>().First(c => c.AccessibleName == accessibleName);
        private static Control AnswerControl(Control root, string questionId) => Descendants(root).First(c => (c is ComboBox || c is TextBox) && (c.Tag as string) == questionId);

        private static void PerformLayout(Control control)
        {
            control.PerformLayout();
            foreach (Control child in Descendants(control))
                if (child.Controls.Count > 0) child.PerformLayout();
        }

        private static bool NoSiblingOverlaps(Control control) => FirstOverlap(control) == null;

        private static string FirstOverlap(Control control)
        {
            foreach (Control container in new[] { control }.Concat(Descendants(control)))
            {
                // an invisible control is not laid out by its container and is never rendered, so
                // only the controls the engineer can actually see take part in the overlap contract
                List<Control> siblings = container.Controls.Cast<Control>().Where(c => c.Visible && c.Width > 0 && c.Height > 0).ToList();
                for (int i = 0; i < siblings.Count; i++)
                    for (int j = i + 1; j < siblings.Count; j++)
                        if (siblings[i].Bounds.IntersectsWith(siblings[j].Bounds))
                            return Describe(siblings[i]) + " " + siblings[i].Bounds + " overlaps " + Describe(siblings[j]) + " " + siblings[j].Bounds + " inside " + Describe(container) + " " + container.Bounds;
            }
            return null;
        }

        private static void CollectionAssert(IList<string> actual, IList<string> expected, string message)
        {
            Assert(actual.Count == expected.Count && actual.SequenceEqual(expected, StringComparer.Ordinal), message + " Actual: [" + string.Join(", ", actual) + "]");
        }

        private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }

        private sealed class StubPickProvider : IEquipmentPlacementPickProvider
        {
            private readonly EquipmentPlacementPick pick;
            internal StubPickProvider(EquipmentPlacementPick pick) { this.pick = pick; }
            public EquipmentPlacementPick PickEntity() => pick;
            public EquipmentPlacementPick PickFreeSpace() => pick;
        }
    }
}
