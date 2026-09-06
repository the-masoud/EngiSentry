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
        private static readonly Regex EquipmentThreePart = new Regex(@"^\s*([A-Za-z]{1,6})-([0-9]+)-([0-9]+)([A-Za-z]+(?:[/~][A-Za-z]+)*)?\s*$", RegexOptions.Compiled);
        private static readonly Regex EquipmentTwoPart = new Regex(@"^\s*([A-Za-z]{1,6})-?([0-9]{3,6})(?:-?([A-Za-z][A-Za-z0-9]*(?:[/~][A-Za-z0-9]+)*))?\s*$", RegexOptions.Compiled);
        private static readonly Regex StructureLegacy = new Regex(@"^\s*([A-Za-z0-9]+)-([A-Za-z]{2})-([0-9]+)\s*$", RegexOptions.Compiled);
        private static readonly Regex StructureTypeFirst = new Regex(@"^\s*([A-Za-z]{2,6})-([0-9]{1,4})-([0-9]{1,4})\s*$", RegexOptions.Compiled);
        private static readonly Regex Coordinate = new Regex(@"^\s*([EN])\s*(?:[.=]\s*|\s+)([+-]?[0-9][0-9.,]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Elevation = new Regex(@"^\s*([TH])\s*\.?\s*([OPSGC])\s*\.?\s*([PSGC])?\s*\.?\s*EL\s*\.?\s*([+-]?[0-9][0-9.,]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BoundaryKeyword = new Regex(@"\b(UNIT\s+LIMIT|BATTERY\s+LIMIT|FENCE|BOUNDARY)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BoundaryCoordinate = new Regex(@"\b(?:UNIT\s+LIMIT|BATTERY\s+LIMIT|BOUNDARY)\b.*?([EN])\s*(?:[.=]\s*|\s+)([+-]?[0-9][0-9.,]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static EngineeringDrawingModel Extract(DrawingSnapshot snapshot)
        {
            var model = new EngineeringDrawingModel { Drawing = new SemanticDrawing { FileName = snapshot.FileName, FullPath = snapshot.FullPath, DwgVersion = snapshot.DwgVersion, CurrentLayout = snapshot.CurrentLayout } };
            var layerVisibility = snapshot.Layers.Where(x => !string.IsNullOrWhiteSpace(x.Name)).ToDictionary(x => x.Name!, x => x.IsOn && !x.IsFrozen, StringComparer.OrdinalIgnoreCase);
            List<TextSnapshot> allText = snapshot.TextEntities.Concat(snapshot.InstantiatedTextEntities ?? new List<TextSnapshot>()).Where(x => !IsBlockDefinitionMetadata(x) && IsLayerActive(x.Layer, layerVisibility)).OrderBy(x => x.Handle, StringComparer.Ordinal).ThenBy(x => x.Text, StringComparer.Ordinal).ToList();
            foreach (TextSnapshot text in allText)
            {
                StructureTag? structure = ParseStructure(text); if (structure != null) model.StructureTags.Add(structure);
                EquipmentTag? equipment = structure == null ? ParseEquipment(text) : null; if (equipment != null) model.EquipmentTags.Add(equipment);
                CoordinateEvidence? coordinate = ParseCoordinate(text); if (coordinate != null) model.Coordinates.Add(coordinate);
                ElevationEvidence? elevation = ParseElevation(text); if (elevation != null) model.Elevations.Add(elevation);
                if (string.Equals(text.SpaceKind, "Model", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsRoadLabel(text.Text)) model.RoadLabels.Add(Evidence(text));
                    BoundaryEvidence? boundary = ParseBoundary(text); if (boundary != null) model.BoundaryLabels.Add(boundary);
                }
            }
            ExtractEquipmentList(allText, model);
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
            // Block definition names are metadata, not visible drawing-tag evidence.
            if (string.Equals(text.Type, "BLOCKNAME", StringComparison.Ordinal)) return null;
            string raw = (text.Text ?? string.Empty).Trim();
            Match three = EquipmentThreePart.Match(raw);
            if (three.Success)
            {
                return new EquipmentTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=NormalizeIdentity(raw), EquipmentPrefix=three.Groups[1].Value.ToUpperInvariant(), UnitNumber=three.Groups[2].Value, SequenceNumber=three.Groups[3].Value, Suffix=three.Groups[4].Success ? three.Groups[4].Value.ToUpperInvariant() : null, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
            }
            Match compact = EquipmentTwoPart.Match(raw);
            if (!compact.Success) return null;
            return new EquipmentTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=NormalizeIdentity(raw), EquipmentPrefix=compact.Groups[1].Value.ToUpperInvariant(), UnitNumber=null, SequenceNumber=compact.Groups[2].Value, Suffix=compact.Groups[3].Success ? compact.Groups[3].Value.ToUpperInvariant() : null, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static StructureTag? ParseStructure(TextSnapshot text)
        {
            if (string.Equals(text.Type, "BLOCKNAME", StringComparison.Ordinal)) return null;
            string raw = text.Text ?? string.Empty;
            Match legacy = StructureLegacy.Match(raw);
            if (legacy.Success)
                return new StructureTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=NormalizeIdentity(raw), AreaOrUnitCode=legacy.Groups[1].Value, TypeCode=legacy.Groups[2].Value.ToUpperInvariant(), SequenceNumber=legacy.Groups[3].Value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
            Match match = StructureTypeFirst.Match(raw); if (!match.Success) return null;
            string type = match.Groups[1].Value.ToUpperInvariant();
            string layer = text.Layer ?? string.Empty;
            bool structuralContext = layer.IndexOf("STR", StringComparison.OrdinalIgnoreCase) >= 0 || type == "STR" || type == "PR" || type == "SH";
            if (!structuralContext) return null;
            return new StructureTag { RawText=text.Text, RawTag=text.Text, NormalizedTag=NormalizeIdentity(raw), AreaOrUnitCode=match.Groups[2].Value, TypeCode=type, SequenceNumber=match.Groups[3].Value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static CoordinateEvidence? ParseCoordinate(TextSnapshot text)
        {
            Match match = Coordinate.Match(text.Text ?? string.Empty); decimal value; if (!match.Success || !TryParseEngineeringNumber(match.Groups[2].Value, out value)) return null;
            return new CoordinateEvidence { RawText=text.Text, Axis=match.Groups[1].Value.ToUpperInvariant(), Value=value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static ElevationEvidence? ParseElevation(TextSnapshot text)
        {
            Match match = Elevation.Match(text.Text ?? string.Empty); decimal value; if (!match.Success || !TryParseEngineeringNumber(match.Groups[4].Value, out value)) return null;
            string type = match.Groups[1].Value.ToUpperInvariant() + match.Groups[2].Value.ToUpperInvariant() + (match.Groups[3].Success ? match.Groups[3].Value.ToUpperInvariant() : string.Empty);
            return new ElevationEvidence { RawText=text.Text, ElevationType=type, Value=value, SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static BoundaryEvidence? ParseBoundary(TextSnapshot text)
        {
            string raw = text.Text ?? string.Empty;
            Match keyword = BoundaryKeyword.Match(raw);
            if (!keyword.Success) return null;
            Match coordinate = BoundaryCoordinate.Match(raw);
            decimal value = 0; bool hasValue = coordinate.Success && TryParseEngineeringNumber(coordinate.Groups[2].Value, out value);
            string sourceKind = keyword.Success ? keyword.Groups[1].Value : (text.Layer ?? string.Empty);
            string kind = sourceKind.IndexOf("FENCE", StringComparison.OrdinalIgnoreCase) >= 0 ? "Fence" : sourceKind.IndexOf("BATTERY", StringComparison.OrdinalIgnoreCase) >= 0 ? "BatteryLimit" : "UnitLimit";
            return new BoundaryEvidence { RawText=text.Text, BoundaryType=kind, Axis=coordinate.Success ? coordinate.Groups[1].Value.ToUpperInvariant() : null, CoordinateValue=hasValue ? value : (decimal?)null, ParseStatus=hasValue ? "Parsed" : "Partial", SourceHandle=text.Handle, SourceObjectType=text.ObjectType, SourceLayer=text.Layer, PositionX=text.PositionX, PositionY=text.PositionY, PositionZ=text.PositionZ };
        }
        private static bool IsLayerActive(string? layer, IReadOnlyDictionary<string, bool> visibility)
        {
            if (visibility.Count == 0 || string.IsNullOrWhiteSpace(layer)) return true;
            bool active;
            return !visibility.TryGetValue(layer!, out active) || active;
        }
        private static bool IsBlockDefinitionMetadata(TextSnapshot text) => string.Equals(text.Type, "BLOCKNAME", StringComparison.OrdinalIgnoreCase) || string.Equals(text.ObjectType, "BLOCKNAME", StringComparison.OrdinalIgnoreCase) || string.Equals(text.ObjectType, "BLOCK", StringComparison.OrdinalIgnoreCase);
        private static bool IsRoadLabel(string? raw) => !string.IsNullOrWhiteSpace(raw) && raw!.IndexOf("ROAD", StringComparison.OrdinalIgnoreCase) >= 0;
        private static string NormalizeIdentity(string value) => Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        private static bool TryParseEngineeringNumber(string raw, out decimal value)
        {
            value = 0;
            string text = (raw ?? string.Empty).Trim().Replace(" ", string.Empty);
            if (text.Length == 0) return false;
            int comma = text.LastIndexOf(',');
            int dot = text.LastIndexOf('.');
            if (comma >= 0 && dot >= 0)
            {
                if (comma > dot) text = text.Replace(".", string.Empty).Replace(',', '.');
                else text = text.Replace(",", string.Empty);
            }
            else if (comma >= 0)
            {
                string[] parts = text.Split(',');
                bool thousands = parts.Length > 1 && parts.Skip(1).All(x => x.Length == 3);
                text = thousands ? string.Concat(parts) : string.Join(string.Empty, parts.Take(parts.Length - 1)) + "." + parts[parts.Length - 1];
            }
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
