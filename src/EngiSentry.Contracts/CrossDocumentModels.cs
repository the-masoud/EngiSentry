using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class ExternalEquipmentRegister
    {
        [DataMember(Order = 1)] public string SourceFileName { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SourceFullPath { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SourceSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4)] public List<ExternalEquipmentRecord> Records { get; set; } = new List<ExternalEquipmentRecord>();
        [DataMember(Order = 5)] public ExternalEquipmentStatistics Statistics { get; set; } = new ExternalEquipmentStatistics();
    }
    [DataContract]
    public sealed class ExternalEquipmentRecord
    {
        [DataMember(Order = 1)] public int SourceRowNumber { get; set; }
        [DataMember(Order = 2)] public string SourceTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string? Description { get; set; }
        [DataMember(Order = 5)] public string? Installation { get; set; }
        [DataMember(Order = 6)] public string ExpansionMethod { get; set; } = "Direct";
        [DataMember(Order = 7)] public int? SourceSheetIndex { get; set; }
        [DataMember(Order = 8)] public string? SourceSheetName { get; set; }
    }
    [DataContract]
    public sealed class ExternalEquipmentStatistics
    {
        [DataMember(Order = 1)] public int SourceRowCount { get; set; }
        [DataMember(Order = 2)] public int ExpandedEquipmentCount { get; set; }
    }
    [DataContract]
    public sealed class CrossDocumentQaReport
    {
        [DataMember(Order = 1)] public PlotPlanModel PlotPlan { get; set; } = new PlotPlanModel();
        [DataMember(Order = 2)] public ExternalEquipmentRegister ExternalEquipmentRegister { get; set; } = new ExternalEquipmentRegister();
        [DataMember(Order = 3)] public QaReport Qa { get; set; } = new QaReport();
    }
    [DataContract]
    public sealed class CrossDocumentEvidence
    {
        [DataMember(Order = 1)] public int? ExternalSourceRowNumber { get; set; }
        [DataMember(Order = 2)] public string? ExternalSourceTag { get; set; }
        [DataMember(Order = 3)] public string? ExternalNormalizedTag { get; set; }
        [DataMember(Order = 4)] public string? MatchedEquipmentCanonicalId { get; set; }
        [DataMember(Order = 5)] public List<string> MismatchFields { get; set; } = new List<string>();
        [DataMember(Order = 6)] public string? ExternalDescription { get; set; }
        [DataMember(Order = 7)] public string? DrawingDescription { get; set; }
        [DataMember(Order = 8)] public string? ExternalInstallation { get; set; }
        [DataMember(Order = 9)] public string? DrawingInstallation { get; set; }
    }
}
