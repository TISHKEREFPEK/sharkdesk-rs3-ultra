using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SharkDesk;

public sealed class ColorPickerWindow : Window
{
    private readonly Canvas spectrum = new() { Height = 170, ClipToBounds = true };
    private readonly Ellipse marker = new() { Width = 12, Height = 12, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
    private readonly Border preview = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(7), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox hex = new() { MaxLength = 7, Width = 120 };
    private readonly Slider hue = new() { Minimum = 0, Maximum = 360, Margin = new Thickness(0, 14, 0, 10) };
    private double h, saturation, brightness;
    private bool updating;
    public string SelectedColor { get; private set; }

    public ColorPickerWindow(string initial)
    {
        Title = "Цвет ступени DPI"; Width = 390; Height = 460; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 14;
        Resources.MergedDictionaries.Add(Application.Current.MainWindow.Resources);
        var rainbow = new LinearGradientBrush();
        Color[] colors = { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Cyan, Colors.Blue, Colors.Magenta, Colors.Red };
        for (int i = 0; i < colors.Length; i++) rainbow.GradientStops.Add(new GradientStop(colors[i], i / 6.0));
        hue.Resources["ButtonBg"] = rainbow;
        SetResourceReference(BackgroundProperty, "CardBg"); SetResourceReference(ForegroundProperty, "TextColor");
        var root = new StackPanel { Margin = new Thickness(22) }; Content = root;
        root.Children.Add(spectrum); root.Children.Add(hue);
        var palette = new System.Windows.Controls.Primitives.UniformGrid { Columns = 10, Margin = new Thickness(0, 0, 0, 16) };
        foreach (string value in new[] { "#FFFFFF", "#8C95A6", "#202938", "#EF4444", "#F97316", "#FACC15", "#22C55E", "#06B6D4", "#5262D8", "#D946EF" }) {
            var dot = new Border { Width = 23, Height = 23, CornerRadius = new CornerRadius(12), Background = (Brush)new BrushConverter().ConvertFromString(value), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5) };
            var button = new Button { Content = dot, Padding = new Thickness(3), Background = Brushes.Transparent, ToolTip = value };
            AutomationProperties.SetName(button, "Цвет " + value); button.Click += (_, _) => SetColor((Color)ColorConverter.ConvertFromString(value)); palette.Children.Add(button);
        }
        root.Children.Add(palette);
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(preview); row.Children.Add(hex); root.Children.Add(row);
        AutomationProperties.SetName(hex, "Цвет HEX"); AutomationProperties.SetName(hue, "Оттенок цвета"); AutomationProperties.SetName(spectrum, "Насыщенность и яркость цвета");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(cancel);
        var done = new Button { Content = "Выбрать", IsDefault = true }; done.SetResourceReference(Control.BackgroundProperty, "Accent"); done.SetResourceReference(Control.ForegroundProperty, "OnAccent");
        done.Click += (_, _) => { try { SetColor((Color)ColorConverter.ConvertFromString(hex.Text)); DialogResult = true; } catch { hex.Focus(); } }; actions.Children.Add(done); root.Children.Add(actions);
        spectrum.SizeChanged += (_, _) => DrawSpectrum();
        spectrum.MouseLeftButtonDown += (_, e) => { spectrum.CaptureMouse(); SelectPoint(e.GetPosition(spectrum)); };
        spectrum.MouseMove += (_, e) => { if (spectrum.IsMouseCaptured) SelectPoint(e.GetPosition(spectrum)); };
        spectrum.MouseLeftButtonUp += (_, _) => spectrum.ReleaseMouseCapture();
        hue.ValueChanged += (_, _) => { if (!updating) { h = hue.Value; UpdateSelected(); DrawSpectrum(); } };
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; try { SetColor((Color)ColorConverter.ConvertFromString(hex.Text)); } catch { } } };
        SetColor((Color)ColorConverter.ConvertFromString(initial));
    }
    private void SetColor(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        h = delta < 0.00001 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        if (h < 0) h += 360; saturation = max == 0 ? 0 : delta / max; brightness = max;
        updating = true; hue.Value = h; updating = false; UpdateSelected(); DrawSpectrum();
    }
    private void SelectPoint(Point point)
    {
        saturation = Math.Clamp(point.X / Math.Max(1, spectrum.ActualWidth), 0, 1);
        brightness = 1 - Math.Clamp(point.Y / spectrum.Height, 0, 1); UpdateSelected(); DrawSpectrum();
    }
    private void UpdateSelected()
    {
        Color color = FromHsv(h, saturation, brightness); SelectedColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        preview.Background = new SolidColorBrush(color); hex.Text = SelectedColor;
    }
    private void DrawSpectrum()
    {
        double width = spectrum.ActualWidth; if (width <= 0) return;
        spectrum.Children.Clear(); spectrum.Background = new SolidColorBrush(FromHsv(h, 1, 1));
        spectrum.Children.Add(new Rectangle { Width = width, Height = 170, Fill = new LinearGradientBrush(Colors.White, Colors.Transparent, 0), IsHitTestVisible = false });
        spectrum.Children.Add(new Rectangle { Width = width, Height = 170, Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), IsHitTestVisible = false });
        spectrum.Children.Add(marker); Canvas.SetLeft(marker, saturation * width - 6); Canvas.SetTop(marker, (1 - brightness) * 170 - 6);
    }
    private static Color FromHsv(double hue, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = hue switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
