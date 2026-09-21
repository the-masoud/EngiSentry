using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalHandoffPackage
    {
        public string PackageId { get; internal set; } = string.Empty;
        public string DirectoryPath { get; internal set; } = string.Empty;
        public EngineeringDistanceApprovalHandoffManifest Manifest { get; internal set; } = new EngineeringDistanceApprovalHandoffManifest();
    }

    public static class EngineeringDistanceApprovalHandoffPackageBuilder
    {
        public const string ManifestFileName = "approval-handoff.manifest.json";
        public const string ReadinessFileName = "approval-readiness.json";
        public const string InstructionsFileName = "APPROVER_INSTRUCTIONS.md";

        public static EngineeringDistanceApprovalHandoffPackage Write(
            string packageRoot,
            string projectDirectory,
            string drawingName,
            string? drawingIdentity,
            DateTime createdAtUtc,
            EngineeringDistanceApprovalRequest roadApproval,
            EngineeringDistanceAuthorityPolicyRequest roadPolicy,
            EngineeringDistanceApprovalRequest fenceApproval,
            EngineeringDistanceAuthorityPolicyRequest fencePolicy,
            IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness)
        {
            if (roadApproval == null || roadPolicy == null || fenceApproval == null || fencePolicy == null || readiness == null) throw new ArgumentNullException();
            if (!string.Equals(roadApproval.CandidateId, "candidate.processing-equipment.access-road-edge.3m", StringComparison.Ordinal) || !string.Equals(fenceApproval.CandidateId, "candidate.equipment.security-fence.30m", StringComparison.Ordinal)) throw new InvalidDataException("H-B package is bounded to the Road and Fence candidates.");
            if (readiness.Count != 2) throw new InvalidDataException("H-B package requires exactly two readiness items.");

            string project = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);
            string roadApprovalName = FileName("road", ".engineering-distance.approval-request.json");
            string roadPolicyName = FileName("road", ".engineering-distance.authority-policy-request.json");
            string fenceApprovalName = FileName("fence", ".engineering-distance.approval-request.json");
            string fencePolicyName = FileName("fence", ".engineering-distance.authority-policy-request.json");
            var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                { roadApprovalName, Json(roadApproval) },
                { roadPolicyName, Json(roadPolicy) },
                { fenceApprovalName, Json(fenceApproval) },
                { fencePolicyName, Json(fencePolicy) },
                { InstructionsFileName, Encoding.UTF8.GetBytes(Instructions()) }
            };

            var readinessSnapshot = new EngineeringDistanceApprovalHandoffReadinessSnapshot
            {
                Candidates = readiness.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(x => x.CandidateId == roadApproval.CandidateId ? ToHandoffCandidate(x, roadApproval) : x.CandidateId == fenceApproval.CandidateId ? ToHandoffCandidate(x, fenceApproval) : ToHandoffCandidate(x)).ToList()
            };
            payloads.Add(ReadinessFileName, Json(readinessSnapshot));

            var manifest = new EngineeringDistanceApprovalHandoffManifest
            {
                ProjectDirectory = project,
                DrawingName = drawingName ?? string.Empty,
                DrawingIdentity = drawingIdentity,
                CreatedAtUtc = Normalize(createdAtUtc),
                RoadCandidateId = roadApproval.CandidateId,
                FenceCandidateId = fenceApproval.CandidateId,
                RoadApprovalRequestId = roadApproval.RequestId,
                FenceApprovalRequestId = fenceApproval.RequestId,
                RoadPolicyRequestId = roadPolicy.RequestId,
                FencePolicyRequestId = fencePolicy.RequestId,
                Road = ToHandoffCandidate(readiness.Single(x => x.CandidateId == roadApproval.CandidateId), roadApproval),
                Fence = ToHandoffCandidate(readiness.Single(x => x.CandidateId == fenceApproval.CandidateId), fenceApproval),
                Safety = new EngineeringDistanceApprovalHandoffSafetyFlags()
            };
            manifest.Files = payloads.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new EngineeringDistanceApprovalHandoffFile { RelativePath = x.Key, Length = x.Value.LongLength, Sha256 = Sha256(x.Value) }).ToList();
            manifest.PackageId = ComputePackageId(manifest);

            string directory = Path.Combine(Path.GetFullPath(packageRoot), manifest.PackageId);
            Directory.CreateDirectory(directory);
            foreach (KeyValuePair<string, byte[]> payload in payloads.OrderBy(x => x.Key, StringComparer.Ordinal)) WriteCreateNew(Path.Combine(directory, payload.Key), payload.Value);
            WriteCreateNew(Path.Combine(directory, ManifestFileName), Json(manifest));
            return new EngineeringDistanceApprovalHandoffPackage { PackageId = manifest.PackageId, DirectoryPath = directory, Manifest = manifest };
        }

        public static string ComputePackageId(EngineeringDistanceApprovalHandoffManifest manifest)
        {
            EngineeringDistanceApprovalHandoffManifest copy = Copy(manifest);
            copy.PackageId = string.Empty;
            return Sha256(Json(copy));
        }

        internal static EngineeringDistanceApprovalHandoffCandidate ToHandoffCandidate(EngineeringDistanceApprovalReadinessItem item, EngineeringDistanceApprovalRequest? request = null)
        {
            return new EngineeringDistanceApprovalHandoffCandidate
            {
                CandidateId = item.CandidateId,
                RuleId = item.RuleId,
                CandidateValueMeters = item.CandidateValueMeters,
                ConfiguredCriterionMeters = item.ConfiguredCriterionMeters,
                CandidateDefinitionSha256 = item.CandidateDefinitionSha256,
                ClauseIds = item.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                SourceIds = request == null ? new List<string>() : request.EvidenceBindings.OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(x => x.SourceId).ToList(),
                SourceSha256 = request == null ? new List<string>() : request.EvidenceBindings.OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(x => x.SourceSha256).ToList(),
                ApprovalArtifactCount = item.ApprovalArtifactCount,
                ActiveApprovalRecordId = item.ActiveApprovalRecordId,
                ApprovalCurrentness = item.ApprovalCurrentness,
                TrustedIssuerPresent = item.TrustedIssuerPresent,
                VerifiedAuthorityPolicyPresent = item.VerifiedAuthorityPolicyPresent,
                GovernanceState = item.GovernanceState,
                AutoExecutionAllowed = item.AutoExecutionAllowed,
                BlockingReasons = item.BlockingReasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
        }

        private static EngineeringDistanceApprovalHandoffManifest Copy(EngineeringDistanceApprovalHandoffManifest x)
        {
            return new EngineeringDistanceApprovalHandoffManifest { SchemaVersion = x.SchemaVersion, PackageId = x.PackageId, ProjectDirectory = x.ProjectDirectory, DrawingName = x.DrawingName, DrawingIdentity = x.DrawingIdentity, CreatedAtUtc = x.CreatedAtUtc, RoadCandidateId = x.RoadCandidateId, FenceCandidateId = x.FenceCandidateId, RoadApprovalRequestId = x.RoadApprovalRequestId, FenceApprovalRequestId = x.FenceApprovalRequestId, RoadPolicyRequestId = x.RoadPolicyRequestId, FencePolicyRequestId = x.FencePolicyRequestId, Road = x.Road, Fence = x.Fence, Files = x.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList(), Safety = x.Safety };
        }

        internal static byte[] Json(object value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return stream.ToArray(); } }
        internal static string Sha256(byte[] value) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(value)).Replace("-", string.Empty); }
        private static void WriteCreateNew(string path, byte[] bytes) { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length); }
        private static string FileName(string label, string suffix) { return label + suffix; }
        private static DateTime Normalize(DateTime value) { DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(); return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc); }
        private static string Instructions() => "# EngiSentry External Approval Handoff\r\n\r\nThis package is a REQUEST only. It grants no authority and does not approve, sign, install, trust, or activate any rule.\r\n\r\n- Road request: 3 m; candidate `candidate.processing-equipment.access-road-edge.3m`; rule `std.plotplan.processing-equipment.access-road-edge.minimum-clearance`.\r\n- Fence request: 30 m; candidate `candidate.equipment.security-fence.30m`; rule `std.plotplan.equipment.security-fence.minimum-clearance`.\r\n- Check the exact candidate, rule, clause, source, source-hash, and candidate-definition identities before acting. Do not edit request files.\r\n- Create a real approval through the existing P9 approval contract. Signing must occur externally. Never return a private key in this package.\r\n- Validate returned artifacts before any authorized installation. `REVIEW_ACCEPTED` is not equivalent to `APPROVED`.\r\n- IPS E-PR-190 remains legacy evidence and cannot become governing authority from this package.\r\n- Returning artifacts never automatically activates a rule.\r\n";
    }

    public static class EngineeringDistanceApprovalHandoffVerifier
    {
        public static EngineeringDistanceApprovalHandoffVerificationResult Verify(string directory)
        {
            var result = new EngineeringDistanceApprovalHandoffVerificationResult { Status = "VALID_HANDOFF_PACKAGE" };
            if (!Directory.Exists(directory)) return Fail(result, "MISSING_FILE", "Package directory is missing.");
            string manifestPath = Path.Combine(directory, EngineeringDistanceApprovalHandoffPackageBuilder.ManifestFileName);
            if (!File.Exists(manifestPath)) return Fail(result, "MISSING_FILE", "Manifest is missing.");
            EngineeringDistanceApprovalHandoffManifest manifest;
            try { manifest = Read<EngineeringDistanceApprovalHandoffManifest>(manifestPath); } catch (Exception e) { return Fail(result, "REQUEST_INVALID", e.Message); }
            if (manifest.SchemaVersion != "1.0" || manifest.PackageId != EngineeringDistanceApprovalHandoffPackageBuilder.ComputePackageId(manifest)) return Fail(result, "REQUEST_INVALID", "Manifest schema or deterministic PackageId is invalid.");
            if (manifest.Safety.IsApproval || manifest.Safety.ContainsSignedAuthorityPolicy || manifest.Safety.ContainsTrustedIssuer || manifest.Safety.GrantsAuthority || manifest.Safety.ActivatesRules) return Fail(result, "AUTHORITY_MATERIAL_PRESENT", "Manifest safety flags are unsafe.");

            var expected = new HashSet<string>(manifest.Files.Select(x => x.RelativePath), StringComparer.Ordinal);
            string[] actual = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x => x != null).Select(x => x!).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            foreach (string file in actual) if (file != EngineeringDistanceApprovalHandoffPackageBuilder.ManifestFileName && !expected.Contains(file)) return Fail(result, AuthorityName(file) ? "AUTHORITY_MATERIAL_PRESENT" : "UNEXPECTED_FILE", "Unexpected package file: " + file);
            foreach (EngineeringDistanceApprovalHandoffFile entry in manifest.Files.OrderBy(x => x.RelativePath, StringComparer.Ordinal))
            {
                string path = Path.Combine(directory, entry.RelativePath);
                if (!File.Exists(path)) return Fail(result, "MISSING_FILE", "Missing package file: " + entry.RelativePath);
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.LongLength != entry.Length || !string.Equals(EngineeringDistanceApprovalHandoffPackageBuilder.Sha256(bytes), entry.Sha256, StringComparison.Ordinal)) return Fail(result, "HASH_MISMATCH", "Payload hash mismatch: " + entry.RelativePath);
                if (AuthorityName(entry.RelativePath)) return Fail(result, "AUTHORITY_MATERIAL_PRESENT", "Authority material is present: " + entry.RelativePath);
            }
            result.PayloadHashesValid = true;

            try
            {
                EngineeringDistanceApprovalRequest road = Read<EngineeringDistanceApprovalRequest>(Path.Combine(directory, Find(manifest, ".engineering-distance.approval-request.json", manifest.RoadCandidateId)));
                EngineeringDistanceApprovalRequest fence = Read<EngineeringDistanceApprovalRequest>(Path.Combine(directory, Find(manifest, ".engineering-distance.approval-request.json", manifest.FenceCandidateId)));
                EngineeringDistanceAuthorityPolicyRequest roadPolicy = Read<EngineeringDistanceAuthorityPolicyRequest>(Path.Combine(directory, Find(manifest, ".engineering-distance.authority-policy-request.json", manifest.RoadCandidateId)));
                EngineeringDistanceAuthorityPolicyRequest fencePolicy = Read<EngineeringDistanceAuthorityPolicyRequest>(Path.Combine(directory, Find(manifest, ".engineering-distance.authority-policy-request.json", manifest.FenceCandidateId)));
                EngineeringDistanceApprovalHandoffReadinessSnapshot readiness = Read<EngineeringDistanceApprovalHandoffReadinessSnapshot>(Path.Combine(directory, EngineeringDistanceApprovalHandoffPackageBuilder.ReadinessFileName));
                VerifyRequest(road, manifest, "candidate.processing-equipment.access-road-edge.3m"); VerifyRequest(fence, manifest, "candidate.equipment.security-fence.30m");
                VerifyPolicy(roadPolicy, road); VerifyPolicy(fencePolicy, fence);
                VerifyReadiness(readiness, manifest, road, fence);
            }
            catch (FileNotFoundException e) { return Fail(result, "MISSING_FILE", e.Message); }
            catch (Exception e) { return Fail(result, e is InvalidDataException ? "REQUEST_INVALID" : "READINESS_MISMATCH", e.Message); }
            return result;
        }

        private static void VerifyRequest(EngineeringDistanceApprovalRequest request, EngineeringDistanceApprovalHandoffManifest manifest, string candidateId)
        {
            if (request.CandidateId != candidateId || request.ProjectDirectory != manifest.ProjectDirectory || request.IsApproval || request.GrantsAuthority || request.AutoExecutionAllowed || string.IsNullOrWhiteSpace(request.RequestId)) throw new InvalidDataException("Approval request is invalid: " + candidateId);
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            EngineeringDistanceCandidateDefinition candidate = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == candidateId);
            EngineeringDistanceApprovalRequest expected = EngineeringDistanceApprovalPreparation.BuildApprovalRequest(manifest.ProjectDirectory, candidate, context.ClauseRegistry, context.SourceRegistry, manifest.DrawingName, manifest.CreatedAtUtc);
            if (!string.Equals(request.RequestId, expected.RequestId, StringComparison.Ordinal) || request.CandidateDefinitionSha256 != expected.CandidateDefinitionSha256 || !request.ClauseIds.SequenceEqual(expected.ClauseIds, StringComparer.Ordinal) || request.EvidenceBindings.Count != expected.EvidenceBindings.Count || request.EvidenceBindings.Any(x => !expected.EvidenceBindings.Any(y => x.ClauseId == y.ClauseId && x.SourceId == y.SourceId && x.SourceSha256 == y.SourceSha256))) throw new InvalidDataException("Approval request content is not the deterministic H-A request.");
        }
        private static void VerifyPolicy(EngineeringDistanceAuthorityPolicyRequest policy, EngineeringDistanceApprovalRequest approval)
        {
            EngineeringDistanceAuthorityPolicyRequest expected = EngineeringDistanceApprovalPreparation.BuildPolicyRequest(approval.ProjectDirectory, EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.CandidateId == approval.CandidateId));
            if (policy.RequestId != expected.RequestId || policy.IsSigned || policy.IsTrusted || policy.IsActive || policy.GrantsAuthority) throw new InvalidDataException("Unsigned policy request is invalid: " + approval.CandidateId);
        }
        private static void VerifyReadiness(EngineeringDistanceApprovalHandoffReadinessSnapshot snapshot, EngineeringDistanceApprovalHandoffManifest manifest, EngineeringDistanceApprovalRequest road, EngineeringDistanceApprovalRequest fence)
        {
            if (snapshot.Candidates.Count != 2 || snapshot.Candidates.Any(x => x.AutoExecutionAllowed || x.CandidateId == "UnitBoundary")) throw new InvalidDataException("Readiness snapshot is invalid.");
            foreach (EngineeringDistanceApprovalRequest request in new[] { road, fence }) { EngineeringDistanceApprovalHandoffCandidate item = snapshot.Candidates.Single(x => x.CandidateId == request.CandidateId); if (item.CandidateDefinitionSha256 != request.CandidateDefinitionSha256 || item.ClauseIds.Count != request.ClauseIds.Count || item.SourceIds.Count != request.EvidenceBindings.Count || item.AutoExecutionAllowed) throw new InvalidDataException("Readiness does not match request: " + request.CandidateId); }
            if (!snapshot.Candidates.Any(x => x.CandidateId == manifest.RoadCandidateId) || !snapshot.Candidates.Any(x => x.CandidateId == manifest.FenceCandidateId)) throw new InvalidDataException("Readiness candidates do not match manifest.");
        }
        private static string Find(EngineeringDistanceApprovalHandoffManifest manifest, string suffix, string candidate) { string label = candidate == "candidate.processing-equipment.access-road-edge.3m" ? "road" : candidate == "candidate.equipment.security-fence.30m" ? "fence" : string.Empty; EngineeringDistanceApprovalHandoffFile? file = manifest.Files.SingleOrDefault(x => x.RelativePath == label + suffix); if (file == null) throw new FileNotFoundException("Expected request is missing."); return file.RelativePath; }
        private static bool AuthorityName(string name) { string lower = name.ToLowerInvariant(); return lower.EndsWith(".engineering-distance.approval.json") || lower.EndsWith(".engineering-distance.authority-policy.json") || lower == "trusted-issuers.json" || lower.EndsWith(".pfx") || lower.EndsWith(".p12") || lower.EndsWith(".pem") || lower.EndsWith(".key"); }
        private static T Read<T>(string path) { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
        private static EngineeringDistanceApprovalHandoffVerificationResult Fail(EngineeringDistanceApprovalHandoffVerificationResult result, string status, string message) { result.Status = status; result.Diagnostics.Add(message); result.PayloadHashesValid = false; result.ProductionAuthorityMaterialPresent = status == "AUTHORITY_MATERIAL_PRESENT"; return result; }
    }
}
