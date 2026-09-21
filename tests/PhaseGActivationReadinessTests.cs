using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PhaseGActivationReadinessTests
    {
        internal static void Run()
        {
            CandidateDefinitionsRemainBounded();
            NoApprovalFailsClosed();
            AuthorityPolicySplitRemainsAuthoritative();
        }

        private static void CandidateDefinitionsRemainBounded()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            IReadOnlyList<EngineeringDistanceCandidateDefinition> candidates = EngineeringDistanceCandidateCatalog.BuiltInCandidates;
            Assert(candidates.Count == 2, "Phase G candidate catalog changed bounded Road/Fence scope.");
            EngineeringDistanceCandidateDefinition road = candidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
            EngineeringDistanceCandidateDefinition fence = candidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence);
            Assert(road.RuleId == "std.plotplan.processing-equipment.access-road-edge.minimum-clearance" && road.ProposedMinimumDistanceMeters == 3m && road.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.ProcessingEquipment, "Road candidate definition changed.");
            Assert(fence.RuleId == "std.plotplan.equipment.security-fence.minimum-clearance" && fence.ProposedMinimumDistanceMeters == 30m && fence.RequiredApplicabilityKey == EngineeringDistanceEvidenceKeys.Equipment, "Fence candidate definition changed.");
            Assert(candidates.All(x => x.RequiredMeasurementBasisKey != EngineeringDistanceEvidenceKeys.UnitBoundary), "Phase G introduced a UnitBoundary candidate.");
            Assert(context.ClauseRegistry.TryGet("trc.pcd-pd-135.3.3.9.b", out _) && context.ClauseRegistry.TryGet("ips.e-pr-190.8.2.3", out _), "Road clause evidence changed.");
            Assert(context.ClauseRegistry.TryGet("trc.pcd-pd-135.3.3.10.a", out _) && context.ClauseRegistry.TryGet("ips.e-pr-190.f.11.2", out _), "Fence clause evidence changed.");
        }

        private static void NoApprovalFailsClosed()
        {
            string project = Path.Combine(Path.GetTempPath(), "EngiSentry-PhaseG-" + Guid.NewGuid().ToString("N"));
            try
            {
                ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
                StandardAuthorityPolicyRegistry standardPolicies = ProjectEngineeringDistanceAuthorityPolicyCatalog.Create(context.SourceRegistry, context.ClauseRegistry);
                ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = project, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
                EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog emptyPolicies = EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(project, project, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition[0]));
                ProjectDistanceGovernanceAggregateResult aggregate = ProjectDistanceGovernanceAggregateResolver.Resolve(project, ProjectDistanceRuleBinder.Create(criteria), EngineeringDistanceCandidateCatalog.BuiltInCandidates, new EngineeringDistanceApprovalRecord[0], emptyPolicies, context.ClauseRegistry, context.SourceRegistry, standardPolicies);
                Assert(aggregate.Items.Count == 2, "No-approval readiness did not retain exactly Road and Fence candidates.");
                Assert(aggregate.Items.All(x => x.Resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly && x.Resolution.GovernanceProvenance == null), "No approval artifact was not fail-closed to PROJECT_RULE_ONLY.");
                Assert(aggregate.Items.All(x => x.Resolution.State != ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible), "No approval artifact became governed.");
            }
            finally { if (Directory.Exists(project)) Directory.Delete(project, true); }
        }

        private static void AuthorityPolicySplitRemainsAuthoritative()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            StandardAuthorityPolicyRegistry policies = ProjectEngineeringDistanceAuthorityPolicyCatalog.Create(context.SourceRegistry, context.ClauseRegistry);
            StandardAuthorityPolicyDefinition pcd = policies.Policies.Single(x => x.SourceId == "trc.pcd-pd-135.rev1");
            StandardAuthorityPolicyDefinition ips = policies.Policies.Single(x => x.SourceId == "ips.e-pr-190.rev1.2009");
            Assert(pcd.AuthorityClass == StandardAuthorityClasses.GoverningProject && pcd.EnforcementMode == StandardEnforcementModes.ConditionalReview, "PCD authority policy changed.");
            Assert(ips.AuthorityClass == StandardAuthorityClasses.LegacyConflict && ips.EnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "IPS legacy authority policy changed.");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
