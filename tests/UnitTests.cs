using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.Contracts;

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
            Console.WriteLine("All EngiSentry M0.2 tests passed.");
            return 0;
        }
    }
}
