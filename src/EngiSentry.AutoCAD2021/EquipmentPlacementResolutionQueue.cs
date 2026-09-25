using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// The engineer-placement queue identity. The queue carries exactly one case per equipment
    /// identity: an identity whose placement is already Complete is never queued, and an identity
    /// with several unresolved placement occurrences contributes exactly one deterministic
    /// representative. Counting unresolved work therefore counts unresolved equipment, never
    /// placement occurrences.
    ///
    /// This is pure queue bookkeeping over the already-computed plot plan. It never infers a
    /// location, never resolves anything, and never writes.
    /// </summary>
    internal static class EquipmentPlacementResolutionQueue
    {
        private const string CompleteStatus = "Complete";

        /// <summary>
        /// One unresolved representative per equipment identity, deterministically ordered. An
        /// identity is omitted entirely as soon as any of its placements is Complete, because a
        /// single resolved occurrence resolves the whole identity.
        /// </summary>
        internal static List<EquipmentPlacement> Build(IEnumerable<EquipmentPlacement>? placements)
        {
            if (placements == null) return new List<EquipmentPlacement>();
            return placements
                .Where(x => x != null)
                .GroupBy(x => x.EquipmentEntityId, StringComparer.Ordinal)
                .Where(group => !group.Any(IsComplete))
                .Select(Representative)
                .OrderBy(x => x.NormalizedTag, StringComparer.Ordinal)
                .ThenBy(x => x.PlacementId, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The number of unique unresolved equipment identities.</summary>
        internal static int CountUnresolved(IEnumerable<EquipmentPlacement>? placements) => Build(placements).Count;

        /// <summary>
        /// Removes every queue entry bound to one equipment identity, so a saved resolution can
        /// never leave a stale duplicate of the same identity behind. Returns the number removed.
        /// </summary>
        internal static int RemoveForEntity(IList<EquipmentPlacement>? queue, string? equipmentEntityId)
        {
            if (queue == null || string.IsNullOrEmpty(equipmentEntityId)) return 0;
            int removed = 0;
            for (int index = queue.Count - 1; index >= 0; index--)
            {
                if (!string.Equals(queue[index].EquipmentEntityId, equipmentEntityId, StringComparison.Ordinal)) continue;
                queue.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        private static bool IsComplete(EquipmentPlacement placement) => string.Equals(placement.PlacementStatus, CompleteStatus, StringComparison.Ordinal);

        // The deterministic representative: the placement with the smallest PlacementId (ordinal),
        // which is stable regardless of the order the placements are presented in.
        private static EquipmentPlacement Representative(IGrouping<string, EquipmentPlacement> group) =>
            group.OrderBy(x => x.PlacementId, StringComparer.Ordinal).ThenBy(x => x.NormalizedTag, StringComparer.Ordinal).First();
    }
}
