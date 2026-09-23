using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace EngiSentry.Intelligence
{
    public static class JevEngineerLabelingStatuses
    {
        public const string NotStarted = "NOT_STARTED";
        public const string InProgress = "IN_PROGRESS";
        public const string Complete = "COMPLETE";
        public const string LockedBenchmarked = "LOCKED_BENCHMARKED";
        public const string InvalidLabelFile = "INVALID_LABEL_FILE";
    }

    public sealed class JevEngineerLabelingPackage
    {
        public string DirectoryPath { get; internal set; } = string.Empty;
        public JevCalibrationPackage Package { get; internal set; } = new JevCalibrationPackage();
        public List<JevEngineerLabel> Labels { get; internal set; } = new List<JevEngineerLabel>();
        public string Status { get; internal set; } = JevEngineerLabelingStatuses.NotStarted;
        public string? ValidationError { get; internal set; }
        public int LabeledCount => Labels.Count(x => JevSemanticLabels.All.Contains(x.EngineerLabel));
        public int UnlabeledCount => Math.Max(0, Package.Cases.Count - LabeledCount);
        public bool IsReadOnly => Status == JevEngineerLabelingStatuses.LockedBenchmarked || Status == JevEngineerLabelingStatuses.InvalidLabelFile;
        public bool BenchmarkReportPresent => File.Exists(Path.Combine(DirectoryPath, "benchmark-report.json"));
        public override string ToString() => Package.DrawingName + " | " + Package.CalibrationPackageId + " | " + Package.SchemaVersion + " | " + Package.Cases.Count + " cases | " + Status;
    }

    public sealed class JevEngineerLabelingService
    {
        private readonly string root;
        public JevEngineerLabelingService() : this(JevCalibrationPackageStore.Root) { }
        public JevEngineerLabelingService(string rootOverride) { root = rootOverride ?? throw new ArgumentNullException(nameof(rootOverride)); }

        public List<JevEngineerLabelingPackage> Discover()
        {
            if (!Directory.Exists(root)) return new List<JevEngineerLabelingPackage>();
            var result = new List<JevEngineerLabelingPackage>();
            foreach (string directory in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
            {
                string casesPath = Path.Combine(directory, "calibration-cases.json");
                string templatePath = Path.Combine(directory, "engineer-labels.template.json");
                if (!File.Exists(casesPath) || !File.Exists(templatePath)) continue;
                try { result.Add(Load(directory, casesPath, templatePath)); }
                catch (Exception exception) { System.Diagnostics.Debug.WriteLine("Ignoring invalid calibration package " + directory + ": " + exception.Message); }
            }
            return result.OrderBy(x => x.Package.DrawingName ?? string.Empty, StringComparer.Ordinal).ThenBy(x => x.Package.CalibrationPackageId, StringComparer.Ordinal).ToList();
        }

        public void Save(JevEngineerLabelingPackage package)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (package.IsReadOnly || File.Exists(Path.Combine(package.DirectoryPath, "benchmark-report.json"))) throw new InvalidOperationException(JevEngineerLabelingStatuses.LockedBenchmarked);
            ValidateLabels(package.Package, package.Labels, true);
            JevEngineerLabels document = new JevEngineerLabels { SchemaVersion = package.Package.SchemaVersion, CalibrationPackageId = package.Package.CalibrationPackageId, Labels = package.Package.Cases.Select(c => Copy(package.Labels.Single(x => x.EvidenceId == c.EvidenceId))).ToList() };
            string target = Path.Combine(package.DirectoryPath, "engineer-labels.json");
            string temporary = Path.Combine(package.DirectoryPath, "engineer-labels.json.tmp");
            if (File.Exists(temporary)) throw new InvalidOperationException("A previous label save temporary file exists.");
            try
            {
                Write(temporary, document);
                JevEngineerLabels verified = Read<JevEngineerLabels>(temporary);
                ValidateDocument(package.Package, verified, true);
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                package.Labels = verified.Labels.Select(Copy).ToList();
                package.Status = Status(package.Package.Cases.Count, package.LabeledCount, false);
                package.ValidationError = null;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static JevEngineerLabelingPackage Load(string directory, string casesPath, string templatePath)
        {
            JevCalibrationPackage package = Read<JevCalibrationPackage>(casesPath);
            JevEngineerLabels template = Read<JevEngineerLabels>(templatePath);
            ValidateDocument(package, template, true);
            List<JevEngineerLabel> labels = package.Cases.Select(c => new JevEngineerLabel { EvidenceId = c.EvidenceId, InputProjectionSha256 = c.InputProjectionSha256 }).ToList();
            string labelsPath = Path.Combine(directory, "engineer-labels.json");
            string? error = null;
            if (File.Exists(labelsPath))
            {
                try
                {
                    JevEngineerLabels existing = Read<JevEngineerLabels>(labelsPath);
                    ValidateDocument(package, existing, false);
                    Dictionary<string, JevEngineerLabel> byId = existing.Labels.ToDictionary(x => x.EvidenceId, StringComparer.Ordinal);
                    labels = package.Cases.Select(c => byId.TryGetValue(c.EvidenceId, out JevEngineerLabel? value) ? Copy(value) : new JevEngineerLabel { EvidenceId = c.EvidenceId, InputProjectionSha256 = c.InputProjectionSha256 }).ToList();
                }
                catch (Exception exception) { error = exception.Message; }
            }
            bool benchmarked = File.Exists(Path.Combine(directory, "benchmark-report.json"));
            int labeled = labels.Count(x => JevSemanticLabels.All.Contains(x.EngineerLabel));
            return new JevEngineerLabelingPackage { DirectoryPath = directory, Package = package, Labels = labels, Status = benchmarked ? JevEngineerLabelingStatuses.LockedBenchmarked : error != null ? JevEngineerLabelingStatuses.InvalidLabelFile : Status(package.Cases.Count, labeled, false), ValidationError = error };
        }

        private static string Status(int total, int labeled, bool benchmarked)
        {
            if (benchmarked) return JevEngineerLabelingStatuses.LockedBenchmarked;
            if (labeled == 0) return JevEngineerLabelingStatuses.NotStarted;
            return labeled == total ? JevEngineerLabelingStatuses.Complete : JevEngineerLabelingStatuses.InProgress;
        }

        private static void ValidateDocument(JevCalibrationPackage package, JevEngineerLabels labels, bool requireAllCases)
        {
            if (labels.SchemaVersion != package.SchemaVersion || labels.CalibrationPackageId != package.CalibrationPackageId) throw new InvalidOperationException("Label file package identity mismatch.");
            ValidateLabels(package, labels.Labels ?? new List<JevEngineerLabel>(), requireAllCases);
        }

        private static void ValidateLabels(JevCalibrationPackage package, IReadOnlyList<JevEngineerLabel> labels, bool requireAllCases)
        {
            if (labels.GroupBy(x => x.EvidenceId, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new InvalidOperationException("Duplicate EvidenceId in label file.");
            Dictionary<string, JevCalibrationCase> cases = package.Cases.ToDictionary(x => x.EvidenceId, StringComparer.Ordinal);
            foreach (JevEngineerLabel label in labels)
            {
                if (!cases.TryGetValue(label.EvidenceId, out JevCalibrationCase? item)) throw new InvalidOperationException("Label EvidenceId does not exist in calibration cases.");
                if (label.InputProjectionSha256 != item.InputProjectionSha256) throw new InvalidOperationException("Label projection hash mismatch.");
                if (!string.IsNullOrWhiteSpace(label.EngineerLabel) && !JevSemanticLabels.All.Contains(label.EngineerLabel)) throw new InvalidOperationException("Invalid EngineerLabel.");
                if ((label.Comment?.Length ?? 0) > 500) throw new InvalidOperationException("Comment exceeds 500 characters.");
            }
            if (requireAllCases && labels.Count != package.Cases.Count) throw new InvalidOperationException("Label row count does not match calibration cases.");
        }

        private static JevEngineerLabel Copy(JevEngineerLabel value) => new JevEngineerLabel { EvidenceId = value.EvidenceId, InputProjectionSha256 = value.InputProjectionSha256, EngineerLabel = string.IsNullOrWhiteSpace(value.EngineerLabel) ? null : value.EngineerLabel, Comment = string.IsNullOrEmpty(value.Comment) ? null : value.Comment };
        private static T Read<T>(string path) { using (FileStream stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream)!; }
        private static void Write<T>(string path, T value) { using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); }
    }
}
