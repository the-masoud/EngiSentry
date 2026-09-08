using System;
using System.IO;
using EngiSentry.Contracts;

namespace EngiSentry.CrossDocument
{
    public static class ExternalEquipmentRegisterLoader
    {
        private const string NoRegisterMessage = "No Equipment Register is linked to this drawing/project. ES_CROSSQA will not use a global or test register because that could create false findings. Add a project register under <drawing folder>\\EngiSentry\\active-equipment-register.xlsx or .csv, a drawing-specific <drawing name>.equipment-register.xlsx or .csv, or <drawing folder>\\active-equipment-register.xlsx or .csv.";

        public static string ResolveAutomaticPath(string drawingPath)
        {
            string drawingDirectory = string.IsNullOrWhiteSpace(drawingPath) ? string.Empty : (Path.GetDirectoryName(drawingPath) ?? string.Empty);
            string drawingBaseName = string.IsNullOrWhiteSpace(drawingPath) ? string.Empty : Path.GetFileNameWithoutExtension(drawingPath);
            if (string.IsNullOrWhiteSpace(drawingDirectory) || string.IsNullOrWhiteSpace(drawingBaseName))
            {
                throw new ExternalRegisterValidationException(NoRegisterMessage);
            }

            string[] candidates =
            {
                Path.Combine(drawingDirectory, "EngiSentry", "active-equipment-register.xlsx"),
                Path.Combine(drawingDirectory, "EngiSentry", "active-equipment-register.csv"),
                Path.Combine(drawingDirectory, drawingBaseName + ".equipment-register.xlsx"),
                Path.Combine(drawingDirectory, drawingBaseName + ".equipment-register.csv"),
                Path.Combine(drawingDirectory, "active-equipment-register.xlsx"),
                Path.Combine(drawingDirectory, "active-equipment-register.csv")
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new ExternalRegisterValidationException(NoRegisterMessage);
        }

        public static ExternalEquipmentRegister ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ExternalRegisterValidationException("external register path is empty");
            }

            string extension = Path.GetExtension(path);
            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ExternalEquipmentRegisterParser.ReadFile(path);
            }

            if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return XlsxEquipmentRegisterExtractor.ReadFile(path);
                }
                catch (XlsxWorkbookValidationException ex)
                {
                    throw new ExternalRegisterValidationException(ex.Message);
                }
            }

            throw new ExternalRegisterValidationException("unsupported equipment register file type; supported types are .xlsx and .csv");
        }
    }
}
