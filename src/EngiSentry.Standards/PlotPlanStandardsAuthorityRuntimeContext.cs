using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class PlotPlanStandardsAuthorityRuntimeContext
    {
        internal PlotPlanStandardsAuthorityRuntimeContext(
            StandardSourceRegistry sourceRegistry,
            StandardClauseRegistry clauseRegistry,
            StandardRulePackResolution ruleResolution,
            StandardAuthorityPolicyRegistry policyRegistry,
            StandardAuthorityResolution authorityResolution,
            StandardExecutionGateResolution executionGate,
            StandardAuthorityReviewProjection reviewProjection)
        {
            SourceRegistry = sourceRegistry;
            ClauseRegistry = clauseRegistry;
            RuleResolution = ruleResolution;
            PolicyRegistry = policyRegistry;
            AuthorityResolution = authorityResolution;
            ExecutionGate = executionGate;
            ReviewProjection = reviewProjection;
        }

        public StandardSourceRegistry SourceRegistry { get; }
        public StandardClauseRegistry ClauseRegistry { get; }
        public StandardRulePackResolution RuleResolution { get; }
        public StandardAuthorityPolicyRegistry PolicyRegistry { get; }
        public StandardAuthorityResolution AuthorityResolution { get; }
        public StandardExecutionGateResolution ExecutionGate { get; }
        public StandardAuthorityReviewProjection ReviewProjection { get; }
    }

    public static class PlotPlanStandardsAuthorityRuntimeContextCatalog
    {
        public static PlotPlanStandardsAuthorityRuntimeContext Create()
        {
            StandardSourceRegistry sourceRegistry = StandardSourceRegistry.Create(Sources());
            StandardClauseRegistry clauseRegistry = StandardClauseRegistry.Create(sourceRegistry, Clauses());
            StandardAuthorityPolicyRegistry policyRegistry = PlotPlanStandardsAuthorityPolicyCatalog.Create(sourceRegistry, clauseRegistry);
            StandardRulePackDefinition pack = new StandardRulePackDefinition
            {
                PackId = "runtime.plotplan.real-standards-authority",
                Name = "Plot Plan Real Standards Authority Runtime Context",
                Rules = new List<StandardRuleDefinition>
                {
                    Rule("runtime.plotplan.authority.pcd.general", "PCD general authority context", "trc.pcd-pd-135.3.3.9.a"),
                    Rule("runtime.plotplan.authority.pcd.flare", "PCD flare authority context", "trc.pcd-pd-135.3.4.13.b"),
                    Rule("runtime.plotplan.authority.epr.legacy", "E-PR-190 legacy authority context", "ips.e-pr-190.1.1"),
                    Rule("runtime.plotplan.authority.gsf.primary", "G-SF-150 primary authority context", "ips.g-sf-150.1.scope")
                }
            };
            StandardRulePackResolution ruleResolution = StandardRulePackResolver.Resolve(
                StandardRulePackRegistry.Create(clauseRegistry, new[] { pack }),
                new[] { new StandardRulePackPrecedenceDefinition { PackId = pack.PackId, Precedence = 10 } });
            StandardAuthorityResolution authorityResolution = StandardAuthorityResolver.Resolve(ruleResolution, clauseRegistry, policyRegistry);
            StandardExecutionGateResolution executionGate = StandardAuthorityExecutionGate.Evaluate(authorityResolution);
            StandardAuthorityReviewProjection reviewProjection = StandardAuthorityReviewProjector.Create(authorityResolution);

            return new PlotPlanStandardsAuthorityRuntimeContext(
                sourceRegistry,
                clauseRegistry,
                ruleResolution,
                policyRegistry,
                authorityResolution,
                executionGate,
                reviewProjection);
        }

        private static IEnumerable<StandardSourceDefinition> Sources()
        {
            return new[]
            {
                new StandardSourceDefinition { SourceId = "trc.pcd-pd-135.rev1", DocumentNumber = "PCD-PD-135", Title = "PIPING DESIGN CRITERIA", Issuer = "Tehran Raymand Consulting Engineers", RevisionOrEdition = "1", SourceFileName = "PCD-PD-135.pdf", SourceSha256 = "A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" },
                new StandardSourceDefinition { SourceId = "ips.e-pr-190.rev1.2009", DocumentNumber = "IPS-E-PR-190(1)", Title = "ENGINEERING STANDARD FOR LAYOUT AND SPACING", Issuer = "Iranian Ministry of Petroleum", RevisionOrEdition = "First Revision / March 2009", SourceFileName = "IPS E-PR-190.pdf", SourceSha256 = "FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" },
                new StandardSourceDefinition { SourceId = "ips.g-sf-150.rev1.2023", DocumentNumber = "IPS-G-SF-150(1):2023 / INSO 23591:1402", Title = "Petroleum industry – Requirements for spacing and layout of industry and equipment", Issuer = "Iranian Ministry of Petroleum", RevisionOrEdition = "First Edition / 1402 / 2023", SourceFileName = "IPS G-SF-150-23591.pdf", SourceSha256 = "B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B" }
            };
        }

        private static IEnumerable<StandardClauseDefinition> Clauses()
        {
            const string pcd = "trc.pcd-pd-135.rev1";
            return new[]
            {
                Clause("trc.pcd-pd-135.3.3.9.a", pcd, "3.3.9(a) — Roadways", "Road and access ways shall offer easy access for mobile equipment during construction and maintenance, fire fighting and emergency escape in a fire situation.", 15),
                Clause("ips.e-pr-190.1.1", "ips.e-pr-190.rev1.2009", "1.1", "This Standard Specification covers the basic requirements of the plant layout and spacing of oil & gas refineries, petrochemical and similar chemical plants to ensure safety and fire prevention together with ease of operation and maintenance.", 12),
                Clause("ips.g-sf-150.1.scope", "ips.g-sf-150.rev1.2023", "1 — هدف و دامنه کاربرد", "هدف از تدوین این استاندارد، تعیین حداقل الزامات ایمنی برای جانمایی و تعیین فاصله‌گذاری تجهیزات و تأسیسات فرایندی صنعت نفت در بخش خشکی است.", 9),
                Clause("trc.pcd-pd-135.3.3.7.a", pcd, "3.3.7(a) — Flare and Burn-pit Area", "Flare and burn pit shall be located at the end of the site with sufficient distance to prevent personnel hazard.", 14),
                Clause("trc.pcd-pd-135.3.3.7.b", pcd, "3.3.7(b) — Flare and Burn-pit Area", "Flaring of process units that generate combustible by products (gas or vapor), in the when required, sufficient space must be left between the flare and the nearest adjacent equipment to keep the radiant heat flux below allowable limits.", 14),
                Clause("trc.pcd-pd-135.3.3.7.c", pcd, "3.3.7(c) — Flare and Burn-pit Area", "Ignited flares are sources of ignition and should not be located down-wind of sources of flammable gas (or should be located far enough from them), except where flare are high enough to avoid any risk. Conversely, the risk of flame-out where the flares become sources of fuel (i.e. release source) shall be considered.", 15),
                Clause("trc.pcd-pd-135.3.4.13.a", pcd, "3.4.13(a) — Flare", "There should be a totally clear area surrounding the flare stack. The size of this area is dependent on heat intensity requirements which depend upon the height of the flare and relieving quantity.", 24),
                Clause("trc.pcd-pd-135.3.4.13.b", pcd, "3.4.13(b) — Flare", "As a preliminary assumption, the flare stack should be located at least 90 meters for HP/MP flare and 50 meters form LP flare from other facilities. After preliminary calculation by Safety/Process Departments, this assumption will be replaced by a more precise pre-calculated data and finally after vendor selection, the final data will be attained. Common roads and piping system, which serves something other than flare system, shall not cross this area.", 24),
                Clause("trc.pcd-pd-135.3.4.13.c", pcd, "3.4.13(c) — Flare", "The ignition panels shall be located outside of the clear area around the stacks.", 24)
            };
        }

        private static StandardClauseDefinition Clause(string clauseId, string sourceId, string clauseReference, string clauseText, int pageNumber)
        {
            return new StandardClauseDefinition { ClauseId = clauseId, SourceId = sourceId, ClauseReference = clauseReference, ClauseText = clauseText, PageNumber = pageNumber };
        }

        private static StandardRuleDefinition Rule(string ruleId, string title, string clauseId)
        {
            return new StandardRuleDefinition { RuleId = ruleId, Title = title, ClauseIds = new List<string> { clauseId } };
        }
    }
}
