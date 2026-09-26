#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    /// <summary>Focused P11.1 lifecycle checks using isolated, test-only persistence roots.</summary>
    internal static class P11ProposalDispositionTests
    {
        private const string Drawing = "P11.1-TEST-ONLY.dwg";
        private const string Sha = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string Engineer = "P11.1 TEST ENGINEER";
        private const string Role = "TEST-ONLY";
        private static int assertions;

        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-P11.1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                RootResolutionTests();
                var reviews = new ProjectEngineerReviewService(Path.Combine(root, "reviews"));
                var dispositions = new PlacementProposalDispositionService(Path.Combine(root, "dispositions"));
                var resolutions = new EquipmentPlacementResolutionService(Path.Combine(root, "resolutions"), reviews, dispositions);
                PlacementProposal proposal = Proposal();

                // P11.1-001: Generated evidence has no disposition until an engineer records one.
                Assert(dispositions.GetStatus(proposal).Disposition == PlacementProposalDispositions.PendingReview, "Generated proposal must default to PENDING_REVIEW.");
                // P11.1-002: Proposal generation alone cannot activate a placement resolution.
                Assert(resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters") == null, "A proposal alone must not create an active resolution.");

                // P11.1-003: An explicit approval activates exactly the original proposed point.
                PlacementProposalDispositionRecord approved = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Approved, Original(proposal), null, 1);
                EquipmentPlacementResolutionRecord originalResolution = BuildResolution(proposal, approved, reviews, PlacementProposalDispositions.Approved, 1);
                resolutions.Save(originalResolution);
                Assert(resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters")?.Easting == 120m, "APPROVED must activate original proposal coordinates.");

                // P11.1-004: A later rejection closes the governing approval and deactivates it.
                PlacementProposalDispositionRecord rejected = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Rejected, null, approved.RecordId, 2);
                Assert(resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters") == null, "REJECTED must not leave an earlier resolution trusted.");
                Assert(rejected.Disposition == PlacementProposalDispositions.Rejected, "REJECTED disposition was not retained.");

                // P11.1-005: A modified placement activates the explicit engineer-selected point.
                PlacementProposalSelectedPlacement modifiedSelection = Selected(proposal, 135m, 245m, EquipmentPlacementResolutionSources.FreeSpace);
                PlacementProposalDispositionRecord modified = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Modified, modifiedSelection, rejected.RecordId, 3);
                EquipmentPlacementResolutionRecord modifiedResolution = BuildResolution(proposal, modified, reviews, PlacementProposalDispositions.Modified, 3);
                resolutions.Save(modifiedResolution);
                var activeModified = resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters");
                Assert(activeModified?.Easting == 135m && activeModified.Northing == 245m, "MODIFIED must activate the engineer-selected coordinates.");

                // P11.1-006: Modification preserves original source evidence and its fingerprint.
                Assert(PlacementProposalFingerprint.Compute(proposal) == proposal.ProposalFingerprint && modified.OriginalProposal.ProposedEasting == 120d && modified.OriginalProposal.ProposedNorthing == 240d, "MODIFIED must preserve the original proposal unchanged.");

                // P11.1-007: Cancellation becomes the latest disposition and removes activation.
                PlacementProposalDispositionRecord cancelled = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Cancelled, null, modified.RecordId, 4);
                Assert(cancelled.Disposition == PlacementProposalDispositions.Cancelled && resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters") == null, "CANCELLED must not activate a placement.");

                // P11.1-008: Superseding disposition is historical, while latest valid state governs.
                PlacementProposalDispositionRecord superseded = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Superseded, null, cancelled.RecordId, 5);
                Assert(dispositions.GetHistory(proposal).Any(x => x.Record.RecordId == cancelled.RecordId && x.EffectiveDisposition == PlacementProposalDispositions.Superseded), "Older disposition must be represented as superseded history.");
                Assert(dispositions.GetStatus(proposal).CurrentRecordId == superseded.RecordId && resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters") == null, "SUPERSEDED latest state must not retain an active resolution.");

                // P11.1-009: A later valid approval becomes governing and restores only its bound resolution.
                PlacementProposalDispositionRecord reapproved = Decide(proposal, dispositions, reviews, PlacementProposalDispositions.Approved, Original(proposal), superseded.RecordId, 6);
                EquipmentPlacementResolutionRecord reapprovedResolution = BuildResolution(proposal, reapproved, reviews, PlacementProposalDispositions.Approved, 6);
                resolutions.Save(reapprovedResolution);
                Assert(dispositions.GetStatus(proposal).CurrentRecordId == reapproved.RecordId && resolutions.ResolveTrusted(Sha, proposal.EquipmentEntityId!, "Millimeters")?.Easting == 120m, "Latest valid APPROVED disposition must govern activation.");

                // P11.1-010: Advisory code cannot author the required engineer disposition.
                Assert(dispositions.GetStatus(proposal).CurrentRecord?.EngineerDecisionSource == PlacementProposalDispositionService.DecisionSourceEsPlacementResolve, "Only the explicit ES_PLACEMENTRESOLVE engineer action may author a disposition.");
                Assert(dispositions.LoadAll().All(x => x.EngineerDecisionSource != "Jev" && x.EngineerDecisionSource != "AI"), "Jev/AI must not author authoritative dispositions.");
                Assert(ProjectEngineerReviewService.WorkflowSourcePlacementProposal == "P11.1-PLACEMENT-PROPOSAL", "The P11.1 disposition workflow-source constant must stay bound to its recorded value.");

                // P11.1-011: Verify proposal -> review -> disposition -> resolution audit bindings.
                Assert(reapproved.OriginalProposalFingerprint == proposal.ProposalFingerprint
                    && reapproved.OriginalProposal.ProposalId == proposal.ProposalId
                    && reapproved.ReviewResponseId == reapprovedResolution.ResponseId
                    && reapproved.RecordId == reapprovedResolution.DispositionRecordId
                    && reapprovedResolution.ProposalId == proposal.ProposalId
                    && reapprovedResolution.ProposalFingerprint == proposal.ProposalFingerprint,
                    "Audit chain must bind proposal, engineer disposition, persisted review, and resolution.");

                // P11.1-012: Replaying the same action is rejected deterministically as stale.
                bool replayRejected = false;
                try { dispositions.Record(proposal, proposal.ProposalFingerprint!, proposal.ProposalFingerprint!, PlacementProposalDispositions.Approved, Original(proposal), "approval reason", Engineer, Role, superseded.RecordId, LoadRequest(reviews, reapproved), LoadResponse(reviews, reapproved), Utc(6)); }
                catch (InvalidOperationException ex) when (ex.Message.IndexOf("stale", StringComparison.OrdinalIgnoreCase) >= 0) { replayRejected = true; }
                Assert(replayRejected, "Duplicate replay must be deterministically rejected as stale.");

                // P11.1-013: Content changed under the old fingerprint is rejected before review or persistence.
                PlacementProposal changed = Proposal();
                changed.ProposedEasting += 1d;
                string changedFingerprint = PlacementProposalFingerprint.Compute(changed);
                bool staleRejected = false;
                try { dispositions.Record(changed, proposal.ProposalFingerprint!, changedFingerprint, PlacementProposalDispositions.Approved, Original(proposal), "stale attempt", Engineer, Role, null, null!, null!, Utc(7)); }
                catch (InvalidOperationException ex) when (ex.Message.IndexOf("stale", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("fingerprint", StringComparison.OrdinalIgnoreCase) >= 0) { staleRejected = true; }
                Assert(staleRejected && dispositions.LoadAll().Count == 6, "A stale proposal fingerprint must be rejected without appending a disposition.");

                Console.WriteLine("P11.1 proposal disposition lifecycle assertions passed: " + assertions);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        // The disposition store resolves its own persistence root, exactly like the accepted review and
        // resolution stores. The default/production root must be used unless an explicit override is set, so
        // an accidental environment variable cannot silently relocate engineer dispositions.
        private static void RootResolutionTests()
        {
            Assert(PlacementProposalDispositionService.ResolveRoot() == PlacementProposalDispositionService.DefaultRoot, "The disposition-root override must stay disabled outside an explicit test scope.");
            string productionRoot = PlacementProposalDispositionService.DefaultRoot;
            int productionCases = Directory.Exists(productionRoot) ? Directory.GetDirectories(productionRoot).Length : 0;
            string overrideRoot = Path.Combine(Path.GetTempPath(), "EngiSentry-PPD-OVERRIDE-" + Guid.NewGuid().ToString("N"));
            try
            {
                Environment.SetEnvironmentVariable(PlacementProposalDispositionService.RootOverrideVariable, overrideRoot);
                Assert(PlacementProposalDispositionService.ResolveRoot() == overrideRoot, "An explicit disposition-root override must be honored when it is set.");
                var overridden = new PlacementProposalDispositionService();
                Assert(overridden.Root == overrideRoot, "The parameterless disposition service must resolve the override root.");
                PlacementProposal proposal = Proposal();
                Assert(overridden.GetStatus(proposal).Disposition == PlacementProposalDispositions.PendingReview, "An isolated override root must start empty and report PENDING_REVIEW.");
                Assert((Directory.Exists(productionRoot) ? Directory.GetDirectories(productionRoot).Length : 0) == productionCases, "The default/production disposition root must be untouched while the override is set.");
            }
            finally { Environment.SetEnvironmentVariable(PlacementProposalDispositionService.RootOverrideVariable, null); if (Directory.Exists(overrideRoot)) Directory.Delete(overrideRoot, true); }
            Assert(PlacementProposalDispositionService.ResolveRoot() == PlacementProposalDispositionService.DefaultRoot, "The disposition-root override must be disabled again after it is cleared.");
            Assert((Directory.Exists(productionRoot) ? Directory.GetDirectories(productionRoot).Length : 0) == productionCases, "The default/production disposition root must be unchanged after the override is cleared.");
        }

        private static PlacementProposal Proposal()
        {
            var proposal = new PlacementProposal
            {
                ProposalId = "P11.1-PROPOSAL-001", PlacementId = "placement:equipment:TEST-01:primary",
                EquipmentEntityId = "equipment:TEST-01", NormalizedTag = "TEST-01", SourceDrawingSha256 = Sha,
                InsertionUnits = "Millimeters", GeneratorIdentity = "EngiSentry.PlacementProposalGenerator/1",
                OriginalEasting = 100d, OriginalNorthing = 200d, ProposedEasting = 120d, ProposedNorthing = 240d,
                EngineeringReviewRequired = true, EngineeringReviewReason = "TEST_ONLY",
                KnownLimitations = new List<string> { ProposalKnownLimitations.PointBasedLimitation },
                Assumptions = new List<string>(ProposalAssumptions.Defaults), ClauseEvidence = new List<StandardClauseCitation>()
            };
            proposal.ProposalFingerprint = PlacementProposalFingerprint.Compute(proposal);
            return proposal;
        }

        private static PlacementProposalSelectedPlacement Original(PlacementProposal proposal) => Selected(proposal, 120m, 240m, EquipmentPlacementResolutionSources.ProposalApproval);

        private static PlacementProposalSelectedPlacement Selected(PlacementProposal proposal, decimal easting, decimal northing, string source)
        {
            var selected = new PlacementProposalSelectedPlacement
            {
                EquipmentEntityId = proposal.EquipmentEntityId!, NormalizedTag = proposal.NormalizedTag!,
                Easting = easting, Northing = northing, InsertionUnits = proposal.InsertionUnits!, Source = source,
                PickedPointX = (double)easting, PickedPointY = (double)northing, PickedPointZ = 0
            };
            selected.PlacementFingerprint = PlacementProposalDispositionService.ComputeSelectedPlacementFingerprint(selected);
            return selected;
        }

        private static PlacementProposalDispositionRecord Decide(PlacementProposal proposal, PlacementProposalDispositionService service, ProjectEngineerReviewService reviews, string decision, PlacementProposalSelectedPlacement? selected, string? previousId, int minute)
        {
            ProjectEngineerReviewRequest request = reviews.CreateRequest("P11.1-PLACEMENT-PROPOSAL", Drawing, Sha, "P11.1-" + proposal.ProposalId, "TEST_ONLY proposal disposition.", new[]
            {
                Evidence("PlacementProposalId", proposal.ProposalId), Evidence("PlacementProposalFingerprint", proposal.ProposalFingerprint!), Evidence("EngineerDisposition", decision)
            }, "TEST_ONLY", "ENGINEER_CONFIRMATION", EquipmentPlacementResolutionQuestions.All);
            var response = new ProjectEngineerReviewResponse
            {
                ReviewRequestId = request.ReviewRequestId, RequestSha256 = request.RequestSha256, DrawingName = request.DrawingName, ReviewedCaseId = request.ReviewedCaseId,
                ReviewerDisplayName = Engineer, ReviewerRole = Role, ResponseTimestampUtc = Utc(minute),
                Answers = EquipmentPlacementResolutionQuestions.All.Select(q => new ProjectEngineerReviewAnswer { QuestionId = q.QuestionId, AnswerText = q.AnswerKind == ProjectEngineerAnswerKinds.FreeText ? "test-only" : "Yes" }).ToList(),
                EngineerComment = "TEST_ONLY", Classification = ProjectEngineerReviewClassifications.Pass
            };
            reviews.SaveResponse(request, response);
            return service.Record(proposal, proposal.ProposalFingerprint!, proposal.ProposalFingerprint!, decision, selected, "TEST_ONLY reason", Engineer, Role, previousId, request, response, Utc(minute));
        }

        private static EquipmentPlacementResolutionRecord BuildResolution(PlacementProposal proposal, PlacementProposalDispositionRecord disposition, ProjectEngineerReviewService reviews, string decision, int minute)
        {
            ProjectEngineerReviewRequest request = LoadRequest(reviews, disposition);
            ProjectEngineerReviewResponse response = LoadResponse(reviews, disposition);
            PlacementProposalSelectedPlacement selected = disposition.SelectedOrModifiedPlacement!;
            var record = EquipmentPlacementResolutionBuilder.Build(Drawing, Sha, selected.EquipmentEntityId, selected.NormalizedTag, selected.Easting, selected.Northing,
                selected.InsertionUnits, selected.Source, selected.SourceHandle, selected.SourceLayer, selected.PickedPointX, selected.PickedPointY, selected.PickedPointZ,
                request, response, Utc(minute), proposal.ProposalId, proposal.ProposalFingerprint, disposition.RecordId, selected.PlacementFingerprint);
            PlacementProposalDispositionService.BindResolutionToDisposition(record, disposition);
            return record;
        }

        private static ProjectEngineerReviewRequest LoadRequest(ProjectEngineerReviewService reviews, PlacementProposalDispositionRecord record) => reviews.LoadRequest(record.ReviewRequestId)!;
        private static ProjectEngineerReviewResponse LoadResponse(ProjectEngineerReviewService reviews, PlacementProposalDispositionRecord record) => reviews.LoadResponse(record.ReviewRequestId)!;
        private static ProjectEngineerReviewEvidenceReference Evidence(string kind, string value) => new ProjectEngineerReviewEvidenceReference { Kind = kind, Label = kind, Value = value };
        private static DateTime Utc(int minute) => new DateTime(2026, 9, 26, 10, minute, 0, DateTimeKind.Utc);
        private static void Assert(bool condition, string message) { assertions++; if (!condition) throw new InvalidOperationException("P11.1 assertion failed: " + message); }
    }
}
