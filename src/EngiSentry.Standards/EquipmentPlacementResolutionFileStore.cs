using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Append-only storage for engineer placement resolution records. A record is written under
    /// <c>&lt;root&gt;\&lt;resolutionId&gt;\&lt;recordId&gt;.epr.json</c> with
    /// <see cref="FileMode.CreateNew"/>, so an existing confirmation is never overwritten: a new
    /// confirmation produces a new file and every earlier confirmation remains as history.
    ///
    /// The directory carries the resolution identity and the file name carries only the record
    /// identity, so the full path stays well under the Windows MAX_PATH limit even though both
    /// identities are 64-character hashes. The identities are also stored inside the record and
    /// are re-verified on read, so the on-disk names are a convenience, never the source of truth.
    /// </summary>
    public static class EquipmentPlacementResolutionFileStore
    {
        public const string FileExtension = ".epr.json";

        public static string Write(EquipmentPlacementResolutionRecord record, string directory)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory is required", nameof(directory));
            EquipmentPlacementResolutionValidator.Validate(record);
            string resolutionDirectory = Path.Combine(directory, record.ResolutionId);
            Directory.CreateDirectory(resolutionDirectory);
            string path = RecordPath(resolutionDirectory, record);
            var serializer = new DataContractJsonSerializer(typeof(EquipmentPlacementResolutionRecord));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, record);
            return path;
        }

        public static EquipmentPlacementResolutionRecord Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));
            var serializer = new DataContractJsonSerializer(typeof(EquipmentPlacementResolutionRecord));
            EquipmentPlacementResolutionRecord record;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) record = (EquipmentPlacementResolutionRecord)serializer.ReadObject(stream)!;
            EquipmentPlacementResolutionValidator.Validate(record);
            return record;
        }

        /// <summary>Enumerates every persisted record path under the root, deterministically ordered.</summary>
        public static IReadOnlyList<string> EnumerateRecordPaths(string root)
        {
            var paths = new List<string>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return paths;
            foreach (string file in Directory.GetFiles(root, "*" + FileExtension, SearchOption.AllDirectories))
            {
                if (file.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                paths.Add(file);
            }
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        public static string RecordPath(string resolutionDirectory, EquipmentPlacementResolutionRecord record) =>
            Path.Combine(resolutionDirectory, record.RecordId + FileExtension);
    }
}
