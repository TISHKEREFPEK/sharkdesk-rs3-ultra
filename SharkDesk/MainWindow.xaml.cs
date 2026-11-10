using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SharkDesk;

public partial class MainWindow : Window, IComponentConnector
{
	private DateTimeOffset nextMouseRead;

	private DateTimeOffset nextDongleRead;

	private int[]? lastDongleModes;

	private int? lastBatteryModelId;

	private bool officialHandoff;

	private readonly SemaphoreSlim deviceGate = new SemaphoreSlim(1, 1);

	private readonly DispatcherTimer batteryTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromSeconds(1L)
	};

	private readonly List<Button> helpButtons = new List<Button>();

	private readonly List<(FrameworkElement Control, string Key)> helpTargets = new List<(FrameworkElement, string)>();

	private bool english;

	private bool ready;

	private bool closing;

	private bool monitorPaused;

	private bool dongleSupported;

	private BatteryStatus? battery;

	private readonly BatteryDisplayFilter batteryDisplayFilter = new BatteryDisplayFilter();

	private BatteryDisplay? batteryDisplay;

	private readonly BatteryRuntimeEstimator runtimeEstimator = new BatteryRuntimeEstimator(System.IO.Path.Combine(AppPaths.Data, "battery-runtime.json"));

	private string mouseVersion = "—";

	private string dongleVersion = "—";

	private bool? latestIsReceiver;

	private readonly TextBox[] dpiBoxes = new TextBox[6];

	private readonly RadioButton[] stages = new RadioButton[6];

	private bool filling = true;

	private bool busy;

	private bool connected;

	private int maxRate = 8000;

	private MouseSettings? lastRead;

	private MouseSpec currentSpec = MouseSpec.Find(124, 14);

	private bool mouseDirty;

	private bool lightingDirty;

	private bool dongleDirty;

	private long editorRevision;

	private bool darkTheme;

	private int currentPage;

	private readonly string profileDir = System.IO.Path.Combine(AppPaths.Data, "profiles");

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private string RuntimeContext
	{
		get
		{
			string[] array = new string[5]
			{
				(lastBatteryModelId ?? 14).ToString(),
				":",
				(latestIsReceiver ?? true) ? "wireless" : "wired",
				":",
				null
			};
			MouseSettings mouseSettings = lastRead;
			array[4] = ((mouseSettings != null && mouseSettings.ModelId == lastBatteryModelId) ? mouseSettings.Polling : 0).ToString();
			return string.Concat(array);
		}
	}

	private void SetCapabilities(MouseSpec spec)
	{
		currentSpec = spec;
		int selectedIndex = LodBox.SelectedIndex;
		LodBox.ItemsSource = spec.LodNames.Select((string value) => value + L(" мм", " mm")).ToArray();
		LodBox.SelectedIndex = Math.Clamp(selectedIndex, 0, spec.LodCodes.Length - 1);
		DpiLimitsLabel.Text = ((spec.Sensor == "3950") ? L($"50–30 000 DPI: шаг 50 · выше — шаг 100, максимум {spec.MaxDpi:N0}", $"50–30,000 DPI: steps of 50 · above: steps of 100, up to {spec.MaxDpi:N0}") : L($"1–42 000 DPI: шаг 1 · выше — шаг 2, максимум {spec.MaxDpi:N0}", $"1–42,000 DPI: steps of 1 · above: steps of 2, up to {spec.MaxDpi:N0}"));
		if (ready || lastRead != null)
		{
			ModelHeading.Text = spec.Name;
			ModelSidebar.Text = spec.Name;
			SensorSidebar.Text = "PAW" + spec.Sensor;
			Title = "SharkDesk · " + spec.Name;
		}
	}

	private async Task AutoReadMouse(MouseDevice device)
	{
		bool sameModel = currentSpec.Mid == device.Spec.Mid;
		if ((DateTimeOffset.UtcNow < nextMouseRead && connected) & sameModel)
		{
			return;
		}
		nextMouseRead = DateTimeOffset.UtcNow.AddSeconds(6.0);
		bool dirtyBefore = mouseDirty;
		bool lightDirtyBefore = lightingDirty;
		long revision = editorRevision;
		try
		{
			MouseSettings mouseSettings = await device.ReadSettings();
			if (buttonSnapshot == null || !mouseDirty) buttonSnapshot = await device.ReadButtons();
			if (closing)
			{
				return;
			}
			if (!sameModel && (mouseDirty || lightingDirty))
			{
				connected = false;
				StatusText.Text = L("Подключена другая модель. Твои правки сохранены в редакторе; нажми «Обновить мышь», чтобы заменить их.", "A different model is connected. Editor changes were preserved; click Refresh mouse to replace them.");
				return;
			}
			connected = true;
			maxRate = mouseSettings.MaxRate;
			if (!busy && AutoReadPolicy.CanReplace(dirtyBefore, mouseDirty, revision, editorRevision))
			{
				lastRead = mouseSettings;
				Fill(mouseSettings);
				mouseDirty = false;
				DeviceSummary.Text = $"{mouseSettings.Dpi[mouseSettings.Active]:N0} DPI   /   {mouseSettings.Polling:N0} " + L("Гц", "Hz") + $"   /   LOD {LodBox.SelectedItem}";
				if (!lightingDirty && !dongleDirty)
				{
					StatusText.Text = (device.Spec.Experimental ? L("Модель определена. Поддержка этой модели экспериментальная.", "Model detected. Support for this model is experimental.") : L("Подключена", "Connected"));
				}
			}
			bool updateLighting = !busy && AutoReadPolicy.CanReplace(lightDirtyBefore, lightingDirty, revision, editorRevision);
			if (!busy)
			{
				SyncDeviceExtras(mouseSettings, updateLighting);
			}
			nextMouseRead = DateTimeOffset.UtcNow.AddSeconds(30.0);
		}
		catch (Exception ex)
		{
			if (lastRead == null && !mouseDirty && !busy)
			{
				StatusText.Text = ErrorText(ex);
			}
		}
		finally
		{
			if (!busy)
			{
				ApplyButton.IsEnabled = connected;
				LightingApplyButton.IsEnabled = connected;
			}
		}
	}

	private async Task AutoReadDongle(MouseDevice mouse)
	{
		if (DateTimeOffset.UtcNow < nextDongleRead)
		{
			return;
		}
		nextDongleRead = DateTimeOffset.UtcNow.AddSeconds(6.0);
		bool dirtyBefore = dongleDirty;
		long revision = editorRevision;
		try
		{
			DongleSettings dongleSettings;
			if (mouse.IsReceiver)
			{
				dongleSettings = await mouse.ReadDongle();
			}
			else
			{
				using MouseDevice receiver = await MouseDevice.Connect(receiverOnly: true);
				if (receiver.Spec.Mid != mouse.Spec.Mid)
				{
					return;
				}
				dongleSettings = await receiver.ReadDongle();
			}
			if (!closing)
			{
				dongleVersion = dongleSettings.Firmware;
				UpdateVersions();
				dongleSupported = dongleSettings.LedModes != null;
				if (!busy && AutoReadPolicy.CanReplace(dirtyBefore, dongleDirty, revision, editorRevision))
				{
					LoadDongle(dongleSettings);
				}
				nextDongleRead = DateTimeOffset.UtcNow.AddSeconds(30.0);
			}
		}
		catch (Exception ex)
		{
			dongleSupported = false;
			if (!dongleDirty)
			{
				DongleStatus.Text = L("Приёмник пока не ответил. Повторю подключение автоматически.", "Receiver has not responded yet. Reconnecting automatically.");
			}
			DongleStatus.ToolTip = ex.Message;
		}
		finally
		{
			if (!busy)
			{
				DongleApplyButton.IsEnabled = dongleSupported;
			}
		}
	}

	private void LoadDongle(DongleSettings settings)
	{
		bool flag = filling;
		filling = true;
		lastDongleModes = settings.LedModes;
		ComboBox[] array = new ComboBox[3] { Led1Box, Led2Box, Led3Box };
		for (int i = 0; i < 3; i++)
		{
			array[i].IsEnabled = settings.LedModes != null;
			ComboBox obj = array[i];
			int[]? ledModes = settings.LedModes;
			obj.SelectedIndex = ((ledModes != null) ? ledModes[i] : (-1));
		}
		filling = flag;
		dongleDirty = false;
		DongleStatus.Text = ((settings.LedModes != null) ? L("Донгл подключён. Назначения LED прочитаны автоматически.", "Dongle connected. LED assignments read automatically.") : L("Приёмник подключён; три LED не поддерживаются.", "Receiver connected; three LEDs are not supported."));
	}

	private void DongleEdited(object sender, SelectionChangedEventArgs e)
	{
		if (ready && !filling)
		{
			dongleDirty = true;
			editorRevision++;
		}
	}

	private void LightingValueEdited(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		if (ready && !filling)
		{
			lightingDirty = true;
			editorRevision++;
		}
	}

	private string L(string ru, string en)
	{
		if (!english)
		{
			return ru;
		}
		return en;
	}

	private void InitExtras()
	{
		bool flag = filling;
		filling = true;
		try
		{
			english = File.ReadAllText(System.IO.Path.Combine(AppPaths.Data, "language.txt")).Trim() == "en";
		}
		catch (IOException)
		{
		}
		LanguageBox.ItemsSource = new string[2] { "Русский", "English" };
		LanguageBox.SelectedIndex = (english ? 1 : 0);
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
		{
			StartupBox.IsChecked = registryKey?.GetValue("SharkDesk-RS3-Ultra") is string;
		}
		ApplyLanguage();
		AddControlHelp(CountBox, "dpi");
		AddControlHelp(RateBox, "rate");
		AddControlHelp(LodBox, "lod");
		AddControlHelp(DebounceBox, "debounce");
		AddControlHelp(SyncBox, "sync");
		AddControlHelp(AngleBox, "angle");
		AddControlHelp(RippleBox, "ripple");
		AddControlHelp(SleepBox, "sleep");
		AddControlHelp(Led1Box, "dongle");
		AddControlHelp(Led2Box, "dongle");
		AddControlHelp(Led3Box, "dongle");
		AddControlHelp(LightModeBox, "light");
		ComboBox[] array = new ComboBox[3] { Led1Box, Led2Box, Led3Box };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SelectionChanged += DongleEdited;
		}
		BrightnessSlider.ValueChanged += LightingValueEdited;
		SpeedSlider.ValueChanged += LightingValueEdited;
		LightingEdited(this, new RoutedEventArgs());
		ready = true;
		filling = flag;
		PreviewMouseDown += (object _, MouseButtonEventArgs e) =>
		{
			for (DependencyObject dependencyObject = e.OriginalSource as DependencyObject; dependencyObject != null; dependencyObject = ((dependencyObject is Visual) ? VisualTreeHelper.GetParent(dependencyObject) : LogicalTreeHelper.GetParent(dependencyObject)))
			{
				if (dependencyObject is Button item && helpButtons.Contains(item))
				{
					return;
				}
			}
			CloseHelp();
		};
		batteryTimer.Tick += async (object? _, EventArgs _) =>
		{
			await PollBattery();
		};
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			batteryTimer.Start();
			await PollBattery();
		};
		Closed += (object? _, EventArgs _) =>
		{
			closing = true;
			batteryTimer.Stop();
			runtimeEstimator.Save();
		};
	}

	private void ApplyLanguage()
	{
		using Stream utf8Json = Assembly.GetExecutingAssembly().GetManifestResourceStream("SharkDesk.Languages." + (english ? "en" : "ru") + ".json");
		foreach (KeyValuePair<string, string> item3 in JsonSerializer.Deserialize<Dictionary<string, string>>(utf8Json))
		{
			Resources[item3.Key] = item3.Value;
		}
		int selectedIndex = LodBox.SelectedIndex;
		int selectedIndex2 = SleepBox.SelectedIndex;
		LodBox.ItemsSource = currentSpec.LodNames.Select((string value) => value + L(" мм", " mm")).ToArray();
		LodBox.SelectedIndex = selectedIndex;
		SleepBox.ItemsSource = ((!english) ? new string[7] { "10 секунд", "30 секунд", "1 минута", "2 минуты", "5 минут", "10 минут", "15 минут" } : new string[7] { "10 seconds", "30 seconds", "1 minute", "2 minutes", "5 minutes", "10 minutes", "15 minutes" });
		SleepBox.SelectedIndex = selectedIndex2;
		ComboBox[] array = new ComboBox[3] { Led1Box, Led2Box, Led3Box };
		foreach (ComboBox comboBox in array)
		{
			int selectedIndex3 = comboBox.SelectedIndex;
			comboBox.ItemsSource = (english ? new string[4] { "Off", "Connection quality", "Mouse battery", "Polling rate" } : new string[4] { "Выключен", "Качество связи", "Заряд мыши", "Частота опроса" });
			comboBox.SelectedIndex = selectedIndex3;
			comboBox.IsEnabled = dongleSupported;
		}
		int selectedIndex4 = LightModeBox.SelectedIndex;
		LightModeBox.ItemsSource = ((!english) ? new string[3] { "Выключен", "Постоянный", "Дыхание" } : new string[3] { "Off", "Always on", "Breathing" });
		LightModeBox.SelectedIndex = ((selectedIndex4 >= 0) ? selectedIndex4 : 0);
		for (int num2 = 0; num2 < 6; num2++)
		{
			stages[num2].Content = L($"Ступень {num2 + 1}", $"Stage {num2 + 1}");
		}
		foreach (Button helpButton in helpButtons)
		{
			if (helpButton.ToolTip is ToolTip toolTip)
			{
				toolTip.IsOpen = false;
			}
			helpButton.ToolTip = MakeTip((string)helpButton.Tag);
		}
		foreach (var helpTarget in helpTargets)
		{
			FrameworkElement item = helpTarget.Control;
			string item2 = helpTarget.Key;
			item.ToolTip = MakeTip(item2);
		}
		ThemeButton.Content = (darkTheme ? L("Светлая тема", "Light theme") : L("Тёмная тема", "Dark theme"));
		Page(currentPage);
		UpdateVersions();
		UpdateBattery(failed: false);
		if (lastRead != null)
		{
			SetCapabilities(currentSpec);
		}
		MouseSettings mouseSettings = lastRead;
		if (mouseSettings != null)
		{
			DeviceSummary.Text = $"{mouseSettings.Dpi[mouseSettings.Active]:N0} DPI   /   {mouseSettings.Polling:N0} " + L("Гц", "Hz") + $"   /   LOD {LodBox.SelectedItem}";
			FirmwareLabel.Text = L("Прошивка ", "Firmware ") + mouseSettings.Firmware;
		}
	}

	private void LanguageChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!ready)
		{
			return;
		}
		english = LanguageBox.SelectedIndex == 1;
		bool flag = filling;
		filling = true;
		string text = ProfileName.Text;
		ApplyLanguage();
		ProfileName.Text = text;
		filling = flag;
		try
		{
			File.WriteAllText(System.IO.Path.Combine(AppPaths.Data, "language.txt"), english ? "en" : "ru");
			StatusText.Text = L("Язык изменён. Настройки мыши не затронуты.", "Language changed. Mouse settings were preserved.");
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private Button HelpButton(string key)
	{
		System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
		{
			Data = Geometry.Parse("M 8.1,6.7 C 8.1,3.5 13.5,3.5 13.5,6.8 C 13.5,8.5 11,8.8 11,10.6 M 11,14 L 11,14.2"),
			StrokeThickness = 1.6,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			Width = 22.0,
			Height = 22.0,
			Stretch = Stretch.None,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		path.SetResourceReference(Shape.StrokeProperty, "TextColor");
		Button button = new Button
		{
			Content = path,
			Tag = key,
			Width = 22.0,
			Height = 22.0,
			Padding = new Thickness(0.0),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		button.ToolTip = MakeTip(key);
		ToolTipService.SetInitialShowDelay(button, 250);
		ToolTipService.SetShowDuration(button, 30000);
		button.Click += (object _, RoutedEventArgs _) =>
		{
			ToolTip toolTip = (ToolTip)button.ToolTip;
			toolTip.PlacementTarget = button;
			toolTip.IsOpen = !toolTip.IsOpen;
		};
		AutomationProperties.SetName(button, "? " + key);
		helpButtons.Add(button);
		return button;
	}

	private void CloseHelp()
	{
		foreach (Button helpButton in helpButtons)
		{
			if (helpButton.ToolTip is ToolTip toolTip)
			{
				toolTip.IsOpen = false;
			}
		}
	}

	private ToolTip MakeTip(string key)
	{
		return new ToolTip
		{
			Content = new TextBlock
			{
				Text = HelpText(key),
				MaxWidth = 340.0,
				TextWrapping = TextWrapping.Wrap,
				LineHeight = 21.0
			},
			Padding = new Thickness(14.0)
		};
	}

	private void AddLabelHelp(FrameworkElement control, string key)
	{
		helpTargets.Add((control, key));
		if (control.Parent is Panel panel)
		{
			int num = panel.Children.IndexOf(control);
			if (num < 1 || !(panel.Children[num - 1] is TextBlock textBlock))
			{
				control.ToolTip = MakeTip(key);
				return;
			}
			panel.Children.RemoveAt(num - 1);
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Margin = ((panel is StackPanel { Orientation: Orientation.Horizontal }) ? new Thickness(0.0, 0.0, 12.0, 0.0) : new Thickness(0.0, 0.0, 0.0, 8.0)),
				VerticalAlignment = VerticalAlignment.Center
			};
			textBlock.Margin = new Thickness(0.0);
			textBlock.VerticalAlignment = VerticalAlignment.Center;
			stackPanel.Children.Add(textBlock);
			stackPanel.Children.Add(HelpButton(key));
			panel.Children.Insert(num - 1, stackPanel);
			control.ToolTip = MakeTip(key);
		}
	}

	private void AddControlHelp(FrameworkElement control, string key)
	{
		helpTargets.Add((control, key));
		control.ToolTip = MakeTip(key);
	}

	private string HelpText(string key)
	{
		return key switch
		{
			"dpi" => L("DPI — чувствительность сенсора. Больше DPI — курсор проходит большее расстояние при том же движении руки. Выбери активную ступень и число доступных ступеней; запись выполняется кнопкой «Применить к мыши».", "DPI controls sensor sensitivity. A higher value moves the cursor farther for the same physical movement. Select the active stage and how many stages are enabled, then click Apply to mouse."),
			"rate" => L("Частота опроса — сколько отчётов мышь передаёт в секунду. 1000 Гц — интервал 1 мс, 8000 Гц — 0,125 мс. Высокая частота расходует больше энергии и ресурсов компьютера. Доступный максимум зависит от подключения.", "Polling rate is the number of reports per second: 1000 Hz means a 1 ms interval; 8000 Hz means 0.125 ms. Higher rates use more battery and CPU. The maximum depends on the connection."),
			"lod" => L("LOD — высота отрыва, на которой сенсор прекращает отслеживание. Низкое значение уменьшает движение курсора, когда ты переставляешь мышь над ковриком.", "Lift-off distance is the height at which the sensor stops tracking. A low value reduces cursor movement when lifting and repositioning the mouse."),
			"debounce" => L("Задержка подавления дребезга переключателя. Малое значение даёт быстрый отклик; если возникают случайные двойные клики, увеличь задержку. Для RS3 Ultra доступно 0–15 мс.", "Debounce filters switch chatter. A low value gives faster response; increase it if unwanted double clicks occur. RS3 Ultra supports 0–15 ms."),
			"sync" => L("Motion Sync синхронизирует выборки сенсора с передачей USB-отчётов. Это может сделать интервалы данных ровнее. Настройка не меняет DPI.", "Motion Sync aligns sensor sampling with USB reports for more consistent data timing. It does not change DPI."),
			"angle" => L("Угловая привязка помогает вести прямую линию, корректируя траекторию. Для точного прицеливания её обычно отключают, чтобы сохранить естественное движение руки.", "Angle snapping corrects the trajectory to help draw straight lines. Leave it off when you want precise, unmodified aiming."),
			"ripple" => L("Подавление дрожания сглаживает мелкие колебания сенсора, особенно заметные при высоком DPI. Это фильтрация движения, поэтому траектория может ощущаться иначе.", "Ripple control smooths small sensor fluctuations, especially at high DPI. It filters movement and can change how tracking feels."),
			"sleep" => L("Время без движения до перехода мыши в сон. Во сне приёмник может отвечать, а сама мышь — нет. Разбуди её движением. Опрос заряда в приложении не записывает настройки сна.", "Idle time before the mouse sleeps. The receiver may still answer while the mouse does not. Move it to wake it. Battery polling does not write sleep settings."),
			"dongle" => L("Выбери информацию для этого физического LED: качество связи, заряд или частоту опроса. «Выключен» гасит LED. Сначала прочитай приёмник: запись доступна только после подтверждения поддержки трёх индикаторов. Схема цветов не является измерением сигнала.", "Choose what this physical LED shows: connection quality, battery or polling rate. Off disables the LED. Read the receiver first; writing is enabled only if it confirms three-LED support. The color guide is not a signal measurement."),
			"light" => L("Это маленький индикатор DPI на мыши. Постоянный эффект использует яркость 1–10; дыхание — скорость 1–5. При выключении сохраняются предыдущие параметры эффекта. Используется цвет активной ступени.", "This is the small DPI indicator on the mouse. Always on uses brightness 1–10; breathing uses speed 1–5. Turning it off preserves the previous effect settings. The active DPI stage supplies the color."),
			_ => L("Батарея опрашивается каждую секунду. Молния означает, что мышь сообщила о зарядке. При отсутствии ответа последнее значение помечается как устаревшее.", "Battery is polled every second. The lightning bolt means the mouse reports charging. If it stops responding, the last value is marked as stale."),
		};
	}

	private async Task PollBattery()
	{
		if (!officialHandoff && Process.GetProcessesByName("ATTACK  SHARK MOUSE HUB").Any())
		{
			StatusText.Text = L("Закрой официальный HUB для подключения SharkDesk. Одновременное управление приостановлено.", "Close the official HUB to connect SharkDesk. Simultaneous access is paused.");
			return;
		}
		if (monitorPaused && officialHandoff && !Process.GetProcessesByName("ATTACK  SHARK MOUSE HUB").Any())
		{
			monitorPaused = false;
			officialHandoff = false;
			nextMouseRead = (nextDongleRead = DateTimeOffset.MinValue);
		}
		if (closing || monitorPaused || busy || !deviceGate.Wait(0))
		{
			return;
		}
		try
		{
			using MouseDevice d = await MouseDevice.Connect();
			try
			{
				BatteryStatus sample = await d.ReadBattery();
				if (closing)
				{
					return;
				}
				battery = sample;
				batteryDisplay = batteryDisplayFilter.Observe(sample, d.Spec.Mid);
				latestIsReceiver = d.IsReceiver;
				lastBatteryModelId = d.Spec.Mid;
				runtimeEstimator.Observe(sample, RuntimeContext);
				UpdateBattery(failed: false);
			}
			catch (Exception)
			{
				UpdateBattery(failed: true);
			}
			await AutoReadMouse(d);
			await AutoReadDongle(d);
		}
		catch (Exception ex2)
		{
			if (closing)
			{
				return;
			}
			UpdateBattery(failed: true);
			connected = false;
			dongleSupported = false;
			nextMouseRead = (nextDongleRead = DateTimeOffset.MinValue);
			if (!busy)
			{
				Button applyButton = ApplyButton;
				Button lightingApplyButton = LightingApplyButton;
				bool flag = (DongleApplyButton.IsEnabled = false);
				bool isEnabled = (lightingApplyButton.IsEnabled = flag);
				applyButton.IsEnabled = isEnabled;
				if (lastRead == null && !mouseDirty)
				{
					StatusText.Text = L("Ищу мышь автоматически. Разбуди её или проверь подключение.", "Searching for a mouse automatically. Wake it or check the connection.");
				}
				StatusText.ToolTip = ex2.Message;
			}
		}
		finally
		{
			deviceGate.Release();
		}
	}

	private void UpdateBattery(bool failed)
    {
        bool stale = failed || battery == null || DateTimeOffset.UtcNow - battery.ReadAt > TimeSpan.FromSeconds(10);
        int? percent = batteryDisplay?.Percent;
        BatteryPercent.Text = percent.HasValue ? percent.Value + "%" : "—";
        BatteryPercent.Opacity = stale ? 0.45 : 1;
        BatteryFill.Width = percent.HasValue ? 26.0 * percent.Value / 100 : 0;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(BatteryPalette.ColorFor(percent, battery?.Charging ?? false)));
        BatteryFill.Background = BatteryTip.Background = BatteryOutline.BorderBrush = brush;
        BatteryFill.Opacity = BatteryTip.Opacity = BatteryOutline.Opacity = stale ? 0.4 : 1;
        ChargingBolt.Visibility = !stale && battery?.Charging == true ? Visibility.Visible : Visibility.Collapsed;
        ChargingBolt.Fill = darkTheme ? Brushes.Black : Brushes.White;
        BatteryPercent.ToolTip = stale ? L("Мышь не отвечает. Разбуди её.", "Move the mouse to reconnect.") : battery?.Charging == true ? L("Заряжается", "Charging") : L("Заряд батареи", "Battery level");
        if (latestIsReceiver.HasValue)
            ConnectionLabel.Text = latestIsReceiver.Value ? "2.4 ГГц" : "USB";
        ConnectionLabel.Opacity = stale ? 0.5 : 1;
    }

	private string FormatRuntime(TimeSpan time)
	{
		if (time.TotalMinutes < 5.0)
		{
			return L("Осталось < 5 мин", "Less than 5 min left");
		}
		int num = ((time.TotalHours >= 1.0) ? 15 : 5);
		int num2 = Math.Max(num, (int)Math.Round(time.TotalMinutes / (double)num) * num);
		if (num2 < 60)
		{
			return L($"Осталось ≈ {num2} мин", $"About {num2} min left");
		}
		int value = num2 / 60;
		int num3 = num2 % 60;
		return L($"Осталось ≈ {value} ч" + ((num3 > 0) ? $" {num3} мин" : ""), $"About {value} h" + ((num3 > 0) ? $" {num3} min" : "") + " left");
	}

	private void UpdateVersions()
	{
		VersionsText.Text = L("Мышь: ", "Mouse: ") + mouseVersion + L("     Приёмник: ", "     Receiver: ") + dongleVersion;
	}

	private void SyncDeviceExtras(MouseSettings s, bool updateLighting = true)
	{
		mouseVersion = s.Firmware;
		if (updateLighting)
		{
			bool flag = filling;
			filling = true;
			LightModeBox.SelectedIndex = s.LedMode;
			BrightnessSlider.Value = s.LedBrightness;
			SpeedSlider.Value = s.LedSpeed;
			filling = flag;
			lightingDirty = false;
		}
		LightingApplyButton.IsEnabled = connected && !busy;
		UpdateVersions();
		FirmwareLabel.Text = L("Прошивка ", "Firmware ") + s.Firmware;
	}

	private void ShowDongle(object sender, RoutedEventArgs e)
	{
		Page(3);
	}

	private void ShowSettings(object sender, RoutedEventArgs e)
	{
		Page(4);
	}

	private async void ReadDongle(object sender, RoutedEventArgs e)
	{
		if (busy || (dongleDirty && MessageBox.Show(L("Заменить изменения LED настройками из донгла?", "Replace LED edits with the dongle settings?"), "SharkDesk", MessageBoxButton.YesNo) != MessageBoxResult.Yes))
		{
			return;
		}
		Busy(value: true);
		await deviceGate.WaitAsync();
		try
		{
			using MouseDevice d = await MouseDevice.Connect(receiverOnly: true);
			DongleSettings dongleSettings = await d.ReadDongle();
			dongleVersion = dongleSettings.Firmware;
			UpdateVersions();
			dongleSupported = dongleSettings.LedModes != null;
			LoadDongle(dongleSettings);
			DongleStatus.Text = (dongleSupported ? L("Три LED поддерживаются. Настройки прочитаны.", "Three LEDs supported. Settings read.") : L("Приёмник ответил, но не подтвердил поддержку трёх LED.", "Receiver responded but did not confirm three-LED support."));
		}
		catch (Exception ex)
		{
			dongleSupported = false;
			DongleStatus.Text = ErrorText(ex);
		}
		finally
		{
			deviceGate.Release();
			Busy(value: false);
			DongleApplyButton.IsEnabled = dongleSupported;
			ComboBox[] array = new ComboBox[3] { Led1Box, Led2Box, Led3Box };
			for (int i = 0; i < array.Length; i++)
			{
				array[i].IsEnabled = dongleSupported;
			}
		}
	}

	private async void ApplyDongle(object sender, RoutedEventArgs e)
	{
		if (busy || !dongleSupported)
		{
			return;
		}
		int[] modes = new int[3] { Led1Box.SelectedIndex, Led2Box.SelectedIndex, Led3Box.SelectedIndex };
		Busy(value: true);
		await deviceGate.WaitAsync();
		try
		{
			using MouseDevice d = await MouseDevice.Connect(receiverOnly: true);
			await d.ApplyDongle(modes);
			dongleDirty = false;
			lastDongleModes = modes;
			DongleStatus.Text = L("Назначения LED сохранены и подтверждены приёмником.", "LED assignments saved and verified by the receiver.");
		}
		catch (Exception ex)
		{
			dongleSupported = false;
			DongleStatus.Text = ErrorText(ex);
		}
		finally
		{
			deviceGate.Release();
			Busy(value: false);
			DongleApplyButton.IsEnabled = dongleSupported;
		}
	}

	private void LightingEdited(object sender, RoutedEventArgs e)
	{
		if (BrightnessSlider != null)
		{
			BrightnessSlider.IsEnabled = LightModeBox.SelectedIndex == 1;
			SpeedSlider.IsEnabled = LightModeBox.SelectedIndex == 2;
			if (ready && !filling)
			{
				lightingDirty = true;
				editorRevision++;
			}
		}
	}

	private async void ApplyLighting(object sender, RoutedEventArgs e)
	{
		if (busy || !connected)
		{
			return;
		}
		int mode = LightModeBox.SelectedIndex;
		int brightness = (int)BrightnessSlider.Value;
		int speed = (int)SpeedSlider.Value;
		Busy(value: true);
		await deviceGate.WaitAsync();
		try
		{
			using MouseDevice d = await MouseDevice.Connect();
			if (d.Spec.Mid != currentSpec.Mid)
			{
				throw new IOException("Подключена другая модель мыши.");
			}
			await d.ApplyLighting(mode, brightness, speed);
			lightingDirty = false;
			StatusText.Text = L("Подсветка записана и подтверждена мышью.", "Lighting saved and verified by the mouse.");
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
		finally
		{
			deviceGate.Release();
			Busy(value: false);
		}
	}

	private void StartupChanged(object sender, RoutedEventArgs e)
	{
		if (!ready)
		{
			return;
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			if (StartupBox.IsChecked == true)
			{
				registryKey.SetValue("SharkDesk-RS3-Ultra", "\"" + Environment.ProcessPath + "\"");
			}
			else
			{
				registryKey.DeleteValue("SharkDesk-RS3-Ultra", throwOnMissingValue: false);
			}
			StatusText.Text = L("Настройка автозапуска сохранена.", "Startup preference saved.");
		}
		catch (Exception ex)
		{
			StartupBox.IsChecked = StartupBox.IsChecked != true;
			ShowError(ex);
		}
	}

	private void OpenMouseSettings(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo("ms-settings:mousetouchpad")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void OpenOfficialTool(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!File.Exists("C:\\Program Files (x86)\\ATTACK  SHARK MOUSE HUB\\ATTACK  SHARK MOUSE HUB\\ATTACK  SHARK MOUSE HUB.exe"))
			{
				throw new FileNotFoundException(L("Официальный HUB не установлен.", "The official HUB is not installed."));
			}
			monitorPaused = true;
			officialHandoff = true;
			UpdateBattery(failed: true);
			Process.Start(new ProcessStartInfo("C:\\Program Files (x86)\\ATTACK  SHARK MOUSE HUB\\ATTACK  SHARK MOUSE HUB\\ATTACK  SHARK MOUSE HUB.exe")
			{
				UseShellExecute = true
			});
			StatusText.Text = L("Официальный HUB открыт. После его закрытия подключение восстановится автоматически.", "Official HUB opened. Connection will resume automatically after it closes.");
		}
		catch (Exception ex)
		{
			monitorPaused = false;
			ShowError(ex);
		}
	}

	private string ErrorText(Exception ex)
	{
		if (english)
		{
			if (!(ex is ArgumentException))
			{
				return "No confirmed response. Wake the mouse, check the receiver/cable and close the official HUB, then retry.";
			}
			return "Invalid value. Check DPI, active stage, polling rate and sensor settings.";
		}
		return ex.Message;
	}

	private void ShowError(Exception ex)
	{
		StatusText.Text = ErrorText(ex);
		StatusText.ToolTip = ex.Message;
	}

	public MainWindow()
	{
		InitializeComponent();
		CountBox.ItemsSource = Enumerable.Range(1, 6);
		RateBox.ItemsSource = new int[7] { 125, 250, 500, 1000, 2000, 4000, 8000 };
		LodBox.ItemsSource = new string[3] { "0.7 мм", "1 мм", "2 мм" };
		SleepBox.ItemsSource = new string[7] { "10 секунд", "30 секунд", "1 минута", "2 минуты", "5 минут", "10 минут", "15 минут" };
		for (int i = 0; i < 6; i++)
		{
			StackPanel stackPanel = new StackPanel
			{
				Margin = new Thickness(0.0, 0.0, (i != 5) ? 10 : 0, 0.0)
			};
			stages[i] = new RadioButton
			{
				Content = $"Ступень {i + 1}",
				GroupName = "dpi",
				Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
			};
			stages[i].Checked += Edited;
			dpiBoxes[i] = new TextBox
			{
				FontSize = 16.0,
				MinWidth = 60.0
			};
			dpiBoxes[i].TextChanged += Edited;
			stackPanel.Children.Add(stages[i]);
			stackPanel.Children.Add(dpiBoxes[i]);
			DpiPanel.Children.Add(stackPanel);
		}
		InitRs3Controls();
		Fill(new MouseSettings());
		RefreshProfiles();
		filling = false;
		try
		{
			darkTheme = File.ReadAllText(System.IO.Path.Combine(AppPaths.Data, "theme.txt")).Trim() == "dark";
		}
		catch (IOException)
		{
		}
		SetTheme();
		InitExtras();
		SourceInitialized += (object? _, EventArgs _) =>
		{
			SetTitleBar();
		};
	}

	private void Edited(object sender, RoutedEventArgs e)
	{
		if (filling || StatusText == null)
		{
			return;
		}
		mouseDirty = true;
		editorRevision++;
		if (CountBox.SelectedItem is int num)
		{
			for (int i = 0; i < 6; i++)
			{
				stages[i].IsEnabled = i < num;
				dpiBoxes[i].IsEnabled = i < num;
			}
			if (ReferenceEquals(sender, CountBox)) {
				if (Array.FindIndex(stages, s => s.IsChecked == true) >= num) {
					filling = true;
					for (int i = 0; i < 6; i++) stages[i].IsChecked = i == num - 1;
					filling = false;
				}
				RefreshDpiEditor();
			}
		}
		StatusText.Text = L("Есть изменения", "Unsaved changes");
	}

	private void Fill(MouseSettings s)
	{
		filling = true;
		SetCapabilities(MouseSpec.Find(124, s.ModelId));
		for (int i = 0; i < 6; i++)
		{
			dpiBoxes[i].Text = s.Dpi[i].ToString();
			stages[i].IsChecked = i == s.Active;
			stages[i].IsEnabled = i < s.Count;
			dpiBoxes[i].IsEnabled = i < s.Count;
		}
		CountBox.SelectedItem = s.Count;
		RateBox.SelectedItem = s.Polling;
		LodBox.SelectedIndex = Array.IndexOf(currentSpec.LodCodes, s.Lod);
		DebounceBox.Text = s.Debounce.ToString();
		SyncBox.IsChecked = s.MotionSync;
		AngleBox.IsChecked = s.AngleSnap;
		RippleBox.IsChecked = s.Ripple;
		SleepBox.SelectedIndex = Array.IndexOf(new int[7] { 10, 30, 60, 120, 300, 600, 900 }, s.Sleep);
		FillRs3(s);
		filling = false;
	}

	private MouseSettings Collect()
	{
		if (!int.TryParse(DebounceBox.Text, out var result))
		{
			throw new ArgumentException(L("Введи задержку клика целым числом.", "Enter click debounce as a whole number."));
		}
		int[] array = new int[6];
		for (int i = 0; i < 6; i++)
		{
			if (!int.TryParse(dpiBoxes[i].Text, out array[i]))
			{
				throw new ArgumentException($"Введи DPI для ступени {i + 1} целым числом.");
			}
		}
		MouseSettings mouseSettings = new MouseSettings();
		mouseSettings.ModelId = currentSpec.Mid;
		mouseSettings.Dpi = array;
		mouseSettings.Count = (int)(CountBox.SelectedItem ?? ((object)6));
		mouseSettings.Active = Array.FindIndex(stages, (RadioButton x) => x.IsChecked == true);
		mouseSettings.Polling = (int)(RateBox.SelectedItem ?? ((object)1000));
		mouseSettings.Lod = currentSpec.LodCodes[Math.Max(0, LodBox.SelectedIndex)];
		mouseSettings.Debounce = result;
		mouseSettings.MotionSync = SyncBox.IsChecked == true;
		mouseSettings.AngleSnap = AngleBox.IsChecked == true;
		mouseSettings.Ripple = RippleBox.IsChecked == true;
		mouseSettings.Sleep = (new int[7] { 10, 30, 60, 120, 300, 600, 900 })[Math.Max(0, SleepBox.SelectedIndex)];
		mouseSettings.MaxRate = maxRate;
		CollectRs3(mouseSettings);
		MouseDevice.Validate(mouseSettings);
		return mouseSettings;
	}

	private void Busy(bool value)
	{
		busy = value;
		ReadButton.IsEnabled = !value;
		ApplyButton.IsEnabled = !value && connected;
		LightingApplyButton.IsEnabled = !value && connected;
		DongleReadButton.IsEnabled = !value;
		DongleApplyButton.IsEnabled = !value && dongleSupported;
		SensorPage.IsEnabled = !value;
		ProfilesPage.IsEnabled = !value;
		DonglePage.IsEnabled = !value;
	}

	private async void ReadDevice(object sender, RoutedEventArgs e)
	{
		if (busy || ((mouseDirty || lightingDirty || (lastRead != null && !SameEditor(lastRead))) && MessageBox.Show(L("Перечитать мышь и заменить изменения в редакторе?", "Read the mouse and replace changes in the editor?"), "SharkDesk", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes))
		{
			return;
		}
		monitorPaused = false;
		Busy(value: true);
		StatusText.Text = L("Подключаюсь и читаю настройки…", "Connecting and reading settings…");
		await deviceGate.WaitAsync();
		try
		{
			using MouseDevice d = await MouseDevice.Connect();
			MouseSettings mouseSettings = await d.ReadSettings();
			buttonSnapshot = await d.ReadButtons();
			maxRate = mouseSettings.MaxRate;
			lastRead = mouseSettings;
			Fill(mouseSettings);
			connected = true;
			mouseDirty = false;
			lightingDirty = false;
			editorRevision++;
			ConnectionLabel.Text = "●  " + (d.IsReceiver ? L("2.4 ГГц · приёмник", "2.4 GHz · receiver") : L("USB · кабель", "USB · cable"));
			DeviceSummary.Text = $"{mouseSettings.Dpi[mouseSettings.Active]:N0} DPI   /   {mouseSettings.Polling:N0} " + L("Гц", "Hz") + $"   /   LOD {LodBox.SelectedItem}";
			SyncDeviceExtras(mouseSettings);
			StatusText.Text = L("Настройки прочитаны из мыши. Можно редактировать.", "Settings read from the mouse. Ready to edit.");
		}
		catch (Exception ex)
		{
			connected = false;
			ConnectionLabel.Text = L("○  Нет связи", "○  No connection");
			ShowError(ex);
		}
		finally
		{
			deviceGate.Release();
			Busy(value: false);
		}
	}

	private bool SameEditor(MouseSettings s)
	{
		try
		{
			return SettingsKey(Collect()) == SettingsKey(s);
		}
		catch
		{
			return false;
		}
	}

	private static string SettingsKey(MouseSettings s)
	{
		return JsonSerializer.Serialize(new { s.Dpi, s.DpiY, s.DpiColors, s.Advanced, s.Count, s.Active, s.Polling, s.Lod, s.Debounce, s.Sleep, s.MotionSync, s.AngleSnap, s.Ripple });
	}

	private async void ApplyDevice(object sender, RoutedEventArgs e)
	{
		if (busy)
		{
			return;
		}
		MouseSettings target;
		try
		{
			target = Collect();
		}
		catch (Exception ex)
		{
			ShowError(ex);
			return;
		}
		Busy(value: true);
		StatusText.Text = L("Записываю изменённые параметры и проверяю ответ…", "Writing changed settings and verifying the response…");
		await deviceGate.WaitAsync();
		try
		{
			using MouseDevice d = await MouseDevice.Connect();
			await d.Apply(target);
			await d.ApplyLighting(target.LedMode, target.LedBrightness, target.LedSpeed);
			MouseSettings mouseSettings = await d.ReadSettings();
			if (SettingsKey(mouseSettings) != SettingsKey(target))
			{
				throw new IOException("Проверка не совпала. Перечитай настройки мыши.");
			}
			lastRead = mouseSettings;
			Fill(mouseSettings);
			mouseDirty = false;
			editorRevision++;
			DeviceSummary.Text = $"{mouseSettings.Dpi[mouseSettings.Active]:N0} DPI   /   {mouseSettings.Polling:N0} " + L("Гц", "Hz") + $"   /   LOD {LodBox.SelectedItem}";
			StatusText.Text = L("Сохранено", "Saved");
		}
		catch (Exception ex2)
		{
			connected = false;
			ConnectionLabel.Text = L("○  Перечитай устройство", "○  Read the device again");
			StatusText.Text = ErrorText(ex2) + L(" Часть изменений могла сохраниться.", " Some changes may have been saved.");
		}
		finally
		{
			deviceGate.Release();
			Busy(value: false);
		}
	}

	private void Page(int index)
	{
		bool flag = currentPage != index;
		currentPage = index;
		foreach (Button helpButton in helpButtons)
		{
			if (helpButton.ToolTip is ToolTip toolTip)
			{
				toolTip.IsOpen = false;
			}
		}
		StackPanel[] array = new StackPanel[5] { SensorPage, ProfilesPage, AboutPage, DonglePage, SettingsPage };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].Visibility = ((i != index) ? Visibility.Collapsed : Visibility.Visible);
		}
		PageTitle.Text = ((!english) ? new string[5] { "Чувствительность", "Профили", "Помощь", "Подсветка", "Настройки" } : new string[5] { "Sensitivity", "Profiles", "Help", "Lighting", "Settings" })[index];
		if (flag)
		{
			MainScroll.ScrollToTop();
		}
		UpdateNav();
	}

	private void UpdateNav()
	{
		Button[] array = new Button[5] { SensorNav, ProfilesNav, AboutNav, DongleNav, SettingsNav };
		for (int i = 0; i < array.Length; i++)
		{
			array[i].SetResourceReference(Control.BackgroundProperty, (i == currentPage) ? "Accent" : "SidebarBg");
			array[i].SetResourceReference(Control.ForegroundProperty, (i == currentPage) ? "OnAccent" : "TextColor");
		}
	}

	private void ToggleTheme(object sender, RoutedEventArgs e)
	{
		darkTheme = !darkTheme;
		SetTheme();
		try
		{
			File.WriteAllText(System.IO.Path.Combine(AppPaths.Data, "theme.txt"), darkTheme ? "dark" : "light");
		}
		catch (IOException)
		{
			StatusText.Text = "Тема переключена, но её не удалось сохранить.";
		}
	}

	private void SetTheme()
	{
		string[] array = ((!darkTheme) ? new string[10] { "#F3F5F8", "#F9FAFC", "#FFFFFF", "#202938", "#687487", "#E3E7EE", "#F3F5F8", "#E9EDF3", "#5262D8", "#FFFFFF" } : new string[10] { "#181D27", "#1D2430", "#242C3A", "#EDF0F7", "#A5AFC0", "#374153", "#1D2533", "#354055", "#8493F5", "#141A27" });
		string[] array2 = new string[10] { "PageBg", "SidebarBg", "CardBg", "TextColor", "Muted", "LineColor", "InputBg", "ButtonBg", "Accent", "OnAccent" };
		for (int i = 0; i < array2.Length; i++)
		{
			Resources[array2[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(array[i]));
		}
		ThemeButton.Content = (darkTheme ? L("Светлая тема", "Light theme") : L("Тёмная тема", "Dark theme"));
		UpdateNav();
		SetTitleBar();
	}

	private void SetTitleBar()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			int value = (darkTheme ? 1 : 0);
			DwmSetWindowAttribute(handle, 20, ref value, 4);
		}
	}

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

	private void ShowSensor(object sender, RoutedEventArgs e)
	{
		Page(0);
	}

	private void ShowProfiles(object sender, RoutedEventArgs e)
	{
		Page(1);
	}

	private void ShowAbout(object sender, RoutedEventArgs e)
	{
		Page(2);
	}

	private void RefreshProfiles()
	{
		ProfilesList.ItemsSource = (Directory.Exists(profileDir) ? Directory.GetFiles(profileDir, "*.json").Select(System.IO.Path.GetFileNameWithoutExtension).ToArray() : Array.Empty<string>());
		if (ProfilesList.Items.Count > 0)
		{
			ProfilesList.SelectedIndex = 0;
		}
	}

	private void SaveProfile(object sender, RoutedEventArgs e)
	{
		try
		{
			MouseSettings value = Collect();
			string text = ProfileName.Text.Trim();
			bool flag = text.Length == 0 || text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0;
			if (!flag)
			{
				bool flag2 = ((text == "." || text == "..") ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				throw new ArgumentException(L("Введи допустимое имя профиля.", "Enter a valid profile name."));
			}
			Directory.CreateDirectory(profileDir);
			string path = System.IO.Path.Combine(profileDir, text + ".json");
			if (!File.Exists(path) || MessageBox.Show(L("Заменить профиль с этим именем?", "Replace the profile with this name?"), "SharkDesk", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
			{
				File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
				RefreshProfiles();
				ProfilesList.SelectedItem = text;
				StatusText.Text = L("Профиль сохранён на компьютере.", "Profile saved on this computer.");
			}
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private MouseSettings ReadProfile(string path)
	{
		MouseSettings? mouseSettings = JsonSerializer.Deserialize<MouseSettings>(File.ReadAllText(path)) ?? throw new ArgumentException("Пустой профиль.");
		if (mouseSettings.ModelId != currentSpec.Mid)
		{
			throw new ArgumentException(L("Профиль создан для другой модели мыши.", "This profile belongs to a different mouse model."));
		}
		mouseSettings.MaxRate = maxRate;
		MouseDevice.Validate(mouseSettings);
		return mouseSettings;
	}

	private void LoadProfile(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!(ProfilesList.SelectedItem is string text))
			{
				throw new ArgumentException(L("Сначала выбери профиль.", "Select a profile first."));
			}
			Fill(ReadProfile(System.IO.Path.Combine(profileDir, text + ".json")));
			FillProfileLighting(ReadProfile(System.IO.Path.Combine(profileDir, text + ".json")));
			mouseDirty = true;
			editorRevision++;
			ProfileName.Text = text;
			StatusText.Text = L("Профиль загружен в редактор. Нажми «Применить к мыши» для записи.", "Profile loaded into the editor. Click Apply to mouse to write it.");
			Page(0);
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void ExportProfile(object sender, RoutedEventArgs e)
	{
		try
		{
			MouseSettings value = Collect();
			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = L("Профиль JSON|*.json", "JSON profile|*.json"),
				FileName = "SharkDesk-profile.json"
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				File.WriteAllText(saveFileDialog.FileName, JsonSerializer.Serialize(value, Json));
				StatusText.Text = L("Профиль экспортирован.", "Profile exported.");
			}
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}

	private void ImportProfile(object sender, RoutedEventArgs e)
	{
		try
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = L("Профиль JSON|*.json", "JSON profile|*.json")
			};
			if (openFileDialog.ShowDialog() == true)
			{
				Fill(ReadProfile(openFileDialog.FileName));
				FillProfileLighting(ReadProfile(openFileDialog.FileName));
				mouseDirty = true;
				editorRevision++;
				ProfileName.Text = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
				StatusText.Text = L("Профиль импортирован в редактор. Можно сохранить или применить.", "Profile imported into the editor. Save or apply it when ready.");
				Page(0);
			}
		}
		catch (Exception ex)
		{
			ShowError(ex);
		}
	}
}
