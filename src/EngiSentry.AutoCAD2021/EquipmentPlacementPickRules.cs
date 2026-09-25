using System;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// A picked point carried without any Autodesk type, so the pick rules can be exercised without
    /// a running editor.
    /// </summary>
    internal readonly struct EquipmentPlacementPoint
    {
        internal EquipmentPlacementPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double X { get; }
        internal double Y { get; }
        internal double Z { get; }
    }

    /// <summary>
    /// The editor's current UCS to WCS transform, held as primitives so no Autodesk type leaks into
    /// the pick rules. It is a rigid transform: the 3x3 rotation is row-major and the translation is
    /// applied after it, matching the row-vector convention of an AutoCAD UCS matrix.
    /// </summary>
    internal sealed class EquipmentPlacementUcsTransform
    {
        private readonly double[] rotation;
        private readonly double[] translation;

        internal EquipmentPlacementUcsTransform(double[] rotationRowMajor, double[] translation)
        {
            if (rotationRowMajor == null || rotationRowMajor.Length != 9) throw new ArgumentException("a 3x3 rotation is required", nameof(rotationRowMajor));
            if (translation == null || translation.Length != 3) throw new ArgumentException("a 3-vector translation is required", nameof(translation));
            this.rotation = (double[])rotationRowMajor.Clone();
            this.translation = (double[])translation.Clone();
        }

        /// <summary>A UCS that coincides with the WCS.</summary>
        internal static EquipmentPlacementUcsTransform Identity { get; } = FromComponents(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);

        internal static EquipmentPlacementUcsTransform FromComponents(
            double r00, double r01, double r02,
            double r10, double r11, double r12,
            double r20, double r21, double r22,
            double tx, double ty, double tz) =>
            new EquipmentPlacementUcsTransform(
                new[] { r00, r01, r02, r10, r11, r12, r20, r21, r22 },
                new[] { tx, ty, tz });

        internal EquipmentPlacementPoint Transform(EquipmentPlacementPoint point)
        {
            double x = point.X, y = point.Y, z = point.Z;
            return new EquipmentPlacementPoint(
                (rotation[0] * x) + (rotation[1] * y) + (rotation[2] * z) + translation[0],
                (rotation[3] * x) + (rotation[4] * y) + (rotation[5] * z) + translation[1],
                (rotation[6] * x) + (rotation[7] * y) + (rotation[8] * z) + translation[2]);
        }
    }

    /// <summary>
    /// The pure pick rules. They decide whether an explicit engineer pick may become a placement and
    /// which WCS coordinate it carries; the editor-backed provider supplies the AutoCAD facts.
    /// Nothing here snaps, infers, or searches for a nearest entity.
    /// </summary>
    internal static class EquipmentPlacementPickRules
    {
        /// <summary>
        /// Converts an editor prompt point into the WCS. An editor prompt point is always expressed
        /// in the current UCS - never in the WCS - so the current UCS transform is always applied.
        /// </summary>
        internal static EquipmentPlacementPoint ToWorld(EquipmentPlacementPoint promptPoint, EquipmentPlacementUcsTransform ucsToWcs) =>
            ucsToWcs.Transform(promptPoint);

        /// <summary>
        /// An entity pick is accepted only on the true Model tab, only for an entity that belongs to
        /// model space, and never for a viewport. A model-space entity reached through a layout
        /// viewport is therefore rejected: the editor is not on the Model tab.
        /// </summary>
        internal static EquipmentPlacementPick? CreateEntityPick(
            EquipmentPlacementPoint promptPoint,
            EquipmentPlacementUcsTransform ucsToWcs,
            bool isTrueModelTab,
            bool entityOwnedByModelSpace,
            bool isViewport,
            string? handle,
            string? layer)
        {
            if (!isTrueModelTab) return null;
            if (!entityOwnedByModelSpace) return null;
            if (isViewport) return null;
            if (string.IsNullOrWhiteSpace(handle) || string.IsNullOrWhiteSpace(layer)) return null;
            EquipmentPlacementPoint world = ToWorld(promptPoint, ucsToWcs);
            return new EquipmentPlacementPick
            {
                Source = EquipmentPlacementResolutionSources.EntityPick,
                PointX = world.X, PointY = world.Y, PointZ = world.Z,
                Handle = handle, Layer = layer
            };
        }

        /// <summary>
        /// A free-space pick is accepted only on the true Model tab. A point picked in a layout is a
        /// paper-space point and a point picked through a layout viewport is not a model coordinate,
        /// so neither may ever be stored.
        /// </summary>
        internal static EquipmentPlacementPick? CreateFreeSpacePick(
            EquipmentPlacementPoint promptPoint,
            EquipmentPlacementUcsTransform ucsToWcs,
            bool isTrueModelTab)
        {
            if (!isTrueModelTab) return null;
            EquipmentPlacementPoint world = ToWorld(promptPoint, ucsToWcs);
            return new EquipmentPlacementPick
            {
                Source = EquipmentPlacementResolutionSources.FreeSpace,
                PointX = world.X, PointY = world.Y, PointZ = world.Z,
                Handle = null, Layer = null
            };
        }
    }
}
