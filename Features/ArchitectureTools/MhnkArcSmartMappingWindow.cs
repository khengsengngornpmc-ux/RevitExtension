using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class MhnkArcSmartMappingRule
    {
        public MhnkArcSmartMappingRule()
        {
            Enabled = true;
            RuleName = "New Rule";
            LayerKeywords = "";
            Action = "Wall";
            RevitTypeKeywords = "";
            HeightMeters = 0.0;
            DepthMeters = 0.0;
            OffsetMeters = 0.0;
            Notes = "";
        }

        public bool Enabled { get; set; }
        public string RuleName { get; set; }
        public string LayerKeywords { get; set; }
        public string Action { get; set; }
        public string RevitTypeKeywords { get; set; }
        public double HeightMeters { get; set; }
        public double DepthMeters { get; set; }
        public double OffsetMeters { get; set; }
        public string Notes { get; set; }

        public MhnkArcSmartMappingRule Clone()
        {
            return new MhnkArcSmartMappingRule
            {
                Enabled = Enabled,
                RuleName = RuleName,
                LayerKeywords = LayerKeywords,
                Action = Action,
                RevitTypeKeywords = RevitTypeKeywords,
                HeightMeters = HeightMeters,
                DepthMeters = DepthMeters,
                OffsetMeters = OffsetMeters,
                Notes = Notes
            };
        }

        public MhnkArcSmartMappingRule Normalize()
        {
            RuleName = string.IsNullOrWhiteSpace(RuleName) ? "Unnamed Rule" : RuleName.Trim();
            LayerKeywords = (LayerKeywords ?? "").Trim();
            Action = MhnkArcSmartMappingRules.NormalizeAction(Action);
            RevitTypeKeywords = (RevitTypeKeywords ?? "").Trim();
            HeightMeters = ClampOptional(HeightMeters, 0.0, 100.0);
            DepthMeters = ClampOptional(DepthMeters, 0.0, 20.0);
            OffsetMeters = ClampOptional(OffsetMeters, -100.0, 100.0);
            Notes = (Notes ?? "").Trim();
            return this;
        }

        private static double ClampOptional(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0.0;
            }

            return Math.Max(min, Math.Min(max, value));
        }
    }

    internal sealed class MhnkArcSmartMappingRules
    {
        public MhnkArcSmartMappingRules()
        {
            Rules = new List<MhnkArcSmartMappingRule>();
        }

        public List<MhnkArcSmartMappingRule> Rules { get; set; }

        public static MhnkArcSmartMappingRules Load()
        {
            string path = GetRulesPath();
            if (!File.Exists(path))
            {
                return FromRules(CreateDefaultRules());
            }

            try
            {
                MhnkArcSmartMappingRules rules = CamboBimJson.Deserialize<MhnkArcSmartMappingRules>(File.ReadAllText(path));
                return rules == null ? FromRules(CreateDefaultRules()) : rules.Normalize();
            }
            catch
            {
                return FromRules(CreateDefaultRules());
            }
        }

        public void Save()
        {
            string path = GetRulesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, CamboBimJson.Serialize(Normalize()));
        }

        public MhnkArcSmartMappingRules Normalize()
        {
            if (Rules == null)
            {
                Rules = new List<MhnkArcSmartMappingRule>();
            }

            Rules = Rules
                .Where(x => x != null)
                .Select(x => x.Normalize())
                .ToList();

            if (Rules.Count == 0)
            {
                Rules = CreateDefaultRules().Select(x => x.Clone()).ToList();
            }

            return this;
        }

        public static string GetRulesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "mapping-rules.json");
        }

        public static string GetExportFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "Exports");
        }

        public static IList<MhnkArcSmartMappingRule> CreateDefaultRules()
        {
            return new List<MhnkArcSmartMappingRule>
            {
                Rule("Walls - CAD Lines", "A-WALL,WALL,ARC-WALL", "Wall", "Generic,Basic,Wall", 3.0, 0.0, 0.0, "Creates basic Revit walls."),
                Rule("Floors - CAD Loops", "A-FLOR,FLOOR,SLAB", "Floor", "Floor,Slab,Generic", 0.0, 0.0, 0.0, "Creates Revit floors from closed loops."),
                Rule("Ceilings - CAD Loops", "A-CLNG,CEILING,RCP", "Ceiling", "Ceiling,Generic", 0.0, 0.0, 0.0, "Creates Revit ceilings from closed loops."),
                Rule("Rooms - CAD Loops", "A-ROOM,ROOM", "Room", "", 0.0, 0.0, 0.0, "Places rooms at loop centers."),
                Rule("Room Boundaries - CAD Lines", "A-AREA,A-ROOM,ROOM,BOUNDARY", "RoomBoundary", "", 0.0, 0.0, 0.0, "Creates room separation lines."),
                Rule("Openings - CAD Loops", "OPENING,VOID,SLEEVE,HATCH", "Opening", "Opening,Void,Sleeve", 0.0, 0.30, 0.0, "Creates opening candidate solids."),
                Rule("Doors - CAD Points/Lines", "A-DOOR,DOOR,D-", "Door", "Door,D-", 0.0, 0.0, 0.0, "Places loaded door symbols."),
                Rule("Windows - CAD Points/Lines", "A-WIND,WINDOW,W-", "Window", "Window,W-", 0.0, 0.0, 0.0, "Places loaded window symbols.")
            };
        }

        public static IList<string> GetActions()
        {
            return new List<string>
            {
                "Wall",
                "Floor",
                "Ceiling",
                "Room",
                "RoomBoundary",
                "Opening",
                "Door",
                "Window"
            };
        }

        public static string NormalizeAction(string action)
        {
            string value = (action ?? "").Trim();
            foreach (string known in GetActions())
            {
                if (string.Equals(value, known, StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }

            return "Wall";
        }

        public static IList<string> SplitKeywords(string value)
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

        private static MhnkArcSmartMappingRules FromRules(IList<MhnkArcSmartMappingRule> rules)
        {
            return new MhnkArcSmartMappingRules
            {
                Rules = (rules ?? new List<MhnkArcSmartMappingRule>()).Select(x => x.Clone()).ToList()
            }.Normalize();
        }

        private static MhnkArcSmartMappingRule Rule(
            string name,
            string layerKeywords,
            string action,
            string typeKeywords,
            double heightMeters,
            double depthMeters,
            double offsetMeters,
            string notes)
        {
            return new MhnkArcSmartMappingRule
            {
                Enabled = true,
                RuleName = name,
                LayerKeywords = layerKeywords,
                Action = action,
                RevitTypeKeywords = typeKeywords,
                HeightMeters = heightMeters,
                DepthMeters = depthMeters,
                OffsetMeters = offsetMeters,
                Notes = notes
            }.Normalize();
        }
    }

    internal sealed class MhnkArcSmartMappingWindow : Window
    {
        private readonly ObservableCollection<MhnkArcSmartMappingRule> _rules;
        private readonly DataGrid _grid;

        public MhnkArcSmartMappingWindow(MhnkArcSmartMappingRules rules, IntPtr revitMainWindowHandle)
        {
            MhnkArcSmartMappingRules normalized = (rules ?? MhnkArcSmartMappingRules.Load()).Normalize();
            _rules = new ObservableCollection<MhnkArcSmartMappingRule>(normalized.Rules.Select(x => x.Clone()));

            Title = "MHNK Smart Mapping Manager";
            Width = 1180;
            Height = 660;
            MinWidth = 920;
            MinHeight = 540;
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
                Text = "MHNK SMART MAPPING MANAGER",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(32, 44, 58)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(header);
            Grid.SetRow(header, 0);

            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                ItemsSource = _rules,
                SelectionMode = DataGridSelectionMode.Extended,
                Margin = new Thickness(0, 0, 0, 10)
            };
            AddCheckColumn(_grid, "On", "Enabled", 46);
            AddTextColumn(_grid, "Rule", "RuleName", 170);
            AddTextColumn(_grid, "Layer Keywords", "LayerKeywords", 220);
            AddActionColumn(_grid);
            AddTextColumn(_grid, "Type Keywords", "RevitTypeKeywords", 170);
            AddTextColumn(_grid, "Height m", "HeightMeters", 82);
            AddTextColumn(_grid, "Depth m", "DepthMeters", 82);
            AddTextColumn(_grid, "Offset m", "OffsetMeters", 82);
            AddTextColumn(_grid, "Notes", "Notes", 240);
            root.Children.Add(_grid);
            Grid.SetRow(_grid, 1);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            root.Children.Add(buttons);
            Grid.SetRow(buttons, 2);

            AddButton(buttons, "Add Default", 112, (_, __) => AddDefaultRules());
            AddButton(buttons, "Add Rule", 96, (_, __) => AddRule());
            AddButton(buttons, "Delete", 84, (_, __) => DeleteSelected());
            AddButton(buttons, "Import", 90, (_, __) => ImportRules());
            AddButton(buttons, "Export", 90, (_, __) => ExportRules());
            AddButton(buttons, "Save", 96, (_, __) => SaveAndClose());
            AddButton(buttons, "Cancel", 96, (_, __) => Close());
            MhnkUiTheme.Apply(this);
        }

        private static void AddCheckColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = header,
                Binding = new Binding(binding),
                Width = width
            });
        }

        private static void AddTextColumn(DataGrid grid, string header, string binding, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(binding) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = width
            });
        }

        private static void AddActionColumn(DataGrid grid)
        {
            grid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Action",
                ItemsSource = MhnkArcSmartMappingRules.GetActions(),
                SelectedItemBinding = new Binding("Action") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 118
            });
        }

        private static void AddButton(StackPanel panel, string text, double width, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = text,
                Width = width,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0)
            };
            button.Click += handler;
            panel.Children.Add(button);
        }

        private void AddDefaultRules()
        {
            foreach (MhnkArcSmartMappingRule rule in MhnkArcSmartMappingRules.CreateDefaultRules())
            {
                _rules.Add(rule.Clone());
            }
        }

        private void AddRule()
        {
            MhnkArcSmartMappingRule rule = new MhnkArcSmartMappingRule();
            _rules.Add(rule);
            _grid.SelectedItem = rule;
            _grid.ScrollIntoView(rule);
        }

        private void DeleteSelected()
        {
            IList<MhnkArcSmartMappingRule> selected = _grid.SelectedItems.Cast<MhnkArcSmartMappingRule>().ToList();
            foreach (MhnkArcSmartMappingRule rule in selected)
            {
                _rules.Remove(rule);
            }
        }

        private void ImportRules()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import MHNK Mapping Rules",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                MhnkArcSmartMappingRules imported = CamboBimJson.Deserialize<MhnkArcSmartMappingRules>(File.ReadAllText(dialog.FileName));
                imported = imported == null ? new MhnkArcSmartMappingRules() : imported.Normalize();
                _rules.Clear();
                foreach (MhnkArcSmartMappingRule rule in imported.Rules)
                {
                    _rules.Add(rule.Clone());
                }

                MessageBox.Show(this, "Imported " + _rules.Count + " mapping rule(s).", "MHNK Xpress", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not import mapping rules." + Environment.NewLine + ex.Message, "MHNK Xpress", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportRules()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            string folder = MhnkArcSmartMappingRules.GetExportFolder();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "mapping-rules_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json");

            var rules = new MhnkArcSmartMappingRules
            {
                Rules = _rules.Select(x => x.Clone().Normalize()).ToList()
            };
            File.WriteAllText(path, CamboBimJson.Serialize(rules.Normalize()));
            MessageBox.Show(this, "Exported mapping rules:" + Environment.NewLine + path, "MHNK Xpress", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveAndClose()
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);

            var rules = new MhnkArcSmartMappingRules
            {
                Rules = _rules.Select(x => x.Clone().Normalize()).ToList()
            };
            rules.Save();
            DialogResult = true;
            Close();
        }
    }
}
