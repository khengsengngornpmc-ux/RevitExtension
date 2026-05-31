using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using WpfColor = System.Windows.Media.Color;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {
        private sealed class BeamNaviateAdditionalRowUi
        {
            public string SpanInfoText { get; set; } = "1";
            public string LayerText { get; set; } = "2";
            public string BarTypeName { get; set; } = "DB12";
            public string AmountText { get; set; } = "2";
            public string StartGridText { get; set; } = "1";
            public string EndGridText { get; set; } = "1";
            public string StartLengthText { get; set; } = "600 mm";
            public string EndLengthText { get; set; } = "600 mm";
            public string StartFactorText { get; set; } = "";
            public string EndFactorText { get; set; } = "";
        }

        private sealed class BeamPreviewSupportUi
        {
            public double StationMm { get; set; }
            public double WidthMm { get; set; }
            public string KindKey { get; set; } = string.Empty;
        }

        private sealed class BeamPreviewSpanUi
        {
            public int SpanIndex { get; set; }
            public double StartStationMm { get; set; }
            public double EndStationMm { get; set; }
            public double StartX { get; set; }
            public double EndX { get; set; }
        }

        private sealed class BeamNaviateStirrupSpanOverrideUi
        {
            public int SpanIndex { get; set; }
            public string StartSpacingText { get; set; } = "150 mm";
            public string MiddleSpacingText { get; set; } = "200 mm";
            public string EndSpacingText { get; set; } = "150 mm";
            public string StartZoneText { get; set; } = "0 mm";
            public string EndZoneText { get; set; } = "0 mm";
        }

        private sealed class BeamNaviateStirrupRowUi
        {
            public int SpanIndex { get; set; }
            public string SpanInfoText { get; set; } = "1";
            public string StartSpacingText { get; set; } = "150 mm";
            public string MiddleSpacingText { get; set; } = "200 mm";
            public string EndSpacingText { get; set; } = "150 mm";
            public string StartGridText { get; set; } = "0";
            public string EndGridText { get; set; } = "1";
            public string StartZoneText { get; set; } = "0 mm";
            public string EndZoneText { get; set; } = "0 mm";
            public string StartFactorText { get; set; } = "0";
            public string EndFactorText { get; set; } = "0";
        }

        private sealed class BeamNaviateSpecialRowUi
        {
            public string SectionText { get; set; } = "1";
            public string CageText { get; set; } = "1";
            public string ModeText { get; set; } = "Tie Stirrup";
            public string SpacingText { get; set; } = "150 mm";
            public string StartZoneText { get; set; } = "800 mm";
            public string EndZoneText { get; set; } = "800 mm";
        }

        private sealed class BeamNaviateSpecialSectionLineTieUiSpec
        {
            public int SectionIndex { get; set; } = 1;
            public int X0GridIndex { get; set; }
            public int Y0GridIndex { get; set; }
            public int X1GridIndex { get; set; }
            public int Y1GridIndex { get; set; }
        }

        private sealed class BeamNaviateSpecialSectionRectTieUiSpec
        {
            public int SectionIndex { get; set; } = 1;
            public int X0GridIndex { get; set; }
            public int Y0GridIndex { get; set; }
            public int X1GridIndex { get; set; }
            public int Y1GridIndex { get; set; }
        }

        private enum BeamNaviateSpecialSectionEditMode
        {
            Select = 0,
            DrawTie = 1,
            DrawRectTie = 2,
            Delete = 3
        }

        private sealed class BeamNaviateSpecialSectionGridRenderState
        {
            public List<double> GridXPx { get; } = new List<double>();
            public List<double> GridYPx { get; } = new List<double>();
            public double TieLeftPx { get; set; }
            public double TieTopPx { get; set; }
            public double TieRightPx { get; set; }
            public double TieBottomPx { get; set; }
        }


        private readonly ObservableCollection<BeamNaviateAdditionalRowUi> _beamNaviateAdditionalBottomRows =
            new ObservableCollection<BeamNaviateAdditionalRowUi>();
        private readonly ObservableCollection<BeamNaviateAdditionalRowUi> _beamNaviateAdditionalTopRows =
            new ObservableCollection<BeamNaviateAdditionalRowUi>();
        private readonly ObservableCollection<string> _beamNaviateBarTypeNames =
            new ObservableCollection<string>();
        private readonly ObservableCollection<string> _beamNaviateSpecialModeOptions =
            new ObservableCollection<string>(new[] { "U Stirrup", "Tie Stirrup", "Rectangular Stirrup" });
        private readonly ObservableCollection<BeamNaviateStirrupRowUi> _beamNaviateStirrupRows =
            new ObservableCollection<BeamNaviateStirrupRowUi>();
        private readonly ObservableCollection<BeamNaviateSpecialRowUi> _beamNaviateSpecialRows =
            new ObservableCollection<BeamNaviateSpecialRowUi>();
        private readonly List<BeamNaviateSpecialSectionLineTieUiSpec> _beamNaviateSpecialCustomLineTies =
            new List<BeamNaviateSpecialSectionLineTieUiSpec>();
        private readonly List<BeamNaviateSpecialSectionRectTieUiSpec> _beamNaviateSpecialCustomRectTies =
            new List<BeamNaviateSpecialSectionRectTieUiSpec>();
        private readonly List<ElementId> _beamRebarSelectedSecondaryElementIds = new List<ElementId>();
        private readonly List<ElementId> _beamRebarSelectedSupportElementIds = new List<ElementId>();
        private readonly List<double> _beamRebarSelectedHostLengthsMm = new List<double>();
        private readonly List<double> _beamRebarDetectedSupportStationsMm = new List<double>();
        private readonly List<double> _beamRebarDetectedSupportWidthsMm = new List<double>();
        private readonly List<string> _beamRebarDetectedSupportKinds = new List<string>();
        private readonly List<BeamPreviewSpanUi> _beamNaviatePreviewSpans = new List<BeamPreviewSpanUi>();
        private readonly Dictionary<int, BeamNaviateStirrupSpanOverrideUi> _beamNaviateStirrupSpanOverrides =
            new Dictionary<int, BeamNaviateStirrupSpanOverrideUi>();
        private string _beamRebarDetectedModelSummary = string.Empty;
        private int _beamRebarDetectedStartConnectionCount;
        private int _beamRebarDetectedEndConnectionCount;
        private double _beamRebarPrimaryHostWidthMm;
        private double _beamRebarPrimaryHostDepthMm;
        private bool _beamNaviatePendingSupportDefaults;
        private bool _beamNaviateAdditionalAutoFillUpdating;
        private bool _beamNaviateAdditionalGridEditing;
        private bool _beamNaviateDesignUiUpdating;
        private bool _beamNaviateAddBottomEnabledState;
        private bool _beamNaviateAddTopEnabledState;
        private bool _beamNaviateStirrupRowsSyncing;
        private int _beamNaviateSelectedPreviewSpanIndex = -1;
        private double _beamNaviatePreviewZoomFactor = 1.0;
        private string _beamNaviateStirrupGlobalS1Text = "150 mm";
        private string _beamNaviateStirrupGlobalS2Text = "200 mm";
        private string _beamNaviateStirrupGlobalS3Text = "150 mm";
        private double _beamNaviateSpecialSectionZoomFactor = 1.0;
        private double _beamNaviateSpecialSectionPanXPx;
        private double _beamNaviateSpecialSectionPanYPx;
        private bool _beamNaviateSpecialSectionIsPanning;
        private System.Windows.Point _beamNaviateSpecialSectionLastMousePoint;
        private bool _beamNaviateSpecialSectionIsLeftDrawDragging;
        private System.Windows.Point _beamNaviateSpecialSectionLeftDrawStartCanvasPoint;
        private BeamNaviateSpecialSectionEditMode _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.Select;
        private BeamNaviateSpecialSectionGridRenderState _beamNaviateSpecialSectionGridRenderState;
        private (int X, int Y)? _beamNaviateSpecialSectionPendingStartGridNode;
        private System.Windows.Point? _beamNaviateSpecialSectionLastMouseCanvasPoint;
        private string _beamNaviateSpecialSectionSelectedShapeKey = "";

        public IReadOnlyList<string> BeamNaviateBarTypeNames => _beamNaviateBarTypeNames;
        public IReadOnlyList<string> BeamNaviateSpecialModeOptions => _beamNaviateSpecialModeOptions;

        private void OnBeamRebarPickHostsClick(object sender, RoutedEventArgs e)
        {
            _handler.Request.RequestType = CadToModelRequestType.PickBeamRebarHosts;
            _externalEvent.Raise();
        }

        private void InitializeBeamRebarDesignUi()
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                RebindBeamNaviateAdditionalTabHandlers();

                if (BeamNaviateSettingCombo != null)
                {
                    BeamNaviateSettingCombo.IsEditable = true;
                }

                if (BeamNaviateAddBottomRowsGrid != null)
                {
                    BeamNaviateAddBottomRowsGrid.ItemsSource = _beamNaviateAdditionalBottomRows;
                }
                if (BeamNaviateAddTopRowsGrid != null)
                {
                    BeamNaviateAddTopRowsGrid.ItemsSource = _beamNaviateAdditionalTopRows;
                }
                if (BeamNaviateStirrupRowsGrid != null)
                {
                    BeamNaviateStirrupRowsGrid.ItemsSource = _beamNaviateStirrupRows;
                }
                DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
                if (specialRowsGrid != null)
                {
                    specialRowsGrid.ItemsSource = _beamNaviateSpecialRows;
                }

                EnsureBeamNaviateDefaultBarTypeItems();
                EnsureBeamNaviateAdditionalRowsInitialized();
                EnsureBeamNaviateSpecialRowsInitialized();
                _beamNaviateAddBottomEnabledState = BeamNaviateAddBottomEnabledCheckBox?.IsChecked == true;
                _beamNaviateAddTopEnabledState = BeamNaviateAddTopEnabledCheckBox?.IsChecked == true;
                InitializeBeamNaviateStirrupGlobalTextsFromUi();
                SyncBeamNaviateStirrupRowsFromState();
                LoadBeamNaviateSettingsFromDisk();
                _beamNaviateSettingsByName["<In Session>"] = CaptureBeamNaviateSnapshotFromUi("<In Session>");
                RefreshBeamNaviateSettingComboItems("<In Session>");
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
        }

        private void RebindBeamNaviateAdditionalTabHandlers()
        {
            if (BeamNaviateAddBottomEnabledCheckBox != null)
            {
                BeamNaviateAddBottomEnabledCheckBox.Checked -= OnBeamNaviateAddBottomEnabledChanged;
                BeamNaviateAddBottomEnabledCheckBox.Checked += OnBeamNaviateAddBottomEnabledChanged;
                BeamNaviateAddBottomEnabledCheckBox.Unchecked -= OnBeamNaviateAddBottomEnabledChanged;
                BeamNaviateAddBottomEnabledCheckBox.Unchecked += OnBeamNaviateAddBottomEnabledChanged;
            }

            if (BeamNaviateAddTopEnabledCheckBox != null)
            {
                BeamNaviateAddTopEnabledCheckBox.Checked -= OnBeamNaviateAddTopEnabledChanged;
                BeamNaviateAddTopEnabledCheckBox.Checked += OnBeamNaviateAddTopEnabledChanged;
                BeamNaviateAddTopEnabledCheckBox.Unchecked -= OnBeamNaviateAddTopEnabledChanged;
                BeamNaviateAddTopEnabledCheckBox.Unchecked += OnBeamNaviateAddTopEnabledChanged;
            }

            if (BeamNaviateAddBottomAddRowButton != null)
            {
                BeamNaviateAddBottomAddRowButton.Click -= OnBeamNaviateAddBottomRowClick;
                BeamNaviateAddBottomAddRowButton.Click += OnBeamNaviateAddBottomRowClick;
            }

            if (BeamNaviateAddTopAddRowButton != null)
            {
                BeamNaviateAddTopAddRowButton.Click -= OnBeamNaviateAddTopRowClick;
                BeamNaviateAddTopAddRowButton.Click += OnBeamNaviateAddTopRowClick;
            }
        }

        private void OnBeamNaviateInputChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            // TabControl SelectionChanged is a routed event; ignore child Selector events bubbling up here.
            // Child controls (ComboBox/DataGrid) already bind this handler directly where needed.
            if (ReferenceEquals(sender, BeamNaviateTabControl) &&
                !ReferenceEquals(e?.OriginalSource, BeamNaviateTabControl))
            {
                return;
            }

            if (ReferenceEquals(sender, BeamNaviateTabControl) &&
                ReferenceEquals(e?.OriginalSource, BeamNaviateTabControl))
            {
                TabItem selectedTab = BeamNaviateTabControl?.SelectedItem as TabItem;
                string headerText = (selectedTab?.Header?.ToString() ?? "").Trim();
                if (headerText.IndexOf("Additional Bottom", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ActivateBeamNaviateAdditionalTab(topRows: false);
                }
                else if (headerText.IndexOf("Additional Top", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ActivateBeamNaviateAdditionalTab(topRows: true);
                }
            }

            if (ReferenceEquals(sender, BeamNaviateStirrupLayoutCombo))
            {
                if (_beamNaviateSelectedPreviewSpanIndex < 0)
                {
                    _beamNaviateStirrupSpanOverrides.Clear();
                }
            }

            if (ReferenceEquals(sender, GetBeamNaviateStirrupZoneInputModeCombo()))
            {
                if (_beamNaviateSelectedPreviewSpanIndex < 0)
                {
                    _beamNaviateStirrupSpanOverrides.Clear();
                }
                SyncBeamNaviateStirrupRowsFromState();
            }

            UpdateBeamRebarDesignUiState();
        }

        private void ActivateBeamNaviateAdditionalTab(bool topRows)
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                SetBeamNaviateAdditionalEnabled(topRows, enabled: true);
                EnsureBeamNaviateAdditionalRowsReady(topRows);
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            // Run once after layout to ensure rows are visible when switching tabs quickly.
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    EnsureBeamNaviateAdditionalRowsReady(topRows);
                }));
        }

        private void OnBeamNaviateInputChanged(object sender, TextChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            if (ReferenceEquals(sender, BeamNaviateStirrupStartSpacingTextBox) ||
                ReferenceEquals(sender, BeamNaviateStirrupMiddleSpacingTextBox) ||
                ReferenceEquals(sender, BeamNaviateStirrupEndSpacingTextBox))
            {
                UpdateBeamNaviateStirrupSpanOverrideFromInputs();
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateInputChanged(object sender, RoutedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateAddBottomEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            _beamNaviateAddBottomEnabledState = BeamNaviateAddBottomEnabledCheckBox?.IsChecked == true;
            if (_beamNaviateAddBottomEnabledState)
            {
                EnsureBeamNaviateAdditionalRowsReady(topRows: false);
            }
            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateAddTopEnabledChanged(object sender, RoutedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            _beamNaviateAddTopEnabledState = BeamNaviateAddTopEnabledCheckBox?.IsChecked == true;
            if (_beamNaviateAddTopEnabledState)
            {
                EnsureBeamNaviateAdditionalRowsReady(topRows: true);
            }
            UpdateBeamRebarDesignUiState();
        }

        private void SetBeamNaviateAdditionalEnabled(bool topRows, bool enabled)
        {
            if (topRows)
            {
                _beamNaviateAddTopEnabledState = enabled;
            }
            else
            {
                _beamNaviateAddBottomEnabledState = enabled;
            }

            CheckBox checkBox = topRows ? BeamNaviateAddTopEnabledCheckBox : BeamNaviateAddBottomEnabledCheckBox;
            if (checkBox != null && checkBox.IsChecked != enabled)
            {
                bool previousSuspend = _beamNaviateUiEventsSuspended;
                _beamNaviateUiEventsSuspended = true;
                try
                {
                    checkBox.IsChecked = enabled;
                }
                finally
                {
                    _beamNaviateUiEventsSuspended = previousSuspend;
                }
            }
        }

        private bool ResolveBeamNaviateAdditionalEnabled(bool topRows)
        {
            bool state = topRows ? _beamNaviateAddTopEnabledState : _beamNaviateAddBottomEnabledState;
            CheckBox checkBox = topRows ? BeamNaviateAddTopEnabledCheckBox : BeamNaviateAddBottomEnabledCheckBox;
            bool enabled = state || (checkBox?.IsChecked == true);
            SetBeamNaviateAdditionalEnabled(topRows, enabled);
            return enabled;
        }

        private bool IsBeamNaviateSecondLayerEnabled(bool topBar)
        {
            CheckBox checkBox = topBar
                ? GetBeamNaviateMainTopSecondLayerCheckBox()
                : GetBeamNaviateMainBottomSecondLayerCheckBox();
            return checkBox?.IsChecked == true;
        }

        private int ResolveBeamNaviateMainLayerCount(bool topBar)
        {
            return IsBeamNaviateSecondLayerEnabled(topBar) ? 2 : 1;
        }

        private int ResolveBeamNaviateSecondLayerBarCountOrFallback(bool topBar, int firstLayerCount)
        {
            if (!IsBeamNaviateSecondLayerEnabled(topBar))
            {
                return Math.Max(1, firstLayerCount);
            }

            TextBox secondCountTextBox = topBar
                ? GetBeamNaviateMainTopBarCount2TextBox()
                : GetBeamNaviateMainBottomBarCount2TextBox();
            return ParseBeamNaviateIntOrDefault(secondCountTextBox?.Text, Math.Max(1, firstLayerCount), 1, 40);
        }

        private void SyncBeamNaviateHiddenLayerCountUi()
        {
            bool topSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: true);
            bool bottomSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: false);
            string topTargetText = topSecondEnabled ? "2" : "1";
            string bottomTargetText = bottomSecondEnabled ? "2" : "1";

            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                if (BeamNaviateMainTopLayerCountTextBox != null &&
                    !string.Equals(BeamNaviateMainTopLayerCountTextBox.Text, topTargetText, StringComparison.Ordinal))
                {
                    BeamNaviateMainTopLayerCountTextBox.Text = topTargetText;
                }

                if (BeamNaviateMainBottomLayerCountTextBox != null &&
                    !string.Equals(BeamNaviateMainBottomLayerCountTextBox.Text, bottomTargetText, StringComparison.Ordinal))
                {
                    BeamNaviateMainBottomLayerCountTextBox.Text = bottomTargetText;
                }
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }
        }

        private static BeamNaviateAdditionalRowUi CreateBeamNaviateAdditionalDefaultRow(bool topRow)
        {
            return new BeamNaviateAdditionalRowUi
            {
                SpanInfoText = "1",
                LayerText = "2",
                BarTypeName = "DB12",
                AmountText = "2",
                StartGridText = "0",
                EndGridText = topRow ? "0" : "1",
                StartLengthText = topRow ? "0.3L" : "0.125L",
                EndLengthText = topRow ? "0.3L" : "0.125L",
                StartFactorText = topRow ? "0.3" : "0.125",
                EndFactorText = topRow ? "0.3" : "0.125"
            };
        }

        private static BeamNaviateAdditionalRowUi CloneBeamNaviateAdditionalRowUi(BeamNaviateAdditionalRowUi source)
        {
            if (source == null)
            {
                return CreateBeamNaviateAdditionalDefaultRow(topRow: false);
            }

            return new BeamNaviateAdditionalRowUi
            {
                SpanInfoText = string.IsNullOrWhiteSpace(source.SpanInfoText) ? "1" : source.SpanInfoText.Trim(),
                LayerText = string.IsNullOrWhiteSpace(source.LayerText) ? "2" : source.LayerText.Trim(),
                BarTypeName = string.IsNullOrWhiteSpace(source.BarTypeName) ? "DB12" : source.BarTypeName.Trim(),
                AmountText = string.IsNullOrWhiteSpace(source.AmountText) ? "2" : source.AmountText.Trim(),
                StartGridText = string.IsNullOrWhiteSpace(source.StartGridText) ? "1" : source.StartGridText.Trim(),
                EndGridText = string.IsNullOrWhiteSpace(source.EndGridText) ? "1" : source.EndGridText.Trim(),
                StartLengthText = string.IsNullOrWhiteSpace(source.StartLengthText) ? "600 mm" : source.StartLengthText.Trim(),
                EndLengthText = string.IsNullOrWhiteSpace(source.EndLengthText) ? "600 mm" : source.EndLengthText.Trim(),
                StartFactorText = string.IsNullOrWhiteSpace(source.StartFactorText) ? "" : source.StartFactorText.Trim(),
                EndFactorText = string.IsNullOrWhiteSpace(source.EndFactorText) ? "" : source.EndFactorText.Trim()
            };
        }

        private void EnsureBeamNaviateAdditionalRowsReady(bool topRows)
        {
            ObservableCollection<BeamNaviateAdditionalRowUi> rows = topRows
                ? _beamNaviateAdditionalTopRows
                : _beamNaviateAdditionalBottomRows;
            DataGrid grid = topRows ? BeamNaviateAddTopRowsGrid : BeamNaviateAddBottomRowsGrid;
            bool changed = false;

            if (rows.Count == 0)
            {
                BeamNaviateAdditionalRowUi seeded = CreateBeamNaviateAdditionalDefaultRow(topRows);
                string preferredType = topRows
                    ? (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo) ?? "").Trim()
                    : (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(preferredType))
                {
                    preferredType = _beamNaviateBarTypeNames.FirstOrDefault()
                        ?? _beamRebarBarTypeItems.FirstOrDefault()?.Name
                        ?? "DB12";
                }

                seeded.BarTypeName = preferredType;
                rows.Add(seeded);
                changed = true;
            }

            if (grid != null)
            {
                if (!ReferenceEquals(grid.ItemsSource, rows))
                {
                    grid.ItemsSource = rows;
                    changed = true;
                }

                if (rows.Count > 0)
                {
                    BeamNaviateAdditionalRowUi selected = grid.SelectedItem as BeamNaviateAdditionalRowUi;
                    if (selected == null || !rows.Contains(selected))
                    {
                        grid.SelectedItem = rows[0];
                        changed = true;
                    }
                }

                // Force a visual rebind only when grid is active/loaded to avoid re-entrant routed events.
                if (rows.Count > 0 && grid.IsLoaded && grid.IsVisible && grid.Items.Count == 0)
                {
                    grid.ItemsSource = null;
                    grid.ItemsSource = rows;
                    changed = true;
                }

                if (changed)
                {
                    grid.Items.Refresh();
                }

                if (changed && grid.SelectedItem != null)
                {
                    grid.ScrollIntoView(grid.SelectedItem);
                }
            }
        }

        private static string GetBeamNaviateAdditionalStartInputText(BeamNaviateAdditionalRowUi row, bool factorMode)
        {
            if (row == null)
            {
                return "";
            }

            string primary = factorMode ? row.StartFactorText : row.StartLengthText;
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim();
            }

            string fallback = factorMode ? row.StartLengthText : row.StartFactorText;
            return (fallback ?? "").Trim();
        }

        private static string GetBeamNaviateAdditionalEndInputText(BeamNaviateAdditionalRowUi row, bool factorMode)
        {
            if (row == null)
            {
                return "";
            }

            string primary = factorMode ? row.EndFactorText : row.EndLengthText;
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim();
            }

            string fallback = factorMode ? row.EndLengthText : row.EndFactorText;
            return (fallback ?? "").Trim();
        }

        private static List<BeamNaviateAdditionalRowUi> CloneBeamNaviateAdditionalRows(IEnumerable<BeamNaviateAdditionalRowUi> rows)
        {
            return (rows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
                .Where(r => r != null)
                .Select(CloneBeamNaviateAdditionalRowUi)
                .ToList();
        }

        private static BeamNaviateSpecialRowUi CreateBeamNaviateSpecialDefaultRow(int sectionIndex = 1)
        {
            return new BeamNaviateSpecialRowUi
            {
                SectionText = Math.Max(1, sectionIndex).ToString(CultureInfo.InvariantCulture),
                CageText = "1",
                ModeText = "Tie Stirrup",
                SpacingText = "150 mm",
                StartZoneText = "800 mm",
                EndZoneText = "800 mm"
            };
        }

        private static BeamNaviateSpecialRowUi CloneBeamNaviateSpecialRowUi(BeamNaviateSpecialRowUi source)
        {
            if (source == null)
            {
                return CreateBeamNaviateSpecialDefaultRow();
            }

            return new BeamNaviateSpecialRowUi
            {
                SectionText = string.IsNullOrWhiteSpace(source.SectionText) ? "1" : source.SectionText.Trim(),
                CageText = string.IsNullOrWhiteSpace(source.CageText) ? "1" : source.CageText.Trim(),
                ModeText = NormalizeBeamNaviateSpecialModeText(source.ModeText),
                SpacingText = string.IsNullOrWhiteSpace(source.SpacingText) ? "150 mm" : source.SpacingText.Trim(),
                StartZoneText = string.IsNullOrWhiteSpace(source.StartZoneText) ? "800 mm" : source.StartZoneText.Trim(),
                EndZoneText = string.IsNullOrWhiteSpace(source.EndZoneText) ? "800 mm" : source.EndZoneText.Trim()
            };
        }

        private static IReadOnlyList<string> GetBeamNaviateSectionDrivenSpecialModes()
        {
            return new[] { "U Stirrup", "Tie Stirrup", "Rectangular Stirrup" };
        }

        private static string NormalizeBeamNaviateSpecialModeText(string value)
        {
            string mode = (value ?? "").Trim();
            if (mode.IndexOf("rect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Rectangular Stirrup";
            }

            if (mode.IndexOf("tie", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Tie Stirrup";
            }

            if (mode.IndexOf("u", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mode.IndexOf("c", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "U Stirrup";
            }

            return "Tie Stirrup";
        }

        private static int GetBeamNaviateSpecialModeLaneIndex(string modeText)
        {
            string mode = NormalizeBeamNaviateSpecialModeText(modeText);
            if (mode.StartsWith("Rectangular", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (mode.StartsWith("Tie", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }

        private static List<BeamNaviateSpecialRowUi> CloneBeamNaviateSpecialRows(IEnumerable<BeamNaviateSpecialRowUi> rows)
        {
            return (rows ?? Enumerable.Empty<BeamNaviateSpecialRowUi>())
                .Where(r => r != null)
                .Select(CloneBeamNaviateSpecialRowUi)
                .ToList();
        }

        private static BeamNaviateSpecialSectionLineTieUiSpec CloneBeamNaviateSpecialLineTieUiSpec(BeamNaviateSpecialSectionLineTieUiSpec source)
        {
            if (source == null)
            {
                return new BeamNaviateSpecialSectionLineTieUiSpec();
            }

            return new BeamNaviateSpecialSectionLineTieUiSpec
            {
                SectionIndex = Math.Max(1, source.SectionIndex),
                X0GridIndex = Math.Max(0, source.X0GridIndex),
                Y0GridIndex = Math.Max(0, source.Y0GridIndex),
                X1GridIndex = Math.Max(0, source.X1GridIndex),
                Y1GridIndex = Math.Max(0, source.Y1GridIndex)
            };
        }

        private static BeamNaviateSpecialSectionRectTieUiSpec CloneBeamNaviateSpecialRectTieUiSpec(BeamNaviateSpecialSectionRectTieUiSpec source)
        {
            if (source == null)
            {
                return new BeamNaviateSpecialSectionRectTieUiSpec();
            }

            return new BeamNaviateSpecialSectionRectTieUiSpec
            {
                SectionIndex = Math.Max(1, source.SectionIndex),
                X0GridIndex = Math.Max(0, source.X0GridIndex),
                Y0GridIndex = Math.Max(0, source.Y0GridIndex),
                X1GridIndex = Math.Max(0, source.X1GridIndex),
                Y1GridIndex = Math.Max(0, source.Y1GridIndex)
            };
        }

        private static List<BeamNaviateSpecialSectionLineTieUiSpec> CloneBeamNaviateSpecialLineTieUiSpecs(
            IEnumerable<BeamNaviateSpecialSectionLineTieUiSpec> specs)
        {
            return (specs ?? Enumerable.Empty<BeamNaviateSpecialSectionLineTieUiSpec>())
                .Where(x => x != null)
                .Select(CloneBeamNaviateSpecialLineTieUiSpec)
                .ToList();
        }

        private static List<BeamNaviateSpecialSectionRectTieUiSpec> CloneBeamNaviateSpecialRectTieUiSpecs(
            IEnumerable<BeamNaviateSpecialSectionRectTieUiSpec> specs)
        {
            return (specs ?? Enumerable.Empty<BeamNaviateSpecialSectionRectTieUiSpec>())
                .Where(x => x != null)
                .Select(CloneBeamNaviateSpecialRectTieUiSpec)
                .ToList();
        }

        private int ResolveBeamNaviateInputSpanCount()
        {
            if (_beamNaviatePreviewSpans.Count > 0)
            {
                return Math.Max(1, _beamNaviatePreviewSpans.Count);
            }
            if (_beamRebarDetectedSupportStationsMm.Count > 0)
            {
                return Math.Max(1, _beamRebarDetectedSupportStationsMm.Count + 1);
            }
            if (_beamRebarSelectedSupportElementIds.Count > 1)
            {
                return Math.Max(1, _beamRebarSelectedSupportElementIds.Count - 1);
            }

            return 1;
        }

        private static List<BeamNaviateAdditionalRowUi> NormalizeBeamNaviateAdditionalRowsForSpanBySpanInput(
            IEnumerable<BeamNaviateAdditionalRowUi> sourceRows,
            int spanCount,
            bool topRows)
        {
            List<BeamNaviateAdditionalRowUi> rows = CloneBeamNaviateAdditionalRows(sourceRows);
            if (rows.Count == 0)
            {
                return rows;
            }

            int safeSpanCount = Math.Max(1, spanCount);
            int segmentCount = topRows ? safeSpanCount + 1 : safeSpanCount;
            if (rows.Count <= 1 || segmentCount <= 1)
            {
                return rows;
            }

            var parsed = rows
                .Select(r => new
                {
                    Row = r,
                    StartGrid = ParseBeamNaviateIntOrDefault(r.StartGridText, 0, 0, 200),
                    EndGrid = ParseBeamNaviateIntOrDefault(r.EndGridText, 0, 0, 200)
                })
                .ToList();

            bool allSingleGrid = parsed.All(x => x.StartGrid == x.EndGrid);
            if (!allSingleGrid)
            {
                return rows;
            }

            int firstStart = parsed[0].StartGrid;
            int firstEnd = parsed[0].EndGrid;
            bool allSameNode = parsed.All(x => x.StartGrid == firstStart && x.EndGrid == firstEnd);
            if (!allSameNode)
            {
                return rows;
            }

            // Ambiguous duplicated rows (same grid pair) are treated as sequential span/node rows.
            for (int i = 0; i < parsed.Count; i++)
            {
                int mapped = i % segmentCount;
                parsed[i].Row.StartGridText = mapped.ToString(CultureInfo.InvariantCulture);
                parsed[i].Row.EndGridText = topRows
                    ? mapped.ToString(CultureInfo.InvariantCulture)
                    : (mapped + 1).ToString(CultureInfo.InvariantCulture);
            }

            return rows;
        }

        private void EnsureBeamNaviateAdditionalRowsInitialized()
        {
            if (_beamNaviateAdditionalBottomRows.Count == 0)
            {
                _beamNaviateAdditionalBottomRows.Add(CreateBeamNaviateAdditionalDefaultRow(topRow: false));
            }
            if (_beamNaviateAdditionalTopRows.Count == 0)
            {
                _beamNaviateAdditionalTopRows.Add(CreateBeamNaviateAdditionalDefaultRow(topRow: true));
            }
        }

        private void EnsureBeamNaviateSpecialRowsInitialized()
        {
            if (_beamNaviateSpecialRows.Count > 0)
            {
                return;
            }

            ApplyBeamNaviateDefaultSpecialRows();
        }

        private void ApplyBeamNaviateDefaultSpecialRows()
        {
            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            IReadOnlyList<string> sectionModes = GetBeamNaviateSectionDrivenSpecialModes();
            _beamNaviateSpecialRows.Clear();
            for (int i = 0; i < sectionCount; i++)
            {
                foreach (string mode in sectionModes)
                {
                    BeamNaviateSpecialRowUi row = CreateBeamNaviateSpecialDefaultRow(i + 1);
                    row.ModeText = mode;
                    if (!string.IsNullOrWhiteSpace((BeamNaviateSpecialSpacingTextBox?.Text ?? "").Trim()))
                    {
                        row.SpacingText = BeamNaviateSpecialSpacingTextBox.Text.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace((BeamNaviateSpecialZoneLengthTextBox?.Text ?? "").Trim()))
                    {
                        row.StartZoneText = BeamNaviateSpecialZoneLengthTextBox.Text.Trim();
                        row.EndZoneText = BeamNaviateSpecialZoneLengthTextBox.Text.Trim();
                    }

                    _beamNaviateSpecialRows.Add(row);
                }
            }
        }

        private void ApplyBeamNaviateDefaultAdditionalRows(ObservableCollection<BeamNaviateAdditionalRowUi> rows, bool topRows)
        {
            if (rows == null)
            {
                return;
            }

            rows.Clear();
            rows.Add(CreateBeamNaviateAdditionalDefaultRow(topRows));
        }

        private void NormalizeBeamNaviateAdditionalRowBarTypes(ObservableCollection<BeamNaviateAdditionalRowUi> rows, string fallbackTypeName)
        {
            if (rows == null || rows.Count == 0)
            {
                return;
            }

            string fallback = (fallbackTypeName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
            }

            foreach (BeamNaviateAdditionalRowUi row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                string rowType = (row.BarTypeName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(rowType) &&
                    _beamNaviateBarTypeNames.Any(x => string.Equals(x, rowType, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                row.BarTypeName = fallback;
            }
        }

        private void ReplaceBeamNaviateAdditionalRows(
            ObservableCollection<BeamNaviateAdditionalRowUi> target,
            IEnumerable<BeamNaviateAdditionalRowUi> sourceRows,
            bool topRows)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            foreach (BeamNaviateAdditionalRowUi row in CloneBeamNaviateAdditionalRows(sourceRows))
            {
                target.Add(row);
            }

            if (target.Count == 0)
            {
                target.Add(CreateBeamNaviateAdditionalDefaultRow(topRows));
            }
        }

        private void OnBeamNaviateAdditionalRowsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            UpdateBeamNaviateAdditionalDeleteButtonsState();
            QueueBeamNaviatePreviewRender();
        }

        private void OnBeamNaviateAdditionalRowsCurrentCellChanged(object sender, EventArgs e)
        {
            if (_beamNaviateUiEventsSuspended || _beamNaviateAdditionalGridEditing)
            {
                return;
            }

            CommitBeamNaviateAdditionalRowEdits();
            AutoFillBeamNaviateAdditionalFrozenColumns();
            if (sender is DataGrid grid)
            {
                grid.Items.Refresh();
            }
            UpdateBeamNaviateAdditionalDeleteButtonsState();
            QueueBeamNaviatePreviewRender();
        }

        private void OnBeamNaviateAdditionalRowsBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            _beamNaviateAdditionalGridEditing = true;
        }

        private void OnBeamNaviateAdditionalRowsCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    _beamNaviateAdditionalGridEditing = false;
                    CommitBeamNaviateAdditionalRowEdits();
                    AutoFillBeamNaviateAdditionalFrozenColumns();
                    if (sender is DataGrid grid)
                    {
                        grid.Items.Refresh();
                    }
                    UpdateBeamNaviateAdditionalDeleteButtonsState();
                    QueueBeamNaviatePreviewRender();
                }));
        }

        private void UpdateBeamNaviateAdditionalDeleteButtonsState()
        {
            bool addBottomEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: false);
            bool addTopEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: true);
            if (BeamNaviateAddBottomDeleteRowButton != null)
            {
                BeamNaviateAddBottomDeleteRowButton.IsEnabled = addBottomEnabled && BeamNaviateAddBottomRowsGrid?.SelectedItem != null;
            }
            if (BeamNaviateAddTopDeleteRowButton != null)
            {
                BeamNaviateAddTopDeleteRowButton.IsEnabled = addTopEnabled && BeamNaviateAddTopRowsGrid?.SelectedItem != null;
            }
        }

        private void OnBeamNaviateAdditionalRowsPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended || e == null || !(sender is DataGrid grid))
            {
                return;
            }

            bool textEditorFocused = Keyboard.FocusedElement is TextBox;

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.C)
            {
                if (textEditorFocused)
                {
                    return;
                }

                if (TryCopyBeamNaviateAdditionalGridSelectionToClipboard(grid))
                {
                    ShowStatus("BEAM: copied selected additional-bar cells.");
                }
                else
                {
                    ShowStatus("BEAM: nothing to copy from additional-bar grid.");
                }

                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.V)
            {
                if (textEditorFocused)
                {
                    return;
                }

                int pasted = PasteClipboardToBeamNaviateAdditionalGrid(grid);
                if (pasted > 0)
                {
                    ShowStatus($"BEAM: pasted {pasted} cell(s) to additional bars.");
                }
                else
                {
                    ShowStatus("BEAM: clipboard paste skipped (empty or no editable target).");
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                if (textEditorFocused)
                {
                    return;
                }

                int cleared = ClearBeamNaviateAdditionalGridSelection(grid);
                if (cleared > 0)
                {
                    ShowStatus($"BEAM: cleared {cleared} cell(s) in additional-bar grid.");
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                bool factorMode = IsBeamNaviateAdditionalInputFactorMode(
                    ReferenceEquals(grid, BeamNaviateAddTopRowsGrid)
                        ? GetBeamNaviateAddTopInputModeCombo()
                        : GetBeamNaviateAddBottomInputModeCombo());
                int currentColumnIndex = grid.Columns.IndexOf(grid.CurrentCell.Column);
                int lastEditableColumnIndex = factorMode ? 9 : 7;
                if (currentColumnIndex >= lastEditableColumnIndex)
                {
                    CommitBeamNaviateAdditionalRowEdits();
                    AutoFillBeamNaviateAdditionalFrozenColumns();
                    MoveBeamNaviateAdditionalGridFocusToNextRow(grid, targetColumnIndex: 1);
                    UpdateBeamRebarDesignUiState();
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key != Key.Enter)
            {
                return;
            }

            CommitBeamNaviateAdditionalRowEdits();
            AutoFillBeamNaviateAdditionalFrozenColumns();
            MoveBeamNaviateAdditionalGridFocusToNextRow(grid);
            UpdateBeamRebarDesignUiState();
            e.Handled = true;
        }

        private void MoveBeamNaviateAdditionalGridFocusToNextRow(DataGrid grid, int targetColumnIndex = -1)
        {
            if (grid == null)
            {
                return;
            }

            DataGridColumn currentColumn = null;
            if (targetColumnIndex >= 0 && targetColumnIndex < grid.Columns.Count)
            {
                currentColumn = grid.Columns[targetColumnIndex];
            }
            if (currentColumn == null)
            {
                currentColumn = grid.CurrentCell.Column
                    ?? grid.Columns.FirstOrDefault(c => c != null && !c.IsReadOnly);
            }
            if (currentColumn == null)
            {
                return;
            }

            int currentRowIndex = grid.Items.IndexOf(grid.CurrentItem);
            if (currentRowIndex < 0)
            {
                currentRowIndex = grid.Items.IndexOf(grid.SelectedItem);
            }
            if (currentRowIndex < 0)
            {
                currentRowIndex = 0;
            }

            int targetRowIndex = currentRowIndex + 1;
            ObservableCollection<BeamNaviateAdditionalRowUi> rows = ResolveBeamNaviateAdditionalRowsByGrid(grid, out bool topRows);
            if (rows != null)
            {
                while (targetRowIndex >= rows.Count)
                {
                    if (AppendBeamNaviateAdditionalRowForGrid(grid, topRows) == null)
                    {
                        break;
                    }
                }
            }

            if (targetRowIndex >= grid.Items.Count)
            {
                targetRowIndex = Math.Max(0, grid.Items.Count - 1);
            }
            if (targetRowIndex < 0 || targetRowIndex >= grid.Items.Count)
            {
                return;
            }

            object targetItem = grid.Items[targetRowIndex];
            if (targetItem == null)
            {
                return;
            }

            grid.SelectedItem = targetItem;
            grid.CurrentCell = new DataGridCellInfo(targetItem, currentColumn);
            grid.ScrollIntoView(targetItem, currentColumn);
            grid.Focus();
            try
            {
                grid.BeginEdit();
            }
            catch
            {
            }
        }

        private bool TryCopyBeamNaviateAdditionalGridSelectionToClipboard(DataGrid grid)
        {
            if (grid == null)
            {
                return false;
            }

            List<DataGridCellInfo> selectedCells = grid.SelectedCells
                .Where(cell => cell.Column != null && cell.Item is BeamNaviateAdditionalRowUi)
                .ToList();
            if (selectedCells.Count == 0 && grid.CurrentCell.Column != null && grid.CurrentCell.Item is BeamNaviateAdditionalRowUi)
            {
                selectedCells.Add(grid.CurrentCell);
            }
            if (selectedCells.Count == 0)
            {
                return false;
            }

            List<int> rowIndices = selectedCells
                .Select(cell => grid.Items.IndexOf(cell.Item))
                .Where(i => i >= 0)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            List<int> columnIndices = selectedCells
                .Select(cell => grid.Columns.IndexOf(cell.Column))
                .Where(i => i >= 0)
                .Distinct()
                .OrderBy(i => i)
                .ToList();
            if (rowIndices.Count == 0 || columnIndices.Count == 0)
            {
                return false;
            }

            int minRow = rowIndices.First();
            int maxRow = rowIndices.Last();
            int minCol = columnIndices.First();
            int maxCol = columnIndices.Last();
            var selectedMap = new HashSet<string>(
                selectedCells
                    .Select(cell =>
                    {
                        int r = grid.Items.IndexOf(cell.Item);
                        int c = grid.Columns.IndexOf(cell.Column);
                        return r >= 0 && c >= 0 ? (r.ToString(CultureInfo.InvariantCulture) + "|" + c.ToString(CultureInfo.InvariantCulture)) : string.Empty;
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x)));

            var sb = new StringBuilder();
            for (int r = minRow; r <= maxRow; r++)
            {
                if (!(grid.Items[r] is BeamNaviateAdditionalRowUi row))
                {
                    continue;
                }

                for (int c = minCol; c <= maxCol; c++)
                {
                    if (c > minCol)
                    {
                        sb.Append('\t');
                    }

                    string key = r.ToString(CultureInfo.InvariantCulture) + "|" + c.ToString(CultureInfo.InvariantCulture);
                    if (!selectedMap.Contains(key))
                    {
                        continue;
                    }

                    sb.Append(GetBeamNaviateAdditionalGridCellValue(row, c));
                }

                if (r < maxRow)
                {
                    sb.AppendLine();
                }
            }

            string tsv = sb.ToString();
            if (string.IsNullOrWhiteSpace(tsv))
            {
                return false;
            }

            try
            {
                Clipboard.SetDataObject(tsv, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private int ClearBeamNaviateAdditionalGridSelection(DataGrid grid)
        {
            if (grid == null)
            {
                return 0;
            }

            ObservableCollection<BeamNaviateAdditionalRowUi> rows = ResolveBeamNaviateAdditionalRowsByGrid(grid, out bool topRows);
            if (rows == null)
            {
                return 0;
            }

            List<DataGridCellInfo> selectedCells = grid.SelectedCells
                .Where(cell => cell.Column != null && cell.Item is BeamNaviateAdditionalRowUi)
                .ToList();
            if (selectedCells.Count == 0 && grid.CurrentCell.Column != null && grid.CurrentCell.Item is BeamNaviateAdditionalRowUi)
            {
                selectedCells.Add(grid.CurrentCell);
            }
            if (selectedCells.Count == 0)
            {
                return 0;
            }

            bool factorMode = IsBeamNaviateAdditionalInputFactorMode(
                topRows ? GetBeamNaviateAddTopInputModeCombo() : GetBeamNaviateAddBottomInputModeCombo());
            int changed = 0;
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                foreach (DataGridCellInfo cell in selectedCells)
                {
                    if (!(cell.Item is BeamNaviateAdditionalRowUi row))
                    {
                        continue;
                    }

                    int colIndex = grid.Columns.IndexOf(cell.Column);
                    if (colIndex < 0)
                    {
                        continue;
                    }

                    if (TrySetBeamNaviateAdditionalGridCellValue(row, colIndex, string.Empty, factorMode))
                    {
                        changed++;
                    }
                }
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            if (changed > 0)
            {
                CommitBeamNaviateAdditionalRowEdits();
                AutoFillBeamNaviateAdditionalFrozenColumns();
                grid.Items.Refresh();
                QueueBeamNaviatePreviewRender();
            }

            return changed;
        }

        private static string GetBeamNaviateAdditionalGridCellValue(BeamNaviateAdditionalRowUi row, int columnIndex)
        {
            if (row == null)
            {
                return string.Empty;
            }

            switch (columnIndex)
            {
                case 0: return row.SpanInfoText ?? string.Empty;
                case 1: return row.LayerText ?? string.Empty;
                case 2: return row.BarTypeName ?? string.Empty;
                case 3: return row.AmountText ?? string.Empty;
                case 4: return row.StartGridText ?? string.Empty;
                case 5: return row.EndGridText ?? string.Empty;
                case 6: return row.StartLengthText ?? string.Empty;
                case 7: return row.EndLengthText ?? string.Empty;
                case 8: return row.StartFactorText ?? string.Empty;
                case 9: return row.EndFactorText ?? string.Empty;
                default: return string.Empty;
            }
        }

        private int PasteClipboardToBeamNaviateAdditionalGrid(DataGrid grid)
        {
            if (grid == null)
            {
                return 0;
            }

            string clipboardText;
            try
            {
                clipboardText = Clipboard.GetText();
            }
            catch
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                return 0;
            }

            ObservableCollection<BeamNaviateAdditionalRowUi> rows = ResolveBeamNaviateAdditionalRowsByGrid(grid, out bool topRows);
            if (rows == null)
            {
                return 0;
            }

            string normalized = clipboardText.Replace("\r\n", "\n").Replace('\r', '\n');
            List<string[]> parsedRows = normalized
                .Split(new[] { '\n' }, StringSplitOptions.None)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Split('\t'))
                .Where(cells => cells.Length > 0)
                .ToList();
            if (parsedRows.Count == 0)
            {
                return 0;
            }

            int startRowIndex = grid.Items.IndexOf(grid.CurrentItem);
            if (startRowIndex < 0)
            {
                startRowIndex = grid.Items.IndexOf(grid.SelectedItem);
            }
            startRowIndex = Math.Max(0, startRowIndex);

            int startColumnIndex = grid.Columns.IndexOf(grid.CurrentCell.Column);
            if (startColumnIndex < 1)
            {
                startColumnIndex = 1; // Skip read-only "Span" column.
            }

            bool factorMode = IsBeamNaviateAdditionalInputFactorMode(
                topRows ? GetBeamNaviateAddTopInputModeCombo() : GetBeamNaviateAddBottomInputModeCombo());

            int changed = 0;
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                for (int r = 0; r < parsedRows.Count; r++)
                {
                    int targetRowIndex = startRowIndex + r;
                    while (targetRowIndex >= rows.Count)
                    {
                        if (AppendBeamNaviateAdditionalRowForGrid(grid, topRows) == null)
                        {
                            break;
                        }
                    }
                    if (targetRowIndex < 0 || targetRowIndex >= rows.Count)
                    {
                        continue;
                    }

                    BeamNaviateAdditionalRowUi targetRow = rows[targetRowIndex];
                    string[] cells = parsedRows[r];
                    for (int c = 0; c < cells.Length; c++)
                    {
                        int targetColumnIndex = startColumnIndex + c;
                        string cellText = (cells[c] ?? "").Trim();
                        if (TrySetBeamNaviateAdditionalGridCellValue(targetRow, targetColumnIndex, cellText, factorMode))
                        {
                            changed++;
                        }
                    }
                }
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            if (changed > 0)
            {
                CommitBeamNaviateAdditionalRowEdits();
                AutoFillBeamNaviateAdditionalFrozenColumns();
                grid.Items.Refresh();
                QueueBeamNaviatePreviewRender();
            }

            return changed;
        }

        private ObservableCollection<BeamNaviateAdditionalRowUi> ResolveBeamNaviateAdditionalRowsByGrid(DataGrid grid, out bool topRows)
        {
            if (ReferenceEquals(grid, BeamNaviateAddTopRowsGrid))
            {
                topRows = true;
                return _beamNaviateAdditionalTopRows;
            }
            if (ReferenceEquals(grid, BeamNaviateAddBottomRowsGrid))
            {
                topRows = false;
                return _beamNaviateAdditionalBottomRows;
            }

            topRows = false;
            return null;
        }

        private BeamNaviateAdditionalRowUi AppendBeamNaviateAdditionalRowForGrid(DataGrid grid, bool topRows)
        {
            ObservableCollection<BeamNaviateAdditionalRowUi> rows = topRows
                ? _beamNaviateAdditionalTopRows
                : _beamNaviateAdditionalBottomRows;
            if (rows == null)
            {
                return null;
            }

            string preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
            BeamNaviateAdditionalRowUi seed = rows.LastOrDefault()
                ?? CreateBeamNaviateAdditionalDefaultRow(topRows);
            BeamNaviateAdditionalRowUi added = CloneBeamNaviateAdditionalRowUi(seed);
            if (string.IsNullOrWhiteSpace(added.BarTypeName))
            {
                added.BarTypeName = preferredType;
            }
            if (string.IsNullOrWhiteSpace(added.LayerText))
            {
                added.LayerText = "2";
            }
            if (string.IsNullOrWhiteSpace(added.AmountText))
            {
                added.AmountText = "2";
            }

            int spanCount = ResolveBeamNaviateInputSpanCount();
            if (topRows)
            {
                int supportNodeCount = Math.Max(2, spanCount + 1);
                int nodeIndex = Math.Max(0, rows.Count % supportNodeCount);
                added.StartGridText = nodeIndex.ToString(CultureInfo.InvariantCulture);
                added.EndGridText = nodeIndex.ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(added.StartLengthText))
                {
                    added.StartLengthText = "0.3L";
                }
                if (string.IsNullOrWhiteSpace(added.EndLengthText))
                {
                    added.EndLengthText = "0.3L";
                }
                if (string.IsNullOrWhiteSpace(added.StartFactorText))
                {
                    added.StartFactorText = "0.3";
                }
                if (string.IsNullOrWhiteSpace(added.EndFactorText))
                {
                    added.EndFactorText = "0.3";
                }
            }
            else
            {
                int spanIndex = Math.Max(0, rows.Count % Math.Max(1, spanCount));
                added.StartGridText = spanIndex.ToString(CultureInfo.InvariantCulture);
                added.EndGridText = (spanIndex + 1).ToString(CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(added.StartLengthText))
                {
                    added.StartLengthText = "0.125L";
                }
                if (string.IsNullOrWhiteSpace(added.EndLengthText))
                {
                    added.EndLengthText = "0.125L";
                }
                if (string.IsNullOrWhiteSpace(added.StartFactorText))
                {
                    added.StartFactorText = "0.125";
                }
                if (string.IsNullOrWhiteSpace(added.EndFactorText))
                {
                    added.EndFactorText = "0.125";
                }
            }

            rows.Add(added);
            grid?.ScrollIntoView(added);

            return added;
        }

        private static bool TrySetBeamNaviateAdditionalGridCellValue(
            BeamNaviateAdditionalRowUi row,
            int columnIndex,
            string value,
            bool factorMode)
        {
            if (row == null)
            {
                return false;
            }

            switch (columnIndex)
            {
                case 1:
                    row.LayerText = value;
                    return true;
                case 2:
                    row.BarTypeName = value;
                    return true;
                case 3:
                    row.AmountText = value;
                    return true;
                case 4:
                    row.StartGridText = value;
                    return true;
                case 5:
                    row.EndGridText = value;
                    return true;
                case 6:
                    if (factorMode) return false;
                    row.StartLengthText = value;
                    return true;
                case 7:
                    if (factorMode) return false;
                    row.EndLengthText = value;
                    return true;
                case 8:
                    if (!factorMode) return false;
                    row.StartFactorText = value;
                    return true;
                case 9:
                    if (!factorMode) return false;
                    row.EndFactorText = value;
                    return true;
                default:
                    return false;
            }
        }

        private void OnBeamNaviateStirrupRowsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended || _beamNaviateStirrupRowsSyncing)
            {
                return;
            }

            if (!(BeamNaviateStirrupRowsGrid?.SelectedItem is BeamNaviateStirrupRowUi selected))
            {
                return;
            }

            _beamNaviateSelectedPreviewSpanIndex = Math.Max(0, selected.SpanIndex);
            ApplyBeamNaviateStirrupInputsForSelectedSpan();
            QueueBeamNaviatePreviewRender();
            ShowStatus($"BEAM: stirrup input target -> span {_beamNaviateSelectedPreviewSpanIndex + 1}.");
        }

        private void OnBeamNaviateStirrupRowsCurrentCellChanged(object sender, EventArgs e)
        {
            if (_beamNaviateUiEventsSuspended || _beamNaviateStirrupRowsSyncing)
            {
                return;
            }

            try
            {
                BeamNaviateStirrupRowsGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                BeamNaviateStirrupRowsGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
            }

            SyncBeamNaviateStirrupOverridesFromRows();
            UpdateBeamRebarDesignUiState();
        }

        private void SyncBeamNaviateStirrupRowsFromState()
        {
            int spanCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            string globalS1 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm");
            string globalS2 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm");
            string globalS3 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm");
            int selectedSpan = _beamNaviateSelectedPreviewSpanIndex;
            bool followAdditionalTop = IsBeamNaviateStirrupZoneFollowAdditionalTopMode(GetBeamNaviateStirrupZoneInputModeCombo());
            bool factorMode = IsBeamNaviateStirrupZoneFactorMode(GetBeamNaviateStirrupZoneInputModeCombo());

            double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
            if (hostLengthMm <= 1.0 || double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm))
            {
                hostLengthMm = 1000.0;
            }

            var spanLengthByIndexMm = new Dictionary<int, double>();
            List<double> supportStationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
            List<double> spanStationsMm = BuildBeamPreviewSpanBoundaryStationsMm(hostLengthMm, supportStationsMm);
            if (spanStationsMm.Count < 2)
            {
                spanStationsMm = new List<double> { 0.0, hostLengthMm };
            }

            for (int i = 0; i < spanStationsMm.Count - 1; i++)
            {
                spanLengthByIndexMm[i] = Math.Max(0.0, spanStationsMm[i + 1] - spanStationsMm[i]);
            }

            _beamNaviateStirrupRowsSyncing = true;
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                foreach (int stale in _beamNaviateStirrupSpanOverrides.Keys.Where(k => k < 0 || k >= spanCount).ToList())
                {
                    _beamNaviateStirrupSpanOverrides.Remove(stale);
                }

                while (_beamNaviateStirrupRows.Count > spanCount)
                {
                    _beamNaviateStirrupRows.RemoveAt(_beamNaviateStirrupRows.Count - 1);
                }

                for (int i = 0; i < spanCount; i++)
                {
                    BeamNaviateStirrupSpanOverrideUi spanOverride = _beamNaviateStirrupSpanOverrides.TryGetValue(i, out BeamNaviateStirrupSpanOverrideUi existing)
                        ? existing
                        : null;

                    string s1 = NormalizeBeamNaviateLengthText(spanOverride?.StartSpacingText, globalS1);
                    string s2 = NormalizeBeamNaviateLengthText(spanOverride?.MiddleSpacingText, globalS2);
                    string s3 = NormalizeBeamNaviateLengthText(spanOverride?.EndSpacingText, globalS3);
                    string z1Raw = NormalizeBeamNaviateLengthText(spanOverride?.StartZoneText, "0 mm");
                    string z3Raw = NormalizeBeamNaviateLengthText(spanOverride?.EndZoneText, "0 mm");
                    double spanLengthMm = spanLengthByIndexMm.TryGetValue(i, out double storedSpanLenMm) && storedSpanLenMm > 1.0
                        ? storedSpanLenMm
                        : Math.Max(1.0, hostLengthMm / Math.Max(1, spanCount));
                    double z1Mm = 0.0;
                    double z3Mm = 0.0;
                    string z1FactorText = "0";
                    string z3FactorText = "0";
                    if (followAdditionalTop &&
                        spanLengthByIndexMm.TryGetValue(i, out double detectedSpanMm) &&
                        detectedSpanMm > 1.0 &&
                        TryResolveBeamNaviateAdditionalTopZoneLengthsMm(i, detectedSpanMm, out double topL1Mm, out double topL3Mm))
                    {
                        z1Mm = Math.Max(0.0, topL1Mm);
                        z3Mm = Math.Max(0.0, topL3Mm);
                    }
                    else if (factorMode && !followAdditionalTop)
                    {
                        z1Mm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(z1Raw, spanLengthMm, interpretPlainAsFactor: true));
                        z3Mm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(z3Raw, spanLengthMm, interpretPlainAsFactor: true));
                    }
                    else
                    {
                        z1Mm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(z1Raw, spanLengthMm, interpretPlainAsFactor: false));
                        z3Mm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(z3Raw, spanLengthMm, interpretPlainAsFactor: false));
                    }

                    string z1 = FormatBeamNaviateLengthCellText(z1Mm);
                    string z3 = FormatBeamNaviateLengthCellText(z3Mm);
                    z1FactorText = FormatBeamNaviateFactorCellText(spanLengthMm > 1e-6 ? (z1Mm / spanLengthMm) : 0.0);
                    z3FactorText = FormatBeamNaviateFactorCellText(spanLengthMm > 1e-6 ? (z3Mm / spanLengthMm) : 0.0);

                    if (i < _beamNaviateStirrupRows.Count)
                    {
                        BeamNaviateStirrupRowUi row = _beamNaviateStirrupRows[i];
                        row.SpanIndex = i;
                        row.SpanInfoText = (i + 1).ToString(CultureInfo.InvariantCulture);
                        row.StartSpacingText = s1;
                        row.MiddleSpacingText = s2;
                        row.EndSpacingText = s3;
                        row.StartGridText = i.ToString(CultureInfo.InvariantCulture);
                        row.EndGridText = (i + 1).ToString(CultureInfo.InvariantCulture);
                        row.StartZoneText = z1;
                        row.EndZoneText = z3;
                        row.StartFactorText = z1FactorText;
                        row.EndFactorText = z3FactorText;
                    }
                    else
                    {
                        _beamNaviateStirrupRows.Add(new BeamNaviateStirrupRowUi
                        {
                            SpanIndex = i,
                            SpanInfoText = (i + 1).ToString(CultureInfo.InvariantCulture),
                            StartSpacingText = s1,
                            MiddleSpacingText = s2,
                            EndSpacingText = s3,
                            StartGridText = i.ToString(CultureInfo.InvariantCulture),
                            EndGridText = (i + 1).ToString(CultureInfo.InvariantCulture),
                            StartZoneText = z1,
                            EndZoneText = z3,
                            StartFactorText = z1FactorText,
                            EndFactorText = z3FactorText
                        });
                    }
                }

                if (BeamNaviateStirrupRowsGrid != null)
                {
                    if (selectedSpan >= 0 && selectedSpan < _beamNaviateStirrupRows.Count)
                    {
                        BeamNaviateStirrupRowsGrid.SelectedItem = _beamNaviateStirrupRows[selectedSpan];
                        BeamNaviateStirrupRowsGrid.ScrollIntoView(_beamNaviateStirrupRows[selectedSpan]);
                    }
                    else
                    {
                        BeamNaviateStirrupRowsGrid.SelectedItem = null;
                    }
                }
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
                _beamNaviateStirrupRowsSyncing = false;
            }
        }

        private void SyncBeamNaviateStirrupOverridesFromRows()
        {
            string globalS1 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm");
            string globalS2 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm");
            string globalS3 = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm");
            bool followAdditionalTop = IsBeamNaviateStirrupZoneFollowAdditionalTopMode(GetBeamNaviateStirrupZoneInputModeCombo());
            bool factorMode = IsBeamNaviateStirrupZoneFactorMode(GetBeamNaviateStirrupZoneInputModeCombo());

            var rebuilt = new Dictionary<int, BeamNaviateStirrupSpanOverrideUi>();
            foreach (BeamNaviateStirrupRowUi row in _beamNaviateStirrupRows.Where(r => r != null))
            {
                int spanIndex = Math.Max(0, row.SpanIndex);
                string s1 = NormalizeBeamNaviateLengthText(row.StartSpacingText, globalS1);
                string s2 = NormalizeBeamNaviateLengthText(row.MiddleSpacingText, globalS2);
                string s3 = NormalizeBeamNaviateLengthText(row.EndSpacingText, globalS3);
                string z1 = NormalizeBeamNaviateLengthText(row.StartZoneText, "0 mm");
                string z3 = NormalizeBeamNaviateLengthText(row.EndZoneText, "0 mm");
                if (factorMode && !followAdditionalTop)
                {
                    z1 = NormalizeBeamNaviateLengthText(row.StartFactorText, "0");
                    z3 = NormalizeBeamNaviateLengthText(row.EndFactorText, "0");
                }
                if (followAdditionalTop)
                {
                    z1 = "0 mm";
                    z3 = "0 mm";
                }

                bool sameAsGlobal =
                    string.Equals(s1, globalS1, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s2, globalS2, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(s3, globalS3, StringComparison.OrdinalIgnoreCase) &&
                    (factorMode
                        ? (IsBeamNaviateZeroOrEmptyFactorText(z1) && IsBeamNaviateZeroOrEmptyFactorText(z3))
                        : (IsBeamNaviateZeroOrEmptyLengthText(z1) && IsBeamNaviateZeroOrEmptyLengthText(z3)));
                if (sameAsGlobal)
                {
                    continue;
                }

                rebuilt[spanIndex] = new BeamNaviateStirrupSpanOverrideUi
                {
                    SpanIndex = spanIndex,
                    StartSpacingText = s1,
                    MiddleSpacingText = s2,
                    EndSpacingText = s3,
                    StartZoneText = z1,
                    EndZoneText = z3
                };
            }

            _beamNaviateStirrupSpanOverrides.Clear();
            foreach (var kvp in rebuilt.OrderBy(x => x.Key))
            {
                _beamNaviateStirrupSpanOverrides[kvp.Key] = kvp.Value;
            }
        }

        private void SelectBeamNaviateStirrupGridRowBySpan(int spanIndex)
        {
            if (BeamNaviateStirrupRowsGrid == null)
            {
                return;
            }

            _beamNaviateStirrupRowsSyncing = true;
            try
            {
                if (spanIndex >= 0 && spanIndex < _beamNaviateStirrupRows.Count)
                {
                    BeamNaviateStirrupRowsGrid.SelectedItem = _beamNaviateStirrupRows[spanIndex];
                    BeamNaviateStirrupRowsGrid.ScrollIntoView(_beamNaviateStirrupRows[spanIndex]);
                }
                else
                {
                    BeamNaviateStirrupRowsGrid.SelectedItem = null;
                }
            }
            finally
            {
                _beamNaviateStirrupRowsSyncing = false;
            }
        }

        private static bool IsBeamNaviateZeroOrEmptyLengthText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return true;
            }

            string text = rawText.Trim();
            if (!TryParseBeamNaviateMmText(text, out double mm))
            {
                return false;
            }

            return Math.Abs(mm) <= 1e-6;
        }

        private static bool IsBeamNaviateZeroOrEmptyFactorText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return true;
            }

            string text = rawText.Trim();
            if (TryParseBeamNaviateLengthRatioOfL(text, out double ratioL))
            {
                return Math.Abs(ratioL) <= 1e-6;
            }

            if (TryParseBeamNaviatePlainFactorText(text, out double factor))
            {
                return Math.Abs(factor) <= 1e-6;
            }

            return false;
        }

        private void OnBeamNaviateAddBottomRowClick(object sender, RoutedEventArgs e)
        {
            SetBeamNaviateAdditionalEnabled(topRows: false, enabled: true);
            EnsureBeamNaviateAdditionalRowsReady(topRows: false);

            string preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
            BeamNaviateAdditionalRowUi seed = BeamNaviateAddBottomRowsGrid?.SelectedItem as BeamNaviateAdditionalRowUi
                ?? _beamNaviateAdditionalBottomRows.LastOrDefault()
                ?? CreateBeamNaviateAdditionalDefaultRow(topRow: false);

            if (_beamNaviateAdditionalBottomRows.Count == 0)
            {
                _beamNaviateAdditionalBottomRows.Add(seed);
                if (BeamNaviateAddBottomRowsGrid != null)
                {
                    BeamNaviateAddBottomRowsGrid.SelectedItem = seed;
                    BeamNaviateAddBottomRowsGrid.ScrollIntoView(seed);
                }

                UpdateBeamRebarDesignUiState();
                return;
            }

            BeamNaviateAdditionalRowUi added = CloneBeamNaviateAdditionalRowUi(seed);
            if (string.IsNullOrWhiteSpace(added.BarTypeName))
            {
                added.BarTypeName = preferredType;
            }
            if (string.IsNullOrWhiteSpace(added.LayerText))
            {
                added.LayerText = "2";
            }
            if (string.IsNullOrWhiteSpace(added.AmountText))
            {
                added.AmountText = "2";
            }

            int spanCount = ResolveBeamNaviateInputSpanCount();
            int spanIndex = Math.Max(0, _beamNaviateAdditionalBottomRows.Count % Math.Max(1, spanCount));
            added.StartGridText = spanIndex.ToString(CultureInfo.InvariantCulture);
            added.EndGridText = (spanIndex + 1).ToString(CultureInfo.InvariantCulture);

            _beamNaviateAdditionalBottomRows.Add(added);
            if (BeamNaviateAddBottomRowsGrid != null)
            {
                BeamNaviateAddBottomRowsGrid.SelectedItem = added;
                BeamNaviateAddBottomRowsGrid.ScrollIntoView(added);
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateDeleteBottomRowClick(object sender, RoutedEventArgs e)
        {
            BeamNaviateAdditionalRowUi selected = BeamNaviateAddBottomRowsGrid?.SelectedItem as BeamNaviateAdditionalRowUi
                ?? _beamNaviateAdditionalBottomRows.LastOrDefault();
            if (selected == null)
            {
                return;
            }

            _beamNaviateAdditionalBottomRows.Remove(selected);
            EnsureBeamNaviateAdditionalRowsInitialized();
            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateAddTopRowClick(object sender, RoutedEventArgs e)
        {
            SetBeamNaviateAdditionalEnabled(topRows: true, enabled: true);
            EnsureBeamNaviateAdditionalRowsReady(topRows: true);

            string preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
            BeamNaviateAdditionalRowUi seed = BeamNaviateAddTopRowsGrid?.SelectedItem as BeamNaviateAdditionalRowUi
                ?? _beamNaviateAdditionalTopRows.LastOrDefault()
                ?? CreateBeamNaviateAdditionalDefaultRow(topRow: true);

            if (_beamNaviateAdditionalTopRows.Count == 0)
            {
                _beamNaviateAdditionalTopRows.Add(seed);
                if (BeamNaviateAddTopRowsGrid != null)
                {
                    BeamNaviateAddTopRowsGrid.SelectedItem = seed;
                    BeamNaviateAddTopRowsGrid.ScrollIntoView(seed);
                }

                UpdateBeamRebarDesignUiState();
                return;
            }

            BeamNaviateAdditionalRowUi added = CloneBeamNaviateAdditionalRowUi(seed);
            if (string.IsNullOrWhiteSpace(added.BarTypeName))
            {
                added.BarTypeName = preferredType;
            }
            if (string.IsNullOrWhiteSpace(added.LayerText))
            {
                added.LayerText = "2";
            }
            if (string.IsNullOrWhiteSpace(added.AmountText))
            {
                added.AmountText = "2";
            }

            int spanCount = ResolveBeamNaviateInputSpanCount();
            int supportNodeCount = Math.Max(2, spanCount + 1);
            int nodeIndex = Math.Max(0, _beamNaviateAdditionalTopRows.Count % supportNodeCount);
            added.StartGridText = nodeIndex.ToString(CultureInfo.InvariantCulture);
            added.EndGridText = nodeIndex.ToString(CultureInfo.InvariantCulture);

            _beamNaviateAdditionalTopRows.Add(added);
            if (BeamNaviateAddTopRowsGrid != null)
            {
                BeamNaviateAddTopRowsGrid.SelectedItem = added;
                BeamNaviateAddTopRowsGrid.ScrollIntoView(added);
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateDeleteTopRowClick(object sender, RoutedEventArgs e)
        {
            BeamNaviateAdditionalRowUi selected = BeamNaviateAddTopRowsGrid?.SelectedItem as BeamNaviateAdditionalRowUi
                ?? _beamNaviateAdditionalTopRows.LastOrDefault();
            if (selected == null)
            {
                return;
            }

            _beamNaviateAdditionalTopRows.Remove(selected);
            EnsureBeamNaviateAdditionalRowsInitialized();
            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateSpecialRowsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateSpecialRowsCurrentCellChanged(object sender, EventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            try
            {
                specialRowsGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                specialRowsGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateAddSpecialRowClick(object sender, RoutedEventArgs e)
        {
            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            BeamNaviateSpecialRowUi seed = specialRowsGrid?.SelectedItem as BeamNaviateSpecialRowUi
                ?? _beamNaviateSpecialRows.LastOrDefault()
                ?? CreateBeamNaviateSpecialDefaultRow();

            BeamNaviateSpecialRowUi added = CloneBeamNaviateSpecialRowUi(seed);
            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            int sectionIndex = ParseBeamNaviateIntOrDefault(seed.SectionText, 1, 1, 200);
            sectionIndex = Math.Max(1, Math.Min(sectionCount, sectionIndex));
            int maxCageInSection = _beamNaviateSpecialRows
                .Where(r => r != null && ParseBeamNaviateIntOrDefault(r.SectionText, 1, 1, 200) == sectionIndex)
                .Select(r => ParseBeamNaviateIntOrDefault(r.CageText, 1, 1, 200))
                .DefaultIfEmpty(0)
                .Max();
            added.SectionText = sectionIndex.ToString(CultureInfo.InvariantCulture);
            added.CageText = Math.Max(1, maxCageInSection + 1).ToString(CultureInfo.InvariantCulture);
            added.ModeText = NormalizeBeamNaviateSpecialModeText(added.ModeText);

            _beamNaviateSpecialRows.Add(added);
            if (specialRowsGrid != null)
            {
                specialRowsGrid.SelectedItem = added;
                specialRowsGrid.ScrollIntoView(added);
            }

            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateDeleteSpecialRowClick(object sender, RoutedEventArgs e)
        {
            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            BeamNaviateSpecialRowUi selected = specialRowsGrid?.SelectedItem as BeamNaviateSpecialRowUi
                ?? _beamNaviateSpecialRows.LastOrDefault();
            if (selected == null)
            {
                return;
            }

            _beamNaviateSpecialRows.Remove(selected);
            EnsureBeamNaviateSpecialRowsInitialized();
            UpdateBeamRebarDesignUiState();
        }

        private void OnBeamNaviateSpecialSetDefaultClick(object sender, RoutedEventArgs e)
        {
            ApplyBeamNaviateDefaultSpecialRows();
            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            if (specialRowsGrid != null && _beamNaviateSpecialRows.Count > 0)
            {
                specialRowsGrid.SelectedItem = _beamNaviateSpecialRows[0];
                specialRowsGrid.ScrollIntoView(_beamNaviateSpecialRows[0]);
            }

            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: special stirrup rows regenerated by section (U/Tie/Rect).");
        }

        private void OnBeamNaviateSpecialSectionCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RenderBeamNaviateSpecialSectionCanvas();
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            _beamNaviateSpecialSectionZoomFactor = Math.Max(0.2, Math.Min(8.0, _beamNaviateSpecialSectionZoomFactor * factor));
            RenderBeamNaviateSpecialSectionCanvas();
            e.Handled = true;
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            _beamNaviateSpecialSectionIsPanning = true;
            _beamNaviateSpecialSectionLastMousePoint = e.GetPosition(element);
            try
            {
                element.CaptureMouse();
            }
            catch
            {
            }

            e.Handled = true;
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(element);
            _beamNaviateSpecialSectionLastMouseCanvasPoint = p;
            if (_beamNaviateSpecialSectionIsPanning)
            {
                Vector delta = p - _beamNaviateSpecialSectionLastMousePoint;
                _beamNaviateSpecialSectionPanXPx += delta.X;
                _beamNaviateSpecialSectionPanYPx += delta.Y;
                _beamNaviateSpecialSectionLastMousePoint = p;
                RenderBeamNaviateSpecialSectionCanvas();
                return;
            }

            if (_beamNaviateSpecialSectionPendingStartGridNode.HasValue &&
                (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie ||
                 _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie))
            {
                RenderBeamNaviateSpecialSectionCanvas();
            }
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _beamNaviateSpecialSectionIsPanning = false;
            if (sender is UIElement element)
            {
                try
                {
                    element.ReleaseMouseCapture();
                }
                catch
                {
                }
            }

            e.Handled = true;
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _beamNaviateSpecialSectionLastMouseCanvasPoint = e.GetPosition(sender as IInputElement);
            if (!(sender is UIElement element))
            {
                return;
            }

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select)
            {
                return;
            }

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
            {
                _beamNaviateSpecialSectionPendingStartGridNode = null;
                _beamNaviateSpecialSectionIsLeftDrawDragging = false;
                return;
            }

            System.Windows.Point clickPoint = e.GetPosition(element);
            if (TryGetBeamNaviateSpecialSectionNodeTag(e.OriginalSource as DependencyObject, out int tagGridX, out int tagGridY))
            {
                if (TryHandleBeamNaviateSpecialSectionDrawNodeClick(element, clickPoint, tagGridX, tagGridY))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!TryGetBeamNaviateSpecialSectionNearestSnapNode(clickPoint, out int gridX, out int gridY, requireSnapRadius: false))
            {
                return;
            }

            if (TryHandleBeamNaviateSpecialSectionDrawNodeClick(element, clickPoint, gridX, gridY))
            {
                e.Handled = true;
            }
        }

        private void OnBeamNaviateSpecialSectionCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            if (_beamNaviateSpecialSectionIsLeftDrawDragging)
            {
                _beamNaviateSpecialSectionIsLeftDrawDragging = false;
                try
                {
                    element.ReleaseMouseCapture();
                }
                catch
                {
                }
            }

            _beamNaviateSpecialSectionLastMouseCanvasPoint = e.GetPosition(element);

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select ||
                _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
            {
                return;
            }

            if (!_beamNaviateSpecialSectionPendingStartGridNode.HasValue)
            {
                return;
            }

            if (!TryGetBeamNaviateSpecialSectionNearestSnapNode(e.GetPosition(element), out int gridX, out int gridY, requireSnapRadius: false))
            {
                return;
            }

            var startNode = _beamNaviateSpecialSectionPendingStartGridNode.Value;
            if (startNode.X == gridX && startNode.Y == gridY)
            {
                RenderBeamNaviateSpecialSectionCanvas();
                e.Handled = true;
                return;
            }

            _beamNaviateSpecialSectionPendingStartGridNode = null;

            bool changed = false;
            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie)
            {
                changed = AddBeamNaviateSpecialDrawTieRow(startNode.X, startNode.Y, gridX, gridY);
            }
            else if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie)
            {
                changed = AddBeamNaviateSpecialDrawRectRow(startNode.X, startNode.Y, gridX, gridY);
            }

            if (changed)
            {
                ShowStatus("BEAM: special section shape added.");
                UpdateBeamRebarDesignUiState();
            }
            else
            {
                RenderBeamNaviateSpecialSectionCanvas();
            }

            e.Handled = true;
        }

        private void OnBeamNaviateSpecialSectionModeChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton rb) || rb.IsChecked != true)
            {
                return;
            }

            string name = rb.Name ?? "";
            if (string.Equals(name, "BeamNaviateSpecialSectionModeDrawTieRadio", StringComparison.Ordinal))
            {
                _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.DrawTie;
            }
            else if (string.Equals(name, "BeamNaviateSpecialSectionModeDrawRectRadio", StringComparison.Ordinal))
            {
                _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.DrawRectTie;
            }
            else if (string.Equals(name, "BeamNaviateSpecialSectionModeDeleteRadio", StringComparison.Ordinal))
            {
                _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.Delete;
            }
            else
            {
                _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.Select;
            }

            _beamNaviateSpecialSectionPendingStartGridNode = null;
            _beamNaviateSpecialSectionIsLeftDrawDragging = false;
            RenderBeamNaviateSpecialSectionCanvas();
        }

        private void OnBeamNaviateSpecialSectionClearDrawnClick(object sender, RoutedEventArgs e)
        {
            int sectionIndex = ResolveBeamNaviateSpecialSectionIndexFromSelection();
            int removed = 0;
            for (int i = _beamNaviateSpecialCustomLineTies.Count - 1; i >= 0; i--)
            {
                BeamNaviateSpecialSectionLineTieUiSpec spec = _beamNaviateSpecialCustomLineTies[i];
                if (spec == null)
                {
                    continue;
                }

                if (Math.Max(1, spec.SectionIndex) != sectionIndex)
                {
                    continue;
                }

                _beamNaviateSpecialCustomLineTies.RemoveAt(i);
                removed++;
            }

            for (int i = _beamNaviateSpecialCustomRectTies.Count - 1; i >= 0; i--)
            {
                BeamNaviateSpecialSectionRectTieUiSpec spec = _beamNaviateSpecialCustomRectTies[i];
                if (spec == null)
                {
                    continue;
                }

                if (Math.Max(1, spec.SectionIndex) != sectionIndex)
                {
                    continue;
                }

                _beamNaviateSpecialCustomRectTies.RemoveAt(i);
                removed++;
            }

            if (removed == 0)
            {
                for (int i = _beamNaviateSpecialRows.Count - 1; i >= 0; i--)
                {
                    BeamNaviateSpecialRowUi row = _beamNaviateSpecialRows[i];
                    if (row == null)
                    {
                        continue;
                    }

                    if (ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200) != sectionIndex)
                    {
                        continue;
                    }

                    string mode = NormalizeBeamNaviateSpecialModeText(row.ModeText);
                    bool clearable = mode.IndexOf("tie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     mode.IndexOf("rect", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!clearable)
                    {
                        continue;
                    }

                    _beamNaviateSpecialRows.RemoveAt(i);
                    removed++;
                }

                EnsureBeamNaviateSpecialRowsInitialized();
            }

            _beamNaviateSpecialSectionSelectedShapeKey = "";
            _beamNaviateSpecialSectionPendingStartGridNode = null;
            _beamNaviateSpecialSectionIsLeftDrawDragging = false;
            if (removed > 0)
            {
                ShowStatus($"BEAM: cleared {removed} drawn special tie shape(s) in section {sectionIndex}.");
                UpdateBeamRebarDesignUiState();
            }
            else
            {
                RenderBeamNaviateSpecialSectionCanvas();
            }
        }

        private void OnBeamNaviateSpecialSectionFitClick(object sender, RoutedEventArgs e)
        {
            _beamNaviateSpecialSectionZoomFactor = 1.0;
            _beamNaviateSpecialSectionPanXPx = 0.0;
            _beamNaviateSpecialSectionPanYPx = 0.0;
            _beamNaviateSpecialSectionPendingStartGridNode = null;
            _beamNaviateSpecialSectionIsLeftDrawDragging = false;
            RenderBeamNaviateSpecialSectionCanvas();
        }

        private void OnBeamNaviateSpecialSectionRowShapeClick(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || fe.Tag == null)
            {
                return;
            }

            string tag = fe.Tag.ToString() ?? "";
            if (tag.StartsWith("BSC:L:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(tag.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineIndex))
                {
                    return;
                }

                if (lineIndex < 0 || lineIndex >= _beamNaviateSpecialCustomLineTies.Count)
                {
                    return;
                }

                BeamNaviateSpecialSectionLineTieUiSpec spec = _beamNaviateSpecialCustomLineTies[lineIndex];
                if (spec == null)
                {
                    return;
                }

                string key = GetBeamNaviateSpecialLineTieKey(spec);
                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
                {
                    _beamNaviateSpecialCustomLineTies.RemoveAt(lineIndex);
                    if (string.Equals(_beamNaviateSpecialSectionSelectedShapeKey, key, StringComparison.Ordinal))
                    {
                        _beamNaviateSpecialSectionSelectedShapeKey = "";
                    }

                    UpdateBeamRebarDesignUiState();
                    e.Handled = true;
                    return;
                }

                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select)
                {
                    _beamNaviateSpecialSectionSelectedShapeKey = key;
                    EnsureBeamNaviateSpecialRowExistsForSection(spec.SectionIndex);
                    UpdateBeamRebarDesignUiState();
                    e.Handled = true;
                }

                return;
            }

            if (tag.StartsWith("BSC:R:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(tag.Substring(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rectIndex))
                {
                    return;
                }

                if (rectIndex < 0 || rectIndex >= _beamNaviateSpecialCustomRectTies.Count)
                {
                    return;
                }

                BeamNaviateSpecialSectionRectTieUiSpec spec = _beamNaviateSpecialCustomRectTies[rectIndex];
                if (spec == null)
                {
                    return;
                }

                string key = GetBeamNaviateSpecialRectTieKey(spec);
                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
                {
                    _beamNaviateSpecialCustomRectTies.RemoveAt(rectIndex);
                    if (string.Equals(_beamNaviateSpecialSectionSelectedShapeKey, key, StringComparison.Ordinal))
                    {
                        _beamNaviateSpecialSectionSelectedShapeKey = "";
                    }

                    UpdateBeamRebarDesignUiState();
                    e.Handled = true;
                    return;
                }

                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select)
                {
                    _beamNaviateSpecialSectionSelectedShapeKey = key;
                    EnsureBeamNaviateSpecialRowExistsForSection(spec.SectionIndex);
                    UpdateBeamRebarDesignUiState();
                    e.Handled = true;
                }

                return;
            }

            if (!tag.StartsWith("BSR:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!int.TryParse(tag.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rowIndex))
            {
                return;
            }

            if (rowIndex < 0 || rowIndex >= _beamNaviateSpecialRows.Count)
            {
                return;
            }

            BeamNaviateSpecialRowUi row = _beamNaviateSpecialRows[rowIndex];
            if (row == null)
            {
                return;
            }

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
            {
                _beamNaviateSpecialRows.RemoveAt(rowIndex);
                EnsureBeamNaviateSpecialRowsInitialized();
                _beamNaviateSpecialSectionSelectedShapeKey = "";
                UpdateBeamRebarDesignUiState();
                e.Handled = true;
                return;
            }

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select)
            {
                _beamNaviateSpecialSectionSelectedShapeKey = "";
                SelectBeamNaviateSpecialRow(row);
                UpdateBeamRebarDesignUiState();
                e.Handled = true;
            }
        }

        private void OnBeamNaviateSpecialSectionNodeHitClick(object sender, MouseButtonEventArgs e)
        {
            if (_beamNaviateSpecialSectionEditMode != BeamNaviateSpecialSectionEditMode.DrawTie &&
                _beamNaviateSpecialSectionEditMode != BeamNaviateSpecialSectionEditMode.DrawRectTie)
            {
                return;
            }

            if (!(sender is FrameworkElement fe))
            {
                return;
            }

            if (!TryGetBeamNaviateSpecialSectionNodeTag(fe, out int gridX, out int gridY))
            {
                return;
            }

            Canvas canvas = GetBeamNaviateSpecialSectionCanvas();
            if (canvas == null)
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(canvas);
            _beamNaviateSpecialSectionLastMouseCanvasPoint = p;
            if (TryHandleBeamNaviateSpecialSectionDrawNodeClick(canvas, p, gridX, gridY))
            {
                e.Handled = true;
            }
        }

        private bool TryHandleBeamNaviateSpecialSectionDrawNodeClick(UIElement element, System.Windows.Point clickPoint, int gridX, int gridY)
        {
            if (element == null)
            {
                return false;
            }

            if (_beamNaviateSpecialSectionEditMode != BeamNaviateSpecialSectionEditMode.DrawTie &&
                _beamNaviateSpecialSectionEditMode != BeamNaviateSpecialSectionEditMode.DrawRectTie)
            {
                return false;
            }

            if (!_beamNaviateSpecialSectionPendingStartGridNode.HasValue)
            {
                _beamNaviateSpecialSectionPendingStartGridNode = (gridX, gridY);
                _beamNaviateSpecialSectionIsLeftDrawDragging = true;
                _beamNaviateSpecialSectionLeftDrawStartCanvasPoint = clickPoint;
                try
                {
                    element.CaptureMouse();
                }
                catch
                {
                }

                RenderBeamNaviateSpecialSectionCanvas();
                return true;
            }

            var startNode = _beamNaviateSpecialSectionPendingStartGridNode.Value;
            if (startNode.X == gridX && startNode.Y == gridY)
            {
                RenderBeamNaviateSpecialSectionCanvas();
                return true;
            }

            _beamNaviateSpecialSectionPendingStartGridNode = null;
            _beamNaviateSpecialSectionIsLeftDrawDragging = false;
            try
            {
                element.ReleaseMouseCapture();
            }
            catch
            {
            }

            bool changed = false;
            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie)
            {
                changed = AddBeamNaviateSpecialDrawTieRow(startNode.X, startNode.Y, gridX, gridY);
            }
            else if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie)
            {
                changed = AddBeamNaviateSpecialDrawRectRow(startNode.X, startNode.Y, gridX, gridY);
            }

            if (changed)
            {
                UpdateBeamRebarDesignUiState();
            }
            else
            {
                RenderBeamNaviateSpecialSectionCanvas();
            }

            return true;
        }

        private static bool TryGetBeamNaviateSpecialSectionNodeTag(DependencyObject source, out int gridX, out int gridY)
        {
            gridX = -1;
            gridY = -1;
            if (!(source is FrameworkElement fe) || fe.Tag == null)
            {
                return false;
            }

            string tag = fe.Tag.ToString() ?? "";
            string[] parts = tag.Split(':');
            if (parts.Length != 3 ||
                !string.Equals(parts[0], "BSN", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out gridX) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out gridY))
            {
                gridX = -1;
                gridY = -1;
                return false;
            }

            return true;
        }

        private bool TryGetBeamNaviateSpecialSectionNearestSnapNode(
            System.Windows.Point canvasPoint,
            out int gridXIndex,
            out int gridYIndex,
            bool requireSnapRadius)
        {
            gridXIndex = -1;
            gridYIndex = -1;
            BeamNaviateSpecialSectionGridRenderState state = _beamNaviateSpecialSectionGridRenderState;
            if (state == null || state.GridXPx.Count == 0 || state.GridYPx.Count == 0)
            {
                return false;
            }

            double bestDist2 = double.MaxValue;
            int bestX = -1;
            int bestY = -1;
            for (int ix = 0; ix < state.GridXPx.Count; ix++)
            {
                double x = state.GridXPx[ix];
                for (int iy = 0; iy < state.GridYPx.Count; iy++)
                {
                    double y = state.GridYPx[iy];
                    double dx = canvasPoint.X - x;
                    double dy = canvasPoint.Y - y;
                    double d2 = (dx * dx) + (dy * dy);
                    if (d2 < bestDist2)
                    {
                        bestDist2 = d2;
                        bestX = ix;
                        bestY = iy;
                    }
                }
            }

            const double snapRadiusPx = 44.0;
            if (bestX < 0 || bestY < 0)
            {
                return false;
            }

            if (requireSnapRadius && bestDist2 > (snapRadiusPx * snapRadiusPx))
            {
                return false;
            }

            gridXIndex = bestX;
            gridYIndex = bestY;
            return true;
        }

        private static bool TryGetBeamNaviateSpecialSectionNodePixel(
            BeamNaviateSpecialSectionGridRenderState state,
            int xIndex,
            int yIndex,
            out double x,
            out double y)
        {
            x = 0.0;
            y = 0.0;
            if (state == null ||
                xIndex < 0 ||
                yIndex < 0 ||
                xIndex >= state.GridXPx.Count ||
                yIndex >= state.GridYPx.Count)
            {
                return false;
            }

            x = state.GridXPx[xIndex];
            y = state.GridYPx[yIndex];
            return true;
        }

        private static void NormalizeBeamNaviateSpecialSectionLineToOrthogonal(
            int startX,
            int startY,
            int endX,
            int endY,
            out int normalizedEndX,
            out int normalizedEndY)
        {
            normalizedEndX = endX;
            normalizedEndY = endY;

            int dx = Math.Abs(endX - startX);
            int dy = Math.Abs(endY - startY);
            if (dx == 0 || dy == 0)
            {
                return;
            }

            if (dx >= dy)
            {
                normalizedEndY = startY;
            }
            else
            {
                normalizedEndX = startX;
            }
        }

        private bool AddBeamNaviateSpecialDrawTieRow(int x0, int y0, int x1, int y1)
        {
            NormalizeBeamNaviateSpecialSectionLineToOrthogonal(x0, y0, x1, y1, out x1, out y1);
            if (x0 == x1 && y0 == y1)
            {
                return false;
            }

            int sectionIndex = ResolveBeamNaviateSpecialSectionIndexFromSelection();
            BeamNaviateSpecialSectionGridRenderState state = _beamNaviateSpecialSectionGridRenderState;
            int xMax = Math.Max(0, (state?.GridXPx.Count ?? 1) - 1);
            int yMax = Math.Max(0, (state?.GridYPx.Count ?? 1) - 1);
            x0 = Math.Max(0, Math.Min(xMax, x0));
            x1 = Math.Max(0, Math.Min(xMax, x1));
            y0 = Math.Max(0, Math.Min(yMax, y0));
            y1 = Math.Max(0, Math.Min(yMax, y1));
            if (x0 == x1 && y0 == y1)
            {
                return false;
            }

            var spec = new BeamNaviateSpecialSectionLineTieUiSpec
            {
                SectionIndex = Math.Max(1, sectionIndex),
                X0GridIndex = x0,
                Y0GridIndex = y0,
                X1GridIndex = x1,
                Y1GridIndex = y1
            };
            NormalizeBeamNaviateSpecialLineTieSpec(spec);
            if (spec.X0GridIndex == spec.X1GridIndex && spec.Y0GridIndex == spec.Y1GridIndex)
            {
                return false;
            }

            string key = GetBeamNaviateSpecialLineTieKey(spec);
            BeamNaviateSpecialSectionLineTieUiSpec existing = _beamNaviateSpecialCustomLineTies.FirstOrDefault(x =>
                x != null &&
                string.Equals(GetBeamNaviateSpecialLineTieKey(x), key, StringComparison.Ordinal));
            if (existing != null)
            {
                _beamNaviateSpecialSectionSelectedShapeKey = key;
                EnsureBeamNaviateSpecialRowExistsForSection(existing.SectionIndex);
                return false;
            }

            _beamNaviateSpecialCustomLineTies.Add(spec);
            _beamNaviateSpecialSectionSelectedShapeKey = key;
            EnsureBeamNaviateSpecialRowExistsForSection(spec.SectionIndex);
            return true;
        }

        private bool AddBeamNaviateSpecialDrawRectRow(int x0, int y0, int x1, int y1)
        {
            int sectionIndex = ResolveBeamNaviateSpecialSectionIndexFromSelection();
            BeamNaviateSpecialSectionGridRenderState state = _beamNaviateSpecialSectionGridRenderState;
            int xMax = Math.Max(0, (state?.GridXPx.Count ?? 1) - 1);
            int yMax = Math.Max(0, (state?.GridYPx.Count ?? 1) - 1);
            int minX = Math.Max(0, Math.Min(xMax, Math.Min(x0, x1)));
            int maxX = Math.Max(0, Math.Min(xMax, Math.Max(x0, x1)));
            int minY = Math.Max(0, Math.Min(yMax, Math.Min(y0, y1)));
            int maxY = Math.Max(0, Math.Min(yMax, Math.Max(y0, y1)));
            if (minX >= maxX || minY >= maxY)
            {
                return false;
            }

            var spec = new BeamNaviateSpecialSectionRectTieUiSpec
            {
                SectionIndex = Math.Max(1, sectionIndex),
                X0GridIndex = minX,
                Y0GridIndex = minY,
                X1GridIndex = maxX,
                Y1GridIndex = maxY
            };
            string key = GetBeamNaviateSpecialRectTieKey(spec);
            BeamNaviateSpecialSectionRectTieUiSpec existing = _beamNaviateSpecialCustomRectTies.FirstOrDefault(x =>
                x != null &&
                string.Equals(GetBeamNaviateSpecialRectTieKey(x), key, StringComparison.Ordinal));
            if (existing != null)
            {
                _beamNaviateSpecialSectionSelectedShapeKey = key;
                EnsureBeamNaviateSpecialRowExistsForSection(existing.SectionIndex);
                return false;
            }

            _beamNaviateSpecialCustomRectTies.Add(spec);
            _beamNaviateSpecialSectionSelectedShapeKey = key;
            EnsureBeamNaviateSpecialRowExistsForSection(spec.SectionIndex);
            return true;
        }

        private void SortBeamNaviateSpecialRows()
        {
            List<BeamNaviateSpecialRowUi> sorted = _beamNaviateSpecialRows
                .Where(row => row != null)
                .OrderBy(row => ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200))
                .ThenBy(row => ParseBeamNaviateIntOrDefault(row.CageText, 1, 1, 200))
                .ThenBy(row => GetBeamNaviateSpecialModeLaneIndex(row.ModeText))
                .ToList();

            _beamNaviateSpecialRows.Clear();
            foreach (BeamNaviateSpecialRowUi row in sorted)
            {
                _beamNaviateSpecialRows.Add(row);
            }
        }

        private void SelectBeamNaviateSpecialRow(BeamNaviateSpecialRowUi row)
        {
            DataGrid grid = GetBeamNaviateSpecialRowsGrid();
            if (grid == null || row == null)
            {
                return;
            }

            grid.SelectedItem = row;
            grid.ScrollIntoView(row);
        }

        private void EnsureBeamNaviateSpecialRowExistsForSection(int sectionIndex)
        {
            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            int safeSection = Math.Max(1, Math.Min(sectionCount, sectionIndex));
            BeamNaviateSpecialRowUi existing = _beamNaviateSpecialRows.FirstOrDefault(row =>
                row != null &&
                ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200) == safeSection);
            if (existing != null)
            {
                SelectBeamNaviateSpecialRow(existing);
                return;
            }

            BeamNaviateSpecialRowUi added = CreateBeamNaviateSpecialDefaultRow(safeSection);
            string spacing = (BeamNaviateSpecialSpacingTextBox?.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(spacing))
            {
                added.SpacingText = spacing;
            }

            string zone = (BeamNaviateSpecialZoneLengthTextBox?.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(zone))
            {
                added.StartZoneText = zone;
                added.EndZoneText = zone;
            }

            _beamNaviateSpecialRows.Add(added);
            SortBeamNaviateSpecialRows();
            SelectBeamNaviateSpecialRow(added);
        }

        private static void NormalizeBeamNaviateSpecialLineTieSpec(BeamNaviateSpecialSectionLineTieUiSpec spec)
        {
            if (spec == null)
            {
                return;
            }

            int x0 = Math.Max(0, spec.X0GridIndex);
            int y0 = Math.Max(0, spec.Y0GridIndex);
            int x1 = Math.Max(0, spec.X1GridIndex);
            int y1 = Math.Max(0, spec.Y1GridIndex);
            NormalizeBeamNaviateSpecialSectionLineToOrthogonal(x0, y0, x1, y1, out x1, out y1);

            if (x0 == x1 && y0 > y1)
            {
                int tmp = y0;
                y0 = y1;
                y1 = tmp;
            }
            else if (y0 == y1 && x0 > x1)
            {
                int tmp = x0;
                x0 = x1;
                x1 = tmp;
            }

            spec.SectionIndex = Math.Max(1, spec.SectionIndex);
            spec.X0GridIndex = x0;
            spec.Y0GridIndex = y0;
            spec.X1GridIndex = x1;
            spec.Y1GridIndex = y1;
        }

        private static string GetBeamNaviateSpecialLineTieKey(BeamNaviateSpecialSectionLineTieUiSpec spec)
        {
            if (spec == null)
            {
                return "";
            }

            BeamNaviateSpecialSectionLineTieUiSpec normalized = CloneBeamNaviateSpecialLineTieUiSpec(spec);
            NormalizeBeamNaviateSpecialLineTieSpec(normalized);
            return string.Join(
                ":",
                "L",
                Math.Max(1, normalized.SectionIndex),
                Math.Max(0, normalized.X0GridIndex),
                Math.Max(0, normalized.Y0GridIndex),
                Math.Max(0, normalized.X1GridIndex),
                Math.Max(0, normalized.Y1GridIndex));
        }

        private static string GetBeamNaviateSpecialRectTieKey(BeamNaviateSpecialSectionRectTieUiSpec spec)
        {
            if (spec == null)
            {
                return "";
            }

            int x0 = Math.Max(0, Math.Min(spec.X0GridIndex, spec.X1GridIndex));
            int x1 = Math.Max(0, Math.Max(spec.X0GridIndex, spec.X1GridIndex));
            int y0 = Math.Max(0, Math.Min(spec.Y0GridIndex, spec.Y1GridIndex));
            int y1 = Math.Max(0, Math.Max(spec.Y0GridIndex, spec.Y1GridIndex));
            return string.Join(":", "R", Math.Max(1, spec.SectionIndex), x0, y0, x1, y1);
        }

        private int ResolveBeamNaviateSpecialSectionIndexFromSelection()
        {
            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            BeamNaviateSpecialRowUi selected = GetBeamNaviateSpecialRowsGrid()?.SelectedItem as BeamNaviateSpecialRowUi
                ?? _beamNaviateSpecialRows.FirstOrDefault();
            int section = ParseBeamNaviateIntOrDefault(selected?.SectionText, 1, 1, 200);
            return Math.Max(1, Math.Min(sectionCount, section));
        }

        private void OnBeamNaviateSettingSaveClick(object sender, RoutedEventArgs e)
        {
            CommitBeamNaviateAdditionalRowEdits();
            AutoFillBeamNaviateAdditionalFrozenColumns();
            SyncBeamNaviateStirrupOverridesFromRows();

            string settingName = GetBeamNaviateSettingName();
            if (string.Equals(settingName, "<In Session>", StringComparison.OrdinalIgnoreCase))
            {
                settingName = "Setting " + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            }

            _beamNaviateSettingsByName[settingName] = CaptureBeamNaviateSnapshotFromUi(settingName);
            PersistBeamNaviateSettingsToDisk();
            RefreshBeamNaviateSettingComboItems(settingName);
            ShowStatus($"BEAM: setting '{settingName}' saved.");
        }

        private void OnBeamNaviateSettingLoadClick(object sender, RoutedEventArgs e)
        {
            ApplySelectedBeamNaviateSetting();
        }

        private void OnBeamNaviateSettingDeleteClick(object sender, RoutedEventArgs e)
        {
            string settingName = GetBeamNaviateSettingName();
            if (string.Equals(settingName, "<In Session>", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("BEAM: '<In Session>' cannot be deleted.");
                return;
            }

            if (_beamNaviateSettingsByName.Remove(settingName))
            {
                PersistBeamNaviateSettingsToDisk();
                RefreshBeamNaviateSettingComboItems("<In Session>");
                ShowStatus($"BEAM: setting '{settingName}' deleted.");
                return;
            }

            ShowStatus($"BEAM: setting '{settingName}' not found.");
        }

        private void OnBeamNaviateSettingCopyClick(object sender, RoutedEventArgs e)
        {
            CommitBeamNaviateAdditionalRowEdits();
            AutoFillBeamNaviateAdditionalFrozenColumns();
            SyncBeamNaviateStirrupOverridesFromRows();

            string sourceName = GetBeamNaviateSettingName();
            if (!_beamNaviateSettingsByName.TryGetValue(sourceName, out BeamNaviateSettingSnapshot source))
            {
                source = CaptureBeamNaviateSnapshotFromUi(sourceName);
            }

            string baseName = string.Equals(sourceName, "<In Session>", StringComparison.OrdinalIgnoreCase)
                ? "Setting"
                : sourceName;

            string candidate = baseName + " Copy";
            int suffix = 2;
            while (_beamNaviateSettingsByName.ContainsKey(candidate))
            {
                candidate = baseName + " Copy " + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            BeamNaviateSettingSnapshot clone = CloneBeamNaviateSettingSnapshot(source);
            clone.Name = candidate;
            _beamNaviateSettingsByName[candidate] = clone;
            PersistBeamNaviateSettingsToDisk();
            RefreshBeamNaviateSettingComboItems(candidate);
            ShowStatus($"BEAM: setting copied to '{candidate}'.");
        }

        private void OnBeamNaviatePreviewClick(object sender, RoutedEventArgs e)
        {
            FlushBeamNaviatePreviewRender();
            ShowStatus("BEAM: preview updated.");
        }

        private void OnBeamNaviateInfoClick(object sender, RoutedEventArgs e)
        {
            int hostCount = _beamRebarSelectedHostElementIds.Count;
            string settingName = GetBeamNaviateSettingName();
            ShowStatus($"BEAM: Naviate data model ready. Hosts: {hostCount}. Setting: {settingName}.");
        }

        private void OnBeamNaviatePreviewCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_beamNaviateUiEventsSuspended)
            {
                return;
            }

            QueueBeamNaviatePreviewRender();
        }

        private void OnBeamNaviatePreviewCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            _beamNaviatePreviewZoomFactor = Math.Max(0.6, Math.Min(2.8, _beamNaviatePreviewZoomFactor + delta));
            QueueBeamNaviatePreviewRender();
            e.Handled = true;
        }

        private void OnBeamNaviatePreviewCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Canvas canvas = BeamNaviatePreviewCanvas;
            if (canvas == null || _beamNaviatePreviewSpans.Count == 0)
            {
                return;
            }

            System.Windows.Point pt = e.GetPosition(canvas);
            BeamPreviewSpanUi selected = _beamNaviatePreviewSpans
                .FirstOrDefault(s => pt.X >= Math.Min(s.StartX, s.EndX) && pt.X <= Math.Max(s.StartX, s.EndX))
                ?? _beamNaviatePreviewSpans
                    .OrderBy(s =>
                    {
                        double cx = (s.StartX + s.EndX) * 0.5;
                        return Math.Abs(cx - pt.X);
                    })
                    .FirstOrDefault();
            if (selected == null)
            {
                return;
            }

            if (_beamNaviateSelectedPreviewSpanIndex == selected.SpanIndex)
            {
                _beamNaviateSelectedPreviewSpanIndex = -1;
                ApplyBeamNaviateStirrupInputsForSelectedSpan();
                SelectBeamNaviateStirrupGridRowBySpan(-1);
                ShowStatus("BEAM: stirrup input target -> ALL spans.");
            }
            else
            {
                _beamNaviateSelectedPreviewSpanIndex = selected.SpanIndex;
                ApplyBeamNaviateStirrupInputsForSelectedSpan();
                SelectBeamNaviateStirrupGridRowBySpan(_beamNaviateSelectedPreviewSpanIndex);
                ShowStatus($"BEAM: stirrup input target -> span {_beamNaviateSelectedPreviewSpanIndex + 1}.");
            }

            QueueBeamNaviatePreviewRender();
            e.Handled = true;
        }

        private void OnBeamNaviateReadSettingClick(object sender, RoutedEventArgs e)
        {
            ApplySelectedBeamNaviateSetting();
        }

        private void OnBeamNaviateDeleteRebarClick(object sender, RoutedEventArgs e)
        {
            TryRaiseBeamNaviateGenerateRequest(deleteOnly: true);
        }

        private void OnBeamNaviateApplyClick(object sender, RoutedEventArgs e)
        {
            TryRaiseBeamNaviateGenerateRequest(deleteOnly: false);
        }

        private void OnBeamNaviateCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnBeamNaviateUserManualClick(object sender, RoutedEventArgs e)
        {
            ShowStatus("BEAM workflow: Pick Beam -> Main Bar -> Stirrup -> Additional Bottom/Top -> Secondary Beam -> Support (Column/Wall/Foundation) -> OK.");
        }

        private void OnBeamNaviateQuickSettingClick(object sender, RoutedEventArgs e)
        {
            SelectBeamNaviateTabByHeader("Main Bar");
            if (BeamNaviateSettingCombo != null)
            {
                BeamNaviateSettingCombo.Focus();
                BeamNaviateSettingCombo.IsDropDownOpen = true;
            }

            ShowStatus("BEAM: SETTING mode. Choose profile or keep <In Session>.");
        }

        private void OnBeamNaviateQuickSecondaryBeamClick(object sender, RoutedEventArgs e)
        {
            SelectBeamNaviateTabByHeader("Secondary Beam");
            if (BeamNaviateSecondaryEnabledCheckBox != null)
            {
                BeamNaviateSecondaryEnabledCheckBox.IsChecked = true;
            }

            ShowStatus("BEAM: SECONDARY BEAM mode. Select secondary reference beam(s).");
            _handler.Request.RequestType = CadToModelRequestType.PickBeamRebarSecondaryHosts;
            _externalEvent.Raise();
        }

        private void OnBeamNaviateQuickSupportBeamClick(object sender, RoutedEventArgs e)
        {
            SelectBeamNaviateTabByHeader("Additional Top Bar");
            if (BeamNaviateStirrupEnabledCheckBox != null)
            {
                BeamNaviateStirrupEnabledCheckBox.IsChecked = true;
            }
            TrySetBeamNaviateComboSelection(BeamNaviateStirrupLayoutCombo, "L/4-L/2-L/4");
            TrySetBeamNaviateComboSelection(GetBeamNaviateStirrupZoneInputModeCombo(), "Follow Additional Top Bar");
            _beamNaviatePendingSupportDefaults = true;
            if (_beamRebarSelectedSupportElementIds.Count > 0)
            {
                _beamNaviatePendingSupportDefaults = false;
                ApplyBeamNaviateSupportSpanDefaults();
            }

            ShowStatus("BEAM: SUPPORT mode. Select support Column/Wall/Foundation; top bar defaults use 0.3L / 0.4L / 0.3L per span.");
            _handler.Request.RequestType = CadToModelRequestType.PickBeamRebarSupportHosts;
            _externalEvent.Raise();
        }

        private void ApplyBeamNaviateSupportSpanDefaults()
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                if (BeamNaviateAddTopEnabledCheckBox != null)
                {
                    SetBeamNaviateAdditionalEnabled(topRows: true, enabled: true);
                }
                if (BeamNaviateStirrupEnabledCheckBox != null)
                {
                    BeamNaviateStirrupEnabledCheckBox.IsChecked = true;
                }
                TrySetBeamNaviateComboSelection(BeamNaviateStirrupLayoutCombo, "L/4-L/2-L/4");
                TrySetBeamNaviateComboSelection(GetBeamNaviateStirrupZoneInputModeCombo(), "Follow Additional Top Bar");

                double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
                List<double> stationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
                List<double> spanBreaksMm = new List<double>();
                if (hostLengthMm > 1.0 && !double.IsNaN(hostLengthMm) && !double.IsInfinity(hostLengthMm))
                {
                    double tolMm = Math.Max(120.0, hostLengthMm * 0.01);
                    spanBreaksMm.Add(0.0);
                    spanBreaksMm.AddRange(stationsMm.Where(s => s > tolMm && s < (hostLengthMm - tolMm)));
                    spanBreaksMm.Add(hostLengthMm);
                }

                string preferredType = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(preferredType))
                {
                    preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
                }

                int amount = ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40);
                List<BeamNaviateAdditionalRowUi> supportRows = BuildBeamNaviateSupportLinkedTopRows(spanBreaksMm, preferredType, amount, spanRatio: 0.30);
                if (supportRows.Count == 0)
                {
                    supportRows.Add(CreateBeamNaviateAdditionalDefaultRow(topRow: true));
                }

                ReplaceBeamNaviateAdditionalRows(_beamNaviateAdditionalTopRows, supportRows, topRows: true);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalTopRows, preferredType);
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: support top-bar defaults applied (span lengths 0.3L / 0.4L / 0.3L).");
        }

        private static List<BeamNaviateAdditionalRowUi> BuildBeamNaviateSupportLinkedTopRows(
            IList<double> spanBreaksMm,
            string barTypeName,
            int amount,
            double spanRatio = 0.30)
        {
            var rows = new List<BeamNaviateAdditionalRowUi>();
            if (spanBreaksMm == null || spanBreaksMm.Count < 2)
            {
                return rows;
            }

            var breaks = spanBreaksMm
                .Where(x => x >= 0.0 && !double.IsNaN(x) && !double.IsInfinity(x))
                .OrderBy(x => x)
                .ToList();
            if (breaks.Count < 2)
            {
                return rows;
            }

            var dedup = new List<double>();
            double dedupTolMm = 1.0;
            foreach (double s in breaks)
            {
                if (dedup.Count == 0 || Math.Abs(s - dedup[dedup.Count - 1]) > dedupTolMm)
                {
                    dedup.Add(s);
                }
            }
            if (dedup.Count < 2)
            {
                return rows;
            }

            string safeType = string.IsNullOrWhiteSpace(barTypeName) ? "DB12" : barTypeName.Trim();
            int safeAmount = Math.Max(1, amount);
            double ratio = Math.Max(0.05, Math.Min(0.45, spanRatio));
            for (int span = 0; span < dedup.Count - 1; span++)
            {
                double spanLengthMm = Math.Max(0.0, dedup[span + 1] - dedup[span]);
                if (spanLengthMm <= 1.0)
                {
                    continue;
                }

                rows.Add(new BeamNaviateAdditionalRowUi
                {
                    LayerText = "2",
                    BarTypeName = safeType,
                    AmountText = safeAmount.ToString(CultureInfo.InvariantCulture),
                    StartGridText = span.ToString(CultureInfo.InvariantCulture),
                    EndGridText = span.ToString(CultureInfo.InvariantCulture),
                    StartLengthText = "0L",
                    EndLengthText = string.Format(CultureInfo.InvariantCulture, "{0:0.###}L", ratio),
                    StartFactorText = "0",
                    EndFactorText = FormatBeamNaviateFactorCellText(ratio)
                });

                rows.Add(new BeamNaviateAdditionalRowUi
                {
                    LayerText = "2",
                    BarTypeName = safeType,
                    AmountText = safeAmount.ToString(CultureInfo.InvariantCulture),
                    StartGridText = (span + 1).ToString(CultureInfo.InvariantCulture),
                    EndGridText = (span + 1).ToString(CultureInfo.InvariantCulture),
                    StartLengthText = string.Format(CultureInfo.InvariantCulture, "{0:0.###}L", ratio),
                    EndLengthText = "0L",
                    StartFactorText = FormatBeamNaviateFactorCellText(ratio),
                    EndFactorText = "0"
                });
            }

            return rows;
        }

        private static List<BeamNaviateAdditionalRowUi> BuildBeamNaviateSupportLinkedBottomRows(
            IList<double> spanBreaksMm,
            string barTypeName,
            int amount,
            double spanEndRatio = 0.125)
        {
            var rows = new List<BeamNaviateAdditionalRowUi>();
            if (spanBreaksMm == null || spanBreaksMm.Count < 2)
            {
                return rows;
            }

            var breaks = spanBreaksMm
                .Where(x => x >= 0.0 && !double.IsNaN(x) && !double.IsInfinity(x))
                .OrderBy(x => x)
                .ToList();
            if (breaks.Count < 2)
            {
                return rows;
            }

            var dedup = new List<double>();
            double dedupTolMm = 1.0;
            foreach (double s in breaks)
            {
                if (dedup.Count == 0 || Math.Abs(s - dedup[dedup.Count - 1]) > dedupTolMm)
                {
                    dedup.Add(s);
                }
            }
            if (dedup.Count < 2)
            {
                return rows;
            }

            string safeType = string.IsNullOrWhiteSpace(barTypeName) ? "DB12" : barTypeName.Trim();
            int safeAmount = Math.Max(1, amount);
            double ratio = Math.Max(0.05, Math.Min(0.30, spanEndRatio));
            for (int span = 0; span < dedup.Count - 1; span++)
            {
                double spanLengthMm = Math.Max(0.0, dedup[span + 1] - dedup[span]);
                if (spanLengthMm <= 1.0)
                {
                    continue;
                }

                rows.Add(new BeamNaviateAdditionalRowUi
                {
                    LayerText = "2",
                    BarTypeName = safeType,
                    AmountText = safeAmount.ToString(CultureInfo.InvariantCulture),
                    StartGridText = span.ToString(CultureInfo.InvariantCulture),
                    EndGridText = (span + 1).ToString(CultureInfo.InvariantCulture),
                    StartLengthText = string.Format(CultureInfo.InvariantCulture, "{0:0.###}L", ratio),
                    EndLengthText = string.Format(CultureInfo.InvariantCulture, "{0:0.###}L", ratio),
                    StartFactorText = FormatBeamNaviateFactorCellText(ratio),
                    EndFactorText = FormatBeamNaviateFactorCellText(ratio)
                });
            }

            return rows;
        }

        private void OnBeamNaviateQuickOkClick(object sender, RoutedEventArgs e)
        {
            OnBeamNaviateApplyClick(sender, e);
        }

        private void OnBeamNaviateStirrupSetAllClick(object sender, RoutedEventArgs e)
        {
            if (BeamNaviateStirrupStartSpacingTextBox != null &&
                BeamNaviateStirrupMiddleSpacingTextBox != null &&
                BeamNaviateStirrupEndSpacingTextBox != null)
            {
                string v = (BeamNaviateStirrupMiddleSpacingTextBox.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(v))
                {
                    v = "200 mm";
                }

                BeamNaviateStirrupStartSpacingTextBox.Text = v;
                BeamNaviateStirrupMiddleSpacingTextBox.Text = v;
                BeamNaviateStirrupEndSpacingTextBox.Text = v;
            }

            _beamNaviateSelectedPreviewSpanIndex = -1;
            _beamNaviateStirrupSpanOverrides.Clear();
            InitializeBeamNaviateStirrupGlobalTextsFromUi();
            SyncBeamNaviateStirrupRowsFromState();
            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: stirrup spacing applied to all spans.");
        }

        private void OnBeamNaviateStirrupSetDefaultClick(object sender, RoutedEventArgs e)
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                TrySetBeamNaviateComboSelection(BeamNaviateStirrupLayoutCombo, "L/4-L/2-L/4");
                TrySetBeamNaviateComboSelection(GetBeamNaviateStirrupZoneInputModeCombo(), "Follow Additional Top Bar");
                if (BeamNaviateStirrupStartSpacingTextBox != null) BeamNaviateStirrupStartSpacingTextBox.Text = "150 mm";
                if (BeamNaviateStirrupMiddleSpacingTextBox != null) BeamNaviateStirrupMiddleSpacingTextBox.Text = "200 mm";
                if (BeamNaviateStirrupEndSpacingTextBox != null) BeamNaviateStirrupEndSpacingTextBox.Text = "150 mm";
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            _beamNaviateSelectedPreviewSpanIndex = -1;
            _beamNaviateStirrupSpanOverrides.Clear();
            InitializeBeamNaviateStirrupGlobalTextsFromUi();
            SyncBeamNaviateStirrupRowsFromState();
            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: default L/4-L/2-L/4 stirrup layout applied.");
        }

        private void OnBeamNaviateAddBottomSetDefaultClick(object sender, RoutedEventArgs e)
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                SetBeamNaviateAdditionalEnabled(topRows: false, enabled: true);
                string preferredType = (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(preferredType))
                {
                    preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
                }

                int amount = ParseBeamNaviateIntOrDefault(BeamNaviateMainBottomBarCountTextBox?.Text, 2, 1, 40);
                double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
                List<double> stationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
                var spanBreaksMm = new List<double>();
                if (hostLengthMm > 1.0 && !double.IsNaN(hostLengthMm) && !double.IsInfinity(hostLengthMm))
                {
                    double tolMm = Math.Max(120.0, hostLengthMm * 0.01);
                    spanBreaksMm.Add(0.0);
                    spanBreaksMm.AddRange(stationsMm.Where(s => s > tolMm && s < (hostLengthMm - tolMm)));
                    spanBreaksMm.Add(hostLengthMm);
                }

                List<BeamNaviateAdditionalRowUi> defaultRows = BuildBeamNaviateSupportLinkedBottomRows(
                    spanBreaksMm,
                    preferredType,
                    amount,
                    spanEndRatio: 0.125);
                if (defaultRows.Count == 0)
                {
                    defaultRows.Add(CreateBeamNaviateAdditionalDefaultRow(topRow: false));
                }

                ReplaceBeamNaviateAdditionalRows(_beamNaviateAdditionalBottomRows, defaultRows, topRows: false);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalBottomRows, preferredType);
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: additional bottom bars defaulted per span (Nb*L, default 0.125L / 0.75L / 0.125L).");
        }

        private void OnBeamNaviateAddTopSetDefaultClick(object sender, RoutedEventArgs e)
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                SetBeamNaviateAdditionalEnabled(topRows: true, enabled: true);
                string preferredType = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(preferredType))
                {
                    preferredType = _beamNaviateBarTypeNames.FirstOrDefault() ?? "DB12";
                }

                int amount = ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40);
                double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
                List<double> stationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
                var spanBreaksMm = new List<double>();
                if (hostLengthMm > 1.0 && !double.IsNaN(hostLengthMm) && !double.IsInfinity(hostLengthMm))
                {
                    double tolMm = Math.Max(120.0, hostLengthMm * 0.01);
                    spanBreaksMm.Add(0.0);
                    spanBreaksMm.AddRange(stationsMm.Where(s => s > tolMm && s < (hostLengthMm - tolMm)));
                    spanBreaksMm.Add(hostLengthMm);
                }

                List<BeamNaviateAdditionalRowUi> defaultRows = BuildBeamNaviateSupportLinkedTopRows(
                    spanBreaksMm,
                    preferredType,
                    amount,
                    spanRatio: 0.30);
                if (defaultRows.Count == 0)
                {
                    defaultRows.Add(CreateBeamNaviateAdditionalDefaultRow(topRow: true));
                }

                ReplaceBeamNaviateAdditionalRows(_beamNaviateAdditionalTopRows, defaultRows, topRows: true);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalTopRows, preferredType);
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: additional top bars defaulted per span (Nt*L, default 0.3L / 0.4L / 0.3L).");
        }

        private void OnBeamNaviateSpecialSetAllClick(object sender, RoutedEventArgs e)
        {
            EnsureBeamNaviateSpecialRowsInitialized();

            string defaultMode = (GetBeamNaviateSelectedComboText(BeamNaviateSpecialModeCombo) ?? "").Trim();
            string defaultSpacing = (BeamNaviateSpecialSpacingTextBox?.Text ?? "").Trim();
            string defaultZone = (BeamNaviateSpecialZoneLengthTextBox?.Text ?? "").Trim();
            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            BeamNaviateSpecialRowUi selected = specialRowsGrid?.SelectedItem as BeamNaviateSpecialRowUi;

            if (!string.IsNullOrWhiteSpace(selected?.ModeText))
            {
                defaultMode = selected.ModeText.Trim();
            }
            if (!string.IsNullOrWhiteSpace(selected?.SpacingText))
            {
                defaultSpacing = selected.SpacingText.Trim();
            }
            if (!string.IsNullOrWhiteSpace(selected?.StartZoneText))
            {
                defaultZone = selected.StartZoneText.Trim();
            }

            if (string.IsNullOrWhiteSpace(defaultMode)) defaultMode = "Tie Stirrup";
            defaultMode = NormalizeBeamNaviateSpecialModeText(defaultMode);
            if (string.IsNullOrWhiteSpace(defaultSpacing)) defaultSpacing = "150 mm";
            if (string.IsNullOrWhiteSpace(defaultZone)) defaultZone = "800 mm";

            foreach (BeamNaviateSpecialRowUi row in _beamNaviateSpecialRows.Where(x => x != null))
            {
                row.ModeText = string.IsNullOrWhiteSpace(row.ModeText)
                    ? defaultMode
                    : NormalizeBeamNaviateSpecialModeText(row.ModeText);
                row.SpacingText = defaultSpacing;
                row.StartZoneText = defaultZone;
                row.EndZoneText = defaultZone;
            }

            specialRowsGrid?.Items.Refresh();

            UpdateBeamRebarDesignUiState();
            ShowStatus("BEAM: special stirrup values applied.");
        }

        private bool TryRaiseBeamNaviateGenerateRequest(bool deleteOnly)
        {
            if (!TryUpdateBeamNaviateRequestFromUi(deleteOnly))
            {
                return false;
            }

            _handler.Request.RequestType = CadToModelRequestType.GenerateBeamRebar;
            _externalEvent.Raise();
            return true;
        }

        private bool TryUpdateBeamNaviateRequestFromUi(bool deleteOnly)
        {
            CommitBeamNaviateAdditionalRowEdits();

            List<ElementId> hostIds = _beamRebarSelectedHostElementIds
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .GroupBy(x => x.Value)
                .Select(g => g.First())
                .ToList();
            if (hostIds.Count == 0)
            {
                ShowStatus("BEAM: select beam host(s) first.");
                return false;
            }

            if (!TryParseBeamNaviatePositiveInt(BeamNaviateMainTopBarCountTextBox?.Text, "Upper 1st-layer bar count", 1, 40, out int topBarsPerLayer) ||
                !TryParseBeamNaviatePositiveInt(BeamNaviateMainBottomBarCountTextBox?.Text, "Lower 1st-layer bar count", 1, 40, out int bottomBarsPerLayer))
            {
                return false;
            }

            bool topSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: true);
            bool bottomSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: false);
            int topLayers = topSecondEnabled ? 2 : 1;
            int bottomLayers = bottomSecondEnabled ? 2 : 1;

            int topSecondBarsPerLayer = topBarsPerLayer;
            TextBox topSecondCountTextBox = GetBeamNaviateMainTopBarCount2TextBox();
            if (topSecondEnabled &&
                !TryParseBeamNaviatePositiveInt(topSecondCountTextBox?.Text, "Upper 2nd-layer bar count", 1, 40, out topSecondBarsPerLayer))
            {
                return false;
            }

            int bottomSecondBarsPerLayer = bottomBarsPerLayer;
            TextBox bottomSecondCountTextBox = GetBeamNaviateMainBottomBarCount2TextBox();
            if (bottomSecondEnabled &&
                !TryParseBeamNaviatePositiveInt(bottomSecondCountTextBox?.Text, "Lower 2nd-layer bar count", 1, 40, out bottomSecondBarsPerLayer))
            {
                return false;
            }

            if (!TryParseBeamNaviateMmTextToFeet(BeamNaviateMainCoverTextBox?.Text, "Cover", false, out double coverFt))
            {
                return false;
            }

            bool sideEnabled = BeamNaviateMainSideRebarCheckBox?.IsChecked == true;
            int sideBarsPerFace = 0;
            if (sideEnabled && !TryParseBeamNaviatePositiveInt(BeamNaviateMainSideBarCountTextBox?.Text, "Side bars per face", 1, 20, out sideBarsPerFace))
            {
                return false;
            }
            if (!TryParseBeamNaviateMmTextToFeet(BeamNaviateMainSideAnchorLengthTextBox?.Text, "Side anchor length", true, out double sideAnchorFt))
            {
                return false;
            }

            bool stirrupEnabled = BeamNaviateStirrupEnabledCheckBox?.IsChecked == true;
            UpdateBeamNaviateStirrupSpanOverrideFromInputs();
            if (!TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm"),
                    "Stirrup spacing S1",
                    false,
                    out double stirrupS1Ft) ||
                !TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm"),
                    "Stirrup spacing S2",
                    false,
                    out double stirrupS2Ft) ||
                !TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm"),
                    "Stirrup spacing S3",
                    false,
                    out double stirrupS3Ft))
            {
                return false;
            }

            List<BeamNaviateStirrupSpanOverrideRequest> stirrupSpanOverrides = new List<BeamNaviateStirrupSpanOverrideRequest>();
            if (stirrupEnabled && !TryBuildBeamNaviateStirrupSpanOverridesForRequest(out stirrupSpanOverrides))
            {
                return false;
            }

            bool addBottomEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: false);
            bool addTopEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: true);
            bool secondaryEnabled = BeamNaviateSecondaryEnabledCheckBox?.IsChecked == true;
            bool specialEnabled = BeamNaviateSpecialEnabledCheckBox?.IsChecked == true;
            bool addBottomFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddBottomInputModeCombo());
            bool addTopFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());
            int spanCountForAdditional = ResolveBeamNaviateInputSpanCount();
            List<BeamNaviateAdditionalRowUi> normalizedBottomInputRows = NormalizeBeamNaviateAdditionalRowsForSpanBySpanInput(
                _beamNaviateAdditionalBottomRows,
                spanCountForAdditional,
                topRows: false);
            List<BeamNaviateAdditionalRowUi> normalizedTopInputRows = NormalizeBeamNaviateAdditionalRowsForSpanBySpanInput(
                _beamNaviateAdditionalTopRows,
                spanCountForAdditional,
                topRows: true);

            int secondaryCount = 0;
            if (secondaryEnabled && !TryParseBeamNaviatePositiveInt(BeamNaviateSecondaryCountTextBox?.Text, "Secondary quantity", 1, 40, out secondaryCount))
            {
                return false;
            }

            if (!TryBuildBeamNaviateAdditionalBarRequests(
                    normalizedBottomInputRows,
                    addBottomEnabled,
                    groupLabel: "Additional Bottom",
                    defaultBarTypeName: "DB12",
                    interpretPlainAsFactor: addBottomFactorMode,
                    out List<BeamNaviateAdditionalBarRequest> addBottomRows) ||
                !TryBuildBeamNaviateAdditionalBarRequests(
                    normalizedTopInputRows,
                    addTopEnabled,
                    groupLabel: "Additional Top",
                    defaultBarTypeName: "DB12",
                    interpretPlainAsFactor: addTopFactorMode,
                    out List<BeamNaviateAdditionalBarRequest> addTopRows))
            {
                return false;
            }

            if (!TryParseBeamNaviateMmTextToFeet(BeamNaviateSecondaryStartLengthTextBox?.Text, "Secondary start length", true, out double secondaryStartFt) ||
                !TryParseBeamNaviateMmTextToFeet(BeamNaviateSecondaryEndLengthTextBox?.Text, "Secondary end length", true, out double secondaryEndFt))
            {
                return false;
            }

            List<BeamNaviateSpecialStirrupRowRequest> specialRows = new List<BeamNaviateSpecialStirrupRowRequest>();
            if (specialEnabled && !TryBuildBeamNaviateSpecialRowsForRequest(out specialRows))
            {
                return false;
            }
            BuildBeamNaviateSpecialCustomTiesForRequest(
                out List<BeamNaviateCustomLineTieSpec> customLineTies,
                out List<BeamNaviateCustomRectTieSpec> customRectTies);

            string topBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo) ?? "").Trim();
            string topSecondBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarType2Combo) ?? "").Trim();
            string bottomBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo) ?? "").Trim();
            string bottomSecondBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarType2Combo) ?? "").Trim();
            string sideBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateMainSideBarTypeCombo) ?? "").Trim();
            string stirrupBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateStirrupBarTypeCombo) ?? "").Trim();
            string secondaryBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateSecondaryBarTypeCombo) ?? "").Trim();
            string specialBarTypeName = (GetBeamNaviateSelectedComboText(BeamNaviateSpecialBarTypeCombo) ?? "").Trim();

            if (string.IsNullOrWhiteSpace(topBarTypeName) || string.IsNullOrWhiteSpace(bottomBarTypeName))
            {
                ShowStatus("BEAM: select upper and lower bar types.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(topSecondBarTypeName))
            {
                topSecondBarTypeName = topBarTypeName;
            }
            if (string.IsNullOrWhiteSpace(bottomSecondBarTypeName))
            {
                bottomSecondBarTypeName = bottomBarTypeName;
            }
            if (sideEnabled && string.IsNullOrWhiteSpace(sideBarTypeName))
            {
                ShowStatus("BEAM: select side bar type.");
                return false;
            }
            if (stirrupEnabled && string.IsNullOrWhiteSpace(stirrupBarTypeName))
            {
                ShowStatus("BEAM: select stirrup bar type.");
                return false;
            }
            if (secondaryEnabled && string.IsNullOrWhiteSpace(secondaryBarTypeName))
            {
                ShowStatus("BEAM: select secondary bar type.");
                return false;
            }
            if (specialEnabled && string.IsNullOrWhiteSpace(specialBarTypeName))
            {
                ShowStatus("BEAM: select special stirrup bar type.");
                return false;
            }

            BeamNaviateAdditionalBarRequest primaryAddBottomRow = addBottomRows.FirstOrDefault()
                ?? new BeamNaviateAdditionalBarRequest
                {
                    IsEnabled = false,
                    BarTypeName = "",
                    BarCount = 0,
                    LayerIndex = 1,
                    StartGridIndex = 0,
                    EndGridIndex = 1,
                    StartLengthFt = 0.0,
                    EndLengthFt = 0.0
                };
            BeamNaviateAdditionalBarRequest primaryAddTopRow = addTopRows.FirstOrDefault()
                ?? new BeamNaviateAdditionalBarRequest
                {
                    IsEnabled = false,
                    BarTypeName = "",
                    BarCount = 0,
                    LayerIndex = 1,
                    StartGridIndex = 0,
                    EndGridIndex = 0,
                    StartLengthFt = 0.0,
                    EndLengthFt = 0.0
                };
            BeamNaviateSpecialStirrupRowRequest primarySpecialRow = specialRows.FirstOrDefault();
            double specialSpacingFt = primarySpecialRow?.SpacingFt ?? 0.0;
            double specialZoneFt = Math.Max(primarySpecialRow?.StartZoneLengthFt ?? 0.0, primarySpecialRow?.EndZoneLengthFt ?? 0.0);
            string specialMode = string.IsNullOrWhiteSpace(primarySpecialRow?.Mode)
                ? (GetBeamNaviateSelectedComboText(BeamNaviateSpecialModeCombo) ?? "Tie Stirrup").Trim()
                : primarySpecialRow.Mode.Trim();
            specialMode = NormalizeBeamNaviateSpecialModeText(specialMode);

            BeamNaviateRebarRequest nav = new BeamNaviateRebarRequest
            {
                IsEnabled = true,
                SettingName = GetBeamNaviateSettingName(),
                DeleteExistingRebar = true,
                DeleteOnly = deleteOnly,
                HostElementIds = hostIds.ToList(),
                MainBars = new BeamNaviateMainBarsRequest
                {
                    UpperBarTypeName = topBarTypeName,
                    UpperSecondLayerBarTypeName = topSecondBarTypeName,
                    UpperLayerCount = topLayers,
                    UpperBarsPerLayer = topBarsPerLayer,
                    UpperSecondBarsPerLayer = topSecondBarsPerLayer,
                    LowerBarTypeName = bottomBarTypeName,
                    LowerSecondLayerBarTypeName = bottomSecondBarTypeName,
                    LowerLayerCount = bottomLayers,
                    LowerBarsPerLayer = bottomBarsPerLayer,
                    LowerSecondBarsPerLayer = bottomSecondBarsPerLayer,
                    CreateSideBars = sideEnabled,
                    SideBarTypeName = sideEnabled ? sideBarTypeName : "",
                    SideBarsPerFace = sideEnabled ? sideBarsPerFace : 0,
                    SideAnchorLengthFt = sideEnabled ? sideAnchorFt : 0.0,
                    AutoSplitMainBars = true,
                    MaxMainBarLengthFt = MmToFeetUi(12000.0),
                    LapLengthFt = MmToFeetUi(600.0),
                    CoverFt = coverFt
                },
                Stirrups = new BeamNaviateStirrupRequest
                {
                    IsEnabled = stirrupEnabled,
                    BarTypeName = stirrupEnabled ? stirrupBarTypeName : "",
                    LayoutType = (GetBeamNaviateSelectedComboText(BeamNaviateStirrupLayoutCombo) ?? "L/4-L/2-L/4").Trim(),
                    ShapeMode = "Closed",
                    StartSpacingFt = stirrupEnabled ? stirrupS1Ft : 0.0,
                    MiddleSpacingFt = stirrupEnabled ? stirrupS2Ft : 0.0,
                    EndSpacingFt = stirrupEnabled ? stirrupS3Ft : 0.0,
                    StartZoneLengthFt = 0.0,
                    EndZoneLengthFt = 0.0,
                    SpanOverrides = stirrupEnabled ? stirrupSpanOverrides : new List<BeamNaviateStirrupSpanOverrideRequest>()
                },
                AdditionalBottomBars = primaryAddBottomRow,
                AdditionalTopBars = primaryAddTopRow,
                AdditionalBottomBarRows = addBottomRows.ToList(),
                AdditionalTopBarRows = addTopRows.ToList(),
                SecondaryBeam = new BeamNaviateSecondaryBeamRequest
                {
                    IsEnabled = secondaryEnabled,
                    BarTypeName = secondaryEnabled ? secondaryBarTypeName : "",
                    BarCount = secondaryEnabled ? secondaryCount : 0,
                    StartLengthFt = secondaryEnabled ? secondaryStartFt : 0.0,
                    EndLengthFt = secondaryEnabled ? secondaryEndFt : 0.0
                },
                SpecialStirrups = new BeamNaviateSpecialStirrupRequest
                {
                    IsEnabled = specialEnabled,
                    Mode = specialMode,
                    BarTypeName = specialEnabled ? specialBarTypeName : "",
                    SpacingFt = specialEnabled ? specialSpacingFt : 0.0,
                    ZoneLengthFt = specialEnabled ? specialZoneFt : 0.0,
                    Rows = specialEnabled ? specialRows.ToList() : new List<BeamNaviateSpecialStirrupRowRequest>(),
                    CustomLineTies = specialEnabled ? customLineTies : new List<BeamNaviateCustomLineTieSpec>(),
                    CustomRectTies = specialEnabled ? customRectTies : new List<BeamNaviateCustomRectTieSpec>()
                }
            };

            _handler.Request.BeamRebarHostElementIds = hostIds;
            _handler.Request.BeamRebarSecondaryElementIds = _beamRebarSelectedSecondaryElementIds.ToList();
            _handler.Request.BeamRebarSupportElementIds = _beamRebarSelectedSupportElementIds.ToList();
            _handler.Request.BeamRebarUseNaviateDataModel = true;
            _handler.Request.BeamRebarNaviate = nav;
            return true;
        }

        private void CommitBeamNaviateAdditionalRowEdits()
        {
            try
            {
                BeamNaviateAddBottomRowsGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                BeamNaviateAddBottomRowsGrid?.CommitEdit(DataGridEditingUnit.Row, true);
                BeamNaviateAddTopRowsGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                BeamNaviateAddTopRowsGrid?.CommitEdit(DataGridEditingUnit.Row, true);
                DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
                specialRowsGrid?.CommitEdit(DataGridEditingUnit.Cell, true);
                specialRowsGrid?.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // Ignore commit failures; validation logic still catches invalid values.
            }
        }

        private bool TryBuildBeamNaviateSpecialRowsForRequest(out List<BeamNaviateSpecialStirrupRowRequest> rows)
        {
            rows = new List<BeamNaviateSpecialStirrupRowRequest>();
            EnsureBeamNaviateSpecialRowsInitialized();

            List<BeamNaviateSpecialRowUi> inputRows = CloneBeamNaviateSpecialRows(_beamNaviateSpecialRows);
            if (inputRows.Count == 0)
            {
                ShowStatus("BEAM: special stirrup needs at least one row.");
                return false;
            }

            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
            if (hostLengthMm <= 1.0 || double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm))
            {
                hostLengthMm = 1000.0;
            }

            List<double> supportStationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
            List<double> spanStationsMm = BuildBeamPreviewSpanBoundaryStationsMm(hostLengthMm, supportStationsMm);
            if (spanStationsMm.Count < 2)
            {
                spanStationsMm = new List<double> { 0.0, hostLengthMm };
            }
            sectionCount = Math.Max(sectionCount, Math.Max(1, spanStationsMm.Count - 1));

            var spanLengthBySectionMm = new Dictionary<int, double>();
            for (int i = 0; i < spanStationsMm.Count - 1; i++)
            {
                spanLengthBySectionMm[i + 1] = Math.Max(0.0, spanStationsMm[i + 1] - spanStationsMm[i]);
            }

            for (int i = 0; i < inputRows.Count; i++)
            {
                BeamNaviateSpecialRowUi row = inputRows[i];
                string rowLabel = $"Special stirrup row {i + 1}";
                int section = ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200);
                section = Math.Max(1, Math.Min(sectionCount, section));
                int cage = ParseBeamNaviateIntOrDefault(row.CageText, 1, 1, 200);
                cage = Math.Max(1, cage);

                if (!TryParseBeamNaviateMmTextToFeet(
                        NormalizeBeamNaviateLengthText(row.SpacingText, "150 mm"),
                        rowLabel + " spacing",
                        allowZero: false,
                        out double spacingFt))
                {
                    return false;
                }

                double sectionSpanMm = spanLengthBySectionMm.TryGetValue(section, out double spanMm) && spanMm > 1.0
                    ? spanMm
                    : Math.Max(1.0, hostLengthMm / sectionCount);
                double leftZoneMm = ParseBeamNaviateAdditionalLengthTextMm(
                    NormalizeBeamNaviateLengthText(row.StartZoneText, "800 mm"),
                    sectionSpanMm,
                    interpretPlainAsFactor: true);
                double rightZoneMm = ParseBeamNaviateAdditionalLengthTextMm(
                    NormalizeBeamNaviateLengthText(row.EndZoneText, "800 mm"),
                    sectionSpanMm,
                    interpretPlainAsFactor: true);

                rows.Add(new BeamNaviateSpecialStirrupRowRequest
                {
                    SectionIndex = section,
                    CageIndex = cage,
                    Mode = NormalizeBeamNaviateSpecialModeText(row.ModeText),
                    SpacingFt = spacingFt,
                    StartZoneLengthFt = Math.Max(0.0, leftZoneMm) / 304.8,
                    EndZoneLengthFt = Math.Max(0.0, rightZoneMm) / 304.8
                });
            }

            rows = rows
                .OrderBy(x => x.SectionIndex)
                .ThenBy(x => x.CageIndex)
                .ThenBy(x => GetBeamNaviateSpecialModeLaneIndex(x.Mode))
                .ToList();
            return rows.Count > 0;
        }

        private void BuildBeamNaviateSpecialCustomTiesForRequest(
            out List<BeamNaviateCustomLineTieSpec> lineTies,
            out List<BeamNaviateCustomRectTieSpec> rectTies)
        {
            lineTies = new List<BeamNaviateCustomLineTieSpec>();
            rectTies = new List<BeamNaviateCustomRectTieSpec>();

            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            foreach (BeamNaviateSpecialSectionLineTieUiSpec source in CloneBeamNaviateSpecialLineTieUiSpecs(_beamNaviateSpecialCustomLineTies))
            {
                if (source == null)
                {
                    continue;
                }

                source.SectionIndex = Math.Max(1, Math.Min(sectionCount, source.SectionIndex));
                NormalizeBeamNaviateSpecialLineTieSpec(source);
                if (source.X0GridIndex == source.X1GridIndex && source.Y0GridIndex == source.Y1GridIndex)
                {
                    continue;
                }

                lineTies.Add(new BeamNaviateCustomLineTieSpec
                {
                    SectionIndex = source.SectionIndex,
                    X0GridIndex = source.X0GridIndex,
                    Y0GridIndex = source.Y0GridIndex,
                    X1GridIndex = source.X1GridIndex,
                    Y1GridIndex = source.Y1GridIndex
                });
            }

            foreach (BeamNaviateSpecialSectionRectTieUiSpec source in CloneBeamNaviateSpecialRectTieUiSpecs(_beamNaviateSpecialCustomRectTies))
            {
                if (source == null)
                {
                    continue;
                }

                int sectionIndex = Math.Max(1, Math.Min(sectionCount, source.SectionIndex));
                int x0 = Math.Max(0, Math.Min(source.X0GridIndex, source.X1GridIndex));
                int x1 = Math.Max(0, Math.Max(source.X0GridIndex, source.X1GridIndex));
                int y0 = Math.Max(0, Math.Min(source.Y0GridIndex, source.Y1GridIndex));
                int y1 = Math.Max(0, Math.Max(source.Y0GridIndex, source.Y1GridIndex));
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                rectTies.Add(new BeamNaviateCustomRectTieSpec
                {
                    SectionIndex = sectionIndex,
                    X0GridIndex = x0,
                    Y0GridIndex = y0,
                    X1GridIndex = x1,
                    Y1GridIndex = y1
                });
            }

            lineTies = lineTies
                .GroupBy(x => $"{x.SectionIndex}:{x.X0GridIndex}:{x.Y0GridIndex}:{x.X1GridIndex}:{x.Y1GridIndex}")
                .Select(g => g.First())
                .ToList();
            rectTies = rectTies
                .GroupBy(x => $"{x.SectionIndex}:{x.X0GridIndex}:{x.Y0GridIndex}:{x.X1GridIndex}:{x.Y1GridIndex}")
                .Select(g => g.First())
                .ToList();
        }

        private bool TryBuildBeamNaviateAdditionalBarRequests(
            IEnumerable<BeamNaviateAdditionalRowUi> sourceRows,
            bool isGroupEnabled,
            string groupLabel,
            string defaultBarTypeName,
            bool interpretPlainAsFactor,
            out List<BeamNaviateAdditionalBarRequest> rows)
        {
            rows = new List<BeamNaviateAdditionalBarRequest>();
            if (!isGroupEnabled)
            {
                return true;
            }

            List<BeamNaviateAdditionalRowUi> normalizedRows = (sourceRows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
                .Where(r => r != null)
                .ToList();
            if (normalizedRows.Count == 0)
            {
                ShowStatus($"BEAM: {groupLabel} needs at least one row.");
                return false;
            }

            for (int i = 0; i < normalizedRows.Count; i++)
            {
                BeamNaviateAdditionalRowUi row = normalizedRows[i];
                string rowLabel = $"{groupLabel} row {i + 1}";

                string rowBarTypeName = (row.BarTypeName ?? "").Trim();
                if (string.IsNullOrWhiteSpace(rowBarTypeName))
                {
                    rowBarTypeName = (defaultBarTypeName ?? "").Trim();
                }
                if (string.IsNullOrWhiteSpace(rowBarTypeName))
                {
                    ShowStatus($"BEAM: {rowLabel} bar type is required.");
                    return false;
                }

                string startRawText = GetBeamNaviateAdditionalStartInputText(row, interpretPlainAsFactor);
                string endRawText = GetBeamNaviateAdditionalEndInputText(row, interpretPlainAsFactor);
                string startLabel = interpretPlainAsFactor ? (rowLabel + " start factor") : (rowLabel + " start length");
                string endLabel = interpretPlainAsFactor ? (rowLabel + " end factor") : (rowLabel + " end length");

                if (!TryParseBeamNaviatePositiveInt(row.LayerText, rowLabel + " layer", 1, 12, out int layerIndex) ||
                    !TryParseBeamNaviatePositiveInt(row.AmountText, rowLabel + " amount", 1, 40, out int amount) ||
                    !TryParseBeamNaviatePositiveInt(row.StartGridText, rowLabel + " start grid", 0, 200, out int startGridIndex) ||
                    !TryParseBeamNaviatePositiveInt(row.EndGridText, rowLabel + " end grid", 0, 200, out int endGridIndex) ||
                    !TryParseBeamNaviateAdditionalLengthTextToFeetOrRatio(startRawText, startLabel, interpretPlainAsFactor, out double startLengthFtOrRatio) ||
                    !TryParseBeamNaviateAdditionalLengthTextToFeetOrRatio(endRawText, endLabel, interpretPlainAsFactor, out double endLengthFtOrRatio))
                {
                    return false;
                }

                rows.Add(new BeamNaviateAdditionalBarRequest
                {
                    IsEnabled = true,
                    BarTypeName = rowBarTypeName,
                    BarCount = amount,
                    LayerIndex = layerIndex,
                    StartGridIndex = startGridIndex,
                    EndGridIndex = endGridIndex,
                    StartLengthFt = startLengthFtOrRatio,
                    EndLengthFt = endLengthFtOrRatio
                });
            }

            return true;
        }

        private bool TryParseBeamNaviateMmTextToFeet(string rawText, string label, bool allowZero, out double valueFt)
        {
            valueFt = 0.0;
            if (!TryParseBeamNaviateMmText(rawText, out double valueMm))
            {
                ShowStatus($"BEAM: {label} must be a valid length.");
                return false;
            }

            if (allowZero)
            {
                if (valueMm < 0.0)
                {
                    ShowStatus($"BEAM: {label} must be >= 0 mm.");
                    return false;
                }
            }
            else if (valueMm <= 0.0)
            {
                ShowStatus($"BEAM: {label} must be > 0 mm.");
                return false;
            }

            valueFt = MmToFeetUi(Math.Max(0.0, valueMm));
            return true;
        }

        private bool TryParseBeamNaviateAdditionalLengthTextToFeetOrRatio(
            string rawText,
            string label,
            bool interpretPlainAsFactor,
            out double encodedValueFt)
        {
            encodedValueFt = 0.0;
            if (TryParseBeamNaviateLengthRatioOfL(rawText, out double ratioL))
            {
                if (ratioL < 0.0)
                {
                    ShowStatus($"BEAM: {label} ratio must be >= 0L.");
                    return false;
                }

                encodedValueFt = -(ratioL + 1.0);
                return true;
            }

            if (interpretPlainAsFactor && TryParseBeamNaviatePlainFactorText(rawText, out double plainFactor))
            {
                if (plainFactor < 0.0)
                {
                    ShowStatus($"BEAM: {label} factor must be >= 0.");
                    return false;
                }

                encodedValueFt = -(plainFactor + 1.0);
                return true;
            }

            return TryParseBeamNaviateMmTextToFeet(rawText, label, allowZero: true, out encodedValueFt);
        }

        private static bool TryParseBeamNaviateMmText(string rawText, out double valueMm)
        {
            valueMm = 0.0;
            string raw = (rawText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            Match m = Regex.Match(
                raw,
                @"^\s*(?<value>[+-]?\d+(?:[.,]\d+)?)\s*(?<unit>mm|millimeter|millimetre|cm|centimeter|centimetre|m|meter|metre)?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success || !TryParseFlexibleDouble(m.Groups["value"].Value, out double numeric))
            {
                return false;
            }

            string unit = (m.Groups["unit"].Value ?? "").Trim().ToLowerInvariant();
            if (unit == "m" || unit == "meter" || unit == "metre")
            {
                valueMm = numeric * 1000.0;
            }
            else if (unit == "cm" || unit == "centimeter" || unit == "centimetre")
            {
                valueMm = numeric * 10.0;
            }
            else
            {
                valueMm = numeric;
            }

            return true;
        }

        private static bool TryParseBeamNaviateLengthRatioOfL(string rawText, out double ratioL)
        {
            ratioL = 0.0;
            string raw = (rawText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            Match m = Regex.Match(
                raw,
                @"^\s*(?<value>[+-]?\d+(?:[.,]\d+)?)\s*(?:(?:\*|\.)\s*)?[lL]\s*$",
                RegexOptions.CultureInvariant);
            if (!m.Success || !TryParseFlexibleDouble(m.Groups["value"].Value, out double numeric))
            {
                return false;
            }

            ratioL = numeric;
            return true;
        }

        private static bool TryParseBeamNaviatePlainFactorText(string rawText, out double factor)
        {
            factor = 0.0;
            string raw = (rawText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            Match m = Regex.Match(
                raw,
                @"^\s*(?<value>[+-]?\d+(?:[.,]\d+)?)\s*$",
                RegexOptions.CultureInvariant);
            if (!m.Success || !TryParseFlexibleDouble(m.Groups["value"].Value, out double numeric))
            {
                return false;
            }

            factor = numeric;
            return true;
        }

        private bool TryParseBeamNaviatePositiveInt(string rawText, string label, int min, int max, out int value)
        {
            value = 0;
            string raw = (rawText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw) || !TryParseFlexibleDouble(raw, out double numeric))
            {
                ShowStatus($"BEAM: {label} must be a number.");
                return false;
            }

            int parsed = (int)Math.Round(numeric, MidpointRounding.AwayFromZero);
            if (parsed < min || parsed > max)
            {
                ShowStatus($"BEAM: {label} must be between {min} and {max}.");
                return false;
            }

            value = parsed;
            return true;
        }

        private static string GetBeamNaviateSelectedComboText(ComboBox combo)
        {
            if (combo == null)
            {
                return string.Empty;
            }

            if (combo.SelectedItem is ComboItem ci)
            {
                return ci.Name ?? string.Empty;
            }

            if (combo.SelectedItem is ComboBoxItem cbi)
            {
                return cbi.Content?.ToString() ?? string.Empty;
            }

            return combo.SelectedItem?.ToString() ?? combo.Text ?? string.Empty;
        }

        private static bool TrySetBeamNaviateComboSelection(ComboBox combo, string text)
        {
            if (combo == null)
            {
                return false;
            }

            string target = (text ?? string.Empty).Trim();
            for (int i = 0; i < combo.Items.Count; i++)
            {
                object item = combo.Items[i];
                string itemText = item is ComboItem ci
                    ? (ci.Name ?? "")
                    : (item is ComboBoxItem cbi ? (cbi.Content?.ToString() ?? "") : (item?.ToString() ?? ""));

                if (string.Equals(itemText.Trim(), target, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static int ParseBeamNaviateIntOrDefault(string rawText, int fallback, int min, int max)
        {
            string raw = (rawText ?? "").Trim();
            if (!TryParseFlexibleDouble(raw, out double numeric))
            {
                return fallback;
            }

            int parsed = (int)Math.Round(numeric, MidpointRounding.AwayFromZero);
            if (parsed < min) parsed = min;
            if (parsed > max) parsed = max;
            return parsed;
        }

        private void EnsureBeamNaviateDefaultBarTypeItems()
        {
            if (_beamRebarBarTypeItems.Count > 0)
            {
                UpdateBeamRebarBarTypes(_beamRebarBarTypeItems.ToList());
                return;
            }

            var defaults = new List<ComboItem>
            {
                new ComboItem(ElementId.InvalidElementId, "DB10"),
                new ComboItem(ElementId.InvalidElementId, "DB12"),
                new ComboItem(ElementId.InvalidElementId, "DB14"),
                new ComboItem(ElementId.InvalidElementId, "DB16"),
                new ComboItem(ElementId.InvalidElementId, "DB18"),
                new ComboItem(ElementId.InvalidElementId, "DB20")
            };

            UpdateBeamRebarBarTypes(defaults);
        }

        private static int FindPreferredBeamBarTypeIndex(IList<ComboItem> items, IEnumerable<string> tokens)
        {
            if (items == null || items.Count == 0)
            {
                return -1;
            }

            foreach (string token in tokens ?? Enumerable.Empty<string>())
            {
                string needle = (token ?? "").Trim();
                if (string.IsNullOrWhiteSpace(needle))
                {
                    continue;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    string name = (items[i]?.Name ?? "").Trim();
                    if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        private void ApplyBeamBarTypeItems(ComboBox combo, IList<ComboItem> items, IEnumerable<string> preferredTokens)
        {
            if (combo == null)
            {
                return;
            }

            string previous = GetBeamNaviateSelectedComboText(combo);
            combo.ItemsSource = null;
            combo.Items.Clear();
            foreach (ComboItem item in items ?? new List<ComboItem>())
            {
                combo.Items.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(previous) && TrySetBeamNaviateComboSelection(combo, previous))
            {
                return;
            }

            int preferredIndex = FindPreferredBeamBarTypeIndex(items, preferredTokens);
            if (preferredIndex >= 0 && preferredIndex < combo.Items.Count)
            {
                combo.SelectedIndex = preferredIndex;
                return;
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }
        }

        private void UpdateBeamRebarBarTypes(List<ComboItem> items)
        {
            List<ComboItem> sourceItems = (items ?? new List<ComboItem>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => new ComboItem(x.Id, x.Name.Trim()))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (sourceItems.Count == 0)
            {
                return;
            }

            _beamRebarBarTypeItems.Clear();
            _beamRebarBarTypeItems.AddRange(sourceItems);
            _beamNaviateBarTypeNames.Clear();
            foreach (string typeName in sourceItems.Select(x => x.Name).Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                _beamNaviateBarTypeNames.Add(typeName);
            }

            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                ApplyBeamBarTypeItems(BeamNaviateMainTopBarTypeCombo, sourceItems, new[] { "DB20", "DB16", "20", "16" });
                ApplyBeamBarTypeItems(BeamNaviateMainTopBarType2Combo, sourceItems, new[] { "DB16", "DB20", "16", "20" });
                ApplyBeamBarTypeItems(BeamNaviateMainBottomBarTypeCombo, sourceItems, new[] { "DB20", "DB16", "20", "16" });
                ApplyBeamBarTypeItems(BeamNaviateMainBottomBarType2Combo, sourceItems, new[] { "DB16", "DB20", "16", "20" });
                ApplyBeamBarTypeItems(BeamNaviateMainSideBarTypeCombo, sourceItems, new[] { "DB12", "DB14", "12", "14" });
                ApplyBeamBarTypeItems(BeamNaviateStirrupBarTypeCombo, sourceItems, new[] { "DB10", "10" });
                ApplyBeamBarTypeItems(BeamNaviateSecondaryBarTypeCombo, sourceItems, new[] { "DB12", "12" });
                ApplyBeamBarTypeItems(BeamNaviateSpecialBarTypeCombo, sourceItems, new[] { "DB10", "10" });

                int preferredAdditionalIndex = FindPreferredBeamBarTypeIndex(sourceItems, new[] { "DB12", "12" });
                string preferredAdditionalBarType = (preferredAdditionalIndex >= 0 && preferredAdditionalIndex < sourceItems.Count)
                    ? sourceItems[preferredAdditionalIndex].Name
                    : sourceItems[0].Name;
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalBottomRows, preferredAdditionalBarType);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalTopRows, preferredAdditionalBarType);
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
        }

        public void UpdateBeamRebarSelectedHosts(IList<ElementId> hostIds, IList<double> hostLengthsMm = null)
        {
            _beamRebarSelectedHostElementIds.Clear();
            _beamRebarSelectedHostElementIds.AddRange((hostIds ?? Array.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .GroupBy(x => x.Value)
                .Select(g => g.First()));
            // Host references changed: clear stale manual secondary/support references so auto-detection can re-evaluate related elements.
            _beamRebarSelectedSecondaryElementIds.Clear();
            _beamRebarSelectedSupportElementIds.Clear();
            if (_handler?.Request != null)
            {
                _handler.Request.BeamRebarSecondaryElementIds = new List<ElementId>();
                _handler.Request.BeamRebarSupportElementIds = new List<ElementId>();
            }
            _beamRebarSelectedHostLengthsMm.Clear();
            _beamRebarSelectedHostLengthsMm.AddRange((hostLengthsMm ?? Array.Empty<double>())
                .Where(x => x > 1.0 && !double.IsNaN(x) && !double.IsInfinity(x)));

            int total = _beamRebarSelectedHostElementIds.Count;
            if (total == 0)
            {
                _beamRebarDetectedModelSummary = string.Empty;
                _beamRebarDetectedStartConnectionCount = 0;
                _beamRebarDetectedEndConnectionCount = 0;
                _beamRebarDetectedSupportStationsMm.Clear();
                _beamRebarDetectedSupportWidthsMm.Clear();
                _beamRebarDetectedSupportKinds.Clear();
                _beamRebarPrimaryHostWidthMm = 0.0;
                _beamRebarPrimaryHostDepthMm = 0.0;
            }
            if (BeamNaviateStatusTextBlock != null)
            {
                BeamNaviateStatusTextBlock.Text = total > 0
                    ? $"Selected {total} beam host(s)."
                    : "Please select beam element(s) in view. Additional Top/Bottom: use Add, Delete, Set Default below the table.";
            }

            UpdateBeamRebarSelectionUiState();
            SyncBeamNaviateStirrupRowsFromState();
            QueueBeamNaviatePreviewRender();
            RenderBeamNaviateSpecialSectionCanvas();
        }

        public void UpdateBeamRebarSelectedHostSection(double widthMm, double depthMm)
        {
            _beamRebarPrimaryHostWidthMm = (widthMm > 1.0 && !double.IsNaN(widthMm) && !double.IsInfinity(widthMm))
                ? widthMm
                : 0.0;
            _beamRebarPrimaryHostDepthMm = (depthMm > 1.0 && !double.IsNaN(depthMm) && !double.IsInfinity(depthMm))
                ? depthMm
                : 0.0;
            QueueBeamNaviatePreviewRender();
            RenderBeamNaviateSpecialSectionCanvas();
        }

        public void UpdateBeamRebarSelectedSecondaryHosts(IList<ElementId> hostIds)
        {
            _beamRebarSelectedSecondaryElementIds.Clear();
            _beamRebarSelectedSecondaryElementIds.AddRange((hostIds ?? Array.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .GroupBy(x => x.Value)
                .Select(g => g.First()));

            UpdateBeamRebarSelectionUiState();
            SyncBeamNaviateStirrupRowsFromState();
            QueueBeamNaviatePreviewRender();
        }

        public void UpdateBeamRebarSelectedSupportHosts(IList<ElementId> hostIds)
        {
            _beamRebarSelectedSupportElementIds.Clear();
            _beamRebarSelectedSupportElementIds.AddRange((hostIds ?? Array.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .GroupBy(x => x.Value)
                .Select(g => g.First()));

            if (_beamNaviatePendingSupportDefaults)
            {
                _beamNaviatePendingSupportDefaults = false;
                ApplyBeamNaviateSupportSpanDefaults();
            }

            UpdateBeamRebarSelectionUiState();
            QueueBeamNaviatePreviewRender();
            SyncBeamNaviateStirrupRowsFromState();
        }

        public void UpdateBeamRebarDetectedModelSummary(string summary)
        {
            _beamRebarDetectedModelSummary = (summary ?? string.Empty).Trim();
            QueueBeamNaviatePreviewRender();
        }

        public void UpdateBeamRebarDetectedModelConnection(int startConnectionCount, int endConnectionCount)
        {
            _beamRebarDetectedStartConnectionCount = Math.Max(0, startConnectionCount);
            _beamRebarDetectedEndConnectionCount = Math.Max(0, endConnectionCount);
            QueueBeamNaviatePreviewRender();
        }

        public void UpdateBeamRebarDetectedSupportStations(
            IList<double> supportStationsMm,
            IList<double> supportWidthsMm = null,
            IList<string> supportKinds = null)
        {
            _beamRebarDetectedSupportStationsMm.Clear();
            _beamRebarDetectedSupportStationsMm.AddRange((supportStationsMm ?? Array.Empty<double>())
                .Where(x => x >= 0.0 && !double.IsNaN(x) && !double.IsInfinity(x)));

            _beamRebarDetectedSupportWidthsMm.Clear();
            _beamRebarDetectedSupportKinds.Clear();

            int stationCount = _beamRebarDetectedSupportStationsMm.Count;
            for (int i = 0; i < stationCount; i++)
            {
                double widthMm = (supportWidthsMm != null && i < supportWidthsMm.Count)
                    ? supportWidthsMm[i]
                    : 0.0;
                if (widthMm < 0.0 || double.IsNaN(widthMm) || double.IsInfinity(widthMm))
                {
                    widthMm = 0.0;
                }
                _beamRebarDetectedSupportWidthsMm.Add(widthMm);

                string kind = (supportKinds != null && i < supportKinds.Count)
                    ? (supportKinds[i] ?? string.Empty).Trim()
                    : string.Empty;
                _beamRebarDetectedSupportKinds.Add(kind);
            }

            QueueBeamNaviatePreviewRender();
        }

        private void UpdateBeamRebarSelectionUiState()
        {
            int count = _beamRebarSelectedHostElementIds.Count;
            int secondaryCount = _beamRebarSelectedSecondaryElementIds.Count;
            int supportCount = _beamRebarSelectedSupportElementIds.Count;
            if (BeamNaviateSelectedCountTextBlock != null)
            {
                string text = count == 1
                    ? "1 beam host selected."
                    : $"{count} beam host(s) selected.";

                if (secondaryCount > 0 || supportCount > 0)
                {
                    text += $"  |  Secondary refs: {secondaryCount}, Support refs: {supportCount}";
                }
                BeamNaviateSelectedCountTextBlock.Text = text;
            }

            if (BeamNaviateApplyButton != null)
            {
                BeamNaviateApplyButton.IsEnabled = count > 0;
            }

            if (BeamNaviateDeleteRebarButton != null)
            {
                BeamNaviateDeleteRebarButton.IsEnabled = count > 0;
            }
        }

        private void UpdateBeamRebarDesignUiState()
        {
            if (_beamNaviateDesignUiUpdating)
            {
                return;
            }

            _beamNaviateDesignUiUpdating = true;
            try
            {
            // Always keep one editable default row available in Additional Bottom/Top tables.
            EnsureBeamNaviateAdditionalRowsReady(topRows: false);
            EnsureBeamNaviateAdditionalRowsReady(topRows: true);

            bool sideEnabled = BeamNaviateMainSideRebarCheckBox?.IsChecked == true;
            bool topSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: true);
            bool bottomSecondEnabled = IsBeamNaviateSecondLayerEnabled(topBar: false);
            SyncBeamNaviateHiddenLayerCountUi();
            if (BeamNaviateMainTopLayerCountTextBox != null) BeamNaviateMainTopLayerCountTextBox.IsEnabled = false;
            if (BeamNaviateMainBottomLayerCountTextBox != null) BeamNaviateMainBottomLayerCountTextBox.IsEnabled = false;
            if (BeamNaviateMainTopBarType2Combo != null) BeamNaviateMainTopBarType2Combo.IsEnabled = topSecondEnabled;
            if (BeamNaviateMainBottomBarType2Combo != null) BeamNaviateMainBottomBarType2Combo.IsEnabled = bottomSecondEnabled;
            TextBox topSecondCountTextBox = GetBeamNaviateMainTopBarCount2TextBox();
            TextBox bottomSecondCountTextBox = GetBeamNaviateMainBottomBarCount2TextBox();
            if (topSecondCountTextBox != null) topSecondCountTextBox.IsEnabled = topSecondEnabled;
            if (bottomSecondCountTextBox != null) bottomSecondCountTextBox.IsEnabled = bottomSecondEnabled;
            if (BeamNaviateMainSideBarTypeCombo != null) BeamNaviateMainSideBarTypeCombo.IsEnabled = sideEnabled;
            if (BeamNaviateMainSideBarCountTextBox != null) BeamNaviateMainSideBarCountTextBox.IsEnabled = sideEnabled;
            if (BeamNaviateMainSideAnchorLengthTextBox != null) BeamNaviateMainSideAnchorLengthTextBox.IsEnabled = sideEnabled;

            bool stirrupEnabled = BeamNaviateStirrupEnabledCheckBox?.IsChecked == true;
            if (BeamNaviateStirrupBarTypeCombo != null) BeamNaviateStirrupBarTypeCombo.IsEnabled = stirrupEnabled;
            if (BeamNaviateStirrupLayoutCombo != null) BeamNaviateStirrupLayoutCombo.IsEnabled = stirrupEnabled;
            ComboBox stirrupZoneInputModeCombo = GetBeamNaviateStirrupZoneInputModeCombo();
            if (stirrupZoneInputModeCombo != null) stirrupZoneInputModeCombo.IsEnabled = stirrupEnabled;
            if (BeamNaviateStirrupStartSpacingTextBox != null) BeamNaviateStirrupStartSpacingTextBox.IsEnabled = stirrupEnabled;
            if (BeamNaviateStirrupMiddleSpacingTextBox != null) BeamNaviateStirrupMiddleSpacingTextBox.IsEnabled = stirrupEnabled;
            if (BeamNaviateStirrupEndSpacingTextBox != null) BeamNaviateStirrupEndSpacingTextBox.IsEnabled = stirrupEnabled;
            if (BeamNaviateStirrupRowsGrid != null) BeamNaviateStirrupRowsGrid.IsEnabled = stirrupEnabled;
            ConfigureBeamNaviateStirrupRowsGridColumns();

            bool addBottomEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: false);
            if (BeamNaviateAddBottomRowsGrid != null) BeamNaviateAddBottomRowsGrid.IsEnabled = addBottomEnabled;
            if (BeamNaviateAddBottomAddRowButton != null) BeamNaviateAddBottomAddRowButton.IsEnabled = true;
            if (BeamNaviateAddBottomSetDefaultButton != null) BeamNaviateAddBottomSetDefaultButton.IsEnabled = true;
            ComboBox addBottomInputModeCombo = GetBeamNaviateAddBottomInputModeCombo();
            if (addBottomInputModeCombo != null) addBottomInputModeCombo.IsEnabled = addBottomEnabled;
            if (BeamNaviateAddBottomDeleteRowButton != null)
            {
                BeamNaviateAddBottomDeleteRowButton.IsEnabled = addBottomEnabled && BeamNaviateAddBottomRowsGrid?.SelectedItem != null;
            }

            bool addTopEnabled = ResolveBeamNaviateAdditionalEnabled(topRows: true);
            if (BeamNaviateAddTopRowsGrid != null) BeamNaviateAddTopRowsGrid.IsEnabled = addTopEnabled;
            if (BeamNaviateAddTopAddRowButton != null) BeamNaviateAddTopAddRowButton.IsEnabled = true;
            if (BeamNaviateAddTopSetDefaultButton != null) BeamNaviateAddTopSetDefaultButton.IsEnabled = true;
            ComboBox addTopInputModeCombo = GetBeamNaviateAddTopInputModeCombo();
            if (addTopInputModeCombo != null) addTopInputModeCombo.IsEnabled = addTopEnabled;
            if (BeamNaviateAddTopDeleteRowButton != null)
            {
                BeamNaviateAddTopDeleteRowButton.IsEnabled = addTopEnabled && BeamNaviateAddTopRowsGrid?.SelectedItem != null;
            }

            bool secondaryEnabled = BeamNaviateSecondaryEnabledCheckBox?.IsChecked == true;
            if (BeamNaviateSecondaryBarTypeCombo != null) BeamNaviateSecondaryBarTypeCombo.IsEnabled = secondaryEnabled;
            if (BeamNaviateSecondaryCountTextBox != null) BeamNaviateSecondaryCountTextBox.IsEnabled = secondaryEnabled;
            if (BeamNaviateSecondaryStartLengthTextBox != null) BeamNaviateSecondaryStartLengthTextBox.IsEnabled = secondaryEnabled;
            if (BeamNaviateSecondaryEndLengthTextBox != null) BeamNaviateSecondaryEndLengthTextBox.IsEnabled = secondaryEnabled;

            bool specialEnabled = BeamNaviateSpecialEnabledCheckBox?.IsChecked == true;
            if (BeamNaviateSpecialBarTypeCombo != null) BeamNaviateSpecialBarTypeCombo.IsEnabled = specialEnabled;
            if (BeamNaviateSpecialModeCombo != null) BeamNaviateSpecialModeCombo.IsEnabled = specialEnabled;
            if (BeamNaviateSpecialSpacingTextBox != null) BeamNaviateSpecialSpacingTextBox.IsEnabled = specialEnabled;
            if (BeamNaviateSpecialZoneLengthTextBox != null) BeamNaviateSpecialZoneLengthTextBox.IsEnabled = specialEnabled;
            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            Button specialAddRowButton = GetBeamNaviateSpecialAddRowButton();
            Button specialSetDefaultButton = GetBeamNaviateSpecialSetDefaultButton();
            Button specialDeleteRowButton = GetBeamNaviateSpecialDeleteRowButton();
            Canvas specialSectionCanvas = GetBeamNaviateSpecialSectionCanvas();
            RadioButton specialSelectModeRadio = GetBeamNaviateSpecialSectionModeSelectRadio();
            RadioButton specialDrawTieModeRadio = GetBeamNaviateSpecialSectionModeDrawTieRadio();
            RadioButton specialDrawRectModeRadio = GetBeamNaviateSpecialSectionModeDrawRectRadio();
            RadioButton specialDeleteModeRadio = GetBeamNaviateSpecialSectionModeDeleteRadio();
            Button specialClearDrawnButton = GetBeamNaviateSpecialSectionClearDrawnButton();
            Button specialFitButton = GetBeamNaviateSpecialSectionFitButton();
            if (specialRowsGrid != null) specialRowsGrid.IsEnabled = specialEnabled;
            if (specialAddRowButton != null) specialAddRowButton.IsEnabled = specialEnabled;
            if (specialSetDefaultButton != null) specialSetDefaultButton.IsEnabled = specialEnabled;
            if (specialDeleteRowButton != null)
            {
                specialDeleteRowButton.IsEnabled = specialEnabled && specialRowsGrid?.SelectedItem != null;
            }
            if (specialSectionCanvas != null) specialSectionCanvas.IsEnabled = specialEnabled;
            if (specialSelectModeRadio != null) specialSelectModeRadio.IsEnabled = specialEnabled;
            if (specialDrawTieModeRadio != null) specialDrawTieModeRadio.IsEnabled = specialEnabled;
            if (specialDrawRectModeRadio != null) specialDrawRectModeRadio.IsEnabled = specialEnabled;
            if (specialDeleteModeRadio != null) specialDeleteModeRadio.IsEnabled = specialEnabled;
            if (specialClearDrawnButton != null) specialClearDrawnButton.IsEnabled = specialEnabled;
            if (specialFitButton != null) specialFitButton.IsEnabled = true;

            if (!specialEnabled && _beamNaviateSpecialSectionEditMode != BeamNaviateSpecialSectionEditMode.Select)
            {
                _beamNaviateSpecialSectionEditMode = BeamNaviateSpecialSectionEditMode.Select;
                _beamNaviateSpecialSectionPendingStartGridNode = null;
                _beamNaviateSpecialSectionIsLeftDrawDragging = false;
                if (specialSelectModeRadio != null)
                {
                    specialSelectModeRadio.IsChecked = true;
                }
            }

            SyncBeamNaviateStirrupRowsFromState();
            AutoFillBeamNaviateAdditionalFrozenColumns();
            if (!_beamNaviateAdditionalGridEditing)
            {
                UpdateBeamNaviateAdditionalRowsUiState();
            }
            else
            {
                UpdateBeamNaviateAdditionalDeleteButtonsState();
            }
            QueueBeamNaviatePreviewRender();
            RenderBeamNaviateSpecialSectionCanvas();
            }
            finally
            {
                _beamNaviateDesignUiUpdating = false;
            }
        }

        private void UpdateBeamNaviateAdditionalRowsUiState()
        {
            EnsureBeamNaviateAdditionalRowsReady(topRows: false);
            EnsureBeamNaviateAdditionalRowsReady(topRows: true);

            ApplyBeamNaviateSpanInfoToAdditionalRows(_beamNaviateAdditionalBottomRows);
            ApplyBeamNaviateSpanInfoToAdditionalRows(_beamNaviateAdditionalTopRows);
            int sectionCount = ResolveBeamNaviateInputSpanCount();
            ApplyBeamNaviateSectionInfoToSpecialRows(_beamNaviateSpecialRows, sectionCount);
            ApplyBeamNaviateSectionInfoToSpecialCustomTies(_beamNaviateSpecialCustomLineTies, _beamNaviateSpecialCustomRectTies, sectionCount);

            bool bottomFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddBottomInputModeCombo());
            bool topFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());

            ConfigureBeamNaviateAdditionalGridColumns(BeamNaviateAddBottomRowsGrid, bottomFactorMode, topRows: false);
            ConfigureBeamNaviateAdditionalGridColumns(BeamNaviateAddTopRowsGrid, topFactorMode, topRows: true);

            BeamNaviateAddBottomRowsGrid?.Items.Refresh();
            BeamNaviateAddTopRowsGrid?.Items.Refresh();
            GetBeamNaviateSpecialRowsGrid()?.Items.Refresh();
        }

        private void ConfigureBeamNaviateStirrupRowsGridColumns()
        {
            if (BeamNaviateStirrupRowsGrid?.Columns == null || BeamNaviateStirrupRowsGrid.Columns.Count < 10)
            {
                return;
            }

            DataGridColumn spanCol = BeamNaviateStirrupRowsGrid.Columns[0];
            DataGridColumn startGridCol = BeamNaviateStirrupRowsGrid.Columns[4];
            DataGridColumn endGridCol = BeamNaviateStirrupRowsGrid.Columns[5];
            DataGridColumn startZoneCol = BeamNaviateStirrupRowsGrid.Columns[6];
            DataGridColumn endZoneCol = BeamNaviateStirrupRowsGrid.Columns[7];
            DataGridColumn startFactorCol = BeamNaviateStirrupRowsGrid.Columns[8];
            DataGridColumn endFactorCol = BeamNaviateStirrupRowsGrid.Columns[9];
            ComboBox zoneModeCombo = GetBeamNaviateStirrupZoneInputModeCombo();
            bool followAdditionalTop = IsBeamNaviateStirrupZoneFollowAdditionalTopMode(zoneModeCombo);
            bool factorMode = IsBeamNaviateStirrupZoneFactorMode(zoneModeCombo);

            if (spanCol != null)
            {
                spanCol.Header = "Span";
                spanCol.IsReadOnly = true;
            }
            if (startGridCol != null)
            {
                startGridCol.Header = "Start Grid";
                startGridCol.IsReadOnly = true;
            }
            if (endGridCol != null)
            {
                endGridCol.Header = "End Grid";
                endGridCol.IsReadOnly = true;
            }

            if (startZoneCol != null)
            {
                startZoneCol.Header = followAdditionalTop
                    ? "Left Length (From Top)"
                    : "Left Length (mm)";
                startZoneCol.IsReadOnly = followAdditionalTop || factorMode;
            }

            if (endZoneCol != null)
            {
                endZoneCol.Header = followAdditionalTop
                    ? "Right Length (From Top)"
                    : "Right Length (mm)";
                endZoneCol.IsReadOnly = followAdditionalTop || factorMode;
            }

            if (startFactorCol != null)
            {
                startFactorCol.Header = "Left Factor (*L)";
                startFactorCol.IsReadOnly = followAdditionalTop || !factorMode;
            }

            if (endFactorCol != null)
            {
                endFactorCol.Header = "Right Factor (*L)";
                endFactorCol.IsReadOnly = followAdditionalTop || !factorMode;
            }
        }

        private static void ApplyBeamNaviateSpanInfoToAdditionalRows(IList<BeamNaviateAdditionalRowUi> rows)
        {
            if (rows == null)
            {
                return;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                BeamNaviateAdditionalRowUi row = rows[i];
                if (row == null)
                {
                    continue;
                }

                row.SpanInfoText = (i + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void ApplyBeamNaviateSectionInfoToSpecialRows(IList<BeamNaviateSpecialRowUi> rows, int sectionCount)
        {
            if (rows == null)
            {
                return;
            }

            int safeCount = Math.Max(1, sectionCount);
            foreach (BeamNaviateSpecialRowUi row in rows.Where(x => x != null))
            {
                int section = ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200);
                row.SectionText = Math.Max(1, Math.Min(safeCount, section)).ToString(CultureInfo.InvariantCulture);
                int cage = ParseBeamNaviateIntOrDefault(row.CageText, 1, 1, 200);
                row.CageText = Math.Max(1, cage).ToString(CultureInfo.InvariantCulture);
                row.ModeText = NormalizeBeamNaviateSpecialModeText(row.ModeText);
            }
        }

        private static void ApplyBeamNaviateSectionInfoToSpecialCustomTies(
            IList<BeamNaviateSpecialSectionLineTieUiSpec> lineTies,
            IList<BeamNaviateSpecialSectionRectTieUiSpec> rectTies,
            int sectionCount)
        {
            int safeSectionCount = Math.Max(1, sectionCount);

            if (lineTies != null)
            {
                foreach (BeamNaviateSpecialSectionLineTieUiSpec tie in lineTies.Where(x => x != null))
                {
                    tie.SectionIndex = Math.Max(1, Math.Min(safeSectionCount, tie.SectionIndex));
                    tie.X0GridIndex = Math.Max(0, tie.X0GridIndex);
                    tie.Y0GridIndex = Math.Max(0, tie.Y0GridIndex);
                    tie.X1GridIndex = Math.Max(0, tie.X1GridIndex);
                    tie.Y1GridIndex = Math.Max(0, tie.Y1GridIndex);
                    NormalizeBeamNaviateSpecialLineTieSpec(tie);
                }
            }

            if (rectTies != null)
            {
                foreach (BeamNaviateSpecialSectionRectTieUiSpec tie in rectTies.Where(x => x != null))
                {
                    tie.SectionIndex = Math.Max(1, Math.Min(safeSectionCount, tie.SectionIndex));
                    int x0 = Math.Max(0, Math.Min(tie.X0GridIndex, tie.X1GridIndex));
                    int x1 = Math.Max(0, Math.Max(tie.X0GridIndex, tie.X1GridIndex));
                    int y0 = Math.Max(0, Math.Min(tie.Y0GridIndex, tie.Y1GridIndex));
                    int y1 = Math.Max(0, Math.Max(tie.Y0GridIndex, tie.Y1GridIndex));
                    tie.X0GridIndex = x0;
                    tie.X1GridIndex = x1;
                    tie.Y0GridIndex = y0;
                    tie.Y1GridIndex = y1;
                }
            }
        }

        private static void ConfigureBeamNaviateAdditionalGridColumns(DataGrid grid, bool factorMode, bool topRows)
        {
            if (grid == null || grid.Columns == null || grid.Columns.Count < 10)
            {
                return;
            }

            DataGridColumn spanCol = grid.Columns[0];
            DataGridColumn lenStartCol = grid.Columns[6];
            DataGridColumn lenEndCol = grid.Columns[7];
            DataGridColumn factorStartCol = grid.Columns[8];
            DataGridColumn factorEndCol = grid.Columns[9];

            if (spanCol != null)
            {
                spanCol.Header = "Span";
                spanCol.IsReadOnly = true;
            }

            if (lenStartCol != null)
            {
                lenStartCol.Header = topRows ? "Left Length" : "Start Length";
                lenStartCol.IsReadOnly = factorMode;
            }
            if (lenEndCol != null)
            {
                lenEndCol.Header = topRows ? "Right Length" : "End Length";
                lenEndCol.IsReadOnly = factorMode;
            }
            if (factorStartCol != null)
            {
                factorStartCol.Header = topRows ? "Left Factor" : "Start Factor";
                factorStartCol.IsReadOnly = !factorMode;
            }
            if (factorEndCol != null)
            {
                factorEndCol.Header = topRows ? "Right Factor" : "End Factor";
                factorEndCol.IsReadOnly = !factorMode;
            }
        }

        private void AutoFillBeamNaviateAdditionalFrozenColumns()
        {
            if (_beamNaviateAdditionalAutoFillUpdating)
            {
                return;
            }

            _beamNaviateAdditionalAutoFillUpdating = true;
            try
            {
                CommitBeamNaviateAdditionalRowEdits();

                double hostLengthMm = _beamRebarSelectedHostLengthsMm.FirstOrDefault();
                if (double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm) || hostLengthMm <= 1.0)
                {
                    hostLengthMm = 1000.0;
                }

                List<double> supportStationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
                List<double> spanStationsMm = BuildBeamPreviewSpanBoundaryStationsMm(hostLengthMm, supportStationsMm);
                IList<double> gridStationsMm = spanStationsMm.Count >= 2 ? spanStationsMm : null;

                int bottomGridMax = ResolveBeamNaviateAdditionalGridMaxIndex(_beamNaviateAdditionalBottomRows);
                int topGridMax = ResolveBeamNaviateAdditionalGridMaxIndex(_beamNaviateAdditionalTopRows);
                int stationGridMax = gridStationsMm != null ? Math.Max(1, gridStationsMm.Count - 1) : 1;
                int gridMaxIndex = Math.Max(stationGridMax, Math.Max(bottomGridMax, topGridMax));

                bool bottomFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddBottomInputModeCombo());
                bool topFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());

                foreach (BeamNaviateAdditionalRowUi row in _beamNaviateAdditionalBottomRows.Where(r => r != null))
                {
                    AutoFillBeamNaviateAdditionalRowFrozenColumns(
                        row,
                        bottomFactorMode,
                        0.0,
                        hostLengthMm,
                        gridMaxIndex,
                        gridStationsMm);
                }

                foreach (BeamNaviateAdditionalRowUi row in _beamNaviateAdditionalTopRows.Where(r => r != null))
                {
                    AutoFillBeamNaviateAdditionalRowFrozenColumns(
                        row,
                        topFactorMode,
                        0.0,
                        hostLengthMm,
                        gridMaxIndex,
                        gridStationsMm);
                }
            }
            finally
            {
                _beamNaviateAdditionalAutoFillUpdating = false;
            }
        }

        private static void AutoFillBeamNaviateAdditionalRowFrozenColumns(
            BeamNaviateAdditionalRowUi row,
            bool factorMode,
            double xMainStartMm,
            double xMainEndMm,
            int gridMaxIndex,
            IList<double> gridStationsMm)
        {
            if (row == null)
            {
                return;
            }

            if (!TryResolveBeamNaviateAdditionalReferenceLengthsMm(
                row,
                xMainStartMm,
                xMainEndMm,
                gridMaxIndex,
                gridStationsMm,
                out double startRefMm,
                out double endRefMm))
            {
                startRefMm = Math.Max(1.0, xMainEndMm - xMainStartMm);
                endRefMm = Math.Max(1.0, xMainEndMm - xMainStartMm);
            }

            if (factorMode)
            {
                if (TryResolveBeamNaviateFactorFromInput(row.StartFactorText, startRefMm, out double startFactor))
                {
                    row.StartLengthText = FormatBeamNaviateLengthCellText(Math.Max(0.0, startFactor) * Math.Max(0.0, startRefMm));
                }
                if (TryResolveBeamNaviateFactorFromInput(row.EndFactorText, endRefMm, out double endFactor))
                {
                    row.EndLengthText = FormatBeamNaviateLengthCellText(Math.Max(0.0, endFactor) * Math.Max(0.0, endRefMm));
                }
            }
            else
            {
                double startLenMm = ParseBeamNaviateAdditionalLengthTextMm(row.StartLengthText, startRefMm, interpretPlainAsFactor: false);
                double endLenMm = ParseBeamNaviateAdditionalLengthTextMm(row.EndLengthText, endRefMm, interpretPlainAsFactor: false);
                row.StartFactorText = FormatBeamNaviateFactorCellText(startRefMm > 1e-6 ? Math.Max(0.0, startLenMm) / startRefMm : 0.0);
                row.EndFactorText = FormatBeamNaviateFactorCellText(endRefMm > 1e-6 ? Math.Max(0.0, endLenMm) / endRefMm : 0.0);
            }
        }

        private static bool TryResolveBeamNaviateFactorFromInput(string rawText, double referenceLengthMm, out double factor)
        {
            factor = 0.0;

            if (TryParseBeamNaviateLengthRatioOfL(rawText, out double ratioL))
            {
                factor = Math.Max(0.0, ratioL);
                return true;
            }

            if (TryParseBeamNaviatePlainFactorText(rawText, out double plainFactor))
            {
                factor = Math.Max(0.0, plainFactor);
                return true;
            }

            if (referenceLengthMm > 1e-6 && TryParseBeamNaviateMmText(rawText, out double mmValue))
            {
                factor = Math.Max(0.0, mmValue) / referenceLengthMm;
                return true;
            }

            return false;
        }

        private static int ResolveBeamNaviateAdditionalGridMaxIndex(IEnumerable<BeamNaviateAdditionalRowUi> rows)
        {
            int maxIndex = 1;
            foreach (BeamNaviateAdditionalRowUi row in rows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
            {
                if (row == null)
                {
                    continue;
                }

                int sg = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
                int eg = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);
                maxIndex = Math.Max(maxIndex, Math.Max(sg, eg));
            }

            return Math.Max(1, maxIndex);
        }

        private static string FormatBeamNaviateLengthCellText(double lengthMm)
        {
            if (double.IsNaN(lengthMm) || double.IsInfinity(lengthMm))
            {
                return "";
            }

            return Math.Max(0.0, lengthMm).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatBeamNaviateFactorCellText(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor))
            {
                return "";
            }

            return Math.Max(0.0, factor).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string NormalizeBeamNaviateLengthText(string rawText, string fallback)
        {
            string text = (rawText ?? "").Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        private void InitializeBeamNaviateStirrupGlobalTextsFromUi()
        {
            _beamNaviateStirrupGlobalS1Text = NormalizeBeamNaviateLengthText(BeamNaviateStirrupStartSpacingTextBox?.Text, "150 mm");
            _beamNaviateStirrupGlobalS2Text = NormalizeBeamNaviateLengthText(BeamNaviateStirrupMiddleSpacingTextBox?.Text, "200 mm");
            _beamNaviateStirrupGlobalS3Text = NormalizeBeamNaviateLengthText(BeamNaviateStirrupEndSpacingTextBox?.Text, "150 mm");
        }

        private void UpdateBeamNaviateStirrupSpanOverrideFromInputs()
        {
            string s1 = NormalizeBeamNaviateLengthText(BeamNaviateStirrupStartSpacingTextBox?.Text, "150 mm");
            string s2 = NormalizeBeamNaviateLengthText(BeamNaviateStirrupMiddleSpacingTextBox?.Text, "200 mm");
            string s3 = NormalizeBeamNaviateLengthText(BeamNaviateStirrupEndSpacingTextBox?.Text, "150 mm");

            if (_beamNaviateSelectedPreviewSpanIndex < 0)
            {
                _beamNaviateStirrupGlobalS1Text = s1;
                _beamNaviateStirrupGlobalS2Text = s2;
                _beamNaviateStirrupGlobalS3Text = s3;
                SyncBeamNaviateStirrupRowsFromState();
                return;
            }

            _beamNaviateStirrupSpanOverrides[_beamNaviateSelectedPreviewSpanIndex] = new BeamNaviateStirrupSpanOverrideUi
            {
                SpanIndex = _beamNaviateSelectedPreviewSpanIndex,
                StartSpacingText = s1,
                MiddleSpacingText = s2,
                EndSpacingText = s3,
                StartZoneText = "0 mm",
                EndZoneText = "0 mm"
            };
            SyncBeamNaviateStirrupRowsFromState();
        }

        private void ApplyBeamNaviateStirrupInputsForSelectedSpan()
        {
            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                if (BeamNaviateStirrupStartSpacingTextBox == null ||
                    BeamNaviateStirrupMiddleSpacingTextBox == null ||
                    BeamNaviateStirrupEndSpacingTextBox == null)
                {
                    return;
                }

                if (_beamNaviateSelectedPreviewSpanIndex < 0)
                {
                    BeamNaviateStirrupStartSpacingTextBox.Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm");
                    BeamNaviateStirrupMiddleSpacingTextBox.Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm");
                    BeamNaviateStirrupEndSpacingTextBox.Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm");
                    return;
                }

                BeamNaviateStirrupSpanOverrideUi spanValues = _beamNaviateStirrupSpanOverrides.TryGetValue(_beamNaviateSelectedPreviewSpanIndex, out BeamNaviateStirrupSpanOverrideUi stored)
                    ? stored
                    : new BeamNaviateStirrupSpanOverrideUi
                    {
                        SpanIndex = _beamNaviateSelectedPreviewSpanIndex,
                        StartSpacingText = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm"),
                        MiddleSpacingText = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm"),
                        EndSpacingText = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm"),
                        StartZoneText = "0 mm",
                        EndZoneText = "0 mm"
                    };

                BeamNaviateStirrupStartSpacingTextBox.Text = NormalizeBeamNaviateLengthText(spanValues.StartSpacingText, "150 mm");
                BeamNaviateStirrupMiddleSpacingTextBox.Text = NormalizeBeamNaviateLengthText(spanValues.MiddleSpacingText, "200 mm");
                BeamNaviateStirrupEndSpacingTextBox.Text = NormalizeBeamNaviateLengthText(spanValues.EndSpacingText, "150 mm");
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            SelectBeamNaviateStirrupGridRowBySpan(_beamNaviateSelectedPreviewSpanIndex);
        }

        private bool TryBuildBeamNaviateStirrupSpanOverridesForRequest(out List<BeamNaviateStirrupSpanOverrideRequest> overrides)
        {
            overrides = new List<BeamNaviateStirrupSpanOverrideRequest>();
            SyncBeamNaviateStirrupOverridesFromRows();

            ComboBox zoneModeCombo = GetBeamNaviateStirrupZoneInputModeCombo();
            bool followAdditionalTop = IsBeamNaviateStirrupZoneFollowAdditionalTopMode(zoneModeCombo);
            bool factorMode = IsBeamNaviateStirrupZoneFactorMode(zoneModeCombo);

            if (!TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm"),
                    "Stirrup spacing S1",
                    allowZero: false,
                    out double globalS1Ft) ||
                !TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm"),
                    "Stirrup spacing S2",
                    allowZero: false,
                    out double globalS2Ft) ||
                !TryParseBeamNaviateMmTextToFeet(
                    NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm"),
                    "Stirrup spacing S3",
                    allowZero: false,
                    out double globalS3Ft))
            {
                return false;
            }

            double hostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
            if (hostLengthMm <= 1.0 || double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm))
            {
                hostLengthMm = 1000.0;
            }

            List<double> supportStationsMm = ResolveBeamPreviewDetectedSupportStations(hostLengthMm);
            List<double> spanStationsMm = BuildBeamPreviewSpanBoundaryStationsMm(hostLengthMm, supportStationsMm);
            if (spanStationsMm.Count < 2)
            {
                spanStationsMm = new List<double> { 0.0, hostLengthMm };
            }

            var spanLengthByIndexMm = new Dictionary<int, double>();
            for (int spanIndex = 0; spanIndex < spanStationsMm.Count - 1; spanIndex++)
            {
                double spanLengthMm = Math.Max(0.0, spanStationsMm[spanIndex + 1] - spanStationsMm[spanIndex]);
                spanLengthByIndexMm[spanIndex] = spanLengthMm;
            }

            var overrideBySpan = new Dictionary<int, BeamNaviateStirrupSpanOverrideRequest>();
            foreach (var entry in _beamNaviateStirrupSpanOverrides
                .Where(kvp => kvp.Key >= 0 && kvp.Value != null)
                .OrderBy(kvp => kvp.Key))
            {
                BeamNaviateStirrupSpanOverrideUi raw = entry.Value;
                int spanIndex = entry.Key;
                if (!TryParseBeamNaviateMmTextToFeet(
                        NormalizeBeamNaviateLengthText(raw.StartSpacingText, "150 mm"),
                        $"Stirrup span {spanIndex + 1} spacing S1",
                        allowZero: false,
                        out double s1Ft) ||
                    !TryParseBeamNaviateMmTextToFeet(
                        NormalizeBeamNaviateLengthText(raw.MiddleSpacingText, "200 mm"),
                        $"Stirrup span {spanIndex + 1} spacing S2",
                        allowZero: false,
                        out double s2Ft) ||
                    !TryParseBeamNaviateMmTextToFeet(
                        NormalizeBeamNaviateLengthText(raw.EndSpacingText, "150 mm"),
                        $"Stirrup span {spanIndex + 1} spacing S3",
                        allowZero: false,
                        out double s3Ft))
                {
                    return false;
                }

                double z1Ft = 0.0;
                double z3Ft = 0.0;
                if (!followAdditionalTop)
                {
                    string startZoneRaw = NormalizeBeamNaviateLengthText(raw.StartZoneText, "0 mm");
                    string endZoneRaw = NormalizeBeamNaviateLengthText(raw.EndZoneText, "0 mm");
                    if (factorMode)
                    {
                        double spanLengthMm = spanLengthByIndexMm.TryGetValue(spanIndex, out double spanMm) && spanMm > 1.0
                            ? spanMm
                            : hostLengthMm;
                        double z1Mm = ParseBeamNaviateAdditionalLengthTextMm(startZoneRaw, spanLengthMm, interpretPlainAsFactor: true);
                        double z3Mm = ParseBeamNaviateAdditionalLengthTextMm(endZoneRaw, spanLengthMm, interpretPlainAsFactor: true);
                        z1Ft = Math.Max(0.0, z1Mm) / 304.8;
                        z3Ft = Math.Max(0.0, z3Mm) / 304.8;
                    }
                    else if (!TryParseBeamNaviateMmTextToFeet(
                            startZoneRaw,
                            $"Stirrup span {spanIndex + 1} zone L1",
                            allowZero: true,
                            out z1Ft) ||
                             !TryParseBeamNaviateMmTextToFeet(
                            endZoneRaw,
                            $"Stirrup span {spanIndex + 1} zone L3",
                            allowZero: true,
                            out z3Ft))
                    {
                        return false;
                    }
                }

                overrideBySpan[spanIndex] = new BeamNaviateStirrupSpanOverrideRequest
                {
                    SpanIndex = spanIndex,
                    StartSpacingFt = s1Ft,
                    MiddleSpacingFt = s2Ft,
                    EndSpacingFt = s3Ft,
                    StartZoneLengthFt = z1Ft,
                    EndZoneLengthFt = z3Ft
                };
            }

            // Optional mode: force stirrup zones to follow Additional Top Bar per span.
            if (followAdditionalTop && BeamNaviateAddTopEnabledCheckBox?.IsChecked == true)
            {
                for (int spanIndex = 0; spanIndex < spanStationsMm.Count - 1; spanIndex++)
                {
                    double spanLengthMm = Math.Max(0.0, spanStationsMm[spanIndex + 1] - spanStationsMm[spanIndex]);
                    if (spanLengthMm <= 1.0 ||
                        !TryResolveBeamNaviateAdditionalTopZoneLengthsMm(spanIndex, spanLengthMm, out double topL1Mm, out double topL3Mm))
                    {
                        continue;
                    }

                    if (!overrideBySpan.TryGetValue(spanIndex, out BeamNaviateStirrupSpanOverrideRequest existingOverride))
                    {
                        existingOverride = new BeamNaviateStirrupSpanOverrideRequest
                        {
                            SpanIndex = spanIndex,
                            StartSpacingFt = globalS1Ft,
                            MiddleSpacingFt = globalS2Ft,
                            EndSpacingFt = globalS3Ft,
                            StartZoneLengthFt = 0.0,
                            EndZoneLengthFt = 0.0
                        };
                    }

                    if (topL1Mm > 0.0)
                    {
                        existingOverride.StartZoneLengthFt = topL1Mm / 304.8;
                    }
                    if (topL3Mm > 0.0)
                    {
                        existingOverride.EndZoneLengthFt = topL3Mm / 304.8;
                    }

                    if (existingOverride.StartZoneLengthFt > 1e-6 || existingOverride.EndZoneLengthFt > 1e-6)
                    {
                        overrideBySpan[spanIndex] = existingOverride;
                    }
                }
            }

            overrides = overrideBySpan
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => kvp.Value)
                .ToList();
            return true;
        }

        private bool TryResolveBeamNaviateAdditionalTopZoneLengthsMm(
            int spanIndex,
            double spanLengthMm,
            out double startZoneMm,
            out double endZoneMm)
        {
            startZoneMm = 0.0;
            endZoneMm = 0.0;

            if (spanIndex < 0 || spanLengthMm <= 1.0 || _beamNaviateAdditionalTopRows.Count == 0)
            {
                return false;
            }

            bool topFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());
            BeamNaviateAdditionalRowUi leftNodeRow = FindBeamPreviewAdditionalRowByGrid(_beamNaviateAdditionalTopRows, spanIndex, spanIndex);
            BeamNaviateAdditionalRowUi rightNodeRow = FindBeamPreviewAdditionalRowByGrid(_beamNaviateAdditionalTopRows, spanIndex + 1, spanIndex + 1);

            if (leftNodeRow != null)
            {
                string leftRawText = GetBeamNaviateAdditionalEndInputText(leftNodeRow, topFactorMode);
                startZoneMm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(leftRawText, spanLengthMm, topFactorMode));
            }

            if (rightNodeRow != null)
            {
                string rightRawText = GetBeamNaviateAdditionalStartInputText(rightNodeRow, topFactorMode);
                endZoneMm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(rightRawText, spanLengthMm, topFactorMode));
            }

            return startZoneMm > 1.0 || endZoneMm > 1.0;
        }

        private void QueueBeamNaviatePreviewRender()
        {
            _beamNaviatePreviewRenderPending = true;
            if (_beamNaviatePreviewRenderTimer == null)
            {
                _beamNaviatePreviewRenderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = BeamNaviatePreviewDebounceInterval
                };
                _beamNaviatePreviewRenderTimer.Tick += OnBeamNaviatePreviewRenderTimerTick;
            }

            _beamNaviatePreviewRenderTimer.Stop();
            _beamNaviatePreviewRenderTimer.Start();
        }

        private void FlushBeamNaviatePreviewRender()
        {
            _beamNaviatePreviewRenderTimer?.Stop();
            _beamNaviatePreviewRenderPending = false;
            RenderBeamNaviatePreviewCanvas();
        }

        private void OnBeamNaviatePreviewRenderTimerTick(object sender, EventArgs e)
        {
            _beamNaviatePreviewRenderTimer?.Stop();
            if (!_beamNaviatePreviewRenderPending)
            {
                return;
            }

            _beamNaviatePreviewRenderPending = false;
            RenderBeamNaviatePreviewCanvas();
        }

        private void SelectBeamNaviateTabByHeader(string headerText)
        {
            if (BeamNaviateTabControl == null || string.IsNullOrWhiteSpace(headerText))
            {
                return;
            }

            foreach (TabItem tab in BeamNaviateTabControl.Items.OfType<TabItem>())
            {
                string text = (tab.Header?.ToString() ?? "").Trim();
                if (text.IndexOf(headerText, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                BeamNaviateTabControl.SelectedItem = tab;
                ShowStatus("BEAM: switched to '" + text + "'.");
                return;
            }
        }

        private ComboBox GetBeamNaviateAddBottomInputModeCombo()
        {
            return FindName("BeamNaviateAddBottomInputModeCombo") as ComboBox;
        }

        private ComboBox GetBeamNaviateAddTopInputModeCombo()
        {
            return FindName("BeamNaviateAddTopInputModeCombo") as ComboBox;
        }

        private ComboBox GetBeamNaviateStirrupZoneInputModeCombo()
        {
            return FindName("BeamNaviateStirrupZoneInputModeCombo") as ComboBox;
        }

        private DataGrid GetBeamNaviateSpecialRowsGrid()
        {
            return FindName("BeamNaviateSpecialRowsGrid") as DataGrid;
        }

        private Button GetBeamNaviateSpecialAddRowButton()
        {
            return FindName("BeamNaviateSpecialAddRowButton") as Button;
        }

        private Button GetBeamNaviateSpecialDeleteRowButton()
        {
            return FindName("BeamNaviateSpecialDeleteRowButton") as Button;
        }

        private Button GetBeamNaviateSpecialSetDefaultButton()
        {
            return FindName("BeamNaviateSpecialSetDefaultButton") as Button;
        }

        private Canvas GetBeamNaviateSpecialSectionCanvas()
        {
            return FindName("BeamNaviateSpecialSectionCanvas") as Canvas;
        }

        private CheckBox GetBeamNaviateMainTopSecondLayerCheckBox()
        {
            return FindName("BeamNaviateMainTopSecondLayerCheckBox") as CheckBox;
        }

        private CheckBox GetBeamNaviateMainBottomSecondLayerCheckBox()
        {
            return FindName("BeamNaviateMainBottomSecondLayerCheckBox") as CheckBox;
        }

        private TextBox GetBeamNaviateMainTopBarCount2TextBox()
        {
            return FindName("BeamNaviateMainTopBarCount2TextBox") as TextBox;
        }

        private TextBox GetBeamNaviateMainBottomBarCount2TextBox()
        {
            return FindName("BeamNaviateMainBottomBarCount2TextBox") as TextBox;
        }

        private TextBlock GetBeamNaviateSpecialSectionInfoText()
        {
            return FindName("BeamNaviateSpecialSectionInfoText") as TextBlock;
        }

        private RadioButton GetBeamNaviateSpecialSectionModeSelectRadio()
        {
            return FindName("BeamNaviateSpecialSectionModeSelectRadio") as RadioButton;
        }

        private RadioButton GetBeamNaviateSpecialSectionModeDrawTieRadio()
        {
            return FindName("BeamNaviateSpecialSectionModeDrawTieRadio") as RadioButton;
        }

        private RadioButton GetBeamNaviateSpecialSectionModeDrawRectRadio()
        {
            return FindName("BeamNaviateSpecialSectionModeDrawRectRadio") as RadioButton;
        }

        private RadioButton GetBeamNaviateSpecialSectionModeDeleteRadio()
        {
            return FindName("BeamNaviateSpecialSectionModeDeleteRadio") as RadioButton;
        }

        private Button GetBeamNaviateSpecialSectionClearDrawnButton()
        {
            return FindName("BeamNaviateSpecialSectionClearDrawnButton") as Button;
        }

        private Button GetBeamNaviateSpecialSectionFitButton()
        {
            return FindName("BeamNaviateSpecialSectionFitButton") as Button;
        }

        private string GetBeamNaviateSettingName()
        {
            string typedName = (BeamNaviateSettingCombo?.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(typedName))
            {
                return typedName;
            }

            string selectedName = (GetBeamNaviateSelectedComboText(BeamNaviateSettingCombo) ?? "").Trim();
            return string.IsNullOrWhiteSpace(selectedName) ? "<In Session>" : selectedName;
        }

        private static string GetBeamNaviateSettingsFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "CamboBIM");
            return Path.Combine(dir, "beam-naviate-settings.json");
        }

        private void LoadBeamNaviateSettingsFromDisk()
        {
            _beamNaviateSettingsByName.Clear();

            string filePath = GetBeamNaviateSettingsFilePath();
            if (!File.Exists(filePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(filePath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                List<BeamNaviateSettingSnapshot> stored = CamboBimJson.Deserialize<List<BeamNaviateSettingSnapshot>>(json)
                    ?? new List<BeamNaviateSettingSnapshot>();
                foreach (BeamNaviateSettingSnapshot item in stored.Where(x => x != null))
                {
                    string name = (item.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(name) ||
                        string.Equals(name, "<In Session>", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    BeamNaviateSettingSnapshot clone = CloneBeamNaviateSettingSnapshot(item);
                    clone.Name = name;
                    _beamNaviateSettingsByName[name] = clone;
                }
            }
            catch
            {
            }
        }

        private void PersistBeamNaviateSettingsToDisk()
        {
            try
            {
                string filePath = GetBeamNaviateSettingsFilePath();
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<BeamNaviateSettingSnapshot> toPersist = _beamNaviateSettingsByName
                    .Where(kvp => kvp.Value != null &&
                        !string.Equals((kvp.Key ?? "").Trim(), "<In Session>", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kvp =>
                    {
                        BeamNaviateSettingSnapshot clone = CloneBeamNaviateSettingSnapshot(kvp.Value);
                        clone.Name = (kvp.Key ?? "").Trim();
                        return clone;
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .ToList();

                string json = CamboBimJson.Serialize(toPersist);
                File.WriteAllText(filePath, json, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static bool IsBeamNaviateAdditionalInputFactorMode(ComboBox combo)
        {
            string text = (GetBeamNaviateSelectedComboText(combo) ?? "").Trim();
            return text.IndexOf("factor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("(L)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(text, "L", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBeamNaviateStirrupZoneFollowAdditionalTopMode(ComboBox combo)
        {
            string text = (GetBeamNaviateSelectedComboText(combo) ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return text.IndexOf("follow", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   text.IndexOf("top", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsBeamNaviateStirrupZoneFactorMode(ComboBox combo)
        {
            if (IsBeamNaviateStirrupZoneFollowAdditionalTopMode(combo))
            {
                return false;
            }

            string text = (GetBeamNaviateSelectedComboText(combo) ?? "").Trim();
            return text.IndexOf("factor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("(L)", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(text, "L", StringComparison.OrdinalIgnoreCase);
        }

        private static BeamNaviateSettingSnapshot CloneBeamNaviateSettingSnapshot(BeamNaviateSettingSnapshot source)
        {
            if (source == null)
            {
                return new BeamNaviateSettingSnapshot();
            }

            return new BeamNaviateSettingSnapshot
            {
                Name = source.Name ?? "<In Session>",
                TopBarType = source.TopBarType ?? "DB16",
                TopSecondBarType = source.TopSecondBarType ?? source.TopBarType ?? "DB16",
                BottomBarType = source.BottomBarType ?? "DB16",
                BottomSecondBarType = source.BottomSecondBarType ?? source.BottomBarType ?? "DB16",
                SideBarType = source.SideBarType ?? "DB12",
                StirrupBarType = source.StirrupBarType ?? "DB10",
                SecondaryBarType = source.SecondaryBarType ?? "DB12",
                SpecialBarType = source.SpecialBarType ?? "DB10",
                AddBottomBarType = source.AddBottomBarType ?? "DB12",
                AddTopBarType = source.AddTopBarType ?? "DB12",
                TopLayers = source.TopLayers,
                TopCount = source.TopCount,
                TopSecondLayerEnabled = source.TopSecondLayerEnabled || source.TopLayers >= 2,
                TopSecondCount = source.TopSecondCount > 0 ? source.TopSecondCount : source.TopCount,
                BottomLayers = source.BottomLayers,
                BottomCount = source.BottomCount,
                BottomSecondLayerEnabled = source.BottomSecondLayerEnabled || source.BottomLayers >= 2,
                BottomSecondCount = source.BottomSecondCount > 0 ? source.BottomSecondCount : source.BottomCount,
                SideEnabled = source.SideEnabled,
                SideCount = source.SideCount,
                CoverMmText = source.CoverMmText ?? "40 mm",
                SideAnchorMmText = source.SideAnchorMmText ?? "200 mm",
                StirrupEnabled = source.StirrupEnabled,
                StirrupLayout = source.StirrupLayout ?? "L/4-L/2-L/4",
                StirrupZoneInputMode = string.IsNullOrWhiteSpace(source.StirrupZoneInputMode) ? "Follow Additional Top Bar" : source.StirrupZoneInputMode,
                StirrupS1Text = source.StirrupS1Text ?? "150 mm",
                StirrupS2Text = source.StirrupS2Text ?? "200 mm",
                StirrupS3Text = source.StirrupS3Text ?? "150 mm",
                AddBottomEnabled = source.AddBottomEnabled,
                AddBottomCount = source.AddBottomCount,
                AddBottomStartText = source.AddBottomStartText ?? "0.125L",
                AddBottomEndText = source.AddBottomEndText ?? "0.125L",
                AddBottomInputMode = string.IsNullOrWhiteSpace(source.AddBottomInputMode) ? "Length (mm)" : source.AddBottomInputMode,
                AddTopEnabled = source.AddTopEnabled,
                AddTopCount = source.AddTopCount,
                AddTopStartText = source.AddTopStartText ?? "0.3L",
                AddTopEndText = source.AddTopEndText ?? "0.3L",
                AddTopInputMode = string.IsNullOrWhiteSpace(source.AddTopInputMode) ? "Length (mm)" : source.AddTopInputMode,
                AddBottomRows = CloneBeamNaviateAdditionalRows(source.AddBottomRows),
                AddTopRows = CloneBeamNaviateAdditionalRows(source.AddTopRows),
                SecondaryEnabled = source.SecondaryEnabled,
                SecondaryCount = source.SecondaryCount,
                SecondaryStartText = source.SecondaryStartText ?? "600 mm",
                SecondaryEndText = source.SecondaryEndText ?? "600 mm",
                SpecialEnabled = source.SpecialEnabled,
                SpecialMode = source.SpecialMode ?? "Tie Stirrup",
                SpecialSpacingText = source.SpecialSpacingText ?? "150 mm",
                SpecialZoneText = source.SpecialZoneText ?? "800 mm",
                SpecialRows = CloneBeamNaviateSpecialRows(source.SpecialRows),
                SpecialCustomLineTies = CloneBeamNaviateSpecialLineTieUiSpecs(source.SpecialCustomLineTies),
                SpecialCustomRectTies = CloneBeamNaviateSpecialRectTieUiSpecs(source.SpecialCustomRectTies),
                AutoSplit = source.AutoSplit,
                MaxLengthText = source.MaxLengthText ?? "12000 mm",
                LapLengthText = source.LapLengthText ?? "600 mm",
                DeleteExisting = source.DeleteExisting
            };
        }

        private BeamNaviateSettingSnapshot CaptureBeamNaviateSnapshotFromUi(string nameOverride = null)
        {
            BeamNaviateAdditionalRowUi firstBottomRow = _beamNaviateAdditionalBottomRows.FirstOrDefault()
                ?? CreateBeamNaviateAdditionalDefaultRow(topRow: false);
            BeamNaviateAdditionalRowUi firstTopRow = _beamNaviateAdditionalTopRows.FirstOrDefault()
                ?? CreateBeamNaviateAdditionalDefaultRow(topRow: true);
            string addBottomInputModeText = GetBeamNaviateSelectedComboText(GetBeamNaviateAddBottomInputModeCombo());
            string addTopInputModeText = GetBeamNaviateSelectedComboText(GetBeamNaviateAddTopInputModeCombo());
            string stirrupZoneInputModeText = GetBeamNaviateSelectedComboText(GetBeamNaviateStirrupZoneInputModeCombo());

            return new BeamNaviateSettingSnapshot
            {
                Name = string.IsNullOrWhiteSpace(nameOverride) ? GetBeamNaviateSettingName() : nameOverride.Trim(),
                TopBarType = GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo),
                TopSecondBarType = string.IsNullOrWhiteSpace(GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarType2Combo))
                    ? GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo)
                    : GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarType2Combo),
                BottomBarType = GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo),
                BottomSecondBarType = string.IsNullOrWhiteSpace(GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarType2Combo))
                    ? GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo)
                    : GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarType2Combo),
                SideBarType = GetBeamNaviateSelectedComboText(BeamNaviateMainSideBarTypeCombo),
                StirrupBarType = GetBeamNaviateSelectedComboText(BeamNaviateStirrupBarTypeCombo),
                AddBottomBarType = (firstBottomRow.BarTypeName ?? "DB12").Trim(),
                AddTopBarType = (firstTopRow.BarTypeName ?? "DB12").Trim(),
                SecondaryBarType = GetBeamNaviateSelectedComboText(BeamNaviateSecondaryBarTypeCombo),
                SpecialBarType = GetBeamNaviateSelectedComboText(BeamNaviateSpecialBarTypeCombo),
                TopLayers = ResolveBeamNaviateMainLayerCount(topBar: true),
                TopCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40),
                TopSecondLayerEnabled = IsBeamNaviateSecondLayerEnabled(topBar: true),
                TopSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(
                    topBar: true,
                    firstLayerCount: ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40)),
                BottomLayers = ResolveBeamNaviateMainLayerCount(topBar: false),
                BottomCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainBottomBarCountTextBox?.Text, 2, 1, 40),
                BottomSecondLayerEnabled = IsBeamNaviateSecondLayerEnabled(topBar: false),
                BottomSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(
                    topBar: false,
                    firstLayerCount: ParseBeamNaviateIntOrDefault(BeamNaviateMainBottomBarCountTextBox?.Text, 2, 1, 40)),
                SideEnabled = BeamNaviateMainSideRebarCheckBox?.IsChecked == true,
                SideCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainSideBarCountTextBox?.Text, 1, 1, 20),
                CoverMmText = (BeamNaviateMainCoverTextBox?.Text ?? "40 mm").Trim(),
                SideAnchorMmText = (BeamNaviateMainSideAnchorLengthTextBox?.Text ?? "200 mm").Trim(),
                StirrupEnabled = BeamNaviateStirrupEnabledCheckBox?.IsChecked == true,
                StirrupLayout = GetBeamNaviateSelectedComboText(BeamNaviateStirrupLayoutCombo),
                StirrupZoneInputMode = string.IsNullOrWhiteSpace(stirrupZoneInputModeText)
                    ? "Follow Additional Top Bar"
                    : stirrupZoneInputModeText,
                StirrupS1Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm"),
                StirrupS2Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm"),
                StirrupS3Text = NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm"),
                AddBottomEnabled = _beamNaviateAddBottomEnabledState || BeamNaviateAddBottomEnabledCheckBox?.IsChecked == true,
                AddBottomCount = ParseBeamNaviateIntOrDefault(firstBottomRow.AmountText, 2, 1, 40),
                AddBottomStartText = (firstBottomRow.StartLengthText ?? "0.125L").Trim(),
                AddBottomEndText = (firstBottomRow.EndLengthText ?? "0.125L").Trim(),
                AddBottomInputMode = string.IsNullOrWhiteSpace(addBottomInputModeText)
                    ? "Length (mm)"
                    : addBottomInputModeText,
                AddTopEnabled = _beamNaviateAddTopEnabledState || BeamNaviateAddTopEnabledCheckBox?.IsChecked == true,
                AddTopCount = ParseBeamNaviateIntOrDefault(firstTopRow.AmountText, 2, 1, 40),
                AddTopStartText = (firstTopRow.StartLengthText ?? "0.3L").Trim(),
                AddTopEndText = (firstTopRow.EndLengthText ?? "0.3L").Trim(),
                AddTopInputMode = string.IsNullOrWhiteSpace(addTopInputModeText)
                    ? "Length (mm)"
                    : addTopInputModeText,
                AddBottomRows = CloneBeamNaviateAdditionalRows(_beamNaviateAdditionalBottomRows),
                AddTopRows = CloneBeamNaviateAdditionalRows(_beamNaviateAdditionalTopRows),
                SecondaryEnabled = BeamNaviateSecondaryEnabledCheckBox?.IsChecked == true,
                SecondaryCount = ParseBeamNaviateIntOrDefault(BeamNaviateSecondaryCountTextBox?.Text, 2, 1, 40),
                SecondaryStartText = (BeamNaviateSecondaryStartLengthTextBox?.Text ?? "600 mm").Trim(),
                SecondaryEndText = (BeamNaviateSecondaryEndLengthTextBox?.Text ?? "600 mm").Trim(),
                SpecialEnabled = BeamNaviateSpecialEnabledCheckBox?.IsChecked == true,
                SpecialMode = GetBeamNaviateSelectedComboText(BeamNaviateSpecialModeCombo),
                SpecialSpacingText = (BeamNaviateSpecialSpacingTextBox?.Text ?? "150 mm").Trim(),
                SpecialZoneText = (BeamNaviateSpecialZoneLengthTextBox?.Text ?? "800 mm").Trim(),
                SpecialRows = CloneBeamNaviateSpecialRows(_beamNaviateSpecialRows),
                SpecialCustomLineTies = CloneBeamNaviateSpecialLineTieUiSpecs(_beamNaviateSpecialCustomLineTies),
                SpecialCustomRectTies = CloneBeamNaviateSpecialRectTieUiSpecs(_beamNaviateSpecialCustomRectTies),
                AutoSplit = true,
                MaxLengthText = "12000 mm",
                LapLengthText = "600 mm",
                DeleteExisting = true
            };
        }

        private void ApplyBeamNaviateSnapshotToUi(BeamNaviateSettingSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                TrySetBeamNaviateComboSelection(BeamNaviateMainTopBarTypeCombo, snapshot.TopBarType);
                TrySetBeamNaviateComboSelection(
                    BeamNaviateMainTopBarType2Combo,
                    string.IsNullOrWhiteSpace(snapshot.TopSecondBarType) ? snapshot.TopBarType : snapshot.TopSecondBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateMainBottomBarTypeCombo, snapshot.BottomBarType);
                TrySetBeamNaviateComboSelection(
                    BeamNaviateMainBottomBarType2Combo,
                    string.IsNullOrWhiteSpace(snapshot.BottomSecondBarType) ? snapshot.BottomBarType : snapshot.BottomSecondBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateMainSideBarTypeCombo, snapshot.SideBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateStirrupBarTypeCombo, snapshot.StirrupBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateSecondaryBarTypeCombo, snapshot.SecondaryBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateSpecialBarTypeCombo, snapshot.SpecialBarType);
                TrySetBeamNaviateComboSelection(BeamNaviateStirrupLayoutCombo, snapshot.StirrupLayout);
                TrySetBeamNaviateComboSelection(
                    GetBeamNaviateStirrupZoneInputModeCombo(),
                    string.IsNullOrWhiteSpace(snapshot.StirrupZoneInputMode) ? "Follow Additional Top Bar" : snapshot.StirrupZoneInputMode);
                TrySetBeamNaviateComboSelection(BeamNaviateSpecialModeCombo, snapshot.SpecialMode);

                bool topSecondEnabled = snapshot.TopSecondLayerEnabled || snapshot.TopLayers >= 2;
                bool bottomSecondEnabled = snapshot.BottomSecondLayerEnabled || snapshot.BottomLayers >= 2;
                CheckBox topSecondLayerCheckBox = GetBeamNaviateMainTopSecondLayerCheckBox();
                CheckBox bottomSecondLayerCheckBox = GetBeamNaviateMainBottomSecondLayerCheckBox();
                if (topSecondLayerCheckBox != null) topSecondLayerCheckBox.IsChecked = topSecondEnabled;
                if (bottomSecondLayerCheckBox != null) bottomSecondLayerCheckBox.IsChecked = bottomSecondEnabled;
                if (BeamNaviateMainTopLayerCountTextBox != null) BeamNaviateMainTopLayerCountTextBox.Text = (topSecondEnabled ? 2 : 1).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateMainBottomLayerCountTextBox != null) BeamNaviateMainBottomLayerCountTextBox.Text = (bottomSecondEnabled ? 2 : 1).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateMainTopBarCountTextBox != null) BeamNaviateMainTopBarCountTextBox.Text = Math.Max(1, snapshot.TopCount).ToString(CultureInfo.InvariantCulture);
                TextBox topSecondCountTextBox = GetBeamNaviateMainTopBarCount2TextBox();
                TextBox bottomSecondCountTextBox = GetBeamNaviateMainBottomBarCount2TextBox();
                if (topSecondCountTextBox != null) topSecondCountTextBox.Text = Math.Max(1, snapshot.TopSecondCount > 0 ? snapshot.TopSecondCount : snapshot.TopCount).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateMainBottomBarCountTextBox != null) BeamNaviateMainBottomBarCountTextBox.Text = Math.Max(1, snapshot.BottomCount).ToString(CultureInfo.InvariantCulture);
                if (bottomSecondCountTextBox != null) bottomSecondCountTextBox.Text = Math.Max(1, snapshot.BottomSecondCount > 0 ? snapshot.BottomSecondCount : snapshot.BottomCount).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateMainSideRebarCheckBox != null) BeamNaviateMainSideRebarCheckBox.IsChecked = snapshot.SideEnabled;
                if (BeamNaviateMainSideBarCountTextBox != null) BeamNaviateMainSideBarCountTextBox.Text = Math.Max(1, snapshot.SideCount).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateMainCoverTextBox != null) BeamNaviateMainCoverTextBox.Text = string.IsNullOrWhiteSpace(snapshot.CoverMmText) ? "40 mm" : snapshot.CoverMmText;
                if (BeamNaviateMainSideAnchorLengthTextBox != null) BeamNaviateMainSideAnchorLengthTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SideAnchorMmText) ? "200 mm" : snapshot.SideAnchorMmText;

                if (BeamNaviateStirrupEnabledCheckBox != null) BeamNaviateStirrupEnabledCheckBox.IsChecked = snapshot.StirrupEnabled;
                _beamNaviateStirrupGlobalS1Text = string.IsNullOrWhiteSpace(snapshot.StirrupS1Text) ? "150 mm" : snapshot.StirrupS1Text;
                _beamNaviateStirrupGlobalS2Text = string.IsNullOrWhiteSpace(snapshot.StirrupS2Text) ? "200 mm" : snapshot.StirrupS2Text;
                _beamNaviateStirrupGlobalS3Text = string.IsNullOrWhiteSpace(snapshot.StirrupS3Text) ? "150 mm" : snapshot.StirrupS3Text;
                _beamNaviateStirrupSpanOverrides.Clear();
                _beamNaviateSelectedPreviewSpanIndex = -1;
                ApplyBeamNaviateStirrupInputsForSelectedSpan();

                SetBeamNaviateAdditionalEnabled(topRows: false, enabled: snapshot.AddBottomEnabled);
                TrySetBeamNaviateComboSelection(
                    GetBeamNaviateAddBottomInputModeCombo(),
                    string.IsNullOrWhiteSpace(snapshot.AddBottomInputMode) ? "Length (mm)" : snapshot.AddBottomInputMode);
                List<BeamNaviateAdditionalRowUi> bottomRows = CloneBeamNaviateAdditionalRows(snapshot.AddBottomRows);
                if (bottomRows.Count == 0)
                {
                    bottomRows.Add(new BeamNaviateAdditionalRowUi
                    {
                        LayerText = "2",
                        BarTypeName = string.IsNullOrWhiteSpace(snapshot.AddBottomBarType) ? "DB12" : snapshot.AddBottomBarType.Trim(),
                        AmountText = Math.Max(1, snapshot.AddBottomCount).ToString(CultureInfo.InvariantCulture),
                        StartGridText = "0",
                        EndGridText = "1",
                        StartLengthText = string.IsNullOrWhiteSpace(snapshot.AddBottomStartText) ? "0.125L" : snapshot.AddBottomStartText,
                        EndLengthText = string.IsNullOrWhiteSpace(snapshot.AddBottomEndText) ? "0.125L" : snapshot.AddBottomEndText,
                        StartFactorText = "0.125",
                        EndFactorText = "0.125"
                    });
                }
                ReplaceBeamNaviateAdditionalRows(_beamNaviateAdditionalBottomRows, bottomRows, topRows: false);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalBottomRows, snapshot.AddBottomBarType);

                SetBeamNaviateAdditionalEnabled(topRows: true, enabled: snapshot.AddTopEnabled);
                TrySetBeamNaviateComboSelection(
                    GetBeamNaviateAddTopInputModeCombo(),
                    string.IsNullOrWhiteSpace(snapshot.AddTopInputMode) ? "Length (mm)" : snapshot.AddTopInputMode);
                List<BeamNaviateAdditionalRowUi> topRows = CloneBeamNaviateAdditionalRows(snapshot.AddTopRows);
                if (topRows.Count == 0)
                {
                    topRows.Add(new BeamNaviateAdditionalRowUi
                    {
                        LayerText = "2",
                        BarTypeName = string.IsNullOrWhiteSpace(snapshot.AddTopBarType) ? "DB12" : snapshot.AddTopBarType.Trim(),
                        AmountText = Math.Max(1, snapshot.AddTopCount).ToString(CultureInfo.InvariantCulture),
                        StartGridText = "0",
                        EndGridText = "0",
                        StartLengthText = string.IsNullOrWhiteSpace(snapshot.AddTopStartText) ? "0.3L" : snapshot.AddTopStartText,
                        EndLengthText = string.IsNullOrWhiteSpace(snapshot.AddTopEndText) ? "0.3L" : snapshot.AddTopEndText,
                        StartFactorText = "0.3",
                        EndFactorText = "0.3"
                    });
                }
                ReplaceBeamNaviateAdditionalRows(_beamNaviateAdditionalTopRows, topRows, topRows: true);
                NormalizeBeamNaviateAdditionalRowBarTypes(_beamNaviateAdditionalTopRows, snapshot.AddTopBarType);

                if (BeamNaviateSecondaryEnabledCheckBox != null) BeamNaviateSecondaryEnabledCheckBox.IsChecked = snapshot.SecondaryEnabled;
                if (BeamNaviateSecondaryCountTextBox != null) BeamNaviateSecondaryCountTextBox.Text = Math.Max(1, snapshot.SecondaryCount).ToString(CultureInfo.InvariantCulture);
                if (BeamNaviateSecondaryStartLengthTextBox != null) BeamNaviateSecondaryStartLengthTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SecondaryStartText) ? "600 mm" : snapshot.SecondaryStartText;
                if (BeamNaviateSecondaryEndLengthTextBox != null) BeamNaviateSecondaryEndLengthTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SecondaryEndText) ? "600 mm" : snapshot.SecondaryEndText;

                if (BeamNaviateSpecialEnabledCheckBox != null) BeamNaviateSpecialEnabledCheckBox.IsChecked = snapshot.SpecialEnabled;
                if (BeamNaviateSpecialSpacingTextBox != null) BeamNaviateSpecialSpacingTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SpecialSpacingText) ? "150 mm" : snapshot.SpecialSpacingText;
                if (BeamNaviateSpecialZoneLengthTextBox != null) BeamNaviateSpecialZoneLengthTextBox.Text = string.IsNullOrWhiteSpace(snapshot.SpecialZoneText) ? "800 mm" : snapshot.SpecialZoneText;
                List<BeamNaviateSpecialRowUi> specialRows = CloneBeamNaviateSpecialRows(snapshot.SpecialRows);
                if (specialRows.Count == 0)
                {
                    BeamNaviateSpecialRowUi legacy = CreateBeamNaviateSpecialDefaultRow(1);
                    legacy.ModeText = string.IsNullOrWhiteSpace(snapshot.SpecialMode) ? "Tie Stirrup" : snapshot.SpecialMode.Trim();
                    legacy.SpacingText = string.IsNullOrWhiteSpace(snapshot.SpecialSpacingText) ? "150 mm" : snapshot.SpecialSpacingText.Trim();
                    string legacyZone = string.IsNullOrWhiteSpace(snapshot.SpecialZoneText) ? "800 mm" : snapshot.SpecialZoneText.Trim();
                    legacy.StartZoneText = legacyZone;
                    legacy.EndZoneText = legacyZone;
                    specialRows.Add(legacy);
                }
                _beamNaviateSpecialRows.Clear();
                foreach (BeamNaviateSpecialRowUi row in specialRows)
                {
                    _beamNaviateSpecialRows.Add(row);
                }
                EnsureBeamNaviateSpecialRowsInitialized();

                _beamNaviateSpecialCustomLineTies.Clear();
                foreach (BeamNaviateSpecialSectionLineTieUiSpec tie in CloneBeamNaviateSpecialLineTieUiSpecs(snapshot.SpecialCustomLineTies))
                {
                    _beamNaviateSpecialCustomLineTies.Add(tie);
                }

                _beamNaviateSpecialCustomRectTies.Clear();
                foreach (BeamNaviateSpecialSectionRectTieUiSpec tie in CloneBeamNaviateSpecialRectTieUiSpecs(snapshot.SpecialCustomRectTies))
                {
                    _beamNaviateSpecialCustomRectTies.Add(tie);
                }
                _beamNaviateSpecialSectionSelectedShapeKey = "";
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }

            UpdateBeamRebarDesignUiState();
        }

        private void RefreshBeamNaviateSettingComboItems(string preferredName)
        {
            if (BeamNaviateSettingCombo == null)
            {
                return;
            }

            string preserve = string.IsNullOrWhiteSpace(preferredName)
                ? (GetBeamNaviateSelectedComboText(BeamNaviateSettingCombo) ?? "<In Session>")
                : preferredName.Trim();

            bool previousSuspend = _beamNaviateUiEventsSuspended;
            _beamNaviateUiEventsSuspended = true;
            try
            {
                BeamNaviateSettingCombo.Items.Clear();
                List<string> names = _beamNaviateSettingsByName.Keys
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!names.Any(x => string.Equals(x, "<In Session>", StringComparison.OrdinalIgnoreCase)))
                {
                    names.Insert(0, "<In Session>");
                }
                else
                {
                    names = new[] { "<In Session>" }
                        .Concat(names.Where(x => !string.Equals(x, "<In Session>", StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }

                foreach (string name in names)
                {
                    BeamNaviateSettingCombo.Items.Add(new ComboBoxItem { Content = name });
                }

                if (!TrySetBeamNaviateComboSelection(BeamNaviateSettingCombo, preserve))
                {
                    TrySetBeamNaviateComboSelection(BeamNaviateSettingCombo, "<In Session>");
                }
                if (BeamNaviateSettingCombo.SelectedIndex < 0 && BeamNaviateSettingCombo.Items.Count > 0)
                {
                    BeamNaviateSettingCombo.SelectedIndex = 0;
                }
            }
            finally
            {
                _beamNaviateUiEventsSuspended = previousSuspend;
            }
        }

        private void ApplySelectedBeamNaviateSetting()
        {
            string selected = GetBeamNaviateSettingName();
            if (string.Equals(selected, "<In Session>", StringComparison.OrdinalIgnoreCase))
            {
                ShowStatus("BEAM: using in-session values.");
                return;
            }

            if (_beamNaviateSettingsByName.TryGetValue(selected, out BeamNaviateSettingSnapshot snapshot))
            {
                ApplyBeamNaviateSnapshotToUi(snapshot);
                ShowStatus($"BEAM: setting '{selected}' loaded.");
                return;
            }

            ShowStatus($"BEAM: setting '{selected}' not found.");
        }

        private void RenderBeamNaviateSpecialSectionCanvas()
        {
            Canvas canvas = GetBeamNaviateSpecialSectionCanvas();
            TextBlock infoText = GetBeamNaviateSpecialSectionInfoText();
            _beamNaviateSpecialSectionGridRenderState = null;
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();
            EnsureBeamNaviateSpecialRowsInitialized();

            int sectionCount = Math.Max(1, ResolveBeamNaviateInputSpanCount());
            int selectedSection = ResolveBeamNaviateSpecialSectionIndexFromSelection();
            bool specialEnabled = BeamNaviateSpecialEnabledCheckBox?.IsChecked == true;

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW < 20.0 || canvasH < 20.0)
            {
                if (infoText != null)
                {
                    infoText.Text = $"Beam section editor ready. Section {selectedSection}/{sectionCount}.";
                }

                return;
            }

            double sectionWidthMm = _beamRebarPrimaryHostWidthMm > 1.0 ? _beamRebarPrimaryHostWidthMm : 1200.0;
            double sectionDepthMm = _beamRebarPrimaryHostDepthMm > 1.0 ? _beamRebarPrimaryHostDepthMm : 750.0;
            if (sectionWidthMm < sectionDepthMm)
            {
                double swap = sectionWidthMm;
                sectionWidthMm = sectionDepthMm;
                sectionDepthMm = swap;
            }

            double margin = 18.0;
            double drawW = Math.Max(10.0, canvasW - (2.0 * margin));
            double drawH = Math.Max(10.0, canvasH - (2.0 * margin));
            double sx = drawW / Math.Max(100.0, sectionWidthMm);
            double sy = drawH / Math.Max(100.0, sectionDepthMm);
            double scale = Math.Min(sx, sy) * Math.Max(0.2, _beamNaviateSpecialSectionZoomFactor);
            double w = sectionWidthMm * scale;
            double h = sectionDepthMm * scale;
            double left = ((canvasW - w) * 0.5) + _beamNaviateSpecialSectionPanXPx;
            double top = ((canvasH - h) * 0.5) + _beamNaviateSpecialSectionPanYPx;

            var concreteRect = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = h,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(120, 160, 210)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(WpfColor.FromArgb(16, 120, 160, 210)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(concreteRect, left);
            Canvas.SetTop(concreteRect, top);
            canvas.Children.Add(concreteRect);

            if (!TryParseBeamNaviateMmText(BeamNaviateMainCoverTextBox?.Text, out double coverMm) || coverMm < 0.0)
            {
                coverMm = 40.0;
            }

            double tieInsetMm = Math.Max(coverMm, 20.0);
            double tieLeft = left + (tieInsetMm * scale);
            double tieTop = top + (tieInsetMm * scale);
            double tieW = Math.Max(10.0, w - (2.0 * tieInsetMm * scale));
            double tieH = Math.Max(10.0, h - (2.0 * tieInsetMm * scale));
            if (tieW <= 10.0 || tieH <= 10.0)
            {
                tieLeft = left + 6.0;
                tieTop = top + 6.0;
                tieW = Math.Max(10.0, w - 12.0);
                tieH = Math.Max(10.0, h - 12.0);
            }

            var stirrupRect = new System.Windows.Shapes.Rectangle
            {
                Width = tieW,
                Height = tieH,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(130, 60, 220)),
                StrokeThickness = 2.0,
                RadiusX = 8.0,
                RadiusY = 8.0,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(stirrupRect, tieLeft);
            Canvas.SetTop(stirrupRect, tieTop);
            canvas.Children.Add(stirrupRect);
            AddBeamNaviateSpecialSectionRectHookSymbols(
                canvas,
                tieLeft,
                tieTop,
                tieLeft + tieW,
                tieTop + tieH,
                WpfColor.FromRgb(244, 81, 74),
                thickness: 2.0,
                showAngleText: false);

            int topCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40);
            int bottomCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainBottomBarCountTextBox?.Text, 2, 1, 40);
            int topSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(topBar: true, firstLayerCount: topCount);
            int bottomSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(topBar: false, firstLayerCount: bottomCount);
            bool sideEnabled = BeamNaviateMainSideRebarCheckBox?.IsChecked == true;
            int sideCount = sideEnabled
                ? ParseBeamNaviateIntOrDefault(BeamNaviateMainSideBarCountTextBox?.Text, 1, 1, 20)
                : 0;
            int barsX = Math.Max(2, Math.Max(Math.Max(topCount, topSecondCount), Math.Max(bottomCount, bottomSecondCount)));
            int barsY = Math.Max(2, sideCount + 2);

            var gridState = new BeamNaviateSpecialSectionGridRenderState
            {
                TieLeftPx = tieLeft,
                TieTopPx = tieTop,
                TieRightPx = tieLeft + tieW,
                TieBottomPx = tieTop + tieH
            };
            foreach (double x in BuildLinearPositions(tieLeft, tieLeft + tieW, barsX))
            {
                gridState.GridXPx.Add(x);
            }
            foreach (double y in BuildLinearPositions(tieTop, tieTop + tieH, barsY))
            {
                gridState.GridYPx.Add(y);
            }
            _beamNaviateSpecialSectionGridRenderState = gridState;

            foreach (double x in gridState.GridXPx.Skip(1).Take(Math.Max(0, gridState.GridXPx.Count - 2)))
            {
                var guide = CreateSectionLine(x, tieTop, x, tieTop + tieH, WpfColor.FromArgb(115, 140, 140, 140), 1.0);
                guide.StrokeDashArray = new DoubleCollection { 2, 2 };
                guide.IsHitTestVisible = false;
                canvas.Children.Add(guide);
            }
            foreach (double y in gridState.GridYPx.Skip(1).Take(Math.Max(0, gridState.GridYPx.Count - 2)))
            {
                var guide = CreateSectionLine(tieLeft, y, tieLeft + tieW, y, WpfColor.FromArgb(115, 140, 140, 140), 1.0);
                guide.StrokeDashArray = new DoubleCollection { 2, 2 };
                guide.IsHitTestVisible = false;
                canvas.Children.Add(guide);
            }

            double barRadius = Math.Max(2.2, Math.Min(tieW, tieH) * 0.018);
            if (gridState.GridYPx.Count > 0)
            {
                double yTopBars = gridState.GridYPx.First();
                double yBottomBars = gridState.GridYPx.Last();
                foreach (double x in gridState.GridXPx)
                {
                    AddBeamPreviewCircle(
                        canvas,
                        x,
                        yTopBars,
                        barRadius,
                        WpfColor.FromRgb(198, 51, 49),
                        WpfColor.FromRgb(237, 108, 100));
                    AddBeamPreviewCircle(
                        canvas,
                        x,
                        yBottomBars,
                        barRadius,
                        WpfColor.FromRgb(198, 51, 49),
                        WpfColor.FromRgb(237, 108, 100));
                }
            }

            if (sideEnabled && sideCount > 0 && gridState.GridXPx.Count >= 2 && gridState.GridYPx.Count >= 3)
            {
                double xLeftBars = gridState.GridXPx.First();
                double xRightBars = gridState.GridXPx.Last();
                foreach (double y in gridState.GridYPx.Skip(1).Take(Math.Max(0, gridState.GridYPx.Count - 2)))
                {
                    AddBeamPreviewCircle(
                        canvas,
                        xLeftBars,
                        y,
                        barRadius,
                        WpfColor.FromRgb(60, 118, 194),
                        WpfColor.FromRgb(124, 172, 232));
                    AddBeamPreviewCircle(
                        canvas,
                        xRightBars,
                        y,
                        barRadius,
                        WpfColor.FromRgb(60, 118, 194),
                        WpfColor.FromRgb(124, 172, 232));
                }
            }

            DataGrid specialRowsGrid = GetBeamNaviateSpecialRowsGrid();
            BeamNaviateSpecialRowUi selectedRow = specialRowsGrid?.SelectedItem as BeamNaviateSpecialRowUi;
            int rowsInSection = 0;
            bool hasCustomShapesInSection = _beamNaviateSpecialCustomLineTies.Any(x => x != null && Math.Max(1, x.SectionIndex) == selectedSection) ||
                                            _beamNaviateSpecialCustomRectTies.Any(x => x != null && Math.Max(1, x.SectionIndex) == selectedSection);

            if (hasCustomShapesInSection)
            {
                for (int rectIndex = 0; rectIndex < _beamNaviateSpecialCustomRectTies.Count; rectIndex++)
                {
                    BeamNaviateSpecialSectionRectTieUiSpec spec = _beamNaviateSpecialCustomRectTies[rectIndex];
                    if (spec == null || Math.Max(1, spec.SectionIndex) != selectedSection)
                    {
                        continue;
                    }

                    int xMaxIdx = Math.Max(0, gridState.GridXPx.Count - 1);
                    int yMaxIdx = Math.Max(0, gridState.GridYPx.Count - 1);
                    int sx0 = Math.Max(0, Math.Min(xMaxIdx, Math.Min(spec.X0GridIndex, spec.X1GridIndex)));
                    int sx1 = Math.Max(0, Math.Min(xMaxIdx, Math.Max(spec.X0GridIndex, spec.X1GridIndex)));
                    int sy0 = Math.Max(0, Math.Min(yMaxIdx, Math.Min(spec.Y0GridIndex, spec.Y1GridIndex)));
                    int sy1 = Math.Max(0, Math.Min(yMaxIdx, Math.Max(spec.Y0GridIndex, spec.Y1GridIndex)));
                    if (!TryGetBeamNaviateSpecialSectionNodePixel(gridState, sx0, sy0, out double x0Px, out double y0Px) ||
                        !TryGetBeamNaviateSpecialSectionNodePixel(gridState, sx1, sy1, out double x1Px, out double y1Px))
                    {
                        continue;
                    }

                    double rowLeft = Math.Min(x0Px, x1Px);
                    double rowTop = Math.Min(y0Px, y1Px);
                    double rowRight = Math.Max(x0Px, x1Px);
                    double rowBottom = Math.Max(y0Px, y1Px);
                    if (rowRight <= rowLeft + 1.0 || rowBottom <= rowTop + 1.0)
                    {
                        continue;
                    }

                    string key = GetBeamNaviateSpecialRectTieKey(spec);
                    bool isSelected = string.Equals(key, _beamNaviateSpecialSectionSelectedShapeKey, StringComparison.Ordinal);
                    WpfColor rowStroke = isSelected
                        ? WpfColor.FromRgb(255, 160, 0)
                        : WpfColor.FromRgb(186, 74, 255);
                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rowRight - rowLeft,
                        Height = rowBottom - rowTop,
                        Stroke = new SolidColorBrush(rowStroke),
                        StrokeThickness = isSelected ? 2.8 : 2.1,
                        RadiusX = 6.0,
                        RadiusY = 6.0,
                        Fill = new SolidColorBrush(WpfColor.FromArgb(8, rowStroke.R, rowStroke.G, rowStroke.B)),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(rect, rowLeft);
                    Canvas.SetTop(rect, rowTop);
                    canvas.Children.Add(rect);
                    AddBeamNaviateSpecialSectionRectHookSymbols(
                        canvas,
                        rowLeft,
                        rowTop,
                        rowRight,
                        rowBottom,
                        WpfColor.FromRgb(244, 81, 74),
                        thickness: isSelected ? 2.4 : 2.0);

                    AddBeamNaviateSpecialSectionRowHitRegion(
                        canvas,
                        rowLeft - 4.0,
                        rowTop - 4.0,
                        (rowRight - rowLeft) + 8.0,
                        (rowBottom - rowTop) + 8.0,
                        $"BSC:R:{rectIndex}",
                        isSelected);
                    rowsInSection++;
                }

                for (int lineIndex = 0; lineIndex < _beamNaviateSpecialCustomLineTies.Count; lineIndex++)
                {
                    BeamNaviateSpecialSectionLineTieUiSpec spec = _beamNaviateSpecialCustomLineTies[lineIndex];
                    if (spec == null || Math.Max(1, spec.SectionIndex) != selectedSection)
                    {
                        continue;
                    }

                    BeamNaviateSpecialSectionLineTieUiSpec normalized = CloneBeamNaviateSpecialLineTieUiSpec(spec);
                    NormalizeBeamNaviateSpecialLineTieSpec(normalized);
                    normalized.X0GridIndex = Math.Max(0, Math.Min(gridState.GridXPx.Count - 1, normalized.X0GridIndex));
                    normalized.X1GridIndex = Math.Max(0, Math.Min(gridState.GridXPx.Count - 1, normalized.X1GridIndex));
                    normalized.Y0GridIndex = Math.Max(0, Math.Min(gridState.GridYPx.Count - 1, normalized.Y0GridIndex));
                    normalized.Y1GridIndex = Math.Max(0, Math.Min(gridState.GridYPx.Count - 1, normalized.Y1GridIndex));
                    if (!TryGetBeamNaviateSpecialSectionNodePixel(gridState, normalized.X0GridIndex, normalized.Y0GridIndex, out double x0Px, out double y0Px) ||
                        !TryGetBeamNaviateSpecialSectionNodePixel(gridState, normalized.X1GridIndex, normalized.Y1GridIndex, out double x1Px, out double y1Px))
                    {
                        continue;
                    }

                    if (Math.Abs(x0Px - x1Px) < 0.4 && Math.Abs(y0Px - y1Px) < 0.4)
                    {
                        continue;
                    }

                    string key = GetBeamNaviateSpecialLineTieKey(normalized);
                    bool isSelected = string.Equals(key, _beamNaviateSpecialSectionSelectedShapeKey, StringComparison.Ordinal);
                    WpfColor rowStroke = isSelected
                        ? WpfColor.FromRgb(255, 160, 0)
                        : WpfColor.FromRgb(0, 160, 255);
                    var line = CreateSectionLine(x0Px, y0Px, x1Px, y1Px, rowStroke, isSelected ? 2.8 : 2.2);
                    line.IsHitTestVisible = false;
                    canvas.Children.Add(line);
                    AddBeamNaviateSpecialSectionLineHookSymbols(
                        canvas,
                        x0Px,
                        y0Px,
                        x1Px,
                        y1Px,
                        WpfColor.FromRgb(244, 81, 74),
                        thickness: isSelected ? 2.4 : 2.0);

                    double hitLeft = Math.Min(x0Px, x1Px) - 6.0;
                    double hitTop = Math.Min(y0Px, y1Px) - 6.0;
                    double hitWidth = Math.Max(12.0, Math.Abs(x1Px - x0Px) + 12.0);
                    double hitHeight = Math.Max(12.0, Math.Abs(y1Px - y0Px) + 12.0);
                    AddBeamNaviateSpecialSectionRowHitRegion(
                        canvas,
                        hitLeft,
                        hitTop,
                        hitWidth,
                        hitHeight,
                        $"BSC:L:{lineIndex}",
                        isSelected);
                    rowsInSection++;
                }
            }
            else
            {
                for (int rowIndex = 0; rowIndex < _beamNaviateSpecialRows.Count; rowIndex++)
                {
                    BeamNaviateSpecialRowUi row = _beamNaviateSpecialRows[rowIndex];
                    if (row == null)
                    {
                        continue;
                    }

                    int rowSection = ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200);
                    if (rowSection != selectedSection)
                    {
                        continue;
                    }

                    rowsInSection++;
                    int cageIndex = Math.Max(1, ParseBeamNaviateIntOrDefault(row.CageText, 1, 1, 200));
                    string modeText = NormalizeBeamNaviateSpecialModeText(row.ModeText);
                    int modeLane = GetBeamNaviateSpecialModeLaneIndex(modeText);
                    bool isRect = modeText.IndexOf("rect", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isTie = modeText.IndexOf("tie", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isU = !isRect && !isTie;
                    bool isSelected = ReferenceEquals(row, selectedRow);

                    double insetPx = ((cageIndex - 1) * 10.0) + (modeLane * 2.5);
                    double rowLeft = tieLeft + 4.0 + insetPx;
                    double rowTop = tieTop + 4.0 + insetPx;
                    double rowRight = (tieLeft + tieW) - 4.0 - insetPx;
                    double rowBottom = (tieTop + tieH) - 4.0 - insetPx;
                    if (rowRight <= rowLeft + 8.0 || rowBottom <= rowTop + 8.0)
                    {
                        continue;
                    }

                    WpfColor rowStroke = isRect
                        ? WpfColor.FromRgb(186, 74, 255)
                        : (isTie ? WpfColor.FromRgb(0, 160, 255) : WpfColor.FromRgb(239, 83, 80));
                    if (isSelected)
                    {
                        rowStroke = WpfColor.FromRgb(255, 160, 0);
                    }

                    if (isRect)
                    {
                        var rect = new System.Windows.Shapes.Rectangle
                        {
                            Width = rowRight - rowLeft,
                            Height = rowBottom - rowTop,
                            Stroke = new SolidColorBrush(rowStroke),
                            StrokeThickness = isSelected ? 2.6 : 2.0,
                            RadiusX = 6.0,
                            RadiusY = 6.0,
                            Fill = new SolidColorBrush(WpfColor.FromArgb(8, rowStroke.R, rowStroke.G, rowStroke.B)),
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(rect, rowLeft);
                        Canvas.SetTop(rect, rowTop);
                        canvas.Children.Add(rect);
                        AddBeamNaviateSpecialSectionRectHookSymbols(
                            canvas,
                            rowLeft,
                            rowTop,
                            rowRight,
                            rowBottom,
                            WpfColor.FromRgb(244, 81, 74),
                            thickness: isSelected ? 2.4 : 1.9);
                    }
                    else if (isTie)
                    {
                        double xMid = (rowLeft + rowRight) * 0.5;
                        double yMid = (rowTop + rowBottom) * 0.5;
                        var v = CreateSectionLine(xMid, rowTop, xMid, rowBottom, rowStroke, isSelected ? 2.6 : 2.1);
                        v.IsHitTestVisible = false;
                        canvas.Children.Add(v);
                        var hLine = CreateSectionLine(rowLeft, yMid, rowRight, yMid, rowStroke, isSelected ? 2.6 : 2.1);
                        hLine.IsHitTestVisible = false;
                        canvas.Children.Add(hLine);
                        AddBeamNaviateSpecialSectionLineHookSymbols(
                            canvas,
                            xMid,
                            rowTop,
                            xMid,
                            rowBottom,
                            WpfColor.FromRgb(244, 81, 74),
                            thickness: isSelected ? 2.4 : 1.9);
                        AddBeamNaviateSpecialSectionLineHookSymbols(
                            canvas,
                            rowLeft,
                            yMid,
                            rowRight,
                            yMid,
                            WpfColor.FromRgb(244, 81, 74),
                            thickness: isSelected ? 2.4 : 1.9);
                    }
                    else if (isU)
                    {
                        double legInset = Math.Max(3.5, (rowRight - rowLeft) * 0.08);
                        double xLeft = rowLeft + legInset;
                        double xRight = rowRight - legInset;
                        double yTopLeg = rowTop + 1.0;
                        double yBottomLeg = rowBottom - 1.0;
                        var leftLeg = CreateSectionLine(xLeft, yTopLeg, xLeft, yBottomLeg, rowStroke, isSelected ? 2.4 : 2.0);
                        leftLeg.IsHitTestVisible = false;
                        canvas.Children.Add(leftLeg);
                        var rightLeg = CreateSectionLine(xRight, yTopLeg, xRight, yBottomLeg, rowStroke, isSelected ? 2.4 : 2.0);
                        rightLeg.IsHitTestVisible = false;
                        canvas.Children.Add(rightLeg);
                        var bottomLine = CreateSectionLine(xLeft, yBottomLeg, xRight, yBottomLeg, rowStroke, isSelected ? 2.4 : 2.0);
                        bottomLine.IsHitTestVisible = false;
                        canvas.Children.Add(bottomLine);
                    }

                    AddBeamNaviateSpecialSectionRowHitRegion(
                        canvas,
                        rowLeft - 3.0,
                        rowTop - 3.0,
                        (rowRight - rowLeft) + 6.0,
                        (rowBottom - rowTop) + 6.0,
                        $"BSR:{rowIndex}",
                        isSelected);
                }
            }

            if (_beamNaviateSpecialSectionPendingStartGridNode.HasValue &&
                TryGetBeamNaviateSpecialSectionNodePixel(
                    gridState,
                    _beamNaviateSpecialSectionPendingStartGridNode.Value.X,
                    _beamNaviateSpecialSectionPendingStartGridNode.Value.Y,
                    out double pendingX,
                    out double pendingY))
            {
                const double pendingMarkerSize = 16.0;
                var marker = new System.Windows.Shapes.Ellipse
                {
                    Width = pendingMarkerSize,
                    Height = pendingMarkerSize,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(0, 110, 255)),
                    StrokeThickness = 2.0,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(180, 255, 255, 255)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, pendingX - (pendingMarkerSize * 0.5));
                Canvas.SetTop(marker, pendingY - (pendingMarkerSize * 0.5));
                canvas.Children.Add(marker);
            }

            if (_beamNaviateSpecialSectionPendingStartGridNode.HasValue &&
                _beamNaviateSpecialSectionLastMouseCanvasPoint.HasValue &&
                TryGetBeamNaviateSpecialSectionNearestSnapNode(
                    _beamNaviateSpecialSectionLastMouseCanvasPoint.Value,
                    out int hoverX,
                    out int hoverY,
                    requireSnapRadius: !_beamNaviateSpecialSectionIsLeftDrawDragging))
            {
                var startNode = _beamNaviateSpecialSectionPendingStartGridNode.Value;
                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie)
                {
                    NormalizeBeamNaviateSpecialSectionLineToOrthogonal(startNode.X, startNode.Y, hoverX, hoverY, out hoverX, out hoverY);
                    if (TryGetBeamNaviateSpecialSectionNodePixel(gridState, startNode.X, startNode.Y, out double sxDraw, out double syDraw) &&
                        TryGetBeamNaviateSpecialSectionNodePixel(gridState, hoverX, hoverY, out double exDraw, out double eyDraw) &&
                        !(Math.Abs(sxDraw - exDraw) < 0.1 && Math.Abs(syDraw - eyDraw) < 0.1))
                    {
                        var ghost = CreateSectionLine(sxDraw, syDraw, exDraw, eyDraw, WpfColor.FromArgb(210, 0, 160, 255), 2.4);
                        ghost.StrokeDashArray = new DoubleCollection { 4, 3 };
                        ghost.IsHitTestVisible = false;
                        canvas.Children.Add(ghost);
                    }
                }
                else if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie)
                {
                    if (TryGetBeamNaviateSpecialSectionNodePixel(gridState, startNode.X, startNode.Y, out double sxRect, out double syRect) &&
                        TryGetBeamNaviateSpecialSectionNodePixel(gridState, hoverX, hoverY, out double exRect, out double eyRect))
                    {
                        double gLeft = Math.Min(sxRect, exRect);
                        double gTop = Math.Min(syRect, eyRect);
                        double gW = Math.Abs(exRect - sxRect);
                        double gH = Math.Abs(eyRect - syRect);
                        if (gW > 1.0 && gH > 1.0)
                        {
                            var ghostRect = new System.Windows.Shapes.Rectangle
                            {
                                Width = gW,
                                Height = gH,
                                Stroke = new SolidColorBrush(WpfColor.FromArgb(210, 184, 74, 255)),
                                StrokeThickness = 2.0,
                                RadiusX = 6.0,
                                RadiusY = 6.0,
                                Fill = new SolidColorBrush(WpfColor.FromArgb(10, 184, 74, 255)),
                                StrokeDashArray = new DoubleCollection { 4, 3 },
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(ghostRect, gLeft);
                            Canvas.SetTop(ghostRect, gTop);
                            canvas.Children.Add(ghostRect);
                        }
                    }
                }
            }

            if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie ||
                _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie)
            {
                const double nodeHitSize = 30.0;
                for (int ix = 0; ix < gridState.GridXPx.Count; ix++)
                {
                    for (int iy = 0; iy < gridState.GridYPx.Count; iy++)
                    {
                        if (!TryGetBeamNaviateSpecialSectionNodePixel(gridState, ix, iy, out double nx, out double ny))
                        {
                            continue;
                        }

                        var dot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 5.0,
                            Height = 5.0,
                            Fill = new SolidColorBrush(WpfColor.FromRgb(0, 190, 90)),
                            StrokeThickness = 0.0,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(dot, nx - 2.5);
                        Canvas.SetTop(dot, ny - 2.5);
                        canvas.Children.Add(dot);

                        var nodeHit = new System.Windows.Shapes.Ellipse
                        {
                            Width = nodeHitSize,
                            Height = nodeHitSize,
                            Fill = new SolidColorBrush(WpfColor.FromArgb(1, 0, 0, 0)),
                            StrokeThickness = 0,
                            Tag = $"BSN:{ix}:{iy}",
                            Cursor = Cursors.Cross
                        };
                        nodeHit.MouseLeftButtonDown += OnBeamNaviateSpecialSectionNodeHitClick;
                        System.Windows.Controls.Panel.SetZIndex(nodeHit, 900);
                        Canvas.SetLeft(nodeHit, nx - (nodeHitSize * 0.5));
                        Canvas.SetTop(nodeHit, ny - (nodeHitSize * 0.5));
                        canvas.Children.Add(nodeHit);
                    }
                }
            }

            if (infoText != null)
            {
                string modeLabel;
                if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawTie)
                {
                    modeLabel = "Draw Tie";
                }
                else if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.DrawRectTie)
                {
                    modeLabel = "Draw Rect Tie";
                }
                else if (_beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete)
                {
                    modeLabel = "Delete";
                }
                else
                {
                    modeLabel = "Select";
                }

                infoText.Text =
                    $"Section {selectedSection}/{sectionCount} | Size {sectionWidthMm:0.#} x {sectionDepthMm:0.#} mm | " +
                    $"Bars {barsX}x{barsY} | {(hasCustomShapesInSection ? "Shapes" : "Rows")} in section: {rowsInSection} | Mode: {modeLabel} | Zoom {Math.Round(_beamNaviateSpecialSectionZoomFactor, 2):0.##}x";
                if (hasCustomShapesInSection)
                {
                    infoText.Text += " | Custom geometry.";
                }
                if (!specialEnabled)
                {
                    infoText.Text += " | Special stirrup disabled.";
                }
            }
        }

        private void AddBeamNaviateSpecialSectionRowHitRegion(
            Canvas canvas,
            double left,
            double top,
            double width,
            double height,
            string hitTag,
            bool isSelected)
        {
            if (canvas == null || width <= 1.0 || height <= 1.0)
            {
                return;
            }

            var hit = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(WpfColor.FromArgb(1, 0, 0, 0)),
                Stroke = isSelected ? new SolidColorBrush(WpfColor.FromArgb(140, 255, 160, 0)) : Brushes.Transparent,
                StrokeThickness = isSelected ? 1.4 : 0.0,
                Tag = hitTag ?? "",
                Cursor = _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete ? Cursors.No : Cursors.Hand,
                IsHitTestVisible = _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Delete ||
                                  _beamNaviateSpecialSectionEditMode == BeamNaviateSpecialSectionEditMode.Select
            };
            hit.MouseLeftButtonDown += OnBeamNaviateSpecialSectionRowShapeClick;
            Canvas.SetLeft(hit, left);
            Canvas.SetTop(hit, top);
            canvas.Children.Add(hit);
        }

        private static void AddBeamNaviateSpecialSectionRectHookSymbols(
            Canvas canvas,
            double left,
            double top,
            double right,
            double bottom,
            WpfColor color,
            double thickness = 2.0,
            bool showAngleText = false)
        {
            if (canvas == null || right <= left + 1.0 || bottom <= top + 1.0)
            {
                return;
            }

            double width = right - left;
            double height = bottom - top;
            double hookSize = Math.Max(8.0, Math.Min(16.0, Math.Min(width, height) * 0.18));
            double edgeInset = Math.Max(5.0, Math.Min(18.0, width * 0.06));
            double leftAnchorX = left + edgeInset;
            double rightAnchorX = right - edgeInset;
            double hookBaseY = bottom - Math.Max(1.0, hookSize * 0.08);
            var accent180 = WpfColor.FromRgb(255, 112, 67);
            var accent135 = WpfColor.FromRgb(255, 82, 82);

            AddBeamNaviateSpecialSectionHook180BottomLeft(
                canvas,
                new System.Windows.Point(leftAnchorX, hookBaseY),
                accent180,
                thickness,
                hookSize);
            AddBeamNaviateSpecialSectionHook135BottomRight(
                canvas,
                new System.Windows.Point(rightAnchorX, hookBaseY - (hookSize * 0.04)),
                accent135,
                thickness,
                hookSize);

            if (showAngleText)
            {
                AddBeamNaviateSpecialSectionHookLabel(
                    canvas,
                    "180",
                    new System.Windows.Point(leftAnchorX + (hookSize * 1.20), hookBaseY - (hookSize * 0.40)),
                    accent180);
                AddBeamNaviateSpecialSectionHookLabel(
                    canvas,
                    "135",
                    new System.Windows.Point(rightAnchorX - (hookSize * 1.10), hookBaseY - (hookSize * 0.95)),
                    accent135);
            }
        }

        private static void AddBeamNaviateSpecialSectionLineHookSymbols(
            Canvas canvas,
            double x0,
            double y0,
            double x1,
            double y1,
            WpfColor color,
            double thickness = 2.0)
        {
            if (canvas == null)
            {
                return;
            }

            var tangent = new System.Windows.Vector(x1 - x0, y1 - y0);
            double length = tangent.Length;
            if (length <= 1e-6)
            {
                return;
            }

            tangent.Normalize();
            double hookSize = Math.Max(7.0, Math.Min(14.0, length * 0.20));
            var accent180 = WpfColor.FromRgb(255, 112, 67);
            var accent135 = WpfColor.FromRgb(255, 82, 82);

            bool vertical = Math.Abs(x1 - x0) <= Math.Abs(y1 - y0);
            if (vertical)
            {
                double x = (x0 + x1) * 0.5;
                double yTop = Math.Min(y0, y1) + 0.8;
                double yBottom = Math.Max(y0, y1) - 0.8;
                AddBeamNaviateSpecialSectionHook135TopRight(
                    canvas,
                    new System.Windows.Point(x, yTop),
                    accent135,
                    thickness,
                    hookSize);
                AddBeamNaviateSpecialSectionHook180BottomRight(
                    canvas,
                    new System.Windows.Point(x, yBottom),
                    accent180,
                    thickness,
                    hookSize);
            }
            else
            {
                var normal = new System.Windows.Vector(0.0, -1.0);
                AddBeamNaviateSpecialSectionHook180(
                    canvas,
                    new System.Windows.Point(Math.Min(x0, x1) + 0.8, (y0 + y1) * 0.5),
                    new System.Windows.Vector(-1.0, 0.0),
                    normal,
                    accent180,
                    thickness,
                    hookSize);
                AddBeamNaviateSpecialSectionHook135(
                    canvas,
                    new System.Windows.Point(Math.Max(x0, x1) - 0.8, (y0 + y1) * 0.5),
                    new System.Windows.Vector(1.0, 0.0),
                    normal,
                    accent135,
                    thickness,
                    hookSize);
            }
        }

        private static void AddBeamNaviateSpecialSectionHook135TopRight(
            Canvas canvas,
            System.Windows.Point anchor,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = new System.Windows.Point(anchor.X + (size * 0.50), anchor.Y);
            System.Windows.Point p2 = new System.Windows.Point(anchor.X + (size * 0.88), anchor.Y + (size * 0.38));
            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook180BottomRight(
            Canvas canvas,
            System.Windows.Point anchor,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = new System.Windows.Point(anchor.X + (size * 0.50), anchor.Y);
            System.Windows.Point p2 = new System.Windows.Point(p1.X, anchor.Y - (size * 0.52));
            System.Windows.Point p3 = new System.Windows.Point(anchor.X + (size * 0.08), p2.Y);
            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p2, p3, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook180BottomLeft(
            Canvas canvas,
            System.Windows.Point anchor,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = new System.Windows.Point(anchor.X + (size * 0.46), anchor.Y);
            System.Windows.Point p2 = new System.Windows.Point(p1.X, anchor.Y - (size * 0.52));
            System.Windows.Point p3 = new System.Windows.Point(anchor.X + (size * 0.04), p2.Y);
            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p2, p3, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook135BottomLeft(
            Canvas canvas,
            System.Windows.Point anchor,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = new System.Windows.Point(anchor.X + (size * 0.40), anchor.Y);
            System.Windows.Point p2 = new System.Windows.Point(anchor.X + (size * 0.82), anchor.Y - (size * 0.40));
            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook135BottomRight(
            Canvas canvas,
            System.Windows.Point anchor,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = new System.Windows.Point(anchor.X - (size * 0.40), anchor.Y);
            System.Windows.Point p2 = new System.Windows.Point(anchor.X - (size * 0.82), anchor.Y - (size * 0.40));
            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook180(
            Canvas canvas,
            System.Windows.Point anchor,
            System.Windows.Vector outwardDirection,
            System.Windows.Vector normalDirection,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            if (!TryNormalizeBeamNaviateHookBasis(ref outwardDirection, ref normalDirection))
            {
                return;
            }

            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = anchor + (normalDirection * (size * 0.56));
            System.Windows.Point p2 = p1 + (outwardDirection * (size * 0.45));
            System.Windows.Point p3 = p2 - (normalDirection * (size * 0.92));
            System.Windows.Point p4 = p3 - (outwardDirection * (size * 0.28));

            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p2, p3, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p3, p4, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHook135(
            Canvas canvas,
            System.Windows.Point anchor,
            System.Windows.Vector outwardDirection,
            System.Windows.Vector normalDirection,
            WpfColor color,
            double thickness,
            double hookSize)
        {
            if (!TryNormalizeBeamNaviateHookBasis(ref outwardDirection, ref normalDirection))
            {
                return;
            }

            double size = Math.Max(5.0, hookSize);
            System.Windows.Point p1 = anchor + (normalDirection * (size * 0.52));
            System.Windows.Point p2 = p1 + (outwardDirection * (size * 0.38));
            System.Windows.Vector bendDirection = (-normalDirection) + (outwardDirection * 0.82);
            if (bendDirection.Length <= 1e-6)
            {
                bendDirection = normalDirection;
            }
            bendDirection.Normalize();
            System.Windows.Point p3 = p2 + (bendDirection * (size * 0.72));

            AddBeamNaviateSpecialSectionHookStroke(canvas, anchor, p1, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p1, p2, color, thickness);
            AddBeamNaviateSpecialSectionHookStroke(canvas, p2, p3, color, thickness);
        }

        private static void AddBeamNaviateSpecialSectionHookStroke(
            Canvas canvas,
            System.Windows.Point from,
            System.Windows.Point to,
            WpfColor color,
            double thickness)
        {
            if (canvas == null)
            {
                return;
            }

            double baseThickness = Math.Max(1.5, thickness);
            var halo = CreateSectionLine(
                from.X,
                from.Y,
                to.X,
                to.Y,
                WpfColor.FromArgb(230, 255, 255, 255),
                baseThickness + 1.2);
            halo.StrokeStartLineCap = PenLineCap.Round;
            halo.StrokeEndLineCap = PenLineCap.Round;
            halo.IsHitTestVisible = false;
            canvas.Children.Add(halo);

            var line = CreateSectionLine(from.X, from.Y, to.X, to.Y, color, baseThickness);
            line.StrokeStartLineCap = PenLineCap.Round;
            line.StrokeEndLineCap = PenLineCap.Round;
            line.IsHitTestVisible = false;
            canvas.Children.Add(line);
        }

        private static void AddBeamNaviateSpecialSectionHookLabel(
            Canvas canvas,
            string text,
            System.Windows.Point anchor,
            WpfColor accentColor)
        {
            if (canvas == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var badge = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromArgb(220, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(170, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(0.8),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(2, 0, 2, 0),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9.0,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(accentColor),
                    IsHitTestVisible = false
                }
            };

            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = badge.DesiredSize;
            double left = Math.Max(0.0, anchor.X - (desired.Width * 0.5));
            double top = Math.Max(0.0, anchor.Y - desired.Height - 1.0);
            Canvas.SetLeft(badge, left);
            Canvas.SetTop(badge, top);
            canvas.Children.Add(badge);
        }

        private static bool TryNormalizeBeamNaviateHookBasis(
            ref System.Windows.Vector outwardDirection,
            ref System.Windows.Vector normalDirection)
        {
            if (outwardDirection.Length <= 1e-6)
            {
                return false;
            }

            outwardDirection.Normalize();
            if (normalDirection.Length <= 1e-6)
            {
                normalDirection = new System.Windows.Vector(-outwardDirection.Y, outwardDirection.X);
            }

            double projected = System.Windows.Vector.Multiply(normalDirection, outwardDirection);
            normalDirection -= (outwardDirection * projected);
            if (normalDirection.Length <= 1e-6)
            {
                normalDirection = new System.Windows.Vector(-outwardDirection.Y, outwardDirection.X);
            }

            if (normalDirection.Length <= 1e-6)
            {
                return false;
            }

            normalDirection.Normalize();
            return true;
        }

        private void RenderBeamNaviatePreviewCanvas()
        {
            Canvas canvas = BeamNaviatePreviewCanvas;
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();
            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (double.IsNaN(w) || w < 80.0) w = 520.0;
            if (double.IsNaN(h) || h < 80.0) h = Math.Max(canvas.MinHeight, 260.0);
            canvas.ClipToBounds = true;
            canvas.Clip = new RectangleGeometry(new Rect(0.0, 0.0, Math.Max(1.0, w), Math.Max(1.0, h)));

            var background = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1.0, w),
                Height = Math.Max(1.0, h),
                Fill = new LinearGradientBrush(
                    WpfColor.FromRgb(250, 251, 253),
                    WpfColor.FromRgb(236, 240, 246),
                    90.0),
                Stroke = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(background, 0.0);
            Canvas.SetTop(background, 0.0);
            canvas.Children.Add(background);

            ResolveBeamPreviewZoneLengthsMm(out double leftZoneMm, out double midZoneMm, out double rightZoneMm, out double totalZoneMm);
            leftZoneMm = Math.Max(200.0, leftZoneMm);
            midZoneMm = Math.Max(600.0, midZoneMm);
            rightZoneMm = Math.Max(200.0, rightZoneMm);
            totalZoneMm = Math.Max(2000.0, leftZoneMm + midZoneMm + rightZoneMm);

            double measuredHostLengthMm = ResolveBeamPreviewPrimaryHostLengthMm();
            bool hasMeasuredHostLength = measuredHostLengthMm > 1.0;
            List<BeamPreviewSupportUi> detectedSupports = ResolveBeamPreviewDetectedSupports(measuredHostLengthMm);
            List<double> detectedSupportStationsMm = detectedSupports
                .Select(x => x.StationMm)
                .ToList();

            double clearSpanMm = hasMeasuredHostLength ? measuredHostLengthMm : totalZoneMm;
            double leftSupportMm = hasMeasuredHostLength
                ? 0.0
                : Math.Max(350.0, Math.Min(clearSpanMm * 0.60, Math.Max(400.0, leftZoneMm * 0.85)));
            double rightSupportMm = hasMeasuredHostLength
                ? 0.0
                : Math.Max(350.0, Math.Min(clearSpanMm * 0.60, Math.Max(400.0, rightZoneMm * 0.85)));
            double totalLengthMm = hasMeasuredHostLength
                ? clearSpanMm
                : (leftSupportMm + clearSpanMm + rightSupportMm);

            double drawLeft = 24.0;
            double drawRight = Math.Max(drawLeft + 240.0, w - 24.0);
            double drawTop = 14.0;
            double drawBottom = Math.Max(drawTop + 140.0, h - 56.0);
            double drawWidth = drawRight - drawLeft;
            double drawHeight = drawBottom - drawTop;
            if (drawWidth <= 30.0 || drawHeight <= 30.0)
            {
                return;
            }

            double supportLeftWidth;
            double supportRightWidth;
            double beamWidth;
            double zoomFactor = Math.Max(0.6, Math.Min(2.8, _beamNaviatePreviewZoomFactor));
            double baseLengthMm = hasMeasuredHostLength ? clearSpanMm : totalLengthMm;
            double baseMmToPx = drawWidth / Math.Max(1.0, baseLengthMm);
            double mmToPx = baseMmToPx * zoomFactor;
            double totalDrawWidth = Math.Max(40.0, baseLengthMm * mmToPx);
            double stationTolMm = Math.Max(120.0, clearSpanMm * 0.01);
            bool showStartSupport = hasMeasuredHostLength && detectedSupports.Any(s => s.StationMm <= stationTolMm);
            bool showEndSupport = hasMeasuredHostLength && detectedSupports.Any(s => s.StationMm >= (clearSpanMm - stationTolMm));
            if (!hasMeasuredHostLength)
            {
                showStartSupport = _beamRebarDetectedStartConnectionCount > 0;
                showEndSupport = _beamRebarDetectedEndConnectionCount > 0;
            }

            double drawCenterX = (drawLeft + drawRight) * 0.5;
            double xSupportStart = drawCenterX - (totalDrawWidth * 0.5);
            double xBeamStart;
            double xBeamEnd;
            double xSupportEnd;
            if (hasMeasuredHostLength)
            {
                supportLeftWidth = 0.0;
                supportRightWidth = 0.0;
                beamWidth = Math.Max(80.0, clearSpanMm * mmToPx);
                xBeamStart = xSupportStart;
                xBeamEnd = xBeamStart + beamWidth;
                xSupportEnd = xBeamEnd;
            }
            else
            {
                supportLeftWidth = leftSupportMm * mmToPx;
                beamWidth = clearSpanMm * mmToPx;
                supportRightWidth = rightSupportMm * mmToPx;
                xBeamStart = xSupportStart + supportLeftWidth;
                xBeamEnd = xBeamStart + beamWidth;
                xSupportEnd = xBeamEnd + supportRightWidth;
            }

            double beamCenterY = drawTop + (drawHeight * 0.56);
            double hostSectionRatio = 1.0;
            if (_beamRebarPrimaryHostWidthMm > 1.0 && _beamRebarPrimaryHostDepthMm > 1.0)
            {
                hostSectionRatio = _beamRebarPrimaryHostDepthMm / Math.Max(1.0, _beamRebarPrimaryHostWidthMm);
                hostSectionRatio = Math.Max(0.65, Math.Min(2.6, hostSectionRatio));
            }
            double beamHeight = Math.Max(24.0, Math.Min(drawHeight * 0.44, (drawHeight * 0.20) * hostSectionRatio));
            double supportHeight = Math.Max(beamHeight + 34.0, drawHeight * 0.42);
            double beamTop = beamCenterY - (beamHeight * 0.5);
            double beamBottom = beamCenterY + (beamHeight * 0.5);
            double supportTop = beamCenterY - (supportHeight * 0.5);
            double prismDepthX = Math.Max(8.0, Math.Min(20.0, drawWidth * 0.018));
            double prismDepthY = Math.Max(5.0, Math.Min(14.0, beamHeight * 0.42));

            if (supportLeftWidth > 1e-6)
            {
                DrawBeamPreviewPrism(
                    canvas,
                    xSupportStart,
                    supportTop,
                    Math.Max(1.0, supportLeftWidth),
                    supportHeight,
                    prismDepthX * 0.75,
                    prismDepthY * 0.75,
                    WpfColor.FromRgb(242, 242, 242),
                    WpfColor.FromRgb(250, 250, 250),
                    WpfColor.FromRgb(222, 222, 222),
                    WpfColor.FromRgb(68, 68, 68),
                    0.9);
            }

            if (supportRightWidth > 1e-6)
            {
                DrawBeamPreviewPrism(
                    canvas,
                    xBeamEnd,
                    supportTop,
                    Math.Max(1.0, supportRightWidth),
                    supportHeight,
                    prismDepthX * 0.75,
                    prismDepthY * 0.75,
                    WpfColor.FromRgb(242, 242, 242),
                    WpfColor.FromRgb(250, 250, 250),
                    WpfColor.FromRgb(222, 222, 222),
                    WpfColor.FromRgb(68, 68, 68),
                    0.9);
            }

            DrawBeamPreviewPrism(
                canvas,
                xBeamStart,
                beamTop,
                Math.Max(1.0, xBeamEnd - xBeamStart),
                beamHeight,
                prismDepthX,
                prismDepthY,
                WpfColor.FromRgb(214, 214, 214),
                WpfColor.FromRgb(232, 232, 232),
                WpfColor.FromRgb(194, 194, 194),
                WpfColor.FromRgb(68, 68, 68),
                1.0);

            double axisYTop = Math.Max(6.0, supportTop - 38.0);
            double leftAxisX = supportLeftWidth > 1e-6 ? xSupportStart + (supportLeftWidth * 0.5) : xBeamStart;
            double rightAxisX = supportRightWidth > 1e-6 ? xBeamEnd + (supportRightWidth * 0.5) : xBeamEnd;
            AddBeamPreviewAxisMarker(canvas, leftAxisX, axisYTop, drawBottom - 2.0, "0");
            AddBeamPreviewAxisMarker(canvas, rightAxisX, axisYTop, drawBottom - 2.0, "1");
            if (hasMeasuredHostLength && !showStartSupport)
            {
                AddBeamPreviewNodeDot(canvas, xBeamStart, beamCenterY);
            }
            if (hasMeasuredHostLength && !showEndSupport)
            {
                AddBeamPreviewNodeDot(canvas, xBeamEnd, beamCenterY);
            }
            if (hasMeasuredHostLength)
            {
                double supportStemTop = Math.Max(drawTop + 14.0, beamTop - Math.Max(24.0, beamHeight * 0.85));
                double supportStemBottom = Math.Min(drawBottom - 14.0, beamBottom + Math.Max(28.0, drawHeight * 0.34));
                double supportStemHeight = Math.Max(14.0, supportStemBottom - supportStemTop);

                foreach (BeamPreviewSupportUi support in detectedSupports)
                {
                    double stationMm = Math.Max(0.0, Math.Min(clearSpanMm, support.StationMm));
                    double supportWidthMm = support.WidthMm > 1.0 ? support.WidthMm : Math.Max(180.0, clearSpanMm * 0.02);
                    double supportWidth = Math.Max(10.0, Math.Min(drawWidth * 0.25, supportWidthMm * mmToPx));
                    double xStation = xBeamStart + (stationMm * mmToPx);
                    double yTop = supportStemTop;
                    double hSupport = supportStemHeight;
                    WpfColor supportColor = ResolveBeamPreviewSupportColor(support.KindKey);
                    double left = xStation - (supportWidth * 0.5);
                    left = Math.Max(drawLeft, Math.Min(drawRight - supportWidth, left));
                    DrawBeamPreviewPrism(
                        canvas,
                        left,
                        yTop,
                        supportWidth,
                        hSupport,
                        prismDepthX * 0.75,
                        prismDepthY * 0.75,
                        supportColor,
                        WpfColor.FromRgb(
                            (byte)Math.Min(255, supportColor.R + 18),
                            (byte)Math.Min(255, supportColor.G + 18),
                            (byte)Math.Min(255, supportColor.B + 18)),
                        WpfColor.FromRgb(
                            (byte)Math.Max(0, supportColor.R - 16),
                            (byte)Math.Max(0, supportColor.G - 16),
                            (byte)Math.Max(0, supportColor.B - 16)),
                        WpfColor.FromRgb(64, 64, 64),
                        0.9);

                    string supportLabel = ResolveBeamPreviewSupportLabel(support.KindKey, support.WidthMm);
                    if (!string.IsNullOrWhiteSpace(supportLabel))
                    {
                        var label = new TextBlock
                        {
                            Text = supportLabel,
                            Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 55, 55)),
                            FontSize = 10.0,
                            FontWeight = FontWeights.SemiBold,
                            IsHitTestVisible = false
                        };
                        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Size labelSize = label.DesiredSize;
                        Canvas.SetLeft(label, left + ((supportWidth - labelSize.Width) * 0.5));
                        Canvas.SetTop(label, yTop + 2.0);
                        canvas.Children.Add(label);
                    }
                }
            }

            double zoneLeftMm = Math.Max(0.0, Math.Min(clearSpanMm * 0.46, leftZoneMm));
            double zoneRightMm = Math.Max(0.0, Math.Min(clearSpanMm * 0.46, rightZoneMm));
            if ((zoneLeftMm + zoneRightMm) > (clearSpanMm - 120.0))
            {
                double scale = (clearSpanMm - 120.0) / Math.Max(1.0, zoneLeftMm + zoneRightMm);
                zoneLeftMm *= scale;
                zoneRightMm *= scale;
            }
            double zoneMiddleMm = Math.Max(120.0, clearSpanMm - zoneLeftMm - zoneRightMm);

            double xZoneLeftBreak = xBeamStart + (zoneLeftMm * mmToPx);
            double xZoneRightBreak = xBeamEnd - (zoneRightMm * mmToPx);
            if (xZoneRightBreak < xZoneLeftBreak)
            {
                double c = (xZoneLeftBreak + xZoneRightBreak) * 0.5;
                xZoneLeftBreak = c - 4.0;
                xZoneRightBreak = c + 4.0;
            }

            List<double> previewSpanStationsMm = BuildBeamPreviewSpanBoundaryStationsMm(clearSpanMm, detectedSupportStationsMm);
            string activeTab = GetBeamNaviateActiveTabHeader();
            bool tabMain = activeTab.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tabStirrup = activeTab.IndexOf("Stirrup", StringComparison.OrdinalIgnoreCase) >= 0 &&
                activeTab.IndexOf("Special", StringComparison.OrdinalIgnoreCase) < 0;
            bool tabAddBottom = activeTab.IndexOf("Additional Bottom", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tabAddTop = activeTab.IndexOf("Additional Top", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tabSecondary = activeTab.IndexOf("Secondary", StringComparison.OrdinalIgnoreCase) >= 0;
            bool tabSpecial = activeTab.IndexOf("Special", StringComparison.OrdinalIgnoreCase) >= 0;

            _beamNaviatePreviewSpans.Clear();
            for (int i = 0; i < previewSpanStationsMm.Count - 1; i++)
            {
                double s0 = previewSpanStationsMm[i];
                double s1 = previewSpanStationsMm[i + 1];
                if (s1 <= s0 + 1.0)
                {
                    continue;
                }

                _beamNaviatePreviewSpans.Add(new BeamPreviewSpanUi
                {
                    SpanIndex = i,
                    StartStationMm = s0,
                    EndStationMm = s1,
                    StartX = xBeamStart + (s0 * mmToPx),
                    EndX = xBeamStart + (s1 * mmToPx)
                });
            }

            bool hasSelectedSpan = _beamNaviateSelectedPreviewSpanIndex >= 0;
            bool selectedSpanValid = _beamNaviatePreviewSpans.Any(s => s.SpanIndex == _beamNaviateSelectedPreviewSpanIndex);
            if (hasSelectedSpan && !selectedSpanValid)
            {
                _beamNaviateSelectedPreviewSpanIndex = -1;
                ApplyBeamNaviateStirrupInputsForSelectedSpan();
            }

            if (tabStirrup)
            {
                foreach (BeamPreviewSpanUi span in _beamNaviatePreviewSpans)
                {
                    bool isSelectedSpan = span.SpanIndex == _beamNaviateSelectedPreviewSpanIndex;
                    double sx = Math.Min(span.StartX, span.EndX);
                    double ex = Math.Max(span.StartX, span.EndX);
                    var spanRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = Math.Max(1.0, ex - sx),
                        Height = beamHeight + 14.0,
                        Fill = isSelectedSpan
                            ? new SolidColorBrush(WpfColor.FromArgb(48, 54, 125, 210))
                            : new SolidColorBrush(WpfColor.FromArgb(16, 54, 125, 210)),
                        Stroke = new SolidColorBrush(isSelectedSpan
                            ? WpfColor.FromRgb(38, 93, 170)
                            : WpfColor.FromRgb(116, 148, 195)),
                        StrokeThickness = isSelectedSpan ? 1.6 : 1.0
                    };
                    Canvas.SetLeft(spanRect, sx);
                    Canvas.SetTop(spanRect, beamTop - 7.0);
                    canvas.Children.Add(spanRect);
                }
            }

            int previewSpanCount = Math.Max(1, previewSpanStationsMm.Count - 1);
            List<BeamNaviateAdditionalRowUi> previewBottomRows = NormalizeBeamNaviateAdditionalRowsForSpanBySpanInput(
                _beamNaviateAdditionalBottomRows,
                previewSpanCount,
                topRows: false);
            List<BeamNaviateAdditionalRowUi> previewTopRows = NormalizeBeamNaviateAdditionalRowsForSpanBySpanInput(
                _beamNaviateAdditionalTopRows,
                previewSpanCount,
                topRows: true);

            int previewGridMaxIndex = Math.Max(1, previewSpanStationsMm.Count - 1);
            foreach (BeamNaviateAdditionalRowUi row in previewBottomRows.Where(r => r != null))
            {
                int sg = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
                int eg = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);
                previewGridMaxIndex = Math.Max(previewGridMaxIndex, Math.Max(sg, eg));
            }
            foreach (BeamNaviateAdditionalRowUi row in previewTopRows.Where(r => r != null))
            {
                int sg = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
                int eg = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);
                previewGridMaxIndex = Math.Max(previewGridMaxIndex, Math.Max(sg, eg));
            }
            IList<double> previewGridStationsMm = previewSpanStationsMm.Count > 2 ? previewSpanStationsMm : null;
            int topLayers = ResolveBeamNaviateMainLayerCount(topBar: true);
            int topCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainTopBarCountTextBox?.Text, 2, 1, 40);
            int topSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(topBar: true, firstLayerCount: topCount);
            int bottomLayers = ResolveBeamNaviateMainLayerCount(topBar: false);
            int bottomCount = ParseBeamNaviateIntOrDefault(BeamNaviateMainBottomBarCountTextBox?.Text, 2, 1, 40);
            int bottomSecondCount = ResolveBeamNaviateSecondLayerBarCountOrFallback(topBar: false, firstLayerCount: bottomCount);
            int sideCount = BeamNaviateMainSideRebarCheckBox?.IsChecked == true
                ? ParseBeamNaviateIntOrDefault(BeamNaviateMainSideBarCountTextBox?.Text, 1, 1, 20)
                : 0;

            if (tabMain)
            {
                double inset = Math.Max(5.0, beamHeight * 0.16);
                var cage = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(8.0, beamWidth - 10.0),
                    Height = Math.Max(8.0, beamHeight - (2.0 * inset)),
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(244, 67, 54)),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(cage, xBeamStart + 5.0);
                Canvas.SetTop(cage, beamTop + inset);
                canvas.Children.Add(cage);

                DrawBeamPreviewBars(
                    canvas,
                    xBeamStart,
                    beamTop,
                    beamWidth,
                    beamHeight,
                    topLayers,
                    topCount,
                    topSecondCount,
                    bottomLayers,
                    bottomCount,
                    bottomSecondCount,
                    sideCount);
            }

            if (tabStirrup)
            {
                var leftZoneFill = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1.0, xZoneLeftBreak - xBeamStart),
                    Height = beamHeight - 2.0,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(96, 255, 82, 23)),
                    Stroke = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(leftZoneFill, xBeamStart + 1.0);
                Canvas.SetTop(leftZoneFill, beamTop + 1.0);
                canvas.Children.Add(leftZoneFill);

                var rightZoneFill = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1.0, xBeamEnd - xZoneRightBreak),
                    Height = beamHeight - 2.0,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(96, 255, 82, 23)),
                    Stroke = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rightZoneFill, xZoneRightBreak);
                Canvas.SetTop(rightZoneFill, beamTop + 1.0);
                canvas.Children.Add(rightZoneFill);
            }

            if (tabStirrup)
            {
                string layoutType = (GetBeamNaviateSelectedComboText(BeamNaviateStirrupLayoutCombo) ?? "L/4-L/2-L/4").Trim();

                if (_beamNaviatePreviewSpans.Count > 0)
                {
                    foreach (BeamPreviewSpanUi span in _beamNaviatePreviewSpans)
                    {
                        ResolveBeamPreviewStirrupSpanInputsMm(
                            span.SpanIndex,
                            Math.Max(0.0, span.EndStationMm - span.StartStationMm),
                            out double stirrupStartMm,
                            out double stirrupMiddleMm,
                            out double stirrupEndMm,
                            out double stirrupStartZoneMm,
                            out double stirrupEndZoneMm);
                        DrawBeamPreviewStirrupSpanLayout(
                            canvas,
                            span.StartX,
                            span.EndX,
                            beamTop,
                            beamBottom,
                            span.EndStationMm - span.StartStationMm,
                            layoutType,
                            stirrupStartMm,
                            stirrupMiddleMm,
                            stirrupEndMm,
                            stirrupStartZoneMm,
                            stirrupEndZoneMm);
                    }
                }
                else
                {
                    ResolveBeamPreviewStirrupSpanInputsMm(
                        -1,
                        clearSpanMm,
                        out double stirrupStartMm,
                        out double stirrupMiddleMm,
                        out double stirrupEndMm,
                        out double stirrupStartZoneMm,
                        out double stirrupEndZoneMm);
                    DrawBeamPreviewStirrupSpanLayout(
                        canvas,
                        xBeamStart,
                        xBeamEnd,
                        beamTop,
                        beamBottom,
                        clearSpanMm,
                        layoutType,
                        stirrupStartMm,
                        stirrupMiddleMm,
                        stirrupEndMm,
                        stirrupStartZoneMm,
                        stirrupEndZoneMm);
                }
            }

            bool addBottomEnabled = _beamNaviateAddBottomEnabledState || BeamNaviateAddBottomEnabledCheckBox?.IsChecked == true;
            bool addBottomFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddBottomInputModeCombo());
            if (tabAddBottom && addBottomEnabled)
            {
                int rowIndex = 0;
                foreach (BeamNaviateAdditionalRowUi row in previewBottomRows.Where(r => r != null).Take(6))
                {
                    int layerIndex = ParseBeamNaviateIntOrDefault(row.LayerText, 2, 1, 12);
                    double yBottomBar = beamBottom - 8.0 - ((Math.Max(1, layerIndex) - 1) * 4.5);
                    List<(double StartMm, double EndMm)> ranges = ExpandBeamPreviewAdditionalBottomRowRangesMm(
                        row,
                        0.0,
                        clearSpanMm,
                        previewGridMaxIndex,
                        previewGridStationsMm,
                        addBottomFactorMode);

                    foreach ((double rowStartMm, double rowEndMm) in ranges)
                    {
                        double sx = xBeamStart + Math.Max(0.0, Math.Min(clearSpanMm, rowStartMm)) * mmToPx;
                        double ex = xBeamStart + Math.Max(0.0, Math.Min(clearSpanMm, rowEndMm)) * mmToPx;
                        if ((ex - sx) < 24.0)
                        {
                            double m = (sx + ex) * 0.5;
                            sx = m - 12.0;
                            ex = m + 12.0;
                        }

                        AddBeamPreviewBarLine(
                            canvas,
                            sx,
                            ex,
                            yBottomBar,
                            rowIndex == 0 ? WpfColor.FromRgb(239, 83, 80) : WpfColor.FromRgb(56, 56, 56),
                            rowIndex == 0 ? 2.4 : 1.8);
                    }
                    rowIndex++;
                }
            }

            bool addTopEnabled = _beamNaviateAddTopEnabledState || BeamNaviateAddTopEnabledCheckBox?.IsChecked == true;
            bool addTopFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());
            if (tabAddTop && addTopEnabled)
            {
                int rowIndex = 0;
                foreach (BeamNaviateAdditionalRowUi row in previewTopRows.Where(r => r != null).Take(6))
                {
                    int layerIndex = ParseBeamNaviateIntOrDefault(row.LayerText, 2, 1, 12);
                    double yTopBar = beamTop + 8.0 + ((Math.Max(1, layerIndex) - 1) * 4.5);
                    if (!TryResolveBeamPreviewAdditionalRowRangeMm(
                        row,
                        0.0,
                        clearSpanMm,
                        previewGridMaxIndex,
                        previewGridStationsMm,
                        addTopFactorMode,
                        out double rowStartMm,
                        out double rowEndMm))
                    {
                        string startRawText = GetBeamNaviateAdditionalStartInputText(row, addTopFactorMode);
                        string endRawText = GetBeamNaviateAdditionalEndInputText(row, addTopFactorMode);
                        double startMm = ParseBeamNaviateAdditionalLengthTextMm(startRawText, clearSpanMm, addTopFactorMode);
                        double endMm = ParseBeamNaviateAdditionalLengthTextMm(endRawText, clearSpanMm, addTopFactorMode);
                        rowStartMm = Math.Max(0.0, startMm);
                        rowEndMm = Math.Max(rowStartMm + 1.0, clearSpanMm - Math.Max(0.0, endMm));
                    }

                    double sx = xBeamStart + Math.Max(0.0, Math.Min(clearSpanMm, rowStartMm)) * mmToPx;
                    double ex = xBeamStart + Math.Max(0.0, Math.Min(clearSpanMm, rowEndMm)) * mmToPx;
                    if ((ex - sx) < 24.0)
                    {
                        double m = (sx + ex) * 0.5;
                        sx = m - 12.0;
                        ex = m + 12.0;
                    }

                    AddBeamPreviewBarLine(
                        canvas,
                        sx,
                        ex,
                        yTopBar,
                        rowIndex == 0 ? WpfColor.FromRgb(239, 83, 80) : WpfColor.FromRgb(56, 56, 56),
                        rowIndex == 0 ? 2.4 : 1.8);
                    rowIndex++;
                }
            }

            if ((tabAddTop || tabAddBottom) && previewSpanStationsMm.Count >= 2)
            {
                if (addTopEnabled)
                {
                    double yTopInput = Math.Max(10.0, beamTop - 40.0);
                    DrawBeamPreviewAdditionalTopInputDimensions(
                        canvas,
                        previewTopRows,
                        previewSpanStationsMm,
                        xBeamStart,
                        mmToPx,
                        yTopInput,
                        addTopFactorMode);
                }
                if (addBottomEnabled)
                {
                    double yBottomInput = Math.Min(drawBottom - 26.0, beamBottom + 44.0);
                    DrawBeamPreviewAdditionalBottomInputDimensions(
                        canvas,
                        previewBottomRows,
                        previewSpanStationsMm,
                        xBeamStart,
                        mmToPx,
                        yBottomInput,
                        addBottomFactorMode);
                }
            }

            bool secondaryEnabled = BeamNaviateSecondaryEnabledCheckBox?.IsChecked == true;
            if (tabSecondary && secondaryEnabled)
            {
                TryParseBeamNaviateMmText(BeamNaviateSecondaryStartLengthTextBox?.Text, out double secondaryStartMm);
                TryParseBeamNaviateMmText(BeamNaviateSecondaryEndLengthTextBox?.Text, out double secondaryEndMm);
                double sx = xBeamStart + Math.Max(0.0, secondaryStartMm) * mmToPx;
                double ex = xBeamEnd - Math.Max(0.0, secondaryEndMm) * mmToPx;
                if ((ex - sx) < 28.0)
                {
                    double m = (sx + ex) * 0.5;
                    sx = m - 14.0;
                    ex = m + 14.0;
                }

                int secondaryCount = ParseBeamNaviateIntOrDefault(BeamNaviateSecondaryCountTextBox?.Text, 2, 1, 40);
                int drawCount = Math.Max(1, Math.Min(3, secondaryCount));
                for (int i = 0; i < drawCount; i++)
                {
                    double y = beamCenterY - ((drawCount - 1) * 3.5) + (i * 7.0);
                    AddBeamPreviewBarLine(
                        canvas,
                        sx,
                        ex,
                        y,
                        i == 0 ? WpfColor.FromRgb(239, 83, 80) : WpfColor.FromRgb(64, 64, 64),
                        i == 0 ? 2.2 : 1.6);
                }
            }

            bool specialEnabled = BeamNaviateSpecialEnabledCheckBox?.IsChecked == true;
            if (tabSpecial && specialEnabled)
            {
                EnsureBeamNaviateSpecialRowsInitialized();
                List<BeamNaviateSpecialRowUi> previewSpecialRows = CloneBeamNaviateSpecialRows(_beamNaviateSpecialRows);
                if (previewSpecialRows.Count == 0)
                {
                    BeamNaviateSpecialRowUi legacy = CreateBeamNaviateSpecialDefaultRow(1);
                    legacy.ModeText = string.IsNullOrWhiteSpace(GetBeamNaviateSelectedComboText(BeamNaviateSpecialModeCombo))
                        ? "Tie Stirrup"
                        : GetBeamNaviateSelectedComboText(BeamNaviateSpecialModeCombo);
                    legacy.SpacingText = string.IsNullOrWhiteSpace((BeamNaviateSpecialSpacingTextBox?.Text ?? "").Trim())
                        ? "150 mm"
                        : BeamNaviateSpecialSpacingTextBox.Text.Trim();
                    string legacyZone = string.IsNullOrWhiteSpace((BeamNaviateSpecialZoneLengthTextBox?.Text ?? "").Trim())
                        ? "800 mm"
                        : BeamNaviateSpecialZoneLengthTextBox.Text.Trim();
                    legacy.StartZoneText = legacyZone;
                    legacy.EndZoneText = legacyZone;
                    previewSpecialRows.Add(legacy);
                }

                int sectionCount = Math.Max(1, previewSpanStationsMm.Count - 1);
                foreach (BeamNaviateSpecialRowUi row in previewSpecialRows.Where(x => x != null).Take(48))
                {
                    int sectionIndex = ParseBeamNaviateIntOrDefault(row.SectionText, 1, 1, 200);
                    sectionIndex = Math.Max(1, Math.Min(sectionCount, sectionIndex));
                    int cageIndex = ParseBeamNaviateIntOrDefault(row.CageText, 1, 1, 200);
                    cageIndex = Math.Max(1, cageIndex);
                    int sectionStartIndex = Math.Max(0, Math.Min(sectionIndex - 1, previewSpanStationsMm.Count - 2));
                    int sectionEndIndex = Math.Max(1, Math.Min(sectionStartIndex + 1, previewSpanStationsMm.Count - 1));
                    double sectionStartMm = previewSpanStationsMm[sectionStartIndex];
                    double sectionEndMm = previewSpanStationsMm[sectionEndIndex];
                    double sectionSpanMm = Math.Max(0.0, sectionEndMm - sectionStartMm);
                    if (sectionSpanMm <= 1.0)
                    {
                        continue;
                    }

                    double spacingMm = ParseBeamNaviateAdditionalLengthTextMm(
                        NormalizeBeamNaviateLengthText(row.SpacingText, "150 mm"),
                        sectionSpanMm,
                        interpretPlainAsFactor: false);
                    spacingMm = Math.Max(60.0, spacingMm);

                    double specialLeftZoneMm = ParseBeamNaviateAdditionalLengthTextMm(
                        NormalizeBeamNaviateLengthText(row.StartZoneText, "800 mm"),
                        sectionSpanMm,
                        interpretPlainAsFactor: true);
                    double specialRightZoneMm = ParseBeamNaviateAdditionalLengthTextMm(
                        NormalizeBeamNaviateLengthText(row.EndZoneText, "800 mm"),
                        sectionSpanMm,
                        interpretPlainAsFactor: true);

                    var rowRangesMm = new List<(double StartMm, double EndMm)>();
                    if (specialLeftZoneMm <= 1.0 && specialRightZoneMm <= 1.0)
                    {
                        rowRangesMm.Add((sectionStartMm, sectionEndMm));
                    }
                    else
                    {
                        double leftEndMm = Math.Min(sectionEndMm, sectionStartMm + Math.Max(0.0, specialLeftZoneMm));
                        if (leftEndMm > sectionStartMm + 1.0)
                        {
                            rowRangesMm.Add((sectionStartMm, leftEndMm));
                        }

                        double rightStartMm = Math.Max(sectionStartMm, sectionEndMm - Math.Max(0.0, specialRightZoneMm));
                        if (sectionEndMm > rightStartMm + 1.0)
                        {
                            rowRangesMm.Add((rightStartMm, sectionEndMm));
                        }

                        if (rowRangesMm.Count == 0)
                        {
                            rowRangesMm.Add((sectionStartMm, sectionEndMm));
                        }
                    }

                    rowRangesMm = rowRangesMm
                        .OrderBy(x => x.StartMm)
                        .ToList();
                    var mergedRanges = new List<(double StartMm, double EndMm)>();
                    foreach ((double StartMm, double EndMm) range in rowRangesMm)
                    {
                        if (mergedRanges.Count == 0)
                        {
                            mergedRanges.Add(range);
                            continue;
                        }

                        (double prevStart, double prevEnd) = mergedRanges[mergedRanges.Count - 1];
                        if (range.StartMm <= prevEnd + 1.0)
                        {
                            mergedRanges[mergedRanges.Count - 1] = (prevStart, Math.Max(prevEnd, range.EndMm));
                        }
                        else
                        {
                            mergedRanges.Add(range);
                        }
                    }

                    string mode = NormalizeBeamNaviateSpecialModeText(row.ModeText);
                    bool rectMode = mode.IndexOf("Rectangular", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool tieMode = mode.IndexOf("Tie", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool uMode = mode.IndexOf("U", StringComparison.OrdinalIgnoreCase) >= 0;
                    int modeLane = GetBeamNaviateSpecialModeLaneIndex(mode);
                    double insetPx = Math.Max(0, cageIndex - 1) * 7.0 + (modeLane * 2.5);
                    double rowTop = beamTop + 5.0 + insetPx;
                    double rowBottom = beamBottom - 5.0 - insetPx;
                    if (rowBottom <= rowTop + 8.0)
                    {
                        continue;
                    }
                    WpfColor zoneColor = rectMode
                        ? WpfColor.FromArgb(84, 224, 107, 73)
                        : (uMode
                            ? WpfColor.FromArgb(84, 72, 143, 219)
                            : WpfColor.FromArgb(84, 255, 82, 23));

                    foreach ((double rangeStartMm, double rangeEndMm) in mergedRanges)
                    {
                        double sx = xBeamStart + (Math.Max(0.0, Math.Min(clearSpanMm, rangeStartMm)) * mmToPx);
                        double ex = xBeamStart + (Math.Max(0.0, Math.Min(clearSpanMm, rangeEndMm)) * mmToPx);
                        if ((ex - sx) < 2.0)
                        {
                            continue;
                        }

                        var zoneFill = new System.Windows.Shapes.Rectangle
                        {
                            Width = Math.Max(1.0, ex - sx),
                            Height = beamHeight - 2.0,
                            Fill = new SolidColorBrush(zoneColor),
                            Stroke = Brushes.Transparent,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(zoneFill, sx);
                        Canvas.SetTop(zoneFill, beamTop + 1.0);
                        canvas.Children.Add(zoneFill);

                        DrawBeamPreviewStirrupRange(
                            canvas,
                            sx,
                            ex,
                            rowTop,
                            rowBottom,
                            Math.Max(0.0, rangeEndMm - rangeStartMm),
                            spacingMm,
                            WpfColor.FromRgb(235, 77, 35));

                        if (rectMode || tieMode)
                        {
                            var rect = new System.Windows.Shapes.Rectangle
                            {
                                Width = Math.Max(4.0, ex - sx - 8.0),
                                Height = Math.Max(8.0, rowBottom - rowTop),
                                Stroke = new SolidColorBrush(WpfColor.FromRgb(239, 83, 80)),
                                StrokeThickness = 1.0,
                                Fill = Brushes.Transparent,
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(rect, sx + 4.0);
                            Canvas.SetTop(rect, rowTop);
                            canvas.Children.Add(rect);
                        }
                        else if (uMode)
                        {
                            double xLeft = sx + 6.0;
                            double xRight = ex - 6.0;
                            double yTop = rowTop + 1.0;
                            double yBottom = rowBottom - 1.0;
                            var leftLeg = new System.Windows.Shapes.Line
                            {
                                X1 = xLeft,
                                X2 = xLeft,
                                Y1 = yTop,
                                Y2 = yBottom,
                                Stroke = new SolidColorBrush(WpfColor.FromRgb(239, 83, 80)),
                                StrokeThickness = 1.4,
                                IsHitTestVisible = false
                            };
                            canvas.Children.Add(leftLeg);

                            var rightLeg = new System.Windows.Shapes.Line
                            {
                                X1 = xRight,
                                X2 = xRight,
                                Y1 = yTop,
                                Y2 = yBottom,
                                Stroke = new SolidColorBrush(WpfColor.FromRgb(239, 83, 80)),
                                StrokeThickness = 1.4,
                                IsHitTestVisible = false
                            };
                            canvas.Children.Add(rightLeg);

                            AddBeamPreviewBarLine(canvas, xLeft, xRight, yBottom, WpfColor.FromRgb(239, 83, 80), 1.4);
                        }
                        else
                        {
                            AddBeamPreviewBarLine(canvas, sx + 6.0, ex - 6.0, rowTop + 1.0, WpfColor.FromRgb(239, 83, 80), 1.4);
                            AddBeamPreviewBarLine(canvas, sx + 6.0, ex - 6.0, rowBottom - 1.0, WpfColor.FromRgb(239, 83, 80), 1.4);
                        }
                    }
                }
            }

            if (!tabAddBottom && !tabAddTop && !tabSecondary)
            {
                AddBeamPreviewBarLine(canvas, xBeamStart + 8.0, xBeamEnd - 8.0, beamCenterY, WpfColor.FromRgb(52, 52, 52), 1.6);
            }

            bool showZoneDimensions =
                (tabSpecial && previewSpanStationsMm.Count <= 2) ||
                (tabStirrup && _beamNaviatePreviewSpans.Count <= 1);
            if (showZoneDimensions)
            {
                AddBeamPreviewDivider(canvas, xZoneLeftBreak, beamTop - 14.0, beamBottom + 4.0);
                AddBeamPreviewDivider(canvas, xZoneRightBreak, beamTop - 14.0, beamBottom + 4.0);
                double topDimY = Math.Max(8.0, beamTop - 10.0);
                AddBeamPreviewDimensionLine(canvas, xBeamStart, xZoneLeftBreak, topDimY, $"{zoneLeftMm:0}");
                AddBeamPreviewDimensionLine(canvas, xZoneLeftBreak, xZoneRightBreak, topDimY, $"{zoneMiddleMm:0}");
                AddBeamPreviewDimensionLine(canvas, xZoneRightBreak, xBeamEnd, topDimY, $"{zoneRightMm:0}");
            }

            double bottomDimY1 = Math.Min(h - 28.0, drawBottom + 8.0);
            double bottomDimY2 = Math.Min(h - 10.0, drawBottom + 26.0);
            if (hasMeasuredHostLength)
            {
                var spanBreaksMm = new List<double> { 0.0 };
                spanBreaksMm.AddRange(detectedSupportStationsMm.Where(s => s > stationTolMm && s < (clearSpanMm - stationTolMm)));
                spanBreaksMm.Add(clearSpanMm);
                for (int i = 0; i < (spanBreaksMm.Count - 1); i++)
                {
                    double s0 = spanBreaksMm[i];
                    double s1 = spanBreaksMm[i + 1];
                    if ((s1 - s0) <= 1.0)
                    {
                        continue;
                    }

                    double x0Seg = xBeamStart + (s0 * mmToPx);
                    double x1Seg = xBeamStart + (s1 * mmToPx);
                    AddBeamPreviewDimensionLine(canvas, x0Seg, x1Seg, bottomDimY1, $"{(s1 - s0):0}");
                }
                AddBeamPreviewDimensionLine(canvas, xBeamStart, xBeamEnd, bottomDimY2, $"{clearSpanMm:0}");
            }
            else
            {
                AddBeamPreviewDimensionLine(canvas, xSupportStart, xBeamStart, bottomDimY1, $"{leftSupportMm:0}");
                AddBeamPreviewDimensionLine(canvas, xBeamStart, xBeamEnd, bottomDimY1, $"{clearSpanMm:0}");
                AddBeamPreviewDimensionLine(canvas, xBeamEnd, xSupportEnd, bottomDimY1, $"{rightSupportMm:0}");
                AddBeamPreviewDimensionLine(canvas, xSupportStart, xSupportEnd, bottomDimY2, $"{totalLengthMm:0}");
            }

            if (BeamNaviatePreviewInfoTextBlock != null)
            {
                string topPrimaryType = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarTypeCombo) ?? "").Trim();
                string topSecondType = (GetBeamNaviateSelectedComboText(BeamNaviateMainTopBarType2Combo) ?? "").Trim();
                string bottomPrimaryType = (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarTypeCombo) ?? "").Trim();
                string bottomSecondType = (GetBeamNaviateSelectedComboText(BeamNaviateMainBottomBarType2Combo) ?? "").Trim();
                string topTypeLabel = BuildBeamPreviewMainLayerTypeLabel(topPrimaryType, topSecondType, topLayers);
                string bottomTypeLabel = BuildBeamPreviewMainLayerTypeLabel(bottomPrimaryType, bottomSecondType, bottomLayers);
                string topCountLabel = topLayers >= 2
                    ? $"L1x{topCount}+L2x{topSecondCount}"
                    : $"L1x{topCount}";
                string bottomCountLabel = bottomLayers >= 2
                    ? $"L1x{bottomCount}+L2x{bottomSecondCount}"
                    : $"L1x{bottomCount}";

                string info = $"Tab: {activeTab} | Hosts: {_beamRebarSelectedHostElementIds.Count} | Top {topCountLabel} [{topTypeLabel}] | Bottom {bottomCountLabel} [{bottomTypeLabel}]";
                if (hasMeasuredHostLength)
                {
                    info += $" | Clear L {clearSpanMm:0}";
                    info += $" | EndConn S{_beamRebarDetectedStartConnectionCount}/E{_beamRebarDetectedEndConnectionCount}";
                }
                if (_beamRebarPrimaryHostWidthMm > 1.0 && _beamRebarPrimaryHostDepthMm > 1.0)
                {
                    info += $" | Sec {_beamRebarPrimaryHostWidthMm:0}x{_beamRebarPrimaryHostDepthMm:0}";
                    info += $" | Beam W {_beamRebarPrimaryHostWidthMm:0}";
                }
                else if (_beamRebarPrimaryHostWidthMm > 1.0)
                {
                    info += $" | Beam W {_beamRebarPrimaryHostWidthMm:0}";
                }
                if (!string.IsNullOrWhiteSpace(_beamRebarDetectedModelSummary))
                {
                    info += $" | {_beamRebarDetectedModelSummary}";
                }
                if (detectedSupports.Count > 0)
                {
                    info += $" | Supports {detectedSupports.Count}";
                    int countColumn = detectedSupports.Count(x =>
                        string.Equals((x.KindKey ?? "").Trim(), "column", StringComparison.OrdinalIgnoreCase));
                    int countWall = detectedSupports.Count(x =>
                        string.Equals((x.KindKey ?? "").Trim(), "wall", StringComparison.OrdinalIgnoreCase));
                    int countFoundation = detectedSupports.Count(x =>
                        string.Equals((x.KindKey ?? "").Trim(), "foundation", StringComparison.OrdinalIgnoreCase));
                    info += $" (C{countColumn}/W{countWall}/F{countFoundation})";
                    string colWidthInfo = BuildBeamPreviewSupportWidthInfo(detectedSupports, "column", "Col W");
                    string wallWidthInfo = BuildBeamPreviewSupportWidthInfo(detectedSupports, "wall", "Wall W");
                    string foundationWidthInfo = BuildBeamPreviewSupportWidthInfo(detectedSupports, "foundation", "Foundation W");
                    if (!string.IsNullOrWhiteSpace(colWidthInfo))
                    {
                        info += $" | {colWidthInfo}";
                    }
                    if (!string.IsNullOrWhiteSpace(wallWidthInfo))
                    {
                        info += $" | {wallWidthInfo}";
                    }
                    if (!string.IsNullOrWhiteSpace(foundationWidthInfo))
                    {
                        info += $" | {foundationWidthInfo}";
                    }
                    info += " | Legend C=Column W=Wall F=Foundation";
                }
                if (sideCount > 0)
                {
                    info += $" | Side {sideCount}/face";
                }
                if (_beamRebarSelectedSecondaryElementIds.Count > 0)
                {
                    info += $" | Secondary refs {_beamRebarSelectedSecondaryElementIds.Count}";
                }
                if (_beamRebarSelectedSupportElementIds.Count > 0)
                {
                    info += $" | Support refs {_beamRebarSelectedSupportElementIds.Count}";
                }
                if (_beamNaviateSelectedPreviewSpanIndex >= 0)
                {
                    info += $" | Stirrup span {_beamNaviateSelectedPreviewSpanIndex + 1}";
                }
                if (Math.Abs(_beamNaviatePreviewZoomFactor - 1.0) > 1e-6)
                {
                    info += $" | Zoom {_beamNaviatePreviewZoomFactor:0.0}x";
                }
                BeamNaviatePreviewInfoTextBlock.Text = info;
            }
        }

        private string GetBeamNaviateActiveTabHeader()
        {
            TabItem selected = BeamNaviateTabControl?.SelectedItem as TabItem;
            string header = (selected?.Header?.ToString() ?? "").Trim();
            return string.IsNullOrWhiteSpace(header) ? "Main Bar" : header;
        }

        private double ResolveBeamPreviewPrimaryHostLengthMm()
        {
            return _beamRebarSelectedHostLengthsMm
                .Where(x => x > 1.0 && !double.IsNaN(x) && !double.IsInfinity(x))
                .DefaultIfEmpty(0.0)
                .Max();
        }

        private List<double> ResolveBeamPreviewDetectedSupportStations(double hostLengthMm)
        {
            return ResolveBeamPreviewDetectedSupports(hostLengthMm)
                .Select(x => x.StationMm)
                .ToList();
        }

        private List<BeamPreviewSupportUi> ResolveBeamPreviewDetectedSupports(double hostLengthMm)
        {
            var result = new List<BeamPreviewSupportUi>();
            if (hostLengthMm <= 1.0)
            {
                return result;
            }

            int stationCount = _beamRebarDetectedSupportStationsMm.Count;
            if (stationCount == 0)
            {
                return result;
            }

            double dedupTolMm = Math.Max(120.0, hostLengthMm * 0.01);
            double maxSupportWidthMm = Math.Max(1200.0, hostLengthMm * 0.25);

            var sorted = Enumerable.Range(0, stationCount)
                .Select(i => new BeamPreviewSupportUi
                {
                    StationMm = _beamRebarDetectedSupportStationsMm[i],
                    WidthMm = i < _beamRebarDetectedSupportWidthsMm.Count ? _beamRebarDetectedSupportWidthsMm[i] : 0.0,
                    KindKey = i < _beamRebarDetectedSupportKinds.Count ? _beamRebarDetectedSupportKinds[i] ?? string.Empty : string.Empty
                })
                .Where(x => x.StationMm >= 0.0 &&
                    x.StationMm <= hostLengthMm &&
                    !double.IsNaN(x.StationMm) &&
                    !double.IsInfinity(x.StationMm))
                .OrderBy(x => x.StationMm)
                .ToList();

            foreach (BeamPreviewSupportUi item in sorted)
            {
                double widthMm = item.WidthMm;
                if (widthMm <= 1.0 || double.IsNaN(widthMm) || double.IsInfinity(widthMm))
                {
                    widthMm = 0.0;
                }
                else
                {
                    widthMm = Math.Max(120.0, Math.Min(maxSupportWidthMm, widthMm));
                }

                if (result.Count == 0 || Math.Abs(item.StationMm - result[result.Count - 1].StationMm) > dedupTolMm)
                {
                    result.Add(new BeamPreviewSupportUi
                    {
                        StationMm = item.StationMm,
                        WidthMm = widthMm,
                        KindKey = item.KindKey
                    });
                    continue;
                }

                BeamPreviewSupportUi merged = result[result.Count - 1];
                merged.StationMm = (merged.StationMm + item.StationMm) * 0.5;
                if (widthMm > merged.WidthMm)
                {
                    merged.WidthMm = widthMm;
                }
                if (string.IsNullOrWhiteSpace(merged.KindKey))
                {
                    merged.KindKey = item.KindKey;
                }
            }

            return result;
        }

        private static string BuildBeamPreviewMainLayerTypeLabel(string primaryType, string secondType, int layerCount)
        {
            string first = string.IsNullOrWhiteSpace(primaryType) ? "-" : primaryType.Trim();
            string second = string.IsNullOrWhiteSpace(secondType) ? first : secondType.Trim();
            if (layerCount <= 1 ||
                string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            {
                return first;
            }

            return first + "/" + second;
        }

        private static string BuildBeamPreviewSupportWidthInfo(
            IEnumerable<BeamPreviewSupportUi> supports,
            string kindKey,
            string label)
        {
            if (supports == null)
            {
                return string.Empty;
            }

            string kind = (kindKey ?? string.Empty).Trim();
            List<double> widths = supports
                .Where(x =>
                    x != null &&
                    x.WidthMm > 1.0 &&
                    !double.IsNaN(x.WidthMm) &&
                    !double.IsInfinity(x.WidthMm) &&
                    string.Equals((x.KindKey ?? string.Empty).Trim(), kind, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.WidthMm)
                .OrderBy(x => x)
                .ToList();
            if (widths.Count == 0)
            {
                return string.Empty;
            }

            double minWidth = widths.First();
            double maxWidth = widths.Last();
            if (Math.Abs(maxWidth - minWidth) <= 1.0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0}", label ?? "W", maxWidth);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0} {1:0}-{2:0}", label ?? "W", minWidth, maxWidth);
        }

        private static List<double> BuildBeamPreviewSpanBoundaryStationsMm(double hostLengthMm, IList<double> supportStationsMm)
        {
            var result = new List<double>();
            if (hostLengthMm <= 1.0 || double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm))
            {
                return result;
            }

            double tolMm = Math.Max(120.0, hostLengthMm * 0.01);
            result.Add(0.0);
            result.AddRange((supportStationsMm ?? Array.Empty<double>())
                .Where(s => s > tolMm && s < (hostLengthMm - tolMm) && !double.IsNaN(s) && !double.IsInfinity(s))
                .OrderBy(s => s));
            result.Add(hostLengthMm);

            var dedup = new List<double>();
            double dedupTolMm = Math.Max(60.0, hostLengthMm * 0.002);
            foreach (double station in result)
            {
                if (dedup.Count == 0 || Math.Abs(station - dedup[dedup.Count - 1]) > dedupTolMm)
                {
                    dedup.Add(station);
                }
            }

            if (dedup.Count < 2)
            {
                dedup.Clear();
                dedup.Add(0.0);
                dedup.Add(hostLengthMm);
            }

            return dedup;
        }

        private static bool TryResolveBeamNaviateAdditionalReferenceLengthsMm(
            BeamNaviateAdditionalRowUi row,
            double xMainStartMm,
            double xMainEndMm,
            int gridMaxIndex,
            IList<double> gridStationsMm,
            out double startRefMm,
            out double endRefMm)
        {
            startRefMm = 0.0;
            endRefMm = 0.0;

            if (row == null || xMainEndMm <= xMainStartMm + 1e-6)
            {
                return false;
            }

            bool useExplicitStations = gridStationsMm != null && gridStationsMm.Count >= 2;
            int safeGridMax = useExplicitStations
                ? Math.Max(1, gridStationsMm.Count - 1)
                : Math.Max(1, gridMaxIndex);

            int startGrid = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
            int endGrid = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);
            startGrid = Math.Max(0, Math.Min(safeGridMax, startGrid));
            endGrid = Math.Max(0, Math.Min(safeGridMax, endGrid));

            double xStartGridMm;
            double xEndGridMm;
            if (useExplicitStations)
            {
                xStartGridMm = gridStationsMm[startGrid];
                xEndGridMm = gridStationsMm[endGrid];
            }
            else
            {
                double beamLengthMm = xMainEndMm - xMainStartMm;
                xStartGridMm = xMainStartMm + (beamLengthMm * ((double)startGrid / safeGridMax));
                xEndGridMm = xMainStartMm + (beamLengthMm * ((double)endGrid / safeGridMax));
            }

            double spanRefMm = Math.Max(0.0, Math.Abs(xEndGridMm - xStartGridMm));
            double leftRefMm = 0.0;
            double rightRefMm = 0.0;
            if (useExplicitStations)
            {
                if (startGrid > 0)
                {
                    leftRefMm = Math.Max(0.0, Math.Abs(xStartGridMm - gridStationsMm[startGrid - 1]));
                }
                if (startGrid < safeGridMax)
                {
                    rightRefMm = Math.Max(0.0, Math.Abs(gridStationsMm[startGrid + 1] - xStartGridMm));
                }
            }

            startRefMm = startGrid == endGrid
                ? (leftRefMm > 1e-6 ? leftRefMm : Math.Max(spanRefMm, rightRefMm))
                : spanRefMm;
            endRefMm = startGrid == endGrid
                ? (rightRefMm > 1e-6 ? rightRefMm : Math.Max(spanRefMm, leftRefMm))
                : spanRefMm;

            if (startRefMm <= 1e-6)
            {
                startRefMm = Math.Max(0.0, xMainEndMm - xMainStartMm);
            }
            if (endRefMm <= 1e-6)
            {
                endRefMm = Math.Max(0.0, xMainEndMm - xMainStartMm);
            }

            return startRefMm > 1e-6 && endRefMm > 1e-6;
        }

        private static bool TryResolveBeamPreviewAdditionalRowRangeMm(
            BeamNaviateAdditionalRowUi row,
            double xMainStartMm,
            double xMainEndMm,
            int gridMaxIndex,
            IList<double> gridStationsMm,
            bool interpretPlainAsFactor,
            out double xRowStartMm,
            out double xRowEndMm)
        {
            xRowStartMm = 0.0;
            xRowEndMm = 0.0;

            if (row == null || xMainEndMm <= xMainStartMm + 1e-6)
            {
                return false;
            }

            bool useExplicitStations = gridStationsMm != null && gridStationsMm.Count >= 2;
            int safeGridMax = useExplicitStations
                ? Math.Max(1, gridStationsMm.Count - 1)
                : Math.Max(1, gridMaxIndex);

            int startGrid = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
            int endGrid = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);
            startGrid = Math.Max(0, Math.Min(safeGridMax, startGrid));
            endGrid = Math.Max(0, Math.Min(safeGridMax, endGrid));

            double xStartGridMm;
            double xEndGridMm;
            if (useExplicitStations)
            {
                xStartGridMm = gridStationsMm[startGrid];
                xEndGridMm = gridStationsMm[endGrid];
            }
            else
            {
                double beamLengthMm = xMainEndMm - xMainStartMm;
                xStartGridMm = xMainStartMm + (beamLengthMm * ((double)startGrid / safeGridMax));
                xEndGridMm = xMainStartMm + (beamLengthMm * ((double)endGrid / safeGridMax));
            }

            double spanRefMm = Math.Max(0.0, Math.Abs(xEndGridMm - xStartGridMm));
            double leftRefMm = 0.0;
            double rightRefMm = 0.0;
            if (useExplicitStations)
            {
                if (startGrid > 0)
                {
                    leftRefMm = Math.Max(0.0, Math.Abs(xStartGridMm - gridStationsMm[startGrid - 1]));
                }
                if (startGrid < safeGridMax)
                {
                    rightRefMm = Math.Max(0.0, Math.Abs(gridStationsMm[startGrid + 1] - xStartGridMm));
                }
            }

            double startRefMm = startGrid == endGrid
                ? (leftRefMm > 1e-6 ? leftRefMm : Math.Max(spanRefMm, rightRefMm))
                : spanRefMm;
            double endRefMm = startGrid == endGrid
                ? (rightRefMm > 1e-6 ? rightRefMm : Math.Max(spanRefMm, leftRefMm))
                : spanRefMm;
            if (startRefMm <= 1e-6)
            {
                startRefMm = Math.Max(0.0, xMainEndMm - xMainStartMm);
            }
            if (endRefMm <= 1e-6)
            {
                endRefMm = Math.Max(0.0, xMainEndMm - xMainStartMm);
            }

            string startRawText = GetBeamNaviateAdditionalStartInputText(row, interpretPlainAsFactor);
            string endRawText = GetBeamNaviateAdditionalEndInputText(row, interpretPlainAsFactor);
            double startLenMm = ParseBeamNaviateAdditionalLengthTextMm(startRawText, startRefMm, interpretPlainAsFactor);
            double endLenMm = ParseBeamNaviateAdditionalLengthTextMm(endRawText, endRefMm, interpretPlainAsFactor);
            startLenMm = Math.Max(0.0, startLenMm);
            endLenMm = Math.Max(0.0, endLenMm);

            if (startGrid == endGrid)
            {
                xRowStartMm = xStartGridMm - startLenMm;
                xRowEndMm = xStartGridMm + endLenMm;
                if (useExplicitStations)
                {
                    if (startGrid == 0 && startLenMm <= 1e-6)
                    {
                        xRowStartMm = xMainStartMm;
                    }
                    if (startGrid == safeGridMax && endLenMm <= 1e-6)
                    {
                        xRowEndMm = xMainEndMm;
                    }
                }
            }
            else if (xStartGridMm <= xEndGridMm)
            {
                xRowStartMm = xStartGridMm + startLenMm;
                xRowEndMm = xEndGridMm - endLenMm;
            }
            else
            {
                xRowStartMm = xEndGridMm + endLenMm;
                xRowEndMm = xStartGridMm - startLenMm;
            }

            xRowStartMm = Math.Max(xMainStartMm, Math.Min(xMainEndMm, xRowStartMm));
            xRowEndMm = Math.Max(xMainStartMm, Math.Min(xMainEndMm, xRowEndMm));

            if (xRowEndMm <= xRowStartMm + 1e-6)
            {
                if (startGrid != endGrid)
                {
                    double xLow = Math.Max(xMainStartMm, Math.Min(xMainEndMm, Math.Min(xStartGridMm, xEndGridMm)));
                    double xHigh = Math.Max(xMainStartMm, Math.Min(xMainEndMm, Math.Max(xStartGridMm, xEndGridMm)));
                    xRowStartMm = xLow;
                    xRowEndMm = xHigh;
                }
                else
                {
                    xRowStartMm = xMainStartMm;
                    xRowEndMm = xMainEndMm;
                }
            }

            return xRowEndMm > xRowStartMm + 1e-6;
        }

        private static double ParseBeamNaviateAdditionalLengthTextMm(
            string rawText,
            double referenceLengthMm,
            bool interpretPlainAsFactor = false)
        {
            if (TryParseBeamNaviateLengthRatioOfL(rawText, out double ratioL))
            {
                return Math.Max(0.0, ratioL) * Math.Max(0.0, referenceLengthMm);
            }

            if (interpretPlainAsFactor && TryParseBeamNaviatePlainFactorText(rawText, out double factor))
            {
                return Math.Max(0.0, factor) * Math.Max(0.0, referenceLengthMm);
            }

            return TryParseBeamNaviateMmText(rawText, out double lengthMm)
                ? Math.Max(0.0, lengthMm)
                : 0.0;
        }

        private static List<(double StartMm, double EndMm)> ExpandBeamPreviewAdditionalBottomRowRangesMm(
            BeamNaviateAdditionalRowUi row,
            double xMainStartMm,
            double xMainEndMm,
            int gridMaxIndex,
            IList<double> gridStationsMm,
            bool interpretPlainAsFactor)
        {
            var ranges = new List<(double StartMm, double EndMm)>();
            if (row == null)
            {
                return ranges;
            }

            bool useExplicitStations = gridStationsMm != null && gridStationsMm.Count >= 2;
            int startGrid = ParseBeamNaviateIntOrDefault(row.StartGridText, 0, 0, 200);
            int endGrid = ParseBeamNaviateIntOrDefault(row.EndGridText, 0, 0, 200);

            if (useExplicitStations && startGrid == endGrid)
            {
                int spanCount = Math.Max(1, gridStationsMm.Count - 1);
                for (int spanIndex = 0; spanIndex < spanCount; spanIndex++)
                {
                    var spanRow = CloneBeamNaviateAdditionalRowUi(row);
                    spanRow.StartGridText = spanIndex.ToString(CultureInfo.InvariantCulture);
                    spanRow.EndGridText = (spanIndex + 1).ToString(CultureInfo.InvariantCulture);
                    if (TryResolveBeamPreviewAdditionalRowRangeMm(
                        spanRow,
                        xMainStartMm,
                        xMainEndMm,
                        gridMaxIndex,
                        gridStationsMm,
                        interpretPlainAsFactor,
                        out double xRowStartMm,
                        out double xRowEndMm))
                    {
                        ranges.Add((xRowStartMm, xRowEndMm));
                    }
                }
            }

            if (ranges.Count == 0)
            {
                if (TryResolveBeamPreviewAdditionalRowRangeMm(
                    row,
                    xMainStartMm,
                    xMainEndMm,
                    gridMaxIndex,
                    gridStationsMm,
                    interpretPlainAsFactor,
                    out double xRowStartMm,
                    out double xRowEndMm))
                {
                    ranges.Add((xRowStartMm, xRowEndMm));
                }
                else
                {
                    string startRawText = GetBeamNaviateAdditionalStartInputText(row, interpretPlainAsFactor);
                    string endRawText = GetBeamNaviateAdditionalEndInputText(row, interpretPlainAsFactor);
                    TryParseBeamNaviateMmText(startRawText, out double startMm);
                    TryParseBeamNaviateMmText(endRawText, out double endMm);
                    double safeStartMm = Math.Max(0.0, startMm);
                    double safeEndMm = Math.Max(safeStartMm + 1.0, xMainEndMm - Math.Max(0.0, endMm));
                    ranges.Add((safeStartMm, safeEndMm));
                }
            }

            return ranges;
        }

        private static void DrawBeamPreviewAdditionalTopInputDimensions(
            Canvas canvas,
            IEnumerable<BeamNaviateAdditionalRowUi> rows,
            IList<double> spanStationsMm,
            double xBeamStart,
            double mmToPx,
            double yDim,
            bool interpretPlainAsFactor)
        {
            List<BeamNaviateAdditionalRowUi> source = (rows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
                .Where(r => r != null)
                .ToList();
            if (canvas == null || source.Count == 0 || spanStationsMm == null || spanStationsMm.Count < 2)
            {
                return;
            }

            for (int spanIndex = 0; spanIndex < spanStationsMm.Count - 1; spanIndex++)
            {
                double s0 = spanStationsMm[spanIndex];
                double s1 = spanStationsMm[spanIndex + 1];
                double spanLengthMm = Math.Max(0.0, s1 - s0);
                if (spanLengthMm <= 1.0)
                {
                    continue;
                }

                BeamNaviateAdditionalRowUi leftNodeRow = FindBeamPreviewAdditionalRowByGrid(source, spanIndex, spanIndex);
                BeamNaviateAdditionalRowUi rightNodeRow = FindBeamPreviewAdditionalRowByGrid(source, spanIndex + 1, spanIndex + 1);
                string leftRawText = GetBeamNaviateAdditionalEndInputText(leftNodeRow, interpretPlainAsFactor);
                string rightRawText = GetBeamNaviateAdditionalStartInputText(rightNodeRow, interpretPlainAsFactor);
                double leftLenMm = leftNodeRow != null
                    ? ParseBeamNaviateAdditionalLengthTextMm(leftRawText, spanLengthMm, interpretPlainAsFactor)
                    : 0.0;
                double rightLenMm = rightNodeRow != null
                    ? ParseBeamNaviateAdditionalLengthTextMm(rightRawText, spanLengthMm, interpretPlainAsFactor)
                    : 0.0;

                DrawBeamPreviewAdditionalInputSpanDimensions(
                    canvas,
                    xBeamStart + (s0 * mmToPx),
                    spanLengthMm,
                    mmToPx,
                    yDim,
                    "Nt",
                    leftLenMm,
                    rightLenMm,
                    leftRawText,
                    rightRawText,
                    interpretPlainAsFactor);
            }
        }

        private static void DrawBeamPreviewAdditionalBottomInputDimensions(
            Canvas canvas,
            IEnumerable<BeamNaviateAdditionalRowUi> rows,
            IList<double> spanStationsMm,
            double xBeamStart,
            double mmToPx,
            double yDim,
            bool interpretPlainAsFactor)
        {
            List<BeamNaviateAdditionalRowUi> source = (rows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
                .Where(r => r != null)
                .ToList();
            if (canvas == null || source.Count == 0 || spanStationsMm == null || spanStationsMm.Count < 2)
            {
                return;
            }

            BeamNaviateAdditionalRowUi genericSingleGridRow = source
                .FirstOrDefault(r =>
                    ParseBeamNaviateIntOrDefault(r.StartGridText, 0, 0, 200) ==
                    ParseBeamNaviateIntOrDefault(r.EndGridText, 0, 0, 200));

            for (int spanIndex = 0; spanIndex < spanStationsMm.Count - 1; spanIndex++)
            {
                double s0 = spanStationsMm[spanIndex];
                double s1 = spanStationsMm[spanIndex + 1];
                double spanLengthMm = Math.Max(0.0, s1 - s0);
                if (spanLengthMm <= 1.0)
                {
                    continue;
                }

                BeamNaviateAdditionalRowUi spanRow = FindBeamPreviewAdditionalRowByGrid(source, spanIndex, spanIndex + 1)
                    ?? genericSingleGridRow;
                if (spanRow == null)
                {
                    continue;
                }

                string leftRawText = GetBeamNaviateAdditionalStartInputText(spanRow, interpretPlainAsFactor);
                string rightRawText = GetBeamNaviateAdditionalEndInputText(spanRow, interpretPlainAsFactor);
                double leftLenMm = ParseBeamNaviateAdditionalLengthTextMm(leftRawText, spanLengthMm, interpretPlainAsFactor);
                double rightLenMm = ParseBeamNaviateAdditionalLengthTextMm(rightRawText, spanLengthMm, interpretPlainAsFactor);

                DrawBeamPreviewAdditionalInputSpanDimensions(
                    canvas,
                    xBeamStart + (s0 * mmToPx),
                    spanLengthMm,
                    mmToPx,
                    yDim,
                    "Nb",
                    leftLenMm,
                    rightLenMm,
                    leftRawText,
                    rightRawText,
                    interpretPlainAsFactor);
            }
        }

        private static BeamNaviateAdditionalRowUi FindBeamPreviewAdditionalRowByGrid(
            IEnumerable<BeamNaviateAdditionalRowUi> rows,
            int startGrid,
            int endGrid)
        {
            return (rows ?? Enumerable.Empty<BeamNaviateAdditionalRowUi>())
                .Where(r => r != null)
                .OrderBy(r => ParseBeamNaviateIntOrDefault(r.LayerText, 2, 1, 12))
                .ThenBy(r => ParseBeamNaviateIntOrDefault(r.StartGridText, 0, 0, 200))
                .FirstOrDefault(r =>
                    ParseBeamNaviateIntOrDefault(r.StartGridText, 0, 0, 200) == startGrid &&
                    ParseBeamNaviateIntOrDefault(r.EndGridText, 0, 0, 200) == endGrid);
        }

        private static void DrawBeamPreviewAdditionalInputSpanDimensions(
            Canvas canvas,
            double xSpanStart,
            double spanLengthMm,
            double mmToPx,
            double yDim,
            string prefix,
            double leftLenMm,
            double rightLenMm,
            string leftRawText,
            string rightRawText,
            bool interpretPlainAsFactor)
        {
            if (canvas == null || spanLengthMm <= 1.0 || mmToPx <= 1e-9)
            {
                return;
            }

            double l1Mm = Math.Max(0.0, leftLenMm);
            double l3Mm = Math.Max(0.0, rightLenMm);
            if (l1Mm + l3Mm > spanLengthMm - 1.0)
            {
                double factor = (spanLengthMm - 1.0) / Math.Max(1.0, l1Mm + l3Mm);
                l1Mm *= factor;
                l3Mm *= factor;
            }
            double l2Mm = Math.Max(0.0, spanLengthMm - l1Mm - l3Mm);

            double x0 = xSpanStart;
            double x1 = xSpanStart + (l1Mm * mmToPx);
            double x2 = xSpanStart + ((l1Mm + l2Mm) * mmToPx);
            double x3 = xSpanStart + (spanLengthMm * mmToPx);

            string label1 = BuildBeamPreviewAdditionalInputLabel(prefix, leftRawText, l1Mm, spanLengthMm, interpretPlainAsFactor);
            string label2 = BuildBeamPreviewAdditionalMiddleInputLabel(prefix, l2Mm, spanLengthMm);
            string label3 = BuildBeamPreviewAdditionalInputLabel(prefix, rightRawText, l3Mm, spanLengthMm, interpretPlainAsFactor);

            if ((x1 - x0) > 28.0)
            {
                AddBeamPreviewDimensionLine(canvas, x0, x1, yDim, label1);
            }
            if ((x2 - x1) > 28.0)
            {
                AddBeamPreviewDimensionLine(canvas, x1, x2, yDim, label2);
            }
            if ((x3 - x2) > 28.0)
            {
                AddBeamPreviewDimensionLine(canvas, x2, x3, yDim, label3);
            }

            AddBeamPreviewDimensionNode(canvas, x0, yDim);
            AddBeamPreviewDimensionNode(canvas, x1, yDim);
            AddBeamPreviewDimensionNode(canvas, x2, yDim);
            AddBeamPreviewDimensionNode(canvas, x3, yDim);
        }

        private static string BuildBeamPreviewAdditionalInputLabel(
            string prefix,
            string rawText,
            double segmentLengthMm,
            double spanLengthMm,
            bool interpretPlainAsFactor)
        {
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "N" : prefix.Trim();
            if (TryParseBeamNaviateLengthRatioOfL(rawText, out double ratioL))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}*L ({1:0.###}*L)", safePrefix, Math.Max(0.0, ratioL));
            }
            if (interpretPlainAsFactor && TryParseBeamNaviatePlainFactorText(rawText, out double plainFactor))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}*L ({1:0.###}*L)", safePrefix, Math.Max(0.0, plainFactor));
            }
            if (TryParseBeamNaviateMmText(rawText, out double mmValue))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.#} mm)", safePrefix, Math.Max(0.0, mmValue));
            }

            double ratio = spanLengthMm > 1e-6 ? Math.Max(0.0, segmentLengthMm) / spanLengthMm : 0.0;
            return string.Format(CultureInfo.InvariantCulture, "{0}*L ({1:0.###}*L)", safePrefix, ratio);
        }

        private static string BuildBeamPreviewAdditionalMiddleInputLabel(
            string prefix,
            double middleLengthMm,
            double spanLengthMm)
        {
            string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "N" : prefix.Trim();
            if (spanLengthMm <= 1e-6)
            {
                return safePrefix;
            }

            double ratio = Math.Max(0.0, middleLengthMm) / spanLengthMm;
            return string.Format(CultureInfo.InvariantCulture, "{0}*L ({1:0.###}*L)", safePrefix, ratio);
        }

        private static void AddBeamPreviewDimensionNode(Canvas canvas, double x, double y)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7.0,
                Height = 7.0,
                Fill = new SolidColorBrush(WpfColor.FromRgb(20, 20, 20)),
                Stroke = Brushes.Black,
                StrokeThickness = 0.6,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x - 3.5);
            Canvas.SetTop(dot, y - 3.5);
            canvas.Children.Add(dot);
        }

        private static WpfColor ResolveBeamPreviewSupportColor(string kindKey)
        {
            string kind = (kindKey ?? string.Empty).Trim();
            if (kind.Equals("column", StringComparison.OrdinalIgnoreCase))
            {
                return WpfColor.FromRgb(188, 188, 188);
            }
            if (kind.Equals("wall", StringComparison.OrdinalIgnoreCase))
            {
                return WpfColor.FromRgb(198, 198, 198);
            }
            if (kind.Equals("foundation", StringComparison.OrdinalIgnoreCase))
            {
                return WpfColor.FromRgb(188, 192, 200);
            }
            if (kind.Equals("framing", StringComparison.OrdinalIgnoreCase))
            {
                return WpfColor.FromRgb(202, 202, 210);
            }

            return WpfColor.FromRgb(210, 210, 210);
        }

        private static string ResolveBeamPreviewSupportLabel(string kindKey)
        {
            string kind = (kindKey ?? string.Empty).Trim();
            if (kind.Equals("column", StringComparison.OrdinalIgnoreCase))
            {
                return "C";
            }
            if (kind.Equals("wall", StringComparison.OrdinalIgnoreCase))
            {
                return "W";
            }
            if (kind.Equals("foundation", StringComparison.OrdinalIgnoreCase))
            {
                return "F";
            }

            return string.Empty;
        }

        private static string ResolveBeamPreviewSupportLabel(string kindKey, double widthMm)
        {
            string baseLabel = ResolveBeamPreviewSupportLabel(kindKey);
            if (string.IsNullOrWhiteSpace(baseLabel))
            {
                return string.Empty;
            }

            if (widthMm > 1.0 && !double.IsNaN(widthMm) && !double.IsInfinity(widthMm))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} {1:0}", baseLabel, widthMm);
            }

            return baseLabel;
        }

        private static void DrawBeamPreviewPrism(
            Canvas canvas,
            double x,
            double y,
            double width,
            double height,
            double depthX,
            double depthY,
            WpfColor frontColor,
            WpfColor topColor,
            WpfColor sideColor,
            WpfColor edgeColor,
            double strokeThickness)
        {
            if (canvas == null || width <= 1e-6 || height <= 1e-6)
            {
                return;
            }

            double w = Math.Max(1.0, width);
            double h = Math.Max(1.0, height);
            double dx = Math.Max(2.0, depthX);
            double dy = Math.Max(2.0, depthY);

            var topFace = new System.Windows.Shapes.Polygon
            {
                Fill = new SolidColorBrush(topColor),
                Stroke = new SolidColorBrush(edgeColor),
                StrokeThickness = Math.Max(0.6, strokeThickness),
                IsHitTestVisible = false,
                Points = new PointCollection
                {
                    new System.Windows.Point(x, y),
                    new System.Windows.Point(x + dx, y - dy),
                    new System.Windows.Point(x + w + dx, y - dy),
                    new System.Windows.Point(x + w, y)
                }
            };
            canvas.Children.Add(topFace);

            var sideFace = new System.Windows.Shapes.Polygon
            {
                Fill = new SolidColorBrush(sideColor),
                Stroke = new SolidColorBrush(edgeColor),
                StrokeThickness = Math.Max(0.6, strokeThickness),
                IsHitTestVisible = false,
                Points = new PointCollection
                {
                    new System.Windows.Point(x + w, y),
                    new System.Windows.Point(x + w + dx, y - dy),
                    new System.Windows.Point(x + w + dx, y + h - dy),
                    new System.Windows.Point(x + w, y + h)
                }
            };
            canvas.Children.Add(sideFace);

            var frontFace = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = h,
                Fill = new SolidColorBrush(frontColor),
                Stroke = new SolidColorBrush(edgeColor),
                StrokeThickness = Math.Max(0.6, strokeThickness),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frontFace, x);
            Canvas.SetTop(frontFace, y);
            canvas.Children.Add(frontFace);
        }

        private static void AddBeamPreviewAxisMarker(Canvas canvas, double x, double yTop, double yBottom, string label)
        {
            var axis = new System.Windows.Shapes.Line
            {
                X1 = x,
                X2 = x,
                Y1 = yTop,
                Y2 = yBottom,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(122, 122, 122)),
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            canvas.Children.Add(axis);

            var marker = new System.Windows.Shapes.Ellipse
            {
                Width = 20.0,
                Height = 20.0,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(130, 130, 130)),
                StrokeThickness = 0.9,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(marker, x - 10.0);
            Canvas.SetTop(marker, yTop - 10.0);
            canvas.Children.Add(marker);

            var text = new TextBlock
            {
                Text = label ?? "",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(88, 88, 88)),
                FontSize = 10.0,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size size = text.DesiredSize;
            Canvas.SetLeft(text, x - (size.Width * 0.5));
            Canvas.SetTop(text, yTop - (size.Height * 0.5));
            canvas.Children.Add(text);
        }

        private static void AddBeamPreviewBarLine(Canvas canvas, double x0, double x1, double y, WpfColor color, double thickness)
        {
            var bar = new System.Windows.Shapes.Line
            {
                X1 = x0,
                X2 = x1,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = Math.Max(0.8, thickness),
                IsHitTestVisible = false
            };
            canvas.Children.Add(bar);
        }

        private static void AddBeamPreviewNodeDot(Canvas canvas, double x, double y)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 6.0,
                Height = 6.0,
                Fill = new SolidColorBrush(WpfColor.FromRgb(54, 109, 191)),
                Stroke = new SolidColorBrush(WpfColor.FromRgb(33, 76, 140)),
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, x - 3.0);
            Canvas.SetTop(dot, y - 3.0);
            canvas.Children.Add(dot);
        }

        private static void DrawBeamPreviewStirrupRange(
            Canvas canvas,
            double x0,
            double x1,
            double beamTop,
            double beamBottom,
            double spanMm,
            double spacingMm,
            WpfColor color)
        {
            if ((x1 - x0) < 6.0)
            {
                return;
            }

            spanMm = Math.Max(0.0, spanMm);
            spacingMm = Math.Max(1.0, spacingMm);
            int count = Math.Max(2, Math.Min(80, (int)Math.Round(spanMm / spacingMm) + 1));
            double y0 = beamTop + 2.0;
            double y1 = beamBottom - 2.0;
            for (int i = 0; i < count; i++)
            {
                double t = count <= 1 ? 0.0 : (double)i / (count - 1);
                double x = x0 + ((x1 - x0) * t);
                var stirrup = new System.Windows.Shapes.Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = y0,
                    Y2 = y1,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.0,
                    IsHitTestVisible = false
                };
                canvas.Children.Add(stirrup);
            }
        }

        private void ResolveBeamPreviewStirrupSpanInputsMm(
            int spanIndex,
            double spanLengthMm,
            out double startSpacingMm,
            out double middleSpacingMm,
            out double endSpacingMm,
            out double startZoneMm,
            out double endZoneMm)
        {
            startSpacingMm = 150.0;
            middleSpacingMm = 200.0;
            endSpacingMm = 150.0;
            startZoneMm = 0.0;
            endZoneMm = 0.0;
            ComboBox zoneModeCombo = GetBeamNaviateStirrupZoneInputModeCombo();
            bool followAdditionalTop = IsBeamNaviateStirrupZoneFollowAdditionalTopMode(zoneModeCombo);
            bool factorMode = IsBeamNaviateStirrupZoneFactorMode(zoneModeCombo);

            if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS1Text, "150 mm"), out double g1) && g1 > 0.0)
            {
                startSpacingMm = g1;
            }
            if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS2Text, "200 mm"), out double g2) && g2 > 0.0)
            {
                middleSpacingMm = g2;
            }
            if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(_beamNaviateStirrupGlobalS3Text, "150 mm"), out double g3) && g3 > 0.0)
            {
                endSpacingMm = g3;
            }

            if (spanIndex >= 0 &&
                _beamNaviateStirrupSpanOverrides.TryGetValue(spanIndex, out BeamNaviateStirrupSpanOverrideUi spanOverride) &&
                spanOverride != null)
            {
                if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(spanOverride.StartSpacingText, "150 mm"), out double s1) && s1 > 0.0)
                {
                    startSpacingMm = s1;
                }
                if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(spanOverride.MiddleSpacingText, "200 mm"), out double s2) && s2 > 0.0)
                {
                    middleSpacingMm = s2;
                }
                if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(spanOverride.EndSpacingText, "150 mm"), out double s3) && s3 > 0.0)
                {
                    endSpacingMm = s3;
                }
                if (!followAdditionalTop && factorMode)
                {
                    string startZoneRaw = NormalizeBeamNaviateLengthText(spanOverride.StartZoneText, "0 mm");
                    string endZoneRaw = NormalizeBeamNaviateLengthText(spanOverride.EndZoneText, "0 mm");
                    startZoneMm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(startZoneRaw, Math.Max(1.0, spanLengthMm), interpretPlainAsFactor: true));
                    endZoneMm = Math.Max(0.0, ParseBeamNaviateAdditionalLengthTextMm(endZoneRaw, Math.Max(1.0, spanLengthMm), interpretPlainAsFactor: true));
                }
                else if (!followAdditionalTop)
                {
                    if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(spanOverride.StartZoneText, "0 mm"), out double l1) && l1 >= 0.0)
                    {
                        startZoneMm = l1;
                    }
                    if (TryParseBeamNaviateMmText(NormalizeBeamNaviateLengthText(spanOverride.EndZoneText, "0 mm"), out double l3) && l3 >= 0.0)
                    {
                        endZoneMm = l3;
                    }
                }
            }

            startSpacingMm = Math.Max(60.0, startSpacingMm);
            middleSpacingMm = Math.Max(60.0, middleSpacingMm);
            endSpacingMm = Math.Max(60.0, endSpacingMm);

            if (followAdditionalTop &&
                BeamNaviateAddTopEnabledCheckBox?.IsChecked == true &&
                TryResolveBeamNaviateAdditionalTopZoneLengthsMm(spanIndex, spanLengthMm, out double topL1Mm, out double topL3Mm))
            {
                if (topL1Mm > 0.0)
                {
                    startZoneMm = topL1Mm;
                }
                if (topL3Mm > 0.0)
                {
                    endZoneMm = topL3Mm;
                }
            }

            startZoneMm = Math.Max(0.0, startZoneMm);
            endZoneMm = Math.Max(0.0, endZoneMm);
        }

        private static void DrawBeamPreviewStirrupSpanLayout(
            Canvas canvas,
            double x0,
            double x1,
            double beamTop,
            double beamBottom,
            double spanLengthMm,
            string layoutType,
            double startSpacingMm,
            double middleSpacingMm,
            double endSpacingMm,
            double startZoneMm,
            double endZoneMm)
        {
            if (canvas == null)
            {
                return;
            }

            if (x1 < x0)
            {
                (x0, x1) = (x1, x0);
            }

            if ((x1 - x0) < 6.0 || spanLengthMm <= 1.0)
            {
                return;
            }

            bool useUniform = (layoutType ?? string.Empty).IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0;
            if (useUniform)
            {
                DrawBeamPreviewStirrupRange(
                    canvas,
                    x0,
                    x1,
                    beamTop,
                    beamBottom,
                    spanLengthMm,
                    Math.Max(60.0, middleSpacingMm),
                    WpfColor.FromRgb(225, 105, 70));
                return;
            }

            double l1Mm = startZoneMm > 1.0 ? startZoneMm : spanLengthMm * 0.30;
            double l3Mm = endZoneMm > 1.0 ? endZoneMm : spanLengthMm * 0.30;
            l1Mm = Math.Max(120.0, Math.Min(l1Mm, spanLengthMm * 0.45));
            l3Mm = Math.Max(120.0, Math.Min(l3Mm, spanLengthMm * 0.45));
            if (l1Mm + l3Mm >= spanLengthMm - 120.0)
            {
                double factor = (spanLengthMm - 120.0) / Math.Max(1.0, l1Mm + l3Mm);
                l1Mm *= factor;
                l3Mm *= factor;
            }

            double l2Mm = Math.Max(120.0, spanLengthMm - l1Mm - l3Mm);
            double t1 = l1Mm / Math.Max(1.0, spanLengthMm);
            double t2 = l2Mm / Math.Max(1.0, spanLengthMm);
            double xA0 = x0;
            double xA1 = x0 + ((x1 - x0) * t1);
            double xB0 = xA1;
            double xB1 = xB0 + ((x1 - x0) * t2);
            double xC0 = xB1;
            double xC1 = x1;

            DrawBeamPreviewStirrupRange(canvas, xA0, xA1, beamTop, beamBottom, l1Mm, startSpacingMm, WpfColor.FromRgb(235, 77, 35));
            DrawBeamPreviewStirrupRange(canvas, xB0, xB1, beamTop, beamBottom, l2Mm, middleSpacingMm, WpfColor.FromRgb(225, 105, 70));
            DrawBeamPreviewStirrupRange(canvas, xC0, xC1, beamTop, beamBottom, l3Mm, endSpacingMm, WpfColor.FromRgb(235, 77, 35));
        }

        private static List<(double StartMm, double EndMm)> BuildBeamPreviewSpecialZoneRangesMm(
            IList<double> spanStationsMm,
            double clearSpanMm,
            double explicitZoneMm)
        {
            double totalMm = Math.Max(1.0, clearSpanMm);
            var boundaries = new List<double>();
            if (spanStationsMm != null)
            {
                foreach (double stationMm in spanStationsMm)
                {
                    if (double.IsNaN(stationMm) || double.IsInfinity(stationMm))
                    {
                        continue;
                    }

                    boundaries.Add(Math.Max(0.0, Math.Min(totalMm, stationMm)));
                }
            }

            if (boundaries.Count < 2)
            {
                boundaries.Clear();
                boundaries.Add(0.0);
                boundaries.Add(totalMm);
            }
            else
            {
                boundaries = boundaries
                    .OrderBy(x => x)
                    .ToList();
                var mergedBoundaries = new List<double>();
                foreach (double stationMm in boundaries)
                {
                    if (mergedBoundaries.Count == 0 || Math.Abs(stationMm - mergedBoundaries[mergedBoundaries.Count - 1]) > 1.0)
                    {
                        mergedBoundaries.Add(stationMm);
                    }
                }
                boundaries = mergedBoundaries;
                if (boundaries.Count < 2)
                {
                    boundaries.Clear();
                    boundaries.Add(0.0);
                    boundaries.Add(totalMm);
                }
            }

            var rawRanges = new List<(double StartMm, double EndMm)>();
            for (int i = 0; i < boundaries.Count - 1; i++)
            {
                double s0 = boundaries[i];
                double s1 = boundaries[i + 1];
                double spanMm = s1 - s0;
                if (spanMm <= 1.0)
                {
                    continue;
                }

                double zoneMm = explicitZoneMm > 1.0 ? explicitZoneMm : Math.Max(500.0, spanMm * 0.15);
                zoneMm = Math.Min(zoneMm, Math.Max(150.0, spanMm * 0.45));
                if (zoneMm <= 1.0)
                {
                    continue;
                }

                double leftEndMm = Math.Min(s1, s0 + zoneMm);
                if (leftEndMm > s0 + 1.0)
                {
                    rawRanges.Add((s0, leftEndMm));
                }

                double rightStartMm = Math.Max(s0, s1 - zoneMm);
                if (s1 > rightStartMm + 1.0)
                {
                    rawRanges.Add((rightStartMm, s1));
                }
            }

            if (rawRanges.Count == 0)
            {
                rawRanges.Add((0.0, totalMm));
            }

            rawRanges = rawRanges
                .OrderBy(x => x.StartMm)
                .ToList();
            var mergedRanges = new List<(double StartMm, double EndMm)>();
            foreach ((double StartMm, double EndMm) range in rawRanges)
            {
                if (mergedRanges.Count == 0)
                {
                    mergedRanges.Add(range);
                    continue;
                }

                var (prevStartMm, prevEndMm) = mergedRanges[mergedRanges.Count - 1];
                if (range.StartMm <= prevEndMm + 1.0)
                {
                    mergedRanges[mergedRanges.Count - 1] = (prevStartMm, Math.Max(prevEndMm, range.EndMm));
                }
                else
                {
                    mergedRanges.Add(range);
                }
            }

            return mergedRanges;
        }

        private static void AddBeamPreviewDivider(Canvas canvas, double x, double y0, double y1)
        {
            var line = new System.Windows.Shapes.Line
            {
                X1 = x,
                X2 = x,
                Y1 = y0,
                Y2 = y1,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(120, 138, 168)),
                StrokeThickness = 0.8,
                StrokeDashArray = new DoubleCollection { 3.0, 3.0 },
                IsHitTestVisible = false
            };
            canvas.Children.Add(line);
        }

        private static void AddBeamPreviewDimensionLine(Canvas canvas, double x0, double x1, double y, string text)
        {
            if (x1 < x0)
            {
                (x0, x1) = (x1, x0);
            }

            var line = new System.Windows.Shapes.Line
            {
                X1 = x0,
                X2 = x1,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(98, 104, 118)),
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            canvas.Children.Add(line);

            var tickA = new System.Windows.Shapes.Line
            {
                X1 = x0,
                X2 = x0,
                Y1 = y - 5.0,
                Y2 = y + 5.0,
                Stroke = line.Stroke,
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            canvas.Children.Add(tickA);

            var tickB = new System.Windows.Shapes.Line
            {
                X1 = x1,
                X2 = x1,
                Y1 = y - 5.0,
                Y2 = y + 5.0,
                Stroke = line.Stroke,
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            canvas.Children.Add(tickB);

            var label = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(84, 91, 105)),
                FontSize = 11.0,
                IsHitTestVisible = false
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size sz = label.DesiredSize;
            Canvas.SetLeft(label, ((x0 + x1) * 0.5) - (sz.Width * 0.5));
            Canvas.SetTop(label, y - sz.Height - 3.0);
            canvas.Children.Add(label);
        }

        private void ResolveBeamPreviewZoneLengthsMm(out double leftMm, out double middleMm, out double rightMm, out double totalMm)
        {
            double hostLengthMm = _beamRebarSelectedHostLengthsMm.FirstOrDefault();
            if (double.IsNaN(hostLengthMm) || double.IsInfinity(hostLengthMm) || hostLengthMm <= 1.0)
            {
                hostLengthMm = 1000.0;
            }

            bool addBottomFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddBottomInputModeCombo());
            bool addTopFactorMode = IsBeamNaviateAdditionalInputFactorMode(GetBeamNaviateAddTopInputModeCombo());
            double addBottomStart = 0.0;
            double addBottomEnd = 0.0;
            if (_beamNaviateAddBottomEnabledState || BeamNaviateAddBottomEnabledCheckBox?.IsChecked == true)
            {
                foreach (BeamNaviateAdditionalRowUi row in _beamNaviateAdditionalBottomRows.Where(r => r != null))
                {
                    string startRawText = GetBeamNaviateAdditionalStartInputText(row, addBottomFactorMode);
                    string endRawText = GetBeamNaviateAdditionalEndInputText(row, addBottomFactorMode);
                    double startMm = ParseBeamNaviateAdditionalLengthTextMm(startRawText, hostLengthMm, addBottomFactorMode);
                    double endMm = ParseBeamNaviateAdditionalLengthTextMm(endRawText, hostLengthMm, addBottomFactorMode);
                    addBottomStart = Math.Max(addBottomStart, Math.Max(0.0, startMm));
                    addBottomEnd = Math.Max(addBottomEnd, Math.Max(0.0, endMm));
                }
            }

            double addTopStart = 0.0;
            double addTopEnd = 0.0;
            if (_beamNaviateAddTopEnabledState || BeamNaviateAddTopEnabledCheckBox?.IsChecked == true)
            {
                foreach (BeamNaviateAdditionalRowUi row in _beamNaviateAdditionalTopRows.Where(r => r != null))
                {
                    string startRawText = GetBeamNaviateAdditionalStartInputText(row, addTopFactorMode);
                    string endRawText = GetBeamNaviateAdditionalEndInputText(row, addTopFactorMode);
                    double startMm = ParseBeamNaviateAdditionalLengthTextMm(startRawText, hostLengthMm, addTopFactorMode);
                    double endMm = ParseBeamNaviateAdditionalLengthTextMm(endRawText, hostLengthMm, addTopFactorMode);
                    addTopStart = Math.Max(addTopStart, Math.Max(0.0, startMm));
                    addTopEnd = Math.Max(addTopEnd, Math.Max(0.0, endMm));
                }
            }

            TryParseBeamNaviateMmText(BeamNaviateSecondaryStartLengthTextBox?.Text, out double secondaryStart);
            TryParseBeamNaviateMmText(BeamNaviateSecondaryEndLengthTextBox?.Text, out double secondaryEnd);
            TryParseBeamNaviateMmText(BeamNaviateSpecialZoneLengthTextBox?.Text, out double specialZone);

            leftMm = Math.Max(400.0, Math.Max(addBottomStart, Math.Max(addTopStart, Math.Max(secondaryStart, specialZone))));
            rightMm = Math.Max(400.0, Math.Max(addBottomEnd, Math.Max(addTopEnd, Math.Max(secondaryEnd, specialZone))));

            if (BeamNaviateStirrupLayoutCombo != null &&
                (GetBeamNaviateSelectedComboText(BeamNaviateStirrupLayoutCombo) ?? "").IndexOf("uniform", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                middleMm = Math.Max(1200.0, Math.Max(leftMm, rightMm));
            }
            else
            {
                middleMm = Math.Max(1600.0, (leftMm + rightMm) * 1.2);
            }

            totalMm = leftMm + middleMm + rightMm;
        }

        private static void DrawBeamPreviewBars(
            Canvas canvas,
            double x0,
            double y0,
            double width,
            double height,
            int topLayers,
            int topCount,
            int topSecondCount,
            int bottomLayers,
            int bottomCount,
            int bottomSecondCount,
            int sideCount)
        {
            topLayers = Math.Max(1, Math.Min(12, topLayers));
            topCount = Math.Max(1, Math.Min(40, topCount));
            topSecondCount = Math.Max(1, Math.Min(40, topSecondCount));
            bottomLayers = Math.Max(1, Math.Min(12, bottomLayers));
            bottomCount = Math.Max(1, Math.Min(40, bottomCount));
            bottomSecondCount = Math.Max(1, Math.Min(40, bottomSecondCount));
            sideCount = Math.Max(0, Math.Min(20, sideCount));

            double r = Math.Max(1.8, Math.Min(width, height) * 0.012);
            double left = x0 + 14.0;
            double right = x0 + width - 14.0;
            double top = y0 + 14.0;
            double bottom = y0 + height - 14.0;

            List<double> topYs = BuildBeamPreviewPositions(top, top + Math.Max(8.0, height * 0.18), topLayers);
            List<double> bottomYs = BuildBeamPreviewPositions(bottom - Math.Max(8.0, height * 0.18), bottom, bottomLayers);

            for (int layerIndex = 0; layerIndex < topYs.Count; layerIndex++)
            {
                double y = topYs[layerIndex];
                int count = layerIndex == 0 ? topCount : topSecondCount;
                foreach (double x in BuildBeamPreviewPositions(left, right, count))
                {
                    AddBeamPreviewCircle(canvas, x, y, r, WpfColor.FromRgb(198, 51, 49), WpfColor.FromRgb(237, 108, 100));
                }
            }
            for (int layerIndex = 0; layerIndex < bottomYs.Count; layerIndex++)
            {
                double y = bottomYs[layerIndex];
                int count = layerIndex == 0 ? bottomCount : bottomSecondCount;
                foreach (double x in BuildBeamPreviewPositions(left, right, count))
                {
                    AddBeamPreviewCircle(canvas, x, y, r, WpfColor.FromRgb(198, 51, 49), WpfColor.FromRgb(237, 108, 100));
                }
            }

            if (sideCount > 0)
            {
                List<double> sideYs = BuildBeamPreviewPositions(top + Math.Max(6.0, height * 0.12), bottom - Math.Max(6.0, height * 0.12), sideCount);
                foreach (double y in sideYs)
                {
                    AddBeamPreviewCircle(canvas, left, y, r, WpfColor.FromRgb(60, 118, 194), WpfColor.FromRgb(124, 172, 232));
                    AddBeamPreviewCircle(canvas, right, y, r, WpfColor.FromRgb(60, 118, 194), WpfColor.FromRgb(124, 172, 232));
                }
            }
        }

        private static List<double> BuildBeamPreviewPositions(double start, double end, int count)
        {
            var result = new List<double>();
            count = Math.Max(1, count);
            if (count == 1)
            {
                result.Add((start + end) * 0.5);
                return result;
            }

            double s = Math.Min(start, end);
            double e = Math.Max(start, end);
            if (e <= s + 1e-9)
            {
                result.Add(s);
                return result;
            }

            double step = (e - s) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                result.Add(s + (step * i));
            }

            return result;
        }

        private static void AddBeamPreviewCircle(Canvas canvas, double cx, double cy, double r, WpfColor strokeColor, WpfColor fillColor)
        {
            var circle = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2.0,
                Height = r * 2.0,
                Stroke = new SolidColorBrush(strokeColor),
                Fill = new SolidColorBrush(fillColor),
                StrokeThickness = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(circle, cx - r);
            Canvas.SetTop(circle, cy - r);
            canvas.Children.Add(circle);
        }
    }
}
