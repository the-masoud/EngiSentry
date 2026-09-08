using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021
{
    public sealed class ProjectDistanceCriteriaValidationException : Exception
    {
        public ProjectDistanceCriteriaValidationException(string message) : base(message) { }
        public ProjectDistanceCriteriaValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static class ProjectDistanceCriteriaStore
    {
        public static string ProductionStorageRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "project-distance-criteria");

        public static string ResolveProjectDirectory(string drawingFullPath)
        {
            if (string.IsNullOrWhiteSpace(drawingFullPath) || !Path.IsPathRooted(drawingFullPath)) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria require a saved drawing.");
            try
            {
                string? directory = Path.GetDirectoryName(Path.GetFullPath(drawingFullPath));
                if (string.IsNullOrWhiteSpace(directory)) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria require a saved drawing.");
                return NormalizeProjectDirectory(directory);
            }
            catch (ProjectDistanceCriteriaValidationException) { throw; }
            catch (Exception exception) { throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria require a saved drawing.", exception); }
        }

        public static string GetSettingsPath(string projectDirectory, string storageRoot)
        {
            string normalizedDirectory = NormalizeProjectDirectory(projectDirectory);
            if (string.IsNullOrWhiteSpace(storageRoot)) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria storage root is required.");
            string normalizedRoot = Path.GetFullPath(storageRoot);
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedDirectory))).Replace("-", string.Empty);
            return Path.Combine(normalizedRoot, hash + ".json");
        }

        public static bool TryLoad(string projectDirectory, string storageRoot, out ProjectDistanceCriteria? criteria)
        {
            string normalizedDirectory = NormalizeProjectDirectory(projectDirectory);
            string path = GetSettingsPath(normalizedDirectory, storageRoot);
            if (!File.Exists(path)) { criteria = null; return false; }
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(ProjectDistanceCriteria));
                    criteria = serializer.ReadObject(stream) as ProjectDistanceCriteria;
                }
            }
            catch (Exception exception)
            {
                throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria settings are invalid: " + path, exception);
            }
            if (criteria == null) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria settings are invalid: " + path);
            ProjectDistanceCriteria normalized = ValidateAndCopy(criteria);
            if (!string.Equals(normalized.ProjectDirectory, normalizedDirectory, StringComparison.Ordinal)) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria settings project directory does not match scope: " + path);
            criteria = normalized;
            return true;
        }

        public static void Save(ProjectDistanceCriteria criteria, string storageRoot)
        {
            ProjectDistanceCriteria validated = ValidateAndCopy(criteria);
            if (validated.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria Save requires schema version " + ProjectDistanceCriteriaDefaults.CurrentSchemaVersion + ".");
            string path = GetSettingsPath(validated.ProjectDirectory, storageRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (FileStream stream = File.Create(path)) new DataContractJsonSerializer(typeof(ProjectDistanceCriteria)).WriteObject(stream, validated);
        }

        public static ProjectDistanceCriteria CreateDefaults(string projectDirectory)
        {
            string normalizedDirectory = NormalizeProjectDirectory(projectDirectory);
            return new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = normalizedDirectory, AccessRoadEdgeMinimumMeters = ProjectDistanceCriteriaDefaults.AccessRoadEdgeMinimumMeters, SecurityFenceMinimumMeters = ProjectDistanceCriteriaDefaults.SecurityFenceMinimumMeters, UnitBoundaryMinimumMeters = ProjectDistanceCriteriaDefaults.UnitBoundaryMinimumMeters };
        }

        public static ProjectDistanceCriteria CreateCurrentDraft(ProjectDistanceCriteria criteria)
        {
            ProjectDistanceCriteria validated = ValidateAndCopy(criteria);
            if (validated.SchemaVersion == 1) return new ProjectDistanceCriteria { SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion, ProjectDirectory = validated.ProjectDirectory, AccessRoadEdgeMinimumMeters = validated.AccessRoadEdgeMinimumMeters, SecurityFenceMinimumMeters = validated.SecurityFenceMinimumMeters, UnitBoundaryMinimumMeters = ProjectDistanceCriteriaDefaults.UnitBoundaryMinimumMeters };
            return new ProjectDistanceCriteria { SchemaVersion = validated.SchemaVersion, ProjectDirectory = validated.ProjectDirectory, AccessRoadEdgeMinimumMeters = validated.AccessRoadEdgeMinimumMeters, SecurityFenceMinimumMeters = validated.SecurityFenceMinimumMeters, UnitBoundaryMinimumMeters = validated.UnitBoundaryMinimumMeters };
        }

        public static ProjectDistanceCriteria? ResolveOrPrompt(string drawingFullPath)
        {
            string projectDirectory = ResolveProjectDirectory(drawingFullPath);
            if (TryLoad(projectDirectory, ProductionStorageRoot, out ProjectDistanceCriteria? criteria) && criteria!.SchemaVersion == ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) return criteria;
            ProjectDistanceCriteria draft = criteria == null ? CreateDefaults(projectDirectory) : CreateCurrentDraft(criteria);
            ProjectDistanceCriteria? selected = ProjectDistanceCriteriaDialog.Show(projectDirectory, draft);
            if (selected == null) return null;
            Save(selected, ProductionStorageRoot);
            return ValidateAndCopy(selected);
        }

        public static ProjectDistanceCriteria ValidateAndCopy(ProjectDistanceCriteria criteria)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            if (criteria.SchemaVersion != 1 && criteria.SchemaVersion != ProjectDistanceCriteriaDefaults.CurrentSchemaVersion) throw new ProjectDistanceCriteriaValidationException("Unsupported Project Distance Criteria schema version: " + criteria.SchemaVersion);
            string directory = NormalizeProjectDirectory(criteria.ProjectDirectory);
            if (criteria.AccessRoadEdgeMinimumMeters <= 0m) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria Access Road Edge minimum must be greater than zero.");
            if (criteria.SecurityFenceMinimumMeters <= 0m) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria Security Fence minimum must be greater than zero.");
            if (criteria.SchemaVersion == ProjectDistanceCriteriaDefaults.CurrentSchemaVersion && criteria.UnitBoundaryMinimumMeters <= 0m) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria Unit Boundary minimum must be greater than zero.");
            return new ProjectDistanceCriteria { SchemaVersion = criteria.SchemaVersion, ProjectDirectory = directory, AccessRoadEdgeMinimumMeters = criteria.AccessRoadEdgeMinimumMeters, SecurityFenceMinimumMeters = criteria.SecurityFenceMinimumMeters, UnitBoundaryMinimumMeters = criteria.UnitBoundaryMinimumMeters };
        }

        private static string NormalizeProjectDirectory(string projectDirectory)
        {
            if (string.IsNullOrWhiteSpace(projectDirectory) || !Path.IsPathRooted(projectDirectory)) throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria ProjectDirectory must be an absolute path.");
            try
            {
                string full = Path.GetFullPath(projectDirectory);
                string root = Path.GetPathRoot(full) ?? string.Empty;
                string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (trimmed.Length == 0) trimmed = root;
                return trimmed.ToUpperInvariant();
            }
            catch (ProjectDistanceCriteriaValidationException) { throw; }
            catch (Exception exception) { throw new ProjectDistanceCriteriaValidationException("Project Distance Criteria ProjectDirectory must be an absolute path.", exception); }
        }
    }
}
