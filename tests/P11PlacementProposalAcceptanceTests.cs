#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using EngiSentry.AutoCAD2021;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Final P11.1 runtime acceptance. It exercises the REAL production governance/projection/resolution
    /// pipeline end to end — the same services and the same ES_SUGGEST / ES_PLACEMENTRESOLVE composition the
    /// commands use — against a disposable synthetic AutoCAD 2021 fixture and isolated TEST_ONLY
    /// review / resolution / disposition roots.
    ///
    /// What is real here:
    /// - ProjectDistanceGovernanceRuntimeLoader, PlacementProposalGovernedRuleProjection,
    ///   PlacementProposalRuntime, PlacementProposalSnapshotWriter and ReadCurrentForDrawing are the exact
    ///   production types the commands call.
    /// - The engineer surface is the real EquipmentPlacementResolutionControl that
    ///   VisualResultsPalette.ShowEquipmentPlacementResolution hosts for ES_PLACEMENTRESOLVE; it is driven
    ///   through its public control surface, exactly as the engineer would. Only the pick provider is a
    ///   TEST_ONLY stand-in for the AutoCAD editor, and the drawing "SHA-256" is the real SHA-256 of a
    ///   disposable synthetic file computed by the accepted HumanEngineeringReviewDecisionFileStore.ComputeSha256.
    /// - Execution authority flows ONLY through an explicit, evidence-backed TEST_ONLY
    ///   ProjectRuleAuthorityBinding validated against a TEST_ONLY deterministic authority context. The real
    ///   production catalog is still asserted to grant nothing.
    ///
    /// What is never done here: no real authority, approval, trusted-issuer entry or engineer judgment is
    /// fabricated; no production store is written; no DWG is modified; every reviewer identity is an
    /// unmistakably TEST_ONLY label.
    /// </summary>
    internal static class P11PlacementProposalAcceptanceTests
    {
        // ---- disposable synthetic drawing fixture (units = metres, 1 drawing unit = 1 metre) ------------
        private const string InsertionUnits = "Meters";
        private const double MetersPerDrawingUnit = 1.0d;
        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const string FenceHandle = "fence:geometry";
        private const double EquipmentEastingMeters = 100d;
        private const decimal RoadMinimumMeters = 15m;

        // ---- TEST_ONLY deterministic governed authority (composed through the REAL authority chain) ----
        private const string TestSourceId = "test.governed.source.rev1";
        private const string TestClauseId = "test.governed.road.clearance";
        private const string TestPolicyId = "test.governed.policy.road";
        private const string TestAuthorityRuleId = "test.authority.governed.road.minimum-clearance";
        private const string TestSourceSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        // ---- unmistakably TEST_ONLY engineer identity ----------------------------------------------------
        private const string Reviewer = "P11.1 TEST-ONLY ACCEPTANCE ENGINEER";
        private const string ReviewerRole = "TEST-ONLY ACCEPTANCE HARNESS";

        private static int _assertions;

        internal static void Run()
        {
            // Driving the real engineer surface creates WinForms control handles on this thread, which
            // installs a WindowsFormsSynchronizationContext. Left in place, that would deadlock later
            // async-over-blocking tests (the Jev calibration harness awaits inside its HTTP handler). The
            // pre-existing context is restored on the way out so this acceptance test leaves no UI state
            // behind on the shared test thread.
            SynchronizationContext previousSyncContext = SynchronizationContext.Current;

            // 0. The production engineer stores must be at their default roots and no override may be active.
            AssertRootsAreProduction();
            Dictionary<string, int> productionBaseline = ProductionStoreSnapshot();
            Assert(ProductionCatalogGrantsNothing(), "The production authority catalog granted auto-execution at the start of acceptance.");

            string projectRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-P11.1-ACCEPT-" + Guid.NewGuid().ToString("N"));
            string reviewRoot = Path.Combine(projectRoot, "reviews");
            string resolutionRoot = Path.Combine(projectRoot, "resolutions");
            string dispositionRoot = Path.Combine(projectRoot, "dispositions");
            string approvalDirectory = Path.Combine(projectRoot, "qa", "plotplan");
            Directory.CreateDirectory(reviewRoot);
            Directory.CreateDirectory(resolutionRoot);
            Directory.CreateDirectory(dispositionRoot);
            Directory.CreateDirectory(approvalDirectory);

            string drawingName = "P11.1-ACCEPT-TEST-ONLY-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".dwg";
            string drawingPath = Path.Combine(projectRoot, drawingName);
            File.WriteAllBytes(drawingPath, SyntheticDrawingBytes());

            string drawingSha = HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath);
            DateTime scanTimestampUtc = DateTime.UtcNow;

            var reviewService = new ProjectEngineerReviewService(reviewRoot);
            var dispositionService = new PlacementProposalDispositionService(dispositionRoot);
            var resolutionService = new EquipmentPlacementResolutionService(resolutionRoot, reviewService, dispositionService);

            var writtenSidecars = new List<string>();
            try
            {
                // ============================ PART A: ES_SUGGEST =======================================
                // The real ES_SUGGEST composition: resolved governance + governed projection + proposal runtime.
                ProjectDistanceCriteria criteria = TestCriteria(projectRoot);
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                PlotPlanStandardsAuthorityRuntimeContext authorityContext = DeterministicTestAuthorityContext();
                ProjectRuleAuthorityBinding binding = ValidBinding();

                IReadOnlyList<StandardsPackRule> governedRules = PlacementProposalGovernedRuleProjection.Build(
                    criteria, governance, authorityContext, new[] { binding });

                PlacementProposalRunResult suggest = PlacementProposalRuntime.Build(
                    PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d), governedRules, governance, out PlacementProposalRunDiagnostics diagnostics);

                // Required: EXACTLY one executable PlacementProposal.
                Assert(diagnostics.ProposalCount == 1, "ES_SUGGEST produced " + diagnostics.ProposalCount + " proposals, expected exactly 1.");
                Assert(diagnostics.ExecutableViolatedCount == 1, "ES_SUGGEST counted " + diagnostics.ExecutableViolatedCount + " executable violations, expected exactly 1.");
                Assert(suggest.Proposals.Count == 1, "ES_SUGGEST emitted " + suggest.Proposals.Count + " proposal objects, expected exactly 1.");
                Assert(diagnostics.ViolatedCount > 0 && diagnostics.ReviewRequiredCount >= 0, "ES_SUGGEST lost the violated/review diagnostics.");

                PlacementProposal generated = suggest.Proposals[0];
                Assert(generated.EngineeringReviewRequired, "A generated proposal stopped requiring engineering review.");
                Assert(generated.ViolatedRule != null && generated.ViolatedRule.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                    "The proposal did not address the governed road-clearance rule.");
                Assert(generated.RequiredDistanceMeters == RoadMinimumMeters, "The proposal lost the 15 m governed criterion: " + generated.RequiredDistanceMeters);
                Assert(generated.ObservedDistanceMeters > 9.5d && generated.ObservedDistanceMeters < 10.5d, "The proposal lost the 10 m observed distance.");
                Assert(generated.EquipmentEntityId == EquipmentEntityId && generated.NormalizedTag == NormalizedTag, "The proposal lost the equipment identity.");

                // The exact stamping ES_SUGGEST performs on every proposal: drawing SHA-256 + fingerprint.
                foreach (PlacementProposal proposal in suggest.Proposals)
                {
                    proposal.SourceDrawingSha256 = drawingSha;
                    proposal.ProposalFingerprint = PlacementProposalFingerprint.Compute(proposal);
                }
                Assert(!string.IsNullOrEmpty(generated.ProposalFingerprint), "The proposal fingerprint was not computed.");
                Assert(generated.SourceDrawingSha256 == drawingSha, "The proposal drawing SHA-256 was not stamped.");

                string sidecar = PlacementProposalSnapshotWriter.Write(suggest.Proposals, drawingName, scanTimestampUtc);
                writtenSidecars.Add(sidecar);
                Assert(File.Exists(sidecar), "The proposal sidecar was not written: " + sidecar);

                // The exact production read path ES_PLACEMENTRESOLVE uses to reload the current proposals.
                List<PlacementProposal> reloaded = PlacementProposalSnapshotWriter.ReadCurrentForDrawing(drawingName, drawingSha);
                Assert(reloaded.Count == 1, "The production proposal reload returned " + reloaded.Count + " proposals, expected exactly 1.");
                Assert(reloaded[0].ProposalId == generated.ProposalId && reloaded[0].ProposalFingerprint == generated.ProposalFingerprint,
                    "The reloaded proposal is not the generated proposal.");
                Assert(PlacementProposalFingerprint.Compute(reloaded[0]) == reloaded[0].ProposalFingerprint, "The reloaded proposal fingerprint does not match its content.");

                string expectedApprovingEasting = Convert.ToDecimal(generated.ProposedEasting, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                string expectedApprovingNorthing = Convert.ToDecimal(generated.ProposedNorthing, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

                // ============================ PART B: ES_PLACEMENTRESOLVE ==============================

                // ---- 1 + 2. The proposal appears in the real palette surface; abandoning it writes nothing.
                {
                    EquipmentPlacementResolutionControl control = CreateResolutionControl(drawingName, drawingSha, drawingPath, reviewService, resolutionService, reloaded, pick: null);
                    try
                    {
                        Assert(control.UnresolvedCount == 0, "The fixture equipment is already Complete and must not appear as an unresolved case.");
                        string item = PlacementListItems(control).FirstOrDefault(x => x.IndexOf(generated.ProposalId!, StringComparison.Ordinal) >= 0) ?? string.Empty;
                        Assert(!string.IsNullOrEmpty(item), "The generated proposal did not appear in the real ES_PLACEMENTRESOLVE palette surface.");
                        Assert(item.IndexOf(PlacementProposalDispositions.PendingReview, StringComparison.Ordinal) >= 0, "The proposal did not appear as PENDING_REVIEW: " + item);

                        // 2. CANCEL: the engineer abandons the operation without recording a decision.
                        control.Dispose();
                        Assert(DispositionFiles(dispositionService) == 0, "Abandoning ES_PLACEMENTRESOLVE wrote a disposition.");
                        Assert(ResolutionFiles(resolutionService) == 0, "Abandoning ES_PLACEMENTRESOLVE wrote a resolution.");
                        Assert(ResponseFiles(reviewRoot) == 0, "Abandoning ES_PLACEMENTRESOLVE wrote an engineer review.");
                    }
                    finally { control.Dispose(); }
                    AssertDrawingUnchanged("after abandon", drawingPath, drawingSha);
                }

                // ---- 3. TEST_ONLY APPROVED -> the original proposal coordinates become trusted.
                {
                    EquipmentPlacementResolutionControl control = CreateResolutionControl(drawingName, drawingSha, drawingPath, reviewService, resolutionService, reloaded, pick: null);
                    try
                    {
                        RecordDisposition(control, PlacementProposalDispositions.Approved, "TEST_ONLY acceptance: approve the generated proposal point.");
                        AssertDispositionRecorded(control, PlacementProposalDispositions.Approved);

                        PlacementProposalDispositionRecord approved = dispositionService.GetStatus(generated).CurrentRecord!;
                        Assert(approved.Disposition == PlacementProposalDispositions.Approved && approved.SelectedOrModifiedPlacement != null, "APPROVED did not persist a selected placement.");
                        Assert(approved.SelectedOrModifiedPlacement!.Source == EquipmentPlacementResolutionSources.ProposalApproval, "APPROVED did not record the proposal-approval source.");

                        EquipmentPlacementResolution trusted = resolutionService.ResolveTrusted(drawingSha, EquipmentEntityId, InsertionUnits)!;
                        Assert(trusted != null, "APPROVED left no trusted placement.");
                        Assert(trusted.Easting == Convert.ToDecimal(generated.ProposedEasting, CultureInfo.InvariantCulture)
                            && trusted.Northing == Convert.ToDecimal(generated.ProposedNorthing, CultureInfo.InvariantCulture),
                            "APPROVED must trust the ORIGINAL proposal coordinates. Actual: " + trusted.Easting + "/" + trusted.Northing
                            + " expected " + expectedApprovingEasting + "/" + expectedApprovingNorthing);
                        Assert(trusted.Source == EquipmentPlacementResolutionSources.ProposalApproval, "APPROVED trust lost its proposal-approval provenance.");
                        Assert(trusted.ProposalId == generated.ProposalId && trusted.DispositionRecordId == approved.RecordId, "APPROVED trust is not bound to its disposition.");
                        Assert(ResolutionFiles(resolutionService) == 1, "APPROVED wrote " + ResolutionFiles(resolutionService) + " resolutions, expected exactly 1.");

                        // A fresh plot-plan QA reflection sees only the governing approval.
                        Assert(FreshPlotQaTrusts(resolutionService, drawingSha, InsertionUnits).Count == 1, "A fresh ES_PLOTQA did not reflect the governing approval.");
                    }
                    finally { control.Dispose(); }
                    AssertDrawingUnchanged("after APPROVED", drawingPath, drawingSha);
                }

                // ---- 4. TEST_ONLY MODIFIED -> the original proposal stays immutable and the engineer point is trusted.
                {
                    // The engineer selects a free-space point clear of the original proposed point.
                    var engineerPick = new EquipmentPlacementPick { Source = EquipmentPlacementResolutionSources.FreeSpace, PointX = EquipmentEastingMeters, PointY = 60d, PointZ = 0d };
                    EquipmentPlacementResolutionControl control = CreateResolutionControl(drawingName, drawingSha, drawingPath, reviewService, resolutionService, reloaded, pick: engineerPick);
                    try
                    {
                        RecordDisposition(control, PlacementProposalDispositions.Modified, "TEST_ONLY acceptance: move the equipment to the engineer-selected point.", performFreeSpacePick: true);
                        AssertDispositionRecorded(control, PlacementProposalDispositions.Modified);

                        PlacementProposalDispositionRecord modified = dispositionService.GetStatus(generated).CurrentRecord!;
                        Assert(modified.Disposition == PlacementProposalDispositions.Modified && modified.SelectedOrModifiedPlacement != null, "MODIFIED did not persist an engineer-selected placement.");
                        Assert(modified.SelectedOrModifiedPlacement!.Easting == Convert.ToDecimal(engineerPick.PointX, CultureInfo.InvariantCulture)
                            && modified.SelectedOrModifiedPlacement!.Northing == Convert.ToDecimal(engineerPick.PointY, CultureInfo.InvariantCulture),
                            "MODIFIED did not record the engineer-selected coordinates.");

                        // The ORIGINAL proposal remains immutable: its fingerprint and coordinates are unchanged.
                        PlacementProposal immutable = PlacementProposalSnapshotWriter.ReadCurrentForDrawing(drawingName, drawingSha).Single(x => x.ProposalId == generated.ProposalId);
                        Assert(PlacementProposalFingerprint.Compute(immutable) == generated.ProposalFingerprint, "MODIFIED changed the original proposal fingerprint (immutability broken).");
                        Assert(immutable.ProposedEasting == generated.ProposedEasting && immutable.ProposedNorthing == generated.ProposedNorthing, "MODIFIED changed the original proposal coordinates.");
                        Assert(modified.OriginalProposal.ProposedEasting == generated.ProposedEasting && modified.OriginalProposal.ProposedNorthing == generated.ProposedNorthing
                            && PlacementProposalFingerprint.Compute(modified.OriginalProposal) == generated.ProposalFingerprint, "The disposition did not preserve the immutable original proposal.");

                        EquipmentPlacementResolution trusted = resolutionService.ResolveTrusted(drawingSha, EquipmentEntityId, InsertionUnits)!;
                        Assert(trusted != null, "MODIFIED left no trusted placement.");
                        Assert(trusted.Easting == Convert.ToDecimal(engineerPick.PointX, CultureInfo.InvariantCulture)
                            && trusted.Northing == Convert.ToDecimal(engineerPick.PointY, CultureInfo.InvariantCulture),
                            "MODIFIED must trust the ENGINEER-SELECTED coordinates. Actual: " + trusted.Easting + "/" + trusted.Northing);
                        Assert(trusted.Source == EquipmentPlacementResolutionSources.FreeSpace, "MODIFIED trust lost its engineer-pick provenance.");
                        Assert(trusted.DispositionRecordId == modified.RecordId, "MODIFIED trust is not bound to its disposition.");
                        Assert(ResolutionFiles(resolutionService) == 2, "MODIFIED wrote " + ResolutionFiles(resolutionService) + " resolutions in total, expected 2 (approval + modification).");

                        // A fresh plot-plan QA reflection sees ONLY the latest valid disposition (the modification).
                        IReadOnlyList<EquipmentPlacementResolution> fresh = FreshPlotQaTrusts(resolutionService, drawingSha, InsertionUnits);
                        Assert(fresh.Count == 1, "A fresh ES_PLOTQA reflected more than one trusted placement for one equipment identity.");
                        Assert(fresh[0].Easting == Convert.ToDecimal(engineerPick.PointX, CultureInfo.InvariantCulture)
                            && fresh[0].Northing == Convert.ToDecimal(engineerPick.PointY, CultureInfo.InvariantCulture),
                            "A fresh ES_PLOTQA did not reflect the LATEST valid disposition.");
                    }
                    finally { control.Dispose(); }
                    AssertDrawingUnchanged("after MODIFIED", drawingPath, drawingSha);
                }

                // ---- 5. TEST_ONLY REJECTED -> no trusted placement remains.
                {
                    EquipmentPlacementResolutionControl control = CreateResolutionControl(drawingName, drawingSha, drawingPath, reviewService, resolutionService, reloaded, pick: null);
                    try
                    {
                        RecordDisposition(control, PlacementProposalDispositions.Rejected, "TEST_ONLY acceptance: reject the proposal; no placement may remain trusted.");
                        AssertDispositionRecorded(control, PlacementProposalDispositions.Rejected);

                        PlacementProposalDispositionRecord rejected = dispositionService.GetStatus(generated).CurrentRecord!;
                        Assert(rejected.Disposition == PlacementProposalDispositions.Rejected && rejected.SelectedOrModifiedPlacement == null, "REJECTED must not carry an active placement.");
                        Assert(resolutionService.ResolveTrusted(drawingSha, EquipmentEntityId, InsertionUnits) == null, "REJECTED left a trusted placement active.");

                        // A fresh plot-plan QA reflection now carries no trusted placement for the identity.
                        Assert(FreshPlotQaTrusts(resolutionService, drawingSha, InsertionUnits).Count == 0, "A fresh ES_PLOTQA still reflected a trusted placement after REJECTED.");
                        // The resolution records remain on disk as audit history but none is trusted.
                        Assert(ResolutionFiles(resolutionService) == 2, "REJECTED must not delete audit history; expected the 2 persisted resolutions to remain.");
                    }
                    finally { control.Dispose(); }
                    AssertDrawingUnchanged("after REJECTED", drawingPath, drawingSha);
                }

                // ---- 6 (consolidated above): every fresh ES_PLOTQA reflection followed only the latest
                //         valid disposition (approve -> modify -> none). Asserted at each stage.

                // ---- 7 + 8. DBMOD and the DWG SHA-256 are unchanged for the whole run.
                AssertDrawingUnchanged("at end of acceptance", drawingPath, drawingSha);
                Assert(ProductionCatalogGrantsNothing(), "The production authority catalog granted auto-execution at the end of acceptance.");

                // ---- 9. The production stores are untouched.
                AssertProductionStoresUntouched(productionBaseline);

                Console.WriteLine("P11.1 placement proposal runtime acceptance passed: " + _assertions);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousSyncContext);
                foreach (string sidecar in writtenSidecars)
                {
                    try { if (File.Exists(sidecar)) File.Delete(sidecar); } catch (IOException) { }
                }
                if (Directory.Exists(projectRoot))
                {
                    try { Directory.Delete(projectRoot, true); } catch (IOException) { }
                }
                // Final guard: the production engineer stores are still at their default roots and untouched.
                AssertRootsAreProduction();
            }
        }

        // ---- production-catalog and store guards -----------------------------------------------------------

        private static bool ProductionCatalogGrantsNothing()
        {
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            return real.ExecutionGate.Rules.Count > 0 && real.ExecutionGate.Rules.All(g => !g.AutoExecutionAllowed);
        }

        private static void AssertRootsAreProduction()
        {
            Assert(ProjectEngineerReviewService.ResolveRoot() == ProjectEngineerReviewService.DefaultRoot, "The production review root override must stay disabled.");
            Assert(EquipmentPlacementResolutionService.ResolveRoot() == EquipmentPlacementResolutionService.DefaultRoot, "The production resolution root override must stay disabled.");
            Assert(PlacementProposalDispositionService.ResolveRoot() == PlacementProposalDispositionService.DefaultRoot, "The production disposition root override must stay disabled.");
        }

        private static Dictionary<string, int> ProductionStoreSnapshot()
        {
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "review", CountFiles(ProjectEngineerReviewService.DefaultRoot) },
                { "resolution", CountFiles(EquipmentPlacementResolutionService.DefaultRoot) },
                { "disposition", CountFiles(PlacementProposalDispositionService.DefaultRoot) },
            };
        }

        private static void AssertProductionStoresUntouched(Dictionary<string, int> baseline)
        {
            Assert(CountFiles(ProjectEngineerReviewService.DefaultRoot) == baseline["review"], "The production review store was written by acceptance.");
            Assert(CountFiles(EquipmentPlacementResolutionService.DefaultRoot) == baseline["resolution"], "The production resolution store was written by acceptance.");
            Assert(CountFiles(PlacementProposalDispositionService.DefaultRoot) == baseline["disposition"], "The production disposition store was written by acceptance.");
        }

        private static int CountFiles(string root) => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;

        // ---- the exact drawing-hash invariant the commands assert through DBMOD ----------------------------

        private static void AssertDrawingUnchanged(string stage, string drawingPath, string expectedSha)
        {
            string actual = HumanEngineeringReviewDecisionFileStore.ComputeSha256(drawingPath);
            Assert(string.Equals(actual, expectedSha, StringComparison.Ordinal), "The drawing SHA-256 changed " + stage + ": " + actual + " != " + expectedSha);
        }

        // ---- the real ES_PLACEMENTRESOLVE surface -----------------------------------------------------------

        private static EquipmentPlacementResolutionControl CreateResolutionControl(string drawingName, string drawingSha, string drawingPath,
            ProjectEngineerReviewService reviewService, EquipmentPlacementResolutionService resolutionService,
            IReadOnlyList<PlacementProposal> proposals, EquipmentPlacementPick? pick)
        {
            return new EquipmentPlacementResolutionControl(
                PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d), drawingName, drawingSha, InsertionUnits,
                reviewService, resolutionService, new StubPickProvider(pick), proposals, drawingPath);
        }

        /// <summary>Drives the real surface exactly as an engineer would, then records the disposition.</summary>
        private static void RecordDisposition(EquipmentPlacementResolutionControl control, string disposition, string comment, bool performFreeSpacePick = false)
        {
            Box(control, "ReviewerDisplayName").Text = Reviewer;
            Box(control, "ReviewerRole").Text = ReviewerRole;
            Box(control, "EngineerComment").Text = comment;
            Combo(control, "Classification").SelectedIndex = 1; // PASS
            foreach (ProjectEngineerReviewQuestionDefinition question in EquipmentPlacementResolutionQuestions.All)
            {
                Control answer = AnswerControl(control, question.QuestionId);
                if (answer is ComboBox combo) combo.SelectedIndex = 1; else answer.Text = "TEST-ONLY acceptance answer";
            }
            Combo(control, "PlacementProposalDisposition").SelectedItem = disposition;
            if (performFreeSpacePick) Button(control, "Pick Free-Space Point").PerformClick();
            Assert(Button(control, "Record Decision").Enabled, "The surface did not enable the record button for disposition " + disposition + ".");
            Button(control, "Record Decision").PerformClick();
        }

        private static void AssertDispositionRecorded(EquipmentPlacementResolutionControl control, string disposition)
        {
            string message = MessageOf(control);
            Assert(message.StartsWith("Engineer disposition recorded: " + disposition, StringComparison.Ordinal),
                "The surface did not record the " + disposition + " disposition. Surface message: " + message);
        }

        // ---- the fresh ES_PLOTQA trusted-resolution reflection (the read the command performs) -----------

        private static IReadOnlyList<EquipmentPlacementResolution> FreshPlotQaTrusts(EquipmentPlacementResolutionService resolutionService, string drawingSha, string insertionUnits)
            => resolutionService.ResolveTrustedForDrawing(drawingSha, insertionUnits);

        // ---- TEST_ONLY deterministic authority context, composed through the REAL authority chain ----------

        private static PlotPlanStandardsAuthorityRuntimeContext DeterministicTestAuthorityContext()
        {
            StandardSourceDefinition source = new StandardSourceDefinition
            {
                SourceId = TestSourceId,
                DocumentNumber = "TEST-GOV-001",
                Title = "TEST_ONLY governed spacing authority",
                Issuer = "TEST_ONLY acceptance harness",
                RevisionOrEdition = "1",
                SourceFileName = "test-governed.pdf",
                SourceSha256 = TestSourceSha256
            };
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { source });

            StandardClauseDefinition clause = new StandardClauseDefinition
            {
                ClauseId = TestClauseId,
                SourceId = TestSourceId,
                ClauseReference = "1",
                ClauseText = "TEST_ONLY deterministic minimum equipment-to-road clearance.",
                PageNumber = 1
            };
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { clause });

            StandardAuthorityPolicyRegistry policies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[]
            {
                new StandardAuthorityPolicyDefinition
                {
                    PolicyId = TestPolicyId,
                    SourceId = TestSourceId,
                    ClauseId = TestClauseId,
                    AuthorityClass = StandardAuthorityClasses.GoverningProject,
                    EnforcementMode = StandardEnforcementModes.Deterministic,
                    Rationale = "TEST_ONLY: explicit deterministic governing authority used only by the acceptance harness."
                }
            });

            StandardRulePackDefinition pack = new StandardRulePackDefinition
            {
                PackId = "test.governed.pack",
                Name = "TEST_ONLY governed pack",
                Rules = new List<StandardRuleDefinition>
                {
                    new StandardRuleDefinition { RuleId = TestAuthorityRuleId, Title = "TEST_ONLY governed road clearance", ClauseIds = new List<string> { TestClauseId } }
                }
            };
            StandardRulePackResolution ruleResolution = StandardRulePackResolver.Resolve(
                StandardRulePackRegistry.Create(clauses, new[] { pack }),
                new[] { new StandardRulePackPrecedenceDefinition { PackId = "test.governed.pack", Precedence = 10 } });

            StandardAuthorityResolution authorityResolution = StandardAuthorityResolver.Resolve(ruleResolution, clauses, policies);
            StandardExecutionGateResolution gate = StandardAuthorityExecutionGate.Evaluate(authorityResolution);
            StandardAuthorityReviewProjection review = StandardAuthorityReviewProjector.Create(authorityResolution, sources, clauses);

            return new PlotPlanStandardsAuthorityRuntimeContext(sources, clauses, ruleResolution, policies, authorityResolution, gate, review);
        }

        /// <summary>An explicit, evidence-backed TEST_ONLY binding whose recorded fields match the TEST_ONLY
        /// deterministic authority chain exactly, so it is the only thing that grants executability here.</summary>
        private static ProjectRuleAuthorityBinding ValidBinding() => new ProjectRuleAuthorityBinding
        {
            ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
            AuthorityRuleId = TestAuthorityRuleId,
            ClauseIds = new List<string> { TestClauseId },
            PolicyId = TestPolicyId,
            SourceId = TestSourceId,
            SourceSha256 = TestSourceSha256,
            AuthorityClass = StandardAuthorityClasses.GoverningProject,
            EffectiveEnforcementMode = StandardEnforcementModes.Deterministic,
            Rationale = "TEST_ONLY: explicit evidence-backed binding for the acceptance harness only."
        };

        private static ProjectDistanceCriteria TestCriteria(string projectDirectory) => new ProjectDistanceCriteria
        {
            SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion,
            ProjectDirectory = projectDirectory,
            AccessRoadEdgeMinimumMeters = RoadMinimumMeters,
            SecurityFenceMinimumMeters = 12m,
            UnitBoundaryMinimumMeters = 9m
        };

        // ---- disposable synthetic drawing bytes (never a real DWG; its SHA-256 is the acceptance hash) ----

        private static byte[] SyntheticDrawingBytes()
        {
            string marker = "TEST_ONLY synthetic AutoCAD 2021 fixture for P11.1 acceptance. Not a drawing. Disposable.";
            return Enumerable.Range(0, 4096).Select(index => (byte)(marker[index % marker.Length] ^ (index & 0x7F))).ToArray();
        }

        // ---- synthetic plot-plan fixture: one executable road-clearance violation ----------------------------

        private static PlotPlanModel PlotPlanWithOneViolatedRoadPlacement(double observedMeters)
        {
            PlotPlanModel model = new PlotPlanModel
            {
                Drawing = new SemanticDrawing { FileName = "synthetic-acceptance.dwg", InsertionUnits = InsertionUnits },
                Geometry = FullGeometry(),
                Measurements = new PlotSpatialMeasurementModel()
            };
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurement(observedMeters));
            model.EquipmentPlacements.Add(PlacementFor(PlacementId));
            return model;
        }

        private static PlotGeometryModel FullGeometry()
        {
            PlotGeometryModel model = new PlotGeometryModel();
            model.Primitives.Add(HorizontalPrimitive(RoadHandle, 0d, -1000d, 1000d));
            model.Primitives.Add(HorizontalPrimitive(FenceHandle, 40d, -1000d, 1000d));
            return model;
        }

        private static PlotGeometryPrimitive HorizontalPrimitive(string handle, double northingMeters, double westMeters, double eastMeters)
        {
            return new PlotGeometryPrimitive
            {
                SourceHandle = handle,
                SourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                Closed = false,
                Vertices = new List<PlotGeometryVertex>
                {
                    new PlotGeometryVertex { X = MetersToDrawingUnits(westMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d },
                    new PlotGeometryVertex { X = MetersToDrawingUnits(eastMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d }
                }
            };
        }

        private static EquipmentPlacement PlacementFor(string placementId)
        {
            return new EquipmentPlacement
            {
                PlacementId = placementId,
                EquipmentEntityId = EquipmentEntityId,
                TagSourceHandle = "tag:equipment-1",
                PlacementStatus = "Complete",
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 100m },
                Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 10m }
            };
        }

        private static EquipmentSpatialMeasurement RoadMeasurement(double observedMeters)
        {
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = "measurement:road-1",
                PlacementId = PlacementId,
                EquipmentEntityId = EquipmentEntityId,
                NormalizedTag = NormalizedTag,
                EquipmentEasting = Convert.ToDecimal(MetersToDrawingUnits(EquipmentEastingMeters), CultureInfo.InvariantCulture),
                EquipmentNorthing = Convert.ToDecimal(MetersToDrawingUnits(observedMeters), CultureInfo.InvariantCulture),
                ConstraintId = "bounded-constraint:" + ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                SubjectKind = "Road",
                LabelSourceHandle = RoadHandle + ":label",
                GeometrySourceHandle = RoadHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                ClosestPointX = MetersToDrawingUnits(EquipmentEastingMeters),
                ClosestPointY = MetersToDrawingUnits(0d),
                Distance2D = MetersToDrawingUnits(observedMeters),
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
            };
        }

        private static double MetersToDrawingUnits(double meters) => meters / MetersPerDrawingUnit;

        // ---- store counters ----------------------------------------------------------------------------------

        private static int DispositionFiles(PlacementProposalDispositionService service) => service.LoadAll().Count;
        private static int ResolutionFiles(EquipmentPlacementResolutionService service) => EquipmentPlacementResolutionFileStore.EnumerateRecordPaths(service.Root).Count();
        private static int ResponseFiles(string reviewRoot) => Directory.Exists(reviewRoot) ? Directory.GetFiles(reviewRoot, ProjectEngineerReviewService.ResponseFileName, SearchOption.AllDirectories).Length : 0;

        // ---- control-surface reflection (the accepted harness convention) ------------------------------------

        private static IEnumerable<string> PlacementListItems(EquipmentPlacementResolutionControl control)
        {
            FieldInfo? field = typeof(EquipmentPlacementResolutionControl).GetField("placementList", BindingFlags.Instance | BindingFlags.NonPublic);
            ListBox list = (ListBox)field!.GetValue(control)!;
            return list.Items.Cast<object>().Select(item => item == null ? string.Empty : item.ToString() ?? string.Empty);
        }

        private static string MessageOf(EquipmentPlacementResolutionControl control)
        {
            FieldInfo? field = typeof(EquipmentPlacementResolutionControl).GetField("message", BindingFlags.Instance | BindingFlags.NonPublic);
            Label label = (Label)field!.GetValue(control)!;
            return label.Text ?? string.Empty;
        }

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

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("P11.1 ACCEPTANCE FAILED: " + message);
        }

        private sealed class StubPickProvider : IEquipmentPlacementPickProvider
        {
            private readonly EquipmentPlacementPick? pick;
            internal StubPickProvider(EquipmentPlacementPick? pick) { this.pick = pick; }
            public EquipmentPlacementPick? PickEntity() => pick;
            public EquipmentPlacementPick? PickFreeSpace() => pick;
        }
    }
}
