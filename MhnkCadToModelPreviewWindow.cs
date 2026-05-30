using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkCadPreviewItem
    {
        public string Index { get; set; }
        public string Source { get; set; }
        public string Layer { get; set; }
        public string Rule { get; set; }
        public string Target { get; set; }
        public string Quantity { get; set; }
        public string Status { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class MhnkCadToModelPreviewWindow : Window
    {
        private readonly IList<MhnkCadPreviewItem> _items;

        public MhnkCadToModelPreviewWindow(
            string title,
            string summary,
            IList<MhnkCadPreviewItem> items,
            IntPtr revitMainWindowHandle)
        {
            _items = items ?? new List<MhnkCadPreviewItem>();

            Title = title ?? "MHNK CAD Preview";
            Width = 920;
            Height = 620;
            MinWidth = 760;
            MinHeight = 500;
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
                Text = title ?? "MHNK CAD Preview",
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

            TextBlock summaryBlock = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            summaryBorder.Child = summaryBlock;

            DataGrid grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = _items,
                Margin = new Thickness(0, 0, 0, 10)
            };
            AddColumn(grid, "No.", "Index", 58);
            AddColumn(grid, "Source", "Source", 120);
            AddColumn(grid, "Layer", "Layer", 160);
            AddColumn(grid, "Rule", "Rule", 150);
            AddColumn(grid, "Target", "Target", 180);
            AddColumn(grid, "Qty", "Quantity", 70);
            AddColumn(grid, "Status", "Status", 90);
            AddColumn(grid, "Notes", "Notes", 220);
            root.Children.Add(grid);
            Grid.SetRow(grid, 2);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button createButton = new Button
            {
                Content = "Run Model Change",
                Width = 142,
                Height = 30,
                IsEnabled = _items.Any(x => string.Equals(x.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            };
            createButton.Click += (_, __) =>
            {
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(createButton);
            MhnkUiTheme.Apply(this);
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
    }
}
