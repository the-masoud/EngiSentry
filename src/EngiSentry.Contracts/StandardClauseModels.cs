using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardClauseDefinition
    {
        [DataMember(Order = 1)]
        public string ClauseId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string SourceId { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string ClauseReference { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public string ClauseText { get; set; } = string.Empty;

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public string? Heading { get; set; }

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public int? PageNumber { get; set; }
    }
}
