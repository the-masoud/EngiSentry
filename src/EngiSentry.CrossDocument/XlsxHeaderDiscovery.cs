using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EngiSentry.CrossDocument
{
    public sealed class XlsxHeaderDiscoveryException : Exception
    {
        public XlsxHeaderDiscoveryException(string message) : base(message) { }
    }

    public sealed class XlsxHeaderDiscoveryResult
    {
        public XlsxHeaderDiscoveryResult(int headerRowNumber, int tagColumnIndex, int descriptionColumnIndex, int? installationColumnIndex)
        {
            HeaderRowNumber = headerRowNumber;
            TagColumnIndex = tagColumnIndex;
            DescriptionColumnIndex = descriptionColumnIndex;
            InstallationColumnIndex = installationColumnIndex;
        }

        public int HeaderRowNumber { get; }
        public int TagColumnIndex { get; }
        public int DescriptionColumnIndex { get; }
        public int? InstallationColumnIndex { get; }
    }

    public static class XlsxHeaderDiscovery
    {
        private static readonly HashSet<string> TagAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "TAG", "TAG NO", "TAG NUMBER", "ITEM NO", "ITEM NUMBER", "EQUIPMENT NO", "EQUIPMENT NUMBER"
        };

        private static readonly HashSet<string> DescriptionAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "DESCRIPTION", "SERVICE"
        };

        public static XlsxHeaderDiscoveryResult Discover(XlsxWorksheetReadResult worksheet)
        {
            if (worksheet == null)
            {
                throw new ArgumentNullException(nameof(worksheet));
            }

            foreach (XlsxWorksheetRow row in worksheet.Rows.OrderBy(x => x.RowNumber))
            {
                var tagMatches = new List<int>();
                var descriptionMatches = new List<int>();
                var installationMatches = new List<int>();
                for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
                {
                    string? normalized = Normalize(row.Cells[columnIndex]);
                    if (normalized == null)
                    {
                        continue;
                    }

                    if (TagAliases.Contains(normalized))
                    {
                        tagMatches.Add(columnIndex);
                    }

                    if (DescriptionAliases.Contains(normalized))
                    {
                        descriptionMatches.Add(columnIndex);
                    }

                    if (string.Equals(normalized, "INSTALLATION", StringComparison.Ordinal))
                    {
                        installationMatches.Add(columnIndex);
                    }
                }

                if (tagMatches.Count == 0 || descriptionMatches.Count == 0)
                {
                    continue;
                }

                if (tagMatches.Count != 1 || descriptionMatches.Count != 1 || installationMatches.Count > 1)
                {
                    throw new XlsxHeaderDiscoveryException("ambiguous equipment header at row " + row.RowNumber.ToString(CultureInfo.InvariantCulture));
                }

                return new XlsxHeaderDiscoveryResult(row.RowNumber, tagMatches[0], descriptionMatches[0], installationMatches.Count == 1 ? installationMatches[0] : (int?)null);
            }

            throw new XlsxHeaderDiscoveryException("equipment header was not found");
        }

        private static string? Normalize(string? value)
        {
            if (value == null)
            {
                return null;
            }

            string normalized = Regex.Replace(value.Trim(), @"\s+", " ");
            normalized = normalized.ToUpperInvariant().Replace(".", string.Empty).Trim();
            return normalized;
        }
    }
}
