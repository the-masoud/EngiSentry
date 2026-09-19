using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class StandardAuthorityReviewPackageBuilder
    {
        public static StandardAuthorityReviewPackage Create(PlotPlanQaReport report, string drawingName, DateTime scanTimestampUtc)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (string.IsNullOrWhiteSpace(drawingName)) throw new ArgumentException("drawing name is required", nameof(drawingName));
            if (report.StandardsAuthority == null) throw new InvalidOperationException("Plot Plan QA report requires StandardsAuthority for review package creation.");
            if (report.ReviewProjection == null) throw new InvalidOperationException("Plot Plan QA report requires ReviewProjection for review package creation.");
            DateTime normalizedScanTimestampUtc = NormalizeToPersistedUtcMillisecond(scanTimestampUtc);

            return new StandardAuthorityReviewPackage
            {
                SchemaVersion = "1.0",
                DrawingName = drawingName,
                ScanTimestampUtc = normalizedScanTimestampUtc,
                StandardsAuthority = CopyAuthority(report.StandardsAuthority),
                ReviewProjection = CopyReviewProjection(report.ReviewProjection)
            };
        }

        private static DateTime NormalizeToPersistedUtcMillisecond(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            long ticks = utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond;
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static PlotPlanStandardsAuthorityReport CopyAuthority(PlotPlanStandardsAuthorityReport source)
        {
            return new PlotPlanStandardsAuthorityReport
            {
                AuthorityResolution = new StandardAuthorityResolution
                {
                    Rules = (source.AuthorityResolution?.Rules ?? new List<StandardRuleAuthorityResolution>())
                        .Select(CopyAuthorityRule)
                        .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
                        .ToList()
                },
                ExecutionGate = new StandardExecutionGateResolution
                {
                    Rules = (source.ExecutionGate?.Rules ?? new List<StandardRuleExecutionGateDecision>())
                        .Select(CopyGateRule)
                        .OrderBy(rule => rule.RuleId, StringComparer.Ordinal)
                        .ToList()
                }
            };
        }

        private static StandardRuleAuthorityResolution CopyAuthorityRule(StandardRuleAuthorityResolution source)
        {
            return new StandardRuleAuthorityResolution
            {
                RuleId = source.RuleId,
                EffectiveEnforcementMode = source.EffectiveEnforcementMode,
                Clauses = (source.Clauses ?? new List<StandardClauseAuthorityResolution>())
                    .Select(CopyClause)
                    .OrderBy(clause => clause.ClauseId, StringComparer.Ordinal)
                    .ToList()
            };
        }

        private static StandardRuleExecutionGateDecision CopyGateRule(StandardRuleExecutionGateDecision source)
        {
            return new StandardRuleExecutionGateDecision
            {
                RuleId = source.RuleId,
                EffectiveEnforcementMode = source.EffectiveEnforcementMode,
                AutoExecutionAllowed = source.AutoExecutionAllowed,
                DecisionState = source.DecisionState
            };
        }

        private static StandardClauseAuthorityResolution CopyClause(StandardClauseAuthorityResolution source)
        {
            return new StandardClauseAuthorityResolution
            {
                ClauseId = source.ClauseId,
                SourceId = source.SourceId,
                PolicyId = source.PolicyId,
                PolicyClauseId = source.PolicyClauseId,
                AuthorityClass = source.AuthorityClass,
                EnforcementMode = source.EnforcementMode,
                Rationale = source.Rationale,
                SourceDocumentNumber = source.SourceDocumentNumber,
                SourceTitle = source.SourceTitle,
                SourceIssuer = source.SourceIssuer,
                SourceRevisionOrEdition = source.SourceRevisionOrEdition,
                SourceFileName = source.SourceFileName,
                SourceSha256 = source.SourceSha256,
                ClauseReference = source.ClauseReference,
                ClauseText = source.ClauseText,
                ClauseHeading = source.ClauseHeading,
                PageNumber = source.PageNumber
            };
        }

        private static StandardAuthorityReviewProjection CopyReviewProjection(StandardAuthorityReviewProjection source)
        {
            return new StandardAuthorityReviewProjection
            {
                ReviewItems = (source.ReviewItems ?? new List<StandardAuthorityReviewItem>())
                    .Select(CopyReviewItem)
                    .OrderBy(item => item.RuleId, StringComparer.Ordinal)
                    .ToList()
            };
        }

        private static StandardAuthorityReviewItem CopyReviewItem(StandardAuthorityReviewItem source)
        {
            return new StandardAuthorityReviewItem
            {
                RuleId = source.RuleId,
                EffectiveEnforcementMode = source.EffectiveEnforcementMode,
                DecisionState = source.DecisionState,
                Message = source.Message,
                Clauses = (source.Clauses ?? new List<StandardClauseAuthorityResolution>())
                    .Select(CopyClause)
                    .OrderBy(clause => clause.ClauseId, StringComparer.Ordinal)
                    .ToList()
            };
        }
    }
}
