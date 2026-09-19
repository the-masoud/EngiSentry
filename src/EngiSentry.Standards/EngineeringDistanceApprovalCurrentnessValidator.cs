using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalCurrentnessValidator
    {
        public static EngineeringDistanceApprovalCurrentnessResult Evaluate(EngineeringDistanceApprovalRecord approval, string expectedProjectDirectory, EngineeringDistanceCandidateDefinition? candidate, StandardClauseRegistry clauses, StandardSourceRegistry sources)
        {
            if (approval == null) throw new ArgumentNullException(nameof(approval)); if (clauses == null) throw new ArgumentNullException(nameof(clauses)); if (sources == null) throw new ArgumentNullException(nameof(sources));
            EngineeringDistanceApprovalRecordValidator.Validate(approval);
            string expectedProject = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProjectDirectory);
            var reasons = new List<string>();
            if (!string.Equals(approval.ProjectDirectory, expectedProject, StringComparison.Ordinal)) reasons.Add(EngineeringDistanceApprovalCurrentnessReasons.ProjectScopeChanged);
            if (candidate == null) reasons.Add(EngineeringDistanceApprovalCurrentnessReasons.CandidateMissing);
            else
            {
                EngineeringDistanceCandidateBinding current = Binding(candidate);
                if (!SameBinding(approval.CandidateBinding, current)) reasons.Add(EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged);
                if (!SameEvidence(approval.EvidenceBindings, Evidence(candidate, clauses, sources))) reasons.Add(EngineeringDistanceApprovalCurrentnessReasons.EvidenceBindingsChanged);
            }
            return new EngineeringDistanceApprovalCurrentnessResult { Status = reasons.Count == 0 ? EngineeringDistanceApprovalCurrentnessStates.Current : EngineeringDistanceApprovalCurrentnessStates.Stale, Reasons = reasons };
        }

        private static EngineeringDistanceCandidateBinding Binding(EngineeringDistanceCandidateDefinition candidate)
        {
            var result = new EngineeringDistanceCandidateBinding { CandidateId = candidate.CandidateId, PackId = candidate.PackId, RuleId = candidate.RuleId, MeasurementKind = candidate.MeasurementKind, ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters, RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey, RequiredApplicabilityKey = candidate.RequiredApplicabilityKey, ClauseIds = (candidate.ClauseIds ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal).ToList() };
            result.CandidateDefinitionSha256 = EngineeringDistanceApprovalRecordBuilder.ComputeCandidateDefinitionSha256(result); return result;
        }
        private static List<EngineeringDistanceApprovalEvidenceBinding> Evidence(EngineeringDistanceCandidateDefinition candidate, StandardClauseRegistry clauses, StandardSourceRegistry sources)
        {
            var evidence = new List<EngineeringDistanceApprovalEvidenceBinding>();
            foreach (string clauseId in (candidate.ClauseIds ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!clauses.TryGet(clauseId, out StandardClauseDefinition? clause) || clause == null || !sources.TryGet(clause.SourceId, out StandardSourceDefinition? source) || source == null) return new List<EngineeringDistanceApprovalEvidenceBinding>();
                string? sourceSha = source.SourceSha256;
                if (sourceSha == null || sourceSha.Length == 0) return new List<EngineeringDistanceApprovalEvidenceBinding>();
                evidence.Add(new EngineeringDistanceApprovalEvidenceBinding { ClauseId = clauseId, SourceId = source.SourceId, SourceSha256 = sourceSha.ToUpperInvariant() });
            }
            return evidence.OrderBy(x => x.ClauseId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ThenBy(x => x.SourceSha256, StringComparer.Ordinal).ToList();
        }
        private static bool SameBinding(EngineeringDistanceCandidateBinding left, EngineeringDistanceCandidateBinding right) => left.CandidateId == right.CandidateId && left.PackId == right.PackId && left.RuleId == right.RuleId && left.MeasurementKind == right.MeasurementKind && left.ProposedMinimumDistanceMeters == right.ProposedMinimumDistanceMeters && left.RequiredMeasurementBasisKey == right.RequiredMeasurementBasisKey && left.RequiredApplicabilityKey == right.RequiredApplicabilityKey && left.ClauseIds.SequenceEqual(right.ClauseIds, StringComparer.Ordinal) && left.CandidateDefinitionSha256 == right.CandidateDefinitionSha256;
        private static bool SameEvidence(List<EngineeringDistanceApprovalEvidenceBinding> left, List<EngineeringDistanceApprovalEvidenceBinding> right) => left.Count == right.Count && left.Select(x => x.ClauseId + "\u001f" + x.SourceId + "\u001f" + x.SourceSha256).SequenceEqual(right.Select(x => x.ClauseId + "\u001f" + x.SourceId + "\u001f" + x.SourceSha256), StringComparer.Ordinal);
    }
}
