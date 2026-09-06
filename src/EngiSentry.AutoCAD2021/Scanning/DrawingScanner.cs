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
        private const int MaxBlockTraversalDepth = 32;
        private const int MaxInstantiatedEntityVisits = 2000000;

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
            var layerStates = snapshot.Layers.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToDictionary(x => x.Name!, x => x.IsOn && !x.IsFrozen, StringComparer.OrdinalIgnoreCase);
            ReadSpace((BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead), transaction, snapshot, true, geometry, layerStates);
            DBDictionary layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in layouts)
            {
                Layout layout = (Layout)transaction.GetObject(entry.Value, OpenMode.ForRead);
                if (!layout.ModelType)
                    ReadSpace((BlockTableRecord)transaction.GetObject(layout.BlockTableRecordId, OpenMode.ForRead), transaction, snapshot, false, null, layerStates);
            }
        }

        private static void ReadSpace(BlockTableRecord space, Transaction transaction, DrawingSnapshot snapshot, bool model, PlotGeometryCapture? geometry, IReadOnlyDictionary<string, bool> layerStates)
        {
            foreach (ObjectId id in space)
            {
                DBObject raw = transaction.GetObject(id, OpenMode.ForRead);
                if (!(raw is Entity entity)) continue;
                if (model) snapshot.ModelSpaceEntityCount++; else snapshot.PaperSpaceEntityCount++;
                if (entity is BlockReference reference)
                {
                    ReadBlockReference(reference, transaction, snapshot);
                    if (model)
                    {
                        int visited = 0;
                        ReadInstantiatedBlockReference(reference, transaction, snapshot, geometry, new List<Matrix3d>(), Handle(reference), reference.Layer, Handle(reference), new HashSet<ObjectId>(), ref visited, 0, layerStates);
                    }
                }
                else if (entity is DBText text) { snapshot.TextCount++; snapshot.TextEntities.Add(Text(text, "TEXT", model ? "Model" : "Paper")); if (model && geometry != null && LayerActive(text.Layer, layerStates)) geometry.ModelSpaceTextSources.Add(TextSource(text)); }
                else if (entity is MText mtext) { snapshot.MTextCount++; snapshot.TextEntities.Add(Text(mtext, "MTEXT", model ? "Model" : "Paper")); if (model && geometry != null && LayerActive(mtext.Layer, layerStates)) geometry.ModelSpaceTextSources.Add(TextSource(mtext)); }
                else if (entity is Dimension dimension) { snapshot.DimensionCount++; snapshot.Dimensions.Add(new DimensionSnapshot { Handle = Handle(dimension), ObjectType = dimension.GetRXClass().DxfName, Layer = dimension.Layer, DimensionStyle = dimension.DimensionStyleName, Measurement = SafeMeasurement(dimension) }); }
                if (model && geometry != null && LayerActive(entity.Layer, layerStates)) { PlotGeometryPrimitive? primitive = Geometry(entity, transaction); if (primitive != null) geometry.Primitives.Add(primitive); }
            }
        }

        private static void ReadInstantiatedBlockReference(BlockReference reference, Transaction transaction, DrawingSnapshot snapshot, PlotGeometryCapture? geometry, List<Matrix3d> parentTransforms, string rootHandle, string? inheritedLayer, string path, HashSet<ObjectId> stack, ref int visited, int depth, IReadOnlyDictionary<string, bool> layerStates)
        {
            if (depth > MaxBlockTraversalDepth) throw new InvalidOperationException("Nested block traversal exceeded the supported depth.");
            ObjectId definitionId = reference.BlockTableRecord;
            if (definitionId.IsNull || stack.Contains(definitionId)) return;
            BlockTableRecord definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
            if (definition.IsFromExternalReference) return;

            string referenceLayer = EffectiveLayer(reference.Layer, inheritedLayer);
            if (!LayerActive(referenceLayer, layerStates)) return;
            Point3d insertion = Transform(reference.Position, parentTransforms);
            snapshot.InstantiatedTextEntities.Add(new TextSnapshot
            {
                Type = "BLOCKNAME", Handle = path, ObjectType = reference.GetRXClass().DxfName,
                Layer = referenceLayer, Text = reference.Name, PositionX = insertion.X, PositionY = insertion.Y, PositionZ = insertion.Z, SpaceKind = "Model",
                Height = 0, Rotation = reference.Rotation, Style = null
            });

            foreach (ObjectId attributeId in reference.AttributeCollection)
            {
                AttributeReference? attribute = transaction.GetObject(attributeId, OpenMode.ForRead) as AttributeReference;
                if (attribute == null || string.IsNullOrWhiteSpace(attribute.TextString)) continue;
                Point3d position = Transform(attribute.Position, parentTransforms);
                string source = depth == 0 ? Handle(attribute) : path + ":" + Handle(attribute);
                string layer = EffectiveLayer(attribute.Layer, referenceLayer);
                snapshot.InstantiatedTextEntities.Add(new TextSnapshot
                {
                    Type = "ATTRIB", Handle = source, ObjectType = attribute.GetRXClass().DxfName, Layer = layer, Text = attribute.TextString,
                    PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, SpaceKind = "Model",
                    Height = TransformHeight(attribute.Height, parentTransforms), Rotation = attribute.Rotation, Style = attribute.TextStyleName
                });
                if (geometry != null && LayerActive(layer, layerStates)) geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = source, SourceLayer = layer, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Height = TransformHeight(attribute.Height, parentTransforms) });
            }

            var currentTransforms = new List<Matrix3d> { reference.BlockTransform };
            currentTransforms.AddRange(parentTransforms);
            stack.Add(definitionId);
            try
            {
                foreach (ObjectId id in definition)
                {
                    visited++;
                    if (visited > MaxInstantiatedEntityVisits) throw new InvalidOperationException("Instantiated block traversal exceeded the safety limit of " + MaxInstantiatedEntityVisits + " entities.");
                    DBObject raw = transaction.GetObject(id, OpenMode.ForRead);
                    if (!(raw is Entity entity)) continue;
                    string layer = EffectiveLayer(entity.Layer, referenceLayer);
                    string source = path + ":" + Handle(entity);
                    if (entity is BlockReference child)
                    {
                        ReadInstantiatedBlockReference(child, transaction, snapshot, geometry, currentTransforms, rootHandle, referenceLayer, source, stack, ref visited, depth + 1, layerStates);
                        continue;
                    }
                    if (entity is DBText text)
                    {
                        Point3d position = Transform(text.Position, currentTransforms);
                        double height = TransformHeight(text.Height, currentTransforms);
                        snapshot.InstantiatedTextEntities.Add(new TextSnapshot { Type = "TEXT", Handle = source, ObjectType = text.GetRXClass().DxfName, Layer = layer, Text = text.TextString, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Height = height, Rotation = text.Rotation, Style = text.TextStyleName, SpaceKind = "Model" });
                        if (geometry != null && LayerActive(layer, layerStates)) geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = source, SourceLayer = layer, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Height = height });
                    }
                    else if (entity is MText mtext)
                    {
                        Point3d position = Transform(mtext.Location, currentTransforms);
                        double height = TransformHeight(mtext.TextHeight, currentTransforms);
                        snapshot.InstantiatedTextEntities.Add(new TextSnapshot { Type = "MTEXT", Handle = source, ObjectType = mtext.GetRXClass().DxfName, Layer = layer, Text = mtext.Contents, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Height = height, Rotation = mtext.Rotation, Style = mtext.TextStyleName, SpaceKind = "Model" });
                        if (geometry != null && LayerActive(layer, layerStates)) geometry.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = source, SourceLayer = layer, PositionX = position.X, PositionY = position.Y, PositionZ = position.Z, Height = height });
                    }
                    if (geometry != null && LayerActive(layer, layerStates))
                    {
                        PlotGeometryPrimitive? primitive = Geometry(entity, transaction);
                        if (primitive != null) geometry.Primitives.Add(TransformPrimitive(primitive, currentTransforms, source, layer));
                    }
                }
            }
            finally { stack.Remove(definitionId); }
        }

        private static string EffectiveLayer(string? layer, string? inheritedLayer)
        {
            if (string.IsNullOrWhiteSpace(layer) || string.Equals(layer, "0", StringComparison.OrdinalIgnoreCase)) return inheritedLayer ?? layer ?? "0";
            return layer!;
        }

        private static bool LayerActive(string? layer, IReadOnlyDictionary<string, bool> layerStates)
        {
            if (string.IsNullOrWhiteSpace(layer) || layerStates.Count == 0) return true;
            bool active;
            return !layerStates.TryGetValue(layer!, out active) || active;
        }

        private static Point3d Transform(Point3d point, IEnumerable<Matrix3d> transforms)
        {
            Point3d result = point;
            foreach (Matrix3d transform in transforms) result = result.TransformBy(transform);
            return result;
        }

        private static double TransformHeight(double height, IEnumerable<Matrix3d> transforms)
        {
            if (height <= 0) return height;
            Point3d origin = Transform(Point3d.Origin, transforms);
            Point3d tip = Transform(new Point3d(0, height, 0), transforms);
            double scaled = origin.DistanceTo(tip);
            return double.IsNaN(scaled) || double.IsInfinity(scaled) || scaled <= 0 ? height : scaled;
        }

        private static PlotGeometryPrimitive TransformPrimitive(PlotGeometryPrimitive primitive, IEnumerable<Matrix3d> transforms, string sourceHandle, string sourceLayer)
        {
            var result = new PlotGeometryPrimitive { SourceHandle = sourceHandle, SourceLayer = sourceLayer, GeometryKind = primitive.GeometryKind, Closed = primitive.Closed };
            foreach (PlotGeometryVertex vertex in primitive.Vertices)
            {
                Point3d p = Transform(new Point3d(vertex.X, vertex.Y, vertex.Z), transforms);
                result.Vertices.Add(new PlotGeometryVertex { X = p.X, Y = p.Y, Z = p.Z });
            }
            return result;
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

        private static TextSnapshot Text(DBText text, string type, string spaceKind) => new TextSnapshot { Type = type, Handle = Handle(text), ObjectType = text.GetRXClass().DxfName, Layer = text.Layer, Text = text.TextString, PositionX = text.Position.X, PositionY = text.Position.Y, PositionZ = text.Position.Z, Height = text.Height, Rotation = text.Rotation, Style = text.TextStyleName, SpaceKind = spaceKind };
        private static TextSnapshot Text(MText text, string type, string spaceKind) => new TextSnapshot { Type = type, Handle = Handle(text), ObjectType = text.GetRXClass().DxfName, Layer = text.Layer, Text = text.Contents, PositionX = text.Location.X, PositionY = text.Location.Y, PositionZ = text.Location.Z, Height = text.TextHeight, Rotation = text.Rotation, Style = text.TextStyleName, SpaceKind = spaceKind };
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
