using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CamboBIM.Revit2024.Addin
{
    internal static class MhnkDiagnostics
    {
        public static string BuildReport(ExternalCommandData commandData)
        {
            var lines = new List<string>();
            Assembly assembly = Assembly.GetExecutingAssembly();
            string assemblyPath = assembly.Location ?? "";
            string revitYear = CamboBimRuntime.RevitYear;

            Add(lines, "Product", "MHNK Revit " + revitYear + " Extension");
            Add(lines, "Assembly name", assembly.GetName().Name);
            Add(lines, "Assembly version", Convert.ToString(assembly.GetName().Version));
            Add(lines, "Assembly path", assemblyPath);
            Add(lines, "Assembly last write", GetFileStamp(assemblyPath));
            Add(lines, "Log folder", MhnkLogger.LogDirectory);
            Add(lines, "Current log", MhnkLogger.CurrentLogPath);
            lines.Add("");

            AddRevitInfo(lines, commandData);
            lines.Add("");

            AddDocumentInfo(lines, commandData);
            lines.Add("");

            AddEnvironmentInfo(lines);
            lines.Add("");

            AddManifestInfo(lines, revitYear, assemblyPath);
            lines.Add("");

            AddLicenseInfo(lines);
            lines.Add("");

            AddFeatureCatalogInfo(lines);

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static void AddRevitInfo(List<string> lines, ExternalCommandData commandData)
        {
            object revitApplication = null;
            try
            {
                if (commandData != null && commandData.Application != null)
                {
                    revitApplication = commandData.Application.Application;
                }
            }
            catch
            {
            }

            Add(lines, "Revit version", ReadProperty(revitApplication, "VersionName"));
            Add(lines, "Revit year", ReadProperty(revitApplication, "VersionNumber"));
            Add(lines, "Revit build", ReadProperty(revitApplication, "VersionBuild"));
            Add(lines, "Autodesk user", ReadProperty(revitApplication, "LoginUserId"));
        }

        private static void AddDocumentInfo(List<string> lines, ExternalCommandData commandData)
        {
            Document document = null;
            try
            {
                if (commandData != null &&
                    commandData.Application != null &&
                    commandData.Application.ActiveUIDocument != null)
                {
                    document = commandData.Application.ActiveUIDocument.Document;
                }
            }
            catch
            {
            }

            if (document == null)
            {
                Add(lines, "Active document", "<none>");
                return;
            }

            Add(lines, "Active document", document.Title);
            Add(lines, "Document path", string.IsNullOrWhiteSpace(document.PathName) ? "<not saved>" : document.PathName);
        }

        private static void AddManifestInfo(List<string> lines, string revitYear, string assemblyPath)
        {
            foreach (string manifestPath in GetManifestCandidates(revitYear))
            {
                string status = File.Exists(manifestPath) ? "found" : "missing";
                Add(lines, "Manifest", manifestPath + " [" + status + "]");

                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                string manifestAssembly = ReadManifestAssemblyPath(manifestPath);
                if (!string.IsNullOrWhiteSpace(manifestAssembly))
                {
                    Add(lines, "Manifest assembly", manifestAssembly);
                    Add(lines, "Manifest matches loaded DLL", PathsEqual(manifestAssembly, assemblyPath) ? "yes" : "no");
                }
            }
        }

        private static void AddLicenseInfo(List<string> lines)
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string licensePath = string.IsNullOrWhiteSpace(programData)
                ? ""
                : Path.Combine(programData, "CamboBIM", "online-license.config.json");

            Add(lines, "License config", string.IsNullOrWhiteSpace(licensePath) ? "<ProgramData unavailable>" : licensePath);
            if (!string.IsNullOrWhiteSpace(licensePath) && File.Exists(licensePath))
            {
                Add(lines, "License config status", "found");
                Add(lines, "License config last write", GetFileStamp(licensePath));
            }
            else
            {
                Add(lines, "License config status", "missing");
            }
        }

        private static void AddEnvironmentInfo(List<string> lines)
        {
            Add(lines, "Local root", ExtensionEnvironment.LocalRoot);
            Add(lines, "Roaming root", ExtensionEnvironment.RoamingRoot);
            Add(lines, "Shared root", ExtensionEnvironment.SharedRoot);

            string architectureGuidePath = "";
            try
            {
                string assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string projectDir = ResolveProjectDirectory(assemblyDir);
                architectureGuidePath = Path.Combine(projectDir, "docs", "PROJECT_ARCHITECTURE_RESTRUCTURE_PLAN.md");
            }
            catch
            {
                architectureGuidePath = "";
            }

            if (!string.IsNullOrWhiteSpace(architectureGuidePath))
            {
                Add(lines, "Architecture guide", architectureGuidePath + (File.Exists(architectureGuidePath) ? " [found]" : " [missing]"));
            }
        }

        private static void AddFeatureCatalogInfo(List<string> lines)
        {
            Add(lines, "Feature catalog count", Convert.ToString(ExtensionFeatureCatalog.All.Count));

            for (int i = 0; i < ExtensionFeatureCatalog.All.Count; i++)
            {
                ExtensionFeature feature = ExtensionFeatureCatalog.All[i];
                Add(lines, "Feature " + (i + 1), feature.Id + " | " + feature.DisplayName + " | " + feature.Workspace);
            }
        }

        private static IEnumerable<string> GetManifestCandidates(string revitYear)
        {
            string fileName = "CamboBIM.Revit" + revitYear + ".Addin.addin";
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (!string.IsNullOrWhiteSpace(programData))
            {
                yield return Path.Combine(programData, "Autodesk", "Revit", "Addins", revitYear, fileName);
            }

            if (!string.IsNullOrWhiteSpace(appData))
            {
                yield return Path.Combine(appData, "Autodesk", "Revit", "Addins", revitYear, fileName);
            }
        }

        private static string ReadManifestAssemblyPath(string manifestPath)
        {
            try
            {
                var document = new XmlDocument();
                document.Load(manifestPath);
                XmlNode node = document.SelectSingleNode("//Assembly");
                return node == null ? "" : (node.InnerText ?? "").Trim();
            }
            catch (Exception ex)
            {
                return "Could not read manifest: " + ex.Message;
            }
        }

        private static string ReadProperty(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return "<unknown>";
            }

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName);
                if (property == null)
                {
                    return "<not available>";
                }

                object value = property.GetValue(instance, null);
                return value == null ? "<empty>" : Convert.ToString(value);
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string GetFileStamp(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return "<missing>";
                }

                return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left ?? "").TrimEnd('\\'),
                    Path.GetFullPath(right ?? "").TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string ResolveProjectDirectory(string assemblyDirectory)
        {
            if (string.IsNullOrWhiteSpace(assemblyDirectory))
            {
                return assemblyDirectory ?? "";
            }

            string current = assemblyDirectory;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(current, "docs");
                if (Directory.Exists(candidate))
                {
                    return current;
                }

                DirectoryInfo parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            return assemblyDirectory;
        }

        private static void Add(List<string> lines, string label, string value)
        {
            lines.Add((label ?? "") + ": " + (value ?? ""));
        }
    }
}
