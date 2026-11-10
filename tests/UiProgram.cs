using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SharkDesk;

internal static class UiProgram
{
    private static int assertions;
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    private static object Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, values);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Expect(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Layout(Window window) { window.UpdateLayout(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout(); }
    private static void Capture(Window window, string file)
    {
        Layout(window); var visual = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        int width = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(visual.ActualWidth + visual.Margin.Left + visual.Margin.Right));
        int height = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(visual.ActualHeight + visual.Margin.Top + visual.Margin.Bottom));
        var client = new CroppedBitmap(bitmap, new Int32Rect(0, 0, width, height));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(client)); using var stream = File.Create(file); encoder.Save(stream);
    }
    [STAThread]
    private static void Main()
    {
        string directory = Path.GetFullPath("dist/qa"); Directory.CreateDirectory(directory);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow { Width = 1000, Height = 720 };
        Set(window, "monitorPaused", true); Set(window, "darkTheme", false); Call(window, "SetTheme");
        app.MainWindow = window;
        var settings = new MouseSettings { Count = 3, Active = 0, Polling = 4000, Debounce = 0, Firmware = "v5.06", DpiY = new[] { 800, 2400, 3200, 5600, 8000, 52000 }, DpiColors = new[] { "#FFFFFF", "#00FF00", "#0055FF", "#FFFF00", "#00FFFF", "#FF00FF" }, OnboardProfile = 0, Advanced = new() { SensorMode = 0, Competitive = false, CompetitiveSeconds = 60, Angle = 0, AngleEnabled = false, Fps20K = false, LongRange = false, WheelDebounce = 30, ReleaseDebounce = 0 } };
        Call(window, "Fill", settings); Call(window, "SyncDeviceExtras", settings, true);
        Set(window, "battery", new BatteryStatus(55, false, 3900, false, DateTimeOffset.UtcNow)); Set(window, "batteryDisplay", new BatteryDisplay(55, false, 55)); Set(window, "latestIsReceiver", (bool?)true);
        Call(window, "UpdateBattery", false);
        window.Show(); Layout(window);
        ((TextBlock)Field(window, "StatusText")).Text = "Подключена";
        var scroll = (ScrollViewer)Field(window, "MainScroll");
        Expect(scroll.ExtentHeight <= scroll.ViewportHeight + 1, $"DPI page scrolls at 1000×720: {scroll.ExtentHeight}/{scroll.ViewportHeight}");
        Expect(((TextBlock)Field(window, "BatteryRemaining")).IsVisible == false, "Battery diagnostic text is visible");
        Capture(window, Path.Combine(directory, "sensitivity-light.png"));
        Call(window, "ChooseStage", 1); Call(window, "SetDpiValue", 1600);
        var collected = (MouseSettings)Call(window, "Collect");
        Expect(collected.Active == 1 && collected.Dpi[1] == 1600 && collected.DpiY[1] == 1600 && collected.Dpi[0] == 800, "Stage editing modified the wrong stage or broke linked axes");
        var axes = (CheckBox)Field(window, "separateAxes"); axes.IsChecked = true; axes.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        ((TextBox)Field(window, "dpiValueY")).Text = "2000"; collected = (MouseSettings)Call(window, "Collect");
        Expect(collected.Dpi[1] == 1600 && collected.DpiY[1] == 2000, "Separate axis editing failed");
        Layout(window); Expect(scroll.ExtentHeight <= scroll.ViewportHeight + 1, $"Split-axis DPI page scrolls at minimum size: {scroll.ExtentHeight}/{scroll.ViewportHeight}");
        ((ComboBox)Field(window, "CountBox")).SelectedItem = 1; collected = (MouseSettings)Call(window, "Collect");
        Expect(collected.Count == 1 && collected.Active == 0, "Shrinking stage count left an invalid active stage");
        Call(window, "Fill", settings); Set(window, "darkTheme", true); Call(window, "SetTheme");
        Capture(window, Path.Combine(directory, "sensitivity-dark.png"));
        Set(window, "darkTheme", false); Call(window, "SetTheme");
        var picker = new ColorPickerWindow("#5262D8") { Owner = window }; picker.Show(); Layout(picker);
        Expect(picker.SelectedColor == "#5262D8", "Picker failed to preserve its initial RGB color");
        ((Slider)Field(picker, "hue")).Value = 180; Call(picker, "SelectPoint", new Point(170, 85));
        Expect(picker.SelectedColor.StartsWith("#") && picker.SelectedColor.Length == 7 && picker.SelectedColor != "#5262D8", "HSV picker did not update color");
        Capture(picker, Path.Combine(directory, "color-picker.png")); picker.Close();
        var buttons = Enumerable.Range(0, 5).Select(i => new ButtonAssignment { Index = i, Type = 1, Parameter = 0x100 << i }).ToArray();
        var editor = new ButtonsWindow(buttons) { Owner = window }; editor.Show(); Layout(editor);
        Capture(editor, Path.Combine(directory, "buttons.png")); editor.Close();
        var tab = ((StackPanel)Field(window, "SensorPage")).Children.OfType<TabControl>().Single(); tab.SelectedIndex = 1;
        Capture(window, Path.Combine(directory, "sensor.png"));
        window.Close(); app.Shutdown(); Console.WriteLine("Passed " + assertions + " UI checks. Previews: " + directory);
    }
}
