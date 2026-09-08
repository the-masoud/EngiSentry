using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardSourceRegistryValidationException : Exception
    {
        public StandardSourceRegistryValidationException(string message)
            : base(message) { }
    }

    public sealed class StandardSourceRegistry
    {
        private readonly List<StandardSourceDefinition> sources;

        private StandardSourceRegistry(List<StandardSourceDefinition> sources)
        {
            this.sources = sources;
        }

        public IReadOnlyList<StandardSourceDefinition> Sources => sources.Select(Copy).ToList();

        public static StandardSourceRegistry Create(IEnumerable<StandardSourceDefinition> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));

            var accepted = new List<StandardSourceDefinition>();
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (StandardSourceDefinition source in sources)
            {
                if (source == null) throw new StandardSourceRegistryValidationException("standard source registry contains a null source");
                Validate(source);
                if (!sourceIds.Add(source.SourceId))
                {
                    throw new StandardSourceRegistryValidationException("duplicate standard source SourceId: " + source.SourceId);
                }

                accepted.Add(Copy(source));
            }

            return new StandardSourceRegistry(accepted.OrderBy(source => source.SourceId, StringComparer.Ordinal).ToList());
        }

        public bool TryGet(string sourceId, out StandardSourceDefinition? source)
        {
            source = null;
            if (string.IsNullOrWhiteSpace(sourceId)) return false;

            StandardSourceDefinition? found = sources.FirstOrDefault(candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal));
            if (found == null) return false;

            source = Copy(found);
            return true;
        }

        public StandardSourceDefinition Get(string sourceId)
        {
            if (TryGet(sourceId, out StandardSourceDefinition? source) && source != null) return source;
            throw new StandardSourceRegistryValidationException("standard source was not found: " + (sourceId ?? string.Empty));
        }

        private static void Validate(StandardSourceDefinition source)
        {
            if (string.IsNullOrWhiteSpace(source.SourceId)) throw new StandardSourceRegistryValidationException("standard source SourceId is required");
            if (string.IsNullOrWhiteSpace(source.DocumentNumber)) throw new StandardSourceRegistryValidationException("standard source DocumentNumber is required: " + source.SourceId);
            if (string.IsNullOrWhiteSpace(source.Title)) throw new StandardSourceRegistryValidationException("standard source Title is required: " + source.SourceId);
            if (string.IsNullOrWhiteSpace(source.Issuer)) throw new StandardSourceRegistryValidationException("standard source Issuer is required: " + source.SourceId);
            if (source.RevisionOrEdition != null && string.IsNullOrWhiteSpace(source.RevisionOrEdition)) throw new StandardSourceRegistryValidationException("standard source RevisionOrEdition is empty: " + source.SourceId);
            if (source.SourceFileName != null && string.IsNullOrWhiteSpace(source.SourceFileName)) throw new StandardSourceRegistryValidationException("standard source SourceFileName is empty: " + source.SourceId);
            if (source.SourceSha256 != null && !IsSha256(source.SourceSha256)) throw new StandardSourceRegistryValidationException("standard source SHA256 is invalid: " + source.SourceId);
        }

        private static bool IsSha256(string value)
        {
            return value.Length == 64 && value.All(character =>
                (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F'));
        }

        private static StandardSourceDefinition Copy(StandardSourceDefinition source)
        {
            return new StandardSourceDefinition
            {
                SourceId = source.SourceId,
                DocumentNumber = source.DocumentNumber,
                Title = source.Title,
                Issuer = source.Issuer,
                RevisionOrEdition = source.RevisionOrEdition,
                SourceFileName = source.SourceFileName,
                SourceSha256 = source.SourceSha256 == null ? null : source.SourceSha256.ToUpperInvariant()
            };
        }
    }
}
