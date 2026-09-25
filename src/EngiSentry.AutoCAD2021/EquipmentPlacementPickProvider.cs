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
    /// One explicit engineer pick. The point is the exact WCS point the editor returned; the
    /// handle and layer are the proven identity of the picked model-space entity, present only for
    /// the entity path. No snapping, inference, or nearest-entity search is performed by this code.
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
    /// AutoCAD editor-backed pick provider. Cancelled or invalid picks return null so the caller
    /// writes nothing. Only read-only editor prompts are issued; the drawing database is never
    /// modified.
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
            PromptEntityOptions options = new PromptEntityOptions("\nSelect the equipment entity in model space (press Esc to cancel): ");
            PromptEntityResult result = document.Editor.GetEntity(options);
            if (result.Status != PromptStatus.OK || result.ObjectId.IsNull) return null;
            using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
            {
                Entity? entity = transaction.GetObject(result.ObjectId, OpenMode.ForRead, false) as Entity;
                if (entity == null) return null;
                ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(document.Database);
                if (entity.OwnerId != modelSpaceId) return null;
                if (entity is Viewport) return null;
                string handle = entity.Handle.ToString();
                string layer = entity.Layer ?? string.Empty;
                if (string.IsNullOrWhiteSpace(handle) || string.IsNullOrWhiteSpace(layer)) return null;
                Point3d point = result.PickedPoint;
                return new EquipmentPlacementPick
                {
                    Source = EquipmentPlacementResolutionSources.EntityPick,
                    PointX = point.X, PointY = point.Y, PointZ = point.Z,
                    Handle = handle, Layer = layer
                };
            }
        }

        public EquipmentPlacementPick? PickFreeSpace()
        {
            PromptPointOptions options = new PromptPointOptions("\nPick the equipment free-space location (press Esc to cancel): ") { AllowNone = false };
            PromptPointResult result = document.Editor.GetPoint(options);
            if (result.Status != PromptStatus.OK) return null;
            Point3d point = result.Value;
            return new EquipmentPlacementPick
            {
                Source = EquipmentPlacementResolutionSources.FreeSpace,
                PointX = point.X, PointY = point.Y, PointZ = point.Z,
                Handle = null, Layer = null
            };
        }
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
