using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class StandardAuthorityReviewPackage
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 3)] public System.DateTime ScanTimestampUtc { get; set; }
        [DataMember(Order = 4)] public PlotPlanStandardsAuthorityReport StandardsAuthority { get; set; } = new PlotPlanStandardsAuthorityReport();
        [DataMember(Order = 5)] public StandardAuthorityReviewProjection ReviewProjection { get; set; } = new StandardAuthorityReviewProjection();
    }

    [DataContract]
    public sealed class StandardAuthorityReviewItem
    {
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string EffectiveEnforcementMode { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string DecisionState { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Message { get; set; } = string.Empty;
        [DataMember(Order = 5)] public List<StandardClauseAuthorityResolution> Clauses { get; set; } = new List<StandardClauseAuthorityResolution>();
    }

    [DataContract]
    public sealed class StandardAuthorityReviewProjection
    {
        [DataMember(Order = 1)] public List<StandardAuthorityReviewItem> ReviewItems { get; set; } = new List<StandardAuthorityReviewItem>();
    }
}
