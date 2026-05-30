using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CamboBIM.DeployRevitAddinExe
{
    internal static class Program
    {
        private const string DefaultConfiguration = "Debug";
        private const string DefaultPlatform = "AnyCPU";
        private const string DefaultRevitYear = "2024";
        private const string DefaultAddInId = "d4265daa-0966-461a-8900-c48c10260677";
        private const string LicenseConfigFileName = "online-license.config.json";
        private const string LicenseConfigRelativePath = @"license\online-license.config.json";
        private const string LicenseGoogleSampleRelativePath = @"license\online-license.google-sheet.sample.json";
        private const string LicenseTemplateRelativePath = @"license\online-license.config.json.template";

        private sealed class Options
        {
            public Options()
            {
                Configuration = DefaultConfiguration;
                Platform = DefaultPlatform;
                RevitYear = GetDefaultRevitYear();
                AddInId = DefaultAddInId;
                AssemblyPath = "";
                ProjectRoot = "";
                LicenseConfigPath = "";
                LicenseServerUrl = "";
                DeployLicenseConfig = true;
                ForceLicenseConfig = false;
                AllUsers = false;
                UnlockTestUsers = false;
                UseUi = true;
                ShowHelp = false;
            }

            public string Configuration { get; set; }
            public string Platform { get; set; }
            public string RevitYear { get; set; }
            public string AddInId { get; set; }
            public string AssemblyPath { get; set; }
            public string ProjectRoot { get; set; }
            public string LicenseConfigPath { get; set; }
            public string LicenseServerUrl { get; set; }
            public bool DeployLicenseConfig { get; set; }
            public bool ForceLicenseConfig { get; set; }
            public bool AllUsers { get; set; }
            public bool UnlockTestUsers { get; set; }
            public bool UseUi { get; set; }
            public bool ShowHelp { get; set; }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string logPath = Path.Combine(Path.GetTempPath(), GetLogFileName(GetDefaultRevitYear()));
            Options options = null;

            try
            {
                options = ParseArgs(args);
                logPath = Path.Combine(Path.GetTempPath(), GetLogFileName(options.RevitYear));

                if (options.ShowHelp)
                {
                    string help = BuildHelpText();
                    WriteLog(logPath, help);
                    if (options.UseUi)
                    {
                        ShowInfo(help);
                    }

                    return 0;
                }

                string projectRoot = ResolveProjectRoot(options.ProjectRoot, options.RevitYear);
                string templateSource;
                string templateContent = ResolveTemplateContent(projectRoot, options.RevitYear, out templateSource, logPath);
                string assemblyPath = ResolveAssemblyPath(projectRoot, options, logPath);
                string manifestContent = BuildManifest(templateContent, assemblyPath, options.AddInId, options.RevitYear);
                string manifestPath = WriteManifest(manifestContent, options.RevitYear, options.AllUsers);

                List<string> warnings = DisableDuplicateManifests(manifestPath, options.RevitYear, options.AddInId, logPath);
                string licenseConfigResult = DeployLicenseConfig(projectRoot, options, logPath);

                var summary = new StringBuilder();
                summary.AppendLine("Manifest deployed successfully.");
                summary.AppendLine();
                summary.AppendLine("Manifest:");
                summary.AppendLine("  " + manifestPath);
                summary.AppendLine("Assembly:");
                summary.AppendLine("  " + assemblyPath);
                summary.AppendLine("Template:");
                summary.AppendLine("  " + templateSource);
                if (!string.IsNullOrWhiteSpace(licenseConfigResult))
                {
                    summary.AppendLine("License config:");
                    summary.AppendLine("  " + licenseConfigResult);
                }
                else if (!options.DeployLicenseConfig)
                {
                    summary.AppendLine("License config:");
                    summary.AppendLine("  skipped");
                }
                summary.AppendLine();
                summary.AppendLine("Restart Revit if it is currently open.");

                if (warnings.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine("Warnings:");
                    foreach (string warning in warnings)
                    {
                        summary.AppendLine("- " + warning);
                    }
                }

                WriteLog(logPath, summary.ToString());

                if (options.UseUi)
                {
                    ShowInfo(summary.ToString());
                }

                return 0;
            }
            catch (Exception ex)
            {
                string defaultRevitYear = GetDefaultRevitYear();
                string manifestHint = Path.Combine(
                    Environment.GetFolderPath(options != null && options.AllUsers ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.ApplicationData),
                    "Autodesk",
                    "Revit",
                    "Addins",
                    defaultRevitYear,
                    GetManifestFileName(defaultRevitYear));

                string message =
                    "Deploy failed.\r\n\r\n" +
                    ex.Message + "\r\n\r\n" +
                    "Try this:\r\n" +
                    "1) Close Revit\r\n" +
                    "2) Run deploy EXE as Administrator\r\n" +
                    "3) Check write access to:\r\n   " + manifestHint + "\r\n\r\n" +
                    "Log:\r\n" + logPath;
                WriteLog(logPath, message + "\r\n" + ex);

                try
                {
                    ShowError(message);
                }
                catch
                {
                }

                return 1;
            }
        }

        private static string BuildHelpText()
        {
            string defaultRevitYear = GetDefaultRevitYear();
            string executableName = GetCurrentExecutableName();
            var sb = new StringBuilder();
            sb.AppendLine("MHNK Revit " + defaultRevitYear + " Deployer");
            sb.AppendLine();
            sb.AppendLine("Usage:");
            sb.AppendLine("  " + executableName + " [options]");
            sb.AppendLine();
            sb.AppendLine("Options:");
            sb.AppendLine("  --configuration <Debug|Release>   Build configuration (default: Debug)");
            sb.AppendLine("  --platform <AnyCPU|x64>           Build platform (default: AnyCPU)");
            sb.AppendLine("  --revit-year <year>               Revit year (default: " + defaultRevitYear + ")");
            sb.AppendLine("  --addin-id <guid>                 AddInId GUID");
            sb.AppendLine("  --assembly <path>                 Explicit DLL path");
            sb.AppendLine("  --project-root <path>             Project root containing addins template");
            sb.AppendLine("  --license-config <path>           Optional source online-license.config.json");
            sb.AppendLine("  --license-server-url <url>        Set server_url in deployed license config");
            sb.AppendLine("  --skip-license-config             Do not deploy ProgramData license config");
            sb.AppendLine("  --force-license-config            Overwrite existing ProgramData license config");
            sb.AppendLine("  --all-users                       Deploy manifest to ProgramData for all Windows users");
            sb.AppendLine("  --unlock-test-users               Disable online license gate for test deployment");
            sb.AppendLine("  --no-ui                           Do not show popup windows");
            sb.AppendLine("  --help                            Show help");
            return sb.ToString();
        }

        private static string GetAddInName(string revitYear)
        {
            string year = string.IsNullOrWhiteSpace(revitYear) ? DefaultRevitYear : revitYear.Trim();
            return "CamboBIM.Revit" + year + ".Addin";
        }

        private static string GetAssemblyFileName(string revitYear)
        {
            return GetAddInName(revitYear) + ".dll";
        }

        private static string GetManifestTemplateRelativePath(string revitYear)
        {
            return Path.Combine("addins", GetAddInName(revitYear) + ".addin.template");
        }

        private static string GetManifestFileName(string revitYear)
        {
            return GetAddInName(revitYear) + ".addin";
        }

        private static string GetLogFileName(string revitYear)
        {
            return GetAddInName(revitYear) + ".Deploy.log";
        }

        private static string GetEmbeddedManifestTemplate(string revitYear)
        {
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>" + "\r\n" +
                "<RevitAddIns>" + "\r\n" +
                "  <AddIn Type=\"Application\">" + "\r\n" +
                "    <Name>MHNK.Revit" + (string.IsNullOrWhiteSpace(revitYear) ? DefaultRevitYear : revitYear.Trim()) + ".Addin</Name>" + "\r\n" +
                "    <Assembly>{{ASSEMBLY_PATH}}</Assembly>" + "\r\n" +
                "    <AddInId>{{ADDIN_ID}}</AddInId>" + "\r\n" +
                "    <FullClassName>CamboBIM.Revit2024.Addin.App</FullClassName>" + "\r\n" +
                "    <VendorId>MHNK</VendorId>" + "\r\n" +
                "    <VendorDescription>Mohanokor Engineering Construction</VendorDescription>" + "\r\n" +
                "  </AddIn>" + "\r\n" +
                "</RevitAddIns>" + "\r\n";
        }

        private static string GetDefaultRevitYear()
        {
            string fromExeName = InferRevitYearFromExecutableName();
            if (!string.IsNullOrWhiteSpace(fromExeName))
            {
                return fromExeName;
            }

            return DefaultRevitYear;
        }

        private static string InferRevitYearFromExecutableName()
        {
            string executableStem = Path.GetFileNameWithoutExtension(GetCurrentExecutableName());
            if (string.IsNullOrWhiteSpace(executableStem))
            {
                return "";
            }

            Match m = Regex.Match(executableStem, "revit(?<year>\\d{4})-addin$", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                return "";
            }

            return m.Groups["year"].Value;
        }

        private static string GetCurrentExecutableName()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args != null && args.Length > 0)
                {
                    string firstArg = args[0] ?? "";
                    string fileName = Path.GetFileName(firstArg);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }
            }
            catch
            {
            }

            return "deploy-revit2024-addin.exe";
        }

        private static Options ParseArgs(string[] args)
        {
            var options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? "";
                string next = (i + 1) < args.Length ? args[i + 1] : "";

                if (string.Equals(arg, "--configuration", StringComparison.OrdinalIgnoreCase))
                {
                    options.Configuration = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--platform", StringComparison.OrdinalIgnoreCase))
                {
                    options.Platform = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--revit-year", StringComparison.OrdinalIgnoreCase))
                {
                    options.RevitYear = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--addin-id", StringComparison.OrdinalIgnoreCase))
                {
                    options.AddInId = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--assembly", StringComparison.OrdinalIgnoreCase))
                {
                    options.AssemblyPath = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--project-root", StringComparison.OrdinalIgnoreCase))
                {
                    options.ProjectRoot = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--license-config", StringComparison.OrdinalIgnoreCase))
                {
                    options.LicenseConfigPath = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--license-server-url", StringComparison.OrdinalIgnoreCase))
                {
                    options.LicenseServerUrl = RequireValue(arg, next);
                    i++;
                    continue;
                }

                if (string.Equals(arg, "--skip-license-config", StringComparison.OrdinalIgnoreCase))
                {
                    options.DeployLicenseConfig = false;
                    continue;
                }

                if (string.Equals(arg, "--force-license-config", StringComparison.OrdinalIgnoreCase))
                {
                    options.ForceLicenseConfig = true;
                    continue;
                }

                if (string.Equals(arg, "--all-users", StringComparison.OrdinalIgnoreCase))
                {
                    options.AllUsers = true;
                    continue;
                }

                if (string.Equals(arg, "--unlock-test-users", StringComparison.OrdinalIgnoreCase))
                {
                    options.UnlockTestUsers = true;
                    options.DeployLicenseConfig = true;
                    options.ForceLicenseConfig = true;
                    continue;
                }

                if (string.Equals(arg, "--no-ui", StringComparison.OrdinalIgnoreCase))
                {
                    options.UseUi = false;
                    continue;
                }

                if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    options.ShowHelp = true;
                    continue;
                }

                throw new ArgumentException("Unknown argument: " + arg);
            }

            return options;
        }

        private static string RequireValue(string arg, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing value for " + arg);
            }

            return value;
        }

        private static string ResolveProjectRoot(string explicitRoot, string revitYear)
        {
            if (!string.IsNullOrWhiteSpace(explicitRoot))
            {
                string full = Path.GetFullPath(explicitRoot);
                if (Directory.Exists(full))
                {
                    return full;
                }
            }

            string byCurrent = TryResolveProjectRootByTemplate(Environment.CurrentDirectory, revitYear);
            if (!string.IsNullOrWhiteSpace(byCurrent))
            {
                return byCurrent;
            }

            string byExe = TryResolveProjectRootByTemplate(AppDomain.CurrentDomain.BaseDirectory ?? "", revitYear);
            if (!string.IsNullOrWhiteSpace(byExe))
            {
                return byExe;
            }

            return "";
        }

        private static string TryResolveProjectRootByTemplate(string startDir, string revitYear)
        {
            if (string.IsNullOrWhiteSpace(startDir))
            {
                return "";
            }

            DirectoryInfo dir;
            try
            {
                string full = Path.GetFullPath(startDir);
                dir = new DirectoryInfo(full);
            }
            catch
            {
                return "";
            }

            while (dir != null)
            {
                string templatePath = Path.Combine(dir.FullName, GetManifestTemplateRelativePath(revitYear));
                if (File.Exists(templatePath))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return "";
        }

        private static string ResolveTemplateContent(string projectRoot, string revitYear, out string templateSource, string logPath)
        {
            if (!string.IsNullOrWhiteSpace(projectRoot))
            {
                string templatePath = Path.Combine(projectRoot, GetManifestTemplateRelativePath(revitYear));
                if (File.Exists(templatePath))
                {
                    try
                    {
                        templateSource = templatePath;
                        return File.ReadAllText(templatePath);
                    }
                    catch (Exception ex)
                    {
                        WriteLog(logPath, "Template read failed from project root: " + ex.Message);
                    }
                }
            }

            templateSource = "embedded template";
            return GetEmbeddedManifestTemplate(revitYear);
        }

        private static string ResolveAssemblyPath(string projectRoot, Options options, string logPath)
        {
            if (!string.IsNullOrWhiteSpace(options.AssemblyPath))
            {
                string full = Path.GetFullPath(options.AssemblyPath);
                if (!File.Exists(full))
                {
                    throw new FileNotFoundException("Assembly path does not exist.", full);
                }

                return full;
            }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string currentDir = Environment.CurrentDirectory ?? "";

            var candidates = new List<string>();
            AddAssemblyCandidates(candidates, exeDir, options);
            AddAssemblyCandidates(candidates, currentDir, options);
            AddAssemblyCandidates(candidates, projectRoot, options);

            List<string> existing = candidates
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existing.Count > 0)
            {
                return existing.OrderByDescending(File.GetLastWriteTimeUtc).First();
            }

            string checkedPaths = string.Join("\r\n", candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => " - " + p));
            WriteLog(logPath, "Assembly auto-detect failed. Paths checked:\r\n" + checkedPaths);

            if (options.UseUi)
            {
                using (var dialog = new OpenFileDialog())
                {
                    string assemblyFileName = GetAssemblyFileName(options.RevitYear);
                    dialog.Title = "Select " + assemblyFileName;
                    dialog.Filter = "MHNK DLL (" + assemblyFileName + ")|" + assemblyFileName + "|DLL files (*.dll)|*.dll|All files (*.*)|*.*";
                    dialog.CheckFileExists = true;
                    dialog.Multiselect = false;

                    string startFolder = !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
                        ? projectRoot
                        : (!string.IsNullOrWhiteSpace(currentDir) && Directory.Exists(currentDir) ? currentDir : exeDir);

                    if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
                    {
                        dialog.InitialDirectory = startFolder;
                    }

                    DialogResult result = dialog.ShowDialog();
                    if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName) && File.Exists(dialog.FileName))
                    {
                        return Path.GetFullPath(dialog.FileName);
                    }
                }
            }

            throw new FileNotFoundException(
                "Could not find " + GetAssemblyFileName(options.RevitYear) + " automatically. " +
                "Place the EXE in the project folder or select the DLL when prompted.");
        }

        private static void AddAssemblyCandidates(List<string> candidates, string baseDir, Options options)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(baseDir))
            {
                return;
            }

            try
            {
                baseDir = Path.GetFullPath(baseDir);
            }
            catch
            {
                return;
            }

            string assemblyFileName = GetAssemblyFileName(options.RevitYear);
            candidates.Add(Path.Combine(baseDir, assemblyFileName));

            if (!string.IsNullOrWhiteSpace(options.Platform) &&
                !string.Equals(options.Platform, "AnyCPU", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(baseDir, "bin", options.Platform, options.Configuration, assemblyFileName));
                candidates.Add(Path.Combine(baseDir, "bin", "Revit" + options.RevitYear, options.Platform, options.Configuration, assemblyFileName));
            }

            candidates.Add(Path.Combine(baseDir, "bin", options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "x64", options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "AnyCPU", options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "Revit" + options.RevitYear, options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "Revit" + options.RevitYear, "x64", options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "Revit" + options.RevitYear, "AnyCPU", options.Configuration, assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "Debug", assemblyFileName));
            candidates.Add(Path.Combine(baseDir, "bin", "Release", assemblyFileName));
        }

        private static string BuildManifest(string templateContent, string assemblyPath, string addInId, string revitYear)
        {
            string xml = templateContent ?? GetEmbeddedManifestTemplate(revitYear);
            xml = xml.Replace("{{ASSEMBLY_PATH}}", assemblyPath ?? "");
            xml = xml.Replace("{{ADDIN_ID}}", addInId ?? DefaultAddInId);
            return xml;
        }

        private static string WriteManifest(string manifestContent, string revitYear, bool allUsers)
        {
            string appData = Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonApplicationData : Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                throw new InvalidOperationException((allUsers ? "ProgramData" : "APPDATA") + " is not available.");
            }

            string revitAddinsDir = Path.Combine(appData, "Autodesk", "Revit", "Addins", revitYear);
            Directory.CreateDirectory(revitAddinsDir);

            string manifestPath = Path.Combine(revitAddinsDir, GetManifestFileName(revitYear));
            TryNormalizeManifestAttributes(manifestPath);

            try
            {
                File.WriteAllText(manifestPath, manifestContent ?? "");
                return manifestPath;
            }
            catch
            {
                // Retry using temp + copy replacement to handle some file-write edge cases.
                string tempPath = manifestPath + ".tmp";
                try
                {
                    File.WriteAllText(tempPath, manifestContent ?? "");
                    TryNormalizeManifestAttributes(manifestPath);
                    File.Copy(tempPath, manifestPath, true);
                    return manifestPath;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void TryNormalizeManifestAttributes(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return;
            }

            try
            {
                File.SetAttributes(manifestPath, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        private static string DeployLicenseConfig(string projectRoot, Options options, string logPath)
        {
            if (options == null || !options.DeployLicenseConfig)
            {
                return "";
            }

            string targetPath = ResolveProgramDataLicensePath();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                throw new InvalidOperationException("ProgramData is not available for online-license.config.json.");
            }

            string targetDir = Path.GetDirectoryName(targetPath) ?? "";
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (options.UnlockTestUsers)
            {
                WriteUnlockedTestLicenseConfig(targetPath, options.RevitYear);
                WriteLog(logPath, "License config set to unlocked test mode: " + targetPath);
                return targetPath;
            }

            bool targetExists = File.Exists(targetPath);
            if (!targetExists || options.ForceLicenseConfig)
            {
                string sourcePath = ResolveLicenseTemplatePath(projectRoot, options, logPath);
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    if (!targetExists)
                    {
                        throw new FileNotFoundException("No license config template found to deploy.", targetPath);
                    }
                }
                else
                {
                    File.Copy(sourcePath, targetPath, true);
                    TrySetProductCodeForRevitYear(targetPath, options.RevitYear, logPath);
                    WriteLog(logPath, "License config copied to ProgramData from: " + sourcePath);
                    targetExists = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(options.LicenseServerUrl))
            {
                if (!File.Exists(targetPath))
                {
                    throw new FileNotFoundException("License config was not created.", targetPath);
                }

                SetServerUrlInJsonFile(targetPath, options.LicenseServerUrl.Trim());
                WriteLog(logPath, "License config server_url set to: " + options.LicenseServerUrl.Trim());
            }

            return targetPath;
        }

        private static string ResolveLicenseTemplatePath(string projectRoot, Options options, string logPath)
        {
            if (options != null && !string.IsNullOrWhiteSpace(options.LicenseConfigPath))
            {
                string explicitPath = Path.GetFullPath(options.LicenseConfigPath);
                if (!File.Exists(explicitPath))
                {
                    throw new FileNotFoundException("License config path does not exist.", explicitPath);
                }

                return explicitPath;
            }

            var candidates = new List<string>();
            AddLicenseConfigCandidates(candidates, projectRoot);
            AddLicenseConfigCandidates(candidates, Environment.CurrentDirectory ?? "");
            AddLicenseConfigCandidates(candidates, AppDomain.CurrentDomain.BaseDirectory ?? "");

            string found = candidates
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .FirstOrDefault(File.Exists);

            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }

            if (candidates.Count > 0)
            {
                WriteLog(logPath, "License config template not found. Paths checked:\r\n" +
                                 string.Join("\r\n", candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => " - " + p)));
            }

            return "";
        }

        private static void AddLicenseConfigCandidates(List<string> candidates, string baseDir)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(baseDir))
            {
                return;
            }

            string root;
            try
            {
                root = Path.GetFullPath(baseDir);
            }
            catch
            {
                return;
            }

            candidates.Add(Path.Combine(root, LicenseConfigRelativePath));
            candidates.Add(Path.Combine(root, LicenseGoogleSampleRelativePath));
            candidates.Add(Path.Combine(root, LicenseTemplateRelativePath));
            candidates.Add(Path.Combine(root, LicenseConfigFileName));
        }

        private static string ResolveProgramDataLicensePath()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (string.IsNullOrWhiteSpace(programData))
            {
                return "";
            }

            return Path.Combine(programData, "CamboBIM", LicenseConfigFileName);
        }

        private static void SetServerUrlInJsonFile(string jsonFilePath, string serverUrl)
        {
            string json = File.ReadAllText(jsonFilePath);
            string escaped = EscapeJsonString(serverUrl ?? "");
            string replacement = "\"server_url\": \"" + escaped + "\"";

            string pattern = "\"server_url\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"";
            if (Regex.IsMatch(json, pattern, RegexOptions.IgnoreCase))
            {
                json = Regex.Replace(json, pattern, replacement, RegexOptions.IgnoreCase);
            }
            else
            {
                Match openObject = Regex.Match(json, "\\{\\s*", RegexOptions.Singleline);
                if (!openObject.Success)
                {
                    throw new InvalidOperationException("License config is not a valid JSON object.");
                }

                int insertPos = openObject.Index + openObject.Length;
                string trailing = json.Substring(insertPos).TrimStart();
                bool hasProperties = !trailing.StartsWith("}", StringComparison.Ordinal);

                string insertion = hasProperties
                    ? replacement + ",\r\n  "
                    : replacement + "\r\n";

                json = json.Insert(insertPos, insertion);
            }

            File.WriteAllText(jsonFilePath, json);
        }

        private static void WriteUnlockedTestLicenseConfig(string jsonFilePath, string revitYear)
        {
            string year = string.IsNullOrWhiteSpace(revitYear) ? DefaultRevitYear : revitYear.Trim();
            string json =
                "{\r\n" +
                "  \"enabled\": false,\r\n" +
                "  \"server_url\": \"\",\r\n" +
                "  \"product_code\": \"CBIM_RVT" + EscapeJsonString(year) + "_EXTENSION\",\r\n" +
                "  \"activate_endpoint\": \"?action=activate\",\r\n" +
                "  \"heartbeat_endpoint\": \"?action=heartbeat\",\r\n" +
                "  \"release_endpoint\": \"?action=release\",\r\n" +
                "  \"change_password_endpoint\": \"?action=change_password\",\r\n" +
                "  \"support_contact_email\": \"support@cambobim.com\",\r\n" +
                "  \"support_contact_phone\": \"+855 69 901 004\",\r\n" +
                "  \"support_contact_telegram\": \"@CamboBIMSupport\",\r\n" +
                "  \"timeout_seconds\": 15,\r\n" +
                "  \"heartbeat_seconds\": 300\r\n" +
                "}\r\n";

            File.WriteAllText(jsonFilePath, json);
        }

        private static void TrySetProductCodeForRevitYear(string jsonFilePath, string revitYear, string logPath)
        {
            if (string.IsNullOrWhiteSpace(jsonFilePath) ||
                string.IsNullOrWhiteSpace(revitYear) ||
                string.Equals(revitYear, "2024", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(jsonFilePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonFilePath);
                string replacement = "\"product_code\": \"CBIM_RVT" + revitYear.Trim() + "_EXTENSION\"";
                string pattern = "\"product_code\"\\s*:\\s*\"CBIM_RVT2024_EXTENSION\"";
                if (Regex.IsMatch(json, pattern, RegexOptions.IgnoreCase))
                {
                    json = Regex.Replace(json, pattern, replacement, RegexOptions.IgnoreCase);
                    File.WriteAllText(jsonFilePath, json);
                }
            }
            catch (Exception ex)
            {
                WriteLog(logPath, "Could not update license product_code for Revit " + revitYear + ": " + ex.Message);
            }
        }

        private static string EscapeJsonString(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static List<string> DisableDuplicateManifests(string deployedManifestPath, string revitYear, string addInId, string logPath)
        {
            var warnings = new List<string>();

            string userDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins",
                revitYear);

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string machineDir = Path.Combine(programData, "Autodesk", "Revit", "Addins", revitYear);

            var scanDirs = new[] { userDir, machineDir }
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            string markerPattern = "<AddInId>\\s*" + Regex.Escape(addInId ?? "") + "\\s*</AddInId>";

            foreach (string dir in scanDirs)
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }

                    foreach (string candidatePath in Directory.GetFiles(dir, "*.addin"))
                    {
                        if (PathEquals(candidatePath, deployedManifestPath))
                        {
                            continue;
                        }

                        bool disable = false;
                        string reason = "";

                        string fileName = Path.GetFileName(candidatePath) ?? "";
                        if (string.Equals(fileName, "CamboBIM.addin", StringComparison.OrdinalIgnoreCase))
                        {
                            disable = true;
                            reason = "legacy CamboBIM manifest";
                        }
                        else
                        {
                            string raw;
                            try
                            {
                                raw = File.ReadAllText(candidatePath);
                            }
                            catch (Exception ex)
                            {
                                warnings.Add("Cannot read manifest: " + candidatePath + " (" + ex.Message + ")");
                                continue;
                            }

                            if (Regex.IsMatch(raw, markerPattern, RegexOptions.IgnoreCase))
                            {
                                disable = true;
                                reason = "duplicate AddInId " + addInId;
                            }
                        }

                        if (!disable)
                        {
                            continue;
                        }

                        string warning;
                        if (!TryDisableManifestFile(candidatePath, reason, out warning))
                        {
                            warnings.Add(warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add("Cannot scan directory: " + dir + " (" + ex.Message + ")");
                }
            }

            if (warnings.Count > 0)
            {
                WriteLog(logPath, "Duplicate-manifest warnings:\r\n" + string.Join("\r\n", warnings));
            }

            return warnings;
        }

        private static bool TryDisableManifestFile(string pathToDisable, string reason, out string warning)
        {
            warning = "";

            string disabledPath = pathToDisable + ".disabled";
            if (File.Exists(disabledPath))
            {
                string stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                disabledPath = pathToDisable + "." + stamp + ".disabled";
            }

            try
            {
                File.Move(pathToDisable, disabledPath);
                return true;
            }
            catch (Exception ex)
            {
                warning = "Failed to disable manifest '" + pathToDisable + "' (" + reason + "). " +
                          "Run as Administrator if needed. Error: " + ex.Message;
                return false;
            }
        }

        private static bool PathEquals(string left, string right)
        {
            string l = NormalizePath(left);
            string r = NormalizePath(right);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return path.Trim().TrimEnd('\\');
            }
        }

        private static void WriteLog(string logPath, string text)
        {
            try
            {
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + (text ?? "") + "\r\n";
                File.AppendAllText(logPath, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void ShowInfo(string message)
        {
            MessageBox.Show(message ?? "", "MHNK Deploy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void ShowError(string message)
        {
            MessageBox.Show(message ?? "", "MHNK Deploy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
