using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;
using EngiSentry.CrossDocument;
using EngiSentry.Rules;

namespace EngiSentry.Tests
{
    internal static class CrossDocumentTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static string FixturePath(string fixtureName)
        {
            DirectoryInfo? directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "EngiSentry.sln")))
                {
                    return Path.Combine(directory.FullName, "tests", "fixtures", fixtureName);
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root containing EngiSentry.sln was not found.");
        }

        private static void AssertWorkbookValidation(Action action, string expectedMessagePrefix)
        {
            try
            {
                action();
            }
            catch (XlsxWorkbookValidationException ex)
            {
                Assert(ex.Message.StartsWith(expectedMessagePrefix, StringComparison.Ordinal), "Unexpected XLSX validation message: " + ex.Message);
                return;
            }

            throw new InvalidOperationException("Expected XLSX validation exception: " + expectedMessagePrefix);
        }

        private static void AssertHeaderDiscovery(Action action, string expectedMessage)
        {
            try
            {
                action();
            }
            catch (XlsxHeaderDiscoveryException ex)
            {
                Assert(ex.Message == expectedMessage || ex.Message.StartsWith(expectedMessage, StringComparison.Ordinal), "Unexpected header discovery message: " + ex.Message);
                return;
            }

            throw new InvalidOperationException("Expected header discovery exception: " + expectedMessage);
        }

        private static string FileHash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static void AssertExternalRegisterValidation(Action action, string expectedMessage)
        {
            try
            {
                action();
            }
            catch (ExternalRegisterValidationException ex)
            {
                Assert(ex.Message == expectedMessage, "Unexpected external register validation message: " + ex.Message);
                return;
            }

            throw new InvalidOperationException("Expected external register validation exception: " + expectedMessage);
        }

        public static void Run()
        {
            string fixturePath = FixturePath("p2.1-reader.xlsx");
            string fixtureHashBefore;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fixtureHashBefore = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            XlsxWorksheetReadResult worksheet = XlsxWorksheetReader.ReadSheet(fixturePath, 0);
            Assert(worksheet.SheetIndex == 0 && worksheet.SheetName == "ReaderSmoke", "XLSX sheet selection failed.");
            Assert(worksheet.Rows.Count >= 3, "XLSX reader did not return fixture rows.");
            Assert(worksheet.Rows[0].RowNumber == 1 && worksheet.Rows[0].Cells[0] == "Tag" && worksheet.Rows[0].Cells[1] == "Description" && worksheet.Rows[0].Cells[2] == "Installation", "XLSX header row was not preserved.");
            Assert(worksheet.Rows[1].RowNumber == 2 && worksheet.Rows[1].Cells[0] == "P-0201" && worksheet.Rows[1].Cells[1] == "Pump" && worksheet.Rows[1].Cells[2] == "EL+10", "XLSX string row was not preserved.");
            Assert(worksheet.Rows[2].RowNumber == 3 && worksheet.Rows[2].Cells[0] == "E-0201" && worksheet.Rows[2].Cells[1] == null && worksheet.Rows[2].Cells[2] == "123.5", "XLSX blank or numeric cell position was not preserved.");

            string fixtureHashAfter;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(fixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fixtureHashAfter = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            Assert(fixtureHashBefore == fixtureHashAfter, "XLSX reader modified the fixture.");

            string sheetFixturePath = FixturePath("p2.2-sheets.xlsx");
            string sheetFixtureHashBefore;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(sheetFixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                sheetFixtureHashBefore = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            var discoveredSheets = XlsxWorksheetReader.ListSheets(sheetFixturePath);
            Assert(discoveredSheets.Count == 3 && discoveredSheets[0].SheetIndex == 0 && discoveredSheets[0].SheetName == "Alpha" && discoveredSheets[1].SheetIndex == 1 && discoveredSheets[1].SheetName == "Beta Sheet" && discoveredSheets[2].SheetIndex == 2 && discoveredSheets[2].SheetName == "Gamma", "XLSX worksheet discovery did not preserve order or names.");

            string sheetFixtureHashAfter;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(sheetFixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                sheetFixtureHashAfter = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            Assert(sheetFixtureHashBefore == sheetFixtureHashAfter, "XLSX worksheet discovery modified the fixture.");
            var rediscoveredSheets = XlsxWorksheetReader.ListSheets(sheetFixturePath);
            Assert(discoveredSheets.Select(x => x.SheetIndex + "|" + x.SheetName).SequenceEqual(rediscoveredSheets.Select(x => x.SheetIndex + "|" + x.SheetName)), "XLSX worksheet discovery was not deterministic.");
            AssertWorkbookValidation(() => XlsxWorksheetReader.ListSheets("   "), "xlsx path is empty");
            AssertWorkbookValidation(() => XlsxWorksheetReader.ListSheets(Path.ChangeExtension(sheetFixturePath, ".csv")), "only .xlsx workbooks are supported");

            string headerFixturePath = FixturePath("p2.3-headers.xlsx");
            string headerFixtureHashBefore;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(headerFixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                headerFixtureHashBefore = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            XlsxHeaderDiscoveryResult engineeringHeader = XlsxHeaderDiscovery.Discover(XlsxWorksheetReader.ReadSheet(headerFixturePath, 0));
            Assert(engineeringHeader.HeaderRowNumber == 5 && engineeringHeader.TagColumnIndex == 1 && engineeringHeader.DescriptionColumnIndex == 4 && engineeringHeader.InstallationColumnIndex == null, "Engineering header discovery failed.");
            XlsxHeaderDiscoveryResult canonicalHeader = XlsxHeaderDiscovery.Discover(XlsxWorksheetReader.ReadSheet(headerFixturePath, 1));
            Assert(canonicalHeader.HeaderRowNumber == 1 && canonicalHeader.TagColumnIndex == 0 && canonicalHeader.DescriptionColumnIndex == 2 && canonicalHeader.InstallationColumnIndex == 5, "Canonical header discovery failed.");
            AssertHeaderDiscovery(() => XlsxHeaderDiscovery.Discover(XlsxWorksheetReader.ReadSheet(headerFixturePath, 2)), "ambiguous equipment header at row 1");
            AssertHeaderDiscovery(() => XlsxHeaderDiscovery.Discover(XlsxWorksheetReader.ReadSheet(headerFixturePath, 3)), "equipment header was not found");
            XlsxHeaderDiscoveryResult engineeringHeaderSecond = XlsxHeaderDiscovery.Discover(XlsxWorksheetReader.ReadSheet(headerFixturePath, 0));
            Assert(engineeringHeader.HeaderRowNumber == engineeringHeaderSecond.HeaderRowNumber && engineeringHeader.TagColumnIndex == engineeringHeaderSecond.TagColumnIndex && engineeringHeader.DescriptionColumnIndex == engineeringHeaderSecond.DescriptionColumnIndex && engineeringHeader.InstallationColumnIndex == engineeringHeaderSecond.InstallationColumnIndex, "Header discovery was not deterministic.");

            string headerFixtureHashAfter;
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = new FileStream(headerFixturePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                headerFixtureHashAfter = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
            }

            Assert(headerFixtureHashBefore == headerFixtureHashAfter, "Header discovery modified the fixture.");

            string xlsxRegisterPath = FixturePath("p2.4-register.xlsx");
            string xlsxDuplicatePath = FixturePath("p2.4-duplicate.xlsx");
            string xlsxRegisterHashBefore = FileHash(xlsxRegisterPath);
            string xlsxDuplicateHashBefore = FileHash(xlsxDuplicatePath);
            ExternalEquipmentRegister xlsxRegister = XlsxEquipmentRegisterExtractor.ReadFile(xlsxRegisterPath);
            Assert(xlsxRegister.Statistics.SourceRowCount == 3 && xlsxRegister.Statistics.ExpandedEquipmentCount == 6 && xlsxRegister.Records.Count == 6, "XLSX register extraction statistics failed.");
            Assert(xlsxRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "E-0201A", "E-0201B", "E-0201C", "P-0201A", "P-0201B", "V-0201" }), "XLSX register deterministic order failed.");
            ExternalEquipmentRecord pumpA = xlsxRegister.Records.Single(x => x.NormalizedTag == "P-0201A");
            Assert(pumpA.SourceSheetIndex == 1 && pumpA.SourceSheetName == "Pumps" && pumpA.SourceRowNumber == 4 && pumpA.SourceTag == "P-0201 A/B" && pumpA.Description == "Feed Pumps" && pumpA.Installation == "UNIT-02" && pumpA.ExpansionMethod == "GroupedSuffixExpansion", "XLSX pump provenance failed.");
            Assert(xlsxRegister.Records.Single(x => x.NormalizedTag == "P-0201B").SourceSheetIndex == 1, "XLSX grouped pump provenance failed.");
            ExternalEquipmentRecord vessel = xlsxRegister.Records.Single(x => x.NormalizedTag == "V-0201");
            Assert(vessel.SourceSheetIndex == 1 && vessel.SourceSheetName == "Pumps" && vessel.SourceRowNumber == 6 && vessel.Installation == null && vessel.ExpansionMethod == "Direct", "XLSX whitespace installation handling failed.");
            Assert(xlsxRegister.Records.Where(x => x.NormalizedTag.StartsWith("E-0201", StringComparison.Ordinal)).All(x => x.SourceSheetIndex == 2 && x.SourceSheetName == "Exchangers" && x.SourceRowNumber == 5 && x.SourceTag == "E-0201 A~C" && x.Description == "Feed/Effluent Exchangers" && x.Installation == null && x.ExpansionMethod == "AlphabeticRangeExpansion"), "XLSX range provenance failed.");
            Assert(!xlsxRegister.Records.Any(x => x.SourceTag == "Notes:"), "Non-equipment XLSX row created a record.");
            Assert(xlsxRegister.SourceSha256 == FileHash(xlsxRegisterPath), "XLSX register source hash failed.");
            bool xlsxDuplicateRejected = false;
            try { XlsxEquipmentRegisterExtractor.ReadFile(xlsxDuplicatePath); } catch (ExternalRegisterValidationException ex) { xlsxDuplicateRejected = ex.Message == "duplicate expanded equipment identity: P-0201A"; }
            Assert(xlsxDuplicateRejected, "Workbook-global XLSX duplicate was accepted.");
            Assert(xlsxRegisterHashBefore == FileHash(xlsxRegisterPath) && xlsxDuplicateHashBefore == FileHash(xlsxDuplicatePath), "XLSX extraction modified a fixture.");

            ExternalEquipmentRegister loaderXlsxRegister = ExternalEquipmentRegisterLoader.ReadFile(xlsxRegisterPath);
            Assert(loaderXlsxRegister.Statistics.SourceRowCount == 3 && loaderXlsxRegister.Statistics.ExpandedEquipmentCount == 6 && loaderXlsxRegister.Records.Count == 6, "XLSX loader dispatch statistics failed.");
            Assert(loaderXlsxRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "E-0201A", "E-0201B", "E-0201C", "P-0201A", "P-0201B", "V-0201" }), "XLSX loader dispatch ordering failed.");

            string loaderCsvPath = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllText(loaderCsvPath, "Tag,Description,Installation\nP-0201 A/B,Feed Pumps,UNIT-02\n", new UTF8Encoding(false));
                ExternalEquipmentRegister loaderCsvRegister = ExternalEquipmentRegisterLoader.ReadFile(loaderCsvPath);
                Assert(loaderCsvRegister.Statistics.SourceRowCount == 1 && loaderCsvRegister.Statistics.ExpandedEquipmentCount == 2 && loaderCsvRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "P-0201A", "P-0201B" }), "CSV loader dispatch failed.");
                Assert(loaderCsvRegister.Records.All(x => x.SourceSheetIndex == null && x.SourceSheetName == null), "CSV loader sheet provenance changed.");
            }
            finally
            {
                if (File.Exists(loaderCsvPath)) File.Delete(loaderCsvPath);
            }

            AssertExternalRegisterValidation(() => ExternalEquipmentRegisterLoader.ReadFile(Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5.txt")), "unsupported equipment register file type; supported types are .xlsx and .csv");

            string loaderCorruptPath = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                File.WriteAllBytes(loaderCorruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
                try
                {
                    ExternalEquipmentRegisterLoader.ReadFile(loaderCorruptPath);
                    throw new InvalidOperationException("Corrupt XLSX loader input was accepted.");
                }
                catch (ExternalRegisterValidationException ex)
                {
                    Assert(ex.Message.StartsWith("xlsx workbook is invalid or unsupported:", StringComparison.Ordinal), "XLSX loader validation was not unified.");
                }
            }
            finally
            {
                if (File.Exists(loaderCorruptPath)) File.Delete(loaderCorruptPath);
            }

            const string noRegisterMessage = "No Equipment Register is linked to this drawing/project. ES_CROSSQA will not use a global or test register because that could create false findings. Add a project register under <drawing folder>\\EngiSentry\\active-equipment-register.xlsx or .csv, a drawing-specific <drawing name>.equipment-register.xlsx or .csv, or <drawing folder>\\active-equipment-register.xlsx or .csv.";
            string precedenceDirectory = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5-" + Guid.NewGuid().ToString("N"));
            try
            {
                string projectDirectory = Path.Combine(precedenceDirectory, "EngiSentry");
                Directory.CreateDirectory(projectDirectory);
                string drawingPath = Path.Combine(precedenceDirectory, "Area-A.dwg");
                string[] precedenceCandidates =
                {
                    Path.Combine(projectDirectory, "active-equipment-register.xlsx"),
                    Path.Combine(projectDirectory, "active-equipment-register.csv"),
                    Path.Combine(precedenceDirectory, "Area-A.equipment-register.xlsx"),
                    Path.Combine(precedenceDirectory, "Area-A.equipment-register.csv"),
                    Path.Combine(precedenceDirectory, "active-equipment-register.xlsx"),
                    Path.Combine(precedenceDirectory, "active-equipment-register.csv")
                };
                foreach (string candidate in precedenceCandidates) File.WriteAllBytes(candidate, Array.Empty<byte>());
                foreach (string candidate in precedenceCandidates)
                {
                    Assert(string.Equals(ExternalEquipmentRegisterLoader.ResolveAutomaticPath(drawingPath), candidate, StringComparison.Ordinal), "Automatic equipment register precedence failed.");
                    File.Delete(candidate);
                }
                AssertExternalRegisterValidation(() => ExternalEquipmentRegisterLoader.ResolveAutomaticPath(drawingPath), noRegisterMessage);
            }
            finally
            {
                if (Directory.Exists(precedenceDirectory)) Directory.Delete(precedenceDirectory, true);
            }

            string endToEndDirectory = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5-" + Guid.NewGuid().ToString("N"));
            try
            {
                string endToEndProjectDirectory = Path.Combine(endToEndDirectory, "EngiSentry");
                Directory.CreateDirectory(endToEndProjectDirectory);
                string endToEndXlsxPath = Path.Combine(endToEndProjectDirectory, "active-equipment-register.xlsx");
                File.Copy(xlsxRegisterPath, endToEndXlsxPath);
                string resolvedEndToEndPath = ExternalEquipmentRegisterLoader.ResolveAutomaticPath(Path.Combine(endToEndDirectory, "Unit02.dwg"));
                ExternalEquipmentRegister endToEndRegister = ExternalEquipmentRegisterLoader.ReadFile(resolvedEndToEndPath);
                Assert(string.Equals(resolvedEndToEndPath, endToEndXlsxPath, StringComparison.Ordinal) && endToEndRegister.Statistics.SourceRowCount == 3 && endToEndRegister.Statistics.ExpandedEquipmentCount == 6, "End-to-end XLSX resolution and load failed.");
            }
            finally
            {
                if (Directory.Exists(endToEndDirectory)) Directory.Delete(endToEndDirectory, true);
            }

            string emptyDirectory = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.5-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(emptyDirectory);
                AssertExternalRegisterValidation(() => ExternalEquipmentRegisterLoader.ResolveAutomaticPath(Path.Combine(emptyDirectory, "NoRegister.dwg")), noRegisterMessage);
            }
            finally
            {
                if (Directory.Exists(emptyDirectory)) Directory.Delete(emptyDirectory, true);
            }

            AssertWorkbookValidation(() => XlsxWorksheetReader.ReadSheet("   ", 0), "xlsx path is empty");
            AssertWorkbookValidation(() => XlsxWorksheetReader.ReadSheet(Path.ChangeExtension(fixturePath, ".csv"), 0), "only .xlsx workbooks are supported");
            AssertWorkbookValidation(() => XlsxWorksheetReader.ReadSheet(fixturePath, -1), "sheet index must be non-negative");
            AssertWorkbookValidation(() => XlsxWorksheetReader.ReadSheet(fixturePath, 1), "worksheet index 1 was not found");

            string corruptPath = Path.Combine(Path.GetTempPath(), "EngiSentry-p2.1-" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                File.WriteAllBytes(corruptPath, new byte[] { 0x00, 0x01, 0x02, 0x03 });
                AssertWorkbookValidation(() => XlsxWorksheetReader.ReadSheet(corruptPath, 0), "xlsx workbook is invalid or unsupported:");
                AssertWorkbookValidation(() => XlsxWorksheetReader.ListSheets(corruptPath), "xlsx workbook is invalid or unsupported:");
            }
            finally
            {
                if (File.Exists(corruptPath))
                {
                    File.Delete(corruptPath);
                }
            }

            const string csv = "Tag,Description,Installation\nP-200-101A/B,\"Pump, main\",TOG 6.400\nE-200-104,,\n";
            ExternalEquipmentRegister register = ExternalEquipmentRegisterParser.ParseBytes("test.csv", "test.csv", Encoding.UTF8.GetBytes(csv));
            Assert(register.Statistics.SourceRowCount == 2 && register.Statistics.ExpandedEquipmentCount == 3, "CSV expansion statistics failed.");
            Assert(register.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "E-200-104", "P-200-101A", "P-200-101B" }), "CSV deterministic ordering failed.");
            Assert(register.Records.Single(x => x.NormalizedTag == "P-200-101A").Description == "Pump, main", "Quoted CSV field failed.");
            Assert(register.Records.Single(x => x.NormalizedTag == "P-200-101A").ExpansionMethod == "GroupedSuffixExpansion", "Grouped expansion method failed.");
            Assert(register.Records.All(x => x.SourceSheetIndex == null && x.SourceSheetName == null), "CSV sheet provenance changed.");
            ExternalEquipmentRegister compactRegister = ExternalEquipmentRegisterParser.ParseBytes("compact.csv", "compact.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-0201 A/B,,\n"));
            Assert(compactRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "P-0201A", "P-0201B" }), "Compact slash-group expansion failed.");
            ExternalEquipmentRegister alphanumericRegister = ExternalEquipmentRegisterParser.ParseBytes("alphanumeric.csv", "alphanumeric.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-0201 A1/B2,,\n"));
            Assert(alphanumericRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "P-0201A1", "P-0201B2" }), "Alphanumeric compact slash-group expansion failed.");
            ExternalEquipmentRegister rangeRegister = ExternalEquipmentRegisterParser.ParseBytes("range.csv", "range.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nE-0201 A~F,,\n"));
            Assert(rangeRegister.Statistics.SourceRowCount == 1 && rangeRegister.Statistics.ExpandedEquipmentCount == 6 && rangeRegister.Records.Select(x => x.NormalizedTag).SequenceEqual(new[] { "E-0201A", "E-0201B", "E-0201C", "E-0201D", "E-0201E", "E-0201F" }) && rangeRegister.Records.All(x => x.SourceTag == "E-0201 A~F" && x.ExpansionMethod == "AlphabeticRangeExpansion"), "Alphabetic range cross-document expansion failed.");
            bool duplicateRejected = false;
            try { ExternalEquipmentRegisterParser.ParseBytes("d.csv", "d.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-1-1A/B,,\nP-1-1A,,\n")); } catch (ExternalRegisterValidationException) { duplicateRejected = true; }
            Assert(duplicateRejected, "Duplicate expanded identity was accepted.");
            bool rangeDuplicateRejected = false;
            try { ExternalEquipmentRegisterParser.ParseBytes("range-duplicate.csv", "range-duplicate.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nE-0201 A~C,,\nE-0201B,,\n")); } catch (ExternalRegisterValidationException ex) { rangeDuplicateRejected = ex.Message.Contains("duplicate expanded equipment identity: E-0201B"); }
            Assert(rangeDuplicateRejected, "Cross-row range-member duplicate was accepted.");
            bool invalidUtf8Rejected = false;
            try { ExternalEquipmentRegisterParser.ParseBytes("bad.csv", "bad.csv", new byte[] { 0xFF }); } catch (ExternalRegisterValidationException) { invalidUtf8Rejected = true; }
            Assert(invalidUtf8Rejected, "Invalid UTF-8 was accepted.");
            var index = new EngineeringEntityIndex { EquipmentEntities = { new EquipmentEntity { CanonicalId = "equipment:P-200-101A", NormalizedTag = "P-200-101A", HasDrawingEvidence = true, EquipmentListLinks = { new EquipmentListLink { Description = "Pump", SourceHandles = { "ABC" } } } } } };
            ExternalEquipmentRegister mismatch = ExternalEquipmentRegisterParser.ParseBytes("m.csv", "m.csv", Encoding.UTF8.GetBytes("Tag,Description,Installation\nP-200-101A,Other,\n"));
            QaRuleEvaluation evaluation = BuiltInCrossDocumentQaRules.All.Single(x => x.RuleId == "XDOC-EQ-003").Evaluate(index, mismatch);
            Assert(evaluation.Result.FindingCount == 1 && evaluation.Findings[0].Evidence.CrossDocumentEvidence!.MismatchFields.Single() == "Description", "Cross-document mismatch rule failed.");
        }
    }
}
