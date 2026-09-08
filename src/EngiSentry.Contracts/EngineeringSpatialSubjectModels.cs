using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringSpatialSubject
    {
        [DataMember(Order = 1)] public string SubjectId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string SubjectKind { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string EngineeringMeasurementBasisKey { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string GeometrySourceHandle { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string GeometrySourceLayer { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string GeometryKind { get; set; } = string.Empty;
        [DataMember(Order = 7)] public List<string> AssociationIds { get; set; } = new List<string>();
        [DataMember(Order = 8)] public List<string> LabelSourceHandles { get; set; } = new List<string>();
    }
}
