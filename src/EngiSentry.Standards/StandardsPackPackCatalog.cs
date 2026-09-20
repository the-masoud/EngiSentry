using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Groups governed rules into packs for the bounded PlantModel v1 use case. This is a grouping/projection
    /// helper only: it contains NO rule content of its own and creates no source, clause, distance, approval
    /// or authority. It must never contain remembered or invented company requirements (no Saudi Aramco, BP,
    /// Shell, Chevron, TotalEnergies, IPS or any other company rules); every row must come from registered
    /// governed evidence supplied by the user.
    ///
    /// Ordering is deterministic: rules are grouped by their effective pack identity and ordered by RuleId
    /// using StringComparer.Ordinal, so equal logical input always yields equal logical output.
    /// </summary>
    public static class StandardsPackPackCatalog
    {
        /// <summary>The bounded facility relationships Phase C supports.</summary>
        public static readonly string[] SupportedRelationships =
        {
            BoundedRelationship.EquipmentToRoad,
            BoundedRelationship.EquipmentToPlantFence,
            BoundedRelationship.EquipmentToUnitBoundary
        };

        /// <summary>
        /// Groups governed pack rules by their effective pack identity. Rules with no pack identity are
        /// collected under the project-criteria pack, which remains explicitly PROJECT_RULE_ONLY provenance
        /// and is never presented as a company standard.
        /// </summary>
        public static IReadOnlyList<StandardsPackGroup> Group(IReadOnlyList<StandardsPackRule> rules)
        {
            if (rules == null) return new List<StandardsPackGroup>();

            return rules
                .Where(x => x != null && !string.IsNullOrEmpty(x.RuleId))
                .GroupBy(x => string.IsNullOrEmpty(x.PackId) ? PackIds.ProjectCriteria : x.PackId!, StringComparer.Ordinal)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(group => new StandardsPackGroup
                {
                    PackId = group.Key,
                    Rules = group.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// Reports only the rules that belong to the bounded PlantModel v1 relationships. Unsupported
        /// relationships are excluded visibly rather than silently repurposed.
        /// </summary>
        public static IReadOnlyList<StandardsPackRule> BoundedRules(IReadOnlyList<StandardsPackRule> rules)
        {
            if (rules == null) return new List<StandardsPackRule>();

            return rules
                .Where(x => x != null && BoundedRelationship.IsSupported(x.MeasurementBasisKey))
                .OrderBy(x => x.RuleId, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>A deterministic group of governed rules sharing one pack identity.</summary>
    public sealed class StandardsPackGroup
    {
        public string PackId { get; set; } = string.Empty;
        public List<StandardsPackRule> Rules { get; set; } = new List<StandardsPackRule>();
    }

    public static class PackIds
    {
        /// <summary>Project criteria that carry no company-standard provenance.</summary>
        public const string ProjectCriteria = "project.plotplan.criteria";
    }
}
