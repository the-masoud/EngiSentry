using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Rules
{
    public sealed class EngineeringDistanceRuleRegistryValidationException : Exception
    {
        public EngineeringDistanceRuleRegistryValidationException(string message) : base(message) { }
    }

    public sealed class EngineeringDistanceRuleRegistry
    {
        private static readonly HashSet<string> SupportedMeasurementKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "EquipmentToRoad2D",
            "EquipmentToBoundary2D"
        };

        private readonly List<EngineeringDistanceRuleDefinition> rules;
        private readonly Dictionary<string, EngineeringDistanceRuleDefinition> rulesById;

        private EngineeringDistanceRuleRegistry(IEnumerable<EngineeringDistanceRuleDefinition> rules)
        {
            this.rules = rules.OrderBy(x => x.RuleId, StringComparer.Ordinal).Select(Copy).ToList();
            rulesById = this.rules.ToDictionary(x => x.RuleId, x => x, StringComparer.Ordinal);
        }

        public IReadOnlyList<EngineeringDistanceRuleDefinition> Rules => rules.Select(Copy).ToList();

        public static EngineeringDistanceRuleRegistry Create(StandardRulePackResolution standardsResolution, IEnumerable<EngineeringDistanceRuleDefinition> rules)
        {
            if (standardsResolution == null) throw new ArgumentNullException(nameof(standardsResolution));
            if (rules == null) throw new ArgumentNullException(nameof(rules));

            HashSet<string> effectiveRuleIds = ValidateEffectiveRules(standardsResolution);
            var accepted = new List<EngineeringDistanceRuleDefinition>();
            var executableRuleIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (EngineeringDistanceRuleDefinition rule in rules)
            {
                ValidateRule(rule, false);
                if (!effectiveRuleIds.Contains(rule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule references unknown effective RuleId: " + rule.RuleId);
                if (!executableRuleIds.Add(rule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("duplicate engineering distance rule RuleId: " + rule.RuleId);

                accepted.Add(Copy(rule));
            }

            return new EngineeringDistanceRuleRegistry(accepted);
        }

        public static EngineeringDistanceRuleRegistry CreateProjectCriteria(IEnumerable<EngineeringDistanceRuleDefinition> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            var accepted = new List<EngineeringDistanceRuleDefinition>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineeringDistanceRuleDefinition rule in rules)
            {
                ValidateRule(rule, true);
                if (!ids.Add(rule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("duplicate engineering distance rule RuleId: " + rule.RuleId);
                accepted.Add(Copy(rule));
            }
            return new EngineeringDistanceRuleRegistry(accepted);
        }

        public bool TryGet(string ruleId, out EngineeringDistanceRuleDefinition? rule)
        {
            EngineeringDistanceRuleDefinition found;
            if (string.IsNullOrWhiteSpace(ruleId) || !rulesById.TryGetValue(ruleId, out found))
            {
                rule = null;
                return false;
            }

            rule = Copy(found);
            return true;
        }

        public EngineeringDistanceRuleDefinition Get(string ruleId)
        {
            EngineeringDistanceRuleDefinition? rule;
            if (!TryGet(ruleId, out rule) || rule == null)
            {
                throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule was not found: " + (ruleId ?? string.Empty));
            }

            return rule;
        }

        private static HashSet<string> ValidateEffectiveRules(StandardRulePackResolution standardsResolution)
        {
            if (standardsResolution.EffectiveRules == null) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule standards EffectiveRules are required");

            var effectiveRuleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EffectiveStandardRuleDefinition effectiveRule in standardsResolution.EffectiveRules)
            {
                if (effectiveRule == null) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule standards contains a null effective rule");
                if (string.IsNullOrWhiteSpace(effectiveRule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule standards RuleId is required");
                if (!effectiveRuleIds.Add(effectiveRule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("duplicate engineering distance rule standards RuleId: " + effectiveRule.RuleId);
            }

            return effectiveRuleIds;
        }

        private static EngineeringDistanceRuleDefinition Copy(EngineeringDistanceRuleDefinition rule) => new EngineeringDistanceRuleDefinition
        {
            RuleId = rule.RuleId,
            MeasurementKind = rule.MeasurementKind,
            MinimumDistanceMeters = rule.MinimumDistanceMeters,
            RequiredMeasurementBasisKey = rule.RequiredMeasurementBasisKey,
            RequiredApplicabilityKey = rule.RequiredApplicabilityKey,
            CriteriaProvenance = CopyCriteriaProvenance(rule.CriteriaProvenance)
        };

        private static void ValidateRule(EngineeringDistanceRuleDefinition rule, bool requireCriteriaProvenance)
        {
            if (rule == null) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule registry contains a null rule");
            if (string.IsNullOrWhiteSpace(rule.RuleId)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule RuleId is required");
            if (string.IsNullOrWhiteSpace(rule.MeasurementKind)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule MeasurementKind is required: " + rule.RuleId);
            if (!SupportedMeasurementKinds.Contains(rule.MeasurementKind)) throw new EngineeringDistanceRuleRegistryValidationException("unsupported engineering distance MeasurementKind: " + rule.RuleId + " | " + rule.MeasurementKind);
            if (string.IsNullOrWhiteSpace(rule.RequiredMeasurementBasisKey)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule RequiredMeasurementBasisKey is required: " + rule.RuleId);
            if (!EngineeringDistanceEvidenceKeys.IsSupportedMeasurementBasisKey(rule.MeasurementKind, rule.RequiredMeasurementBasisKey)) throw new EngineeringDistanceRuleRegistryValidationException("unsupported engineering distance RequiredMeasurementBasisKey: " + rule.RuleId + " | " + rule.RequiredMeasurementBasisKey);
            if (string.IsNullOrWhiteSpace(rule.RequiredApplicabilityKey)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule RequiredApplicabilityKey is required: " + rule.RuleId);
            if (!EngineeringDistanceEvidenceKeys.IsSupportedApplicabilityKey(rule.RequiredApplicabilityKey)) throw new EngineeringDistanceRuleRegistryValidationException("unsupported engineering distance RequiredApplicabilityKey: " + rule.RuleId + " | " + rule.RequiredApplicabilityKey);
            if (rule.MinimumDistanceMeters <= 0m) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule MinimumDistanceMeters must be greater than zero: " + rule.RuleId);
            if (requireCriteriaProvenance && rule.CriteriaProvenance == null) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance rule CriteriaProvenance is required: " + rule.RuleId);
            ValidateCriteriaProvenance(rule);
        }

        private static void ValidateCriteriaProvenance(EngineeringDistanceRuleDefinition rule)
        {
            EngineeringDistanceCriteriaProvenance? provenance = rule.CriteriaProvenance;
            if (provenance == null) return;
            if (provenance.SourceKind != "ProjectDistanceCriteria") throw new EngineeringDistanceRuleRegistryValidationException("unsupported engineering distance criteria SourceKind: " + rule.RuleId + " | " + provenance.SourceKind);
            if (provenance.SchemaVersion != 1 && provenance.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new EngineeringDistanceRuleRegistryValidationException("unsupported engineering distance criteria SchemaVersion: " + rule.RuleId + " | " + provenance.SchemaVersion);
            if (string.IsNullOrWhiteSpace(provenance.ProjectDirectory) || !System.IO.Path.IsPathRooted(provenance.ProjectDirectory)) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance criteria ProjectDirectory must be absolute: " + rule.RuleId);
            if (provenance.ConfiguredMinimumDistanceMeters <= 0m) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance criteria ConfiguredMinimumDistanceMeters must be greater than zero: " + rule.RuleId);
            if (provenance.ConfiguredMinimumDistanceMeters != rule.MinimumDistanceMeters) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance criteria configured minimum does not match rule minimum: " + rule.RuleId);
            string expected = rule.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge ? "AccessRoadEdgeMinimumMeters" : rule.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence ? "SecurityFenceMinimumMeters" : rule.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary ? "UnitBoundaryMinimumMeters" : string.Empty;
            if (string.IsNullOrEmpty(expected) || provenance.CriteriaSettingKey != expected) throw new EngineeringDistanceRuleRegistryValidationException("engineering distance criteria setting key is incompatible with rule basis: " + rule.RuleId);
        }

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
