using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021.Scanning
{
    internal static class DrawingScanner
    {
        internal static void Scan(Document document, out DrawingSnapshot snapshot)
        {
            Scan(document, false, out snapshot, out _);
        }

        internal static void Scan(Document document, bool capturePlotGeometry, out DrawingSnapshot snapshot, out PlotGeometryCapture geometry)
        {
            geometry = new PlotGeometryCapture();
            int before = ReadDbMod();
            var stopwatch = Stopwatch.StartNew();
            snapshot = new DrawingSnapshot
            {
                FileName = string.IsNullOrEmpty(document.Name) ? null : Path.GetFileName(document.Name),
                FullPath = string.IsNullOrEmpty(document.Name) ? null : document.Name,
                ScanTimestampUtc = DateTime.UtcNow,
                DbModBefore = before
            };
            try
            {
                Database database = document.Database;
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    snapshot.DwgVersion = null;
                    snapshot.InsertionUnits = database.Insunits.ToString();
                    snapshot.CurrentLayout = LayoutManager.Current.CurrentLayout;
                    ReadLayouts(database, transaction, snapshot);
                    ReadLayers(database, transaction, snapshot);
                    ReadBlocks(database, transaction, snapshot);
                    ReadSpaces(database, transaction, snapshot, capturePlotGeometry ? geometry : null);
                    ReadTextStyles(database, transaction, snapshot);
                    ReadRasterImages(database, transaction, snapshot);
                    transaction.Commit();
                }
                stopwatch.Stop();
                snapshot.ScanDurationMs = stopwatch.ElapsedMilliseconds;
                snapshot.DbModAfter = ReadDbMod();
                snapshot.DrawingModifiedByScan = snapshot.DbModBefore != snapshot.DbModAfter;
                if (snapshot.DrawingModifiedByScan) throw new InvalidOperationException("DBMOD changed during read-only scan.");
                return;
            }
            catch
            {
                stopwatch.Stop();
                snapshot.ScanDurationMs = stopwatch.ElapsedMilliseconds;
                snapshot.DbModAfter = ReadDbMod();
                snapshot.DrawingModifiedByScan = snapshot.DbModBefore != snapshot.DbModAfter;
                throw;
            }
        }

        private static int ReadDbMod()
        {
            object value = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DBMOD");
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ReadLayouts(Database database, Transaction transaction, DrawingSnapshot snapshot)
        {
            DBDictionary layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                Layout layout = (Layout)transaction.GetObject(entry.Value, OpenMode.ForRead);
                snapshot.Layouts.Add(new LayoutSnapshot { Name = layout.LayoutName, IsModel = layout.ModelType, TabOrder = layout.TabOrder });
            }
        }

        private static void ReadLayers(Database database, Transaction transaction, DrawingSnapshot snapshot)
        {
            LayerTable layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in layers)
            {
                LayerTableRecord layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                string? linetype = null;
                if (!layer.LinetypeObjectId.IsNull)
                    linetype = ((LinetypeTableRecord)transaction.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name;
                snapshot.Layers.Add(new LayerSnapshot
                {
                    Name = layer.Name, IsOn = !layer.IsOff, IsFrozen = layer.IsFrozen,
                    IsLocked = layer.IsLocked, Color = layer.Color.ColorIndex, Linetype = linetype,
                    Lineweight = layer.LineWeight.ToString(), IsPlottable = layer.IsPlottable
                });
            }
        }

        private static void ReadBlocks(Database database, Transaction transaction, DrawingSnapshot snapshot)
        {
            BlockTable blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId id in blocks)
            {
                BlockTableRecord block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                int entityCount = 0;
                foreach (ObjectId ignored in block) entityCount++;
                snapshot.BlockDefinitionEntityCount += entityCount;
                snapshot.BlockDefinitions.Add(new BlockDefinitionSnapshot
                {
                    Name = block.Name, IsAnonymous = block.IsAnonymous, IsLayout = block.IsLayout,
                    IsXref = block.IsFromExternalReference, EntityCount = entityCount
                });
                if (block.IsFromExternalReference)
                {
                    string status = block.XrefStatus.ToString();
                    snapshot.Xrefs.Add(new XrefSnapshot
                    {
                        Name = block.Name, RawPath = block.PathName, ResolvedPath = block.PathName,
                        Status = status, IsResolved = string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase),
                        IsNested = block.IsDependent
                    });
                }
            }
        }

        private static void ReadSpaces(Database database, Transaction transaction, DrawingSnapshot snapshot, PlotGeometryCapture? geometry)
        {
            BlockTable blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            ReadSpace((BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead), transaction, snapshot, true, geometry);
            DBDictionary layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                Layout layout = (Layout)transaction.GetObject(entry.Value, OpenMode.ForRead);
                if (!layout.ModelType)
                    ReadSpace((BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead), transaction, snapshot, false, null);
            }
        }

        private static void ReadSpace(BlockTableRecord space, Transaction transaction, DrawingSnapshot snapshot, bool model, PlotGeometryCapture? geometry)
        {
            foreach (ObjectId id in space)
            {
                DBObject raw = transaction.GetObject(id, OpenMode.ForRead);
                if (!(raw is Entity entity)) continue;
                if (model) snapshot.ModelSpaceEntityCount++; else snapshot.PaperSpaceEntityCount++;
                if (entity is BlockReference reference) ReadBlockReference(reference, transaction, snapshot);
                else if (entity is DBText text) { snapshot.TextCount++; snapshot.TextEntities.Add(Text(text, "TEXT")); if (model && geometry != null) geometry.ModelSpaceTextSources.Add(TextSource(text)); }
                else if (entity is MText mtext) { snapshot.MTextCount++; snapshot.TextEntities.Add(Text(mtext, "MTEXT")); if (model && geometry != null) geometry.ModelSpaceTextSources.Add(TextSource(mtext)); }
                else if (entity is Dimension dimension) { snapshot.DimensionCount++; snapshot.Dimensions.Add(new DimensionSnapshot { Handle = Handle(dimension), ObjectType = dimension.GetRXClass().DxfName, Layer = dimension.Layer, DimensionStyle = dimension.DimensionStyleName, Measurement = SafeMeasurement(dimension) }); }
                if (model && geometry != null) { PlotGeometryPrimitive? primitive = Geometry(entity, transaction); if (primitive != null) geometry.Primitives.Add(primitive); }
            }
        }

        private static void ReadBlockReference(BlockReference reference, Transaction transaction, DrawingSnapshot snapshot)
        {
            snapshot.BlockReferenceCount++;
            var result = new BlockReferenceSnapshot { BlockName = reference.Name, Handle = Handle(reference), ObjectType = reference.GetRXClass().DxfName, Layer = reference.Layer, PositionX = reference.Position.X, PositionY = reference.Position.Y, PositionZ = reference.Position.Z, Rotation = reference.Rotation, ScaleX = reference.ScaleFactors.X, ScaleY = reference.ScaleFactors.Y, ScaleZ = reference.ScaleFactors.Z };
            foreach (ObjectId id in reference.AttributeCollection)
            {
                AttributeReference? attribute = transaction.GetObject(id, OpenMode.ForRead) as AttributeReference;
                if (attribute != null) result.Attributes.Add(new AttributeSnapshot { Tag = attribute.Tag, Value = attribute.TextString, Handle = Handle(attribute), ObjectType = attribute.GetRXClass().DxfName, Layer = attribute.Layer });
            }
            snapshot.BlockReferences.Add(result);
        }

        private static TextSnapshot Text(DBText text, string type) => new TextSnapshot { Type = type, Handle = Handle(text), ObjectType = text.GetRXClass().DxfName, Layer = text.Layer, Text = text.TextString, PositionX = text.Position.X, PositionY = text.Position.Y, PositionZ = text.Position.Z, Height = text.Height, Rotation = text.Rotation, Style = text.TextStyleName };
        private static TextSnapshot Text(MText text, string type) => new TextSnapshot { Type = type, Handle = Handle(text), ObjectType = text.GetRXClass().DxfName, Layer = text.Layer, Text = text.Contents, PositionX = text.Location.X, PositionY = text.Location.Y, PositionZ = text.Location.Z, Height = text.TextHeight, Rotation = text.Rotation, Style = text.TextStyleName };
        private static PlotTextSourceRecord TextSource(DBText text) => new PlotTextSourceRecord { SourceHandle = Handle(text), SourceLayer = text.Layer, PositionX = text.Position.X, PositionY = text.Position.Y, PositionZ = text.Position.Z, Height = text.Height };
        private static PlotTextSourceRecord TextSource(MText text) => new PlotTextSourceRecord { SourceHandle = Handle(text), SourceLayer = text.Layer, PositionX = text.Location.X, PositionY = text.Location.Y, PositionZ = text.Location.Z, Height = text.TextHeight };
        private static PlotGeometryPrimitive? Geometry(Entity entity, Transaction transaction)
        {
            var result = new PlotGeometryPrimitive { SourceHandle = Handle(entity), SourceLayer = entity.Layer };
            if (entity is Line line) { result.GeometryKind = "Line"; Add(result, line.StartPoint); Add(result, line.EndPoint); return result; }
            if (entity is Polyline polyline) { if (Enumerable.Range(0, polyline.NumberOfVertices).Any(i => polyline.GetBulgeAt(i) != 0)) return null; result.GeometryKind="Polyline"; result.Closed=polyline.Closed; for(int i=0;i<polyline.NumberOfVertices;i++){Point2d p=polyline.GetPoint2dAt(i); result.Vertices.Add(new PlotGeometryVertex{X=p.X,Y=p.Y,Z=polyline.Elevation});} return result; }
            if (entity is Polyline2d polyline2d) { result.GeometryKind="Polyline2d"; result.Closed=polyline2d.Closed; foreach(ObjectId id in polyline2d){Vertex2d v=(Vertex2d)transaction.GetObject(id,OpenMode.ForRead); if(v.Bulge!=0)return null; Add(result,v.Position);} return result; }
            if (entity is Polyline3d polyline3d) { result.GeometryKind="Polyline3d"; result.Closed=polyline3d.Closed; foreach(ObjectId id in polyline3d){PolylineVertex3d v=(PolylineVertex3d)transaction.GetObject(id,OpenMode.ForRead);Add(result,v.Position);} return result; }
            return null;
        }
        private static void Add(PlotGeometryPrimitive primitive, Point3d point) { primitive.Vertices.Add(new PlotGeometryVertex { X = point.X, Y = point.Y, Z = point.Z }); }
        private static string Handle(DBObject obj) => obj.Handle.ToString();
        private static double? SafeMeasurement(Dimension dimension) { try { return dimension.Measurement; } catch { return null; } }

        private static void ReadTextStyles(Database database, Transaction transaction, DrawingSnapshot snapshot)
        {
            TextStyleTable styles = (TextStyleTable)transaction.GetObject(database.TextStyleTableId, OpenMode.ForRead);
            foreach (ObjectId id in styles)
            {
                TextStyleTableRecord style = (TextStyleTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                AddDependency(snapshot, "TextStyleFont", style.Name, style.FileName);
                AddDependency(snapshot, "BigFont", style.Name, style.BigFontFileName);
            }
        }

        private static void AddDependency(DrawingSnapshot snapshot, string kind, string name, string path)
        {
            snapshot.ExternalDependencies.Add(DependencyClassifier.Classify(kind, name, path));
        }

        private static void ReadRasterImages(Database database, Transaction transaction, DrawingSnapshot snapshot)
        {
            ObjectId dictionaryId = RasterImageDef.GetImageDictionary(database);
            if (dictionaryId.IsNull) return;
            DBDictionary images = (DBDictionary)transaction.GetObject(dictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in images)
            {
                RasterImageDef definition = (RasterImageDef)transaction.GetObject(entry.Value, OpenMode.ForRead);
                AddDependency(snapshot, "RasterImage", entry.Key, definition.SourceFileName);
            }
        }
    }
}
