using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using EngiSentry.Contracts;

namespace EngiSentry.Semantics
{
    public static class SemanticExtractor
    {
        private const double RowYTolerance = 1.0;
        private static readonly Regex Equipment = new Regex(@"^\s*([A-Za-z]{1,5})-([0-9]+)-([0-9]+)([A-Za-z]+(?:/[A-Za-z]+)*)?\s*$", RegexOptions.Compiled);
        private static readonly Regex Structure = new Regex(@"^\s*([A-Za-z0-9]+)-([A-Za-z]{2})-([0-9]+)\s*$", RegexOptions.Compiled);
        private static readonly Regex Coordinate = new Regex(@"^\s*([EN])\s*\.?\s*([+-]?[0-9]+(?:\.[0-9]+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Elevation = new Regex(@"^\s*([TH])\s*\.?\s*([OPSGC])\s*\.?\s*([PSGC])?\s*\.?\s*EL\s*\.?\s*([+-]?[0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BoundaryCoordinate = new Regex(@"UNIT\s+LIMIT(?:\s+([EN])\s*\.?\s*([+-]?[0-9]+(?:\.[0-9]+)?))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static EngineeringDrawingModel Extract(DrawingSnapshot snapshot)
        {
            var model = new EngineeringDrawingModel { Drawing = new SemanticDrawing { FileName = snapshot.FileName, FullPath = snapshot.FullPath, DwgVersion = snapshot.DwgVersion, CurrentLayout = snapshot.CurrentLayout } };
            foreach (TextSnapshot text in snapshot.TextEntities.OrderBy(x => x.Handle, StringComparer.Ordinal))
            {
                EquipmentTag? equipment = ParseEquipment(text); if (equipment != null) model.EquipmentTags.Add(equipment);
                StructureTag? structure = ParseStructure(text); if (structure != null) model.StructureTags.Add(structure);
                CoordinateEvidence? coordinate = ParseCoordinate(text); if (coordinate != null) model.Coordinates.Add(coordinate);
                ElevationEvidence? elevation = ParseElevation(text); if (elevation != null) model.Elevations.Add(elevation);
                if (text.Text != null && text.Text.IndexOf("ROAD C.L.", StringComparison.OrdinalIgnoreCase) >= 0) model.RoadLabels.Add(Evidence(text));
                BoundaryEvidence? boundary = ParseBoundary(text); if (boundary != null) model.BoundaryLabels.Add(boundary);
            }
            ExtractEquipmentList(snapshot.TextEntities, model);
            Sort(model);
            model.Statistics = new SemanticStatistics
            {
                EquipmentTagOccurrences = model.EquipmentTags.Count,
                UniqueEquipmentTags = model.EquipmentTags.Select(x => x.NormalizedTag).Where(x => x != null).Distinct(StringComparer.Ordinal).Count(),
                StructureTagOccurrences = model.StructureTags.Count,
                UniqueStructureTags = model.StructureTags.Select(x => x.NormalizedTag).Where(x => x != null).Distinct(StringComparer.Ordinal).Count(),
                CoordinateCount = model.Coordinates.Count, ElevationCount = model.Elevations.Count,
                RoadLabelCount = model.RoadLabels.Count, BoundaryLabelCount = model.BoundaryLabels.Count,
                EquipmentListEntryCount = model.EquipmentListEntries.Count
            };
            return model;
        }

        private static EquipmentTag? ParseEquipment(TextSnapshot text)
        {
            Match match = Equipment.Match(text.Text ?? string.Empty); if (!match.Success) return null;
            return new EquipmentTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=(text.Text ?? string.Empty).Trim(), EquipmentPrefix=match.Groups[1].Value, UnitNumber=match.Groups[2].Value, SequenceNumber=match.Groups[3].Value, Suffix=match.Groups[4].Success ? match.Groups[4].Value : null, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static StructureTag? ParseStructure(TextSnapshot text)
        {
            Match match = Structure.Match(text.Text ?? string.Empty); if (!match.Success) return null;
            return new StructureTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=(text.Text ?? string.Empty).Trim(), AreaOrUnitCode=match.Groups[1].Value, TypeCode=match.Groups[2].Value, SequenceNumber=match.Groups[3].Value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static CoordinateEvidence? ParseCoordinate(TextSnapshot text)
        {
            Match match = Coordinate.Match(text.Text ?? string.Empty); decimal value = 0; if (!match.Success || !decimal.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
            return new CoordinateEvidence { RawText=text.Text, Axis=match.Groups[1].Value.ToUpperInvariant(), Value=value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static ElevationEvidence? ParseElevation(TextSnapshot text)
        {
            Match match = Elevation.Match(text.Text ?? string.Empty); decimal value; if (!match.Success || !decimal.TryParse(match.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
            string type = match.Groups[1].Value.ToUpperInvariant() + match.Groups[2].Value.ToUpperInvariant() + (match.Groups[3].Success ? match.Groups[3].Value.ToUpperInvariant() : string.Empty);
            return new ElevationEvidence { RawText=text.Text, ElevationType=type, Value=value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static BoundaryEvidence? ParseBoundary(TextSnapshot text)
        {
            Match match = BoundaryCoordinate.Match(text.Text ?? string.Empty); if (!match.Success) return null;
            decimal value = 0; bool hasValue = match.Groups[2].Success && decimal.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            return new BoundaryEvidence { RawText=text.Text, Axis=match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : null, CoordinateValue=hasValue ? value : (decimal?)null, ParseStatus=hasValue ? "Parsed" : "Partial", SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static TextEvidence Evidence(TextSnapshot text) => new TextEvidence { RawText=text.Text, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };

        private static void ExtractEquipmentList(IEnumerable<TextSnapshot> all, EngineeringDrawingModel model)
        {
            List<TextSnapshot> texts = all.Where(x => string.Equals(x.Layer, "EQ-LIST", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.PositionY).ThenBy(x => x.PositionX).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
            TextSnapshot? name = texts.FirstOrDefault(x => string.Equals((x.Text ?? string.Empty).Trim(), "NAME", StringComparison.OrdinalIgnoreCase));
            TextSnapshot? description = texts.FirstOrDefault(x => string.Equals((x.Text ?? string.Empty).Trim(), "DESCRIPTION", StringComparison.OrdinalIgnoreCase));
            TextSnapshot? installation = texts.FirstOrDefault(x => string.Equals((x.Text ?? string.Empty).Trim(), "INSTALLATION", StringComparison.OrdinalIgnoreCase));
            if (name == null || description == null || installation == null) return;
            var rows = new List<List<TextSnapshot>>();
            foreach (TextSnapshot text in texts.Where(x => x != name && x != description && x != installation))
            {
                List<TextSnapshot>? row = rows.FirstOrDefault(r => Math.Abs(r[0].PositionY - text.PositionY) <= RowYTolerance);
                if (row == null) { row = new List<TextSnapshot>(); rows.Add(row); } row.Add(text);
            }
            foreach (List<TextSnapshot> row in rows)
            {
                List<TextSnapshot> tagTexts = row.Where(x => ParseEquipment(x) != null).OrderBy(x => x.PositionX).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
                if (tagTexts.Count != 1) continue;
                TextSnapshot tagText = tagTexts[0];
                List<TextSnapshot> cells = row.Where(x => x != tagText).OrderBy(x => x.PositionX).ThenBy(x => x.Handle, StringComparer.Ordinal).ToList();
                if (cells.Count == 0 || cells.Any(x => x.PositionX <= tagText.PositionX)) continue;
                EquipmentTag tag = ParseEquipment(tagText)!;
                var entry = new EquipmentListEntry { Tag = tag.NormalizedTag, ParseStatus = "Parsed" };
                if (cells.Count > 2) entry.ParseStatus = "Partial";
                bool horizontalAmbiguity = cells.Zip(cells.Skip(1), (left, right) => left.PositionX == right.PositionX).Any(x => x);
                if (horizontalAmbiguity) entry.ParseStatus = "Partial";
                foreach (TextSnapshot text in row.OrderBy(x => x.Handle, StringComparer.Ordinal))
                {
                    string handle = text.Handle ?? string.Empty;
                    if (text == tagText) { if (handle.Length != 0) entry.SourceHandles.Add(handle); continue; }
                    if (horizontalAmbiguity) { if (handle.Length != 0) entry.SourceHandles.Add(handle); continue; }
                    int cellIndex = cells.IndexOf(text);
                    if (cellIndex == 0) entry.Description = text.Text == null ? null : text.Text.Trim();
                    else if (cellIndex == 1) entry.Installation = text.Text == null ? null : text.Text.Trim();
                    if (handle.Length != 0) entry.SourceHandles.Add(handle);
                }
                model.EquipmentListEntries.Add(entry);
            }
        }
        private static void Sort(EngineeringDrawingModel model)
        {
            model.EquipmentTags = model.EquipmentTags.OrderBy(x=>x.NormalizedTag,StringComparer.Ordinal).ThenBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList();
            model.StructureTags = model.StructureTags.OrderBy(x=>x.NormalizedTag,StringComparer.Ordinal).ThenBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList();
            model.Coordinates = model.Coordinates.OrderBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList(); model.Elevations = model.Elevations.OrderBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList(); model.RoadLabels = model.RoadLabels.OrderBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList(); model.BoundaryLabels = model.BoundaryLabels.OrderBy(x=>x.SourceHandle,StringComparer.Ordinal).ToList(); model.EquipmentListEntries = model.EquipmentListEntries.OrderBy(x=>x.SourceHandles.FirstOrDefault(),StringComparer.Ordinal).ToList();
        }
    }
}
