using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcValidationRecord
    {
        public string Category { get; set; }
        public string ToolTitle { get; set; }
        public string ToolId { get; set; }
        public string SourceGroup { get; set; }
        public string Risk { get; set; }
        public string RequiredInput { get; set; }
        public string ExpectedResult { get; set; }
        public string Summary { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public string LastTested { get; set; }
        public string TestDocument { get; set; }
        public string TestView { get; set; }
        public string LastExecuted { get; set; }
        public string ExecutionResult { get; set; }
        public string RuntimeReportPath { get; set; }
        public string RuntimeDocument { get; set; }
        public string RuntimeView { get; set; }
    }

    internal sealed class MhnkArcValidationFile
    {
        public MhnkArcValidationFile()
        {
            Records = new List<MhnkArcValidationRecord>();
        }

        public IList<MhnkArcValidationRecord> Records { get; set; }
    }

    internal sealed class MhnkArcValidationPackItem
    {
        public string ToolId { get; set; }
        public string Category { get; set; }
        public string SourceGroup { get; set; }
        public string ToolTitle { get; set; }
        public string Risk { get; set; }
        public string RequiredInput { get; set; }
        public string SupportedSourceModes { get; set; }
        public string RecommendedSourceMode { get; set; }
        public string LiveRetrieveScope { get; set; }
        public string PreviewColumns { get; set; }
        public string ResultColumns { get; set; }
        public string ExpectedResult { get; set; }
        public string TestModelSetup { get; set; }
        public string PassCriteria { get; set; }
    }

    internal sealed class MhnkArcValidationPack
    {
        public MhnkArcValidationPack()
        {
            Version = "ARC-vNext-1";
            GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Items = new List<MhnkArcValidationPackItem>();
        }

        public string Version { get; set; }
        public string GeneratedAt { get; set; }
        public string RevitVersion { get; set; }
        public IList<MhnkArcValidationPackItem> Items { get; set; }
    }

    internal static class MhnkArcValidationStore
    {
        public const string NotTested = "Not Tested";
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string Blocked = "Blocked";

        public static readonly string[] Statuses = { NotTested, Passed, Failed, Blocked };

        public static IList<MhnkArcValidationRecord> Load(IList<MhnkArcCommandOption> options)
        {
            MhnkArcValidationFile file = ReadFile();
            var savedByKey = (file.Records ?? new List<MhnkArcValidationRecord>())
                .Where(x => x != null)
                .GroupBy(x => GetKey(x.Category, x.ToolTitle), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var records = new List<MhnkArcValidationRecord>();
            foreach (MhnkArcCommandOption option in options ?? new List<MhnkArcCommandOption>())
            {
                if (option == null)
                {
                    continue;
                }

                MhnkArcValidationRecord saved;
                savedByKey.TryGetValue(GetKey(option.Category, option.Title), out saved);
                records.Add(new MhnkArcValidationRecord
                {
                    Category = option.Category,
                    ToolTitle = option.Title,
                    ToolId = option.Metadata?.ToolId ?? saved?.ToolId ?? "",
                    SourceGroup = option.Metadata?.SourceGroup ?? saved?.SourceGroup ?? "",
                    Risk = option.Metadata?.Risk ?? saved?.Risk ?? "",
                    RequiredInput = option.Metadata?.RequiredInputText ?? saved?.RequiredInput ?? "",
                    ExpectedResult = option.Metadata?.ExpectedResultText ?? saved?.ExpectedResult ?? "",
                    Summary = option.Summary,
                    Status = NormalizeStatus(saved?.Status),
                    Notes = saved?.Notes ?? "",
                    LastTested = saved?.LastTested ?? "",
                    TestDocument = saved?.TestDocument ?? "",
                    TestView = saved?.TestView ?? "",
                    LastExecuted = saved?.LastExecuted ?? "",
                    ExecutionResult = saved?.ExecutionResult ?? "",
                    RuntimeReportPath = saved?.RuntimeReportPath ?? "",
                    RuntimeDocument = saved?.RuntimeDocument ?? "",
                    RuntimeView = saved?.RuntimeView ?? ""
                });
            }

            return records;
        }

        public static void Save(IList<MhnkArcValidationRecord> records)
        {
            string path = GetDataPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, CamboBimJson.Serialize(new MhnkArcValidationFile
            {
                Records = (records ?? new List<MhnkArcValidationRecord>()).ToList()
            }));
        }

        public static void RecordRuntime(
            MhnkArcCommandOption option,
            string executionResult,
            string reportPath,
            string documentTitle,
            string activeViewName)
        {
            if (option == null)
            {
                return;
            }

            try
            {
                MhnkArcValidationFile file = ReadFile();
                if (file.Records == null)
                {
                    file.Records = new List<MhnkArcValidationRecord>();
                }

                MhnkArcValidationRecord record = file.Records.FirstOrDefault(x =>
                    string.Equals(x.Category, option.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ToolTitle, option.Title, StringComparison.OrdinalIgnoreCase));
                if (record == null)
                {
                    record = new MhnkArcValidationRecord
                    {
                        Category = option.Category,
                        ToolTitle = option.Title,
                        ToolId = option.Metadata?.ToolId ?? "",
                        SourceGroup = option.Metadata?.SourceGroup ?? "",
                        Risk = option.Metadata?.Risk ?? "",
                        RequiredInput = option.Metadata?.RequiredInputText ?? "",
                        ExpectedResult = option.Metadata?.ExpectedResultText ?? "",
                        Summary = option.Summary,
                        Status = NotTested,
                        Notes = ""
                    };
                    file.Records.Add(record);
                }

                record.Summary = option.Summary;
                record.ToolId = option.Metadata?.ToolId ?? record.ToolId ?? "";
                record.SourceGroup = option.Metadata?.SourceGroup ?? record.SourceGroup ?? "";
                record.Risk = option.Metadata?.Risk ?? record.Risk ?? "";
                record.RequiredInput = option.Metadata?.RequiredInputText ?? record.RequiredInput ?? "";
                record.ExpectedResult = option.Metadata?.ExpectedResultText ?? record.ExpectedResult ?? "";
                record.LastExecuted = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                record.ExecutionResult = executionResult ?? "";
                record.RuntimeReportPath = reportPath ?? "";
                record.RuntimeDocument = documentTitle ?? "";
                record.RuntimeView = activeViewName ?? "";
                Save(file.Records);
            }
            catch
            {
                // Evidence recording must never interrupt the Revit command itself.
            }
        }

        public static string ExportHtml(IList<MhnkArcValidationRecord> records)
        {
            string folder = Path.GetDirectoryName(GetDataPath());
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "MHNK_ARC_Workflow_Validation_Report.html");
            File.WriteAllText(path, BuildHtml(records), Encoding.UTF8);
            return path;
        }

        public static string ExportTestPack(IList<MhnkArcCommandOption> options, string revitVersion)
        {
            string folder = Path.GetDirectoryName(GetDataPath());
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "MHNK_ARC_Test_Pack.json");
            var pack = new MhnkArcValidationPack
            {
                RevitVersion = revitVersion ?? ""
            };

            foreach (MhnkArcCommandOption option in options ?? new List<MhnkArcCommandOption>())
            {
                MhnkArcToolMetadata metadata = option?.Metadata;
                if (option == null || metadata == null)
                {
                    continue;
                }

                pack.Items.Add(new MhnkArcValidationPackItem
                {
                    ToolId = metadata.ToolId,
                    Category = option.Category,
                    SourceGroup = metadata.SourceGroup,
                    ToolTitle = option.Title,
                    Risk = metadata.Risk,
                    RequiredInput = metadata.RequiredInputText,
                    SupportedSourceModes = metadata.SupportedSourceModeText,
                    RecommendedSourceMode = MhnkArcToolMetadataRules.GetSourceModeLabel(metadata.RecommendedSourceMode),
                    LiveRetrieveScope = metadata.LiveRetrieveScope,
                    PreviewColumns = string.Join(", ", metadata.PreviewColumns.ToArray()),
                    ResultColumns = string.Join(", ", metadata.ResultColumns.ToArray()),
                    ExpectedResult = metadata.ExpectedResultText,
                    TestModelSetup = BuildTestModelSetup(metadata),
                    PassCriteria = BuildPassCriteria(metadata)
                });
            }

            File.WriteAllText(path, CamboBimJson.Serialize(pack), Encoding.UTF8);
            return path;
        }

        public static string GetDataPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "Validation",
                "workflow-validation.json");
        }

        private static string BuildTestModelSetup(MhnkArcToolMetadata metadata)
        {
            if (metadata == null)
            {
                return "";
            }

            if (string.Equals(metadata.SourceGroup, MhnkArcToolMetadataRules.SourceGroupAutoCad, StringComparison.OrdinalIgnoreCase))
            {
                return "Use an ARC test model with one linked/imported CAD plan containing wall, floor, ceiling, room, opening, door, and window layers.";
            }

            if (string.Equals(metadata.SourceGroup, MhnkArcToolMetadataRules.SourceGroupSelectedItems, StringComparison.OrdinalIgnoreCase))
            {
                return "Use an ARC test model with known walls, floors, ceilings, and generic models; select only the intended test elements before running.";
            }

            if (string.Equals(metadata.SourceGroup, MhnkArcToolMetadataRules.SourceGroupReviewSetup, StringComparison.OrdinalIgnoreCase))
            {
                return "Use the standard ARC test model after loading mapping rules, required family types, several warnings, and at least one CAD import.";
            }

            return "Use an ARC test model with rooms, walls, floors, ceilings, doors, windows, MEP coordination elements, and generated MHNK solids visible in active views.";
        }

        private static string BuildPassCriteria(MhnkArcToolMetadata metadata)
        {
            if (metadata == null)
            {
                return "";
            }

            return "Retrieve shows live candidates; preview includes columns [" +
                   string.Join(", ", metadata.PreviewColumns.ToArray()) +
                   "]; skipped candidates include a reason; run result matches: " +
                   metadata.ExpectedResultText;
        }

        public static string NormalizeStatus(string status)
        {
            return Statuses.FirstOrDefault(x => string.Equals(x, status, StringComparison.OrdinalIgnoreCase)) ?? NotTested;
        }

        private static MhnkArcValidationFile ReadFile()
        {
            string path = GetDataPath();
            if (!File.Exists(path))
            {
                return new MhnkArcValidationFile();
            }

            try
            {
                return CamboBimJson.Deserialize<MhnkArcValidationFile>(File.ReadAllText(path)) ?? new MhnkArcValidationFile();
            }
            catch
            {
                return new MhnkArcValidationFile();
            }
        }

        private static string BuildHtml(IList<MhnkArcValidationRecord> records)
        {
            IList<MhnkArcValidationRecord> items = (records ?? new List<MhnkArcValidationRecord>())
                .OrderBy(x => GetCategoryOrder(x.Category))
                .ThenBy(x => x.ToolTitle)
                .ToList();
            int passed = Count(items, Passed);
            int failed = Count(items, Failed);
            int blocked = Count(items, Blocked);
            int pending = Count(items, NotTested);

            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            html.AppendLine("<title>MHNK ARC Workflow Validation Report</title>");
            html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:0;background:#f5f7fb;color:#1f2937}header{padding:28px 34px;background:#172033;color:#fff;border-bottom:4px solid #2f80ed}header h1{margin:0 0 8px;font-size:26px}main{max-width:1300px;margin:auto;padding:24px}.metrics{display:grid;grid-template-columns:repeat(5,minmax(130px,1fr));gap:10px;margin-bottom:20px}.metric{background:#fff;border:1px solid #d9e2ec;border-radius:6px;padding:12px}.metric strong{display:block;font-size:25px}.category{margin:24px 0 10px;font-size:21px}table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #d9e2ec}th,td{padding:9px 10px;border-bottom:1px solid #e5e7eb;text-align:left;vertical-align:top;font-size:13px}th{background:#eef3f8}.Passed{color:#047857;font-weight:600}.Failed{color:#b91c1c;font-weight:600}.Blocked{color:#b45309;font-weight:600}.Not-Tested{color:#64748b}footer{margin-top:22px;color:#64748b;font-size:12px}</style></head><body>");
            html.AppendLine("<header><h1>MHNK ARC Workflow Validation Report</h1><div>Manual eTLipse parity test record generated " + Encode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "</div></header><main>");
            html.AppendLine("<section class=\"metrics\"><div class=\"metric\"><span>Total</span><strong>" + items.Count + "</strong></div><div class=\"metric\"><span>Passed</span><strong>" + passed + "</strong></div><div class=\"metric\"><span>Failed</span><strong>" + failed + "</strong></div><div class=\"metric\"><span>Blocked</span><strong>" + blocked + "</strong></div><div class=\"metric\"><span>Not Tested</span><strong>" + pending + "</strong></div></section>");
            html.AppendLine("<p>This report records hands-on Revit validation. Runtime evidence is captured automatically, but a command is not parity-confirmed until it is marked <strong>Passed</strong> after comparison with the expected workflow.</p>");
            foreach (IGrouping<string, MhnkArcValidationRecord> group in items.GroupBy(x => x.Category))
            {
                html.AppendLine("<h2 class=\"category\">" + Encode(group.Key) + "</h2><table><thead><tr><th>Tool</th><th>Metadata</th><th>Status</th><th>Last Tested</th><th>Latest Execution Evidence</th><th>Document / View</th><th>Notes</th></tr></thead><tbody>");
                foreach (MhnkArcValidationRecord item in group)
                {
                    string statusClass = Encode((item.Status ?? "").Replace(" ", "-"));
                    string evidence = string.IsNullOrWhiteSpace(item.LastExecuted)
                        ? ""
                        : item.LastExecuted + " | " + item.ExecutionResult + " | " + Path.GetFileName(item.RuntimeReportPath ?? "");
                    string metadata = "ID: " + item.ToolId +
                                      "\nSource: " + item.SourceGroup +
                                      "\nRisk: " + item.Risk +
                                      "\nInput: " + item.RequiredInput +
                                      "\nExpected: " + item.ExpectedResult;
                    html.AppendLine("<tr><td><strong>" + Encode(item.ToolTitle) + "</strong><br>" + Encode(item.Summary) + "</td><td>" + Encode(metadata) + "</td><td class=\"" + statusClass + "\">" + Encode(item.Status) + "</td><td>" + Encode(item.LastTested) + "</td><td>" + Encode(evidence) + "</td><td>" + Encode(CombineContext(item)) + "</td><td>" + Encode(item.Notes) + "</td></tr>");
                }

                html.AppendLine("</tbody></table>");
            }

            html.AppendLine("<footer>Stored validation file: " + Encode(GetDataPath()) + "</footer></main></body></html>");
            return html.ToString();
        }

        private static int Count(IEnumerable<MhnkArcValidationRecord> records, string status)
        {
            return records.Count(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));
        }

        private static string CombineContext(MhnkArcValidationRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.TestDocument))
            {
                return "";
            }

            return record.TestDocument + (string.IsNullOrWhiteSpace(record.TestView) ? "" : " / " + record.TestView);
        }

        private static string GetKey(string category, string title)
        {
            return (category ?? "") + "|" + (title ?? "");
        }

        private static int GetCategoryOrder(string category)
        {
            string[] categories = { "Filter", "Creation", "Edition", "Solids", "Xpress" };
            for (int i = 0; i < categories.Length; i++)
            {
                if (string.Equals(categories[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return categories.Length;
        }

        private static string Encode(string value)
        {
            return WebUtility.HtmlEncode(value ?? "");
        }
    }

    internal sealed class MhnkArcValidationWindow : Window
    {
        private readonly IList<MhnkArcCommandOption> _options;
        private readonly IList<MhnkArcValidationRecord> _records;
        private readonly string _documentTitle;
        private readonly string _activeViewName;
        private readonly TextBlock _summaryText;
        private readonly TextBox _searchBox;
        private readonly ComboBox _categoryFilter;
        private readonly ComboBox _statusFilter;
        private readonly DataGrid _grid;
        private readonly TextBlock _selectedToolText;
        private readonly ComboBox _resultCombo;
        private readonly TextBox _notesBox;
        private readonly TextBlock _expectedCheckText;
        private readonly TextBlock _evidenceText;
        private readonly Button _guidelineButton;
        private readonly Button _saveButton;
        private readonly Button _openEvidenceButton;

        public MhnkArcValidationWindow(
            IList<MhnkArcCommandOption> options,
            string documentTitle,
            string activeViewName,
            IntPtr revitMainWindowHandle)
        {
            _options = options ?? new List<MhnkArcCommandOption>();
            _records = MhnkArcValidationStore.Load(_options);
            _documentTitle = documentTitle ?? "";
            _activeViewName = activeViewName ?? "";

            Title = "MHNK ARC Workflow Validation Center";
            Width = 1240;
            Height = 760;
            MinWidth = 980;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "MHNK ARC WORKFLOW VALIDATION",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 44, 58)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            _summaryText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(_summaryText);
            Grid.SetRow(_summaryText, 1);

            Grid filters = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(filters);
            Grid.SetRow(filters, 2);

            _categoryFilter = CreateFilterCombo();
            _categoryFilter.ItemsSource = new[] { "All Categories", "Filter", "Creation", "Edition", "Solids", "Xpress" };
            _categoryFilter.SelectedIndex = 0;
            filters.Children.Add(_categoryFilter);

            _statusFilter = CreateFilterCombo();
            _statusFilter.ItemsSource = new[] { "All Results", MhnkArcValidationStore.NotTested, MhnkArcValidationStore.Passed, MhnkArcValidationStore.Failed, MhnkArcValidationStore.Blocked };
            _statusFilter.SelectedIndex = 0;
            _statusFilter.Margin = new Thickness(0, 0, 10, 0);
            filters.Children.Add(_statusFilter);
            Grid.SetColumn(_statusFilter, 1);

            _searchBox = new TextBox
            {
                Height = 30,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            filters.Children.Add(_searchBox);
            Grid.SetColumn(_searchBox, 2);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Single,
                Margin = new Thickness(0, 0, 0, 10)
            };
            AddColumn(_grid, "Group", "Category", 120);
            AddColumn(_grid, "Tool", "ToolTitle", 250);
            AddColumn(_grid, "Validation", "Status", 108);
            AddColumn(_grid, "Last Run", "LastExecuted", 132);
            AddColumn(_grid, "Execution", "ExecutionResult", 100);
            AddColumn(_grid, "Notes", "Notes", 370);
            root.Children.Add(_grid);
            Grid.SetRow(_grid, 3);

            Border detail = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 231)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(detail);
            Grid.SetRow(detail, 4);

            Grid form = new Grid();
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            detail.Child = form;

            _selectedToolText = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 44, 58)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            form.Children.Add(_selectedToolText);
            Grid.SetColumnSpan(_selectedToolText, 4);

            TextBlock resultLabel = CreateLabel("Result");
            form.Children.Add(resultLabel);
            Grid.SetRow(resultLabel, 1);

            _resultCombo = CreateFilterCombo();
            _resultCombo.ItemsSource = MhnkArcValidationStore.Statuses;
            form.Children.Add(_resultCombo);
            Grid.SetRow(_resultCombo, 1);
            Grid.SetColumn(_resultCombo, 1);

            TextBlock notesLabel = CreateLabel("Notes");
            notesLabel.Margin = new Thickness(12, 4, 8, 4);
            form.Children.Add(notesLabel);
            Grid.SetRow(notesLabel, 1);
            Grid.SetColumn(notesLabel, 2);

            _notesBox = new TextBox
            {
                Height = 34,
                Padding = new Thickness(7, 5, 7, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            form.Children.Add(_notesBox);
            Grid.SetRow(_notesBox, 1);
            Grid.SetColumn(_notesBox, 3);

            TextBlock expectedLabel = CreateLabel("Check");
            expectedLabel.Margin = new Thickness(0, 8, 8, 4);
            form.Children.Add(expectedLabel);
            Grid.SetRow(expectedLabel, 2);

            _expectedCheckText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 4)
            };
            form.Children.Add(_expectedCheckText);
            Grid.SetRow(_expectedCheckText, 2);
            Grid.SetColumn(_expectedCheckText, 1);
            Grid.SetColumnSpan(_expectedCheckText, 3);

            TextBlock evidenceLabel = CreateLabel("Evidence");
            evidenceLabel.Margin = new Thickness(0, 5, 8, 0);
            form.Children.Add(evidenceLabel);
            Grid.SetRow(evidenceLabel, 3);

            _evidenceText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            };
            form.Children.Add(_evidenceText);
            Grid.SetRow(_evidenceText, 3);
            Grid.SetColumn(_evidenceText, 1);
            Grid.SetColumnSpan(_evidenceText, 3);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 5);

            _guidelineButton = CreateButton("Guideline", 108);
            _guidelineButton.Click += (_, __) => OpenGuideline();
            buttons.Children.Add(_guidelineButton);

            _openEvidenceButton = CreateButton("Open Evidence", 118);
            _openEvidenceButton.Click += (_, __) => OpenEvidence();
            buttons.Children.Add(_openEvidenceButton);

            _saveButton = CreateButton("Save Result", 118);
            _saveButton.Click += (_, __) => SaveSelectedResult();
            buttons.Children.Add(_saveButton);

            Button resetButton = CreateButton("Reset Result", 118);
            resetButton.Click += (_, __) => ResetSelectedResult();
            buttons.Children.Add(resetButton);

            Button exportButton = CreateButton("Export HTML", 118);
            exportButton.Click += (_, __) => ExportReport();
            buttons.Children.Add(exportButton);

            Button testPackButton = CreateButton("Test Pack", 104);
            testPackButton.Click += (_, __) => ExportTestPack();
            buttons.Children.Add(testPackButton);

            Button closeButton = new Button { Content = "Close", Width = 96, Height = 30 };
            closeButton.Click += (_, __) => Close();
            buttons.Children.Add(closeButton);

            _categoryFilter.SelectionChanged += (_, __) => RefreshGrid();
            _statusFilter.SelectionChanged += (_, __) => RefreshGrid();
            _searchBox.TextChanged += (_, __) => RefreshGrid();
            _grid.SelectionChanged += (_, __) => LoadSelectedRecord();

            RefreshSummary();
            RefreshGrid();
            MhnkUiTheme.Apply(this);
        }

        private void RefreshSummary()
        {
            int passed = _records.Count(x => x.Status == MhnkArcValidationStore.Passed);
            int failed = _records.Count(x => x.Status == MhnkArcValidationStore.Failed);
            int blocked = _records.Count(x => x.Status == MhnkArcValidationStore.Blocked);
            int pending = _records.Count(x => x.Status == MhnkArcValidationStore.NotTested);
            int withEvidence = _records.Count(x => !string.IsNullOrWhiteSpace(x.LastExecuted));
            _summaryText.Text =
                "Tool-by-tool Revit parity record | Total: " + _records.Count +
                " | Passed: " + passed +
                " | Failed: " + failed +
                " | Blocked: " + blocked +
                " | Not Tested: " + pending +
                " | Runtime Evidence: " + withEvidence + Environment.NewLine +
                "Current test context: " + _documentTitle + " / " + _activeViewName;
        }

        private void RefreshGrid()
        {
            MhnkArcValidationRecord selected = _grid.SelectedItem as MhnkArcValidationRecord;
            string category = _categoryFilter.SelectedItem as string;
            string status = _statusFilter.SelectedItem as string;
            string query = (_searchBox.Text ?? "").Trim();

            IEnumerable<MhnkArcValidationRecord> filtered = _records;
            if (!string.IsNullOrWhiteSpace(category) && category != "All Categories")
            {
                filtered = filtered.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(status) && status != "All Results")
            {
                filtered = filtered.Where(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(x =>
                    Contains(x.Category, query) ||
                    Contains(x.ToolTitle, query) ||
                    Contains(x.Notes, query) ||
                    Contains(x.ExecutionResult, query) ||
                    Contains(x.RuntimeDocument, query));
            }

            IList<MhnkArcValidationRecord> visible = filtered.ToList();
            _grid.ItemsSource = visible;
            _grid.SelectedItem = selected != null ? visible.FirstOrDefault(x => ReferenceEquals(x, selected)) : visible.FirstOrDefault();
            if (_grid.SelectedItem == null)
            {
                LoadSelectedRecord();
            }
        }

        private void LoadSelectedRecord()
        {
            MhnkArcValidationRecord record = _grid.SelectedItem as MhnkArcValidationRecord;
            bool selected = record != null;
            _guidelineButton.IsEnabled = selected;
            _saveButton.IsEnabled = selected;
            _resultCombo.IsEnabled = selected;
            _notesBox.IsEnabled = selected;
            _openEvidenceButton.IsEnabled = selected && !string.IsNullOrWhiteSpace(record.RuntimeReportPath) && File.Exists(record.RuntimeReportPath);
            _selectedToolText.Text = selected ? record.Category + " > " + record.ToolTitle : "No tool selected.";
            _resultCombo.SelectedItem = selected ? record.Status : MhnkArcValidationStore.NotTested;
            _notesBox.Text = selected ? record.Notes : "";
            _expectedCheckText.Text = selected ? GetExpectedCheck(record) : "";
            _evidenceText.Text = selected ? GetEvidenceSummary(record) : "";
        }

        private void SaveSelectedResult()
        {
            MhnkArcValidationRecord record = _grid.SelectedItem as MhnkArcValidationRecord;
            if (record == null)
            {
                return;
            }

            record.Status = MhnkArcValidationStore.NormalizeStatus(_resultCombo.SelectedItem as string);
            record.Notes = (_notesBox.Text ?? "").Trim();
            record.LastTested = record.Status == MhnkArcValidationStore.NotTested ? "" : DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            record.TestDocument = record.Status == MhnkArcValidationStore.NotTested ? "" : _documentTitle;
            record.TestView = record.Status == MhnkArcValidationStore.NotTested ? "" : _activeViewName;
            MhnkArcValidationStore.Save(_records);
            RefreshSummary();
            RefreshGrid();
        }

        private void ResetSelectedResult()
        {
            _resultCombo.SelectedItem = MhnkArcValidationStore.NotTested;
            _notesBox.Text = "";
            SaveSelectedResult();
        }

        private void OpenGuideline()
        {
            MhnkArcValidationRecord record = _grid.SelectedItem as MhnkArcValidationRecord;
            MhnkArcCommandOption option = record == null
                ? null
                : _options.FirstOrDefault(x =>
                    string.Equals(x.Category, record.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Title, record.ToolTitle, StringComparison.OrdinalIgnoreCase));
            if (option != null)
            {
                MhnkArcGuideline.Open(_options, option);
            }
        }

        private void OpenEvidence()
        {
            MhnkArcValidationRecord record = _grid.SelectedItem as MhnkArcValidationRecord;
            if (record == null || string.IsNullOrWhiteSpace(record.RuntimeReportPath) || !File.Exists(record.RuntimeReportPath))
            {
                MessageBox.Show(this, "No runtime report has been recorded for this tool yet.", "MHNK ARC Validation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = record.RuntimeReportPath, UseShellExecute = true });
        }

        private void ExportReport()
        {
            try
            {
                string path = MhnkArcValidationStore.ExportHtml(_records);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot export workflow validation report." + Environment.NewLine + ex.Message, "MHNK ARC Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportTestPack()
        {
            try
            {
                string path = MhnkArcValidationStore.ExportTestPack(_options, _documentTitle);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot export ARC test pack." + Environment.NewLine + ex.Message, "MHNK ARC Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static ComboBox CreateFilterCombo()
        {
            return new ComboBox
            {
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private static TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72)),
                Margin = new Thickness(0, 4, 8, 4)
            };
        }

        private static Button CreateButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
        }

        private static void AddColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = width
            });
        }

        private static bool Contains(string source, string query)
        {
            return (source ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetEvidenceSummary(MhnkArcValidationRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.LastExecuted))
            {
                return "No runtime evidence recorded. Run or Dry Run this tool from ARC Workspace first.";
            }

            string report = string.IsNullOrWhiteSpace(record.RuntimeReportPath)
                ? "No report file"
                : Path.GetFileName(record.RuntimeReportPath);
            string context = string.IsNullOrWhiteSpace(record.RuntimeDocument)
                ? ""
                : " | " + record.RuntimeDocument + (string.IsNullOrWhiteSpace(record.RuntimeView) ? "" : " / " + record.RuntimeView);
            return record.LastExecuted + " | " + record.ExecutionResult + " | " + report + context;
        }

        private static string GetExpectedCheck(MhnkArcValidationRecord record)
        {
            string text = ((record?.ToolTitle ?? "") + " " + (record?.Summary ?? "")).ToLowerInvariant();
            if (text.Contains("create walls by room"))
            {
                return "Confirm bounded rooms produce walls on the chosen level/type with height, offsets, join behavior, and Room Bounding matching the panel settings.";
            }

            if (text.Contains("create floors by room") || text.Contains("create ceilings by room") || text.Contains("multiple ceilings"))
            {
                return "Confirm only approved bounded rooms create one valid floor/ceiling boundary each with selected type, level/elevation, and offset.";
            }

            if (text.Contains("finish"))
            {
                return "Confirm checked host walls/rooms generate finishes on the requested internal/external or horizontal side, with correct type and offset.";
            }

            if (text.Contains("door") || text.Contains("window"))
            {
                return "Confirm markers find the correct host walls, family types, placement positions, side orientation, and window sill height.";
            }

            if (text.Contains("split walls") || text.Contains("lower walls"))
            {
                return "Confirm only intended walls change, using the selected split/ceiling reference height, target type, and offsets.";
            }

            if (text.Contains("join") || text.Contains("cut") || text.Contains("solid"))
            {
                return "Confirm retrieved/selected source and interacting targets are correct before accepting Join, Cut, Switch, or report results.";
            }

            if (string.Equals(record?.Category, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                return "Confirm selection/view results contain only the intended category, type, layer, parameter, or current view scope.";
            }

            if (string.Equals(record?.Category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                return "Confirm dashboard/report rows reflect the open model, current active view, warnings, mapping rules, and exported evidence.";
            }

            return "Run the command, compare model output with its guideline and reference workflow, then record Passed, Failed, or Blocked.";
        }
    }
}
