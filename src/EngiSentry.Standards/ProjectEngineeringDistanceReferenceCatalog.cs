using System.Collections.Generic;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectEngineeringDistanceReferenceContext
    {
        public StandardSourceRegistry SourceRegistry { get; }
        public StandardClauseRegistry ClauseRegistry { get; }
        public StandardRulePackResolution Resolution { get; }
        internal ProjectEngineeringDistanceReferenceContext(StandardSourceRegistry sourceRegistry, StandardClauseRegistry clauseRegistry, StandardRulePackResolution resolution) { SourceRegistry = sourceRegistry; ClauseRegistry = clauseRegistry; Resolution = resolution; }
    }
    public static class ProjectEngineeringDistanceReferenceCatalog
    {
        public static ProjectEngineeringDistanceReferenceContext Create()
        {
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] {
                new StandardSourceDefinition { SourceId="trc.pcd-pd-135.rev1", DocumentNumber="PCD-PD-135", Title="PIPING DESIGN CRITERIA", Issuer="Tehran Raymand Consulting Engineers", RevisionOrEdition="1", SourceFileName="PCD-PD-135.pdf", SourceSha256="A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E" },
                new StandardSourceDefinition { SourceId="ips.e-pr-190.rev1.2009", DocumentNumber="IPS-E-PR-190(1)", Title="ENGINEERING STANDARD FOR LAYOUT AND SPACING", Issuer="Iranian Ministry of Petroleum", RevisionOrEdition="First Revision / March 2009", SourceFileName="IPS E-PR-190.pdf", SourceSha256="FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152" }});
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] {
                Clause("trc.pcd-pd-135.3.3.9.b","trc.pcd-pd-135.rev1","3.3.9(b) — Roadways","3.3.9 — Roadways",16,"Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions."),
                Clause("ips.e-pr-190.8.2.3","ips.e-pr-190.rev1.2009","8.2(3) — Roadways","8.2 — Roadways",22,"Access roads shall be at least 3 m from processing equipment between road edges to prevent vehicle collisions."),
                Clause("trc.pcd-pd-135.3.3.10.a","trc.pcd-pd-135.rev1","3.3.10(a) — Overall Concerns","3.3.10 — Overall Concerns",16,"The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m."),
                Clause("ips.e-pr-190.f.11.2","ips.e-pr-190.rev1.2009","f.11.2 — Security fence","f.11 — Security fence",21,"The minimum space between security fence and units’ boundary shall be 20 m, and between security fence and equipment shall be 30 m.") });
            EngineeringDistanceCandidateCatalog catalog = EngineeringDistanceCandidateCatalog.Create(clauses, EngineeringDistanceCandidateCatalog.BuiltInCandidates);
            StandardRulePackRegistry packs = StandardRulePackRegistry.Create(clauses, catalog.ToStandardRulePacks());
            StandardRulePackResolution resolution = StandardRulePackResolver.Resolve(packs, new[] { new StandardRulePackPrecedenceDefinition { PackId="candidate.project.access-road-clearance", Precedence=10 }, new StandardRulePackPrecedenceDefinition { PackId="candidate.project.security-fence-clearance", Precedence=10 } });
            return new ProjectEngineeringDistanceReferenceContext(sources, clauses, resolution);
        }
        private static StandardClauseDefinition Clause(string id, string source, string reference, string heading, int page, string text) => new StandardClauseDefinition { ClauseId=id, SourceId=source, ClauseReference=reference, Heading=heading, PageNumber=page, ClauseText=text };
    }
}
