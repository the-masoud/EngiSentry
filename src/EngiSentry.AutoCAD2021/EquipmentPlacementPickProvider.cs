using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// One explicit engineer pick. The point is the exact WCS coordinate of the pick, after the
    /// current UCS transform has been applied; the handle and layer are the proven identity of the
    /// picked model-space entity, present only for the entity path. No snapping, inference, or
    /// nearest-entity search is performed by this code.
    /// </summary>
    internal sealed class EquipmentPlacementPick
    {
        internal string Source { get; set; } = string.Empty;
        internal double PointX { get; set; }
        internal double PointY { get; set; }
        internal double PointZ { get; set; }
        internal string? Handle { get; set; }
        internal string? Layer { get; set; }
    }

    internal interface IEquipmentPlacementPickProvider
    {
        /// <summary>An explicit model-space entity pick, or null when the engineer cancelled or the pick was invalid.</summary>
        EquipmentPlacementPick? PickEntity();
        /// <summary>An explicit free-space point, or null when the engineer cancelled.</summary>
        EquipmentPlacementPick? PickFreeSpace();
    }

    /// <summary>
    /// AutoCAD editor-backed pick provider. Cancelled, invalid, or out-of-scope picks return null so
    /// the caller writes nothing. Only read-only editor prompts are issued; the drawing database is
    /// never modified.
    ///
    /// Two editor facts are handled explicitly:
    /// - an editor prompt point is always expressed in the current UCS, never in the WCS, so the
    ///   current UCS transform is applied before any coordinate is stored;
    /// - a pick is accepted only from the true Model tab, so paper space and model space reached
    ///   through a layout viewport are rejected before anything is stored.
    /// </summary>
    internal sealed class EditorEquipmentPlacementPickProvider : IEquipmentPlacementPickProvider
    {
        private readonly Document document;

        internal EditorEquipmentPlacementPickProvider(Document document)
        {
            this.document = document ?? throw new ArgumentNullException(nameof(document));
        }

        public EquipmentPlacementPick? PickEntity()
        {
            if (!IsTrueModelTab()) return null;
            EquipmentPlacementUcsTransform? ucsToWcs = TryGetUcsToWcs();
            if (ucsToWcs == null) return null;
            PromptEntityOptions options = new PromptEntityOptions("\nSelect the equipment entity in model space (press Esc to cancel): ");
            PromptEntityResult result = document.Editor.GetEntity(options);
            if (result.Status != PromptStatus.OK || result.ObjectId.IsNull) return null;
            using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
            {
                Entity? entity = transaction.GetObject(result.ObjectId, OpenMode.ForRead, false) as Entity;
                if (entity == null) return null;
                ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(document.Database);
                return EquipmentPlacementPickRules.CreateEntityPick(
                    FromPoint3d(result.PickedPoint),
                    ucsToWcs,
                    true,
                    entity.OwnerId == modelSpaceId,
                    entity is Viewport,
                    entity.Handle.ToString(),
                    entity.Layer);
            }
        }

        public EquipmentPlacementPick? PickFreeSpace()
        {
            if (!IsTrueModelTab()) return null;
            EquipmentPlacementUcsTransform? ucsToWcs = TryGetUcsToWcs();
            if (ucsToWcs == null) return null;
            PromptPointOptions options = new PromptPointOptions("\nPick the equipment free-space location (press Esc to cancel): ") { AllowNone = false };
            PromptPointResult result = document.Editor.GetPoint(options);
            if (result.Status != PromptStatus.OK) return null;
            return EquipmentPlacementPickRules.CreateFreeSpacePick(FromPoint3d(result.Value), ucsToWcs, true);
        }

        /// <summary>
        /// True only on the true Model tab. A layout (paper space) and model space reached through a
        /// layout viewport both report TILEMODE = 0, so both are rejected. Any failure to read the
        /// state fails closed.
        /// </summary>
        private bool IsTrueModelTab()
        {
            try { return document.Database.TileMode; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The editor's current UCS to WCS transform, or null when it cannot be read. Editor prompt
        /// points are expressed in the current UCS, so without this transform a pick must not be
        /// stored at all.
        /// </summary>
        private EquipmentPlacementUcsTransform? TryGetUcsToWcs()
        {
            try
            {
                Matrix3d ucs = document.Editor.CurrentUserCoordinateSystem;
                return EquipmentPlacementUcsTransform.FromComponents(
                    ucs[0, 0], ucs[0, 1], ucs[0, 2],
                    ucs[1, 0], ucs[1, 1], ucs[1, 2],
                    ucs[2, 0], ucs[2, 1], ucs[2, 2],
                    ucs[0, 3], ucs[1, 3], ucs[2, 3]);
            }
            catch (Exception) { return null; }
        }

        private static EquipmentPlacementPoint FromPoint3d(Point3d point) => new EquipmentPlacementPoint(point.X, point.Y, point.Z);
    }

    internal static class EquipmentPlacementPickFormatting
    {
        /// <summary>Converts a picked WCS ordinate to the exact stored decimal, or null when it is out of range.</summary>
        internal static decimal? ToStoredDecimal(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;
            try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return null; }
        }

        internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
