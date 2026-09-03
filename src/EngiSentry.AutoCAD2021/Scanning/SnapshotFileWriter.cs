using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021.Scanning
{
    internal static class SnapshotFileWriter
    {
        internal static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "EngiSentry.sln"))) return directory.FullName;
                directory = directory.Parent;
            }
            throw new InvalidOperationException("EngiSentry.sln was not found above the executing assembly.");
        }

        internal static string SanitizeFileName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder(name.Length);
            foreach (char character in name) builder.Append(invalid.Contains(character) ? '_' : character);
            return builder.ToString();
        }

        internal static string Write(DrawingSnapshot snapshot)
        {
            string directory = Path.Combine(FindRepositoryRoot(), "artifacts", "scans");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(snapshot.FileName ?? "unnamed");
            string timestamp = snapshot.ScanTimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".snapshot.json");
            var serializer = new DataContractJsonSerializer(typeof(DrawingSnapshot));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, snapshot);
            }
            return path;
        }

        internal static string WriteSemantic(EngineeringDrawingModel model, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(FindRepositoryRoot(), "artifacts", "semantics");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".semantic.json");
            var serializer = new DataContractJsonSerializer(typeof(EngineeringDrawingModel));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, model);
            return path;
        }

        internal static string WriteEntityIndex(EngineeringEntityIndex index, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(FindRepositoryRoot(), "artifacts", "entities");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".entity-index.json");
            var serializer = new DataContractJsonSerializer(typeof(EngineeringEntityIndex));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, index);
            return path;
        }
    }
}
