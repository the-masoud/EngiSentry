using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.Revision;

namespace EngiSentry.AutoCAD2021.Scanning
{
    internal static class SnapshotFileWriter
    {
        internal static string ResolveArtifactsRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "EngiSentry.sln"))) return Path.Combine(directory.FullName, "artifacts");
                directory = directory.Parent;
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts");
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
            string directory = Path.Combine(ResolveArtifactsRoot(), "scans");
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
            string directory = Path.Combine(ResolveArtifactsRoot(), "semantics");
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
            string directory = Path.Combine(ResolveArtifactsRoot(), "entities");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".entity-index.json");
            var serializer = new DataContractJsonSerializer(typeof(EngineeringEntityIndex));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, index);
            return path;
        }

        internal static string WriteQaReport(QaReport report, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "qa");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".qa.json");
            var serializer = new DataContractJsonSerializer(typeof(QaReport));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, report);
            return path;
        }

        internal static string WritePlotPlanQaReport(PlotPlanQaReport report, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "qa", "plotplan");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".plotplan.qa.json");
            var serializer = new DataContractJsonSerializer(typeof(PlotPlanQaReport));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, PlotPlanReportCompactor.Create(report));
            return path;
        }

        internal static string WriteStandardsReviewPackage(StandardAuthorityReviewPackage package)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "qa", "plotplan");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(package.DrawingName);
            string timestamp = package.ScanTimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".standards.review.json");
            var serializer = new DataContractJsonSerializer(typeof(StandardAuthorityReviewPackage));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, package);
            return path;
        }

        internal static string WriteProfilePlotPlanQaReport(PlotPlanQaReport report, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "qa", "plotplan");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".profile.plotplan.qa.json");
            var serializer = new DataContractJsonSerializer(typeof(PlotPlanQaReport));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, PlotPlanReportCompactor.Create(report));
            return path;
        }

        internal static string WriteCrossDocumentQaReport(CrossDocumentQaReport report, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "qa", "plotplan");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed");
            string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".cross.plotplan.qa.json");
            var serializer = new DataContractJsonSerializer(typeof(CrossDocumentQaReport));
            var compact = PlotPlanReportCompactor.Create(new PlotPlanQaReport { PlotPlan = report.PlotPlan, Qa = report.Qa });
            var output = new CrossDocumentQaReport { PlotPlan = compact.PlotPlan, Qa = compact.Qa, ExternalEquipmentRegister = report.ExternalEquipmentRegister };
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, output);
            return path;
        }

        internal static string WriteRevisionBaseline(RevisionSnapshot snapshot)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "revision"); Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(snapshot.Drawing.FileName ?? "unnamed"); string timestamp = snapshot.Drawing.ScanTimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".revision.baseline.json");
            var serializer = new DataContractJsonSerializer(typeof(RevisionSnapshot)); using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, snapshot); return path;
        }

        internal static string WriteRevisionComparison(RevisionComparisonReport report, string? drawingName, DateTime timestampUtc)
        {
            string directory = Path.Combine(ResolveArtifactsRoot(), "revision"); Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(drawingName ?? "unnamed"); string timestamp = timestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + timestamp + ".revision.compare.json");
            var serializer = new DataContractJsonSerializer(typeof(RevisionComparisonReport)); using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, report); return path;
        }
    }
}
