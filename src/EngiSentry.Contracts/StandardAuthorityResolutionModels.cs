using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardClauseAuthorityResolution
    {
        [DataMember(Order = 1)] public string ClauseId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string PolicyId { get; set; } = string.Empty;
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? PolicyClauseId { get; set; }
        [DataMember(Order = 5)] public string AuthorityClass { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string EnforcementMode { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string Rationale { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class StandardRuleAuthorityResolution
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EffectiveEnforcementMode { get; set; } = string.Empty;
        [DataMember(Order = 3)] public List<StandardClauseAuthorityResolution> Clauses { get; set; } = new List<StandardClauseAuthorityResolution>();
    }

    [DataContract]
    public sealed class StandardAuthorityResolution
    {
        [DataMember(Order = 1)] public List<StandardRuleAuthorityResolution> Rules { get; set; } = new List<StandardRuleAuthorityResolution>();
    }
}
