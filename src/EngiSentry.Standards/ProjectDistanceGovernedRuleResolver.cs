using System;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectDistanceGovernedRuleResolverValidationException : Exception
    {
        public ProjectDistanceGovernedRuleResolverValidationException(string message) : base(message) { }
    }

    public static class ProjectDistanceGovernedRuleResolver
    {
        public static ProjectDistanceGovernedRuleResolution Resolve(EngineeringDistanceRuleDefinition projectRule, EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceApprovalRecord? approval, EngineeringDistanceApprovalAuthorityResult? authority)
        {
            if (projectRule == null) throw new ArgumentNullException(nameof(projectRule));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            EngineeringDistanceCriteriaProvenance provenance = projectRule.CriteriaProvenance ?? throw new ProjectDistanceGovernedRuleResolverValidationException("project rule CriteriaProvenance is required");
            if (provenance.SourceKind != "ProjectDistanceCriteria") throw new ProjectDistanceGovernedRuleResolverValidationException("project rule CriteriaProvenance SourceKind is invalid");

            var result = new ProjectDistanceGovernedRuleResolution { ProjectRuleId = projectRule.RuleId, CandidateId = candidate.CandidateId };
            if (approval == null && authority == null)
            {
                result.State = ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly;
                return result;
            }
            if (approval == null || authority == null) throw new ProjectDistanceGovernedRuleResolverValidationException("approval and authority must be supplied together");

            ValidateIdentity(provenance, candidate, approval, authority);
            result.AuthorityState = authority.State;
            if (authority.State == EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional)
            {
                result.State = ProjectDistanceGovernedRuleResolutionStates.GovernedConditional;
                result.GovernanceProvenance = CreateProvenance(projectRule, candidate, approval, authority);
                return result;
            }
            if (authority.State != EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic)
            {
                if (!IsBlockedAuthorityState(authority.State)) throw new ProjectDistanceGovernedRuleResolverValidationException("authority State is invalid: " + authority.State);
                result.State = ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly;
                return result;
            }

            ProjectDistanceGovernanceProvenance governed = CreateProvenance(projectRule, candidate, approval, authority);
            result.GovernanceProvenance = governed;
            if (projectRule.MeasurementKind != candidate.MeasurementKind || projectRule.RequiredMeasurementBasisKey != candidate.RequiredMeasurementBasisKey || projectRule.RequiredApplicabilityKey != candidate.RequiredApplicabilityKey)
            {
                result.State = ProjectDistanceGovernedRuleResolutionStates.SemanticScopeConflict;
                return result;
            }
            result.State = projectRule.MinimumDistanceMeters == candidate.ProposedMinimumDistanceMeters ? ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible : ProjectDistanceGovernedRuleResolutionStates.ValueConflict;
            return result;
        }

        private static void ValidateIdentity(EngineeringDistanceCriteriaProvenance provenance, EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceApprovalRecord approval, EngineeringDistanceApprovalAuthorityResult authority)
        {
            if (approval.CandidateBinding.CandidateId != candidate.CandidateId) throw new ProjectDistanceGovernedRuleResolverValidationException("approval CandidateId does not match candidate");
            if (authority.CandidateId != candidate.CandidateId) throw new ProjectDistanceGovernedRuleResolverValidationException("authority CandidateId does not match candidate");
            if (authority.ApprovalRecordId != approval.ApprovalRecordId) throw new ProjectDistanceGovernedRuleResolverValidationException("authority ApprovalRecordId does not match approval");
            if (authority.ProjectDirectory != approval.ProjectDirectory) throw new ProjectDistanceGovernedRuleResolverValidationException("authority ProjectDirectory does not match approval");
            string projectDirectory = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(provenance.ProjectDirectory);
            if (projectDirectory != approval.ProjectDirectory) throw new ProjectDistanceGovernedRuleResolverValidationException("project rule ProjectDirectory does not match approval");
        }

        private static ProjectDistanceGovernanceProvenance CreateProvenance(EngineeringDistanceRuleDefinition projectRule, EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceApprovalRecord approval, EngineeringDistanceApprovalAuthorityResult authority)
        {
            string? policyId = authority.PolicyId;
            string? authorityBasisReference = authority.AuthorityBasisReference;
            if (policyId == null || authorityBasisReference == null) throw new ProjectDistanceGovernedRuleResolverValidationException("successful governing authority requires PolicyId and AuthorityBasisReference");
            if (string.IsNullOrWhiteSpace(policyId) || string.IsNullOrWhiteSpace(authorityBasisReference)) throw new ProjectDistanceGovernedRuleResolverValidationException("successful governing authority requires PolicyId and AuthorityBasisReference");
            return new ProjectDistanceGovernanceProvenance { ProjectRuleId = projectRule.RuleId, CandidateId = candidate.CandidateId, CandidateRuleId = candidate.RuleId, ApprovalRecordId = approval.ApprovalRecordId, PolicyId = policyId, AuthorityBasisReference = authorityBasisReference, AuthorityState = authority.State, ApprovedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters };
        }

        private static bool IsBlockedAuthorityState(string state) => state == EngineeringDistanceApprovalAuthorityStates.InactiveApproval || state == EngineeringDistanceApprovalAuthorityStates.NotApproved || state == EngineeringDistanceApprovalAuthorityStates.StaleApproval || state == EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy || state == EngineeringDistanceApprovalAuthorityStates.AuthorityPolicyMismatch || state == EngineeringDistanceApprovalAuthorityStates.LegacyConflictUnresolved || state == EngineeringDistanceApprovalAuthorityStates.SourceQuarantined;
    }
}
