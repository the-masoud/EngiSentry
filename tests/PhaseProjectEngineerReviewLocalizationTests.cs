#nullable disable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    // Focused localization / RTL assertions for the ES_ENGINEERREVIEW surface. English is the
    // default UI language and Persian (fa) is the selectable alternative. These assertions never
    // touch the production review root and never answer a real P10.3 case: an isolated test-only
    // root is used, and the only save exercised is a synthetic TEST_ONLY case (inherited coverage).
    internal static class PhaseProjectEngineerReviewLocalizationTests
    {
        private static int assertions;

        // every key the surface reads must resolve in BOTH cultures, or the fallback would leak the
        // raw key name into the UI
        private static readonly string[] AllKeys =
        {
            "AppTitle", "AppSubtitle", "AppSubtitleToolTip", "LanguageSwitchToolTip",
            "StatusNone", "StatusPendingBadge", "CaseOf", "ReviewedOf", "ReviewedToolTip",
            "NoCaseToDisplayToolTip", "StatusCompletedDetail", "StatusInvalidDetail", "StatusPendingDetail",
            "NoReviewableCases", "NoCaseSelected", "SectionIdentity", "ChipRequiredBeforeSaving",
            "FieldReviewerName", "FieldReviewerRole", "ReviewerNameToolTip", "ReviewerRoleToolTip",
            "IdentityNote", "SectionSummary", "KeyDrawing", "KeyCase", "KeyOperationalResult",
            "KeyReviewReason", "ValueNotRecorded", "SectionEvidence", "EvidenceCountFormat",
            "EvidenceReviewRequestId", "EvidenceRequestSha256", "EvidenceWorkflowSource",
            "EvidenceReviewedCase", "EvidenceDrawing", "EvidenceDrawingSha256",
            "EvidenceSoftwareResult", "EvidenceSoftwareState", "SectionQuestions", "RequiredNote",
            "RequiredSuffix", "OptionalSuffix", "SectionCriteriaUx", "SectionResultsUx",
            "SectionEngineeringInterpretation", "SectionUsability", "SectionDisposition",
            "FieldClassification", "ClassificationToolTip", "ChipRequired", "FieldComment",
            "CommentCountFormat", "ButtonPrevious", "ButtonNext", "ButtonNextPending", "ButtonSave",
            "PreviousToolTip", "NextToolTip", "NextPendingToolTip", "SaveToolTip", "MsgCompleted",
            "MsgInvalid", "MsgNoPending", "MsgNotSavedPrefix", "MsgSavedFor", "MsgSaveFailed",
            "RemainingAllComplete", "RemainingSingular", "RemainingPlural", "RemainingCompleteToolTip",
            "RemainingPendingToolTip"
        };

        internal static void Run()
        {
            Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The review-root override must stay disabled outside an explicit test scope.");
            Assert(ReviewLocalization.SupportedCultures.Count == 2, "Exactly two UI languages must be supported.");

            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PER-LOC-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            CultureInfo previous = ReviewStrings.Culture;
            try
            {
                var service = new ProjectEngineerReviewService(root);

                // 1. English is the default language and every key resolves to the accepted text
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en-US"));
                Assert(ReviewLocalization.IsPersian == false, "English must be the default UI language.");
                Assert(ReviewStrings.Get("ButtonSave") == "Save Review", "English default button text changed.");
                Assert(ReviewStrings.Get("AppTitle") == "Project Engineer Review", "English default title changed.");
                Assert(ReviewStrings.Format("CaseOf", 1, 4) == "Case 1 of 4", "English case counter format changed.");
                Assert(ReviewStrings.Format("ReviewedOf", 2, 4) == "Reviewed 2 of 4", "English progress format changed.");
                Assert(ReviewStrings.Format("RemainingPlural", 3) == "3 required answers remaining", "English remaining-answers format changed.");
                Assert(ReviewStrings.Format("CommentCountFormat", 5) == "5 / 1000", "English comment counter format changed.");
                Assert(ReviewStrings.Format("EvidenceCountFormat", 7) == "7 evidence items", "English evidence counter format changed.");

                // 2. Persian resolves to a distinct, non-empty Persian translation for every key
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa-IR"));
                Assert(ReviewLocalization.IsPersian, "fa must select the Persian UI language.");
                Assert(ReviewStrings.Get("ButtonSave") == "ذخیره بازبینی", "Persian save button text is wrong.");
                Assert(ReviewStrings.Get("AppTitle") == "بازبینی مهندس پروژه", "Persian title is wrong.");
                Assert(ReviewStrings.Format("CaseOf", 1, 4) == "مورد 1 از 4", "Persian case counter format is wrong.");
                foreach (string key in AllKeys)
                {
                    string value = ReviewStrings.Get(key);
                    Assert(!string.IsNullOrEmpty(value), "Persian resource is missing: " + key);
                    Assert(value != key, "Persian resource fell back to its key name: " + key);
                }
                // governed vocabulary tokens stay identical across cultures (IDs/hashes stay LTR)
                Assert(ReviewStrings.Get("StatusPendingBadge") == "● PENDING", "The pending status token must stay identical across languages.");
                Assert(ReviewStrings.Format("MsgNotSavedPrefix") == "بازبینی ذخیره نشد — ", "Persian validation prefix is wrong.");
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
                Assert(ReviewStrings.Get("StatusPendingBadge") == "● PENDING", "The pending status token must stay identical in English too.");

                // 3. Persian-capable system font is present and selected for the Persian surface
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa-IR"));
                Assert(ReviewLocalization.UiFontFamily == "Tahoma", "Persian must use the system-safe Persian-capable font Tahoma.");
                Assert(ReviewLocalization.UiFontFamilySemibold == "Tahoma", "Persian semibold slots must also use Tahoma.");
                Assert(FontFamily.Families.Any(f => f.Name == "Tahoma"), "Tahoma must be installed on the system for Persian rendering.");
                Assert(ContainsPersian(ReviewStrings.Get("SectionQuestions")), "The Persian questions section title must actually be Persian.");

                // 4. every Persian string renders real glyphs with the selected font (not blank/tofu)
                using (Font persianFont = new Font(ReviewLocalization.UiFontFamily, 9F))
                {
                    foreach (string key in AllKeys)
                    {
                        string value = ReviewStrings.Get(key);
                        if (!ContainsPersian(value)) continue; // tokens like "● PENDING" are LTR by design
                        Size size = TextRenderer.MeasureText(value, persianFont);
                        Assert(size.Width > 0 && size.Height > 0, "Persian text must measure into a real extent: " + key);
                        Assert(RendersGlyphs(value, persianFont), "Persian text must render visible glyphs with Tahoma: " + key);
                    }
                }

                // 5. the Persian surface keeps the full review contract, mirrored RTL
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa-IR"));
                var persianControl = new EngiSentry.AutoCAD2021.ProjectEngineerReviewControl(service);
                Assert(persianControl.QueueCount == 4, "The Persian surface must show the same four pilot cases.");
                Assert(service.LoadQueue().All(x => service.ResolveStatus(x) == ProjectEngineerReviewStatuses.Pending), "Opening the Persian UI must not complete any review.");
                Assert(ResponseFiles(root).Count == 0, "Constructing the Persian UI must not write any response.");
                Assert(persianControl.RightToLeft == RightToLeft.Yes, "The Persian surface must be laid out right-to-left.");
                Assert(Descendants(persianControl).All(c => c.RightToLeft == RightToLeft.Yes), "Every control on the Persian surface must inherit the RTL direction.");
                Assert(Descendants(persianControl).OfType<Button>().Select(b => b.Text).ToList().SequenceEqual(
                    new[] { ReviewStrings.Get("ButtonPrevious"), ReviewStrings.Get("ButtonNext"), ReviewStrings.Get("ButtonNextPending"), ReviewStrings.Get("ButtonSave") }),
                    "The Persian action bar must show translated labels.");
                Assert(Descendants(persianControl).OfType<Button>().Count() == 4, "The Persian action bar must keep exactly four buttons.");
                Assert(Descendants(persianControl).OfType<ComboBox>().Count() == 13, "The Persian surface must keep exactly the 12 answer combos plus the classification combo.");
                List<Control> answers = ProjectEngineerReviewQuestions.All.Select(q => AnswerControl(persianControl, q.QuestionId)).ToList();
                Assert(answers.Count == 14 && answers.Distinct().Count() == 14, "Every one of the 14 questions must keep its own answer control in Persian mode.");
                foreach (ProjectEngineerReviewQuestionDefinition question in ProjectEngineerReviewQuestions.All)
                {
                    Control answer = AnswerControl(persianControl, question.QuestionId);
                    if (answer is ComboBox combo)
                    {
                        List<string> items = combo.Items.Cast<string>().ToList();
                        List<string> expected = new List<string> { string.Empty }.Concat(ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)).ToList();
                        Assert(items.SequenceEqual(expected, StringComparer.Ordinal), "Answer vocabulary must be unchanged in Persian mode for " + question.QuestionId + ".");
                    }
                }
                // governed evidence (hashes/IDs) is unchanged and still presented as readable content
                Label hashLabel = HashLabels(persianControl).First();
                Assert(hashLabel.Text == service.LoadQueue()[0].RequestSha256, "The request hash must stay part of the Persian evidence panel.");
                Assert(hashLabel.Font.FontFamily.Name == "Consolas", "Hashes must stay in the LTR monospace face in Persian mode.");
                Assert(TitleText(persianControl) == ReviewStrings.Get("AppTitle"), "The Persian header must show the translated title.");
                Assert(SectionTitles(persianControl).Contains(ReviewStrings.Get("SectionIdentity")), "The Persian surface must show translated section titles.");
                Assert(SectionTitles(persianControl).Contains(ReviewStrings.Get("SectionQuestions")), "The Persian surface must show the translated questions section title.");
                // Persian question section titles are presented in Persian while the section identifiers stay governed
                Assert(SectionTitles(persianControl).Contains(ReviewStrings.Get("SectionCriteriaUx")), "The Criteria UX section title must be presented in Persian.");

                // 6. the language link is present, names the switch target, and toggles back to English
                LinkLabel link = Descendants(persianControl).OfType<LinkLabel>().First(l => l.AccessibleName == "UiLanguage");
                Assert(link.Text == "English", "While Persian is active the link must offer English.");
                persianControl.Rebuild();
                Assert(link.Text == "English", "The rebuilt surface must still offer English while Persian is active.");
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
                persianControl.Rebuild();
                Assert(persianControl.RightToLeft == RightToLeft.No, "Switching back to English must restore left-to-right layout.");
                Assert(Descendants(persianControl).All(c => c.RightToLeft == RightToLeft.No), "Every control must restore LTR after switching back to English.");
                Assert(Descendants(persianControl).OfType<Button>().Select(b => b.Text).ToList().SequenceEqual(new[] { "Previous", "Next", "Next Pending", "Save Review" }), "Switching back to English must restore the English labels.");
                LinkLabel englishLink = Descendants(persianControl).OfType<LinkLabel>().First(l => l.AccessibleName == "UiLanguage");
                Assert(englishLink.Text == "فارسی", "While English is active the link must offer Persian.");

                // 6b. the Persian surface actually renders: the custom RTL painters (badge, severity
                // message, progress rail) only run on a live handle, so force a real render pass
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa-IR"));
                var renderControl = new EngiSentry.AutoCAD2021.ProjectEngineerReviewControl(service);
                renderControl.Size = new Size(1000, 960);
                using (Bitmap shot = new Bitmap(1000, 960))
                {
                    renderControl.DrawToBitmap(shot, new Rectangle(0, 0, 1000, 960));
                    Assert(HasContent(shot), "The Persian RTL surface must render visible content.");
                    Assert(DistinctContentColumns(shot) > 8, "The Persian RTL surface must render text across the surface, not a single blob.");
                }
                renderControl.Dispose();
                ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));

                // 7. a language switch preserves unsaid entries and writes nothing
                ReviewerNameBox(persianControl).Text = "Test Reviewer";
                ReviewerRoleBox(persianControl).Text = "Test Role";
                Control firstAnswer = AnswerControl(persianControl, ProjectEngineerReviewQuestions.All[0].QuestionId);
                if (firstAnswer is ComboBox firstCombo) firstCombo.SelectedIndex = 1; else firstAnswer.Text = "test-only free text";
                ReviewLocalization.Toggle();
                persianControl.Rebuild();
                Assert(ReviewerNameBox(persianControl).Text == "Test Reviewer", "A language switch must not discard entered reviewer identity.");
                Assert(ReviewerRoleBox(persianControl).Text == "Test Role", "A language switch must not discard the entered role.");
                Assert(ResponseFiles(root).Count == 0, "A language switch must never write a review response.");
                Assert(ReviewLocalization.IsPersian, "Toggle must move from English to Persian.");
                ReviewLocalization.Toggle();
                Assert(ReviewLocalization.IsPersian == false, "Toggle must move back from Persian to English.");
                persianControl.Dispose();

                Console.WriteLine("Phase Project Engineer Review localization assertions passed: " + assertions);
            }
            finally
            {
                ReviewStrings.Culture = previous ?? CultureInfo.GetCultureInfo("en");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static bool ContainsPersian(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.Any(c => c >= '\u0600' && c <= '\u06FF');
        }

        // a rendered control must show real foreground pixels
        private static bool HasContent(Bitmap bmp)
        {
            int content = 0;
            for (int y = 0; y < bmp.Height; y += 7)
                for (int x = 0; x < bmp.Width; x += 7)
                    if (bmp.GetPixel(x, y).R > 50 || bmp.GetPixel(x, y).G > 80) content++;
            return content > 40;
        }

        // counts horizontally separated content bands so a mirror-flipped blank surface cannot pass
        private static int DistinctContentColumns(Bitmap bmp)
        {
            var columns = new HashSet<int>();
            for (int x = 0; x < bmp.Width; x += 5)
                for (int y = 0; y < bmp.Height; y += 5)
                    if (bmp.GetPixel(x, y).R > 50 || bmp.GetPixel(x, y).G > 80) { columns.Add(x / 25); break; }
            return columns.Count;
        }

        // renders the string into a bitmap and confirms it produces real foreground pixels
        private static bool RendersGlyphs(string value, Font font)
        {
            Size size = TextRenderer.MeasureText(value, font);
            if (size.Width <= 0 || size.Height <= 0) return false;
            using (Bitmap bmp = new Bitmap(size.Width + 4, size.Height + 4))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Black);
                TextRenderer.DrawText(g, value, font, new Point(2, 2), Color.White,
                    TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if (bmp.GetPixel(x, y).R > 40) return true;
            }
            return false;
        }

        private static List<string> ResponseFiles(string root) => Directory.GetFiles(root, "review-response.json", SearchOption.AllDirectories).ToList();

        private static IEnumerable<Control> Descendants(Control control)
        {
            foreach (Control child in control.Controls)
            {
                yield return child;
                foreach (Control grandchild in Descendants(child)) yield return grandchild;
            }
        }

        private static Control AnswerControl(Control root, string questionId) => Descendants(root).First(c => (c is ComboBox || c is TextBox) && (c.Tag as string) == questionId);

        private static TextBox ReviewerNameBox(Control root) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == "ReviewerDisplayName");
        private static TextBox ReviewerRoleBox(Control root) => Descendants(root).OfType<TextBox>().First(b => b.AccessibleName == "ReviewerRole");

        private static string TitleText(Control root) => Descendants(root).OfType<Label>().First(l => l.Text == ReviewStrings.Get("AppTitle") || l.Text == "Project Engineer Review").Text;

        private static List<string> SectionTitles(Control root) => Descendants(root).OfType<Label>().Select(l => l.Text).ToList();

        private static List<Label> HashLabels(Control root) => Descendants(root).OfType<Label>().Where(l => l.Text.Length == 64 && l.Text.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))).ToList();

        private static void Assert(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
    }
}
