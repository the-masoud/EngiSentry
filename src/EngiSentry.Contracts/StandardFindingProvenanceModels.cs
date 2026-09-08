using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardClauseCitation
    {
        [DataMember(Order = 1)] public string ClauseId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ClauseReference { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string ClauseText { get; set; } = string.Empty;
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? Heading { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public int? PageNumber { get; set; }
        [DataMember(Order = 7)] public string DocumentNumber { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string SourceTitle { get; set; } = string.Empty;
        [DataMember(Order = 9)] public string Issuer { get; set; } = string.Empty;
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? RevisionOrEdition { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? SourceFileName { get; set; }
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SourceSha256 { get; set; }
    }

    [DataContract]
    public sealed class StandardFindingProvenance
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleTitle { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string WinningPackId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public int WinningPrecedence { get; set; }
        [DataMember(Order = 5)] public List<string> OverriddenPackIds { get; set; } = new List<string>();
        [DataMember(Order = 6)] public List<StandardClauseCitation> Clauses { get; set; } = new List<StandardClauseCitation>();
    }
}
