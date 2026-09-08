using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class PlotPlanStandardsAuthorityPolicyCatalog
    {
        public static StandardAuthorityPolicyRegistry Create(StandardSourceRegistry sourceRegistry, StandardClauseRegistry clauseRegistry)
        {
            const string pcd = "trc.pcd-pd-135.rev1";
            const string flareRationale = "This PCD-PD-135 clause is a flare-location term covered by the provider-reported reliability warning and must never drive an automatic verdict.";
            return StandardAuthorityPolicyRegistry.Create(sourceRegistry, clauseRegistry, new[]
            {
                new StandardAuthorityPolicyDefinition { PolicyId = "real.pcd-pd-135.rev1.default", SourceId = pcd, AuthorityClass = StandardAuthorityClasses.GoverningProject, EnforcementMode = StandardEnforcementModes.ConditionalReview, Rationale = "PCD-PD-135 Rev.1 is governing project/company criteria, but automatic enforcement requires explicit applicable deterministic clauses." },
                new StandardAuthorityPolicyDefinition { PolicyId = "real.ips-e-pr-190.rev1.2009.default", SourceId = "ips.e-pr-190.rev1.2009", AuthorityClass = StandardAuthorityClasses.LegacyConflict, EnforcementMode = StandardEnforcementModes.NeverAutoEnforce, Rationale = "IPS E-PR-190(1), March 2009 is retained as a legacy/conflict reference and must never auto-enforce." },
                new StandardAuthorityPolicyDefinition { PolicyId = "real.ips-g-sf-150.rev1.2023.default", SourceId = "ips.g-sf-150.rev1.2023", AuthorityClass = StandardAuthorityClasses.ActivePrimary, EnforcementMode = StandardEnforcementModes.ConditionalReview, Rationale = "IPS G-SF-150(1):2023 / INSO 23591:1402 is the active primary Iran spacing/layout baseline; controlled-copy and applicability conditions require review by default." },
                Flare("3.3.7.a", pcd, flareRationale),
                Flare("3.3.7.b", pcd, flareRationale),
                Flare("3.3.7.c", pcd, flareRationale),
                Flare("3.4.13.a", pcd, flareRationale),
                Flare("3.4.13.b", pcd, flareRationale),
                Flare("3.4.13.c", pcd, flareRationale)
            });
        }

        private static StandardAuthorityPolicyDefinition Flare(string suffix, string sourceId, string rationale)
        {
            return new StandardAuthorityPolicyDefinition
            {
                PolicyId = "real.pcd-pd-135.rev1.flare." + suffix,
                SourceId = sourceId,
                ClauseId = "trc.pcd-pd-135." + suffix,
                AuthorityClass = StandardAuthorityClasses.Quarantined,
                EnforcementMode = StandardEnforcementModes.NeverAutoEnforce,
                Rationale = rationale
            };
        }
    }
}
