using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceQaIntegrationException : Exception
    {
        public EngineeringDistanceQaIntegrationException(string message) : base(message) { }
    }

    public static class EngineeringDistanceQaIntegrator
    {
        public static QaReport Create(IEnumerable<EngineeringDistanceRuleEvaluation> evaluations, StandardRulePackResolution standardsResolution, StandardClauseRegistry clauseRegistry, StandardSourceRegistry sourceRegistry)
        {
            if (evaluations == null) throw new ArgumentNullException(nameof(evaluations));
            if (standardsResolution == null) throw new ArgumentNullException(nameof(standardsResolution));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));

            HashSet<string> effectiveRuleIds = ValidateEffectiveRules(standardsResolution);
            List<EngineeringDistanceRuleEvaluation> supplied = evaluations.ToList();
            ValidateEvaluations(supplied, effectiveRuleIds);

            var report = new QaReport { Drawing = null, ExecutionStatus = "Complete", ProfileResolution = null };
            foreach (EngineeringDistanceRuleEvaluation evaluation in supplied.OrderBy(x => x.RuleId, StringComparer.Ordinal))
            {
                report.RuleResults.Add(new QaRuleResult
                {
                    RuleId = evaluation.RuleId,
                    Severity = "Warning",
                    EvaluatedSubjectCount = evaluation.EvaluatedSubjectCount,
                    FindingCount = evaluation.ViolationCount,
                    Status = QaStatus(evaluation.Status)
                });

                foreach (EngineeringDistanceViolation violation in evaluation.Violations.OrderBy(x => x.PlacementId, StringComparer.Ordinal).ThenBy(x => x.MeasurementId, StringComparer.Ordinal))
                {
                    report.Findings.Add(CreateFinding(evaluation, violation));
                }
            }

            report.RuleResults = report.RuleResults.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            report.Findings = report.Findings.OrderBy(x => x.RuleId, StringComparer.Ordinal).ThenBy(x => x.SubjectId, StringComparer.Ordinal).ThenBy(x => x.FindingId, StringComparer.Ordinal).ToList();
            StandardFindingProvenanceEnricher.Apply(report, standardsResolution, clauseRegistry, sourceRegistry);
            foreach (QaFinding finding in report.Findings)
            {
                EngineeringDistanceRuleEvaluation evaluation = supplied.Single(x => x.RuleId == finding.RuleId);
                if (evaluation.CriteriaProvenance != null)
                {
                    finding.StandardsProvenance = null;
                    if (finding.Evidence.EngineeringDistanceEvidence!.CriteriaProvenance == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA finding is missing criteria provenance: " + finding.FindingId);
                }
                else if (finding.StandardsProvenance == null || !string.Equals(finding.StandardsProvenance.RuleId, finding.RuleId, StringComparison.Ordinal))
                {
                    throw new EngineeringDistanceQaIntegrationException("engineering distance QA finding is missing standards provenance: " + finding.FindingId);
                }
            }

            report.OverallStatus = report.Findings.Count > 0 ? "REVIEW_REQUIRED" : report.RuleResults.Any(x => x.Status == "INSUFFICIENT_EVIDENCE") ? "INSUFFICIENT_EVIDENCE" : "PASS";
            report.Statistics = new QaStatistics
            {
                RuleCount = report.RuleResults.Count,
                RulesPassed = report.RuleResults.Count(x => x.Status == "PASS"),
                RulesWithFindings = report.RuleResults.Count(x => x.Status == "FINDINGS"),
                RulesNotApplicable = 0,
                RulesInsufficientEvidence = report.RuleResults.Count(x => x.Status == "INSUFFICIENT_EVIDENCE"),
                TotalFindings = report.Findings.Count,
                InfoFindings = 0,
                WarningFindings = report.Findings.Count,
                ErrorFindings = 0
            };
            return report;
        }

        public static QaReport CreateProjectCriteria(IEnumerable<EngineeringDistanceRuleEvaluation> evaluations)
        {
            if (evaluations == null) throw new ArgumentNullException(nameof(evaluations));
            List<EngineeringDistanceRuleEvaluation> supplied = evaluations.ToList();
            ValidateEvaluations(supplied, null);
            if (supplied.Any(x => x.CriteriaProvenance == null)) throw new EngineeringDistanceQaIntegrationException("engineering distance project QA criteria provenance is required");
            var report = new QaReport { Drawing = null, ExecutionStatus = "Complete", ProfileResolution = null };
            foreach (EngineeringDistanceRuleEvaluation evaluation in supplied.OrderBy(x => x.RuleId, StringComparer.Ordinal))
            {
                report.RuleResults.Add(new QaRuleResult { RuleId = evaluation.RuleId, Severity = "Warning", EvaluatedSubjectCount = evaluation.EvaluatedSubjectCount, FindingCount = evaluation.ViolationCount, Status = QaStatus(evaluation.Status) });
                foreach (EngineeringDistanceViolation violation in evaluation.Violations.OrderBy(x => x.PlacementId, StringComparer.Ordinal).ThenBy(x => x.MeasurementId, StringComparer.Ordinal)) report.Findings.Add(CreateFinding(evaluation, violation));
            }
            report.RuleResults = report.RuleResults.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            report.Findings = report.Findings.OrderBy(x => x.RuleId, StringComparer.Ordinal).ThenBy(x => x.SubjectId, StringComparer.Ordinal).ThenBy(x => x.FindingId, StringComparer.Ordinal).ToList();
            report.OverallStatus = report.Findings.Count > 0 ? "REVIEW_REQUIRED" : report.RuleResults.Any(x => x.Status == "INSUFFICIENT_EVIDENCE") ? "INSUFFICIENT_EVIDENCE" : "PASS";
            report.Statistics = new QaStatistics { RuleCount = report.RuleResults.Count, RulesPassed = report.RuleResults.Count(x => x.Status == "PASS"), RulesWithFindings = report.RuleResults.Count(x => x.Status == "FINDINGS"), RulesNotApplicable = 0, RulesInsufficientEvidence = report.RuleResults.Count(x => x.Status == "INSUFFICIENT_EVIDENCE"), TotalFindings = report.Findings.Count, InfoFindings = 0, WarningFindings = report.Findings.Count, ErrorFindings = 0 };
            return report;
        }

        private static HashSet<string> ValidateEffectiveRules(StandardRulePackResolution standardsResolution)
        {
            if (standardsResolution.EffectiveRules == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA standards EffectiveRules are required");
            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EffectiveStandardRuleDefinition effectiveRule in standardsResolution.EffectiveRules)
            {
                if (effectiveRule == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA standards contains a null effective rule");
                if (string.IsNullOrWhiteSpace(effectiveRule.RuleId)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA standards RuleId is required");
                if (!ruleIds.Add(effectiveRule.RuleId)) throw new EngineeringDistanceQaIntegrationException("duplicate engineering distance QA standards RuleId: " + effectiveRule.RuleId);
            }
            return ruleIds;
        }

        private static void ValidateEvaluations(IEnumerable<EngineeringDistanceRuleEvaluation> evaluations, HashSet<string>? effectiveRuleIds)
        {
            var evaluationRuleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineeringDistanceRuleEvaluation evaluation in evaluations)
            {
                if (evaluation == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA contains a null evaluation");
                if (string.IsNullOrWhiteSpace(evaluation.RuleId)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA RuleId is required");
                if (!evaluationRuleIds.Add(evaluation.RuleId)) throw new EngineeringDistanceQaIntegrationException("duplicate engineering distance QA RuleId: " + evaluation.RuleId);
                if (effectiveRuleIds != null && !effectiveRuleIds.Contains(evaluation.RuleId)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA references unknown effective RuleId: " + evaluation.RuleId);
                if (string.IsNullOrWhiteSpace(evaluation.MeasurementKind)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA MeasurementKind is required: " + evaluation.RuleId);
                if (!IsSupportedMeasurementKind(evaluation.MeasurementKind)) throw new EngineeringDistanceQaIntegrationException("unsupported engineering distance QA MeasurementKind: " + evaluation.RuleId + " | " + evaluation.MeasurementKind);
                if (evaluation.Violations == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA Violations are required: " + evaluation.RuleId);
                if (evaluation.EvaluatedSubjectCount < 0) throw new EngineeringDistanceQaIntegrationException("engineering distance QA EvaluatedSubjectCount is invalid: " + evaluation.RuleId);
                if (evaluation.ViolationCount < 0) throw new EngineeringDistanceQaIntegrationException("engineering distance QA ViolationCount is invalid: " + evaluation.RuleId);
                if (evaluation.ViolationCount != evaluation.Violations.Count) throw new EngineeringDistanceQaIntegrationException("engineering distance QA ViolationCount is inconsistent: " + evaluation.RuleId);
                if (evaluation.ViolationCount > evaluation.EvaluatedSubjectCount) throw new EngineeringDistanceQaIntegrationException("engineering distance QA violation count exceeds evaluated subjects: " + evaluation.RuleId);
                ValidateStatus(evaluation);
                ValidateCriteriaProvenance(evaluation);
                ValidateViolations(evaluation);
            }
        }

        private static void ValidateStatus(EngineeringDistanceRuleEvaluation evaluation)
        {
            if (evaluation.Status == "PASS")
            {
                if (evaluation.EvaluatedSubjectCount <= 0 || evaluation.ViolationCount != 0) throw new EngineeringDistanceQaIntegrationException("engineering distance QA PASS state is inconsistent: " + evaluation.RuleId);
                return;
            }
            if (evaluation.Status == "VIOLATIONS")
            {
                if (evaluation.EvaluatedSubjectCount <= 0 || evaluation.ViolationCount <= 0) throw new EngineeringDistanceQaIntegrationException("engineering distance QA VIOLATIONS state is inconsistent: " + evaluation.RuleId);
                return;
            }
            if (evaluation.Status == "INSUFFICIENT_EVIDENCE")
            {
                if (evaluation.EvaluatedSubjectCount != 0 || evaluation.ViolationCount != 0) throw new EngineeringDistanceQaIntegrationException("engineering distance QA INSUFFICIENT_EVIDENCE state is inconsistent: " + evaluation.RuleId);
                return;
            }
            throw new EngineeringDistanceQaIntegrationException("unsupported engineering distance QA status: " + evaluation.RuleId + " | " + evaluation.Status);
        }

        private static void ValidateViolations(EngineeringDistanceRuleEvaluation evaluation)
        {
            var placementIds = new HashSet<string>(StringComparer.Ordinal);
            string expectedSubjectKind = evaluation.MeasurementKind == "EquipmentToRoad2D" ? "Road" : "Boundary";
            foreach (EngineeringDistanceViolation violation in evaluation.Violations)
            {
                if (violation == null) throw new EngineeringDistanceQaIntegrationException("engineering distance QA contains a null violation: " + evaluation.RuleId);
                if (string.IsNullOrWhiteSpace(violation.MeasurementId)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA violation MeasurementId is required: " + evaluation.RuleId);
                if (string.IsNullOrWhiteSpace(violation.PlacementId)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA violation PlacementId is required: " + evaluation.RuleId + " | " + violation.MeasurementId);
                if (!placementIds.Add(violation.PlacementId)) throw new EngineeringDistanceQaIntegrationException("duplicate engineering distance QA violation PlacementId: " + evaluation.RuleId + " | " + violation.PlacementId);
                if (!string.Equals(violation.SubjectKind, expectedSubjectKind, StringComparison.Ordinal)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA SubjectKind is inconsistent: " + evaluation.RuleId + " | " + violation.MeasurementId);
                if (!string.Equals(violation.MeasurementBasisKey, evaluation.RequiredMeasurementBasisKey, StringComparison.Ordinal)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA MeasurementBasisKey is inconsistent: " + evaluation.RuleId + " | " + violation.MeasurementId);
                if (!string.Equals(violation.ApplicabilityKey, evaluation.RequiredApplicabilityKey, StringComparison.Ordinal)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA ApplicabilityKey is inconsistent: " + evaluation.RuleId + " | " + violation.MeasurementId);
            }
        }

        private static QaFinding CreateFinding(EngineeringDistanceRuleEvaluation evaluation, EngineeringDistanceViolation violation)
        {
            return new QaFinding
            {
                FindingId = evaluation.RuleId + ":" + violation.PlacementId,
                RuleId = evaluation.RuleId,
                Severity = "Warning",
                Category = "EngineeringDistance",
                SubjectType = "EquipmentPlacement",
                SubjectId = violation.PlacementId,
                Message = evaluation.CriteriaProvenance == null
                    ? evaluation.MeasurementKind == "EquipmentToRoad2D" ? "Equipment placement is closer to Road geometry than the standards-backed minimum distance." : "Equipment placement is closer to Boundary geometry than the standards-backed minimum distance."
                    : evaluation.CriteriaProvenance.SchemaVersion == ProjectDistanceCriteriaDefaults.CurrentSchemaVersion ? ProjectMessage(evaluation.RequiredMeasurementBasisKey) : evaluation.MeasurementKind == "EquipmentToRoad2D" ? "Equipment placement is closer to Road geometry than the configured project minimum distance." : "Equipment placement is closer to Boundary geometry than the configured project minimum distance.",
                SourceHandles = new[] { violation.LabelSourceHandle, violation.GeometrySourceHandle }.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Evidence = new QaEvidence
                {
                    PlacementId = violation.PlacementId,
                    EquipmentEntityId = violation.EquipmentEntityId,
                    NormalizedTag = violation.NormalizedTag,
                    EngineeringDistanceEvidence = new EngineeringDistanceEvidence
                    {
                        MeasurementId = violation.MeasurementId,
                        MeasurementKind = evaluation.MeasurementKind,
                        PlacementId = violation.PlacementId,
                        EquipmentEntityId = violation.EquipmentEntityId,
                        NormalizedTag = violation.NormalizedTag,
                        SubjectKind = violation.SubjectKind,
                        ConstraintId = violation.ConstraintId,
                        MinimumDistanceMeters = evaluation.MinimumDistanceMeters,
                        MinimumDistanceDrawingUnits = evaluation.MinimumDistanceDrawingUnits,
                        ActualDistanceDrawingUnits = violation.ActualDistanceDrawingUnits,
                        ActualDistanceMeters = violation.ActualDistanceMeters,
                        InsertionUnits = evaluation.InsertionUnits,
                        CanonicalUnit = evaluation.CanonicalUnit,
                        MetersPerDrawingUnit = evaluation.MetersPerDrawingUnit,
                        LabelSourceHandle = violation.LabelSourceHandle,
                        GeometrySourceHandle = violation.GeometrySourceHandle,
                        ClosestPointX = violation.ClosestPointX,
                        ClosestPointY = violation.ClosestPointY,
                        RequiredMeasurementBasisKey = evaluation.RequiredMeasurementBasisKey,
                        ActualMeasurementBasisKey = violation.MeasurementBasisKey,
                        RequiredApplicabilityKey = evaluation.RequiredApplicabilityKey,
                        ActualApplicabilityKey = violation.ApplicabilityKey,
                        CriteriaProvenance = CopyCriteriaProvenance(evaluation.CriteriaProvenance)
                    }
                }
            };
        }

        private static bool IsSupportedMeasurementKind(string measurementKind) => measurementKind == "EquipmentToRoad2D" || measurementKind == "EquipmentToBoundary2D";
        private static string QaStatus(string status) => status == "PASS" ? "PASS" : status == "VIOLATIONS" ? "FINDINGS" : "INSUFFICIENT_EVIDENCE";

        private static void ValidateCriteriaProvenance(EngineeringDistanceRuleEvaluation evaluation)
        {
            EngineeringDistanceCriteriaProvenance? provenance = evaluation.CriteriaProvenance;
            if (provenance == null) return;
            if (provenance.SourceKind != "ProjectDistanceCriteria") throw new EngineeringDistanceQaIntegrationException("unsupported engineering distance QA criteria SourceKind: " + evaluation.RuleId + " | " + provenance.SourceKind);
            if (provenance.SchemaVersion != 1 && provenance.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new EngineeringDistanceQaIntegrationException("unsupported engineering distance QA criteria SchemaVersion: " + evaluation.RuleId + " | " + provenance.SchemaVersion);
            if (string.IsNullOrWhiteSpace(provenance.ProjectDirectory) || !System.IO.Path.IsPathRooted(provenance.ProjectDirectory)) throw new EngineeringDistanceQaIntegrationException("engineering distance QA criteria ProjectDirectory must be absolute: " + evaluation.RuleId);
            if (provenance.ConfiguredMinimumDistanceMeters <= 0m) throw new EngineeringDistanceQaIntegrationException("engineering distance QA criteria ConfiguredMinimumDistanceMeters must be greater than zero: " + evaluation.RuleId);
            if (provenance.ConfiguredMinimumDistanceMeters != evaluation.MinimumDistanceMeters) throw new EngineeringDistanceQaIntegrationException("engineering distance QA criteria configured minimum does not match evaluation minimum: " + evaluation.RuleId);
            string expected = evaluation.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge ? "AccessRoadEdgeMinimumMeters" : evaluation.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence ? "SecurityFenceMinimumMeters" : evaluation.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary ? "UnitBoundaryMinimumMeters" : string.Empty;
            if (string.IsNullOrEmpty(expected) || provenance.CriteriaSettingKey != expected) throw new EngineeringDistanceQaIntegrationException("engineering distance QA criteria setting key is incompatible with evaluation basis: " + evaluation.RuleId);
        }

        private static string ProjectMessage(string basis) => basis == EngineeringDistanceEvidenceKeys.AccessRoadEdge ? "Equipment placement is closer to Road Edge geometry than the configured project minimum distance." : basis == EngineeringDistanceEvidenceKeys.SecurityFence ? "Equipment placement is closer to Security Fence geometry than the configured Equipment-to-Plant minimum distance." : basis == EngineeringDistanceEvidenceKeys.UnitBoundary ? "Equipment placement is closer to Unit Boundary geometry than the configured project minimum distance." : "Equipment placement is closer to Boundary geometry than the configured project minimum distance.";

        private static EngineeringDistanceCriteriaProvenance? CopyCriteriaProvenance(EngineeringDistanceCriteriaProvenance? provenance) => provenance == null ? null : new EngineeringDistanceCriteriaProvenance
        {
            SourceKind = provenance.SourceKind,
            ProjectDirectory = provenance.ProjectDirectory,
            CriteriaSettingKey = provenance.CriteriaSettingKey,
            SchemaVersion = provenance.SchemaVersion,
            ConfiguredMinimumDistanceMeters = provenance.ConfiguredMinimumDistanceMeters
        };
    }
}
