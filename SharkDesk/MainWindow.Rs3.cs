using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SharkDesk;

public partial class MainWindow
{
    private void FillProfileLighting(MouseSettings profile)
    {
        bool wasFilling = filling; filling = true;
        LightModeBox.SelectedIndex = profile.LedMode; BrightnessSlider.Value = profile.LedBrightness; SpeedSlider.Value = profile.LedSpeed;
        filling = wasFilling; lightingDirty = true;
    }
    private readonly TextBox[] yDpiBoxes = new TextBox[6];
    private readonly TextBox[] colorBoxes = new TextBox[6];
    private ComboBox sensorMode, competitionTime, onboardProfile;
    private Button switchProfile;
    private CheckBox competition, fps20k, longRange, angleEnabled;
    private TextBox sensorAngle, wheelDebounce, releaseDebounce;
    private AdvancedSettings advancedCapabilities;
    private ButtonAssignment[]? buttonSnapshot;

    private TextBlock Rs3Label(string text)
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 6) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return label;
    }
    private Border Rs3Card(StackPanel content) => new Border { Style = (Style)FindResource("Card"), Child = content, Margin = new Thickness(0, 0, 0, 16) };
    private void InitRs3Controls()
    {
        ModelHeading.Text = "RS3 Ultra";
        ModelSidebar.Text = "RS3 Ultra";
        SensorSidebar.Text = "PAW3955";
        Title = "SharkDesk · RS3 Ultra";
        for (int i = 0; i < 6; i++) {
            yDpiBoxes[i] = new TextBox { MinWidth = 60, FontSize = 16 };
            yDpiBoxes[i].TextChanged += Edited;
            colorBoxes[i] = new TextBox { MaxLength = 7, FontSize = 12, MinWidth = 60 };
            colorBoxes[i].TextChanged += Edited;
        }
        InitDpiEditor();
        var content = new StackPanel();
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 8, 0, 0) };
        StackPanel Field(string title, Control input) {
            var p = new StackPanel { Margin = new Thickness(0, 0, 16, 14) }; p.Children.Add(Rs3Label(title)); p.Children.Add(input); grid.Children.Add(p); return p;
        }
        sensorMode = new ComboBox { ItemsSource = new[] { "LP · экономичный", "HP · производительный" } };
        Field("Режим сенсора", sensorMode);
        sensorMode.ToolTip = "LP/HP выбирается при 125–1000 Гц. При высоких частотах и 20K FPS прошивка выбирает режим автоматически.";
        sensorAngle = new TextBox(); Field("Поворот сенсора, −30…30°", sensorAngle);
        wheelDebounce = new TextBox(); Field("Задержка колеса, мс", wheelDebounce);
        releaseDebounce = new TextBox(); Field("Задержка отпускания кнопки, 0…15 мс", releaseDebounce);
        competitionTime = new ComboBox { ItemsSource = new[] { 10, 30, 60, 120, 300, 600, 900 } };
        Field("Время соревновательного режима, с", competitionTime);
        content.Children.Add(grid);
        competition = new CheckBox { Content = "Соревновательный режим" };
        fps20k = new CheckBox { Content = "Сканирование сенсора 20K FPS" };
        longRange = new CheckBox { Content = "Дальняя связь", ToolTip = "Увеличивает дальность и расход батареи." };
        angleEnabled = new CheckBox { Content = "Применять поворот сенсора" };
        foreach (var check in new[] { competition, fps20k, longRange, angleEnabled }) { check.Click += Edited; content.Children.Add(check); }
        foreach (var input in new[] { sensorAngle, wheelDebounce, releaseDebounce }) input.TextChanged += Edited;
        sensorMode.SelectionChanged += Edited; competitionTime.SelectionChanged += Edited;
        AdvancedHost.Children.Add(Rs3Card(content));
        var profiles = new StackPanel();
        profiles.Children.Add(new TextBlock { Text = "Профили в памяти мыши", FontSize = 19, FontWeight = FontWeights.SemiBold });
        profiles.Children.Add(Rs3Label("Переключение сразу меняет активную конфигурацию устройства. Доступно, если прошивка подтверждает поддержку."));
        onboardProfile = new ComboBox { ItemsSource = new[] { "Профиль 1", "Профиль 2", "Профиль 3", "Профиль 4" }, IsEnabled = false };
        profiles.Children.Add(onboardProfile);
        switchProfile = new Button { Content = "Переключить профиль мыши", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        switchProfile.Click += SwitchOnboardProfile; profiles.Children.Add(switchProfile);
        ProfilesPage.Children.Insert(0, Rs3Card(profiles));
        var navPanel = (StackPanel)SensorNav.Parent;
        var nav = new Button { Content = "Кнопки и макросы", HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        nav.Click += OpenButtonEditor; navPanel.Children.Insert(navPanel.Children.IndexOf(SensorNav) + 1, nav);
        var backup = new Button { Content = "Сохранить резервную копию мыши", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        backup.Click += SaveBackup; ((StackPanel)((Border)SettingsPage.Children[1]).Child).Children.Add(backup);
    }

    private void FillRs3(MouseSettings settings)
    {
        for (int i = 0; i < 6; i++) {
            yDpiBoxes[i].Text = (settings.DpiY?[i] ?? settings.Dpi[i]).ToString();
            colorBoxes[i].Text = settings.DpiColors?[i] ?? new[] { "#FF0000", "#00FF00", "#0000FF", "#FFFF00", "#00FFFF", "#FF00FF" }[i];
        }
        var a = settings.Advanced ?? new AdvancedSettings();
        advancedCapabilities = a;
        sensorMode.IsEnabled = a.SensorMode.HasValue;
        sensorMode.SelectedIndex = a.SensorMode ?? -1;
        sensorAngle.IsEnabled = a.Angle.HasValue; sensorAngle.Text = a.Angle?.ToString() ?? "";
        wheelDebounce.IsEnabled = a.WheelDebounce.HasValue; wheelDebounce.Text = a.WheelDebounce?.ToString() ?? "";
        releaseDebounce.IsEnabled = a.ReleaseDebounce.HasValue; releaseDebounce.Text = a.ReleaseDebounce?.ToString() ?? "";
        competitionTime.IsEnabled = a.CompetitiveSeconds.HasValue;
        if (a.CompetitiveSeconds.HasValue && !competitionTime.Items.Contains(a.CompetitiveSeconds.Value))
            competitionTime.ItemsSource = new[] { 10, 30, 60, 120, 300, 600, 900, a.CompetitiveSeconds.Value }.Distinct().OrderBy(x => x).ToArray();
        competitionTime.SelectedItem = a.CompetitiveSeconds;
        void Check(CheckBox control, bool? value) { control.IsEnabled = value.HasValue; control.IsChecked = value; }
        Check(competition, a.Competitive); Check(fps20k, a.Fps20K); Check(longRange, a.LongRange); Check(angleEnabled, a.AngleEnabled);
        if (settings.Buttons != null) buttonSnapshot = CloneButtons(settings.Buttons);
        onboardProfile.IsEnabled = switchProfile.IsEnabled = settings.OnboardProfile.HasValue;
        onboardProfile.SelectedIndex = settings.OnboardProfile ?? -1;
        RefreshDpiEditor();
    }

    private void CollectRs3(MouseSettings settings)
    {
        int Number(TextBox text) => int.TryParse(text.Text, out int value) ? value : throw new ArgumentException("Введи целое число: " + text.Text);
        settings.DpiY = yDpiBoxes.Select(Number).ToArray(); settings.DpiColors = colorBoxes.Select(c => c.Text.Trim().ToUpperInvariant()).ToArray();
        var a = advancedCapabilities ?? new();
        settings.Advanced = new() {
            SensorMode = a.SensorMode.HasValue ? sensorMode.SelectedIndex : null,
            Competitive = a.Competitive.HasValue ? competition.IsChecked : null,
            CompetitiveSeconds = a.CompetitiveSeconds.HasValue ? (int?)competitionTime.SelectedItem : null,
            Angle = a.Angle.HasValue ? Number(sensorAngle) : null, AngleEnabled = a.AngleEnabled.HasValue ? angleEnabled.IsChecked : null,
            Fps20K = a.Fps20K.HasValue ? fps20k.IsChecked : null, LongRange = a.LongRange.HasValue ? longRange.IsChecked : null,
            WheelDebounce = a.WheelDebounce.HasValue ? Number(wheelDebounce) : null,
            ReleaseDebounce = a.ReleaseDebounce.HasValue ? Number(releaseDebounce) : null
        };
        settings.Buttons = buttonSnapshot == null ? null : CloneButtons(buttonSnapshot);
        settings.LedMode = Math.Max(0, LightModeBox.SelectedIndex);
        settings.LedBrightness = (int)BrightnessSlider.Value; settings.LedSpeed = (int)SpeedSlider.Value;
    }

    internal static ButtonAssignment[] CloneButtons(ButtonAssignment[] buttons) => JsonSerializer.Deserialize<ButtonAssignment[]>(JsonSerializer.Serialize(buttons))!;

    private async void SwitchOnboardProfile(object sender, RoutedEventArgs args)
    {
        if (busy || onboardProfile.SelectedIndex < 0) return;
        if (mouseDirty && MessageBox.Show(this, "Переключить профиль и заменить несохранённые изменения?", "RS3 Ultra", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Busy(true); await deviceGate.WaitAsync();
        try {
            using var device = await MouseDevice.Connect(); await device.SelectOnboardProfile(onboardProfile.SelectedIndex);
            buttonSnapshot = await device.ReadButtons(); lastRead = await device.ReadSettings(); Fill(lastRead); SyncDeviceExtras(lastRead); mouseDirty = lightingDirty = false;
            StatusText.Text = "Профиль мыши переключён, настройки перечитаны.";
        } catch (Exception error) { ShowError(error); }
        finally { deviceGate.Release(); Busy(false); }
    }

    private async void OpenButtonEditor(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        Busy(true); await deviceGate.WaitAsync();
        try {
            if (Process.GetProcessesByName("ATTACK  SHARK MOUSE HUB").Any()) throw new IOException("Сначала закрой официальный HUB.");
            using var device = await MouseDevice.Connect();
            buttonSnapshot = await device.ReadButtons();
            monitorPaused = true;
            var editor = new ButtonsWindow(CloneButtons(buttonSnapshot)) { Owner = this };
            editor.ShowDialog();
            if (editor.Applied != null) { buttonSnapshot = CloneButtons(editor.Applied); if (lastRead != null) lastRead.Buttons = CloneButtons(buttonSnapshot); }
        } catch (Exception error) { ShowError(error); }
        finally { monitorPaused = false; deviceGate.Release(); Busy(false); }
    }

    private async void SaveBackup(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        Busy(true); await deviceGate.WaitAsync();
        try {
            if (Process.GetProcessesByName("ATTACK  SHARK MOUSE HUB").Any()) throw new IOException("Сначала закрой официальный HUB.");
            using var device = await MouseDevice.Connect();
            var snapshot = new { model = "ATTACK SHARK RS3 Ultra", cid = 124, mid = 14, at = DateTimeOffset.UtcNow, settings = await device.ReadSettings(), buttons = await device.ReadButtons(), memory = await device.ReadBackup() };
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "RS3-Ultra-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", Filter = "Резервная копия JSON|*.json" };
            if (dialog.ShowDialog(this) == true) { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(snapshot, Json)); StatusText.Text = "Резервная копия сохранена."; }
        } catch (Exception error) { ShowError(error); }
        finally { deviceGate.Release(); Busy(false); }
    }
}
