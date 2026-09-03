using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Core
{
    public static class EntityIndexer
    {
        public static EngineeringEntityIndex Build(EngineeringDrawingModel model)
        {
            var index = new EngineeringEntityIndex { Drawing = model.Drawing };
            var entities = new Dictionary<string, EquipmentEntity>(StringComparer.Ordinal);
            var listEvidence = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentListEntry entry in model.EquipmentListEntries)
                foreach (string handle in entry.SourceHandles.Where(x => !string.IsNullOrEmpty(x))) listEvidence.Add(EvidenceKey(handle, entry.Tag));

            foreach (EquipmentTag occurrence in model.EquipmentTags)
            {
                if (string.IsNullOrEmpty(occurrence.NormalizedTag) || IsGrouped(occurrence)) continue;
                EquipmentEntity entity = GetOrCreate(entities, occurrence);
                entity.Occurrences.Add(ToOccurrence(occurrence, listEvidence.Contains(EvidenceKey(occurrence.SourceHandle, occurrence.NormalizedTag)) ? "EquipmentListTag" : "DrawingTag"));
            }

            foreach (EquipmentListEntry entry in model.EquipmentListEntries)
            {
                EquipmentTag[] sources = model.EquipmentTags.Where(x => x.SourceHandlesEqual(entry.SourceHandles) && string.Equals(x.NormalizedTag, entry.Tag, StringComparison.Ordinal)).ToArray();
                bool grouped = sources.Length > 0 && sources.Any(IsGrouped);
                if (sources.Length == 0)
                {
                    index.UnresolvedLinks.Add(new UnresolvedLink { SourceType = "EquipmentListEntry", SourceValue = entry.Tag ?? string.Empty, ExpectedTargetIdentity = Canonical("equipment", entry.Tag), Reason = "EquipmentListTagSourceNotFound", SourceHandles = Sorted(entry.SourceHandles) });
                    continue;
                }
                if (sources.Length > 1)
                {
                    index.AmbiguousLinks.Add(new AmbiguousLink { SourceType = "EquipmentListEntry", SourceValue = entry.Tag ?? string.Empty, CandidateEntityIds = sources.Select(x => Canonical("equipment", x.NormalizedTag)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(), Reason = "MultipleEquipmentListTagSources", SourceHandles = Sorted(entry.SourceHandles) });
                    continue;
                }
                EquipmentTag source = sources[0];
                if (grouped)
                {
                    GroupedTagLink groupedLink = BuildGroupedLink(index, entities, entry, source);
                    index.GroupedTagLinks.Add(groupedLink);
                }
                else
                {
                    EquipmentEntity entity = GetOrCreate(entities, source);
                    entity.EquipmentListLinks.Add(ToLink(entry, "ExactNormalizedTag"));
                }
            }

            index.EquipmentEntities = entities.Values.OrderBy(x => x.CanonicalId, StringComparer.Ordinal).ToList();
            foreach (EquipmentEntity entity in index.EquipmentEntities)
            {
                entity.Occurrences = entity.Occurrences.OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
                entity.EquipmentListLinks = entity.EquipmentListLinks.OrderBy(x => x.SourceTag, StringComparer.Ordinal).ThenBy(x => x.SourceHandles.FirstOrDefault(), StringComparer.Ordinal).ToList();
                entity.HasDrawingEvidence = entity.Occurrences.Any(x => x.Role == "DrawingTag");
                entity.HasEquipmentListEvidence = entity.EquipmentListLinks.Count > 0;
            }

            index.StructureEntities = model.StructureTags.GroupBy(x => x.NormalizedTag ?? string.Empty, StringComparer.Ordinal).Where(x => x.Key.Length > 0).Select(group => new StructureEntity { CanonicalId = Canonical("structure", group.Key), NormalizedTag = group.Key, AreaOrUnitCode = group.First().AreaOrUnitCode, TypeCode = group.First().TypeCode, SequenceNumber = group.First().SequenceNumber, Occurrences = group.OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList() }).OrderBy(x => x.CanonicalId, StringComparer.Ordinal).ToList();
            index.GroupedTagLinks = index.GroupedTagLinks.OrderBy(x => x.SourceGroupedTag, StringComparer.Ordinal).ThenBy(x => x.SourceHandles.FirstOrDefault(), StringComparer.Ordinal).ToList();
            index.UnresolvedLinks = index.UnresolvedLinks.OrderBy(x => x.SourceValue, StringComparer.Ordinal).ThenBy(x => x.SourceHandles.FirstOrDefault(), StringComparer.Ordinal).ToList();
            index.AmbiguousLinks = index.AmbiguousLinks.OrderBy(x => x.SourceValue, StringComparer.Ordinal).ThenBy(x => x.SourceHandles.FirstOrDefault(), StringComparer.Ordinal).ToList();
            index.Statistics = new EntityIndexStatistics
            {
                EquipmentEntityCount = index.EquipmentEntities.Count,
                StructureEntityCount = index.StructureEntities.Count,
                EquipmentEntitiesWithDrawingEvidence = index.EquipmentEntities.Count(x => x.HasDrawingEvidence),
                EquipmentEntitiesWithEquipmentListEvidence = index.EquipmentEntities.Count(x => x.HasEquipmentListEvidence),
                EquipmentEntitiesWithBoth = index.EquipmentEntities.Count(x => x.HasDrawingEvidence && x.HasEquipmentListEvidence),
                GroupedTagCount = index.GroupedTagLinks.Count,
                GroupedMemberLinkCount = index.GroupedTagLinks.Sum(x => x.ResolvedEntityIds.Count),
                UnresolvedLinkCount = index.UnresolvedLinks.Count,
                AmbiguousLinkCount = index.AmbiguousLinks.Count
            };
            return index;
        }

        private static GroupedTagLink BuildGroupedLink(EngineeringEntityIndex index, Dictionary<string, EquipmentEntity> entities, EquipmentListEntry entry, EquipmentTag source)
        {
            var link = new GroupedTagLink { SourceGroupedTag = source.NormalizedTag ?? entry.Tag ?? string.Empty, SourceHandles = Sorted(entry.SourceHandles) };
            string[] suffixes = (source.Suffix ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (suffixes.Length == 0 || string.IsNullOrEmpty(source.EquipmentPrefix) || string.IsNullOrEmpty(source.UnitNumber) || string.IsNullOrEmpty(source.SequenceNumber))
            {
                index.UnresolvedLinks.Add(new UnresolvedLink { SourceType = "GroupedEquipmentListEntry", SourceValue = entry.Tag ?? string.Empty, Reason = "GroupedMemberDerivationFailed", SourceHandles = Sorted(entry.SourceHandles) });
                return link;
            }
            foreach (string suffix in suffixes)
            {
                string tag = source.EquipmentPrefix + "-" + source.UnitNumber + "-" + source.SequenceNumber + suffix;
                link.MemberNormalizedTags.Add(tag);
                EquipmentTag parsed = new EquipmentTag { NormalizedTag = tag, EquipmentPrefix = source.EquipmentPrefix, UnitNumber = source.UnitNumber, SequenceNumber = source.SequenceNumber, Suffix = suffix };
                EquipmentEntity entity = GetOrCreate(entities, parsed);
                entity.EquipmentListLinks.Add(ToLink(entry, "GroupedSuffixExpansion"));
                link.ResolvedEntityIds.Add(entity.CanonicalId);
            }
            link.MemberNormalizedTags = link.MemberNormalizedTags.OrderBy(x => x, StringComparer.Ordinal).ToList();
            link.ResolvedEntityIds = link.ResolvedEntityIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            return link;
        }

        private static EquipmentEntity GetOrCreate(Dictionary<string, EquipmentEntity> entities, EquipmentTag tag)
        {
            string normalized = tag.NormalizedTag ?? string.Empty;
            string id = Canonical("equipment", normalized);
            EquipmentEntity entity;
            if (!entities.TryGetValue(id, out entity!))
            {
                entity = new EquipmentEntity { CanonicalId = id, NormalizedTag = normalized, EquipmentPrefix = tag.EquipmentPrefix, UnitNumber = tag.UnitNumber, SequenceNumber = tag.SequenceNumber, Suffix = tag.Suffix };
                entities.Add(id, entity);
            }
            return entity;
        }
        private static EquipmentOccurrence ToOccurrence(EquipmentTag tag, string role) => new EquipmentOccurrence { RawText = tag.RawText, RawTag = tag.RawTag, NormalizedTag = tag.NormalizedTag, EquipmentPrefix = tag.EquipmentPrefix, UnitNumber = tag.UnitNumber, SequenceNumber = tag.SequenceNumber, Suffix = tag.Suffix, SourceHandle = tag.SourceHandle, SourceObjectType = tag.SourceObjectType, SourceLayer = tag.SourceLayer, PositionX = tag.PositionX, PositionY = tag.PositionY, PositionZ = tag.PositionZ, ParseStatus = tag.ParseStatus, Role = role };
        private static EquipmentListLink ToLink(EquipmentListEntry entry, string method) => new EquipmentListLink { SourceTag = entry.Tag ?? string.Empty, Description = entry.Description, Installation = entry.Installation, SourceHandles = Sorted(entry.SourceHandles), LinkMethod = method };
        private static bool IsGrouped(EquipmentTag tag) => tag.Suffix != null && tag.Suffix.IndexOf('/') >= 0;
        private static string Canonical(string kind, string? value) => kind + ":" + (value ?? string.Empty);
        private static string EvidenceKey(string? handle, string? tag) => (handle ?? string.Empty) + "\u001f" + (tag ?? string.Empty);
        private static List<string> Sorted(IEnumerable<string> values) => values.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    internal static class EquipmentTagExtensions
    {
        internal static bool SourceHandlesEqual(this EquipmentTag tag, IEnumerable<string> handles) => handles.Contains(tag.SourceHandle ?? string.Empty, StringComparer.Ordinal);
    }
}
