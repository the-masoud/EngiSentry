using System;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    public static class ProjectDistanceGovernanceRuntimeLoader
    {
        public static ProjectDistanceGovernanceAggregateResult LoadCanonical(ProjectDistanceCriteria criteria, string approvalDirectory)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            if (string.IsNullOrWhiteSpace(approvalDirectory)) throw new ArgumentException("approval directory is required", nameof(approvalDirectory));
            ProjectDistanceCriteria valid = ProjectDistanceCriteriaStore.ValidateAndCopy(criteria);
            if (valid.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new ProjectDistanceGovernanceAggregateValidationException("project distance governance requires schema version " + ProjectDistanceCriteriaDefaults.CurrentSchemaVersion);
            ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
            StandardAuthorityPolicyRegistry standardPolicies = ProjectEngineeringDistanceAuthorityPolicyCatalog.Create(context.SourceRegistry, context.ClauseRegistry);
            var approvals = EngineeringDistanceApprovalArtifactCatalog.Load(approvalDirectory, valid.ProjectDirectory);
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers = EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical();
            EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog policies = EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.LoadCanonical(valid.ProjectDirectory, trustedIssuers);
            return ProjectDistanceGovernanceAggregateResolver.Resolve(valid.ProjectDirectory, ProjectDistanceRuleBinder.Create(valid), EngineeringDistanceCandidateCatalog.BuiltInCandidates, approvals, policies, context.ClauseRegistry, context.SourceRegistry, standardPolicies);
        }
    }
}
