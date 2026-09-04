using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class ProfileValidationException : Exception
    {
        public ProfileValidationException(string message) : base(message) { }
    }

    public static class ProfileJson
    {
        public static List<EngineeringStandardProfile> ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ProfileValidationException("Profile path is required.");
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    object? value = new DataContractJsonSerializer(typeof(List<EngineeringStandardProfile>)).ReadObject(stream);
                    return value as List<EngineeringStandardProfile> ?? throw new ProfileValidationException("Profile JSON root must be an array.");
                }
            }
            catch (ProfileValidationException) { throw; }
            catch (Exception exception) when (exception is IOException || exception is System.Runtime.Serialization.SerializationException || exception is ArgumentException)
            {
                throw new ProfileValidationException("Invalid profile JSON: " + exception.Message);
            }
        }
    }

    public static class ProfileResolver
    {
        public const string BuiltInProfileId = "engisentry.builtin.default";
        private static readonly string[] ExternalLevels = { "Company", "Client", "Project" };
        private static readonly HashSet<string> Severities = new HashSet<string>(new[] { "Info", "Warning", "Error" }, StringComparer.Ordinal);

        public static ProfileResolution Resolve(IEnumerable<KnownQaRule> knownRules, IEnumerable<EngineeringStandardProfile>? externalProfiles)
        {
            if (knownRules == null) throw new ArgumentNullException(nameof(knownRules));
            List<KnownQaRule> known = knownRules.OrderBy(x => x.RuleId, StringComparer.Ordinal).ToList();
            ValidateKnownRules(known);
            List<EngineeringStandardProfile> external = (externalProfiles ?? Enumerable.Empty<EngineeringStandardProfile>()).ToList();
            ValidateExternalProfiles(external, known);
            var effective = known.ToDictionary(x => x.RuleId, x => new EffectiveRuleSetting { EffectiveRuleId = x.RuleId, Enabled = true, EffectiveSeverity = x.Severity, EnabledSourceProfileId = BuiltInProfileId, SeveritySourceProfileId = BuiltInProfileId }, StringComparer.Ordinal);
            var applied = new List<string> { BuiltInProfileId };
            foreach (string level in ExternalLevels)
            {
                EngineeringStandardProfile? profile = external.SingleOrDefault(x => x.Level == level);
                if (profile == null) continue;
                applied.Add(profile.ProfileId!);
                foreach (RuleSetting setting in profile.RuleSettings ?? new List<RuleSetting>())
                {
                    EffectiveRuleSetting target = effective[setting.RuleId!];
                    if (setting.Enabled.HasValue) { target.Enabled = setting.Enabled.Value; target.EnabledSourceProfileId = profile.ProfileId!; }
                    if (setting.SeverityOverride != null) { target.EffectiveSeverity = setting.SeverityOverride; target.SeveritySourceProfileId = profile.ProfileId!; }
                }
            }
            return new ProfileResolution { ValidationStatus = "VALID", AppliedProfileIds = applied, EffectiveRuleSettings = effective.Values.OrderBy(x => x.EffectiveRuleId, StringComparer.Ordinal).ToList() };
        }

        private static void ValidateKnownRules(List<KnownQaRule> known)
        {
            if (known.Count == 0) throw new ProfileValidationException("Known rule catalog is empty.");
            if (known.Any(x => string.IsNullOrWhiteSpace(x.RuleId))) throw new ProfileValidationException("Known rule catalog contains an empty RuleId.");
            if (known.GroupBy(x => x.RuleId, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new ProfileValidationException("Known rule catalog contains duplicate RuleIds.");
            if (known.Any(x => !Severities.Contains(x.Severity))) throw new ProfileValidationException("Known rule catalog contains an invalid severity.");
        }

        private static void ValidateExternalProfiles(List<EngineeringStandardProfile> profiles, List<KnownQaRule> known)
        {
            if (profiles.Any(x => x == null)) throw new ProfileValidationException("Profile array contains a null profile.");
            if (profiles.Any(x => string.IsNullOrWhiteSpace(x.ProfileId))) throw new ProfileValidationException("ProfileId is required.");
            if (profiles.GroupBy(x => x.ProfileId!, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new ProfileValidationException("Duplicate ProfileId.");
            if (profiles.Any(x => string.IsNullOrWhiteSpace(x.Name))) throw new ProfileValidationException("Profile Name is required.");
            if (profiles.Any(x => string.IsNullOrWhiteSpace(x.Source))) throw new ProfileValidationException("Profile Source is required.");
            if (profiles.Any(x => x.Level == "BuiltIn")) throw new ProfileValidationException("External profiles cannot use Level BuiltIn.");
            if (profiles.Any(x => x.Level == null || !ExternalLevels.Contains(x.Level, StringComparer.Ordinal))) throw new ProfileValidationException("Unknown profile Level.");
            if (profiles.GroupBy(x => x.Level!, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new ProfileValidationException("Duplicate profile Level.");
            var knownIds = new HashSet<string>(known.Select(x => x.RuleId), StringComparer.Ordinal);
            foreach (EngineeringStandardProfile profile in profiles)
            {
                List<RuleSetting> settings = profile.RuleSettings ?? new List<RuleSetting>();
                if (settings.Any(x => x == null)) throw new ProfileValidationException("Profile contains a null RuleSetting.");
                if (settings.Any(x => string.IsNullOrWhiteSpace(x.RuleId))) throw new ProfileValidationException("RuleSetting RuleId is required.");
                if (settings.GroupBy(x => x.RuleId!, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new ProfileValidationException("Duplicate RuleId inside profile " + profile.ProfileId + ".");
                if (settings.Any(x => !knownIds.Contains(x.RuleId!))) throw new ProfileValidationException("Unknown RuleId.");
                if (settings.Any(x => x.SeverityOverride != null && !Severities.Contains(x.SeverityOverride))) throw new ProfileValidationException("Invalid SeverityOverride.");
                if (settings.Any(x => !x.Enabled.HasValue && x.SeverityOverride == null)) throw new ProfileValidationException("RuleSetting must set Enabled or SeverityOverride.");
            }
        }
    }
}
