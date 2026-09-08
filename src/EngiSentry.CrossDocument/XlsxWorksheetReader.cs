using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;
using System.Xml;
using ExcelDataReader;

namespace EngiSentry.CrossDocument
{
    public sealed class XlsxWorkbookValidationException : Exception
    {
        public XlsxWorkbookValidationException(string message) : base(message) { }
    }

    public sealed class XlsxWorksheetRow
    {
        public XlsxWorksheetRow(int rowNumber, IReadOnlyList<string?> cells)
        {
            RowNumber = rowNumber;
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public int RowNumber { get; }
        public IReadOnlyList<string?> Cells { get; }
    }

    public sealed class XlsxWorksheetReadResult
    {
        public XlsxWorksheetReadResult(int sheetIndex, string sheetName, IReadOnlyList<XlsxWorksheetRow> rows)
        {
            SheetIndex = sheetIndex;
            SheetName = sheetName ?? throw new ArgumentNullException(nameof(sheetName));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        public int SheetIndex { get; }
        public string SheetName { get; }
        public IReadOnlyList<XlsxWorksheetRow> Rows { get; }
    }

    public sealed class XlsxWorksheetInfo
    {
        public XlsxWorksheetInfo(int sheetIndex, string sheetName)
        {
            SheetIndex = sheetIndex;
            SheetName = sheetName ?? throw new ArgumentNullException(nameof(sheetName));
        }

        public int SheetIndex { get; }
        public string SheetName { get; }
    }

    public static class XlsxWorksheetReader
    {
        public static IReadOnlyList<XlsxWorksheetInfo> ListSheets(string path)
        {
            ValidatePath(path);

            FileStream stream = OpenReadStream(path);
            using (stream)
            {
                try
                {
                    using (IExcelDataReader reader = ExcelReaderFactory.CreateOpenXmlReader(stream))
                    {
                        var sheets = new List<XlsxWorksheetInfo>();
                        int sheetIndex = 0;
                        do
                        {
                            sheets.Add(new XlsxWorksheetInfo(sheetIndex, reader.Name));
                            sheetIndex++;
                        }
                        while (reader.NextResult());

                        return sheets;
                    }
                }
                catch (Exception ex) when (IsWorkbookFormatException(ex))
                {
                    throw new XlsxWorkbookValidationException("xlsx workbook is invalid or unsupported: " + ex.Message);
                }
            }
        }

        public static XlsxWorksheetReadResult ReadSheet(string path, int sheetIndex)
        {
            ValidatePath(path);

            if (sheetIndex < 0)
            {
                throw new XlsxWorkbookValidationException("sheet index must be non-negative");
            }

            FileStream stream = OpenReadStream(path);
            using (stream)
            {
                try
                {
                    using (IExcelDataReader reader = ExcelReaderFactory.CreateOpenXmlReader(stream))
                    {
                        int currentSheetIndex = 0;
                        while (currentSheetIndex < sheetIndex && reader.NextResult())
                        {
                            currentSheetIndex++;
                        }

                        if (currentSheetIndex != sheetIndex)
                        {
                            throw new XlsxWorkbookValidationException("worksheet index " + sheetIndex.ToString(CultureInfo.InvariantCulture) + " was not found");
                        }

                        var rows = new List<XlsxWorksheetRow>();
                        int rowNumber = 0;
                        while (reader.Read())
                        {
                            rowNumber++;
                            var cells = new List<string?>(reader.FieldCount);
                            for (int columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
                            {
                                cells.Add(ToInvariantText(reader.GetValue(columnIndex)));
                            }

                            rows.Add(new XlsxWorksheetRow(rowNumber, cells));
                        }

                        return new XlsxWorksheetReadResult(sheetIndex, reader.Name, rows);
                    }
                }
                catch (XlsxWorkbookValidationException)
                {
                    throw;
                }
                catch (Exception ex) when (IsWorkbookFormatException(ex))
                {
                    throw new XlsxWorkbookValidationException("xlsx workbook is invalid or unsupported: " + ex.Message);
                }
            }
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new XlsxWorkbookValidationException("xlsx path is empty");
            }

            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new XlsxWorkbookValidationException("only .xlsx workbooks are supported");
            }
        }

        private static FileStream OpenReadStream(string path)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is ArgumentException)
            {
                throw new XlsxWorkbookValidationException("xlsx workbook is unreadable: " + ex.Message);
            }
        }

        private static string? ToInvariantText(object? value)
        {
            if (value == null)
            {
                return null;
            }

            if (value is string text)
            {
                return text;
            }

            if (value is bool boolean)
            {
                return boolean ? "True" : "False";
            }

            if (value is DateTime dateTime)
            {
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool IsWorkbookFormatException(Exception exception)
        {
            return exception is InvalidDataException ||
                   exception is XmlException ||
                   exception is FormatException ||
                   exception is NotSupportedException ||
                   string.Equals(exception.GetType().FullName, "ExcelDataReader.Exceptions.HeaderException", StringComparison.Ordinal);
        }
    }
}
