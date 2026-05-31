using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfPath = System.Windows.Shapes.Path;

namespace CamboBIM.Revit2024.Addin
{
    internal static class IdentifySvgIconFactory
    {
        private const string IconFolderRelativePath = @"images\identify-tools";
        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, UIElement> IconCache = new Dictionary<string, UIElement>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ExactIconMap = CreateExactIconMap();

        public static FrameworkElement CreateIconForLabel(string label, double size, bool muted = false)
        {
            return CreateIcon(ResolveIconFile(label), size, muted);
        }

        public static FrameworkElement CreateIconForTool(string label, string key, string toolName, string category, double size, bool muted = false)
        {
            return CreateIcon(ResolveIconFile(label, key, toolName, category), size, muted);
        }

        public static FrameworkElement CreateIcon(string iconFile, double size, bool muted = false)
        {
            string normalizedIconFile = NormalizeIconFileName(iconFile);
            if (string.IsNullOrWhiteSpace(normalizedIconFile))
            {
                return CreateFallbackIcon(size, muted);
            }

            try
            {
                string cacheKey = normalizedIconFile + "|" + muted.ToString(CultureInfo.InvariantCulture);
                UIElement cached;
                lock (CacheLock)
                {
                    if (IconCache.TryGetValue(cacheKey, out cached))
                    {
                        return WrapIcon(CloneElement(cached), size);
                    }
                }

                string path = ResolveIconPath(normalizedIconFile);
                if (string.IsNullOrWhiteSpace(path))
                {
                    return CreateFallbackIcon(size, muted);
                }

                UIElement icon = LoadSvgIcon(path, muted);
                lock (CacheLock)
                {
                    if (!IconCache.ContainsKey(cacheKey))
                    {
                        IconCache[cacheKey] = icon;
                    }
                }

                return WrapIcon(CloneElement(icon), size);
            }
            catch
            {
                return CreateFallbackIcon(size, muted);
            }
        }

        public static FrameworkElement CreateIconText(string text, string iconFileOrLabel, double iconSize, bool muted = false)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(CreateIcon(ResolveIconFile(iconFileOrLabel), iconSize, muted));
            panel.Children.Add(new TextBlock
            {
                Text = text ?? "",
                FontSize = Math.Max(11.0, iconSize * 0.72),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        public static string ResolveIconFile(params string[] candidates)
        {
            foreach (string rawCandidate in candidates ?? new string[0])
            {
                string candidate = StripShortcut(rawCandidate);
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string exact;
                if (ExactIconMap.TryGetValue(candidate.Trim(), out exact) && IconExists(exact))
                {
                    return exact;
                }

                string normalized = NormalizeIconFileName(candidate);
                if (IconExists(normalized))
                {
                    return normalized;
                }

                string slug = Slugify(candidate) + ".svg";
                if (IconExists(slug))
                {
                    return slug;
                }
            }

            return "custom-quantity.svg";
        }

        public static string ResolveButtonIconFile(string text)
        {
            string exact;
            if (!string.IsNullOrWhiteSpace(text) && ExactIconMap.TryGetValue(text.Trim(), out exact))
            {
                return exact;
            }

            return ResolveIconFile(text);
        }

        private static Dictionary<string, string> CreateExactIconMap()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Axis", "axis-grid.svg" },
                { "Axis Grid", "axis-grid.svg" },
                { "Axis Grid(X)", "axis-grid.svg" },
                { "Gridline", "gridline.svg" },
                { "Segmentation", "segmentation.svg" },
                { "Column", "column.svg" },
                { "Column(C)", "column.svg" },
                { "Wall", "wall.svg" },
                { "Wall(W)", "wall.svg" },
                { "Door/Window Opening", "doorwin.svg" },
                { "DoorWin", "doorwin.svg" },
                { "Beam", "beam.svg" },
                { "Beam(B)", "beam.svg" },
                { "Slab", "slab.svg" },
                { "In-situ Slab", "in-situ-slab.svg" },
                { "In-situ Slab(S)", "in-situ-slab.svg" },
                { "Steel Structure", "steel-beam.svg" },
                { "Steel/Composite Column", "steel-composite-column.svg" },
                { "Steel/Composite Beam", "steel-composite-beam.svg" },
                { "Steel/Composite Slab", "steel-composite-slab.svg" },
                { "Steel/Composite Slab(S)", "steel-composite-slab.svg" },
                { "Staircase", "staircase.svg" },
                { "Finishes", "floor-finish.svg" },
                { "Prefabrication", "component.svg" },
                { "Foundation", "foundation.svg" },
                { "Excavation", "soil-excavation.svg" },
                { "Others", "custom-quantity.svg" },
                { "Custom Element", "custom-area.svg" },
                { "Custom Quantity", "custom-quantity.svg" },
                { "Lean Concrete", "lean-concrete.svg" },
                { "Blinding", "blinding.svg" },
                { "Blinding(X)", "blinding.svg" },
                { "Soil Excavation", "soil-excavation.svg" },
                { "Pile Cap", "pile-cap.svg" },
                { "Pile Cap(P)", "pile-cap.svg" },
                { "Bored Pile", "bored-pile.svg" },
                { "Pile", "pile.svg" },
                { "Pile(P)", "pile.svg" },
                { "Raft Foundation", "raft-foundation.svg" },
                { "Raft Foundation(M)", "raft-foundation.svg" },
                { "Pad Foundation", "pad-foundation.svg" },
                { "Pad Foundation(I)", "pad-foundation.svg" },
                { "Strip Foundation", "strip-foundation.svg" },
                { "Strip Foundation(N)", "strip-foundation.svg" },
                { "Element Schedule", "element-schedule.svg" },
                { "Identification Options", "identification-options.svg" },
                { "Select CAD Data", "pick-objects.svg" },
                { "Batch Replace", "clear-filters.svg" },
                { "Import ADAPT", "import-excel.svg" },
                { "Import Excel", "import-excel.svg" },
                { "Import Excel File", "import-excel.svg" },
                { "Open Excel", "open-excel.svg" },
                { "Export Excel", "export-excel.svg" },
                { "Paste", "paste.svg" },
                { "Clear", "clear-import.svg" },
                { "Clear Import", "clear-import.svg" },
                { "Clear Filters", "clear-filters.svg" },
                { "Pick Link", "pick-link.svg" },
                { "Pick Objects", "pick-objects.svg" },
                { "Pick Sideline", "pick-sideline.svg" },
                { "Pick Label", "pick-label.svg" },
                { "Check Source", "check-source.svg" },
                { "Identification Check", "check-source.svg" },
                { "Auto-Identify", "auto-identify.svg" },
                { "Auto-Identify v", "auto-identify.svg" },
                { "Click-Identify", "click-identify.svg" },
                { "Drag-Identify", "drag-identify.svg" },
                { "Apply Attribute", "apply-attribute.svg" },
                { "Generate Revit", "generate-revit.svg" },
                { "Move", "move.svg" },
                { "Copy", "copy.svg" },
                { "Align", "align.svg" },
                { "Rotate", "rotate.svg" },
                { "Mirror", "mirror.svg" },
                { "Offset", "offset.svg" },
                { "Trim", "trim.svg" },
                { "Extend", "extend.svg" },
                { "Split", "split.svg" },
                { "Delete", "delete.svg" },
                { "Drawing Manager", "drawing-manager.svg" },
                { "Layer Manager", "layer-manager.svg" },
                { "Delete Row", "delete-row.svg" },
                { "Delete Column", "delete-column.svg" }
            };
        }

        private static UIElement LoadSvgIcon(string path, bool muted)
        {
            XDocument document = XDocument.Load(path);
            XElement root = document.Root;
            Rect viewBox = ParseViewBox((string)(root == null ? null : root.Attribute("viewBox")));
            if (viewBox.Width <= 0 || viewBox.Height <= 0)
            {
                viewBox = new Rect(0, 0, 32, 32);
            }

            var canvas = new Canvas
            {
                Width = viewBox.Width,
                Height = viewBox.Height
            };

            string inheritedFill = root == null ? "" : (string)root.Attribute("fill");
            foreach (XElement element in document.Descendants())
            {
                string localName = element.Name.LocalName;
                if (string.Equals(localName, "path", StringComparison.OrdinalIgnoreCase))
                {
                    AddPath(canvas, element, inheritedFill, muted);
                }
                else if (string.Equals(localName, "circle", StringComparison.OrdinalIgnoreCase))
                {
                    AddCircle(canvas, element, inheritedFill, muted);
                }
            }

            if (canvas.Children.Count == 0)
            {
                return CreateFallbackIcon(32, muted);
            }

            return canvas;
        }

        private static void AddPath(Canvas canvas, XElement element, string inheritedFill, bool muted)
        {
            string data = (string)element.Attribute("d");
            if (string.IsNullOrWhiteSpace(data))
            {
                return;
            }

            Geometry geometry;
            try
            {
                geometry = Geometry.Parse(data);
            }
            catch
            {
                return;
            }

            var path = new WpfPath
            {
                Data = geometry,
                Stroke = ResolveBrush((string)element.Attribute("stroke"), "#2E75B6", muted),
                StrokeThickness = ParseDouble((string)element.Attribute("stroke-width"), 2.2),
                StrokeStartLineCap = ParseLineCap((string)element.Attribute("stroke-linecap")),
                StrokeEndLineCap = ParseLineCap((string)element.Attribute("stroke-linecap")),
                StrokeLineJoin = ParseLineJoin((string)element.Attribute("stroke-linejoin"))
            };

            string fill = (string)element.Attribute("fill");
            if (string.IsNullOrWhiteSpace(fill))
            {
                fill = inheritedFill;
            }

            if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
            {
                path.Fill = ResolveBrush(fill, "#2E75B6", muted);
            }

            canvas.Children.Add(path);
        }

        private static void AddCircle(Canvas canvas, XElement element, string inheritedFill, bool muted)
        {
            double radius = ParseDouble((string)element.Attribute("r"), 0);
            if (radius <= 0)
            {
                return;
            }

            double cx = ParseDouble((string)element.Attribute("cx"), radius);
            double cy = ParseDouble((string)element.Attribute("cy"), radius);
            string fill = (string)element.Attribute("fill");
            if (string.IsNullOrWhiteSpace(fill))
            {
                fill = inheritedFill;
            }

            var ellipse = new WpfEllipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Stroke = ResolveBrush((string)element.Attribute("stroke"), "", muted),
                StrokeThickness = ParseDouble((string)element.Attribute("stroke-width"), 2.2)
            };

            if (!string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
            {
                ellipse.Fill = ResolveBrush(fill, "#2E75B6", muted);
            }

            Canvas.SetLeft(ellipse, cx - radius);
            Canvas.SetTop(ellipse, cy - radius);
            canvas.Children.Add(ellipse);
        }

        private static FrameworkElement WrapIcon(UIElement icon, double size)
        {
            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Child = icon,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private static FrameworkElement CreateFallbackIcon(double size, bool muted)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(2),
                BorderBrush = new SolidColorBrush(muted ? Color.FromRgb(150, 160, 172) : Color.FromRgb(46, 117, 182)),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "?",
                    FontSize = Math.Max(8, size * 0.55),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(muted ? Color.FromRgb(150, 160, 172) : Color.FromRgb(46, 117, 182)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
        }

        private static UIElement CloneElement(UIElement element)
        {
            if (element is Canvas canvas)
            {
                var clone = new Canvas
                {
                    Width = canvas.Width,
                    Height = canvas.Height
                };

                foreach (UIElement child in canvas.Children)
                {
                    if (child is WpfPath path)
                    {
                        clone.Children.Add(new WpfPath
                        {
                            Data = path.Data == null ? null : path.Data.Clone(),
                            Stroke = path.Stroke,
                            StrokeThickness = path.StrokeThickness,
                            StrokeStartLineCap = path.StrokeStartLineCap,
                            StrokeEndLineCap = path.StrokeEndLineCap,
                            StrokeLineJoin = path.StrokeLineJoin,
                            Fill = path.Fill
                        });
                    }
                    else if (child is WpfEllipse ellipse)
                    {
                        var ellipseClone = new WpfEllipse
                        {
                            Width = ellipse.Width,
                            Height = ellipse.Height,
                            Stroke = ellipse.Stroke,
                            StrokeThickness = ellipse.StrokeThickness,
                            Fill = ellipse.Fill
                        };
                        Canvas.SetLeft(ellipseClone, Canvas.GetLeft(ellipse));
                        Canvas.SetTop(ellipseClone, Canvas.GetTop(ellipse));
                        clone.Children.Add(ellipseClone);
                    }
                }

                return clone;
            }

            return CreateFallbackIcon(32, false);
        }

        private static string ResolveIconPath(string iconFile)
        {
            string normalized = NormalizeIconFileName(iconFile);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            foreach (string folder in GetIconFolders())
            {
                string path = Path.Combine(folder, normalized);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return "";
        }

        private static IEnumerable<string> GetIconFolders()
        {
            string assemblyFolder = "";
            try
            {
                assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch
            {
                assemblyFolder = "";
            }

            string baseFolder = AppDomain.CurrentDomain.BaseDirectory;
            string currentFolder = "";
            try
            {
                currentFolder = Directory.GetCurrentDirectory();
            }
            catch
            {
                currentFolder = "";
            }

            var folders = new[]
            {
                Path.Combine(assemblyFolder ?? "", IconFolderRelativePath),
                Path.Combine(baseFolder ?? "", IconFolderRelativePath),
                Path.Combine(currentFolder ?? "", IconFolderRelativePath),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", @"..\..\..\", IconFolderRelativePath)
            };

            return folders
                .Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Select(folder =>
                {
                    try
                    {
                        return Path.GetFullPath(folder);
                    }
                    catch
                    {
                        return folder;
                    }
                })
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static bool IconExists(string iconFile)
        {
            string normalized = NormalizeIconFileName(iconFile);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return GetIconFolders().Any(folder => File.Exists(Path.Combine(folder, normalized)));
        }

        private static string NormalizeIconFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string trimmed = value.Trim().Replace('/', '-').Replace('\\', '-');
            if (trimmed.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return Slugify(trimmed) + ".svg";
        }

        private static string StripShortcut(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return Regex.Replace(value, @"\([^)]*\)", "").Trim();
        }

        private static string Slugify(string value)
        {
            string stripped = StripShortcut(value).Trim().ToLowerInvariant();
            stripped = stripped.Replace("&", " and ");
            stripped = Regex.Replace(stripped, @"[^a-z0-9]+", "-").Trim('-');
            return stripped;
        }

        private static Rect ParseViewBox(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new Rect(0, 0, 32, 32);
            }

            string[] parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4)
            {
                return new Rect(0, 0, 32, 32);
            }

            return new Rect(
                ParseDouble(parts[0], 0),
                ParseDouble(parts[1], 0),
                ParseDouble(parts[2], 32),
                ParseDouble(parts[3], 32));
        }

        private static double ParseDouble(string value, double fallback)
        {
            double result;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : fallback;
        }

        private static Brush ResolveBrush(string value, string fallback, bool muted)
        {
            if (muted)
            {
                return new SolidColorBrush(Color.FromRgb(118, 130, 148));
            }

            string colorText = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            if (string.Equals(colorText, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(colorText))
            {
                return null;
            }

            try
            {
                object converted = ColorConverter.ConvertFromString(colorText);
                if (converted is Color color)
                {
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
            }

            return new SolidColorBrush(Color.FromRgb(46, 117, 182));
        }

        private static PenLineCap ParseLineCap(string value)
        {
            if (string.Equals(value, "round", StringComparison.OrdinalIgnoreCase))
            {
                return PenLineCap.Round;
            }

            if (string.Equals(value, "square", StringComparison.OrdinalIgnoreCase))
            {
                return PenLineCap.Square;
            }

            return PenLineCap.Flat;
        }

        private static PenLineJoin ParseLineJoin(string value)
        {
            if (string.Equals(value, "round", StringComparison.OrdinalIgnoreCase))
            {
                return PenLineJoin.Round;
            }

            if (string.Equals(value, "bevel", StringComparison.OrdinalIgnoreCase))
            {
                return PenLineJoin.Bevel;
            }

            return PenLineJoin.Miter;
        }
    }
}
