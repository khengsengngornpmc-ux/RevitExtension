using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Grid = System.Windows.Controls.Grid;
using RevitResult = Autodesk.Revit.UI.Result;

namespace CamboBIM.Revit2024.Addin
{
    internal enum MhnkRuntimeAction
    {
        Cancel,
        DryRun,
        Run
    }

    internal static class MhnkArcCommandRuntime
    {
        public static RevitResult Execute(
            MhnkArcContext context,
            string toolName,
            MhnkArcCommandOption option,
            IntPtr revitMainWindowHandle)
        {
            MhnkRuntimePreflight preflight = MhnkRuntimePreflight.Build(context, toolName, option);
            var previewWindow = new MhnkRuntimePreviewWindow(preflight, revitMainWindowHandle);
            bool? accepted = previewWindow.ShowDialog();
            if (accepted != true || previewWindow.SelectedAction == MhnkRuntimeAction.Cancel)
            {
                return RevitResult.Cancelled;
            }

            MhnkRuntimeSnapshot before = MhnkRuntimeSnapshot.Capture(context);
            if (previewWindow.SelectedAction == MhnkRuntimeAction.DryRun)
            {
                string dryRunPath = WriteReport(preflight, before, before, RevitResult.Succeeded, TimeSpan.Zero, "Dry run only. No Revit model changes were executed.");
                MhnkArcValidationStore.RecordRuntime(
                    option,
                    "Dry Run",
                    dryRunPath,
                    preflight.DocumentTitle,
                    preflight.ActiveViewName);
                if (previewWindow.ShowReportAfterRun)
                {
                    new MhnkRuntimeResultWindow(preflight, before, before, RevitResult.Succeeded, TimeSpan.Zero, dryRunPath, "Dry run complete.", revitMainWindowHandle).ShowDialog();
                }

                return RevitResult.Succeeded;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            RevitResult result = RevitResult.Cancelled;
            string notes = "";
            bool wrapped = false;

            try
            {
                if (preflight.UseTransactionGroup)
                {
                    result = RunInTransactionGroupOrDirect(context.Document, preflight, option, out wrapped);
                }
                else
                {
                    result = option.Run(context);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                MhnkRuntimeSnapshot failedAfter = MhnkRuntimeSnapshot.Capture(context);
                string failedPath = WriteReport(preflight, before, failedAfter, RevitResult.Failed, stopwatch.Elapsed, "Runtime exception: " + ex.Message);
                MhnkArcValidationStore.RecordRuntime(
                    option,
                    "Failed",
                    failedPath,
                    preflight.DocumentTitle,
                    preflight.ActiveViewName);
                if (previewWindow.ShowReportAfterRun)
                {
                    new MhnkRuntimeResultWindow(preflight, before, failedAfter, RevitResult.Failed, stopwatch.Elapsed, failedPath, ex.Message, revitMainWindowHandle).ShowDialog();
                }

                throw;
            }

            stopwatch.Stop();
            MhnkRuntimeSnapshot after = MhnkRuntimeSnapshot.Capture(context);
            if (wrapped)
            {
                notes = "Undo group: MHNK - " + option.Title;
            }

            string reportPath = WriteReport(preflight, before, after, result, stopwatch.Elapsed, notes);
            MhnkArcValidationStore.RecordRuntime(
                option,
                result.ToString(),
                reportPath,
                preflight.DocumentTitle,
                preflight.ActiveViewName);
            if (previewWindow.ShowReportAfterRun)
            {
                new MhnkRuntimeResultWindow(preflight, before, after, result, stopwatch.Elapsed, reportPath, notes, revitMainWindowHandle).ShowDialog();
            }

            return result;
        }

        private static RevitResult RunInTransactionGroupOrDirect(
            Document document,
            MhnkRuntimePreflight preflight,
            MhnkArcCommandOption option,
            out bool wrapped)
        {
            wrapped = false;
            TransactionGroup group;
            try
            {
                group = new TransactionGroup(document, "MHNK - " + option.Title);
                group.Start();
            }
            catch
            {
                return option.Run(preflight.Context);
            }

            try
            {
                wrapped = true;
                RevitResult result = option.Run(preflight.Context);
                if (result == RevitResult.Succeeded)
                {
                    group.Assimilate();
                }
                else
                {
                    group.RollBack();
                }

                return result;
            }
            catch
            {
                try
                {
                    group.RollBack();
                }
                catch
                {
                    // The original command exception is more important than rollback cleanup.
                }

                throw;
            }
            finally
            {
                group.Dispose();
            }
        }

        private static string WriteReport(
            MhnkRuntimePreflight preflight,
            MhnkRuntimeSnapshot before,
            MhnkRuntimeSnapshot after,
            RevitResult result,
            TimeSpan elapsed,
            string notes)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "Reports");
            Directory.CreateDirectory(folder);
            string fileName = "MHNK_CommandRuntime_" + Sanitize(preflight.Option.Title) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".html";
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, BuildHtmlReport(preflight, before, after, result, elapsed, notes), Encoding.UTF8);
            return path;
        }

        private static string BuildHtmlReport(
            MhnkRuntimePreflight preflight,
            MhnkRuntimeSnapshot before,
            MhnkRuntimeSnapshot after,
            RevitResult result,
            TimeSpan elapsed,
            string notes)
        {
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>MHNK Runtime Report</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2a38}h1{font-size:22px}.grid{display:grid;grid-template-columns:220px 1fr;gap:8px 14px}.card{border:1px solid #d7dee8;padding:14px;margin:12px 0;background:#f8fafc}.risk{font-weight:700}</style></head><body>");
            html.AppendLine("<h1>MHNK Command Runtime Report</h1>");
            html.AppendLine("<div class=\"card\"><div class=\"grid\">");
            AddRow(html, "Tool", preflight.Option.Category + " / " + preflight.Option.Title);
            AddRow(html, "Tool ID", preflight.ToolId);
            AddRow(html, "Production Panel", preflight.PanelKind);
            AddRow(html, "Status", result.ToString());
            AddRow(html, "Risk", preflight.Risk);
            AddRow(html, "Source Mode", preflight.SourceModeName);
            AddRow(html, "Supported Modes", preflight.SupportedSourceModes);
            AddRow(html, "Generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AddRow(html, "Elapsed", elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " seconds");
            AddRow(html, "Undo Group", preflight.UseTransactionGroup ? "Enabled" : "Not used");
            AddRow(html, "Notes", notes ?? "");
            html.AppendLine("</div></div>");

            html.AppendLine("<div class=\"card\"><h2>Preflight</h2><div class=\"grid\">");
            AddRow(html, "Document", preflight.DocumentTitle);
            AddRow(html, "Active View", preflight.ActiveViewName);
            AddRow(html, "Selection", preflight.SelectionCount + " selected element(s)");
            AddRow(html, "Visible Elements", preflight.VisibleElementCount + " visible element(s)");
            AddRow(html, "Warnings", preflight.WarningCount + " model warning(s)");
            AddRow(html, "Input", preflight.InputSummary);
            AddRow(html, "Expected Result", preflight.ExpectedResult);
            AddRow(html, "Live Retrieve", preflight.LiveRetrieveScope);
            AddRow(html, "Preview Required", preflight.RequiresPreviewBeforeRun ? "Yes" : "Recommended");
            AddRow(html, "Live Preview", preflight.PreviewSummary);
            AddRow(html, "Preview Rows", preflight.PreviewReadyCount.ToString(CultureInfo.InvariantCulture) + " ready / " + preflight.PreviewSkippedCount.ToString(CultureInfo.InvariantCulture) + " skipped");
            AddRow(html, "Validation", string.Join("; ", preflight.ValidationMessages.ToArray()));
            html.AppendLine("</div></div>");
            AppendPreviewTable(html, preflight);

            html.AppendLine("<div class=\"card\"><h2>Before / After Snapshot</h2><div class=\"grid\">");
            AddRow(html, "Selection", before.SelectionCount + " -> " + after.SelectionCount);
            AddRow(html, "Warnings", before.WarningCount + " -> " + after.WarningCount);
            AddRow(html, "Visible Elements", before.VisibleElementCount + " -> " + after.VisibleElementCount);
            AddRow(html, "Active View", before.ActiveViewName + " -> " + after.ActiveViewName);
            html.AppendLine("</div></div>");

            html.AppendLine("</body></html>");
            return html.ToString();
        }

        private static void AppendPreviewTable(StringBuilder html, MhnkRuntimePreflight preflight)
        {
            if (preflight?.PreviewRows == null || preflight.PreviewRows.Count == 0)
            {
                return;
            }

            html.AppendLine("<div class=\"card\"><h2>Live Preview Rows</h2>");
            html.AppendLine("<table style=\"border-collapse:collapse;width:100%;font-size:12px\"><thead><tr>");
            html.AppendLine("<th style=\"text-align:left;border-bottom:1px solid #d7dee8;padding:6px\">Item</th>");
            html.AppendLine("<th style=\"text-align:left;border-bottom:1px solid #d7dee8;padding:6px\">Mode</th>");
            html.AppendLine("<th style=\"text-align:left;border-bottom:1px solid #d7dee8;padding:6px\">Target</th>");
            html.AppendLine("<th style=\"text-align:left;border-bottom:1px solid #d7dee8;padding:6px\">Status</th>");
            html.AppendLine("<th style=\"text-align:left;border-bottom:1px solid #d7dee8;padding:6px\">Detail</th>");
            html.AppendLine("</tr></thead><tbody>");

            foreach (MhnkArcPreviewRow row in preflight.PreviewRows.Take(30))
            {
                html.AppendLine("<tr>");
                AddCell(html, row.Item);
                AddCell(html, row.ModeText);
                AddCell(html, row.TargetType);
                AddCell(html, row.Status);
                AddCell(html, row.DetailText);
                html.AppendLine("</tr>");
            }

            html.AppendLine("</tbody></table></div>");
        }

        private static void AddCell(StringBuilder html, string value)
        {
            html.Append("<td style=\"border-bottom:1px solid #eef2f7;padding:6px;vertical-align:top\">")
                .Append(Encode(value))
                .AppendLine("</td>");
        }

        private static void AddRow(StringBuilder html, string label, string value)
        {
            html.Append("<div><strong>").Append(Encode(label)).Append("</strong></div><div>")
                .Append(Encode(value)).AppendLine("</div>");
        }

        private static string Sanitize(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "Tool" : value;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }

            return text.Replace(' ', '_');
        }

        private static string Encode(string value)
        {
            return (value ?? "")
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }

    internal sealed class MhnkRuntimePreflight
    {
        private MhnkRuntimePreflight()
        {
            ValidationMessages = new List<string>();
            PreviewRows = new List<MhnkArcPreviewRow>();
        }

        public MhnkArcContext Context { get; private set; }
        public MhnkArcCommandOption Option { get; private set; }
        public string ToolName { get; private set; }
        public string DocumentTitle { get; private set; }
        public string ActiveViewName { get; private set; }
        public int SelectionCount { get; private set; }
        public int VisibleElementCount { get; private set; }
        public int WarningCount { get; private set; }
        public MhnkArcSourceMode SourceMode { get; private set; }
        public string SourceModeName { get; private set; }
        public string Risk { get; private set; }
        public string InputSummary { get; private set; }
        public string ExpectedResult { get; private set; }
        public string ToolId { get; private set; }
        public string PanelKind { get; private set; }
        public string SupportedSourceModes { get; private set; }
        public string LiveRetrieveScope { get; private set; }
        public string PreviewSummary { get; private set; }
        public int PreviewReadyCount { get; private set; }
        public int PreviewSkippedCount { get; private set; }
        public bool RequiresPreviewBeforeRun { get; private set; }
        public bool UseTransactionGroup { get; private set; }
        public IList<string> ValidationMessages { get; private set; }
        public IList<MhnkArcPreviewRow> PreviewRows { get; private set; }

        public static MhnkRuntimePreflight Build(MhnkArcContext context, string toolName, MhnkArcCommandOption option)
        {
            MhnkRuntimeSnapshot snapshot = MhnkRuntimeSnapshot.Capture(context);
            MhnkArcPreviewResult preview = MhnkArcPreviewService.Retrieve(context);
            var preflight = new MhnkRuntimePreflight
            {
                Context = context,
                Option = option,
                ToolName = toolName ?? "",
                DocumentTitle = snapshot.DocumentTitle,
                ActiveViewName = snapshot.ActiveViewName,
                SelectionCount = snapshot.SelectionCount,
                VisibleElementCount = snapshot.VisibleElementCount,
                WarningCount = snapshot.WarningCount,
                SourceMode = context.SourceMode,
                SourceModeName = context.SourceModeName,
                Risk = InferRisk(option),
                InputSummary = InferInput(option, snapshot.SelectionCount, context.SourceMode),
                ExpectedResult = InferResult(option),
                ToolId = option?.Metadata?.ToolId ?? "",
                PanelKind = option?.Metadata?.PanelKind ?? "",
                SupportedSourceModes = option?.Metadata?.SupportedSourceModeText ?? "",
                LiveRetrieveScope = option?.Metadata?.LiveRetrieveScope ?? "",
                PreviewSummary = preview?.Summary ?? "",
                PreviewReadyCount = preview?.ReadyCount ?? 0,
                PreviewSkippedCount = preview?.SkippedCount ?? 0,
                RequiresPreviewBeforeRun = option?.Metadata?.RequiresPreviewBeforeRun ?? false,
                UseTransactionGroup = ShouldUseTransactionGroup(option)
            };

            foreach (string validationMessage in BuildValidationMessages(option, snapshot, context.SourceMode))
            {
                preflight.ValidationMessages.Add(validationMessage);
            }

            if (preview != null)
            {
                foreach (MhnkArcPreviewRow row in preview.SourceRows.Concat(preview.TargetRows).Take(40))
                {
                    preflight.PreviewRows.Add(row);
                }
            }

            return preflight;
        }

        private static IEnumerable<string> BuildValidationMessages(
            MhnkArcCommandOption option,
            MhnkRuntimeSnapshot snapshot,
            MhnkArcSourceMode sourceMode)
        {
            var messages = new List<string>();
            string text = GetCommandText(option);
            bool selectionLikelyRequired = text.Contains("selected") || text.Contains("selection") || text.Contains("select ");
            if (selectionLikelyRequired && snapshot.SelectionCount == 0)
            {
                messages.Add("No current Revit selection. Tool may use the active view fallback or ask for selection.");
            }
            else
            {
                messages.Add("Selection scope captured.");
            }

            if (snapshot.WarningCount > 0)
            {
                messages.Add("Model has warnings; review if geometry operations behave unexpectedly.");
            }

            if (snapshot.VisibleElementCount == 0)
            {
                messages.Add("Active view has no visible model elements.");
            }

            if (sourceMode == MhnkArcSourceMode.ByLayer)
            {
                messages.Add("By Layer mode uses selected/visible CAD imports and ARC layer mapping where the tool supports CAD sources.");
            }
            else if (sourceMode == MhnkArcSourceMode.Category)
            {
                messages.Add("Category mode gathers applicable active-view model categories for this tool.");
            }
            else if (sourceMode == MhnkArcSourceMode.All)
            {
                messages.Add("All mode can collect every visible active-view candidate; review the preview before running.");
            }
            else
            {
                messages.Add("Free Select mode uses the current Revit selection first.");
            }

            if (ShouldUseTransactionGroup(option))
            {
                messages.Add("Undo grouping will be attempted for model-edit operations.");
            }
            else
            {
                messages.Add("Command is treated as report, manager, or interactive workflow.");
            }

            return messages;
        }

        private static string InferRisk(MhnkArcCommandOption option)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.Risk;
            }

            string text = GetCommandText(option);
            if (text.Contains("delete") || text.Contains("clean") || text.Contains("cut") || text.Contains("uncut") ||
                text.Contains("join") || text.Contains("unjoin") || text.Contains("switch") || text.Contains("batch") ||
                text.Contains("split") || text.Contains("lower") || text.Contains("rename"))
            {
                return "High";
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert") || text.Contains("pin") ||
                text.Contains("unpin") || text.Contains("set ") || text.Contains("reset") || text.Contains("color") ||
                text.Contains("hide") || text.Contains("isolate"))
            {
                return "Medium";
            }

            return "Low";
        }

        private static string InferInput(MhnkArcCommandOption option, int selectionCount, MhnkArcSourceMode sourceMode)
        {
            if (option?.Metadata != null)
            {
                return "Source mode: " + MhnkArcContext.GetSourceModeName(sourceMode) + ". " +
                       option.Metadata.RequiredInputText + " Current selection: " + selectionCount + ".";
            }

            string text = GetCommandText(option);
            string prefix = "Source mode: " + MhnkArcContext.GetSourceModeName(sourceMode) + ". ";
            if (text.Contains("manager") || text.Contains("dashboard") || text.Contains("settings") || text.Contains("report"))
            {
                return prefix + "No strict selection required. Current selection: " + selectionCount + ".";
            }

            if (text.Contains("cad"))
            {
                return prefix + "CAD import/curve source, CAD layer source, or active-view CAD source depending on mode. Current selection: " + selectionCount + ".";
            }

            if (text.Contains("room"))
            {
                return prefix + "Selected rooms, visible rooms, or all bounded rooms depending on mode. Current selection: " + selectionCount + ".";
            }

            return prefix + "Controlled selection or active-view fallback depending on tool. Current selection: " + selectionCount + ".";
        }

        private static string InferResult(MhnkArcCommandOption option)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.ExpectedResultText;
            }

            string text = GetCommandText(option);
            if (text.Contains("report") || text.Contains("dashboard") || text.Contains("check"))
            {
                return "A report, selected candidates, or review window.";
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert"))
            {
                return "New model elements or candidate elements may be created.";
            }

            if (InferRisk(option) == "High")
            {
                return "Existing model geometry or metadata may be modified.";
            }

            return "Current model/view state may be reviewed or updated.";
        }

        private static bool ShouldUseTransactionGroup(MhnkArcCommandOption option)
        {
            string text = GetCommandText(option);
            if (text.Contains("manager") || text.Contains("dashboard") || text.Contains("settings") ||
                text.Contains("guideline") || text.Contains("report") || text.Contains("diagnostics") ||
                text.Contains("validation center") || text.Contains("interaction center") ||
                text.Contains("check ") || text.Contains("candidate check"))
            {
                return false;
            }

            return InferRisk(option) == "Medium" || InferRisk(option) == "High";
        }

        private static string GetCommandText(MhnkArcCommandOption option)
        {
            return ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
        }
    }

    internal sealed class MhnkRuntimeSnapshot
    {
        public string DocumentTitle { get; private set; }
        public string ActiveViewName { get; private set; }
        public int SelectionCount { get; private set; }
        public int VisibleElementCount { get; private set; }
        public int WarningCount { get; private set; }

        public static MhnkRuntimeSnapshot Capture(MhnkArcContext context)
        {
            var snapshot = new MhnkRuntimeSnapshot();
            snapshot.DocumentTitle = context.Document?.Title ?? "";
            snapshot.ActiveViewName = context.ActiveView?.Name ?? "";
            snapshot.SelectionCount = SafeSelectionCount(context);
            snapshot.VisibleElementCount = SafeVisibleElementCount(context);
            snapshot.WarningCount = SafeWarningCount(context);
            return snapshot;
        }

        private static int SafeSelectionCount(MhnkArcContext context)
        {
            try
            {
                return context.UiDocument.Selection.GetElementIds().Count;
            }
            catch
            {
                return 0;
            }
        }

        private static int SafeVisibleElementCount(MhnkArcContext context)
        {
            try
            {
                return new FilteredElementCollector(context.Document, context.ActiveView.Id)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
            }
            catch
            {
                return 0;
            }
        }

        private static int SafeWarningCount(MhnkArcContext context)
        {
            try
            {
                return context.Document.GetWarnings().Count;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal sealed class MhnkRuntimePreviewWindow : Window
    {
        private readonly CheckBox _showReportCheck;

        public MhnkRuntimePreviewWindow(MhnkRuntimePreflight preflight, IntPtr ownerHandle)
        {
            SelectedAction = MhnkRuntimeAction.Cancel;

            Title = "MHNK Command Runtime";
            Width = 680;
            Height = 520;
            MinWidth = 600;
            MinHeight = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (ownerHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ownerHandle;
            }

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            root.Children.Add(BuildHeader(preflight));
            Grid.SetRow(root.Children[root.Children.Count - 1], 0);

            ScrollViewer scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel body = new StackPanel();
            scroll.Content = body;
            body.Children.Add(BuildWorkflowSection(preflight));
            body.Children.Add(BuildSection("Preflight", new[]
            {
                Pair("Group", preflight.Option.Category),
                Pair("Tool", preflight.Option.Title),
                Pair("Tool ID", preflight.ToolId),
                Pair("Panel", preflight.PanelKind),
                Pair("Risk", preflight.Risk),
                Pair("Source Mode", preflight.SourceModeName),
                Pair("Supported Modes", preflight.SupportedSourceModes),
                Pair("Document", preflight.DocumentTitle),
                Pair("Active View", preflight.ActiveViewName),
                Pair("Selection", preflight.SelectionCount + " selected element(s)"),
                Pair("Visible Elements", preflight.VisibleElementCount + " visible element(s)"),
                Pair("Warnings", preflight.WarningCount + " model warning(s)")
            }));
            body.Children.Add(BuildSection("Runtime Plan", new[]
            {
                Pair("Input", preflight.InputSummary),
                Pair("Expected Result", preflight.ExpectedResult),
                Pair("Live Retrieve", preflight.LiveRetrieveScope),
                Pair("Live Preview", preflight.PreviewSummary),
                Pair("Preview Rows", preflight.PreviewReadyCount.ToString(CultureInfo.InvariantCulture) + " ready / " + preflight.PreviewSkippedCount.ToString(CultureInfo.InvariantCulture) + " skipped"),
                Pair("Preview Required", preflight.RequiresPreviewBeforeRun ? "Yes" : "Recommended"),
                Pair("Undo Group", preflight.UseTransactionGroup ? "Enabled where Revit permits it" : "Not used for this workflow"),
                Pair("Validation", string.Join("; ", preflight.ValidationMessages.ToArray()))
            }));
            body.Children.Add(BuildPreviewRowsSection(preflight));
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            _showReportCheck = new CheckBox
            {
                Content = "Show report after run",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            footer.Children.Add(_showReportCheck);
            footer.Children.Add(new TextBlock
            {
                Text = "Start with Safe Dry Run.",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            footer.Children.Add(BuildActionButton("Safe Dry Run", () => CloseWith(MhnkRuntimeAction.DryRun), 124, true));
            footer.Children.Add(BuildActionButton("Run Model Change", () => CloseWith(MhnkRuntimeAction.Run), 142));
            footer.Children.Add(BuildActionButton("Cancel", () => CloseWith(MhnkRuntimeAction.Cancel), 88));
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            MhnkUiTheme.Apply(this);
        }

        public MhnkRuntimeAction SelectedAction { get; private set; }
        public bool ShowReportAfterRun => _showReportCheck.IsChecked == true;

        private static StackPanel BuildHeader(MhnkRuntimePreflight preflight)
        {
            StackPanel header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = "COMMAND PREFLIGHT",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            header.Children.Add(new TextBlock
            {
                Text = preflight.Option.Category + " / " + preflight.Option.Title,
                TextWrapping = TextWrapping.Wrap
            });
            return header;
        }

        private static Border BuildWorkflowSection(MhnkRuntimePreflight preflight)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Workflow",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            IList<RuntimeWorkflowStep> steps = BuildWorkflowSteps(preflight);
            panel.Children.Add(BuildWorkflowStepper(steps.Count));

            for (int i = 0; i < steps.Count; i++)
            {
                panel.Children.Add(BuildWorkflowStepCard(i + 1, steps[i]));
            }

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = panel
            };
        }

        private static UIElement BuildWorkflowStepper(int count)
        {
            WrapPanel panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            for (int i = 1; i <= count; i++)
            {
                panel.Children.Add(BuildStepCircle(i));
                if (i < count)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = ">",
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(6, 3, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
            }

            return panel;
        }

        private static UIElement BuildWorkflowStepCard(int number, RuntimeWorkflowStep step)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(BuildStepCircle(number));
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 0);

            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = step.Title,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            });
            text.Children.Add(new TextBlock
            {
                Text = step.Description,
                TextWrapping = TextWrapping.Wrap
            });

            Border card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Child = text
            };
            grid.Children.Add(card);
            Grid.SetColumn(card, 1);

            return grid;
        }

        private static Border BuildStepCircle(int number)
        {
            bool dark = MhnkUiTheme.IsDark;
            Brush background = CreateBrush(dark, 30, 64, 175, 219, 234, 254);
            Brush border = CreateBrush(dark, 96, 165, 250, 37, 99, 235);
            Brush foreground = CreateBrush(dark, 239, 246, 255, 30, 64, 175);

            return new Border
            {
                Tag = "MhnkThemePreserve",
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = background,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Tag = "MhnkThemePreserve",
                    Text = number.ToString(CultureInfo.InvariantCulture),
                    Foreground = foreground,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        private static IList<RuntimeWorkflowStep> BuildWorkflowSteps(MhnkRuntimePreflight preflight)
        {
            MhnkArcCommandOption option = preflight.Option;
            string category = option.Category ?? "";
            string text = ((option.Title ?? "") + " " + (option.Summary ?? "")).ToLowerInvariant();

            if (string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Prepare view", preflight.InputSummary,
                    "Choose scope", text.Contains("preset") ? "Load the saved preset or prepare the current selection to save." : "Confirm visible categories and the selection/filter rule.",
                    "Run filter", "Use Dry Run first when the scope is uncertain, then Run when ready.",
                    "Review result", preflight.ExpectedResult);
            }

            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    text.Contains("cad") ? "Prepare CAD" : "Prepare source",
                    preflight.InputSummary,
                    text.Contains("cad") ? "Review mapping" : "Set options",
                    text.Contains("cad") ? "Confirm layers, target Revit types, level, offsets, and creation settings." : "Confirm type, level, offset, and selected source elements.",
                    "Create model",
                    "Use Dry Run first, then Run to create elements inside the current Revit document.",
                    "Verify elements",
                    preflight.ExpectedResult);
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Select elements", preflight.InputSummary,
                    "Confirm order", "Check reference elements, target elements, and active view before editing.",
                    "Apply edit", "Use Run to apply the change inside one controlled Revit transaction.",
                    "Inspect model", "Review the edited elements immediately and undo if the scope is not correct.");
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Open 3D view", "Use a coordination or review view with ARC, MEP, and generated solids visible.",
                    "Check candidates", "Confirm selected hosts, linked elements, clash pairs, or opening candidates.",
                    "Run solid tool", "Use Dry Run first when checking candidates; Run only for approved solid actions.",
                    "Review report", preflight.ExpectedResult);
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Open project", preflight.InputSummary,
                    "Run quick tool", "Launch the audit, setup, manager, or report workflow.",
                    "Read result", preflight.ExpectedResult,
                    "Continue work", "Fix warnings, update settings, or move to the next ARC command.");
            }

            return Steps(
                "Prepare", preflight.InputSummary,
                "Preview", "Review validation messages and current Revit scope.",
                "Run", "Use Dry Run first when uncertain, then Run.",
                "Verify", preflight.ExpectedResult);
        }

        private static IList<RuntimeWorkflowStep> Steps(
            string title1,
            string description1,
            string title2,
            string description2,
            string title3,
            string description3,
            string title4,
            string description4)
        {
            return new List<RuntimeWorkflowStep>
            {
                new RuntimeWorkflowStep(title1, description1),
                new RuntimeWorkflowStep(title2, description2),
                new RuntimeWorkflowStep(title3, description3),
                new RuntimeWorkflowStep(title4, description4)
            };
        }

        private static Brush CreateBrush(
            bool dark,
            byte darkR,
            byte darkG,
            byte darkB,
            byte lightR,
            byte lightG,
            byte lightB)
        {
            return new SolidColorBrush(dark
                ? System.Windows.Media.Color.FromRgb(darkR, darkG, darkB)
                : System.Windows.Media.Color.FromRgb(lightR, lightG, lightB));
        }

        private static Border BuildPreviewRowsSection(MhnkRuntimePreflight preflight)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Live Preview Rows",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            IList<MhnkArcPreviewRow> rows = preflight?.PreviewRows ?? new List<MhnkArcPreviewRow>();
            if (rows.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "No live preview rows were captured.",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            else
            {
                foreach (MhnkArcPreviewRow row in rows.Take(12))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = (row.Item ?? "Candidate") + " | " + row.ModeText + " | " + row.Status + " | " + row.DetailText,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 5)
                    });
                }
            }

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = panel
            };
        }

        private static Border BuildSection(string title, IEnumerable<KeyValuePair<string, string>> rows)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int rowIndex = 0;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock titleText = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            grid.Children.Add(titleText);
            Grid.SetColumnSpan(titleText, 2);
            Grid.SetRow(titleText, rowIndex++);

            foreach (KeyValuePair<string, string> row in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                TextBlock label = new TextBlock
                {
                    Text = row.Key,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 12, 7)
                };
                TextBlock value = new TextBlock
                {
                    Text = row.Value,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 7)
                };
                grid.Children.Add(label);
                Grid.SetRow(label, rowIndex);
                Grid.SetColumn(label, 0);
                grid.Children.Add(value);
                Grid.SetRow(value, rowIndex);
                Grid.SetColumn(value, 1);
                rowIndex++;
            }

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
        }

        private static Button BuildActionButton(string text, Action action, double width, bool primary = false)
        {
            Button button = new Button
            {
                Content = text,
                Width = width,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0),
                FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal
            };
            button.Click += (_, __) => action();
            return button;
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value ?? "");
        }

        private void CloseWith(MhnkRuntimeAction action)
        {
            if (action == MhnkRuntimeAction.Run)
            {
                MessageBoxResult confirm = MessageBox.Show(
                    this,
                    "Run Model Change will execute this command in the current Revit document. Use Safe Dry Run first when you are testing.",
                    "MHNK Command Runtime",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            SelectedAction = action;
            DialogResult = action != MhnkRuntimeAction.Cancel;
            Close();
        }

        private sealed class RuntimeWorkflowStep
        {
            public RuntimeWorkflowStep(string title, string description)
            {
                Title = title ?? "";
                Description = description ?? "";
            }

            public string Title { get; }
            public string Description { get; }
        }
    }

    internal sealed class MhnkRuntimeResultWindow : Window
    {
        private readonly string _reportPath;

        public MhnkRuntimeResultWindow(
            MhnkRuntimePreflight preflight,
            MhnkRuntimeSnapshot before,
            MhnkRuntimeSnapshot after,
            RevitResult result,
            TimeSpan elapsed,
            string reportPath,
            string notes,
            IntPtr ownerHandle)
        {
            _reportPath = reportPath;

            Title = "MHNK Runtime Result";
            Width = 620;
            Height = 420;
            MinWidth = 560;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (ownerHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = ownerHandle;
            }

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            root.Children.Add(new TextBlock
            {
                Text = "RUNTIME RESULT",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            StackPanel body = new StackPanel();
            body.Children.Add(Line("Tool", preflight.Option.Category + " / " + preflight.Option.Title));
            body.Children.Add(Line("Status", result.ToString()));
            body.Children.Add(Line("Elapsed", elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + " seconds"));
            body.Children.Add(Line("Selection", before.SelectionCount + " -> " + after.SelectionCount));
            body.Children.Add(Line("Warnings", before.WarningCount + " -> " + after.WarningCount));
            body.Children.Add(Line("Visible Elements", before.VisibleElementCount + " -> " + after.VisibleElementCount));
            body.Children.Add(Line("Report", reportPath));
            if (!string.IsNullOrWhiteSpace(notes))
            {
                body.Children.Add(Line("Notes", notes));
            }

            root.Children.Add(body);
            Grid.SetRow(body, 1);

            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            footer.Children.Add(BuildButton("Open Report", OpenReport, 112));
            footer.Children.Add(BuildButton("Close", Close, 88));
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            MhnkUiTheme.Apply(this);
        }

        private static TextBlock Line(string label, string value)
        {
            return new TextBlock
            {
                Text = label + ": " + (value ?? ""),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static Button BuildButton(string text, Action action, double width)
        {
            Button button = new Button
            {
                Content = text,
                Width = width,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0)
            };
            button.Click += (_, __) => action();
            return button;
        }

        private void OpenReport()
        {
            if (!File.Exists(_reportPath))
            {
                MessageBox.Show(this, "Report file was not found.", "MHNK Runtime", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(_reportPath) { UseShellExecute = true });
        }
    }
}
