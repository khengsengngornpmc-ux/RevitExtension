using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CamboBIM.Revit2024.Addin
{
    internal static class MhnkUiTheme
    {
        private static readonly Brush LightWindowBrush = Brushes.White;
        private static readonly Brush LightPanelBrush = new SolidColorBrush(Color.FromRgb(248, 250, 252));
        private static readonly Brush LightBorderBrush = new SolidColorBrush(Color.FromRgb(218, 224, 231));
        private static readonly Brush LightTextBrush = new SolidColorBrush(Color.FromRgb(32, 44, 58));
        private static readonly Brush LightMutedTextBrush = new SolidColorBrush(Color.FromRgb(74, 85, 104));

        private static readonly Brush DarkWindowBrush = new SolidColorBrush(Color.FromRgb(24, 28, 34));
        private static readonly Brush DarkPanelBrush = new SolidColorBrush(Color.FromRgb(34, 40, 48));
        private static readonly Brush DarkBorderBrush = new SolidColorBrush(Color.FromRgb(78, 88, 101));
        private static readonly Brush DarkTextBrush = new SolidColorBrush(Color.FromRgb(236, 241, 247));
        private static readonly Brush DarkMutedTextBrush = new SolidColorBrush(Color.FromRgb(197, 207, 219));

        public static bool IsDark
        {
            get
            {
                try
                {
                    return string.Equals(File.ReadAllText(GetThemePath()).Trim(), "dark", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool Toggle()
        {
            bool next = !IsDark;
            Save(next);
            return next;
        }

        public static void Save(bool dark)
        {
            string path = GetThemePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, dark ? "dark" : "light");
        }

        public static string GetThemePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MHNK",
                "ArcTools",
                "theme.txt");
        }

        public static void Apply(Window window)
        {
            if (window == null)
            {
                return;
            }

            window.Background = IsDark ? DarkWindowBrush : LightWindowBrush;
            ApplyElement(window.Content as DependencyObject);
        }

        private static void ApplyElement(DependencyObject element)
        {
            if (element == null)
            {
                return;
            }

            FrameworkElement frameworkElement = element as FrameworkElement;
            if (string.Equals(frameworkElement?.Tag as string, "MhnkThemePreserve", StringComparison.Ordinal))
            {
                return;
            }

            bool dark = IsDark;
            Brush panelBrush = dark ? DarkPanelBrush : LightPanelBrush;
            Brush borderBrush = dark ? DarkBorderBrush : LightBorderBrush;
            Brush textBrush = dark ? DarkTextBrush : LightTextBrush;
            Brush mutedTextBrush = dark ? DarkMutedTextBrush : LightMutedTextBrush;
            Brush inputBrush = dark ? new SolidColorBrush(Color.FromRgb(29, 34, 41)) : Brushes.White;

            Border border = element as Border;
            if (border != null)
            {
                border.Background = panelBrush;
                border.BorderBrush = borderBrush;
            }

            TextBlock textBlock = element as TextBlock;
            if (textBlock != null)
            {
                textBlock.Foreground = textBlock.FontWeight == FontWeights.SemiBold ? textBrush : mutedTextBrush;
            }

            Control control = element as Control;
            if (control != null)
            {
                control.Foreground = textBrush;
                if (control is TextBox || control is ListBox || control is DataGrid || control is ComboBox)
                {
                    control.Background = inputBrush;
                    control.BorderBrush = borderBrush;
                }
                else if (control is Button)
                {
                    control.Background = dark ? new SolidColorBrush(Color.FromRgb(46, 54, 64)) : new SolidColorBrush(Color.FromRgb(245, 247, 250));
                    control.BorderBrush = borderBrush;
                }
            }

            Panel panel = element as Panel;
            if (panel != null)
            {
                panel.Background = Brushes.Transparent;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < childCount; i++)
            {
                ApplyElement(VisualTreeHelper.GetChild(element, i));
            }
        }
    }
}
