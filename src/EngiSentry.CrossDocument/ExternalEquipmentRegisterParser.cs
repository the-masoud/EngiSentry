using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.CrossDocument
{
    public sealed class ExternalRegisterValidationException : Exception
    {
        public ExternalRegisterValidationException(string message) : base(message) { }
    }

    public static class ExternalEquipmentRegisterParser
    {
        public static ExternalEquipmentRegister ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ExternalRegisterValidationException("external register path is empty");
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); } catch (Exception ex) { throw new ExternalRegisterValidationException("external register is unreadable: " + ex.Message); }
            return ParseBytes(Path.GetFileName(path), path, bytes);
        }

        public static ExternalEquipmentRegister ParseBytes(string fileName, string fullPath, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            string text;
            try { text = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { throw new ExternalRegisterValidationException("external register is not valid UTF-8"); }
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            List<CsvRow> rows = ParseCsv(text);
            if (rows.Count == 0 || rows[0].LineNumber != 1 || rows[0].Fields.Count != 3 || rows[0].Fields[0] != "Tag" || rows[0].Fields[1] != "Description" || rows[0].Fields[2] != "Installation")
                throw new ExternalRegisterValidationException("header must be exactly Tag,Description,Installation");
            var records = new List<ExternalEquipmentRecord>();
            int sourceRows = 0;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (CsvRow row in rows.Skip(1))
            {
                if (row.Fields.Count == 1 && string.IsNullOrWhiteSpace(row.Fields[0])) continue;
                if (row.Fields.Count != 3) throw new ExternalRegisterValidationException("row " + row.LineNumber.ToString() + " has the wrong column count");
                string sourceTag = row.Fields[0];
                EquipmentTagParseResult parts;
                try { parts = EquipmentTagGrammar.Parse(sourceTag); }
                catch (FormatException) { throw new ExternalRegisterValidationException("invalid equipment tag: " + (sourceTag ?? string.Empty)); }
                sourceRows++;
                EquipmentTagExpansionResult expansion = EquipmentTagExpander.Expand(parts);
                if (!expansion.Success) throw new ExternalRegisterValidationException((expansion.FailureReason ?? "InvalidGroupedTag") + ": " + parts.NormalizedTag);
                foreach (EquipmentTagExpansionMember member in expansion.Members)
                {
                    string normalized = member.NormalizedMemberTag;
                    if (!identities.Add(normalized)) throw new ExternalRegisterValidationException("duplicate expanded equipment identity: " + normalized);
                    records.Add(new ExternalEquipmentRecord { SourceRowNumber = row.LineNumber, SourceTag = sourceTag, NormalizedTag = normalized, Description = BlankToNull(row.Fields[1]), Installation = BlankToNull(row.Fields[2]), ExpansionMethod = member.ExpansionMethod });
                }
            }
            records = records.OrderBy(x => x.NormalizedTag, StringComparer.Ordinal).ThenBy(x => x.SourceRowNumber).ToList();
            return new ExternalEquipmentRegister { SourceFileName = fileName ?? string.Empty, SourceFullPath = fullPath ?? string.Empty, SourceSha256 = Hex(SHA256.Create().ComputeHash(bytes)), Records = records, Statistics = new ExternalEquipmentStatistics { SourceRowCount = sourceRows, ExpandedEquipmentCount = records.Count } };
        }

        private static string? BlankToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty);

        private static List<CsvRow> ParseCsv(string text)
        {
            var result = new List<CsvRow>(); var fields = new List<string>(); var value = new StringBuilder(); bool quoted = false; bool quoteClosed = false; int line = 1; int rowLine = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; } else { quoted = false; quoteClosed = true; } }
                    else if (c == '\r' || c == '\n') throw new ExternalRegisterValidationException("multiline CSV fields are not supported");
                    else value.Append(c);
                    continue;
                }
                if (quoteClosed)
                {
                    if (c == ',') { fields.Add(value.ToString()); value.Clear(); quoteClosed = false; continue; }
                    if (c == '\r' || c == '\n') { fields.Add(value.ToString()); AddRow(result, rowLine, fields); fields = new List<string>(); value.Clear(); quoteClosed = false; if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; line++; rowLine = line; continue; }
                    if (!char.IsWhiteSpace(c)) throw new ExternalRegisterValidationException("malformed CSV near line " + rowLine.ToString());
                    value.Append(c); continue;
                }
                if (c == '"') { if (value.Length != 0) throw new ExternalRegisterValidationException("malformed CSV near line " + rowLine.ToString()); quoted = true; continue; }
                if (c == ',') { fields.Add(value.ToString()); value.Clear(); continue; }
                if (c == '\r' || c == '\n') { fields.Add(value.ToString()); AddRow(result, rowLine, fields); fields = new List<string>(); value.Clear(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; line++; rowLine = line; continue; }
                value.Append(c);
            }
            if (quoted) throw new ExternalRegisterValidationException("unterminated quoted CSV field");
            if (fields.Count > 0 || value.Length > 0 || quoteClosed) { fields.Add(value.ToString()); AddRow(result, rowLine, fields); }
            return result;
        }
        private static void AddRow(List<CsvRow> rows, int line, List<string> fields) { if (!(fields.Count == 1 && string.IsNullOrWhiteSpace(fields[0]))) rows.Add(new CsvRow { LineNumber = line, Fields = fields }); }
        private sealed class CsvRow { public int LineNumber; public List<string> Fields = new List<string>(); }
    }

}
