using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Semantics;

namespace EngiSentry.Tests
{
    internal static class UnitTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (char c in value) builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return builder.ToString();
        }

        public static int Main()
        {
            var snapshot = new DrawingSnapshot { FileName = "drawing.dwg", ScanTimestampUtc = new DateTime(2026, 9, 3, 12, 34, 56, 123, DateTimeKind.Utc) };
            Assert(snapshot.Layers != null && snapshot.BlockReferences != null, "Snapshot collections were not initialized.");
            Assert(Sanitize("A:B< C>") == "A_B_ C_", "Filename sanitization failed.");
            string filename = Sanitize(snapshot.FileName ?? "unnamed") + ".20260903T123456123Z.snapshot.json";
            Assert(filename == "drawing.dwg.20260903T123456123Z.snapshot.json", "Snapshot filename generation failed.");
            var serializer = new DataContractJsonSerializer(typeof(DrawingSnapshot));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                string json = Encoding.UTF8.GetString(stream.ToArray());
                Assert(json.StartsWith("{", StringComparison.Ordinal), "Serialization did not produce JSON.");
                using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json))) Assert(serializer.ReadObject(input) is DrawingSnapshot, "Serialized JSON could not be read.");
            }
            string existing = Path.GetTempFileName();
            try
            {
                ExternalDependencySnapshot found = DependencyClassifier.Classify("Font", "existing", existing);
                Assert(found.Status == "Found" && found.ExistsOnDisk == true && found.ResolvedPath == existing, "Resolved existing dependency classification failed.");
                string missing = Path.Combine(Path.GetDirectoryName(existing)!, "engi-sentry-missing-font.shx");
                ExternalDependencySnapshot absent = DependencyClassifier.Classify("Font", "missing", missing);
                Assert(absent.Status == "Missing" && absent.ExistsOnDisk == false && absent.ResolvedPath == missing, "Resolved missing dependency classification failed.");
                ExternalDependencySnapshot unresolved = DependencyClassifier.Classify("Font", "romans.shx", "romans.shx");
                Assert(unresolved.Status == "Unknown" && unresolved.ExistsOnDisk == null && unresolved.ResolvedPath == null, "Unresolved dependency classification failed.");
                ExternalDependencySnapshot absoluteMissing = DependencyClassifier.Classify("Font", "absolute-missing", missing);
                Assert(absoluteMissing.Status == "Missing" && absoluteMissing.ExistsOnDisk == false, "Absolute missing dependency classification failed.");
            }
            finally { File.Delete(existing); }
            var semanticSnapshot = new DrawingSnapshot();
            string[] semanticTexts = { "P-200-101A", "P-200-101A/B", "E-200-104", "TK-200-101", "PK-200-103", "1200-ST-101", "1200-PL-101", "1200-DK-101", "E. 1438500", "E 1500000", "N. 1492000", "N 1503500", "T.O.P. EL. +10.030", "T.O.S. EL+18.000", "T.O.G. EL+6.300", "H.P.P. EL+6.100", "ROAD C.L.", "UNIT LIMIT", "UNIT LIMIT N. 1444000", "UNIT LIMIT E. 1416000", "1", "16", "A0", "1:200", "00", "2026", "XXXX" };
            for (int i = 0; i < semanticTexts.Length; i++) semanticSnapshot.TextEntities.Add(new TextSnapshot { Text = semanticTexts[i], Handle = (i + 1).ToString("X"), ObjectType = "TEXT", Layer = "NOTES", PositionX = i, PositionY = i });
            var semantic = SemanticExtractor.Extract(semanticSnapshot);
            Assert(semantic.EquipmentTags.Count == 5, "Equipment tag parsing failed.");
            Assert(semantic.EquipmentTags.Any(x => x.RawTag == "P-200-101A/B" && x.NormalizedTag == "P-200-101A/B" && x.EquipmentPrefix == "P" && x.UnitNumber == "200" && x.SequenceNumber == "101" && x.Suffix == "A/B"), "Grouped equipment tag parsing failed.");
            Assert(semantic.StructureTags.Count == 3 && semantic.StructureTags.Any(x => x.TypeCode == "DK"), "Structure tag parsing failed.");
            Assert(semantic.Coordinates.Count == 4 && semantic.Coordinates.Any(x => x.Axis == "E" && x.Value == 1438500m), "Coordinate parsing failed.");
            Assert(semantic.Elevations.Count == 4 && semantic.Elevations.Any(x => x.ElevationType == "HPP" && x.Value == 6.100m), "Elevation parsing failed.");
            Assert(semantic.RoadLabels.Count == 1, "Road label parsing failed.");
            Assert(semantic.BoundaryLabels.Count == 3 && semantic.BoundaryLabels.Count(x => x.ParseStatus == "Partial") == 1, "Boundary parsing failed.");
            var duplicate = new DrawingSnapshot(); duplicate.TextEntities.Add(new TextSnapshot { Text = "P-200-101A", Handle = "AA", ObjectType = "TEXT" }); duplicate.TextEntities.Add(new TextSnapshot { Text = "P-200-101A", Handle = "BB", ObjectType = "TEXT" });
            var duplicateModel = SemanticExtractor.Extract(duplicate); Assert(duplicateModel.EquipmentTags.Count == 2 && duplicateModel.Statistics.UniqueEquipmentTags == 1, "Duplicate evidence handling failed.");
            var listSnapshot = new DrawingSnapshot();
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="NAME", Layer="EQ-LIST", Handle="1", PositionX=0, PositionY=100 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="DESCRIPTION", Layer="EQ-LIST", Handle="2", PositionX=100, PositionY=100 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="INSTALLATION", Layer="EQ-LIST", Handle="3", PositionX=200, PositionY=100 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="P-200-101A/B", Layer="EQ-LIST", Handle="4", PositionX=0, PositionY=90 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="Pump", Layer="EQ-LIST", Handle="5", PositionX=100, PositionY=90 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="EL+10", Layer="EQ-LIST", Handle="6", PositionX=200, PositionY=90 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="E-200-104", Layer="EQ-LIST", Handle="7", PositionX=0, PositionY=80 });
            listSnapshot.TextEntities.Add(new TextSnapshot { Text="ambiguous", Layer="EQ-LIST", Handle="8", PositionX=150, PositionY=80 });
            var listModel = SemanticExtractor.Extract(listSnapshot); Assert(listModel.EquipmentListEntries.Count == 2, "Equipment list row grouping failed."); Assert(listModel.EquipmentListEntries.Any(x => x.Tag == "P-200-101A/B" && x.Description == "Pump" && x.Installation == "EL+10"), "Equipment list column assignment failed."); Assert(listModel.EquipmentListEntries.Any(x => x.Tag == "E-200-104" && x.ParseStatus == "Partial"), "Equipment list ambiguity handling failed.");
            using (var semanticStream = new MemoryStream()) { var semanticSerializer = new DataContractJsonSerializer(typeof(EngineeringDrawingModel)); semanticSerializer.WriteObject(semanticStream, semantic); Assert(Encoding.UTF8.GetString(semanticStream.ToArray()).StartsWith("{", StringComparison.Ordinal), "Semantic model serialization failed."); }
            Console.WriteLine("All EngiSentry M0.3 tests passed.");
            return 0;
        }
    }
}
