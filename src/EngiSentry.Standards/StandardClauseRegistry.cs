using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardClauseRegistryValidationException : Exception
    {
        public StandardClauseRegistryValidationException(string message)
            : base(message) { }
    }

    public sealed class StandardClauseRegistry
    {
        private readonly List<StandardClauseDefinition> clauses;

        private StandardClauseRegistry(List<StandardClauseDefinition> clauses)
        {
            this.clauses = clauses;
        }

        public IReadOnlyList<StandardClauseDefinition> Clauses => clauses.Select(Copy).ToList();

        public static StandardClauseRegistry Create(StandardSourceRegistry sourceRegistry, IEnumerable<StandardClauseDefinition> clauses)
        {
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));
            if (clauses == null) throw new ArgumentNullException(nameof(clauses));

            var accepted = new List<StandardClauseDefinition>();
            var clauseIds = new HashSet<string>(StringComparer.Ordinal);
            var referencesBySource = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (StandardClauseDefinition clause in clauses)
            {
                if (clause == null) throw new StandardClauseRegistryValidationException("standard clause registry contains a null clause");
                Validate(clause);
                if (!sourceRegistry.TryGet(clause.SourceId, out _))
                {
                    throw new StandardClauseRegistryValidationException("standard clause references unknown SourceId: " + clause.SourceId);
                }

                if (!clauseIds.Add(clause.ClauseId))
                {
                    throw new StandardClauseRegistryValidationException("duplicate standard clause ClauseId: " + clause.ClauseId);
                }

                if (!referencesBySource.TryGetValue(clause.SourceId, out HashSet<string>? references))
                {
                    references = new HashSet<string>(StringComparer.Ordinal);
                    referencesBySource.Add(clause.SourceId, references);
                }

                if (!references.Add(clause.ClauseReference))
                {
                    throw new StandardClauseRegistryValidationException("duplicate standard clause reference: " + clause.SourceId + " | " + clause.ClauseReference);
                }

                accepted.Add(Copy(clause));
            }

            return new StandardClauseRegistry(accepted.OrderBy(clause => clause.ClauseId, StringComparer.Ordinal).ToList());
        }

        public bool TryGet(string clauseId, out StandardClauseDefinition? clause)
        {
            clause = null;
            if (string.IsNullOrWhiteSpace(clauseId)) return false;

            StandardClauseDefinition? found = clauses.FirstOrDefault(candidate => string.Equals(candidate.ClauseId, clauseId, StringComparison.Ordinal));
            if (found == null) return false;

            clause = Copy(found);
            return true;
        }

        public StandardClauseDefinition Get(string clauseId)
        {
            if (TryGet(clauseId, out StandardClauseDefinition? clause) && clause != null) return clause;
            throw new StandardClauseRegistryValidationException("standard clause was not found: " + (clauseId ?? string.Empty));
        }

        private static void Validate(StandardClauseDefinition clause)
        {
            if (string.IsNullOrWhiteSpace(clause.ClauseId)) throw new StandardClauseRegistryValidationException("standard clause ClauseId is required");
            if (string.IsNullOrWhiteSpace(clause.SourceId)) throw new StandardClauseRegistryValidationException("standard clause SourceId is required: " + clause.ClauseId);
            if (string.IsNullOrWhiteSpace(clause.ClauseReference)) throw new StandardClauseRegistryValidationException("standard clause ClauseReference is required: " + clause.ClauseId);
            if (string.IsNullOrWhiteSpace(clause.ClauseText)) throw new StandardClauseRegistryValidationException("standard clause ClauseText is required: " + clause.ClauseId);
            if (clause.Heading != null && string.IsNullOrWhiteSpace(clause.Heading)) throw new StandardClauseRegistryValidationException("standard clause Heading is empty: " + clause.ClauseId);
            if (clause.PageNumber.HasValue && clause.PageNumber.Value <= 0) throw new StandardClauseRegistryValidationException("standard clause PageNumber must be greater than zero: " + clause.ClauseId);
        }

        private static StandardClauseDefinition Copy(StandardClauseDefinition clause)
        {
            return new StandardClauseDefinition
            {
                ClauseId = clause.ClauseId,
                SourceId = clause.SourceId,
                ClauseReference = clause.ClauseReference,
                ClauseText = clause.ClauseText,
                Heading = clause.Heading,
                PageNumber = clause.PageNumber
            };
        }
    }
}
