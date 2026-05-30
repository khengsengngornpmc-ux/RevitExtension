using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcToolsWindow : Window
    {
        private const string SourceGroupAutoCad = MhnkArcToolMetadataRules.SourceGroupAutoCad;
        private const string SourceGroupRevitCategory = MhnkArcToolMetadataRules.SourceGroupRevitCategory;
        private const string SourceGroupSelectedItems = MhnkArcToolMetadataRules.SourceGroupSelectedItems;
        private const string SourceGroupReviewSetup = MhnkArcToolMetadataRules.SourceGroupReviewSetup;
        private const string SourceGroupAllTools = MhnkArcToolMetadataRules.SourceGroupAllTools;

        private static readonly string[] CategoryOrder = { "Filter", "Creation", "Edition", "Solids", "Xpress" };
        private static readonly string[] SourceGroupOrder = MhnkArcToolMetadataRules.SourceGroupOrder;

        private readonly IList<MhnkArcCommandOption> _options;
        private readonly Action<MhnkArcCommandOption, MhnkArcSourceMode> _runRequested;
        private readonly Action<MhnkArcSourceMode> _refreshRequested;
        private ListBox _categoryList;
        private ListBox _toolList;
        private TextBox _searchBox;
        private TextBlock _titleText;
        private TextBlock _summaryText;
        private WrapPanel _detailBadgePanel;
        private RadioButton _freeSelectModeButton;
        private RadioButton _categoryModeButton;
        private RadioButton _byLayerModeButton;
        private RadioButton _allModeButton;
        private TextBlock _sourceModeDescriptionText;
        private TextBlock _sourceModeRecommendationText;
        private StackPanel _workflowPanel;
        private ContentControl _workbenchContent;
        private TextBlock _workbenchTitleText;
        private TextBlock _requirementText;
        private TextBlock _resultText;
        private Button _guidelineButton;
        private Button _runButton;
        private Button _clearSearchButton;
        private CheckBox _technicalViewCheckBox;
        private bool _isRunPending;
        private bool _isApplyingSourceMode;
        private bool _technicalViewEnabled;
        private MhnkArcCommandOption _sourcePolicyOption;
        private ColumnDefinition _groupsColumn;
        private ColumnDefinition _toolsColumn;
        private ColumnDefinition _workflowBandColumn;
        private RowDefinition _upperBandRow;
        private MhnkArcWorkspaceSnapshot _snapshot;

        public MhnkArcToolsWindow(
            IList<MhnkArcCommandOption> options,
            string initialCategory,
            IntPtr revitMainWindowHandle,
            Action<MhnkArcCommandOption, MhnkArcSourceMode> runRequested = null,
            Action<MhnkArcSourceMode> refreshRequested = null,
            MhnkArcWorkspaceSnapshot snapshot = null)
        {
            _options = options ?? new List<MhnkArcCommandOption>();
            _runRequested = runRequested;
            _refreshRequested = refreshRequested;
            _snapshot = snapshot;
            SourceMode = LoadLastSourceMode();
            _technicalViewEnabled = LoadTechnicalViewMode();
            WorkspaceLayoutState layout = LoadWorkspaceLayout();

            Title = "MHNK ARC Workspace";
            Width = ClampLayoutValue(layout?.WindowWidth ?? 1380, 1120, 2600);
            Height = ClampLayoutValue(layout?.WindowHeight ?? 780, 640, 1600);
            MinWidth = 1120;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            Grid root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            Grid header = BuildHeader();
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            Grid workspace = new Grid { Margin = new Thickness(0, 12, 0, 12) };
            _groupsColumn = new ColumnDefinition { Width = new GridLength(ClampLayoutValue(layout?.GroupsWidth ?? 176, 142, 240)), MinWidth = 142, MaxWidth = 240 };
            _toolsColumn = new ColumnDefinition { Width = new GridLength(ClampLayoutValue(layout?.ToolsWidth ?? 330, 270, 380)), MinWidth = 270, MaxWidth = 380 };
            workspace.ColumnDefinitions.Add(_groupsColumn);
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            workspace.ColumnDefinitions.Add(_toolsColumn);
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 620 });
            root.Children.Add(workspace);
            Grid.SetRow(workspace, 1);

            _categoryList = new ListBox
            {
                BorderThickness = new Thickness(1),
                ItemTemplate = BuildSourceGroupItemTemplate(),
                FontSize = 13
            };
            EnableVirtualization(_categoryList);
            _categoryList.SelectionChanged += (_, __) => RefreshTools(true);
            workspace.Children.Add(BuildSection("SOURCE GROUPS", _categoryList));
            Grid.SetColumn(workspace.Children[workspace.Children.Count - 1], 0);
            workspace.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(workspace.Children[workspace.Children.Count - 1], 1);

            _toolList = BuildToolList();
            _toolList.SelectionChanged += OnToolPanelSelectionChanged;
            _toolList.MouseDoubleClick += (_, __) => AcceptSelection();
            workspace.Children.Add(BuildSection("ARC TOOLS", _toolList));
            Grid.SetColumn(workspace.Children[workspace.Children.Count - 1], 2);
            workspace.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(workspace.Children[workspace.Children.Count - 1], 3);

            Border details = BuildDetailPanel(layout);
            workspace.Children.Add(details);
            Grid.SetColumn(details, 4);

            StackPanel footer = BuildFooter();
            root.Children.Add(footer);
            Grid.SetRow(footer, 2);

            PopulateCategories(initialCategory);
            RefreshTools(false);
            SelectInitialTool();
            Closing += (_, __) => SaveWorkspaceLayout();
            MhnkUiTheme.Apply(this);
            RefreshActiveNavigationVisuals();
        }

        public MhnkArcCommandOption SelectedOption { get; private set; }
        public MhnkArcSourceMode SourceMode { get; private set; }

        public void UpdateSnapshot(MhnkArcWorkspaceSnapshot snapshot)
        {
            _snapshot = snapshot;
            SelectTool(SelectedOption);
        }

        public void SelectCategory(string category)
        {
            _searchBox.Text = "";
            PopulateCategories(category);
            RefreshTools(false);
            SelectInitialTool();
        }

        public void CompleteRunRequest()
        {
            _isRunPending = false;
            _runButton.Content = "Preview / Run";
            _runButton.IsEnabled = SelectedOption != null;
        }

        private Grid BuildHeader()
        {
            Grid header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(460) });

            StackPanel titlePanel = new StackPanel();
            titlePanel.Children.Add(new TextBlock
            {
                Text = "MHNK ARC WORKSPACE",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Group tools by AutoCAD source, Revit category, selected items, or review/setup workflow before running.",
                TextWrapping = TextWrapping.Wrap
            });
            _technicalViewCheckBox = new CheckBox
            {
                Content = "Technical view",
                IsChecked = _technicalViewEnabled,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "Show developer evidence tables and command rows."
            };
            _technicalViewCheckBox.Checked += OnTechnicalViewChanged;
            _technicalViewCheckBox.Unchecked += OnTechnicalViewChanged;
            titlePanel.Children.Add(_technicalViewCheckBox);
            header.Children.Add(titlePanel);
            Grid.SetColumn(titlePanel, 0);

            Grid search = new Grid { VerticalAlignment = VerticalAlignment.Center };
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            search.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _searchBox = new TextBox
            {
                Height = 32,
                Padding = new Thickness(10, 5, 10, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Search by tool name, summary, status, or group."
            };
            _searchBox.TextChanged += (_, __) => RefreshTools(false);
            search.Children.Add(_searchBox);
            Grid.SetColumn(_searchBox, 0);

            _clearSearchButton = new Button
            {
                Content = "Clear",
                Width = 58,
                Height = 32,
                Margin = new Thickness(6, 0, 0, 0)
            };
            _clearSearchButton.Click += (_, __) => _searchBox.Text = "";
            search.Children.Add(_clearSearchButton);
            Grid.SetColumn(_clearSearchButton, 1);

            header.Children.Add(search);
            Grid.SetColumn(search, 1);
            return header;
        }

        private static DataTemplate BuildSourceGroupItemTemplate()
        {
            var template = new DataTemplate(typeof(SourceGroupNavItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(FrameworkElement.TagProperty, "MhnkSourceGroupPanel");
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));
            border.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 4));

            var panel = new FrameworkElementFactory(typeof(StackPanel));

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(SourceGroupNavItem.DisplayText)));
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            panel.AppendChild(title);

            var description = new FrameworkElementFactory(typeof(TextBlock));
            description.SetBinding(TextBlock.TextProperty, new Binding(nameof(SourceGroupNavItem.Description)));
            description.SetValue(TextBlock.FontSizeProperty, 11.0);
            description.SetValue(UIElement.OpacityProperty, 0.78);
            description.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            description.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
            panel.AppendChild(description);

            border.AppendChild(panel);
            template.VisualTree = border;
            return template;
        }

        private static ListBox BuildToolList()
        {
            ListBox list = new ListBox
            {
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);

            Style itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 8)));
            list.ItemContainerStyle = itemStyle;

            EnableVirtualization(list);
            return list;
        }

        private Border BuildDetailPanel(WorkspaceLayoutState layout)
        {
            Grid detail = new Grid();
            detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _upperBandRow = new RowDefinition { Height = new GridLength(0), MinHeight = 0 };
            detail.RowDefinitions.Add(_upperBandRow);
            detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            detail.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 220 });
            detail.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _workbenchTitleText = new TextBlock
            {
                Text = "ARC WORKBENCH",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            detail.Children.Add(_workbenchTitleText);
            Grid.SetRow(_workbenchTitleText, 0);

            _titleText = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            detail.Children.Add(_titleText);
            Grid.SetRow(_titleText, 1);

            _detailBadgePanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            detail.Children.Add(_detailBadgePanel);
            Grid.SetRow(_detailBadgePanel, 2);

            _summaryText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            detail.Children.Add(_summaryText);
            Grid.SetRow(_summaryText, 3);

            Grid operatingBands = new Grid
            {
                Margin = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            operatingBands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
            operatingBands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            _workflowBandColumn = new ColumnDefinition { Width = new GridLength(ClampLayoutValue(layout?.WorkflowWidth ?? 292, 220, 520)), MinWidth = 220 };
            operatingBands.ColumnDefinitions.Add(_workflowBandColumn);

            Border sourceBand = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "RETRIEVE SOURCE",
                                FontWeight = FontWeights.SemiBold,
                                Margin = new Thickness(0, 0, 0, 8)
                            },
                            BuildSourceModePanel()
                        }
                    }
                }
            };
            operatingBands.Children.Add(sourceBand);
            Grid.SetColumn(sourceBand, 0);
            operatingBands.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(operatingBands.Children[operatingBands.Children.Count - 1], 1);

            StackPanel workflow = new StackPanel();
            workflow.Children.Add(new TextBlock
            {
                Text = "WORKFLOW",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            _workflowPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            workflow.Children.Add(_workflowPanel);
            workflow.Children.Add(new TextBlock
            {
                Text = "ACTIVE INPUT / OUTPUT",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });
            _requirementText = BuildFactText();
            _resultText = BuildFactText();
            workflow.Children.Add(_requirementText);
            workflow.Children.Add(_resultText);
            Border workflowBand = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = workflow
                }
            };
            operatingBands.Children.Add(workflowBand);
            Grid.SetColumn(workflowBand, 2);
            detail.Children.Add(operatingBands);
            Grid.SetRow(operatingBands, 4);

            UIElement workbenchSplitter = BuildHorizontalGridSplitter();
            workbenchSplitter.Visibility = Visibility.Collapsed;
            detail.Children.Add(workbenchSplitter);
            Grid.SetRow(workbenchSplitter, 5);

            _workbenchContent = new ContentControl();
            ScrollViewer workbenchScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _workbenchContent
            };
            detail.Children.Add(workbenchScroller);
            Grid.SetRow(workbenchScroller, 6);

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0),
                Visibility = Visibility.Collapsed
            };
            _guidelineButton = new Button
            {
                Content = "Guideline",
                Width = 108,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = false
            };
            _guidelineButton.Click += (_, __) => OpenGuideline();
            actions.Children.Add(_guidelineButton);

            _runButton = new Button
            {
                Content = "Preview / Run",
                Width = 116,
                Height = 32,
                IsEnabled = false
            };
            _runButton.Click += (_, __) => AcceptSelection();
            actions.Children.Add(_runButton);
            detail.Children.Add(actions);
            Grid.SetRow(actions, 7);

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Child = detail
            };
        }

        private static TextBlock BuildFactText()
        {
            return new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private StackPanel BuildFooter()
        {
            StackPanel footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            Button close = new Button
            {
                Content = "Close",
                Width = 92,
                Height = 32
            };
            close.Click += (_, __) => Close();
            footer.Children.Add(close);
            return footer;
        }

        private static Border BuildSection(string title, UIElement content)
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 0);

            grid.Children.Add(content);
            Grid.SetRow(content, 1);

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = grid
            };
        }

        private static GridSplitter BuildVerticalGridSplitter()
        {
            bool dark = MhnkUiTheme.IsDark;
            return new GridSplitter
            {
                Tag = "MhnkThemePreserve",
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false,
                Background = CreateBrush(dark, 96, 165, 250, 191, 219, 254),
                Cursor = Cursors.SizeWE,
                ToolTip = "Drag to resize panels."
            };
        }

        private static GridSplitter BuildHorizontalGridSplitter()
        {
            bool dark = MhnkUiTheme.IsDark;
            return new GridSplitter
            {
                Tag = "MhnkThemePreserve",
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false,
                Background = CreateBrush(dark, 96, 165, 250, 191, 219, 254),
                Cursor = Cursors.SizeNS,
                ToolTip = "Drag to resize the retrieve/workbench area."
            };
        }

        private UIElement BuildSourceModePanel()
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            WrapPanel buttons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _freeSelectModeButton = BuildSourceModeButton("Select Item", MhnkArcSourceMode.FreeSelect);
            _categoryModeButton = BuildSourceModeButton("Category", MhnkArcSourceMode.Category);
            _byLayerModeButton = BuildSourceModeButton("By Layer", MhnkArcSourceMode.ByLayer);
            _allModeButton = BuildSourceModeButton("All", MhnkArcSourceMode.All);
            buttons.Children.Add(_freeSelectModeButton);
            buttons.Children.Add(_categoryModeButton);
            buttons.Children.Add(_byLayerModeButton);
            buttons.Children.Add(_allModeButton);
            panel.Children.Add(buttons);

            _sourceModeRecommendationText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            panel.Children.Add(_sourceModeRecommendationText);

            _sourceModeDescriptionText = new TextBlock
            {
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 2)
            };
            panel.Children.Add(_sourceModeDescriptionText);
            ApplySourceModeToButtons();
            UpdateSourceModeDescription();
            return panel;
        }

        private RadioButton BuildSourceModeButton(string text, MhnkArcSourceMode mode)
        {
            RadioButton button = new RadioButton
            {
                Content = text,
                GroupName = "MhnkArcSourceMode",
                Tag = mode,
                Margin = new Thickness(0, 0, 12, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Checked += OnSourceModeChanged;
            return button;
        }

        private void OnSourceModeChanged(object sender, RoutedEventArgs e)
        {
            if (_isApplyingSourceMode || (!IsLoaded && _sourceModeDescriptionText == null))
            {
                return;
            }

            RadioButton button = sender as RadioButton;
            if (button?.IsChecked == true && button.Tag is MhnkArcSourceMode mode)
            {
                SourceMode = mode;
                SaveLastSourceMode(SourceMode);
                UpdateSourceModeDescription();
                if (_workflowPanel == null || _requirementText == null || _runButton == null)
                {
                    return;
                }

                SelectTool(SelectedOption);
            }
        }

        private void ApplySourceModeToButtons()
        {
            if (_freeSelectModeButton == null || _categoryModeButton == null ||
                _byLayerModeButton == null || _allModeButton == null)
            {
                return;
            }

            _freeSelectModeButton.IsChecked = SourceMode == MhnkArcSourceMode.FreeSelect;
            _categoryModeButton.IsChecked = SourceMode == MhnkArcSourceMode.Category;
            _byLayerModeButton.IsChecked = SourceMode == MhnkArcSourceMode.ByLayer;
            _allModeButton.IsChecked = SourceMode == MhnkArcSourceMode.All;
        }

        private void UpdateSourceModeDescription()
        {
            if (_sourceModeDescriptionText == null)
            {
                return;
            }

            _sourceModeDescriptionText.Text = GetSourceModeDescription(SourceMode, SelectedOption);
            if (_sourceModeRecommendationText != null)
            {
                MhnkArcSourceMode recommended = GetRecommendedSourceMode(SelectedOption);
                _sourceModeRecommendationText.Text =
                    "Recommended: " + GetSourceModeLabel(recommended) + " - " +
                    GetSourceModeReason(SelectedOption, recommended);
            }
        }

        private void PopulateCategories(string initialCategory)
        {
            IList<SourceGroupNavItem> sourceGroups = SourceGroupOrder
                .Select(x => new SourceGroupNavItem(x, _options.Count(o => IsOptionInSourceGroup(o, x)), GetSourceGroupDescription(x)))
                .ToList();

            _categoryList.ItemsSource = sourceGroups;
            string selectedGroup = string.IsNullOrWhiteSpace(initialCategory)
                ? LoadLastGroup()
                : initialCategory;
            SourceGroupNavItem selected = sourceGroups.FirstOrDefault(x => string.Equals(x.Name, selectedGroup, StringComparison.OrdinalIgnoreCase));
            if (selected == null)
            {
                selected = sourceGroups.FirstOrDefault(x => string.Equals(x.Name, GetDefaultSourceGroupForCommandCategory(selectedGroup), StringComparison.OrdinalIgnoreCase));
            }

            _categoryList.SelectedItem = selected ?? sourceGroups.FirstOrDefault();
        }

        private void RefreshTools(bool persistCategory)
        {
            SourceGroupNavItem sourceGroup = _categoryList.SelectedItem as SourceGroupNavItem;
            string sourceGroupName = sourceGroup?.Name ?? SourceGroupOrder.First();
            if (persistCategory)
            {
                SaveLastGroup(sourceGroupName);
            }

            string query = (_searchBox.Text ?? "").Trim();
            IEnumerable<MhnkArcCommandOption> filtered = _options
                .Where(x => IsOptionInSourceGroup(x, sourceGroupName));

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(x =>
                    Contains(GetSourceGroupName(x), query) ||
                    Contains(x.Category, query) ||
                    Contains(x.Title, query) ||
                    Contains(x.Summary, query) ||
                    Contains(x.Status, query));
            }

            IList<MhnkArcCommandOption> rows = filtered
                .OrderBy(x => GetSourceGroupIndex(GetSourceGroupName(x)))
                .ThenBy(x => GetCategoryIndex(x.Category))
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            MhnkArcCommandOption previous = SelectedOption;
            _toolList.Items.Clear();
            var panelItems = rows
                .Select(x => new ToolPanelItem(x, GetSourceGroupName(x), InferRisk(x), InferInput(x, GetEffectiveSourceMode(x)), InferResult(x)))
                .ToList();
            foreach (ToolPanelItem panelItem in panelItems)
            {
                _toolList.Items.Add(new ListBoxItem
                {
                    Tag = panelItem,
                    Content = BuildToolPanel(panelItem),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                });
            }

            _clearSearchButton.IsEnabled = !string.IsNullOrWhiteSpace(query);

            if (previous != null)
            {
                ListBoxItem match = _toolList.Items.Cast<ListBoxItem>().FirstOrDefault(x =>
                {
                    ToolPanelItem item = x.Tag as ToolPanelItem;
                    return item != null &&
                           string.Equals(item.Option.Category, previous.Category, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.Option.Title, previous.Title, StringComparison.OrdinalIgnoreCase);
                });
                if (match != null)
                {
                    _toolList.SelectedItem = match;
                    _toolList.ScrollIntoView(match);
                    SelectTool((match.Tag as ToolPanelItem)?.Option);
                    if (IsLoaded)
                    {
                        MhnkUiTheme.Apply(this);
                    }

                    RefreshActiveNavigationVisuals();

                    return;
                }
            }

            _toolList.SelectedItem = _toolList.Items.Cast<ListBoxItem>().FirstOrDefault();
            SelectTool(((_toolList.SelectedItem as ListBoxItem)?.Tag as ToolPanelItem)?.Option);
            if (IsLoaded)
            {
                MhnkUiTheme.Apply(this);
            }

            RefreshActiveNavigationVisuals();
        }

        private void SelectInitialTool()
        {
            if (_toolList.SelectedItem == null && _toolList.Items.Count > 0)
            {
                _toolList.SelectedIndex = 0;
            }

            SelectTool(((_toolList.SelectedItem as ListBoxItem)?.Tag as ToolPanelItem)?.Option);
        }

        private void OnToolPanelSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectTool(((_toolList.SelectedItem as ListBoxItem)?.Tag as ToolPanelItem)?.Option);
            RefreshActiveNavigationVisuals();
        }

        private UIElement BuildToolPanel(ToolPanelItem item)
        {
            Grid root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border activeMarker = new Border
            {
                Tag = "MhnkActiveToolMarker",
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 10, 0)
            };
            root.Children.Add(activeMarker);
            Grid.SetColumn(activeMarker, 0);

            StackPanel content = new StackPanel();
            root.Children.Add(content);
            Grid.SetColumn(content, 1);

            WrapPanel meta = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            meta.Children.Add(BuildBadge(item.SourceGroup, item.SourceGroup));
            meta.Children.Add(BuildBadge(item.Category, "Group"));
            meta.Children.Add(BuildBadge(item.Status, "Status"));
            meta.Children.Add(BuildBadge("Risk " + item.Risk, item.Risk));
            content.Children.Add(meta);

            content.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            content.Children.Add(new TextBlock
            {
                Text = item.Summary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            Grid facts = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            facts.Children.Add(BuildPanelFact("Need", item.Input));
            Grid.SetRow(facts.Children[facts.Children.Count - 1], 0);
            facts.Children.Add(BuildPanelFact("Result", item.Result));
            Grid.SetRow(facts.Children[facts.Children.Count - 1], 1);
            content.Children.Add(facts);

            WrapPanel actions = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Button guidelineButton = new Button
            {
                Content = "Guideline",
                Width = 86,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                Tag = item
            };
            guidelineButton.Click += OnToolPanelGuidelineClicked;
            actions.Children.Add(guidelineButton);

            Button runButton = new Button
            {
                Content = "Preview / Run",
                Width = 102,
                Height = 30,
                Tag = item
            };
            runButton.Click += OnToolPanelRunClicked;
            actions.Children.Add(runButton);
            content.Children.Add(actions);

            return new Border
            {
                Tag = "MhnkToolPanel",
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12),
                Child = root
            };
        }

        private static UIElement BuildPanelFact(string label, string value)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock labelText = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 1, 8, 0)
            };
            grid.Children.Add(labelText);
            Grid.SetColumn(labelText, 0);

            TextBlock valueText = new TextBlock
            {
                Text = value ?? "",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            grid.Children.Add(valueText);
            Grid.SetColumn(valueText, 1);

            return grid;
        }

        private static Border BuildBadge(string text, string tone)
        {
            Brush background;
            Brush border;
            Brush foreground;
            GetBadgeBrushes(tone, out background, out border, out foreground);

            return new Border
            {
                Tag = "MhnkThemePreserve",
                Background = background,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 6, 4),
                Child = new TextBlock
                {
                    Tag = "MhnkThemePreserve",
                    Text = text ?? "",
                    Foreground = foreground,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        private static void GetBadgeBrushes(string tone, out Brush background, out Brush border, out Brush foreground)
        {
            string key = (tone ?? "").Trim().ToLowerInvariant();
            bool dark = MhnkUiTheme.IsDark;

            if (key.Contains("high"))
            {
                background = CreateBrush(dark, 69, 10, 10, 254, 226, 226);
                border = CreateBrush(dark, 185, 28, 28, 248, 113, 113);
                foreground = CreateBrush(dark, 254, 226, 226, 153, 27, 27);
                return;
            }

            if (key.Contains("medium"))
            {
                background = CreateBrush(dark, 69, 26, 3, 254, 243, 199);
                border = CreateBrush(dark, 217, 119, 6, 245, 158, 11);
                foreground = CreateBrush(dark, 254, 243, 199, 146, 64, 14);
                return;
            }

            if (key.Contains("low"))
            {
                background = CreateBrush(dark, 20, 83, 45, 220, 252, 231);
                border = CreateBrush(dark, 34, 197, 94, 134, 239, 172);
                foreground = CreateBrush(dark, 220, 252, 231, 22, 101, 52);
                return;
            }

            if (key.Contains("autocad"))
            {
                background = CreateBrush(dark, 64, 35, 7, 255, 237, 213);
                border = CreateBrush(dark, 251, 146, 60, 251, 146, 60);
                foreground = CreateBrush(dark, 255, 237, 213, 154, 52, 18);
                return;
            }

            if (key.Contains("revit"))
            {
                background = CreateBrush(dark, 8, 47, 73, 224, 242, 254);
                border = CreateBrush(dark, 14, 165, 233, 56, 189, 248);
                foreground = CreateBrush(dark, 224, 242, 254, 12, 74, 110);
                return;
            }

            if (key.Contains("selected"))
            {
                background = CreateBrush(dark, 76, 29, 149, 243, 232, 255);
                border = CreateBrush(dark, 168, 85, 247, 192, 132, 252);
                foreground = CreateBrush(dark, 243, 232, 255, 107, 33, 168);
                return;
            }

            if (key.Contains("review") || key.Contains("setup"))
            {
                background = CreateBrush(dark, 63, 63, 70, 244, 244, 245);
                border = CreateBrush(dark, 161, 161, 170, 161, 161, 170);
                foreground = CreateBrush(dark, 244, 244, 245, 63, 63, 70);
                return;
            }

            if (key.Contains("status"))
            {
                background = CreateBrush(dark, 49, 46, 129, 238, 242, 255);
                border = CreateBrush(dark, 129, 140, 248, 165, 180, 252);
                foreground = CreateBrush(dark, 238, 242, 255, 55, 48, 163);
                return;
            }

            background = CreateBrush(dark, 30, 58, 95, 219, 234, 254);
            border = CreateBrush(dark, 96, 165, 250, 96, 165, 250);
            foreground = CreateBrush(dark, 219, 234, 254, 30, 64, 175);
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
                ? Color.FromRgb(darkR, darkG, darkB)
                : Color.FromRgb(lightR, lightG, lightB));
        }

        private void OnToolPanelGuidelineClicked(object sender, RoutedEventArgs e)
        {
            ToolPanelItem item = (sender as FrameworkElement)?.Tag as ToolPanelItem;
            if (item == null)
            {
                return;
            }

            SelectedOption = item.Option;
            SelectListBoxItemForOption(item.Option);
            OpenGuideline();
            e.Handled = true;
        }

        private void OnToolPanelRunClicked(object sender, RoutedEventArgs e)
        {
            ToolPanelItem item = (sender as FrameworkElement)?.Tag as ToolPanelItem;
            if (item == null)
            {
                return;
            }

            SelectedOption = item.Option;
            SelectListBoxItemForOption(item.Option);
            AcceptSelection();
            e.Handled = true;
        }

        private void SelectListBoxItemForOption(MhnkArcCommandOption option)
        {
            if (option == null)
            {
                return;
            }

            ListBoxItem existing = FindListBoxItemForOption(option);
            if (existing == null && _categoryList != null)
            {
                string primaryGroup = GetSourceGroupName(option);
                SourceGroupNavItem sourceGroup = _categoryList.Items
                    .Cast<SourceGroupNavItem>()
                    .FirstOrDefault(x => string.Equals(x.Name, primaryGroup, StringComparison.OrdinalIgnoreCase));
                if (sourceGroup != null && !Equals(_categoryList.SelectedItem, sourceGroup))
                {
                    _categoryList.SelectedItem = sourceGroup;
                }
            }

            existing = FindListBoxItemForOption(option);
            if (existing != null)
            {
                _toolList.SelectedItem = existing;
                _toolList.ScrollIntoView(existing);
            }
        }

        private ListBoxItem FindListBoxItemForOption(MhnkArcCommandOption option)
        {
            foreach (ListBoxItem listBoxItem in _toolList.Items)
            {
                ToolPanelItem item = listBoxItem.Tag as ToolPanelItem;
                if (item != null &&
                    string.Equals(item.Option.Category, option.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Option.Title, option.Title, StringComparison.OrdinalIgnoreCase))
                {
                    return listBoxItem;
                }
            }

            return null;
        }

        private void SelectTool(MhnkArcCommandOption option)
        {
            SelectedOption = option;
            ApplySourceModePolicy(option);
            bool hasSelection = option != null;
            _guidelineButton.IsEnabled = hasSelection;
            _runButton.IsEnabled = hasSelection && !_isRunPending;

            if (!hasSelection)
            {
                if (_workbenchTitleText != null)
                {
                    _workbenchTitleText.Text = "ARC WORKBENCH";
                }

                _titleText.Text = "No tool selected";
                _summaryText.Text = "Select a tool from the list.";
                _detailBadgePanel.Children.Clear();
                _workflowPanel.Children.Clear();
                _workbenchContent.Content = BuildEmptyWorkbench();
                _requirementText.Text = "";
                _resultText.Text = "";
                UpdateSourceModeDescription();
                RefreshActiveNavigationVisuals();
                return;
            }

            _workbenchTitleText.Text = option.Category.ToUpperInvariant() + " WORKBENCH";
            _titleText.Text = option.Title;
            RenderDetailBadges(option);
            _summaryText.Text = option.Summary;
            _requirementText.Text = "Input: " + InferInput(option, SourceMode);
            _resultText.Text = "Result: " + InferResult(option);
            UpdateSourceModeDescription();
            RenderWorkflow(option);
            RenderWorkbench(option);
            RefreshActiveNavigationVisuals();
        }

        private void RefreshActiveNavigationVisuals()
        {
            RefreshActiveSourceGroupVisuals();
            RefreshActiveToolVisuals();
        }

        private void RefreshActiveSourceGroupVisuals()
        {
            if (_categoryList == null)
            {
                return;
            }

            Brush activeBackground = GetActiveSelectionBackgroundBrush();
            Brush activeBorder = GetActiveSelectionBorderBrush();
            Brush normalBackground = GetPanelBackgroundBrush();
            Brush normalBorder = GetPanelBorderBrush();

            foreach (object sourceGroup in _categoryList.Items)
            {
                ListBoxItem item = _categoryList.ItemContainerGenerator.ContainerFromItem(sourceGroup) as ListBoxItem;
                if (item == null)
                {
                    continue;
                }

                bool active = Equals(sourceGroup, _categoryList.SelectedItem);
                item.Background = Brushes.Transparent;
                item.BorderBrush = Brushes.Transparent;
                item.Padding = new Thickness(0);

                Border panel = FindVisualChildByTag<Border>(item, "MhnkSourceGroupPanel");
                if (panel == null)
                {
                    continue;
                }

                panel.Background = active ? activeBackground : normalBackground;
                panel.BorderBrush = active ? activeBorder : normalBorder;
                panel.BorderThickness = active ? new Thickness(2) : new Thickness(1);
                panel.Padding = active ? new Thickness(7, 5, 7, 5) : new Thickness(8, 6, 8, 6);
            }
        }

        private void RefreshActiveToolVisuals()
        {
            if (_toolList == null)
            {
                return;
            }

            Brush activeBackground = GetActiveSelectionBackgroundBrush();
            Brush activeBorder = GetActiveSelectionBorderBrush();
            Brush activeMarker = GetActiveSelectionMarkerBrush();
            Brush normalBackground = GetPanelBackgroundBrush();
            Brush normalBorder = GetPanelBorderBrush();

            foreach (ListBoxItem item in _toolList.Items.OfType<ListBoxItem>())
            {
                bool active = Equals(item, _toolList.SelectedItem);
                item.Background = active ? activeBackground : Brushes.Transparent;
                item.BorderBrush = active ? activeBorder : Brushes.Transparent;
                item.Padding = active ? new Thickness(4) : new Thickness(2);

                Border panel = item.Content as Border;
                if (panel == null)
                {
                    continue;
                }

                panel.Background = active ? activeBackground : normalBackground;
                panel.BorderBrush = active ? activeBorder : normalBorder;
                panel.BorderThickness = active ? new Thickness(2) : new Thickness(1);
                panel.Padding = active ? new Thickness(11) : new Thickness(12);

                Border marker = FindVisualChildByTag<Border>(panel, "MhnkActiveToolMarker");
                if (marker != null)
                {
                    marker.Background = active ? activeMarker : Brushes.Transparent;
                    marker.Visibility = active ? Visibility.Visible : Visibility.Hidden;
                }
            }
        }

        private static T FindVisualChildByTag<T>(DependencyObject parent, string tag)
            where T : FrameworkElement
        {
            if (parent == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                T typed = child as T;
                if (typed != null && string.Equals(typed.Tag as string, tag, StringComparison.Ordinal))
                {
                    return typed;
                }

                T nested = FindVisualChildByTag<T>(child, tag);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static Brush GetActiveSelectionBackgroundBrush()
        {
            return CreateBrush(MhnkUiTheme.IsDark, 15, 42, 72, 239, 246, 255);
        }

        private static Brush GetActiveSelectionBorderBrush()
        {
            return CreateBrush(MhnkUiTheme.IsDark, 96, 165, 250, 37, 99, 235);
        }

        private static Brush GetActiveSelectionMarkerBrush()
        {
            return CreateBrush(MhnkUiTheme.IsDark, 147, 197, 253, 37, 99, 235);
        }

        private static Brush GetPanelBackgroundBrush()
        {
            return CreateBrush(MhnkUiTheme.IsDark, 34, 40, 48, 248, 250, 252);
        }

        private static Brush GetPanelBorderBrush()
        {
            return CreateBrush(MhnkUiTheme.IsDark, 78, 88, 101, 218, 224, 231);
        }

        private void ApplySourceModePolicy(MhnkArcCommandOption option)
        {
            if (option == null || _freeSelectModeButton == null)
            {
                return;
            }

            bool toolChanged = _sourcePolicyOption == null ||
                               !string.Equals(_sourcePolicyOption.Category, option.Category, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(_sourcePolicyOption.Title, option.Title, StringComparison.OrdinalIgnoreCase);
            MhnkArcSourceMode recommended = GetRecommendedSourceMode(option);
            if (toolChanged || !IsSourceModeSupported(option, SourceMode))
            {
                SourceMode = recommended;
                SaveLastSourceMode(SourceMode);
            }

            _sourcePolicyOption = option;
            _isApplyingSourceMode = true;
            try
            {
                _freeSelectModeButton.IsEnabled = IsSourceModeSupported(option, MhnkArcSourceMode.FreeSelect);
                _categoryModeButton.IsEnabled = IsSourceModeSupported(option, MhnkArcSourceMode.Category);
                _byLayerModeButton.IsEnabled = IsSourceModeSupported(option, MhnkArcSourceMode.ByLayer);
                _allModeButton.IsEnabled = IsSourceModeSupported(option, MhnkArcSourceMode.All);
                ApplySourceModeToButtons();
            }
            finally
            {
                _isApplyingSourceMode = false;
            }
        }

        private void RenderDetailBadges(MhnkArcCommandOption option)
        {
            _detailBadgePanel.Children.Clear();
            if (option == null)
            {
                return;
            }

            _detailBadgePanel.Children.Add(BuildBadge(GetSourceGroupName(option), GetSourceGroupName(option)));
            _detailBadgePanel.Children.Add(BuildBadge(option.Category, "Group"));
            _detailBadgePanel.Children.Add(BuildBadge(option.Status, "Status"));
            _detailBadgePanel.Children.Add(BuildBadge("Risk " + InferRisk(option), InferRisk(option)));
        }

        private void RenderWorkflow(MhnkArcCommandOption option)
        {
            _workflowPanel.Children.Clear();
            IList<WorkflowStep> steps = BuildWorkflowSteps(option, SourceMode);
            if (steps.Count == 0)
            {
                return;
            }

            _workflowPanel.Children.Add(BuildWorkflowStepper(steps.Count));

            for (int i = 0; i < steps.Count; i++)
            {
                _workflowPanel.Children.Add(BuildCompactWorkflowStep(i + 1, steps[i]));
            }
        }

        private void RenderWorkbench(MhnkArcCommandOption option)
        {
            if (_workbenchContent == null)
            {
                return;
            }

            if (option == null)
            {
                _workbenchContent.Content = BuildEmptyWorkbench();
                return;
            }

            string category = option.Category ?? "";
            if (string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                _workbenchContent.Content = BuildFilterWorkbench(option);
                return;
            }

            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase))
            {
                _workbenchContent.Content = BuildCreationWorkbench(option);
                return;
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                _workbenchContent.Content = BuildEditionWorkbench(option);
                return;
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                _workbenchContent.Content = BuildSolidsWorkbench(option);
                return;
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                _workbenchContent.Content = BuildXpressWorkbench(option);
                return;
            }

            _workbenchContent.Content = BuildGenericWorkbench(option);
        }

        private static UIElement BuildCompactWorkflowStep(int number, WorkflowStep step)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(BuildStepCircle(number));
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 0);

            TextBlock title = new TextBlock
            {
                Text = step.Title,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.Children.Add(title);
            Grid.SetColumn(title, 1);

            return grid;
        }

        private UIElement BuildEmptyWorkbench()
        {
            return BuildWorkbenchPanel("TOOL PANEL", new TextBlock
            {
                Text = "Select a tool to load its working panel.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        private UIElement BuildFilterWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "SELECTED ELEMENTS",
                BuildFilterSourceRows(option),
                "FILTER RESULT",
                BuildFilterResultRows(option));
        }

        private UIElement BuildCreationWorkbench(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (IsDoorWindowTool(text))
            {
                return BuildDoorWindowWorkbench(option);
            }

            if (IsCadToWallsTool(text))
            {
                return BuildCadToWallsWorkbench(option);
            }

            if (IsCadCreationTool(text))
            {
                return BuildCadCreationWorkbench(option);
            }

            if (IsFinishCreationTool(text))
            {
                return BuildFinishCreationWorkbench(option);
            }

            if (IsRoomCreationTool(text))
            {
                return BuildRoomCreationWorkbench(option);
            }

            return BuildEtlipseToolSurface(
                option,
                "SOURCE ELEMENTS",
                BuildCreationSourceRows(option),
                "TARGET / CREATION OPTIONS",
                BuildCreationResultRows(option));
        }

        private UIElement BuildEditionWorkbench(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("join") || text.Contains("cut") || text.Contains("uncut") || text.Contains("switch"))
            {
                return BuildJoinCutEditionWorkbench(option);
            }

            if (text.Contains("split") || text.Contains("lower"))
            {
                return BuildWallShapeEditionWorkbench(option);
            }

            return BuildEtlipseToolSurface(
                option,
                "SELECTED ELEMENTS",
                BuildEditionSourceRows(option),
                "EDIT CONDITION / RESULT",
                BuildEditionResultRows(option));
        }

        private UIElement BuildRoomCreationWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "ROOMS / BOUNDARIES",
                new List<WorkbenchRow>
                {
                    Row("Rooms", GetSourceModeLabel(SourceMode), "Bounded rooms from selection, active view, category scope, or all model rooms.", "Listed"),
                    Row("Boundary loops", "Room boundary", "Closed loops are reviewed before wall, floor, or ceiling creation.", "Checked"),
                    Row("Level", "Room Level", "Room level is the default; wall tools can switch to selected level or top constraint.", "Ready"),
                    Row("Existing geometry", "Skip / Review", "Existing walls and invalid room loops are skipped or shown in preview.", "Safe")
                },
                "PARAMETERS",
                new List<WorkbenchRow>
                {
                    Row("Type", "System type", GetRoomCreationTypeLabel(option), "Required"),
                    Row("Height / Elevation", "Numeric", GetRoomCreationHeightLabel(option), "Editable"),
                    Row("Offset", "Base / vertical", "Base offset, floor/ceiling elevation, and top offset are confirmed in the options panel.", "Editable"),
                    Row("Top constraint", "Lock / Level above", "Walls can lock to upper level and use top offset like the eTLipse panel.", "Optional"),
                    Row("Post process", "Join / Room Bounding", "Generated walls can join and be set Room Bounding.", "Optional")
                });
        }

        private UIElement BuildCadCreationWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "CAD LAYERS / CURVES",
                new List<WorkbenchRow>
                {
                    Row("CAD imports", GetSourceModeLabel(SourceMode), "Selected or visible CAD imports are scanned by layer.", "Ready"),
                    Row("Model lines", "Curve source", "Model/detail lines can be used with the same curve extraction path.", "Ready"),
                    Row("Layer keyword", "Mapping", "Layer name is matched against smart mapping and ARC Tool Settings.", "Ready"),
                    Row("Closed loops", "Boundary", "Floors, ceilings, rooms, and openings require valid closed loops.", "Checked")
                },
                "LAYER RULE / TARGET",
                BuildCadCreationTargetRows(option));
        }

        private UIElement BuildCadToWallsWorkbench(MhnkArcCommandOption option)
        {
            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();
            return BuildEtlipseToolSurface(
                option,
                "WALL CURVE SOURCE",
                new List<WorkbenchRow>
                {
                    Row("Wall source", GetSourceModeLabel(SourceMode), GetCadToWallsSourceDetail(SourceMode), "Required"),
                    Row("Wall layers", "Mapping", "Matched layer keywords: " + settings.WallLayerKeywords, "Mapped"),
                    Row("Wall lines", "Line / Arc", "CAD polyline segments, CAD curves, model lines, and detail lines are projected to the target level.", "Ready"),
                    Row("Layer fallback", "Settings", "If no smart mapping rule exists, ARC Tool Settings wall layer keywords are used.", "Ready"),
                    Row("Retrieve", "Model read", "Use Retrieve first to confirm visible CAD imports, selected curves, warnings, and available wall types.", "Safe")
                },
                "WALL CREATION SETUP",
                new List<WorkbenchRow>
                {
                    Row("Wall type", "Keyword", settings.PreferredWallTypeKeywords, "Required"),
                    Row("Level", "Active / Selected", "The selected level or active plan level becomes the wall base level.", "Required"),
                    Row("Height", "Unconnected", settings.WallHeightMeters.ToString("0.###") + " m default, overridden by mapping rule when available.", "Editable"),
                    Row("Preview", "Ready / Skip", "Preview lists each candidate curve, layer, mapping rule, length, height, and skip reason before model write.", "Required"),
                    Row("Created walls", "Selection", "After Run Model Change, created walls are selected so the tester can inspect them immediately.", "Result")
                });
        }

        private UIElement BuildDoorWindowWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "MARKERS / HOST WALLS",
                new List<WorkbenchRow>
                {
                    Row("Markers", GetSourceModeLabel(SourceMode), "CAD curves, points, or selected positions are interpreted as door/window markers.", "Ready"),
                    Row("Host walls", "Category", "Nearest wall is found for hosted placement tools.", "Ready"),
                    Row("Side mode", "Wall Side", "Marker side controls hosted family facing/orientation.", "Optional"),
                    Row("Position mode", "Position", "Marker position controls insert point on nearest wall.", "Optional")
                },
                "FAMILY / PLACEMENT",
                new List<WorkbenchRow>
                {
                    Row("Door type", "Family symbol", "Door type keywords select a loaded door family symbol.", "Required"),
                    Row("Window type", "Family symbol", "Window type keywords select a loaded window family symbol.", "Required"),
                    Row("Sill height", "Numeric", "Window sill height defaults to 0.9 m and can use CAD height.", "Editable"),
                    Row("Orientation", "Marker side", "The hosted instance is oriented toward the selected marker side when available.", "Ready"),
                    Row("Preview", "Candidate rows", "Placement candidates are listed before writing instances.", "Ready")
                });
        }

        private UIElement BuildFinishCreationWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "HOSTS / ROOMS",
                new List<WorkbenchRow>
                {
                    Row("Host walls", GetSourceModeLabel(SourceMode), "Selected or visible walls are read as finish hosts.", "Ready"),
                    Row("Rooms", "Category", "Room position helps classify internal and external finish sides.", "Optional"),
                    Row("Existing finishes", "Review", "Existing finish candidates can be skipped during preview.", "Safe"),
                    Row("Boundaries", "Side faces", "Finish wall layers are generated along host side faces.", "Checked")
                },
                "FINISH TARGET",
                new List<WorkbenchRow>
                {
                    Row("Side", "Internal / External", GetFinishSideLabel(option), "Required"),
                    Row("Finish type", "System type", "Selected wall, floor, or ceiling finish type is confirmed before creation.", "Required"),
                    Row("Offset", "Layer thickness", "Finish offset follows type thickness and user options.", "Ready"),
                    Row("Room Bounding", "Parameter", "Wall finish candidates can be room bounding where supported.", "Optional"),
                    Row("Preview", "Candidate rows", "Candidates are listed and checked before write.", "Ready")
                });
        }

        private UIElement BuildJoinCutEditionWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "SELECTED ELEMENTS",
                new List<WorkbenchRow>
                {
                    Row("First element", "Reference", "First selected element is the join-order reference or cutter for first-by-selected tools.", "Required"),
                    Row("Other elements", "Targets", "Remaining selected solid-capable elements become targets.", "Required"),
                    Row("Intersecting pairs", "Check", "Pair tools evaluate intersections before join/cut operations.", "Checked"),
                    Row("Existing state", "Joined / Cut", "Unjoin, switch, and uncut tools only affect pairs with matching current state.", "Safe")
                },
                "INTERACTING / TARGET ELEMENTS",
                new List<WorkbenchRow>
                {
                    Row("Can Join", "Intersections", "Join commands only process intersecting elements Revit allows to join.", "Checked"),
                    Row("Can Cut", "Solid cut", "Cut commands check solid-cut capability before writing.", "Checked"),
                    Row("Switch order", "Joined pairs", "Switch only runs on pairs already joined.", "Checked"),
                    Row("Undo path", "Transaction", "Every operation runs in one Revit transaction.", "Ready")
                });
        }

        private UIElement BuildWallShapeEditionWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "WALLS / REFERENCES",
                new List<WorkbenchRow>
                {
                    Row("Walls", GetSourceModeLabel(SourceMode), "Selected, category, or visible walls are the editable set.", "Ready"),
                    Row("Ceilings", "Reference", "Lower Walls to Ceilings reads nearby ceiling heights.", "Conditional"),
                    Row("Split height", "Numeric", "Horizontal split height is entered before operation.", "Editable"),
                    Row("Target type", "Wall type", "New upper/lower wall type is chosen in the tool options.", "Required")
                },
                "WALL EDIT OPTIONS",
                new List<WorkbenchRow>
                {
                    Row("Operation", "Split / Lower", option.Title, "Selected"),
                    Row("Height source", "User / Ceiling", "Split uses user height; lower uses ceiling reference height.", "Ready"),
                    Row("Offsets", "Top/Base", "Tool options control trim offset and resulting constraints.", "Editable"),
                    Row("Review", "Preview", "Candidate walls are reviewed before writing model changes.", "Ready")
                });
        }

        private UIElement BuildSolidsWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "SELECTED ELEMENTS",
                BuildSolidsSourceRows(option),
                "INTERACTING / TARGET ELEMENTS",
                BuildSolidsResultRows(option));
        }

        private UIElement BuildXpressWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "MODEL / REVIEW SOURCE",
                BuildXpressSourceRows(option),
                "CHECK / OUTPUT",
                BuildXpressResultRows(option));
        }

        private UIElement BuildGenericWorkbench(MhnkArcCommandOption option)
        {
            return BuildEtlipseToolSurface(
                option,
                "SELECTED ELEMENTS",
                new List<WorkbenchRow>
                {
                    Row("Scope", GetSourceModeLabel(SourceMode), option?.Summary, option?.Status)
                },
                "TARGET / RESULT",
                new List<WorkbenchRow>
                {
                    Row(option?.Category, GetPrimaryActionText(option), option?.Title, "Ready")
                });
        }

        private UIElement BuildEtlipseToolSurface(
            MhnkArcCommandOption option,
            string leftTitle,
            IList<WorkbenchRow> leftRows,
            string rightTitle,
            IList<WorkbenchRow> rightRows)
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement guide = BuildOperatorGuide(option);
            root.Children.Add(guide);
            Grid.SetRow(guide, 0);

            UIElement main = _technicalViewEnabled
                ? BuildTechnicalToolSurface(option, leftTitle, leftRows, rightTitle, rightRows)
                : BuildGuidedToolSurface(option, leftTitle, leftRows, rightTitle, rightRows);

            root.Children.Add(main);
            Grid.SetRow(main, 1);

            return root;
        }

        private UIElement BuildTechnicalToolSurface(
            MhnkArcCommandOption option,
            string leftTitle,
            IList<WorkbenchRow> leftRows,
            string rightTitle,
            IList<WorkbenchRow> rightRows)
        {
            Grid main = BuildThreePaneGrid();
            UIElement left = BuildEtlipseTablePanel(option, leftTitle, leftRows, true);
            main.Children.Add(left);
            Grid.SetColumn(left, 0);

            UIElement commandRail = BuildEtlipseCommandRail(option);
            main.Children.Add(commandRail);
            Grid.SetColumn(commandRail, 2);

            UIElement right = BuildEtlipseTablePanel(option, rightTitle, rightRows, false);
            main.Children.Add(right);
            Grid.SetColumn(right, 4);
            return main;
        }

        private UIElement BuildGuidedToolSurface(
            MhnkArcCommandOption option,
            string leftTitle,
            IList<WorkbenchRow> leftRows,
            string rightTitle,
            IList<WorkbenchRow> rightRows)
        {
            Grid main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });

            UIElement source = BuildGuidedPanel(
                "1. Prepare Source",
                "Choose the source, then retrieve live model information.",
                BuildGuidedSourceControls(option),
                SelectGuidedRows(BuildRowsWithSnapshot(option, BuildProductionPanelRows(option, leftRows, true), true), true));
            main.Children.Add(source);
            Grid.SetColumn(source, 0);

            UIElement result = BuildGuidedPanel(
                "2. Check Result Before Running",
                "Confirm expected output and warnings before opening the safe preview.",
                BuildGuidedResultControls(option),
                SelectGuidedRows(BuildRowsWithSnapshot(option, BuildProductionPanelRows(option, rightRows, false), false), false));
            main.Children.Add(result);
            Grid.SetColumn(result, 2);
            return main;
        }

        private static IList<WorkbenchRow> BuildProductionPanelRows(MhnkArcCommandOption option, IList<WorkbenchRow> rows, bool sourcePanel)
        {
            var merged = new List<WorkbenchRow>();
            MhnkArcToolMetadata metadata = option?.Metadata;
            if (metadata != null)
            {
                if (sourcePanel)
                {
                    merged.Add(Row("Panel", metadata.PanelKind, "Stable tool id: " + metadata.ToolId, "vNext"));
                    merged.Add(Row("Source modes", metadata.SupportedSourceModeText, "Recommended: " + GetSourceModeLabel(metadata.RecommendedSourceMode), "Ready"));
                    merged.Add(Row("Required input", GetSourceModeLabel(metadata.RecommendedSourceMode), metadata.RequiredInputText, "Required"));
                    merged.Add(Row("Live retrieve", metadata.SupportsLiveRetrieve ? "Enabled" : "Planned", metadata.LiveRetrieveScope, metadata.SupportsLiveRetrieve ? "Ready" : "Next"));
                }
                else
                {
                    merged.Add(Row("Preview columns", "Candidate grid", string.Join(", ", metadata.PreviewColumns.ToArray()), metadata.RequiresPreviewBeforeRun ? "Required" : "Ready"));
                    merged.Add(Row("Skip reasons", "Per row", "Every skipped candidate should show a reason before model write.", "Required"));
                    merged.Add(Row("Result columns", "After run", string.Join(", ", metadata.ResultColumns.ToArray()), "Evidence"));
                    merged.Add(Row("Undo", metadata.Risk + " risk", metadata.UndoStrategy, metadata.RequiresPreviewBeforeRun ? "Required" : "Ready"));
                }
            }

            foreach (WorkbenchRow row in rows ?? new List<WorkbenchRow>())
            {
                if (!merged.Any(x => string.Equals(x.Item, row.Item, StringComparison.OrdinalIgnoreCase)))
                {
                    merged.Add(row);
                }
            }

            return merged;
        }

        private UIElement BuildGuidedPanel(
            string title,
            string subtitle,
            UIElement controls,
            IList<WorkbenchRow> rows)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            panel.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(controls);
            foreach (WorkbenchRow row in rows)
            {
                panel.Children.Add(BuildGuidedRow(row));
            }

            return BuildEtlipsePanel(panel);
        }

        private UIElement BuildGuidedSourceControls(MhnkArcCommandOption option)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            WrapPanel modes = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            modes.Children.Add(BuildSurfaceSourceModeButton("Select Item", MhnkArcSourceMode.FreeSelect, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("Category", MhnkArcSourceMode.Category, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("By Layer", MhnkArcSourceMode.ByLayer, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("All", MhnkArcSourceMode.All, option));
            panel.Children.Add(modes);

            WrapPanel actions = new WrapPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(BuildSurfaceButton("RETRIEVE MODEL", (_, __) => RefreshWorkspaceSnapshot(), 130));
            actions.Children.Add(BuildSurfaceButton("GUIDELINE", (_, __) => OpenGuideline(), 98));
            panel.Children.Add(actions);
            return panel;
        }

        private UIElement BuildGuidedResultControls(MhnkArcCommandOption option)
        {
            WrapPanel actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            actions.Children.Add(BuildSurfaceButton("SAFE PREVIEW / RUN", (_, __) => AcceptSelection(), 156));
            return actions;
        }

        private static IList<WorkbenchRow> SelectGuidedRows(IList<WorkbenchRow> rows, bool sourcePanel)
        {
            if (rows == null)
            {
                return new List<WorkbenchRow>();
            }

            string[] preferred = sourcePanel
                ? new[] { "Live candidates", "Tool preview", "Panel", "Source modes", "Required input", "Live retrieve", "CAD layer", "Curve source", "Room", "Selected item", "Category item", "Open model", "Selection", "CAD imports", "Wall source", "Wall layers", "Rooms", "Top categories", "Scope", "Layer keyword", "Boundary loops" }
                : new[] { "Live candidates", "Preview contract", "Tool preview", "Preview columns", "Skip reasons", "Result columns", "Undo", "Warnings", "Target types", "Wall type", "Level", "Height", "Preview", "Created walls", "Output", "Command", "Review", "Type", "Evidence" };

            var selected = new List<WorkbenchRow>();
            foreach (string key in preferred)
            {
                WorkbenchRow match = rows.FirstOrDefault(x => string.Equals(x.Item, key, StringComparison.OrdinalIgnoreCase));
                if (match != null && !selected.Contains(match))
                {
                    selected.Add(match);
                }
            }

            foreach (WorkbenchRow row in rows.Where(x => !selected.Contains(x)).Take(Math.Max(0, 4 - selected.Count)))
            {
                selected.Add(row);
            }

            return selected.Take(5).ToList();
        }

        private static UIElement BuildGuidedRow(WorkbenchRow row)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock label = new TextBlock
            {
                Text = row.Item ?? "",
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 1, 8, 0),
                TextWrapping = TextWrapping.Wrap
            };
            grid.Children.Add(label);
            Grid.SetColumn(label, 0);

            TextBlock value = new TextBlock
            {
                Text = row.Detail ?? "",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 10, 0)
            };
            grid.Children.Add(value);
            Grid.SetColumn(value, 1);

            Border badge = BuildBadge(row.State ?? "", row.State ?? "");
            grid.Children.Add(badge);
            Grid.SetColumn(badge, 2);
            return new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 7, 0, 4),
                Margin = new Thickness(0, 2, 0, 0),
                Child = grid
            };
        }

        private UIElement BuildOperatorGuide(MhnkArcCommandOption option)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = GetOperatorTitle(option),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = GetOperatorNextAction(option),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            content.Children.Add(BuildOperatorSteps(option));
            grid.Children.Add(content);
            Grid.SetColumn(content, 0);

            StackPanel quick = new StackPanel
            {
                Width = 160,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            quick.Children.Add(BuildOperatorStatusBox("Source", GetSourceModeLabel(SourceMode)));
            quick.Children.Add(BuildOperatorStatusBox("Recommended", GetSourceModeLabel(GetRecommendedSourceMode(option))));
            quick.Children.Add(BuildSurfaceButton("GUIDELINE", (_, __) => OpenGuideline(), 112));
            quick.Children.Add(BuildSurfaceButton("PREVIEW / RUN", (_, __) => AcceptSelection(), 132));
            grid.Children.Add(quick);
            Grid.SetColumn(quick, 1);

            return BuildEtlipsePanel(grid);
        }

        private UIElement BuildOperatorSteps(MhnkArcCommandOption option)
        {
            IList<WorkflowStep> steps = BuildOperatorStepList(option);
            Grid grid = new Grid();
            for (int i = 0; i < steps.Count; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 118 });
            }

            for (int i = 0; i < steps.Count; i++)
            {
                UIElement card = BuildOperatorStepCard(i + 1, steps[i]);
                grid.Children.Add(card);
                Grid.SetColumn(card, i);
            }

            return grid;
        }

        private static UIElement BuildOperatorStepCard(int number, WorkflowStep step)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 0, 8, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(BuildStepCircle(number));
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 0);

            StackPanel text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = step.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            text.Children.Add(new TextBlock
            {
                Text = step.Description,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            grid.Children.Add(text);
            Grid.SetColumn(text, 1);
            return grid;
        }

        private string GetOperatorTitle(MhnkArcCommandOption option)
        {
            return option == null
                ? "Use This Tool"
                : "Use This Tool: " + option.Title;
        }

        private string GetOperatorNextAction(MhnkArcCommandOption option)
        {
            if (option == null)
            {
                return "Select a tool from the left, then follow the numbered workflow.";
            }

            string recommended = GetSourceModeLabel(GetRecommendedSourceMode(option));
            string action = GetPrimaryActionText(option);
            string snapshot = _snapshot != null && _snapshot.HasData
                ? "Model data is loaded for " + _snapshot.ModelLabel + "."
                : "Click Retrieve first to load the current Revit model into this panel.";

            if (IsCadToWallsTool(GetToolText(option)))
            {
                return snapshot + " Recommended source is By Layer for CAD imports, or Select Item for picked model/detail wall lines. Use CREATE only after wall layers, wall type, level, and height look correct.";
            }

            return snapshot + " Recommended source is " + recommended + ". Use " + action + " only after the source rows look correct.";
        }

        private IList<WorkflowStep> BuildOperatorStepList(MhnkArcCommandOption option)
        {
            MhnkArcSourceMode effectiveMode = GetEffectiveSourceMode(option);
            string action = GetPrimaryActionText(option);
            if (IsCadToWallsTool(GetToolText(option)))
            {
                return new List<WorkflowStep>
                {
                    new WorkflowStep("Source", GetSourceModeLabel(effectiveMode) + ": " + GetCadToWallsSourceDetail(effectiveMode)),
                    new WorkflowStep("Retrieve", "Load visible CAD imports, selected CAD/model/detail lines, warnings, and wall type availability."),
                    new WorkflowStep("Preview", "Check wall candidates, layer mapping, target wall type, level, length, height, and skipped curves."),
                    new WorkflowStep("Create", "Run Model Change creates walls and selects the new walls for review.")
                };
            }

            return new List<WorkflowStep>
            {
                new WorkflowStep("Source", GetSourceModeLabel(effectiveMode) + ": " + GetShortSourceModeText(effectiveMode, option)),
                new WorkflowStep("Retrieve", "Read the active model into this panel. No Revit elements are changed."),
                new WorkflowStep("Preview", "Check rows, warnings, target types, and any option window before running."),
                new WorkflowStep(action, GetShortActionText(option, action))
            };
        }

        private static string GetShortSourceModeText(MhnkArcSourceMode mode, MhnkArcCommandOption option)
        {
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (mode == MhnkArcSourceMode.ByLayer)
            {
                return text.Contains("cad") || text.Contains("layer")
                    ? "use selected or visible CAD imports and layer mapping."
                    : "use layer mode only when the tool supports CAD sources.";
            }

            if (mode == MhnkArcSourceMode.Category)
            {
                return "use visible matching categories in the active view.";
            }

            if (mode == MhnkArcSourceMode.All)
            {
                return "use all safe candidates for this model review tool.";
            }

            return "use only the elements you manually selected.";
        }

        private static string GetShortActionText(MhnkArcCommandOption option, string action)
        {
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (string.Equals(action, "OPEN", StringComparison.OrdinalIgnoreCase))
            {
                return "open the dedicated panel or report window.";
            }

            if (string.Equals(action, "CHECK", StringComparison.OrdinalIgnoreCase))
            {
                return "run a review and read the report before editing model geometry.";
            }

            if (text.Contains("create") || text.Contains("cad to") || string.Equals(action, "CREATE", StringComparison.OrdinalIgnoreCase))
            {
                return "create model elements only after preview confirms the target type and source.";
            }

            if (text.Contains("hide") || text.Contains("isolate") || text.Contains("color") || text.Contains("reset"))
            {
                return "change the active view only after confirming the scope.";
            }

            return "run the command inside Revit; use Undo immediately if the scope is wrong.";
        }

        private UIElement BuildEtlipseTablePanel(
            MhnkArcCommandOption option,
            string title,
            IList<WorkbenchRow> rows,
            bool isSourcePanel)
        {
            IList<WorkbenchRow> sourceRows = BuildRowsWithSnapshot(option, rows, isSourcePanel);
            Grid grid = new Grid { MinHeight = 280 };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(BuildEtlipseSectionHeader(title, isSourcePanel ? "Retrieve and review the source set for this tool." : "Review target conditions and command output for this tool."));
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 0);

            UIElement controls = isSourcePanel ? BuildEtlipseSourceControls(option) : BuildEtlipseTargetControls(option);
            grid.Children.Add(controls);
            Grid.SetRow(controls, 1);

            DataGrid dataGrid = BuildEtlipseDataGrid(sourceRows);
            grid.Children.Add(dataGrid);
            Grid.SetRow(dataGrid, 2);

            UIElement actions = isSourcePanel ? BuildEtlipseSourceActions(option) : BuildEtlipseTargetActions(option);
            grid.Children.Add(actions);
            Grid.SetRow(actions, 3);

            return BuildEtlipsePanel(grid);
        }

        private UIElement BuildEtlipseSectionHeader(string title, string subtitle)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = (title ?? "").ToUpperInvariant(),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            panel.Children.Add(new TextBlock
            {
                Text = subtitle ?? "",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        private UIElement BuildEtlipseSourceControls(MhnkArcCommandOption option)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            WrapPanel modes = new WrapPanel { Orientation = Orientation.Horizontal };
            modes.Children.Add(BuildSurfaceSourceModeButton("Select Item", MhnkArcSourceMode.FreeSelect, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("Category", MhnkArcSourceMode.Category, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("By Layer", MhnkArcSourceMode.ByLayer, option));
            modes.Children.Add(BuildSurfaceSourceModeButton("All", MhnkArcSourceMode.All, option));
            panel.Children.Add(modes);

            WrapPanel commands = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            commands.Children.Add(BuildSurfaceButton("RETRIEVE", (_, __) => RefreshWorkspaceSnapshot(), 92));
            commands.Children.Add(BuildSurfaceButton("ADD", (_, __) => AcceptSelection(), 54));
            commands.Children.Add(BuildSurfaceButton("PREVIEW", (_, __) => AcceptSelection(), 82));
            panel.Children.Add(commands);
            return panel;
        }

        private UIElement BuildEtlipseTargetControls(MhnkArcCommandOption option)
        {
            WrapPanel panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            panel.Children.Add(BuildParameterBox("Tool", option?.Title ?? ""));
            panel.Children.Add(BuildParameterBox("Mode", GetSourceModeLabel(SourceMode)));
            panel.Children.Add(BuildSurfaceButton(GetPrimaryActionText(option), (_, __) => AcceptSelection(), 94));
            return panel;
        }

        private UIElement BuildEtlipseSourceActions(MhnkArcCommandOption option)
        {
            WrapPanel panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(BuildSurfaceButton("SELECT", (_, __) => AcceptSelection(), 74));
            panel.Children.Add(BuildSurfaceButton("ISOLATE", (_, __) => AcceptSelection(), 74));
            panel.Children.Add(BuildSurfaceButton("HIDE", (_, __) => AcceptSelection(), 58));
            panel.Children.Add(BuildSurfaceButton("RESET", (_, __) => AcceptSelection(), 66));
            return panel;
        }

        private UIElement BuildEtlipseTargetActions(MhnkArcCommandOption option)
        {
            WrapPanel panel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(BuildSurfaceButton("CHECK", (_, __) => AcceptSelection(), 70));
            panel.Children.Add(BuildSurfaceButton("SELECT", (_, __) => AcceptSelection(), 74));
            panel.Children.Add(BuildSurfaceButton("EXPORT", (_, __) => AcceptSelection(), 72));
            return panel;
        }

        private UIElement BuildEtlipseCommandRail(MhnkArcCommandOption option)
        {
            StackPanel panel = new StackPanel
            {
                Width = 94,
                Margin = new Thickness(8, 22, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            panel.Children.Add(new TextBlock
            {
                Text = "COMMAND",
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            foreach (string label in BuildEtlipseCommandLabels(option))
            {
                panel.Children.Add(BuildCommandRailButton(label, option));
            }

            return panel;
        }

        private IList<string> BuildEtlipseCommandLabels(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            string category = option?.Category ?? "";
            List<string> labels = new List<string>();

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("interaction center"))
            {
                labels.Add("OPEN");
                labels.Add("JOIN");
                labels.Add("UNJOIN");
                labels.Add("SWITCH");
                labels.Add("CUT");
                labels.Add("UNCUT");
            }
            else if (text.Contains("join") || text.Contains("cut") || text.Contains("uncut") || text.Contains("switch"))
            {
                labels.Add("CHECK");
                labels.Add(GetPrimaryActionText(option));
            }
            else
            {
                labels.Add(GetPrimaryActionText(option));
                labels.Add("PREVIEW");
            }

            labels.Add("GUIDELINE");
            return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private Button BuildCommandRailButton(string label, MhnkArcCommandOption option)
        {
            Button button = new Button
            {
                Content = label,
                Width = 86,
                Height = 34,
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.SemiBold,
                ToolTip = label == "GUIDELINE" ? "Open the HTML guideline for this tool." : "Open this tool preview and run workflow."
            };
            if (string.Equals(label, "GUIDELINE", StringComparison.OrdinalIgnoreCase))
            {
                button.Click += (_, __) => OpenGuideline();
            }
            else
            {
                button.Click += (_, __) => AcceptSelection();
            }

            return button;
        }

        private UIElement BuildEtlipseBottomStrip(MhnkArcCommandOption option)
        {
            Grid grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            WrapPanel status = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            status.Children.Add(BuildCompactStatusBox("Source", GetSourceModeLabel(SourceMode)));
            status.Children.Add(BuildCompactStatusBox("Recommended", GetSourceModeLabel(GetRecommendedSourceMode(option))));
            status.Children.Add(BuildCompactStatusBox("Result", InferResult(option)));
            grid.Children.Add(status);
            Grid.SetColumn(status, 0);

            WrapPanel commands = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            commands.Children.Add(BuildSurfaceButton("GUIDELINE", (_, __) => OpenGuideline(), 92));
            commands.Children.Add(BuildSurfaceButton("PREVIEW / RUN", (_, __) => AcceptSelection(), 116));
            grid.Children.Add(commands);
            Grid.SetColumn(commands, 1);

            return BuildEtlipsePanel(grid);
        }

        private RadioButton BuildSurfaceSourceModeButton(string text, MhnkArcSourceMode mode, MhnkArcCommandOption option)
        {
            RadioButton button = new RadioButton
            {
                Content = text,
                GroupName = "MhnkArcSurfaceSourceMode",
                Tag = mode,
                IsEnabled = IsSourceModeSupported(option, mode),
                IsChecked = SourceMode == mode,
                Margin = new Thickness(0, 0, 12, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Checked += (_, __) =>
            {
                if (_isApplyingSourceMode || SourceMode == mode || !IsSourceModeSupported(SelectedOption, mode))
                {
                    return;
                }

                SourceMode = mode;
                SaveLastSourceMode(SourceMode);
                SelectTool(SelectedOption);
            };
            return button;
        }

        private Button BuildSurfaceButton(string label, RoutedEventHandler handler, double width)
        {
            Button button = new Button
            {
                Content = label,
                Width = width,
                Height = 30,
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 12
            };
            if (handler != null)
            {
                button.Click += handler;
            }

            return button;
        }

        private DataGrid BuildEtlipseDataGrid(IList<WorkbenchRow> rows)
        {
            IList<WorkbenchRow> sourceRows = rows ?? new List<WorkbenchRow>();
            DataGrid dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                IsReadOnly = false,
                ItemsSource = sourceRows,
                Height = GetEtlipseGridHeight(sourceRows.Count),
                MinHeight = 180,
                MaxHeight = 340,
                SelectionMode = DataGridSelectionMode.Extended,
                VerticalAlignment = VerticalAlignment.Top
            };
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "", Binding = new System.Windows.Data.Binding("Include"), Width = 28, MinWidth = 28 });
            dataGrid.Columns.Add(BuildReadOnlyColumn("Item", "Item", 92, 64));
            dataGrid.Columns.Add(BuildReadOnlyColumn("Mode", "Mode", 86, 58));
            dataGrid.Columns.Add(BuildReadOnlyColumn("Condition / Target", "Detail", 1, 120, true));
            dataGrid.Columns.Add(BuildReadOnlyColumn("State", "State", 72, 54));
            return dataGrid;
        }

        private IList<WorkbenchRow> BuildRowsWithSnapshot(MhnkArcCommandOption option, IList<WorkbenchRow> rows, bool isSourcePanel)
        {
            bool explicitEvidence = rows != null && rows.Any(x => string.Equals(x.Item, "Evidence", StringComparison.OrdinalIgnoreCase));
            var result = new List<WorkbenchRow>();
            result.AddRange(BuildSnapshotRows(option, isSourcePanel)
                .Where(x => !explicitEvidence || !string.Equals(x.Item, "Evidence", StringComparison.OrdinalIgnoreCase)));
            result.AddRange(BuildLivePreviewRows(option, isSourcePanel));
            if (rows != null)
            {
                result.AddRange(rows);
            }

            return result;
        }

        private IList<WorkbenchRow> BuildSnapshotRows(MhnkArcCommandOption option, bool isSourcePanel)
        {
            if (_snapshot == null || !_snapshot.HasData)
            {
                return Rows(Row(
                    "Workspace",
                    "Retrieve",
                    "Click Retrieve to read the active Revit model into this panel without changing the model.",
                    "Ready"));
            }

            string toolText = GetToolText(option);
            var rows = new List<WorkbenchRow>();
            if (isSourcePanel)
            {
                rows.Add(Row("Open model", "Active", _snapshot.ModelLabel, "Snapshot"));
                rows.Add(Row(
                    "Selection",
                    GetSourceModeLabel(SourceMode),
                    _snapshot.SelectionCount.ToString() + " selected / " + _snapshot.VisibleElementCount.ToString() + " visible in active view",
                    _snapshot.SelectionCount > 0 ? "Ready" : "Empty"));

                if (SourceMode == MhnkArcSourceMode.ByLayer || toolText.Contains("cad") || toolText.Contains("layer"))
                {
                    rows.Add(Row(
                        "CAD imports",
                        "By Layer",
                        _snapshot.VisibleCadImportCount.ToString() + " visible / " + _snapshot.AllCadImportCount.ToString() + " total import instance(s)",
                        _snapshot.VisibleCadImportCount > 0 || _snapshot.AllCadImportCount > 0 ? "Ready" : "Empty"));
                }
                else if (toolText.Contains("room") || toolText.Contains("wall") || toolText.Contains("floor") || toolText.Contains("ceiling"))
                {
                    rows.Add(Row(
                        "Rooms",
                        "Category / All",
                        _snapshot.VisibleRoomCount.ToString() + " visible / " + _snapshot.AllRoomCount.ToString() + " model room(s)",
                        _snapshot.VisibleRoomCount > 0 || _snapshot.AllRoomCount > 0 ? "Ready" : "Empty"));
                }
                else
                {
                    rows.Add(Row(
                        "Top categories",
                        "Active view",
                        _snapshot.TopCategorySummary,
                        _snapshot.TopCategories.Count > 0 ? "Ready" : "Empty"));
                }
            }
            else
            {
                rows.Add(Row(
                    "Warnings",
                    "Model health",
                    _snapshot.WarningCount.ToString() + " warning(s) in the current model.",
                    _snapshot.WarningCount == 0 ? "Clean" : "Review"));
                rows.Add(Row("Target types", "Available", GetSnapshotTargetSummary(option), "Snapshot"));
                rows.Add(Row(
                    "Evidence",
                    "Runtime report",
                    "Preview / Run records HTML evidence under %LOCALAPPDATA%\\MHNK\\Reports.",
                    "Ready"));
            }

            return rows;
        }

        private IList<WorkbenchRow> BuildLivePreviewRows(MhnkArcCommandOption option, bool isSourcePanel)
        {
            var rows = new List<WorkbenchRow>();
            MhnkArcPreviewResult preview = _snapshot?.Preview;
            if (option == null || preview == null || !preview.HasData)
            {
                return rows;
            }

            if (!preview.Matches(option, SourceMode))
            {
                rows.Add(Row(
                    "Tool preview",
                    "Retrieve",
                    "Click Retrieve to load live candidates for " + (option?.Title ?? "this tool") + " using " + GetSourceModeLabel(SourceMode) + ".",
                    "Needed"));
                return rows;
            }

            rows.Add(Row(
                "Live candidates",
                preview.SourceModeName,
                preview.Summary + " Ready " + preview.ReadyCount.ToString(CultureInfo.InvariantCulture) +
                " / skipped " + preview.SkippedCount.ToString(CultureInfo.InvariantCulture) + ".",
                "Live"));

            IList<MhnkArcPreviewRow> previewRows = isSourcePanel ? preview.SourceRows : preview.TargetRows;
            foreach (MhnkArcPreviewRow previewRow in previewRows
                .Where(x => !string.Equals(x.Item, "Open model", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(x.Item, "Selection scope", StringComparison.OrdinalIgnoreCase))
                .Take(isSourcePanel ? 18 : 12))
            {
                rows.Add(Row(
                    string.IsNullOrWhiteSpace(previewRow.Item) ? "Candidate" : previewRow.Item,
                    string.IsNullOrWhiteSpace(previewRow.ModeText) ? preview.SourceModeName : previewRow.ModeText,
                    previewRow.DetailText,
                    string.IsNullOrWhiteSpace(previewRow.Status) ? "Ready" : previewRow.Status));
            }

            if (!isSourcePanel)
            {
                foreach (string warning in preview.Warnings.Take(3))
                {
                    rows.Add(Row("Retrieve warning", "Review", warning, "Review"));
                }
            }

            return rows;
        }

        private string GetSnapshotTargetSummary(MhnkArcCommandOption option)
        {
            if (_snapshot == null)
            {
                return "No snapshot loaded.";
            }

            string text = GetToolText(option);
            if (text.Contains("door"))
            {
                return _snapshot.DoorTypeCount.ToString() + " door type(s), " + _snapshot.VisibleDoorCount.ToString() + " visible door instance(s).";
            }

            if (text.Contains("window"))
            {
                return _snapshot.WindowTypeCount.ToString() + " window type(s), " + _snapshot.VisibleWindowCount.ToString() + " visible window instance(s).";
            }

            if (text.Contains("ceiling"))
            {
                return _snapshot.CeilingTypeCount.ToString() + " ceiling type(s), " + _snapshot.VisibleCeilingCount.ToString() + " visible ceiling instance(s).";
            }

            if (text.Contains("floor"))
            {
                return _snapshot.FloorTypeCount.ToString() + " floor type(s), " + _snapshot.VisibleFloorCount.ToString() + " visible floor instance(s).";
            }

            if (text.Contains("wall"))
            {
                return _snapshot.WallTypeCount.ToString() + " wall type(s), " + _snapshot.VisibleWallCount.ToString() + " visible wall instance(s).";
            }

            return _snapshot.LevelCount.ToString() + " level(s), " + _snapshot.VisibleElementCount.ToString() + " visible active-view element(s).";
        }

        private static double GetEtlipseGridHeight(int rowCount)
        {
            int count = Math.Max(5, rowCount);
            return Math.Max(180, Math.Min(340, 44 + (count * 24)));
        }

        private static Border BuildEtlipsePanel(UIElement content)
        {
            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = content
            };
        }

        private static Border BuildCompactStatusBox(string label, string value)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            panel.Children.Add(new TextBlock
            {
                Text = value ?? "",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 112,
                Child = panel
            };
        }

        private static Border BuildOperatorStatusBox(string label, string value)
        {
            Border box = BuildCompactStatusBox(label, value);
            box.Margin = new Thickness(0, 0, 0, 6);
            box.MinWidth = 138;
            return box;
        }

        private IList<WorkbenchRow> BuildFilterSourceRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("reset temporary"))
            {
                return Rows(
                    Row("Active view", "View", "Current Revit active view containing temporary hidden/isolate state.", "Required"),
                    Row("Temporary state", "Reset", "Temporary hide/isolate modes in this view will be cleared.", "Ready"));
            }

            if (text.Contains("color review"))
            {
                return Rows(
                    Row("Visible ARC/MEP", "Active view", "Visible architectural and coordination categories receive review colors.", "Ready"),
                    Row("Categories", "ARC/MEP", "Walls, floors, ceilings, rooms, doors/windows, MEP coordination, and generated review solids where visible.", "Ready"),
                    Row("Overrides", "Graphics", "Existing active-view element overrides are updated only for this review command.", "Review"),
                    Row("Scope", GetSourceModeLabel(SourceMode), "Use the selected/source scope to avoid coloring unintended model areas.", "Required"));
            }

            if (text.Contains("hide selection") || text.Contains("isolate selection"))
            {
                return Rows(
                    Row("Selected items", "Select Item", "Only current Revit selected elements are used.", "Required"),
                    Row("Active view", "Temporary", "The operation changes temporary visibility in the active view only.", "Ready"));
            }

            if (text.Contains("preset"))
            {
                return Rows(
                    Row("Selection", "Save", "When elements are selected, they become the saved filter preset.", "Ready"),
                    Row("Saved preset", "Load", "When nothing is selected, the stored preset is restored to selection.", "Ready"));
            }

            if (text.Contains("cad layer"))
            {
                return Rows(
                    Row("CAD import", GetSourceModeLabel(SourceMode), "Selected or visible CAD import instances are inspected.", "Required"),
                    Row("Layers", "By Layer", "CAD layer names are reported and matched in the active view.", "Ready"));
            }

            if (text.Contains("mep elements"))
            {
                return Rows(
                    Row("Active view", "Category", "Visible MEP coordination categories in the ARC view are queried.", "Required"),
                    Row("MEP elements", "Selection", "Matching coordination elements become the current selection.", "Ready"));
            }

            return Rows(
                Row("Seed element", "Select Item", GetFilterSeedDescription(text), "Required"),
                Row("Candidates", "Active view", "Visible elements are compared against the seed filter rule.", "Ready"));
        }

        private IList<WorkbenchRow> BuildFilterResultRows(MhnkArcCommandOption option)
        {
            string title = option?.Title ?? "Filter";
            string text = GetToolText(option);
            if (text.Contains("reset temporary"))
            {
                return Rows(
                    Row("Command", "Reset", title, "Ready"),
                    Row("Output", "Active view", "Cleared temporary hidden/isolate state.", "Result"));
            }

            if (text.Contains("color review"))
            {
                return Rows(
                    Row("Command", "Apply", title, "Ready"),
                    Row("Output", "Graphics", "Color overrides applied to visible review categories.", "Result"),
                    Row("Evidence", "Runtime report", "Dry Run or Run records a command report in the Validation Center.", "Available"),
                    Row("Review", "Active view", "Confirm colors are visible only on the intended categories and view.", "Required"));
            }

            if (text.Contains("hide") || text.Contains("isolate"))
            {
                return Rows(
                    Row("Command", "View operation", title, "Ready"),
                    Row("Output", "Temporary view", "Selected elements change view visibility only.", "Result"));
            }

            if (text.Contains("preset"))
            {
                return Rows(
                    Row("Command", "Save / Load", title, "Ready"),
                    Row("Output", "Selection set", "Persisted or restored element IDs.", "Result"));
            }

            return Rows(
                Row("Rule", "Selection", title, "Ready"),
                Row("Output", "Revit selection", "Matching elements selected for review.", "Result"),
                Row("Validation", "Review", "Inspect selected objects in the active view before further edits.", "Required"));
        }

        private static string GetFilterSeedDescription(string text)
        {
            if ((text ?? "").Contains("family") || (text ?? "").Contains("type"))
            {
                return "The first selected element supplies the family/type match.";
            }

            if ((text ?? "").Contains("level"))
            {
                return "The selected element or active level supplies the level match.";
            }

            if ((text ?? "").Contains("workset"))
            {
                return "The first selected element supplies the workset match.";
            }

            if ((text ?? "").Contains("phase"))
            {
                return "The first selected element supplies its created phase.";
            }

            if ((text ?? "").Contains("parameter"))
            {
                return "The first selected element supplies a useful parameter/value pair.";
            }

            if ((text ?? "").Contains("material"))
            {
                return "The first selected element supplies a material match.";
            }

            return "The first selected element supplies the category match.";
        }

        private IList<WorkbenchRow> BuildCreationResultRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("3d view") || text.Contains("drafting view") || text.Contains("working plan"))
            {
                return Rows(
                    Row("Command", "View creation", option?.Title, "Ready"),
                    Row("Output", "Revit view", "A new configured ARC view is created and opened.", "Result"),
                    Row("Review", "Project Browser", "Verify naming, level, template, scale, and visibility.", "Required"));
            }

            if (text.Contains("model group"))
            {
                return Rows(
                    Row("Selection", "Select Item", "Current selected model elements form the group.", "Required"),
                    Row("Output", "Model Group", "A new Revit model group is created from selection.", "Result"));
            }

            return Rows(
                Row("Command", "Creation", option?.Title, "Ready"),
                Row("Output", "Model", option?.Summary, "Result"),
                Row("Review", "Revit", "Inspect generated model elements immediately after run.", "Required"));
        }

        private IList<WorkbenchRow> BuildCadCreationTargetRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("model manager"))
            {
                return Rows(
                    Row("Layer rows", "Scan", "Mapped CAD layer rows are listed with action, target type, sources, curves, and status.", "Result"),
                    Row("Selection", "Run checkbox", "Only checked ready rows are run from the manager panel.", "Required"),
                    Row("Export", "Scan report", "The layer scan may be exported before creation.", "Optional"));
            }

            if (text.Contains("walls"))
            {
                return Rows(
                    Row("Wall layer", "Layer -> Type", "Mapped wall curves generate basic walls using preferred wall type and height.", "Mapped"),
                    Row("Preview", "Curves", "Line-based wall candidates are shown before model write.", "Required"));
            }

            if (text.Contains("floors"))
            {
                return Rows(
                    Row("Floor layer", "Closed curve", "Mapped closed boundaries create floor candidates.", "Mapped"),
                    Row("Preview", "Boundary loop", "Invalid or open loops are skipped before creation.", "Required"));
            }

            if (text.Contains("ceilings"))
            {
                return Rows(
                    Row("Ceiling layer", "Closed curve", "Mapped closed boundaries create ceiling candidates.", "Mapped"),
                    Row("Type", "Ceiling type", "Selected ceiling type is confirmed in the preview/options window.", "Required"),
                    Row("Elevation", "Level / Offset", "Ceiling elevation follows level reference and vertical offset.", "Editable"),
                    Row("Preview", "Boundary loop", "Invalid/open loops are skipped before model creation.", "Required"),
                    Row("Output", "Ceilings", "One ceiling is created per approved closed boundary.", "Result"));
            }

            if (text.Contains("room boundaries"))
            {
                return Rows(
                    Row("Boundary layer", "Lines", "Mapped curves create Revit room separation lines.", "Mapped"),
                    Row("Output", "Room boundary", "Created boundary lines must be checked in plan view.", "Result"));
            }

            if (text.Contains("rooms"))
            {
                return Rows(
                    Row("Room layer", "Closed curve", "Mapped boundary loops define room centroid candidates.", "Mapped"),
                    Row("Output", "Room points", "Rooms are placed at valid candidate centroids.", "Result"));
            }

            if (text.Contains("openings"))
            {
                return Rows(
                    Row("Opening layer", "Closed curve", "Mapped closed outlines define opening solids.", "Mapped"),
                    Row("Depth", "Setting", "Opening candidate solid depth comes from ARC Tool Settings.", "Ready"));
            }

            return BuildCreationResultRows(option);
        }

        private IList<WorkbenchRow> BuildCreationSourceRows(MhnkArcCommandOption option)
        {
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (text.Contains("cad") || text.Contains("door") || text.Contains("window") || text.Contains("opening"))
            {
                return new List<WorkbenchRow>
                {
                    Row("CAD imports", "By Layer", "Selected/visible CAD imports and model/detail curves are scanned by mapping rules.", SourceMode == MhnkArcSourceMode.ByLayer ? "Ready" : "Optional"),
                    Row("Layer rules", "Mapping", "Wall, floor, ceiling, room, opening, door, and window keywords drive the target action.", "Ready"),
                    Row("Type hints", "Settings", "Preferred wall, door, window, offset, height, and opening depth values feed the preview.", "Ready"),
                    Row("Preview rows", "Manager", "Runnable layer rows appear in CAD to Model Manager before creation.", "Ready")
                };
            }

            if (text.Contains("finish"))
            {
                return new List<WorkbenchRow>
                {
                    Row("Host walls", GetSourceModeLabel(SourceMode), "Selected or visible host walls are reviewed before finish layers are generated.", "Ready"),
                    Row("Rooms", "Category", "Rooms identify internal/external sides and finish candidates where available.", "Optional"),
                    Row("Finish type", "Settings", "Wall, floor, and ceiling finish type options are confirmed in the preview window.", "Ready"),
                    Row("Review table", "Preview", "Candidates are checked and filtered before writing model elements.", "Ready")
                };
            }

            if (text.Contains("room") || text.Contains("ceiling") || text.Contains("floor") || text.Contains("wall"))
            {
                return new List<WorkbenchRow>
                {
                    Row("Rooms", GetSourceModeLabel(SourceMode), "Selected, active-view, or all bounded rooms are listed before creation.", "Ready"),
                    Row("Boundary lines", "Rooms", "Room boundary segments become wall, floor, or ceiling loops.", "Ready"),
                    Row("Level", "Room Level", "The tool can use room level, selected level, all levels, or level above depending on the command.", "Ready"),
                    Row("Types", "Revit System Type", "Wall, floor, ceiling, height, top constraint, and offset are chosen in the tool panel.", "Ready")
                };
            }

            return new List<WorkbenchRow>
            {
                Row("Selection", GetSourceModeLabel(SourceMode), "Current Revit selection or active-view candidates drive the command.", "Ready"),
                Row("Target", option?.Title ?? "Creation", option?.Summary ?? "", option?.Status ?? "Ready")
            };
        }

        private IList<WorkbenchRow> BuildEditionSourceRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("duplicate"))
            {
                return Rows(
                    Row("Candidates", GetSourceModeLabel(SourceMode), "Selected or visible model elements are evaluated for duplicate geometry.", "Ready"),
                    Row("Safety", "Review only", "Candidates are selected for review and are not deleted automatically.", "Safe"));
            }

            if (text.Contains("rename"))
            {
                return Rows(
                    Row("Views / Sheets", "Select Item", "Selected supported views and sheets are rename targets.", "Required"),
                    Row("Prefix", "MHNK", "Only supported selected item names are prefixed.", "Ready"));
            }

            if (text.Contains("graphic"))
            {
                return Rows(
                    Row("Elements", GetSourceModeLabel(SourceMode), "Selected or visible ARC elements provide override targets.", "Ready"),
                    Row("Active view", "Graphics", "Overrides are reset in the active view only.", "Ready"));
            }

            if (text.Contains("pin") || text.Contains("unpin"))
            {
                return Rows(
                    Row("Elements", "Select Item", "Only selected model elements are changed.", "Required"),
                    Row("Pinned state", text.Contains("unpin") ? "Off" : "On", "The selected elements receive the requested pinned state.", "Ready"));
            }

            return Rows(
                Row("Reference", "Select Item", "First selected element supplies the reference value or alignment center.", "Required"),
                Row("Targets", GetSourceModeLabel(SourceMode), "Remaining selected or scoped elements receive the selected edit.", "Ready"),
                Row("Transaction", "Controlled", "The command runs as one reviewable Revit operation.", "Ready"));
        }

        private IList<WorkbenchRow> BuildEditionResultRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("align"))
            {
                return Rows(
                    Row("Operation", "Align", "Target element centers align to the first selected element center.", "Ready"),
                    Row("Reference", "First selected", "The first selected element stays as the alignment seed.", "Required"),
                    Row("Targets", "Remaining selected", "Only the remaining selected elements are moved.", "Required"),
                    Row("Transaction", "Single edit", "All moves are grouped in one Revit operation.", "Ready"),
                    Row("Review", "Location", "Inspect moved elements immediately in the active view.", "Required"));
            }

            if (text.Contains("copy") || text.Contains("mirror") || text.Contains("array"))
            {
                return Rows(
                    Row("Operation", "Copy preset", "Creates a one-step 1 m offset copy of selected elements.", "Ready"),
                    Row("Review", "Copies", "Confirm duplicate positions and host relationships.", "Required"));
            }

            if (text.Contains("duplicate"))
            {
                return Rows(
                    Row("Operation", "Find", "Potential duplicate elements are selected for review.", "Ready"),
                    Row("Output", "Selection", "No automatic delete is performed.", "Safe"));
            }

            if (text.Contains("parameter"))
            {
                return Rows(
                    Row("Operation", "Copy values", "Mark/Comments-style values from the first selected element are copied.", "Ready"),
                    Row("Review", "Parameters", "Check written target parameter values after run.", "Required"));
            }

            if (text.Contains("level") || text.Contains("offset") || text.Contains("type change"))
            {
                return Rows(
                    Row("Operation", "Batch edit", option?.Title, "Ready"),
                    Row("Reference", "Seed element", "The selected reference/active level supplies the target value.", "Required"),
                    Row("Review", "Properties", "Check every changed target in Revit properties.", "Required"));
            }

            return Rows(
                Row("Operation", "Edition", option?.Title, "Ready"),
                Row("Output", "Model state", option?.Summary, "Result"),
                Row("Review", "Revit", "Inspect only intended elements changed.", "Required"));
        }

        private IList<WorkbenchRow> BuildSolidsSourceRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("interaction center"))
            {
                return Rows(
                    Row("Selected elements", "Category / Selection", "The center retrieves host or linked elements into its source table.", "Ready"),
                    Row("Interacting elements", "Check mode", "Its result table supports Intersections, Joined, Can Cut, and Cuts.", "Ready"),
                    Row("Selection set", "Slot 1-9", "Saved set and view isolate/hide controls are inside this tool.", "Ready"));
            }

            if (text.Contains("bounding") || text.Contains("directshape") || text.Contains("delete"))
            {
                return Rows(
                    Row("MHNK solids", GetSourceModeLabel(SourceMode), "Selected or generated MHNK DirectShape solids are operation targets.", "Required"),
                    Row("Scope", "Model / Active view", "Confirm generated-solid scope before writing or deleting.", "Review"));
            }

            return Rows(
                Row("Elements", GetSourceModeLabel(SourceMode), "Selected or visible solid-capable coordination elements are queried.", "Ready"),
                Row("Geometry", "Solids", "Solid geometry is evaluated for report, color, clash, opening, or cut conditions.", "Ready"));
        }

        private IList<WorkbenchRow> BuildSolidsResultRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("interaction center"))
            {
                return Rows(
                    Row("Tool", "Open panel", "Opens its full dual-table retrieve/check/operate workspace.", "Ready"),
                    Row("Operations", "Inside panel", "Join, Unjoin, Switch, Cut, Uncut, selection set, and CSV are contained in this single tool.", "Available"));
            }

            if (text.Contains("report"))
            {
                return Rows(
                    Row("Command", "Report", option?.Title, "Ready"),
                    Row("Output", "Metrics / File", "Solid measurement or exported review results.", "Result"));
            }

            if (text.Contains("check") || text.Contains("clash") || text.Contains("opening"))
            {
                return Rows(
                    Row("Check", "Geometry", option?.Title, "Ready"),
                    Row("Output", "Candidates", "Only candidate intersections/openings/clashes are reported or selected.", "Result"));
            }

            return Rows(
                Row("Operation", "Solids", option?.Title, "Ready"),
                Row("Output", "Model / View", option?.Summary, "Result"),
                Row("Review", "3D view", "Inspect changed solids and hosts after run.", "Required"));
        }

        private IList<WorkbenchRow> BuildXpressSourceRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("settings") || text.Contains("mapping") || text.Contains("theme"))
            {
                return Rows(
                    Row("Configuration", "Saved settings", "This tool reads or modifies MHNK ARC configuration only.", "Ready"),
                    Row("Scope", "No model edit", "Model elements are not created by this setup command.", "Safe"));
            }

            if (text.Contains("cad"))
            {
                return Rows(
                    Row("CAD imports", GetSourceModeLabel(SourceMode), "Selected or visible CAD import instances are the review source.", "Ready"),
                    Row("Layer data", "Mapping", "Mapped layer data is scanned or selected in the current model.", "Ready"));
            }

            return Rows(
                Row("Active model", "All", "The open model and active view provide this command's review scope.", "Ready"),
                Row("Checks", "Read / Report", option?.Summary, "Ready"));
        }

        private IList<WorkbenchRow> BuildXpressResultRows(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("dashboard"))
            {
                return Rows(
                    Row("Output", "Dashboard", "Overview, checks, warnings, CAD imports, and mapping rule tabs.", "Result"),
                    Row("Export", "Report", "Dashboard results can be exported for model review.", "Available"));
            }

            if (text.Contains("validation"))
            {
                return Rows(
                    Row("Output", "Validation Center", "Tool-by-tool runtime evidence and manual parity results.", "Result"),
                    Row("Approval", "Manual", "Only explicitly reviewed commands should be marked Passed.", "Required"));
            }

            if (text.Contains("settings") || text.Contains("mapping") || text.Contains("theme"))
            {
                return Rows(
                    Row("Output", "Configuration", option?.Summary, "Result"),
                    Row("Persist", "Local settings", "Saved changes apply to later ARC tool runs.", "Ready"));
            }

            return Rows(
                Row("Command", "Xpress", option?.Title, "Ready"),
                Row("Output", "Review result", option?.Summary, "Result"),
                Row("Evidence", "Runtime report", "The run is recorded for later validation.", "Available"));
        }

        private static string GetToolText(MhnkArcCommandOption option)
        {
            return ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
        }

        private static bool IsRoomCreationTool(string text)
        {
            text = text ?? "";
            return text.Contains("by room") ||
                   text.Contains("bounded room") ||
                   text.Contains("multiple ceiling") ||
                   text.Contains("multiple ceilings");
        }

        private static bool IsCadCreationTool(string text)
        {
            text = text ?? "";
            return text.Contains("cad to") ||
                   text.Contains("cad layer") ||
                   text.Contains("cad imports") ||
                   text.Contains("model manager");
        }

        private static bool IsCadToWallsTool(string text)
        {
            text = text ?? "";
            return text.Contains("cad to walls") ||
                   text.Contains("cad to wall") ||
                   (text.Contains("cad") && text.Contains("walls"));
        }

        private static string GetCadToWallsSourceDetail(MhnkArcSourceMode mode)
        {
            switch (mode)
            {
                case MhnkArcSourceMode.ByLayer:
                    return "Selected CAD imports are used first; otherwise visible CAD imports in the active view are scanned by wall layer rules.";
                case MhnkArcSourceMode.All:
                    return "All visible CAD imports, model curves, detail curves, grids, and curve-based elements in the active view are scanned.";
                case MhnkArcSourceMode.Category:
                    return "Visible curve-source categories are scanned in the active view when this mode is enabled.";
                case MhnkArcSourceMode.FreeSelect:
                default:
                    return "Manually select CAD import instances, model lines, detail lines, grids, or curve-based wall guide elements.";
            }
        }

        private static bool IsDoorWindowTool(string text)
        {
            text = text ?? "";
            return text.Contains("door") || text.Contains("window");
        }

        private static bool IsFinishCreationTool(string text)
        {
            text = text ?? "";
            return text.Contains("finish") || text.Contains("finishes") || text.Contains("revest");
        }

        private static string GetRoomCreationTypeLabel(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("wall"))
            {
                return "Wall type selected in the options panel.";
            }

            if (text.Contains("floor"))
            {
                return "Floor type selected in the options panel.";
            }

            if (text.Contains("ceiling"))
            {
                return "Ceiling type selected in the options panel.";
            }

            return "Target Revit system type selected in the options panel.";
        }

        private static string GetRoomCreationHeightLabel(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("wall"))
            {
                return "Wall height can use a numeric value or top level constraint.";
            }

            if (text.Contains("floor"))
            {
                return "Floor elevation is controlled by selected level and vertical offset.";
            }

            if (text.Contains("ceiling"))
            {
                return "Ceiling elevation can use offset or level reference.";
            }

            return "Numeric options are confirmed before creation.";
        }

        private static string GetFinishSideLabel(MhnkArcCommandOption option)
        {
            string text = GetToolText(option);
            if (text.Contains("external") || text.Contains("exterior"))
            {
                return "External host-wall side.";
            }

            if (text.Contains("internal") || text.Contains("interior"))
            {
                return "Internal host-wall side.";
            }

            if (text.Contains("floor"))
            {
                return "Room/floor finish region.";
            }

            if (text.Contains("ceiling"))
            {
                return "Room/ceiling finish region.";
            }

            return "Finish side selected by the command.";
        }

        private StackPanel BuildToolRail(string title, IEnumerable<WorkbenchAction> actions)
        {
            StackPanel panel = new StackPanel
            {
                Width = 132,
                Margin = new Thickness(10, 22, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            foreach (WorkbenchAction action in actions ?? new List<WorkbenchAction>())
            {
                panel.Children.Add(BuildCommandButton(action.Label, action.Category, action.Title, 118));
            }

            return panel;
        }

        private UIElement BuildSelectedToolStrip(MhnkArcCommandOption option)
        {
            string title = option?.Title ?? "";
            string text = (title + " " + (option?.Summary ?? "")).ToLowerInvariant();
            MhnkArcToolSettings settings = MhnkArcToolSettings.Load();

            Grid strip = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            strip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string firstLabel = "Source";
            string firstValue = GetSourceModeLabel(SourceMode);
            string secondLabel = "Target";
            string secondValue = title;
            string thirdLabel = "Value";
            string thirdValue = option?.Category ?? "";
            string actionText = GetPrimaryActionText(option);

            if (text.Contains("wall") && text.Contains("room"))
            {
                firstLabel = "Level";
                firstValue = "Room Level";
                secondLabel = "Wall Type";
                secondValue = settings.PreferredWallTypeKeywords;
                thirdLabel = "Height";
                thirdValue = settings.WallHeightMeters.ToString("0.###") + " m";
                actionText = "CREATE";
            }
            else if (text.Contains("floor") && text.Contains("room"))
            {
                firstLabel = "Level";
                firstValue = "Room Level";
                secondLabel = "Floor Type";
                secondValue = "Selected in panel";
                thirdLabel = "Offset";
                thirdValue = "0 mm";
                actionText = "CREATE";
            }
            else if (text.Contains("ceiling"))
            {
                firstLabel = "Reference";
                firstValue = text.Contains("room") ? "Room Level" : GetSourceModeLabel(SourceMode);
                secondLabel = "Ceiling Type";
                secondValue = "Selected in panel";
                thirdLabel = "Elevation";
                thirdValue = "By level/offset";
                actionText = "CREATE";
            }
            else if (text.Contains("cad"))
            {
                firstLabel = "Layer";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Mapping";
                secondValue = "Smart Rules";
                thirdLabel = "Preview";
                thirdValue = "Layer rows";
                actionText = "CREATE";
            }
            else if (text.Contains("door") || text.Contains("window"))
            {
                firstLabel = "Mode";
                firstValue = text.Contains("side") ? "Wall Side" : text.Contains("position") ? "Position" : "CAD Marker";
                secondLabel = "Family Type";
                secondValue = "Door / Window";
                thirdLabel = "Sill";
                thirdValue = "0.9 m";
                actionText = "INSERT";
            }
            else if (text.Contains("split"))
            {
                firstLabel = "Wall Set";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Split Height";
                secondValue = "User value";
                thirdLabel = "New Type";
                thirdValue = "Selected in panel";
                actionText = "APPLY";
            }
            else if (text.Contains("lower"))
            {
                firstLabel = "Walls";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Reference";
                secondValue = "Ceilings";
                thirdLabel = "Offset";
                thirdValue = "User value";
                actionText = "APPLY";
            }
            else if (string.Equals(option?.Category, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                firstLabel = "Rule";
                firstValue = title.Replace("Select by ", "");
                secondLabel = "Scope";
                secondValue = GetSourceModeLabel(SourceMode);
                thirdLabel = "Output";
                thirdValue = title.Contains("Hide") || title.Contains("Isolate") ? "View" : "Selection";
                if (text.Contains("color"))
                {
                    thirdValue = "Graphics";
                    actionText = "APPLY";
                }
                else if (text.Contains("reset"))
                {
                    thirdValue = "View State";
                    actionText = "RESET";
                }
                else if (text.Contains("preset"))
                {
                    thirdValue = "Selection Set";
                    actionText = "SAVE / LOAD";
                }
                else if (text.Contains("hide"))
                {
                    actionText = "HIDE";
                }
                else if (text.Contains("isolate"))
                {
                    actionText = "ISOLATE";
                }
                else
                {
                    actionText = "SELECT";
                }
            }
            else if (string.Equals(option?.Category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                firstLabel = "Source";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Operation";
                secondValue = title;
                thirdLabel = "Output";
                thirdValue = "Model Change";
                actionText = text.Contains("duplicate") ? "REVIEW" : "APPLY";
            }
            else if (string.Equals(option?.Category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                firstLabel = "Source";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Operation";
                secondValue = title;
                thirdLabel = "Output";
                thirdValue = text.Contains("report") ? "Report" : text.Contains("check") ? "Candidates" : "Solids";
                actionText = text.Contains("center") ? "OPEN" : text.Contains("report") ? "EXPORT" : text.Contains("check") ? "CHECK" : "RUN";
            }
            else if (string.Equals(option?.Category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                firstLabel = "Scope";
                firstValue = GetSourceModeLabel(SourceMode);
                secondLabel = "Command";
                secondValue = title;
                thirdLabel = "Output";
                thirdValue = text.Contains("dashboard") ? "Dashboard" : text.Contains("report") ? "Report" : "Review";
                actionText = GetPrimaryActionText(option);
            }

            strip.Children.Add(BuildParameterBox(firstLabel, firstValue));
            Grid.SetColumn(strip.Children[strip.Children.Count - 1], 0);
            strip.Children.Add(BuildParameterBox(secondLabel, secondValue));
            Grid.SetColumn(strip.Children[strip.Children.Count - 1], 1);
            strip.Children.Add(BuildParameterBox(thirdLabel, thirdValue));
            Grid.SetColumn(strip.Children[strip.Children.Count - 1], 2);
            Button run = new Button
            {
                Content = actionText,
                Width = 96,
                Height = 42,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0)
            };
            run.Click += (_, __) => AcceptSelection();
            strip.Children.Add(run);
            Grid.SetColumn(run, 3);

            return BuildWorkbenchPanel("CURRENT TOOL PANEL", strip);
        }

        private static Border BuildParameterBox(string label, string value)
        {
            StackPanel panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            });
            panel.Children.Add(new TextBlock
            {
                Text = value ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 8, 0),
                Child = panel
            };
        }

        private static string GetPrimaryActionText(MhnkArcCommandOption option)
        {
            string title = (option?.Title ?? "").ToLowerInvariant();
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            if (title.Contains("dashboard") || title.Contains("manager") || title.Contains("settings") ||
                title.Contains("validation center") || title.Contains("interaction center") || title.Contains("mapping manager"))
            {
                return "OPEN";
            }

            if (text.Contains("report") || text.Contains("check") || text.Contains("validation"))
            {
                return "CHECK";
            }

            if (text.Contains("create") || text.Contains("cad to") || text.Contains("place"))
            {
                return "CREATE";
            }

            if (text.Contains("select"))
            {
                return "SELECT";
            }

            return "RUN";
        }

        private Grid BuildTwoPaneGrid(double leftWeight, double rightWeight)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star), MinWidth = 260 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWeight, GridUnitType.Star), MinWidth = 260 });
            grid.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 1);
            return grid;
        }

        private Grid BuildThreePaneGrid()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 190 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96), MinWidth = 90, MaxWidth = 104 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star), MinWidth = 190 });
            grid.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 1);
            grid.Children.Add(BuildVerticalGridSplitter());
            Grid.SetColumn(grid.Children[grid.Children.Count - 1], 3);
            return grid;
        }

        private UIElement BuildWorkbenchGrid(string title, IList<WorkbenchRow> rows)
        {
            IList<WorkbenchRow> sourceRows = rows ?? new List<WorkbenchRow>();
            DataGrid dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserResizeColumns = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                IsReadOnly = false,
                ItemsSource = sourceRows,
                Height = GetWorkbenchGridHeight(sourceRows.Count),
                MinHeight = 86,
                MaxHeight = 280,
                SelectionMode = DataGridSelectionMode.Single
            };
            dataGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "", Binding = new System.Windows.Data.Binding("Include"), Width = 34, MinWidth = 34 });
            dataGrid.Columns.Add(BuildReadOnlyColumn("Item", "Item", 132, 92));
            dataGrid.Columns.Add(BuildReadOnlyColumn("Mode", "Mode", 118, 88));
            dataGrid.Columns.Add(BuildReadOnlyColumn("Condition / Target", "Detail", 1, 260, true));
            dataGrid.Columns.Add(BuildReadOnlyColumn("State", "State", 92, 76));
            return BuildWorkbenchPanel(title, dataGrid);
        }

        private static double GetWorkbenchGridHeight(int rowCount)
        {
            int count = Math.Max(1, rowCount);
            return Math.Max(92, Math.Min(280, 36 + (count * 24)));
        }

        private static DataGridTextColumn BuildReadOnlyColumn(string header, string binding, double width, double minWidth, bool star = false)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = star ? new DataGridLength(width, DataGridLengthUnitType.Star) : new DataGridLength(width),
                MinWidth = minWidth,
                IsReadOnly = true,
                ElementStyle = BuildWorkbenchCellStyle()
            };
        }

        private static Style BuildWorkbenchCellStyle()
        {
            Style style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        private static Border BuildWorkbenchPanel(string title, UIElement content)
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 0);
            grid.Children.Add(content);
            Grid.SetRow(content, 1);

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = grid
            };
        }

        private UIElement BuildActionStrip(string title, IEnumerable<WorkbenchAction> actions)
        {
            WrapPanel panel = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (WorkbenchAction action in actions ?? new List<WorkbenchAction>())
            {
                panel.Children.Add(BuildCommandButton(action.Label, action.Category, action.Title, 126));
            }

            return BuildWorkbenchPanel(title, panel);
        }

        private Button BuildCommandButton(string label, string category, string title, double width = 112)
        {
            Button button = new Button
            {
                Content = label,
                Width = width,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 8),
                Tag = Action(label, category, title)
            };
            button.Click += OnWorkbenchActionClicked;
            return button;
        }

        private void OnWorkbenchActionClicked(object sender, RoutedEventArgs e)
        {
            WorkbenchAction action = (sender as FrameworkElement)?.Tag as WorkbenchAction;
            if (action == null)
            {
                return;
            }

            MhnkArcCommandOption option = FindOption(action.Category, action.Title);
            if (option == null)
            {
                MessageBox.Show(this, "Tool not found: " + action.Title, "MHNK ARC Workspace", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectListBoxItemForOption(option);
            SelectTool(option);
            AcceptSelection();
        }

        private MhnkArcCommandOption FindOption(string category, string title)
        {
            return _options.FirstOrDefault(x =>
                string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
        }

        private static WorkbenchRow Row(string item, string mode, string detail, string state)
        {
            return new WorkbenchRow
            {
                Include = true,
                Item = item ?? "",
                Mode = mode ?? "",
                Detail = detail ?? "",
                State = state ?? ""
            };
        }

        private static IList<WorkbenchRow> Rows(params WorkbenchRow[] rows)
        {
            return (rows ?? new WorkbenchRow[0]).ToList();
        }

        private static WorkbenchAction Action(string label, string category, string title)
        {
            return new WorkbenchAction
            {
                Label = label ?? "",
                Category = category ?? "",
                Title = title ?? ""
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

        private static UIElement BuildWorkflowStepCard(int number, WorkflowStep step)
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
                FontSize = 12,
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
                    Text = number.ToString(),
                    Foreground = foreground,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }

        private static IList<WorkflowStep> BuildWorkflowSteps(MhnkArcCommandOption option, MhnkArcSourceMode sourceMode)
        {
            if (option == null)
            {
                return new List<WorkflowStep>();
            }

            string title = option.Title ?? "";
            string category = option.Category ?? "";
            string text = (title + " " + option.Summary).ToLowerInvariant();

            if (string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Choose source", InferInput(option, sourceMode),
                    "Confirm scope", text.Contains("preset") ? "Load the saved preset or prepare the current selection to save." : "Check the active view, source mode, visible categories, and filter rule.",
                    "Run filter", "Click Preview / Run to select, hide, isolate, color, or reset the intended scope.",
                    "Review result", InferResult(option));
            }

            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase))
            {
                if (IsCadToWallsTool(text))
                {
                    return Steps(
                        "Choose wall source", InferInput(option, sourceMode),
                        "Retrieve wall layers", "Read CAD imports and model/detail wall lines, then check mapped wall layers and available wall types.",
                        "Preview walls", "Confirm each ready and skipped wall curve, target type, level, height, and mapping rule.",
                        "Inspect created walls", "Run Model Change, then review the selected new walls in Revit.");
                }

                if (text.Contains("cad"))
                {
                    return Steps(
                        "Choose source", InferInput(option, sourceMode),
                        "Review mapping", "Check layer names, target Revit types, level, offsets, and creation settings.",
                        "Create model", "Click Preview / Run and confirm the creation preview before writing elements.",
                        "Verify elements", "Inspect created elements, host relationships, levels, and room boundaries.");
                }

                if (text.Contains("room"))
                {
                    return Steps(
                        "Choose rooms", InferInput(option, sourceMode),
                        "Set type rules", "Confirm target wall, floor, ceiling, finish, offset, and boundary options.",
                        "Create by room", "Click Preview / Run to generate elements from valid room boundaries.",
                        "Check boundaries", "Inspect created elements by level, room, type, and boundary fit.");
                }

                return Steps(
                    "Choose source", InferInput(option, sourceMode),
                    "Set options", "Review the required type, level, offset, and current selection before running.",
                    "Create elements", "Click Preview / Run to create the selected ARC output.",
                    "Inspect output", InferResult(option));
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Choose source", InferInput(option, sourceMode),
                    "Confirm order", "Check reference elements, target elements, and current view before editing.",
                    "Apply edit", "Click Preview / Run to apply the change inside one Revit transaction.",
                    "Inspect model", "Review the edited elements immediately and undo if the scope is not correct.");
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Open 3D view", "Use a coordination or review view with ARC, MEP, and generated solids visible.",
                    "Check source", InferInput(option, sourceMode),
                    "Run solid tool", "Click Preview / Run to create, select, report, color, or edit solids.",
                    "Review report", InferResult(option));
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase))
            {
                return Steps(
                    "Choose source", InferInput(option, sourceMode),
                    "Run quick tool", "Click Preview / Run to launch the audit, setup, manager, or report workflow.",
                    "Read result", InferResult(option),
                    "Continue work", "Fix warnings, update settings, or move to the next ARC command.");
            }

            return Steps(
                "Choose source", InferInput(option, sourceMode),
                "Preview", "Review the guideline and any options window before running.",
                "Run", "Click Preview / Run when the scope is correct.",
                "Verify", InferResult(option));
        }

        private static IList<WorkflowStep> Steps(
            string title1,
            string description1,
            string title2,
            string description2,
            string title3,
            string description3,
            string title4,
            string description4)
        {
            return new List<WorkflowStep>
            {
                new WorkflowStep(title1, description1),
                new WorkflowStep(title2, description2),
                new WorkflowStep(title3, description3),
                new WorkflowStep(title4, description4)
            };
        }

        private static string InferInput(MhnkArcCommandOption option, MhnkArcSourceMode sourceMode)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.RequiredInputText;
            }

            string text = (option.Title + " " + option.Summary).ToLowerInvariant();
            if (text.Contains("settings") || text.Contains("manager") || text.Contains("dashboard") || text.Contains("report"))
            {
                return "No strict model selection required; source mode still controls CAD scan defaults where applicable.";
            }

            if (sourceMode == MhnkArcSourceMode.FreeSelect)
            {
                return "Select Item: freely select only the Revit elements that should be used.";
            }

            if (sourceMode == MhnkArcSourceMode.Category)
            {
                return "Category: collect applicable elements in the active view, such as rooms, walls, floors, or solids.";
            }

            if (sourceMode == MhnkArcSourceMode.ByLayer)
            {
                return "By Layer: use selected/visible CAD imports and filter by ARC Tool Settings or Mapping Manager layers.";
            }

            if (sourceMode == MhnkArcSourceMode.All)
            {
                return "All: collect all visible active-view candidates, or all bounded rooms for room tools.";
            }

            if (text.Contains("selected") || text.Contains("selection") || text.Contains("select "))
            {
                return "Use a controlled Revit selection or an active coordination view.";
            }

            if (text.Contains("cad"))
            {
                return "Open a view with visible CAD imports, or select the CAD curves/imports first.";
            }

            if (text.Contains("room"))
            {
                return "Use selected bounded rooms or an active plan view with bounded rooms.";
            }

            return "Check the guideline before running on a production model.";
        }

        private static string InferResult(MhnkArcCommandOption option)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.ExpectedResultText;
            }

            string text = (option.Title + " " + option.Summary).ToLowerInvariant();
            if (text.Contains("report") || text.Contains("dashboard") || text.Contains("check"))
            {
                return "Review report, selected candidates, or a validation window.";
            }

            if (text.Contains("create") || text.Contains("place") || text.Contains("convert"))
            {
                return "New model elements or candidate elements may be created.";
            }

            if (text.Contains("join") || text.Contains("cut") || text.Contains("change") || text.Contains("reset") || text.Contains("hide") || text.Contains("isolate"))
            {
                return "Existing model/view state may be changed. Review scope before running.";
            }

            return "The selected workflow runs inside the current Revit document.";
        }

        private static string InferRisk(MhnkArcCommandOption option)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.Risk;
            }

            string text = (option.Title + " " + option.Summary).ToLowerInvariant();
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

        private void OpenGuideline()
        {
            try
            {
                MhnkArcGuideline.Open(_options, SelectedOption);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot open guideline." + Environment.NewLine + ex.Message, "MHNK ARC Workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshWorkspaceSnapshot()
        {
            if (_refreshRequested == null)
            {
                UpdateSnapshot(null);
                return;
            }

            try
            {
                _refreshRequested(SourceMode);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot retrieve Revit model data." + Environment.NewLine + ex.Message, "MHNK ARC Workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnTechnicalViewChanged(object sender, RoutedEventArgs e)
        {
            if (_technicalViewCheckBox == null)
            {
                return;
            }

            _technicalViewEnabled = _technicalViewCheckBox.IsChecked == true;
            SaveTechnicalViewMode(_technicalViewEnabled);
            SelectTool(SelectedOption);
        }

        private void AcceptSelection()
        {
            if (SelectedOption == null || _isRunPending)
            {
                return;
            }

            if (_runRequested != null)
            {
                _isRunPending = true;
                _runButton.Content = "Requested...";
                _runButton.IsEnabled = false;
                try
                {
                    _runRequested(SelectedOption, SourceMode);
                }
                catch (Exception ex)
                {
                    CompleteRunRequest();
                    MessageBox.Show(this, "Cannot start tool." + Environment.NewLine + ex.Message, "MHNK ARC Workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                return;
            }

            DialogResult = true;
            Close();
        }

        private static bool Contains(string source, string query)
        {
            return (source ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSourceGroupName(MhnkArcCommandOption option)
        {
            return option?.Metadata?.SourceGroup ?? SourceGroupAllTools;
        }

        private static bool IsOptionInSourceGroup(MhnkArcCommandOption option, string sourceGroup)
        {
            if (string.Equals(sourceGroup, SourceGroupAllTools, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(GetSourceGroupName(option), sourceGroup, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAutoCadTool(string text)
        {
            text = text ?? "";
            return text.Contains("cad") ||
                   text.Contains("layer") ||
                   text.Contains("mapping") ||
                   text.Contains("marker");
        }

        private static bool IsSelectionTool(string category, string text)
        {
            text = text ?? "";
            return string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("selected") ||
                   text.Contains("selection") ||
                   text.Contains("join") ||
                   text.Contains("unjoin") ||
                   text.Contains("cut") ||
                   text.Contains("uncut") ||
                   text.Contains("pin") ||
                   text.Contains("align") ||
                   text.Contains("rename") ||
                   text.Contains("group from selection");
        }

        private static bool IsRevitCategoryTool(string category, string text)
        {
            text = text ?? "";
            return string.Equals(category, "Filter", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("category") ||
                   text.Contains("room") ||
                   text.Contains("wall") ||
                   text.Contains("floor") ||
                   text.Contains("ceiling") ||
                   text.Contains("door") ||
                   text.Contains("window") ||
                   text.Contains("mep") ||
                   text.Contains("opening") ||
                   text.Contains("solid") ||
                   text.Contains("view");
        }

        private static bool IsReviewOrSetupTool(string text)
        {
            text = text ?? "";
            return text.Contains("settings") ||
                   text.Contains("dashboard") ||
                   text.Contains("report") ||
                   text.Contains("validation") ||
                   text.Contains("diagnostics") ||
                   text.Contains("health") ||
                   text.Contains("warnings") ||
                   text.Contains("missing") ||
                   text.Contains("prepare") ||
                   text.Contains("theme");
        }

        private static string GetSourceGroupDescription(string sourceGroup)
        {
            return MhnkArcToolMetadataRules.GetSourceGroupDescription(sourceGroup);
        }

        private static string GetDefaultSourceGroupForCommandCategory(string category)
        {
            return MhnkArcToolMetadataRules.GetDefaultSourceGroupForCommandCategory(category);
        }

        private static int GetSourceGroupIndex(string sourceGroup)
        {
            return MhnkArcToolMetadataRules.GetSourceGroupIndex(sourceGroup);
        }

        private static int GetCategoryIndex(string category)
        {
            for (int i = 0; i < CategoryOrder.Length; i++)
            {
                if (string.Equals(CategoryOrder[i], category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return CategoryOrder.Length;
        }

        private static string GetSourceModeDescription(MhnkArcSourceMode mode, MhnkArcCommandOption option)
        {
            string toolText = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();
            switch (mode)
            {
                case MhnkArcSourceMode.Category:
                    return "Collect the tool's applicable category in the active view, using a selected seed category when required.";
                case MhnkArcSourceMode.ByLayer:
                    return toolText.Contains("cad") || toolText.Contains("layer")
                        ? "Selected/visible CAD imports are scanned by layer keywords and mapping rules."
                        : "Layer mode is active; non-CAD tools will use their safest selected/visible source fallback.";
                case MhnkArcSourceMode.All:
                    return "All visible active-view candidates are allowed; room tools can collect all bounded rooms.";
                case MhnkArcSourceMode.FreeSelect:
                default:
                    return "Freely select one or more Revit items. Only selected elements are used.";
            }
        }

        private static MhnkArcSourceMode GetRecommendedSourceMode(MhnkArcCommandOption option)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.RecommendedSourceMode;
            }

            string category = option?.Category ?? "";
            string text = ((option?.Title ?? "") + " " + (option?.Summary ?? "")).ToLowerInvariant();

            if (text.Contains("cad") || text.Contains("layer") || text.Contains("door") && text.Contains("window"))
            {
                return MhnkArcSourceMode.ByLayer;
            }

            if (string.Equals(category, "Creation", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("room") || text.Contains("finish")))
            {
                return MhnkArcSourceMode.Category;
            }

            if (string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("cut") || text.Contains("join") || text.Contains("pin") ||
                text.Contains("group") || text.Contains("align") || text.Contains("rename"))
            {
                return MhnkArcSourceMode.FreeSelect;
            }

            if (string.Equals(category, "Solids", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("check") || text.Contains("color") || text.Contains("select")))
            {
                return MhnkArcSourceMode.Category;
            }

            if (string.Equals(category, "Xpress", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("report") || text.Contains("dashboard") || text.Contains("warnings"))
            {
                return MhnkArcSourceMode.All;
            }

            return MhnkArcSourceMode.FreeSelect;
        }

        private MhnkArcSourceMode GetEffectiveSourceMode(MhnkArcCommandOption option)
        {
            if (option == null || IsSourceModeSupported(option, SourceMode))
            {
                return SourceMode;
            }

            MhnkArcSourceMode recommended = GetRecommendedSourceMode(option);
            return IsSourceModeSupported(option, recommended) ? recommended : MhnkArcSourceMode.FreeSelect;
        }

        private static bool IsSourceModeSupported(MhnkArcCommandOption option, MhnkArcSourceMode mode)
        {
            if (option?.Metadata != null)
            {
                return option.Metadata.SupportsSourceMode(mode);
            }

            if (mode == MhnkArcSourceMode.FreeSelect || option == null)
            {
                return true;
            }

            string category = option.Category ?? "";
            string text = ((option.Title ?? "") + " " + (option.Summary ?? "")).ToLowerInvariant();
            bool cadDriven = text.Contains("cad") || text.Contains("layer") ||
                             (text.Contains("door") && text.Contains("window"));
            bool highRisk = string.Equals(category, "Edition", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("cut") || text.Contains("uncut") || text.Contains("join") ||
                            text.Contains("unjoin") || text.Contains("delete") || text.Contains("rename");

            if (mode == MhnkArcSourceMode.ByLayer)
            {
                return cadDriven;
            }

            if (mode == MhnkArcSourceMode.Category)
            {
                return (!cadDriven || text.Contains("select by cad layer")) && !highRisk;
            }

            if (mode == MhnkArcSourceMode.All)
            {
                return !highRisk;
            }

            return true;
        }

        private static string GetSourceModeLabel(MhnkArcSourceMode mode)
        {
            return MhnkArcToolMetadataRules.GetSourceModeLabel(mode);
        }

        private static string GetSourceModeReason(MhnkArcCommandOption option, MhnkArcSourceMode mode)
        {
            switch (mode)
            {
                case MhnkArcSourceMode.ByLayer:
                    return "the tool reads CAD or mapped marker geometry.";
                case MhnkArcSourceMode.Category:
                    return "the tool works from model element categories.";
                case MhnkArcSourceMode.All:
                    return "the tool reviews the broader active model scope.";
                case MhnkArcSourceMode.FreeSelect:
                default:
                    return "the tool edits only controlled selected items.";
            }
        }

        private static string GetSourceModeStatePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "source_mode.txt");
        }

        private static MhnkArcSourceMode LoadLastSourceMode()
        {
            try
            {
                string value = File.ReadAllText(GetSourceModeStatePath()).Trim();
                if (Enum.TryParse(value, true, out MhnkArcSourceMode mode))
                {
                    return mode;
                }
            }
            catch
            {
            }

            return MhnkArcSourceMode.FreeSelect;
        }

        private static void SaveLastSourceMode(MhnkArcSourceMode mode)
        {
            try
            {
                string path = GetSourceModeStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, mode.ToString());
            }
            catch
            {
                // UI state should never block tool execution.
            }
        }

        private static string GetTechnicalViewStatePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "technical_view.txt");
        }

        private static bool LoadTechnicalViewMode()
        {
            try
            {
                string value = File.ReadAllText(GetTechnicalViewStatePath()).Trim();
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void SaveTechnicalViewMode(bool enabled)
        {
            try
            {
                string path = GetTechnicalViewStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, enabled ? "true" : "false");
            }
            catch
            {
                // UI preference should never block tool execution.
            }
        }

        private static void EnableVirtualization(ItemsControl control)
        {
            control.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            control.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            control.SetValue(ScrollViewer.CanContentScrollProperty, true);
        }

        private static string GetStatePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "ui_state.txt");
        }

        private static string LoadLastGroup()
        {
            try
            {
                return File.ReadAllText(GetStatePath()).Trim();
            }
            catch
            {
                return "";
            }
        }

        private static void SaveLastGroup(string sourceGroup)
        {
            try
            {
                string path = GetStatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sourceGroup ?? "");
            }
            catch
            {
                // UI state should never block tool execution.
            }
        }

        private static string GetWorkspaceLayoutPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "workspace-layout.json");
        }

        private static WorkspaceLayoutState LoadWorkspaceLayout()
        {
            try
            {
                string path = GetWorkspaceLayoutPath();
                if (File.Exists(path))
                {
                    return CamboBimJson.Deserialize<WorkspaceLayoutState>(File.ReadAllText(path)) ?? new WorkspaceLayoutState();
                }
            }
            catch
            {
                // Invalid saved layout should not prevent the workspace from opening.
            }

            return null;
        }

        private void SaveWorkspaceLayout()
        {
            try
            {
                Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
                var layout = new WorkspaceLayoutState
                {
                    WindowWidth = bounds.Width > 0 ? bounds.Width : Width,
                    WindowHeight = bounds.Height > 0 ? bounds.Height : Height,
                    GroupsWidth = _groupsColumn?.ActualWidth ?? 162,
                    ToolsWidth = _toolsColumn?.ActualWidth ?? 340,
                    WorkflowWidth = _workflowBandColumn?.ActualWidth ?? 292,
                    UpperBandHeight = _upperBandRow?.ActualHeight ?? 178
                };
                string path = GetWorkspaceLayoutPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, CamboBimJson.Serialize(layout));
            }
            catch
            {
                // Resized panel state is optional UI convenience.
            }
        }

        private static double ClampLayoutValue(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return minimum;
            }

            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private sealed class SourceGroupNavItem
        {
            public SourceGroupNavItem(string name, int count, string description)
            {
                Name = name;
                Count = count;
                Description = description ?? "";
            }

            public string Name { get; }
            public int Count { get; }
            public string Description { get; }
            public string DisplayText => Name + "  (" + Count + ")";
        }

        private sealed class ToolPanelItem
        {
            public ToolPanelItem(MhnkArcCommandOption option, string sourceGroup, string risk, string input, string result)
            {
                Option = option;
                SourceGroup = sourceGroup ?? "";
                Risk = risk ?? "";
                Input = input ?? "";
                Result = result ?? "";
            }

            public MhnkArcCommandOption Option { get; }
            public string SourceGroup { get; }
            public string Title => Option.Title;
            public string Summary => Option.Summary;
            public string Category => Option.Category;
            public string Status => Option.Status;
            public string Risk { get; }
            public string Input { get; }
            public string Result { get; }
        }

        private sealed class WorkflowStep
        {
            public WorkflowStep(string title, string description)
            {
                Title = title ?? "";
                Description = description ?? "";
            }

            public string Title { get; }
            public string Description { get; }
        }

        private sealed class WorkbenchRow
        {
            public bool Include { get; set; }
            public string Item { get; set; }
            public string Mode { get; set; }
            public string Detail { get; set; }
            public string State { get; set; }
        }

        private sealed class WorkbenchAction
        {
            public string Label { get; set; }
            public string Category { get; set; }
            public string Title { get; set; }
        }

        private sealed class WorkspaceLayoutState
        {
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public double GroupsWidth { get; set; }
            public double ToolsWidth { get; set; }
            public double WorkflowWidth { get; set; }
            public double UpperBandHeight { get; set; }
        }
    }
}
