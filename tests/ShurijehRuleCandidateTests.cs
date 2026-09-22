using System;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class ShurijehRuleCandidateTests
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void Run()
        {
            ShurijehRuleCandidateCatalog.Validate();
            var all = ShurijehRuleCandidateCatalog.Candidates;

            string[] expectedKinds = {
                ShurijehRuleKinds.WellToRoad2D,
                ShurijehRuleKinds.FlareToRoad2D,
                ShurijehRuleKinds.FireTrainingAreaToFacility2D,
                ShurijehRuleKinds.StorageToBoundaryOrPublicHighway2D,
                ShurijehRuleKinds.DikedAreaRoadAccessTopology,
                ShurijehRuleKinds.RestrictedAreaToSecurityFenceSemantic
            };
            Assert(expectedKinds.All(k => all.Any(x => x.RuleKind == k)), "Not all six Shurijeh rule families are represented.");
            Assert(all.All(x => x.ExecutionState == ShurijehRuleCandidateCatalog.ReviewRequired), "A Shurijeh rule candidate escaped REVIEW_REQUIRED.");
            Assert(all.All(x => x.SourceSha256 == ShurijehRuleCandidateCatalog.SourceSha256), "Source hash binding is inconsistent.");
            Assert(all.All(x => x.ProjectApplicabilityReference == ShurijehRuleCandidateCatalog.ProjectApplicabilityReference), "Project applicability binding is inconsistent.");

            EngineeringRuleCandidateDefinition publicRoad = all.Single(x => x.CandidateId == "shurijeh.well.public-road.500m");
            EngineeringRuleCandidateDefinition privateRoad = all.Single(x => x.CandidateId == "shurijeh.well.private-road.200m");
            Assert(publicRoad.MinimumValue == 500m && publicRoad.Unit == "m" && publicRoad.PdfPageNumber == 27, "Verified well-to-public-road value/provenance changed.");
            Assert(privateRoad.MinimumValue == 200m && privateRoad.Unit == "m" && privateRoad.PdfPageNumber == 27, "Verified well-to-private-road value/provenance changed.");

            Assert(all.Single(x => x.CandidateId == "shurijeh.ground-flare.private-road.150m").MinimumValue == 150m, "Ground-flare/private-road value changed.");
            Assert(all.Single(x => x.CandidateId == "shurijeh.high-level-flare.public-road.150m").MinimumValue == 150m, "High-level-flare/public-road value changed.");
            Assert(all.Single(x => x.CandidateId == "shurijeh.cold-flare.private-road.50m").MinimumValue == 50m, "Cold-flare/private-road value changed.");

            var semantic = all.Single(x => x.RuleKind == ShurijehRuleKinds.RestrictedAreaToSecurityFenceSemantic);
            Assert(!semantic.MinimumValue.HasValue && semantic.ClauseReference == "6.2.1", "Restricted-area/fence semantic candidate became numeric or lost provenance.");

            var topology = all.Single(x => x.RuleKind == ShurijehRuleKinds.DikedAreaRoadAccessTopology);
            Assert(!topology.MinimumValue.HasValue && topology.ApprovalOrDeviationSemantics.Contains("Client"), "Diked-area road topology lost conditional Client approval semantics.");

            Assert(!all.Any(x => x.RuleId.Contains("equipment.access-road") || x.RuleId.Contains("equipment.security-fence")), "Generic equipment-to-road/fence rule was incorrectly introduced.");
            Assert(all.Where(x => x.MinimumValue.HasValue).All(x => x.NormativeStrength == "Mandatory"), "Numeric candidates must remain mandatory source-derived values.");
        }
    }
}
