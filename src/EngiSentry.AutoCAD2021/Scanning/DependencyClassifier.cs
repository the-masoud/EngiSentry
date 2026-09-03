using System.IO;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021.Scanning
{
    public static class DependencyClassifier
    {
        public static ExternalDependencySnapshot Classify(string kind, string name, string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath) || !Path.IsPathRooted(rawPath))
            {
                return new ExternalDependencySnapshot
                {
                    Kind = kind, Name = name, RawPath = rawPath, Status = "Unknown", ExistsOnDisk = null
                };
            }

            bool exists = File.Exists(rawPath);
            return new ExternalDependencySnapshot
            {
                Kind = kind, Name = name, RawPath = rawPath, ResolvedPath = rawPath,
                Status = exists ? "Found" : "Missing", ExistsOnDisk = exists
            };
        }
    }
}
