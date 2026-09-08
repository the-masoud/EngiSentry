using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardRulePackResolutionException : Exception
    {
        public StandardRulePackResolutionException(string message)
            : base(message) { }
    }

    public static class StandardRulePackResolver
    {
        public static StandardRulePackResolution Resolve(StandardRulePackRegistry rulePackRegistry, IEnumerable<StandardRulePackPrecedenceDefinition> precedence)
        {
            if (rulePackRegistry == null) throw new ArgumentNullException(nameof(rulePackRegistry));
            if (precedence == null) throw new ArgumentNullException(nameof(precedence));

            IReadOnlyList<StandardRulePackDefinition> packs = rulePackRegistry.Packs;
            var precedences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (StandardRulePackPrecedenceDefinition entry in precedence)
            {
                if (entry == null) throw new StandardRulePackResolutionException("standard rule precedence contains a null entry");
                if (string.IsNullOrWhiteSpace(entry.PackId)) throw new StandardRulePackResolutionException("standard rule precedence PackId is required");
                if (entry.Precedence <= 0) throw new StandardRulePackResolutionException("standard rule precedence must be greater than zero: " + entry.PackId);
                if (!rulePackRegistry.TryGet(entry.PackId, out _)) throw new StandardRulePackResolutionException("standard rule precedence references unknown PackId: " + entry.PackId);
                if (precedences.ContainsKey(entry.PackId)) throw new StandardRulePackResolutionException("duplicate standard rule precedence PackId: " + entry.PackId);
                precedences.Add(entry.PackId, entry.Precedence);
            }

            foreach (StandardRulePackDefinition pack in packs.OrderBy(pack => pack.PackId, StringComparer.Ordinal))
            {
                if (!precedences.ContainsKey(pack.PackId)) throw new StandardRulePackResolutionException("standard rule precedence is missing for PackId: " + pack.PackId);
            }

            var candidatesByRuleId = new Dictionary<string, List<RuleCandidate>>(StringComparer.Ordinal);
            foreach (StandardRulePackDefinition pack in packs)
            {
                foreach (StandardRuleDefinition rule in pack.Rules)
                {
                    if (!candidatesByRuleId.TryGetValue(rule.RuleId, out List<RuleCandidate>? candidates))
                    {
                        candidates = new List<RuleCandidate>();
                        candidatesByRuleId.Add(rule.RuleId, candidates);
                    }

                    candidates.Add(new RuleCandidate(pack, rule, precedences[pack.PackId]));
                }
            }

            var effectiveRules = new List<EffectiveStandardRuleDefinition>();
            foreach (KeyValuePair<string, List<RuleCandidate>> entry in candidatesByRuleId.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                int highestPrecedence = entry.Value.Max(candidate => candidate.Precedence);
                List<RuleCandidate> winners = entry.Value.Where(candidate => candidate.Precedence == highestPrecedence).OrderBy(candidate => candidate.Pack.PackId, StringComparer.Ordinal).ToList();
                if (winners.Count != 1)
                {
                    throw new StandardRulePackResolutionException("unresolved standard rule conflict: " + entry.Key + " | " + string.Join(",", winners.Select(candidate => candidate.Pack.PackId)));
                }

                RuleCandidate winner = winners[0];
                List<string> overriddenPackIds = entry.Value
                    .Where(candidate => !string.Equals(candidate.Pack.PackId, winner.Pack.PackId, StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.Precedence)
                    .ThenBy(candidate => candidate.Pack.PackId, StringComparer.Ordinal)
                    .Select(candidate => candidate.Pack.PackId)
                    .ToList();
                effectiveRules.Add(new EffectiveStandardRuleDefinition
                {
                    RuleId = winner.Rule.RuleId,
                    Title = winner.Rule.Title,
                    ClauseIds = winner.Rule.ClauseIds.ToList(),
                    WinningPackId = winner.Pack.PackId,
                    WinningPrecedence = winner.Precedence,
                    OverriddenPackIds = overriddenPackIds
                });
            }

            return new StandardRulePackResolution
            {
                AppliedPrecedence = precedences
                    .OrderByDescending(entry => entry.Value)
                    .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new StandardRulePackPrecedenceDefinition { PackId = entry.Key, Precedence = entry.Value })
                    .ToList(),
                EffectiveRules = effectiveRules
            };
        }

        private sealed class RuleCandidate
        {
            public RuleCandidate(StandardRulePackDefinition pack, StandardRuleDefinition rule, int precedence)
            {
                Pack = pack;
                Rule = rule;
                Precedence = precedence;
            }

            public StandardRulePackDefinition Pack { get; }
            public StandardRuleDefinition Rule { get; }
            public int Precedence { get; }
        }
    }
}
