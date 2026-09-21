using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.AutoCAD2021;
using EngiSentry.Contracts;
using EngiSentry.AutoCAD2021.Scanning;

namespace EngiSentry.Tests
{
    internal static class PhaseFPresentationTests
    {
        internal static void Run()
        {
            ZeroProposalSummary();
            ProposalProjection();
            DeterministicOrderingAndSafeMetadata();
        }

        private static void ZeroProposalSummary()
        {
            List<VisualDetail> details = PlacementProposalPresentation.BuildDetails(
                new List<PlacementProposal>(),
                new PlacementProposalRunDiagnostics { GovernedRuleCount = 3, BoundedConstraintCount = 3 },
                "sidecar.json");
            Assert(details.Any(x => x.Label == "Proposal details" && x.Value.Contains("valid governed result") == false), "Zero-proposal presentation lost its explicit result detail.");
            Assert(details.Any(x => x.Label == "Status" && x.Value == "READ-ONLY"), "Zero-proposal presentation lost read-only status.");
            Assert(details.Any(x => x.Label == "Assumptions" && x.Value.Length > 0), "Zero-proposal assumptions are not visible.");
            Assert(details.Any(x => x.Label == "Unchecked constraints" && x.Value.Length > 0), "Zero-proposal unchecked constraints are not visible.");
            Assert(details.Any(x => x.Label == "Known limitations" && x.Value.Length > 0), "Zero-proposal known limitations are not visible.");
        }

        private static void ProposalProjection()
        {
            var proposal = new PlacementProposal
            {
                ProposalId = "proposal:b",
                ViolatedRule = new StandardsPackRule { RuleId = "RULE-2", Company = "ACME", PackName = "Pack", DocumentNumber = "DOC-2", DocumentTitle = "Title", RevisionOrEdition = "Rev 3" },
                ObservedDistanceMeters = 2,
                RequiredDistanceMeters = 5,
                ObservedMeasurementBasis = "SecurityFence",
                UnitContext = "mm (m, 0.001 m per drawing unit)",
                EngineeringReviewRequired = true,
                ClauseEvidence = new List<StandardClauseCitation> { new StandardClauseCitation { ClauseReference = "4.2.1", PageNumber = 17, SourceSha256 = "ABC" } },
                SatisfiedConstraints = new List<PlacementProposalConstraintOutcome> { new PlacementProposalConstraintOutcome { ConstraintId = "A", EvaluationState = ProposalConstraintState.Satisfied } },
                ViolatedConstraints = new List<PlacementProposalConstraintOutcome> { new PlacementProposalConstraintOutcome { ConstraintId = "B", EvaluationState = ProposalConstraintState.Violated } },
                InsufficientEvidenceConstraints = new List<PlacementProposalConstraintOutcome> { new PlacementProposalConstraintOutcome { ConstraintId = "C", EvaluationState = ProposalConstraintState.InsufficientEvidence } },
                RoadReEvaluation = new PlacementProposalConstraintOutcome { ConstraintId = "D", EvaluationState = ProposalConstraintState.ReviewRequired },
                PlantFenceReEvaluation = new PlacementProposalConstraintOutcome { ConstraintId = "E", EvaluationState = ProposalConstraintState.NotApplicable },
                Assumptions = new List<string> { "assumption" },
                UncheckedConstraints = new List<string> { "unchecked" },
                KnownLimitations = new List<string> { "limitation" },
                RejectedCandidates = new List<PlacementProposalCandidate> { new PlacementProposalCandidate { CandidateId = "candidate-1", RejectedReasons = new List<string> { "reason" } } }
            };
            List<VisualDetail> details = PlacementProposalPresentation.BuildDetails(new[] { proposal }, new PlacementProposalRunDiagnostics(), "sidecar.json");
            string text = string.Join("\n", details.Select(x => x.Label + "=" + x.Value));
            foreach (string expected in new[] { "RULE-2", "ACME", "Pack", "DOC-2", "Title", "Rev 3", "4.2.1", "17", "ABC", "SecurityFence", "assumption", "unchecked", "limitation", "candidate-1", "reason", "SATISFIED", "VIOLATED", "INSUFFICIENT_EVIDENCE", "REVIEW_REQUIRED", "NOT_APPLICABLE", "EngineeringReviewRequired" })
                Assert(text.Contains(expected), "Phase F presentation omitted: " + expected);
            Assert(text.Contains("Not available in PlacementProposal projection"), "Unavailable placement identity was not rendered safely.");
        }

        private static void DeterministicOrderingAndSafeMetadata()
        {
            var first = new PlacementProposal { ProposalId = "proposal:b" };
            var second = new PlacementProposal { ProposalId = "proposal:a" };
            List<VisualDetail> details = PlacementProposalPresentation.BuildDetails(new[] { first, second }, new PlacementProposalRunDiagnostics(), "sidecar.json");
            int a = details.FindIndex(x => x.Label == "Proposal proposal:a");
            int b = details.FindIndex(x => x.Label == "Proposal proposal:b");
            Assert(a >= 0 && b > a, "Proposal presentation ordering is not deterministic.");
            Assert(details.Any(x => x.Label == "  RuleId" && x.Value.Contains("Not available")), "Absent metadata was not rendered safely.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
