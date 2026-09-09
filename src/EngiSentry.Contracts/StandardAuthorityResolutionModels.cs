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
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? SourceDocumentNumber { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? SourceTitle { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? SourceIssuer { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? SourceRevisionOrEdition { get; set; }
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SourceFileName { get; set; }
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? SourceSha256 { get; set; }
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? ClauseReference { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? ClauseText { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? ClauseHeading { get; set; }
        [DataMember(Order = 17, EmitDefaultValue = false)] public int? PageNumber { get; set; }
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
