using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Content-addressed installation store for authoritative equipment coordinate packages.
    ///
    /// Layout:
    /// <code>
    /// &lt;root&gt;\&lt;DrawingSha256&gt;\&lt;PackageFingerprint&gt;\equipment-coordinate-manifest.json
    /// &lt;root&gt;\&lt;DrawingSha256&gt;\&lt;PackageFingerprint&gt;\equipment-coordinate-schedule.csv
    /// &lt;root&gt;\&lt;DrawingSha256&gt;\&lt;PackageFingerprint&gt;\import-receipt.json
    /// &lt;root&gt;\&lt;DrawingSha256&gt;\active.json
    /// </code>
    ///
    /// The manifest and schedule are stored as the exact source bytes that were validated — they are
    /// never rewritten or reformatted. A package directory is immutable: its name is the hash of its
    /// contents, so an existing package is never overwritten. Installing a new package writes a new
    /// directory and repoints <c>active.json</c>; every earlier package and receipt remains as history
    /// and is never deleted.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinatePackageStore
    {
        public const string RootOverrideVariable = "ENGISENTRY_EQUIPMENT_COORDINATE_ROOT";

        public static string DefaultRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EngiSentry", "authoritative-equipment-coordinates");

        public static string? ResolveRootOverride()
        {
            string? value = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string ResolveRoot() => ResolveRootOverride() ?? DefaultRoot;

        private readonly string root;

        public AuthoritativeEquipmentCoordinatePackageStore() : this(ResolveRoot()) { }

        public AuthoritativeEquipmentCoordinatePackageStore(string root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public string Root => root;

        public string DrawingDirectory(string drawingSha256) => Path.Combine(root, RequireSha(drawingSha256));
        public string PackageDirectory(string drawingSha256, string fingerprint) => Path.Combine(DrawingDirectory(drawingSha256), RequireSha(fingerprint));
        public string ActivePath(string drawingSha256) => Path.Combine(DrawingDirectory(drawingSha256), AuthoritativeEquipmentCoordinateSchema.ActiveFileName);

        /// <summary>The active package descriptor for a drawing, or null when none is active.</summary>
        public AuthoritativeEquipmentCoordinateActivePackage? ReadActive(string? drawingSha256)
        {
            if (!IsSha256(drawingSha256)) return null;
            string path = ActivePath(drawingSha256!);
            if (!File.Exists(path)) return null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AuthoritativeEquipmentCoordinateActivePackage));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return serializer.ReadObject(stream) as AuthoritativeEquipmentCoordinateActivePackage;
            }
            catch (Exception) { return null; }
        }

        public AuthoritativeEquipmentCoordinateImportReceipt? ReadReceipt(string? drawingSha256, string? fingerprint)
        {
            if (!IsSha256(drawingSha256) || !IsSha256(fingerprint)) return null;
            string path = Path.Combine(PackageDirectory(drawingSha256!, fingerprint!), AuthoritativeEquipmentCoordinateSchema.ReceiptFileName);
            if (!File.Exists(path)) return null;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AuthoritativeEquipmentCoordinateImportReceipt));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    return serializer.ReadObject(stream) as AuthoritativeEquipmentCoordinateImportReceipt;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Installs a fully validated package and activates it. The caller must have validated the
        /// package first; this method only performs the mechanical write. Source bytes are preserved
        /// verbatim. <paramref name="previousFingerprint"/> is recorded on the receipt when a different
        /// package was previously active, so the superseded package stays traceable.
        /// </summary>
        public AuthoritativeEquipmentCoordinateImportReceipt Install(
            string drawingSha256,
            string sourcePackageDirectory,
            AuthoritativeEquipmentCoordinateValidationResult validation,
            string? previousFingerprint)
        {
            if (!IsSha256(drawingSha256)) throw new ArgumentException("drawingSha256 must be a 64-character hexadecimal SHA256", nameof(drawingSha256));
            if (string.IsNullOrWhiteSpace(sourcePackageDirectory)) throw new ArgumentException("sourcePackageDirectory is required", nameof(sourcePackageDirectory));
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            if (!validation.IsValid) throw new InvalidOperationException("an invalid package can never be installed");

            // The exact source bytes are read back from the validated package and copied verbatim.
            byte[] manifestBytes = File.ReadAllBytes(Path.Combine(sourcePackageDirectory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName));
            byte[] scheduleBytes = File.ReadAllBytes(Path.Combine(sourcePackageDirectory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName));
            if (!string.Equals(validation.PackageFingerprint, AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifestBytes, scheduleBytes), StringComparison.Ordinal))
                throw new InvalidOperationException("package fingerprint does not match the source bytes");

            string packageDirectory = PackageDirectory(drawingSha256, validation.PackageFingerprint);
            Directory.CreateDirectory(packageDirectory);
            WriteIfAbsent(Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName), manifestBytes);
            WriteIfAbsent(Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName), scheduleBytes);

            var receipt = new AuthoritativeEquipmentCoordinateImportReceipt
            {
                Schema = AuthoritativeEquipmentCoordinateSchema.Current,
                Status = AuthoritativeEquipmentCoordinateInstallationStatuses.Installed,
                DrawingName = validation.DrawingName ?? string.Empty,
                DrawingSha256 = drawingSha256,
                ManifestSha256 = validation.ManifestSha256,
                ScheduleSha256 = validation.ScheduleSha256,
                PackageFingerprint = validation.PackageFingerprint,
                ImportedTimestampUtc = UtcTimestamp(),
                IssuedBy = validation.IssuedBy ?? string.Empty,
                SourceReference = validation.SourceReference ?? string.Empty,
                OccurrenceCount = validation.ResolvedOccurrenceCount,
                PackageDirectory = packageDirectory,
                PreviousPackageFingerprint = string.Equals(previousFingerprint, validation.PackageFingerprint, StringComparison.Ordinal) ? null : previousFingerprint
            };
            WriteIfAbsent(Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ReceiptFileName), Serialize(receipt, typeof(AuthoritativeEquipmentCoordinateImportReceipt)));

            var active = new AuthoritativeEquipmentCoordinateActivePackage
            {
                Schema = AuthoritativeEquipmentCoordinateSchema.Current,
                DrawingSha256 = drawingSha256,
                PackageFingerprint = validation.PackageFingerprint,
                ManifestSha256 = validation.ManifestSha256,
                ScheduleSha256 = validation.ScheduleSha256,
                PackageDirectory = packageDirectory,
                ActivatedTimestampUtc = UtcTimestamp(),
                OccurrenceCount = validation.ResolvedOccurrenceCount,
                IssuedBy = validation.IssuedBy ?? string.Empty
            };
            WriteAtomic(ActivePath(drawingSha256), Serialize(active, typeof(AuthoritativeEquipmentCoordinateActivePackage)));

            return receipt;
        }

        /// <summary>True when the exact package content is already installed and active for the drawing.</summary>
        public bool IsActive(string? drawingSha256, string? fingerprint)
        {
            AuthoritativeEquipmentCoordinateActivePackage? active = ReadActive(drawingSha256);
            return active != null && IsSha256(fingerprint) && string.Equals(active.PackageFingerprint, fingerprint, StringComparison.Ordinal);
        }

        private static void WriteIfAbsent(string path, byte[] bytes)
        {
            if (File.Exists(path)) return; // content-addressed: identical by construction
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
            }
            catch (IOException)
            {
                // A concurrent writer created the same content-addressed file first: nothing to do.
                if (!File.Exists(path)) throw;
            }
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) stream.Write(bytes, 0, bytes.Length);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static byte[] Serialize(object value, Type type)
        {
            var serializer = new DataContractJsonSerializer(type);
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        internal static string UtcTimestamp() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        internal static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        private static string RequireSha(string value)
        {
            if (!IsSha256(value)) throw new ArgumentException("expected a 64-character hexadecimal SHA256: " + value);
            return value;
        }
    }
}
