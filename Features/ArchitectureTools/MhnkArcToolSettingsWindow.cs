using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcToolSettings
    {
        public MhnkArcToolSettings()
        {
            WallLayerKeywords = "A-WALL,WALL,ARC-WALL";
            FloorLayerKeywords = "A-FLOR,FLOOR,SLAB";
            CeilingLayerKeywords = "A-CLNG,CEILING,RCP";
            RoomBoundaryLayerKeywords = "A-AREA,A-ROOM,ROOM,BOUNDARY";
            OpeningLayerKeywords = "OPENING,VOID,SLEEVE,HATCH";
            DoorLayerKeywords = "A-DOOR,DOOR,D-";
            WindowLayerKeywords = "A-WIND,WINDOW,W-";
            DoorTypeKeywords = "Door,D-";
            WindowTypeKeywords = "Window,W-";
            PreferredWallTypeKeywords = "Generic,Basic,Wall";
            WallHeightMeters = 3.0;
            OpeningDepthMeters = 0.30;
            CopyOffsetMeters = 1.0;
            ClashToleranceMillimeters = 10.0;
        }

        public string WallLayerKeywords { get; set; }
        public string FloorLayerKeywords { get; set; }
        public string CeilingLayerKeywords { get; set; }
        public string RoomBoundaryLayerKeywords { get; set; }
        public string OpeningLayerKeywords { get; set; }
        public string DoorLayerKeywords { get; set; }
        public string WindowLayerKeywords { get; set; }
        public string DoorTypeKeywords { get; set; }
        public string WindowTypeKeywords { get; set; }
        public string PreferredWallTypeKeywords { get; set; }
        public double WallHeightMeters { get; set; }
        public double OpeningDepthMeters { get; set; }
        public double CopyOffsetMeters { get; set; }
        public double ClashToleranceMillimeters { get; set; }

        public static MhnkArcToolSettings Load()
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new MhnkArcToolSettings();
            }

            try
            {
                MhnkArcToolSettings settings = CamboBimJson.Deserialize<MhnkArcToolSettings>(File.ReadAllText(path));
                return settings == null ? new MhnkArcToolSettings() : settings.Normalize();
            }
            catch
            {
                return new MhnkArcToolSettings();
            }
        }

        public void Save()
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, CamboBimJson.Serialize(Normalize()));
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "settings.json");
        }

        public IList<string> SplitKeywords(string value)
        {
            var keywords = new List<string>();
            foreach (string item in (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string keyword = item.Trim();
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    keywords.Add(keyword);
                }
            }

            return keywords;
        }

        public MhnkArcToolSettings Normalize()
        {
            if (string.IsNullOrWhiteSpace(WallLayerKeywords)) WallLayerKeywords = "A-WALL,WALL,ARC-WALL";
            if (string.IsNullOrWhiteSpace(FloorLayerKeywords)) FloorLayerKeywords = "A-FLOR,FLOOR,SLAB";
            if (string.IsNullOrWhiteSpace(CeilingLayerKeywords)) CeilingLayerKeywords = "A-CLNG,CEILING,RCP";
            if (string.IsNullOrWhiteSpace(RoomBoundaryLayerKeywords)) RoomBoundaryLayerKeywords = "A-AREA,A-ROOM,ROOM,BOUNDARY";
            if (string.IsNullOrWhiteSpace(OpeningLayerKeywords)) OpeningLayerKeywords = "OPENING,VOID,SLEEVE,HATCH";
            if (string.IsNullOrWhiteSpace(DoorLayerKeywords)) DoorLayerKeywords = "A-DOOR,DOOR,D-";
            if (string.IsNullOrWhiteSpace(WindowLayerKeywords)) WindowLayerKeywords = "A-WIND,WINDOW,W-";
            if (string.IsNullOrWhiteSpace(DoorTypeKeywords)) DoorTypeKeywords = "Door,D-";
            if (string.IsNullOrWhiteSpace(WindowTypeKeywords)) WindowTypeKeywords = "Window,W-";
            if (string.IsNullOrWhiteSpace(PreferredWallTypeKeywords)) PreferredWallTypeKeywords = "Generic,Basic,Wall";
            WallHeightMeters = Clamp(WallHeightMeters, 0.10, 100.0, 3.0);
            OpeningDepthMeters = Clamp(OpeningDepthMeters, 0.01, 20.0, 0.30);
            CopyOffsetMeters = Clamp(CopyOffsetMeters, 0.01, 100.0, 1.0);
            ClashToleranceMillimeters = Clamp(ClashToleranceMillimeters, 0.0, 1000.0, 10.0);
            return this;
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return fallback;
            }

            return Math.Max(min, Math.Min(max, value));
        }
    }

    internal sealed class MhnkArcToolSettingsWindow : Window
    {
        private readonly MhnkArcToolSettings _settings;
        private readonly Dictionary<string, TextBox> _textBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);

        public MhnkArcToolSettingsWindow(MhnkArcToolSettings settings, IntPtr revitMainWindowHandle)
        {
            _settings = (settings ?? new MhnkArcToolSettings()).Normalize();

            Title = "MHNK ARC Tool Settings";
            Width = 760;
            Height = 620;
            MinWidth = 680;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;

            if (revitMainWindowHandle != IntPtr.Zero)
            {
                new WindowInteropHelper(this).Owner = revitMainWindowHandle;
            }

            Grid root = new Grid();
            root.Margin = new Thickness(14);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            TextBlock header = new TextBlock
            {
                Text = "MHNK ARC SETTINGS",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 44, 58)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            ScrollViewer scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(scroll);
            Grid.SetRow(scroll, 1);

            Grid form = new Grid();
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scroll.Content = form;

            int row = 0;
            AddText(form, ref row, "Wall CAD layers", "WallLayerKeywords", _settings.WallLayerKeywords);
            AddText(form, ref row, "Floor CAD layers", "FloorLayerKeywords", _settings.FloorLayerKeywords);
            AddText(form, ref row, "Ceiling CAD layers", "CeilingLayerKeywords", _settings.CeilingLayerKeywords);
            AddText(form, ref row, "Room boundary layers", "RoomBoundaryLayerKeywords", _settings.RoomBoundaryLayerKeywords);
            AddText(form, ref row, "Opening layers", "OpeningLayerKeywords", _settings.OpeningLayerKeywords);
            AddText(form, ref row, "Door layers", "DoorLayerKeywords", _settings.DoorLayerKeywords);
            AddText(form, ref row, "Window layers", "WindowLayerKeywords", _settings.WindowLayerKeywords);
            AddText(form, ref row, "Door type keywords", "DoorTypeKeywords", _settings.DoorTypeKeywords);
            AddText(form, ref row, "Window type keywords", "WindowTypeKeywords", _settings.WindowTypeKeywords);
            AddText(form, ref row, "Wall type keywords", "PreferredWallTypeKeywords", _settings.PreferredWallTypeKeywords);
            AddText(form, ref row, "Wall height m", "WallHeightMeters", Format(_settings.WallHeightMeters));
            AddText(form, ref row, "Opening depth m", "OpeningDepthMeters", Format(_settings.OpeningDepthMeters));
            AddText(form, ref row, "Copy offset m", "CopyOffsetMeters", Format(_settings.CopyOffsetMeters));
            AddText(form, ref row, "Clash tolerance mm", "ClashToleranceMillimeters", Format(_settings.ClashToleranceMillimeters));

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 2);

            Button saveButton = new Button
            {
                Content = "Save",
                Width = 96,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            saveButton.Click += (_, __) => SaveAndClose();
            buttons.Children.Add(saveButton);

            Button cancelButton = new Button
            {
                Content = "Cancel",
                Width = 96,
                Height = 30
            };
            cancelButton.Click += (_, __) => Close();
            buttons.Children.Add(cancelButton);
            MhnkUiTheme.Apply(this);
        }

        public MhnkArcToolSettings Settings
        {
            get { return _settings; }
        }

        private void AddText(Grid form, ref int row, string label, string key, string value)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock labelBlock = new TextBlock
            {
                Text = label,
                Margin = new Thickness(0, 4, 12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72))
            };
            form.Children.Add(labelBlock);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            TextBox textBox = new TextBox
            {
                Text = value ?? "",
                MinHeight = 28,
                Margin = new Thickness(0, 4, 0, 4),
                Padding = new Thickness(6, 3, 6, 3),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            form.Children.Add(textBox);
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            _textBoxes[key] = textBox;

            row++;
        }

        private void SaveAndClose()
        {
            _settings.WallLayerKeywords = Text("WallLayerKeywords");
            _settings.FloorLayerKeywords = Text("FloorLayerKeywords");
            _settings.CeilingLayerKeywords = Text("CeilingLayerKeywords");
            _settings.RoomBoundaryLayerKeywords = Text("RoomBoundaryLayerKeywords");
            _settings.OpeningLayerKeywords = Text("OpeningLayerKeywords");
            _settings.DoorLayerKeywords = Text("DoorLayerKeywords");
            _settings.WindowLayerKeywords = Text("WindowLayerKeywords");
            _settings.DoorTypeKeywords = Text("DoorTypeKeywords");
            _settings.WindowTypeKeywords = Text("WindowTypeKeywords");
            _settings.PreferredWallTypeKeywords = Text("PreferredWallTypeKeywords");
            _settings.WallHeightMeters = Number("WallHeightMeters", _settings.WallHeightMeters);
            _settings.OpeningDepthMeters = Number("OpeningDepthMeters", _settings.OpeningDepthMeters);
            _settings.CopyOffsetMeters = Number("CopyOffsetMeters", _settings.CopyOffsetMeters);
            _settings.ClashToleranceMillimeters = Number("ClashToleranceMillimeters", _settings.ClashToleranceMillimeters);
            _settings.Normalize().Save();
            DialogResult = true;
            Close();
        }

        private string Text(string key)
        {
            TextBox textBox;
            return _textBoxes.TryGetValue(key, out textBox) ? textBox.Text.Trim() : "";
        }

        private double Number(string key, double fallback)
        {
            double value;
            return double.TryParse(Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(Text(key), NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                ? value
                : fallback;
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
