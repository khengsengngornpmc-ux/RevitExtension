using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal static class CBIM_BOREY
    {
        private const string DefaultSharedParameterGroupName = "CBIM-BOREY";
        private const string ProjectInfoLogicalTable = "Project_Info";
        private const string SaleInfoLogicalTable = "Sale_Info";
        private const string SiteInfoLogicalTable = "Site_Info";
        private const string PlaningInfoLogicalTable = "Planing_Info";

        private static readonly string[] LogicalTableNames =
        {
            ProjectInfoLogicalTable,
            SaleInfoLogicalTable,
            SiteInfoLogicalTable,
            PlaningInfoLogicalTable
        };

        private static readonly Dictionary<string, List<string>> LogicalColumnsByTable =
            CreateLogicalColumnsByTable();

        private static readonly Dictionary<string, string[]> ColumnAliases =
            CreateColumnAliases();

        private static readonly string[] MatchKeyColumns =
        {
            "Id",
            "HOUSE ID",
            "House No(Sell)",
            "House Code",
            "Code",
            "Code_Item",
            "Mark"
        };
        private static readonly string[] SelectionFilterColumns =
        {
            "Code_Item",
            "Code",
            "HOUSE ID",
            "House No(Sell)",
            "House Code",
            "ZONE",
            "BLOCK",
            "SUB-BLOCK",
            "HOUSE-TYPE",
            "HANDOVERED STATUS",
            "Plan Description",
            "TOC (Lyna)",
            "TOC(Lyna)"
        };

        private static readonly HashSet<string> NumericColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "%Site_Progress",
            "HANDOVERED"
        };
        private static readonly HashSet<int> EnsuredDocumentKeys = new HashSet<int>();

        internal static IReadOnlyList<string> GetLogicalColumns(string logicalTableName)
        {
            if (LogicalColumnsByTable.TryGetValue(logicalTableName ?? "", out List<string> cols))
            {
                return cols;
            }

            return LogicalColumnsByTable[ProjectInfoLogicalTable];
        }

        internal static IReadOnlyCollection<string> GetManagedParameterNames()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (List<string> cols in LogicalColumnsByTable.Values)
            {
                foreach (string col in cols)
                {
                    if (string.Equals(col, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    set.Add(col);
                }
            }

            return set.ToList();
        }

        internal static void SyncFloorData(
            UIApplication uiapp,
            UIDocument uidoc,
            Document doc,
            CadToModelRequest request,
            CamboBIMWindow window)
        {
            if (uiapp == null || doc == null)
            {
                window?.ShowStatus("BOREY: Revit document is not available.");
                return;
            }

            request = request ?? new CadToModelRequest();

            int documentKey = doc.GetHashCode();
            bool shouldEnsureSharedParameters = request.BoreyEnsureSharedParameters || request.BoreyApplyGridChanges;
            bool ensureOk = EnsuredDocumentKeys.Contains(documentKey);
            bool ensureRequested = shouldEnsureSharedParameters;
            if (shouldEnsureSharedParameters && !ensureOk)
            {
                string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string requestedPath = request.BoreySharedParameterFilePath?.Trim() ?? "";
                string sharedParamPath = string.IsNullOrWhiteSpace(requestedPath)
                    ? SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir)
                    : requestedPath;
                sharedParamPath = SharedParameterPathResolver.ResolveWritableSharedParameterPath(sharedParamPath, baseDir);
                string groupName = string.IsNullOrWhiteSpace(request.BoreySharedParameterGroupName)
                    ? DefaultSharedParameterGroupName
                    : request.BoreySharedParameterGroupName.Trim();

                ensureOk = EnsureSharedParameters(uiapp.Application, doc, sharedParamPath, groupName);
                if (ensureOk)
                {
                    EnsuredDocumentKeys.Add(documentKey);
                }
            }

            int appliedCount = 0;
            int skippedCount = 0;
            if (request.BoreyApplyGridChanges &&
                !string.IsNullOrWhiteSpace(request.BoreyLogicalTableName) &&
                request.BoreyRows != null &&
                request.BoreyRows.Count > 0)
            {
                ApplyGridRowsToFloors(doc, request.BoreyLogicalTableName, request.BoreyRows, out appliedCount, out skippedCount);
            }

            string normalizedRequestedTable = NormalizeLogicalTableName(request.BoreyLogicalTableName);
            List<string> targetTables = string.IsNullOrWhiteSpace(request.BoreyLogicalTableName)
                ? LogicalTableNames.ToList()
                : new List<string> { normalizedRequestedTable };
            Dictionary<string, DataTable> tables = BuildLogicalTablesFromFloors(doc, targetTables, out int floorCount);
            string status = BuildStatusMessage(ensureRequested, ensureOk, floorCount, tables, appliedCount, skippedCount);
            window?.UpdateBoreyTablesFromRevit(tables, status);

            request.BoreyEnsureSharedParameters = false;
            request.BoreyApplyGridChanges = false;
            request.BoreyLogicalTableName = "";
            request.BoreyRows = new List<BoreyGridRowPayload>();
        }

        internal static void SelectBoreyElements(
            UIDocument uidoc,
            Document doc,
            CadToModelRequest request,
            CamboBIMWindow window)
        {
            if (uidoc == null || doc == null)
            {
                window?.ShowStatus("BOREY: Revit document is not available.");
                return;
            }

            request = request ?? new CadToModelRequest();
            IList<BoreyGridRowPayload> rows = request.BoreyRows ?? new List<BoreyGridRowPayload>();
            if (rows.Count == 0)
            {
                return;
            }

            List<Element> floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            if (floors.Count == 0)
            {
                window?.ShowStatus("BOREY: no Floors found in model.");
                return;
            }

            Dictionary<string, Dictionary<string, List<Element>>> floorKeyIndex = BuildFloorMatchIndex(doc, floors);
            var matchedIds = new HashSet<long>();
            int unresolvedRows = 0;
            foreach (BoreyGridRowPayload row in rows)
            {
                if (row?.Values == null || row.Values.Count == 0)
                {
                    unresolvedRows++;
                    continue;
                }

                // Selection should be resilient: if one key is stale, still resolve by other strong keys.
                Element floor = TryResolveFloorFromPayloadForSelection(doc, floorKeyIndex, row.Values);
                if (floor != null)
                {
                    matchedIds.Add(floor.Id.Value);
                    continue;
                }

                List<Element> fallbackMatches = FindFloorMatchesFromPayloadForSelection(doc, floors, row.Values);
                if (fallbackMatches.Count == 0)
                {
                    unresolvedRows++;
                    continue;
                }

                foreach (Element match in fallbackMatches)
                {
                    if (match != null)
                    {
                        matchedIds.Add(match.Id.Value);
                    }
                }
            }

            if (matchedIds.Count == 0)
            {
                window?.ShowStatus("BOREY: selected row(s) are not linked to Floors in this model.");
                return;
            }

            List<ElementId> validIds = matchedIds
                .Select(i => new ElementId(i))
                .Where(id => id != ElementId.InvalidElementId && doc.GetElement(id) != null)
                .ToList();
            if (validIds.Count == 0)
            {
                window?.ShowStatus("BOREY: selected row(s) are not linked to Floors in this model.");
                return;
            }

            uidoc.Selection.SetElementIds(validIds);
            try
            {
                uidoc.ShowElements(validIds);
            }
            catch
            {
            }

            if (unresolvedRows > 0)
            {
                window?.ShowStatus(
                    $"BOREY: selected {validIds.Count} floor element(s) in model (unmatched rows: {unresolvedRows}).");
            }
            else
            {
                window?.ShowStatus($"BOREY: selected {validIds.Count} floor element(s) in model.");
            }
        }

        private static string BuildStatusMessage(
            bool ensureRequested,
            bool ensureOk,
            int floorCount,
            IDictionary<string, DataTable> tables,
            int appliedCount,
            int skippedCount)
        {
            string ensureLabel;
            if (ensureRequested)
            {
                ensureLabel = ensureOk
                    ? "shared parameters verified"
                    : "shared parameters verify failed";
            }
            else
            {
                ensureLabel = ensureOk
                    ? "shared parameters ready (cached)"
                    : "shared parameter check skipped (fast mode)";
            }

            string rowSummary;
            if (tables == null || tables.Count == 0)
            {
                rowSummary = "none";
            }
            else
            {
                rowSummary = string.Join(", ",
                    tables
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(kv => GetTableShortName(kv.Key) + "=" + (kv.Value?.Rows.Count ?? 0).ToString(CultureInfo.InvariantCulture)));
            }

            string saveLabel = appliedCount > 0 || skippedCount > 0
                ? $"; applied={appliedCount}, skipped={skippedCount}"
                : "";

            return "BOREY: " + ensureLabel +
                   $"; floors={floorCount}; rows -> {rowSummary}{saveLabel}.";
        }

        private static string GetTableShortName(string logicalTableName)
        {
            if (string.Equals(logicalTableName, ProjectInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return "Project";
            }

            if (string.Equals(logicalTableName, SaleInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return "Sale";
            }

            if (string.Equals(logicalTableName, SiteInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return "Site";
            }

            if (string.Equals(logicalTableName, PlaningInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return "Monthly Plan";
            }

            return logicalTableName ?? "Unknown";
        }

        private static Dictionary<string, DataTable> BuildLogicalTablesFromFloors(
            Document doc,
            IList<string> logicalTablesToLoad,
            out int floorCount)
        {
            var result = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
            List<string> targetTables = (logicalTablesToLoad ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(NormalizeLogicalTableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targetTables.Count == 0)
            {
                targetTables = LogicalTableNames.ToList();
            }

            List<Element> floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            floorCount = floors.Count;
            List<string> logicalColumns = targetTables
                .SelectMany(GetLogicalColumns)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sharedParameterNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var floorValueById = new Dictionary<long, Dictionary<string, string>>();
            foreach (Element floor in floors)
            {
                if (floor == null)
                {
                    continue;
                }

                var valueMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Populate shared parameters in one pass to avoid repeated LookupParameter calls.
                foreach (Parameter parameter in floor.Parameters)
                {
                    if (parameter == null || !parameter.IsShared || parameter.Definition == null)
                    {
                        continue;
                    }

                    string name = (parameter.Definition.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    sharedParameterNameSet.Add(name);
                    string value = GetParameterTextValue(doc, parameter);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        valueMap[name] = value;
                    }
                }

                // Populate logical columns (small set) on top of shared values.
                foreach (string logicalColumn in logicalColumns)
                {
                    if (valueMap.ContainsKey(logicalColumn))
                    {
                        continue;
                    }

                    string value = GetFloorValueByLogicalName(doc, floor, logicalColumn);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        valueMap[logicalColumn] = value;
                    }
                }

                floorValueById[floor.Id.Value] = valueMap;
            }

            List<string> sharedParameterColumns = sharedParameterNameSet
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string logicalTable in targetTables)
            {
                List<string> columns = GetLogicalColumns(logicalTable).ToList();
                foreach (string sharedColumn in sharedParameterColumns)
                {
                    if (!columns.Contains(sharedColumn, StringComparer.OrdinalIgnoreCase))
                    {
                        columns.Add(sharedColumn);
                    }
                }

                DataTable table = new DataTable(logicalTable);
                foreach (string column in columns)
                {
                    if (!table.Columns.Contains(column))
                    {
                        table.Columns.Add(column, typeof(string));
                    }
                }
                var tableColumnSet = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);

                foreach (Element floor in floors)
                {
                    if (floor == null)
                    {
                        continue;
                    }

                    if (!floorValueById.TryGetValue(floor.Id.Value, out Dictionary<string, string> valueMap) ||
                        valueMap == null ||
                        valueMap.Count == 0)
                    {
                        continue;
                    }

                    DataRow row = table.NewRow();
                    bool hasValue = false;
                    foreach (KeyValuePair<string, string> pair in valueMap)
                    {
                        if (!tableColumnSet.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                        {
                            continue;
                        }

                        row[pair.Key] = pair.Value;
                        hasValue = true;
                    }

                    if (hasValue)
                    {
                        table.Rows.Add(row);
                    }
                }

                result[logicalTable] = table;
            }

            return result;
        }

        private static List<string> GetFloorSharedParameterNames(IEnumerable<Element> floors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (floors == null)
            {
                return names.ToList();
            }

            foreach (Element floor in floors)
            {
                if (floor == null)
                {
                    continue;
                }

                foreach (Parameter parameter in floor.Parameters)
                {
                    if (parameter == null || !parameter.IsShared || parameter.Definition == null)
                    {
                        continue;
                    }

                    string name = (parameter.Definition.Name ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }
            }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void ApplyGridRowsToFloors(
            Document doc,
            string logicalTableName,
            IList<BoreyGridRowPayload> rows,
            out int appliedCount,
            out int skippedCount)
        {
            appliedCount = 0;
            skippedCount = 0;

            if (doc == null || rows == null || rows.Count == 0)
            {
                return;
            }

            string normalizedLogicalTable = NormalizeLogicalTableName(logicalTableName);
            List<Element> floors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType()
                .ToElements()
                .ToList();
            if (floors.Count == 0)
            {
                skippedCount = rows.Count;
                return;
            }

            // Keep logical defaults writable, and also allow writing discovered Floor shared parameters.
            // This keeps newly toggled optional columns functional (not just visible).
            var writableColumns = new HashSet<string>(
                GetLogicalColumns(normalizedLogicalTable),
                StringComparer.OrdinalIgnoreCase);
            foreach (string sharedColumn in GetFloorSharedParameterNames(floors))
            {
                if (!string.IsNullOrWhiteSpace(sharedColumn))
                {
                    writableColumns.Add(sharedColumn.Trim());
                }
            }
            writableColumns.Remove("Id");
            if (writableColumns.Count == 0)
            {
                skippedCount = rows.Count;
                return;
            }

            Dictionary<string, Dictionary<string, List<Element>>> floorKeyIndex = BuildFloorMatchIndex(doc, floors);

            using (Transaction tx = new Transaction(doc, "CamboBIM - Apply Borey Grid To Floors"))
            {
                tx.Start();
                try
                {
                    foreach (BoreyGridRowPayload payload in rows)
                    {
                        if (payload?.Values == null || payload.Values.Count == 0)
                        {
                            skippedCount++;
                            continue;
                        }

                        Element floor = TryResolveFloorFromPayload(doc, floorKeyIndex, payload.Values);
                        if (floor == null)
                        {
                            skippedCount++;
                            continue;
                        }

                        bool changed = false;
                        foreach (string column in writableColumns)
                        {
                            if (!TryGetRowValue(payload.Values, column, out string raw))
                            {
                                continue;
                            }

                            string value = (raw ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                continue;
                            }

                            Parameter p = LookupFloorParameterByLogicalName(floor, column);
                            if (p == null || p.IsReadOnly)
                            {
                                continue;
                            }

                            if (SetParameterFromText(p, value))
                            {
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            appliedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }
                    }

                    tx.Commit();
                }
                catch
                {
                    try
                    {
                        tx.RollBack();
                    }
                    catch
                    {
                    }

                    skippedCount += rows.Count - appliedCount - skippedCount;
                }
            }
        }

        private static Dictionary<string, Dictionary<string, List<Element>>> BuildFloorMatchIndex(Document doc, IList<Element> floors)
        {
            var index = new Dictionary<string, Dictionary<string, List<Element>>>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || floors == null)
            {
                return index;
            }

            foreach (string key in MatchKeyColumns.Where(k => !string.Equals(k, "Id", StringComparison.OrdinalIgnoreCase)))
            {
                index[key] = new Dictionary<string, List<Element>>(StringComparer.OrdinalIgnoreCase);
            }
            List<string> indexKeys = index.Keys.ToList();

            foreach (Element floor in floors)
            {
                if (floor == null)
                {
                    continue;
                }

                foreach (string key in indexKeys)
                {
                    string value = NormalizeValue(GetFloorValueByLogicalName(doc, floor, key));
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    Dictionary<string, List<Element>> byValue = index[key];
                    if (!byValue.TryGetValue(value, out List<Element> bucket))
                    {
                        bucket = new List<Element>();
                        byValue[value] = bucket;
                    }

                    bucket.Add(floor);
                }
            }

            return index;
        }

        private static Element TryResolveFloorFromPayload(
            Document doc,
            Dictionary<string, Dictionary<string, List<Element>>> floorKeyIndex,
            IDictionary<string, string> values)
        {
            if (doc == null || values == null)
            {
                return null;
            }

            if (TryGetRowValue(values, "Id", out string idRaw) &&
                long.TryParse((idRaw ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long idLong))
            {
                Element byId = doc.GetElement(new ElementId(idLong));
                if (byId != null && byId.Category != null &&
                    byId.Category.Id.Value == (int)BuiltInCategory.OST_Floors)
                {
                    return byId;
                }
            }

            List<Element> candidates = null;
            bool hasAnyKey = false;

            foreach (string key in MatchKeyColumns.Where(k => !string.Equals(k, "Id", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryGetRowValue(values, key, out string rowRaw))
                {
                    continue;
                }

                string rowValue = (rowRaw ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rowValue))
                {
                    continue;
                }

                hasAnyKey = true;
                if (floorKeyIndex == null ||
                    !floorKeyIndex.TryGetValue(key, out Dictionary<string, List<Element>> byValue) ||
                    !byValue.TryGetValue(NormalizeValue(rowValue), out List<Element> keyMatches) ||
                    keyMatches == null ||
                    keyMatches.Count == 0)
                {
                    return null;
                }

                if (candidates == null)
                {
                    candidates = keyMatches.ToList();
                }
                else
                {
                    candidates = IntersectCandidates(candidates, keyMatches);
                }

                if (candidates.Count <= 1)
                {
                    break;
                }
            }

            if (!hasAnyKey || candidates == null || candidates.Count != 1)
            {
                return null;
            }

            return candidates[0];
        }

        private static Element TryResolveFloorFromPayloadForSelection(
            Document doc,
            Dictionary<string, Dictionary<string, List<Element>>> floorKeyIndex,
            IDictionary<string, string> values)
        {
            if (doc == null || values == null)
            {
                return null;
            }

            if (TryGetRowValue(values, "Id", out string idRaw) &&
                long.TryParse((idRaw ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long idLong))
            {
                Element byId = doc.GetElement(new ElementId(idLong));
                if (byId != null && byId.Category != null &&
                    byId.Category.Id.Value == (int)BuiltInCategory.OST_Floors)
                {
                    return byId;
                }
            }

            var keyedMatches = new List<List<Element>>();
            foreach (string key in MatchKeyColumns.Where(k => !string.Equals(k, "Id", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryGetRowValue(values, key, out string rowRaw))
                {
                    continue;
                }

                string rowValue = NormalizeValue(rowRaw);
                if (string.IsNullOrWhiteSpace(rowValue))
                {
                    continue;
                }

                if (floorKeyIndex == null ||
                    !floorKeyIndex.TryGetValue(key, out Dictionary<string, List<Element>> byValue) ||
                    !byValue.TryGetValue(rowValue, out List<Element> keyMatches) ||
                    keyMatches == null ||
                    keyMatches.Count == 0)
                {
                    continue;
                }

                List<Element> distinct = DistinctCandidates(keyMatches);
                if (distinct.Count == 0)
                {
                    continue;
                }

                keyedMatches.Add(distinct);
            }

            if (keyedMatches.Count == 0)
            {
                return null;
            }

            List<Element> intersection = keyedMatches[0];
            for (int i = 1; i < keyedMatches.Count; i++)
            {
                intersection = IntersectCandidates(intersection, keyedMatches[i]);
                if (intersection.Count == 1)
                {
                    return intersection[0];
                }
            }

            if (intersection.Count == 1)
            {
                return intersection[0];
            }

            // If key sets conflict, fall back to the strongest unique key match.
            foreach (List<Element> match in keyedMatches)
            {
                if (match.Count == 1)
                {
                    return match[0];
                }
            }

            // Last resort: if all matched candidates collapse to one distinct floor, use it.
            List<Element> allDistinct = DistinctCandidates(keyedMatches.SelectMany(m => m).ToList());
            if (allDistinct.Count == 1)
            {
                return allDistinct[0];
            }

            return null;
        }

        private static List<Element> IntersectCandidates(IList<Element> left, IList<Element> right)
        {
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return new List<Element>();
            }

            var rightIds = new HashSet<long>(right
                .Where(e => e != null)
                .Select(e => e.Id.Value));

            return left
                .Where(e => e != null && rightIds.Contains(e.Id.Value))
                .ToList();
        }

        private static List<Element> DistinctCandidates(IList<Element> elements)
        {
            var seen = new HashSet<long>();
            var result = new List<Element>();
            if (elements == null)
            {
                return result;
            }

            foreach (Element element in elements)
            {
                if (element == null)
                {
                    continue;
                }

                long id = element.Id.Value;
                if (seen.Add(id))
                {
                    result.Add(element);
                }
            }

            return result;
        }

        private static List<Element> FindFloorMatchesFromPayloadForSelection(
            Document doc,
            IList<Element> floors,
            IDictionary<string, string> values)
        {
            if (doc == null || floors == null || floors.Count == 0 || values == null || values.Count == 0)
            {
                return new List<Element>();
            }

            var predicates = new List<(string Key, string Value)>();
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string key in SelectionFilterColumns)
            {
                if (!TryGetRowValue(values, key, out string raw))
                {
                    continue;
                }

                string value = NormalizeValue(raw);
                if (string.IsNullOrWhiteSpace(value) ||
                    string.Equals(value, "Total", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (usedKeys.Add(key))
                {
                    predicates.Add((key, value));
                }
            }

            if (predicates.Count == 0)
            {
                return new List<Element>();
            }

            var matches = new List<Element>();
            foreach (Element floor in floors)
            {
                if (floor == null)
                {
                    continue;
                }

                bool isMatch = true;
                foreach ((string key, string value) in predicates)
                {
                    string floorValue = NormalizeValue(GetFloorValueByLogicalName(doc, floor, key));
                    if (!string.Equals(floorValue, value, StringComparison.OrdinalIgnoreCase))
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (isMatch)
                {
                    matches.Add(floor);
                }
            }

            return DistinctCandidates(matches);
        }

        private static bool TryGetRowValue(IDictionary<string, string> values, string logicalName, out string value)
        {
            value = "";
            if (values == null || string.IsNullOrWhiteSpace(logicalName))
            {
                return false;
            }

            if (values.TryGetValue(logicalName, out string direct))
            {
                value = direct ?? "";
                return true;
            }

            foreach (string alias in GetAliases(logicalName))
            {
                if (values.TryGetValue(alias, out string aliasValue))
                {
                    value = aliasValue ?? "";
                    return true;
                }
            }

            return false;
        }

        private static string GetFloorValueByLogicalName(Document doc, Element floor, string logicalName)
        {
            if (floor == null || string.IsNullOrWhiteSpace(logicalName))
            {
                return "";
            }

            if (string.Equals(logicalName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                return floor.Id.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (string.Equals(logicalName, "Mark", StringComparison.OrdinalIgnoreCase))
            {
                Parameter mark = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                string markValue = GetParameterTextValue(doc, mark);
                if (!string.IsNullOrWhiteSpace(markValue))
                {
                    return markValue;
                }
            }

            foreach (string alias in GetAliases(logicalName))
            {
                Parameter p = floor.LookupParameter(alias);
                string value = GetParameterTextValue(doc, p);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private static Parameter LookupFloorParameterByLogicalName(Element floor, string logicalName)
        {
            if (floor == null || string.IsNullOrWhiteSpace(logicalName))
            {
                return null;
            }

            foreach (string alias in GetAliases(logicalName))
            {
                Parameter p = floor.LookupParameter(alias);
                if (p != null)
                {
                    return p;
                }
            }

            return null;
        }

        private static string GetParameterTextValue(Document doc, Parameter parameter)
        {
            if (parameter == null)
            {
                return "";
            }

            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return (parameter.AsString() ?? "").Trim();
                case StorageType.Integer:
                    return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double:
                    string valueString = parameter.AsValueString();
                    if (!string.IsNullOrWhiteSpace(valueString))
                    {
                        return valueString.Trim();
                    }

                    return parameter.AsDouble().ToString("0.########", CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    ElementId id = parameter.AsElementId();
                    if (id == null || id == ElementId.InvalidElementId)
                    {
                        return "";
                    }

                    Element e = doc?.GetElement(id);
                    return e?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static bool SetParameterFromText(Parameter parameter, string rawValue)
        {
            if (parameter == null || parameter.IsReadOnly)
            {
                return false;
            }

            string value = (rawValue ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        string current = parameter.AsString() ?? "";
                        if (string.Equals(current, value, StringComparison.Ordinal))
                        {
                            return false;
                        }

                        return parameter.Set(value);
                    case StorageType.Integer:
                        int intVal;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intVal) &&
                            !int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out intVal))
                        {
                            return false;
                        }

                        if (parameter.AsInteger() == intVal)
                        {
                            return false;
                        }

                        return parameter.Set(intVal);
                    case StorageType.Double:
                        double dblVal;
                        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out dblVal) &&
                            !double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out dblVal))
                        {
                            return false;
                        }

                        if (Math.Abs(parameter.AsDouble() - dblVal) <= 1e-9)
                        {
                            return false;
                        }

                        return parameter.Set(dblVal);
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeLogicalTableName(string logicalTableName)
        {
            string value = (logicalTableName ?? "").Trim();
            if (string.Equals(value, SaleInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return SaleInfoLogicalTable;
            }

            if (string.Equals(value, SiteInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return SiteInfoLogicalTable;
            }

            if (string.Equals(value, PlaningInfoLogicalTable, StringComparison.OrdinalIgnoreCase))
            {
                return PlaningInfoLogicalTable;
            }

            return ProjectInfoLogicalTable;
        }

        private static string NormalizeValue(string value)
        {
            return (value ?? "").Trim();
        }

        private static IEnumerable<string> GetAliases(string logicalName)
        {
            string key = logicalName ?? "";
            if (ColumnAliases.TryGetValue(key, out string[] aliases) && aliases != null && aliases.Length > 0)
            {
                return aliases;
            }

            return new[] { key };
        }

        private static bool EnsureSharedParameters(
            Autodesk.Revit.ApplicationServices.Application app,
            Document doc,
            string sharedParameterPath,
            string groupName)
        {
            if (app == null || doc == null || string.IsNullOrWhiteSpace(sharedParameterPath))
            {
                return false;
            }

            EnsureSharedParameterFile(sharedParameterPath);
            string originalFile = app.SharedParametersFilename;

            try
            {
                app.SharedParametersFilename = sharedParameterPath;
                DefinitionFile defFile = TryOpenSharedParameterFile(app);
                if (defFile == null)
                {
                    BackupSharedParameterFile(sharedParameterPath);
                    EnsureSharedParameterFile(sharedParameterPath, overwrite: true);
                    defFile = TryOpenSharedParameterFile(app);
                }
                if (defFile == null)
                {
                    return false;
                }

                DefinitionGroup group = defFile.Groups.get_Item(groupName) ?? defFile.Groups.Create(groupName);
                if (group == null)
                {
                    return false;
                }

                Category floorCategory = null;
                try
                {
                    floorCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Floors);
                }
                catch
                {
                }

                if (floorCategory == null)
                {
                    return false;
                }

                using (Transaction tx = new Transaction(doc, "CamboBIM - Bind Borey Shared Parameters"))
                {
                    tx.Start();
                    BindingMap map = doc.ParameterBindings;

                    foreach (string managedName in GetManagedParameterNames())
                    {
                        List<Definition> boundDefs = GetBoundDefinitionsByName(map, managedName);
                        if (boundDefs.Count > 1)
                        {
                            RemoveDuplicateBindingsByName(map, managedName, boundDefs[0]);
                        }
                    }

                    foreach (string paramName in GetManagedParameterNames())
                    {
                        ForgeTypeId typeId = NumericColumns.Contains(paramName)
                            ? SpecTypeId.Number
                            : SpecTypeId.String.Text;

                        Definition def = FindSharedDefinitionByName(defFile, paramName) ?? group.Definitions.get_Item(paramName);
                        if (def == null)
                        {
                            var options = new ExternalDefinitionCreationOptions(paramName, typeId)
                            {
                                Visible = true
                            };
                            def = group.Definitions.Create(options);
                        }
                        if (def == null)
                        {
                            continue;
                        }

                        List<Definition> defsByName = GetBoundDefinitionsByName(map, paramName);
                        Definition bindDef = defsByName.FirstOrDefault() ?? def;
                        RemoveDuplicateBindingsByName(map, paramName, bindDef);

                        CategorySet set = app.Create.NewCategorySet();
                        set.Insert(floorCategory);

                        ElementBinding existing = map.get_Item(bindDef) as ElementBinding;
                        if (existing == null)
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(set);
                            if (!map.Insert(bindDef, binding, GroupTypeId.Data))
                            {
                                map.ReInsert(bindDef, binding, GroupTypeId.Data);
                            }
                        }
                        else if (!CategorySetContains(existing.Categories, floorCategory))
                        {
                            InstanceBinding binding = app.Create.NewInstanceBinding(set);
                            map.ReInsert(bindDef, binding, GroupTypeId.Data);
                        }
                    }

                    tx.Commit();
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                app.SharedParametersFilename = originalFile;
            }
        }

        private static DefinitionFile TryOpenSharedParameterFile(Autodesk.Revit.ApplicationServices.Application app)
        {
            if (app == null)
            {
                return null;
            }

            try
            {
                return app.OpenSharedParameterFile();
            }
            catch
            {
                return null;
            }
        }

        private static Definition FindSharedDefinitionByName(DefinitionFile defFile, string name)
        {
            if (defFile == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (DefinitionGroup g in defFile.Groups)
            {
                if (g == null || g.Definitions == null)
                {
                    continue;
                }

                Definition d = g.Definitions.get_Item(name);
                if (d != null)
                {
                    return d;
                }
            }

            return null;
        }

        private static List<Definition> GetBoundDefinitionsByName(BindingMap map, string name)
        {
            var result = new List<Definition>();
            if (map == null || string.IsNullOrWhiteSpace(name))
            {
                return result;
            }

            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition current = it.Key;
                if (current == null)
                {
                    continue;
                }

                if (!string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(current);
            }

            return result;
        }

        private static void RemoveDuplicateBindingsByName(BindingMap map, string name, Definition keepDefinition)
        {
            if (map == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var toRemove = new List<Definition>();
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                Definition current = it.Key;
                if (current == null)
                {
                    continue;
                }

                if (!string.Equals(current.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ReferenceEquals(current, keepDefinition))
                {
                    continue;
                }

                toRemove.Add(current);
            }

            foreach (Definition d in toRemove)
            {
                try
                {
                    map.Remove(d);
                }
                catch
                {
                }
            }
        }

        private static bool CategorySetContains(CategorySet set, Category category)
        {
            if (set == null || category == null)
            {
                return false;
            }

            foreach (Category c in set)
            {
                if (c != null && c.Id.Value == category.Id.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BackupSharedParameterFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return;
                }

                string dir = Path.GetDirectoryName(filePath) ?? "";
                string name = Path.GetFileNameWithoutExtension(filePath);
                string ext = Path.GetExtension(filePath);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string backup = Path.Combine(dir, $"{name}.corrupt.{stamp}{ext}.bak");
                File.Copy(filePath, backup, true);
            }
            catch
            {
            }
        }

        private static void EnsureSharedParameterFile(string filePath, bool overwrite = false)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!overwrite && File.Exists(filePath))
            {
                if (IsCorruptSharedParameterFile(filePath))
                {
                    BackupSharedParameterFile(filePath);
                    overwrite = true;
                }
            }

            if (overwrite || !File.Exists(filePath))
            {
                const string header =
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually.\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n";
                File.WriteAllText(filePath, header, new UTF8Encoding(false));
            }
        }

        private static bool IsCorruptSharedParameterFile(string filePath)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                if (bytes.Length == 0)
                {
                    return true;
                }

                if (bytes.Any(b => b == 0))
                {
                    return true;
                }

                string text = Encoding.UTF8.GetString(bytes);
                if (!text.Contains("*META\tVERSION\tMINVERSION"))
                {
                    return true;
                }

                if (!text.Contains("*GROUP\tID\tNAME"))
                {
                    return true;
                }

                if (!text.Contains("*PARAM\tGUID\tNAME\tDATATYPE"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static Dictionary<string, List<string>> CreateLogicalColumnsByTable()
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [ProjectInfoLogicalTable] = new List<string>
                {
                    "Code_Item",
                    "Code",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "HOUSE ID",
                    "House No(Sell)",
                    "House Code",
                    "LAND LOTS",
                    "Sold Only Land",
                    "House-Units",
                    "HANDOVERED STATUS",
                    "H_Tp",
                    "SUBCONTRACTOR",
                    "CONTRACT/LOA/BLC No."
                },
                [SaleInfoLogicalTable] = new List<string>
                {
                    "Code_Item",
                    "Code",
                    "Name",
                    "Description",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "HOUSE ID",
                    "House No(Sell)",
                    "House Code",
                    "SPA Date",
                    "SPA (HO Date)",
                    "SPA Duration",
                    "Duration GP",
                    "Grace Period Date",
                    "Priority Type",
                    "Plan Date",
                    "In Months"
                },
                [SiteInfoLogicalTable] = new List<string>
                {
                    "Code_Item",
                    "Code",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "HOUSE ID",
                    "House No(Sell)",
                    "House Code",
                    "%Site_Progress",
                    "H_Tp"
                },
                [PlaningInfoLogicalTable] = new List<string>
                {
                    "Code_Item",
                    "ZONE",
                    "BLOCK",
                    "SUB-BLOCK",
                    "HOUSE-TYPE",
                    "HOUSE ID",
                    "House Code",
                    "Plan Description",
                    "Plan Handover",
                    "House-Units",
                    "Handovered-Units",
                    "HANDOVERED",
                    "HANDOVERED STATUS",
                    "TOC (Lyna)"
                }
            };

            return map;
        }

        private static Dictionary<string, string[]> CreateColumnAliases()
        {
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Id", new[] { "Id" } },
                { "Code_Item", new[] { "Code_Item", "CodeItem", "Code Item" } },
                { "Code", new[] { "Code", "Code_Item" } },
                { "Name", new[] { "Name" } },
                { "Description", new[] { "Description" } },
                { "ZONE", new[] { "ZONE" } },
                { "BLOCK", new[] { "BLOCK" } },
                { "SUB-BLOCK", new[] { "SUB-BLOCK", "SUB BLOCK" } },
                { "HOUSE-TYPE", new[] { "HOUSE-TYPE", "HOUSE TYPE", "H_Tp" } },
                { "H_Tp", new[] { "H_Tp", "HOUSE-TYPE", "HOUSE TYPE" } },
                { "HOUSE ID", new[] { "HOUSE ID" } },
                { "House No(Sell)", new[] { "House No(Sell)" } },
                { "House Code", new[] { "House Code", "House code(FN)", "HOUSE ID", "House No(Sell)" } },
                { "LAND LOTS", new[] { "LAND LOTS" } },
                { "House-Units", new[] { "House-Units" } },
                { "%Site_Progress", new[] { "%Site_Progress" } },
                { "SPA Date", new[] { "SPA Date" } },
                { "SPA (HO Date)", new[] { "SPA (HO Date)", "SPA  (HO Date)" } },
                { "SPA Duration", new[] { "SPA Duration" } },
                { "Duration GP", new[] { "Duration GP", "SPA+GP" } },
                { "Grace Period Date", new[] { "Grace Period Date", "End Date SPA+ GP" } },
                { "Priority Type", new[] { "Priority Type", "Priority", "Priority build", "House Priority" } },
                { "Plan Date", new[] { "Plan Date", "PlanDate" } },
                { "In Months", new[] { "In Months", "In Month", "InMonths" } },
                { "Data_Sold_Out", new[] { "Data_Sold_Out" } },
                { "Sold Only Land", new[] { "Sold Only Land" } },
                { "Handovered-Units", new[] { "Handovered-Units" } },
                { "HANDOVERED", new[] { "HANDOVERED" } },
                { "Sold/Unsold", new[] { "Sold/Unsold" } },
                { "Construction Type", new[] { "Construction Type" } },
                { "Plan Description", new[] { "Plan Description" } },
                { "HANDOVERED STATUS", new[] { "HANDOVERED STATUS", "HANDOVER STATUS" } },
                { "Plan Handover", new[] { "Plan Handover" } },
                { "TOC (Lyna)", new[] { "TOC (Lyna)", "TOC(Lyna)" } },
                { "TOC(Lyna)", new[] { "TOC(Lyna)", "TOC (Lyna)" } },
                { "SUBCONTRACTOR", new[] { "SUBCONTRACTOR", "Subcontractor", "SUB CONTRACTOR" } },
                { "CONTRACT/LOA/BLC No.", new[] { "CONTRACT/LOA/BLC No.", "CONTRACT/LOA/BLC NO.", "CONTRACT/LOA/BLC No", "CONTRACT LOA BLC No." } },
                { "Mark", new[] { "Mark" } }
            };
        }
    }
}
