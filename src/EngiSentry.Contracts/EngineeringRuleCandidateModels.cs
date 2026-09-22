using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringRuleCandidateDefinition
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Title { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string RuleKind { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string SourceId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string SourceSha256 { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string RevisionOrEdition { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string ClauseReference { get; set; } = string.Empty;
        [DataMember(Order = 9)] public int PdfPageNumber { get; set; }
        [DataMember(Order = 10)] public string ProjectApplicabilityReference { get; set; } = string.Empty;
        [DataMember(Order = 11)] public string SubjectClass { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string TargetClass { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string MeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 14)] public string Unit { get; set; } = string.Empty;
        [DataMember(Order = 15, EmitDefaultValue = false)] public decimal? MinimumValue { get; set; }
        [DataMember(Order = 16)] public string ApplicabilityPredicate { get; set; } = string.Empty;
        [DataMember(Order = 17)] public string NormativeStrength { get; set; } = string.Empty;
        [DataMember(Order = 18)] public string ApprovalOrDeviationSemantics { get; set; } = string.Empty;
        [DataMember(Order = 19)] public string ExecutionState { get; set; } = string.Empty;
    }
}
