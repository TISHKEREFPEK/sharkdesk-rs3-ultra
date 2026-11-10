using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SharkDesk;

public sealed class ButtonsWindow : Window
{
    private sealed record ActionChoice(string Label, int Type, int Parameter)
    { public override string ToString() => Label; }
    private readonly ButtonAssignment[] assignments;
    private readonly ListBox buttons = new();
    private readonly ComboBox action = new(), repeatMode = new();
    private readonly TextBox shortcut = new(), macroName = new(), repeatCount = new(), fireCount = new(), fireDelay = new();
    private readonly DataGrid events = new();
    private readonly StackPanel shortcutPanel = new(), macroPanel = new(), firePanel = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button apply = new() { Content = "Применить назначения к мыши" };
    private readonly Button record = new() { Content = "Записать клавиши" };
    private readonly List<ActionChoice> choices = new();
    private ObservableCollection<MacroStep> steps = new();
    private readonly HashSet<Key> held = new();
    private int selected;
    private bool filling, recording, writing;
    private DateTimeOffset lastEvent;
    public ButtonAssignment[]? Applied { get; private set; }

    public ButtonsWindow(ButtonAssignment[] snapshot)
    {
        assignments = snapshot.OrderBy(b => b.Index).ToArray();
        Title = "RS3 Ultra · Кнопки и макросы"; Width = 940; Height = 470; MinWidth = 880; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Resources.MergedDictionaries.Add(Application.Current.MainWindow.Resources);
        SetResourceReference(BackgroundProperty, "PageBg"); SetResourceReference(ForegroundProperty, "TextColor");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var title = new StackPanel(); title.Children.Add(new TextBlock { Text = "Кнопки и макросы", FontSize = 28, FontWeight = FontWeights.SemiBold });
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        apply.Click += Apply; DockPanel.SetDock(apply, Dock.Right); footer.Children.Add(apply); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var layout = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        layout.ColumnDefinitions.Add(new() { Width = new GridLength(205) }); layout.ColumnDefinitions.Add(new()); root.Children.Add(layout);
        buttons.ItemsSource = Enumerable.Range(0, 5).Select(ButtonAssignment.Label).ToArray();
        buttons.BorderThickness = new Thickness(0); buttons.Padding = new Thickness(8);
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 12, 9, 12))); itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); buttons.ItemContainerStyle = itemStyle;
        buttons.Margin = new Thickness(0, 0, 20, 0); buttons.SelectionChanged += SelectButton;
        buttons.SetResourceReference(Control.BackgroundProperty, "CardBg"); buttons.SetResourceReference(Control.ForegroundProperty, "TextColor");
        layout.Children.Add(buttons);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); layout.Children.Add(scroll);
        var editor = new StackPanel(); scroll.Content = editor;
        editor.Children.Add(Label("Назначение")); editor.Children.Add(action); BuildChoices();
        action.ItemsSource = choices; action.SelectionChanged += (_, _) => UpdatePanels();
        shortcutPanel.Children.Add(Label("Комбинация клавиш (до пяти): Ctrl+Shift+A, Alt+F4, VolumeUp…")); shortcutPanel.Children.Add(shortcut);
        editor.Children.Add(shortcutPanel);
        var fireFields = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        var f1 = new StackPanel(); f1.Children.Add(Label("Число кликов, 0…3")); f1.Children.Add(fireCount); fireFields.Children.Add(f1);
        var f2 = new StackPanel(); f2.Children.Add(Label("Интервал, 10…255 мс")); f2.Children.Add(fireDelay); fireFields.Children.Add(f2);
        firePanel.Children.Add(fireFields); firePanel.Children.Add(Label("0 — кликать, пока кнопка удерживается.")); editor.Children.Add(firePanel);
        macroPanel.Children.Add(Label("Имя макроса")); macroPanel.Children.Add(macroName);
        var modes = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        repeatMode.ItemsSource = new[] { "Заданное число повторов", "Пока кнопка удерживается", "До нажатия любой кнопки", "До повторного нажатия этой кнопки" };
        repeatMode.SelectionChanged += (_, _) => repeatCount.IsEnabled = repeatMode.SelectedIndex == 0;
        var rp = new StackPanel(); rp.Children.Add(Label("Режим повтора")); rp.Children.Add(repeatMode); modes.Children.Add(rp);
        var rc = new StackPanel(); rc.Children.Add(Label("Повторов, 1…250")); rc.Children.Add(repeatCount); modes.Children.Add(rc); macroPanel.Children.Add(modes);
        macroPanel.Children.Add(Label("События: клавиша, нажатие/отпускание и задержка после события, мс. До 70 событий."));
        events.AutoGenerateColumns = false; events.CanUserAddRows = true; events.CanUserDeleteRows = true;
        events.Height = 225; events.HeadersVisibility = DataGridHeadersVisibility.Column; events.RowHeaderWidth = 0;
        events.Columns.Add(new DataGridTextColumn { Header = "Клавиша", Binding = new Binding("Key"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        events.Columns.Add(new DataGridCheckBoxColumn { Header = "Нажатие ↓", Binding = new Binding("Down"), Width = 115 });
        events.Columns.Add(new DataGridTextColumn { Header = "Задержка, мс", Binding = new Binding("Delay"), Width = 130 });
        events.SetResourceReference(Control.BackgroundProperty, "CardBg"); events.SetResourceReference(Control.ForegroundProperty, "TextColor");
        macroPanel.Children.Add(events);
        var tools = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        tools.Children.Add(record); record.Click += (_, _) => ToggleRecording();
        AddButton(tools, "Добавить клик", (_, _) => { if (!recording && steps.Count <= 68) { steps.Add(new() { Key = "MouseLeft", Down = true, Delay = 20 }); steps.Add(new() { Key = "MouseLeft", Down = false, Delay = 20 }); } });
        AddButton(tools, "Очистить события", (_, _) => { StopRecording(); steps.Clear(); });
        AddButton(tools, "Импорт JSON", ImportMacro); AddButton(tools, "Экспорт JSON", ExportMacro);
        macroPanel.Children.Add(tools); macroPanel.Children.Add(Label("При записи окно перехватывает клавиши только пока оно активно. Esc завершает запись. Для редактирования используй A…Z, F1…F12, Ctrl, Shift, Space, MouseLeft и т. д."));
        editor.Children.Add(macroPanel);
        buttons.SelectedIndex = 0;
        PreviewKeyDown += CaptureDown; PreviewKeyUp += CaptureUp;
        Deactivated += (_, _) => StopRecording();
        Closing += (_, e) => { if (writing) { e.Cancel = true; return; } StopRecording(); };
    }
    private TextBlock Label(string text) { var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8), FontSize = 12 }; t.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return t; }
    private void AddButton(Panel panel, string text, RoutedEventHandler handler) { var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) }; b.Click += handler; panel.Children.Add(b); }
    private void BuildChoices()
    {
        choices.Add(new("Отключить кнопку", 0, 0));
        for (int i = 0; i < 5; i++) choices.Add(new(ButtonAssignment.Label(i), 1, 0x100 << i));
        choices.AddRange(new[] {
            new ActionChoice("DPI · следующая ступень", 2, 0x100), new("DPI +", 2, 0x200), new("DPI −", 2, 0x300),
            new("Переключить частоту опроса", 7, 0), new("Комбинация клавиш / мультимедиа", 5, 0), new("Макрос", 6, 0),
            new("Огонь · повтор левого клика", 4, 0), new("Прокрутка влево", 3, 0x100), new("Прокрутка вправо", 3, 0x200),
            new("Прокрутка вверх", 11, 0x100), new("Прокрутка вниз", 11, 0x200), new("Перетаскивание и прокрутка", 15, 0),
            new("Переключить подсветку", 8, 0), new("Переключить аппаратный профиль", 9, 0)
        });
    }
    private void SelectButton(object sender, SelectionChangedEventArgs args)
    {
        if (filling || buttons.SelectedIndex < 0) return;
        try {
            if (action.SelectedItem != null) Store();
            selected = buttons.SelectedIndex; Fill();
        } catch (Exception error) { filling = true; buttons.SelectedIndex = selected; filling = false; status.Text = error.Message; }
    }
    private void Fill()
    {
        filling = true;
        var b = assignments[selected];
        var option = choices.FirstOrDefault(c => c.Type == b.Type && (c.Type is 4 or 5 or 6 || c.Parameter == b.Parameter));
        if (option == null) { option = new($"Текущее назначение · {b.Type:X}/{b.Parameter:X4}", b.Type, b.Parameter); choices.Add(option); action.Items.Refresh(); }
        action.SelectedItem = option; shortcut.Text = b.Shortcut;
        macroName.Text = b.Macro?.Name ?? "Макрос " + (selected + 1);
        steps = new(b.Macro?.Steps ?? new List<MacroStep>()); events.ItemsSource = steps;
        int repeat = b.Parameter & 255;
        repeatMode.SelectedIndex = repeat == 254 ? 1 : repeat == 255 ? 2 : repeat == 253 ? 3 : 0;
        repeatCount.Text = (repeat is >= 1 and <= 250 ? repeat : 1).ToString();
        fireCount.Text = (b.Type == 4 ? b.Parameter >> 8 : 1).ToString(); fireDelay.Text = (b.Type == 4 ? b.Parameter & 255 : 50).ToString();
        filling = false; UpdatePanels();
    }
    private void UpdatePanels()
    {
        if (action.SelectedItem is not ActionChoice a) return;
        shortcutPanel.Visibility = a.Type == 5 ? Visibility.Visible : Visibility.Collapsed;
        macroPanel.Visibility = a.Type == 6 ? Visibility.Visible : Visibility.Collapsed;
        firePanel.Visibility = a.Type == 4 ? Visibility.Visible : Visibility.Collapsed;
        Height = a.Type == 6 ? 760 : a.Type is 4 or 5 ? 520 : 470;
        if (a.Type != 6) StopRecording();
    }
    private MouseMacro ReadMacro()
    {
        events.CommitEdit(DataGridEditingUnit.Cell, true); events.CommitEdit(DataGridEditingUnit.Row, true);
        var macro = new MouseMacro { Name = macroName.Text.Trim(), Steps = steps.ToList() }; HubCodec.EncodeMacro(macro); return macro;
    }
    private void Store()
    {
        StopRecording();
        if (action.SelectedItem is not ActionChoice a) return;
        var b = assignments[selected]; int parameter = a.Parameter;
        if (a.Type == 5) HubCodec.EncodeShortcut(shortcut.Text);
        if (a.Type == 6) {
            int count = repeatMode.SelectedIndex == 1 ? 254 : repeatMode.SelectedIndex == 2 ? 255 : repeatMode.SelectedIndex == 3 ? 253 : int.TryParse(repeatCount.Text, out int n) && n is >= 1 and <= 250 ? n : throw new ArgumentException("Повторов — от 1 до 250.");
            parameter = selected << 8 | count;
        }
        if (a.Type == 4) {
            if (!int.TryParse(fireCount.Text, out int count) || count is < 0 or > 3 || !int.TryParse(fireDelay.Text, out int delay) || delay is < 10 or > 255) throw new ArgumentException("Огонь: 0–3 клика, интервал 10–255 мс.");
            parameter = count << 8 | delay;
        }
        var macro = a.Type == 6 ? ReadMacro() : b.Macro;
        b.Type = a.Type; b.Parameter = parameter; b.Shortcut = shortcut.Text.Trim(); b.Macro = macro;
    }
    private async void Apply(object sender, RoutedEventArgs args)
    {
        try {
            Store(); HubCodec.ValidateButtons(assignments);
            writing = true; apply.IsEnabled = false; buttons.IsEnabled = false; action.IsEnabled = false;
            status.Text = "Записываю назначения и проверяю чтением…";
            if (Process.GetProcessesByName("ATTACK  SHARK MOUSE HUB").Any()) throw new IOException("Закрой официальный HUB.");
            using var device = await MouseDevice.Connect();
            await device.ApplyButtons(assignments);
            Applied = MainWindow.CloneButtons(await device.ReadButtons());
            status.Text = "Назначения записаны и проверены. Они работают после закрытия SharkDesk.";
        } catch (Exception error) { status.Text = error.Message; }
        finally { writing = false; apply.IsEnabled = true; buttons.IsEnabled = true; action.IsEnabled = true; }
    }
    private void ImportMacro(object sender, RoutedEventArgs args)
    {
        try {
            StopRecording(); var d = new Microsoft.Win32.OpenFileDialog { Filter = "Макрос JSON|*.json" };
            if (d.ShowDialog(this) != true) return;
            var macro = JsonSerializer.Deserialize<MouseMacro>(File.ReadAllText(d.FileName)) ?? throw new ArgumentException("Пустой макрос.");
            HubCodec.EncodeMacro(macro); macroName.Text = macro.Name; steps = new(macro.Steps); events.ItemsSource = steps;
            status.Text = "Макрос загружен в редактор.";
        } catch (Exception error) { status.Text = error.Message; }
    }
    private void ExportMacro(object sender, RoutedEventArgs args)
    {
        try {
            StopRecording(); var macro = ReadMacro();
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "Макрос JSON|*.json", FileName = "RS3-Ultra-macro.json" };
            if (d.ShowDialog(this) == true) File.WriteAllText(d.FileName, JsonSerializer.Serialize(macro, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception error) { status.Text = error.Message; }
    }
    private void ToggleRecording()
    {
        if (recording) { StopRecording(); return; }
        recording = true; held.Clear(); record.Content = "Остановить запись · Esc"; lastEvent = DateTimeOffset.UtcNow;
        status.Text = "Запись клавиш: нажимай клавиши в этом окне; Esc — стоп."; Focus();
    }
    private void StopRecording()
    {
        if (!recording) return;
        foreach (Key key in held.ToArray()) if (steps.Count < 70) AddKey(key, false);
        held.Clear(); recording = false; record.Content = "Записать клавиши";
    }
    private static string KeyName(Key key) => key switch {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num" + ((int)key - (int)Key.NumPad0),
        Key.OemMinus => "Minus", Key.OemPlus => "Plus", Key.OemComma => "Comma", Key.OemPeriod => "Period", Key.OemQuestion => "Slash",
        Key.Oem1 => "Semicolon", Key.OemQuotes => "Quote", Key.OemOpenBrackets => "LeftBracket", Key.OemCloseBrackets => "RightBracket", Key.OemPipe => "Backslash", Key.OemTilde => "Tilde",
        _ => key.ToString()
    };
    private void CaptureDown(object sender, KeyEventArgs args)
    {
        if (!recording) return;
        args.Handled = true; Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key == Key.Escape) { StopRecording(); status.Text = "Запись завершена. Проверь события и примени назначения."; return; }
        if (args.IsRepeat || held.Contains(key)) return;
        try {
            HidKeys.Parse(KeyName(key));
            if (steps.Count + held.Count + 2 > 70) { StopRecording(); status.Text = "Достигнут предел 70 событий."; return; }
            held.Add(key); AddKey(key, true);
        } catch (ArgumentException error) { status.Text = error.Message; }
    }
    private void CaptureUp(object sender, KeyEventArgs args)
    {
        if (!recording) return;
        args.Handled = true; Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (held.Remove(key)) AddKey(key, false);
    }
    private void AddKey(Key key, bool down)
    {
        var now = DateTimeOffset.UtcNow;
        if (steps.Count > 0) steps[^1].Delay = Math.Clamp((int)(now - lastEvent).TotalMilliseconds, 0, 65535);
        steps.Add(new() { Key = KeyName(key), Down = down, Delay = 10 });
        lastEvent = now; events.Items.Refresh(); events.ScrollIntoView(steps[^1]);
    }
}
