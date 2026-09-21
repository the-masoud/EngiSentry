using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PhaseHApprovalOnboardingTests
    {
        internal static void Run()
        {
            RequestsAreDeterministicAndBounded();
            RequestArtifactsAreNotProductionArtifacts();
            OfflineValidationFailsClosed();
            ReadinessDoesNotActivateProduction();
        }

        private static void RequestsAreDeterministicAndBounded()
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
            EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence);
            DateTime timestamp = new DateTime(2026, 9, 21, 10, 0, 0, 123, DateTimeKind.Utc);
            EngineeringDistanceApprovalRequest first = EngineeringDistanceApprovalPreparation.BuildApprovalRequest("C:\\PhaseH", road, context.ClauseRegistry, context.SourceRegistry, "drawing.dwg", timestamp);
            EngineeringDistanceApprovalRequest second = EngineeringDistanceApprovalPreparation.BuildApprovalRequest("C:\\PhaseH", road, context.ClauseRegistry, context.SourceRegistry, "drawing.dwg", timestamp);
            Assert(first.RequestId == second.RequestId && Json(first) == Json(second), "Road approval request is not deterministic.");
            EngineeringDistanceApprovalRequest fenceRequest = EngineeringDistanceApprovalPreparation.BuildApprovalRequest("C:\\PhaseH", fence, context.ClauseRegistry, context.SourceRegistry, "drawing.dwg", timestamp);
            Assert(first.RequestId != fenceRequest.RequestId, "Road and Fence request identities collided.");
            Assert(first.CandidateDefinitionSha256 == EngineeringDistanceApprovalPreparation.CandidateHash(road), "Road candidate hash does not use the accepted P9 computation.");
            Assert(!first.IsApproval && !first.GrantsAuthority && !first.AutoExecutionAllowed, "Approval request carries authority or execution state.");
            Assert(first.ClauseIds.SequenceEqual(road.ClauseIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal), "Road clause ordering changed.");
            Assert(first.EvidenceBindings.Select(x => x.ClauseId).SequenceEqual(first.ClauseIds, StringComparer.Ordinal) && first.EvidenceBindings.All(x => !string.IsNullOrWhiteSpace(x.SourceSha256)), "Road source evidence is incomplete.");
            EngineeringDistanceAuthorityPolicyRequest policy = EngineeringDistanceApprovalPreparation.BuildPolicyRequest("C:\\PhaseH", road);
            Assert(!policy.IsSigned && !policy.IsTrusted && !policy.IsActive && !policy.GrantsAuthority, "Unsigned policy request carries authority state.");
            Assert(EngineeringDistanceCandidateCatalog.BuiltInCandidates.All(x => x.RequiredMeasurementBasisKey != EngineeringDistanceEvidenceKeys.UnitBoundary), "UnitBoundary entered Phase H-A tooling.");
        }

        private static void RequestArtifactsAreNotProductionArtifacts()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PhaseH-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
                EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
                string request = ApprovalPreparationArtifactWriter.WriteApprovalRequest(EngineeringDistanceApprovalPreparation.BuildApprovalRequest(root, road, context.ClauseRegistry, context.SourceRegistry, null, DateTime.UtcNow), root);
                string policy = ApprovalPreparationArtifactWriter.WritePolicyRequest(EngineeringDistanceApprovalPreparation.BuildPolicyRequest(root, road), root);
                Assert(request.EndsWith(".engineering-distance.approval-request.json", StringComparison.Ordinal), "Approval request suffix is incorrect.");
                Assert(policy.EndsWith(".engineering-distance.authority-policy-request.json", StringComparison.Ordinal), "Policy request suffix is incorrect.");
                Assert(EngineeringDistanceApprovalArtifactCatalog.Load(root, root).Count == 0, "Request artifact was loaded as production approval.");
                Assert(EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(root, root, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition[0])).Artifacts.Count == 0, "Unsigned policy request was loaded as production policy.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void OfflineValidationFailsClosed()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PhaseH-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string approval = Path.Combine(root, "bad.engineering-distance.approval.json");
                string policy = Path.Combine(root, "bad.engineering-distance.authority-policy.json");
                File.WriteAllText(approval, "{}");
                File.WriteAllText(policy, "{}");
                EngineeringDistanceOfflineValidationResult result = EngineeringDistanceOfflineValidator.Validate(approval, policy, Path.Combine(root, "missing-trust.json"), root, "candidate.processing-equipment.access-road-edge.3m");
                Assert(result.Status == EngineeringDistanceOfflineValidationStatuses.Invalid, "Malformed offline data was not rejected.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void ReadinessDoesNotActivateProduction()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PhaseH-" + Guid.NewGuid().ToString("N"));
            try
            {
                IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness = EngineeringDistanceApprovalReadinessService.Inspect(new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = root, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m }, Path.Combine(root, "approvals"), Path.Combine(root, "trust.json"), Path.Combine(root, "policies"));
                Assert(readiness.Count == 2 && readiness.All(x => !x.AutoExecutionAllowed && x.GovernanceState == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly), "Readiness inspection activated a production candidate.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static string Json(object value)
        {
            using (var stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return Encoding.UTF8.GetString(stream.ToArray()); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
