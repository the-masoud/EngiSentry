using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Resolves authoritative equipment coordinates from the active package for a drawing.
    ///
    /// A coordinate is returned only when the drawing hash matches, the stored package still
    /// recomputes to the hashes recorded in <c>active.json</c>, the manifest schema/unit/basis are
    /// still supported, and the caller supplies BOTH the occurrence identity and the equipment tag.
    /// There is no tag-only lookup, no nearest match, no fuzzy match, and no proximity fallback: if no
    /// active package exists, or anything fails to re-verify, this provider returns nothing and the
    /// existing placement behaviour is left completely unchanged.
    /// </summary>
    public sealed class AuthoritativeEquipmentCoordinateProvider
    {
        private readonly AuthoritativeEquipmentCoordinatePackageStore store;

        public AuthoritativeEquipmentCoordinateProvider() : this(new AuthoritativeEquipmentCoordinatePackageStore()) { }

        public AuthoritativeEquipmentCoordinateProvider(AuthoritativeEquipmentCoordinatePackageStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public AuthoritativeEquipmentCoordinatePackageStore Store => store;

        public AuthoritativeEquipmentCoordinateActivePackage? GetActive(string? drawingSha256) => store.ReadActive(drawingSha256);

        /// <summary>
        /// Every authoritative coordinate for the drawing's active package, or an empty list when no
        /// package is active or the stored package no longer re-verifies.
        /// </summary>
        public IReadOnlyList<AuthoritativeEquipmentCoordinate> ResolveForDrawing(string? drawingSha256)
        {
            if (!AuthoritativeEquipmentCoordinatePackageStore.IsSha256(drawingSha256)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            AuthoritativeEquipmentCoordinateActivePackage? active = store.ReadActive(drawingSha256);
            if (active == null) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(active.DrawingSha256, drawingSha256, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!AuthoritativeEquipmentCoordinatePackageStore.IsSha256(active.PackageFingerprint)) return Array.Empty<AuthoritativeEquipmentCoordinate>();

            string packageDirectory = store.PackageDirectory(drawingSha256!, active.PackageFingerprint);
            string manifestPath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ManifestFileName);
            string schedulePath = Path.Combine(packageDirectory, AuthoritativeEquipmentCoordinateSchema.ScheduleFileName);
            if (!File.Exists(manifestPath) || !File.Exists(schedulePath)) return Array.Empty<AuthoritativeEquipmentCoordinate>();

            byte[] manifestBytes;
            byte[] scheduleBytes;
            try
            {
                manifestBytes = File.ReadAllBytes(manifestPath);
                scheduleBytes = File.ReadAllBytes(schedulePath);
            }
            catch (Exception) { return Array.Empty<AuthoritativeEquipmentCoordinate>(); }

            // Defence in depth: the stored bytes must still hash to exactly what was activated.
            if (!string.Equals(AuthoritativeEquipmentCoordinateHashing.ComputeSha256(manifestBytes), active.ManifestSha256, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(AuthoritativeEquipmentCoordinateHashing.ComputeSha256(scheduleBytes), active.ScheduleSha256, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(AuthoritativeEquipmentCoordinateHashing.ComputePackageFingerprint(manifestBytes, scheduleBytes), active.PackageFingerprint, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();

            AuthoritativeEquipmentCoordinateManifest? manifest = AuthoritativeEquipmentCoordinatePackageSerializer.TryDeserializeManifest(manifestBytes);
            if (manifest == null) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(manifest.Schema, AuthoritativeEquipmentCoordinateSchema.Current, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(manifest.CoordinateUnits, AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();
            if (!string.Equals(manifest.CoordinateBasis, AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs, StringComparison.Ordinal)) return Array.Empty<AuthoritativeEquipmentCoordinate>();

            return AuthoritativeEquipmentCoordinatePackageValidator.ReadScheduleCoordinates(manifestBytes, scheduleBytes, manifest);
        }

        /// <summary>
        /// The authoritative coordinate for one occurrence. Both the occurrence identity and the
        /// equipment tag must match exactly (tag case-normalized); a tag-only match is never returned.
        /// </summary>
        public AuthoritativeEquipmentCoordinate? Resolve(string? drawingSha256, string? occurrenceId, string? equipmentTag)
        {
            if (string.IsNullOrWhiteSpace(occurrenceId) || string.IsNullOrWhiteSpace(equipmentTag)) return null;
            string occurrence = occurrenceId!.Trim();
            string tag = equipmentTag!.Trim().ToUpperInvariant();
            return ResolveForDrawing(drawingSha256)
                .FirstOrDefault(x => string.Equals(x.OccurrenceId, occurrence, StringComparison.Ordinal)
                    && string.Equals(x.EquipmentTag.Trim().ToUpperInvariant(), tag, StringComparison.Ordinal));
        }
    }
}
