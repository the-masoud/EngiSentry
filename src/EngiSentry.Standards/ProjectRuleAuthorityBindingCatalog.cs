using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProjectRuleAuthorityBindingCatalogException : Exception { public ProjectRuleAuthorityBindingCatalogException(string message, Exception? innerException = null) : base(message, innerException) { } }

    /// <summary>
    /// The deterministic production intake loader for <see cref="ProjectRuleAuthorityBinding"/>. It is the ONLY path
    /// through which production acquires authority bindings, and it creates a binding exclusively from verified
    /// material: an artifact that is schema-correct, content-consistent, signed by a trusted issuer/key pair, scoped to
    /// the current project, non-revoked, and unambiguous.
    ///
    /// Determinism and fail-closed behaviour:
    /// - No binding directory => zero bindings (0 auto-executable rules). Missing material is the normal production
    ///   state and never an error; it simply leaves every project rule non-executable.
    /// - Any artifact that is malformed, unsigned, tampered, or signed by an untrusted issuer is invalid material. The
    ///   loader rejects it by failing closed (throwing), because silently ignoring unverifiable authority material
    ///   would hide a genuine integrity problem. ES_SUGGEST surfaces that as a failed, non-executing command.
    /// - Out-of-scope bindings (a different signed ProjectDirectory) are ignored, so one project's authority is never
    ///   active in another.
    /// - Revoked artifacts (signed RevokedAtUtc) are dropped.
    /// - Duplicate or conflicting authority for one project rule — two distinct surviving artifacts binding the same
    ///   ProjectRuleId, whether to the same or to a different authority rule — is an ambiguity the loader NEVER
    ///   resolves by picking a winner. It fails closed. Only unambiguous, single-source authority may execute.
    ///
    /// This loader grants nothing on its own: every surviving binding must still be validated field-by-field against
    /// the authority chain's actual resolution by <see cref="ProjectRuleAuthorityBindingValidator"/> inside the
    /// governed projection before it can execute anything.
    /// </summary>
    public static class ProjectRuleAuthorityBindingCatalog
    {
        public const string Suffix = ".project-rule.authority-binding.json";

        /// <summary>The production location for signed binding artifacts, alongside the existing authority-policies and
        /// authority-trust material under the shared EngiSentry program-data root.</summary>
        public static string CanonicalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngiSentry", "authority-bindings");

        /// <summary>Loads the verified bindings for the current project from the production directory.</summary>
        public static IReadOnlyList<ProjectRuleAuthorityBinding> LoadCanonical(string projectDirectory, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers)
            => Load(CanonicalDirectory, projectDirectory, trustedIssuers);

        /// <summary>Loads and verifies the bindings scoped to <paramref name="projectDirectory"/> from
        /// <paramref name="directory"/>. Returns an empty list when the directory is absent or no in-scope, non-revoked,
        /// unambiguous binding survives verification.</summary>
        public static IReadOnlyList<ProjectRuleAuthorityBinding> Load(string directory, string projectDirectory, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers)
        {
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));
            string expected = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);

            // No material => no bindings. This is the ordinary production state and stays fail-closed, not an error.
            if (!Directory.Exists(directory)) return new List<ProjectRuleAuthorityBinding>();

            var verified = new List<ProjectRuleAuthorityBinding>();
            var artifactIds = new HashSet<string>(StringComparer.Ordinal);
            var projectRuleIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (string path in Directory.GetFiles(directory, "*" + Suffix, SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
            {
                ProjectRuleAuthorityBindingArtifact artifact;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) artifact = (ProjectRuleAuthorityBindingArtifact)new DataContractJsonSerializer(typeof(ProjectRuleAuthorityBindingArtifact)).ReadObject(stream)!;
                    artifact = ProjectRuleAuthorityBindingArtifactValidator.Validate(artifact, trustedIssuers);
                }
                catch (Exception exception) { throw new ProjectRuleAuthorityBindingCatalogException("invalid project rule authority binding artifact: " + path, exception); }

                ProjectRuleAuthorityBinding binding = artifact.Binding!;

                // Scope: only this project's signed authority is active here.
                if (!string.Equals(binding.ProjectDirectory, expected, StringComparison.Ordinal)) continue;

                // Revocation: a signed revocation retires the binding without deleting the audit trail.
                if (artifact.RevokedAtUtc.HasValue) continue;

                // Duplicate artifact content is duplicate material and is rejected outright.
                if (!artifactIds.Add(artifact.BindingArtifactId)) throw new ProjectRuleAuthorityBindingCatalogException("duplicate binding artifact BindingArtifactId: " + artifact.BindingArtifactId);

                // Conflicting authority for one project rule is an unresolvable ambiguity: fail closed rather than
                // choose. This holds whether the conflict targets the same or a different authority rule.
                if (!projectRuleIds.Add(binding.ProjectRuleId)) throw new ProjectRuleAuthorityBindingCatalogException("conflicting authority bindings for project rule: " + binding.ProjectRuleId);

                verified.Add(ProjectRuleAuthorityBindingArtifactCanonicalizer.Copy(binding));
            }

            // Deterministic output order, independent of file system enumeration order.
            return verified.OrderBy(x => x.ProjectRuleId, StringComparer.Ordinal).ThenBy(x => x.AuthorityRuleId, StringComparer.Ordinal).ToList();
        }
    }
}
