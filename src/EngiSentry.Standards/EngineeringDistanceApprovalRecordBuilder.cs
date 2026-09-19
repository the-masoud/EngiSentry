using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalRecordBuilder
    {
        public static EngineeringDistanceApprovalRecord Create(string projectDirectory, EngineeringDistanceCandidateDefinition candidate, StandardClauseRegistry clauses, StandardSourceRegistry sources, EngineeringDistanceApprovalDecision decision, string approverDisplayName, string? authenticatedApproverIdentity, DateTime decisionTimestampUtc, string? comment = null, string? supersedesApprovalRecordId = null)
        {
            if (candidate == null || clauses == null || sources == null || decision == null) throw new ArgumentNullException();
            EngineeringDistanceCandidateBinding binding = Binding(candidate);
            var evidence = new List<EngineeringDistanceApprovalEvidenceBinding>();
            foreach (string clauseId in binding.ClauseIds)
            {
                if (!clauses.TryGet(clauseId, out StandardClauseDefinition? clause) || clause == null)
                    throw new InvalidOperationException("candidate contains unresolved clause/source evidence: " + clauseId);
                if (!sources.TryGet(clause.SourceId, out StandardSourceDefinition? source) || source == null)
                    throw new InvalidOperationException("candidate contains unresolved clause/source evidence: " + clauseId);
                string? sourceSha = source.SourceSha256;
                if (sourceSha == null || !EngineeringDistanceApprovalRecordValidator.IsSha(sourceSha))
                    throw new InvalidOperationException("candidate contains unresolved clause/source evidence: " + clauseId);
                evidence.Add(new EngineeringDistanceApprovalEvidenceBinding { ClauseId = clauseId, SourceId = source.SourceId, SourceSha256 = sourceSha.ToUpperInvariant() });
            }
            EngineeringDistanceApprovalRecord record = new EngineeringDistanceApprovalRecord { SchemaVersion = EngineeringDistanceApprovalRecordValidator.SchemaVersion, ProjectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory), CandidateBinding = binding, Decision = new EngineeringDistanceApprovalDecision { CandidateId = decision.CandidateId, DecisionStatus = decision.DecisionStatus, ApprovalReference = decision.ApprovalReference }, ApproverDisplayName = approverDisplayName, AuthenticatedApproverIdentity = authenticatedApproverIdentity, DecisionTimestampUtc = NormalizeTimestamp(decisionTimestampUtc), Comment = comment, SupersedesApprovalRecordId = supersedesApprovalRecordId, EvidenceBindings = evidence.OrderBy(x => x.ClauseId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ThenBy(x => x.SourceSha256, StringComparer.Ordinal).ToList() };
            record.ApprovalRecordId = ComputeApprovalRecordId(record); EngineeringDistanceApprovalRecordValidator.Validate(record); return record;
        }
        public static string ComputeCandidateDefinitionSha256(EngineeringDistanceCandidateBinding binding) { var copy = new EngineeringDistanceCandidateBinding { CandidateId = binding.CandidateId, PackId = binding.PackId, RuleId = binding.RuleId, MeasurementKind = binding.MeasurementKind, ProposedMinimumDistanceMeters = binding.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = binding.RequiredMeasurementBasisKey, RequiredApplicabilityKey = binding.RequiredApplicabilityKey, ClauseIds = binding.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList(), CandidateDefinitionSha256 = string.Empty }; using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Serialize(copy))).Replace("-", string.Empty); }
        public static string ComputeApprovalRecordId(EngineeringDistanceApprovalRecord record) { EngineeringDistanceApprovalRecord copy = Copy(record); copy.ApprovalRecordId = string.Empty; using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Serialize(copy))).Replace("-", string.Empty); }
        private static EngineeringDistanceCandidateBinding Binding(EngineeringDistanceCandidateDefinition candidate) { var b = new EngineeringDistanceCandidateBinding { CandidateId = candidate.CandidateId, PackId = candidate.PackId, RuleId = candidate.RuleId, MeasurementKind = candidate.MeasurementKind, ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey, RequiredApplicabilityKey = candidate.RequiredApplicabilityKey, ClauseIds = (candidate.ClauseIds ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList() }; b.CandidateDefinitionSha256 = ComputeCandidateDefinitionSha256(b); return b; }
        private static DateTime NormalizeTimestamp(DateTime value) { DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(); return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc); }
        private static byte[] Serialize(object value) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value); return stream.ToArray(); } }
        private static EngineeringDistanceApprovalRecord Copy(EngineeringDistanceApprovalRecord x) => new EngineeringDistanceApprovalRecord { SchemaVersion=x.SchemaVersion, ApprovalRecordId=x.ApprovalRecordId, ProjectDirectory=x.ProjectDirectory, CandidateBinding=new EngineeringDistanceCandidateBinding { CandidateId=x.CandidateBinding.CandidateId, PackId=x.CandidateBinding.PackId, RuleId=x.CandidateBinding.RuleId, MeasurementKind=x.CandidateBinding.MeasurementKind, ProposedMinimumDistanceMeters=x.CandidateBinding.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey=x.CandidateBinding.RequiredMeasurementBasisKey, RequiredApplicabilityKey=x.CandidateBinding.RequiredApplicabilityKey, ClauseIds=x.CandidateBinding.ClauseIds.OrderBy(v=>v,StringComparer.Ordinal).ToList(), CandidateDefinitionSha256=x.CandidateBinding.CandidateDefinitionSha256 }, Decision=new EngineeringDistanceApprovalDecision { CandidateId=x.Decision.CandidateId, DecisionStatus=x.Decision.DecisionStatus, ApprovalReference=x.Decision.ApprovalReference }, ApproverDisplayName=x.ApproverDisplayName, AuthenticatedApproverIdentity=x.AuthenticatedApproverIdentity, DecisionTimestampUtc=x.DecisionTimestampUtc, Comment=x.Comment, SupersedesApprovalRecordId=x.SupersedesApprovalRecordId, EvidenceBindings=x.EvidenceBindings.OrderBy(v=>v.ClauseId,StringComparer.Ordinal).ThenBy(v=>v.SourceId,StringComparer.Ordinal).ThenBy(v=>v.SourceSha256,StringComparer.Ordinal).Select(v=>new EngineeringDistanceApprovalEvidenceBinding { ClauseId=v.ClauseId,SourceId=v.SourceId,SourceSha256=v.SourceSha256}).ToList() };
    }
}
