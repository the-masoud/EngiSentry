using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardRuleExecutionGateDecision
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EffectiveEnforcementMode { get; set; } = string.Empty;
        [DataMember(Order = 3)] public bool AutoExecutionAllowed { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? DecisionState { get; set; }
    }

    [DataContract]
    public sealed class StandardExecutionGateResolution
    {
        [DataMember(Order = 1)] public List<StandardRuleExecutionGateDecision> Rules { get; set; } = new List<StandardRuleExecutionGateDecision>();
    }
}
