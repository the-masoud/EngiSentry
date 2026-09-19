using System;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class ProjectEngineeringDistanceAuthorityPolicyCatalog
    {
        public static StandardAuthorityPolicyRegistry Create(StandardSourceRegistry sources, StandardClauseRegistry clauses)
        {
            if (sources == null || clauses == null) throw new ArgumentNullException();
            return StandardAuthorityPolicyRegistry.Create(sources, clauses, new[]
            {
                new StandardAuthorityPolicyDefinition { PolicyId = "real.pcd-pd-135.rev1.default", SourceId = "trc.pcd-pd-135.rev1", AuthorityClass = StandardAuthorityClasses.GoverningProject, EnforcementMode = StandardEnforcementModes.ConditionalReview, Rationale = "PCD-PD-135 Rev.1 is governing project/company criteria, but automatic enforcement requires explicit applicable deterministic clauses." },
                new StandardAuthorityPolicyDefinition { PolicyId = "real.ips-e-pr-190.rev1.2009.default", SourceId = "ips.e-pr-190.rev1.2009", AuthorityClass = StandardAuthorityClasses.LegacyConflict, EnforcementMode = StandardEnforcementModes.NeverAutoEnforce, Rationale = "IPS E-PR-190(1), March 2009 is retained as a legacy/conflict reference and must never auto-enforce." }
            });
        }
    }
}
