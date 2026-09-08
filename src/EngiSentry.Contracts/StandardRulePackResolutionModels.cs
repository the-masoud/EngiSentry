using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardRulePackPrecedenceDefinition
    {
        [DataMember(Order = 1)]
        public string PackId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public int Precedence { get; set; }
    }

    [DataContract]
    public sealed class EffectiveStandardRuleDefinition
    {
        [DataMember(Order = 1)]
        public string RuleId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Title { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public List<string> ClauseIds { get; set; } = new List<string>();

        [DataMember(Order = 4)]
        public string WinningPackId { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public int WinningPrecedence { get; set; }

        [DataMember(Order = 6)]
        public List<string> OverriddenPackIds { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class StandardRulePackResolution
    {
        [DataMember(Order = 1)]
        public List<StandardRulePackPrecedenceDefinition> AppliedPrecedence { get; set; } = new List<StandardRulePackPrecedenceDefinition>();

        [DataMember(Order = 2)]
        public List<EffectiveStandardRuleDefinition> EffectiveRules { get; set; } = new List<EffectiveStandardRuleDefinition>();
    }
}
