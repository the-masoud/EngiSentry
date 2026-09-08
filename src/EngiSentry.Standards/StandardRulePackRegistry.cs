using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardRulePackRegistryValidationException : Exception
    {
        public StandardRulePackRegistryValidationException(string message)
            : base(message) { }
    }

    public sealed class StandardRulePackRegistry
    {
        private readonly List<StandardRulePackDefinition> packs;

        private StandardRulePackRegistry(List<StandardRulePackDefinition> packs)
        {
            this.packs = packs;
        }

        public IReadOnlyList<StandardRulePackDefinition> Packs => packs.Select(Copy).ToList();

        public static StandardRulePackRegistry Create(StandardClauseRegistry clauseRegistry, IEnumerable<StandardRulePackDefinition> packs)
        {
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (packs == null) throw new ArgumentNullException(nameof(packs));

            var accepted = new List<StandardRulePackDefinition>();
            var packIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (StandardRulePackDefinition pack in packs)
            {
                if (pack == null) throw new StandardRulePackRegistryValidationException("standard rule pack registry contains a null pack");
                ValidatePack(pack);
                if (!packIds.Add(pack.PackId))
                {
                    throw new StandardRulePackRegistryValidationException("duplicate standard rule pack PackId: " + pack.PackId);
                }

                var ruleIds = new HashSet<string>(StringComparer.Ordinal);
                var rules = new List<StandardRuleDefinition>();
                foreach (StandardRuleDefinition rule in pack.Rules)
                {
                    if (rule == null) throw new StandardRulePackRegistryValidationException("standard rule pack contains a null rule: " + pack.PackId);
                    ValidateRule(rule, pack.PackId);
                    if (!ruleIds.Add(rule.RuleId))
                    {
                        throw new StandardRulePackRegistryValidationException("duplicate standard rule RuleId in pack: " + pack.PackId + " | " + rule.RuleId);
                    }

                    var clauseIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string clauseId in rule.ClauseIds)
                    {
                        if (string.IsNullOrWhiteSpace(clauseId)) throw new StandardRulePackRegistryValidationException("standard rule ClauseId is required: " + pack.PackId + " | " + rule.RuleId);
                        if (!clauseRegistry.TryGet(clauseId, out _))
                        {
                            throw new StandardRulePackRegistryValidationException("standard rule references unknown ClauseId: " + clauseId);
                        }

                        if (!clauseIds.Add(clauseId))
                        {
                            throw new StandardRulePackRegistryValidationException("duplicate standard rule ClauseId: " + pack.PackId + " | " + rule.RuleId + " | " + clauseId);
                        }
                    }

                    rules.Add(Copy(rule));
                }

                accepted.Add(new StandardRulePackDefinition
                {
                    PackId = pack.PackId,
                    Name = pack.Name,
                    Rules = rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToList()
                });
            }

            return new StandardRulePackRegistry(accepted.OrderBy(pack => pack.PackId, StringComparer.Ordinal).ToList());
        }

        public bool TryGet(string packId, out StandardRulePackDefinition? pack)
        {
            pack = null;
            if (string.IsNullOrWhiteSpace(packId)) return false;

            StandardRulePackDefinition? found = packs.FirstOrDefault(candidate => string.Equals(candidate.PackId, packId, StringComparison.Ordinal));
            if (found == null) return false;

            pack = Copy(found);
            return true;
        }

        public StandardRulePackDefinition Get(string packId)
        {
            if (TryGet(packId, out StandardRulePackDefinition? pack) && pack != null) return pack;
            throw new StandardRulePackRegistryValidationException("standard rule pack was not found: " + (packId ?? string.Empty));
        }

        private static void ValidatePack(StandardRulePackDefinition pack)
        {
            if (string.IsNullOrWhiteSpace(pack.PackId)) throw new StandardRulePackRegistryValidationException("standard rule pack PackId is required");
            if (string.IsNullOrWhiteSpace(pack.Name)) throw new StandardRulePackRegistryValidationException("standard rule pack Name is required: " + pack.PackId);
            if (pack.Rules == null) throw new StandardRulePackRegistryValidationException("standard rule pack Rules is required: " + pack.PackId);
            if (pack.Rules.Count == 0) throw new StandardRulePackRegistryValidationException("standard rule pack contains no rules: " + pack.PackId);
        }

        private static void ValidateRule(StandardRuleDefinition rule, string packId)
        {
            if (string.IsNullOrWhiteSpace(rule.RuleId)) throw new StandardRulePackRegistryValidationException("standard rule RuleId is required: " + packId);
            if (string.IsNullOrWhiteSpace(rule.Title)) throw new StandardRulePackRegistryValidationException("standard rule Title is required: " + packId + " | " + rule.RuleId);
            if (rule.ClauseIds == null) throw new StandardRulePackRegistryValidationException("standard rule ClauseIds is required: " + packId + " | " + rule.RuleId);
            if (rule.ClauseIds.Count == 0) throw new StandardRulePackRegistryValidationException("standard rule requires at least one ClauseId: " + packId + " | " + rule.RuleId);
        }

        private static StandardRulePackDefinition Copy(StandardRulePackDefinition pack)
        {
            return new StandardRulePackDefinition
            {
                PackId = pack.PackId,
                Name = pack.Name,
                Rules = pack.Rules.Select(Copy).ToList()
            };
        }

        private static StandardRuleDefinition Copy(StandardRuleDefinition rule)
        {
            return new StandardRuleDefinition
            {
                RuleId = rule.RuleId,
                Title = rule.Title,
                ClauseIds = rule.ClauseIds.OrderBy(clauseId => clauseId, StringComparer.Ordinal).ToList()
            };
        }
    }
}
