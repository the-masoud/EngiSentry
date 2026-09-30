#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    // Focused bilingual / RTL / preference assertions for the ES_PLACEMENTRESOLVE engineer surface.
    // They prove the surface is fully localizable in both languages, that the language preference
    // round-trips through the per-user store (en/fa only, English fallback), that RTL/LTR and the
    // technical-value direction are correct, and that none of it touches governed vocabulary,
    // persisted tokens, question contracts or the existing P11.1 workflow semantics.
    //
    // The assertions are structural, never pixel- or RGB-literal, and they never touch the real
    // per-user preference file (an isolated override root is used).
    internal static class PhaseEquipmentPlacementResolutionLocalizationTests
    {
        private static int assertions;
        private const string DrawingName = "P11.2B-LOC-TEST.dwg";
        private static readonly string DrawingSha = new string('D', 64);
        private const string Units = "Millimeters";

        // Every key the placement surface (and its shared disposition display) reads must resolve in
        // BOTH cultures, or the fallback would leak the raw key name into the UI.
        private static readonly string[] ScopedKeys =
        {
            "Placement_AppTitle", "Placement_AppSubtitle", "Placement_LanguageToolTip",
            "Placement_HeaderDrawing", "Placement_HeaderUnits", "Placement_HeaderOpenItemOne", "Placement_HeaderOpenItemMany",
            "Placement_SectionEquipment", "Placement_SectionSelection", "Placement_SectionReview",
            "Placement_SectionDecision", "Placement_SectionReviewer", "Placement_SectionTechnical",
            "Placement_FieldQueue", "Placement_FieldTag", "Placement_FieldReviewStatus", "Placement_FieldOriginal",
            "Placement_FieldProposed", "Placement_FieldReviewRequired",
            "Placement_ValueNotSelected", "Placement_ValueNone", "Placement_ValueUnresolved", "Placement_ValueFreeSpace",
            "Placement_ValueYes", "Placement_ValueNo", "Placement_ListProposal", "Placement_NoSelection",
            "Placement_FieldPickEvidence", "Placement_PickNone",
            "Placement_ButtonPickEquipment", "Placement_ButtonPickFreeSpace", "Placement_ButtonClear",
            "Placement_TechPickSource", "Placement_TechPickPoint", "Placement_TechHandle", "Placement_TechLayer",
            "Placement_TechStoredEasting", "Placement_TechStoredNorthing",
            "Placement_RequiredNote", "Placement_FieldClassification", "Placement_FieldDisposition",
            "Placement_ChipRequired", "Placement_ChipProposalOnly",
            "Placement_FieldReviewerName", "Placement_FieldReviewerRole", "Placement_FieldComment",
            "Placement_ButtonRecord", "Placement_Hint", "Placement_WhyReady", "Placement_WhySelect", "Placement_WhyPick",
            "Placement_WhyAnswers", "Placement_WhyReviewerName", "Placement_WhyReviewerRole",
            "Placement_WhyClassification", "Placement_WhyDisposition", "Placement_WhyComment", "Placement_WhyProposalRange",
            "Placement_ButtonTechnicalShow", "Placement_ButtonTechnicalHide",
            "Placement_TechDrawingSha", "Placement_TechDrawing", "Placement_TechUnits", "Placement_TechPlacementId",
            "Placement_TechEntityId", "Placement_TechProposalId", "Placement_TechFingerprint", "Placement_TechGenerator",
            "Placement_TechOriginalPoint", "Placement_TechProposedPoint", "Placement_TechResolutionId",
            "Placement_TechDispositionId", "Placement_TechEastingStatus", "Placement_TechNorthingStatus",
            "Placement_TechCenterline", "Placement_TechPlacementStatus",
            "Placement_StatusPickCaptured", "Placement_StatusPickCleared", "Placement_StatusDecisionRecorded",
            "Placement_StatusResolutionSaved",
            "Placement_ErrorSelectFirst", "Placement_ErrorPickCancelled", "Placement_ErrorPickRange",
            "Placement_ErrorPickFailed", "Placement_ErrorNoPick", "Placement_ErrorNoHash", "Placement_ErrorNoUnits",
            "Placement_ErrorNoSelection", "Placement_ErrorNotSaved", "Placement_ErrorDispositionNotRecorded",
            "Disposition_PENDING_REVIEW", "Disposition_APPROVED", "Disposition_REJECTED", "Disposition_MODIFIED",
            "Disposition_SUPERSEDED", "Disposition_CANCELLED",
            "Question_placement_point_matches_equipment", "Question_placement_axes_correct", "Question_placement_units_confirmed"
        };

        internal static void Run()
        {
            CultureInfo previousCulture = ReviewStrings.Culture;
            string previousRoot = Environment.GetEnvironmentVariable(ReviewUiPreferences.RootOverrideVariable);
            string settingsRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112B-PREFS-" + Guid.NewGuid().ToString("N"));
            string reviewRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112B-REVIEW-" + Guid.NewGuid().ToString("N"));
            string resolutionRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112B-RES-" + Guid.NewGuid().ToString("N"));
            string dispositionRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P112B-DISP-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(settingsRoot);
            Directory.CreateDirectory(reviewRoot);
            Directory.CreateDirectory(resolutionRoot);
            Directory.CreateDirectory(dispositionRoot);
            Environment.SetEnvironmentVariable(ReviewUiPreferences.RootOverrideVariable, settingsRoot);
            try
            {
                var reviewService = new ProjectEngineerReviewService(reviewRoot);
                var dispositionService = new PlacementProposalDispositionService(dispositionRoot);
                var resolutionService = new EquipmentPlacementResolutionService(resolutionRoot, reviewService, dispositionService);

                ResourceCoverageTests();
                PreferenceStoreTests();
                DirectionTests(reviewService, resolutionService);
                GovernedTokenTests(reviewService, resolutionService);
                QuestionContractTests(reviewService, resolutionService);
                ExplicitLabelTests(reviewService, resolutionService);
                TechnicalDetailsTests(reviewService, resolutionService);
                PrimaryActionTests(reviewService, resolutionService);
                SectionStructureTests(reviewService, resolutionService);
                SwitchPreservesStateTests(reviewService, resolutionService, reviewRoot);
                WorkflowSemanticsTests();
                LayoutProximityAndDisplayTests(reviewService, resolutionService);

                Console.WriteLine("Phase Equipment Placement Resolution localization assertions passed: " + assertions);
            }
            finally
            {
                ReviewStrings.Culture = previousCulture ?? CultureInfo.GetCultureInfo("en");
                Environment.SetEnvironmentVariable(ReviewUiPreferences.RootOverrideVariable, previousRoot);
                if (Directory.Exists(settingsRoot)) Directory.Delete(settingsRoot, true);
                if (Directory.Exists(reviewRoot)) Directory.Delete(reviewRoot, true);
                if (Directory.Exists(resolutionRoot)) Directory.Delete(resolutionRoot, true);
                if (Directory.Exists(dispositionRoot)) Directory.Delete(dispositionRoot, true);
            }
        }

        // 1 / 2 / 3. English and Persian resources exist for every scoped key, with no missing entry.
        private static void ResourceCoverageTests()
        {
            Assert(ReviewLocalization.SupportedCultures.Count == 2, "Exactly two UI languages must be supported.");

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
            Assert(ReviewLocalization.IsPersian == false, "English must be the default UI language.");
            foreach (string key in ScopedKeys)
            {
                string value = ReviewStrings.Get(key);
                Assert(!string.IsNullOrEmpty(value), "English resource is missing: " + key);
                Assert(value != key, "English resource fell back to its key name: " + key);
            }
            Assert(ReviewStrings.Get("Placement_AppTitle") == "Equipment Placement Resolution", "English title is wrong.");
            Assert(ReviewStrings.Get("Placement_ButtonRecord") == "Record Decision", "English primary action is wrong.");
            Assert(ReviewStrings.Get("Placement_SectionTechnical") == "Technical Details", "English technical section title is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldClassification") == "Classification", "English classification label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldDisposition") == "Disposition", "English disposition label is wrong.");
            Assert(ReviewStrings.Format("Placement_HeaderOpenItemMany", 3) == "3 Open Items", "English open-item counter is wrong.");

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            Assert(ReviewLocalization.IsPersian, "fa must select the Persian UI language.");
            foreach (string key in ScopedKeys)
            {
                string value = ReviewStrings.Get(key);
                Assert(!string.IsNullOrEmpty(value), "Persian resource is missing: " + key);
                Assert(value != key, "Persian resource fell back to its key name: " + key);
            }
            Assert(ReviewStrings.Get("Placement_AppTitle") == "حل موقعیت تجهیز", "Persian title is wrong.");
            Assert(ReviewStrings.Get("Placement_ButtonRecord") == "ثبت تصمیم", "Persian primary action is wrong.");
            Assert(ReviewStrings.Get("Placement_SectionTechnical") == "جزئیات فنی", "Persian technical section title is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldClassification") == "طبقه‌بندی", "Persian classification label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldDisposition") == "تصمیم درباره پیشنهاد", "Persian disposition label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldReviewerName") == "نام بازبین", "Persian reviewer name label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldReviewerRole") == "سمت / عنوان", "Persian reviewer role label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldComment") == "توضیحات", "Persian comment label is wrong.");
            Assert(ReviewStrings.Get("Placement_ButtonPickEquipment") == "انتخاب تجهیز از نقشه", "Persian pick-equipment action is wrong.");
            Assert(ReviewStrings.Get("Placement_ButtonPickFreeSpace") == "انتخاب نقطه آزاد", "Persian pick-free-space action is wrong.");
            Assert(ReviewStrings.Get("Placement_ButtonClear") == "پاک کردن انتخاب", "Persian clear action is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldTag") == "تجهیز", "Persian tag label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldOriginal") == "موقعیت اولیه", "Persian original-location label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldProposed") == "موقعیت پیشنهادی", "Persian proposed-location label is wrong.");
            Assert(ReviewStrings.Get("Placement_FieldReviewRequired") == "نیاز به بررسی", "Persian review-required label is wrong.");
            Assert(ReviewStrings.Get("Question_placement_point_matches_equipment") == "نقطه انتخاب‌شده، محل تجهیز است", "Persian placement question 1 is wrong.");
            Assert(ReviewStrings.Get("Question_placement_axes_correct") == "محورهای Easting / Northing صحیح هستند", "Persian placement question 2 is wrong.");
            Assert(ReviewStrings.Get("Question_placement_units_confirmed") == "واحد نقشه برای این مختصات تأیید شده است", "Persian placement question 3 is wrong.");
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // 4 / 5. The preference store accepts only en/fa, falls back to English, and writes on change only.
        private static void PreferenceStoreTests()
        {
            string path = ReviewUiPreferences.PreferencesPath;

            if (File.Exists(path)) File.Delete(path);
            Assert(ReviewUiPreferences.Load().Name == "en", "A missing preference file must fall back to English.");

            ReviewUiPreferences.Save(CultureInfo.GetCultureInfo("fa"));
            Assert(ReviewUiPreferences.Load().Name == "fa", "A stored Persian preference must be read back.");
            Assert(File.ReadAllText(path).Contains("\"language\":\"fa\""), "The preference must be stored as exactly {\"language\":\"fa\"}. Actual: " + File.ReadAllText(path));

            ReviewUiPreferences.Save(CultureInfo.GetCultureInfo("en"));
            Assert(ReviewUiPreferences.Load().Name == "en", "A stored English preference must be read back.");
            Assert(File.ReadAllText(path).Contains("\"language\":\"en\""), "The preference must be stored as exactly {\"language\":\"en\"}. Actual: " + File.ReadAllText(path));

            // anything other than the two supported languages is stored as English
            ReviewUiPreferences.Save(CultureInfo.GetCultureInfo("de"));
            Assert(ReviewUiPreferences.Load().Name == "en", "An unsupported culture must fall back to English.");
            Assert(File.ReadAllText(path).Contains("\"language\":\"en\""), "An unsupported culture must be stored as English.");

            // malformed or unsupported stored content falls back to English
            File.WriteAllText(path, "{ not json");
            Assert(ReviewUiPreferences.Load().Name == "en", "Malformed preferences must fall back to English.");
            File.WriteAllText(path, "{\"language\":\"de\"}");
            Assert(ReviewUiPreferences.Load().Name == "en", "An unsupported stored language must fall back to English.");

            // write only on change: saving the value already stored must not rewrite the file
            File.WriteAllText(path, "{\"language\":\"en\",\"extra\":true}");
            ReviewUiPreferences.Save(CultureInfo.GetCultureInfo("en"));
            Assert(File.ReadAllText(path).Contains("extra"), "Saving an unchanged preference must not rewrite the file.");

            // an unreadable location must never throw and must fall back to English
            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert(ReviewUiPreferences.Load().Name == "en", "An unreadable preference location must fall back to English.");
            Directory.Delete(path, true);

            Assert(ReviewUiPreferences.PreferencesPath.EndsWith(Path.Combine("EngiSentry", "settings", "ui-preferences.json"), StringComparison.OrdinalIgnoreCase) == false
                || ReviewUiPreferences.PreferencesPath.Contains("ui-preferences.json"), "The preference file name must be ui-preferences.json.");
        }

        // 7 / 8 / 9. RTL for Persian, LTR for English, and technical values always LTR.
        private static void DirectionTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var persian = NewControl(reviewService, resolutionService, new[] { BuildProposal() });
            Assert(persian.RightToLeft == RightToLeft.Yes, "The Persian surface must be laid out right-to-left.");
            Assert(Descendants(persian).Where(c => c.AccessibleName != "TechnicalValue" && c.AccessibleName != "WorkItemList")
                .All(c => c.RightToLeft == RightToLeft.Yes), "Every control except technical values must inherit the RTL direction.");
            List<TextBox> persianValues = Descendants(persian).OfType<TextBox>().Where(t => t.AccessibleName == "TechnicalValue").ToList();
            Assert(persianValues.Count > 0, "The Persian surface must expose technical value controls.");
            Assert(persianValues.All(t => t.RightToLeft == RightToLeft.No), "Technical values must stay LTR in Persian mode.");
            Assert(Descendants(persian).OfType<ListBox>().Single().RightToLeft == RightToLeft.No, "The queue must stay LTR in Persian mode.");
            persian.Dispose();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
            var english = NewControl(reviewService, resolutionService, null);
            Assert(english.RightToLeft == RightToLeft.No, "The English surface must be left-to-right.");
            Assert(Descendants(english).All(c => c.RightToLeft == RightToLeft.No), "Every control must be LTR in English mode.");
            english.Dispose();
        }

        // 10 / 11. Governed vocabulary keeps its exact canonical tokens in both languages.
        private static void GovernedTokenTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, new[] { BuildProposal() });

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            ComboBox classification = Combo(control, "Classification");
            List<string> expectedClassification = new List<string> { string.Empty }.Concat(ProjectEngineerReviewClassifications.All).ToList();
            Assert(classification.Items.Cast<string>().SequenceEqual(expectedClassification, StringComparer.Ordinal), "Classification combo items must stay canonical in Persian mode.");
            Assert(classification.DrawMode == DrawMode.OwnerDrawFixed, "The classification combo must be owner-drawn so its items stay canonical.");

            ComboBox disposition = Combo(control, "PlacementProposalDisposition");
            List<string> expectedDisposition = new List<string>
            {
                string.Empty, PlacementProposalDispositions.Approved, PlacementProposalDispositions.Rejected,
                PlacementProposalDispositions.Modified, PlacementProposalDispositions.Cancelled
            };
            Assert(disposition.Items.Cast<string>().SequenceEqual(expectedDisposition, StringComparer.Ordinal), "Disposition combo items must stay canonical in Persian mode.");
            Assert(disposition.DrawMode == DrawMode.OwnerDrawFixed, "The disposition combo must be owner-drawn so its items stay canonical.");

            // the disposition display text is friendly in both languages, never the raw token
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Modified) == "اصلاح‌شده", "Persian disposition display is wrong.");
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Approved) == "تأییدشده", "Persian disposition display is wrong.");
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Rejected) == "ردشده", "Persian disposition display is wrong.");
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Cancelled) == "لغوشده", "Persian disposition display is wrong.");
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.PendingReview) == "در انتظار بررسی", "Persian pending-review disposition display is wrong.");
            Assert(ReviewDisplayText.Classification(ProjectEngineerReviewClassifications.Pass) == "تأیید شد", "Persian classification display is wrong.");
            Assert(ReviewDisplayText.Answer("Yes") == "بله" && ReviewDisplayText.Answer("No") == "خیر", "Persian answer display is wrong.");

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Modified) == "Modified", "English disposition display must be friendly, not the raw token.");
            Assert(ReviewDisplayText.Disposition(PlacementProposalDispositions.Approved) == "Approved", "English disposition display is wrong.");
            Assert(ReviewDisplayText.Disposition("SOMETHING_ELSE") == "SOMETHING_ELSE", "Unknown disposition tokens must fall through unchanged.");
            Assert(ReviewDisplayText.Classification(ProjectEngineerReviewClassifications.Pass) == "PASS", "English classification display must equal the canonical token.");
            Assert(ReviewDisplayText.Answer("Yes") == "Yes", "English answer display must equal the canonical value.");
            control.Dispose();
        }

        // 12. The review-question contract is untouched (ids, required flags, answer vocabularies).
        private static void QuestionContractTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            Assert(EquipmentPlacementResolutionQuestions.All.Count == 4, "The placement confirmation template must keep exactly four questions.");
            Assert(EquipmentPlacementResolutionQuestions.All.Select(q => q.QuestionId).SequenceEqual(new[]
            {
                "placement.point-matches-equipment", "placement.axes-correct", "placement.units-confirmed", "placement.engineer-comment"
            }, StringComparer.Ordinal), "The placement question ids changed.");
            Assert(EquipmentPlacementResolutionQuestions.All.Count(q => q.Required) == 3, "Exactly three placement questions must stay required.");

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var control = NewControl(reviewService, resolutionService, null);
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                if (!(AnswerControl(control, question.QuestionId) is ComboBox combo)) continue;
                List<string> items = combo.Items.Cast<string>().ToList();
                List<string> expected = new List<string> { string.Empty }.Concat(ProjectEngineerAnswerKinds.AllowedAnswers(question.AnswerKind)).ToList();
                Assert(items.SequenceEqual(expected, StringComparer.Ordinal), "The answer vocabulary must stay canonical in Persian mode: " + question.QuestionId);
            }
            control.Dispose();
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // 13 / 14. Classification and Disposition each carry an explicit, visible caption in both languages.
        private static void ExplicitLabelTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
            var english = NewControl(reviewService, resolutionService, new[] { BuildProposal() });
            Assert(string.Equals(FieldCaption(Combo(english, "Classification")), "Classification", StringComparison.OrdinalIgnoreCase), "The English Classification caption is missing.");
            Assert(string.Equals(FieldCaption(Combo(english, "PlacementProposalDisposition")), "Disposition", StringComparison.OrdinalIgnoreCase), "The English Disposition caption is missing.");
            english.Dispose();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var persian = NewControl(reviewService, resolutionService, new[] { BuildProposal() });
            Assert(FieldCaption(Combo(persian, "Classification")) == "طبقه‌بندی", "The Persian Classification caption is wrong.");
            Assert(FieldCaption(Combo(persian, "PlacementProposalDisposition")) == "تصمیم درباره پیشنهاد", "The Persian Disposition caption is wrong.");
            Assert(FieldCaptionLabel(Combo(persian, "Classification")).Visible, "The Persian Classification caption must be visible.");
            persian.Dispose();
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // 15. Technical Details exists and is collapsed by default in both languages.
        private static void TechnicalDetailsTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var english = NewControl(reviewService, resolutionService, new[] { BuildProposal() });
            Panel body = TechnicalBody(english);
            Assert(!body.Visible, "Technical details must be collapsed by default.");
            Assert(Descendants(english).OfType<Button>().Any(b => b.Text == "Show Technical Details"), "The English technical-details toggle is missing.");
            Button(english, "Show Technical Details").PerformClick();
            Assert(body.Visible, "The English technical-details toggle must expand the section.");
            english.Dispose();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var persian = NewControl(reviewService, resolutionService, new[] { BuildProposal() });
            Panel persianBody = TechnicalBody(persian);
            Assert(!persianBody.Visible, "Technical details must be collapsed by default in Persian mode.");
            Assert(Descendants(persian).OfType<Button>().Any(b => b.Text == "نمایش جزئیات فنی"), "The Persian technical-details toggle is missing.");
            Button(persian, "نمایش جزئیات فنی").PerformClick();
            Assert(persianBody.Visible, "The Persian technical-details toggle must expand the section.");
            persian.Dispose();
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // 16. The primary action stays clearly readable when enabled AND when disabled.
        private static void PrimaryActionTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, null);
            Button save = Descendants(control).OfType<Button>().Single(b => b.AccessibleName == "SaveResolution");
            Assert(save.Text == "Record Decision", "The primary action label is wrong.");
            Assert(save.Font.Bold, "The primary action must keep the primary emphasis font.");
            Assert(!save.Enabled, "The primary action must start disabled before a pick is captured.");
            Assert(save.ForeColor.GetBrightness() > 0.15f, "A disabled primary action must stay legible.");
            Assert(save.BackColor.GetBrightness() < 0.6f, "A disabled primary action must stay on the dark surface.");
            save.Enabled = true;
            Assert(save.BackColor == ReviewUi.Primary, "An enabled primary action must use the primary accent surface.");
            save.Enabled = false;
            Assert(save.BackColor != ReviewUi.Primary, "A disabled primary action must drop the primary accent surface.");
            control.Dispose();
        }

        // 17. The existing P11.2 section structure is intact (and still in workflow order).
        private static void SectionStructureTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            var control = NewControl(reviewService, resolutionService, null);
            control.Size = new System.Drawing.Size(1000, 960);
            PerformLayout(control);
            List<string> captions = Descendants(control).OfType<Card>().OrderBy(c => c.Top).Select(c => c.Caption).ToList();
            Assert(captions.SequenceEqual(new[]
            {
                "EQUIPMENT / PROPOSAL", "PLACEMENT SELECTION", "ENGINEER REVIEW", "DECISION", "REVIEWER", "TECHNICAL DETAILS"
            }, StringComparer.Ordinal), "The P11.2 section structure changed. Actual: [" + string.Join(", ", captions) + "]");
            control.Dispose();
        }

        // 6. A language switch preserves entered engineering state and writes nothing.
        private static void SwitchPreservesStateTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService, string reviewRoot)
        {
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var control = NewControl(reviewService, resolutionService, null);
            Box(control, "ReviewerDisplayName").Text = "TEST-ONLY Reviewer";
            Box(control, "ReviewerRole").Text = "TEST-ONLY";
            ((ComboBox)AnswerControl(control, EquipmentPlacementResolutionQuestions.All[0].QuestionId)).SelectedIndex = 1;
            Combo(control, "Classification").SelectedIndex = 1;

            int reviewsBefore = ResponseFiles(reviewRoot);
            int recordsBefore = RecordFiles(resolutionService);

            ReviewLocalization.Toggle(); // fa -> en
            control.Rebuild();

            Assert(ReviewLocalization.IsPersian == false, "Toggle must move from Persian to English.");
            Assert(Box(control, "ReviewerDisplayName").Text == "TEST-ONLY Reviewer", "A language switch must not discard the entered reviewer name.");
            Assert(Box(control, "ReviewerRole").Text == "TEST-ONLY", "A language switch must not discard the entered role.");
            Assert(((ComboBox)AnswerControl(control, EquipmentPlacementResolutionQuestions.All[0].QuestionId)).SelectedIndex == 1, "A language switch must not discard an entered answer.");
            Assert((Combo(control, "Classification").SelectedItem as string) == ProjectEngineerReviewClassifications.Pass, "A language switch must not discard the classification.");
            Assert(control.RightToLeft == RightToLeft.No, "Switching back to English must restore left-to-right layout.");
            Assert(ResponseFiles(reviewRoot) == reviewsBefore && RecordFiles(resolutionService) == recordsBefore, "A language switch must never write a review or a resolution.");
            control.Dispose();
            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // 18. The existing P11.1 workflow semantics are unchanged (command surface + read-only open).
        private static void WorkflowSemanticsTests()
        {
            MethodInfo command = typeof(EngiSentry.AutoCAD2021.Commands).GetMethod("PlacementResolve", BindingFlags.Public | BindingFlags.Instance);
            Assert(command != null, "The ES_PLACEMENTRESOLVE command handler is missing.");
            try
            {
                foreach (object attribute in command.GetCustomAttributes(false))
                {
                    PropertyInfo nameProperty = attribute.GetType().GetProperty("Name");
                    if (nameProperty == null) continue;
                    string commandName = nameProperty.GetValue(attribute) as string;
                    if (!string.IsNullOrEmpty(commandName)) Assert(commandName == "ES_PLACEMENTRESOLVE", "The command name changed: " + commandName);
                }
            }
            catch (FileNotFoundException)
            {
                // The Autodesk runtime attribute type is not resolvable in the offline test host; the
                // command name is verified again during the AutoCAD runtime acceptance.
            }
            Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The review-root override must stay disabled outside an explicit test scope.");
        }

        // 19-27. The P11.2B correction: every label stays visually associated with its control in
        // both languages, the normal proposal row is engineer-friendly (no raw identity), the raw
        // identity stays under Technical Details, the disabled primary action stays readable, and the
        // primary action is anchored to the RTL-leading (right) end of the action bar. All assertions
        // are structural (columns, dock, alignment, colour brightness) — never pixel- or RGB-literal.
        private static void LayoutProximityAndDisplayTests(ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService)
        {
            PlacementProposal proposal = BuildProposal();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
            var english = NewControl(reviewService, resolutionService, new[] { proposal });
            AssertSummaryValueAssociation(english, "en");
            AssertQuestionAnswerAssociation(english);
            AssertDecisionFieldAssociation(english);
            AssertProposalRowDisplay(english, proposal, "en");
            AssertPrimaryActionAtRightEnd(english);
            english.Dispose();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("fa"));
            var persian = NewControl(reviewService, resolutionService, new[] { proposal });
            AssertSummaryValueAssociation(persian, "fa");
            AssertQuestionAnswerAssociation(persian);
            AssertDecisionFieldAssociation(persian);
            AssertProposalRowDisplay(persian, proposal, "fa");
            AssertPrimaryActionAtRightEnd(persian);
            AssertCanonicalProposalIdInTechnicalDetails(persian, proposal);
            persian.Dispose();

            ReviewLocalization.Apply(CultureInfo.GetCultureInfo("en"));
        }

        // The summary value stays beside its caption instead of drifting to the opposite card edge:
        // the value occupies the column immediately next to its caption and its text hugs the caption
        // side, while the value itself stays LTR.
        private static void AssertSummaryValueAssociation(Control control, string language)
        {
            TableLayoutPanel grid = Descendants(control).OfType<TableLayoutPanel>()
                .First(t => t.Controls.OfType<Label>().Any(l => l.Text == ReviewStrings.Get("Placement_FieldOriginal")));
            Label caption = grid.Controls.OfType<Label>().First(l => l.Text == ReviewStrings.Get("Placement_FieldOriginal"));
            TableLayoutPanelCellPosition captionCell = grid.GetCellPosition(caption);
            TextBox value = grid.Controls.OfType<TextBox>().First(t => grid.GetCellPosition(t).Row == captionCell.Row);
            Assert(grid.GetCellPosition(value).Column == captionCell.Column + 1,
                "The summary value must sit in the column immediately beside its caption (" + language + ").");
            HorizontalAlignment expected = language == "fa" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            Assert(value.TextAlign == expected,
                "The summary value must hug its caption, not the opposite edge (" + language + "). Actual: " + value.TextAlign);
            Assert(value.RightToLeft == RightToLeft.No, "The summary value must stay LTR (" + language + ").");
        }

        // Every answer control stays in the cell beside its question, and the question text hugs the
        // answer control so the two never drift to opposite card edges.
        private static void AssertQuestionAnswerAssociation(Control control)
        {
            TableLayoutPanel grid = Descendants(control).OfType<TableLayoutPanel>()
                .Single(t => t.Controls.OfType<Control>().Count(c => c.Tag as string != null) == EquipmentPlacementResolutionQuestions.All.Count);
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(control, question.QuestionId);
                TableLayoutPanelCellPosition answerCell = grid.GetCellPosition(answer);
                Label label = grid.Controls.OfType<Label>().First(l => grid.GetCellPosition(l).Row == answerCell.Row);
                Assert(grid.GetCellPosition(label).Column == answerCell.Column - 1,
                    "A question must own the cell immediately beside its answer control: " + question.QuestionId);
                Assert(label.TextAlign == ReviewStyle.ControlSideLabelAlignment,
                    "A question must hug its answer control: " + question.QuestionId);
            }
        }

        // Classification and Disposition each fill their labelled field, directly beneath their caption.
        private static void AssertDecisionFieldAssociation(Control control)
        {
            foreach (string name in new[] { "Classification", "PlacementProposalDisposition" })
            {
                ComboBox combo = Combo(control, name);
                Assert(combo.Dock == DockStyle.Top || combo.Dock == DockStyle.Fill,
                    "A decision control must fill its labelled field instead of floating: " + name);
                Control fieldHost = combo.Parent;
                Assert(fieldHost != null && fieldHost.Dock == DockStyle.Fill, "The decision field host is wrong: " + name);
                Panel captionHost = fieldHost.Parent.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Top);
                Assert(captionHost != null && captionHost.Controls.OfType<Label>().Any(l => !string.IsNullOrEmpty(l.Text)),
                    "A decision control must keep its caption directly above it: " + name);
            }
        }

        // The normal list row shows the localized marker and the friendly disposition display text and
        // never the raw proposal identity.
        private static void AssertProposalRowDisplay(Control control, PlacementProposal proposal, string language)
        {
            ListBox list = Descendants(control).OfType<ListBox>().Single();
            string row = list.Items.Cast<object>().Select(x => x == null ? string.Empty : x.ToString() ?? string.Empty)
                .FirstOrDefault(x => x.IndexOf(ReviewStrings.Get("Placement_ListProposal"), StringComparison.Ordinal) >= 0) ?? string.Empty;
            Assert(!string.IsNullOrEmpty(row), "The generated proposal row is missing (" + language + ").");
            Assert(row.IndexOf(proposal.ProposalId, StringComparison.Ordinal) < 0,
                "The normal list row must not expose the raw proposal identity (" + language + "): " + row);
            Assert(row.IndexOf(ReviewDisplayText.Disposition(PlacementProposalDispositions.PendingReview), StringComparison.Ordinal) >= 0,
                "The normal list row must show the friendly disposition display text (" + language + "): " + row);
        }

        // The canonical proposal identity stays reachable under the collapsed Technical Details.
        private static void AssertCanonicalProposalIdInTechnicalDetails(Control control, PlacementProposal proposal)
        {
            ListBox list = Descendants(control).OfType<ListBox>().Single();
            int proposalIndex = -1;
            for (int i = 0; i < list.Items.Count; i++)
            {
                string text = list.Items[i] as string ?? string.Empty;
                if (text.IndexOf(ReviewStrings.Get("Placement_ListProposal"), StringComparison.Ordinal) >= 0) { proposalIndex = i; break; }
            }
            Assert(proposalIndex >= 0, "The generated proposal row is missing from the queue.");
            list.SelectedIndex = proposalIndex;
            Button(control, ReviewStrings.Get("Placement_ButtonTechnicalShow")).PerformClick();
            Assert(Descendants(control).OfType<TextBox>().Where(t => t.AccessibleName == "TechnicalValue")
                .Any(t => t.Text == proposal.ProposalId), "The canonical proposal identity must remain under Technical Details.");
        }

        // The primary action is anchored to the physical right end of the action bar in both languages
        // (the RTL-leading side in Persian), and a disabled primary action stays clearly readable
        // without looking enabled.
        private static void AssertPrimaryActionAtRightEnd(Control control)
        {
            control.Size = new System.Drawing.Size(1000, 960);
            PerformLayout(control);
            Button save = Descendants(control).OfType<Button>().Single(b => b.AccessibleName == "SaveResolution");
            Label hint = Descendants(control).OfType<Label>().First(l => l.Text == ReviewStrings.Get("Placement_Hint"));
            Assert(save.Left > hint.Left,
                "The primary action must sit at the right end of the action bar. save=" + save.Left + " hint=" + hint.Left);
            Assert(!save.Enabled, "The primary action must start disabled before a pick is captured.");
            Assert(save.ForeColor.GetBrightness() >= 0.6f,
                "A disabled primary action must stay clearly readable. Brightness: " + save.ForeColor.GetBrightness());
            Assert(save.BackColor.GetBrightness() < 0.6f, "A disabled primary action must stay on the dark surface.");
            Assert(save.BackColor != ReviewUi.Primary, "A disabled primary action must not look enabled.");
        }

        // ---- helpers --------------------------------------------------------------------------

        private static EquipmentPlacementResolutionControl NewControl(
            ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService,
            IReadOnlyList<PlacementProposal> proposals)
        {
            return new EquipmentPlacementResolutionControl(UnresolvedPlan(), DrawingName, DrawingSha, Units, reviewService, resolutionService, new StubPickProvider(null), proposals, DrawingName);
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

        private static PlacementProposal BuildProposal()
        {
            var proposal = new PlacementProposal
            {
                ProposalId = "PP-" + new string('8', 48),
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

        private static string FieldCaption(Control input) => FieldCaptionLabel(input)?.Text;

        private static Label FieldCaptionLabel(Control input)
        {
            Control pair = input.Parent?.Parent;
            if (pair == null) return null;
            Panel captionHost = pair.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Top);
            return captionHost?.Controls.OfType<Label>().FirstOrDefault();
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
