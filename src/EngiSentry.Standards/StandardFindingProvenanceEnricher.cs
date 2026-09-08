using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardFindingProvenanceException : Exception
    {
        public StandardFindingProvenanceException(string message)
            : base(message) { }
    }

    public static class StandardFindingProvenanceEnricher
    {
        public static void Apply(QaReport report, StandardRulePackResolution resolution, StandardClauseRegistry clauseRegistry, StandardSourceRegistry sourceRegistry)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));
            if (report.Findings == null) throw new StandardFindingProvenanceException("QA report Findings are required");

            Dictionary<string, StandardFindingProvenance> templates = BuildTemplates(resolution, clauseRegistry, sourceRegistry);
            foreach (QaFinding finding in report.Findings)
            {
                finding.StandardsProvenance = templates.TryGetValue(finding.RuleId, out StandardFindingProvenance? template)
                    ? Copy(template)
                    : null;
            }
        }

        private static Dictionary<string, StandardFindingProvenance> BuildTemplates(StandardRulePackResolution resolution, StandardClauseRegistry clauseRegistry, StandardSourceRegistry sourceRegistry)
        {
            if (resolution.EffectiveRules == null) throw new StandardFindingProvenanceException("standard finding provenance EffectiveRules is required");

            var templates = new Dictionary<string, StandardFindingProvenance>(StringComparer.Ordinal);
            foreach (EffectiveStandardRuleDefinition effectiveRule in resolution.EffectiveRules)
            {
                Validate(effectiveRule);
                if (templates.ContainsKey(effectiveRule.RuleId)) throw new StandardFindingProvenanceException("duplicate standard finding provenance RuleId: " + effectiveRule.RuleId);

                var clauseIds = new HashSet<string>(StringComparer.Ordinal);
                var citations = new List<StandardClauseCitation>();
                foreach (string clauseId in effectiveRule.ClauseIds)
                {
                    if (string.IsNullOrWhiteSpace(clauseId)) throw new StandardFindingProvenanceException("standard finding provenance ClauseId is required: " + effectiveRule.RuleId);
                    if (!clauseRegistry.TryGet(clauseId, out StandardClauseDefinition? clause) || clause == null)
                    {
                        throw new StandardFindingProvenanceException("standard finding provenance references unknown ClauseId: " + clauseId);
                    }

                    if (!clauseIds.Add(clauseId)) throw new StandardFindingProvenanceException("duplicate standard finding provenance ClauseId: " + effectiveRule.RuleId + " | " + clauseId);
                    if (!sourceRegistry.TryGet(clause.SourceId, out StandardSourceDefinition? source) || source == null)
                    {
                        throw new StandardFindingProvenanceException("standard finding provenance references unknown SourceId: " + clause.SourceId);
                    }

                    citations.Add(new StandardClauseCitation
                    {
                        ClauseId = clause.ClauseId,
                        SourceId = clause.SourceId,
                        ClauseReference = clause.ClauseReference,
                        ClauseText = clause.ClauseText,
                        Heading = clause.Heading,
                        PageNumber = clause.PageNumber,
                        DocumentNumber = source.DocumentNumber,
                        SourceTitle = source.Title,
                        Issuer = source.Issuer,
                        RevisionOrEdition = source.RevisionOrEdition,
                        SourceFileName = source.SourceFileName,
                        SourceSha256 = source.SourceSha256
                    });
                }

                templates.Add(effectiveRule.RuleId, new StandardFindingProvenance
                {
                    RuleId = effectiveRule.RuleId,
                    RuleTitle = effectiveRule.Title,
                    WinningPackId = effectiveRule.WinningPackId,
                    WinningPrecedence = effectiveRule.WinningPrecedence,
                    OverriddenPackIds = effectiveRule.OverriddenPackIds.ToList(),
                    Clauses = citations.OrderBy(citation => citation.ClauseId, StringComparer.Ordinal).ToList()
                });
            }

            return templates;
        }

        private static void Validate(EffectiveStandardRuleDefinition? effectiveRule)
        {
            if (effectiveRule == null) throw new StandardFindingProvenanceException("standard finding provenance contains a null effective rule");
            if (string.IsNullOrWhiteSpace(effectiveRule.RuleId)) throw new StandardFindingProvenanceException("standard finding provenance RuleId is required");
            if (string.IsNullOrWhiteSpace(effectiveRule.WinningPackId)) throw new StandardFindingProvenanceException("standard finding provenance WinningPackId is required: " + effectiveRule.RuleId);
            if (effectiveRule.WinningPrecedence <= 0) throw new StandardFindingProvenanceException("standard finding provenance WinningPrecedence must be greater than zero: " + effectiveRule.RuleId);
            if (effectiveRule.ClauseIds == null) throw new StandardFindingProvenanceException("standard finding provenance ClauseIds is required: " + effectiveRule.RuleId);
            if (effectiveRule.ClauseIds.Count == 0) throw new StandardFindingProvenanceException("standard finding provenance requires at least one ClauseId: " + effectiveRule.RuleId);
            if (effectiveRule.OverriddenPackIds == null) throw new StandardFindingProvenanceException("standard finding provenance OverriddenPackIds is required: " + effectiveRule.RuleId);
        }

        private static StandardFindingProvenance Copy(StandardFindingProvenance provenance)
        {
            return new StandardFindingProvenance
            {
                RuleId = provenance.RuleId,
                RuleTitle = provenance.RuleTitle,
                WinningPackId = provenance.WinningPackId,
                WinningPrecedence = provenance.WinningPrecedence,
                OverriddenPackIds = provenance.OverriddenPackIds.ToList(),
                Clauses = provenance.Clauses.Select(citation => new StandardClauseCitation
                {
                    ClauseId = citation.ClauseId,
                    SourceId = citation.SourceId,
                    ClauseReference = citation.ClauseReference,
                    ClauseText = citation.ClauseText,
                    Heading = citation.Heading,
                    PageNumber = citation.PageNumber,
                    DocumentNumber = citation.DocumentNumber,
                    SourceTitle = citation.SourceTitle,
                    Issuer = citation.Issuer,
                    RevisionOrEdition = citation.RevisionOrEdition,
                    SourceFileName = citation.SourceFileName,
                    SourceSha256 = citation.SourceSha256
                }).ToList()
            };
        }
    }
}
