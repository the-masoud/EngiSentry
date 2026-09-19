using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectDistanceGovernanceAggregateValidationException : Exception { public ProjectDistanceGovernanceAggregateValidationException(string message, Exception? innerException = null) : base(message, innerException) { } }

    public static class ProjectDistanceGovernanceAggregateResolver
    {
        public static ProjectDistanceGovernanceAggregateResult Resolve(string projectDirectory, IEnumerable<EngineeringDistanceRuleDefinition> projectRules, IEnumerable<EngineeringDistanceCandidateDefinition> candidates, IEnumerable<EngineeringDistanceApprovalRecord> approvalRecords, EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog verifiedPolicyCatalog, StandardClauseRegistry clauses, StandardSourceRegistry sources, StandardAuthorityPolicyRegistry standardPolicies)
        {
            if (projectRules == null || candidates == null || approvalRecords == null || verifiedPolicyCatalog == null || clauses == null || sources == null || standardPolicies == null) throw new ArgumentNullException();
            string project = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);
            List<EngineeringDistanceRuleDefinition> rules = projectRules.ToList();
            List<EngineeringDistanceApprovalRecord> approvals = approvalRecords.ToList();
            foreach (EngineeringDistanceApprovalRecord approval in approvals)
            {
                if (approval == null) throw new ProjectDistanceGovernanceAggregateValidationException("aggregate approval records contain null");
                if (approval.ProjectDirectory != project) Fail("aggregate approval record ProjectDirectory does not match requested project");
            }
            foreach (EngineeringDistanceRuleDefinition rule in rules) ValidateProjectRule(rule, project);

            IReadOnlyList<EngineeringDistanceCandidateDefinition> currentCandidates;
            try { currentCandidates = EngineeringDistanceCandidateCatalog.Create(clauses, candidates).Candidates; }
            catch (Exception exception) { throw new ProjectDistanceGovernanceAggregateValidationException("aggregate candidates are invalid", exception); }
            EngineeringDistanceApprovalHistoryResolution history;
            try { history = EngineeringDistanceApprovalHistoryResolver.Resolve(approvals); }
            catch (Exception exception) { throw new ProjectDistanceGovernanceAggregateValidationException("aggregate approval history is invalid", exception); }

            IReadOnlyList<EngineeringDistanceApprovalAuthorityPolicyArtifact> verifiedArtifacts = verifiedPolicyCatalog.Artifacts;
            foreach (EngineeringDistanceApprovalAuthorityPolicyArtifact artifact in verifiedArtifacts)
            {
                if (artifact.Policy == null || artifact.Policy.ProjectDirectory != project) Fail("verified policy artifact ProjectDirectory does not match requested project");
            }
            EngineeringDistanceApprovalAuthorityPolicyRegistry verifiedPolicies = verifiedPolicyCatalog.CreatePolicyRegistry();
            var targetedRuleIds = new HashSet<string>(StringComparer.Ordinal);
            var items = new List<ProjectDistanceGovernanceAggregateItem>();

            foreach (EngineeringDistanceCandidateDefinition candidate in currentCandidates)
            {
                EngineeringDistanceRuleDefinition[] matches = rules.Where(x => x.MeasurementKind == candidate.MeasurementKind && x.RequiredMeasurementBasisKey == candidate.RequiredMeasurementBasisKey).ToArray();
                if (matches.Length != 1) Fail(matches.Length == 0 ? "candidate has no matching project rule: " + candidate.CandidateId : "candidate has multiple matching project rules: " + candidate.CandidateId);
                EngineeringDistanceRuleDefinition rule = matches[0];
                if (!targetedRuleIds.Add(rule.RuleId)) Fail("multiple candidates target ProjectRuleId: " + rule.RuleId);
                EngineeringDistanceApprovalRecord[] candidateApprovals = approvals.Where(x => x.CandidateBinding.CandidateId == candidate.CandidateId).ToArray();
                if (candidateApprovals.Length == 0)
                {
                    items.Add(NewItem(candidate, rule, null, null, null, null));
                    continue;
                }
                EngineeringDistanceApprovalHistoryEntry[] active = history.Entries.Where(x => x.ProjectDirectory == project && x.CandidateId == candidate.CandidateId && x.State == EngineeringDistanceApprovalHistoryStates.Active).ToArray();
                if (active.Length != 1) Fail("candidate approval history does not contain exactly one active approval: " + candidate.CandidateId);
                EngineeringDistanceApprovalRecord[] activeApprovalMatches = candidateApprovals.Where(x => x.ApprovalRecordId == active[0].ApprovalRecordId).ToArray();
                if (activeApprovalMatches.Length != 1) Fail("active approval history entry does not resolve to exactly one approval record: " + candidate.CandidateId);
                EngineeringDistanceApprovalRecord activeApproval = activeApprovalMatches[0];
                EngineeringDistanceApprovalCurrentnessResult currentness = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(activeApproval, project, candidate, clauses, sources);
                EngineeringDistanceApprovalAuthorityPolicyArtifact[] policyMatches = verifiedArtifacts.Where(x => x.Policy!.ProjectDirectory == project && x.Policy.CandidateId == candidate.CandidateId && x.Policy.ApprovalRecordId == activeApproval.ApprovalRecordId).ToArray();
                if (policyMatches.Length > 1) Fail("active approval has multiple verified policy artifacts: " + candidate.CandidateId);
                EngineeringDistanceApprovalAuthorityResult authority = EngineeringDistanceApprovalAuthorityResolver.Resolve(activeApproval, active[0], currentness, verifiedPolicies, clauses, sources, standardPolicies);
                items.Add(NewItem(candidate, rule, activeApproval, active[0], currentness, authority, policyMatches.Length == 1 ? policyMatches[0] : null));
            }
            return new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = project, Items = items.OrderBy(x => x.CandidateId, StringComparer.Ordinal).ThenBy(x => x.ProjectRuleId, StringComparer.Ordinal).Select(CopyItem).ToList() };
        }

        private static ProjectDistanceGovernanceAggregateItem NewItem(EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceRuleDefinition rule, EngineeringDistanceApprovalRecord? approval, EngineeringDistanceApprovalHistoryEntry? history, EngineeringDistanceApprovalCurrentnessResult? currentness, EngineeringDistanceApprovalAuthorityResult? authority, EngineeringDistanceApprovalAuthorityPolicyArtifact? artifact = null)
        {
            ProjectDistanceGovernedRuleResolution resolution = ProjectDistanceGovernedRuleResolver.Resolve(rule, candidate, approval, authority);
            return new ProjectDistanceGovernanceAggregateItem { CandidateId = candidate.CandidateId, CandidateRuleId = candidate.RuleId, ProjectRuleId = rule.RuleId, ApprovalRecordId = approval?.ApprovalRecordId, HistoryState = history?.State, CurrentnessState = currentness?.Status, AuthorityState = authority?.State, VerifiedPolicyArtifactId = artifact?.PolicyArtifactId, VerifiedPolicyIssuerId = artifact?.IssuerId, VerifiedPolicyKeyId = artifact?.KeyId, Resolution = CopyResolution(resolution) };
        }

        private static void ValidateProjectRule(EngineeringDistanceRuleDefinition? rule, string project)
        {
            if (rule == null) throw new ProjectDistanceGovernanceAggregateValidationException("aggregate project rules contain null");
            EngineeringDistanceCriteriaProvenance provenance = rule.CriteriaProvenance ?? throw new ProjectDistanceGovernanceAggregateValidationException("aggregate project rule CriteriaProvenance is required");
            if (provenance.SourceKind != "ProjectDistanceCriteria") Fail("aggregate project rule CriteriaProvenance SourceKind is invalid");
            if (EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(provenance.ProjectDirectory) != project) Fail("aggregate project rule ProjectDirectory does not match requested project");
        }

        private static ProjectDistanceGovernanceAggregateItem CopyItem(ProjectDistanceGovernanceAggregateItem x) => new ProjectDistanceGovernanceAggregateItem { CandidateId = x.CandidateId, CandidateRuleId = x.CandidateRuleId, ProjectRuleId = x.ProjectRuleId, ApprovalRecordId = x.ApprovalRecordId, HistoryState = x.HistoryState, CurrentnessState = x.CurrentnessState, AuthorityState = x.AuthorityState, VerifiedPolicyArtifactId = x.VerifiedPolicyArtifactId, VerifiedPolicyIssuerId = x.VerifiedPolicyIssuerId, VerifiedPolicyKeyId = x.VerifiedPolicyKeyId, Resolution = CopyResolution(x.Resolution) };
        private static ProjectDistanceGovernedRuleResolution CopyResolution(ProjectDistanceGovernedRuleResolution x) => new ProjectDistanceGovernedRuleResolution { State = x.State, ProjectRuleId = x.ProjectRuleId, CandidateId = x.CandidateId, AuthorityState = x.AuthorityState, GovernanceProvenance = x.GovernanceProvenance == null ? null : new ProjectDistanceGovernanceProvenance { ProjectRuleId = x.GovernanceProvenance.ProjectRuleId, CandidateId = x.GovernanceProvenance.CandidateId, CandidateRuleId = x.GovernanceProvenance.CandidateRuleId, ApprovalRecordId = x.GovernanceProvenance.ApprovalRecordId, PolicyId = x.GovernanceProvenance.PolicyId, AuthorityBasisReference = x.GovernanceProvenance.AuthorityBasisReference, AuthorityState = x.GovernanceProvenance.AuthorityState, ApprovedMinimumDistanceMeters = x.GovernanceProvenance.ApprovedMinimumDistanceMeters } };
        private static void Fail(string message) => throw new ProjectDistanceGovernanceAggregateValidationException(message);
    }
}
