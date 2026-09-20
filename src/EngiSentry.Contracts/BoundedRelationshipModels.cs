namespace EngiSentry.Contracts
{
    /// <summary>
    /// Bounded facility relationship vocabulary for the Standards Assistant (Phase A architecture C/D).
    /// Only Equipment → Road, Equipment → Plant Fence and Equipment → Unit Boundary are supported. Any other
    /// relationship is unsupported and must never be evaluated as a bounded constraint: this keeps the
    /// bounded use case closed and prevents speculative relationships from being repurposed as engineering
    /// criteria.
    ///
    /// Lives in Contracts so both the governed projection (EngiSentry.Standards) and the constraint engine
    /// (EngiSentry.Rules) can share it without reversing the existing dependency direction.
    /// </summary>
    public static class BoundedRelationship
    {
        public const string EquipmentToRoad = "EquipmentToRoad";
        public const string EquipmentToPlantFence = "EquipmentToPlantFence";
        public const string EquipmentToUnitBoundary = "EquipmentToUnitBoundary";

        /// <summary>Reports whether a measurement basis key belongs to the bounded taxonomy.</summary>
        public static bool IsSupported(string? measurementBasisKey)
        {
            if (string.IsNullOrEmpty(measurementBasisKey)) return false;
            return measurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge
                || measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge
                || measurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence
                || measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary;
        }

        /// <summary>
        /// Resolves the bounded relationship for an existing accepted measurement basis key, or null when the
        /// basis is outside the bounded taxonomy (for example SiteBoundary, or an unrecognized basis). Null is
        /// explicit: the caller must surface the unsupported basis rather than guess a relationship.
        /// </summary>
        public static string? TryResolve(string? measurementBasisKey)
        {
            if (string.IsNullOrEmpty(measurementBasisKey)) return null;
            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge) return EquipmentToRoad;
            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge) return EquipmentToRoad;
            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence) return EquipmentToPlantFence;
            if (measurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary) return EquipmentToUnitBoundary;
            return null;
        }
    }
}
