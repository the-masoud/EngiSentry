using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardAuthorityPolicyArtifactCatalogException : Exception { public StandardAuthorityPolicyArtifactCatalogException(string message, Exception? innerException = null) : base(message, innerException) { } }

    /// <summary>
    /// The verified set of external standards-authority policies loaded from signed material. It carries only
    /// policies that are authentic, trusted, in-scope, non-revoked and unambiguous; it never decides
    /// executability. Callers merge these with the built-in baseline into a single
    /// <see cref="StandardAuthorityPolicyRegistry"/>, which is the sole place the resolver reads policies from.
    /// </summary>
    public sealed class StandardAuthorityPolicyVerifiedCatalog
    {
        private readonly List<StandardAuthorityPolicyArtifact> artifacts;
        internal StandardAuthorityPolicyVerifiedCatalog(IEnumerable<StandardAuthorityPolicyArtifact> values) { artifacts = values.Select(StandardAuthorityPolicyArtifactCanonicalizer.CopyArtifact).ToList(); }
        public IReadOnlyList<StandardAuthorityPolicyArtifact> Artifacts => artifacts.Select(StandardAuthorityPolicyArtifactCanonicalizer.CopyArtifact).ToList();
        public IReadOnlyList<StandardAuthorityPolicyDefinition> Policies => artifacts.Select(x => StandardAuthorityPolicyArtifactCanonicalizer.Copy(x.Policy!)).ToList();
    }

    /// <summary>
    /// The deterministic production intake loader for external <see cref="StandardAuthorityPolicyDefinition"/>
    /// material. It is the ONLY path through which production acquires standards-authority policies outside the
    /// built-in safe baseline, and it accepts a policy exclusively from verified material: an artifact that is
    /// schema-correct, project-scoped, content-consistent, signed by a trusted issuer/key pair, non-revoked and
    /// unambiguous.
    ///
    /// Determinism and fail-closed behaviour:
    /// - No policy directory => zero external policies (the built-in baseline alone yields 0 deterministic
    ///   gates). Missing material is the normal production state and never an error.
    /// - Any artifact that is malformed, unsigned, tampered, or signed by an untrusted issuer is invalid
    ///   material. The loader rejects it by failing closed (throwing), because silently ignoring unverifiable
    ///   authority material would hide a genuine integrity problem.
    /// - Out-of-scope policies (a different signed ProjectDirectory) are ignored, so one project's deterministic
    ///   authority is never active in another.
    /// - Revoked artifacts (signed RevokedAtUtc) are dropped; their source/clause returns to the built-in
    ///   baseline rather than retaining any grant.
    /// - Duplicate or conflicting active authority — the same PolicyArtifactId, or two active policies claiming
    ///   the same PolicyId, the same source-wide SourceId, or the same ClauseId — is an ambiguity the loader
    ///   NEVER resolves by picking a winner. It fails closed. Only unambiguous authority may reach the registry.
    ///
    /// This loader grants nothing on its own: every surviving policy must still pass the accepted
    /// <see cref="StandardAuthorityPolicyRegistry"/> validation (which additionally rejects conflicts with the
    /// built-in baseline) before <see cref="StandardAuthorityResolver"/> and
    /// <see cref="StandardAuthorityExecutionGate"/> can decide anything.
    /// </summary>
    public static class StandardAuthorityPolicyArtifactCatalog
    {
        public const string Suffix = ".standard-authority-policy.json";

        /// <summary>The canonical machine-wide production location for signed standards-authority policy
        /// artifacts, under the shared EngiSentry program-data root.</summary>
        public static string CanonicalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngiSentry", "standard-authority-policies");

        /// <summary>Loads the verified external policies for the current project from the production
        /// directory.</summary>
        public static StandardAuthorityPolicyVerifiedCatalog LoadCanonical(
            string expectedProjectDirectory,
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers,
            StandardSourceRegistry sourceRegistry,
            StandardClauseRegistry clauseRegistry)
            => Load(CanonicalDirectory, expectedProjectDirectory, trustedIssuers, sourceRegistry, clauseRegistry);

        /// <summary>Loads and verifies the policies scoped to <paramref name="expectedProjectDirectory"/> from
        /// <paramref name="directory"/>. Returns an empty set when the directory is absent or no in-scope,
        /// non-revoked, unambiguous policy survives verification.</summary>
        public static StandardAuthorityPolicyVerifiedCatalog Load(
            string directory,
            string expectedProjectDirectory,
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers,
            StandardSourceRegistry sourceRegistry,
            StandardClauseRegistry clauseRegistry)
        {
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            string expected = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProjectDirectory);

            // No material => no external policies. This is the ordinary production state and stays fail-closed.
            if (!Directory.Exists(directory)) return new StandardAuthorityPolicyVerifiedCatalog(new StandardAuthorityPolicyArtifact[0]);

            var retained = new List<StandardAuthorityPolicyArtifact>();
            var artifactIds = new HashSet<string>(StringComparer.Ordinal);
            var policyIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceWideSourceIds = new HashSet<string>(StringComparer.Ordinal);
            var clauseIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(directory, "*" + Suffix, SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
            {
                StandardAuthorityPolicyArtifact artifact;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) artifact = (StandardAuthorityPolicyArtifact)new DataContractJsonSerializer(typeof(StandardAuthorityPolicyArtifact)).ReadObject(stream)!;
                    artifact = StandardAuthorityPolicyArtifactValidator.Validate(artifact, trustedIssuers, sourceRegistry, clauseRegistry);
                }
                catch (Exception exception) { throw new StandardAuthorityPolicyArtifactCatalogException("invalid standard authority policy artifact: " + path, exception); }

                // Scope: only this project's signed authority is active here.
                if (!string.Equals(artifact.ProjectDirectory, expected, StringComparison.Ordinal)) continue;

                // Revocation: a signed revocation retires the policy without deleting the audit trail.
                if (artifact.RevokedAtUtc.HasValue) continue;

                // Duplicate artifact content is duplicate material and is rejected outright.
                if (!artifactIds.Add(artifact.PolicyArtifactId)) throw new StandardAuthorityPolicyArtifactCatalogException("duplicate standard authority policy artifact PolicyArtifactId: " + artifact.PolicyArtifactId);

                StandardAuthorityPolicyDefinition policy = artifact.Policy!;

                // Conflicting active authority is an unresolvable ambiguity: fail closed rather than choose. This
                // holds for the same PolicyId, the same source-wide SourceId and the same ClauseId, mirroring the
                // accepted registry so the ambiguity can never be hidden by ordering.
                if (!policyIds.Add(policy.PolicyId)) throw new StandardAuthorityPolicyArtifactCatalogException("conflicting active standard authority policy: duplicate PolicyId " + policy.PolicyId);
                if (policy.ClauseId == null)
                {
                    if (!sourceWideSourceIds.Add(policy.SourceId)) throw new StandardAuthorityPolicyArtifactCatalogException("conflicting active standard authority policy: duplicate source-wide policy for " + policy.SourceId);
                }
                else if (!clauseIds.Add(policy.ClauseId)) throw new StandardAuthorityPolicyArtifactCatalogException("conflicting active standard authority policy: duplicate clause policy for " + policy.ClauseId);

                retained.Add(artifact);
            }

            // Deterministic output order, independent of file system enumeration order.
            return new StandardAuthorityPolicyVerifiedCatalog(retained
                .OrderBy(x => x.Policy!.SourceId, StringComparer.Ordinal)
                .ThenBy(x => x.Policy!.ClauseId == null ? 0 : 1)
                .ThenBy(x => x.Policy!.ClauseId, StringComparer.Ordinal)
                .ThenBy(x => x.Policy!.PolicyId, StringComparer.Ordinal));
        }
    }
}
