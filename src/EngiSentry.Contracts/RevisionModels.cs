using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class RevisionSnapshot
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "1.0";
        [DataMember(Order = 2)] public RevisionDrawing Drawing { get; set; } = new RevisionDrawing();
        [DataMember(Order = 3)] public List<RevisionEquipmentRecord> Equipment { get; set; } = new List<RevisionEquipmentRecord>();
        [DataMember(Order = 4)] public RevisionSnapshotStatistics Statistics { get; set; } = new RevisionSnapshotStatistics();
    }
    [DataContract]
    public sealed class RevisionDrawing
    {
        [DataMember(Order = 1)] public string? FileName { get; set; }
        [DataMember(Order = 2)] public string? FullPath { get; set; }
        [DataMember(Order = 3)] public string? DwgVersion { get; set; }
        [DataMember(Order = 4)] public string? InsertionUnits { get; set; }
        [DataMember(Order = 5)] public string? CurrentLayout { get; set; }
        [DataMember(Order = 6)] public DateTime ScanTimestampUtc { get; set; }
    }
    [DataContract]
    public sealed class RevisionEquipmentRecord
    {
        [DataMember(Order = 1)] public string EquipmentCanonicalId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public List<RevisionCoordinatePair> CompleteCoordinatePairs { get; set; } = new List<RevisionCoordinatePair>();
        [DataMember(Order = 4)] public List<string> DescriptionValues { get; set; } = new List<string>();
        [DataMember(Order = 5)] public List<string> InstallationValues { get; set; } = new List<string>();
    }
    [DataContract]
    public sealed class RevisionCoordinatePair
    {
        [DataMember(Order = 1)] public decimal Easting { get; set; }
        [DataMember(Order = 2)] public decimal Northing { get; set; }
    }
    [DataContract]
    public sealed class RevisionSnapshotStatistics
    {
        [DataMember(Order = 1)] public int EquipmentCount { get; set; }
    }
    [DataContract]
    public sealed class RevisionChangeRecord
    {
        [DataMember(Order = 1)] public string ChangeId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string ChangeKind { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string EquipmentCanonicalId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 5)] public List<string> ChangedFields { get; set; } = new List<string>();
        [DataMember(Order = 6, EmitDefaultValue = false)] public RevisionEquipmentRecord? Baseline { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public RevisionEquipmentRecord? Current { get; set; }
    }
    [DataContract]
    public sealed class RevisionComparisonStatistics
    {
        [DataMember(Order = 1)] public int BaselineEquipmentCount { get; set; }
        [DataMember(Order = 2)] public int CurrentEquipmentCount { get; set; }
        [DataMember(Order = 3)] public int AddedEquipmentCount { get; set; }
        [DataMember(Order = 4)] public int RemovedEquipmentCount { get; set; }
        [DataMember(Order = 5)] public int CoordinateChangedEquipmentCount { get; set; }
        [DataMember(Order = 6)] public int AttributeChangedEquipmentCount { get; set; }
        [DataMember(Order = 7)] public int ChangedEquipmentCount { get; set; }
        [DataMember(Order = 8)] public int UnchangedEquipmentCount { get; set; }
        [DataMember(Order = 9)] public int TotalChangeCount { get; set; }
    }
    [DataContract]
    public sealed class RevisionComparisonReport
    {
        [DataMember(Order = 1)] public string BaselineSourceFileName { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string BaselineSourceFullPath { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string BaselineSourceSha256 { get; set; } = string.Empty;
        [DataMember(Order = 4)] public RevisionSnapshot Baseline { get; set; } = new RevisionSnapshot();
        [DataMember(Order = 5)] public RevisionSnapshot Current { get; set; } = new RevisionSnapshot();
        [DataMember(Order = 6)] public RevisionComparisonStatistics Statistics { get; set; } = new RevisionComparisonStatistics();
        [DataMember(Order = 7)] public List<RevisionChangeRecord> Changes { get; set; } = new List<RevisionChangeRecord>();
    }
}
