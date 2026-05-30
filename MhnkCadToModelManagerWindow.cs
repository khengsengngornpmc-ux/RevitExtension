using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using RevitElementId = Autodesk.Revit.DB.ElementId;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkCadToModelScanItem
    {
        public MhnkCadToModelScanItem()
        {
            Include = true;
            Action = "";
            Rule = "";
            Layer = "";
            Target = "";
            SourceElements = "";
            CurveCount = 0;
            Status = "";
            Notes = "";
            SourceElementIds = new List<RevitElementId>();
        }

        public bool Include { get; set; }
        public string Action { get; set; }
        public string Rule { get; set; }
        public string Layer { get; set; }
        public string Target { get; set; }
        public string SourceElements { get; set; }
        public int CurveCount { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
        public IList<RevitElementId> SourceElementIds { get; set; }
    }

    internal sealed class MhnkCadToModelManagerWindow : Window
    {
        private readonly IList<MhnkCadToModelScanItem> _items;
        private readonly string _summary;
        private readonly DataGrid _grid;

        public MhnkCadToModelManagerWindow(
            string summary,
            IList<MhnkCadToModelScanItem> items,
            IntPtr revitMainWindowHandle)
        {
            _summary = summary ?? "";
            _items = items ?? new List<MhnkCadToModelScanItem>();

            Title = "MHNK CAD to Model Manager";
            Width = 1160;
            Height = 680;
            MinWidth = 900;
            MinHeight = 540;
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
                Text = "MHNK CAD TO MODEL MANAGER",
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
                Text = _summary,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = _items,
                SelectionMode = DataGridSelectionMode.Extended,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Run",
                Binding = new System.Windows.Data.Binding("Include"),
                Width = 54
            });
            AddColumn(_grid, "Action", "Action", 120);
            AddColumn(_grid, "Rule", "Rule", 160);
            AddColumn(_grid, "Layer", "Layer", 190);
            AddColumn(_grid, "Target", "Target", 170);
            AddColumn(_grid, "Sources", "SourceElements", 115);
            AddColumn(_grid, "Curves", "CurveCount", 74);
            AddColumn(_grid, "Status", "Status", 92);
            AddColumn(_grid, "Notes", "Notes", 260);
            root.Children.Add(_grid);
            Grid.SetRow(_grid, 2);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Button exportButton = new Button
            {
                Content = "Export Scan",
                Width = 108,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            exportButton.Click += (_, __) => ExportScan();
            buttons.Children.Add(exportButton);

            Button runButton = new Button
            {
                Content = "Run Selected",
                Width = 112,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = _items.Any(x => string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            };
            runButton.Click += (_, __) => RunSelected();
            buttons.Children.Add(runButton);

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

        public IList<MhnkCadToModelScanItem> SelectedScanItems { get; private set; }

        private static void AddColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = width,
                IsReadOnly = true
            });
        }

        private void RunSelected()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            IList<MhnkCadToModelScanItem> selected = _items
                .Where(x => x.Include && string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No ready scan rows are checked.", "MHNK Creation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedScanItems = selected;
            DialogResult = true;
            Close();
        }

        private void ExportScan()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "Reports");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "MHNK_CAD_To_Model_Scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

            var lines = new List<string>();
            lines.Add("MHNK CAD to Model Scan");
            lines.Add("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            lines.Add("");
            lines.Add(_summary);
            lines.Add("");
            foreach (MhnkCadToModelScanItem item in _items)
            {
                lines.Add("[" + item.Status + "] " + item.Action + " | " + item.Rule + " | " + item.Layer + " | curves=" + item.CurveCount + " | " + item.Notes);
            }

            File.WriteAllLines(path, lines);
            MessageBox.Show(this, "CAD scan exported:" + Environment.NewLine + path, "MHNK Creation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
