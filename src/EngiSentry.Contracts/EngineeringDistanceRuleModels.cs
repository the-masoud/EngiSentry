using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    [DataContract]
    public sealed class EngineeringDistanceRuleDefinition
    {
        [DataMember(Order = 1)]
        public string RuleId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string MeasurementKind { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public decimal MinimumDistanceMeters { get; set; }

        [DataMember(Order = 4)]
        public string RequiredMeasurementBasisKey { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string RequiredApplicabilityKey { get; set; } = string.Empty;

        [DataMember(Order = 6, EmitDefaultValue = false)]
        public EngineeringDistanceCriteriaProvenance? CriteriaProvenance { get; set; }
    }

    public static class EngineeringDistanceEvidenceKeys
    {
        public const string AccessRoadEdge = "AccessRoadEdge";
        public const string UnitPeripheralRoadEdge = "UnitPeripheralRoadEdge";
        public const string SecurityFence = "SecurityFence";
        public const string UnitBoundary = "UnitBoundary";
        public const string SiteBoundary = "SiteBoundary";

        public const string Equipment = "Equipment";
        public const string ProcessingEquipment = "ProcessingEquipment";
        public const string SpecialHazardousUnit = "SpecialHazardousUnit";

        public static bool IsSupportedApplicabilityKey(string value) =>
            value == Equipment || value == ProcessingEquipment || value == SpecialHazardousUnit;

        public static bool IsSupportedMeasurementBasisKey(string measurementKind, string value)
        {
            if (measurementKind == "EquipmentToRoad2D") return value == AccessRoadEdge || value == UnitPeripheralRoadEdge;
            if (measurementKind == "EquipmentToBoundary2D") return value == SecurityFence || value == UnitBoundary || value == SiteBoundary;
            return false;
        }
    }
}
