using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.Tests
{
    internal static class PhaseHApprovalQuarantineTests
    {
        private const string Project = @"C:\PhaseHCProject";
        private const string Road = "candidate.processing-equipment.access-road-edge.3m";
        private const string Fence = "candidate.equipment.security-fence.30m";

        internal static void Run()
        {
            string root = Path.Combine(@"C:\", "EHC-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                Assert(EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(root, "absent"), Path.Combine(root, "q"), Path.Combine(root, "h"), Project, Path.Combine(root, "trust.json")).Status == EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts, "absent incoming root was not a no-op");
                Directory.CreateDirectory(Path.Combine(root, "incoming", "empty"));
                Assert(EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(root, "incoming"), Path.Combine(root, "q"), Path.Combine(root, "h"), Project, Path.Combine(root, "trust.json")).Status == EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts, "empty package was not a no-op");
                AssertScenario(root, "unknown", new string[0], new string[0], EngineeringDistanceApprovalQuarantineStates.Rejected, false, false);
                AssertScenario(root, "road-only", new[] { Road }, new[] { Road }, EngineeringDistanceApprovalQuarantineStates.Quarantined, true, false);
                AssertScenario(root, "both-ready", new[] { Road, Fence }, new[] { Road, Fence }, EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation, true, true);
                AssertScenario(root, "fence-only", new[] { Fence }, new[] { Fence }, EngineeringDistanceApprovalQuarantineStates.Quarantined, false, true);
                AssertScenario(root, "untrusted", new[] { Road }, new[] { Road }, EngineeringDistanceApprovalQuarantineStates.PendingTrust, false, false, false);
                AssertScenario(root, "policy-wrong-approval", new[] { Road }, new[] { Road }, EngineeringDistanceApprovalQuarantineStates.Rejected, false, false, true, true);
                AssertForbiddenMatrix(root); AssertNestedRejected(root); AssertVerifierRevalidates(root);
                Assert(!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngiSentry")), "H-C tests touched ProgramData");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void AssertScenario(string root, string name, string[] approvalCandidates, string[] policyCandidates, string expected, bool roadReady, bool fenceReady, bool trusted = true, bool wrongApproval = false)
        {
            string scenario = Path.Combine(root, name); Directory.CreateDirectory(scenario); EngineeringDistanceApprovalHandoffPackage handoff = BuildHandoff(Path.Combine(scenario, "handoff"));
            string incomingPackage = Path.Combine(scenario, "incoming", handoff.PackageId); Directory.CreateDirectory(incomingPackage); var records = new Dictionary<string, EngineeringDistanceApprovalRecord>(StringComparer.Ordinal); using (RSA signer = RSA.Create(2048))
            {
                foreach (string candidateId in approvalCandidates) { EngineeringDistanceApprovalRecord record = Approval(candidateId); WriteApproval(incomingPackage, candidateId == Road ? "road" : "fence", record); records[candidateId] = record; }
                foreach (string candidateId in policyCandidates) { EngineeringDistanceApprovalRecord record = records[candidateId]; if (wrongApproval) record = Approval(candidateId, "different-reference"); WritePolicy(incomingPackage, Policy(record, signer)); }
                if (policyCandidates.Length != 0 && trusted) WriteTrust(Path.Combine(scenario, "trust.json"), signer);
            }
            if (approvalCandidates.Length == 0) File.WriteAllText(Path.Combine(incomingPackage, "unknown.txt"), "unknown");
            EngineeringDistanceApprovalQuarantineResult result = EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(scenario, "incoming"), Path.Combine(scenario, "quarantine"), Path.Combine(scenario, "handoff"), Project, Path.Combine(scenario, "trust.json"));
            Assert(result.Status == expected, name + " overall state mismatch: " + result.Status + " | " + string.Join(" || ", result.Diagnostics)); if (roadReady) Assert(result.Road.CandidateState == EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation, name + " Road projection missing"); if (fenceReady) Assert(result.Fence.CandidateState == EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation, name + " Fence projection missing");
        }

        private static void AssertForbiddenMatrix(string root)
        {
            foreach (string name in new[] { "trusted-issuers.json", "x.pfx", "x.p12", "x.pem", "x.key", "x.exe", "x.dll", "x.ps1", "x.bat", "x.cmd", "x.vbs", "x.js", "x.zip", "x.7z", "x.rar", "x.unknown" })
            {
                string scenario = Path.Combine(root, "forbidden-" + name.Replace('.', '_')); Directory.CreateDirectory(Path.Combine(scenario, "incoming", "pkg")); File.WriteAllText(Path.Combine(scenario, "incoming", "pkg", name), "blocked"); EngineeringDistanceApprovalQuarantineResult result = EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(scenario, "incoming"), Path.Combine(scenario, "quarantine"), Path.Combine(scenario, "handoff"), Project, Path.Combine(scenario, "trust.json")); Assert(result.Status == EngineeringDistanceApprovalQuarantineStates.Rejected, "forbidden/unknown file was accepted: " + name);
            }
        }

        private static void AssertNestedRejected(string root)
        {
            string scenario = Path.Combine(root, "nested"); Directory.CreateDirectory(Path.Combine(scenario, "incoming", "pkg", "nested")); File.WriteAllText(Path.Combine(scenario, "incoming", "pkg", "nested", "x.json"), "nested"); EngineeringDistanceApprovalQuarantineResult result = EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(scenario, "incoming"), Path.Combine(scenario, "quarantine"), Path.Combine(scenario, "handoff"), Project, Path.Combine(scenario, "trust.json")); Assert(result.Status == EngineeringDistanceApprovalQuarantineStates.Rejected && result.Diagnostics.Any(x => x.IndexOf("nested", StringComparison.OrdinalIgnoreCase) >= 0), "nested returned directory was not rejected");
        }

        private static void AssertVerifierRevalidates(string root)
        {
            string scenario = Path.Combine(root, "verifier"); Directory.CreateDirectory(scenario); EngineeringDistanceApprovalHandoffPackage handoff = BuildHandoff(Path.Combine(scenario, "handoff")); string incoming = Path.Combine(scenario, "incoming", handoff.PackageId); Directory.CreateDirectory(incoming); EngineeringDistanceApprovalRecord record = Approval(Road); WriteApproval(incoming, "road", record); using (RSA signer = RSA.Create(2048)) { WritePolicy(incoming, Policy(record, signer)); WriteTrust(Path.Combine(scenario, "trust.json"), signer); }
            EngineeringDistanceApprovalQuarantineResult intake = EngineeringDistanceApprovalQuarantineService.Intake(Path.Combine(scenario, "incoming"), Path.Combine(scenario, "quarantine"), Path.Combine(scenario, "handoff"), Project, Path.Combine(scenario, "trust.json")); string manifestPath = Path.Combine(intake.QuarantineDirectory, EngineeringDistanceApprovalQuarantineService.ManifestFileName); EngineeringDistanceApprovalQuarantineManifest manifest = Read<EngineeringDistanceApprovalQuarantineManifest>(manifestPath); manifest.OverallStatus = EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation; manifest.QuarantineId = EngineeringDistanceApprovalQuarantineService.ComputeQuarantineId(manifest); Write(manifestPath, manifest); Assert(EngineeringDistanceApprovalQuarantineVerifier.Verify(intake.QuarantineDirectory, Path.Combine(scenario, "trust.json")).Status == EngineeringDistanceApprovalQuarantineStates.Rejected, "verifier trusted tampered stored validation");
        }

        private static EngineeringDistanceApprovalHandoffPackage BuildHandoff(string root)
        {
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create(); EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == Road); EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == Fence); DateTime timestamp = new DateTime(2026, 9, 21, 10, 0, 0, 123, DateTimeKind.Utc); ProjectDistanceCriteria criteria = new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = Project, AccessRoadEdgeMinimumMeters = 3m, SecurityFenceMinimumMeters = 30m, UnitBoundaryMinimumMeters = 15m }; var readiness = EngineeringDistanceApprovalReadinessService.Inspect(criteria, Path.Combine(root, "approvals"), Path.Combine(root, "trust.json"), Path.Combine(root, "policies")); return EngineeringDistanceApprovalHandoffPackageBuilder.Write(root, Project, "sample.dwg", @"C:\PhaseHCProject\sample.dwg", timestamp, EngineeringDistanceApprovalPreparation.BuildApprovalRequest(Project, road, context.ClauseRegistry, context.SourceRegistry, "sample.dwg", timestamp), EngineeringDistanceApprovalPreparation.BuildPolicyRequest(Project, road), EngineeringDistanceApprovalPreparation.BuildApprovalRequest(Project, fence, context.ClauseRegistry, context.SourceRegistry, "sample.dwg", timestamp), EngineeringDistanceApprovalPreparation.BuildPolicyRequest(Project, fence), readiness);
        }

        private static EngineeringDistanceApprovalRecord Approval(string candidateId, string? reference = null)
        { ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create(); EngineeringDistanceCandidateDefinition candidate = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == candidateId); return EngineeringDistanceApprovalRecordBuilder.Create(Project, candidate, context.ClauseRegistry, context.SourceRegistry, new EngineeringDistanceApprovalDecision { CandidateId = candidateId, DecisionStatus = "APPROVED", ApprovalReference = reference ?? ("APPROVED-" + candidateId) }, "H-C Test Approver", null, new DateTime(2026, 9, 21, 11, 0, 0, DateTimeKind.Utc)); }
        private static EngineeringDistanceApprovalAuthorityPolicyArtifact Policy(EngineeringDistanceApprovalRecord record, RSA signer)
        { string approvalReference = record.Decision.ApprovalReference ?? throw new InvalidOperationException("H-C policy test requires an approval reference."); var artifact = new EngineeringDistanceApprovalAuthorityPolicyArtifact { SchemaVersion = EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.SchemaVersion, IssuerId = "issuer.hc", KeyId = "key.hc", IssuedAtUtc = new DateTime(2026, 9, 21, 11, 1, 0, DateTimeKind.Utc), SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm, Policy = new EngineeringDistanceApprovalAuthorityPolicyDefinition { PolicyId = "policy-" + record.CandidateBinding.CandidateId, ProjectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(Project), CandidateId = record.CandidateBinding.CandidateId, ApprovalRecordId = record.ApprovalRecordId, ApprovalReference = approvalReference, AuthorityBasisReference = "H-C-TEST", EnforcementMode = StandardEnforcementModes.Deterministic, LegacyConflictDisposition = EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision, Rationale = "H-C focused test" } }; artifact.PolicyArtifactId = EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact); artifact.SignatureBase64 = Convert.ToBase64String(signer.SignData(EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)); return artifact; }
        private static void WriteApproval(string directory, string label, EngineeringDistanceApprovalRecord record) { using (var stream = new FileStream(Path.Combine(directory, label + ".engineering-distance.approval.json"), FileMode.CreateNew)) new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalRecord)).WriteObject(stream, record); }
        private static void WritePolicy(string directory, EngineeringDistanceApprovalAuthorityPolicyArtifact artifact) { string label = artifact.Policy!.CandidateId == Road ? "road" : "fence"; using (var stream = new FileStream(Path.Combine(directory, label + ".engineering-distance.authority-policy.json"), FileMode.CreateNew)) new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalAuthorityPolicyArtifact)).WriteObject(stream, artifact); }
        private static void WriteTrust(string path, RSA signer) { RSAParameters p = signer.ExportParameters(false); var document = new EngineeringDistanceApprovalAuthorityTrustStoreDocument { SchemaVersion = EngineeringDistanceApprovalAuthorityTrustStore.SchemaVersion, TrustedKeys = new List<EngineeringDistanceApprovalAuthorityTrustedKeyDefinition> { new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition { IssuerId = "issuer.hc", KeyId = "key.hc", SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm, RsaModulusBase64 = Convert.ToBase64String(p.Modulus), RsaExponentBase64 = Convert.ToBase64String(p.Exponent) } } }; Write(path, document); }
        private static void Write<T>(string path, T value) { using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); }
        private static T Read<T>(string path) { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
