using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using EngiSentry.Contracts;

namespace EngiSentry.CrossDocument
{
    public static class XlsxEquipmentRegisterExtractor
    {
        public static ExternalEquipmentRegister ReadFile(string path)
        {
            IReadOnlyList<XlsxWorksheetInfo> sheets = XlsxWorksheetReader.ListSheets(path);
            var records = new List<ExternalEquipmentRecord>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            int sourceRowCount = 0;

            foreach (XlsxWorksheetInfo sheet in sheets)
            {
                XlsxWorksheetReadResult worksheet = XlsxWorksheetReader.ReadSheet(path, sheet.SheetIndex);
                XlsxHeaderDiscoveryResult header;
                try
                {
                    header = XlsxHeaderDiscovery.Discover(worksheet);
                }
                catch (XlsxHeaderDiscoveryException ex) when (ex.Message == "equipment header was not found")
                {
                    continue;
                }
                catch (XlsxHeaderDiscoveryException ex)
                {
                    throw new ExternalRegisterValidationException("ambiguous equipment header in worksheet " + sheet.SheetName + ": " + ex.Message);
                }

                foreach (XlsxWorksheetRow row in worksheet.Rows.Where(x => x.RowNumber > header.HeaderRowNumber))
                {
                    string? sourceTag = CellAt(row, header.TagColumnIndex);
                    if (string.IsNullOrWhiteSpace(sourceTag))
                    {
                        continue;
                    }

                    EquipmentTagParseResult? parsed;
                    if (!EquipmentTagGrammar.TryParse(sourceTag, out parsed) || parsed == null)
                    {
                        continue;
                    }

                    sourceRowCount++;
                    EquipmentTagExpansionResult expansion = EquipmentTagExpander.Expand(parsed);
                    if (!expansion.Success)
                    {
                        throw new ExternalRegisterValidationException((expansion.FailureReason ?? "InvalidGroupedTag") + ": " + parsed.NormalizedTag);
                    }

                    string? description = BlankToNull(CellAt(row, header.DescriptionColumnIndex));
                    string? installation = header.InstallationColumnIndex.HasValue
                        ? BlankToNull(CellAt(row, header.InstallationColumnIndex.Value))
                        : null;
                    foreach (EquipmentTagExpansionMember member in expansion.Members)
                    {
                        if (!identities.Add(member.NormalizedMemberTag))
                        {
                            throw new ExternalRegisterValidationException("duplicate expanded equipment identity: " + member.NormalizedMemberTag);
                        }

                        records.Add(new ExternalEquipmentRecord
                        {
                            SourceRowNumber = row.RowNumber,
                        SourceTag = sourceTag!,
                            NormalizedTag = member.NormalizedMemberTag,
                            Description = description,
                            Installation = installation,
                            ExpansionMethod = member.ExpansionMethod,
                            SourceSheetIndex = sheet.SheetIndex,
                            SourceSheetName = sheet.SheetName
                        });
                    }
                }
            }

            records = records
                .OrderBy(x => x.NormalizedTag, StringComparer.Ordinal)
                .ThenBy(x => x.SourceSheetIndex)
                .ThenBy(x => x.SourceRowNumber)
                .ToList();

            return new ExternalEquipmentRegister
            {
                SourceFileName = Path.GetFileName(path),
                SourceFullPath = path,
                SourceSha256 = ComputeSha256(path),
                Records = records,
                Statistics = new ExternalEquipmentStatistics
                {
                    SourceRowCount = sourceRowCount,
                    ExpandedEquipmentCount = records.Count
                }
            };
        }

        private static string? CellAt(XlsxWorksheetRow row, int columnIndex)
        {
            return columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null;
        }

        private static string? BlankToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }
    }
}
