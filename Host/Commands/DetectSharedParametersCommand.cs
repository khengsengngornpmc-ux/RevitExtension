using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DetectSharedParametersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string autodeskUserId = commandData?.Application?.Application?.LoginUserId ?? "";
            App.SetAutodeskLoginUserId(autodeskUserId);

            if (!App.EnsureLicenseActivated(out string licenseFailure, autodeskUserId))
            {
                TaskDialog.Show("CamboBIM License", licenseFailure);
                return Result.Cancelled;
            }

            UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show("Shared Parameter Check", "No active Revit document.");
                return Result.Cancelled;
            }

            var sharedParams = new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .Select(x =>
                {
                    Definition def = x.GetDefinition();
                    return new SharedParamItem
                    {
                        Name = def?.Name ?? "<Unnamed>",
                        Guid = x.GuidValue.ToString()
                    };
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            HashSet<string> foundNames = new HashSet<string>(
                sharedParams.Select(x => x.Name),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> foundGuids = new HashSet<string>(
                sharedParams.Select(x => x.Guid),
                StringComparer.OrdinalIgnoreCase);

            string baseDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string sharedParamFile = SharedParameterPathResolver.ResolveDefaultSharedParameterPath(baseDir);
            List<SharedParamItem> expectedFromFile = LoadExpectedFromSharedParameterFile(sharedParamFile);

            List<string> expectedNames;
            List<string> missing;
            List<SharedParamItem> missingByGuid = new List<SharedParamItem>();
            if (expectedFromFile.Count > 0)
            {
                expectedNames = expectedFromFile.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                missing = expectedNames
                    .Where(x => !foundNames.Contains(x))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                missingByGuid = expectedFromFile
                    .Where(x => !string.IsNullOrWhiteSpace(x.Guid) && !foundGuids.Contains(x.Guid))
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                expectedNames = GetFallbackExpectedParameterNames();
                missing = expectedNames
                    .Where(x => !foundNames.Contains(x))
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            string reportPath = WriteReport(doc, sharedParams, expectedNames, missing, sharedParamFile, expectedFromFile, missingByGuid);
            string summary =
                "Shared parameters found: " + sharedParams.Count + Environment.NewLine +
                "Shared parameter file: " + sharedParamFile + Environment.NewLine +
                "Expected parameters checked: " + expectedNames.Count + Environment.NewLine +
                "Missing by name: " + missing.Count + Environment.NewLine +
                "Missing by GUID: " + missingByGuid.Count + Environment.NewLine + Environment.NewLine +
                "Report saved to:" + Environment.NewLine + reportPath;

            TaskDialog dlg = new TaskDialog("Shared Parameter Check");
            dlg.MainInstruction = "Shared parameter detection completed.";
            dlg.MainContent = summary;
            if (missing.Count > 0)
            {
                string preview = string.Join(", ", missing.Take(12));
                if (missing.Count > 12) preview += ", ...";
                dlg.ExpandedContent = "Missing (preview): " + preview;
            }
            dlg.Show();

            return Result.Succeeded;
        }

        private static string WriteReport(
            Document doc,
            List<SharedParamItem> sharedParams,
            List<string> expected,
            List<string> missing,
            string sharedParamFile,
            List<SharedParamItem> expectedFromFile,
            List<SharedParamItem> missingByGuid)
        {
            string fileName = "CamboBIM_SharedParameterReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            string reportPath = Path.Combine(Path.GetTempPath(), fileName);
            var sb = new StringBuilder();

            sb.AppendLine("CamboBIM Shared Parameter Detection");
            sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Document: " + (doc?.Title ?? "<unknown>"));
            sb.AppendLine("Shared parameter file: " + (sharedParamFile ?? ""));
            sb.AppendLine();

            sb.AppendLine("Expected Key Parameters");
            sb.AppendLine("-----------------------");
            foreach (string name in expected.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine("- " + name);
            }

            sb.AppendLine();
            sb.AppendLine("Missing Expected Parameters");
            sb.AppendLine("---------------------------");
            if (missing.Count == 0)
            {
                sb.AppendLine("- <none>");
            }
            else
            {
                foreach (string name in missing)
                {
                    sb.AppendLine("- " + name);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Expected From Shared Parameter File (Name | GUID)");
            sb.AppendLine("-----------------------------------------------");
            if (expectedFromFile.Count == 0)
            {
                sb.AppendLine("- <not loaded>");
            }
            else
            {
                foreach (SharedParamItem item in expectedFromFile.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine("- " + item.Name + " | " + item.Guid);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Missing By GUID (from shared parameter file)");
            sb.AppendLine("--------------------------------------------");
            if (missingByGuid.Count == 0)
            {
                sb.AppendLine("- <none>");
            }
            else
            {
                foreach (SharedParamItem item in missingByGuid)
                {
                    sb.AppendLine("- " + item.Name + " | " + item.Guid);
                }
            }

            sb.AppendLine();
            sb.AppendLine("Detected Shared Parameters (Name | GUID)");
            sb.AppendLine("----------------------------------------");
            if (sharedParams.Count == 0)
            {
                sb.AppendLine("- <none>");
            }
            else
            {
                foreach (SharedParamItem item in sharedParams)
                {
                    sb.AppendLine("- " + item.Name + " | " + item.Guid);
                }
            }

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return reportPath;
        }

        private static List<SharedParamItem> LoadExpectedFromSharedParameterFile(string path)
        {
            var result = new List<SharedParamItem>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return result;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string line = raw.Trim();
                    if (!line.StartsWith("PARAM\t", StringComparison.OrdinalIgnoreCase)) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length < 4) continue;

                    string guid = parts[1]?.Trim() ?? "";
                    string name = parts[2]?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    result.Add(new SharedParamItem
                    {
                        Name = name,
                        Guid = guid
                    });
                }
            }
            catch
            {
            }

            return result
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        private static List<string> GetFallbackExpectedParameterNames()
        {
            return new List<string>
            {
                "Code_Item",
                "ZONE",
                "BLOCK",
                "SUB-BLOCK",
                "HOUSE-TYPE",
                "House Code",
                "LAND LOTS",
                "House-Units",
                "Data_Sold_Out",
                "Sold Only Land",
                "Handovered-Units",
                "HANDOVERED",
                "Sold/Unsold",
                "Construction Type",
                "Plan Description",
                "HANDOVERED STATUS",
                "TOC (Lyna)",
                "TOC(Lyna)",
                "%Site_Progress",
                "Plan Handover",
                "SPA Date",
                "SPA  (HO Date)",
                "SPA Duration",
                "Duration GP",
                "Grace Period Date",
                "Priority Type",
                "H_Tp"
            };
        }

        private sealed class SharedParamItem
        {
            public string Name { get; set; }
            public string Guid { get; set; }
        }
    }
}
