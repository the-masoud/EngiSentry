using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Rules;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    public sealed class ProjectEngineeringDistanceRuntimeException : Exception { public ProjectEngineeringDistanceRuntimeException(string message) : base(message) { } }
    public static class ProjectEngineeringDistanceRuntime
    {
        public static QaReport Evaluate(ProjectDistanceCriteria criteria, PlotPlanModel plotPlan, QaReport baseQa)
        {
            return EvaluateCore(criteria, plotPlan, baseQa, null);
        }

        public static QaReport Evaluate(ProjectDistanceCriteria criteria, PlotPlanModel plotPlan, QaReport baseQa, ProjectDistanceGovernanceAggregateResult governance)
        {
            if (governance == null) throw new ArgumentNullException(nameof(governance));
            ProjectDistanceCriteria valid = ProjectDistanceCriteriaStore.ValidateAndCopy(criteria);
            if (valid.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new ProjectEngineeringDistanceRuntimeException("governed engineering distance runtime requires schema version " + ProjectDistanceCriteriaDefaults.CurrentSchemaVersion);
            ProjectDistanceGovernanceAggregateResult detached = CopyGovernance(governance, valid);
            return EvaluateCore(valid, plotPlan, baseQa, detached);
        }

        private static QaReport EvaluateCore(ProjectDistanceCriteria criteria, PlotPlanModel plotPlan, QaReport baseQa, ProjectDistanceGovernanceAggregateResult? governance)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria)); if (plotPlan == null) throw new ArgumentNullException(nameof(plotPlan)); if (baseQa == null) throw new ArgumentNullException(nameof(baseQa));
            if (baseQa.RuleResults == null || baseQa.Findings == null) throw new ProjectEngineeringDistanceRuntimeException("base QA RuleResults and Findings are required");
            if (baseQa.EngineeringDistanceSummary != null) throw new ProjectEngineeringDistanceRuntimeException("base QA already contains engineering distance summary");
            ProjectDistanceCriteria valid = ProjectDistanceCriteriaStore.ValidateAndCopy(criteria);
            EngineeringDistanceRuleRegistry registry;
            bool projectOnly = valid.SchemaVersion == ProjectDistanceCriteriaDefaults.CurrentSchemaVersion;
            ProjectEngineeringDistanceReferenceContext? context = null;
            if (projectOnly) registry = EngineeringDistanceRuleRegistry.CreateProjectCriteria(ProjectDistanceRuleBinder.Create(valid));
            else { context = ProjectEngineeringDistanceReferenceCatalog.Create(); registry = EngineeringDistanceRuleRegistry.Create(context.Resolution, ProjectDistanceRuleBinder.Create(valid)); }
            List<EngineeringDistanceRuleEvaluation> evaluations = EngineeringDistanceRuleEvaluator.Evaluate(registry, plotPlan).OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            QaReport engineering = projectOnly ? EngineeringDistanceQaIntegrator.CreateProjectCriteria(evaluations) : EngineeringDistanceQaIntegrator.Create(evaluations, context!.Resolution, context.ClauseRegistry, context.SourceRegistry);
            List<QaRuleResult> rules = baseQa.RuleResults.Concat(engineering.RuleResults).OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            List<QaFinding> findings = baseQa.Findings.Concat(engineering.Findings).OrderBy(x => x.RuleId, StringComparer.Ordinal).ThenBy(x => x.SubjectId, StringComparer.Ordinal).ThenBy(x => x.FindingId, StringComparer.Ordinal).ToList();
            RequireUnique(rules.Select(x => x.RuleId), "RuleId"); RequireUnique(findings.Select(x => x.FindingId), "FindingId");
            return new QaReport { Drawing=baseQa.Drawing, ExecutionStatus=baseQa.ExecutionStatus, ProfileResolution=baseQa.ProfileResolution, RuleResults=rules, Findings=findings, OverallStatus=Overall(rules, findings), Statistics=Statistics(rules, findings), EngineeringDistanceSummary=new EngineeringDistanceRunSummary { SourceKind="ProjectDistanceCriteria", ProjectDirectory=valid.ProjectDirectory, SchemaVersion=valid.SchemaVersion, AccessRoadEdgeMinimumMeters=valid.AccessRoadEdgeMinimumMeters, SecurityFenceMinimumMeters=valid.SecurityFenceMinimumMeters, UnitBoundaryMinimumMeters=valid.UnitBoundaryMinimumMeters, Evaluations=evaluations, Governance=governance == null ? null : CopyGovernance(governance, valid) } };
        }
        private static ProjectDistanceGovernanceAggregateResult CopyGovernance(ProjectDistanceGovernanceAggregateResult governance, ProjectDistanceCriteria criteria)
        {
            if (governance.ProjectDirectory != criteria.ProjectDirectory || governance.Items == null) throw new ProjectEngineeringDistanceRuntimeException("governance project scope is invalid");
            IReadOnlyList<EngineeringDistanceRuleDefinition> rules = ProjectDistanceRuleBinder.Create(criteria); var candidateIds = new HashSet<string>(StringComparer.Ordinal); var ruleIds = new HashSet<string>(StringComparer.Ordinal); var items = new List<ProjectDistanceGovernanceAggregateItem>();
            foreach (ProjectDistanceGovernanceAggregateItem item in governance.Items)
            {
                if (item == null || item.Resolution == null) throw new ProjectEngineeringDistanceRuntimeException("governance item is invalid");
                EngineeringDistanceCandidateDefinition[] candidates = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Where(x => x.CandidateId == item.CandidateId && x.RuleId == item.CandidateRuleId).ToArray();
                if (candidates.Length != 1 || item.Resolution.CandidateId != item.CandidateId || item.Resolution.ProjectRuleId != item.ProjectRuleId) throw new ProjectEngineeringDistanceRuntimeException("governance item does not match schema-v2 project rules");
                EngineeringDistanceCandidateDefinition candidate = candidates[0];
                EngineeringDistanceRuleDefinition[] matches = rules.Where(x => x.RuleId == item.ProjectRuleId && x.MeasurementKind == candidate.MeasurementKind && x.RequiredMeasurementBasisKey == candidate.RequiredMeasurementBasisKey).ToArray();
                if (matches.Length != 1 || !candidateIds.Add(item.CandidateId) || !ruleIds.Add(item.ProjectRuleId)) throw new ProjectEngineeringDistanceRuntimeException("governance item does not match schema-v2 project rules");
                items.Add(CopyItem(item));
            }
            return new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = governance.ProjectDirectory, Items = items.OrderBy(x => x.CandidateId, StringComparer.Ordinal).ThenBy(x => x.ProjectRuleId, StringComparer.Ordinal).ToList() };
        }
        private static ProjectDistanceGovernanceAggregateItem CopyItem(ProjectDistanceGovernanceAggregateItem x) => new ProjectDistanceGovernanceAggregateItem { CandidateId=x.CandidateId, CandidateRuleId=x.CandidateRuleId, ProjectRuleId=x.ProjectRuleId, ApprovalRecordId=x.ApprovalRecordId, HistoryState=x.HistoryState, CurrentnessState=x.CurrentnessState, AuthorityState=x.AuthorityState, VerifiedPolicyArtifactId=x.VerifiedPolicyArtifactId, VerifiedPolicyIssuerId=x.VerifiedPolicyIssuerId, VerifiedPolicyKeyId=x.VerifiedPolicyKeyId, Resolution=CopyResolution(x.Resolution) };
        private static ProjectDistanceGovernedRuleResolution CopyResolution(ProjectDistanceGovernedRuleResolution x) => new ProjectDistanceGovernedRuleResolution { State=x.State, ProjectRuleId=x.ProjectRuleId, CandidateId=x.CandidateId, AuthorityState=x.AuthorityState, GovernanceProvenance=x.GovernanceProvenance == null ? null : new ProjectDistanceGovernanceProvenance { ProjectRuleId=x.GovernanceProvenance.ProjectRuleId, CandidateId=x.GovernanceProvenance.CandidateId, CandidateRuleId=x.GovernanceProvenance.CandidateRuleId, ApprovalRecordId=x.GovernanceProvenance.ApprovalRecordId, PolicyId=x.GovernanceProvenance.PolicyId, AuthorityBasisReference=x.GovernanceProvenance.AuthorityBasisReference, AuthorityState=x.GovernanceProvenance.AuthorityState, ApprovedMinimumDistanceMeters=x.GovernanceProvenance.ApprovedMinimumDistanceMeters } };
        private static void RequireUnique(IEnumerable<string> values, string kind) { foreach (IGrouping<string,string> group in values.GroupBy(x=>x, StringComparer.Ordinal)) if (group.Count()!=1) throw new ProjectEngineeringDistanceRuntimeException("duplicate combined QA " + kind + ": " + group.Key); }
        private static string Overall(List<QaRuleResult> rules, List<QaFinding> findings) => findings.Any(x=>x.Severity=="Error") ? "FAIL" : findings.Count>0 ? "REVIEW_REQUIRED" : rules.Any(x=>x.Status=="INSUFFICIENT_EVIDENCE") ? "INSUFFICIENT_EVIDENCE" : "PASS";
        private static QaStatistics Statistics(List<QaRuleResult> rules, List<QaFinding> findings) => new QaStatistics { RuleCount=rules.Count, RulesPassed=rules.Count(x=>x.Status=="PASS"), RulesWithFindings=rules.Count(x=>x.Status=="FINDINGS"), RulesNotApplicable=rules.Count(x=>x.Status=="NOT_APPLICABLE"), RulesInsufficientEvidence=rules.Count(x=>x.Status=="INSUFFICIENT_EVIDENCE"), TotalFindings=findings.Count, InfoFindings=findings.Count(x=>x.Severity=="Info"), WarningFindings=findings.Count(x=>x.Severity=="Warning"), ErrorFindings=findings.Count(x=>x.Severity=="Error") };
    }
}
