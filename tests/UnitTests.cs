using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.AutoCAD2021.Scanning;

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
            Console.WriteLine("All EngiSentry M0.2 tests passed.");
            return 0;
        }
    }
}
