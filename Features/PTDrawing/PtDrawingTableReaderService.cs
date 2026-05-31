using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamboBIM.Revit2024.Addin
{
    internal static class PtDrawingTableReaderService
    {
        public static AdaptTendonImportReadResult ReadText(
            string path,
            Func<string, List<string>> splitDelimitedLine,
            Func<IReadOnlyList<string[]>, AdaptTendonImportReadResult> readRows)
        {
            string[] lines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8);
            var rows = new List<string[]>();
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(Array.Empty<string>());
                    continue;
                }

                List<string> parsed = splitDelimitedLine == null
                    ? new List<string> { line }
                    : splitDelimitedLine(line);
                rows.Add((parsed ?? new List<string>()).ToArray());
            }

            AdaptTendonImportReadResult result = readRows == null
                ? new AdaptTendonImportReadResult()
                : readRows(rows);
            result.SheetCount = result.Segments.Count > 0 ? 1 : 0;
            return result;
        }

        public static AdaptTendonImportReadResult ReadExcel(
            string path,
            Func<object, object[,]> normalizeExcelRangeToMatrix,
            Func<object, string> toCellString,
            Func<IReadOnlyList<string[]>, AdaptTendonImportReadResult> readRows,
            Action<object> safeReleaseCom,
            Func<AdaptTendonProfileSegmentPayload, string> buildSegmentGroupKey)
        {
            var result = new AdaptTendonImportReadResult();
            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    throw new InvalidOperationException("Microsoft Excel is not available. Export from ADAPT as CSV/TXT, or install Excel.");
                }

                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Open(path, Type.Missing, true);
                dynamic workbook = workbookObj;
                object worksheetsObj = null;
                try
                {
                    worksheetsObj = workbook.Worksheets;
                    dynamic worksheets = worksheetsObj;
                    int sheetCount = Convert.ToInt32(worksheets.Count, CultureInfo.InvariantCulture);

                    for (int wsIndex = 1; wsIndex <= sheetCount; wsIndex++)
                    {
                        object worksheetObj = null;
                        object usedRangeObj = null;
                        try
                        {
                            worksheetObj = worksheets[wsIndex];
                            dynamic worksheet = worksheetObj;
                            usedRangeObj = worksheet.UsedRange;
                            dynamic usedRange = usedRangeObj;
                            object[,] values = normalizeExcelRangeToMatrix == null
                                ? null
                                : normalizeExcelRangeToMatrix(usedRange.Value2);
                            if (values == null)
                            {
                                continue;
                            }

                            AdaptTendonImportReadResult sheetResult = readRows == null
                                ? new AdaptTendonImportReadResult()
                                : readRows(BuildRowsFromExcelMatrix(values, toCellString));
                            if (sheetResult.Segments.Count == 0)
                            {
                                result.SkippedRows += sheetResult.SkippedRows;
                                continue;
                            }

                            if (result.Segments.Count > 0 && result.Mode != sheetResult.Mode)
                            {
                                continue;
                            }

                            result.Mode = sheetResult.Mode;
                            result.InferredUnit = sheetResult.InferredUnit;
                            result.PointCount += sheetResult.PointCount;
                            result.SkippedRows += sheetResult.SkippedRows;
                            result.Segments.AddRange(sheetResult.Segments);
                            result.SheetCount++;
                        }
                        finally
                        {
                            safeReleaseCom?.Invoke(usedRangeObj);
                            safeReleaseCom?.Invoke(worksheetObj);
                        }
                    }
                }
                finally
                {
                    safeReleaseCom?.Invoke(worksheetsObj);
                }

                workbook.Close(false);
                app.Quit();
            }
            finally
            {
                safeReleaseCom?.Invoke(workbookObj);
                safeReleaseCom?.Invoke(workbooksObj);
                safeReleaseCom?.Invoke(appObj);
            }

            result.ProfileCount = result.Segments
                .Select(item => buildSegmentGroupKey == null ? string.Empty : buildSegmentGroupKey(item))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            return result;
        }

        private static List<string[]> BuildRowsFromExcelMatrix(object[,] values, Func<object, string> toCellString)
        {
            var rows = new List<string[]>();
            if (values == null)
            {
                return rows;
            }

            int rMin = values.GetLowerBound(0);
            int rMax = values.GetUpperBound(0);
            int cMin = values.GetLowerBound(1);
            int cMax = values.GetUpperBound(1);

            for (int r = rMin; r <= rMax; r++)
            {
                var cells = new List<string>();
                bool any = false;
                for (int c = cMin; c <= cMax; c++)
                {
                    string text = toCellString == null ? Convert.ToString(values[r, c]) : toCellString(values[r, c]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        any = true;
                    }

                    cells.Add(text ?? string.Empty);
                }

                rows.Add(any ? cells.ToArray() : Array.Empty<string>());
            }

            return rows;
        }
    }
}
