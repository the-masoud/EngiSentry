using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringStandardProfile
    {
        [DataMember(Order = 1)] public string? ProfileId { get; set; }
        [DataMember(Order = 2)] public string? Name { get; set; }
        [DataMember(Order = 3)] public string? Level { get; set; }
        [DataMember(Order = 4)] public string? Source { get; set; }
        [DataMember(Order = 5)] public string? Version { get; set; }
        [DataMember(Order = 6)] public List<RuleSetting> RuleSettings { get; set; } = new List<RuleSetting>();
    }

    [DataContract]
    public sealed class RuleSetting
    {
        [DataMember(Order = 1)] public string? RuleId { get; set; }
        [DataMember(Order = 2)] public bool? Enabled { get; set; }
        [DataMember(Order = 3)] public string? SeverityOverride { get; set; }
        [DataMember(Order = 4)] public decimal? MinimumDistanceDrawingUnits { get; set; }
    }

    [DataContract]
    public sealed class KnownQaRule
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string Severity { get; set; } = string.Empty;
        [DataMember(Order = 3)] public bool DefaultEnabled { get; set; } = true;
    }

    [DataContract]
    public sealed class ProfileResolution
    {
        [DataMember(Order = 1)] public string ValidationStatus { get; set; } = "VALID";
        [DataMember(Order = 2)] public List<string> AppliedProfileIds { get; set; } = new List<string>();
        [DataMember(Order = 3)] public List<EffectiveRuleSetting> EffectiveRuleSettings { get; set; } = new List<EffectiveRuleSetting>();
    }

    [DataContract]
    public sealed class EffectiveRuleSetting
    {
        [DataMember(Order = 1)] public string EffectiveRuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public bool Enabled { get; set; }
        [DataMember(Order = 3)] public string EffectiveSeverity { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string EnabledSourceProfileId { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SeveritySourceProfileId { get; set; } = string.Empty;
        [DataMember(Order = 6, EmitDefaultValue = false)] public decimal? EffectiveMinimumDistanceDrawingUnits { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? MinimumDistanceSourceProfileId { get; set; }
    }
}
