using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PhaseHApprovalHandoffTests
    {
        internal static void Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "EngiSentry-PhaseHB-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                EngineeringDistanceApprovalHandoffPackage first = Build(Path.Combine(root, "a"), false);
                EngineeringDistanceApprovalHandoffPackage second = Build(Path.Combine(root, "b"), true);
                Assert(first.PackageId == second.PackageId, "H-B package ID was not deterministic.");
                Assert(first.Manifest.Files.Count == 6, "H-B package payload inventory is not exact.");
                Assert(File.Exists(Path.Combine(first.DirectoryPath, EngineeringDistanceApprovalHandoffPackageBuilder.ReadinessFileName)), "Readiness snapshot is missing.");
                Assert(File.Exists(Path.Combine(first.DirectoryPath, EngineeringDistanceApprovalHandoffPackageBuilder.InstructionsFileName)), "Approver instructions are missing.");
                EngineeringDistanceApprovalHandoffVerificationResult initialVerification = EngineeringDistanceApprovalHandoffVerifier.Verify(first.DirectoryPath);
                Assert(initialVerification.Status == "VALID_HANDOFF_PACKAGE", "H-B package did not verify: " + string.Join(" | ", initialVerification.Diagnostics));
                Assert(first.Manifest.Safety.IsApproval == false && first.Manifest.Safety.GrantsAuthority == false && first.Manifest.Road.AutoExecutionAllowed == false && first.Manifest.Fence.AutoExecutionAllowed == false, "H-B package carries authority state.");
                Assert(first.Manifest.Road.CandidateId != "UnitBoundary" && first.Manifest.Fence.CandidateId != "UnitBoundary", "UnitBoundary entered H-B.");

                TamperAndExpect(first.DirectoryPath, first.Manifest.Files.Single(x => x.RelativePath.StartsWith("road.", StringComparison.Ordinal) && x.RelativePath.EndsWith("approval-request.json", StringComparison.Ordinal)).RelativePath, "HASH_MISMATCH");
                first = Build(Path.Combine(root, "c"), false);
                TamperAndExpect(first.DirectoryPath, first.Manifest.Files.Single(x => x.RelativePath.StartsWith("road.", StringComparison.Ordinal) && x.RelativePath.EndsWith("authority-policy-request.json", StringComparison.Ordinal)).RelativePath, "HASH_MISMATCH");
                first = Build(Path.Combine(root, "d"), false);
                TamperAndExpect(first.DirectoryPath, EngineeringDistanceApprovalHandoffPackageBuilder.ReadinessFileName, "HASH_MISMATCH");
                first = Build(Path.Combine(root, "e"), false);
                File.Delete(Path.Combine(first.DirectoryPath, EngineeringDistanceApprovalHandoffPackageBuilder.InstructionsFileName));
                Assert(EngineeringDistanceApprovalHandoffVerifier.Verify(first.DirectoryPath).Status == "MISSING_FILE", "Missing handoff file was not rejected.");
                first = Build(Path.Combine(root, "f"), false);
                File.WriteAllText(Path.Combine(first.DirectoryPath, "unexpected.txt"), "unexpected");
                Assert(EngineeringDistanceApprovalHandoffVerifier.Verify(first.DirectoryPath).Status == "UNEXPECTED_FILE", "Unexpected handoff file was not rejected.");
                foreach (string name in new[] { "bad.engineering-distance.approval.json", "bad.engineering-distance.authority-policy.json", "trusted-issuers.json", "private.key" })
                {
                    first = Build(Path.Combine(root, Guid.NewGuid().ToString("N")), false);
                    File.WriteAllText(Path.Combine(first.DirectoryPath, name), "forbidden");
                    Assert(EngineeringDistanceApprovalHandoffVerifier.Verify(first.DirectoryPath).Status == "AUTHORITY_MATERIAL_PRESENT", "Authority material was accepted: " + name);
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static EngineeringDistanceApprovalHandoffPackage Build(string root, bool reversed)
        {
            string project = @"C:\PhaseHBProject";
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            var candidates = EngineeringDistanceCandidateCatalog.BuiltInCandidates.OrderBy(x => x.CandidateId, StringComparer.Ordinal).ToList();
            if (reversed) candidates.Reverse();
            EngineeringDistanceCandidateDefinition road = candidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
            EngineeringDistanceCandidateDefinition fence = candidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence);
            ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = project, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m };
            IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness = EngineeringDistanceApprovalReadinessService.Inspect(criteria, Path.Combine(root, "approvals"), Path.Combine(root, "trust.json"), Path.Combine(root, "policies"));
            DateTime timestamp = new DateTime(2026, 9, 21, 10, 0, 0, 123, DateTimeKind.Utc);
            return EngineeringDistanceApprovalHandoffPackageBuilder.Write(Path.Combine(root, "handoff"), project, "sample.dwg", @"C:\PhaseHBProject\sample.dwg", timestamp,
                EngineeringDistanceApprovalPreparation.BuildApprovalRequest(project, road, context.ClauseRegistry, context.SourceRegistry, "sample.dwg", timestamp),
                EngineeringDistanceApprovalPreparation.BuildPolicyRequest(project, road),
                EngineeringDistanceApprovalPreparation.BuildApprovalRequest(project, fence, context.ClauseRegistry, context.SourceRegistry, "sample.dwg", timestamp),
                EngineeringDistanceApprovalPreparation.BuildPolicyRequest(project, fence), readiness);
        }

        private static void TamperAndExpect(string directory, string relativePath, string status)
        {
            File.AppendAllText(Path.Combine(directory, relativePath), "tampered");
            Assert(EngineeringDistanceApprovalHandoffVerifier.Verify(directory).Status == status, "Tampered handoff file was not rejected: " + relativePath);
        }

        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
