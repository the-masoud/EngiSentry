using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardSourceDefinition
    {
        [DataMember(Order = 1)]
        public string SourceId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string DocumentNumber { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string Title { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public string Issuer { get; set; } = string.Empty;

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public string? RevisionOrEdition { get; set; }

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public string? SourceFileName { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)]
        public string? SourceSha256 { get; set; }
    }
}
