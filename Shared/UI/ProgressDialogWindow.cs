using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class ProgressDialogWindow : Window
    {
        private readonly TextBlock _titleText;
        private readonly TextBlock _percentText;
        private readonly TextBlock _detailText;
        private readonly ProgressBar _progressBar;
        private readonly Button _cancelButton;

        public event EventHandler CancelRequested;

        public ProgressDialogWindow()
        {
            Title = "Progress";
            Width = 560;
            Height = 180;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.ToolWindow;
            Background = Brushes.White;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(26, 131, 214)),
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(20, 104, 170)),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            var headerPanel = new DockPanel();
            var logo = CreateLogoImage();
            if (logo != null)
            {
                Icon = logo.Source;
                logo.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(logo, Dock.Left);
                headerPanel.Children.Add(logo);
            }

            _titleText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Text = "Processing",
                VerticalAlignment = global::System.Windows.VerticalAlignment.Center
            };
            headerPanel.Children.Add(_titleText);
            header.Child = headerPanel;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new Grid
            {
                Margin = new Thickness(12, 10, 12, 6)
            };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _percentText = new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(40, 52, 78)),
                Text = "0%"
            };
            _detailText = new TextBlock
            {
                Margin = new Thickness(0, 4, 0, 8),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(70, 84, 112)),
                Text = "Preparing..."
            };
            _progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Height = 16,
                Value = 0,
                Margin = new Thickness(0, 4, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(232, 237, 245)),
                Foreground = new SolidColorBrush(Color.FromRgb(26, 131, 214)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(188, 201, 222)),
                BorderThickness = new Thickness(1)
            };

            Grid.SetRow(_percentText, 0);
            Grid.SetRow(_detailText, 1);
            Grid.SetRow(_progressBar, 2);
            body.Children.Add(_percentText);
            body.Children.Add(_detailText);
            body.Children.Add(_progressBar);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var footer = new Grid
            {
                Margin = new Thickness(12, 0, 12, 10)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _cancelButton = new Button
            {
                Width = 92,
                Height = 28,
                Content = "Cancel"
            };
            _cancelButton.Click += (_, __) =>
            {
                _cancelButton.IsEnabled = false;
                CancelRequested?.Invoke(this, EventArgs.Empty);
            };
            Grid.SetColumn(_cancelButton, 1);
            footer.Children.Add(_cancelButton);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        private static Image CreateLogoImage()
        {
            try
            {
                var source = new BitmapImage(new Uri(
                    CamboBimRuntime.GetPackUri("images/CamboBIM_32.png"),
                    UriKind.Absolute));

                return new Image
                {
                    Source = source,
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform
                };
            }
            catch
            {
                return null;
            }
        }

        public void SetTitleText(string title)
        {
            _titleText.Text = string.IsNullOrWhiteSpace(title) ? "Processing" : title.Trim();
        }

        public void SetProgress(int percent, string detail)
        {
            int clamped = Math.Max(0, Math.Min(100, percent));
            _progressBar.Value = clamped;
            _percentText.Text = clamped.ToString() + "%";
            _detailText.Text = string.IsNullOrWhiteSpace(detail) ? "" : detail;
        }

        public void ResetCancelButton()
        {
            _cancelButton.IsEnabled = true;
        }
    }
}
