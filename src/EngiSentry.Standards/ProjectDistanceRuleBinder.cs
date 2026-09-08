using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectDistanceRuleBindingException : Exception
    {
        public ProjectDistanceRuleBindingException(string message) : base(message) { }
    }

    public static class ProjectDistanceRuleBinder
    {
        public const string EquipmentToRoadRuleId = "project.plotplan.equipment.road.minimum-clearance";
        public const string EquipmentToPlantRuleId = "project.plotplan.equipment.plant.minimum-clearance";
        public const string EquipmentToUnitBoundaryRuleId = "project.plotplan.equipment.unit-boundary.minimum-clearance";

        public static IReadOnlyList<EngineeringDistanceRuleDefinition> Create(ProjectDistanceCriteria criteria)
        {
            Validate(criteria);
            if (criteria.SchemaVersion == 2) return new[]
            {
                ProjectRule(EquipmentToRoadRuleId, "EquipmentToRoad2D", EngineeringDistanceEvidenceKeys.AccessRoadEdge, EngineeringDistanceEvidenceKeys.Equipment, "AccessRoadEdgeMinimumMeters", criteria),
                ProjectRule(EquipmentToPlantRuleId, "EquipmentToBoundary2D", EngineeringDistanceEvidenceKeys.SecurityFence, EngineeringDistanceEvidenceKeys.Equipment, "SecurityFenceMinimumMeters", criteria),
                ProjectRule(EquipmentToUnitBoundaryRuleId, "EquipmentToBoundary2D", EngineeringDistanceEvidenceKeys.UnitBoundary, EngineeringDistanceEvidenceKeys.Equipment, "UnitBoundaryMinimumMeters", criteria)
            }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            EngineeringDistanceCandidateDefinition road = Candidate("candidate.processing-equipment.access-road-edge.3m");
            EngineeringDistanceCandidateDefinition fence = Candidate("candidate.equipment.security-fence.30m");
            return new[]
            {
                Rule(road, criteria.ProjectDirectory, criteria.SchemaVersion, "AccessRoadEdgeMinimumMeters", criteria.AccessRoadEdgeMinimumMeters),
                Rule(fence, criteria.ProjectDirectory, criteria.SchemaVersion, "SecurityFenceMinimumMeters", criteria.SecurityFenceMinimumMeters)
            }.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
        }

        private static EngineeringDistanceRuleDefinition ProjectRule(string ruleId, string measurementKind, string basis, string applicability, string settingKey, ProjectDistanceCriteria criteria)
        {
            decimal value = settingKey == "AccessRoadEdgeMinimumMeters" ? criteria.AccessRoadEdgeMinimumMeters : settingKey == "SecurityFenceMinimumMeters" ? criteria.SecurityFenceMinimumMeters : criteria.UnitBoundaryMinimumMeters;
            return new EngineeringDistanceRuleDefinition { RuleId = ruleId, MeasurementKind = measurementKind, MinimumDistanceMeters = value, RequiredMeasurementBasisKey = basis, RequiredApplicabilityKey = applicability, CriteriaProvenance = new EngineeringDistanceCriteriaProvenance { SourceKind = "ProjectDistanceCriteria", ProjectDirectory = criteria.ProjectDirectory, CriteriaSettingKey = settingKey, SchemaVersion = criteria.SchemaVersion, ConfiguredMinimumDistanceMeters = value } };
        }

        private static EngineeringDistanceCandidateDefinition Candidate(string candidateId)
        {
            EngineeringDistanceCandidateDefinition[] matches = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Where(x => x.CandidateId == candidateId).ToArray();
            if (matches.Length != 1) throw new ProjectDistanceRuleBindingException("Project Distance Rule Binder requires exactly one candidate: " + candidateId);
            return matches[0];
        }

        private static EngineeringDistanceRuleDefinition Rule(EngineeringDistanceCandidateDefinition candidate, string directory, int schemaVersion, string settingKey, decimal value) => new EngineeringDistanceRuleDefinition
        {
            RuleId = candidate.RuleId,
            MeasurementKind = candidate.MeasurementKind,
            MinimumDistanceMeters = value,
            RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey,
            RequiredApplicabilityKey = candidate.RequiredApplicabilityKey,
            CriteriaProvenance = new EngineeringDistanceCriteriaProvenance { SourceKind = "ProjectDistanceCriteria", ProjectDirectory = directory, CriteriaSettingKey = settingKey, SchemaVersion = schemaVersion, ConfiguredMinimumDistanceMeters = value }
        };

        private static void Validate(ProjectDistanceCriteria criteria)
        {
            if (criteria == null) throw new ProjectDistanceRuleBindingException("Project Distance Criteria are required.");
            if (criteria.SchemaVersion != 1 && criteria.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new ProjectDistanceRuleBindingException("Unsupported Project Distance Criteria schema version: " + criteria.SchemaVersion);
            if (string.IsNullOrWhiteSpace(criteria.ProjectDirectory) || !Path.IsPathRooted(criteria.ProjectDirectory)) throw new ProjectDistanceRuleBindingException("Project Distance Criteria ProjectDirectory must be an absolute path.");
            if (criteria.AccessRoadEdgeMinimumMeters <= 0m) throw new ProjectDistanceRuleBindingException("Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            if (criteria.SecurityFenceMinimumMeters <= 0m) throw new ProjectDistanceRuleBindingException("Project Distance Criteria Security Fence minimum must be greater than zero.");
            if (criteria.SchemaVersion == ProjectDistanceCriteriaDefaults.CurrentSchemaVersion && criteria.UnitBoundaryMinimumMeters <= 0m) throw new ProjectDistanceRuleBindingException("Project Distance Criteria Unit Boundary minimum must be greater than zero.");
        }
    }
}
