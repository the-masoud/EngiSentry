using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalPreparation
    {
        public static EngineeringDistanceApprovalRequest BuildApprovalRequest(string projectDirectory, EngineeringDistanceCandidateDefinition candidate, StandardClauseRegistry clauses, StandardSourceRegistry sources, string? drawingName, DateTime createdAtUtc)
        {
            if (candidate == null || clauses == null || sources == null) throw new ArgumentNullException();
            string project = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);
            var evidence = Evidence(candidate, clauses, sources);
            var request = new EngineeringDistanceApprovalRequest
            {
                ProjectDirectory = project,
                CandidateId = candidate.CandidateId,
                PackId = candidate.PackId,
                RuleId = candidate.RuleId,
                MeasurementKind = candidate.MeasurementKind,
                ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters,
                RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey,
                RequiredApplicabilityKey = candidate.RequiredApplicabilityKey,
                CandidateDefinitionSha256 = CandidateHash(candidate),
                ClauseIds = candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                EvidenceBindings = evidence,
                GeneratedFromDrawing = drawingName,
                GeneratedFromProjectIdentity = project,
                CreatedAtUtc = Normalize(createdAtUtc),
                IsApproval = false,
                GrantsAuthority = false,
                AutoExecutionAllowed = false
            };
            request.RequestId = Hash(request, true);
            return request;
        }

        public static EngineeringDistanceAuthorityPolicyRequest BuildPolicyRequest(string projectDirectory, EngineeringDistanceCandidateDefinition candidate)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            var request = new EngineeringDistanceAuthorityPolicyRequest
            {
                ProjectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory),
                CandidateId = candidate.CandidateId,
                RuleId = candidate.RuleId,
                RequestedEnforcementMode = StandardEnforcementModes.Deterministic,
                LegacyConflictDisposition = EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision,
                Rationale = "Preparation request only. An authorized external authority must approve, sign, and install the real policy artifact.",
                IsSigned = false,
                IsTrusted = false,
                IsActive = false,
                GrantsAuthority = false
            };
            request.RequestId = Hash(request, false);
            return request;
        }

        public static string CandidateHash(EngineeringDistanceCandidateDefinition candidate)
        {
            var binding = new EngineeringDistanceCandidateBinding { CandidateId = candidate.CandidateId, PackId = candidate.PackId, RuleId = candidate.RuleId, MeasurementKind = candidate.MeasurementKind, ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey, RequiredApplicabilityKey = candidate.RequiredApplicabilityKey, ClauseIds = candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList() };
            return EngineeringDistanceApprovalRecordBuilder.ComputeCandidateDefinitionSha256(binding);
        }

        private static List<EngineeringDistanceApprovalRequestEvidenceBinding> Evidence(EngineeringDistanceCandidateDefinition candidate, StandardClauseRegistry clauses, StandardSourceRegistry sources)
        {
            return candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).Select(id =>
            {
                if (!clauses.TryGet(id, out StandardClauseDefinition? clause) || clause == null || !sources.TryGet(clause.SourceId, out StandardSourceDefinition? source) || source == null || string.IsNullOrWhiteSpace(source.SourceSha256)) throw new InvalidOperationException("candidate evidence is unresolved: " + id);
                return new EngineeringDistanceApprovalRequestEvidenceBinding { ClauseId = id, SourceId = source.SourceId, SourceSha256 = source.SourceSha256!.ToUpperInvariant() };
            }).ToList();
        }

        private static string Hash(object value, bool approval)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Serialize(value, approval))).Replace("-", string.Empty);
        }

        private static byte[] Serialize(object value, bool approval)
        {
            if (approval)
            {
                EngineeringDistanceApprovalRequest x = (EngineeringDistanceApprovalRequest)value;
                x = new EngineeringDistanceApprovalRequest { SchemaVersion=x.SchemaVersion, ProjectDirectory=x.ProjectDirectory, CandidateId=x.CandidateId, PackId=x.PackId, RuleId=x.RuleId, MeasurementKind=x.MeasurementKind, ProposedMinimumDistanceMeters=x.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey=x.RequiredMeasurementBasisKey, RequiredApplicabilityKey=x.RequiredApplicabilityKey, CandidateDefinitionSha256=x.CandidateDefinitionSha256, ClauseIds=x.ClauseIds.OrderBy(v=>v,StringComparer.Ordinal).ToList(), EvidenceBindings=x.EvidenceBindings.OrderBy(v=>v.ClauseId,StringComparer.Ordinal).ThenBy(v=>v.SourceId,StringComparer.Ordinal).ToList(), GeneratedFromDrawing=x.GeneratedFromDrawing, GeneratedFromProjectIdentity=x.GeneratedFromProjectIdentity, CreatedAtUtc=x.CreatedAtUtc, IsApproval=x.IsApproval, GrantsAuthority=x.GrantsAuthority, AutoExecutionAllowed=x.AutoExecutionAllowed };
                return Json(x);
            }
            EngineeringDistanceAuthorityPolicyRequest p = (EngineeringDistanceAuthorityPolicyRequest)value;
            p = new EngineeringDistanceAuthorityPolicyRequest { SchemaVersion=p.SchemaVersion, ProjectDirectory=p.ProjectDirectory, CandidateId=p.CandidateId, RuleId=p.RuleId, ExpectedApprovalRecordId=p.ExpectedApprovalRecordId, ExpectedApprovalReference=p.ExpectedApprovalReference, AuthorityBasisReference=p.AuthorityBasisReference, RequestedEnforcementMode=p.RequestedEnforcementMode, LegacyConflictDisposition=p.LegacyConflictDisposition, Rationale=p.Rationale, IsSigned=p.IsSigned, IsTrusted=p.IsTrusted, IsActive=p.IsActive, GrantsAuthority=p.GrantsAuthority };
            return Json(p);
        }

        private static byte[] Json(object value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return stream.ToArray(); } }
        private static DateTime Normalize(DateTime value) { DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(); return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc); }
    }
}
