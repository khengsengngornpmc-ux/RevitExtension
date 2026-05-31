using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using ElementId = Autodesk.Revit.DB.ElementId;
using WpfGrid = System.Windows.Controls.Grid;
using WpfPanel = System.Windows.Controls.Panel;
using WpfVisibility = System.Windows.Visibility;

namespace CamboBIM.Revit2024.Addin
{
    internal enum MhnkRoomCreationMode
    {
        Walls,
        Floors,
        Ceilings
    }

    internal enum MhnkWallLineSourceMode
    {
        LinesFromRooms
    }

    internal sealed class MhnkElementTypeOption
    {
        public MhnkElementTypeOption(ElementId id, string name)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed type)" : name.Trim();
        }

        public ElementId Id { get; }
        public string Name { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class MhnkLevelOption
    {
        public MhnkLevelOption(ElementId id, string name, bool isAllLevels = false, bool isLevelAbove = false)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "(level)" : name.Trim();
            IsAllLevels = isAllLevels;
            IsLevelAbove = isLevelAbove;
        }

        public ElementId Id { get; }
        public string Name { get; }
        public bool IsAllLevels { get; }
        public bool IsLevelAbove { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class MhnkMaterialOption
    {
        public MhnkMaterialOption(ElementId id, string name, bool keepTypeMaterial = false)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "(material)" : name.Trim();
            KeepTypeMaterial = keepTypeMaterial;
        }

        public ElementId Id { get; }
        public string Name { get; }
        public bool KeepTypeMaterial { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class MhnkRoomCreationOptions
    {
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
        public ElementId SelectedLevelId { get; set; }
        public bool UseAllLevels { get; set; }
        public MhnkWallLineSourceMode WallLineSourceMode { get; set; }
        public double WallHeightMeters { get; set; }
        public double OffsetMillimeters { get; set; }
        public double BaseOffsetMillimeters { get; set; }
        public bool UseTopConstraint { get; set; }
        public bool UseLevelAboveTopConstraint { get; set; }
        public ElementId TopConstraintLevelId { get; set; }
        public double TopOffsetMillimeters { get; set; }
        public bool SkipExistingWallSegments { get; set; }
        public bool JoinGeneratedWalls { get; set; }
        public bool AllowWallJoinsAtEnds { get; set; }
        public bool UseMiterWallJoins { get; set; }
        public bool AutoInternalExternalWallTypes { get; set; }
        public bool SetRoomBounding { get; set; }
    }

    internal sealed class MhnkRoomBoundaryLineReviewItem
    {
        public bool IsSelected { get; set; }
        public int Index { get; set; }
        public string Room { get; set; }
        public string Level { get; set; }
        public string Boundary { get; set; }
        public string Kind { get; set; }
        public string Length { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class MhnkRoomCreationOptionsWindow : Window
    {
        private readonly MhnkRoomCreationMode _mode;
        private readonly ComboBox _levelCombo;
        private readonly ComboBox _lineModeCombo;
        private readonly ComboBox _typeCombo;
        private readonly TextBox _heightText;
        private readonly TextBox _offsetText;
        private readonly CheckBox _topConstraintCheck;
        private readonly ComboBox _topLevelCombo;
        private readonly TextBox _topOffsetText;
        private readonly CheckBox _skipExistingWallsCheck;
        private readonly CheckBox _joinWallsCheck;
        private readonly CheckBox _allowWallJoinsCheck;
        private readonly CheckBox _miterJoinCheck;
        private readonly CheckBox _autoWallTypeChangeCheck;
        private readonly CheckBox _roomBoundingCheck;

        public MhnkRoomCreationOptionsWindow(
            MhnkRoomCreationMode mode,
            string summary,
            IList<MhnkElementTypeOption> typeOptions,
            ElementId defaultTypeId,
            double defaultWallHeightMeters,
            double defaultOffsetMillimeters,
            IntPtr revitMainWindowHandle,
            IList<MhnkLevelOption> levelOptions = null,
            ElementId defaultLevelId = null)
        {
            _mode = mode;

            Title = "MHNK Room Creation Options";
            Width = 620;
            Height = mode == MhnkRoomCreationMode.Walls ? 700 : 480;
            MinWidth = 560;
            MinHeight = mode == MhnkRoomCreationMode.Walls ? 620 : 430;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = GetHeader(mode),
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
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            WpfGrid form = new WpfGrid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(form);
            Grid.SetRow(form, 2);

            int row = 0;
            bool isWallMode = mode == MhnkRoomCreationMode.Walls;
            IList<MhnkLevelOption> availableLevels = BuildLevelOptions(levelOptions);
            _levelCombo = new ComboBox
            {
                ItemsSource = availableLevels,
                Height = 28,
                MinWidth = 260
            };
            _levelCombo.SelectedItem = SelectDefaultLevel(availableLevels, defaultLevelId);
            if (_levelCombo.SelectedItem == null && _levelCombo.Items.Count > 0)
            {
                _levelCombo.SelectedIndex = 0;
            }

            AddRow(form, ref row, GetLevelLabel(mode), _levelCombo);

            _lineModeCombo = new ComboBox
            {
                ItemsSource = new[] { "Lines from Rooms" },
                SelectedIndex = 0,
                Height = 28,
                IsEnabled = false
            };
            if (isWallMode)
            {
                AddRow(form, ref row, "Wall source mode", _lineModeCombo);
            }

            _typeCombo = new ComboBox
            {
                ItemsSource = typeOptions ?? new List<MhnkElementTypeOption>(),
                Height = 28,
                MinWidth = 260
            };
            _typeCombo.SelectedItem = SelectDefaultType(typeOptions, defaultTypeId);
            if (_typeCombo.SelectedItem == null && _typeCombo.Items.Count > 0)
            {
                _typeCombo.SelectedIndex = 0;
            }

            AddRow(form, ref row, GetTypeLabel(mode), _typeCombo);

            _heightText = new TextBox
            {
                Text = defaultWallHeightMeters.ToString("0.###", CultureInfo.InvariantCulture),
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (mode == MhnkRoomCreationMode.Walls)
            {
                AddRow(form, ref row, "Wall height (m)", _heightText);
            }

            _offsetText = new TextBox
            {
                Text = defaultOffsetMillimeters.ToString("0.###", CultureInfo.InvariantCulture),
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, GetOffsetLabel(mode), _offsetText);

            _topConstraintCheck = new CheckBox
            {
                Content = "Use upper level constraint",
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            _topConstraintCheck.Checked += (_, __) => UpdateWallConstraintControls();
            _topConstraintCheck.Unchecked += (_, __) => UpdateWallConstraintControls();

            _topLevelCombo = new ComboBox
            {
                ItemsSource = BuildTopLevelOptions(levelOptions),
                Height = 28,
                MinWidth = 260
            };
            if (_topLevelCombo.Items.Count > 0)
            {
                _topLevelCombo.SelectedIndex = 0;
            }

            _topOffsetText = new TextBox
            {
                Text = "0",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (isWallMode)
            {
                AddRow(form, ref row, "Top constraint", _topConstraintCheck);
                AddRow(form, ref row, "Top level", _topLevelCombo);
                AddRow(form, ref row, "Top offset (mm)", _topOffsetText);
            }

            StackPanel checks = new StackPanel
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            root.Children.Add(checks);
            Grid.SetRow(checks, 3);

            _skipExistingWallsCheck = AddCheck(checks, "Skip room edges already bounded by existing walls", mode == MhnkRoomCreationMode.Walls);
            _autoWallTypeChangeCheck = AddCheck(checks, "Toggle change of wall type (-EXT+ / -INT+)", false);
            _joinWallsCheck = AddCheck(checks, "Join generated walls where geometry touches", true);
            _allowWallJoinsCheck = AddCheck(checks, "Allow wall joins at both ends", true);
            _miterJoinCheck = AddCheck(checks, "Use miter/chamfer wall joins", true);
            _roomBoundingCheck = AddCheck(checks, "Set generated walls Room Bounding", true);
            _allowWallJoinsCheck.Checked += (_, __) => UpdateWallConstraintControls();
            _allowWallJoinsCheck.Unchecked += (_, __) => UpdateWallConstraintControls();

            _skipExistingWallsCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _autoWallTypeChangeCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _joinWallsCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _allowWallJoinsCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _miterJoinCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            _roomBoundingCheck.Visibility = isWallMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            UpdateWallConstraintControls();

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 4);

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
                Content = "Continue",
                Width = 110,
                Height = 30,
                IsEnabled = _typeCombo.Items.Count > 0
            };
            createButton.Click += (_, __) => Accept();
            buttons.Children.Add(createButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkRoomCreationOptions SelectedOptions { get; private set; }

        private void Accept()
        {
            var selectedType = _typeCombo.SelectedItem as MhnkElementTypeOption;
            if (selectedType == null)
            {
                MessageBox.Show(this, "Select a Revit type before continuing.", "MHNK Room Creation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double wallHeightMeters = ParseNumber(_heightText.Text, 3.0);
            double offsetMillimeters = ParseNumber(_offsetText.Text, 0.0);
            double topOffsetMillimeters = ParseNumber(_topOffsetText.Text, 0.0);
            if (_mode == MhnkRoomCreationMode.Walls && wallHeightMeters <= 0.0)
            {
                MessageBox.Show(this, "Wall height must be greater than 0.", "MHNK Room Creation", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedLevel = _levelCombo.SelectedItem as MhnkLevelOption;
            var selectedTopLevel = _topLevelCombo.SelectedItem as MhnkLevelOption;
            bool useAllLevels = selectedLevel == null || selectedLevel.IsAllLevels;
            bool useTopConstraint = _mode == MhnkRoomCreationMode.Walls && _topConstraintCheck.IsChecked == true;
            bool useLevelAbove = useTopConstraint && (selectedTopLevel == null || selectedTopLevel.IsLevelAbove);

            SelectedOptions = new MhnkRoomCreationOptions
            {
                TypeId = selectedType.Id,
                TypeName = selectedType.Name,
                SelectedLevelId = useAllLevels ? ElementId.InvalidElementId : selectedLevel.Id,
                UseAllLevels = useAllLevels,
                WallLineSourceMode = MhnkWallLineSourceMode.LinesFromRooms,
                WallHeightMeters = Math.Max(0.01, wallHeightMeters),
                OffsetMillimeters = offsetMillimeters,
                BaseOffsetMillimeters = offsetMillimeters,
                UseTopConstraint = useTopConstraint,
                UseLevelAboveTopConstraint = useLevelAbove,
                TopConstraintLevelId = !useTopConstraint || useLevelAbove || selectedTopLevel == null ? ElementId.InvalidElementId : selectedTopLevel.Id,
                TopOffsetMillimeters = topOffsetMillimeters,
                SkipExistingWallSegments = _skipExistingWallsCheck.IsChecked == true,
                JoinGeneratedWalls = _joinWallsCheck.IsChecked == true,
                AllowWallJoinsAtEnds = _allowWallJoinsCheck.IsChecked == true,
                UseMiterWallJoins = _miterJoinCheck.IsChecked == true,
                AutoInternalExternalWallTypes = _autoWallTypeChangeCheck.IsChecked == true,
                SetRoomBounding = _roomBoundingCheck.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private static void AddRow(WpfGrid form, ref int row, string label, FrameworkElement editor)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            editor.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            row++;
        }

        private static CheckBox AddCheck(WpfPanel panel, string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            panel.Children.Add(checkBox);
            return checkBox;
        }

        private void UpdateWallConstraintControls()
        {
            bool isWallMode = _mode == MhnkRoomCreationMode.Walls;
            bool useTopConstraint = isWallMode && _topConstraintCheck != null && _topConstraintCheck.IsChecked == true;
            if (_topLevelCombo != null)
            {
                _topLevelCombo.IsEnabled = useTopConstraint;
            }

            if (_topOffsetText != null)
            {
                _topOffsetText.IsEnabled = useTopConstraint;
            }

            if (_miterJoinCheck != null)
            {
                _miterJoinCheck.IsEnabled = isWallMode && _allowWallJoinsCheck != null && _allowWallJoinsCheck.IsChecked == true;
            }
        }

        private static MhnkElementTypeOption SelectDefaultType(IList<MhnkElementTypeOption> typeOptions, ElementId defaultTypeId)
        {
            if (typeOptions == null || defaultTypeId == null)
            {
                return null;
            }

            return typeOptions.FirstOrDefault(x => x.Id != null && x.Id.Value == defaultTypeId.Value);
        }

        private static IList<MhnkLevelOption> BuildLevelOptions(IList<MhnkLevelOption> levelOptions)
        {
            var options = new List<MhnkLevelOption>
            {
                new MhnkLevelOption(ElementId.InvalidElementId, "All Levels", true)
            };

            foreach (MhnkLevelOption option in levelOptions ?? new List<MhnkLevelOption>())
            {
                if (option != null && !option.IsAllLevels && option.Id != null && option.Id != ElementId.InvalidElementId)
                {
                    options.Add(option);
                }
            }

            return options;
        }

        private static IList<MhnkLevelOption> BuildTopLevelOptions(IList<MhnkLevelOption> levelOptions)
        {
            var options = new List<MhnkLevelOption>
            {
                new MhnkLevelOption(ElementId.InvalidElementId, "Level Above", false, true)
            };

            foreach (MhnkLevelOption option in levelOptions ?? new List<MhnkLevelOption>())
            {
                if (option != null && !option.IsAllLevels && option.Id != null && option.Id != ElementId.InvalidElementId)
                {
                    options.Add(option);
                }
            }

            return options;
        }

        private static MhnkLevelOption SelectDefaultLevel(IList<MhnkLevelOption> levelOptions, ElementId defaultLevelId)
        {
            if (levelOptions == null || levelOptions.Count == 0)
            {
                return null;
            }

            if (defaultLevelId != null && defaultLevelId != ElementId.InvalidElementId)
            {
                MhnkLevelOption match = levelOptions.FirstOrDefault(x => x.Id != null && x.Id.Value == defaultLevelId.Value);
                if (match != null)
                {
                    return match;
                }
            }

            return levelOptions.FirstOrDefault(x => x.IsAllLevels) ?? levelOptions.FirstOrDefault();
        }

        private static string GetHeader(MhnkRoomCreationMode mode)
        {
            switch (mode)
            {
                case MhnkRoomCreationMode.Walls:
                    return "Create Walls by Room";
                case MhnkRoomCreationMode.Floors:
                    return "Create Floors by Room";
                case MhnkRoomCreationMode.Ceilings:
                    return "Create Ceilings by Room";
                default:
                    return "Create by Room";
            }
        }

        private static string GetTypeLabel(MhnkRoomCreationMode mode)
        {
            switch (mode)
            {
                case MhnkRoomCreationMode.Walls:
                    return "Wall type";
                case MhnkRoomCreationMode.Floors:
                    return "Floor type";
                case MhnkRoomCreationMode.Ceilings:
                    return "Ceiling type";
                default:
                    return "Type";
            }
        }

        private static string GetLevelLabel(MhnkRoomCreationMode mode)
        {
            switch (mode)
            {
                case MhnkRoomCreationMode.Walls:
                    return "Source level";
                case MhnkRoomCreationMode.Floors:
                    return "Floor rooms level";
                case MhnkRoomCreationMode.Ceilings:
                    return "Ceiling rooms level";
                default:
                    return "Room level";
            }
        }

        private static string GetOffsetLabel(MhnkRoomCreationMode mode)
        {
            return mode == MhnkRoomCreationMode.Walls
                ? "Base offset (mm)"
                : "Height offset from level (mm)";
        }

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            return fallback;
        }
    }

    internal sealed class MhnkRoomBoundaryLineReviewWindow : Window
    {
        private readonly IList<MhnkRoomBoundaryLineReviewItem> _items;
        private readonly DataGrid _grid;

        public MhnkRoomBoundaryLineReviewWindow(
            string title,
            string summary,
            IList<MhnkRoomBoundaryLineReviewItem> items,
            IntPtr revitMainWindowHandle)
        {
            _items = items ?? new List<MhnkRoomBoundaryLineReviewItem>();

            Title = title ?? "MHNK Retrieved Lines";
            Width = 980;
            Height = 640;
            MinWidth = 780;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = title ?? "MHNK Retrieved Lines",
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
                Text = summary ?? "",
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
                Margin = new Thickness(0, 0, 0, 10)
            };
            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Use",
                Binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 58
            });
            AddColumn(_grid, "No.", "Index", 56);
            AddColumn(_grid, "Room", "Room", 170);
            AddColumn(_grid, "Level", "Level", 130);
            AddColumn(_grid, "Boundary", "Boundary", 180);
            AddColumn(_grid, "Kind", "Kind", 90);
            AddColumn(_grid, "Length", "Length", 80);
            AddColumn(_grid, "Notes", "Notes", 220);
            root.Children.Add(_grid);
            Grid.SetRow(_grid, 2);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Button selectAllButton = new Button
            {
                Content = "Select All",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            selectAllButton.Click += (_, __) => SetAllSelected(true);
            buttons.Children.Add(selectAllButton);

            Button clearButton = new Button
            {
                Content = "Clear",
                Width = 76,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            clearButton.Click += (_, __) => SetAllSelected(false);
            buttons.Children.Add(clearButton);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button continueButton = new Button
            {
                Content = "Continue",
                Width = 110,
                Height = 30,
                IsEnabled = _items.Count > 0
            };
            continueButton.Click += (_, __) =>
            {
                if (!_items.Any(x => x.IsSelected))
                {
                    MessageBox.Show(this, "Select at least one retrieved line before continuing.", "MHNK Retrieved Lines", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DialogResult = true;
                Close();
            };
            buttons.Children.Add(continueButton);
            MhnkUiTheme.Apply(this);
        }

        private void SetAllSelected(bool selected)
        {
            foreach (MhnkRoomBoundaryLineReviewItem item in _items)
            {
                item.IsSelected = selected;
            }

            _grid.Items.Refresh();
        }

        private static void AddColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width
            });
        }
    }

    internal sealed class MhnkWallFinishOptions
    {
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
        public ElementId MaterialId { get; set; }
        public string MaterialName { get; set; }
        public bool UseSelectedTypeMaterial { get; set; }
        public double ThicknessMillimeters { get; set; }
        public double GapMillimeters { get; set; }
        public double CreationHeightMeters { get; set; }
        public double BaseOffsetMillimeters { get; set; }
        public ElementId TopConstraintLevelId { get; set; }
        public string TopConstraintName { get; set; }
        public bool UseLevelAboveTopConstraint { get; set; }
        public double TopOffsetMillimeters { get; set; }
        public bool CreateDedicatedMaterialType { get; set; }
        public bool MatchHostConstraints { get; set; }
        public bool SkipExistingFinishWalls { get; set; }
        public bool JoinGeneratedWalls { get; set; }
        public bool AllowWallJoinsAtEnds { get; set; }
        public bool SetRoomBounding { get; set; }
    }

    internal sealed class MhnkWallFinishReviewItem
    {
        public bool IsSelected { get; set; }
        public ElementId WallId { get; set; }
        public string Index { get; set; }
        public string Wall { get; set; }
        public string Level { get; set; }
        public string Type { get; set; }
        public string Function { get; set; }
        public string Length { get; set; }
        public string Notes { get; set; }
    }

    internal sealed class MhnkWallFinishReviewWindow : Window
    {
        private readonly IList<MhnkWallFinishReviewItem> _items;
        private readonly DataGrid _grid;

        public MhnkWallFinishReviewWindow(
            string title,
            string summary,
            IList<MhnkWallFinishReviewItem> items,
            IntPtr revitMainWindowHandle)
        {
            _items = items ?? new List<MhnkWallFinishReviewItem>();

            Title = title ?? "MHNK Collected Walls";
            Width = 1040;
            Height = 640;
            MinWidth = 820;
            MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = title ?? "MHNK Collected Walls",
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
                Text = summary ?? "",
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
                Margin = new Thickness(0, 0, 0, 10)
            };
            _grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "Use",
                Binding = new Binding("IsSelected") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 58
            });
            AddColumn(_grid, "No.", "Index", 56);
            AddColumn(_grid, "Wall", "Wall", 190);
            AddColumn(_grid, "Level", "Level", 130);
            AddColumn(_grid, "Type", "Type", 210);
            AddColumn(_grid, "Function", "Function", 90);
            AddColumn(_grid, "Length", "Length", 80);
            AddColumn(_grid, "Notes", "Notes", 230);
            root.Children.Add(_grid);
            Grid.SetRow(_grid, 2);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 3);

            Button selectAllButton = new Button
            {
                Content = "Select All",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            selectAllButton.Click += (_, __) => SetAllSelected(true);
            buttons.Children.Add(selectAllButton);

            Button clearButton = new Button
            {
                Content = "Clear",
                Width = 76,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            clearButton.Click += (_, __) => SetAllSelected(false);
            buttons.Children.Add(clearButton);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button continueButton = new Button
            {
                Content = "Continue",
                Width = 110,
                Height = 30,
                IsEnabled = _items.Count > 0
            };
            continueButton.Click += (_, __) =>
            {
                if (!_items.Any(x => x.IsSelected))
                {
                    MessageBox.Show(this, "Select at least one collected wall before continuing.", "MHNK Collected Walls", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                DialogResult = true;
                Close();
            };
            buttons.Children.Add(continueButton);
            MhnkUiTheme.Apply(this);
        }

        private void SetAllSelected(bool selected)
        {
            foreach (MhnkWallFinishReviewItem item in _items)
            {
                item.IsSelected = selected;
            }

            _grid.Items.Refresh();
        }

        private static void AddColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width
            });
        }
    }

    internal sealed class MhnkWallHorizontalSplitOptions
    {
        public ElementId NewTypeId { get; set; }
        public string NewTypeName { get; set; }
        public double SplitHeightMeters { get; set; }
        public bool ApplyNewTypeToLowerSegment { get; set; }
        public bool KeepOriginalTopConstraint { get; set; }
        public bool CopyRoomBounding { get; set; }
    }

    internal sealed class MhnkWallHorizontalSplitOptionsWindow : Window
    {
        private readonly ComboBox _typeCombo;
        private readonly TextBox _heightText;
        private readonly CheckBox _lowerSegmentCheck;
        private readonly CheckBox _keepTopConstraintCheck;
        private readonly CheckBox _copyRoomBoundingCheck;

        public MhnkWallHorizontalSplitOptionsWindow(
            string summary,
            IList<MhnkElementTypeOption> typeOptions,
            ElementId defaultTypeId,
            IntPtr revitMainWindowHandle)
        {
            Title = "MHNK Split Walls Horizontally";
            Width = 620;
            Height = 420;
            MinWidth = 540;
            MinHeight = 380;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "Split Walls Horizontally",
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
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            WpfGrid form = new WpfGrid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(form);
            Grid.SetRow(form, 2);

            int row = 0;
            _heightText = new TextBox
            {
                Text = "2.55",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Division height (m)", _heightText);

            _typeCombo = new ComboBox
            {
                ItemsSource = typeOptions ?? new List<MhnkElementTypeOption>(),
                Height = 28,
                MinWidth = 280
            };
            _typeCombo.SelectedItem = SelectDefaultType(typeOptions, defaultTypeId);
            if (_typeCombo.SelectedItem == null && _typeCombo.Items.Count > 0)
            {
                _typeCombo.SelectedIndex = 0;
            }
            AddRow(form, ref row, "New wall type", _typeCombo);

            StackPanel checks = new StackPanel
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            root.Children.Add(checks);
            Grid.SetRow(checks, 3);

            _lowerSegmentCheck = AddCheck(checks, "Apply selected type to lower segment", true);
            _keepTopConstraintCheck = AddCheck(checks, "Keep original top constraint on upper segment", true);
            _copyRoomBoundingCheck = AddCheck(checks, "Copy Room Bounding to created segment", true);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 4);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button continueButton = new Button
            {
                Content = "Continue",
                Width = 110,
                Height = 30,
                IsEnabled = _typeCombo.Items.Count > 0
            };
            continueButton.Click += (_, __) => Accept();
            buttons.Children.Add(continueButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkWallHorizontalSplitOptions SelectedOptions { get; private set; }

        private static void AddRow(WpfGrid form, ref int row, string label, FrameworkElement editor)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            editor.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            row++;
        }

        private static CheckBox AddCheck(WpfPanel panel, string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            panel.Children.Add(checkBox);
            return checkBox;
        }

        private static MhnkElementTypeOption SelectDefaultType(IList<MhnkElementTypeOption> typeOptions, ElementId defaultTypeId)
        {
            if (typeOptions == null || defaultTypeId == null)
            {
                return null;
            }

            return typeOptions.FirstOrDefault(x => x.Id != null && x.Id.Value == defaultTypeId.Value);
        }

        private void Accept()
        {
            var selectedType = _typeCombo.SelectedItem as MhnkElementTypeOption;
            if (selectedType == null)
            {
                MessageBox.Show(this, "Select a wall type before continuing.", "MHNK Split Walls", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double splitHeightMeters = ParseNumber(_heightText.Text, 2.55);
            if (splitHeightMeters <= 0.0)
            {
                MessageBox.Show(this, "Division height must be greater than 0.", "MHNK Split Walls", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedOptions = new MhnkWallHorizontalSplitOptions
            {
                NewTypeId = selectedType.Id,
                NewTypeName = selectedType.Name,
                SplitHeightMeters = splitHeightMeters,
                ApplyNewTypeToLowerSegment = _lowerSegmentCheck.IsChecked == true,
                KeepOriginalTopConstraint = _keepTopConstraintCheck.IsChecked == true,
                CopyRoomBounding = _copyRoomBoundingCheck.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            return fallback;
        }
    }

    internal sealed class MhnkWallCeilingTrimOptions
    {
        public double TopOffsetMillimeters { get; set; }
        public bool OnlyLowerWalls { get; set; }
    }

    internal sealed class MhnkWallCeilingTrimOptionsWindow : Window
    {
        private readonly TextBox _offsetText;
        private readonly CheckBox _onlyLowerCheck;

        public MhnkWallCeilingTrimOptionsWindow(string summary, IntPtr revitMainWindowHandle)
        {
            Title = "MHNK Lower Walls to Ceilings";
            Width = 600;
            Height = 330;
            MinWidth = 520;
            MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "Lower Walls to Ceilings",
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
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            WpfGrid form = new WpfGrid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(form);
            Grid.SetRow(form, 2);

            int row = 0;
            _offsetText = new TextBox
            {
                Text = "0",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Top offset above ceiling (mm)", _offsetText);

            StackPanel checks = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };
            root.Children.Add(checks);
            Grid.SetRow(checks, 3);
            _onlyLowerCheck = AddCheck(checks, "Only lower walls; never raise short walls", true);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 4);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button continueButton = new Button
            {
                Content = "Continue",
                Width = 110,
                Height = 30
            };
            continueButton.Click += (_, __) => Accept();
            buttons.Children.Add(continueButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkWallCeilingTrimOptions SelectedOptions { get; private set; }

        private static void AddRow(WpfGrid form, ref int row, string label, FrameworkElement editor)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            editor.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            row++;
        }

        private static CheckBox AddCheck(WpfPanel panel, string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            panel.Children.Add(checkBox);
            return checkBox;
        }

        private void Accept()
        {
            SelectedOptions = new MhnkWallCeilingTrimOptions
            {
                TopOffsetMillimeters = ParseNumber(_offsetText.Text, 0.0),
                OnlyLowerWalls = _onlyLowerCheck.IsChecked == true
            };
            DialogResult = true;
            Close();
        }

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            return fallback;
        }
    }

    internal sealed class MhnkDoorWindowPlacementOptions
    {
        public double MaxHostDistanceMillimeters { get; set; }
        public double WindowSillHeightMillimeters { get; set; }
        public bool UseCadHeightForWindowSill { get; set; }
        public bool FlipToMarkerSide { get; set; }
    }

    internal sealed class MhnkDoorWindowPlacementOptionsWindow : Window
    {
        private readonly TextBox _maxDistanceText;
        private readonly TextBox _windowSillText;
        private readonly CheckBox _cadHeightCheck;
        private readonly CheckBox _flipCheck;

        public MhnkDoorWindowPlacementOptionsWindow(string summary, bool wallSideMode, IntPtr revitMainWindowHandle)
        {
            Title = wallSideMode ? "MHNK Doors / Windows by Wall Side" : "MHNK Doors / Windows by Position";
            Width = 620;
            Height = 400;
            MinWidth = 540;
            MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = wallSideMode ? "Doors / Windows by Wall Side" : "Doors / Windows by Position",
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
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            WpfGrid form = new WpfGrid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(form);
            Grid.SetRow(form, 2);

            int row = 0;
            _maxDistanceText = new TextBox
            {
                Text = "1500",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Max host distance (mm)", _maxDistanceText);

            _windowSillText = new TextBox
            {
                Text = "900",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Default window sill (mm)", _windowSillText);

            StackPanel checks = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };
            root.Children.Add(checks);
            Grid.SetRow(checks, 3);
            _cadHeightCheck = AddCheck(checks, "Use marker Z height for windows when available", true);
            _flipCheck = AddCheck(checks, "Face hosted families toward the marker side", wallSideMode);
            _flipCheck.Visibility = wallSideMode ? WpfVisibility.Visible : WpfVisibility.Collapsed;

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 4);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);

            Button continueButton = new Button
            {
                Content = "Continue",
                Width = 110,
                Height = 30
            };
            continueButton.Click += (_, __) => Accept();
            buttons.Children.Add(continueButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkDoorWindowPlacementOptions SelectedOptions { get; private set; }

        private static void AddRow(WpfGrid form, ref int row, string label, FrameworkElement editor)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            editor.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            row++;
        }

        private static CheckBox AddCheck(WpfPanel panel, string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            panel.Children.Add(checkBox);
            return checkBox;
        }

        private void Accept()
        {
            SelectedOptions = new MhnkDoorWindowPlacementOptions
            {
                MaxHostDistanceMillimeters = Math.Max(1.0, ParseNumber(_maxDistanceText.Text, 1500.0)),
                WindowSillHeightMillimeters = Math.Max(0.0, ParseNumber(_windowSillText.Text, 900.0)),
                UseCadHeightForWindowSill = _cadHeightCheck.IsChecked == true,
                FlipToMarkerSide = _flipCheck.IsChecked == true
            };
            DialogResult = true;
            Close();
        }

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            return fallback;
        }
    }

    internal sealed class MhnkWallFinishOptionsWindow : Window
    {
        private readonly ComboBox _typeCombo;
        private readonly ComboBox _materialCombo;
        private readonly TextBox _thicknessText;
        private readonly TextBox _gapText;
        private readonly TextBox _heightText;
        private readonly TextBox _baseOffsetText;
        private readonly ComboBox _topConstraintCombo;
        private readonly TextBox _topOffsetText;
        private readonly CheckBox _dedicatedTypeCheck;
        private readonly CheckBox _matchConstraintsCheck;
        private readonly CheckBox _skipExistingCheck;
        private readonly CheckBox _joinWallsCheck;
        private readonly CheckBox _allowWallJoinsCheck;
        private readonly CheckBox _roomBoundingCheck;

        public MhnkWallFinishOptionsWindow(
            string summary,
            IList<MhnkElementTypeOption> typeOptions,
            ElementId defaultTypeId,
            IList<MhnkMaterialOption> materialOptions,
            IntPtr revitMainWindowHandle,
            bool exteriorSide = true,
            IList<MhnkLevelOption> levelOptions = null)
        {
            string sideName = exteriorSide ? "External" : "Internal";
            string sideLower = exteriorSide ? "exterior" : "interior";
            Title = "MHNK " + sideName + " Wall Finish Options";
            Width = 660;
            Height = 710;
            MinWidth = 580;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            WpfGrid root = new WpfGrid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "Create " + sideName + " Wall Finishes",
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
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252))
            };
            root.Children.Add(summaryBorder);
            Grid.SetRow(summaryBorder, 1);

            summaryBorder.Child = new TextBlock
            {
                Text = summary ?? "",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };

            WpfGrid form = new WpfGrid
            {
                Margin = new Thickness(0, 0, 0, 12)
            };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(form);
            Grid.SetRow(form, 2);

            int row = 0;
            _typeCombo = new ComboBox
            {
                ItemsSource = typeOptions ?? new List<MhnkElementTypeOption>(),
                Height = 28,
                MinWidth = 280
            };
            _typeCombo.SelectedItem = SelectDefaultType(typeOptions, defaultTypeId);
            if (_typeCombo.SelectedItem == null && _typeCombo.Items.Count > 0)
            {
                _typeCombo.SelectedIndex = 0;
            }
            AddRow(form, ref row, "Finish wall type", _typeCombo);

            _materialCombo = new ComboBox
            {
                ItemsSource = BuildMaterialOptions(materialOptions),
                Height = 28,
                MinWidth = 280
            };
            if (_materialCombo.Items.Count > 0)
            {
                _materialCombo.SelectedIndex = 0;
            }
            _materialCombo.SelectionChanged += (_, __) => UpdateMaterialControls();
            AddRow(form, ref row, "Finish material", _materialCombo);

            _thicknessText = new TextBox
            {
                Text = "10",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Finish thickness (mm)", _thicknessText);

            _gapText = new TextBox
            {
                Text = "0",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, sideName + " gap (mm)", _gapText);

            _heightText = new TextBox
            {
                Text = exteriorSide ? "3" : "2.5",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Height (m)", _heightText);

            _baseOffsetText = new TextBox
            {
                Text = "0",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Base offset (mm)", _baseOffsetText);

            _topConstraintCombo = new ComboBox
            {
                ItemsSource = BuildTopConstraintOptions(levelOptions),
                Height = 28,
                MinWidth = 280
            };
            if (_topConstraintCombo.Items.Count > 0)
            {
                _topConstraintCombo.SelectedIndex = 0;
            }
            AddRow(form, ref row, "Top constraint", _topConstraintCombo);

            _topOffsetText = new TextBox
            {
                Text = "0",
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddRow(form, ref row, "Top offset (mm)", _topOffsetText);

            StackPanel checks = new StackPanel
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            root.Children.Add(checks);
            Grid.SetRow(checks, 3);

            _dedicatedTypeCheck = AddCheck(checks, "Create single-layer finish type from selected material", true);
            _matchConstraintsCheck = AddCheck(checks, "Match host wall base/top constraints", false);
            _matchConstraintsCheck.Checked += (_, __) => UpdateHeightControls();
            _matchConstraintsCheck.Unchecked += (_, __) => UpdateHeightControls();
            _skipExistingCheck = AddCheck(checks, "Skip finish walls already created on same host line", true);
            _joinWallsCheck = AddCheck(checks, "Join generated finish walls where geometry touches", true);
            _allowWallJoinsCheck = AddCheck(checks, "Allow wall joins at both ends", true);
            _roomBoundingCheck = AddCheck(checks, "Set finish walls Room Bounding", false);
            UpdateMaterialControls();
            UpdateHeightControls();

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 4);

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
                Content = "Continue",
                Width = 110,
                Height = 30,
                IsEnabled = _typeCombo.Items.Count > 0
            };
            createButton.Click += (_, __) => Accept();
            buttons.Children.Add(createButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkWallFinishOptions SelectedOptions { get; private set; }

        private static void AddRow(WpfGrid form, ref int row, string label, FrameworkElement editor)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            editor.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(editor);
            Grid.SetRow(editor, row);
            Grid.SetColumn(editor, 1);
            row++;
        }

        private static CheckBox AddCheck(WpfPanel panel, string text, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            panel.Children.Add(checkBox);
            return checkBox;
        }

        private static MhnkElementTypeOption SelectDefaultType(IList<MhnkElementTypeOption> typeOptions, ElementId defaultTypeId)
        {
            if (typeOptions == null || defaultTypeId == null)
            {
                return null;
            }

            return typeOptions.FirstOrDefault(x => x.Id != null && x.Id.Value == defaultTypeId.Value);
        }

        private static IList<MhnkLevelOption> BuildTopConstraintOptions(IList<MhnkLevelOption> levelOptions)
        {
            var options = new List<MhnkLevelOption>
            {
                new MhnkLevelOption(ElementId.InvalidElementId, "Unconnected height"),
                new MhnkLevelOption(ElementId.InvalidElementId, "Level Above", isLevelAbove: true)
            };

            foreach (MhnkLevelOption level in levelOptions ?? new List<MhnkLevelOption>())
            {
                if (level == null || level.IsAllLevels || level.IsLevelAbove)
                {
                    continue;
                }

                options.Add(level);
            }

            return options;
        }

        private void Accept()
        {
            var selectedType = _typeCombo.SelectedItem as MhnkElementTypeOption;
            if (selectedType == null)
            {
                MessageBox.Show(this, "Select a finish wall type before continuing.", "MHNK External Wall Finish", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMaterial = _materialCombo.SelectedItem as MhnkMaterialOption;
            bool keepTypeMaterial = selectedMaterial == null || selectedMaterial.KeepTypeMaterial;
            double thicknessMillimeters = ParseNumber(_thicknessText.Text, 10.0);
            double gapMillimeters = ParseNumber(_gapText.Text, 0.0);
            double heightMeters = ParseNumber(_heightText.Text, 3.0);
            double baseOffsetMillimeters = ParseNumber(_baseOffsetText.Text, 0.0);
            double topOffsetMillimeters = ParseNumber(_topOffsetText.Text, 0.0);
            var topConstraint = _topConstraintCombo.SelectedItem as MhnkLevelOption;
            if (!keepTypeMaterial && thicknessMillimeters <= 0.0)
            {
                MessageBox.Show(this, "Finish thickness must be greater than 0 when creating a material finish type.", "MHNK External Wall Finish", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_matchConstraintsCheck.IsChecked != true && heightMeters <= 0.0)
            {
                MessageBox.Show(this, "Height must be greater than 0 when host constraints are not matched.", "MHNK External Wall Finish", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedOptions = new MhnkWallFinishOptions
            {
                TypeId = selectedType.Id,
                TypeName = selectedType.Name,
                MaterialId = keepTypeMaterial ? ElementId.InvalidElementId : selectedMaterial.Id,
                MaterialName = keepTypeMaterial ? "" : selectedMaterial.Name,
                UseSelectedTypeMaterial = keepTypeMaterial,
                ThicknessMillimeters = Math.Max(1.0, thicknessMillimeters),
                GapMillimeters = gapMillimeters,
                CreationHeightMeters = Math.Max(0.01, heightMeters),
                BaseOffsetMillimeters = baseOffsetMillimeters,
                TopConstraintLevelId = topConstraint != null ? topConstraint.Id : ElementId.InvalidElementId,
                TopConstraintName = topConstraint != null ? topConstraint.Name : "",
                UseLevelAboveTopConstraint = topConstraint != null && topConstraint.IsLevelAbove,
                TopOffsetMillimeters = topOffsetMillimeters,
                CreateDedicatedMaterialType = !keepTypeMaterial && _dedicatedTypeCheck.IsChecked == true,
                MatchHostConstraints = _matchConstraintsCheck.IsChecked == true,
                SkipExistingFinishWalls = _skipExistingCheck.IsChecked == true,
                JoinGeneratedWalls = _joinWallsCheck.IsChecked == true,
                AllowWallJoinsAtEnds = _allowWallJoinsCheck.IsChecked == true,
                SetRoomBounding = _roomBoundingCheck.IsChecked == true
            };

            DialogResult = true;
            Close();
        }

        private void UpdateHeightControls()
        {
            bool enabled = _matchConstraintsCheck == null || _matchConstraintsCheck.IsChecked != true;
            if (_heightText != null) _heightText.IsEnabled = enabled;
            if (_baseOffsetText != null) _baseOffsetText.IsEnabled = enabled;
            if (_topConstraintCombo != null) _topConstraintCombo.IsEnabled = enabled;
            if (_topOffsetText != null) _topOffsetText.IsEnabled = enabled;
        }

        private static double ParseNumber(string text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current))
            {
                return current;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant))
            {
                return invariant;
            }

            return fallback;
        }

        private void UpdateMaterialControls()
        {
            var selectedMaterial = _materialCombo.SelectedItem as MhnkMaterialOption;
            bool materialSelected = selectedMaterial != null && !selectedMaterial.KeepTypeMaterial;
            if (_dedicatedTypeCheck != null)
            {
                _dedicatedTypeCheck.IsEnabled = materialSelected;
                if (!materialSelected)
                {
                    _dedicatedTypeCheck.IsChecked = false;
                }
                else if (_dedicatedTypeCheck.IsChecked != true)
                {
                    _dedicatedTypeCheck.IsChecked = true;
                }
            }

            if (_thicknessText != null)
            {
                _thicknessText.IsEnabled = materialSelected;
            }
        }

        private static IList<MhnkMaterialOption> BuildMaterialOptions(IList<MhnkMaterialOption> materialOptions)
        {
            var options = new List<MhnkMaterialOption>
            {
                new MhnkMaterialOption(ElementId.InvalidElementId, "Use selected wall type material", true)
            };

            foreach (MhnkMaterialOption option in materialOptions ?? new List<MhnkMaterialOption>())
            {
                if (option != null && option.Id != null && option.Id != ElementId.InvalidElementId)
                {
                    options.Add(option);
                }
            }

            return options;
        }
    }
}
