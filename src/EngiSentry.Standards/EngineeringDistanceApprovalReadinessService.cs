using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalReadinessItem
    {
        public string CandidateId { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public decimal CandidateValueMeters { get; set; }
        public decimal ConfiguredCriterionMeters { get; set; }
        public string CandidateDefinitionSha256 { get; set; } = string.Empty;
        public List<string> ClauseIds { get; set; } = new List<string>();
        public bool SourceHashesAvailable { get; set; }
        public int ApprovalArtifactCount { get; set; }
        public string? ActiveApprovalRecordId { get; set; }
        public string ApprovalCurrentness { get; set; } = "NOT_AVAILABLE";
        public bool TrustedIssuerPresent { get; set; }
        public bool VerifiedAuthorityPolicyPresent { get; set; }
        public string GovernanceState { get; set; } = ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly;
        public bool AutoExecutionAllowed { get; set; }
        public List<string> BlockingReasons { get; set; } = new List<string>();
    }

    public static class EngineeringDistanceApprovalReadinessService
    {
        public static IReadOnlyList<EngineeringDistanceApprovalReadinessItem> Inspect(ProjectDistanceCriteria criteria, string approvalDirectory, string trustStorePath, string policyDirectory)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            IReadOnlyList<EngineeringDistanceCandidateDefinition> candidates = EngineeringDistanceCandidateCatalog.BuiltInCandidates;
            IReadOnlyList<EngineeringDistanceApprovalRecord> approvals = EngineeringDistanceApprovalArtifactCatalog.Load(approvalDirectory, criteria.ProjectDirectory);
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustStore.Load(trustStorePath);
            EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog policies = EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.Load(policyDirectory, criteria.ProjectDirectory, trusted);
            StandardAuthorityPolicyRegistry standardPolicies = ProjectEngineeringDistanceAuthorityPolicyCatalog.Create(context.SourceRegistry, context.ClauseRegistry);
            ProjectDistanceGovernanceAggregateResult aggregate = ProjectDistanceGovernanceAggregateResolver.Resolve(criteria.ProjectDirectory, ProjectDistanceRuleBinder.Create(criteria), candidates, approvals, policies, context.ClauseRegistry, context.SourceRegistry, standardPolicies);
            var result = new List<EngineeringDistanceApprovalReadinessItem>();
            foreach (EngineeringDistanceCandidateDefinition candidate in candidates.OrderBy(x => x.CandidateId, StringComparer.Ordinal))
            {
                ProjectDistanceGovernanceAggregateItem item = aggregate.Items.Single(x => x.CandidateId == candidate.CandidateId);
                EngineeringDistanceRuleDefinition rule = ProjectDistanceRuleBinder.Create(criteria).Single(x => x.MeasurementKind == candidate.MeasurementKind && x.RequiredMeasurementBasisKey == candidate.RequiredMeasurementBasisKey);
                bool sourceHashes = candidate.ClauseIds.All(id => context.ClauseRegistry.TryGet(id, out StandardClauseDefinition? clause) && clause != null && context.SourceRegistry.TryGet(clause.SourceId, out StandardSourceDefinition? source) && source != null && !string.IsNullOrWhiteSpace(source.SourceSha256));
                var readiness = new EngineeringDistanceApprovalReadinessItem { CandidateId=candidate.CandidateId, RuleId=candidate.RuleId, CandidateValueMeters=candidate.ProposedMinimumDistanceMeters, ConfiguredCriterionMeters=rule.MinimumDistanceMeters, CandidateDefinitionSha256=EngineeringDistanceApprovalPreparation.CandidateHash(candidate), ClauseIds=candidate.ClauseIds.OrderBy(x=>x,StringComparer.Ordinal).ToList(), SourceHashesAvailable=sourceHashes, ApprovalArtifactCount=approvals.Count(x=>x.CandidateBinding.CandidateId==candidate.CandidateId), ActiveApprovalRecordId=item.ApprovalRecordId, ApprovalCurrentness=item.CurrentnessState ?? "NOT_AVAILABLE", TrustedIssuerPresent=!string.IsNullOrWhiteSpace(item.VerifiedPolicyIssuerId), VerifiedAuthorityPolicyPresent=!string.IsNullOrWhiteSpace(item.VerifiedPolicyArtifactId), GovernanceState=item.Resolution.State, AutoExecutionAllowed=item.Resolution.State==ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible };
                if (readiness.ApprovalArtifactCount == 0) readiness.BlockingReasons.Add("no approval artifact");
                if (!readiness.SourceHashesAvailable) readiness.BlockingReasons.Add("source hash evidence unavailable");
                if (!readiness.TrustedIssuerPresent) readiness.BlockingReasons.Add("trusted issuer/policy artifact unavailable");
                if (!readiness.VerifiedAuthorityPolicyPresent) readiness.BlockingReasons.Add("verified authority policy unavailable");
                if (!readiness.AutoExecutionAllowed) readiness.BlockingReasons.Add("governance state is " + readiness.GovernanceState);
                result.Add(readiness);
            }
            return result;
        }
    }
}
