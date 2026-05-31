using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.ComponentModel;
using System.Globalization;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Media3D = System.Windows.Media.Media3D;
using WpfColor = System.Windows.Media.Color;
using Microsoft.Win32;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;

namespace CamboBIM.Revit2024.Addin
{
    public partial class CamboBIMWindow
    {

        private void OnPickColumnRebarHostClick(object sender, RoutedEventArgs e)
        {
            _handler.Request.RequestType = CadToModelRequestType.PickColumnRebarHost;
            QueueExternalRequest();
        }

        private void OnUpdateColumnRebarPreviewClick(object sender, RoutedEventArgs e)
        {
            RefreshColumnRebarPreviewFromUi();
        }

        private void OnGenerateColumnRebarClick(object sender, RoutedEventArgs e)
        {
            TryRaiseColumnRebarGenerateRequest();
        }

        private void OnColumnRebarPreviewInputChanged(object sender, RoutedEventArgs e)
        {
            ApplyColumnRebarTopLapModeUi();
            SyncColumnRebarTieShapeBrowserListsFromCombos();
            RefreshColumnRebarTieShapeBrowserModeState();
            RefreshColumnRebarTieShapeBrowserPreviews();
            ScheduleColumnRebarPreviewRefresh(refresh3D: true, refreshSection: true);
        }

        private void OnColumnRebarSectionCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarElevationCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!IsLoaded)
            {
                return;
            }

            if (!e.WidthChanged && !e.HeightChanged)
            {
                return;
            }

            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: false);
        }

        private void OnColumnRebarElevationCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            _columnRebarElevationZoomFactor = Math.Max(0.2, Math.Min(8.0, _columnRebarElevationZoomFactor * factor));
            e.Handled = true;
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: false);
        }

        private void OnColumnRebarElevationCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            _columnRebarElevationIsPanning = true;
            _columnRebarElevationLastMousePoint = e.GetPosition(element);
            try
            {
                element.CaptureMouse();
            }
            catch
            {
            }

            e.Handled = true;
        }

        private void OnColumnRebarElevationCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(element);
            if (_columnRebarElevationIsPanning)
            {
                Vector delta = p - _columnRebarElevationLastMousePoint;
                _columnRebarElevationPanXPx += delta.X;
                _columnRebarElevationPanYPx += delta.Y;
                _columnRebarElevationLastMousePoint = p;
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: false);
                return;
            }

            int newHoverIndex = -1;
            var state = _columnRebarElevationRenderState;
            if (state != null &&
                state.TieLevelYPx.Count > 0 &&
                p.X >= state.TieLeftPx - 10.0 &&
                p.X <= state.TieRightPx + 10.0)
            {
                double bestDist = 8.0;
                for (int i = 0; i < state.TieLevelYPx.Count; i++)
                {
                    double d = Math.Abs(p.Y - state.TieLevelYPx[i]);
                    if (d <= bestDist)
                    {
                        bestDist = d;
                        newHoverIndex = i;
                    }
                }
            }

            if (newHoverIndex != _columnRebarElevationHoveredTieLevelIndex)
            {
                _columnRebarElevationHoveredTieLevelIndex = newHoverIndex;
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: false);
            }
        }

        private void OnColumnRebarElevationCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _columnRebarElevationIsPanning = false;
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

        private void OnColumnRebarElevationCanvasMouseLeave(object sender, MouseEventArgs e)
        {
            if (_columnRebarElevationHoveredTieLevelIndex >= 0)
            {
                _columnRebarElevationHoveredTieLevelIndex = -1;
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: false);
            }
        }

        private void OnColumnRebarTieShapePreviewCanvasLoaded(object sender, RoutedEventArgs e)
        {
            SyncColumnRebarTieShapeBrowserListsFromCombos();
            RefreshColumnRebarTieShapeBrowserPreviews();
        }

        private void OnColumnRebarTieShapePreviewCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!e.WidthChanged && !e.HeightChanged)
            {
                return;
            }

            SyncColumnRebarTieShapeBrowserListsFromCombos();
            RefreshColumnRebarTieShapeBrowserPreviews();
        }

        private void OnColumnRebarSectionCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            _columnRebarSectionZoomFactor = Math.Max(0.2, Math.Min(8.0, _columnRebarSectionZoomFactor * factor));
            e.Handled = true;
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarSectionCanvasMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            _columnRebarSectionIsPanning = true;
            _columnRebarSectionLastMousePoint = e.GetPosition(element);
            try
            {
                element.CaptureMouse();
            }
            catch
            {
            }
            e.Handled = true;
        }

        private void OnColumnRebarSectionCanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(element);
            _columnRebarSectionLastMouseCanvasPoint = p;

            if (_columnRebarSectionIsPanning)
            {
                Vector delta = p - _columnRebarSectionLastMousePoint;
                _columnRebarSectionPanXPx += delta.X;
                _columnRebarSectionPanYPx += delta.Y;
                _columnRebarSectionLastMousePoint = p;
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
                return;
            }

            if (_columnRebarSectionPendingRectStartGridNode.HasValue &&
                (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie ||
                 _columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie))
            {
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
            }
        }

        private void OnColumnRebarSectionCanvasMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _columnRebarSectionIsPanning = false;
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

        private void OnColumnRebarSectionCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _columnRebarSectionLastMouseCanvasPoint = e.GetPosition(sender as IInputElement);

            // Keep existing click-select tie behavior on child line elements.
            if (!(sender is UIElement element))
            {
                return;
            }

            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select)
            {
                return;
            }

            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete)
            {
                _columnRebarSectionPendingRectStartGridNode = null;
                _columnRebarSectionIsLeftDrawDragging = false;
                return;
            }

            System.Windows.Point clickPoint = e.GetPosition(element);
            if (TryGetColumnRebarSectionNodeTag(e.OriginalSource as DependencyObject, out int tagGridX, out int tagGridY))
            {
                if (TryHandleColumnRebarSectionDrawNodeClick(element, clickPoint, tagGridX, tagGridY))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (!TryGetColumnRebarSectionNearestSnapNode(clickPoint, out int gridX, out int gridY, requireSnapRadius: false))
            {
                return;
            }

            if (TryHandleColumnRebarSectionDrawNodeClick(element, clickPoint, gridX, gridY))
            {
                e.Handled = true;
            }
        }

        private static bool TryGetColumnRebarSectionNodeTag(DependencyObject source, out int gridX, out int gridY)
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
                !string.Equals(parts[0], "N", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out gridX) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out gridY))
            {
                gridX = -1;
                gridY = -1;
                return false;
            }

            return true;
        }

        private bool TryHandleColumnRebarSectionDrawNodeClick(UIElement element, System.Windows.Point clickPoint, int gridX, int gridY)
        {
            if (element == null)
            {
                return false;
            }

            if (_columnRebarSectionEditMode != ColumnRebarSectionEditMode.DrawLineTie &&
                _columnRebarSectionEditMode != ColumnRebarSectionEditMode.DrawRectTie)
            {
                return false;
            }

            if (!_columnRebarSectionPendingRectStartGridNode.HasValue)
            {
                _columnRebarSectionPendingRectStartGridNode = (gridX, gridY);
                _columnRebarSectionIsLeftDrawDragging = true;
                _columnRebarSectionLeftDrawStartCanvasPoint = clickPoint;
                try
                {
                    element.CaptureMouse();
                }
                catch
                {
                }
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
                return true;
            }

            var (startX, startY) = _columnRebarSectionPendingRectStartGridNode.Value;

            // Same point: keep pending start point.
            if (startX == gridX && startY == gridY)
            {
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
                return true;
            }

            _columnRebarSectionPendingRectStartGridNode = null;
            _columnRebarSectionIsLeftDrawDragging = false;
            try
            {
                element.ReleaseMouseCapture();
            }
            catch
            {
            }

            bool changed = false;
            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie)
            {
                changed = AddColumnRebarCustomLineTie(startX, startY, gridX, gridY);
            }
            else if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie)
            {
                changed = AddColumnRebarCustomRectTie(startX, startY, gridX, gridY);
            }

            if (changed)
            {
                ShowStatus("Column Section: tie shape added.");
            }

            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
            return true;
        }

        private void OnColumnRebarSectionCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            if (_columnRebarSectionIsLeftDrawDragging)
            {
                _columnRebarSectionIsLeftDrawDragging = false;
                try
                {
                    element.ReleaseMouseCapture();
                }
                catch
                {
                }
            }

            _columnRebarSectionLastMouseCanvasPoint = e.GetPosition(element);

            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select ||
                _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete)
            {
                return;
            }

            if (!_columnRebarSectionPendingRectStartGridNode.HasValue)
            {
                return;
            }

            if (!TryGetColumnRebarSectionNearestSnapNode(e.GetPosition(element), out int gridX, out int gridY, requireSnapRadius: false))
            {
                return;
            }

            var (startX, startY) = _columnRebarSectionPendingRectStartGridNode.Value;

            // Single click on same point only sets/keeps the start point; drag or second click to another point completes.
            if (startX == gridX && startY == gridY)
            {
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
                e.Handled = true;
                return;
            }

            _columnRebarSectionPendingRectStartGridNode = null;

            bool changed = false;
            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie)
            {
                changed = AddColumnRebarCustomLineTie(startX, startY, gridX, gridY);
            }
            else if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie)
            {
                changed = AddColumnRebarCustomRectTie(startX, startY, gridX, gridY);
            }

            if (changed)
            {
                ShowStatus("Column Section: tie shape added.");
            }

            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
            e.Handled = true;
        }

        private void OnColumnRebarSectionModeChanged(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton rb) || rb.IsChecked != true)
            {
                return;
            }

            string name = rb.Name ?? "";
            if (string.Equals(name, "ColumnRebarSectionModeDrawLineRadio", StringComparison.Ordinal))
            {
                _columnRebarSectionEditMode = ColumnRebarSectionEditMode.DrawLineTie;
            }
            else if (string.Equals(name, "ColumnRebarSectionModeDrawRectRadio", StringComparison.Ordinal))
            {
                _columnRebarSectionEditMode = ColumnRebarSectionEditMode.DrawRectTie;
            }
            else if (string.Equals(name, "ColumnRebarSectionModeDeleteRadio", StringComparison.Ordinal))
            {
                _columnRebarSectionEditMode = ColumnRebarSectionEditMode.Delete;
            }
            else
            {
                _columnRebarSectionEditMode = ColumnRebarSectionEditMode.Select;
            }

            _columnRebarSectionPendingRectStartGridNode = null;
            _columnRebarSectionIsLeftDrawDragging = false;
            RefreshColumnRebarTieShapeBrowserModeState();
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarSectionClearCustomTiesClick(object sender, RoutedEventArgs e)
        {
            _columnRebarSectionCustomLineTies.Clear();
            _columnRebarSectionCustomRectTies.Clear();
            _columnRebarSectionPendingRectStartGridNode = null;
            _columnRebarSectionIsLeftDrawDragging = false;
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarSectionFitClick(object sender, RoutedEventArgs e)
        {
            _columnRebarSectionZoomFactor = 1.0;
            _columnRebarSectionPanXPx = 0.0;
            _columnRebarSectionPanYPx = 0.0;
            _columnRebarSectionIsLeftDrawDragging = false;
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarSectionResetViewClick(object sender, RoutedEventArgs e)
        {
            _columnRebarSectionZoomFactor = 1.0;
            _columnRebarSectionPanXPx = 0.0;
            _columnRebarSectionPanYPx = 0.0;
            _columnRebarSectionPendingRectStartGridNode = null;
            _columnRebarSectionIsLeftDrawDragging = false;
            _columnRebarSectionEditMode = ColumnRebarSectionEditMode.Select;
            if (FindName("ColumnRebarSectionModeSelectRadio") is RadioButton selectRadio)
            {
                selectRadio.IsChecked = true;
            }
            RefreshColumnRebarTieShapeBrowserModeState();
            RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
        }

        private void OnColumnRebarTieShapeBrowserListSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_columnRebarTieShapeBrowserSyncing)
            {
                return;
            }

            if (!(sender is System.Windows.Controls.ListBox listBox))
            {
                return;
            }

            if (!(listBox.SelectedItem is ComboItem selected))
            {
                return;
            }

            _columnRebarTieShapeBrowserSyncing = true;
            try
            {
                if (ReferenceEquals(listBox, GetColumnRebarOuterTieShapeBrowserList()))
                {
                    var combo = GetColumnRebarOuterTieShapeCombo();
                    if (combo != null)
                    {
                        TrySelectComboItemById(combo, selected.Id);
                    }
                }
                else if (ReferenceEquals(listBox, GetColumnRebarInnerTieShapeBrowserList()))
                {
                    var combo = GetColumnRebarInnerTieShapeCombo();
                    if (combo != null)
                    {
                        TrySelectComboItemById(combo, selected.Id);
                    }
                }
            }
            finally
            {
                _columnRebarTieShapeBrowserSyncing = false;
            }

            RefreshColumnRebarTieShapeBrowserModeState();
            RefreshColumnRebarTieShapeBrowserPreviews();
            RefreshColumnRebarPreviewFromUi();
        }

        private void RefreshColumnRebarTieShapeBrowserModeState()
        {
            var outerList = GetColumnRebarOuterTieShapeBrowserList();
            var innerList = GetColumnRebarInnerTieShapeBrowserList();
            if (outerList == null || innerList == null)
            {
                return;
            }

            bool outerActive = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie;
            bool innerActive = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie;
            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select ||
                _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete)
            {
                outerActive = true;
                innerActive = true;
            }

            outerList.IsEnabled = true;
            innerList.IsEnabled = true;
            outerList.Opacity = outerActive ? 1.0 : 0.75;
            innerList.Opacity = innerActive ? 1.0 : 0.75;
        }

        private void SyncColumnRebarTieShapeBrowserListsFromCombos()
        {
            if (_columnRebarTieShapeBrowserSyncing)
            {
                return;
            }

            var outerList = GetColumnRebarOuterTieShapeBrowserList();
            var innerList = GetColumnRebarInnerTieShapeBrowserList();
            var outerCombo = GetColumnRebarOuterTieShapeCombo();
            var innerCombo = GetColumnRebarInnerTieShapeCombo();
            if (outerList == null || innerList == null)
            {
                return;
            }

            _columnRebarTieShapeBrowserSyncing = true;
            try
            {
                if (outerList.ItemsSource == null && outerCombo?.ItemsSource != null)
                {
                    outerList.ItemsSource = outerCombo.ItemsSource;
                }

                if (innerList.ItemsSource == null && innerCombo?.ItemsSource != null)
                {
                    innerList.ItemsSource = innerCombo.ItemsSource;
                }

                ElementId outerId = (outerCombo?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;
                ElementId innerId = (innerCombo?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;
                TrySelectListBoxComboItemById(outerList, outerId);
                TrySelectListBoxComboItemById(innerList, innerId);
            }
            finally
            {
                _columnRebarTieShapeBrowserSyncing = false;
            }
        }

        private void OnColumnRebarSectionTieLineClick(object sender, MouseButtonEventArgs e)
        {
            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie ||
                _columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie)
            {
                var canvas = GetColumnRebarSectionCanvas();
                if (canvas != null)
                {
                    System.Windows.Point p = e.GetPosition(canvas);
                    _columnRebarSectionLastMouseCanvasPoint = p;
                    if (TryGetColumnRebarSectionNearestSnapNode(p, out int gx, out int gy, requireSnapRadius: false) &&
                        TryHandleColumnRebarSectionDrawNodeClick(canvas, p, gx, gy))
                    {
                        e.Handled = true;
                    }
                }
                return;
            }

            if (_columnRebarSectionEditMode != ColumnRebarSectionEditMode.Select)
            {
                return;
            }

            if (!(sender is FrameworkElement fe) || fe.Tag == null)
            {
                return;
            }

            string tag = fe.Tag.ToString() ?? "";
            string[] parts = tag.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx))
            {
                return;
            }

            bool changed = false;
            if (string.Equals(parts[0], "X", StringComparison.OrdinalIgnoreCase))
            {
                changed = _columnRebarSectionSelectedTieXIndices.Contains(idx)
                    ? _columnRebarSectionSelectedTieXIndices.Remove(idx)
                    : _columnRebarSectionSelectedTieXIndices.Add(idx);
            }
            else if (string.Equals(parts[0], "Y", StringComparison.OrdinalIgnoreCase))
            {
                changed = _columnRebarSectionSelectedTieYIndices.Contains(idx)
                    ? _columnRebarSectionSelectedTieYIndices.Remove(idx)
                    : _columnRebarSectionSelectedTieYIndices.Add(idx);
            }

            if (changed)
            {
                SyncColumnRebarTieLegTextBoxesFromSelection();
                RefreshColumnRebarPreviewFromUi();
            }

            e.Handled = true;
        }

        private void OnColumnRebarSectionCustomTieShapeClick(object sender, MouseButtonEventArgs e)
        {
            if (_columnRebarSectionEditMode != ColumnRebarSectionEditMode.Delete)
            {
                return;
            }

            if (!(sender is FrameworkElement fe) || fe.Tag == null)
            {
                return;
            }

            string tag = fe.Tag.ToString() ?? "";
            bool changed = false;
            if (tag.StartsWith("CL:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(tag.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineIdx))
            {
                if (lineIdx >= 0 && lineIdx < _columnRebarSectionCustomLineTies.Count)
                {
                    _columnRebarSectionCustomLineTies.RemoveAt(lineIdx);
                    changed = true;
                }
            }
            else if (tag.StartsWith("CR:", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(tag.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rectIdx))
            {
                if (rectIdx >= 0 && rectIdx < _columnRebarSectionCustomRectTies.Count)
                {
                    _columnRebarSectionCustomRectTies.RemoveAt(rectIdx);
                    changed = true;
                }
            }

            if (changed)
            {
                RefreshColumnRebarPreviewFromUi(refresh3D: false, refreshSection: true);
                e.Handled = true;
            }
        }

        private void OnColumnRebarSectionNodeHitClick(object sender, MouseButtonEventArgs e)
        {
            if (_columnRebarSectionEditMode != ColumnRebarSectionEditMode.DrawLineTie &&
                _columnRebarSectionEditMode != ColumnRebarSectionEditMode.DrawRectTie)
            {
                return;
            }

            if (!(sender is FrameworkElement fe) || fe.Tag == null)
            {
                return;
            }

            if (!TryGetColumnRebarSectionNodeTag(fe, out int gridX, out int gridY))
            {
                return;
            }

            var canvas = GetColumnRebarSectionCanvas();
            if (canvas == null)
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(canvas);
            _columnRebarSectionLastMouseCanvasPoint = p;
            if (TryHandleColumnRebarSectionDrawNodeClick(canvas, p, gridX, gridY))
            {
                e.Handled = true;
            }
        }

        private void OnColumnRebarPreviewViewportMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!(sender is UIElement element))
            {
                return;
            }

            _columnRebarPreviewIsDragging = true;
            _columnRebarPreviewLastMousePoint = e.GetPosition(element);
            try
            {
                element.CaptureMouse();
            }
            catch
            {
            }
        }

        private void OnColumnRebarPreviewViewportMouseMove(object sender, MouseEventArgs e)
        {
            if (!_columnRebarPreviewIsDragging || !(sender is UIElement element) || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            System.Windows.Point p = e.GetPosition(element);
            Vector delta = p - _columnRebarPreviewLastMousePoint;
            _columnRebarPreviewLastMousePoint = p;

            _columnRebarPreviewYawDeg += delta.X * 0.45;
            _columnRebarPreviewPitchDeg -= delta.Y * 0.30;
            _columnRebarPreviewPitchDeg = Math.Max(-80.0, Math.Min(80.0, _columnRebarPreviewPitchDeg));

            RefreshColumnRebarPreviewCameraOnly();
        }

        private void OnColumnRebarPreviewViewportMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _columnRebarPreviewIsDragging = false;
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
        }

        private void OnColumnRebarPreviewViewportMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double scale = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
            _columnRebarPreviewZoomFactor *= scale;
            _columnRebarPreviewZoomFactor = Math.Max(0.2, Math.Min(6.0, _columnRebarPreviewZoomFactor));
            RefreshColumnRebarPreviewCameraOnly();
            e.Handled = true;
        }

        public void UpdateColumnRebarBarTypes(List<ComboItem> items)
        {
            var mainBarCombo = GetColumnRebarMainBarTypeCombo();
            if (mainBarCombo != null)
            {
                mainBarCombo.ItemsSource = items;
                if (mainBarCombo.SelectedIndex < 0 && items.Count > 0)
                {
                    mainBarCombo.SelectedIndex = 0;
                }
            }

            var tieBarCombo = GetColumnRebarTieBarTypeCombo();
            if (tieBarCombo != null)
            {
                tieBarCombo.ItemsSource = items;
                if (tieBarCombo.SelectedIndex < 0 && items.Count > 0)
                {
                    int tieIndex = items.Count > 1 ? 1 : 0;
                    tieBarCombo.SelectedIndex = tieIndex;
                }
            }

            ApplyColumnRebarTopLapModeUi();
            RefreshColumnRebarPreviewFromUi();
            UpdateFoundationRebarBarTypes(items);
        }

        public void UpdateColumnRebarTieShapes(List<ComboItem> items)
        {
            var sourceItems = items ?? new List<ComboItem>();
            List<ComboItem> displayItems = sourceItems
                .Select(i => new ComboItem(i.Id, FormatColumnRebarTieShapeDisplayName(i?.Name)))
                .ToList();

            ElementId previousOuterId = (GetColumnRebarOuterTieShapeCombo()?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;
            ElementId previousInnerId = (GetColumnRebarInnerTieShapeCombo()?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;

            var outerShapeCombo = GetColumnRebarOuterTieShapeCombo();
            var outerShapeList = GetColumnRebarOuterTieShapeBrowserList();
            if (outerShapeCombo != null)
            {
                outerShapeCombo.ItemsSource = displayItems;
                if (!TrySelectComboItemById(outerShapeCombo, previousOuterId) && outerShapeCombo.SelectedIndex < 0 && displayItems.Count > 0)
                {
                    if (!TrySelectColumnRebarTieShapePreferred(outerShapeCombo, displayItems, preferredAliasToken: "M_T1", preferredShapeCode: "12"))
                    {
                        outerShapeCombo.SelectedIndex = 0;
                    }
                }
            }
            if (outerShapeList != null)
            {
                outerShapeList.ItemsSource = displayItems;
            }

            var innerShapeCombo = GetColumnRebarInnerTieShapeCombo();
            var innerShapeList = GetColumnRebarInnerTieShapeBrowserList();
            if (innerShapeCombo != null)
            {
                innerShapeCombo.ItemsSource = displayItems;
                if (!TrySelectComboItemById(innerShapeCombo, previousInnerId) && innerShapeCombo.SelectedIndex < 0 && displayItems.Count > 0)
                {
                    if (!TrySelectColumnRebarTieShapePreferred(innerShapeCombo, displayItems, preferredAliasToken: "M_01", preferredShapeCode: "01"))
                    {
                        int idx = displayItems.Count > 0 ? 0 : -1;
                        innerShapeCombo.SelectedIndex = idx;
                    }
                }
            }
            if (innerShapeList != null)
            {
                innerShapeList.ItemsSource = displayItems;
            }

            SyncColumnRebarTieShapeBrowserListsFromCombos();
            RefreshColumnRebarTieShapeBrowserModeState();
            RefreshColumnRebarTieShapeBrowserPreviews();
            RefreshColumnRebarPreviewFromUi();
        }

        private static bool TrySelectColumnRebarTieShapePreferred(System.Windows.Controls.ComboBox combo, IList<ComboItem> items, string preferredAliasToken, string preferredShapeCode)
        {
            if (combo == null || items == null || items.Count == 0)
            {
                return false;
            }

            int aliasIndex = -1;
            int shapeCodeIndex = -1;
            for (int i = 0; i < items.Count; i++)
            {
                string name = (items[i]?.Name ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(preferredAliasToken) &&
                    name.IndexOf(preferredAliasToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    aliasIndex = i;
                    break;
                }

                ParseColumnRebarTieShapeDisplay(name, out _, out string shapeCode, out _);
                if (string.Equals((shapeCode ?? "").Trim(), (preferredShapeCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    shapeCodeIndex = i;
                }
            }

            int pickIndex = aliasIndex >= 0 ? aliasIndex : shapeCodeIndex;
            if (pickIndex < 0 || pickIndex >= items.Count)
            {
                return false;
            }

            combo.SelectedIndex = pickIndex;
            return true;
        }

        public void UpdateColumnRebarHostPreview(ColumnRebarHostPreviewData data)
        {
            _columnRebarHostPreview = data;
            _columnRebarSectionZoomFactor = 1.0;
            _columnRebarSectionPanXPx = 0.0;
            _columnRebarSectionPanYPx = 0.0;
            _columnRebarElevationZoomFactor = 1.0;
            _columnRebarElevationPanXPx = 0.0;
            _columnRebarElevationPanYPx = 0.0;
            _columnRebarElevationHoveredTieLevelIndex = -1;
            _columnRebarSectionSelectedTieXIndices.Clear();
            _columnRebarSectionSelectedTieYIndices.Clear();
            _columnRebarSectionCustomLineTies.Clear();
            _columnRebarSectionCustomRectTies.Clear();
            _columnRebarSectionPendingRectStartGridNode = null;
            _columnRebarSectionGridRenderState = null;
            _columnRebarElevationRenderState = null;

            var hostTextBox = GetColumnRebarHostTextBox();
            if (hostTextBox != null)
            {
                hostTextBox.Text = data == null || data.HostElementId == ElementId.InvalidElementId
                    ? "(not selected)"
                    : (string.IsNullOrWhiteSpace(data.HostDisplayName) ? $"Element {data.HostElementId.Value}" : data.HostDisplayName);
            }

            RefreshColumnRebarPreviewFromUi();

            if (data != null && data.HostElementId != ElementId.InvalidElementId)
            {
                ShowStatus($"Column Rebar: selected host column {data.HostElementId.Value}.");
            }
        }

        private bool IsDrawingColumnRebarSubTab()
        {
            if (!IsDrawingTab()) return false;

            var drawingSubTabControl = GetDrawingSubTabControl();
            if (drawingSubTabControl?.SelectedItem is System.Windows.Controls.TabItem item)
            {
                if (ReferenceEquals(item, FindName("ReinforcementColumnTab") as System.Windows.Controls.TabItem))
                {
                    return true;
                }

                return string.Equals(item.Header?.ToString(), "COLUMN", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private System.Windows.Controls.TextBox GetColumnRebarHostTextBox()
        {
            return FindName("ColumnRebarHostTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarMainBarTypeCombo()
        {
            return FindName("ColumnRebarMainBarTypeCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarTieBarTypeCombo()
        {
            return FindName("ColumnRebarTieBarTypeCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarOuterTieShapeCombo()
        {
            return FindName("ColumnRebarOuterTieShapeCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarInnerTieShapeCombo()
        {
            return FindName("ColumnRebarInnerTieShapeCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.Canvas GetColumnRebarOuterTieShapePreviewCanvas()
        {
            return FindName("ColumnRebarOuterTieShapePreviewCanvas") as System.Windows.Controls.Canvas;
        }

        private System.Windows.Controls.Canvas GetColumnRebarInnerTieShapePreviewCanvas()
        {
            return FindName("ColumnRebarInnerTieShapePreviewCanvas") as System.Windows.Controls.Canvas;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarOuterTieShapePreviewText()
        {
            return FindName("ColumnRebarOuterTieShapePreviewText") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarInnerTieShapePreviewText()
        {
            return FindName("ColumnRebarInnerTieShapePreviewText") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.ListBox GetColumnRebarOuterTieShapeBrowserList()
        {
            return FindName("ColumnRebarOuterTieShapeBrowserList") as System.Windows.Controls.ListBox;
        }

        private System.Windows.Controls.ListBox GetColumnRebarInnerTieShapeBrowserList()
        {
            return FindName("ColumnRebarInnerTieShapeBrowserList") as System.Windows.Controls.ListBox;
        }

        private System.Windows.Controls.CheckBox GetColumnRebarCreateTiesCheck()
        {
            return FindName("ColumnRebarCreateTiesCheck") as System.Windows.Controls.CheckBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarCoverMmTextBox()
        {
            return FindName("ColumnRebarCoverMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieSpacingMmTextBox()
        {
            return FindName("ColumnRebarTieSpacingMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieSpacingBottomMmTextBox()
        {
            return FindName("ColumnRebarTieSpacingBottomMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieSpacingTopMmTextBox()
        {
            return FindName("ColumnRebarTieSpacingTopMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieZoneBottomLengthMmTextBox()
        {
            return FindName("ColumnRebarTieZoneBottomLengthMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieZoneMiddleLengthMmTextBox()
        {
            return FindName("ColumnRebarTieZoneMiddleLengthMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieZoneTopLengthMmTextBox()
        {
            return FindName("ColumnRebarTieZoneTopLengthMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarBarsXTextBox()
        {
            return FindName("ColumnRebarBarsXTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarBarsYTextBox()
        {
            return FindName("ColumnRebarBarsYTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTopOffsetMmTextBox()
        {
            return FindName("ColumnRebarTopOffsetMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTopLapMmTextBox()
        {
            return FindName("ColumnRebarTopLapMmTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarTopLapModeCombo()
        {
            return FindName("ColumnRebarTopLapModeCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarTopLapInputLabel()
        {
            return FindName("ColumnRebarTopLapInputLabel") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.ComboBox GetColumnRebarTopLapCoverageCombo()
        {
            return FindName("ColumnRebarTopLapCoverageCombo") as System.Windows.Controls.ComboBox;
        }

        private System.Windows.Controls.Viewport3D GetColumnRebarPreviewViewport()
        {
            return FindName("ColumnRebarPreviewViewport") as System.Windows.Controls.Viewport3D;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarPreviewInfoText()
        {
            return FindName("ColumnRebarPreviewInfoText") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.Canvas GetColumnRebarElevationCanvas()
        {
            return FindName("ColumnRebarElevationCanvas") as System.Windows.Controls.Canvas;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarElevationInfoText()
        {
            return FindName("ColumnRebarElevationInfoText") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.Canvas GetColumnRebarSectionCanvas()
        {
            return FindName("ColumnRebarSectionCanvas") as System.Windows.Controls.Canvas;
        }

        private System.Windows.Controls.TextBlock GetColumnRebarSectionInfoText()
        {
            return FindName("ColumnRebarSectionInfoText") as System.Windows.Controls.TextBlock;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieInnerLegsXTextBox()
        {
            return FindName("ColumnRebarTieInnerLegsXTextBox") as System.Windows.Controls.TextBox;
        }

        private System.Windows.Controls.TextBox GetColumnRebarTieInnerLegsYTextBox()
        {
            return FindName("ColumnRebarTieInnerLegsYTextBox") as System.Windows.Controls.TextBox;
        }

        private bool TryRaiseColumnRebarGenerateRequest()
        {
            if (!TryUpdateColumnRebarRequestFromUi())
            {
                return false;
            }

            _handler.Request.RequestType = CadToModelRequestType.GenerateColumnRebar;
            QueueExternalRequest();
            return true;
        }

        private bool TryUpdateColumnRebarRequestFromUi()
        {
            if (_columnRebarHostPreview == null || _columnRebarHostPreview.HostElementId == ElementId.InvalidElementId)
            {
                ShowStatus("Column Rebar: pick a structural column first.");
                return false;
            }

            var mainBarCombo = GetColumnRebarMainBarTypeCombo();
            if (!(mainBarCombo?.SelectedItem is ComboItem mainBar))
            {
                ShowStatus("Column Rebar: select main bar type.");
                return false;
            }

            var tieBarCombo = GetColumnRebarTieBarTypeCombo();
            if (!(tieBarCombo?.SelectedItem is ComboItem tieBar))
            {
                ShowStatus("Column Rebar: select tie bar type.");
                return false;
            }

            if (!TryReadColumnRebarPreviewInput(out ColumnRebarPreviewInputData input, true))
            {
                return false;
            }

            _handler.Request.ColumnRebarHostElementId = _columnRebarHostPreview.HostElementId;
            _handler.Request.ColumnRebarMainBarTypeId = mainBar.Id;
            _handler.Request.ColumnRebarTieBarTypeId = tieBar.Id;
            _handler.Request.ColumnRebarOuterTieShapeId = (GetColumnRebarOuterTieShapeCombo()?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;
            _handler.Request.ColumnRebarInnerTieShapeId = (GetColumnRebarInnerTieShapeCombo()?.SelectedItem as ComboItem)?.Id ?? ElementId.InvalidElementId;
            _handler.Request.ColumnRebarBarsX = input.BarsX;
            _handler.Request.ColumnRebarBarsY = input.BarsY;
            _handler.Request.ColumnRebarCoverFt = input.CoverFt;
            _handler.Request.ColumnRebarTieSpacingBottomFt = input.TieSpacingBottomFt;
            _handler.Request.ColumnRebarTieSpacingFt = input.TieSpacingMiddleFt;
            _handler.Request.ColumnRebarTieSpacingTopFt = input.TieSpacingTopFt;
            _handler.Request.ColumnRebarTieZoneBottomLengthFt = input.TieZoneBottomLengthFt;
            _handler.Request.ColumnRebarTieZoneMiddleLengthFt = input.TieZoneMiddleLengthFt;
            _handler.Request.ColumnRebarTieZoneTopLengthFt = input.TieZoneTopLengthFt;
            _handler.Request.ColumnRebarTieZoneBottomPercent = input.TieZoneBottomPercent;
            _handler.Request.ColumnRebarTieZoneMiddlePercent = input.TieZoneMiddlePercent;
            _handler.Request.ColumnRebarTieZoneTopPercent = input.TieZoneTopPercent;
            _handler.Request.ColumnRebarBottomOffsetFt = input.BottomOffsetFt;
            _handler.Request.ColumnRebarTopOffsetFt = input.TopOffsetFt;
            _handler.Request.ColumnRebarTopLapLengthFt = input.TopLapLengthFt;
            _handler.Request.ColumnRebarTopLapBarDiameterMultiplier = input.TopLapBarDiameterMultiplier;
            _handler.Request.ColumnRebarTopLapBarCoveragePercent = input.TopLapBarCoveragePercent;
            _handler.Request.ColumnRebarTieInnerLegsX = input.TieInnerLegsX;
            _handler.Request.ColumnRebarTieInnerLegsY = input.TieInnerLegsY;
            _handler.Request.ColumnRebarTieSelectedXIndices = input.SelectedTieXIndices?.ToList() ?? new List<int>();
            _handler.Request.ColumnRebarTieSelectedYIndices = input.SelectedTieYIndices?.ToList() ?? new List<int>();
            _handler.Request.ColumnRebarCustomLineTies = _columnRebarSectionCustomLineTies
                .Select(x => new ColumnRebarCustomLineTieSpec
                {
                    X0GridIndex = x.X0GridIndex,
                    Y0GridIndex = x.Y0GridIndex,
                    X1GridIndex = x.X1GridIndex,
                    Y1GridIndex = x.Y1GridIndex
                })
                .ToList();
            _handler.Request.ColumnRebarCustomRectTies = _columnRebarSectionCustomRectTies
                .Select(x => new ColumnRebarCustomRectTieSpec
                {
                    X0GridIndex = x.X0GridIndex,
                    Y0GridIndex = x.Y0GridIndex,
                    X1GridIndex = x.X1GridIndex,
                    Y1GridIndex = x.Y1GridIndex
                })
                .ToList();
            _handler.Request.ColumnRebarCreateTies = input.CreateTies;
            return true;
        }

        private void ScheduleColumnRebarPreviewRefresh(bool refresh3D, bool refreshSection)
        {
            _columnRebarPreviewRefreshPending3D = _columnRebarPreviewRefreshPending3D || refresh3D;
            _columnRebarPreviewRefreshPendingSection = _columnRebarPreviewRefreshPendingSection || refreshSection;

            if (_columnRebarPreviewRefreshTimer == null)
            {
                _columnRebarPreviewRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
                {
                    Interval = ColumnRebarPreviewRefreshDebounceInterval
                };
                _columnRebarPreviewRefreshTimer.Tick += OnColumnRebarPreviewRefreshTimerTick;
            }

            _columnRebarPreviewRefreshTimer.Stop();
            _columnRebarPreviewRefreshTimer.Start();
        }

        private void OnColumnRebarPreviewRefreshTimerTick(object sender, EventArgs e)
        {
            _columnRebarPreviewRefreshTimer?.Stop();

            bool refresh3D = _columnRebarPreviewRefreshPending3D;
            bool refreshSection = _columnRebarPreviewRefreshPendingSection;
            _columnRebarPreviewRefreshPending3D = false;
            _columnRebarPreviewRefreshPendingSection = false;

            if (!refresh3D && !refreshSection)
            {
                return;
            }

            RefreshColumnRebarPreviewFromUi(refresh3D: refresh3D, refreshSection: refreshSection, clearQueuedRefresh: false);
        }

        private void InitializeColumnRebarPreviewViewport()
        {
            try
            {
                RefreshColumnRebarTieShapeBrowserPreviews();
                RefreshColumnRebarPreviewFromUi();
            }
            catch
            {
                // Keep window usable even if preview setup fails in design/runtime edge cases.
            }
        }

        private void RefreshColumnRebarPreviewFromUi()
        {
            RefreshColumnRebarPreviewFromUi(refresh3D: true, refreshSection: true);
        }

        private void RefreshColumnRebarPreviewFromUi(bool refresh3D, bool refreshSection)
        {
            RefreshColumnRebarPreviewFromUi(refresh3D, refreshSection, clearQueuedRefresh: true);
        }

        private void RefreshColumnRebarPreviewFromUi(bool refresh3D, bool refreshSection, bool clearQueuedRefresh)
        {
            if (clearQueuedRefresh)
            {
                _columnRebarPreviewRefreshTimer?.Stop();
                _columnRebarPreviewRefreshPending3D = false;
                _columnRebarPreviewRefreshPendingSection = false;
            }

            if (!TryReadColumnRebarPreviewInput(out ColumnRebarPreviewInputData input, false))
            {
                var createTiesCheck = GetColumnRebarCreateTiesCheck();
                input = new ColumnRebarPreviewInputData
                {
                    CoverFt = MmToFeetUi(40.0),
                    TieSpacingBottomFt = MmToFeetUi(100.0),
                    TieSpacingMiddleFt = MmToFeetUi(150.0),
                    TieSpacingTopFt = MmToFeetUi(100.0),
                    TieZoneBottomLengthFt = 0.0,
                    TieZoneMiddleLengthFt = 0.0,
                    TieZoneTopLengthFt = 0.0,
                    TieZoneBottomPercent = 33.33,
                    TieZoneMiddlePercent = 33.33,
                    TieZoneTopPercent = 33.34,
                    BottomOffsetFt = 0.0,
                    TopOffsetFt = MmToFeetUi(50.0),
                    TopLapLengthFt = MmToFeetUi(600.0),
                    TopLapBarDiameterMultiplier = 0,
                    TopLapBarCoveragePercent = 100,
                    BarsX = 4,
                    BarsY = 4,
                    TieInnerLegsX = 0,
                    TieInnerLegsY = 0,
                    CreateTies = createTiesCheck?.IsChecked != false
                };
            }

            if (refresh3D)
            {
                RenderColumnRebarPreview(_columnRebarHostPreview, input, renderSectionPreview: refreshSection);
            }
            else if (refreshSection)
            {
                RenderColumnRebarSectionPreview(_columnRebarHostPreview, input);
            }

            RenderColumnRebarElevationPreview(_columnRebarHostPreview, input);
        }

        private void RefreshColumnRebarPreviewCameraOnly()
        {
            var previewViewport = GetColumnRebarPreviewViewport();
            if (previewViewport == null)
            {
                return;
            }

            if (_columnRebarPreviewSceneWidthFt <= 0.0 ||
                _columnRebarPreviewSceneDepthFt <= 0.0 ||
                _columnRebarPreviewSceneHeightFt <= 0.0)
            {
                return;
            }

            previewViewport.Camera = BuildColumnRebarPreviewCamera(
                _columnRebarPreviewSceneWidthFt,
                _columnRebarPreviewSceneDepthFt,
                _columnRebarPreviewSceneHeightFt);
        }

        private bool TryReadColumnRebarPreviewInput(out ColumnRebarPreviewInputData input, bool showErrors)
        {
            input = null;
            var coverTextBox = GetColumnRebarCoverMmTextBox();
            var tieSpacingBottomTextBox = GetColumnRebarTieSpacingBottomMmTextBox();
            var tieSpacingTextBox = GetColumnRebarTieSpacingMmTextBox();
            var tieSpacingTopTextBox = GetColumnRebarTieSpacingTopMmTextBox();
            var tieZoneBottomLengthTextBox = GetColumnRebarTieZoneBottomLengthMmTextBox();
            var tieZoneMiddleLengthTextBox = GetColumnRebarTieZoneMiddleLengthMmTextBox();
            var tieZoneTopLengthTextBox = GetColumnRebarTieZoneTopLengthMmTextBox();
            var topOffsetTextBox = GetColumnRebarTopOffsetMmTextBox();
            var topLapTextBox = GetColumnRebarTopLapMmTextBox();
            var topLapModeCombo = GetColumnRebarTopLapModeCombo();
            var topLapCoverageCombo = GetColumnRebarTopLapCoverageCombo();
            var barsXTextBox = GetColumnRebarBarsXTextBox();
            var barsYTextBox = GetColumnRebarBarsYTextBox();
            var tieInnerLegsXTextBox = GetColumnRebarTieInnerLegsXTextBox();
            var tieInnerLegsYTextBox = GetColumnRebarTieInnerLegsYTextBox();
            var createTiesCheck = GetColumnRebarCreateTiesCheck();

            if (!TryParseNonNegativeDoubleMm(coverTextBox?.Text, out double coverMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Cover (mm) must be a non-negative number.");
                return false;
            }

            if (!TryParsePositiveDoubleMm(tieSpacingBottomTextBox?.Text, out double tieSpacingBottomMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Tie Bottom (mm) must be greater than zero.");
                return false;
            }

            if (!TryParsePositiveDoubleMm(tieSpacingTextBox?.Text, out double tieSpacingMiddleMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Tie Middle (mm) must be greater than zero.");
                return false;
            }

            if (!TryParsePositiveDoubleMm(tieSpacingTopTextBox?.Text, out double tieSpacingTopMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Tie Top (mm) must be greater than zero.");
                return false;
            }

            double columnHeightMmForTieLengthExpression = Math.Max(0.0, FeetToMmUi(_columnRebarHostPreview?.HeightFt ?? 0.0));

            if (!TryParseColumnRebarTieZoneLengthMm(tieZoneBottomLengthTextBox?.Text, columnHeightMmForTieLengthExpression, out double tieZoneBottomLengthMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Bottom Length must be mm, NxL (6x100), or factor*L (0.2*L, L=column height).");
                return false;
            }

            if (!TryParseColumnRebarTieZoneLengthMm(tieZoneMiddleLengthTextBox?.Text, columnHeightMmForTieLengthExpression, out double tieZoneMiddleLengthMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Middle Length must be mm, NxL (8x150), or factor*L (0.3*L, L=column height).");
                return false;
            }

            if (!TryParseColumnRebarTieZoneLengthMm(tieZoneTopLengthTextBox?.Text, columnHeightMmForTieLengthExpression, out double tieZoneTopLengthMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Top Length must be mm, NxL (5x100), or factor*L (0.15*L, L=column height).");
                return false;
            }

            if (!TryParseNonNegativeDoubleMm(topOffsetTextBox?.Text, out double topOffsetMm))
            {
                if (showErrors) ShowStatus("Column Rebar: Top Offset (mm) must be a non-negative number.");
                return false;
            }

            bool topLapByDiameterMode = IsColumnRebarTopLapByDiameterMode(topLapModeCombo);
            if (!TryParseNonNegativeDoubleMm(topLapTextBox?.Text, out double topLapInputValue))
            {
                if (showErrors)
                {
                    ShowStatus(topLapByDiameterMode
                        ? "Column Rebar: Top Lap N (Dia) must be a non-negative number."
                        : "Column Rebar: Top Lap (mm) must be a non-negative number.");
                }
                return false;
            }

            int topLapBarDiameterMultiplier = 0;
            double topLapMm = topLapInputValue;
            if (topLapByDiameterMode)
            {
                if (Math.Abs(topLapInputValue - Math.Round(topLapInputValue)) > 1e-6)
                {
                    if (showErrors) ShowStatus("Column Rebar: Top Lap N (Dia) must be a whole number.");
                    return false;
                }

                topLapBarDiameterMultiplier = (int)Math.Round(topLapInputValue);
                if (topLapBarDiameterMultiplier < 0)
                {
                    if (showErrors) ShowStatus("Column Rebar: Top Lap N (Dia) must be a non-negative number.");
                    return false;
                }

                topLapMm = 0.0;
                if (topLapBarDiameterMultiplier > 0 &&
                    TryEstimateColumnRebarMainBarDiameterMm(out double mainBarDiameterMm) &&
                    mainBarDiameterMm > 0.0)
                {
                    topLapMm = mainBarDiameterMm * topLapBarDiameterMultiplier;
                }
            }

            int topLapBarCoveragePercent = GetColumnRebarTopLapCoveragePercent(topLapCoverageCombo);

            if (!TryParsePositiveInt(barsXTextBox?.Text, out int barsX) || barsX < 2)
            {
                if (showErrors) ShowStatus("Column Rebar: Bars X must be an integer >= 2.");
                return false;
            }

            if (!TryParsePositiveInt(barsYTextBox?.Text, out int barsY) || barsY < 2)
            {
                if (showErrors) ShowStatus("Column Rebar: Bars Y must be an integer >= 2.");
                return false;
            }

            if (!TryParseNonNegativeInt(tieInnerLegsXTextBox?.Text, out int tieInnerLegsX))
            {
                if (showErrors) ShowStatus("Column Rebar: Tie Legs X must be an integer >= 0.");
                return false;
            }

            if (!TryParseNonNegativeInt(tieInnerLegsYTextBox?.Text, out int tieInnerLegsY))
            {
                if (showErrors) ShowStatus("Column Rebar: Tie Legs Y must be an integer >= 0.");
                return false;
            }

            int candidateTieXCount = Math.Max(0, barsX - 2);
            int candidateTieYCount = Math.Max(0, barsY - 2);
            NormalizeColumnRebarSectionSelectionSet(_columnRebarSectionSelectedTieXIndices, candidateTieXCount);
            NormalizeColumnRebarSectionSelectionSet(_columnRebarSectionSelectedTieYIndices, candidateTieYCount);

            bool xCountTextFocused = tieInnerLegsXTextBox?.IsKeyboardFocusWithin == true;
            bool yCountTextFocused = tieInnerLegsYTextBox?.IsKeyboardFocusWithin == true;

            tieInnerLegsX = Math.Min(candidateTieXCount, Math.Max(0, tieInnerLegsX));
            tieInnerLegsY = Math.Min(candidateTieYCount, Math.Max(0, tieInnerLegsY));

            if (xCountTextFocused || (_columnRebarSectionSelectedTieXIndices.Count == 0 && tieInnerLegsX > 0))
            {
                ApplyColumnRebarSectionSelectionFromCount(_columnRebarSectionSelectedTieXIndices, candidateTieXCount, tieInnerLegsX);
            }

            if (yCountTextFocused || (_columnRebarSectionSelectedTieYIndices.Count == 0 && tieInnerLegsY > 0))
            {
                ApplyColumnRebarSectionSelectionFromCount(_columnRebarSectionSelectedTieYIndices, candidateTieYCount, tieInnerLegsY);
            }

            if (_columnRebarSectionSelectedTieXIndices.Count == 0 && tieInnerLegsX == 0)
            {
                _columnRebarSectionSelectedTieXIndices.Clear();
            }
            if (_columnRebarSectionSelectedTieYIndices.Count == 0 && tieInnerLegsY == 0)
            {
                _columnRebarSectionSelectedTieYIndices.Clear();
            }

            tieInnerLegsX = _columnRebarSectionSelectedTieXIndices.Count;
            tieInnerLegsY = _columnRebarSectionSelectedTieYIndices.Count;

            input = new ColumnRebarPreviewInputData
            {
                CoverFt = MmToFeetUi(coverMm),
                TieSpacingBottomFt = MmToFeetUi(tieSpacingBottomMm),
                TieSpacingMiddleFt = MmToFeetUi(tieSpacingMiddleMm),
                TieSpacingTopFt = MmToFeetUi(tieSpacingTopMm),
                TieZoneBottomLengthFt = MmToFeetUi(tieZoneBottomLengthMm),
                TieZoneMiddleLengthFt = MmToFeetUi(tieZoneMiddleLengthMm),
                TieZoneTopLengthFt = MmToFeetUi(tieZoneTopLengthMm),
                TieZoneBottomPercent = 33.33,
                TieZoneMiddlePercent = 33.33,
                TieZoneTopPercent = 33.34,
                BottomOffsetFt = 0.0,
                TopOffsetFt = MmToFeetUi(topOffsetMm),
                TopLapLengthFt = MmToFeetUi(topLapMm),
                TopLapBarDiameterMultiplier = topLapBarDiameterMultiplier,
                TopLapBarCoveragePercent = topLapBarCoveragePercent,
                BarsX = barsX,
                BarsY = barsY,
                TieInnerLegsX = tieInnerLegsX,
                TieInnerLegsY = tieInnerLegsY,
                SelectedTieXIndices = _columnRebarSectionSelectedTieXIndices.OrderBy(i => i).ToList(),
                SelectedTieYIndices = _columnRebarSectionSelectedTieYIndices.OrderBy(i => i).ToList(),
                CreateTies = createTiesCheck?.IsChecked != false
            };
            return true;
        }

        private static bool IsColumnRebarTopLapByDiameterMode(System.Windows.Controls.ComboBox modeCombo)
        {
            string mode = modeCombo?.SelectedItem is System.Windows.Controls.ComboBoxItem comboItem
                ? (comboItem.Content?.ToString() ?? "")
                : (modeCombo?.SelectedItem?.ToString() ?? modeCombo?.Text ?? "");

            mode = (mode ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(mode))
            {
                return false;
            }

            string normalized = Regex.Replace(mode, "\\s+", "");
            bool isLengthMode = normalized.Contains("LENGTH") || normalized.Contains("MM");
            bool isDiameterMode = normalized.Contains("DIA") ||
                                  normalized.Contains("DIAMETER") ||
                                  normalized.Contains("Ø") ||
                                  normalized.Contains("Ã˜");
            return isDiameterMode && !isLengthMode;
        }

        private static int GetColumnRebarTopLapCoveragePercent(System.Windows.Controls.ComboBox combo)
        {
            string text = combo?.SelectedItem is System.Windows.Controls.ComboBoxItem item
                ? (item.Content?.ToString() ?? "")
                : (combo?.SelectedItem?.ToString() ?? combo?.Text ?? "");

            text = (text ?? "").Trim();
            if (text.IndexOf("50", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 50;
            }

            return 100;
        }

        private void ApplyColumnRebarTopLapModeUi()
        {
            var modeCombo = GetColumnRebarTopLapModeCombo();
            var topLapTextBox = GetColumnRebarTopLapMmTextBox();
            var topLapInputLabel = GetColumnRebarTopLapInputLabel();
            if (modeCombo == null || topLapTextBox == null)
            {
                return;
            }

            bool byDiameterMode = IsColumnRebarTopLapByDiameterMode(modeCombo);
            topLapTextBox.IsReadOnly = false;

            if (topLapInputLabel != null)
            {
                topLapInputLabel.Text = byDiameterMode ? "Top Lap N (Dia):" : "Top Lap (mm):";
            }
        }

        private void RefreshColumnRebarTieShapeBrowserPreviews()
        {
            RenderColumnRebarTieShapeBrowserPreview(
                GetColumnRebarOuterTieShapePreviewCanvas(),
                GetColumnRebarOuterTieShapePreviewText(),
                (GetColumnRebarOuterTieShapeCombo()?.SelectedItem as ComboItem)?.Name,
                "Outer");

            RenderColumnRebarTieShapeBrowserPreview(
                GetColumnRebarInnerTieShapePreviewCanvas(),
                GetColumnRebarInnerTieShapePreviewText(),
                (GetColumnRebarInnerTieShapeCombo()?.SelectedItem as ComboItem)?.Name,
                "Inner");
        }

        private void RenderColumnRebarTieShapeBrowserPreview(
            System.Windows.Controls.Canvas canvas,
            System.Windows.Controls.TextBlock infoText,
            string displayName,
            string roleLabel)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();

            double w = canvas.ActualWidth;
            double h = canvas.ActualHeight;
            if (double.IsNaN(w) || double.IsInfinity(w) || w < 40.0) w = 120.0;
            if (double.IsNaN(h) || double.IsInfinity(h) || h < 24.0) h = 56.0;

            string text = (displayName ?? "").Trim();
            ParseColumnRebarTieShapeDisplay(text, out string familyCode, out string shapeCode, out string standardLabel);

            if (infoText != null)
            {
                string shapeLabel = string.IsNullOrWhiteSpace(shapeCode) ? "-" : shapeCode;
                string std = string.IsNullOrWhiteSpace(standardLabel) ? "" : $" | {standardLabel}";
                string fam = string.IsNullOrWhiteSpace(familyCode) ? "" : $" | ID {familyCode}";
                infoText.Text = $"{roleLabel}: Shape {shapeLabel}{std}{fam}";
            }

            double left = 10.0;
            double top = 8.0;
            double right = w - 10.0;
            double bottom = h - 8.0;
            if (right <= left + 6.0 || bottom <= top + 6.0)
            {
                return;
            }

            string normalizedCode = (shapeCode ?? "").Trim().ToUpperInvariant();
            bool isClosedRect = normalizedCode == "00" || normalizedCode == "51" || normalizedCode == "52" || normalizedCode == "98" || normalizedCode == "99";
            bool isUShape = normalizedCode == "22" || normalizedCode == "46" || normalizedCode == "47";
            bool isBent = normalizedCode == "44";

            var stroke = new SolidColorBrush(WpfColor.FromRgb(55, 70, 95));
            var accent = new SolidColorBrush(WpfColor.FromRgb(0, 120, 215));

            if (isUShape)
            {
                var pl = new System.Windows.Shapes.Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = 2.0
                };
                double legTop = top + (bottom - top) * 0.22;
                pl.Points.Add(new System.Windows.Point(left, legTop));
                pl.Points.Add(new System.Windows.Point(left, bottom));
                pl.Points.Add(new System.Windows.Point(right, bottom));
                pl.Points.Add(new System.Windows.Point(right, legTop));
                canvas.Children.Add(pl);

                AddShapePreviewHook(canvas, right, legTop, -8, -8, stroke);
                AddShapePreviewHook(canvas, left, legTop, 8, -8, stroke);
            }
            else if (isBent)
            {
                var pl = new System.Windows.Shapes.Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = 2.0
                };
                double midY = (top + bottom) * 0.5;
                pl.Points.Add(new System.Windows.Point(left, top + 6));
                pl.Points.Add(new System.Windows.Point(left + (right - left) * 0.35, midY));
                pl.Points.Add(new System.Windows.Point(right - (right - left) * 0.20, midY));
                pl.Points.Add(new System.Windows.Point(right, top + 10));
                canvas.Children.Add(pl);
            }
            else
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = right - left,
                    Height = bottom - top,
                    RadiusX = isClosedRect ? 4.0 : 1.5,
                    RadiusY = isClosedRect ? 4.0 : 1.5,
                    Stroke = stroke,
                    StrokeThickness = 2.0
                };
                Canvas.SetLeft(rect, left);
                Canvas.SetTop(rect, top);
                canvas.Children.Add(rect);

                if (normalizedCode == "98" || normalizedCode == "99")
                {
                    AddShapePreviewHook(canvas, right - 3, top + 3, -8, 8, accent);
                    AddShapePreviewHook(canvas, left + 3, bottom - 3, 8, -8, accent);
                }
            }

            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = "No shape",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(110, 120, 140)),
                    FontSize = 10
                };
                Canvas.SetLeft(tb, 6);
                Canvas.SetTop(tb, Math.Max(2, h * 0.35));
                canvas.Children.Add(tb);
            }
        }

        private static string FormatColumnRebarTieShapeDisplayName(string rawName)
        {
            string text = (rawName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            ParseColumnRebarTieShapeDisplay(text, out string familyCode, out string shapeCode, out string standardLabel);
            if (string.IsNullOrWhiteSpace(shapeCode) && string.IsNullOrWhiteSpace(familyCode))
            {
                return text;
            }

            string shapePart = string.IsNullOrWhiteSpace(shapeCode) ? "Shape ?" : $"Shape {shapeCode}";
            string stdPart = string.IsNullOrWhiteSpace(standardLabel) ? "" : $" [{standardLabel}]";
            string idPart = string.IsNullOrWhiteSpace(familyCode) ? "" : $" - {familyCode}";
            return $"{shapePart}{stdPart}{idPart}";
        }

        private static void ParseColumnRebarTieShapeDisplay(string text, out string familyCode, out string shapeCode, out string standardLabel)
        {
            familyCode = "";
            shapeCode = "";
            standardLabel = "";

            string raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            Match m = Regex.Match(raw, @"^(?<shapeLabel>Shape\s+)?(?<family>\d+)\s*-\s*(?<shape>[^\[\]-]+)\s*(\[(?<std>[^\]]+)\])?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                familyCode = (m.Groups["family"].Value ?? "").Trim();
                shapeCode = (m.Groups["shape"].Value ?? "").Trim();
                standardLabel = (m.Groups["std"].Value ?? "").Trim();
                return;
            }

            Match m2 = Regex.Match(raw, @"Shape\s+(?<shape>[^\[\]-]+)\s*(\[(?<std>[^\]]+)\])?\s*-\s*(?<family>\d+)", RegexOptions.IgnoreCase);
            if (m2.Success)
            {
                familyCode = (m2.Groups["family"].Value ?? "").Trim();
                shapeCode = (m2.Groups["shape"].Value ?? "").Trim();
                standardLabel = (m2.Groups["std"].Value ?? "").Trim();
                return;
            }
        }

        private bool TryEstimateColumnRebarMainBarDiameterMm(out double diameterMm)
        {
            return TryEstimateColumnRebarBarDiameterMm(GetColumnRebarMainBarTypeCombo(), out diameterMm);
        }

        private bool TryEstimateColumnRebarTieBarDiameterMm(out double diameterMm)
        {
            return TryEstimateColumnRebarBarDiameterMm(GetColumnRebarTieBarTypeCombo(), out diameterMm);
        }

        private static bool TryEstimateColumnRebarBarDiameterMm(System.Windows.Controls.ComboBox combo, out double diameterMm)
        {
            diameterMm = 0.0;
            string text = (combo?.SelectedItem as ComboItem)?.Name
                          ?? combo?.Text
                          ?? "";

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            MatchCollection matches = Regex.Matches(text, @"\d+(\.\d+)?");
            if (matches.Count == 0)
            {
                return false;
            }

            string last = matches[matches.Count - 1].Value;
            if (!double.TryParse(last, NumberStyles.Float, CultureInfo.InvariantCulture, out diameterMm) &&
                !double.TryParse(last, NumberStyles.Float, CultureInfo.CurrentCulture, out diameterMm))
            {
                return false;
            }

            return diameterMm > 0.0;
        }

        private void SyncColumnRebarTieLegTextBoxesFromSelection()
        {
            var xBox = GetColumnRebarTieInnerLegsXTextBox();
            var yBox = GetColumnRebarTieInnerLegsYTextBox();
            if (xBox != null)
            {
                string text = _columnRebarSectionSelectedTieXIndices.Count.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals((xBox.Text ?? "").Trim(), text, StringComparison.Ordinal))
                {
                    xBox.Text = text;
                }
            }

            if (yBox != null)
            {
                string text = _columnRebarSectionSelectedTieYIndices.Count.ToString(CultureInfo.InvariantCulture);
                if (!string.Equals((yBox.Text ?? "").Trim(), text, StringComparison.Ordinal))
                {
                    yBox.Text = text;
                }
            }
        }

        private static void NormalizeColumnRebarSectionSelectionSet(HashSet<int> indices, int candidateCount)
        {
            if (indices == null)
            {
                return;
            }

            candidateCount = Math.Max(0, candidateCount);
            foreach (int idx in indices.Where(i => i < 0 || i >= candidateCount).ToList())
            {
                indices.Remove(idx);
            }
        }

        private static void ApplyColumnRebarSectionSelectionFromCount(HashSet<int> target, int candidateCount, int requestedCount)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            foreach (int idx in BuildDistributedSelectionIndices(candidateCount, requestedCount))
            {
                target.Add(idx);
            }
        }

        private static HashSet<int> BuildColumnRebarTopLapBarIndexSet(IList<XYZ> points, int coveragePercent)
        {
            var result = new HashSet<int>();
            if (points == null || points.Count == 0)
            {
                return result;
            }

            coveragePercent = Math.Max(0, Math.Min(100, coveragePercent));
            if (coveragePercent <= 0)
            {
                return result;
            }

            if (coveragePercent >= 100)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    result.Add(i);
                }
                return result;
            }

            List<int> ordered = BuildColumnRebarPerimeterOrderedIndices(points);
            if (ordered.Count == 0)
            {
                return BuildColumnRebarTopLapBarIndexSet(points.Count, coveragePercent);
            }

            int requested = Math.Max(1, (int)Math.Ceiling(ordered.Count * (coveragePercent / 100.0)));
            foreach (int orderedPos in BuildDistributedSelectionIndices(ordered.Count, requested))
            {
                if (orderedPos >= 0 && orderedPos < ordered.Count)
                {
                    result.Add(ordered[orderedPos]);
                }
            }

            return result;
        }

        private static List<int> BuildColumnRebarPerimeterOrderedIndices(IList<XYZ> points)
        {
            var ordered = new List<int>();
            if (points == null || points.Count == 0)
            {
                return ordered;
            }

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ p = points[i];
                if (p == null) continue;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (double.IsNaN(minX) || double.IsInfinity(minX) ||
                double.IsNaN(maxX) || double.IsInfinity(maxX) ||
                double.IsNaN(minY) || double.IsInfinity(minY) ||
                double.IsNaN(maxY) || double.IsInfinity(maxY))
            {
                return ordered;
            }

            double tol = Math.Max(MmToFeetUi(0.5), Math.Max(maxX - minX, maxY - minY) * 1e-5);
            var used = new HashSet<int>();

            void AddRange(IEnumerable<int> indices)
            {
                foreach (int idx in indices)
                {
                    if (idx >= 0 && idx < points.Count && used.Add(idx))
                    {
                        ordered.Add(idx);
                    }
                }
            }

            AddRange(Enumerable.Range(0, points.Count)
                .Where(i => points[i] != null && Math.Abs(points[i].Y - minY) <= tol)
                .OrderBy(i => points[i].X));
            AddRange(Enumerable.Range(0, points.Count)
                .Where(i => points[i] != null && Math.Abs(points[i].X - maxX) <= tol)
                .OrderBy(i => points[i].Y));
            AddRange(Enumerable.Range(0, points.Count)
                .Where(i => points[i] != null && Math.Abs(points[i].Y - maxY) <= tol)
                .OrderByDescending(i => points[i].X));
            AddRange(Enumerable.Range(0, points.Count)
                .Where(i => points[i] != null && Math.Abs(points[i].X - minX) <= tol)
                .OrderByDescending(i => points[i].Y));
            AddRange(Enumerable.Range(0, points.Count));

            return ordered;
        }

        private static HashSet<int> BuildColumnRebarTopLapBarIndexSet(int totalCount, int coveragePercent)
        {
            totalCount = Math.Max(0, totalCount);
            coveragePercent = Math.Max(0, Math.Min(100, coveragePercent));
            var result = new HashSet<int>();
            if (totalCount <= 0)
            {
                return result;
            }

            if (coveragePercent >= 100)
            {
                for (int i = 0; i < totalCount; i++)
                {
                    result.Add(i);
                }
                return result;
            }

            if (coveragePercent <= 0)
            {
                return result;
            }

            int requested = Math.Max(1, (int)Math.Ceiling(totalCount * (coveragePercent / 100.0)));
            foreach (int idx in BuildDistributedSelectionIndices(totalCount, requested))
            {
                result.Add(idx);
            }

            return result;
        }

        private bool TryGetColumnRebarSectionNearestSnapNode(System.Windows.Point canvasPoint, out int gridXIndex, out int gridYIndex)
        {
            return TryGetColumnRebarSectionNearestSnapNode(canvasPoint, out gridXIndex, out gridYIndex, requireSnapRadius: true);
        }

        private bool TryGetColumnRebarSectionNearestSnapNode(System.Windows.Point canvasPoint, out int gridXIndex, out int gridYIndex, bool requireSnapRadius)
        {
            gridXIndex = -1;
            gridYIndex = -1;
            var state = _columnRebarSectionGridRenderState;
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

            // Larger snap radius makes point picking usable on dense grids and small canvases.
            const double snapRadiusPx = 44.0;
            if (bestX < 0 || bestY < 0)
            {
                return false;
            }

            if (requireSnapRadius && bestDist2 > snapRadiusPx * snapRadiusPx)
            {
                return false;
            }

            gridXIndex = bestX;
            gridYIndex = bestY;
            return true;
        }

        private static void NormalizeColumnRebarLineTieToOrthogonal(int startX, int startY, int endX, int endY, out int normalizedEndX, out int normalizedEndY)
        {
            normalizedEndX = endX;
            normalizedEndY = endY;

            int dx = Math.Abs(endX - startX);
            int dy = Math.Abs(endY - startY);
            if (dx == 0 || dy == 0)
            {
                return;
            }

            // TRB-like crosstie editor: line ties are axis-aligned in section.
            if (dx >= dy)
            {
                normalizedEndY = startY;
            }
            else
            {
                normalizedEndX = startX;
            }
        }

        private bool AddColumnRebarCustomLineTie(int x0, int y0, int x1, int y1)
        {
            NormalizeColumnRebarLineTieToOrthogonal(x0, y0, x1, y1, out x1, out y1);

            if (x0 == x1 && y0 == y1)
            {
                return false;
            }

            if (x0 > x1 || (x0 == x1 && y0 > y1))
            {
                (x0, x1) = (x1, x0);
                (y0, y1) = (y1, y0);
            }

            if (_columnRebarSectionCustomLineTies.Any(t =>
                t.X0GridIndex == x0 && t.Y0GridIndex == y0 &&
                t.X1GridIndex == x1 && t.Y1GridIndex == y1))
            {
                return false;
            }

            _columnRebarSectionCustomLineTies.Add(new ColumnRebarSectionLineTieUiSpec
            {
                X0GridIndex = x0,
                Y0GridIndex = y0,
                X1GridIndex = x1,
                Y1GridIndex = y1
            });
            return true;
        }

        private bool AddColumnRebarCustomRectTie(int x0, int y0, int x1, int y1)
        {
            int minX = Math.Min(x0, x1);
            int maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1);
            int maxY = Math.Max(y0, y1);
            if (minX == maxX || minY == maxY)
            {
                return false;
            }

            if (_columnRebarSectionCustomRectTies.Any(t =>
                t.X0GridIndex == minX && t.X1GridIndex == maxX &&
                t.Y0GridIndex == minY && t.Y1GridIndex == maxY))
            {
                return false;
            }

            _columnRebarSectionCustomRectTies.Add(new ColumnRebarSectionRectTieUiSpec
            {
                X0GridIndex = minX,
                X1GridIndex = maxX,
                Y0GridIndex = minY,
                Y1GridIndex = maxY
            });
            return true;
        }

        private void RenderColumnRebarSectionCustomTieShapes(System.Windows.Controls.Canvas canvas)
        {
            var state = _columnRebarSectionGridRenderState;
            if (canvas == null || state == null)
            {
                return;
            }

            for (int i = 0; i < _columnRebarSectionCustomRectTies.Count; i++)
            {
                var rect = _columnRebarSectionCustomRectTies[i];
                if (!TryGetSectionGridNodePixel(state, rect.X0GridIndex, rect.Y0GridIndex, out double x0, out double y0) ||
                    !TryGetSectionGridNodePixel(state, rect.X1GridIndex, rect.Y1GridIndex, out double x1, out double y1))
                {
                    continue;
                }

                double left = Math.Min(x0, x1);
                double top = Math.Min(y0, y1);
                double w = Math.Abs(x1 - x0);
                double h = Math.Abs(y1 - y0);
                if (w < 1 || h < 1) continue;

                var box = new System.Windows.Shapes.Rectangle
                {
                    Width = w,
                    Height = h,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(184, 74, 255)),
                    StrokeThickness = 2.0,
                    RadiusX = 6.0,
                    RadiusY = 6.0,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(8, 184, 74, 255)),
                    Tag = $"CR:{i}",
                    Cursor = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete ? Cursors.No : Cursors.Arrow,
                    IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete
                };
                box.MouseLeftButtonDown += OnColumnRebarSectionCustomTieShapeClick;
                Canvas.SetLeft(box, left);
                Canvas.SetTop(box, top);
                canvas.Children.Add(box);
            }

            for (int i = 0; i < _columnRebarSectionCustomLineTies.Count; i++)
            {
                var line = _columnRebarSectionCustomLineTies[i];
                if (!TryGetSectionGridNodePixel(state, line.X0GridIndex, line.Y0GridIndex, out double x0, out double y0) ||
                    !TryGetSectionGridNodePixel(state, line.X1GridIndex, line.Y1GridIndex, out double x1, out double y1))
                {
                    continue;
                }

                var vis = CreateSectionLine(x0, y0, x1, y1, WpfColor.FromRgb(0, 160, 255), 2.4);
                vis.Tag = $"CL:{i}";
                vis.Cursor = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete ? Cursors.No : Cursors.Arrow;
                vis.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete;
                vis.MouseLeftButtonDown += OnColumnRebarSectionCustomTieShapeClick;
                canvas.Children.Add(vis);

                var hit = CreateSectionLine(x0, y0, x1, y1, WpfColor.FromArgb(1, 0, 0, 0), 10.0);
                hit.Tag = $"CL:{i}";
                hit.Cursor = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete ? Cursors.No : Cursors.Arrow;
                hit.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Delete;
                hit.MouseLeftButtonDown += OnColumnRebarSectionCustomTieShapeClick;
                canvas.Children.Add(hit);
            }

            if (_columnRebarSectionPendingRectStartGridNode.HasValue &&
                TryGetSectionGridNodePixel(state,
                    _columnRebarSectionPendingRectStartGridNode.Value.X,
                    _columnRebarSectionPendingRectStartGridNode.Value.Y,
                    out double px,
                    out double py))
            {
                const double pendingMarkerSize = 16.0;
                var marker = new System.Windows.Shapes.Ellipse
                {
                    Width = pendingMarkerSize,
                    Height = pendingMarkerSize,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(0, 110, 255)),
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(WpfColor.FromArgb(180, 255, 255, 255)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(marker, px - (pendingMarkerSize * 0.5));
                Canvas.SetTop(marker, py - (pendingMarkerSize * 0.5));
                canvas.Children.Add(marker);
            }

            if (_columnRebarSectionPendingRectStartGridNode.HasValue &&
                _columnRebarSectionLastMouseCanvasPoint.HasValue &&
                TryGetColumnRebarSectionNearestSnapNode(
                    _columnRebarSectionLastMouseCanvasPoint.Value,
                    out int hoverX,
                    out int hoverY,
                    requireSnapRadius: !_columnRebarSectionIsLeftDrawDragging))
            {
                var (startX, startY) = _columnRebarSectionPendingRectStartGridNode.Value;
                if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie)
                {
                    NormalizeColumnRebarLineTieToOrthogonal(startX, startY, hoverX, hoverY, out hoverX, out hoverY);
                    if (TryGetSectionGridNodePixel(state, startX, startY, out double sx, out double sy) &&
                        TryGetSectionGridNodePixel(state, hoverX, hoverY, out double ex, out double ey) &&
                        !(Math.Abs(sx - ex) < 0.1 && Math.Abs(sy - ey) < 0.1))
                    {
                        var ghost = CreateSectionLine(sx, sy, ex, ey, WpfColor.FromArgb(210, 0, 160, 255), 2.4);
                        ghost.StrokeDashArray = new DoubleCollection { 4, 3 };
                        ghost.IsHitTestVisible = false;
                        canvas.Children.Add(ghost);
                    }
                }
                else if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie)
                {
                    if (TryGetSectionGridNodePixel(state, startX, startY, out double sx, out double sy) &&
                        TryGetSectionGridNodePixel(state, hoverX, hoverY, out double ex, out double ey))
                    {
                        double left = Math.Min(sx, ex);
                        double top = Math.Min(sy, ey);
                        double w = Math.Abs(ex - sx);
                        double h = Math.Abs(ey - sy);
                        if (w > 1.0 && h > 1.0)
                        {
                            var ghostRect = new System.Windows.Shapes.Rectangle
                            {
                                Width = w,
                                Height = h,
                                Stroke = new SolidColorBrush(WpfColor.FromArgb(210, 184, 74, 255)),
                                StrokeThickness = 2.0,
                                RadiusX = 6.0,
                                RadiusY = 6.0,
                                Fill = new SolidColorBrush(WpfColor.FromArgb(10, 184, 74, 255)),
                                StrokeDashArray = new DoubleCollection { 4, 3 },
                                IsHitTestVisible = false
                            };
                            Canvas.SetLeft(ghostRect, left);
                            Canvas.SetTop(ghostRect, top);
                            canvas.Children.Add(ghostRect);
                        }
                    }
                }
            }
        }

        private static bool TryParseColumnRebarTieZoneLengthMm(string text, double columnHeightMm, out double value)
        {
            value = 0.0;
            string raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (TryParseNonNegativeDoubleMm(raw, out value))
            {
                return true;
            }

            if (TryParseColumnRebarRelativeLengthExpressionMm(raw, columnHeightMm, out value))
            {
                return value >= 0.0;
            }

            string normalized = raw
                .Replace("×", "x")
                .Replace("Ã—", "x")
                .Replace("X", "x")
                .Replace("*", "x")
                .Replace("@", "x")
                .Replace(" ", "");

            int opIndex = normalized.IndexOf('x');
            if (opIndex <= 0 || opIndex >= normalized.Length - 1)
            {
                return false;
            }

            if (normalized.IndexOf('x', opIndex + 1) >= 0)
            {
                return false;
            }

            string countPart = normalized.Substring(0, opIndex);
            string lengthPart = normalized.Substring(opIndex + 1);

            if (!TryParseFlexibleDouble(countPart, out double count) ||
                !TryParseFlexibleDouble(lengthPart, out double lengthMm))
            {
                return false;
            }

            if (count < 0.0 || lengthMm < 0.0)
            {
                return false;
            }

            value = count * lengthMm;
            return value >= 0.0;
        }

        private static bool TryParseColumnRebarRelativeLengthExpressionMm(string text, double columnHeightMm, out double value)
        {
            value = 0.0;
            if (columnHeightMm <= 1e-9)
            {
                return false;
            }

            string raw = (text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            string compact = Regex.Replace(raw.ToUpperInvariant(), "\\s+", "");
            Match match = Regex.Match(
                compact,
                "^(?<factor>[0-9]+(?:[\\.,][0-9]+)?)(?<percent>%?)\\*(?<ref>L|H|COL|COLUMN|HEIGHT)$",
                RegexOptions.CultureInvariant);

            if (!match.Success)
            {
                return false;
            }

            if (!TryParseFlexibleDouble(match.Groups["factor"].Value, out double factor))
            {
                return false;
            }

            if (match.Groups["percent"].Value == "%")
            {
                factor /= 100.0;
            }

            if (factor < 0.0)
            {
                return false;
            }

            value = factor * columnHeightMm;
            return true;
        }

        private static bool TryGetColumnRebarTopLapReferenceFromPreviewContext(ColumnRebarHostPreviewData host, out double topRefFt)
        {
            topRefFt = 0.0;
            if (host == null)
            {
                return false;
            }

            double widthFt = Math.Max(0.0, host.WidthFt);
            double heightFt = Math.Max(0.0, host.HeightFt);
            double minRequiredOverlapX = Math.Max(MmToFeetUi(50.0), widthFt * 0.15);
            double bestZ = double.NegativeInfinity;

            foreach (var item in host.ElevationContextElements ?? Enumerable.Empty<ColumnRebarElevationContextElementPreview>())
            {
                if (item == null)
                {
                    continue;
                }

                string kind = (item.KindKey ?? "").Trim();
                if (!string.Equals(kind, "beam", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(kind, "slab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double x0 = Math.Min(item.X0Ft, item.X1Ft);
                double x1 = Math.Max(item.X0Ft, item.X1Ft);
                double zTop = Math.Max(item.Z0Ft, item.Z1Ft);
                if (double.IsNaN(x0) || double.IsInfinity(x0) ||
                    double.IsNaN(x1) || double.IsInfinity(x1) ||
                    double.IsNaN(zTop) || double.IsInfinity(zTop))
                {
                    continue;
                }

                double overlapX = Math.Min(widthFt, x1) - Math.Max(0.0, x0);
                if (overlapX < minRequiredOverlapX)
                {
                    continue;
                }

                if (zTop <= MmToFeetUi(1.0))
                {
                    continue;
                }

                if (zTop > heightFt + MmToFeetUi(5.0))
                {
                    continue;
                }

                if (zTop > bestZ)
                {
                    bestZ = zTop;
                }
            }

            if (double.IsNegativeInfinity(bestZ) || double.IsNaN(bestZ) || double.IsInfinity(bestZ))
            {
                return false;
            }

            topRefFt = Math.Max(0.0, Math.Min(heightFt, bestZ));
            return true;
        }

        private void RenderColumnRebarPreview(ColumnRebarHostPreviewData host, ColumnRebarPreviewInputData input, bool renderSectionPreview = true)
        {
            var previewViewport = GetColumnRebarPreviewViewport();
            if (previewViewport == null)
            {
                return;
            }

            previewViewport.Children.Clear();

            var modelGroup = new Media3D.Model3DGroup();
            modelGroup.Children.Add(new Media3D.AmbientLight(Colors.White));
            modelGroup.Children.Add(new Media3D.DirectionalLight(WpfColor.FromRgb(220, 230, 245), new Media3D.Vector3D(-1, -1, -1)));
            modelGroup.Children.Add(new Media3D.DirectionalLight(WpfColor.FromRgb(180, 200, 225), new Media3D.Vector3D(1, 1, -0.5)));

            double widthFt = host != null ? Math.Max(host.WidthFt, MmToFeetUi(100)) : MmToFeetUi(800);
            double depthFt = host != null ? Math.Max(host.DepthFt, MmToFeetUi(100)) : MmToFeetUi(800);
            double heightFt = host != null ? Math.Max(host.HeightFt, MmToFeetUi(300)) : MmToFeetUi(1500);
            double topLapFt = host != null && input != null ? Math.Max(0.0, input.TopLapLengthFt) : 0.0;

            XYZ min = new XYZ(-widthFt * 0.5, -depthFt * 0.5, 0.0);
            XYZ columnMax = new XYZ(widthFt * 0.5, depthFt * 0.5, heightFt);

            Media3D.Material columnWireMat = CreatePreviewMaterial(WpfColor.FromRgb(120, 160, 210));
            Media3D.Material barMat = CreatePreviewMaterial(WpfColor.FromRgb(0, 200, 60));
            Media3D.Material tieMat = CreatePreviewMaterial(WpfColor.FromRgb(240, 120, 40));

            double frameThicknessFt = Math.Max(Math.Min(widthFt, depthFt) * 0.003, MmToFeetUi(4));
            AddAxisAlignedPreviewWireBox(modelGroup, min, columnMax, frameThicknessFt, columnWireMat);

            if (host != null && input != null)
            {
                double verticalStartZ = min.Z;
                double defaultLapStartZ = columnMax.Z - input.TopOffsetFt;
                bool hasConnectedTopReference = TryGetColumnRebarTopLapReferenceFromPreviewContext(host, out double connectedTopReferenceFt);
                double verticalLapStartZ = hasConnectedTopReference ? connectedTopReferenceFt : defaultLapStartZ;
                verticalLapStartZ = Math.Max(verticalStartZ, Math.Min(columnMax.Z, verticalLapStartZ));
                double verticalContinuationTopZ = hasConnectedTopReference
                    ? columnMax.Z
                    : Math.Max(verticalStartZ, columnMax.Z - input.TopOffsetFt);
                double verticalTieTopZ = hasConnectedTopReference
                    ? Math.Max(verticalStartZ, Math.Min(verticalLapStartZ, verticalLapStartZ - input.TopOffsetFt))
                    : verticalContinuationTopZ;
                double verticalTopLapZ = verticalLapStartZ + topLapFt;
                List<XYZ> mainBarPreviewPoints = BuildColumnRebarPreviewBarPoints(min, columnMax, input).ToList();
                HashSet<int> lappedBarIndices = BuildColumnRebarTopLapBarIndexSet(
                    mainBarPreviewPoints,
                    input.TopLapBarCoveragePercent);
                for (int i = 0; i < mainBarPreviewPoints.Count; i++)
                {
                    XYZ p = mainBarPreviewPoints[i];
                    XYZ p0 = new XYZ(p.X, p.Y, verticalStartZ);
                    XYZ p1 = new XYZ(p.X, p.Y, verticalContinuationTopZ);
                    if (p1.Z <= p0.Z) continue;
                    AddPreviewLineBox(modelGroup, p0, p1, Math.Max(Math.Min(widthFt, depthFt) * 0.02, MmToFeetUi(12)), barMat);

                    if (!hasConnectedTopReference || topLapFt <= 1e-6 || !lappedBarIndices.Contains(i))
                    {
                        continue;
                    }

                    XYZ pLap0 = new XYZ(p.X, p.Y, verticalLapStartZ);
                    XYZ pLap1 = new XYZ(p.X, p.Y, verticalTopLapZ);
                    if (pLap1.Z <= pLap0.Z) continue;
                    AddPreviewLineBox(modelGroup, pLap0, pLap1, Math.Max(Math.Min(widthFt, depthFt) * 0.016, MmToFeetUi(10)), barMat);
                }

                if (input.CreateTies)
                {
                    double cover = Math.Max(0.0, input.CoverFt);
                    double z0 = verticalStartZ;
                    double z1 = verticalTieTopZ;
                    if (z1 > z0 + 1e-6)
                    {
                        double x0 = min.X + cover;
                        double x1 = columnMax.X - cover;
                        double y0 = min.Y + cover;
                        double y1 = columnMax.Y - cover;
                        if (x1 > x0 + 1e-6 && y1 > y0 + 1e-6)
                        {
                            List<double> tieZs = BuildColumnRebarPreviewTieZLevels(z0, z1, input);
                                if (tieZs.Count > 80)
                                {
                                    tieZs = tieZs
                                        .Where((_, idx) => idx == 0 || idx == tieZs.Count - 1 || idx % Math.Max(1, tieZs.Count / 48) == 0)
                                        .Distinct()
                                        .OrderBy(z => z)
                                        .ToList();
                                }

                            foreach (double z in tieZs)
                            {
                                double tieThk = Math.Max(Math.Min(widthFt, depthFt) * 0.006, MmToFeetUi(6));
                                AddPreviewLineBox(modelGroup, new XYZ(x0, y0, z), new XYZ(x1, y0, z), tieThk, tieMat);
                                AddPreviewLineBox(modelGroup, new XYZ(x1, y0, z), new XYZ(x1, y1, z), tieThk, tieMat);
                                AddPreviewLineBox(modelGroup, new XYZ(x1, y1, z), new XYZ(x0, y1, z), tieThk, tieMat);
                                AddPreviewLineBox(modelGroup, new XYZ(x0, y1, z), new XYZ(x0, y0, z), tieThk, tieMat);

                                foreach (double xi in BuildColumnRebarPreviewSelectedInnerLinePositions(x0, x1, input.BarsX, input.SelectedTieXIndices, input.TieInnerLegsX))
                                {
                                    AddPreviewLineBox(modelGroup, new XYZ(xi, y0, z), new XYZ(xi, y1, z), tieThk, tieMat);
                                }

                                foreach (double yi in BuildColumnRebarPreviewSelectedInnerLinePositions(y0, y1, input.BarsY, input.SelectedTieYIndices, input.TieInnerLegsY))
                                {
                                    AddPreviewLineBox(modelGroup, new XYZ(x0, yi, z), new XYZ(x1, yi, z), tieThk, tieMat);
                                }
                            }
                        }
                    }
                }
            }

            double previewLapStartZ = Math.Max(0.0, columnMax.Z - (input?.TopOffsetFt ?? 0.0));
            if (host != null && TryGetColumnRebarTopLapReferenceFromPreviewContext(host, out double connectedTopReferenceForCameraFt))
            {
                previewLapStartZ = Math.Max(0.0, Math.Min(columnMax.Z, connectedTopReferenceForCameraFt));
            }
            double previewHeightFt = Math.Max(heightFt, previewLapStartZ + (input?.TopLapLengthFt ?? 0.0));
            _columnRebarPreviewSceneWidthFt = widthFt;
            _columnRebarPreviewSceneDepthFt = depthFt;
            _columnRebarPreviewSceneHeightFt = previewHeightFt;
            previewViewport.Camera = BuildColumnRebarPreviewCamera(widthFt, depthFt, previewHeightFt);
            previewViewport.Children.Add(new Media3D.ModelVisual3D { Content = modelGroup });

            var previewInfoText = GetColumnRebarPreviewInfoText();
            if (previewInfoText != null)
            {
                if (host == null || host.HostElementId == ElementId.InvalidElementId)
                {
                    previewInfoText.Text = "Pick a structural column to preview.";
                }
                else
                {
                    string lapModeText = input?.TopLapBarDiameterMultiplier > 0
                        ? $"{input.TopLapBarDiameterMultiplier}Ã˜"
                        : "Length";
                    previewInfoText.Text =
                        $"Column #{host.HostElementId.Value} | {host.LevelName}\n" +
                        $"Size: {FeetToMmUi(host.WidthFt):0.#} x {FeetToMmUi(host.DepthFt):0.#} mm | Height: {FeetToMmUi(host.HeightFt):0.#} mm | Top Lap ({lapModeText}): {FeetToMmUi(topLapFt):0.#} mm | Lap Bars: {Math.Max(0, Math.Min(100, input?.TopLapBarCoveragePercent ?? 100))}%";
                }
            }

            if (renderSectionPreview)
            {
                RenderColumnRebarSectionPreview(host, input);
            }
        }

        private Media3D.ProjectionCamera BuildColumnRebarPreviewCamera(double widthFt, double depthFt, double heightFt)
        {
            double maxDim = Math.Max(Math.Max(widthFt, depthFt), heightFt);
            if (maxDim < 1e-6) maxDim = MmToFeetUi(1000);

            double yawRad = _columnRebarPreviewYawDeg * Math.PI / 180.0;
            double pitchRad = _columnRebarPreviewPitchDeg * Math.PI / 180.0;
            double radius = (maxDim * 2.8) / Math.Max(0.2, _columnRebarPreviewZoomFactor);
            double horizontal = Math.Cos(pitchRad);
            var center = new Media3D.Point3D(0.0, 0.0, Math.Max(heightFt * 0.5, MmToFeetUi(300)));
            var cameraPos = new Media3D.Point3D(
                center.X + radius * horizontal * Math.Cos(yawRad),
                center.Y + radius * horizontal * Math.Sin(yawRad),
                center.Z + radius * Math.Sin(pitchRad));

            var camera = new Media3D.PerspectiveCamera
            {
                Position = cameraPos,
                LookDirection = new Media3D.Vector3D(center.X - cameraPos.X, center.Y - cameraPos.Y, center.Z - cameraPos.Z),
                UpDirection = new Media3D.Vector3D(0, 0, 1),
                FieldOfView = 42
            };
            return camera;
        }

        private static IEnumerable<XYZ> BuildColumnRebarPreviewBarPoints(XYZ min, XYZ max, ColumnRebarPreviewInputData input)
        {
            double cover = Math.Max(0.0, input?.CoverFt ?? 0.0);
            int barsX = Math.Max(2, input?.BarsX ?? 2);
            int barsY = Math.Max(2, input?.BarsY ?? 2);

            double x0 = min.X + cover;
            double x1 = max.X - cover;
            double y0 = min.Y + cover;
            double y1 = max.Y - cover;
            if (x1 <= x0 || y1 <= y0)
            {
                yield break;
            }

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            double tol = MmToFeetUi(0.1);
            string Key(XYZ p) => $"{Math.Round(p.X / tol):0}|{Math.Round(p.Y / tol):0}";

            // Corners + perimeter distribution
            foreach (double x in BuildLinearPositions(x0, x1, barsX))
            {
                XYZ pBottom = new XYZ(x, y0, 0);
                if (yielded.Add(Key(pBottom))) yield return pBottom;
                XYZ pTop = new XYZ(x, y1, 0);
                if (yielded.Add(Key(pTop))) yield return pTop;
            }

            foreach (double y in BuildLinearPositions(y0, y1, barsY))
            {
                XYZ pLeft = new XYZ(x0, y, 0);
                if (yielded.Add(Key(pLeft))) yield return pLeft;
                XYZ pRight = new XYZ(x1, y, 0);
                if (yielded.Add(Key(pRight))) yield return pRight;
            }
        }

        private List<double> BuildColumnRebarPreviewTieZLevels(double z0, double z1, ColumnRebarPreviewInputData input)
        {
            var result = new List<double>();
            if (input == null || z1 <= z0 + 1e-6)
            {
                return result;
            }

            double bottomSpacing = Math.Max(input.TieSpacingBottomFt, MmToFeetUi(50.0));
            double midSpacing = Math.Max(input.TieSpacingMiddleFt, MmToFeetUi(50.0));
            double topSpacing = Math.Max(input.TieSpacingTopFt, MmToFeetUi(50.0));
            double height = z1 - z0;
            ResolveColumnRebarPreviewZoneLengths(height, input, out double bottomLen, out double middleLen, out double topLen);
            double zBottomEnd = z0 + bottomLen;
            double zMiddleEnd = zBottomEnd + middleLen;

            void AddRange(double start, double end, double spacing, bool includeEnd)
            {
                if (end < start) return;
                double tol = MmToFeetUi(0.5);
                if (spacing < tol) spacing = tol;

                if (result.Count == 0 || Math.Abs(result[result.Count - 1] - start) > tol)
                {
                    result.Add(start);
                }

                double z = start + spacing;
                int guard = 0;
                while (z < end - tol && guard < 500)
                {
                    result.Add(z);
                    z += spacing;
                    guard++;
                }

                if (includeEnd)
                {
                    if (result.Count == 0 || Math.Abs(result[result.Count - 1] - end) > tol)
                    {
                        result.Add(end);
                    }
                }
            }

            AddRange(z0, zBottomEnd, bottomSpacing, true);
            AddRange(zBottomEnd, zMiddleEnd, midSpacing, true);
            AddRange(zMiddleEnd, z1, topSpacing, true);

            return result
                .Distinct()
                .OrderBy(z => z)
                .ToList();
        }

        private static void ResolveColumnRebarPreviewZoneLengths(
            double totalHeightFt,
            ColumnRebarPreviewInputData input,
            out double bottomLenFt,
            out double middleLenFt,
            out double topLenFt)
        {
            bottomLenFt = 0.0;
            middleLenFt = 0.0;
            topLenFt = 0.0;

            if (totalHeightFt <= 1e-9)
            {
                return;
            }

            double reqB = Math.Max(0.0, input?.TieZoneBottomLengthFt ?? 0.0);
            double reqM = Math.Max(0.0, input?.TieZoneMiddleLengthFt ?? 0.0);
            double reqT = Math.Max(0.0, input?.TieZoneTopLengthFt ?? 0.0);
            bool useLengths = reqB > 1e-9 || reqM > 1e-9 || reqT > 1e-9;

            if (!useLengths)
            {
                double pB = Math.Max(0.0, input?.TieZoneBottomPercent ?? 0.0);
                double pM = Math.Max(0.0, input?.TieZoneMiddlePercent ?? 0.0);
                double pT = Math.Max(0.0, input?.TieZoneTopPercent ?? 0.0);
                double pSum = pB + pM + pT;
                if (pSum <= 1e-9)
                {
                    pB = pM = 1.0;
                    pSum = 3.0;
                }

                bottomLenFt = totalHeightFt * (pB / pSum);
                middleLenFt = totalHeightFt * (pM / pSum);
                topLenFt = Math.Max(0.0, totalHeightFt - bottomLenFt - middleLenFt);
                return;
            }

            double sumReq = reqB + reqM + reqT;
            if (sumReq > totalHeightFt + 1e-9 && sumReq > 1e-9)
            {
                double scale = totalHeightFt / sumReq;
                reqB *= scale;
                reqM *= scale;
                reqT *= scale;
                sumReq = totalHeightFt;
            }

            double remaining = Math.Max(0.0, totalHeightFt - sumReq);
            bool autoB = reqB <= 1e-9;
            bool autoM = reqM <= 1e-9;
            bool autoT = reqT <= 1e-9;
            if (!autoB && !autoM && !autoT)
            {
                bottomLenFt = reqB;
                middleLenFt = reqM;
                topLenFt = reqT;
                return;
            }

            double pBauto = autoB ? Math.Max(0.0, input?.TieZoneBottomPercent ?? 0.0) : 0.0;
            double pMauto = autoM ? Math.Max(0.0, input?.TieZoneMiddlePercent ?? 0.0) : 0.0;
            double pTauto = autoT ? Math.Max(0.0, input?.TieZoneTopPercent ?? 0.0) : 0.0;
            double pAutoSum = pBauto + pMauto + pTauto;
            if (pAutoSum <= 1e-9)
            {
                pBauto = autoB ? 1.0 : 0.0;
                pMauto = autoM ? 1.0 : 0.0;
                pTauto = autoT ? 1.0 : 0.0;
                pAutoSum = pBauto + pMauto + pTauto;
            }

            bottomLenFt = autoB ? remaining * (pBauto / pAutoSum) : reqB;
            middleLenFt = autoM ? remaining * (pMauto / pAutoSum) : reqM;
            topLenFt = autoT ? remaining * (pTauto / pAutoSum) : reqT;
        }

        private static IEnumerable<double> BuildColumnRebarInnerLinePositions(double start, double end, int innerLegCount)
        {
            innerLegCount = Math.Max(0, innerLegCount);
            if (innerLegCount <= 0 || end <= start + 1e-9)
            {
                yield break;
            }

            double step = (end - start) / (innerLegCount + 1);
            for (int i = 1; i <= innerLegCount; i++)
            {
                yield return start + (step * i);
            }
        }

        private static IEnumerable<double> BuildColumnRebarPreviewSelectedInnerLinePositions(
            double start,
            double end,
            int barCount,
            IList<int> selectedIndices,
            int fallbackInnerLegCount)
        {
            List<double> candidates = BuildColumnRebarInternalTieCandidatePositions(start, end, barCount).ToList();
            if (candidates.Count == 0)
            {
                yield break;
            }

            var emitted = new HashSet<int>();
            if (selectedIndices != null && selectedIndices.Count > 0)
            {
                foreach (int idx in selectedIndices.Where(i => i >= 0 && i < candidates.Count).OrderBy(i => i))
                {
                    if (emitted.Add(idx))
                    {
                        yield return candidates[idx];
                    }
                }

                if (emitted.Count > 0)
                {
                    yield break;
                }
            }

            foreach (double p in BuildColumnRebarInnerLinePositions(start, end, fallbackInnerLegCount))
            {
                yield return p;
            }
        }

        private void RenderColumnRebarSectionPreview(ColumnRebarHostPreviewData host, ColumnRebarPreviewInputData input)
        {
            var canvas = GetColumnRebarSectionCanvas();
            var sectionInfo = GetColumnRebarSectionInfoText();
            _columnRebarSectionGridRenderState = null;
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();

            if (host == null || host.HostElementId == ElementId.InvalidElementId || input == null)
            {
                if (sectionInfo != null)
                {
                    sectionInfo.Text = "Pick a structural column to see section and tie layout.";
                }
                return;
            }

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW < 20 || canvasH < 20)
            {
                if (sectionInfo != null)
                {
                    sectionInfo.Text =
                        $"Section: {FeetToMmUi(host.WidthFt):0.#} x {FeetToMmUi(host.DepthFt):0.#} mm | Rebar layout updates after panel resize.";
                }
                return;
            }

            double margin = 20.0;
            double drawW = Math.Max(10.0, canvasW - 2 * margin);
            double drawH = Math.Max(10.0, canvasH - 2 * margin);
            double sx = drawW / Math.Max(MmToFeetUi(10.0), host.WidthFt);
            double sy = drawH / Math.Max(MmToFeetUi(10.0), host.DepthFt);
            double scale = Math.Min(sx, sy) * Math.Max(0.2, _columnRebarSectionZoomFactor);
            double w = host.WidthFt * scale;
            double h = host.DepthFt * scale;
            double left = ((canvasW - w) * 0.5) + _columnRebarSectionPanXPx;
            double top = ((canvasH - h) * 0.5) + _columnRebarSectionPanYPx;

            NormalizeColumnRebarSectionSelectionSet(_columnRebarSectionSelectedTieXIndices, Math.Max(0, input.BarsX - 2));
            NormalizeColumnRebarSectionSelectionSet(_columnRebarSectionSelectedTieYIndices, Math.Max(0, input.BarsY - 2));
            SyncColumnRebarTieLegTextBoxesFromSelection();

            var columnRect = new System.Windows.Shapes.Rectangle
            {
                Width = w,
                Height = h,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(120, 160, 210)),
                StrokeThickness = 1.5,
                Fill = new SolidColorBrush(WpfColor.FromArgb(16, 120, 160, 210)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(columnRect, left);
            Canvas.SetTop(columnRect, top);
            canvas.Children.Add(columnRect);

            double coverFt = Math.Max(0.0, input.CoverFt);
            double estimatedMainBarDiameterFt = 0.0;
            double estimatedTieBarDiameterFt = 0.0;
            if (TryEstimateColumnRebarMainBarDiameterMm(out double mainBarDiameterMmEstimate))
            {
                estimatedMainBarDiameterFt = MmToFeetUi(mainBarDiameterMmEstimate);
            }
            if (TryEstimateColumnRebarTieBarDiameterMm(out double tieBarDiameterMmEstimate))
            {
                estimatedTieBarDiameterFt = MmToFeetUi(tieBarDiameterMmEstimate);
            }

            double tieCenterlineCoverFt = coverFt + (estimatedTieBarDiameterFt > 0.0 ? estimatedTieBarDiameterFt * 0.5 : 0.0);
            double mainBarCoverLineFt = coverFt
                                        + ((input.CreateTies && estimatedTieBarDiameterFt > 0.0) ? estimatedTieBarDiameterFt : 0.0)
                                        + (estimatedMainBarDiameterFt > 0.0 ? estimatedMainBarDiameterFt * 0.5 : 0.0);
            mainBarCoverLineFt = Math.Max(coverFt, mainBarCoverLineFt);

            double tieLeftFt = tieCenterlineCoverFt;
            double tieTopFt = tieCenterlineCoverFt;
            double tieWft = host.WidthFt - (2 * tieCenterlineCoverFt);
            double tieHft = host.DepthFt - (2 * tieCenterlineCoverFt);
            if (tieWft > MmToFeetUi(5.0) && tieHft > MmToFeetUi(5.0))
            {
                double tLeft = left + tieLeftFt * scale;
                double tTop = top + tieTopFt * scale;
                double tW = tieWft * scale;
                double tH = tieHft * scale;

                double barGridLeftFt = mainBarCoverLineFt;
                double barGridTopFt = mainBarCoverLineFt;
                double barGridWft = host.WidthFt - (2 * mainBarCoverLineFt);
                double barGridHft = host.DepthFt - (2 * mainBarCoverLineFt);
                if (barGridWft <= MmToFeetUi(1.0) || barGridHft <= MmToFeetUi(1.0))
                {
                    barGridLeftFt = coverFt;
                    barGridTopFt = coverFt;
                    barGridWft = host.WidthFt - (2 * coverFt);
                    barGridHft = host.DepthFt - (2 * coverFt);
                }

                double gLeft = left + barGridLeftFt * scale;
                double gTop = top + barGridTopFt * scale;
                double gW = barGridWft * scale;
                double gH = barGridHft * scale;
                var gridState = new ColumnRebarSectionGridRenderState
                {
                    TieLeftPx = tLeft,
                    TieTopPx = tTop,
                    TieRightPx = tLeft + tW,
                    TieBottomPx = tTop + tH
                };
                foreach (double xPx in BuildLinearPositions(gLeft, gLeft + gW, Math.Max(2, input.BarsX)))
                {
                    gridState.GridXPx.Add(xPx);
                }
                foreach (double yPx in BuildLinearPositions(gTop, gTop + gH, Math.Max(2, input.BarsY)))
                {
                    gridState.GridYPx.Add(yPx);
                }
                foreach (double xLocal in BuildLinearPositions(barGridLeftFt, barGridLeftFt + barGridWft, Math.Max(2, input.BarsX)))
                {
                    gridState.GridXLocalFt.Add(xLocal);
                }
                foreach (double yLocal in BuildLinearPositions(barGridTopFt, barGridTopFt + barGridHft, Math.Max(2, input.BarsY)))
                {
                    gridState.GridYLocalFt.Add(yLocal);
                }
                _columnRebarSectionGridRenderState = gridState;

                var stirrupRect = new System.Windows.Shapes.Rectangle
                {
                    Width = tW,
                    Height = tH,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(130, 60, 220)),
                    StrokeThickness = 2.0,
                    RadiusX = 8.0,
                    RadiusY = 8.0,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(stirrupRect, tLeft);
                Canvas.SetTop(stirrupRect, tTop);
                canvas.Children.Add(stirrupRect);

                int xIdx = 0;
                foreach (double xi in gridState.GridXPx.Skip(1).Take(Math.Max(0, gridState.GridXPx.Count - 2)))
                {
                    bool selected = _columnRebarSectionSelectedTieXIndices.Contains(xIdx);
                    bool emphasizeSelected = selected && _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    var vis = CreateSectionLine(xi, tTop, xi, tTop + tH,
                        emphasizeSelected ? WpfColor.FromRgb(235, 88, 88) : WpfColor.FromArgb(115, 140, 140, 140),
                        emphasizeSelected ? 1.8 : 1.0);
                    if (!emphasizeSelected)
                    {
                        vis.StrokeDashArray = new DoubleCollection { 2, 2 };
                    }
                    vis.Tag = $"X:{xIdx}";
                    vis.Cursor = Cursors.Hand;
                    vis.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    vis.MouseLeftButtonDown += OnColumnRebarSectionTieLineClick;
                    canvas.Children.Add(vis);

                    var hit = CreateSectionLine(xi, tTop, xi, tTop + tH, WpfColor.FromArgb(1, 0, 0, 0), 14.0);
                    hit.Tag = $"X:{xIdx}";
                    hit.Cursor = Cursors.Hand;
                    hit.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    hit.MouseLeftButtonDown += OnColumnRebarSectionTieLineClick;
                    canvas.Children.Add(hit);
                    xIdx++;
                }
                int yIdx = 0;
                foreach (double yi in gridState.GridYPx.Skip(1).Take(Math.Max(0, gridState.GridYPx.Count - 2)))
                {
                    bool selected = _columnRebarSectionSelectedTieYIndices.Contains(yIdx);
                    bool emphasizeSelected = selected && _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    var vis = CreateSectionLine(tLeft, yi, tLeft + tW, yi,
                        emphasizeSelected ? WpfColor.FromRgb(235, 88, 88) : WpfColor.FromArgb(115, 140, 140, 140),
                        emphasizeSelected ? 1.8 : 1.0);
                    if (!emphasizeSelected)
                    {
                        vis.StrokeDashArray = new DoubleCollection { 2, 2 };
                    }
                    vis.Tag = $"Y:{yIdx}";
                    vis.Cursor = Cursors.Hand;
                    vis.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    vis.MouseLeftButtonDown += OnColumnRebarSectionTieLineClick;
                    canvas.Children.Add(vis);

                    var hit = CreateSectionLine(tLeft, yi, tLeft + tW, yi, WpfColor.FromArgb(1, 0, 0, 0), 14.0);
                    hit.Tag = $"Y:{yIdx}";
                    hit.Cursor = Cursors.Hand;
                    hit.IsHitTestVisible = _columnRebarSectionEditMode == ColumnRebarSectionEditMode.Select;
                    hit.MouseLeftButtonDown += OnColumnRebarSectionTieLineClick;
                    canvas.Children.Add(hit);
                    yIdx++;
                }

                RenderColumnRebarSectionCustomTieShapes(canvas);
            }

            double barRadius = 5.0;
            foreach (XYZ p in BuildColumnRebarPreviewBarPoints(new XYZ(0, 0, 0), new XYZ(host.WidthFt, host.DepthFt, 0), new ColumnRebarPreviewInputData
            {
                CoverFt = mainBarCoverLineFt,
                BarsX = input.BarsX,
                BarsY = input.BarsY
            }))
            {
                double cx = left + p.X * scale;
                double cy = top + p.Y * scale;
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = barRadius * 2,
                    Height = barRadius * 2,
                    Fill = new SolidColorBrush(WpfColor.FromRgb(0, 200, 60)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, cx - barRadius);
                Canvas.SetTop(dot, cy - barRadius);
                canvas.Children.Add(dot);
            }

            if (_columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawLineTie ||
                _columnRebarSectionEditMode == ColumnRebarSectionEditMode.DrawRectTie)
            {
                var state = _columnRebarSectionGridRenderState;
                if (state != null)
                {
                    const double nodeHitSize = 34.0;
                    for (int ix = 0; ix < state.GridXPx.Count; ix++)
                    {
                        for (int iy = 0; iy < state.GridYPx.Count; iy++)
                        {
                            if (!TryGetSectionGridNodePixel(state, ix, iy, out double nx, out double ny))
                            {
                                continue;
                            }

                            var nodeHit = new System.Windows.Shapes.Ellipse
                            {
                                Width = nodeHitSize,
                                Height = nodeHitSize,
                                Fill = new SolidColorBrush(WpfColor.FromArgb(1, 0, 0, 0)),
                                StrokeThickness = 0,
                                Tag = $"N:{ix}:{iy}",
                                Cursor = Cursors.Cross
                            };
                            nodeHit.MouseLeftButtonDown += OnColumnRebarSectionNodeHitClick;
                            System.Windows.Controls.Panel.SetZIndex(nodeHit, 1000);
                            Canvas.SetLeft(nodeHit, nx - (nodeHitSize * 0.5));
                            Canvas.SetTop(nodeHit, ny - (nodeHitSize * 0.5));
                            canvas.Children.Add(nodeHit);
                        }
                    }
                }
            }

            if (sectionInfo != null)
            {
                string mainBarName = (GetColumnRebarMainBarTypeCombo()?.SelectedItem as ComboItem)?.Name ?? GetColumnRebarMainBarTypeCombo()?.Text ?? "-";
                string tieBarName = (GetColumnRebarTieBarTypeCombo()?.SelectedItem as ComboItem)?.Name ?? GetColumnRebarTieBarTypeCombo()?.Text ?? "-";
                string outerShapeName = (GetColumnRebarOuterTieShapeCombo()?.SelectedItem as ComboItem)?.Name ?? "-";
                string innerShapeName = (GetColumnRebarInnerTieShapeCombo()?.SelectedItem as ComboItem)?.Name ?? "-";
                sectionInfo.Text =
                    $"Section {FeetToMmUi(host.WidthFt):0.#} x {FeetToMmUi(host.DepthFt):0.#} mm | " +
                    $"Main: {mainBarName} ({input.BarsX}x{input.BarsY}) | Tie: {tieBarName} | Selected ties X={_columnRebarSectionSelectedTieXIndices.Count}, Y={_columnRebarSectionSelectedTieYIndices.Count}";
                sectionInfo.Text += $"\nOuter: {outerShapeName} | Inner: {innerShapeName}";
                if (_columnRebarSectionCustomLineTies.Count > 0 || _columnRebarSectionCustomRectTies.Count > 0)
                {
                    sectionInfo.Text += $" | Drawn ties L={_columnRebarSectionCustomLineTies.Count}, R={_columnRebarSectionCustomRectTies.Count}";
                }
            }
        }

        private void RenderColumnRebarElevationPreview(ColumnRebarHostPreviewData host, ColumnRebarPreviewInputData input)
        {
            var canvas = GetColumnRebarElevationCanvas();
            var infoText = GetColumnRebarElevationInfoText();
            _columnRebarElevationRenderState = null;
            if (canvas == null)
            {
                return;
            }

            canvas.Children.Clear();

            if (host == null || host.HostElementId == ElementId.InvalidElementId || input == null)
            {
                _columnRebarElevationHoveredTieLevelIndex = -1;
                if (infoText != null)
                {
                    infoText.Text = "Pick a structural column to preview section view.";
                }
                return;
            }

            double canvasW = canvas.ActualWidth;
            double canvasH = canvas.ActualHeight;
            if (canvasW < 20.0 || canvasH < 20.0)
            {
                _columnRebarElevationHoveredTieLevelIndex = -1;
                if (infoText != null)
                {
                    infoText.Text = "Column Section View updates after the preview tab is visible and sized.";
                }
                return;
            }

            double widthFt = Math.Max(host.WidthFt, MmToFeetUi(100.0));
            double heightFt = Math.Max(host.HeightFt, MmToFeetUi(300.0));
            double topOffsetFt = Math.Max(0.0, input.TopOffsetFt);
            double topLapFt = Math.Max(0.0, input.TopLapLengthFt);
            double defaultLapStartFt = Math.Max(0.0, heightFt - topOffsetFt);
            bool showRelatedElementsInElevationPreview = true;

            var elevationContext = (host.ElevationContextElements ?? new List<ColumnRebarElevationContextElementPreview>())
                .Where(e => e != null)
                .Select(e => new
                {
                    Item = e,
                    X0 = Math.Min(e.X0Ft, e.X1Ft),
                    X1 = Math.Max(e.X0Ft, e.X1Ft),
                    Z0 = Math.Min(e.Z0Ft, e.Z1Ft),
                    Z1 = Math.Max(e.Z0Ft, e.Z1Ft)
                })
                .Where(e => (e.X1 - e.X0) > 1e-6 && (e.Z1 - e.Z0) > 1e-6)
                .ToList();

            bool hasConnectedTopReference = TryGetColumnRebarTopLapReferenceFromPreviewContext(host, out double connectedTopReferenceFt);
            double lapStartFt = hasConnectedTopReference ? connectedTopReferenceFt : defaultLapStartFt;
            lapStartFt = Math.Max(0.0, Math.Min(heightFt, lapStartFt));
            double cageTopFt = hasConnectedTopReference
                ? Math.Max(0.0, Math.Min(lapStartFt, lapStartFt - topOffsetFt))
                : lapStartFt;
            double hostPreviewTopFt = Math.Max(Math.Max(heightFt, lapStartFt + topLapFt), MmToFeetUi(300.0));

            double contextDisplayMinXFt = 0.0;
            double contextDisplayMaxXFt = widthFt;
            double contextDisplayMinZFt = 0.0;
            double contextDisplayMaxZFt = hostPreviewTopFt;
            if (showRelatedElementsInElevationPreview)
            {
                // Short-view rule: show only a local stub of connected elements around the column.
                // Continuation is indicated by break lines on the connected elements (not on the column).
                double sideStubFt = Math.Max(MmToFeetUi(500.0), widthFt * 0.7);
                double topStubFt = Math.Max(MmToFeetUi(220.0), Math.Min(MmToFeetUi(700.0), heightFt * 0.2));
                double bottomStubFt = Math.Max(MmToFeetUi(350.0), Math.Min(MmToFeetUi(1000.0), heightFt * 0.3));

                contextDisplayMinXFt = -sideStubFt;
                contextDisplayMaxXFt = widthFt + sideStubFt;
                contextDisplayMinZFt = -bottomStubFt;
                contextDisplayMaxZFt = hostPreviewTopFt + topStubFt;
            }

            double previewMinXFt = contextDisplayMinXFt;
            double previewMaxXFt = contextDisplayMaxXFt;
            double previewMinZFt = contextDisplayMinZFt;
            double previewMaxZFt = contextDisplayMaxZFt;

            double previewPadXFt = MmToFeetUi(60.0);
            double previewPadZFt = showRelatedElementsInElevationPreview ? MmToFeetUi(80.0) : MmToFeetUi(40.0);
            previewMinXFt -= previewPadXFt;
            previewMaxXFt += previewPadXFt;
            if (showRelatedElementsInElevationPreview)
            {
                previewMinZFt -= previewPadZFt;
            }
            previewMaxZFt += previewPadZFt;

            double previewWidthFt = Math.Max(MmToFeetUi(100.0), previewMaxXFt - previewMinXFt);
            double previewHeightFt = Math.Max(MmToFeetUi(300.0), previewMaxZFt - previewMinZFt);

            double coverFt = Math.Max(0.0, input.CoverFt);
            double estimatedMainBarDiameterFt = 0.0;
            double estimatedTieBarDiameterFt = 0.0;
            if (TryEstimateColumnRebarMainBarDiameterMm(out double mainBarDiameterMm))
            {
                estimatedMainBarDiameterFt = MmToFeetUi(mainBarDiameterMm);
            }
            if (TryEstimateColumnRebarTieBarDiameterMm(out double tieBarDiameterMm))
            {
                estimatedTieBarDiameterFt = MmToFeetUi(tieBarDiameterMm);
            }

            double tieCenterlineCoverFt = coverFt + (estimatedTieBarDiameterFt > 0.0 ? estimatedTieBarDiameterFt * 0.5 : 0.0);
            double mainBarCoverLineFt = coverFt
                                        + ((input.CreateTies && estimatedTieBarDiameterFt > 0.0) ? estimatedTieBarDiameterFt : 0.0)
                                        + (estimatedMainBarDiameterFt > 0.0 ? estimatedMainBarDiameterFt * 0.5 : 0.0);
            mainBarCoverLineFt = Math.Max(coverFt, mainBarCoverLineFt);

            double tieLeftFt = tieCenterlineCoverFt;
            double tieRightFt = widthFt - tieCenterlineCoverFt;
            if (tieRightFt <= tieLeftFt + MmToFeetUi(1.0))
            {
                tieLeftFt = coverFt;
                tieRightFt = widthFt - coverFt;
            }

            double barLeftFt = mainBarCoverLineFt;
            double barRightFt = widthFt - mainBarCoverLineFt;
            if (barRightFt <= barLeftFt + MmToFeetUi(1.0))
            {
                barLeftFt = coverFt;
                barRightFt = widthFt - coverFt;
            }
            if (barRightFt <= barLeftFt + 1e-6)
            {
                barLeftFt = 0.0;
                barRightFt = widthFt;
            }

            double marginX = 26.0;
            double marginY = 16.0;
            double availableW = Math.Max(20.0, canvasW - (2.0 * marginX));
            double availableH = Math.Max(20.0, canvasH - (2.0 * marginY));
            double baseScale = Math.Min(
                availableW / Math.Max(MmToFeetUi(10.0), previewWidthFt),
                availableH / Math.Max(MmToFeetUi(50.0), previewHeightFt));
            double scale = baseScale * Math.Max(0.2, _columnRebarElevationZoomFactor);

            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0.0)
            {
                if (infoText != null)
                {
                    infoText.Text = "Column Section View: unable to compute preview scale.";
                }
                return;
            }

            double drawWidthPx = previewWidthFt * scale;
            double drawHeightPx = previewHeightFt * scale;
            double leftPx = ((canvasW - drawWidthPx) * 0.5) + _columnRebarElevationPanXPx;
            double topPx = ((canvasH - drawHeightPx) * 0.5) + _columnRebarElevationPanYPx;

            double Xpx(double xFt) => leftPx + ((xFt - previewMinXFt) * scale);
            double Ypx(double zFt) => topPx + ((previewMaxZFt - zFt) * scale);

            // Background frame for section/elevation preview.
            var frame = new System.Windows.Shapes.Rectangle
            {
                Width = drawWidthPx,
                Height = drawHeightPx,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(120, 160, 210)),
                StrokeThickness = 1.0,
                Fill = new SolidColorBrush(WpfColor.FromArgb(10, 120, 160, 210)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(frame, leftPx);
            Canvas.SetTop(frame, topPx);
            canvas.Children.Add(frame);

            // Optional connected structural context (projected from nearby Revit elements).
            if (showRelatedElementsInElevationPreview)
            {
                foreach (var ctx in elevationContext)
                {
                    bool clippedLeft = ctx.X0 < contextDisplayMinXFt;
                    bool clippedRight = ctx.X1 > contextDisplayMaxXFt;
                    bool clippedBottom = ctx.Z0 < contextDisplayMinZFt;
                    bool clippedTop = ctx.Z1 > contextDisplayMaxZFt;

                    double clippedX0Ft = Math.Max(ctx.X0, contextDisplayMinXFt);
                    double clippedX1Ft = Math.Min(ctx.X1, contextDisplayMaxXFt);
                    double clippedZ0Ft = Math.Max(ctx.Z0, contextDisplayMinZFt);
                    double clippedZ1Ft = Math.Min(ctx.Z1, contextDisplayMaxZFt);
                    if (clippedX1Ft <= clippedX0Ft + 1e-6 || clippedZ1Ft <= clippedZ0Ft + 1e-6)
                    {
                        continue;
                    }

                    double x0 = Xpx(clippedX0Ft);
                    double x1 = Xpx(clippedX1Ft);
                    double y0 = Ypx(clippedZ1Ft);
                    double y1 = Ypx(clippedZ0Ft);
                    double rw = x1 - x0;
                    double rh = y1 - y0;
                    if (rw < 1.0 || rh < 1.0)
                    {
                        continue;
                    }

                    WpfColor strokeColor;
                    WpfColor fillColor;
                    switch ((ctx.Item.KindKey ?? "").ToLowerInvariant())
                    {
                        case "foundation":
                            strokeColor = WpfColor.FromRgb(115, 83, 44);
                            fillColor = WpfColor.FromArgb(28, 176, 130, 70);
                            break;
                        case "slab":
                            strokeColor = WpfColor.FromRgb(92, 122, 160);
                            fillColor = WpfColor.FromArgb(24, 120, 170, 220);
                            break;
                        case "beam":
                            strokeColor = WpfColor.FromRgb(120, 88, 155);
                            fillColor = WpfColor.FromArgb(22, 165, 115, 220);
                            break;
                        case "wall":
                            strokeColor = WpfColor.FromRgb(90, 90, 90);
                            fillColor = WpfColor.FromArgb(18, 130, 130, 130);
                            break;
                        default:
                            strokeColor = WpfColor.FromRgb(110, 110, 110);
                            fillColor = WpfColor.FromArgb(14, 120, 120, 120);
                            break;
                    }

                    var ctxRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rw,
                        Height = rh,
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 1.0,
                        Fill = new SolidColorBrush(fillColor),
                        RadiusX = 2.0,
                        RadiusY = 2.0,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(ctxRect, x0);
                    Canvas.SetTop(ctxRect, y0);
                    canvas.Children.Add(ctxRect);

                    bool isFoundation = string.Equals(ctx.Item.KindKey, "foundation", StringComparison.OrdinalIgnoreCase);
                    bool isBeam = string.Equals(ctx.Item.KindKey, "beam", StringComparison.OrdinalIgnoreCase);
                    double breakInsetX = 3.0;
                    double breakInsetY = 2.0;
                    double sideBreakOffsetPx = 9.0;
                    if (clippedBottom && rw > 16.0)
                    {
                        if (isFoundation)
                        {
                            AddElevationFoundationBreakLine(x0 + breakInsetX, x1 - breakInsetX, y1 - breakInsetY, strokeColor);
                        }
                        else if (isBeam)
                        {
                            AddElevationBeamBreakLine(x0 + breakInsetX, x1 - breakInsetX, y1 - breakInsetY, strokeColor);
                        }
                        else
                        {
                            AddElevationBreakLine(x0 + breakInsetX, x1 - breakInsetX, y1 - breakInsetY, strokeColor);
                        }
                    }
                    if (clippedTop && rw > 16.0)
                    {
                        if (isBeam)
                        {
                            AddElevationBeamBreakLine(x0 + breakInsetX, x1 - breakInsetX, y0 + breakInsetY, strokeColor);
                        }
                        else
                        {
                            AddElevationBreakLine(x0 + breakInsetX, x1 - breakInsetX, y0 + breakInsetY, strokeColor);
                        }
                    }
                    if (clippedLeft && rh > 14.0)
                    {
                        if (isBeam)
                        {
                            AddElevationBeamEndBreakMarker(x0 + sideBreakOffsetPx, y0 + breakInsetX, y1 - breakInsetX, strokeColor, true);
                        }
                        else
                        {
                            AddElevationVerticalBreakLine(x0 + sideBreakOffsetPx, y0 + breakInsetX, y1 - breakInsetX, strokeColor);
                        }
                    }
                    if (clippedRight && rh > 14.0)
                    {
                        if (isBeam)
                        {
                            AddElevationBeamEndBreakMarker(x1 - sideBreakOffsetPx, y0 + breakInsetX, y1 - breakInsetX, strokeColor, false);
                        }
                        else
                        {
                            AddElevationVerticalBreakLine(x1 - sideBreakOffsetPx, y0 + breakInsetX, y1 - breakInsetX, strokeColor);
                        }
                    }

                    if (rw > 42.0 && rh > 14.0)
                    {
                        AddElevationLabel(ctx.Item.Label, x0 + 3.0, y0 + 2.0, strokeColor, fontSize: 9.0);
                    }
                }
            }

            // Column concrete outline (actual host height).
            double columnTopPx = Ypx(heightFt);
            double columnBottomPx = Ypx(0.0);
            double columnLeftPx = Xpx(0.0);
            double columnRightPx = Xpx(widthFt);
            var columnRect = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1.0, columnRightPx - columnLeftPx),
                Height = Math.Max(1.0, columnBottomPx - columnTopPx),
                Stroke = new SolidColorBrush(WpfColor.FromRgb(80, 80, 80)),
                StrokeThickness = 1.2,
                Fill = new SolidColorBrush(WpfColor.FromArgb(14, 110, 110, 110)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(columnRect, columnLeftPx);
            Canvas.SetTop(columnRect, columnTopPx);
            canvas.Children.Add(columnRect);

            // Rebar cage region (within column before top offset).
            double cageLeftPx = Xpx(barLeftFt);
            double cageRightPx = Xpx(barRightFt);
            double cageTopPx = Ypx(cageTopFt);
            double cageBottomPx = Ypx(0.0);
            if (cageRightPx > cageLeftPx + 1.0 && cageBottomPx > cageTopPx + 1.0)
            {
                var cageRect = new System.Windows.Shapes.Rectangle
                {
                    Width = cageRightPx - cageLeftPx,
                    Height = cageBottomPx - cageTopPx,
                    Stroke = new SolidColorBrush(WpfColor.FromRgb(70, 120, 240)),
                    StrokeThickness = 1.2,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(cageRect, cageLeftPx);
                Canvas.SetTop(cageRect, cageTopPx);
                canvas.Children.Add(cageRect);
            }

            double tieLeftPx = Xpx(tieLeftFt);
            double tieRightPx = Xpx(tieRightFt);
            double tieStrokePx = Math.Max(0.8, Math.Min(2.0, estimatedTieBarDiameterFt > 0.0 ? estimatedTieBarDiameterFt * scale * 0.35 : 1.0));
            WpfColor tieColor = WpfColor.FromRgb(240, 120, 40);
            WpfColor tieVerticalColor = WpfColor.FromRgb(235, 145, 80);
            WpfColor barColor = WpfColor.FromRgb(0, 95, 255);
            WpfColor tieHoverColor = WpfColor.FromRgb(215, 70, 70);

            ResolveColumnRebarPreviewZoneLengths(cageTopFt, input, out double zoneBottomLenFt, out double zoneMiddleLenFt, out double zoneTopLenFt);
            double zBottomEndFt = zoneBottomLenFt;
            double zMiddleEndFt = zBottomEndFt + zoneMiddleLenFt;

            void AddElevationLabel(string text, double x, double y, WpfColor color, double fontSize = 10.0, bool bold = false)
            {
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = text,
                    Foreground = new SolidColorBrush(color),
                    Background = new SolidColorBrush(WpfColor.FromArgb(170, 248, 250, 253)),
                    Padding = new Thickness(2, 0, 2, 0),
                    FontSize = fontSize,
                    FontWeight = bold ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(tb, x);
                Canvas.SetTop(tb, y);
                canvas.Children.Add(tb);
            }

            void AddElevationBreakLine(double x0, double x1, double y, WpfColor color)
            {
                if (x1 <= x0 + 8.0)
                {
                    return;
                }

                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.8,
                    IsHitTestVisible = false
                };

                const int segments = 8;
                const double amp = 4.0;
                for (int i = 0; i <= segments; i++)
                {
                    double t = (double)i / segments;
                    double x = x0 + ((x1 - x0) * t);
                    double yy = y + ((i % 2 == 0) ? -amp : amp);
                    poly.Points.Add(new System.Windows.Point(x, yy));
                }

                canvas.Children.Add(poly);
            }

            void AddElevationFoundationBreakLine(double x0, double x1, double y, WpfColor color)
            {
                if (x1 <= x0 + 12.0)
                {
                    return;
                }

                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                };

                double width = x1 - x0;
                int lobes = Math.Max(2, Math.Min(8, (int)Math.Round(width / 22.0)));
                int segments = Math.Max(12, lobes * 8);
                const double amp = 4.5;
                for (int i = 0; i <= segments; i++)
                {
                    double t = (double)i / segments;
                    double x = x0 + (width * t);
                    double theta = t * lobes * Math.PI;
                    double yy = y + (Math.Abs(Math.Sin(theta)) * amp);
                    poly.Points.Add(new System.Windows.Point(x, yy));
                }

                canvas.Children.Add(poly);
            }

            void AddElevationBeamBreakLine(double x0, double x1, double y, WpfColor color)
            {
                if (x1 <= x0 + 16.0)
                {
                    return;
                }

                double width = x1 - x0;
                double notchWidth = Math.Max(14.0, Math.Min(28.0, width * 0.18));
                double notchDepth = 5.0;
                double cx = (x0 + x1) * 0.5;
                double n0 = Math.Max(x0 + 4.0, cx - (notchWidth * 0.5));
                double n1 = Math.Min(x1 - 4.0, cx + (notchWidth * 0.5));
                if (n1 <= n0 + 6.0)
                {
                    AddElevationBreakLine(x0, x1, y, color);
                    return;
                }

                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                };

                poly.Points.Add(new System.Windows.Point(x0, y));
                poly.Points.Add(new System.Windows.Point(n0, y));
                poly.Points.Add(new System.Windows.Point(n0 + ((n1 - n0) * 0.25), y + notchDepth));
                poly.Points.Add(new System.Windows.Point(n0 + ((n1 - n0) * 0.50), y));
                poly.Points.Add(new System.Windows.Point(n0 + ((n1 - n0) * 0.75), y + notchDepth));
                poly.Points.Add(new System.Windows.Point(n1, y));
                poly.Points.Add(new System.Windows.Point(x1, y));
                canvas.Children.Add(poly);
            }

            void AddElevationVerticalBreakLine(double x, double y0, double y1, WpfColor color)
            {
                if (y1 <= y0 + 10.0)
                {
                    return;
                }

                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1.6,
                    IsHitTestVisible = false
                };

                double height = y1 - y0;
                int zigs = Math.Max(3, Math.Min(9, (int)Math.Round(height / 22.0)));
                const double amp = 4.6;

                // Drafting-style vertical break line (zig-zag), intentionally offset from the clipped edge.
                for (int i = 0; i <= zigs; i++)
                {
                    double t = (double)i / zigs;
                    double y = y0 + (height * t);
                    double xx;
                    if (i == 0 || i == zigs)
                    {
                        xx = x;
                    }
                    else
                    {
                        xx = x + ((i % 2 == 0) ? -amp : amp);
                    }
                    poly.Points.Add(new System.Windows.Point(xx, y));
                }

                canvas.Children.Add(poly);
            }

            void AddElevationBeamEndBreakMarker(double xEdge, double y0, double y1, WpfColor color, bool leftEdge)
            {
                if (y1 <= y0 + 10.0)
                {
                    return;
                }

                double h = y1 - y0;
                double inset = Math.Max(2.0, Math.Min(6.0, h * 0.08));
                double xA = leftEdge ? xEdge + inset : xEdge - inset;
                double xB = leftEdge ? xEdge + (inset * 2.4) : xEdge - (inset * 2.4);

                var line1 = CreateSectionLine(xA, y0, xA, y1, color, 1.2);
                line1.IsHitTestVisible = false;
                canvas.Children.Add(line1);

                var line2 = CreateSectionLine(xB, y0, xB, y1, color, 1.2);
                line2.IsHitTestVisible = false;
                canvas.Children.Add(line2);

                double midY = (y0 + y1) * 0.5;
                double tickLen = Math.Max(4.0, Math.Min(9.0, h * 0.12));
                double xTick0 = leftEdge ? xB : xA;
                double xTick1 = leftEdge ? (xB + tickLen) : (xA - tickLen);
                var tick = CreateSectionLine(xTick0, midY, xTick1, midY, color, 1.1);
                tick.IsHitTestVisible = false;
                canvas.Children.Add(tick);
            }

            // Tie vertical legs shown in elevation (outer + selected/internal X lines).
            if (input.CreateTies && cageTopFt > 1e-6 && tieRightFt > tieLeftFt + 1e-6)
            {
                _columnRebarElevationRenderState = new ColumnRebarElevationRenderState
                {
                    TieLeftPx = tieLeftPx,
                    TieRightPx = tieRightPx
                };

                var projectedTieXs = new List<double> { tieLeftFt, tieRightFt };
                projectedTieXs.AddRange(BuildColumnRebarPreviewSelectedInnerLinePositions(
                    tieLeftFt,
                    tieRightFt,
                    input.BarsX,
                    input.SelectedTieXIndices,
                    input.TieInnerLegsX));

                foreach (double xFt in projectedTieXs.Distinct().OrderBy(x => x))
                {
                    double x = Xpx(xFt);
                    var vLine = CreateSectionLine(x, cageTopPx, x, cageBottomPx, tieVerticalColor, Math.Max(0.8, tieStrokePx * 0.9));
                    vLine.IsHitTestVisible = false;
                    canvas.Children.Add(vLine);
                }

                // Zone boundary lines and labels (Bottom / Middle / Top).
                if (cageTopFt > 1e-6)
                {
                    double zoneLabelX = tieRightPx + 8.0;
                    if (zoneLabelX > canvasW - 120.0)
                    {
                        zoneLabelX = Math.Max(4.0, leftPx + 6.0);
                    }

                    var zoneBoundaryYs = new List<double>();
                    if (zBottomEndFt > 1e-6 && zBottomEndFt < cageTopFt - 1e-6) zoneBoundaryYs.Add(Ypx(zBottomEndFt));
                    if (zMiddleEndFt > 1e-6 && zMiddleEndFt < cageTopFt - 1e-6) zoneBoundaryYs.Add(Ypx(zMiddleEndFt));
                    foreach (double by in zoneBoundaryYs.Distinct().OrderBy(v => v))
                    {
                        var bLine = CreateSectionLine(leftPx, by, leftPx + drawWidthPx, by, WpfColor.FromArgb(135, 110, 110, 110), 1.0);
                        bLine.StrokeDashArray = new DoubleCollection { 3, 3 };
                        bLine.IsHitTestVisible = false;
                        canvas.Children.Add(bLine);
                    }

                    double zBottomMidFt = zBottomEndFt * 0.5;
                    double zMiddleMidFt = (zBottomEndFt + zMiddleEndFt) * 0.5;
                    double zTopMidFt = (zMiddleEndFt + cageTopFt) * 0.5;
                    AddElevationLabel(
                        $"BOTTOM {FeetToMmUi(zoneBottomLenFt):0.#} @{FeetToMmUi(input.TieSpacingBottomFt):0.#}",
                        zoneLabelX,
                        Ypx(zBottomMidFt) - 7.0,
                        WpfColor.FromRgb(76, 96, 128));
                    AddElevationLabel(
                        $"MIDDLE {FeetToMmUi(zoneMiddleLenFt):0.#} @{FeetToMmUi(input.TieSpacingMiddleFt):0.#}",
                        zoneLabelX,
                        Ypx(zMiddleMidFt) - 7.0,
                        WpfColor.FromRgb(76, 96, 128));
                    AddElevationLabel(
                        $"TOP {FeetToMmUi(zoneTopLenFt):0.#} @{FeetToMmUi(input.TieSpacingTopFt):0.#}",
                        zoneLabelX,
                        Ypx(zTopMidFt) - 7.0,
                        WpfColor.FromRgb(76, 96, 128));

                    if (topLapFt > 1e-6)
                    {
                        AddElevationLabel(
                            $"LAP {FeetToMmUi(topLapFt):0.#} ({Math.Max(0, Math.Min(100, input.TopLapBarCoveragePercent))}%)",
                            zoneLabelX,
                            Ypx(Math.Min(previewMaxZFt, lapStartFt + (topLapFt * 0.5))) - 7.0,
                            WpfColor.FromRgb(150, 78, 78),
                            fontSize: 10.0,
                            bold: true);
                    }
                }

                List<double> tieZs = BuildColumnRebarPreviewTieZLevels(0.0, cageTopFt, input);
                if (tieZs.Count > 180)
                {
                    int keepEvery = Math.Max(2, (int)Math.Ceiling(tieZs.Count / 120.0));
                    tieZs = tieZs
                        .Where((_, i) => i == 0 || i == tieZs.Count - 1 || (i % keepEvery) == 0)
                        .Distinct()
                        .OrderBy(z => z)
                        .ToList();
                }

                if (_columnRebarElevationHoveredTieLevelIndex >= tieZs.Count)
                {
                    _columnRebarElevationHoveredTieLevelIndex = -1;
                }

                for (int i = 0; i < tieZs.Count; i++)
                {
                    double zFt = tieZs[i];
                    double y = Ypx(zFt);
                    _columnRebarElevationRenderState.TieLevelYPx.Add(y);
                    _columnRebarElevationRenderState.TieLevelZFt.Add(zFt);

                    bool isHovered = i == _columnRebarElevationHoveredTieLevelIndex;
                    if (isHovered)
                    {
                        var band = new System.Windows.Shapes.Rectangle
                        {
                            Width = Math.Max(1.0, tieRightPx - tieLeftPx),
                            Height = 8.0,
                            Fill = new SolidColorBrush(WpfColor.FromArgb(44, tieHoverColor.R, tieHoverColor.G, tieHoverColor.B)),
                            StrokeThickness = 0,
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(band, tieLeftPx);
                        Canvas.SetTop(band, y - 4.0);
                        canvas.Children.Add(band);
                    }

                    var hLine = CreateSectionLine(
                        tieLeftPx,
                        y,
                        tieRightPx,
                        y,
                        isHovered ? tieHoverColor : tieColor,
                        isHovered ? Math.Max(tieStrokePx + 0.8, 2.1) : tieStrokePx);
                    hLine.IsHitTestVisible = false;
                    canvas.Children.Add(hLine);

                    if (isHovered)
                    {
                        double labelX = tieRightPx + 8.0;
                        if (labelX > canvasW - 140.0)
                        {
                            labelX = Math.Max(4.0, tieLeftPx + 6.0);
                        }
                        AddElevationLabel(
                            $"Tie L{i + 1} @ {FeetToMmUi(zFt):0.#} mm",
                            labelX,
                            y - 9.0,
                            tieHoverColor,
                            fontSize: 10.0,
                            bold: true);
                    }
                }
            }
            else
            {
                _columnRebarElevationHoveredTieLevelIndex = -1;
            }

            // Main vertical bars (projected along column width).
            List<double> barXsFt = BuildLinearPositions(barLeftFt, barRightFt, Math.Max(2, input.BarsX)).ToList();
            double barTopWithLapFt = Math.Max(0.0, lapStartFt + topLapFt);
            double continuationBarTopFt = hasConnectedTopReference ? heightFt : lapStartFt;
            double barStrokePx = Math.Max(1.2, Math.Min(3.2, estimatedMainBarDiameterFt > 0.0 ? estimatedMainBarDiameterFt * scale * 0.55 : 1.4));
            HashSet<int> lappedProjectedBarIndices = BuildColumnRebarTopLapBarIndexSet(barXsFt.Count, input.TopLapBarCoveragePercent);
            for (int i = 0; i < barXsFt.Count; i++)
            {
                double xFt = barXsFt[i];
                double x = Xpx(xFt);
                double barTopFt = continuationBarTopFt;
                var barLine = CreateSectionLine(x, Ypx(barTopFt), x, Ypx(0.0), barColor, barStrokePx);
                barLine.IsHitTestVisible = false;
                canvas.Children.Add(barLine);

                if (!hasConnectedTopReference || topLapFt <= 1e-6 || !lappedProjectedBarIndices.Contains(i))
                {
                    continue;
                }

                // Slight horizontal offset so the additional end/lap bar condition is visible in schematic section view.
                double offsetPx = (i % 2 == 0) ? -2.2 : 2.2;
                double xLap = Math.Max(columnLeftPx + 2.0, Math.Min(columnRightPx - 2.0, x + offsetPx));
                var lapBarLine = CreateSectionLine(xLap, Ypx(barTopWithLapFt), xLap, Ypx(lapStartFt), barColor, Math.Max(1.0, barStrokePx * 0.9));
                lapBarLine.Opacity = 0.9;
                lapBarLine.IsHitTestVisible = false;
                canvas.Children.Add(lapBarLine);
            }

            // Top of tied zone / top-lap start indicator.
            if (lapStartFt > 1e-6 && lapStartFt < previewMaxZFt - 1e-6)
            {
                var lapStart = CreateSectionLine(leftPx, Ypx(lapStartFt), leftPx + drawWidthPx, Ypx(lapStartFt), WpfColor.FromRgb(120, 120, 120), 1.0);
                lapStart.StrokeDashArray = new DoubleCollection { 4, 3 };
                lapStart.IsHitTestVisible = false;
                canvas.Children.Add(lapStart);
            }

            // Simple right-side level markers (top/bottom of column).
            double markerX0 = leftPx + drawWidthPx + 6.0;
            double markerX1 = markerX0 + 26.0;
            if (markerX1 <= canvasW - 2.0)
            {
                foreach (double y in new[] { Ypx(heightFt), Ypx(0.0) })
                {
                    var tick = CreateSectionLine(markerX0, y, markerX1, y, WpfColor.FromRgb(70, 70, 70), 1.1);
                    tick.IsHitTestVisible = false;
                    canvas.Children.Add(tick);
                }
            }

            // Column remains continuous; short-view break indicators are applied on truncated context elements only.

            if (infoText != null)
            {
                int tieLevelCount = 0;
                if (input.CreateTies && cageTopFt > 1e-6)
                {
                    tieLevelCount = BuildColumnRebarPreviewTieZLevels(0.0, cageTopFt, input).Count;
                }

                string lapModeText = input.TopLapBarDiameterMultiplier > 0
                    ? $"{input.TopLapBarDiameterMultiplier}Ã˜"
                    : "Length";
                infoText.Text =
                    $"Section View: {FeetToMmUi(host.WidthFt):0.#} x {FeetToMmUi(host.DepthFt):0.#} mm | Height: {FeetToMmUi(host.HeightFt):0.#} mm | " +
                    $"Ties: {tieLevelCount} levels | B/M/T L= {FeetToMmUi(zoneBottomLenFt):0.#}/{FeetToMmUi(zoneMiddleLenFt):0.#}/{FeetToMmUi(zoneTopLenFt):0.#} mm | Top Lap ({lapModeText}): {FeetToMmUi(topLapFt):0.#} mm ({Math.Max(0, Math.Min(100, input.TopLapBarCoveragePercent))}%)";
                if (_columnRebarElevationRenderState != null &&
                    _columnRebarElevationHoveredTieLevelIndex >= 0 &&
                    _columnRebarElevationHoveredTieLevelIndex < _columnRebarElevationRenderState.TieLevelZFt.Count)
                {
                    infoText.Text += $" | Hover Tie @ {FeetToMmUi(_columnRebarElevationRenderState.TieLevelZFt[_columnRebarElevationHoveredTieLevelIndex]):0.#} mm";
                }
                if (showRelatedElementsInElevationPreview && elevationContext.Count > 0)
                {
                    int slabCount = elevationContext.Count(e => string.Equals(e.Item.KindKey, "slab", StringComparison.OrdinalIgnoreCase));
                    int beamCount = elevationContext.Count(e => string.Equals(e.Item.KindKey, "beam", StringComparison.OrdinalIgnoreCase));
                    int wallCount = elevationContext.Count(e => string.Equals(e.Item.KindKey, "wall", StringComparison.OrdinalIgnoreCase));
                    int foundationCount = elevationContext.Count(e => string.Equals(e.Item.KindKey, "foundation", StringComparison.OrdinalIgnoreCase));
                    infoText.Text += $" | Ctx S{slabCount} B{beamCount} W{wallCount} F{foundationCount}";
                }
                infoText.Text += $" | Zoom {Math.Round(_columnRebarElevationZoomFactor, 2):0.##}x";
            }
        }

        private static IEnumerable<double> BuildColumnRebarInternalTieCandidatePositions(double start, double end, int barCount)
        {
            barCount = Math.Max(2, barCount);
            int idx = 0;
            foreach (double p in BuildLinearPositions(start, end, barCount))
            {
                if (idx > 0 && idx < (barCount - 1))
                {
                    yield return p;
                }
                idx++;
            }
        }

            private sealed class FoundationRebarPreviewUiData
            {
                public string HostText { get; set; } = "";
                public string TypeMode { get; set; } = "";
                public string ScopeMode { get; set; } = "";
                public string MainBarType { get; set; } = "";
                public string DistributionBarType { get; set; } = "";
                public string LinkBarType { get; set; } = "";
                public double LengthMm { get; set; }
                public double WidthMm { get; set; }
                public double ThicknessMm { get; set; }
                public double PedestalHeightMm { get; set; }
                public double PedestalWidthMm { get; set; }
                public double PedestalDepthMm { get; set; }
                public double TopCoverMm { get; set; }
                public double BottomCoverMm { get; set; }
                public double SideCoverMm { get; set; }
                public double TopSpacingXMm { get; set; }
                public double TopSpacingYMm { get; set; }
                public double BottomSpacingXMm { get; set; }
                public double BottomSpacingYMm { get; set; }
                public double BottomTieSpacingMm { get; set; }
                public double MidTieSpacingMm { get; set; }
                public double TopTieSpacingMm { get; set; }
                public string TopLapExpr { get; set; } = "";
                public string BottomLapExpr { get; set; } = "";
                public string BottomZoneExpr { get; set; } = "";
                public string MidZoneExpr { get; set; } = "";
                public string TopZoneExpr { get; set; } = "";
                public string SideLapExpr { get; set; } = "";
                public bool IncludeStarterBars { get; set; }
                public bool CreateLinks { get; set; }
                public bool CreateTopBars { get; set; }
                public bool CreateBottomBars { get; set; }
                public bool CreateTopBarsX { get; set; }
                public bool CreateTopBarsY { get; set; }
                public bool CreateBottomBarsX { get; set; }
                public bool CreateBottomBarsY { get; set; }
                public bool ShowDimensions { get; set; }
                public bool ShowBarMarkers { get; set; }
                public int EstimatedTopBarsX { get; set; }
                public int EstimatedTopBarsY { get; set; }
                public int EstimatedBottomBarsX { get; set; }
                public int EstimatedBottomBarsY { get; set; }
                public int EstimatedTieLevels { get; set; }
            }

        public sealed class FoundationRebarHostPreviewData
        {
            public ElementId HostElementId { get; set; } = ElementId.InvalidElementId;
            public ElementId HostTypeElementId { get; set; } = ElementId.InvalidElementId;
            public string HostDisplayName { get; set; } = "";
            public string LevelName { get; set; } = "";
            public string SuggestedTypeMode { get; set; } = "";
            public double LengthFt { get; set; }
            public double WidthFt { get; set; }
            public double ThicknessFt { get; set; }
            public bool HasDetectedTopElementGeometry { get; set; }
            public string TopElementDisplayName { get; set; } = "";
            public double TopElementHeightFt { get; set; }
            public double TopElementWidthFt { get; set; }
            public double TopElementDepthFt { get; set; }
            public Autodesk.Revit.DB.Transform HostTransform { get; set; } = Autodesk.Revit.DB.Transform.Identity;
            public XYZ LocalMin { get; set; } = XYZ.Zero;
            public XYZ LocalMax { get; set; } = XYZ.Zero;
        }

        public sealed class ColumnRebarHostPreviewData
        {
            public ElementId HostElementId { get; set; } = ElementId.InvalidElementId;
            public string HostDisplayName { get; set; } = "";
            public string LevelName { get; set; } = "";
            public double WidthFt { get; set; }
            public double DepthFt { get; set; }
            public double HeightFt { get; set; }
            public Autodesk.Revit.DB.Transform HostTransform { get; set; } = Autodesk.Revit.DB.Transform.Identity;
            public XYZ LocalMin { get; set; } = XYZ.Zero;
            public XYZ LocalMax { get; set; } = XYZ.Zero;
            public List<ColumnRebarElevationContextElementPreview> ElevationContextElements { get; } =
                new List<ColumnRebarElevationContextElementPreview>();
        }

        public sealed class ColumnRebarElevationContextElementPreview
        {
            public ElementId ElementId { get; set; } = ElementId.InvalidElementId;
            public string KindKey { get; set; } = "";
            public string Label { get; set; } = "";
            public double X0Ft { get; set; }
            public double X1Ft { get; set; }
            public double Z0Ft { get; set; }
            public double Z1Ft { get; set; }
        }

        private sealed class ColumnRebarPreviewInputData
        {
            public double CoverFt { get; set; }
            public double TieSpacingBottomFt { get; set; }
            public double TieSpacingMiddleFt { get; set; }
            public double TieSpacingTopFt { get; set; }
            public double TieZoneBottomLengthFt { get; set; }
            public double TieZoneMiddleLengthFt { get; set; }
            public double TieZoneTopLengthFt { get; set; }
            public double TieZoneBottomPercent { get; set; }
            public double TieZoneMiddlePercent { get; set; }
            public double TieZoneTopPercent { get; set; }
            public double BottomOffsetFt { get; set; }
            public double TopOffsetFt { get; set; }
            public double TopLapLengthFt { get; set; }
            public int TopLapBarDiameterMultiplier { get; set; }
            public int TopLapBarCoveragePercent { get; set; } = 100;
            public int BarsX { get; set; }
            public int BarsY { get; set; }
            public int TieInnerLegsX { get; set; }
            public int TieInnerLegsY { get; set; }
            public List<int> SelectedTieXIndices { get; set; } = new List<int>();
            public List<int> SelectedTieYIndices { get; set; } = new List<int>();
            public bool CreateTies { get; set; }
        }

        private enum ColumnRebarSectionEditMode
        {
            Select = 0,
            DrawLineTie = 1,
            DrawRectTie = 2,
            Delete = 3
        }

        private sealed class ColumnRebarSectionLineTieUiSpec
        {
            public int X0GridIndex { get; set; }
            public int Y0GridIndex { get; set; }
            public int X1GridIndex { get; set; }
            public int Y1GridIndex { get; set; }
        }

        private sealed class ColumnRebarSectionRectTieUiSpec
        {
            public int X0GridIndex { get; set; }
            public int X1GridIndex { get; set; }
            public int Y0GridIndex { get; set; }
            public int Y1GridIndex { get; set; }
        }

        private sealed class ColumnRebarSectionGridRenderState
        {
            public List<double> GridXPx { get; } = new List<double>();
            public List<double> GridYPx { get; } = new List<double>();
            public List<double> GridXLocalFt { get; } = new List<double>();
            public List<double> GridYLocalFt { get; } = new List<double>();
            public double TieLeftPx { get; set; }
            public double TieTopPx { get; set; }
            public double TieRightPx { get; set; }
            public double TieBottomPx { get; set; }
        }

        private sealed class ColumnRebarElevationRenderState
        {
            public List<double> TieLevelYPx { get; } = new List<double>();
            public List<double> TieLevelZFt { get; } = new List<double>();
            public double TieLeftPx { get; set; }
            public double TieRightPx { get; set; }
        }
        private FoundationRebarHostPreviewData _foundationRebarHostPreview;
        private ColumnRebarHostPreviewData _columnRebarHostPreview;
        private double _columnRebarPreviewYawDeg = 135.0;
        private double _columnRebarPreviewPitchDeg = 22.0;
        private double _columnRebarPreviewZoomFactor = 1.0;
        private double _columnRebarPreviewSceneWidthFt;
        private double _columnRebarPreviewSceneDepthFt;
        private double _columnRebarPreviewSceneHeightFt;
        private bool _columnRebarPreviewIsDragging;
        private System.Windows.Point _columnRebarPreviewLastMousePoint;
        private double _columnRebarSectionZoomFactor = 1.0;
        private double _columnRebarSectionPanXPx = 0.0;
        private double _columnRebarSectionPanYPx = 0.0;
        private bool _columnRebarSectionIsPanning;
        private System.Windows.Point _columnRebarSectionLastMousePoint;
        private bool _columnRebarSectionIsLeftDrawDragging;
        private System.Windows.Point _columnRebarSectionLeftDrawStartCanvasPoint;
        private double _columnRebarElevationZoomFactor = 1.0;
        private double _columnRebarElevationPanXPx = 0.0;
        private double _columnRebarElevationPanYPx = 0.0;
        private bool _columnRebarElevationIsPanning;
        private System.Windows.Point _columnRebarElevationLastMousePoint;
        private int _columnRebarElevationHoveredTieLevelIndex = -1;
        private ColumnRebarElevationRenderState _columnRebarElevationRenderState;
        private bool _columnRebarTieShapeBrowserSyncing;
        private readonly HashSet<int> _columnRebarSectionSelectedTieXIndices = new HashSet<int>();
        private readonly HashSet<int> _columnRebarSectionSelectedTieYIndices = new HashSet<int>();
        private ColumnRebarSectionEditMode _columnRebarSectionEditMode = ColumnRebarSectionEditMode.Select;
        private readonly List<ColumnRebarSectionLineTieUiSpec> _columnRebarSectionCustomLineTies = new List<ColumnRebarSectionLineTieUiSpec>();
        private readonly List<ColumnRebarSectionRectTieUiSpec> _columnRebarSectionCustomRectTies = new List<ColumnRebarSectionRectTieUiSpec>();
        private ColumnRebarSectionGridRenderState _columnRebarSectionGridRenderState;
        private (int X, int Y)? _columnRebarSectionPendingRectStartGridNode;
        private System.Windows.Point? _columnRebarSectionLastMouseCanvasPoint;
        private bool _foundationRebarUiEventsSuspended;
        private bool _foundationRebarPreviewDirty = true;
        private bool _foundationRebarPreviewHasRendered;
        private DateTime _foundationRebarLastPreviewUtc = DateTime.MinValue;

        private sealed class FoundationRebarQuickNotationSpec
        {
            public string Label { get; set; }
            public string RawText { get; set; }
            public bool HasValue { get; set; }
            public bool IsCountMode { get; set; }
            public int Count { get; set; }
            public double DiameterMm { get; set; }
            public double SpacingMm { get; set; }
        }
    }
}

