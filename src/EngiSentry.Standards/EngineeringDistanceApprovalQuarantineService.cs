using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalQuarantineService
    {
        public const string ManifestFileName = "approval-intake.quarantine-manifest.json";
        public const string ApprovalType = "APPROVAL_RECORD";
        public const string PolicyType = "SIGNED_AUTHORITY_POLICY";
        private const string RoadId = "candidate.processing-equipment.access-road-edge.3m";
        private const string FenceId = "candidate.equipment.security-fence.30m";

        public static EngineeringDistanceApprovalQuarantineResult Intake(string incomingRoot, string quarantineRoot, string handoffRoot, string expectedProjectDirectory, string trustStorePath)
        {
            var result = new EngineeringDistanceApprovalQuarantineResult { Road = CandidateResult(RoadId, ""), Fence = CandidateResult(FenceId, "") };
            if (!Directory.Exists(incomingRoot)) return result;
            string packageDirectory = Directory.GetDirectories(incomingRoot, "*", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty;
            if (packageDirectory.Length == 0) return result;
            string packageId = Path.GetFileName(packageDirectory) ?? string.Empty;
            string handoffDirectory = Path.Combine(handoffRoot, packageId);
            string[] returned = Directory.GetFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            result.SourcePackageId = packageId; result.ReturnedFileCount = returned.Length;
            bool nested = Directory.GetDirectories(packageDirectory, "*", SearchOption.TopDirectoryOnly).Length != 0;
            if (returned.Length == 0 && !nested) return result;
            var manifest = new EngineeringDistanceApprovalQuarantineManifest
            {
                SourcePackageId = packageId, SourcePackageDirectory = packageDirectory, HandoffDirectory = handoffDirectory,
                ExpectedProjectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProjectDirectory), IntakeTimestampUtc = Normalize(DateTime.UtcNow)
            };
            EngineeringDistanceApprovalHandoffVerificationResult handoff = EngineeringDistanceApprovalHandoffVerifier.Verify(handoffDirectory);
            manifest.HandoffVerificationStatus = handoff.Status;
            if (handoff.Status != "VALID_HANDOFF_PACKAGE") manifest.Diagnostics.AddRange(handoff.Diagnostics);
            EngineeringDistanceApprovalHandoffManifest handoffManifest = handoff.Status == "VALID_HANDOFF_PACKAGE" ? Read<EngineeringDistanceApprovalHandoffManifest>(Path.Combine(handoffDirectory, EngineeringDistanceApprovalHandoffPackageBuilder.ManifestFileName)) : new EngineeringDistanceApprovalHandoffManifest();
            if (nested) manifest.Diagnostics.Add("nested directories are forbidden in the returned-artifact package");
            EngineeringDistanceApprovalQuarantineEvaluation evaluation = Evaluate(returned, handoffManifest, handoff.Status == "VALID_HANDOFF_PACKAGE" && handoffManifest.PackageId == packageId, expectedProjectDirectory, trustStorePath, manifest.Diagnostics);
            manifest.Files = evaluation.Files; manifest.Road = evaluation.Road; manifest.Fence = evaluation.Fence;
            if (nested) manifest.Diagnostics.Add("returned-artifact package rejected before any installation or activation");
            manifest.OverallStatus = nested ? EngineeringDistanceApprovalQuarantineStates.Rejected : evaluation.OverallStatus;
            manifest.QuarantineId = ComputeQuarantineId(manifest);
            string quarantineDirectory = Path.Combine(Path.GetFullPath(quarantineRoot), manifest.QuarantineId); Directory.CreateDirectory(quarantineDirectory);
            foreach (string path in returned.OrderBy(x => x, StringComparer.Ordinal)) WriteCreateNew(Path.Combine(quarantineDirectory, Path.GetFileName(path)), File.ReadAllBytes(path));
            WriteCreateNew(Path.Combine(quarantineDirectory, ManifestFileName), Json(manifest));
            result.Status = manifest.OverallStatus; result.QuarantineId = manifest.QuarantineId; result.QuarantineDirectory = quarantineDirectory; result.QuarantinedFileCount = manifest.Files.Count;
            result.Files = manifest.Files; result.Road = manifest.Road; result.Fence = manifest.Fence; result.Diagnostics = manifest.Diagnostics.Concat(manifest.Files.SelectMany(x => x.Diagnostics)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return result;
        }

        public static string ComputeQuarantineId(EngineeringDistanceApprovalQuarantineManifest manifest)
        {
            string content = manifest.SourcePackageId + "\u001f" + string.Join("\u001e", manifest.Files.OrderBy(x => x.OriginalFileName, StringComparer.Ordinal).ThenBy(x => x.Sha256, StringComparer.Ordinal).Select(x => x.OriginalFileName + "\u001f" + x.RelativePath + "\u001f" + x.Length.ToString(CultureInfo.InvariantCulture) + "\u001f" + x.Sha256));
            return Sha256(Encoding.UTF8.GetBytes(content));
        }

        internal static EngineeringDistanceApprovalQuarantineEvaluation Evaluate(string[] paths, EngineeringDistanceApprovalHandoffManifest handoff, bool handoffValid, string expectedProject, string trustStorePath, List<string> diagnostics)
        {
            var evaluation = new EngineeringDistanceApprovalQuarantineEvaluation { Road = CandidateResult(RoadId, Rule(handoff, RoadId)), Fence = CandidateResult(FenceId, Rule(handoff, FenceId)) };
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trust;
            try { trust = EngineeringDistanceApprovalAuthorityTrustStore.Load(trustStorePath); }
            catch (Exception exception) { diagnostics.Add(exception.Message); trust = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition[0]); }
            var approvals = new Dictionary<string, EngineeringDistanceApprovalRecord>(StringComparer.Ordinal);
            var policyPaths = new List<Tuple<string, EngineeringDistanceApprovalQuarantineFile>>();
            foreach (string path in paths.OrderBy(x => x, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(path) ?? string.Empty; byte[] bytes = File.ReadAllBytes(path);
                var item = new EngineeringDistanceApprovalQuarantineFile { OriginalFileName = name, RelativePath = name, Length = bytes.LongLength, Sha256 = Sha256(bytes), ArtifactType = Classify(name), ValidationStatus = EngineeringDistanceApprovalQuarantineStates.Rejected };
                try
                {
                    if (!handoffValid) throw new InvalidDataException("H-B package binding is invalid");
                    if (item.ArtifactType == string.Empty) throw new InvalidDataException(Forbidden(name) ? "forbidden returned file" : "unsupported returned file");
                    if (item.ArtifactType == ApprovalType)
                    {
                        EngineeringDistanceApprovalRecord record = ValidateApproval(path, item, handoff, EngineeringDistanceCandidateCatalog.BuiltInCandidates, expectedProject);
                        if (approvals.ContainsKey(record.CandidateBinding.CandidateId)) throw new InvalidDataException("duplicate returned approval for candidate");
                        approvals.Add(record.CandidateBinding.CandidateId, record);
                    }
                    else policyPaths.Add(Tuple.Create(path, item));
                }
                catch (Exception exception) { item.Diagnostics.Add(exception.Message); }
                evaluation.Files.Add(item);
            }
            foreach (Tuple<string, EngineeringDistanceApprovalQuarantineFile> tuple in policyPaths)
            {
                string path = tuple.Item1; EngineeringDistanceApprovalQuarantineFile item = tuple.Item2;
                try
                {
                    EngineeringDistanceApprovalAuthorityPolicyArtifact artifact = Read<EngineeringDistanceApprovalAuthorityPolicyArtifact>(path);
                    bool trusted; EngineeringDistanceApprovalAuthorityPolicyArtifact validated = ValidateStructureAndTrust(artifact, trust, out trusted);
                    EngineeringDistanceApprovalAuthorityPolicyDefinition policy = validated.Policy!; EngineeringDistanceApprovalQuarantineCandidateResult candidateResult = ResultFor(evaluation, policy.CandidateId);
                    candidateResult.PolicyPresent = true; candidateResult.PolicyArtifactId = validated.PolicyArtifactId; candidateResult.TrustStatus = trusted ? "TRUSTED" : "PENDING_TRUST";
                    item.CandidateId = policy.CandidateId; item.PolicyArtifactId = validated.PolicyArtifactId; item.IssuerId = validated.IssuerId; item.KeyId = validated.KeyId;
                    if (!trusted) { item.ValidationStatus = EngineeringDistanceApprovalQuarantineStates.PendingTrust; item.Diagnostics.Add("policy signer is structurally valid but not trusted"); candidateResult.PolicyValidationStatus = EngineeringDistanceApprovalQuarantineStates.PendingTrust; continue; }
                    if (!approvals.TryGetValue(policy.CandidateId, out EngineeringDistanceApprovalRecord? approval)) throw new InvalidDataException("policy has no matching returned approved approval");
                    EngineeringDistanceApprovalHandoffCandidate expected = Candidate(handoff, policy.CandidateId);
                    if (policy.ProjectDirectory != EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProject) || policy.ApprovalRecordId != approval.ApprovalRecordId || policy.ApprovalReference != approval.Decision.ApprovalReference || policy.EnforcementMode != StandardEnforcementModes.Deterministic || expected.RuleId != approval.CandidateBinding.RuleId) throw new InvalidDataException("returned authority policy does not match the returned approved approval");
                    item.ValidationStatus = EngineeringDistanceApprovalQuarantineStates.Quarantined; candidateResult.PolicyValidationStatus = EngineeringDistanceApprovalQuarantineStates.Quarantined;
                }
                catch (Exception exception) { item.Diagnostics.Add(exception.Message); if (item.CandidateId != null) ResultFor(evaluation, item.CandidateId).PolicyValidationStatus = EngineeringDistanceApprovalQuarantineStates.Rejected; }
            }
            foreach (EngineeringDistanceApprovalQuarantineFile item in evaluation.Files.Where(x => x.CandidateId != null && x.ArtifactType == ApprovalType))
            {
                EngineeringDistanceApprovalQuarantineCandidateResult candidateResult = ResultFor(evaluation, item.CandidateId!); candidateResult.ApprovalPresent = true; candidateResult.ApprovalRecordId = item.ApprovalRecordId; candidateResult.ApprovalValidationStatus = item.ValidationStatus;
            }
            FinalizeCandidate(evaluation.Road, evaluation.Files); FinalizeCandidate(evaluation.Fence, evaluation.Files); evaluation.OverallStatus = Overall(evaluation); return evaluation;
        }

        private static EngineeringDistanceApprovalRecord ValidateApproval(string path, EngineeringDistanceApprovalQuarantineFile item, EngineeringDistanceApprovalHandoffManifest handoff, IReadOnlyList<EngineeringDistanceCandidateDefinition> candidates, string expectedProject)
        {
            EngineeringDistanceApprovalRecord record = EngineeringDistanceApprovalRecordFileStore.Read(path); EngineeringDistanceApprovalHandoffCandidate expected = Candidate(handoff, record.CandidateBinding.CandidateId); EngineeringDistanceCandidateDefinition candidate = candidates.Single(x => x.CandidateId == record.CandidateBinding.CandidateId);
            if (record.ProjectDirectory != EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProject) || record.CandidateBinding.RuleId != expected.RuleId || record.CandidateBinding.CandidateDefinitionSha256 != expected.CandidateDefinitionSha256 || record.CandidateBinding.ProposedMinimumDistanceMeters != expected.CandidateValueMeters || record.CandidateBinding.MeasurementKind != candidate.MeasurementKind || record.CandidateBinding.RequiredMeasurementBasisKey != candidate.RequiredMeasurementBasisKey || record.CandidateBinding.RequiredApplicabilityKey != candidate.RequiredApplicabilityKey || !record.CandidateBinding.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expected.ClauseIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal) || !record.EvidenceBindings.OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(x => x.SourceId).SequenceEqual(expected.SourceIds, StringComparer.Ordinal) || !record.EvidenceBindings.OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(x => x.SourceSha256).SequenceEqual(expected.SourceSha256, StringComparer.Ordinal) || record.Decision.DecisionStatus != "APPROVED") throw new InvalidDataException("returned approval does not match the bound H-B request");
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create(); EngineeringDistanceApprovalCurrentnessResult currentness = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(record, expectedProject, candidate, context.ClauseRegistry, context.SourceRegistry);
            if (currentness.Status != EngineeringDistanceApprovalCurrentnessStates.Current) throw new InvalidDataException("returned approval is stale: " + string.Join(",", currentness.Reasons));
            item.CandidateId = record.CandidateBinding.CandidateId; item.RuleId = record.CandidateBinding.RuleId; item.ApprovalRecordId = record.ApprovalRecordId; item.ValidationStatus = EngineeringDistanceApprovalQuarantineStates.Quarantined; return record;
        }

        private static EngineeringDistanceApprovalAuthorityPolicyArtifact ValidateStructureAndTrust(EngineeringDistanceApprovalAuthorityPolicyArtifact artifact, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trust, out bool trusted)
        {
            trusted = false;
            try { EngineeringDistanceApprovalAuthorityPolicyArtifact validated = EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(artifact, trust); trusted = true; return validated; }
            catch (EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException exception) when (exception.Message.IndexOf("not trusted", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // The existing P9 validator performs all structural, content, signature-format, and ID checks before trust lookup.
                // Its explicit trust failure is therefore the fail-closed PENDING_TRUST state for a structurally valid artifact.
                return artifact;
            }
        }

        private static void FinalizeCandidate(EngineeringDistanceApprovalQuarantineCandidateResult candidate, IReadOnlyList<EngineeringDistanceApprovalQuarantineFile> files)
        {
            if (files.Any(x => x.CandidateId == candidate.CandidateId && x.ArtifactType == PolicyType && x.ValidationStatus == EngineeringDistanceApprovalQuarantineStates.PendingTrust)) candidate.CandidateState = EngineeringDistanceApprovalQuarantineStates.PendingTrust;
            else if (candidate.ApprovalValidationStatus == EngineeringDistanceApprovalQuarantineStates.Quarantined && candidate.PolicyValidationStatus == EngineeringDistanceApprovalQuarantineStates.Quarantined) candidate.CandidateState = EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation;
            else if (files.Any(x => x.CandidateId == candidate.CandidateId && x.ValidationStatus == EngineeringDistanceApprovalQuarantineStates.Rejected)) candidate.CandidateState = EngineeringDistanceApprovalQuarantineStates.Rejected;
            else candidate.CandidateState = EngineeringDistanceApprovalQuarantineStates.Quarantined;
        }
        private static string Overall(EngineeringDistanceApprovalQuarantineEvaluation evaluation) { if (evaluation.Files.Any(x => x.ValidationStatus == EngineeringDistanceApprovalQuarantineStates.Rejected)) return EngineeringDistanceApprovalQuarantineStates.Rejected; if (evaluation.Road.CandidateState == EngineeringDistanceApprovalQuarantineStates.PendingTrust || evaluation.Fence.CandidateState == EngineeringDistanceApprovalQuarantineStates.PendingTrust) return EngineeringDistanceApprovalQuarantineStates.PendingTrust; if (evaluation.Road.CandidateState == EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation && evaluation.Fence.CandidateState == EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation) return EngineeringDistanceApprovalQuarantineStates.ReadyForAuthorizedInstallation; return EngineeringDistanceApprovalQuarantineStates.Quarantined; }
        private static EngineeringDistanceApprovalQuarantineCandidateResult CandidateResult(string id, string rule) => new EngineeringDistanceApprovalQuarantineCandidateResult { CandidateId = id, RuleId = rule };
        private static EngineeringDistanceApprovalQuarantineCandidateResult ResultFor(EngineeringDistanceApprovalQuarantineEvaluation evaluation, string id) { if (id == RoadId) return evaluation.Road; if (id == FenceId) return evaluation.Fence; throw new InvalidDataException("candidate is not bound to the H-B package"); }
        private static string Rule(EngineeringDistanceApprovalHandoffManifest manifest, string id) { try { return Candidate(manifest, id).RuleId; } catch { return string.Empty; } }
        private static EngineeringDistanceApprovalHandoffCandidate Candidate(EngineeringDistanceApprovalHandoffManifest manifest, string id) { if (id == manifest.Road.CandidateId) return manifest.Road; if (id == manifest.Fence.CandidateId) return manifest.Fence; throw new InvalidDataException("candidate is not bound to the H-B package"); }
        private static string Classify(string name) { string lower = name.ToLowerInvariant(); if (lower.EndsWith(".engineering-distance.approval.json")) return ApprovalType; if (lower.EndsWith(".engineering-distance.authority-policy.json")) return PolicyType; return string.Empty; }
        private static bool Forbidden(string name) { string lower = name.ToLowerInvariant(); return lower == "trusted-issuers.json" || new[] { ".pfx", ".p12", ".pem", ".key", ".exe", ".dll", ".ps1", ".bat", ".cmd", ".vbs", ".js", ".zip", ".7z", ".rar" }.Any(lower.EndsWith); }
        private static T Read<T>(string path) { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
        private static byte[] Json(object value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return stream.ToArray(); } }
        private static string Sha256(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty); }
        private static void WriteCreateNew(string path, byte[] bytes) { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length); }
        private static DateTime Normalize(DateTime value) { DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(); return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc); }
    }

    internal sealed class EngineeringDistanceApprovalQuarantineEvaluation
    {
        public List<EngineeringDistanceApprovalQuarantineFile> Files { get; } = new List<EngineeringDistanceApprovalQuarantineFile>();
        public EngineeringDistanceApprovalQuarantineCandidateResult Road { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
        public EngineeringDistanceApprovalQuarantineCandidateResult Fence { get; set; } = new EngineeringDistanceApprovalQuarantineCandidateResult();
        public string OverallStatus { get; set; } = EngineeringDistanceApprovalQuarantineStates.Rejected;
    }

    public static class EngineeringDistanceApprovalQuarantineVerifier
    {
        public static EngineeringDistanceApprovalQuarantineResult Verify(string quarantineDirectory, string trustStorePath)
        {
            try
            {
                EngineeringDistanceApprovalQuarantineManifest stored = Read<EngineeringDistanceApprovalQuarantineManifest>(Path.Combine(quarantineDirectory, EngineeringDistanceApprovalQuarantineService.ManifestFileName));
                if (stored.QuarantineId != EngineeringDistanceApprovalQuarantineService.ComputeQuarantineId(stored)) throw new InvalidDataException("quarantine ID does not match inventory");
                foreach (EngineeringDistanceApprovalQuarantineFile item in stored.Files)
                {
                    string file = Path.Combine(quarantineDirectory, item.OriginalFileName); if (!File.Exists(file)) throw new InvalidDataException("quarantined file is missing: " + item.OriginalFileName); byte[] bytes = File.ReadAllBytes(file); if (bytes.LongLength != item.Length || Sha256(bytes) != item.Sha256) throw new InvalidDataException("quarantined file hash mismatch: " + item.OriginalFileName);
                }
                EngineeringDistanceApprovalHandoffVerificationResult handoff = EngineeringDistanceApprovalHandoffVerifier.Verify(stored.HandoffDirectory); EngineeringDistanceApprovalHandoffManifest handoffManifest = handoff.Status == "VALID_HANDOFF_PACKAGE" ? Read<EngineeringDistanceApprovalHandoffManifest>(Path.Combine(stored.HandoffDirectory, EngineeringDistanceApprovalHandoffPackageBuilder.ManifestFileName)) : new EngineeringDistanceApprovalHandoffManifest();
                string[] files = stored.Files.Select(x => Path.Combine(quarantineDirectory, x.OriginalFileName)).OrderBy(x => x, StringComparer.Ordinal).ToArray(); var diagnostics = new List<string>(); EngineeringDistanceApprovalQuarantineEvaluation evaluation = EngineeringDistanceApprovalQuarantineService.Evaluate(files, handoffManifest, handoff.Status == "VALID_HANDOFF_PACKAGE" && handoffManifest.PackageId == stored.SourcePackageId, stored.ExpectedProjectDirectory, trustStorePath, diagnostics);
                if (!SameSemanticInventory(stored.Files, evaluation.Files) || stored.OverallStatus != evaluation.OverallStatus) throw new InvalidDataException("stored quarantine validation does not match independent revalidation");
                return new EngineeringDistanceApprovalQuarantineResult { Status = evaluation.OverallStatus, SourcePackageId = stored.SourcePackageId, QuarantineId = stored.QuarantineId, QuarantineDirectory = quarantineDirectory, ReturnedFileCount = stored.Files.Count, QuarantinedFileCount = stored.Files.Count, Files = evaluation.Files, Diagnostics = diagnostics.Concat(evaluation.Files.SelectMany(x => x.Diagnostics)).OrderBy(x => x, StringComparer.Ordinal).ToList(), Road = evaluation.Road, Fence = evaluation.Fence };
            }
            catch (Exception exception) { return new EngineeringDistanceApprovalQuarantineResult { Status = EngineeringDistanceApprovalQuarantineStates.Rejected, Diagnostics = new List<string> { exception.Message } }; }
        }
        private static bool SameSemanticInventory(IReadOnlyList<EngineeringDistanceApprovalQuarantineFile> a, IReadOnlyList<EngineeringDistanceApprovalQuarantineFile> b) { return a.Count == b.Count && a.OrderBy(x => x.OriginalFileName, StringComparer.Ordinal).Zip(b.OrderBy(x => x.OriginalFileName, StringComparer.Ordinal), (x, y) => x.OriginalFileName == y.OriginalFileName && x.ValidationStatus == y.ValidationStatus && x.CandidateId == y.CandidateId && x.ApprovalRecordId == y.ApprovalRecordId && x.PolicyArtifactId == y.PolicyArtifactId).All(x => x); }
        private static T Read<T>(string path) { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
        private static string Sha256(byte[] bytes) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty); }
    }
}
