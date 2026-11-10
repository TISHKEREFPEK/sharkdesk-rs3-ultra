using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SharkDesk;

public partial class MainWindow
{
    private readonly Button[] dpiChips = new Button[6];
    private readonly Border[] dpiChipBorders = new Border[6];
    private readonly Ellipse[] dpiDots = new Ellipse[6];
    private readonly TextBlock[] dpiChipValues = new TextBlock[6];
    private TextBox dpiValue, dpiValueY;
    private Slider dpiSlider;
    private CheckBox separateAxes;
    private StackPanel dpiYPanel;
    private Border colorPreview;
    private TextBlock dpiUnit;
    private System.Windows.Controls.Primitives.UniformGrid dpiChipStrip;
    private bool updatingDpi;
    private int editingStage;

    private void InitDpiEditor()
    {
        DpiPanel.Children.Clear();
        var editor = new StackPanel(); DpiPanel.Children.Add(editor);
        var chips = dpiChipStrip = new System.Windows.Controls.Primitives.UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 12) };
        editor.Children.Add(chips);
        for (int i = 0; i < 6; i++) {
            int index = i;
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            dpiDots[i] = new Ellipse { Width = 8, Height = 8, Stroke = Brushes.Gray, StrokeThickness = 0.5, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            dpiChipValues[i] = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.NoWrap };
            content.Children.Add(dpiDots[i]); content.Children.Add(dpiChipValues[i]);
            dpiChipBorders[i] = new Border { Child = content, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 8, 7, 8) };
            dpiChips[i] = new Button { Content = dpiChipBorders[i], Padding = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(0, 0, i == 5 ? 0 : 7, 0) };
            dpiChips[i].Click += (_, _) => ChooseStage(index);
            AutomationProperties.SetName(dpiChips[i], "Ступень DPI " + (i + 1));
            chips.Children.Add(dpiChips[i]);
        }
        var numberRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        numberRow.ColumnDefinitions.Add(new ColumnDefinition()); numberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var numbers = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        dpiValue = new TextBox { FontSize = 42, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Width = 180, Padding = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, MaxLength = 5 };
        AutomationProperties.SetName(dpiValue, "Чувствительность DPI X");
        dpiUnit = new TextBlock { Text = "DPI", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        dpiUnit.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        numbers.Children.Add(dpiValue); numbers.Children.Add(dpiUnit); numberRow.Children.Add(numbers);
        colorPreview = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        var color = new Button { Content = colorPreview, Padding = new Thickness(9), Background = Brushes.Transparent, ToolTip = "Цвет индикатора этой ступени", VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(color, "Выбрать цвет ступени DPI");
        color.Click += (_, _) => PickStageColor(); Grid.SetColumn(color, 1); numberRow.Children.Add(color); editor.Children.Add(numberRow);
        dpiYPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        dpiYPanel.Children.Add(new TextBlock { Text = "DPI Y", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        dpiValueY = new TextBox { Width = 110, FontSize = 18, TextAlignment = TextAlignment.Center, MaxLength = 5 };
        AutomationProperties.SetName(dpiValueY, "Чувствительность DPI Y"); dpiYPanel.Children.Add(dpiValueY); editor.Children.Add(dpiYPanel);
        var sliderRow = new Grid { Margin = new Thickness(0, 6, 0, 4) };
        sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); sliderRow.ColumnDefinitions.Add(new ColumnDefinition()); sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var minus = new Button { Content = "−", Padding = new Thickness(10, 3, 10, 3), Background = Brushes.Transparent, FontSize = 20 }; AutomationProperties.SetName(minus, "Уменьшить DPI");
        minus.Click += (_, _) => StepDpi(-1); sliderRow.Children.Add(minus);
        dpiSlider = new Slider { Minimum = 0, Maximum = 1, SmallChange = 0.005, LargeChange = 0.05, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(dpiSlider, "Ползунок чувствительности DPI"); Grid.SetColumn(dpiSlider, 1); sliderRow.Children.Add(dpiSlider);
        var plus = new Button { Content = "+", Padding = new Thickness(10, 3, 10, 3), Background = Brushes.Transparent, FontSize = 20 }; AutomationProperties.SetName(plus, "Увеличить DPI"); plus.Click += (_, _) => StepDpi(1); Grid.SetColumn(plus, 2); sliderRow.Children.Add(plus);
        editor.Children.Add(sliderRow);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        foreach (int value in new[] { 400, 800, 1600, 3200 }) { var b = new Button { Content = value.ToString(), FontSize = 12, Padding = new Thickness(13, 6, 13, 6), Background = Brushes.Transparent }; b.Click += (_, _) => SetDpiValue(value); presets.Children.Add(b); }
        editor.Children.Add(presets);
        separateAxes = new CheckBox { Content = "Раздельные X/Y", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0) };
        separateAxes.Click += (_, _) => {
            if (updatingDpi) return;
            if (separateAxes.IsChecked != true) yDpiBoxes[editingStage].Text = dpiBoxes[editingStage].Text;
            RefreshDpiEditor(); Edited(this, new RoutedEventArgs());
        };
        editor.Children.Add(separateAxes);
        dpiValue.TextChanged += (_, _) => CommitDpiText(false); dpiValueY.TextChanged += (_, _) => CommitDpiText(true);
        dpiValue.GotKeyboardFocus += (_, _) => dpiValue.SelectAll(); dpiValueY.GotKeyboardFocus += (_, _) => dpiValueY.SelectAll();
        dpiSlider.ValueChanged += (_, _) => {
            if (updatingDpi) return;
            int value = (int)Math.Round(50 * Math.Exp(dpiSlider.Value * Math.Log(1040)));
            value = value < 100 ? value : value < 42000 ? (int)Math.Round(value / 50.0) * 50 : (int)Math.Round(value / 2.0) * 2;
            SetDpiValue(Math.Clamp(value, 1, 52000));
        };
    }

    private void ChooseStage(int index)
    {
        if (filling || updatingDpi || index >= (int)(CountBox.SelectedItem ?? 6)) return;
        bool was = filling; filling = true;
        for (int i = 0; i < 6; i++) stages[i].IsChecked = i == index;
        filling = was; editingStage = index;
        RefreshDpiEditor(); Edited(this, new RoutedEventArgs());
    }
    private void RefreshDpiEditor()
    {
        if (dpiValue == null) return;
        updatingDpi = true;
        int count = (int)(CountBox.SelectedItem ?? 6);
        dpiChipStrip.Columns = count;
        int active = Array.FindIndex(stages, s => s.IsChecked == true);
        editingStage = Math.Clamp(active < 0 ? 0 : active, 0, Math.Max(0, count - 1));
        for (int i = 0; i < 6; i++) {
            dpiChips[i].Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
            dpiChipValues[i].Text = dpiBoxes[i].Text;
            dpiChipBorders[i].SetResourceReference(Border.BorderBrushProperty, i == editingStage ? "Accent" : "LineColor");
            dpiChipBorders[i].SetResourceReference(Border.BackgroundProperty, i == editingStage ? "InputBg" : "CardBg");
            try { dpiDots[i].Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorBoxes[i].Text)); } catch { }
        }
        dpiValue.Text = dpiBoxes[editingStage].Text; dpiValueY.Text = yDpiBoxes[editingStage].Text;
        bool split = dpiValue.Text != dpiValueY.Text || separateAxes.IsChecked == true;
        separateAxes.IsChecked = split; dpiYPanel.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        dpiUnit.Text = split ? "DPI X" : "DPI";
        if (int.TryParse(dpiValue.Text, out int dpi) && dpi > 0) dpiSlider.Value = Math.Clamp(Math.Log(dpi / 50.0) / Math.Log(1040), 0, 1);
        try { colorPreview.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorBoxes[editingStage].Text)); } catch { }
        updatingDpi = false;
    }
    private void CommitDpiText(bool y)
    {
        if (updatingDpi || filling) return;
        if (y) yDpiBoxes[editingStage].Text = dpiValueY.Text;
        else { dpiBoxes[editingStage].Text = dpiValue.Text; if (separateAxes.IsChecked != true) yDpiBoxes[editingStage].Text = dpiValue.Text; }
        if (int.TryParse(dpiValue.Text, out int dpi) && dpi is >= 1 and <= 52000) {
            updatingDpi = true; dpiSlider.Value = Math.Clamp(Math.Log(dpi / 50.0) / Math.Log(1040), 0, 1); dpiChipValues[editingStage].Text = dpiValue.Text; updatingDpi = false;
        }
    }
    private void SetDpiValue(int value) => dpiValue.Text = value.ToString(CultureInfo.InvariantCulture);
    private void StepDpi(int direction)
    {
        if (!int.TryParse(dpiValue.Text, out int value)) return;
        int step = value >= 42000 ? 2 : value < 100 ? 1 : 50;
        int next = Math.Clamp(value + step * direction, 1, 52000); if (next > 42000 && next % 2 != 0) next += direction;
        SetDpiValue(next);
    }
    private void PickStageColor()
    {
        var picker = new ColorPickerWindow(colorBoxes[editingStage].Text) { Owner = this };
        if (picker.ShowDialog() != true) return;
        colorBoxes[editingStage].Text = picker.SelectedColor; RefreshDpiEditor();
    }
}
