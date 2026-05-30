using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.ComponentModel;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CamboBIM.Revit2024.Addin
{
    public partial class BoredPileToolWindow : Window
    {
        private readonly BoredPileToolExternalEventHandler _handler;
        private readonly ExternalEvent _externalEvent;

        public BoredPileToolWindow(IntPtr revitMainWindowHandle)
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MHNK", $"BoredPileToolWindow failed to load XAML:\n{ex}");
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = "Failed to load Bored Pile tool UI. See error dialog.",
                    Margin = new Thickness(12)
                };
                return;
            }

            if (DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }

            new WindowInteropHelper(this)
            {
                Owner = revitMainWindowHandle
            };

            _handler = new BoredPileToolExternalEventHandler();
            _handler.SetWindow(this);
            _externalEvent = ExternalEvent.Create(_handler);

            LayerCombo.IsEnabled = false;

            RequestInitialize();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnPickImportClick(object sender, RoutedEventArgs e)
        {
            _handler.Request.RequestType = BoredPileRequestType.PickImport;
            _externalEvent.Raise();
        }

        private void OnPickObjectsClick(object sender, RoutedEventArgs e)
        {
            if (LayerModeRadio.IsChecked == true)
            {
                ShowStatus("Switch to Select Objects to pick CAD geometry.");
                return;
            }

            _handler.Request.RequestType = BoredPileRequestType.PickObjects;
            _externalEvent.Raise();
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            if (!TryUpdateRequest())
            {
                return;
            }

            _handler.Request.RequestType = BoredPileRequestType.Generate;
            _externalEvent.Raise();
        }

        private void OnSelectionModeChanged(object sender, RoutedEventArgs e)
        {
            LayerCombo.IsEnabled = LayerModeRadio.IsChecked == true;
        }

        private void RequestInitialize()
        {
            _handler.Request.RequestType = BoredPileRequestType.Initialize;
            _externalEvent.Raise();
        }

        private bool TryUpdateRequest()
        {
            if (BoredPileCombo.SelectedItem is ComboItem bored &&
                SpunPileCombo.SelectedItem is ComboItem spun &&
                SheetPileCombo.SelectedItem is ComboItem sheet &&
                BaseLevelCombo.SelectedItem is ComboItem baseLevel)
            {
                _handler.Request.BoredPileTypeId = bored.Id;
                _handler.Request.SpunPileTypeId = spun.Id;
                _handler.Request.SheetPileTypeId = sheet.Id;
                _handler.Request.BaseLevelId = baseLevel.Id;
            }
            else
            {
                ShowStatus("Select pile families and base level.");
                return false;
            }

            _handler.Request.UseLayerFilter = LayerModeRadio.IsChecked == true;
            _handler.Request.SelectedLayerName = LayerCombo.SelectedItem as string ?? "";

            return true;
        }

        public void UpdateImportName(string name, List<string> layers)
        {
            ImportNameText.Text = string.IsNullOrWhiteSpace(name) ? "(not selected)" : name;
            LayerCombo.ItemsSource = layers;
            if (layers.Count > 0)
            {
                LayerCombo.SelectedIndex = 0;
            }
        }

        public void UpdateFoundationTypes(List<ComboItem> items)
        {
            BoredPileCombo.ItemsSource = items;
            SpunPileCombo.ItemsSource = items;
            SheetPileCombo.ItemsSource = items;

            if (items.Count > 0)
            {
                BoredPileCombo.SelectedIndex = 0;
                SpunPileCombo.SelectedIndex = 0;
                SheetPileCombo.SelectedIndex = 0;
            }
        }

        public void UpdateLevels(List<ComboItem> items)
        {
            BaseLevelCombo.ItemsSource = items;
            if (items.Count > 0)
            {
                BaseLevelCombo.SelectedIndex = 0;
            }
        }

        public void ShowStatus(string message)
        {
            StatusText.Text = message ?? "";
        }

        public class ComboItem
        {
            public ElementId Id { get; }
            public string Name { get; }

            public ComboItem(ElementId id, string name)
            {
                Id = id;
                Name = name;
            }

            public override string ToString() => Name;
        }
    }
}
