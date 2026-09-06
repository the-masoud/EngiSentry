using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.Revision
{
    public sealed class RevisionValidationException : Exception
    {
        public RevisionValidationException(string message) : base(message) { }
    }

    public sealed class RevisionLineageAssessment
    {
        public string Confidence { get; set; } = "INSUFFICIENT_EVIDENCE";
        public int CommonIdentityCount { get; set; }
        public int UnionIdentityCount { get; set; }
        public decimal IdentityOverlapPercent { get; set; }
    }

    public static class RevisionLineageAssessor
    {
        public static RevisionLineageAssessment Assess(RevisionSnapshot baseline, RevisionSnapshot current)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (current == null) throw new ArgumentNullException(nameof(current));
            var baselineIds = new HashSet<string>((baseline.Equipment ?? new List<RevisionEquipmentRecord>()).Select(x => x.EquipmentCanonicalId).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
            var currentIds = new HashSet<string>((current.Equipment ?? new List<RevisionEquipmentRecord>()).Select(x => x.EquipmentCanonicalId).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
            int common = baselineIds.Intersect(currentIds).Count();
            int union = baselineIds.Union(currentIds).Count();
            decimal overlap = union == 0 ? 0m : decimal.Round(common * 100m / union, 1, MidpointRounding.AwayFromZero);
            string confidence = union == 0 ? "INSUFFICIENT_EVIDENCE" : overlap >= 80m ? "HIGH" : overlap >= 50m ? "MEDIUM" : "LOW";
            return new RevisionLineageAssessment { Confidence = confidence, CommonIdentityCount = common, UnionIdentityCount = union, IdentityOverlapPercent = overlap };
        }
    }

    public static class RevisionSnapshotBuilder
    {
        public static RevisionSnapshot Build(DrawingSnapshot snapshot, EngineeringEntityIndex index, PlotPlanModel plotPlan)
        {
            if (snapshot == null || index == null || plotPlan == null) throw new ArgumentNullException("revision inputs");
            var result = new RevisionSnapshot { Drawing = new RevisionDrawing { FileName = snapshot.FileName, FullPath = snapshot.FullPath, DwgVersion = snapshot.DwgVersion, InsertionUnits = snapshot.InsertionUnits, CurrentLayout = snapshot.CurrentLayout, ScanTimestampUtc = snapshot.ScanTimestampUtc } };
            foreach (EquipmentEntity entity in index.EquipmentEntities.Where(x => x.HasDrawingEvidence).OrderBy(x => x.NormalizedTag, StringComparer.OrdinalIgnoreCase))
            {
                string tag = (entity.NormalizedTag ?? string.Empty).Trim().ToUpperInvariant();
                if (result.Equipment.Any(x => x.EquipmentCanonicalId == "equipment:" + tag)) throw new RevisionValidationException("duplicate normalized revision identity: " + tag);
                var record = new RevisionEquipmentRecord { EquipmentCanonicalId = "equipment:" + tag, NormalizedTag = tag };
                foreach (EquipmentPlacement placement in plotPlan.EquipmentPlacements.Where(x => x.EquipmentEntityId == entity.CanonicalId && x.PlacementStatus == "Complete" && x.Easting.Status == "Resolved" && x.Northing.Status == "Resolved" && x.Easting.SelectedValue.HasValue && x.Northing.SelectedValue.HasValue))
                    record.CompleteCoordinatePairs.Add(new RevisionCoordinatePair { Easting = placement.Easting.SelectedValue!.Value, Northing = placement.Northing.SelectedValue!.Value });
                record.CompleteCoordinatePairs = record.CompleteCoordinatePairs.GroupBy(x => x.Easting.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + x.Northing.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.Easting).ThenBy(x => x.Northing).ToList();
                record.DescriptionValues = Values(entity.EquipmentListLinks.Select(x => x.Description));
                record.InstallationValues = Values(entity.EquipmentListLinks.Select(x => x.Installation));
                result.Equipment.Add(record);
            }
            result.Equipment = result.Equipment.OrderBy(x => x.EquipmentCanonicalId, StringComparer.Ordinal).ToList();
            result.Statistics.EquipmentCount = result.Equipment.Count;
            return result;
        }
        private static List<string> Values(IEnumerable<string?> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderBy(v => v, StringComparer.Ordinal).First()).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    public static class RevisionSnapshotFile
    {
        public static RevisionSnapshot Read(string path, out string sha256)
        {
            byte[] bytes; try { bytes = File.ReadAllBytes(path); } catch (Exception ex) { throw new RevisionValidationException("baseline is unreadable: " + ex.Message); }
            sha256 = BitConverter.ToString(SHA256.Create().ComputeHash(bytes)).Replace("-", string.Empty);
            try { using (var stream = new MemoryStream(bytes)) { var value = new DataContractJsonSerializer(typeof(RevisionSnapshot)).ReadObject(stream) as RevisionSnapshot; if (value == null) throw new RevisionValidationException("baseline JSON is invalid"); Validate(value); return value; } }
            catch (RevisionValidationException) { throw; } catch (Exception ex) { throw new RevisionValidationException("baseline JSON is invalid: " + ex.Message); }
        }
        public static void Validate(RevisionSnapshot value)
        {
            if (value == null || value.SchemaVersion != "1.0") throw new RevisionValidationException("baseline SchemaVersion must be 1.0");
            if (value.Drawing == null) throw new RevisionValidationException("baseline Drawing is missing");
            if (value.Equipment == null) throw new RevisionValidationException("baseline Equipment is missing");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RevisionEquipmentRecord record in value.Equipment)
            {
                if (record == null || string.IsNullOrEmpty(record.EquipmentCanonicalId) || string.IsNullOrEmpty(record.NormalizedTag)) throw new RevisionValidationException("baseline equipment identity is empty");
                if (record.NormalizedTag != record.NormalizedTag.ToUpperInvariant() || record.EquipmentCanonicalId != "equipment:" + record.NormalizedTag) throw new RevisionValidationException("baseline equipment identity is not canonical: " + record.EquipmentCanonicalId);
                if (!ids.Add(record.EquipmentCanonicalId)) throw new RevisionValidationException("duplicate baseline equipment identity: " + record.EquipmentCanonicalId);
                if (record.CompleteCoordinatePairs == null || record.DescriptionValues == null || record.InstallationValues == null) throw new RevisionValidationException("baseline evidence collection is null: " + record.EquipmentCanonicalId);
                var pairs = new HashSet<string>(StringComparer.Ordinal); foreach (RevisionCoordinatePair pair in record.CompleteCoordinatePairs) if (!pairs.Add(pair.Easting.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + pair.Northing.ToString(System.Globalization.CultureInfo.InvariantCulture))) throw new RevisionValidationException("duplicate baseline coordinate pair: " + record.EquipmentCanonicalId);
            }
        }
    }

    public static class RevisionComparer
    {
        public static RevisionComparisonReport Compare(RevisionSnapshot baseline, RevisionSnapshot current, string fileName, string fullPath, string sha256)
        {
            RevisionSnapshotFile.Validate(baseline); RevisionSnapshotFile.Validate(current);
            var b = baseline.Equipment.ToDictionary(x => x.EquipmentCanonicalId, StringComparer.Ordinal); var c = current.Equipment.ToDictionary(x => x.EquipmentCanonicalId, StringComparer.Ordinal); var changes = new List<RevisionChangeRecord>(); var changedIds = new HashSet<string>(StringComparer.Ordinal); int coordinate = 0, attributes = 0, unchanged = 0;
            foreach (string id in b.Keys.Union(c.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                RevisionEquipmentRecord br; RevisionEquipmentRecord cr;
                if (!b.TryGetValue(id, out br!)) { cr = c[id]; changes.Add(new RevisionChangeRecord { ChangeId = "revision:EquipmentAdded:" + id, ChangeKind = "EquipmentAdded", EquipmentCanonicalId = id, NormalizedTag = cr.NormalizedTag, Current = cr }); changedIds.Add(id); continue; }
                if (!c.TryGetValue(id, out cr!)) { changes.Add(new RevisionChangeRecord { ChangeId = "revision:EquipmentRemoved:" + id, ChangeKind = "EquipmentRemoved", EquipmentCanonicalId = id, NormalizedTag = br.NormalizedTag, Baseline = br }); changedIds.Add(id); continue; }
                if (!PairsEqual(br.CompleteCoordinatePairs, cr.CompleteCoordinatePairs)) { coordinate++; changedIds.Add(id); changes.Add(new RevisionChangeRecord { ChangeId = "revision:EquipmentCoordinateEvidenceChanged:" + id, ChangeKind = "EquipmentCoordinateEvidenceChanged", EquipmentCanonicalId = id, NormalizedTag = cr.NormalizedTag, Baseline = br, Current = cr }); }
                var fields = new List<string>(); if (!ValuesEqual(br.DescriptionValues, cr.DescriptionValues)) fields.Add("Description"); if (!ValuesEqual(br.InstallationValues, cr.InstallationValues)) fields.Add("Installation");
                if (fields.Count > 0) { attributes++; changedIds.Add(id); changes.Add(new RevisionChangeRecord { ChangeId = "revision:EquipmentAttributeEvidenceChanged:" + id, ChangeKind = "EquipmentAttributeEvidenceChanged", EquipmentCanonicalId = id, NormalizedTag = cr.NormalizedTag, ChangedFields = fields.OrderBy(x => x, StringComparer.Ordinal).ToList(), Baseline = br, Current = cr }); }
                if (!changedIds.Contains(id)) unchanged++;
            }
            changes = changes.OrderBy(x => x.EquipmentCanonicalId, StringComparer.Ordinal).ThenBy(x => x.ChangeKind, StringComparer.Ordinal).ToList();
            return new RevisionComparisonReport { BaselineSourceFileName = fileName ?? string.Empty, BaselineSourceFullPath = fullPath ?? string.Empty, BaselineSourceSha256 = sha256 ?? string.Empty, Baseline = baseline, Current = current, Changes = changes, Statistics = new RevisionComparisonStatistics { BaselineEquipmentCount = baseline.Equipment.Count, CurrentEquipmentCount = current.Equipment.Count, AddedEquipmentCount = changes.Count(x => x.ChangeKind == "EquipmentAdded"), RemovedEquipmentCount = changes.Count(x => x.ChangeKind == "EquipmentRemoved"), CoordinateChangedEquipmentCount = coordinate, AttributeChangedEquipmentCount = attributes, ChangedEquipmentCount = changedIds.Count, UnchangedEquipmentCount = unchanged, TotalChangeCount = changes.Count } };
        }
        private static bool PairsEqual(IEnumerable<RevisionCoordinatePair> a, IEnumerable<RevisionCoordinatePair> b) => a.Count() == b.Count() && a.OrderBy(x => x.Easting).ThenBy(x => x.Northing).Zip(b.OrderBy(x => x.Easting).ThenBy(x => x.Northing), (x, y) => x.Easting == y.Easting && x.Northing == y.Northing).All(x => x);
        private static bool ValuesEqual(IEnumerable<string> a, IEnumerable<string> b) => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
    }
}
