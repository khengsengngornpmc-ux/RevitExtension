using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class IdentifyElementScheduleWindow : Window
    {
        private const string IgnoreField = "Ignore";
        private const string NameField = "Name";
        private const string TypeField = "Type";
        private const string WidthField = "Width";
        private const string LengthField = "Length";
        private const string HeightField = "Height";
        private const string ThicknessField = "Thickness";
        private const string WidthHeightField = "Width * Height (Radius/Diameter)";
        private const string HeightWidthField = "Height * Width (Radius/Diameter)";
        private const string WidthHeightSimpleField = "Width * Height";
        private const string HeightWidthSimpleField = "Height * Width";
        private const string LengthWidthField = "Length * Width";
        private const string WidthLengthField = "Width * Length";
        private const string LengthWidthHeightField = "Length * Width * Height";
        private const string HeightAboveFloorField = "Height above Floor";
        private const string CorrespondingZoneField = "Corresponding Floor";
        private const string SteelRatioField = "Steel Ratio";
        private const string SummaryInfoField = "Summary Info";
        private const string RemarksField = "Remarks";
        private const string ElementGroupColumn = "Column";
        private const string ElementGroupWall = "Wall";
        private const string ElementGroupOpening = "Door/Window Opening";
        private const string ElementGroupBeam = "Beam";
        private const string ElementGroupSlab = "Slab";
        private const string ElementGroupFoundation = "Foundation";

        private readonly bool _autoImportOnOpen;
        private readonly ObservableCollection<ScheduleColumnMapping> _mappings =
            new ObservableCollection<ScheduleColumnMapping>();
        private readonly List<ScheduleTableData> _worksheets = new List<ScheduleTableData>();
        private readonly List<ColumnSectionSpec> _resultSpecs = new List<ColumnSectionSpec>();
        private readonly List<WallSectionSpec> _resultWallSpecs = new List<WallSectionSpec>();
        private readonly List<BeamSectionSpec> _resultBeamSpecs = new List<BeamSectionSpec>();
        private readonly List<SlabSectionSpec> _resultSlabSpecs = new List<SlabSectionSpec>();

        private ComboBox _worksheetCombo;
        private ComboBox _elementGroupCombo;
        private ComboBox _elementTypeCombo;
        private ComboBox _unitCombo;
        private ComboBox _singleSectionCombo;
        private DataGrid _mappingGrid;
        private DataGridComboBoxColumn _mappingFieldColumn;
        private DataGrid _previewGrid;
        private TextBlock _statusText;
        private TextBlock _operationText;
        private ScheduleTableData _activeTable;

        public IdentifyElementScheduleWindow(
            IEnumerable<ColumnSectionSpec> currentSpecs,
            bool autoImportOnOpen,
            string initialElementGroup = ElementGroupColumn)
        {
            _autoImportOnOpen = autoImportOnOpen;
            ResultSpecs = _resultSpecs;

            Title = "Identify Element Schedule";
            Width = 1280;
            Height = 700;
            MinWidth = 980;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.White;

            BuildContent();
            SelectElementGroup(initialElementGroup);
            LoadInitialSchedule(currentSpecs, GetSelectedElementGroup());

            Loaded += (_, __) =>
            {
                if (_autoImportOnOpen)
                {
                    Dispatcher.BeginInvoke(new Action(ImportExcelFile), DispatcherPriority.ApplicationIdle);
                }
            };
        }

        public IdentifyElementScheduleWindow(
            IEnumerable<ColumnSectionSpec> currentSpecs,
            bool autoImportOnOpen,
            IEnumerable<IEnumerable<string>> scheduleRows,
            string worksheetName,
            string statusText,
            string initialElementGroup = ElementGroupColumn)
        {
            _autoImportOnOpen = autoImportOnOpen;
            ResultSpecs = _resultSpecs;

            Title = "Identify Element Schedule";
            Width = 1280;
            Height = 700;
            MinWidth = 980;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.White;

            BuildContent();
            SelectElementGroup(initialElementGroup);

            List<List<string>> rows = NormalizeRows(scheduleRows);
            if (rows.Count > 0)
            {
                UseWorksheets(new[]
                {
                    new ScheduleTableData(
                        string.IsNullOrWhiteSpace(worksheetName) ? "CAD Data" : worksheetName,
                        rows)
                }, string.IsNullOrWhiteSpace(statusText) ? "Loaded selected CAD data." : statusText);
            }
            else
            {
                LoadInitialSchedule(currentSpecs, GetSelectedElementGroup());
            }

            Loaded += (_, __) =>
            {
                if (_autoImportOnOpen)
                {
                    Dispatcher.BeginInvoke(new Action(ImportExcelFile), DispatcherPriority.ApplicationIdle);
                }
            };
        }

        public bool RequestedCadDataSelection { get; private set; }

        public IReadOnlyList<ColumnSectionSpec> ResultSpecs { get; }
        public IReadOnlyList<WallSectionSpec> ResultWallSpecs => _resultWallSpecs;
        public IReadOnlyList<BeamSectionSpec> ResultBeamSpecs => _resultBeamSpecs;
        public IReadOnlyList<SlabSectionSpec> ResultSlabSpecs => _resultSlabSpecs;
        public string ResultElementGroup { get; private set; } = "Column";
        public string ResultElementType { get; private set; } = "Column";
        public string RequestedCadDataElementGroup { get; private set; } = "Column";

        public IReadOnlyList<string> MappingOptions => GetMappingOptions(GetSelectedElementGroup(), GetSelectedElementType());

        private void BuildContent()
        {
            var root = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(14)
            };
            Content = root;

            UIElement footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            UIElement header = BuildHeader();
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            UIElement filters = BuildFilterRow();
            DockPanel.SetDock(filters, Dock.Top);
            root.Children.Add(filters);

            root.Children.Add(BuildMainArea());
        }

        private UIElement BuildHeader()
        {
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };

            toolbar.Children.Add(MakeToolbarButton("Import Excel File", OnImportExcelClick, 132));
            toolbar.Children.Add(MakeToolbarButton("Select CAD Data", OnSelectCadDataClick, 128));
            toolbar.Children.Add(MakeToolbarButton("Clear Import", OnClearImportClick, 110));
            toolbar.Children.Add(MakeSeparator());
            toolbar.Children.Add(MakeToolbarButton("Delete Row", OnDeleteRowsClick, 104));
            toolbar.Children.Add(MakeToolbarButton("Delete Column", OnDeleteColumnsClick, 122));
            toolbar.Children.Add(MakeSeparator());
            toolbar.Children.Add(MakeToolbarButton("Paste", OnPasteClick, 78));
            toolbar.Children.Add(MakeToolbarButton("Batch Replace", OnBatchReplaceClick, 120));

            return toolbar;
        }

        private UIElement BuildFilterRow()
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _worksheetCombo = MakeComboBox();
            _worksheetCombo.SelectionChanged += OnWorksheetChanged;
            _elementGroupCombo = MakeComboBox(
                ElementGroupColumn,
                ElementGroupWall,
                ElementGroupOpening,
                ElementGroupBeam,
                ElementGroupSlab,
                ElementGroupFoundation);
            _elementGroupCombo.SelectionChanged += OnElementGroupChanged;
            _elementTypeCombo = MakeComboBox();
            _elementTypeCombo.SelectionChanged += OnElementTypeChanged;
            _unitCombo = MakeComboBox("mm", "cm", "m");
            _singleSectionCombo = MakeComboBox("Radius", "Diameter");
            PopulateElementTypeCombo("Column");

            AddLabeledControl(row, 0, "Worksheet:", _worksheetCombo, 1);
            AddLabeledControl(row, 3, "Element Group:", _elementGroupCombo, 4);
            AddLabeledControl(row, 6, "Element Type:", _elementTypeCombo, 7);
            AddLabeledControl(row, 9, "Minimum Unit in Drawing:", _unitCombo, 10);
            AddLabeledControl(row, 12, "Identify Single-Section Data as:", _singleSectionCombo, 13);

            return row;
        }

        private UIElement BuildMainArea()
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(204, 212, 222)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };

            var grid = new Grid();
            border.Child = grid;
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(196) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _mappingGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeaderWidth = 0,
                ItemsSource = _mappings,
                Margin = new Thickness(0),
                BorderThickness = new Thickness(0)
            };

            _mappingGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Column",
                Binding = new Binding(nameof(ScheduleColumnMapping.ColumnNumber)),
                Width = 68,
                IsReadOnly = true
            });
            _mappingGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Source Header",
                Binding = new Binding(nameof(ScheduleColumnMapping.HeaderText)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            });
            _mappingFieldColumn = new DataGridComboBoxColumn
            {
                Header = "Corresponding Attribute",
                ItemsSource = MappingOptions,
                SelectedItemBinding = new Binding(nameof(ScheduleColumnMapping.TargetField))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                },
                Width = 260
            };
            _mappingGrid.Columns.Add(_mappingFieldColumn);
            _mappingGrid.CellEditEnding += (_, __) =>
                Dispatcher.BeginInvoke(new Action(UpdateOperationDetails), DispatcherPriority.Background);
            grid.Children.Add(_mappingGrid);

            var splitter = new GridSplitter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromRgb(234, 238, 244)),
                Height = 6
            };
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            _previewGrid = new DataGrid
            {
                AutoGenerateColumns = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeaderWidth = 0,
                BorderThickness = new Thickness(0)
            };
            Grid.SetRow(_previewGrid, 2);
            grid.Children.Add(_previewGrid);

            return border;
        }

        private UIElement BuildFooter()
        {
            var footer = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var details = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _operationText = new TextBlock
            {
                Text = "Operation steps details",
                Foreground = new SolidColorBrush(Color.FromRgb(70, 79, 92)),
                VerticalAlignment = VerticalAlignment.Center
            };
            details.Children.Add(_operationText);

            var info = new Button
            {
                Content = "i",
                Width = 20,
                Height = 20,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(0),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 119, 210)),
                ToolTip = "Show operation steps"
            };
            info.Click += OnOperationStepsClick;
            details.Children.Add(info);

            Grid.SetRow(details, 0);
            Grid.SetColumn(details, 0);
            footer.Children.Add(details);

            _statusText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(86, 100, 120)),
                Margin = new Thickness(0, 6, 18, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(_statusText, 1);
            Grid.SetColumn(_statusText, 0);
            footer.Children.Add(_statusText);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var identify = new Button
            {
                Content = "Identify",
                Width = 92,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(36, 122, 214)),
                Foreground = Brushes.White
            };
            identify.Click += OnIdentifyClick;
            buttons.Children.Add(identify);

            var cancel = new Button
            {
                Content = "Cancel",
                Width = 92,
                Height = 32
            };
            cancel.Click += (_, __) => Close();
            buttons.Children.Add(cancel);

            Grid.SetRow(buttons, 0);
            Grid.SetRowSpan(buttons, 2);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            return footer;
        }

        private static Button MakeToolbarButton(string text, RoutedEventHandler handler, double width)
        {
            var button = new Button
            {
                Content = IdentifySvgIconFactory.CreateIconText(text, IdentifySvgIconFactory.ResolveButtonIconFile(text), 14),
                Width = Math.Max(width, Math.Min(170, Math.Max(82, 34 + ((text ?? "").Length * 7)))),
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            button.Click += handler;
            return button;
        }

        private static UIElement MakeSeparator()
        {
            return new Border
            {
                Width = 1,
                Height = 24,
                Background = new SolidColorBrush(Color.FromRgb(199, 206, 216)),
                Margin = new Thickness(4, 2, 12, 2)
            };
        }

        private static ComboBox MakeComboBox(params string[] items)
        {
            var combo = new ComboBox
            {
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                IsReadOnly = true
            };

            foreach (string item in items)
            {
                combo.Items.Add(item);
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        private static void AddLabeledControl(Grid grid, int labelColumn, string label, Control control, int controlColumn)
        {
            var text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(58, 67, 81))
            };
            Grid.SetColumn(text, labelColumn);
            grid.Children.Add(text);

            Grid.SetColumn(control, controlColumn);
            grid.Children.Add(control);
        }

        private void LoadCurrentSpecs(IEnumerable<ColumnSectionSpec> currentSpecs)
        {
            var rows = new List<List<string>>
            {
                new List<string> { "MARK", "TYPE", "Corresponding Floor" }
            };

            if (currentSpecs != null)
            {
                foreach (ColumnSectionSpec spec in currentSpecs.Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name)))
                {
                    rows.Add(new List<string>
                    {
                        spec.Name.Trim(),
                        FormatSize(spec.WidthMm, spec.LengthMm),
                        "Zone-1[0]"
                    });
                }
            }

            UseWorksheets(new[]
            {
                new ScheduleTableData("Current", rows)
            }, "Current schedule data.");
        }

        private void LoadInitialSchedule(IEnumerable<ColumnSectionSpec> currentSpecs, string group)
        {
            if (IsElementGroup(group, ElementGroupColumn))
            {
                LoadCurrentSpecs(currentSpecs);
                return;
            }

            LoadBlankScheduleForGroup(group);
        }

        private void LoadBlankScheduleForGroup(string group)
        {
            var header = new List<string> { NameField, WidthHeightField, CorrespondingZoneField };
            if (IsElementGroup(group, ElementGroupWall))
            {
                header = new List<string> { NameField, ThicknessField, CorrespondingZoneField };
            }
            else if (IsElementGroup(group, ElementGroupOpening))
            {
                header = new List<string> { NameField, WidthHeightSimpleField, TypeField, CorrespondingZoneField };
            }
            else if (IsElementGroup(group, ElementGroupBeam))
            {
                header = new List<string> { NameField, WidthHeightSimpleField, CorrespondingZoneField };
            }
            else if (IsElementGroup(group, ElementGroupSlab))
            {
                header = new List<string> { NameField, ThicknessField, CorrespondingZoneField };
            }
            else if (IsElementGroup(group, ElementGroupFoundation))
            {
                header = new List<string> { NameField, LengthWidthField, CorrespondingZoneField };
            }

            UseWorksheets(new[]
            {
                new ScheduleTableData(GetSelectedElementGroup() + " Schedule", new List<List<string>> { header })
            }, "Select CAD Data, paste, or import an Excel/CSV schedule.");
        }

        private static List<List<string>> NormalizeRows(IEnumerable<IEnumerable<string>> scheduleRows)
        {
            var rows = new List<List<string>>();
            if (scheduleRows == null)
            {
                return rows;
            }

            foreach (IEnumerable<string> sourceRow in scheduleRows)
            {
                if (sourceRow == null)
                {
                    continue;
                }

                var row = sourceRow
                    .Select(cell => (cell ?? "").Trim())
                    .ToList();
                TrimTrailingEmpty(row);
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static List<List<string>> CloneRows(IEnumerable<List<string>> rows)
        {
            return rows == null
                ? new List<List<string>>()
                : rows.Select(row => row == null ? new List<string>() : new List<string>(row)).ToList();
        }

        private void OnImportExcelClick(object sender, RoutedEventArgs e)
        {
            ImportExcelFile();
        }

        private void ImportExcelFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Element Schedule Excel/CSV",
                Filter = "Excel files (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|CSV/Text (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            bool? ok = dialog.ShowDialog(this);
            if (ok != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                List<ScheduleTableData> tables = ReadScheduleTables(dialog.FileName);
                if (tables.Count == 0)
                {
                    SetStatus("No schedule rows were found in the selected file.");
                    return;
                }

                UseWorksheets(tables, "Loaded " + Path.GetFileName(dialog.FileName) + ".");
            }
            catch (Exception ex)
            {
                SetStatus("Import failed: " + ex.Message);
            }
        }

        private void OnSelectCadDataClick(object sender, RoutedEventArgs e)
        {
            RequestedCadDataSelection = true;
            RequestedCadDataElementGroup = GetSelectedElementGroup();
            DialogResult = false;
            Close();
        }

        private void OnClearImportClick(object sender, RoutedEventArgs e)
        {
            UseWorksheets(new[]
            {
                new ScheduleTableData("Blank", new List<List<string>>())
            }, "Import cleared.");
        }

        private void OnDeleteRowsClick(object sender, RoutedEventArgs e)
        {
            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                SetStatus("No schedule rows to delete.");
                return;
            }

            List<int> selectedRows = GetSelectedSourceRowIndices();
            if (selectedRows.Count == 0)
            {
                SetStatus("Select one or more rows in the schedule preview, then click Delete Row.");
                return;
            }

            List<List<string>> rows = CloneRows(_activeTable.Rows);
            foreach (int rowIndex in selectedRows.OrderByDescending(i => i))
            {
                if (rowIndex >= 0 && rowIndex < rows.Count)
                {
                    rows.RemoveAt(rowIndex);
                }
            }

            ReplaceActiveTableRows(
                rows,
                "Deleted " + selectedRows.Count.ToString(CultureInfo.InvariantCulture) + " schedule row(s).");
        }

        private void OnDeleteColumnsClick(object sender, RoutedEventArgs e)
        {
            if (_activeTable == null || _activeTable.ColumnCount == 0)
            {
                SetStatus("No schedule columns to delete.");
                return;
            }

            List<int> selectedColumns = GetSelectedSourceColumnIndices();
            if (selectedColumns.Count == 0)
            {
                SetStatus("Select column row(s) in the mapping table or cell(s) in the preview, then click Delete Column.");
                return;
            }

            List<List<string>> rows = CloneRows(_activeTable.Rows);
            foreach (List<string> row in rows)
            {
                foreach (int columnIndex in selectedColumns.OrderByDescending(i => i))
                {
                    if (columnIndex >= 0 && columnIndex < row.Count)
                    {
                        row.RemoveAt(columnIndex);
                    }
                }
            }

            ReplaceActiveTableRows(
                rows,
                "Deleted " + selectedColumns.Count.ToString(CultureInfo.InvariantCulture) + " schedule column(s).");
        }

        private void OnPasteClick(object sender, RoutedEventArgs e)
        {
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("Clipboard is empty.");
                return;
            }

            var rows = ParseDelimitedText(text);
            if (rows.Count == 0)
            {
                SetStatus("Clipboard does not contain table data.");
                return;
            }

            UseWorksheets(new[] { new ScheduleTableData("Clipboard", rows) }, "Loaded clipboard data.");
        }

        private void OnBatchReplaceClick(object sender, RoutedEventArgs e)
        {
            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                SetStatus("No imported data to replace.");
                return;
            }

            foreach (List<string> row in _activeTable.Rows)
            {
                for (int i = 0; i < row.Count; i++)
                {
                    row[i] = NormalizeScheduleCell(row[i]);
                }
            }

            RefreshPreview();
            SetStatus("Normalized schedule text.");
        }

        private void OnWorksheetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_worksheetCombo?.SelectedItem is ScheduleTableData table)
            {
                UseTable(table);
            }
        }

        private void OnElementGroupChanged(object sender, SelectionChangedEventArgs e)
        {
            string group = GetSelectedElementGroup();
            ResultElementGroup = group;
            PopulateElementTypeCombo(group);
            if (_activeTable != null)
            {
                BuildMappings();
            }
            else
            {
                RefreshMappingOptionsForActiveGroup();
            }

            UpdateOperationDetails();
            SetStatus("Element group set to " + group + ". Adjust mapping, then click Identify.");
        }

        private void OnElementTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            ResultElementGroup = GetSelectedElementGroup();
            ResultElementType = GetSelectedElementType();
            RefreshMappingOptionsForActiveGroup();
            UpdateOperationDetails();
        }

        private void PopulateElementTypeCombo(string group)
        {
            if (_elementTypeCombo == null)
            {
                return;
            }

            _elementTypeCombo.SelectionChanged -= OnElementTypeChanged;
            _elementTypeCombo.Items.Clear();
            foreach (string item in GetElementTypeOptions(group))
            {
                _elementTypeCombo.Items.Add(item);
            }

            if (_elementTypeCombo.Items.Count > 0)
            {
                _elementTypeCombo.SelectedIndex = 0;
            }

            _elementTypeCombo.SelectionChanged += OnElementTypeChanged;
            ResultElementType = GetSelectedElementType();
        }

        private void RefreshMappingOptionsForActiveGroup()
        {
            List<string> options = GetMappingOptions(GetSelectedElementGroup(), GetSelectedElementType()).ToList();
            if (_mappingFieldColumn != null)
            {
                _mappingFieldColumn.ItemsSource = options;
            }

            foreach (ScheduleColumnMapping mapping in _mappings)
            {
                if (!options.Any(option => string.Equals(option, mapping.TargetField, StringComparison.OrdinalIgnoreCase)))
                {
                    mapping.TargetField = IgnoreField;
                }
            }

            _mappingGrid?.Items.Refresh();
        }

        private void SelectElementGroup(string group)
        {
            if (_elementGroupCombo == null)
            {
                return;
            }

            string target = string.IsNullOrWhiteSpace(group) ? ElementGroupColumn : group;
            object match = null;
            foreach (object item in _elementGroupCombo.Items)
            {
                string text = Convert.ToString(item, CultureInfo.InvariantCulture) ?? "";
                if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase))
                {
                    match = item;
                    break;
                }
            }

            _elementGroupCombo.SelectedItem = match ?? (_elementGroupCombo.Items.Count > 0 ? _elementGroupCombo.Items[0] : null);
            ResultElementGroup = GetSelectedElementGroup();
            PopulateElementTypeCombo(ResultElementGroup);
        }

        private string GetSelectedElementGroup()
        {
            string group = Convert.ToString(_elementGroupCombo?.SelectedItem, CultureInfo.InvariantCulture) ?? "";
            return string.IsNullOrWhiteSpace(group) ? ElementGroupColumn : group;
        }

        private string GetSelectedElementType()
        {
            string type = Convert.ToString(_elementTypeCombo?.SelectedItem, CultureInfo.InvariantCulture) ?? "";
            return string.IsNullOrWhiteSpace(type) ? GetSelectedElementGroup() : type;
        }

        private static IEnumerable<string> GetElementTypeOptions(string group)
        {
            if (IsElementGroup(group, ElementGroupWall))
            {
                return new[] { "Wall", "RC Wall", "Curtain Wall", "Vertical Projection", "Horizontal Projection" };
            }

            if (IsElementGroup(group, ElementGroupOpening))
            {
                return new[] { "Door/Window", "Door", "Window", "DoorWin", "Wall Opening", "Ribbon Window", "Ribbon Opening", "Bay Window", "Dormer", "Lintel", "Niche", "Skylight" };
            }

            if (IsElementGroup(group, ElementGroupBeam))
            {
                return new[] { "Beam", "Main Beam", "Secondary Beam", "Coupling Beam", "Ring Beam", "Ground Beam" };
            }

            if (IsElementGroup(group, ElementGroupSlab))
            {
                return new[] { "In-situ Slab", "Precast Slab", "Spiral Slab", "Ramp", "Drop Panel", "Slab Opening" };
            }

            if (IsElementGroup(group, ElementGroupFoundation))
            {
                return new[] { "Pile Cap", "Pile", "Isolated Foundation", "Pad Foundation", "Strip Foundation", "Raft Foundation", "Sump Pit", "Blinding" };
            }

            return new[] { "Column", "Rectangular Column", "Circular Column", "Stiffener", "Corbel" };
        }

        private static IReadOnlyList<string> GetMappingOptions(string group, string elementType)
        {
            if (IsElementGroup(group, ElementGroupWall))
            {
                return new[]
                {
                    IgnoreField,
                    NameField,
                    ThicknessField,
                    CorrespondingZoneField,
                    SteelRatioField,
                    SummaryInfoField,
                    RemarksField
                };
            }

            if (IsElementGroup(group, ElementGroupOpening))
            {
                return new[]
                {
                    IgnoreField,
                    NameField,
                    WidthField,
                    HeightField,
                    WidthHeightSimpleField,
                    HeightWidthSimpleField,
                    TypeField,
                    CorrespondingZoneField,
                    HeightAboveFloorField,
                    SummaryInfoField,
                    RemarksField
                };
            }

            if (IsElementGroup(group, ElementGroupBeam))
            {
                return new[]
                {
                    IgnoreField,
                    NameField,
                    WidthField,
                    HeightField,
                    WidthHeightSimpleField,
                    HeightWidthSimpleField,
                    CorrespondingZoneField,
                    SteelRatioField,
                    SummaryInfoField,
                    RemarksField
                };
            }

            if (IsElementGroup(group, ElementGroupSlab))
            {
                return new[]
                {
                    IgnoreField,
                    NameField,
                    ThicknessField,
                    CorrespondingZoneField,
                    SteelRatioField,
                    SummaryInfoField,
                    RemarksField
                };
            }

            if (IsElementGroup(group, ElementGroupFoundation))
            {
                return new[]
                {
                    IgnoreField,
                    NameField,
                    LengthField,
                    WidthField,
                    HeightField,
                    LengthWidthField,
                    WidthLengthField,
                    LengthWidthHeightField,
                    CorrespondingZoneField,
                    SteelRatioField,
                    SummaryInfoField,
                    RemarksField
                };
            }

            return new[]
            {
                IgnoreField,
                NameField,
                WidthField,
                HeightField,
                WidthHeightField,
                HeightWidthField,
                CorrespondingZoneField,
                SteelRatioField,
                SummaryInfoField,
                RemarksField
            };
        }

        private static bool IsElementGroup(string group, string expected)
        {
            return string.Equals(group ?? "", expected ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private void OnOperationStepsClick(object sender, RoutedEventArgs e)
        {
            string group = GetSelectedElementGroup();
            string supported =
                IsElementGroup(group, ElementGroupColumn) ||
                IsElementGroup(group, ElementGroupWall) ||
                IsElementGroup(group, ElementGroupBeam) ||
                IsElementGroup(group, ElementGroupSlab)
                    ? "This group can update its Revit section table."
                    : "This group is visible for TAS workflow planning, but Revit generation is not connected yet.";

            MessageBox.Show(
                this,
                "1. Choose Element Group and Element Type.\n" +
                "2. Select CAD Data, paste table text, or import Excel/CSV.\n" +
                "3. Check the Corresponding Attribute mapping.\n" +
                "4. Click Identify to send the schedule rows back to the workspace.\n\n" +
                supported,
                "Identify Element Schedule",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnIdentifyClick(object sender, RoutedEventArgs e)
        {
            ResultElementGroup = GetSelectedElementGroup();
            ResultElementType = GetSelectedElementType();

            _resultSpecs.Clear();
            _resultWallSpecs.Clear();
            _resultBeamSpecs.Clear();
            _resultSlabSpecs.Clear();

            string message;
            if (IsElementGroup(ResultElementGroup, ElementGroupColumn))
            {
                if (!TryBuildColumnSpecs(out List<ColumnSectionSpec> specs, out message))
                {
                    SetStatus(message);
                    return;
                }

                _resultSpecs.AddRange(specs);
            }
            else if (IsElementGroup(ResultElementGroup, ElementGroupWall))
            {
                if (!TryBuildWallSpecs(out List<WallSectionSpec> specs, out message))
                {
                    SetStatus(message);
                    return;
                }

                _resultWallSpecs.AddRange(specs);
            }
            else if (IsElementGroup(ResultElementGroup, ElementGroupBeam))
            {
                if (!TryBuildBeamSpecs(out List<BeamSectionSpec> specs, out message))
                {
                    SetStatus(message);
                    return;
                }

                _resultBeamSpecs.AddRange(specs);
            }
            else if (IsElementGroup(ResultElementGroup, ElementGroupSlab))
            {
                if (!TryBuildSlabSpecs(out List<SlabSectionSpec> specs, out message))
                {
                    SetStatus(message);
                    return;
                }

                _resultSlabSpecs.AddRange(specs);
            }
            else
            {
                SetStatus(ResultElementGroup + " schedule identify is not connected to Revit generation yet.");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void UseWorksheets(IEnumerable<ScheduleTableData> tables, string status)
        {
            _worksheets.Clear();
            foreach (ScheduleTableData table in tables.Where(t => t != null))
            {
                _worksheets.Add(table);
            }

            _worksheetCombo.ItemsSource = null;
            _worksheetCombo.ItemsSource = _worksheets;
            _worksheetCombo.DisplayMemberPath = nameof(ScheduleTableData.Name);
            _worksheetCombo.SelectedIndex = _worksheets.Count > 0 ? 0 : -1;

            if (_worksheets.Count > 0)
            {
                UseTable(_worksheets[0]);
            }

            SetStatus(status);
        }

        private void UseTable(ScheduleTableData table)
        {
            _activeTable = table ?? new ScheduleTableData("Blank", new List<List<string>>());
            BuildMappings();
            RefreshPreview();
            UpdateOperationDetails();
        }

        private void ReplaceActiveTableRows(List<List<string>> rows, string status)
        {
            string name = string.IsNullOrWhiteSpace(_activeTable?.Name) ? "Schedule" : _activeTable.Name;
            var replacement = new ScheduleTableData(name, rows ?? new List<List<string>>());

            int index = _worksheets.FindIndex(table => ReferenceEquals(table, _activeTable));
            if (index >= 0)
            {
                _worksheets[index] = replacement;
            }
            else
            {
                _worksheets.Add(replacement);
            }

            if (_worksheetCombo != null)
            {
                _worksheetCombo.SelectionChanged -= OnWorksheetChanged;
                _worksheetCombo.ItemsSource = null;
                _worksheetCombo.ItemsSource = _worksheets;
                _worksheetCombo.DisplayMemberPath = nameof(ScheduleTableData.Name);
                _worksheetCombo.SelectedItem = replacement;
                _worksheetCombo.SelectionChanged += OnWorksheetChanged;
            }

            UseTable(replacement);
            SetStatus(status);
        }

        private List<int> GetSelectedSourceRowIndices()
        {
            var indices = new SortedSet<int>();
            if (_previewGrid == null)
            {
                return indices.ToList();
            }

            foreach (DataRowView rowView in _previewGrid.SelectedItems.OfType<DataRowView>())
            {
                AddPreviewRowIndex(indices, rowView);
            }

            if (indices.Count == 0)
            {
                foreach (DataGridCellInfo cell in _previewGrid.SelectedCells)
                {
                    if (cell.Item is DataRowView rowView)
                    {
                        AddPreviewRowIndex(indices, rowView);
                    }
                }
            }

            if (indices.Count == 0 && _previewGrid.CurrentItem is DataRowView current)
            {
                AddPreviewRowIndex(indices, current);
            }

            int rowCount = _activeTable?.Rows.Count ?? 0;
            return indices.Where(index => index >= 0 && index < rowCount).ToList();
        }

        private List<int> GetSelectedSourceColumnIndices()
        {
            var indices = new SortedSet<int>();

            if (_mappingGrid != null)
            {
                foreach (ScheduleColumnMapping mapping in _mappingGrid.SelectedItems.OfType<ScheduleColumnMapping>())
                {
                    AddMappedColumnIndex(indices, mapping);
                }
            }

            if (indices.Count == 0 && _previewGrid != null)
            {
                foreach (DataGridCellInfo cell in _previewGrid.SelectedCells)
                {
                    AddPreviewColumnIndex(indices, cell.Column);
                }

                if (indices.Count == 0)
                {
                    AddPreviewColumnIndex(indices, _previewGrid.CurrentCell.Column);
                }
            }

            int columnCount = _activeTable?.ColumnCount ?? 0;
            if (_previewGrid != null &&
                _previewGrid.SelectedItems.Count > 0 &&
                columnCount > 1 &&
                indices.Count >= columnCount)
            {
                indices.Clear();
                AddPreviewColumnIndex(indices, _previewGrid.CurrentCell.Column);
            }

            return indices.Where(index => index >= 0 && index < columnCount).ToList();
        }

        private static void AddPreviewRowIndex(SortedSet<int> indices, DataRowView rowView)
        {
            if (indices == null || rowView == null)
            {
                return;
            }

            string rowNumberText = Convert.ToString(rowView["Row"], CultureInfo.InvariantCulture) ?? "";
            if (int.TryParse(rowNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowNumber))
            {
                indices.Add(rowNumber - 1);
            }
        }

        private static void AddMappedColumnIndex(SortedSet<int> indices, ScheduleColumnMapping mapping)
        {
            if (indices == null || mapping == null)
            {
                return;
            }

            indices.Add(mapping.ColumnNumber - 1);
        }

        private static void AddPreviewColumnIndex(SortedSet<int> indices, DataGridColumn column)
        {
            if (indices == null || column == null)
            {
                return;
            }

            string header = Convert.ToString(column.Header, CultureInfo.InvariantCulture) ?? "";
            if (int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out int columnNumber))
            {
                indices.Add(columnNumber - 1);
            }
        }

        private void BuildMappings()
        {
            _mappings.Clear();
            RefreshMappingOptionsForActiveGroup();
            int columnCount = _activeTable?.ColumnCount ?? 0;
            List<string> header = FindHeaderRow(_activeTable?.Rows);

            for (int i = 0; i < columnCount; i++)
            {
                string headerText = i < header.Count ? header[i] : "";
                string sample = FindSampleCell(_activeTable.Rows, i);
                _mappings.Add(new ScheduleColumnMapping
                {
                    ColumnNumber = i + 1,
                    HeaderText = string.IsNullOrWhiteSpace(headerText) ? sample : headerText,
                    TargetField = InferMapping(i, headerText, sample)
                });
            }
        }

        private void RefreshPreview()
        {
            var table = new DataTable();
            table.Columns.Add("Row");

            int colCount = _activeTable?.ColumnCount ?? 0;
            for (int i = 0; i < colCount; i++)
            {
                table.Columns.Add((i + 1).ToString(CultureInfo.InvariantCulture));
            }

            if (_activeTable != null)
            {
                for (int r = 0; r < _activeTable.Rows.Count; r++)
                {
                    DataRow row = table.NewRow();
                    row[0] = (r + 1).ToString(CultureInfo.InvariantCulture);
                    for (int c = 0; c < colCount; c++)
                    {
                        row[c + 1] = GetCell(_activeTable.Rows[r], c);
                    }

                    table.Rows.Add(row);
                }
            }

            _previewGrid.ItemsSource = table.DefaultView;
        }

        private void UpdateOperationDetails()
        {
            int rowCount = Math.Max(0, (_activeTable?.Rows.Count ?? 0) - 1);
            string mapped = string.Join(", ", _mappings
                .Where(m => !string.Equals(m.TargetField, IgnoreField, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.ColumnNumber.ToString(CultureInfo.InvariantCulture) + "=" + m.TargetField));
            _operationText.Text = string.IsNullOrWhiteSpace(mapped)
                ? "Operation steps details"
                : "Operation steps details: " + rowCount + " source row(s), " + mapped;
        }

        private bool TryBuildColumnSpecs(out List<ColumnSectionSpec> specs, out string message)
        {
            specs = new List<ColumnSectionSpec>();
            message = "";

            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                message = "No schedule data is loaded.";
                return false;
            }

            int nameIndex = FindMappedColumn(NameField);
            int widthIndex = FindMappedColumn(WidthField);
            int heightIndex = FindMappedColumn(HeightField);
            int widthHeightIndex = FindMappedColumn(WidthHeightField);
            int heightWidthIndex = FindMappedColumn(HeightWidthField);

            if (nameIndex < 0)
            {
                message = "Map one column to Name.";
                return false;
            }

            if (widthHeightIndex < 0 && heightWidthIndex < 0 && (widthIndex < 0 || heightIndex < 0))
            {
                message = "Map TYPE to Width * Height, or map separate Width and Height columns.";
                return false;
            }

            double defaultFactor = GetSelectedUnitFactorMm();
            var byName = new Dictionary<string, ColumnSectionSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (List<string> row in _activeTable.Rows)
            {
                string name = CleanName(GetCell(row, nameIndex));
                if (string.IsNullOrWhiteSpace(name) || IsScheduleHeaderText(name))
                {
                    continue;
                }

                double widthMm;
                double heightMm;

                if (widthHeightIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, widthHeightIndex), false, defaultFactor, out widthMm, out heightMm))
                    {
                        continue;
                    }
                }
                else if (heightWidthIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, heightWidthIndex), true, defaultFactor, out widthMm, out heightMm))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!TryParseLength(GetCell(row, widthIndex), defaultFactor, out widthMm) ||
                        !TryParseLength(GetCell(row, heightIndex), defaultFactor, out heightMm))
                    {
                        continue;
                    }
                }

                if (widthMm <= 0 || heightMm <= 0)
                {
                    continue;
                }

                byName[name] = new ColumnSectionSpec
                {
                    Name = name,
                    WidthMm = widthMm,
                    LengthMm = heightMm
                };
            }

            specs.AddRange(byName.Values.OrderBy(s => NaturalSortKey(s.Name)));
            if (specs.Count == 0)
            {
                message = "No valid column rows were identified. Check Name and TYPE column mapping.";
                return false;
            }

            message = "Identified " + specs.Count.ToString(CultureInfo.InvariantCulture) + " column row(s).";
            return true;
        }

        private bool TryBuildWallSpecs(out List<WallSectionSpec> specs, out string message)
        {
            specs = new List<WallSectionSpec>();
            message = "";

            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                message = "No schedule data is loaded.";
                return false;
            }

            int nameIndex = FindMappedColumn(NameField);
            int widthIndex = FindMappedColumn(WidthField);
            int thicknessIndex = FindMappedColumn(ThicknessField);
            int widthHeightIndex = FindMappedColumn(WidthHeightField);
            int heightWidthIndex = FindMappedColumn(HeightWidthField);

            if (nameIndex < 0)
            {
                message = "Map one column to Name.";
                return false;
            }

            if (widthIndex < 0 && thicknessIndex < 0 && widthHeightIndex < 0 && heightWidthIndex < 0)
            {
                message = "Map wall TYPE to Width or Thickness, or map a size column to Width * Height.";
                return false;
            }

            double defaultFactor = GetSelectedUnitFactorMm();
            var byName = new Dictionary<string, WallSectionSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (List<string> row in _activeTable.Rows)
            {
                string name = CleanName(GetCell(row, nameIndex));
                if (string.IsNullOrWhiteSpace(name) || IsScheduleHeaderText(name))
                {
                    continue;
                }

                double widthMm;
                if (widthIndex >= 0 && TryParseLength(GetCell(row, widthIndex), defaultFactor, out widthMm))
                {
                }
                else if (thicknessIndex >= 0 && TryParseLength(GetCell(row, thicknessIndex), defaultFactor, out widthMm))
                {
                }
                else if (widthHeightIndex >= 0 &&
                         TryParseSizePair(GetCell(row, widthHeightIndex), false, defaultFactor, out widthMm, out _))
                {
                }
                else if (heightWidthIndex >= 0 &&
                         TryParseSizePair(GetCell(row, heightWidthIndex), true, defaultFactor, out widthMm, out _))
                {
                }
                else
                {
                    continue;
                }

                if (widthMm <= 0)
                {
                    continue;
                }

                byName[name] = new WallSectionSpec
                {
                    Name = name,
                    WidthMm = widthMm
                };
            }

            specs.AddRange(byName.Values.OrderBy(s => NaturalSortKey(s.Name)));
            if (specs.Count == 0)
            {
                message = "No valid wall rows were identified. Check Name and Width/Thickness mapping.";
                return false;
            }

            message = "Identified " + specs.Count.ToString(CultureInfo.InvariantCulture) + " wall row(s).";
            return true;
        }

        private bool TryBuildBeamSpecs(out List<BeamSectionSpec> specs, out string message)
        {
            specs = new List<BeamSectionSpec>();
            message = "";

            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                message = "No schedule data is loaded.";
                return false;
            }

            int nameIndex = FindMappedColumn(NameField);
            int widthIndex = FindMappedColumn(WidthField);
            int heightIndex = FindMappedColumn(HeightField);
            int widthHeightIndex = FindMappedColumn(WidthHeightField);
            int heightWidthIndex = FindMappedColumn(HeightWidthField);
            int widthHeightSimpleIndex = FindMappedColumn(WidthHeightSimpleField);
            int heightWidthSimpleIndex = FindMappedColumn(HeightWidthSimpleField);

            if (nameIndex < 0)
            {
                message = "Map one column to Name.";
                return false;
            }

            if (widthHeightIndex < 0 && heightWidthIndex < 0 &&
                widthHeightSimpleIndex < 0 && heightWidthSimpleIndex < 0 &&
                (widthIndex < 0 || heightIndex < 0))
            {
                message = "Map beam TYPE to Width * Height, or map separate Width and Height/Depth columns.";
                return false;
            }

            double defaultFactor = GetSelectedUnitFactorMm();
            var byName = new Dictionary<string, BeamSectionSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (List<string> row in _activeTable.Rows)
            {
                string name = CleanName(GetCell(row, nameIndex));
                if (string.IsNullOrWhiteSpace(name) || IsScheduleHeaderText(name))
                {
                    continue;
                }

                double widthMm;
                double depthMm;

                if (widthHeightIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, widthHeightIndex), false, defaultFactor, out widthMm, out depthMm))
                    {
                        continue;
                    }
                }
                else if (heightWidthIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, heightWidthIndex), true, defaultFactor, out widthMm, out depthMm))
                    {
                        continue;
                    }
                }
                else if (widthHeightSimpleIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, widthHeightSimpleIndex), false, defaultFactor, out widthMm, out depthMm))
                    {
                        continue;
                    }
                }
                else if (heightWidthSimpleIndex >= 0)
                {
                    if (!TryParseSizePair(GetCell(row, heightWidthSimpleIndex), true, defaultFactor, out widthMm, out depthMm))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!TryParseLength(GetCell(row, widthIndex), defaultFactor, out widthMm) ||
                        !TryParseLength(GetCell(row, heightIndex), defaultFactor, out depthMm))
                    {
                        continue;
                    }
                }

                if (widthMm <= 0 || depthMm <= 0)
                {
                    continue;
                }

                byName[name] = new BeamSectionSpec
                {
                    Name = name,
                    WidthMm = widthMm,
                    DepthMm = depthMm
                };
            }

            specs.AddRange(byName.Values.OrderBy(s => NaturalSortKey(s.Name)));
            if (specs.Count == 0)
            {
                message = "No valid beam rows were identified. Check Name and TYPE column mapping.";
                return false;
            }

            message = "Identified " + specs.Count.ToString(CultureInfo.InvariantCulture) + " beam row(s).";
            return true;
        }

        private bool TryBuildSlabSpecs(out List<SlabSectionSpec> specs, out string message)
        {
            specs = new List<SlabSectionSpec>();
            message = "";

            if (_activeTable == null || _activeTable.Rows.Count == 0)
            {
                message = "No schedule data is loaded.";
                return false;
            }

            int nameIndex = FindMappedColumn(NameField);
            int thicknessIndex = FindMappedColumn(ThicknessField);
            int widthIndex = FindMappedColumn(WidthField);
            int heightIndex = FindMappedColumn(HeightField);

            if (nameIndex < 0)
            {
                message = "Map one column to Name.";
                return false;
            }

            int sourceIndex = thicknessIndex >= 0 ? thicknessIndex : widthIndex >= 0 ? widthIndex : heightIndex;
            if (sourceIndex < 0)
            {
                message = "Map one column to Thickness.";
                return false;
            }

            double defaultFactor = GetSelectedUnitFactorMm();
            var byName = new Dictionary<string, SlabSectionSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (List<string> row in _activeTable.Rows)
            {
                string name = CleanName(GetCell(row, nameIndex));
                if (string.IsNullOrWhiteSpace(name) || IsScheduleHeaderText(name))
                {
                    continue;
                }

                if (!TryParseLength(GetCell(row, sourceIndex), defaultFactor, out double thicknessMm) ||
                    thicknessMm <= 0)
                {
                    continue;
                }

                byName[name] = new SlabSectionSpec
                {
                    Name = name,
                    ThicknessMm = thicknessMm
                };
            }

            specs.AddRange(byName.Values.OrderBy(s => NaturalSortKey(s.Name)));
            if (specs.Count == 0)
            {
                message = "No valid slab rows were identified. Check Name and Thickness mapping.";
                return false;
            }

            message = "Identified " + specs.Count.ToString(CultureInfo.InvariantCulture) + " slab row(s).";
            return true;
        }

        private int FindMappedColumn(string field)
        {
            ScheduleColumnMapping mapping = _mappings.FirstOrDefault(m =>
                string.Equals(m.TargetField, field, StringComparison.OrdinalIgnoreCase));
            return mapping == null ? -1 : mapping.ColumnNumber - 1;
        }

        private double GetSelectedUnitFactorMm()
        {
            string unit = Convert.ToString(_unitCombo.SelectedItem, CultureInfo.InvariantCulture) ?? "mm";
            if (string.Equals(unit, "cm", StringComparison.OrdinalIgnoreCase))
            {
                return 10.0;
            }

            if (string.Equals(unit, "m", StringComparison.OrdinalIgnoreCase))
            {
                return 1000.0;
            }

            return 1.0;
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.Text = text ?? "";
            }
        }

        private static List<ScheduleTableData> ReadScheduleTables(string path)
        {
            string extension = Path.GetExtension(path) ?? "";
            if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                return new List<ScheduleTableData>
                {
                    new ScheduleTableData(Path.GetFileName(path), ParseDelimitedText(File.ReadAllText(path, Encoding.UTF8)))
                };
            }

            return ReadExcelTables(path);
        }

        private static List<ScheduleTableData> ReadExcelTables(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Excel file not found: " + fullPath, fullPath);
            }

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            var result = new List<ScheduleTableData>();

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application") ??
                                 throw new InvalidOperationException("Microsoft Excel is not available.");
                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Open(fullPath, Type.Missing, true);
                dynamic workbook = workbookObj;
                dynamic worksheets = workbook.Worksheets;
                int count = worksheets.Count;

                for (int i = 1; i <= count; i++)
                {
                    object worksheetObj = null;
                    object usedRangeObj = null;
                    try
                    {
                        worksheetObj = worksheets[i];
                        dynamic worksheet = worksheetObj;
                        string name = Convert.ToString(worksheet.Name, CultureInfo.InvariantCulture) ?? "Sheet" + i.ToString(CultureInfo.InvariantCulture);
                        usedRangeObj = worksheet.UsedRange;
                        object valuesObj = ((dynamic)usedRangeObj).Value2;
                        List<List<string>> rows = MatrixToRows(valuesObj);
                        if (rows.Count > 0)
                        {
                            result.Add(new ScheduleTableData(name, rows));
                        }
                    }
                    finally
                    {
                        SafeReleaseCom(usedRangeObj);
                        SafeReleaseCom(worksheetObj);
                    }
                }

                workbook.Close(false);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }

            return result;
        }

        private static List<List<string>> MatrixToRows(object valuesObj)
        {
            var rows = new List<List<string>>();
            if (valuesObj == null)
            {
                return rows;
            }

            if (valuesObj is object[,] values)
            {
                int r0 = values.GetLowerBound(0);
                int r1 = values.GetUpperBound(0);
                int c0 = values.GetLowerBound(1);
                int c1 = values.GetUpperBound(1);

                for (int r = r0; r <= r1; r++)
                {
                    var row = new List<string>();
                    bool hasValue = false;
                    for (int c = c0; c <= c1; c++)
                    {
                        string text = Convert.ToString(values[r, c], CultureInfo.InvariantCulture) ?? "";
                        text = text.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            hasValue = true;
                        }

                        row.Add(text);
                    }

                    TrimTrailingEmpty(row);
                    if (hasValue)
                    {
                        rows.Add(row);
                    }
                }
            }
            else
            {
                string text = Convert.ToString(valuesObj, CultureInfo.InvariantCulture) ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    rows.Add(new List<string> { text.Trim() });
                }
            }

            return rows;
        }

        private static List<List<string>> ParseDelimitedText(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return rows;
            }

            foreach (string rawLine in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                char delimiter = rawLine.Contains("\t") ? '\t' : rawLine.Contains(",") ? ',' : ';';
                List<string> row = SplitDelimitedLine(rawLine, delimiter);
                TrimTrailingEmpty(row);
                if (row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static List<string> SplitDelimitedLine(string line, char delimiter)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (ch == delimiter && !quoted)
                {
                    cells.Add(cell.ToString().Trim());
                    cell.Clear();
                }
                else
                {
                    cell.Append(ch);
                }
            }

            cells.Add(cell.ToString().Trim());
            return cells;
        }

        private static List<string> FindHeaderRow(IReadOnlyList<List<string>> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return new List<string>();
            }

            List<string> best = rows[0];
            int bestScore = -1;
            foreach (List<string> row in rows.Take(Math.Min(rows.Count, 10)))
            {
                int score = row.Count(cell =>
                {
                    string text = NormalizeKey(cell);
                    return text.Contains("MARK") ||
                           text.Contains("NAME") ||
                           text.Contains("TYPE") ||
                           text.Contains("WIDTH") ||
                           text.Contains("LENGTH") ||
                           text.Contains("HEIGHT") ||
                           text.Contains("THICK") ||
                           text.Contains("DEPTH") ||
                           text.Contains("DIAMETER") ||
                           text.Contains("ZONE") ||
                           text.Contains("FLOOR") ||
                           text.Contains("STEEL") ||
                           text.Contains("SUMMARY") ||
                           text.Contains("REMARK");
                });

                if (score > bestScore)
                {
                    bestScore = score;
                    best = row;
                }
            }

            return best ?? new List<string>();
        }

        private static string FindSampleCell(IReadOnlyList<List<string>> rows, int column)
        {
            if (rows == null)
            {
                return "";
            }

            foreach (List<string> row in rows.Skip(1))
            {
                string value = GetCell(row, column);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return "";
        }

        private string InferMapping(int zeroBasedColumnIndex, string header, string sample)
        {
            string key = NormalizeKey(header + " " + sample);
            string group = GetSelectedElementGroup();
            if (key.Contains("MARK") || key.Contains("NAME"))
            {
                return NameField;
            }

            if (key.Contains("REMARK"))
            {
                return RemarksField;
            }

            if (key.Contains("SUMMARY") || key.Contains("INFO"))
            {
                return SummaryInfoField;
            }

            if (key.Contains("STEEL") || key.Contains("RATIO"))
            {
                return SteelRatioField;
            }

            if (key.Contains("ZONE") || key.Contains("CORRESPOND"))
            {
                return CorrespondingZoneField;
            }

            if (IsElementGroup(group, ElementGroupOpening) &&
                (key.Contains("ABOVEFLOOR") || key.Contains("SILL") || key.Contains("BOTTOMHEIGHT")))
            {
                return HeightAboveFloorField;
            }

            if (IsElementGroup(group, ElementGroupSlab) &&
                (key.Contains("THICK") || key.Contains("THK") || key.Contains("DEPTH")))
            {
                return ThicknessField;
            }

            int widthPosition = key.IndexOf("WIDTH", StringComparison.OrdinalIgnoreCase);
            int heightPosition = key.IndexOf("HEIGHT", StringComparison.OrdinalIgnoreCase);
            int lengthPosition = key.IndexOf("LENGTH", StringComparison.OrdinalIgnoreCase);

            if (lengthPosition >= 0 && widthPosition >= 0 && heightPosition >= 0)
            {
                return LengthWidthHeightField;
            }

            if (widthPosition >= 0 && heightPosition >= 0)
            {
                bool widthBeforeHeight = widthPosition <= heightPosition;
                if (IsElementGroup(group, ElementGroupColumn))
                {
                    return widthBeforeHeight ? WidthHeightField : HeightWidthField;
                }

                return widthBeforeHeight ? WidthHeightSimpleField : HeightWidthSimpleField;
            }

            if (lengthPosition >= 0 && widthPosition >= 0)
            {
                return lengthPosition <= widthPosition ? LengthWidthField : WidthLengthField;
            }

            if (Regex.IsMatch(sample ?? "", @"\d+\s*[xX*]\s*\d+"))
            {
                if (IsElementGroup(group, ElementGroupColumn))
                {
                    return WidthHeightField;
                }

                if (IsElementGroup(group, ElementGroupFoundation))
                {
                    return LengthWidthField;
                }

                return WidthHeightSimpleField;
            }

            if (IsElementGroup(group, ElementGroupSlab))
            {
                if (zeroBasedColumnIndex == 0)
                {
                    return NameField;
                }

                if (key.Contains("TYPE") || key.Contains("SIZE") || zeroBasedColumnIndex == 1)
                {
                    return ThicknessField;
                }
            }

            if (IsElementGroup(group, ElementGroupWall))
            {
                if (key.Contains("THICK") ||
                    key.Contains("THK") ||
                    key.Contains("WIDTH") ||
                    key.Contains("TYPE") ||
                    key.Contains("SIZE"))
                {
                    return ThicknessField;
                }
            }

            if (IsElementGroup(group, ElementGroupOpening))
            {
                if (key.Contains("DOOR") || key.Contains("WINDOW") || key.Contains("TYPE"))
                {
                    return TypeField;
                }
            }

            if (IsElementGroup(group, ElementGroupFoundation))
            {
                if (key.Contains("TYPE") || key.Contains("SIZE"))
                {
                    return LengthWidthField;
                }

                if (key.Contains("LENGTH"))
                {
                    return LengthField;
                }
            }

            if (key.Contains("TYPE") || key.Contains("SIZE"))
            {
                return IsElementGroup(group, ElementGroupColumn) ? WidthHeightField : WidthHeightSimpleField;
            }

            if (key.Contains("WIDTH"))
            {
                return WidthField;
            }

            if (key.Contains("THICK") || key.Contains("THK"))
            {
                return IsElementGroup(group, ElementGroupSlab) || IsElementGroup(group, ElementGroupWall)
                    ? ThicknessField
                    : WidthField;
            }

            if (key.Contains("HEIGHT") || key.Contains("LENGTH") || key.Contains("DEPTH"))
            {
                if (IsElementGroup(group, ElementGroupFoundation) && key.Contains("LENGTH"))
                {
                    return LengthField;
                }

                return HeightField;
            }

            if (zeroBasedColumnIndex == 0)
            {
                return NameField;
            }

            if (zeroBasedColumnIndex == 1)
            {
                if (IsElementGroup(group, ElementGroupWall))
                {
                    return ThicknessField;
                }

                if (IsElementGroup(group, ElementGroupSlab))
                {
                    return ThicknessField;
                }

                if (IsElementGroup(group, ElementGroupFoundation))
                {
                    return LengthWidthField;
                }

                return IsElementGroup(group, ElementGroupColumn) ? WidthHeightField : WidthHeightSimpleField;
            }

            return IgnoreField;
        }

        private static bool TryParseSizePair(string text, bool reverse, double defaultFactorMm, out double widthMm, out double heightMm)
        {
            widthMm = 0;
            heightMm = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = Regex.Match(text, @"(?<a>\d+(?:[.,]\d+)?)\s*(?:x|X|\*|/)\s*(?<b>\d+(?:[.,]\d+)?)");
            if (!match.Success)
            {
                return false;
            }

            if (!TryParseNumber(match.Groups["a"].Value, out double a) ||
                !TryParseNumber(match.Groups["b"].Value, out double b))
            {
                return false;
            }

            double factor = GetUnitFactorFromText(text, defaultFactorMm);
            if (reverse)
            {
                widthMm = b * factor;
                heightMm = a * factor;
            }
            else
            {
                widthMm = a * factor;
                heightMm = b * factor;
            }

            return widthMm > 0 && heightMm > 0;
        }

        private static bool TryParseLength(string text, double defaultFactorMm, out double valueMm)
        {
            valueMm = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = Regex.Match(text, @"\d+(?:[.,]\d+)?");
            if (!match.Success || !TryParseNumber(match.Value, out double value))
            {
                return false;
            }

            valueMm = value * GetUnitFactorFromText(text, defaultFactorMm);
            return valueMm > 0;
        }

        private static double GetUnitFactorFromText(string text, double defaultFactorMm)
        {
            string key = NormalizeKey(text);
            if (key.Contains("MM"))
            {
                return 1.0;
            }

            if (key.Contains("CM"))
            {
                return 10.0;
            }

            if (Regex.IsMatch(key, @"(^|[^A-Z])M([^A-Z]|$)"))
            {
                return 1000.0;
            }

            return defaultFactorMm;
        }

        private static bool TryParseNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string cleaned = text.Trim().Replace(",", ".");
            return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string GetCell(IReadOnlyList<string> row, int index)
        {
            if (row == null || index < 0 || index >= row.Count)
            {
                return "";
            }

            return row[index] ?? "";
        }

        private static string CleanName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string cleaned = text.Trim();
            int paren = cleaned.IndexOf('(');
            if (paren > 0)
            {
                cleaned = cleaned.Substring(0, paren);
            }

            return cleaned.Replace(" ", "").Trim();
        }

        private static bool IsScheduleHeaderText(string text)
        {
            string key = NormalizeKey(text);
            return key == "MARK" ||
                   key == "NAME" ||
                   key == "TYPE" ||
                   key == "WIDTH" ||
                   key == "HEIGHT" ||
                   key == "THICKNESS" ||
                   key == "THICK" ||
                   key == "THK" ||
                   key == "DEPTH" ||
                   key == "LENGTH" ||
                   key == "FLOOR" ||
                   key == "ZONE" ||
                   key.Contains("SCHEDULE");
        }

        private static string NormalizeScheduleCell(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            return text
                .Trim()
                .Replace(" X ", "x")
                .Replace(" x ", "x")
                .Replace(" * ", "x")
                .Replace("*", "x");
        }

        private static string NormalizeKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var chars = new List<char>();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars.Add(char.ToUpperInvariant(c));
                }
            }

            return new string(chars.ToArray());
        }

        private static string FormatSize(double widthMm, double heightMm)
        {
            return "(" +
                   widthMm.ToString("0.###", CultureInfo.InvariantCulture) +
                   "x" +
                   heightMm.ToString("0.###", CultureInfo.InvariantCulture) +
                   ")mm";
        }

        private static string NaturalSortKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            return Regex.Replace(text, @"\d+", m => m.Value.PadLeft(10, '0')).ToUpperInvariant();
        }

        private static void TrimTrailingEmpty(List<string> row)
        {
            if (row == null)
            {
                return;
            }

            for (int i = row.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(row[i]))
                {
                    break;
                }

                row.RemoveAt(i);
            }
        }

        private static void SafeReleaseCom(object obj)
        {
            if (obj == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(obj))
                {
                    Marshal.FinalReleaseComObject(obj);
                }
            }
            catch
            {
            }
        }

        private sealed class ScheduleColumnMapping
        {
            public int ColumnNumber { get; set; }
            public string HeaderText { get; set; } = "";
            public string TargetField { get; set; } = IgnoreField;
        }

        private sealed class ScheduleTableData
        {
            public ScheduleTableData(string name, List<List<string>> rows)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "Sheet" : name;
                Rows = rows ?? new List<List<string>>();
                ColumnCount = Rows.Count == 0 ? 0 : Rows.Max(row => row?.Count ?? 0);
            }

            public string Name { get; }
            public List<List<string>> Rows { get; }
            public int ColumnCount { get; }
        }
    }
}
