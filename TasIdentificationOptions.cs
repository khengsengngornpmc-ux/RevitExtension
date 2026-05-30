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
using System.Xml.Linq;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class TasIdentificationOptionsProfile
    {
        private const string SettingsFileName = "identification-options.json";

        public List<TasIdentificationOptionItem> Items { get; set; } = new List<TasIdentificationOptionItem>();

        public static TasIdentificationOptionsProfile Load(TasReferenceSettings tasReference)
        {
            TasIdentificationOptionsProfile defaults = CreateDefault(tasReference);
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return defaults;
            }

            try
            {
                TasIdentificationOptionsProfile stored =
                    CamboBimJson.Deserialize<TasIdentificationOptionsProfile>(File.ReadAllText(path, Encoding.UTF8));
                if (stored == null || stored.Items == null || stored.Items.Count == 0)
                {
                    return defaults;
                }

                Dictionary<string, TasIdentificationOptionItem> byKey = stored.Items
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Key))
                    .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                foreach (TasIdentificationOptionItem item in defaults.Items)
                {
                    TasIdentificationOptionItem storedItem;
                    if (item != null &&
                        !item.IsGroup &&
                        !string.IsNullOrWhiteSpace(item.Key) &&
                        byKey.TryGetValue(item.Key, out storedItem))
                    {
                        item.Value = storedItem.Value ?? item.Value;
                    }
                }
            }
            catch
            {
                return defaults;
            }

            return defaults;
        }

        public void Save()
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "");
            File.WriteAllText(path, CamboBimJson.Serialize(this), new UTF8Encoding(false));
        }

        public TasIdentificationOptionsProfile Clone()
        {
            return new TasIdentificationOptionsProfile
            {
                Items = Items == null
                    ? new List<TasIdentificationOptionItem>()
                    : Items.Select(item => item == null ? null : item.Clone()).Where(item => item != null).ToList()
            };
        }

        public string GetValue(string key, string fallback = "")
        {
            if (Items == null || string.IsNullOrWhiteSpace(key))
            {
                return fallback ?? "";
            }

            TasIdentificationOptionItem item = Items.FirstOrDefault(row =>
                row != null &&
                !row.IsGroup &&
                string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(item?.Value) ? (fallback ?? "") : item.Value.Trim();
        }

        public double GetDouble(string key, double fallback)
        {
            string text = GetValue(key, "");
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        public string BuildWorkflowSummary()
        {
            string unit = GetValue("CADMinUnit", "mm");
            string lineMerge = GetValue("LineMergeSettings", "400");
            string correction = GetValue("LineErrorSettingType", "Auto Mode");
            return "Unit " + unit + ", line merge " + lineMerge + " mm, correction " + correction;
        }

        public string BuildSummaryForTool(string toolName)
        {
            if (string.Equals(toolName, "Column", StringComparison.OrdinalIgnoreCase))
            {
                return "Column code " + JoinNonEmpty(GetValue("CADKZCode", "KZ,C"), GetValue("CADAZCode", "AZ")) +
                       ", max sideline " + GetValue("MaxColumnSidelineLen", "1800") + " mm";
            }

            if (string.Equals(toolName, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                return "Wall code " + GetValue("CADWallCode", "WS,W") +
                       ", thickness tolerance " + GetValue("CADWallWidthTol", "5") + " mm";
            }

            if (string.Equals(toolName, "Beam", StringComparison.OrdinalIgnoreCase))
            {
                return "Beam code " + GetValue("CADKLCode", "KL,B,G,CG,CB,FB,P,E,D") +
                       ", size " + GetValue("CADBeamSizeIdentifyMode", "Width * Height");
            }

            if (string.Equals(toolName, "Slab", StringComparison.OrdinalIgnoreCase))
            {
                return "Slab code " + GetValue("CADSlabCode", "S") +
                       ", default thickness " + GetValue("CADDefSlabThickness", "120") + " mm";
            }

            if (string.Equals(toolName, "Foundation", StringComparison.OrdinalIgnoreCase))
            {
                return "Pile cap " + GetValue("CADPileCapCode", "CT,PC,F,P") +
                       ", pad " + GetValue("CADIsolatedFDCode", "J,I,F,P");
            }

            return BuildWorkflowSummary();
        }

        public static TasIdentificationOptionsProfile CreateDefault(TasReferenceSettings tasReference)
        {
            var profile = new TasIdentificationOptionsProfile();
            List<TasIdentificationOptionItem> sourceRows = TryReadTasCadOptionRows(tasReference);
            if (sourceRows.Count == 0)
            {
                sourceRows = BuildFallbackRows();
            }

            Dictionary<string, string> defaults = BuildDefaultValueMap();
            foreach (TasIdentificationOptionItem row in sourceRows)
            {
                if (row == null)
                {
                    continue;
                }

                string value;
                if (!row.IsGroup && defaults.TryGetValue(row.Key ?? "", out value))
                {
                    row.DefaultValue = value;
                    row.Value = value;
                }

                profile.Items.Add(row);
            }

            return profile;
        }

        private static List<TasIdentificationOptionItem> TryReadTasCadOptionRows(TasReferenceSettings tasReference)
        {
            var rows = new List<TasIdentificationOptionItem>();
            try
            {
                string bin = tasReference == null ? "" : tasReference.BinPath;
                if (string.IsNullOrWhiteSpace(bin))
                {
                    bin = Path.GetDirectoryName(TasReferenceSettings.DefaultTasExePath) ?? "";
                }

                string path = Path.Combine(bin, "Config", "CADOptions", "CADOption.xml");
                if (!File.Exists(path))
                {
                    return rows;
                }

                XDocument document = XDocument.Load(path);
                foreach (XElement element in document.Descendants("CADOptionRowNode"))
                {
                    string key = ((string)element.Attribute("GSPField") ?? "").Trim();
                    string label = NormalizeAttributeLabel((string)element.Attribute("TextCol0") ?? "");
                    int rowNumber = ParseInt((string)element.Attribute("RowNum"), rows.Count);
                    bool isGroup = string.IsNullOrWhiteSpace(key);
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    rows.Add(new TasIdentificationOptionItem
                    {
                        Key = isGroup ? "__group_" + rowNumber.ToString(CultureInfo.InvariantCulture) : key,
                        Group = isGroup ? label : "",
                        Attribute = label,
                        Value = "",
                        DefaultValue = "",
                        IsGroup = isGroup,
                        Order = rowNumber
                    });
                }
            }
            catch
            {
                rows.Clear();
            }

            return rows.OrderBy(row => row.Order).ToList();
        }

        private static List<TasIdentificationOptionItem> BuildFallbackRows()
        {
            string[] groups =
            {
                "Wall Setting",
                "Door & Window Opening Setting",
                "Beam Setting",
                "Column Setting",
                "Slab Setting",
                "Pad Foundation Setting",
                "Pile Cap Setting",
                "Pile Setting",
                "Steel Structure Setting",
                "Font Settings of CAD Text",
                "Unit Setting",
                "Line Merge Settings",
                "Correction for Line Length Errors"
            };

            var rows = new List<TasIdentificationOptionItem>();
            int order = 0;
            foreach (string group in groups)
            {
                rows.Add(new TasIdentificationOptionItem
                {
                    Key = "__fallback_group_" + order.ToString(CultureInfo.InvariantCulture),
                    Attribute = group,
                    Group = group,
                    IsGroup = true,
                    Order = order++
                });
            }

            foreach (KeyValuePair<string, string> pair in BuildDefaultValueMap())
            {
                rows.Add(new TasIdentificationOptionItem
                {
                    Key = pair.Key,
                    Attribute = pair.Key,
                    Value = pair.Value,
                    DefaultValue = pair.Value,
                    Order = order++
                });
            }

            return rows;
        }

        private static Dictionary<string, string> BuildDefaultValueMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "CADWallCode", "WS,W" },
                { "CADWallWidthTol", "5" },
                { "CADWallIntExtTol", "100" },
                { "CADWallIntWinHor", "100" },
                { "CADWallIntWinVer", "100" },
                { "CADWallExtTolWithoutColumn", "10" },
                { "MaxOpeningWidth", "500" },
                { "CADMaxThickness", "600" },
                { "CADDoorCode", "M,D" },
                { "CADWindowCode", "C,W" },
                { "CADOpeningCode", "O" },
                { "CADDoorAboveFloorHeight", "0" },
                { "CADWinAboveFloorHeight", "900" },
                { "CADOpeningAboveFloorHeight", "600" },
                { "CADBeamSupportExtend", "200" },
                { "CADBeamOutLineExtendLen", "80" },
                { "CADNoLabelBeamMaxWidth", "1000" },
                { "CADKLCode", "KL,B,G,CG,CB,FB,P,E,D" },
                { "CADKZLCode", "KZL" },
                { "CADLCode", "L,J" },
                { "CADJZLCode", "JZL" },
                { "CADFZLCode", "FG" },
                { "CADFCLCode", "JCL" },
                { "CADLLCode", "LL" },
                { "CADBeamSizeIdentifyMode", "Width * Height" },
                { "CADBeamEndExtendMode", "Extend into column/wall/foundation" },
                { "CADBeamAbsoluteElevMode", "Read elevation value as absolute value" },
                { "CADBeamDefaultHeight", "500" },
                { "CADKZCode", "KZ,C" },
                { "CADAZCode", "AZ" },
                { "CADDZCode", "DZ" },
                { "CADKZZCode", "KZZ" },
                { "CADGZCode", "GZ" },
                { "MaxColumnSidelineLen", "1800" },
                { "ShortToLongEdgeRatio", "4" },
                { "CADSlabCode", "S" },
                { "CADDefSlabThickness", "120" },
                { "CADIsolatedFDHeight", "500" },
                { "CADIsolatedFDCode", "J,I,F,P" },
                { "CADPileCapHeight", "500" },
                { "CADPileCapCode", "CT,PC,F,P" },
                { "CADPileHeight", "3000" },
                { "CADPileCode", "ZJ,WKZ,P" },
                { "CADSCCode", "GZ,SC,C,K" },
                { "CADSBCode", "GL,SB,B,SG,G,R,K" },
                { "CADSBEndsExtendLength", "500" },
                { "CADSBMinSpace", "1000" },
                { "CADSBCorrectionDis", "30" },
                { "CADSBMinLength", "650" },
                { "CADTextHeightRate", "0.6" },
                { "CADMinUnit", "mm" },
                { "LineMergeSettings", "400" },
                { "LineErrorSettingType", "Auto Mode" },
                { "RoundToUp", "7;8;9" },
                { "RoundToDown", "1;2;3" },
                { "RoundToMid", "4;5;6" }
            };
        }

        private static string NormalizeAttributeLabel(string text)
        {
            return (text ?? "")
                .Replace("%UnitNoTrans%", "mm")
                .Replace("(mm)", "(mm)")
                .Trim();
        }

        private static int ParseInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static string JoinNonEmpty(params string[] values)
        {
            return string.Join(",", (values ?? new string[0]).Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MHNK", "RevitExtension", "CAD2MODEL", SettingsFileName);
        }
    }

    internal sealed class TasIdentificationOptionItem
    {
        public string Key { get; set; } = "";
        public string Group { get; set; } = "";
        public string Attribute { get; set; } = "";
        public string Value { get; set; } = "";
        public string DefaultValue { get; set; } = "";
        public bool IsGroup { get; set; }
        public int Order { get; set; }

        public TasIdentificationOptionItem Clone()
        {
            return new TasIdentificationOptionItem
            {
                Key = Key ?? "",
                Group = Group ?? "",
                Attribute = Attribute ?? "",
                Value = Value ?? "",
                DefaultValue = DefaultValue ?? "",
                IsGroup = IsGroup,
                Order = Order
            };
        }
    }

    internal sealed class TasIdentificationOptionsWindow : Window
    {
        private readonly TasReferenceSettings _tasReference;
        private readonly ObservableCollection<TasIdentificationOptionItem> _rows;
        private readonly DataGrid _grid;

        public TasIdentificationOptionsProfile SelectedProfile { get; private set; }

        public TasIdentificationOptionsWindow(TasIdentificationOptionsProfile profile, TasReferenceSettings tasReference)
        {
            _tasReference = tasReference ?? TasReferenceSettings.Load();
            SelectedProfile = (profile ?? TasIdentificationOptionsProfile.CreateDefault(_tasReference)).Clone();
            _rows = new ObservableCollection<TasIdentificationOptionItem>(
                SelectedProfile.Items.Select(item => item == null ? null : item.Clone()).Where(item => item != null));

            Title = "Identification Options";
            Width = 980;
            Height = 720;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = Brushes.White;

            var root = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(12)
            };
            Content = root;

            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            _grid = BuildGrid();
            root.Children.Add(_grid);
        }

        private UIElement BuildFooter()
        {
            var footer = new Grid
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var note = new TextBlock
            {
                Text = "You can input multiple element codes separated by comma.\nFormat: AZ, GAZ, GYZ",
                Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(note, 0);
            footer.Children.Add(note);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            Button restore = MakeFooterButton("Restore Default", 126);
            restore.Click += (sender, args) => RestoreDefaults();
            buttons.Children.Add(restore);

            Button ok = MakeFooterButton("OK", 86);
            ok.Margin = new Thickness(8, 0, 0, 0);
            ok.Background = new SolidColorBrush(Color.FromRgb(36, 122, 214));
            ok.Foreground = Brushes.White;
            ok.IsDefault = true;
            ok.Click += (sender, args) =>
            {
                CommitGridEdits();
                SelectedProfile = new TasIdentificationOptionsProfile
                {
                    Items = _rows.Select(item => item.Clone()).ToList()
                };
                DialogResult = true;
                Close();
            };
            buttons.Children.Add(ok);

            Button cancel = MakeFooterButton("Cancel", 86);
            cancel.Margin = new Thickness(8, 0, 0, 0);
            cancel.IsCancel = true;
            buttons.Children.Add(cancel);

            return footer;
        }

        private DataGrid BuildGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeaderWidth = 34,
                ItemsSource = _rows,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1)
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Attribute",
                Binding = new Binding(nameof(TasIdentificationOptionItem.Attribute)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Value",
                Binding = new Binding(nameof(TasIdentificationOptionItem.Value))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                },
                Width = new DataGridLength(280)
            });

            grid.LoadingRow += (sender, args) =>
            {
                args.Row.Header = (args.Row.GetIndex() + 1).ToString(CultureInfo.InvariantCulture);
                TasIdentificationOptionItem item = args.Row.Item as TasIdentificationOptionItem;
                if (item != null && item.IsGroup)
                {
                    args.Row.Background = new SolidColorBrush(Color.FromRgb(255, 248, 218));
                    args.Row.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    args.Row.Background = Brushes.White;
                    args.Row.FontWeight = FontWeights.Normal;
                }
            };

            grid.BeginningEdit += (sender, args) =>
            {
                TasIdentificationOptionItem item = args.Row?.Item as TasIdentificationOptionItem;
                if (item != null && item.IsGroup)
                {
                    args.Cancel = true;
                }
            };

            return grid;
        }

        private void RestoreDefaults()
        {
            CommitGridEdits();
            TasIdentificationOptionsProfile defaults = TasIdentificationOptionsProfile.CreateDefault(_tasReference);
            _rows.Clear();
            foreach (TasIdentificationOptionItem item in defaults.Items)
            {
                _rows.Add(item.Clone());
            }
        }

        private void CommitGridEdits()
        {
            if (_grid == null)
            {
                return;
            }

            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private static Button MakeFooterButton(string text, double width)
        {
            return new Button
            {
                Content = text,
                Width = width,
                Height = 32,
                Padding = new Thickness(10, 0, 10, 0)
            };
        }
    }
}
