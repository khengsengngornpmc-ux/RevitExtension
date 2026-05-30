using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private readonly ObservableCollection<QsMeasurementSettingRow> _qsMeasurementSettingsRows =
            new ObservableCollection<QsMeasurementSettingRow>();
        private readonly ObservableCollection<QsMeasurementSettingRow> _qsMeasurementTasRows =
            new ObservableCollection<QsMeasurementSettingRow>();

        private QsMeasurementSettingsProfile _qsMeasurementSettingsProfile =
            QsMeasurementSettingsProfile.CreateDefault();

        private bool _qsMeasurementSettingsDirty;
        private bool _qsMeasurementSettingsLoading;

        private void InitializeQsMeasurementSettings()
        {
            QsMeasurementSettingsProfile profile = TryLoadQsMeasurementSettingsFromDefaultPath();
            if (profile == null)
            {
                profile = QsMeasurementSettingsProfile.CreateDefault();
            }

            LoadQsMeasurementSettingsProfile(profile, false);
        }

        private QsMeasurementSettingsProfile TryLoadQsMeasurementSettingsFromDefaultPath()
        {
            string path = GetQsMeasurementSettingsDefaultPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                QsMeasurementSettingsProfile profile = CamboBimJson.Deserialize<QsMeasurementSettingsProfile>(json);
                if (profile == null || profile.Rules == null || profile.Rules.Count == 0)
                {
                    return null;
                }

                return profile;
            }
            catch
            {
                return null;
            }
        }

        private void LoadQsMeasurementSettingsProfile(QsMeasurementSettingsProfile profile, bool markDirty)
        {
            _qsMeasurementSettingsLoading = true;

            foreach (QsMeasurementSettingRow oldRow in GetQsMeasurementSettingsAllRows())
            {
                oldRow.PropertyChanged -= OnQsMeasurementSettingRowChanged;
            }

            _qsMeasurementSettingsProfile = profile ?? QsMeasurementSettingsProfile.CreateDefault();
            _qsMeasurementSettingsProfile.Normalize();

            foreach (QsMeasurementSettingRow row in GetQsMeasurementSettingsAllRows())
            {
                row.PropertyChanged += OnQsMeasurementSettingRowChanged;
            }

            RefreshQsMeasurementCategoryList();
            RefreshQsMeasurementConditionList();
            ApplyQsMeasurementSettingsFilter();
            if (QsMeasurementProfileNameTextBox != null)
            {
                QsMeasurementProfileNameTextBox.Text = _qsMeasurementSettingsProfile.ProfileName ?? "";
            }

            _qsMeasurementSettingsDirty = markDirty;
            _qsMeasurementSettingsLoading = false;
            UpdateQsMeasurementSettingsKpis();
        }

        private IEnumerable<QsMeasurementSettingRow> GetQsMeasurementSettingsAllRows()
        {
            if (_qsMeasurementSettingsProfile == null || _qsMeasurementSettingsProfile.Rules == null)
            {
                return Enumerable.Empty<QsMeasurementSettingRow>();
            }

            return _qsMeasurementSettingsProfile.Rules.Where(r => r != null);
        }

        private void RefreshQsMeasurementCategoryList()
        {
            if (QsMeasurementCategoryCombo == null)
            {
                return;
            }

            string current = QsMeasurementCategoryCombo.SelectedItem as string;
            List<string> categories = GetQsMeasurementSettingsAllRows()
                .Select(r => r.Category ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetQsMeasurementCategoryOrder)
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            categories.Insert(0, "All Elements");
            QsMeasurementCategoryCombo.ItemsSource = categories;
            if (!string.IsNullOrWhiteSpace(current) &&
                categories.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                QsMeasurementCategoryCombo.SelectedItem = current;
            }
            else
            {
                QsMeasurementCategoryCombo.SelectedIndex = 0;
            }
        }

        private static int GetQsMeasurementCategoryOrder(string category)
        {
            string[] order =
            {
                "Excavation",
                "Foundation",
                "Pile",
                "Raft Foundation",
                "Pad Foundation",
                "Column",
                "Beam",
                "Wall",
                "Slab",
                "Slab Opening",
                "Kerb",
                "Others",
                "Wall Finish",
                "Ceiling Finish",
                "Suspended Ceiling",
                "Lintel",
                "Floor Finish",
                "Waterproof",
                "Drop Panel",
                "Eave",
                "Staircase",
                "Roof",
                "Steel/Composite Slab"
            };

            for (int i = 0; i < order.Length; i++)
            {
                if (string.Equals(order[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return order.Length + 1;
        }

        private void RefreshQsMeasurementConditionList()
        {
            if (QsMeasurementConditionCombo == null)
            {
                return;
            }

            string current = QsMeasurementConditionCombo.SelectedItem as string;
            string selectedCategory = QsMeasurementCategoryCombo?.SelectedItem as string;
            bool allElements = IsAllMeasurementElementSelection(selectedCategory);

            List<string> conditions = GetQsMeasurementSettingsAllRows()
                .Where(r => allElements || string.Equals(r.Category, selectedCategory, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Method ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetQsMeasurementConditionOrder)
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            conditions.Insert(0, "All Conditions");
            QsMeasurementConditionCombo.ItemsSource = conditions;
            if (!string.IsNullOrWhiteSpace(current) &&
                conditions.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                QsMeasurementConditionCombo.SelectedItem = current;
            }
            else
            {
                QsMeasurementConditionCombo.SelectedIndex = 0;
            }
        }

        private static int GetQsMeasurementConditionOrder(string condition)
        {
            string[] order =
            {
                "Option",
                "Deduction",
                "Condition",
                "Method",
                "Numeric",
                "Elevation",
                "Staged",
                "Settings",
                "Area",
                "Classification",
                "Segmentation"
            };

            for (int i = 0; i < order.Length; i++)
            {
                if (string.Equals(order[i], condition, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return order.Length + 1;
        }

        private static bool IsAllMeasurementElementSelection(string selectedCategory)
        {
            return string.IsNullOrWhiteSpace(selectedCategory) ||
                   string.Equals(selectedCategory, "All Elements", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectedCategory, "All Categories", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllMeasurementConditionSelection(string selectedCondition)
        {
            return string.IsNullOrWhiteSpace(selectedCondition) ||
                   string.Equals(selectedCondition, "All Conditions", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(selectedCondition, "All Rule Types", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyQsMeasurementSettingsFilter()
        {
            string selectedCategory = QsMeasurementCategoryCombo?.SelectedItem as string;
            string selectedCondition = QsMeasurementConditionCombo?.SelectedItem as string;
            bool allElements = IsAllMeasurementElementSelection(selectedCategory);
            bool allConditions = IsAllMeasurementConditionSelection(selectedCondition);

            _qsMeasurementSettingsRows.Clear();
            _qsMeasurementTasRows.Clear();
            foreach (QsMeasurementSettingRow row in GetQsMeasurementSettingsAllRows()
                         .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                         .ThenBy(r => r.SortOrder)
                          .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase))
            {
                bool elementMatches = allElements ||
                                      string.Equals(row.Category, selectedCategory, StringComparison.OrdinalIgnoreCase);
                bool conditionMatches = allConditions ||
                                        string.Equals(row.Method, selectedCondition, StringComparison.OrdinalIgnoreCase);

                if (elementMatches && conditionMatches)
                {
                    _qsMeasurementSettingsRows.Add(row);
                    if (IsQsMeasurementTasRowVisible(row))
                    {
                        row.DisplayIndex = _qsMeasurementTasRows.Count + 1;
                        _qsMeasurementTasRows.Add(row);
                    }
                }
            }

            if (QsMeasurementSettingsGrid != null)
            {
                QsMeasurementSettingsGrid.ItemsSource = _qsMeasurementSettingsRows;
            }

            if (QsMeasurementTasGrid != null)
            {
                QsMeasurementTasGrid.ItemsSource = _qsMeasurementTasRows;
            }

            UpdateQsMeasurementSettingsKpis();
        }

        private bool IsQsMeasurementTasRowVisible(QsMeasurementSettingRow row)
        {
            if (row == null || !row.IsCubicostVisible)
            {
                return false;
            }

            string code = row.Code ?? "";
            if (string.Equals(code, "WF.SUSPENDED.SETVALUE", StringComparison.OrdinalIgnoreCase))
            {
                return IsWallFinishSetValueVisible("WF.INT.TOP");
            }

            if (string.Equals(code, "WF.CUSTOM.LAYERS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.CUSTOM.INT.BOTTOM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.CUSTOM.INT.TOP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "WF.CUSTOM.EXT.METHOD", StringComparison.OrdinalIgnoreCase))
            {
                return IsWallFinishCustomRowsVisible();
            }

            if (string.Equals(code, "WF.CUSTOM.SUSPENDED.SETVALUE", StringComparison.OrdinalIgnoreCase))
            {
                return IsWallFinishCustomRowsVisible() && IsWallFinishSetValueVisible("WF.CUSTOM.INT.TOP");
            }

            return true;
        }

        private bool IsWallFinishCustomRowsVisible()
        {
            QsMeasurementSettingRow row = _qsMeasurementSettingsProfile?.FindRule("WF.CUSTOM.SHOW");
            if (row == null || !row.IsEnabled)
            {
                return false;
            }

            string value = (row.Value ?? "").Trim();
            return value.StartsWith("1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsWallFinishSetValueVisible(string methodCode)
        {
            string value = GetQsMeasurementRuleValue(methodCode, "0 If with suspended ceiling");
            return value.StartsWith("0 If with suspended ceiling", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("2 Select floor bottom elevation", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateQsMeasurementSettingsKpis()
        {
            int total = GetQsMeasurementSettingsAllRows().Count();
            int active = GetQsMeasurementSettingsAllRows().Count(r => r.IsEnabled);
            int visible = _qsMeasurementSettingsRows.Count;

            if (QsMeasurementProfileBadgeText != null)
            {
                QsMeasurementProfileBadgeText.Text = string.IsNullOrWhiteSpace(_qsMeasurementSettingsProfile?.ProfileName)
                    ? "MHNK Measurement Settings"
                    : _qsMeasurementSettingsProfile.ProfileName;
            }

            if (QsMeasurementRuleCountText != null)
            {
                QsMeasurementRuleCountText.Text = visible.ToString(CultureInfo.InvariantCulture) + " shown / " +
                                                  total.ToString(CultureInfo.InvariantCulture);
            }

            if (QsMeasurementActiveCountText != null)
            {
                QsMeasurementActiveCountText.Text = active.ToString(CultureInfo.InvariantCulture) + " active";
            }

            if (QsMeasurementStatusText != null)
            {
                QsMeasurementStatusText.Text = _qsMeasurementSettingsDirty ? "Modified" : "Saved";
            }
        }

        private void MarkQsMeasurementSettingsDirty()
        {
            if (_qsMeasurementSettingsLoading)
            {
                return;
            }

            _qsMeasurementSettingsDirty = true;
            if (_qsMeasurementSettingsProfile != null)
            {
                _qsMeasurementSettingsProfile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            UpdateQsMeasurementSettingsKpis();
        }

        private void OnQsMeasurementSettingRowChanged(object sender, PropertyChangedEventArgs e)
        {
            bool shouldRefreshFilter = false;
            if (!_qsMeasurementSettingsLoading &&
                string.Equals(e?.PropertyName, "Method", StringComparison.OrdinalIgnoreCase))
            {
                RefreshQsMeasurementConditionList();
                shouldRefreshFilter = true;
            }

            if (!_qsMeasurementSettingsLoading &&
                string.Equals(e?.PropertyName, "Value", StringComparison.OrdinalIgnoreCase) &&
                IsQsMeasurementVisibilityDriver(sender as QsMeasurementSettingRow))
            {
                shouldRefreshFilter = true;
            }

            if (shouldRefreshFilter)
            {
                ApplyQsMeasurementSettingsFilter();
            }

            MarkQsMeasurementSettingsDirty();
        }

        private static bool IsQsMeasurementVisibilityDriver(QsMeasurementSettingRow row)
        {
            string code = row?.Code ?? "";
            return string.Equals(code, "WF.INT.TOP", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "WF.CUSTOM.SHOW", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(code, "WF.CUSTOM.INT.TOP", StringComparison.OrdinalIgnoreCase);
        }

        private void OnQsMeasurementProfileNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_qsMeasurementSettingsLoading || _qsMeasurementSettingsProfile == null)
            {
                return;
            }

            _qsMeasurementSettingsProfile.ProfileName = QsMeasurementProfileNameTextBox?.Text ?? "";
            MarkQsMeasurementSettingsDirty();
        }

        private void OnQsMeasurementCategoryChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_qsMeasurementSettingsLoading)
            {
                return;
            }

            RefreshQsMeasurementConditionList();
            ApplyQsMeasurementSettingsFilter();
        }

        private void OnQsMeasurementConditionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_qsMeasurementSettingsLoading)
            {
                return;
            }

            ApplyQsMeasurementSettingsFilter();
        }

        private void OnQsMeasurementDetailsClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            QsMeasurementSettingRow row = button?.DataContext as QsMeasurementSettingRow;
            if (row == null)
            {
                return;
            }

            if (string.Equals(row.Code, "FOUN.SIDE.SETTINGS", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsFoundationSideFormworkSettingsDialog();
            }
            else if (string.Equals(row.Code, "WALL.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsWallEdgeSegmentationDialog();
            }
            else if (string.Equals(row.Code, "WALL.OPENING.EDGE.CONDITION", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsWallOpeningEdgeConditionDialog();
            }
            else if (string.Equals(row.Code, "SLAB.EDGE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsSlabEdgeSegmentationDialog();
            }
            else if (string.Equals(row.Code, "SLAB.OPENING.SIDE", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsSlabOpeningSideConditionDialog();
            }
            else if (string.Equals(row.Code, "SC.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsSuspendedCeilingVerticalSegmentationDialog();
            }
            else if (string.Equals(row.Code, "SC.AREA.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsSuspendedCeilingAreaSegmentationDialog();
            }
            else if (string.Equals(row.Code, "FF.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsFloorFinishVerticalSegmentationDialog();
            }
            else if (string.Equals(row.Code, "STAIR.SIDE.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsStaircaseSideSegmentationDialog();
            }
            else if (string.Equals(row.Code, "ROOF.WP.VERTICAL.SEGMENT", StringComparison.OrdinalIgnoreCase))
            {
                OpenQsRoofWaterproofVerticalSegmentationDialog();
            }
        }

        private void OpenQsFoundationSideFormworkSettingsDialog()
        {
            List<QsFoundationSideSettingsEditorContext> contexts = BuildQsFoundationSideSettingsContexts();

            var dialog = new Window
            {
                Owner = this,
                Title = "Measurement settings for side formwork of foundation element",
                Width = 640,
                Height = 455,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tabControl = new TabControl();
            foreach (QsFoundationSideSettingsEditorContext context in contexts)
            {
                tabControl.Items.Add(new TabItem
                {
                    Header = context.Header,
                    Content = BuildQsFoundationSideSettingsPanel(context)
                });
            }

            Grid.SetRow(tabControl, 0);
            root.Children.Add(tabControl);

            var bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var okButton = new Button
            {
                Content = "OK",
                Width = 76,
                Height = 28,
                IsDefault = true
            };
            okButton.Click += (s, args) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };
            bottom.Children.Add(okButton);

            Grid.SetRow(bottom, 1);
            root.Children.Add(bottom);
            dialog.Content = root;

            bool? result = dialog.ShowDialog();
            if (result != true)
            {
                return;
            }

            SaveQsFoundationSideSettingsContexts(contexts);
            MarkQsMeasurementSettingsDirty();
            ApplyQsMeasurementSettingsFilter();
            ShowStatus("Foundation side formwork measurement settings updated.");
        }

        private UIElement BuildQsFoundationSideSettingsPanel(QsFoundationSideSettingsEditorContext context)
        {
            var panel = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid methodGrid = BuildQsFoundationSideMethodGrid(context);
            Grid.SetRow(methodGrid, 0);
            panel.Children.Add(methodGrid);

            var segmentTitle = new TextBlock
            {
                Text = "Segmentation Standard",
                Margin = new Thickness(0, 14, 0, 6),
                Foreground = new SolidColorBrush(Color.FromRgb(36, 64, 97)),
                FontWeight = FontWeights.SemiBold
            };
            Grid.SetRow(segmentTitle, 1);
            panel.Children.Add(segmentTitle);

            var segmentGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 250, 255)),
                ItemsSource = context.Segments,
                MinHeight = 150
            };
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "",
                Binding = new Binding("DisplayIndex"),
                IsReadOnly = true,
                Width = 44
            });
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Condition",
                Binding = new Binding("Condition") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 140
            });
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Maximum Height (m)",
                Binding = new Binding("MaximumHeight") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 190
            });

            Grid.SetRow(segmentGrid, 2);
            panel.Children.Add(segmentGrid);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            AddFooterText(footer, "And thereafter in", 0);
            var thereafterText = new TextBox
            {
                Height = 26,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            thereafterText.SetBinding(TextBox.TextProperty, new Binding("Thereafter")
            {
                Source = context,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(thereafterText, 1);
            footer.Children.Add(thereafterText);
            AddFooterText(footer, "m stages", 2);

            var addButton = new Button
            {
                Content = "Add",
                Width = 78,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0)
            };
            addButton.Click += (s, e) =>
            {
                context.Segments.Add(new QsFoundationSideSegmentRow
                {
                    Condition = "<=",
                    MaximumHeight = string.IsNullOrWhiteSpace(context.Thereafter) ? "0.500" : context.Thereafter
                });
                RefreshFoundationSideSegmentIndexes(context);
                segmentGrid.Items.Refresh();
            };
            Grid.SetColumn(addButton, 4);
            footer.Children.Add(addButton);

            var deleteButton = new Button
            {
                Content = "Delete",
                Width = 78,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0)
            };
            deleteButton.Click += (s, e) =>
            {
                QsFoundationSideSegmentRow selected = segmentGrid.SelectedItem as QsFoundationSideSegmentRow;
                if (selected != null)
                {
                    context.Segments.Remove(selected);
                    RefreshFoundationSideSegmentIndexes(context);
                    segmentGrid.Items.Refresh();
                }
            };
            Grid.SetColumn(deleteButton, 5);
            footer.Children.Add(deleteButton);

            Grid.SetRow(footer, 3);
            panel.Children.Add(footer);

            return panel;
        }

        private static Grid BuildQsFoundationSideMethodGrid(QsFoundationSideSettingsEditorContext context)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });

            AddGridHeader(grid, "", 0, 0);
            AddGridHeader(grid, "Description", 0, 1);
            AddGridHeader(grid, "Value", 0, 2);

            QsFoundationSideOptionRow methodRow = context.Rows[0];
            QsFoundationSideOptionRow conditionRow = context.Rows[1];
            AddGridIndex(grid, "1", 1);
            AddGridText(grid, methodRow.Description, 1, 1);
            var methodCombo = new ComboBox
            {
                ItemsSource = methodRow.ValueChoices,
                IsEditable = false,
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            methodCombo.SetBinding(ComboBox.TextProperty, new Binding("Value")
            {
                Source = methodRow,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetRow(methodCombo, 1);
            Grid.SetColumn(methodCombo, 2);
            grid.Children.Add(methodCombo);

            AddGridIndex(grid, "2", 2);
            AddGridText(grid, conditionRow.Description, 2, 1);
            var conditionText = new TextBox
            {
                Height = 24,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            conditionText.SetBinding(TextBox.TextProperty, new Binding("Value")
            {
                Source = conditionRow,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetRow(conditionText, 2);
            Grid.SetColumn(conditionText, 2);
            grid.Children.Add(conditionText);

            return grid;
        }

        private static void AddGridHeader(Grid grid, string text, int row, int column)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(112, 174, 235)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(175, 195, 222)),
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            grid.Children.Add(border);
        }

        private static void AddGridIndex(Grid grid, string text, int row)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 232)),
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock
                {
                    Text = text,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, 0);
            grid.Children.Add(border);
        }

        private static void AddGridText(Grid grid, string text, int row, int column)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 232)),
                BorderThickness = new Thickness(0.5),
                Child = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            grid.Children.Add(border);
        }

        private static void AddFooterText(Grid footer, string text, int column)
        {
            var block = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(block, column);
            footer.Children.Add(block);
        }

        private List<QsFoundationSideSettingsEditorContext> BuildQsFoundationSideSettingsContexts()
        {
            string[] keys = { "BLINDING", "PAD", "PILECAP", "RAFT", "GROUNDBEAM", "STRIP" };
            string[] headers = { "Blinding", "Pad Foundation", "Pile Cap", "Raft Foundation", "Ground Beam", "Strip Foundation" };
            var result = new List<QsFoundationSideSettingsEditorContext>();

            for (int i = 0; i < keys.Length; i++)
            {
                string prefix = "FOUN.SIDE.SETTING." + keys[i] + ".";
                var context = new QsFoundationSideSettingsEditorContext
                {
                    Key = keys[i],
                    Header = headers[i],
                    Thereafter = GetQsMeasurementRuleValue(prefix + "THEREAFTER", "0.500")
                };
                context.Rows.Add(new QsFoundationSideOptionRow
                {
                    Description = "Method for calculating side formwork",
                    Value = GetQsMeasurementRuleValue(prefix + "METHOD", QsMeasurementSettingsProfile.FoundationSideDefaultMethodValue()),
                    ValueChoices = BuildFoundationSideMethodChoices()
                });
                context.Rows.Add(new QsFoundationSideOptionRow
                {
                    Description = "Condition for calculating side formwork in stages",
                    Value = GetQsMeasurementRuleValue(prefix + "CONDITION", "1.000")
                });

                for (int segment = 1; segment <= 12; segment++)
                {
                    QsMeasurementSettingRow row = _qsMeasurementSettingsProfile?.FindRule(prefix + "SEG." + segment.ToString(CultureInfo.InvariantCulture));
                    if (row == null)
                    {
                        continue;
                    }

                    if (!row.IsEnabled && segment > 3)
                    {
                        continue;
                    }

                    context.Segments.Add(new QsFoundationSideSegmentRow
                    {
                        Condition = "<=",
                        MaximumHeight = string.IsNullOrWhiteSpace(row.Value) ? "0.500" : row.Value
                    });
                }

                if (context.Segments.Count == 0)
                {
                    context.Segments.Add(new QsFoundationSideSegmentRow { Condition = "<=", MaximumHeight = "0.250" });
                    context.Segments.Add(new QsFoundationSideSegmentRow { Condition = "<=", MaximumHeight = "0.500" });
                    context.Segments.Add(new QsFoundationSideSegmentRow { Condition = "<=", MaximumHeight = "1.000" });
                }

                RefreshFoundationSideSegmentIndexes(context);
                result.Add(context);
            }

            return result;
        }

        private void SaveQsFoundationSideSettingsContexts(IEnumerable<QsFoundationSideSettingsEditorContext> contexts)
        {
            foreach (QsFoundationSideSettingsEditorContext context in contexts ?? Enumerable.Empty<QsFoundationSideSettingsEditorContext>())
            {
                string prefix = "FOUN.SIDE.SETTING." + context.Key + ".";
                string label = context.Header ?? "";
                SetQsMeasurementRuleValue(prefix + "METHOD", "Foundation", "Method for calculating side formwork", label, context.Rows[0].Value, "-", "Method", true);
                SetQsMeasurementRuleValue(prefix + "CONDITION", "Foundation", "Condition for calculating side formwork in stages", label, context.Rows[1].Value, "m", "Numeric", true);

                int index = 1;
                foreach (QsFoundationSideSegmentRow segment in context.Segments)
                {
                    SetQsMeasurementRuleValue(
                        prefix + "SEG." + index.ToString(CultureInfo.InvariantCulture),
                        "Foundation",
                        "Segmentation Standard",
                        label,
                        string.IsNullOrWhiteSpace(segment.MaximumHeight) ? "0.500" : segment.MaximumHeight,
                        "m",
                        "Segmentation",
                        true);
                    index++;
                }

                for (int disabledIndex = index; disabledIndex <= 12; disabledIndex++)
                {
                    QsMeasurementSettingRow stale = _qsMeasurementSettingsProfile?.FindRule(prefix + "SEG." + disabledIndex.ToString(CultureInfo.InvariantCulture));
                    if (stale != null)
                    {
                        stale.IsEnabled = false;
                    }
                }

                SetQsMeasurementRuleValue(prefix + "THEREAFTER", "Foundation", "And thereafter in m stages", label, context.Thereafter, "m", "Numeric", true);
            }
        }

        private void OpenQsWallEdgeSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Wall",
                "WALL.EDGE.SEGMENT",
                "Open or select the segmentation standard for wall edge and break formwork",
                "Segmentation standard of edge and break formwork",
                "Maximum Width (m)",
                4,
                "Wall edge and break segmentation settings updated.");
        }

        private void OpenQsSlabEdgeSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Slab",
                "SLAB.EDGE.SEGMENT",
                "Open or select the segmentation standard for slab edge and break formwork",
                "Segmentation Standard",
                "Maximum Height (m)",
                3,
                "Slab edge and break segmentation settings updated.");
        }

        private void OpenQsSuspendedCeilingVerticalSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Suspended Ceiling",
                "SC.VERTICAL.SEGMENT",
                "Segmentation standard of suspended ceiling to vertical surface",
                "Open or select the height segmentation standard for suspended ceiling to vertical surface",
                "Segmentation standard of suspended ceiling to vertical surface",
                "Suspended ceiling to vertical surface",
                "Height Segmentation",
                "Maximum Height (m)",
                2,
                new[] { "0.500", "" },
                "0.500",
                "Suspended ceiling vertical surface segmentation settings updated.");
        }

        private void OpenQsSuspendedCeilingAreaSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Suspended Ceiling",
                "SC.AREA.SEGMENT",
                "Segmentation standard of suspended ceiling",
                "Open or select the depth segmentation standard for suspended ceiling area",
                "Segmentation standard of suspended ceiling",
                "Suspended ceiling area",
                "Depth Segmentation",
                "Maximum Height (m)",
                2,
                new[] { "0.150", "0.500" },
                "0.500",
                "Suspended ceiling area segmentation settings updated.");
        }

        private void OpenQsFloorFinishVerticalSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Floor Finish",
                "FF.VERTICAL.SEGMENT",
                "Segmentation standard of floor finish to vertical surface",
                "Open or select the height segmentation standard for floor finish to vertical surface",
                "Segmentation standard of floor finish to vertical surface",
                "Floor finish to vertical surface",
                "Height Segmentation",
                "Maximum Height (m)",
                3,
                new[] { "0.150", "0.225", "0.300" },
                "0.075",
                "Floor finish vertical surface segmentation settings updated.");
        }

        private void OpenQsStaircaseSideSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Staircase",
                "STAIR.SIDE.SEGMENT",
                "Segmentation standard of side formwork",
                "Open or select the height segmentation standard for staircase side formwork",
                "Segmentation standard of side formwork",
                "Side formwork",
                "Segmentation Standard",
                "Maximum Height (m)",
                3,
                new[] { "0.250", "0.500", "1.000" },
                "0.500",
                "Staircase side formwork segmentation settings updated.");
        }

        private void OpenQsRoofWaterproofVerticalSegmentationDialog()
        {
            OpenQsEdgeSegmentationDialog(
                "Roof",
                "ROOF.WP.VERTICAL.SEGMENT",
                "Segmentation standard of waterproof to vertical surface",
                "Open or select the height segmentation standard for waterproof to vertical surface",
                "Segmentation standard of waterproof to vertical surface",
                "Waterproof to vertical surface",
                "Height Segmentation",
                "Maximum Height (m)",
                3,
                new[] { "0.150", "0.225", "0.300" },
                "0.075",
                "Roof waterproof vertical surface segmentation settings updated.");
        }

        private void OpenQsEdgeSegmentationDialog(string category, string code, string option, string dialogTitle, string maximumColumnHeader, int defaultSegmentCount, string statusMessage)
        {
            OpenQsEdgeSegmentationDialog(
                category,
                code,
                "Segmentation standard of edge and break formwork",
                option,
                "Segmentation standard of edge and break formwork",
                "Edge and break formwork",
                dialogTitle,
                maximumColumnHeader,
                defaultSegmentCount,
                null,
                "0.500",
                statusMessage);
        }

        private void OpenQsEdgeSegmentationDialog(
            string category,
            string code,
            string description,
            string option,
            string displayValue,
            string segmentOption,
            string dialogTitle,
            string maximumColumnHeader,
            int defaultSegmentCount,
            IReadOnlyList<string> defaultSegments,
            string defaultThereafter,
            string statusMessage)
        {
            QsWallEdgeSegmentationContext context = BuildQsEdgeSegmentationContext(code, defaultSegmentCount, defaultSegments, defaultThereafter);
            var dialog = new Window
            {
                Owner = this,
                Title = dialogTitle,
                Width = 510,
                Height = 295,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var segmentGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 250, 255)),
                ItemsSource = context.Segments,
                MinHeight = 150
            };
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "",
                Binding = new Binding("DisplayIndex"),
                IsReadOnly = true,
                Width = 44
            });
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Condition",
                Binding = new Binding("Condition") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 130
            });
            segmentGrid.Columns.Add(new DataGridTextColumn
            {
                Header = maximumColumnHeader,
                Binding = new Binding("MaximumWidth") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 190
            });

            Grid.SetRow(segmentGrid, 0);
            root.Children.Add(segmentGrid);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });

            AddFooterText(footer, "And thereafter in", 0);
            var thereafterText = new TextBox
            {
                Height = 26,
                Margin = new Thickness(6, 0, 6, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            thereafterText.SetBinding(TextBox.TextProperty, new Binding("Thereafter")
            {
                Source = context,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            Grid.SetColumn(thereafterText, 1);
            footer.Children.Add(thereafterText);
            AddFooterText(footer, "m stages", 2);

            var addButton = new Button { Content = "Add", Width = 72, Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            addButton.Click += (s, e) =>
            {
                context.Segments.Add(new QsWallEdgeSegmentRow
                {
                    Condition = "<=",
                    MaximumWidth = string.IsNullOrWhiteSpace(context.Thereafter) ? "0.500" : context.Thereafter
                });
                RefreshWallEdgeSegmentIndexes(context);
                segmentGrid.Items.Refresh();
            };
            Grid.SetColumn(addButton, 4);
            footer.Children.Add(addButton);

            var deleteButton = new Button { Content = "Delete", Width = 72, Height = 28, Margin = new Thickness(6, 0, 0, 0) };
            deleteButton.Click += (s, e) =>
            {
                QsWallEdgeSegmentRow selected = segmentGrid.SelectedItem as QsWallEdgeSegmentRow;
                if (selected != null)
                {
                    context.Segments.Remove(selected);
                    RefreshWallEdgeSegmentIndexes(context);
                    segmentGrid.Items.Refresh();
                }
            };
            Grid.SetColumn(deleteButton, 5);
            footer.Children.Add(deleteButton);

            var okButton = new Button { Content = "OK", Width = 72, Height = 28, Margin = new Thickness(6, 0, 0, 0), IsDefault = true };
            okButton.Click += (s, e) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };
            Grid.SetColumn(okButton, 6);
            footer.Children.Add(okButton);

            Grid.SetRow(footer, 1);
            root.Children.Add(footer);
            dialog.Content = root;

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SaveQsEdgeSegmentationContext(context, category, code, description, option, displayValue, segmentOption);
            MarkQsMeasurementSettingsDirty();
            ApplyQsMeasurementSettingsFilter();
            ShowStatus(statusMessage);
        }

        private QsWallEdgeSegmentationContext BuildQsEdgeSegmentationContext(string code, int defaultSegmentCount, IReadOnlyList<string> defaultSegments, string defaultThereafter)
        {
            var context = new QsWallEdgeSegmentationContext
            {
                Thereafter = GetQsMeasurementRuleValue(code + ".THEREAFTER", string.IsNullOrWhiteSpace(defaultThereafter) ? "0.500" : defaultThereafter)
            };

            for (int segment = 1; segment <= 12; segment++)
            {
                QsMeasurementSettingRow row = _qsMeasurementSettingsProfile?.FindRule(code + "." + segment.ToString(CultureInfo.InvariantCulture));
                if (row == null || !row.IsEnabled)
                {
                    continue;
                }

                context.Segments.Add(new QsWallEdgeSegmentRow
                {
                    Condition = "<=",
                    MaximumWidth = string.IsNullOrWhiteSpace(row.Value) ? "0.500" : row.Value
                });
            }

            if (context.Segments.Count == 0)
            {
                if (defaultSegments != null && defaultSegments.Count > 0)
                {
                    foreach (string defaultSegment in defaultSegments)
                    {
                        context.Segments.Add(new QsWallEdgeSegmentRow { Condition = "<=", MaximumWidth = defaultSegment ?? "" });
                    }
                }
                else
                {
                    context.Segments.Add(new QsWallEdgeSegmentRow { Condition = "<=", MaximumWidth = "0.250" });
                    context.Segments.Add(new QsWallEdgeSegmentRow { Condition = "<=", MaximumWidth = "0.500" });
                    context.Segments.Add(new QsWallEdgeSegmentRow { Condition = "<=", MaximumWidth = "1.000" });
                    if (defaultSegmentCount >= 4)
                    {
                        context.Segments.Add(new QsWallEdgeSegmentRow { Condition = "<=", MaximumWidth = "1.200" });
                    }
                }
            }

            RefreshWallEdgeSegmentIndexes(context);
            return context;
        }

        private void SaveQsEdgeSegmentationContext(QsWallEdgeSegmentationContext context, string category, string code, string description, string option, string displayValue, string segmentOption)
        {
            SetQsMeasurementRuleValue(
                code,
                category,
                description,
                option,
                displayValue,
                "-",
                "Segmentation",
                true);

            int index = 1;
            foreach (QsWallEdgeSegmentRow segment in context.Segments)
            {
                SetQsMeasurementRuleValue(
                    code + "." + index.ToString(CultureInfo.InvariantCulture),
                    category,
                    description,
                    segmentOption,
                    segment.MaximumWidth ?? "",
                    "m",
                    "Segmentation",
                    true);
                index++;
            }

            for (int disabledIndex = index; disabledIndex <= 12; disabledIndex++)
            {
                QsMeasurementSettingRow stale = _qsMeasurementSettingsProfile?.FindRule(code + "." + disabledIndex.ToString(CultureInfo.InvariantCulture));
                if (stale != null)
                {
                    stale.IsEnabled = false;
                }
            }

            SetQsMeasurementRuleValue(
                code + ".THEREAFTER",
                category,
                "And thereafter in m stages",
                segmentOption,
                context.Thereafter,
                "m",
                "Numeric",
                true);
        }

        private void OpenQsWallOpeningEdgeConditionDialog()
        {
            OpenQsOpeningConditionDialog(
                "Wall",
                "WALL.OPENING.EDGE.CONDITION",
                "Condition for calculating side formwork of opening as edge and break formwork",
                "Opening-area condition for treating opening side formwork as edge and break formwork",
                "Wall opening edge and break condition settings updated.");
        }

        private void OpenQsSlabOpeningSideConditionDialog()
        {
            OpenQsOpeningConditionDialog(
                "Slab",
                "SLAB.OPENING.SIDE",
                "Condition for calculating side formwork of opening as side formwork",
                "Opening-area condition for slab opening side formwork",
                "Slab opening side formwork condition settings updated.");
        }

        private void OpenQsOpeningConditionDialog(string category, string code, string title, string option, string statusMessage)
        {
            QsWallOpeningConditionContext context = BuildQsOpeningConditionContext(code);
            var dialog = new Window
            {
                Owner = this,
                Title = title,
                Width = 560,
                Height = 250,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var conditionGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 250, 255)),
                ItemsSource = context.Rows,
                MinHeight = 130
            };
            conditionGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "",
                Binding = new Binding("DisplayIndex"),
                IsReadOnly = true,
                Width = 44
            });
            conditionGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Description",
                Binding = new Binding("Description"),
                IsReadOnly = true,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            conditionGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Value",
                Binding = new Binding("Value") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 170
            });

            Grid.SetRow(conditionGrid, 0);
            root.Children.Add(conditionGrid);

            var bottom = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var okButton = new Button { Content = "OK", Width = 76, Height = 28, IsDefault = true };
            okButton.Click += (s, e) =>
            {
                dialog.DialogResult = true;
                dialog.Close();
            };
            bottom.Children.Add(okButton);

            Grid.SetRow(bottom, 1);
            root.Children.Add(bottom);
            dialog.Content = root;

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            SaveQsOpeningConditionContext(context, category, code, title, option);
            MarkQsMeasurementSettingsDirty();
            ApplyQsMeasurementSettingsFilter();
            ShowStatus(statusMessage);
        }

        private QsWallOpeningConditionContext BuildQsOpeningConditionContext(string code)
        {
            string mainValue = GetQsMeasurementRuleValue(code, "Area (m2) >5.000");
            string areaFallback = ExtractGreaterThanValue(mainValue, "5.000");

            var context = new QsWallOpeningConditionContext();
            context.Rows.Add(new QsWallOpeningConditionRow
            {
                DisplayIndex = 1,
                Description = "Girth (m) >",
                Value = GetQsMeasurementRuleValue(code + ".GIRTH", "")
            });
            context.Rows.Add(new QsWallOpeningConditionRow
            {
                DisplayIndex = 2,
                Description = "Area (m2) >",
                Value = GetQsMeasurementRuleValue(code + ".AREA", areaFallback)
            });
            context.Rows.Add(new QsWallOpeningConditionRow
            {
                DisplayIndex = 3,
                Description = "Volume (m3) >",
                Value = GetQsMeasurementRuleValue(code + ".VOLUME", "")
            });

            return context;
        }

        private void SaveQsOpeningConditionContext(QsWallOpeningConditionContext context, string category, string code, string description, string option)
        {
            QsWallOpeningConditionRow girth = context.Rows.FirstOrDefault(r => r.DisplayIndex == 1);
            QsWallOpeningConditionRow area = context.Rows.FirstOrDefault(r => r.DisplayIndex == 2);
            QsWallOpeningConditionRow volume = context.Rows.FirstOrDefault(r => r.DisplayIndex == 3);

            SetQsMeasurementRuleValue(code + ".GIRTH", category, "Girth condition for opening side formwork", "Opening side formwork", girth?.Value ?? "", "m", "Condition", !string.IsNullOrWhiteSpace(girth?.Value));
            SetQsMeasurementRuleValue(code + ".AREA", category, "Area condition for opening side formwork", "Opening side formwork", area?.Value ?? "", "m2", "Condition", !string.IsNullOrWhiteSpace(area?.Value));
            SetQsMeasurementRuleValue(code + ".VOLUME", category, "Volume condition for opening side formwork", "Opening side formwork", volume?.Value ?? "", "m3", "Condition", !string.IsNullOrWhiteSpace(volume?.Value));

            string displayValue = BuildWallOpeningConditionDisplayValue(girth?.Value, area?.Value, volume?.Value);
            SetQsMeasurementRuleValue(
                code,
                category,
                description,
                option,
                displayValue,
                string.Equals(code, "SLAB.OPENING.SIDE", StringComparison.OrdinalIgnoreCase) ? "m2" : "-",
                "Condition",
                true);
        }

        private static string BuildWallOpeningConditionDisplayValue(string girth, string area, string volume)
        {
            if (!string.IsNullOrWhiteSpace(area))
            {
                return "Area (m2) >" + area.Trim();
            }

            if (!string.IsNullOrWhiteSpace(girth))
            {
                return "Girth (m) >" + girth.Trim();
            }

            if (!string.IsNullOrWhiteSpace(volume))
            {
                return "Volume (m3) >" + volume.Trim();
            }

            return "Area (m2) >5.000";
        }

        private static string ExtractGreaterThanValue(string text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            int index = text.IndexOf('>');
            if (index < 0 || index + 1 >= text.Length)
            {
                return fallback;
            }

            string value = text.Substring(index + 1).Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static void RefreshWallEdgeSegmentIndexes(QsWallEdgeSegmentationContext context)
        {
            if (context == null)
            {
                return;
            }

            int i = 1;
            foreach (QsWallEdgeSegmentRow row in context.Segments)
            {
                row.DisplayIndex = i++;
            }
        }

        private string GetQsMeasurementRuleValue(string code, string fallback)
        {
            QsMeasurementSettingRow row = _qsMeasurementSettingsProfile?.FindRule(code);
            if (row == null || string.IsNullOrWhiteSpace(row.Value))
            {
                return fallback;
            }

            return row.Value;
        }

        private void SetQsMeasurementRuleValue(string code, string category, string description, string option, string value, string unit, string method, bool enabled)
        {
            if (_qsMeasurementSettingsProfile == null)
            {
                _qsMeasurementSettingsProfile = QsMeasurementSettingsProfile.CreateDefault();
            }

            QsMeasurementSettingRow row = _qsMeasurementSettingsProfile.FindRule(code);
            if (row == null)
            {
                int nextSort = GetQsMeasurementSettingsAllRows().Any()
                    ? GetQsMeasurementSettingsAllRows().Max(r => r.SortOrder) + 10
                    : 10;
                row = new QsMeasurementSettingRow
                {
                    Code = code,
                    SortOrder = nextSort
                };
                _qsMeasurementSettingsProfile.Rules.Add(row);
                row.PropertyChanged += OnQsMeasurementSettingRowChanged;
            }

            row.Category = category;
            row.Description = description;
            row.Option = option;
            row.Value = value ?? "";
            row.Unit = unit;
            row.Method = method;
            row.IsEnabled = enabled;
            QsMeasurementSettingsProfile.RefreshChoices(row);
        }

        private static List<string> BuildFoundationSideMethodChoices()
        {
            return new List<string>
            {
                "0 Not calculate in stages: calculate by area",
                QsMeasurementSettingsProfile.FoundationSideDefaultMethodValue(),
                "2 Calculate in stages: calculate by area"
            };
        }

        private static void RefreshFoundationSideSegmentIndexes(QsFoundationSideSettingsEditorContext context)
        {
            if (context == null)
            {
                return;
            }

            int i = 1;
            foreach (QsFoundationSideSegmentRow row in context.Segments)
            {
                row.DisplayIndex = i++;
            }
        }

        private sealed class QsFoundationSideSettingsEditorContext
        {
            public string Key { get; set; } = "";
            public string Header { get; set; } = "";
            public ObservableCollection<QsFoundationSideOptionRow> Rows { get; } =
                new ObservableCollection<QsFoundationSideOptionRow>();
            public ObservableCollection<QsFoundationSideSegmentRow> Segments { get; } =
                new ObservableCollection<QsFoundationSideSegmentRow>();
            public string Thereafter { get; set; } = "0.500";
        }

        private sealed class QsFoundationSideOptionRow
        {
            public string Description { get; set; } = "";
            public string Value { get; set; } = "";
            public List<string> ValueChoices { get; set; } = new List<string>();
        }

        private sealed class QsFoundationSideSegmentRow
        {
            public int DisplayIndex { get; set; }
            public string Condition { get; set; } = "<=";
            public string MaximumHeight { get; set; } = "";
        }

        private sealed class QsWallEdgeSegmentationContext
        {
            public ObservableCollection<QsWallEdgeSegmentRow> Segments { get; } =
                new ObservableCollection<QsWallEdgeSegmentRow>();
            public string Thereafter { get; set; } = "0.500";
        }

        private sealed class QsWallEdgeSegmentRow
        {
            public int DisplayIndex { get; set; }
            public string Condition { get; set; } = "<=";
            public string MaximumWidth { get; set; } = "";
        }

        private sealed class QsWallOpeningConditionContext
        {
            public ObservableCollection<QsWallOpeningConditionRow> Rows { get; } =
                new ObservableCollection<QsWallOpeningConditionRow>();
        }

        private sealed class QsWallOpeningConditionRow
        {
            public int DisplayIndex { get; set; }
            public string Description { get; set; } = "";
            public string Value { get; set; } = "";
        }

        private void OnQsMeasurementSettingsSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveQsMeasurementSettingsProfile(GetQsMeasurementSettingsDefaultPath());
                ShowStatus("Measurement Settings saved.");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Settings save failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementSettingsImportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Measurement Settings",
                Filter = "Measurement Settings (*.json;*.xlsx;*.xlsm;*.xls;*.csv)|*.json;*.xlsx;*.xlsm;*.xls;*.csv|JSON files (*.json)|*.json|Excel files (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                QsMeasurementSettingsProfile profile = ReadQsMeasurementSettingsProfile(dialog.FileName);
                if (profile == null || profile.Rules == null || profile.Rules.Count == 0)
                {
                    throw new InvalidOperationException("The selected file does not contain measurement setting rows.");
                }

                LoadQsMeasurementSettingsProfile(profile, true);
                ShowStatus($"Measurement Settings imported: {profile.Rules.Count} rule row(s).");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Settings import failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementSettingsExportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Measurement Settings",
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "MHNK-MeasurementSettings_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".xlsx",
                AddExtension = true,
                DefaultExt = ".xlsx"
            };

            bool? result = dialog.ShowDialog(this);
            if (result != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                ExportQsMeasurementSettingsProfile(dialog.FileName);
                ShowStatus($"Measurement Settings exported: {dialog.FileName}.");
            }
            catch (Exception ex)
            {
                ShowStatus("Measurement Settings export failed: " + ex.Message);
            }
        }

        private void OnQsMeasurementSettingsRestoreDefaultClick(object sender, RoutedEventArgs e)
        {
            LoadQsMeasurementSettingsProfile(QsMeasurementSettingsProfile.CreateDefault(), true);
            ShowStatus("Measurement Settings restored to MHNK defaults.");
        }

        private void ApplyQsMeasurementSettingsToRequest()
        {
            if (_handler == null || _handler.Request == null)
            {
                return;
            }

            QsMeasurementSettingsProfile profile = _qsMeasurementSettingsProfile != null
                ? _qsMeasurementSettingsProfile.Clone()
                : QsMeasurementSettingsProfile.CreateDefault();

            _handler.Request.QsMeasurementSettingsProfile = profile;

            _handler.Request.QsFoundationIncludeTop =
                profile.GetBoolean("FOUN.TOP", _handler.Request.QsFoundationIncludeTop);

            _handler.Request.QsColumnSubtractBeam =
                profile.GetBoolean("COL.DEDUCT.BEAM", false);
            _handler.Request.QsColumnSubtractBeamGreaterOrEqual =
                _handler.Request.QsColumnSubtractBeam &&
                profile.GetBoolean("COL.DEDUCT.BEAM.SIZE", false);

            _handler.Request.QsBeamIncludeBottom =
                profile.GetBoolean("BEAM.BOTTOM", _handler.Request.QsBeamIncludeBottom);

            _handler.Request.QsWallIncludeOpeningBottom =
                profile.GetBoolean("WALL.OPENING.BOTTOM", _handler.Request.QsWallIncludeOpeningBottom);

            _handler.Request.QsFloorIncludeBottom =
                profile.GetBoolean("SLAB.BOTTOM", _handler.Request.QsFloorIncludeBottom);
            _handler.Request.QsFloorSubtractBeam =
                profile.GetBoolean("SLAB.DEDUCT.BEAM", _handler.Request.QsFloorSubtractBeam);
            _handler.Request.QsFloorSubtractFoundation =
                profile.GetBoolean("SLAB.DEDUCT.FOUN", _handler.Request.QsFloorSubtractFoundation);
            _handler.Request.QsFloorSubtractOthers =
                profile.GetBoolean("SLAB.DEDUCT.OTHER", _handler.Request.QsFloorSubtractOthers);

            _handler.Request.QsStairIncludeTop =
                profile.GetBoolean("STAIR.TOP", _handler.Request.QsStairIncludeTop);
            _handler.Request.QsStairSubtractBeam =
                profile.GetBoolean("STAIR.DEDUCT.BEAM", _handler.Request.QsStairSubtractBeam);
            _handler.Request.QsStairSubtractOthers =
                profile.GetBoolean("STAIR.DEDUCT.OTHER", _handler.Request.QsStairSubtractOthers);
        }

        private void SaveQsMeasurementSettingsProfile(string filePath)
        {
            if (_qsMeasurementSettingsProfile == null)
            {
                _qsMeasurementSettingsProfile = QsMeasurementSettingsProfile.CreateDefault();
            }

            if (QsMeasurementProfileNameTextBox != null)
            {
                _qsMeasurementSettingsProfile.ProfileName = QsMeasurementProfileNameTextBox.Text ?? "";
            }

            string validationMessage;
            if (!TryValidateQsMeasurementSettingsProfile(_qsMeasurementSettingsProfile, out validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            _qsMeasurementSettingsProfile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, CamboBimJson.Serialize(_qsMeasurementSettingsProfile), new UTF8Encoding(false));
            _qsMeasurementSettingsDirty = false;
            UpdateQsMeasurementSettingsKpis();
        }

        private QsMeasurementSettingsProfile ReadQsMeasurementSettingsProfile(string filePath)
        {
            string ext = Path.GetExtension(filePath) ?? "";
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                return CamboBimJson.Deserialize<QsMeasurementSettingsProfile>(json);
            }

            if (string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xlsm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ReadQsMeasurementSettingsFromExcel(filePath);
            }

            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return ReadQsMeasurementSettingsFromCsv(filePath);
            }

            throw new InvalidOperationException("Unsupported Measurement Settings file type.");
        }

        private void ExportQsMeasurementSettingsProfile(string filePath)
        {
            string ext = Path.GetExtension(filePath) ?? "";
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase))
            {
                SaveQsMeasurementSettingsProfile(filePath);
                return;
            }

            if (_qsMeasurementSettingsProfile == null)
            {
                _qsMeasurementSettingsProfile = QsMeasurementSettingsProfile.CreateDefault();
            }

            if (QsMeasurementProfileNameTextBox != null)
            {
                _qsMeasurementSettingsProfile.ProfileName = QsMeasurementProfileNameTextBox.Text ?? "";
            }

            _qsMeasurementSettingsProfile.Normalize();
            string validationMessage;
            if (!TryValidateQsMeasurementSettingsProfile(_qsMeasurementSettingsProfile, out validationMessage))
            {
                throw new InvalidOperationException(validationMessage);
            }

            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(filePath, BuildQsMeasurementSettingsCsv(), new UTF8Encoding(false));
            }
            else
            {
                ExportQsMeasurementSettingsToExcel(filePath);
            }
        }

        private string BuildQsMeasurementSettingsCsv()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("Active,Element,Rule Code,Description,Option / Theory,Value,Unit,Condition,Sort Order,Value Choices,Condition Choices");
            foreach (QsMeasurementSettingRow row in GetQsMeasurementSettingsRowsForExport())
            {
                sb.Append(row.IsEnabled ? "1" : "0").Append(',')
                  .Append(EscapeCsv(row.Category)).Append(',')
                  .Append(EscapeCsv(row.Code)).Append(',')
                  .Append(EscapeCsv(row.Description)).Append(',')
                  .Append(EscapeCsv(row.Option)).Append(',')
                  .Append(EscapeCsv(row.Value)).Append(',')
                  .Append(EscapeCsv(row.Unit)).Append(',')
                  .Append(EscapeCsv(row.Method)).Append(',')
                  .Append(row.SortOrder.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(EscapeCsv(JoinMeasurementChoices(row.ValueChoices)))
                  .Append(',')
                  .Append(EscapeCsv(JoinMeasurementChoices(row.MethodChoices)))
                  .AppendLine();
            }

            return sb.ToString();
        }

        private List<QsMeasurementSettingRow> GetQsMeasurementSettingsRowsForExport()
        {
            return GetQsMeasurementSettingsAllRows()
                .OrderBy(r => GetQsMeasurementCategoryOrder(r.Category))
                .ThenBy(r => r.SortOrder)
                .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string JoinMeasurementChoices(IEnumerable<string> choices)
        {
            if (choices == null)
            {
                return "";
            }

            return string.Join("; ", choices.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()));
        }

        private static QsMeasurementSettingsProfile ReadQsMeasurementSettingsFromCsv(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
            {
                return null;
            }

            List<string> headers = ParseCsvLine(lines[0]);
            var importedRows = new List<QsMeasurementSettingRow>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                QsMeasurementSettingRow row = BuildQsMeasurementRowFromCells(ParseCsvLine(lines[i]), headers);
                if (row != null)
                {
                    importedRows.Add(row);
                }
            }

            return BuildMergedQsMeasurementProfile(importedRows);
        }

        private static QsMeasurementSettingsProfile ReadQsMeasurementSettingsFromExcel(string filePath)
        {
            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object worksheetObj = null;
            object usedRangeObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel is not available.");
                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Open(filePath, ReadOnly: true);
                dynamic workbook = workbookObj;
                worksheetObj = workbook.Worksheets[1];
                dynamic worksheet = worksheetObj;
                usedRangeObj = worksheet.UsedRange;
                dynamic usedRange = usedRangeObj;
                object raw = usedRange.Value2;
                var importedRows = BuildQsMeasurementRowsFromExcelMatrix(raw);
                return BuildMergedQsMeasurementProfile(importedRows);
            }
            finally
            {
                try
                {
                    if (workbookObj != null)
                    {
                        dynamic workbook = workbookObj;
                        workbook.Close(false);
                    }
                }
                catch
                {
                    // ignore close issues
                }

                try
                {
                    if (appObj != null)
                    {
                        dynamic app = appObj;
                        app.Quit();
                    }
                }
                catch
                {
                    // ignore quit issues
                }

                SafeReleaseCom(usedRangeObj);
                SafeReleaseCom(worksheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private static List<QsMeasurementSettingRow> BuildQsMeasurementRowsFromExcelMatrix(object raw)
        {
            var importedRows = new List<QsMeasurementSettingRow>();
            object[,] values = raw as object[,];
            if (values == null)
            {
                return importedRows;
            }

            int rMin = values.GetLowerBound(0);
            int rMax = values.GetUpperBound(0);
            int cMin = values.GetLowerBound(1);
            int cMax = values.GetUpperBound(1);
            int headerRow = -1;
            for (int r = rMin; r <= Math.Min(rMax, rMin + 30); r++)
            {
                var headers = new List<string>();
                for (int c = cMin; c <= cMax; c++)
                {
                    headers.Add(ToCellString(values[r, c]).Trim());
                }

                if (IndexOfHeader(headers, "Rule Code", "RuleCode", "Code") >= 0)
                {
                    headerRow = r;
                    break;
                }
            }

            if (headerRow < rMin)
            {
                return importedRows;
            }

            var headerNames = new List<string>();
            for (int c = cMin; c <= cMax; c++)
            {
                headerNames.Add(ToCellString(values[headerRow, c]).Trim());
            }

            for (int r = headerRow + 1; r <= rMax; r++)
            {
                var cells = new List<string>();
                for (int c = cMin; c <= cMax; c++)
                {
                    cells.Add(ToCellString(values[r, c]).Trim());
                }

                QsMeasurementSettingRow row = BuildQsMeasurementRowFromCells(cells, headerNames);
                if (row != null)
                {
                    importedRows.Add(row);
                }
            }

            return importedRows;
        }

        private static QsMeasurementSettingRow BuildQsMeasurementRowFromCells(IReadOnlyList<string> cells, IReadOnlyList<string> headers)
        {
            int idxActive = IndexOfHeader(headers, "Active", "Use", "Enabled");
            int idxElement = IndexOfHeader(headers, "Element", "Category");
            int idxCode = IndexOfHeader(headers, "Rule Code", "RuleCode", "Code");
            int idxDescription = IndexOfHeader(headers, "Description", "Desc");
            int idxOption = IndexOfHeader(headers, "Option / Theory", "Option", "Theory");
            int idxValue = IndexOfHeader(headers, "Value", "Rule Value");
            int idxUnit = IndexOfHeader(headers, "Unit", "UOM");
            int idxMethod = IndexOfHeader(headers, "Condition", "Method", "Rule Type");
            int idxSort = IndexOfHeader(headers, "Sort Order", "SortOrder", "Order");

            string code = ValueAt(cells, idxCode);
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            return new QsMeasurementSettingRow
            {
                IsEnabled = BoolAt(cells, idxActive, true),
                Category = ValueAt(cells, idxElement),
                Code = code,
                Description = ValueAt(cells, idxDescription),
                Option = ValueAt(cells, idxOption),
                Value = ValueAt(cells, idxValue),
                Unit = ValueAt(cells, idxUnit),
                Method = ValueAt(cells, idxMethod),
                SortOrder = IntAt(cells, idxSort)
            };
        }

        private static QsMeasurementSettingsProfile BuildMergedQsMeasurementProfile(IEnumerable<QsMeasurementSettingRow> importedRows)
        {
            QsMeasurementSettingsProfile profile = QsMeasurementSettingsProfile.CreateDefault();
            profile.Normalize();
            var byCode = profile.Rules
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.Code))
                .ToDictionary(r => r.Code, StringComparer.OrdinalIgnoreCase);
            int nextSort = profile.Rules.Count == 0 ? 10 : profile.Rules.Max(r => r.SortOrder) + 10;
            int importedCount = 0;

            foreach (QsMeasurementSettingRow imported in importedRows ?? Enumerable.Empty<QsMeasurementSettingRow>())
            {
                if (imported == null || string.IsNullOrWhiteSpace(imported.Code)) continue;
                importedCount++;

                QsMeasurementSettingRow target;
                if (!byCode.TryGetValue(imported.Code, out target))
                {
                    target = new QsMeasurementSettingRow
                    {
                        Code = imported.Code,
                        SortOrder = imported.SortOrder > 0 ? imported.SortOrder : nextSort
                    };
                    nextSort += 10;
                    profile.Rules.Add(target);
                    byCode[target.Code] = target;
                }

                target.IsEnabled = imported.IsEnabled;
                if (!string.IsNullOrWhiteSpace(imported.Category)) target.Category = imported.Category;
                if (!string.IsNullOrWhiteSpace(imported.Description)) target.Description = imported.Description;
                if (!string.IsNullOrWhiteSpace(imported.Option)) target.Option = imported.Option;
                if (!string.IsNullOrWhiteSpace(imported.Value)) target.Value = imported.Value;
                if (!string.IsNullOrWhiteSpace(imported.Unit)) target.Unit = imported.Unit;
                if (!string.IsNullOrWhiteSpace(imported.Method)) target.Method = imported.Method;
                if (imported.SortOrder > 0) target.SortOrder = imported.SortOrder;
            }

            if (importedCount == 0)
            {
                throw new InvalidOperationException("No Measurement Settings rule rows were found.");
            }

            profile.UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            profile.Normalize();
            return profile;
        }

        private void ExportQsMeasurementSettingsToExcel(string filePath)
        {
            List<QsMeasurementSettingRow> rows = GetQsMeasurementSettingsRowsForExport();
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("No Measurement Settings rows to export.");
            }

            object appObj = null;
            object workbooksObj = null;
            object workbookObj = null;
            object worksheetObj = null;
            object rangeObj = null;
            object topLeftObj = null;
            object bottomRightObj = null;
            object headerRangeObj = null;

            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application") ?? throw new InvalidOperationException("Microsoft Excel is not available.");
                appObj = Activator.CreateInstance(excelType);
                dynamic app = appObj;
                app.DisplayAlerts = false;
                app.Visible = false;

                workbooksObj = app.Workbooks;
                dynamic workbooks = workbooksObj;
                workbookObj = workbooks.Add();
                dynamic workbook = workbookObj;
                worksheetObj = workbook.Worksheets[1];
                dynamic worksheet = worksheetObj;
                worksheet.Name = "MeasurementSettings";

                int rowCount = rows.Count + 1;
                const int colCount = 11;
                var matrix = new object[rowCount, colCount];
                matrix[0, 0] = "Active";
                matrix[0, 1] = "Element";
                matrix[0, 2] = "Rule Code";
                matrix[0, 3] = "Description";
                matrix[0, 4] = "Option / Theory";
                matrix[0, 5] = "Value";
                matrix[0, 6] = "Unit";
                matrix[0, 7] = "Condition";
                matrix[0, 8] = "Sort Order";
                matrix[0, 9] = "Value Choices";
                matrix[0, 10] = "Condition Choices";

                for (int i = 0; i < rows.Count; i++)
                {
                    QsMeasurementSettingRow row = rows[i];
                    matrix[i + 1, 0] = row.IsEnabled ? 1 : 0;
                    matrix[i + 1, 1] = row.Category ?? "";
                    matrix[i + 1, 2] = row.Code ?? "";
                    matrix[i + 1, 3] = row.Description ?? "";
                    matrix[i + 1, 4] = row.Option ?? "";
                    matrix[i + 1, 5] = row.Value ?? "";
                    matrix[i + 1, 6] = row.Unit ?? "";
                    matrix[i + 1, 7] = row.Method ?? "";
                    matrix[i + 1, 8] = row.SortOrder;
                    matrix[i + 1, 9] = JoinMeasurementChoices(row.ValueChoices);
                    matrix[i + 1, 10] = JoinMeasurementChoices(row.MethodChoices);
                }

                topLeftObj = worksheet.Cells[1, 1];
                bottomRightObj = worksheet.Cells[rowCount, colCount];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Value2 = matrix;

                headerRangeObj = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, colCount]];
                dynamic headerRange = headerRangeObj;
                headerRange.Font.Bold = true;
                try
                {
                    range.AutoFilter(1);
                    ApplyQsMeasurementSettingsExcelValidation(worksheet, rows);
                    dynamic window = app.ActiveWindow;
                    window.SplitRow = 1;
                    window.FreezePanes = true;
                    worksheet.Columns.AutoFit();
                }
                catch
                {
                    // ignore formatting issues
                }

                workbook.SaveAs(filePath, 51);
                workbook.Close(false);
                app.Quit();
            }
            finally
            {
                SafeReleaseCom(headerRangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(worksheetObj);
                SafeReleaseCom(workbookObj);
                SafeReleaseCom(workbooksObj);
                SafeReleaseCom(appObj);
            }
        }

        private static bool TryValidateQsMeasurementSettingsProfile(QsMeasurementSettingsProfile profile, out string message)
        {
            message = "";
            if (profile == null)
            {
                return true;
            }

            profile.Normalize();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QsMeasurementSettingRow row in profile.Rules ?? new List<QsMeasurementSettingRow>())
            {
                if (row == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(row.Code))
                {
                    message = row.IsEnabled
                        ? "Measurement Settings contains an active row without a rule code."
                        : "Measurement Settings contains a row without a rule code.";
                    return false;
                }

                if (!seenCodes.Add(row.Code.Trim()))
                {
                    message = $"Measurement Settings contains duplicate rule code {row.Code}.";
                    return false;
                }

                if (!row.IsEnabled)
                {
                    continue;
                }

                if (IsNumericMeasurementSetting(row) &&
                    !QsMeasurementSettingsProfile.TryParseMeasurementDouble(row.Value, out _))
                {
                    message = $"Measurement Settings rule {row.Code} needs a numeric value for {row.Unit}.";
                    return false;
                }

                if (IsBooleanMeasurementSetting(row) &&
                    !QsMeasurementSettingsProfile.TryParseMeasurementBoolean(row.Value, out _))
                {
                    message = $"Measurement Settings rule {row.Code} must use Yes or No.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsNumericMeasurementSetting(QsMeasurementSettingRow row)
        {
            if (row == null)
            {
                return false;
            }

            string unit = (row.Unit ?? "").Trim();
            if (string.IsNullOrWhiteSpace(unit) || string.Equals(unit, "-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string method = (row.Method ?? "").Trim();
            return string.Equals(method, "Numeric", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(method, "Condition", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBooleanMeasurementSetting(QsMeasurementSettingRow row)
        {
            if (row == null)
            {
                return false;
            }

            List<string> choices = row.ValueChoices ?? new List<string>();
            bool hasYes = choices.Any(c => string.Equals(c, "Yes", StringComparison.OrdinalIgnoreCase));
            bool hasNo = choices.Any(c => string.Equals(c, "No", StringComparison.OrdinalIgnoreCase));
            return hasYes && hasNo;
        }

        private static void ApplyQsMeasurementSettingsExcelValidation(dynamic worksheet, IReadOnlyList<QsMeasurementSettingRow> rows)
        {
            if (worksheet == null || rows == null || rows.Count == 0)
            {
                return;
            }

            int firstDataRow = 2;
            int lastDataRow = rows.Count + 1;
            ApplyExcelValidationListToRange(worksheet, firstDataRow, lastDataRow, 1, new[] { "1", "0" });
            ApplyExcelValidationListToRange(
                worksheet,
                firstDataRow,
                lastDataRow,
                2,
                rows.Select(r => r?.Category ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
            ApplyExcelValidationListToRange(
                worksheet,
                firstDataRow,
                lastDataRow,
                7,
                rows.Select(r => r?.Unit ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));

            for (int i = 0; i < rows.Count; i++)
            {
                int rowIndex = i + 2;
                QsMeasurementSettingRow row = rows[i];
                ApplyExcelValidationListToCell(worksheet, rowIndex, 6, row?.ValueChoices);
                ApplyExcelValidationListToCell(worksheet, rowIndex, 8, row?.MethodChoices);
            }
        }

        private static void ApplyExcelValidationListToRange(dynamic worksheet, int firstRow, int lastRow, int column, IEnumerable<string> choices)
        {
            string formula = BuildExcelValidationFormula(choices);
            if (string.IsNullOrWhiteSpace(formula))
            {
                return;
            }

            object topLeftObj = null;
            object bottomRightObj = null;
            object rangeObj = null;
            try
            {
                topLeftObj = worksheet.Cells[firstRow, column];
                bottomRightObj = worksheet.Cells[lastRow, column];
                rangeObj = worksheet.Range[topLeftObj, bottomRightObj];
                dynamic range = rangeObj;
                range.Validation.Delete();
                range.Validation.Add(3, 1, 1, formula);
                range.Validation.IgnoreBlank = true;
                range.Validation.InCellDropdown = true;
            }
            catch
            {
                // Export still succeeds if Excel refuses validation on this machine.
            }
            finally
            {
                SafeReleaseCom(rangeObj);
                SafeReleaseCom(bottomRightObj);
                SafeReleaseCom(topLeftObj);
            }
        }

        private static void ApplyExcelValidationListToCell(dynamic worksheet, int row, int column, IEnumerable<string> choices)
        {
            string formula = BuildExcelValidationFormula(choices);
            if (string.IsNullOrWhiteSpace(formula))
            {
                return;
            }

            object cellObj = null;
            try
            {
                cellObj = worksheet.Cells[row, column];
                dynamic cell = cellObj;
                cell.Validation.Delete();
                cell.Validation.Add(3, 1, 1, formula);
                cell.Validation.IgnoreBlank = true;
                cell.Validation.InCellDropdown = true;
            }
            catch
            {
                // Export still succeeds if Excel refuses validation on this machine.
            }
            finally
            {
                SafeReleaseCom(cellObj);
            }
        }

        private static string BuildExcelValidationFormula(IEnumerable<string> choices)
        {
            if (choices == null)
            {
                return "";
            }

            List<string> values = choices
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().Replace("\"", "").Replace(",", " "))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (values.Count == 0)
            {
                return "";
            }

            string list = string.Join(",", values);
            if (list.Length > 240)
            {
                return "";
            }

            return "\"" + list + "\"";
        }

        private static string GetQsMeasurementSettingsDefaultPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            return Path.Combine(appData, "MHNK", "RevitExtension", "QS", "measurement-settings.json");
        }
    }
}
