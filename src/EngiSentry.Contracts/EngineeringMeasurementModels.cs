using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class DrawingUnitResolution
    {
        [DataMember(Order = 1)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string CanonicalUnit { get; set; } = string.Empty;
        [DataMember(Order = 3)] public double MetersPerDrawingUnit { get; set; }
        [DataMember(Order = 4)] public string ResolutionSource { get; set; } = string.Empty;
    }
}
