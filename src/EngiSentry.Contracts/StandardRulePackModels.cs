using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardRuleDefinition
    {
        [DataMember(Order = 1)]
        public string RuleId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Title { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public List<string> ClauseIds { get; set; } = new List<string>();
    }

    [DataContract]
    public sealed class StandardRulePackDefinition
    {
        [DataMember(Order = 1)]
        public string PackId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public List<StandardRuleDefinition> Rules { get; set; } = new List<StandardRuleDefinition>();
    }
}
