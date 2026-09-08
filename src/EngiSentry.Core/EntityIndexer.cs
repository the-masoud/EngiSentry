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
                if (string.IsNullOrEmpty(occurrence.NormalizedTag)) continue;
                bool equipmentListOnly = listEvidence.Contains(EvidenceKey(occurrence.SourceHandle, occurrence.NormalizedTag));
                if (!IsGrouped(occurrence))
                {
                    EquipmentEntity entity = GetOrCreate(entities, occurrence);
                    entity.Occurrences.Add(ToOccurrence(occurrence, equipmentListOnly ? "EquipmentListTag" : "DrawingTag"));
                    continue;
                }

                EquipmentTagParseResult parsed;
                if (!EquipmentTagGrammar.TryParse(occurrence.RawTag ?? occurrence.NormalizedTag, out parsed))
                {
                    AddGroupedOccurrenceUnresolved(index, occurrence, "GroupedMemberDerivationFailed");
                    continue;
                }
                EquipmentTagExpansionResult expansion = EquipmentTagExpander.Expand(parsed);
                if (!expansion.Success)
                {
                    AddGroupedOccurrenceUnresolved(index, occurrence, expansion.FailureReason ?? "GroupedMemberDerivationFailed");
                    continue;
                }

                // Embedded Equipment List grouped evidence is represented by EquipmentListLinks below.
                // Only real drawing evidence becomes member occurrences here.
                if (equipmentListOnly) continue;
                foreach (EquipmentTagExpansionMember member in expansion.Members)
                {
                    EquipmentEntity entity = GetOrCreate(entities, MemberTag(parsed, member));
                    entity.Occurrences.Add(ToExpandedOccurrence(occurrence, parsed, member, "DrawingTag"));
                }
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
                entity.Occurrences = entity.Occurrences.OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ThenBy(x => x.RawTag, StringComparer.Ordinal).ToList();
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
            EquipmentTagParseResult parsed;
            if (!EquipmentTagGrammar.TryParse(source.RawTag ?? source.NormalizedTag, out parsed))
            {
                index.UnresolvedLinks.Add(new UnresolvedLink { SourceType = "GroupedEquipmentListEntry", SourceValue = entry.Tag ?? string.Empty, Reason = "GroupedMemberDerivationFailed", SourceHandles = Sorted(entry.SourceHandles) });
                return link;
            }

            EquipmentTagExpansionResult expansion = EquipmentTagExpander.Expand(parsed);
            if (!expansion.Success)
            {
                index.UnresolvedLinks.Add(new UnresolvedLink { SourceType = "GroupedEquipmentListEntry", SourceValue = entry.Tag ?? string.Empty, Reason = expansion.FailureReason ?? "GroupedMemberDerivationFailed", SourceHandles = Sorted(entry.SourceHandles) });
                return link;
            }

            if (expansion.Members.Count > 0) link.LinkMethod = expansion.Members[0].ExpansionMethod;
            foreach (EquipmentTagExpansionMember member in expansion.Members)
            {
                link.MemberNormalizedTags.Add(member.NormalizedMemberTag);
                EquipmentEntity entity = GetOrCreate(entities, MemberTag(parsed, member));
                entity.EquipmentListLinks.Add(ToLink(entry, member.ExpansionMethod));
                link.ResolvedEntityIds.Add(entity.CanonicalId);
            }
            return link;
        }

        private static EquipmentTag MemberTag(EquipmentTagParseResult parsed, EquipmentTagExpansionMember member) => new EquipmentTag
        {
            NormalizedTag = member.NormalizedMemberTag,
            EquipmentPrefix = parsed.Prefix,
            UnitNumber = parsed.UnitNumber,
            SequenceNumber = parsed.SequenceNumber,
            Suffix = member.MemberSuffix
        };

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

        private static EquipmentOccurrence ToExpandedOccurrence(EquipmentTag source, EquipmentTagParseResult parsed, EquipmentTagExpansionMember member, string role) => new EquipmentOccurrence
        {
            RawText = source.RawText,
            RawTag = source.RawTag,
            NormalizedTag = member.NormalizedMemberTag,
            EquipmentPrefix = parsed.Prefix,
            UnitNumber = parsed.UnitNumber,
            SequenceNumber = parsed.SequenceNumber,
            Suffix = member.MemberSuffix,
            SourceHandle = source.SourceHandle,
            SourceObjectType = source.SourceObjectType,
            SourceLayer = source.SourceLayer,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            PositionZ = source.PositionZ,
            ParseStatus = source.ParseStatus,
            Role = role
        };

        private static void AddGroupedOccurrenceUnresolved(EngineeringEntityIndex index, EquipmentTag source, string reason)
        {
            string? sourceHandle = source.SourceHandle;
            var sourceHandles = new List<string>();
            if (sourceHandle != null && sourceHandle.Length > 0) sourceHandles.Add(sourceHandle);
            index.UnresolvedLinks.Add(new UnresolvedLink
            {
                SourceType = "GroupedEquipmentTag",
                SourceValue = source.NormalizedTag ?? source.RawTag ?? string.Empty,
                Reason = reason,
                SourceHandles = sourceHandles
            });
        }

        private static EquipmentListLink ToLink(EquipmentListEntry entry, string method) => new EquipmentListLink { SourceTag = entry.Tag ?? string.Empty, Description = entry.Description, Installation = entry.Installation, SourceHandles = Sorted(entry.SourceHandles), LinkMethod = method };
        private static bool IsGrouped(EquipmentTag tag)
        {
            EquipmentTagParseResult parsed;
            return EquipmentTagGrammar.TryParse(tag.RawTag ?? tag.NormalizedTag, out parsed) ? parsed.IsGrouped : tag.Suffix != null && (tag.Suffix.IndexOf('/') >= 0 || tag.Suffix.IndexOf('~') >= 0);
        }
        private static string Canonical(string kind, string? value) => kind + ":" + (value ?? string.Empty);
        private static string EvidenceKey(string? handle, string? tag) => (handle ?? string.Empty) + "\u001f" + (tag ?? string.Empty);
        private static List<string> Sorted(IEnumerable<string> values) => values.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    internal static class EquipmentTagExtensions
    {
        internal static bool SourceHandlesEqual(this EquipmentTag tag, IEnumerable<string> handles) => handles.Contains(tag.SourceHandle ?? string.Empty, StringComparer.Ordinal);
    }
}
