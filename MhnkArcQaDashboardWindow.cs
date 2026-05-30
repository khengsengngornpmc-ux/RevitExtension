using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcQaMetric
    {
        public string Area { get; set; }
        public string Name { get; set; }
        public string Value { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class MhnkArcQaItem
    {
        public string Index { get; set; }
        public string Category { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
        public string ElementIds { get; set; }
    }

    internal sealed class MhnkArcQaDashboardData
    {
        public MhnkArcQaDashboardData()
        {
            Title = "MHNK ARC QA Dashboard";
            Summary = "";
            Report = "";
            Metrics = new List<MhnkArcQaMetric>();
            Checks = new List<MhnkArcQaItem>();
            Warnings = new List<MhnkArcQaItem>();
            CadImports = new List<MhnkArcQaItem>();
            MappingRules = new List<MhnkArcQaItem>();
        }

        public string Title { get; set; }
        public string Summary { get; set; }
        public string Report { get; set; }
        public IList<MhnkArcQaMetric> Metrics { get; set; }
        public IList<MhnkArcQaItem> Checks { get; set; }
        public IList<MhnkArcQaItem> Warnings { get; set; }
        public IList<MhnkArcQaItem> CadImports { get; set; }
        public IList<MhnkArcQaItem> MappingRules { get; set; }
    }

    internal sealed class MhnkArcQaDashboardWindow : Window
    {
        private readonly MhnkArcQaDashboardData _data;

        public MhnkArcQaDashboardWindow(MhnkArcQaDashboardData data, IntPtr revitMainWindowHandle)
        {
            _data = data ?? new MhnkArcQaDashboardData();

            Title = _data.Title;
            Width = 1120;
            Height = 700;
            MinWidth = 880;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            Grid root = new Grid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "MHNK ARC QA DASHBOARD",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 44, 58)),
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            Border summaryBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 231)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = _data.Summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            TabControl tabs = new TabControl
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            tabs.Items.Add(CreateMetricTab("Overview", _data.Metrics));
            tabs.Items.Add(CreateItemTab("Checks", _data.Checks));
            tabs.Items.Add(CreateItemTab("Warnings", _data.Warnings));
            tabs.Items.Add(CreateItemTab("CAD Imports", _data.CadImports));
            tabs.Items.Add(CreateItemTab("Mapping Rules", _data.MappingRules));
            root.Children.Add(tabs);
            Grid.SetRow(tabs, 2);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Button exportButton = new Button
            {
                Content = "Export Report",
                Width = 118,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            exportButton.Click += (_, __) => ExportReport();
            buttons.Children.Add(exportButton);

            Button closeButton = new Button
            {
                Content = "Close",
                Width = 96,
                Height = 30
            };
            closeButton.Click += (_, __) => Close();
            buttons.Children.Add(closeButton);
            MhnkUiTheme.Apply(this);
        }

        private static TabItem CreateMetricTab(string header, IList<MhnkArcQaMetric> metrics)
        {
            DataGrid grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = metrics ?? new List<MhnkArcQaMetric>()
            };

            AddColumn(grid, "Area", "Area", 130);
            AddColumn(grid, "Metric", "Name", 230);
            AddColumn(grid, "Value", "Value", 120);
            AddColumn(grid, "Status", "Status", 100);
            AddColumn(grid, "Notes", "Notes", 460);

            return new TabItem { Header = header, Content = grid };
        }

        private static TabItem CreateItemTab(string header, IList<MhnkArcQaItem> items)
        {
            DataGrid grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = items ?? new List<MhnkArcQaItem>()
            };

            AddColumn(grid, "No.", "Index", 54);
            AddColumn(grid, "Category", "Category", 140);
            AddColumn(grid, "Status", "Status", 100);
            AddColumn(grid, "Detail", "Detail", 520);
            AddColumn(grid, "Element IDs", "ElementIds", 220);

            return new TabItem { Header = header, Content = grid };
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

        private void ExportReport()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "Reports");
            Directory.CreateDirectory(folder);
            string fileName = "MHNK_QA_Dashboard_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, _data.Report ?? "");
            MessageBox.Show(this, "QA report exported:" + Environment.NewLine + path, "MHNK Xpress", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
