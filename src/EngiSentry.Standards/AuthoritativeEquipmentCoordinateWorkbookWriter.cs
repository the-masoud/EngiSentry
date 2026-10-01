using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Generates the single engineer-facing workbook: <c>EngiSentry-Equipment-Coordinates.xlsx</c>.
    ///
    /// The workbook contains a visible <c>Equipment Coordinates</c> sheet (source-document metadata at
    /// the top, then one row per equipment occurrence with blank Easting/Northing/Elevation) and a very
    /// hidden <c>_EngiSentry</c> sheet that binds the workbook to the exact drawing revision and the
    /// exact set of expected occurrences. The engineer opens this one file, fills it in, saves the same
    /// file, and returns it. Nothing here is an answer: EngiSentry never supplies or infers a coordinate.
    ///
    /// The generation is pure file I/O. No drawing is touched and no modal dialog is shown.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateWorkbookWriter
    {
        // Style indices, in the order the stylesheet defines them.
        private const uint StyleLocked = 0;      // read-only cells
        private const uint StyleEditable = 1;    // ES_* metadata values
        private const uint StyleBold = 2;        // title, labels, table header
        private const uint StyleCoordinate = 3;  // Easting / Northing / Elevation

        private const int TitleRow = 1;
        private const int InstructionRow = 2;
        private const int MetadataFirstRow = 4;
        private const int MetadataLastRow = 10;  // seven metadata fields
        private const int HeaderRow = 12;
        private const int DataFirstRow = 13;

        private const string InstructionText =
            "Complete the source-document fields above, then enter Easting and Northing for every row "
            + "(Elevation is optional). Save this same file and return it. Do not add, delete or reorder rows.";

        /// <summary>Writes the workbook to <paramref name="path"/>, creating the directory if needed.</summary>
        public static void Write(string path, AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));
            if (model == null) throw new ArgumentNullException(nameof(model));

            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // OpenXml 3.x requires a readable AND writable stream even when creating a package.
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                Write(stream, model);
        }

        /// <summary>Writes the workbook into memory. Used by the request generator and by focused tests.</summary>
        public static byte[] WriteToBytes(AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            using (var stream = new MemoryStream())
            {
                Write(stream, model);
                return stream.ToArray();
            }
        }

        private static void Write(Stream stream, AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            AuthoritativeEquipmentCoordinateWorkbookDefects defects =
                model.Defects ?? new AuthoritativeEquipmentCoordinateWorkbookDefects();

            int rowCount = model.Rows?.Count ?? 0;
            int dataLastRow = Math.Max(DataFirstRow, HeaderRow + Math.Max(rowCount, 1));

            using (SpreadsheetDocument document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = BuildStylesheet();
                stylesPart.Stylesheet.Save();

                var sheets = new Sheets();

                WorksheetPart? visiblePart = null;
                if (!defects.OmitVisibleSheet)
                {
                    visiblePart = workbookPart.AddNewPart<WorksheetPart>();
                    visiblePart.Worksheet = BuildVisibleSheet(model, dataLastRow, defects);
                    visiblePart.Worksheet.Save();

                    if (!defects.OmitTable)
                    {
                        TableDefinitionPart tablePart = visiblePart.AddNewPart<TableDefinitionPart>();
                        tablePart.Table = BuildTable(dataLastRow);
                        tablePart.Table.Save();
                        visiblePart.Worksheet.AppendChild(new TableParts(
                            new TablePart { Id = visiblePart.GetIdOfPart(tablePart) }) { Count = 1U });
                        visiblePart.Worksheet.Save();
                    }

                    var visibleSheet = new Sheet
                    {
                        Name = AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName,
                        SheetId = 1U,
                        Id = workbookPart.GetIdOfPart(visiblePart)
                    };
                    if (defects.HideVisibleSheet) visibleSheet.State = SheetStateValues.Hidden;
                    sheets.Append(visibleSheet);
                }

                if (!defects.OmitInternalSheet)
                {
                    WorksheetPart internalPart = workbookPart.AddNewPart<WorksheetPart>();
                    internalPart.Worksheet = BuildInternalSheet(model);
                    internalPart.Worksheet.Save();
                    sheets.Append(new Sheet
                    {
                        Name = AuthoritativeEquipmentCoordinateWorkbookSchema.InternalSheetName,
                        SheetId = 2U,
                        State = SheetStateValues.VeryHidden,
                        Id = workbookPart.GetIdOfPart(internalPart)
                    });
                }

                workbookPart.Workbook.AppendChild(sheets);

                if (!defects.OmitNamedMetadata)
                    workbookPart.Workbook.AppendChild(BuildDefinedNames());

                workbookPart.Workbook.Save();
            }
        }

        // ---------------------------------------------------------------- visible sheet

        private static Worksheet BuildVisibleSheet(
            AuthoritativeEquipmentCoordinateWorkbookModel model, int dataLastRow,
            AuthoritativeEquipmentCoordinateWorkbookDefects defects)
        {
            var worksheet = new Worksheet();

            var sheetViews = new SheetViews();
            var sheetView = new SheetView { WorkbookViewId = 0U, TabSelected = true };
            sheetView.AppendChild(new Pane
            {
                VerticalSplit = HeaderRow,
                TopLeftCell = "A" + DataFirstRow.ToString(CultureInfo.InvariantCulture),
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            });
            sheetView.AppendChild(new Selection
            {
                Pane = PaneValues.BottomLeft,
                ActiveCell = "A" + DataFirstRow.ToString(CultureInfo.InvariantCulture),
                ActiveCellId = 0U
            });
            sheetViews.AppendChild(sheetView);
            worksheet.AppendChild(sheetViews);

            var columns = new Columns();
            columns.AppendChild(new Column { Min = 1U, Max = 1U, Width = 6D, CustomWidth = true });
            columns.AppendChild(new Column { Min = 2U, Max = 2U, Width = 22D, CustomWidth = true });
            columns.AppendChild(new Column { Min = 3U, Max = 3U, Width = 30D, CustomWidth = true });
            columns.AppendChild(new Column { Min = 4U, Max = 5U, Width = 20D, CustomWidth = true });
            columns.AppendChild(new Column { Min = 6U, Max = 6U, Width = 14D, CustomWidth = true });
            // The two row-identity columns are hidden, not deleted: they must travel with the row.
            columns.AppendChild(new Column { Min = 7U, Max = 8U, Width = 0D, Hidden = true, CustomWidth = true });
            worksheet.AppendChild(columns);

            var sheetData = new SheetData();

            sheetData.AppendChild(TextRow(TitleRow, StyleBold,
                "EngiSentry Equipment Coordinate Request"));
            sheetData.AppendChild(TextRow(InstructionRow, StyleLocked, InstructionText));

            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 0, "Project Identifier", model.ProjectIdentifier));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 1, "Source Document Number", model.SourceDocumentNumber));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 2, "Source Revision", model.SourceRevision));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 3, "Source Issue Status", model.SourceIssueStatus));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 4, "Issued By", model.IssuedBy));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 5, "Issue Date (YYYY-MM-DD)", model.IssueDate));
            sheetData.AppendChild(MetadataRow(MetadataFirstRow + 6, "Notes (optional)", model.Notes));

            var headerCells = new List<Cell>
            {
                TextCell(Reference(1, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNumber, StyleBold),
                TextCell(Reference(2, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEquipmentTag, StyleBold),
                TextCell(Reference(3, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnDescription, StyleBold),
                TextCell(Reference(4, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEasting, StyleBold),
                TextCell(Reference(5, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNorthing, StyleBold),
                TextCell(Reference(6, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnElevation, StyleBold),
                TextCell(Reference(7, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnOccurrenceId, StyleBold),
                TextCell(Reference(8, HeaderRow), AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnSourceEntityHandle, StyleBold)
            };
            var headerRow = new Row { RowIndex = (uint)HeaderRow };
            foreach (Cell cell in headerCells) headerRow.AppendChild(cell);
            sheetData.AppendChild(headerRow);

            List<AuthoritativeEquipmentCoordinateWorkbookRow> rows =
                model.Rows ?? new List<AuthoritativeEquipmentCoordinateWorkbookRow>();

            for (int index = 0; index < Math.Max(rows.Count, 1); index++)
            {
                int excelRow = DataFirstRow + index;
                AuthoritativeEquipmentCoordinateWorkbookRow row = index < rows.Count
                    ? rows[index]
                    : new AuthoritativeEquipmentCoordinateWorkbookRow();

                var dataRow = new Row { RowIndex = (uint)excelRow };
                dataRow.AppendChild(NumberOrBlankCell(Reference(1, excelRow), row.Number, StyleLocked));
                dataRow.AppendChild(TextCell(Reference(2, excelRow), row.EquipmentTag, StyleLocked));
                dataRow.AppendChild(TextCell(Reference(3, excelRow), row.Description, StyleLocked));
                dataRow.AppendChild(CoordinateCell(Reference(4, excelRow), row.Easting, row.EastingKind));
                dataRow.AppendChild(CoordinateCell(Reference(5, excelRow), row.Northing, row.NorthingKind));
                dataRow.AppendChild(CoordinateCell(Reference(6, excelRow), row.Elevation, row.ElevationKind));
                dataRow.AppendChild(TextCell(Reference(7, excelRow), row.OccurrenceId, StyleLocked));
                dataRow.AppendChild(TextCell(Reference(8, excelRow), row.SourceEntityHandle, StyleLocked));
                sheetData.AppendChild(dataRow);
            }

            worksheet.AppendChild(sheetData);

            // Protection exists only to reduce accidental edits. It is not a security boundary, and the
            // importer re-validates everything: nothing in the workbook is trusted because it is locked.
            worksheet.AppendChild(new SheetProtection { Sheet = true, Objects = true, Scenarios = true });

            return worksheet;
        }

        private static DefinedNames BuildDefinedNames()
        {
            var definedNames = new DefinedNames();
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameProjectIdentifier, MetadataFirstRow + 0);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceDocumentNumber, MetadataFirstRow + 1);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceRevision, MetadataFirstRow + 2);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameSourceIssueStatus, MetadataFirstRow + 3);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameIssuedBy, MetadataFirstRow + 4);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameIssueDate, MetadataFirstRow + 5);
            AppendDefinedName(definedNames, AuthoritativeEquipmentCoordinateWorkbookSchema.NameNotes, MetadataFirstRow + 6);
            return definedNames;
        }

        private static void AppendDefinedName(DefinedNames definedNames, string name, int row)
        {
            definedNames.AppendChild(new DefinedName(
                "'" + AuthoritativeEquipmentCoordinateWorkbookSchema.VisibleSheetName + "'!$B$" + row.ToString(CultureInfo.InvariantCulture))
            { Name = name });
        }

        private static Table BuildTable(int dataLastRow)
        {
            string reference = "A" + HeaderRow.ToString(CultureInfo.InvariantCulture)
                + ":H" + dataLastRow.ToString(CultureInfo.InvariantCulture);

            var table = new Table
            {
                Id = 1U,
                Name = AuthoritativeEquipmentCoordinateWorkbookSchema.TableName,
                DisplayName = AuthoritativeEquipmentCoordinateWorkbookSchema.TableName,
                Reference = reference
            };
            table.AppendChild(new AutoFilter { Reference = reference });

            var columns = new TableColumns { Count = 8U };
            string[] names =
            {
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNumber,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEquipmentTag,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnDescription,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnEasting,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnNorthing,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnElevation,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnOccurrenceId,
                AuthoritativeEquipmentCoordinateWorkbookSchema.ColumnSourceEntityHandle
            };
            for (int i = 0; i < names.Length; i++)
                columns.AppendChild(new TableColumn { Id = (uint)(i + 1), Name = names[i] });
            table.AppendChild(columns);

            table.AppendChild(new TableStyleInfo
            {
                Name = "TableStyleLight1",
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = true,
                ShowColumnStripes = false
            });
            return table;
        }

        // ---------------------------------------------------------------- internal sheet

        private static Worksheet BuildInternalSheet(AuthoritativeEquipmentCoordinateWorkbookModel model)
        {
            var worksheet = new Worksheet();
            var sheetData = new SheetData();

            sheetData.AppendChild(KeyValueRow(1, "Key", "Value"));
            sheetData.AppendChild(KeyValueRow(2, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeySchemaVersion, model.SchemaVersion));
            sheetData.AppendChild(KeyValueRow(3, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyDrawingFileName, model.DrawingFileName));
            sheetData.AppendChild(KeyValueRow(4, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyDrawingSha256, model.DrawingSha256));
            sheetData.AppendChild(KeyValueRow(5, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyGeneratedUtc, model.GeneratedUtc));
            sheetData.AppendChild(KeyValueRow(6, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyExpectedOccurrenceCount,
                model.ExpectedOccurrenceCount.ToString(CultureInfo.InvariantCulture)));
            sheetData.AppendChild(KeyValueRow(7, AuthoritativeEquipmentCoordinateWorkbookSchema.InternalKeyRequestFingerprint, model.RequestFingerprint));

            worksheet.AppendChild(sheetData);
            worksheet.AppendChild(new SheetProtection { Sheet = true, Objects = true, Scenarios = true });
            return worksheet;
        }

        private static Row KeyValueRow(int rowIndex, string key, string value)
        {
            var row = new Row { RowIndex = (uint)rowIndex };
            row.AppendChild(TextCell(Reference(1, rowIndex), key, StyleBold));
            row.AppendChild(TextCell(Reference(2, rowIndex), value, StyleLocked));
            return row;
        }

        // ---------------------------------------------------------------- cell helpers

        private static Row TextRow(int rowIndex, uint style, string text)
        {
            var row = new Row { RowIndex = (uint)rowIndex };
            row.AppendChild(TextCell(Reference(1, rowIndex), text, style));
            return row;
        }

        /// <summary>
        /// One spreadsheet row holding both the label (column A, locked) and the value (column B,
        /// editable). A row index may only appear once in a worksheet, so the label and value must share
        /// one Row element — emitting two Row elements with the same index makes the file unreadable.
        /// </summary>
        private static Row MetadataRow(int rowIndex, string label, string value)
        {
            var row = new Row { RowIndex = (uint)rowIndex };
            row.AppendChild(TextCell(Reference(1, rowIndex), label, StyleBold));
            row.AppendChild(TextCell(Reference(2, rowIndex), value, StyleEditable));
            return row;
        }

        private static Cell TextCell(string reference, string? text, uint style)
        {
            var cell = new Cell { CellReference = reference, StyleIndex = style, DataType = CellValues.InlineString };
            cell.AppendChild(new InlineString(new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));
            return cell;
        }

        private static Cell NumberCell(string reference, string raw, uint style)
        {
            var cell = new Cell { CellReference = reference, StyleIndex = style, DataType = CellValues.Number };
            cell.AppendChild(new CellValue(raw));
            return cell;
        }

        private static Cell NumberOrBlankCell(string reference, int number, uint style)
            => number <= 0 ? BlankCell(reference, style) : NumberCell(reference, number.ToString(CultureInfo.InvariantCulture), style);

        private static Cell BlankCell(string reference, uint style) => new Cell { CellReference = reference, StyleIndex = style };

        private static Cell CoordinateCell(string reference, string? raw, AuthoritativeEquipmentCoordinateCellKind kind)
        {
            string text = raw ?? string.Empty;
            AuthoritativeEquipmentCoordinateCellKind effective = kind == AuthoritativeEquipmentCoordinateCellKind.Auto
                ? (text.Length == 0 ? AuthoritativeEquipmentCoordinateCellKind.Blank : AuthoritativeEquipmentCoordinateCellKind.Number)
                : kind;

            switch (effective)
            {
                case AuthoritativeEquipmentCoordinateCellKind.Blank:
                    return BlankCell(reference, StyleCoordinate);
                case AuthoritativeEquipmentCoordinateCellKind.Text:
                    return TextCell(reference, text, StyleCoordinate);
                case AuthoritativeEquipmentCoordinateCellKind.Formula:
                    {
                        var cell = new Cell { CellReference = reference, StyleIndex = StyleCoordinate, DataType = CellValues.Number };
                        cell.AppendChild(new CellFormula("1+1"));
                        cell.AppendChild(new CellValue("2"));
                        return cell;
                    }
                case AuthoritativeEquipmentCoordinateCellKind.Error:
                    {
                        var cell = new Cell { CellReference = reference, StyleIndex = StyleCoordinate, DataType = CellValues.Error };
                        cell.AppendChild(new CellValue("#VALUE!"));
                        return cell;
                    }
                default:
                    return NumberCell(reference, text, StyleCoordinate);
            }
        }

        private static string Reference(int column, int row)
            => ColumnName(column - 1) + row.ToString(CultureInfo.InvariantCulture);

        private static string ColumnName(int zeroBased)
        {
            int value = zeroBased + 1;
            var chars = new List<char>();
            while (value > 0)
            {
                int remainder = (value - 1) % 26;
                chars.Insert(0, (char)('A' + remainder));
                value = (value - 1) / 26;
            }
            return new string(chars.ToArray());
        }

        // ---------------------------------------------------------------- stylesheet

        private static Stylesheet BuildStylesheet()
        {
            var numberingFormats = new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164U, FormatCode = "0.############" })
            { Count = 1U };

            var fonts = new Fonts(
                new Font(),
                new Font(new Bold()))
            { Count = 2U };

            var fills = new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
            { Count = 2U };

            var borders = new Borders(new Border()) { Count = 1U };

            var cellStyleFormats = new CellStyleFormats(new CellFormat()) { Count = 1U };

            var cellFormats = new CellFormats(
                new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U },                                             // 0 locked
                new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U, ApplyProtection = true,
                                 Protection = new Protection { Locked = false } },                                     // 1 editable
                new CellFormat { FontId = 1U, FillId = 0U, BorderId = 0U, ApplyFont = true },                          // 2 bold
                new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U, NumberFormatId = 164U,
                                 ApplyNumberFormat = true, ApplyProtection = true,
                                 Protection = new Protection { Locked = false } })                                     // 3 coordinate
            { Count = 4U };

            var cellStyles = new CellStyles(
                new CellStyle { Name = "Normal", FormatId = 0U, BuiltinId = 0U })
            { Count = 1U };

            return new Stylesheet(numberingFormats, fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
        }

        /// <summary>UTF-8 without BOM, used wherever a text payload must be byte-stable.</summary>
        internal static Encoding Utf8NoBom { get; } = new UTF8Encoding(false);
    }
}
