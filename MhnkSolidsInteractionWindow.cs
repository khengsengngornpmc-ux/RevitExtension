using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using RevitUIDocument = Autodesk.Revit.UI.UIDocument;
using WpfBinding = System.Windows.Data.Binding;
using Grid = System.Windows.Controls.Grid;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkSolidsInteractionWindow : Window
    {
        private const int MaxRetrievedElements = 1200;
        private const int MaxInteractionElements = 1600;
        private const int MaxPairs = 2400;
        private const int MaxPairEvaluations = 250000;

        private readonly MhnkArcContext _context;
        private readonly Document _document;
        private readonly RevitUIDocument _uiDocument;
        private readonly IList<SolidsElementRow> _leftRows = new List<SolidsElementRow>();
        private readonly IList<SolidsPairRow> _pairRows = new List<SolidsPairRow>();

        private readonly ComboBox _leftCategoryBox;
        private readonly ComboBox _rightCategoryBox;
        private readonly ComboBox _checkModeBox;
        private readonly ComboBox _savedSlotBox;
        private readonly RadioButton _allInCategoryRadio;
        private readonly RadioButton _selectionFromRevitRadio;
        private readonly CheckBox _activeViewOnlyCheck;
        private readonly CheckBox _includeLinkedCheck;
        private readonly CheckBox _highlightOnRetrieveCheck;
        private readonly CheckBox _highlightOnCheckCheck;
        private readonly CheckBox _stackResultsCheck;
        private readonly CheckBox _includeAdjacentCheck;
        private readonly CheckBox _limitToSavedSetCheck;
        private readonly CheckBox _framingEndJoinCheck;
        private readonly TextBox _leftFilterBox;
        private readonly TextBox _rightFilterBox;
        private readonly DataGrid _leftGrid;
        private readonly DataGrid _rightGrid;
        private readonly TextBlock _statusText;
        private Button _joinButton;
        private Button _unjoinButton;
        private Button _switchButton;
        private Button _uncutButton;
        private Button _cutButton;

        private string _lastCheckMode = "";

        public MhnkSolidsInteractionWindow(MhnkArcContext context, IntPtr revitMainWindowHandle)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _document = context.Document;
            _uiDocument = context.UiDocument;

            Title = "MHNK SOLIDS - INTERACTION CENTER";
            Width = 1220;
            Height = 720;
            MinWidth = 980;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            Grid root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            Menu menu = BuildMenu();
            root.Children.Add(menu);
            Grid.SetRow(menu, 0);

            Border workflowHeader = BuildWorkflowHeader();
            root.Children.Add(workflowHeader);
            Grid.SetRow(workflowHeader, 1);

            Grid main = new Grid { Margin = new Thickness(0, 8, 0, 8) };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(main);
            Grid.SetRow(main, 2);

            _leftCategoryBox = new ComboBox { MinWidth = 190, Height = 28 };
            _rightCategoryBox = new ComboBox { MinWidth = 190, Height = 28 };
            _checkModeBox = new ComboBox { MinWidth = 132, Height = 28 };
            _savedSlotBox = new ComboBox { Width = 58, Height = 28 };
            _leftFilterBox = new TextBox { Height = 28, Margin = new Thickness(0, 6, 0, 6) };
            _rightFilterBox = new TextBox { Height = 28, Margin = new Thickness(0, 6, 0, 6) };
            _leftGrid = CreateElementGrid();
            _rightGrid = CreatePairGrid();

            _allInCategoryRadio = new RadioButton { Content = "All in Category", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
            _selectionFromRevitRadio = new RadioButton { Content = "Selection from Revit", Margin = new Thickness(0, 0, 14, 0) };
            _activeViewOnlyCheck = new CheckBox { Content = "Active view only", IsChecked = true, Margin = new Thickness(0, 0, 14, 0) };
            _includeLinkedCheck = new CheckBox { Content = "Include linked model checks", IsChecked = true };
            _highlightOnRetrieveCheck = new CheckBox { Content = "Auto-select retrieved", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
            _highlightOnCheckCheck = new CheckBox { Content = "Auto-select checked pairs", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
            _stackResultsCheck = new CheckBox { Content = "Stack results", IsChecked = true };
            _includeAdjacentCheck = new CheckBox { Content = "Include adjacent elements", Margin = new Thickness(0, 0, 12, 0) };
            _limitToSavedSetCheck = new CheckBox { Content = "Limit checks to saved set", Margin = new Thickness(0, 0, 12, 0) };
            _framingEndJoinCheck = new CheckBox { Content = "Include structural framing end joins" };

            Border leftPanel = BuildLeftPanel();
            main.Children.Add(leftPanel);
            Grid.SetColumn(leftPanel, 0);

            StackPanel commandPanel = BuildCommandPanel();
            main.Children.Add(commandPanel);
            Grid.SetColumn(commandPanel, 1);

            Border rightPanel = BuildRightPanel();
            main.Children.Add(rightPanel);
            Grid.SetColumn(rightPanel, 2);

            _statusText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 0)
            };
            root.Children.Add(_statusText);
            Grid.SetRow(_statusText, 3);

            _checkModeBox.ItemsSource = new[] { "Intersections", "Joined", "Can Cut", "Cuts" };
            _checkModeBox.SelectedIndex = 0;
            _savedSlotBox.ItemsSource = Enumerable.Range(1, 9).Select(x => x.ToString()).ToList();
            _savedSlotBox.SelectedIndex = 0;

            _leftFilterBox.TextChanged += (_, __) => RefreshLeftGrid();
            _rightFilterBox.TextChanged += (_, __) => RefreshRightGrid();
            _includeLinkedCheck.Checked += (_, __) => RefreshCategories();
            _includeLinkedCheck.Unchecked += (_, __) => RefreshCategories();

            RefreshCategories();
            RefreshLeftGrid();
            RefreshRightGrid();
            UpdateOperationButtons();
            Notify("Review videos 1, 2, and 4: retrieve elements, check interactions, then run enabled operations.");
            MhnkUiTheme.Apply(this);
        }

        private Menu BuildMenu()
        {
            Menu menu = new Menu();

            MenuItem application = new MenuItem { Header = "Application" };
            application.Items.Add(BuildMenuItem("Export CSV", (_, __) => ExportCsv()));
            application.Items.Add(BuildMenuItem("Reset Application", (_, __) => ResetApplication()));
            application.Items.Add(new Separator());
            application.Items.Add(BuildMenuItem("Close", (_, __) => Close()));
            menu.Items.Add(application);

            MenuItem tools = new MenuItem { Header = "Tools" };
            tools.Items.Add(BuildMenuItem("Select Checked", (_, __) => SelectCheckedPairsOrLeft()));
            tools.Items.Add(BuildMenuItem("Isolate Checked", (_, __) => IsolateCheckedPairsOrLeft()));
            tools.Items.Add(BuildMenuItem("Hide Checked", (_, __) => HideCheckedPairsOrLeft()));
            tools.Items.Add(BuildMenuItem("Reset View", (_, __) => ResetTemporaryView()));
            menu.Items.Add(tools);

            MenuItem help = new MenuItem { Header = "Help" };
            help.Items.Add(BuildMenuItem("Video Checklist", (_, __) => ShowVideoChecklist()));
            menu.Items.Add(help);

            return menu;
        }

        private static MenuItem BuildMenuItem(string header, RoutedEventHandler click)
        {
            var item = new MenuItem { Header = header };
            item.Click += click;
            return item;
        }

        private static Border BuildWorkflowHeader()
        {
            Grid steps = new Grid();
            steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            steps.Children.Add(BuildStepBlock("1. Select", "Retrieve host or linked elements by category or Revit selection."));
            Grid.SetColumn(steps.Children[steps.Children.Count - 1], 0);
            steps.Children.Add(BuildStepBlock("2. Check", "Find intersections, joined pairs, cut-capable pairs, or existing cuts."));
            Grid.SetColumn(steps.Children[steps.Children.Count - 1], 1);
            steps.Children.Add(BuildStepBlock("3. Operate", "Run only the operation unlocked by the latest check."));
            Grid.SetColumn(steps.Children[steps.Children.Count - 1], 2);

            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 8, 0, 0),
                Child = steps
            };
        }

        private static StackPanel BuildStepBlock(string title, string detail)
        {
            StackPanel panel = new StackPanel { Margin = new Thickness(6, 0, 10, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            panel.Children.Add(new TextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        private Border BuildLeftPanel()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(BuildSectionHeader("SELECTED ELEMENTS"));
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 0);

            WrapPanel sourceRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
            sourceRow.Children.Add(_allInCategoryRadio);
            sourceRow.Children.Add(_selectionFromRevitRadio);
            grid.Children.Add(sourceRow);
            Grid.SetRow(sourceRow, 1);

            StackPanel retrieveRow = new StackPanel { Orientation = Orientation.Horizontal };
            retrieveRow.Children.Add(_leftCategoryBox);
            retrieveRow.Children.Add(CreateSmallButton("SELECT", (_, __) => RetrieveLeft(false), 78));
            retrieveRow.Children.Add(CreateSmallButton("+", (_, __) => RetrieveLeft(true), 34));
            retrieveRow.Children.Add(CreateSmallButton("All", (_, __) => SetLeftChecks(true), 44));
            retrieveRow.Children.Add(CreateSmallButton("None", (_, __) => SetLeftChecks(false), 54));
            grid.Children.Add(retrieveRow);
            Grid.SetRow(retrieveRow, 2);

            WrapPanel retrievalAdvanced = new WrapPanel { Orientation = Orientation.Horizontal };
            retrievalAdvanced.Children.Add(_activeViewOnlyCheck);
            retrievalAdvanced.Children.Add(_includeLinkedCheck);
            grid.Children.Add(BuildAdvancedExpander("Advanced retrieval", retrievalAdvanced, false));
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 3);

            _leftFilterBox.ToolTip = "Filter retrieved elements by id, category, family, type, source, or name.";
            grid.Children.Add(_leftFilterBox);
            Grid.SetRow(_leftFilterBox, 4);

            grid.Children.Add(_leftGrid);
            Grid.SetRow(_leftGrid, 5);

            StackPanel viewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            viewRow.Children.Add(_highlightOnRetrieveCheck);
            viewRow.Children.Add(CreateSmallButton("Select", (_, __) => SelectLeftChecked(), 64));
            viewRow.Children.Add(CreateSmallButton("Isolate", (_, __) => IsolateLeftChecked(), 64));
            viewRow.Children.Add(CreateSmallButton("Hide", (_, __) => HideLeftChecked(), 52));
            viewRow.Children.Add(CreateSmallButton("Reset", (_, __) => ResetTemporaryView(), 58));
            grid.Children.Add(viewRow);
            Grid.SetRow(viewRow, 6);

            StackPanel savedRow = new StackPanel { Orientation = Orientation.Horizontal };
            savedRow.Children.Add(new TextBlock { Text = "Selection set", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            savedRow.Children.Add(_savedSlotBox);
            savedRow.Children.Add(CreateSmallButton("+", (_, __) => SaveSelectionSet(), 34));
            savedRow.Children.Add(CreateSmallButton("-", (_, __) => DeleteSelectionSet(), 34));
            savedRow.Children.Add(CreateSmallButton("CLEAR", (_, __) => ClearLeftList(), 64));
            savedRow.Children.Add(CreateSmallButton("LOAD", (_, __) => LoadSelectionSet(), 58));
            grid.Children.Add(savedRow);
            Grid.SetRow(savedRow, 7);

            return BuildPanel(grid);
        }

        private Border BuildRightPanel()
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(BuildSectionHeader("INTERACTING ELEMENTS"));
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 0);

            StackPanel checkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
            checkRow.Children.Add(_checkModeBox);
            checkRow.Children.Add(_rightCategoryBox);
            checkRow.Children.Add(CreateSmallButton("CHECK", (_, __) => CheckInteractions(), 74));
            grid.Children.Add(checkRow);
            Grid.SetRow(checkRow, 1);

            WrapPanel modifierRow = new WrapPanel { Orientation = Orientation.Horizontal };
            modifierRow.Children.Add(_stackResultsCheck);
            modifierRow.Children.Add(_includeAdjacentCheck);
            modifierRow.Children.Add(_limitToSavedSetCheck);
            modifierRow.Children.Add(_framingEndJoinCheck);
            grid.Children.Add(BuildAdvancedExpander("Advanced checks and modifiers", modifierRow, false));
            Grid.SetRow(grid.Children[grid.Children.Count - 1], 2);

            _rightFilterBox.ToolTip = "Filter interaction rows by id, category, type, status, source, or name.";
            grid.Children.Add(_rightFilterBox);
            Grid.SetRow(_rightFilterBox, 3);

            grid.Children.Add(_rightGrid);
            Grid.SetRow(_rightGrid, 4);

            StackPanel viewRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            viewRow.Children.Add(_highlightOnCheckCheck);
            viewRow.Children.Add(CreateSmallButton("Select", (_, __) => SelectPairChecked(), 64));
            viewRow.Children.Add(CreateSmallButton("Isolate", (_, __) => IsolatePairChecked(), 64));
            viewRow.Children.Add(CreateSmallButton("Hide", (_, __) => HidePairChecked(), 52));
            viewRow.Children.Add(CreateSmallButton("Reset", (_, __) => ResetTemporaryView(), 58));
            viewRow.Children.Add(CreateSmallButton("All", (_, __) => SetPairChecks(true), 44));
            viewRow.Children.Add(CreateSmallButton("None", (_, __) => SetPairChecks(false), 54));
            grid.Children.Add(viewRow);
            Grid.SetRow(viewRow, 5);

            StackPanel exportRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            exportRow.Children.Add(CreateSmallButton("EXPORT CSV", (_, __) => ExportCsv(), 104));
            grid.Children.Add(exportRow);
            Grid.SetRow(exportRow, 6);

            return BuildPanel(grid);
        }

        private StackPanel BuildCommandPanel()
        {
            StackPanel panel = new StackPanel
            {
                Width = 124,
                Margin = new Thickness(10, 22, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            panel.Children.Add(new TextBlock
            {
                Text = "OPERATIONS",
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });

            _joinButton = CreateCommandButton("Join", (_, __) => RunOperation("Join"));
            _unjoinButton = CreateCommandButton("Unjoin", (_, __) => RunOperation("Unjoin"));
            _switchButton = CreateCommandButton("Switch", (_, __) => RunOperation("Switch"));
            _uncutButton = CreateCommandButton("Uncut", (_, __) => RunOperation("Uncut"));
            _cutButton = CreateCommandButton("Cut", (_, __) => RunOperation("Cut"));

            panel.Children.Add(_joinButton);
            panel.Children.Add(_unjoinButton);
            panel.Children.Add(_switchButton);
            panel.Children.Add(_uncutButton);
            panel.Children.Add(_cutButton);
            return panel;
        }

        private static TextBlock BuildSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
        }

        private static Border BuildPanel(UIElement child)
        {
            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = child
            };
        }

        private static Expander BuildAdvancedExpander(string header, UIElement content, bool isExpanded)
        {
            return new Expander
            {
                Header = header,
                IsExpanded = isExpanded,
                Margin = new Thickness(0, 4, 0, 4),
                Content = content
            };
        }

        private static Button CreateSmallButton(string content, RoutedEventHandler click, double width)
        {
            Button button = new Button
            {
                Content = content,
                Width = width,
                Height = 28,
                Margin = new Thickness(6, 0, 0, 0)
            };
            button.Click += click;
            return button;
        }

        private static Button CreateCommandButton(string content, RoutedEventHandler click)
        {
            Button button = new Button
            {
                Content = content,
                Width = 104,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.SemiBold
            };
            button.Click += click;
            return button;
        }

        private static DataGrid CreateElementGrid()
        {
            DataGrid grid = CreateBaseGrid();
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "", Binding = new WpfBinding("IsChecked"), Width = 36 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Id", Binding = new WpfBinding("IdText"), Width = 76 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Category", Binding = new WpfBinding("Category"), Width = 118 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Family / Type", Binding = new WpfBinding("FamilyType"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Source", Binding = new WpfBinding("Source"), Width = 86 });
            return grid;
        }

        private static DataGrid CreatePairGrid()
        {
            DataGrid grid = CreateBaseGrid();
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "", Binding = new WpfBinding("IsChecked"), Width = 36 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Selected", Binding = new WpfBinding("FirstLabel"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Interacting", Binding = new WpfBinding("SecondLabel"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new WpfBinding("Status"), Width = 150 });
            return grid;
        }

        private static DataGrid CreateBaseGrid()
        {
            DataGrid grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowHeaderWidth = 0,
                Margin = new Thickness(0),
                FontSize = 12,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            grid.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
            grid.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
            grid.SetValue(ScrollViewer.CanContentScrollProperty, true);
            return grid;
        }

        private void RefreshCategories()
        {
            IList<string> categories = GetAvailableCategoryNames();
            _leftCategoryBox.ItemsSource = categories;
            _rightCategoryBox.ItemsSource = categories;
            SelectCategory(_leftCategoryBox, "Structural Framing");
            SelectCategory(_rightCategoryBox, "Walls");
        }

        private void SelectCategory(ComboBox comboBox, string preferred)
        {
            if (comboBox.Items.Count == 0)
            {
                return;
            }

            string current = comboBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(current) && comboBox.Items.Contains(current))
            {
                comboBox.SelectedItem = current;
                return;
            }

            string match = comboBox.Items.Cast<string>()
                .FirstOrDefault(x => string.Equals(x, preferred, StringComparison.OrdinalIgnoreCase));
            comboBox.SelectedItem = match ?? comboBox.Items[0];
        }

        private IList<string> GetAvailableCategoryNames()
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            names.Add("All Categories");

            foreach (Element element in SafeCollectElements(_document, UseActiveViewScope(), 5000))
            {
                if (IsUsableModelElement(element))
                {
                    names.Add(element.Category.Name);
                }
            }

            if (_includeLinkedCheck == null || _includeLinkedCheck.IsChecked == true)
            {
                foreach (RevitLinkInstance link in new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                {
                    Document linkDocument = link.GetLinkDocument();
                    if (linkDocument == null)
                    {
                        continue;
                    }

                    foreach (Element element in SafeCollectElements(linkDocument, false, 2500))
                    {
                        if (IsUsableModelElement(element))
                        {
                            names.Add(element.Category.Name);
                        }
                    }
                }
            }

            return names.ToList();
        }

        private bool UseActiveViewScope()
        {
            return _activeViewOnlyCheck == null || _activeViewOnlyCheck.IsChecked == true;
        }

        private void RetrieveLeft(bool append)
        {
            string category = GetSelectedCategory(_leftCategoryBox);
            bool fromSelection = _selectionFromRevitRadio.IsChecked == true;
            IList<SolidsElementProxy> proxies = CollectElementProxies(category, fromSelection, _includeLinkedCheck.IsChecked == true, MaxRetrievedElements);

            if (!append)
            {
                _leftRows.Clear();
            }

            int added = AddElementRows(proxies);
            RefreshLeftGrid();
            ClearInteractionRows();

            if (_highlightOnRetrieveCheck.IsChecked == true)
            {
                SelectLeftChecked();
            }

            Notify((append ? "Added " : "Retrieved ") + added + " selected element(s). Source: " +
                   (fromSelection ? "Revit selection" : category) + ".");
        }

        private int AddElementRows(IEnumerable<SolidsElementProxy> proxies)
        {
            var existing = new HashSet<string>(_leftRows.Select(x => x.Proxy.Key), StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (SolidsElementProxy proxy in proxies)
            {
                if (proxy == null || !existing.Add(proxy.Key))
                {
                    continue;
                }

                _leftRows.Add(new SolidsElementRow(proxy));
                added++;
            }

            return added;
        }

        private void CheckInteractions()
        {
            CommitGridEdits();
            IList<SolidsElementProxy> selected = _leftRows.Where(x => x.IsChecked).Select(x => x.Proxy).ToList();
            if (selected.Count == 0)
            {
                Notify("Check cancelled. Retrieve and check at least one selected element first.");
                return;
            }

            string mode = _checkModeBox.SelectedItem as string ?? "Intersections";
            string category = GetSelectedCategory(_rightCategoryBox);
            IList<SolidsElementProxy> candidates = CollectElementProxies(category, false, _includeLinkedCheck.IsChecked == true, MaxInteractionElements);

            if (_limitToSavedSetCheck.IsChecked == true)
            {
                ISet<string> allowed = LoadSavedSetKeys(GetSelectedSlot());
                candidates = candidates.Where(x => allowed.Contains(x.Key)).ToList();
            }

            if (_stackResultsCheck.IsChecked != true)
            {
                _pairRows.Clear();
            }

            var existing = new HashSet<string>(_pairRows.Select(x => x.PairKey), StringComparer.OrdinalIgnoreCase);
            int checkedPairs = 0;
            int added = 0;
            bool includeAdjacent = _includeAdjacentCheck.IsChecked == true;
            double tolerance = MhnkArcToolSettings.Load().ClashToleranceMillimeters / 304.8;
            if (includeAdjacent)
            {
                tolerance = Math.Max(tolerance, 25.0 / 304.8);
            }

            foreach (SolidsElementProxy first in selected)
            {
                foreach (SolidsElementProxy second in candidates)
                {
                    if (first == null || second == null || IsSameProxy(first, second))
                    {
                        continue;
                    }

                    string pairKey = GetOrderedPairKey(first.Key, second.Key);
                    if (existing.Contains(pairKey))
                    {
                        continue;
                    }

                    checkedPairs++;
                    if (checkedPairs > MaxPairEvaluations || added >= MaxPairs)
                    {
                        break;
                    }

                    SolidsPairResult result = EvaluatePair(first, second, mode, includeAdjacent, tolerance);
                    if (!result.IsMatch)
                    {
                        continue;
                    }

                    _pairRows.Add(new SolidsPairRow(first, second, mode, result.Status, pairKey, result.CutDirection));
                    existing.Add(pairKey);
                    added++;
                }

                if (checkedPairs > MaxPairEvaluations || added >= MaxPairs)
                {
                    break;
                }
            }

            _lastCheckMode = mode;
            RefreshRightGrid();
            UpdateOperationButtons();

            if (_highlightOnCheckCheck.IsChecked == true)
            {
                SelectPairChecked();
            }

            Notify("Check complete: " + mode + ". Added " + added + " interaction row(s), checked up to " + checkedPairs + " candidate pair(s).");
        }

        private SolidsPairResult EvaluatePair(SolidsElementProxy first, SolidsElementProxy second, string mode, bool includeAdjacent, double tolerance)
        {
            if (string.Equals(mode, "Intersections", StringComparison.OrdinalIgnoreCase))
            {
                if (!BoxesInteract(first, second, tolerance))
                {
                    return SolidsPairResult.NoMatch;
                }

                if (SolidsIntersect(first, second))
                {
                    return new SolidsPairResult(true, "Intersection", 0);
                }

                return includeAdjacent ? new SolidsPairResult(true, "Adjacent candidate", 0) : SolidsPairResult.NoMatch;
            }

            if (first.IsLinked || second.IsLinked)
            {
                return SolidsPairResult.NoMatch;
            }

            if (string.Equals(mode, "Joined", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return JoinGeometryUtils.AreElementsJoined(_document, first.Element, second.Element)
                        ? new SolidsPairResult(true, "Joined", 0)
                        : SolidsPairResult.NoMatch;
                }
                catch
                {
                    return SolidsPairResult.NoMatch;
                }
            }

            if (string.Equals(mode, "Can Cut", StringComparison.OrdinalIgnoreCase))
            {
                int direction = GetCutDirection(first.Element, second.Element);
                if (direction == 0)
                {
                    return SolidsPairResult.NoMatch;
                }

                return new SolidsPairResult(true, direction > 0 ? "Can cut selected -> interacting" : "Can cut interacting -> selected", direction);
            }

            if (string.Equals(mode, "Cuts", StringComparison.OrdinalIgnoreCase))
            {
                bool firstCutsSecond;
                try
                {
                    if (!SolidSolidCutUtils.CutExistsBetweenElements(first.Element, second.Element, out firstCutsSecond))
                    {
                        return SolidsPairResult.NoMatch;
                    }

                    return new SolidsPairResult(true, firstCutsSecond ? "Cut selected -> interacting" : "Cut interacting -> selected", firstCutsSecond ? 1 : -1);
                }
                catch
                {
                    return SolidsPairResult.NoMatch;
                }
            }

            return SolidsPairResult.NoMatch;
        }

        private int GetCutDirection(Element first, Element second)
        {
            try
            {
                CutFailureReason reason;
                if (SolidSolidCutUtils.CanElementCutElement(first, second, out reason))
                {
                    return 1;
                }

                if (SolidSolidCutUtils.CanElementCutElement(second, first, out reason))
                {
                    return -1;
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }

        private void RunOperation(string operation)
        {
            CommitGridEdits();
            IList<SolidsPairRow> rows = _pairRows.Where(x => x.IsChecked).ToList();
            if (rows.Count == 0)
            {
                Notify("Operation cancelled. Check at least one interaction row first.");
                return;
            }

            if (!IsOperationAllowedAfterCheck(operation))
            {
                Notify(operation + " requires a matching pre-check. Intersections -> Join, Joined -> Unjoin/Switch, Can Cut -> Cut, Cuts -> Uncut.");
                return;
            }

            int changed = 0;
            int skipped = 0;
            int linkedSkipped = 0;
            int framingEnds = 0;

            using (Transaction transaction = new Transaction(_document, "MHNK - Solids " + operation))
            {
                transaction.Start();
                foreach (SolidsPairRow row in rows)
                {
                    if (row.First.IsLinked || row.Second.IsLinked)
                    {
                        linkedSkipped++;
                        continue;
                    }

                    try
                    {
                        bool didChange = RunHostOperation(row, operation);
                        if (didChange)
                        {
                            changed++;
                        }
                        else
                        {
                            skipped++;
                        }

                        if (_framingEndJoinCheck.IsChecked == true &&
                            (string.Equals(operation, "Join", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(operation, "Unjoin", StringComparison.OrdinalIgnoreCase)))
                        {
                            framingEnds += ApplyStructuralFramingEndJoinModifier(row, string.Equals(operation, "Join", StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                transaction.Commit();
            }

            _uiDocument.RefreshActiveView();
            Notify(operation + " complete. Changed: " + changed + ". Skipped: " + skipped +
                   ". Linked skipped: " + linkedSkipped + ". Framing end updates: " + framingEnds + ".");
        }

        private bool RunHostOperation(SolidsPairRow row, string operation)
        {
            Element first = row.First.Element;
            Element second = row.Second.Element;

            if (string.Equals(operation, "Join", StringComparison.OrdinalIgnoreCase))
            {
                if (JoinGeometryUtils.AreElementsJoined(_document, first, second))
                {
                    return false;
                }

                JoinGeometryUtils.JoinGeometry(_document, first, second);
                return true;
            }

            if (string.Equals(operation, "Unjoin", StringComparison.OrdinalIgnoreCase))
            {
                if (!JoinGeometryUtils.AreElementsJoined(_document, first, second))
                {
                    return false;
                }

                JoinGeometryUtils.UnjoinGeometry(_document, first, second);
                return true;
            }

            if (string.Equals(operation, "Switch", StringComparison.OrdinalIgnoreCase))
            {
                if (!JoinGeometryUtils.AreElementsJoined(_document, first, second))
                {
                    return false;
                }

                JoinGeometryUtils.SwitchJoinOrder(_document, first, second);
                return true;
            }

            if (string.Equals(operation, "Cut", StringComparison.OrdinalIgnoreCase))
            {
                bool firstCutsSecond;
                if (SolidSolidCutUtils.CutExistsBetweenElements(first, second, out firstCutsSecond))
                {
                    return false;
                }

                int direction = row.CutDirection == 0 ? GetCutDirection(first, second) : row.CutDirection;
                if (direction > 0)
                {
                    SolidSolidCutUtils.AddCutBetweenSolids(_document, first, second);
                    return true;
                }

                if (direction < 0)
                {
                    SolidSolidCutUtils.AddCutBetweenSolids(_document, second, first);
                    return true;
                }

                return false;
            }

            if (string.Equals(operation, "Uncut", StringComparison.OrdinalIgnoreCase))
            {
                bool firstCutsSecond;
                if (!SolidSolidCutUtils.CutExistsBetweenElements(first, second, out firstCutsSecond))
                {
                    return false;
                }

                SolidSolidCutUtils.RemoveCutBetweenSolids(_document, first, second);
                return true;
            }

            return false;
        }

        private int ApplyStructuralFramingEndJoinModifier(SolidsPairRow row, bool allow)
        {
            int changed = 0;
            changed += ApplyStructuralFramingEndJoinModifier(row.First.Element as FamilyInstance, allow);
            changed += ApplyStructuralFramingEndJoinModifier(row.Second.Element as FamilyInstance, allow);
            return changed;
        }

        private static int ApplyStructuralFramingEndJoinModifier(FamilyInstance instance, bool allow)
        {
            if (instance == null || instance.Category == null ||
                !string.Equals(instance.Category.Name, "Structural Framing", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            int changed = 0;
            for (int end = 0; end < 2; end++)
            {
                try
                {
                    bool isAllowed = StructuralFramingUtils.IsJoinAllowedAtEnd(instance, end);
                    if (isAllowed == allow)
                    {
                        continue;
                    }

                    if (allow)
                    {
                        StructuralFramingUtils.AllowJoinAtEnd(instance, end);
                    }
                    else
                    {
                        StructuralFramingUtils.DisallowJoinAtEnd(instance, end);
                    }

                    changed++;
                }
                catch
                {
                    // Some framing families do not expose both ends consistently.
                }
            }

            return changed;
        }

        private bool IsOperationAllowedAfterCheck(string operation)
        {
            return (string.Equals(operation, "Join", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_lastCheckMode, "Intersections", StringComparison.OrdinalIgnoreCase)) ||
                   ((string.Equals(operation, "Unjoin", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(operation, "Switch", StringComparison.OrdinalIgnoreCase)) &&
                    string.Equals(_lastCheckMode, "Joined", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(operation, "Cut", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_lastCheckMode, "Can Cut", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(operation, "Uncut", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_lastCheckMode, "Cuts", StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateOperationButtons()
        {
            _joinButton.IsEnabled = string.Equals(_lastCheckMode, "Intersections", StringComparison.OrdinalIgnoreCase);
            _unjoinButton.IsEnabled = string.Equals(_lastCheckMode, "Joined", StringComparison.OrdinalIgnoreCase);
            _switchButton.IsEnabled = string.Equals(_lastCheckMode, "Joined", StringComparison.OrdinalIgnoreCase);
            _cutButton.IsEnabled = string.Equals(_lastCheckMode, "Can Cut", StringComparison.OrdinalIgnoreCase);
            _uncutButton.IsEnabled = string.Equals(_lastCheckMode, "Cuts", StringComparison.OrdinalIgnoreCase);
        }

        private IList<SolidsElementProxy> CollectElementProxies(string category, bool fromSelection, bool includeLinks, int maxElements)
        {
            var result = new List<SolidsElementProxy>();
            if (fromSelection)
            {
                foreach (ElementId id in _uiDocument.Selection.GetElementIds())
                {
                    Element element = _document.GetElement(id);
                    if (IsElementInCategory(element, category))
                    {
                        result.Add(SolidsElementProxy.CreateHost(_document, element));
                    }

                    if (result.Count >= maxElements)
                    {
                        return result;
                    }
                }

                return result;
            }

            foreach (Element element in SafeCollectElements(_document, UseActiveViewScope(), int.MaxValue))
            {
                if (IsElementInCategory(element, category))
                {
                    result.Add(SolidsElementProxy.CreateHost(_document, element));
                }

                if (result.Count >= maxElements)
                {
                    return result;
                }
            }

            if (!includeLinks)
            {
                return result;
            }

            foreach (RevitLinkInstance link in new FilteredElementCollector(_document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            {
                Document linkDocument = link.GetLinkDocument();
                if (linkDocument == null)
                {
                    continue;
                }

                foreach (Element element in SafeCollectElements(linkDocument, false, int.MaxValue))
                {
                    if (IsElementInCategory(element, category))
                    {
                        result.Add(SolidsElementProxy.CreateLinked(linkDocument, element, link));
                    }

                    if (result.Count >= maxElements)
                    {
                        return result;
                    }
                }
            }

            return result;
        }

        private IEnumerable<Element> SafeCollectElements(Document document, bool activeViewOnly, int maxElements)
        {
            IEnumerable<Element> elements;
            try
            {
                elements = activeViewOnly && document.Equals(_document)
                    ? new FilteredElementCollector(document, _context.ActiveView.Id).WhereElementIsNotElementType().ToElements()
                    : new FilteredElementCollector(document).WhereElementIsNotElementType().ToElements();
            }
            catch
            {
                elements = new FilteredElementCollector(document).WhereElementIsNotElementType().ToElements();
            }

            return elements.Where(IsUsableModelElement).Take(Math.Max(1, maxElements));
        }

        private static bool IsUsableModelElement(Element element)
        {
            return element != null &&
                   element.Category != null &&
                   element.Category.CategoryType == CategoryType.Model &&
                   !(element is ElementType) &&
                   element.get_BoundingBox(null) != null;
        }

        private static bool IsElementInCategory(Element element, string category)
        {
            return IsUsableModelElement(element) &&
                   (string.IsNullOrWhiteSpace(category) ||
                    string.Equals(category, "All Categories", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Category.Name, category, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetSelectedCategory(ComboBox comboBox)
        {
            return comboBox.SelectedItem as string ?? "All Categories";
        }

        private static bool BoxesInteract(SolidsElementProxy first, SolidsElementProxy second, double tolerance)
        {
            BoundingBoxXYZ firstBox = first.GetWorldBoundingBox();
            BoundingBoxXYZ secondBox = second.GetWorldBoundingBox();
            if (firstBox == null || secondBox == null)
            {
                return false;
            }

            return BoundingBoxesIntersect(ExpandBoundingBox(firstBox, tolerance), ExpandBoundingBox(secondBox, tolerance));
        }

        private static bool SolidsIntersect(SolidsElementProxy first, SolidsElementProxy second)
        {
            IList<Solid> firstSolids = first.GetWorldSolids();
            IList<Solid> secondSolids = second.GetWorldSolids();
            if (firstSolids.Count == 0 || secondSolids.Count == 0)
            {
                return false;
            }

            foreach (Solid firstSolid in firstSolids)
            {
                foreach (Solid secondSolid in secondSolids)
                {
                    try
                    {
                        Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(firstSolid, secondSolid, BooleanOperationsType.Intersect);
                        if (intersection != null && intersection.Volume > 1e-9)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Boolean checks can fail on malformed family geometry; skip that solid pair.
                    }
                }
            }

            return false;
        }

        private static bool BoundingBoxesIntersect(BoundingBoxXYZ first, BoundingBoxXYZ second)
        {
            return first.Min.X <= second.Max.X && first.Max.X >= second.Min.X &&
                   first.Min.Y <= second.Max.Y && first.Max.Y >= second.Min.Y &&
                   first.Min.Z <= second.Max.Z && first.Max.Z >= second.Min.Z;
        }

        private static BoundingBoxXYZ ExpandBoundingBox(BoundingBoxXYZ box, double amount)
        {
            if (box == null || amount <= 0)
            {
                return box;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(box.Min.X - amount, box.Min.Y - amount, box.Min.Z - amount),
                Max = new XYZ(box.Max.X + amount, box.Max.Y + amount, box.Max.Z + amount)
            };
        }

        private void RefreshLeftGrid()
        {
            string query = (_leftFilterBox.Text ?? "").Trim();
            _leftGrid.ItemsSource = _leftRows.Where(x => MatchesElementQuery(x, query)).ToList();
        }

        private void RefreshRightGrid()
        {
            string query = (_rightFilterBox.Text ?? "").Trim();
            _rightGrid.ItemsSource = _pairRows.Where(x => MatchesPairQuery(x, query)).ToList();
        }

        private static bool MatchesElementQuery(SolidsElementRow row, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return Contains(row.IdText, query) ||
                   Contains(row.Category, query) ||
                   Contains(row.FamilyType, query) ||
                   Contains(row.Source, query) ||
                   Contains(row.Name, query);
        }

        private static bool MatchesPairQuery(SolidsPairRow row, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return Contains(row.FirstLabel, query) ||
                   Contains(row.SecondLabel, query) ||
                   Contains(row.Status, query) ||
                   Contains(row.Mode, query);
        }

        private static bool Contains(string value, string query)
        {
            return (value ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetLeftChecks(bool isChecked)
        {
            foreach (SolidsElementRow row in _leftRows)
            {
                row.IsChecked = isChecked;
            }

            RefreshLeftGrid();
            Notify((isChecked ? "Checked" : "Unchecked") + " all selected element rows.");
        }

        private void SetPairChecks(bool isChecked)
        {
            foreach (SolidsPairRow row in _pairRows)
            {
                row.IsChecked = isChecked;
            }

            RefreshRightGrid();
            Notify((isChecked ? "Checked" : "Unchecked") + " all interaction rows.");
        }

        private void ClearInteractionRows()
        {
            _pairRows.Clear();
            _lastCheckMode = "";
            RefreshRightGrid();
            UpdateOperationButtons();
        }

        private void ClearLeftList()
        {
            _leftRows.Clear();
            RefreshLeftGrid();
            ClearInteractionRows();
            Notify("Selected element list cleared.");
        }

        private void ResetApplication()
        {
            ClearLeftList();
            _rightFilterBox.Text = "";
            _leftFilterBox.Text = "";
            ResetTemporaryView();
            Notify("Solids Interaction Center reset.");
        }

        private void SaveSelectionSet()
        {
            CommitGridEdits();
            IList<SolidsSavedRef> refs = _leftRows.Where(x => x.IsChecked).Select(x => x.Proxy.ToSavedRef()).ToList();
            if (refs.Count == 0)
            {
                Notify("No checked selected elements to save.");
                return;
            }

            Directory.CreateDirectory(GetSolidsFolder());
            File.WriteAllText(GetSelectionSetPath(GetSelectedSlot()), CamboBimJson.Serialize(refs));
            Notify("Saved selection set " + GetSelectedSlot() + " with " + refs.Count + " element reference(s).");
        }

        private void LoadSelectionSet()
        {
            string path = GetSelectionSetPath(GetSelectedSlot());
            if (!File.Exists(path))
            {
                Notify("Selection set " + GetSelectedSlot() + " is empty.");
                return;
            }

            IList<SolidsSavedRef> refs = CamboBimJson.Deserialize<List<SolidsSavedRef>>(File.ReadAllText(path)) ?? new List<SolidsSavedRef>();
            _leftRows.Clear();
            int added = AddElementRows(refs.Select(ResolveSavedRef).Where(x => x != null));
            RefreshLeftGrid();
            ClearInteractionRows();
            Notify("Loaded selection set " + GetSelectedSlot() + " with " + added + " available element(s).");
        }

        private void DeleteSelectionSet()
        {
            string path = GetSelectionSetPath(GetSelectedSlot());
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            Notify("Deleted selection set " + GetSelectedSlot() + ".");
        }

        private ISet<string> LoadSavedSetKeys(int slot)
        {
            string path = GetSelectionSetPath(slot);
            if (!File.Exists(path))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            IList<SolidsSavedRef> refs = CamboBimJson.Deserialize<List<SolidsSavedRef>>(File.ReadAllText(path)) ?? new List<SolidsSavedRef>();
            return new HashSet<string>(refs.Select(GetSavedRefKey), StringComparer.OrdinalIgnoreCase);
        }

        private SolidsElementProxy ResolveSavedRef(SolidsSavedRef savedRef)
        {
            if (savedRef == null)
            {
                return null;
            }

            if (!savedRef.IsLinked)
            {
                Element element = _document.GetElement(new ElementId(savedRef.ElementId));
                return element == null ? null : SolidsElementProxy.CreateHost(_document, element);
            }

            RevitLinkInstance link = _document.GetElement(new ElementId(savedRef.LinkInstanceId)) as RevitLinkInstance;
            Document linkDocument = link?.GetLinkDocument();
            Element linkedElement = linkDocument?.GetElement(new ElementId(savedRef.ElementId));
            return linkedElement == null ? null : SolidsElementProxy.CreateLinked(linkDocument, linkedElement, link);
        }

        private static string GetSavedRefKey(SolidsSavedRef savedRef)
        {
            if (savedRef == null)
            {
                return "";
            }

            return savedRef.IsLinked
                ? "L|" + savedRef.LinkInstanceId + "|" + savedRef.ElementId
                : "H|" + savedRef.ElementId;
        }

        private int GetSelectedSlot()
        {
            string selected = _savedSlotBox.SelectedItem as string ?? "1";
            int slot;
            return int.TryParse(selected, out slot) ? Math.Max(1, Math.Min(9, slot)) : 1;
        }

        private static string GetSolidsFolder()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "ArcTools", "Solids");
        }

        private static string GetSelectionSetPath(int slot)
        {
            return Path.Combine(GetSolidsFolder(), "selection_set_" + slot + ".json");
        }

        private void SelectCheckedPairsOrLeft()
        {
            if (_pairRows.Any(x => x.IsChecked))
            {
                SelectPairChecked();
            }
            else
            {
                SelectLeftChecked();
            }
        }

        private void IsolateCheckedPairsOrLeft()
        {
            if (_pairRows.Any(x => x.IsChecked))
            {
                IsolatePairChecked();
            }
            else
            {
                IsolateLeftChecked();
            }
        }

        private void HideCheckedPairsOrLeft()
        {
            if (_pairRows.Any(x => x.IsChecked))
            {
                HidePairChecked();
            }
            else
            {
                HideLeftChecked();
            }
        }

        private void SelectLeftChecked()
        {
            SetRevitSelection(GetHostIds(_leftRows.Where(x => x.IsChecked).Select(x => x.Proxy)));
        }

        private void IsolateLeftChecked()
        {
            IsolateIds(GetHostIds(_leftRows.Where(x => x.IsChecked).Select(x => x.Proxy)));
        }

        private void HideLeftChecked()
        {
            HideIds(GetHostIds(_leftRows.Where(x => x.IsChecked).Select(x => x.Proxy)));
        }

        private void SelectPairChecked()
        {
            SetRevitSelection(GetHostIds(_pairRows.Where(x => x.IsChecked).SelectMany(x => new[] { x.First, x.Second })));
        }

        private void IsolatePairChecked()
        {
            IsolateIds(GetHostIds(_pairRows.Where(x => x.IsChecked).SelectMany(x => new[] { x.First, x.Second })));
        }

        private void HidePairChecked()
        {
            HideIds(GetHostIds(_pairRows.Where(x => x.IsChecked).SelectMany(x => new[] { x.First, x.Second })));
        }

        private static IList<ElementId> GetHostIds(IEnumerable<SolidsElementProxy> proxies)
        {
            return proxies
                .Where(x => x != null && !x.IsLinked)
                .Select(x => x.Element.Id)
                .Distinct(new ElementIdComparer())
                .ToList();
        }

        private void SetRevitSelection(ICollection<ElementId> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                Notify("No host-model elements are available for Revit selection. Linked rows are check/report only.");
                return;
            }

            _uiDocument.Selection.SetElementIds(ids);
            _uiDocument.RefreshActiveView();
            Notify("Selected " + ids.Count + " host-model element(s) in Revit.");
        }

        private void IsolateIds(ICollection<ElementId> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                Notify("No host-model elements are available to isolate.");
                return;
            }

            using (Transaction transaction = new Transaction(_document, "MHNK - Solids Isolate Checked"))
            {
                transaction.Start();
                TryDisableTemporaryHideIsolate(_context.ActiveView);
                _context.ActiveView.IsolateElementsTemporary(ids);
                transaction.Commit();
            }

            _uiDocument.RefreshActiveView();
            Notify("Temporarily isolated " + ids.Count + " host-model element(s).");
        }

        private void HideIds(ICollection<ElementId> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                Notify("No host-model elements are available to hide.");
                return;
            }

            using (Transaction transaction = new Transaction(_document, "MHNK - Solids Hide Checked"))
            {
                transaction.Start();
                _context.ActiveView.HideElementsTemporary(ids);
                transaction.Commit();
            }

            _uiDocument.RefreshActiveView();
            Notify("Temporarily hidden " + ids.Count + " host-model element(s).");
        }

        private void ResetTemporaryView()
        {
            using (Transaction transaction = new Transaction(_document, "MHNK - Solids Reset Temporary View"))
            {
                transaction.Start();
                TryDisableTemporaryHideIsolate(_context.ActiveView);
                transaction.Commit();
            }

            _uiDocument.RefreshActiveView();
            Notify("Temporary hide/isolate reset.");
        }

        private static void TryDisableTemporaryHideIsolate(Autodesk.Revit.DB.View view)
        {
            try
            {
                if (view != null && view.IsTemporaryHideIsolateActive())
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                }
            }
            catch
            {
                // Some view types do not support temporary hide/isolate.
            }
        }

        private void ExportCsv()
        {
            CommitGridEdits();
            if (_pairRows.Count == 0)
            {
                Notify("No interaction rows to export.");
                return;
            }

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MHNK", "Reports");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "MHNK_Solids_Interactions_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");

            var csv = new StringBuilder();
            csv.AppendLine("Mode,Status,SelectedId,SelectedCategory,SelectedType,SelectedSource,InteractingId,InteractingCategory,InteractingType,InteractingSource");
            foreach (SolidsPairRow row in _pairRows)
            {
                csv.AppendLine(string.Join(",",
                    Csv(row.Mode),
                    Csv(row.Status),
                    Csv(row.First.IdText),
                    Csv(row.First.Category),
                    Csv(row.First.FamilyType),
                    Csv(row.First.Source),
                    Csv(row.Second.IdText),
                    Csv(row.Second.Category),
                    Csv(row.Second.FamilyType),
                    Csv(row.Second.Source)));
            }

            File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            Notify("Exported CSV: " + path);
        }

        private static string Csv(string value)
        {
            string text = value ?? "";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private void ShowVideoChecklist()
        {
            MessageBox.Show(
                this,
                "Implemented from eTLipse SOLIDS videos 1, 2, and 4:\n\n" +
                "- Retrieve selected elements by category or current Revit selection.\n" +
                "- Check intersections, joined pairs, can-cut pairs, and existing cuts.\n" +
                "- Enable Join, Unjoin, Switch, Cut, and Uncut only after the matching check.\n" +
                "- Check/uncheck rows, select/isolate/hide/reset view, save/load sets, stack multi-category results, and export CSV.\n" +
                "- Include adjacent-element checks, structural-framing end-join modifier, dark theme support, and linked-model check rows.\n\n" +
                "Linked model rows are available for check/report workflows. Revit only allows geometry edits on host-model elements.",
                "MHNK Solids Video Checklist",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Notify(string message)
        {
            _statusText.Text = message ?? "";
        }

        private void CommitGridEdits()
        {
            _leftGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _leftGrid.CommitEdit(DataGridEditingUnit.Row, true);
            _rightGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            _rightGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private static bool IsSameProxy(SolidsElementProxy first, SolidsElementProxy second)
        {
            return first != null && second != null && string.Equals(first.Key, second.Key, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetOrderedPairKey(string first, string second)
        {
            return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        }

        private sealed class SolidsElementProxy
        {
            private BoundingBoxXYZ _worldBoundingBox;
            private IList<Solid> _worldSolids;

            private SolidsElementProxy(Document document, Element element, RevitLinkInstance linkInstance)
            {
                Document = document;
                Element = element;
                LinkInstance = linkInstance;
                IsLinked = linkInstance != null;
                Key = IsLinked
                    ? "L|" + linkInstance.Id.Value + "|" + element.Id.Value
                    : "H|" + element.Id.Value;
            }

            public Document Document { get; }
            public Element Element { get; }
            public RevitLinkInstance LinkInstance { get; }
            public bool IsLinked { get; }
            public string Key { get; }
            public string IdText => Element.Id.Value.ToString();
            public string Category => Element.Category?.Name ?? "";
            public string FamilyType => GetFamilyType(Element);
            public string Source => IsLinked ? "Linked" : "Host";

            public static SolidsElementProxy CreateHost(Document document, Element element)
            {
                return new SolidsElementProxy(document, element, null);
            }

            public static SolidsElementProxy CreateLinked(Document document, Element element, RevitLinkInstance linkInstance)
            {
                return new SolidsElementProxy(document, element, linkInstance);
            }

            public BoundingBoxXYZ GetWorldBoundingBox()
            {
                if (_worldBoundingBox != null)
                {
                    return _worldBoundingBox;
                }

                BoundingBoxXYZ box = Element?.get_BoundingBox(null);
                if (box == null)
                {
                    return null;
                }

                Transform transform = IsLinked ? LinkInstance.GetTotalTransform() : Transform.Identity;
                _worldBoundingBox = TransformBoundingBox(box, transform);
                return _worldBoundingBox;
            }

            public IList<Solid> GetWorldSolids()
            {
                if (_worldSolids != null)
                {
                    return _worldSolids;
                }

                var solids = new List<Solid>();
                Options options = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };

                CollectSolids(Element?.get_Geometry(options), solids);
                if (IsLinked)
                {
                    Transform transform = LinkInstance.GetTotalTransform();
                    _worldSolids = solids.Select(x => SolidUtils.CreateTransformed(x, transform)).ToList();
                }
                else
                {
                    _worldSolids = solids;
                }

                return _worldSolids;
            }

            public SolidsSavedRef ToSavedRef()
            {
                return new SolidsSavedRef
                {
                    IsLinked = IsLinked,
                    ElementId = Element.Id.Value,
                    LinkInstanceId = IsLinked ? LinkInstance.Id.Value : 0
                };
            }

            private static BoundingBoxXYZ TransformBoundingBox(BoundingBoxXYZ box, Transform transform)
            {
                XYZ[] points =
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                };

                IList<XYZ> transformed = points.Select(transform.OfPoint).ToList();
                return new BoundingBoxXYZ
                {
                    Min = new XYZ(transformed.Min(p => p.X), transformed.Min(p => p.Y), transformed.Min(p => p.Z)),
                    Max = new XYZ(transformed.Max(p => p.X), transformed.Max(p => p.Y), transformed.Max(p => p.Z))
                };
            }

            private static void CollectSolids(GeometryElement geometry, IList<Solid> solids)
            {
                if (geometry == null)
                {
                    return;
                }

                foreach (GeometryObject geometryObject in geometry)
                {
                    Solid solid = geometryObject as Solid;
                    if (solid != null && solid.Volume > 1e-9)
                    {
                        solids.Add(solid);
                        continue;
                    }

                    GeometryInstance instance = geometryObject as GeometryInstance;
                    if (instance != null)
                    {
                        CollectSolids(instance.GetInstanceGeometry(), solids);
                    }
                }
            }
        }

        private sealed class SolidsElementRow
        {
            public SolidsElementRow(SolidsElementProxy proxy)
            {
                Proxy = proxy;
                IsChecked = true;
            }

            public bool IsChecked { get; set; }
            public SolidsElementProxy Proxy { get; }
            public string IdText => Proxy.Element.Id.Value.ToString();
            public string Category => Proxy.Element.Category?.Name ?? "";
            public string Name => Proxy.Element.Name ?? "";
            public string FamilyType => GetFamilyType(Proxy.Element);
            public string Source => Proxy.IsLinked ? "Linked" : "Host";
        }

        private sealed class SolidsPairRow
        {
            public SolidsPairRow(SolidsElementProxy first, SolidsElementProxy second, string mode, string status, string pairKey, int cutDirection)
            {
                First = first;
                Second = second;
                Mode = mode;
                Status = status;
                PairKey = pairKey;
                CutDirection = cutDirection;
                IsChecked = true;
            }

            public bool IsChecked { get; set; }
            public SolidsElementProxy First { get; }
            public SolidsElementProxy Second { get; }
            public string Mode { get; }
            public string Status { get; }
            public string PairKey { get; }
            public int CutDirection { get; }
            public string FirstLabel => BuildLabel(First);
            public string SecondLabel => BuildLabel(Second);
        }

        private sealed class SolidsPairResult
        {
            public static readonly SolidsPairResult NoMatch = new SolidsPairResult(false, "", 0);

            public SolidsPairResult(bool isMatch, string status, int cutDirection)
            {
                IsMatch = isMatch;
                Status = status;
                CutDirection = cutDirection;
            }

            public bool IsMatch { get; }
            public string Status { get; }
            public int CutDirection { get; }
        }

        private sealed class SolidsSavedRef
        {
            public bool IsLinked { get; set; }
            public long ElementId { get; set; }
            public long LinkInstanceId { get; set; }
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                return GetElementIdText(x) == GetElementIdText(y);
            }

            public int GetHashCode(ElementId obj)
            {
                return StringComparer.OrdinalIgnoreCase.GetHashCode(GetElementIdText(obj));
            }
        }

        private static string BuildLabel(SolidsElementProxy proxy)
        {
            if (proxy == null || proxy.Element == null)
            {
                return "";
            }

            string source = proxy.IsLinked ? "Linked" : "Host";
            return proxy.Element.Id.Value + " | " + (proxy.Element.Category?.Name ?? "") + " | " + GetFamilyType(proxy.Element) + " | " + source;
        }

        private static string GetFamilyType(Element element)
        {
            if (element == null)
            {
                return "";
            }

            Element type = element.Document.GetElement(element.GetTypeId());
            if (type != null && !string.IsNullOrWhiteSpace(type.Name))
            {
                return type.Name;
            }

            return string.IsNullOrWhiteSpace(element.Name) ? element.GetType().Name : element.Name;
        }

        private static string GetElementIdText(ElementId id)
        {
            return id == null ? "" : id.Value.ToString();
        }
    }
}
