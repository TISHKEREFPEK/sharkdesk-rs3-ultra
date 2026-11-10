using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace SharkDesk;

public sealed partial class MouseDevice : IDisposable
{
	private struct HidCaps
	{
		public ushort Usage;

		public ushort UsagePage;

		public ushort InputReportByteLength;

		public ushort OutputReportByteLength;

		public ushort FeatureReportByteLength;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
		public ushort[] Reserved;

		public ushort NumberLinkCollectionNodes;

		public ushort NumberInputButtonCaps;

		public ushort NumberInputValueCaps;

		public ushort NumberInputDataIndices;

		public ushort NumberOutputButtonCaps;

		public ushort NumberOutputValueCaps;

		public ushort NumberOutputDataIndices;

		public ushort NumberFeatureButtonCaps;

		public ushort NumberFeatureValueCaps;

		public ushort NumberFeatureDataIndices;
	}

	private readonly FileStream stream;

	private readonly int inputLength;

	private readonly int outputLength;

	private static readonly Dictionary<int, int> Rates = new Dictionary<int, int>
	{
		{ 125, 8 },
		{ 250, 4 },
		{ 500, 2 },
		{ 1000, 1 },
		{ 2000, 16 },
		{ 4000, 32 },
		{ 8000, 64 }
	};

	private static readonly int[] Voltages = new int[21]
	{
		3050, 3170, 3230, 3300, 3600, 3660, 3720, 3760, 3800, 3840,
		3880, 3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080,
		4110
	};

	private static readonly int[] BrightnessValues = new int[10] { 16, 30, 60, 90, 128, 150, 180, 210, 230, 255 };

	public int Link { get; private set; }

	public bool IsReceiver { get; private set; }

	public MouseSpec Spec { get; private set; } = MouseSpec.Find(124, 14);

	public int MaxRate
	{
		get
		{
			switch (Link)
			{
			case 0:
			case 2:
				return 1000;
			case 1:
				return 4000;
			case 4:
				return 2000;
			case 3:
			case 5:
				return 8000;
			default:
				return 0;
			}
		}
	}

	private MouseDevice(SafeFileHandle handle, int input, int output)
	{
		stream = new FileStream(handle, FileAccess.ReadWrite, 4096, isAsync: true);
		inputLength = input;
		outputLength = output;
	}

	public static async Task<MouseDevice> Connect(bool receiverOnly = false)
	{
		Guid guid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
		uint length = 0u;
		if (CM_Get_Device_Interface_List_SizeW(ref length, ref guid, null, 0u) != 0)
		{
			throw new IOException("Не удалось получить список HID-устройств.");
		}
		char[] array = new char[length];
		if (CM_Get_Device_Interface_ListW(ref guid, null, array, length, 0u) != 0)
		{
			throw new IOException("Не удалось прочитать список HID-устройств.");
		}
		string text = null;
		foreach (string item in from p in new string(array).Split('\0', StringSplitOptions.RemoveEmptyEntries)
			orderby (!p.Contains("pid_f515", StringComparison.OrdinalIgnoreCase)) ? 1 : 0
			select p)
		{
			string lower = item.ToLowerInvariant();
			if (!lower.Contains("vid_3554") || !new string[8] { "fb44", "fb35", "fb43", "f517", "f515", "f516", "f5f6", "f50e" }.Any((string p) => lower.Contains("pid_" + p)))
			{
				continue;
			}
			bool receiver = new string[4] { "fb44", "fb35", "fb43", "f517" }.Any((string p) => lower.Contains("pid_" + p));
			if (receiverOnly && !receiver)
			{
				continue;
			}
			SafeFileHandle safeFileHandle = CreateFileW(item, 3221225472u, 3u, IntPtr.Zero, 3u, 1073741824u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				safeFileHandle.Dispose();
				continue;
			}
			if (!HidD_GetPreparsedData(safeFileHandle, out var data))
			{
				safeFileHandle.Dispose();
				continue;
			}
			HidCaps caps;
			try
			{
				if (HidP_GetCaps(data, out caps) != 1114112)
				{
					safeFileHandle.Dispose();
					continue;
				}
			}
			finally
			{
				HidD_FreePreparsedData(data);
			}
			if (caps.UsagePage < 65280 || caps.InputReportByteLength < 17 || caps.OutputReportByteLength < 17)
			{
				safeFileHandle.Dispose();
				continue;
			}
			MouseDevice device = new MouseDevice(safeFileHandle, caps.InputReportByteLength, caps.OutputReportByteLength);
			try
			{
				byte[] array2 = new byte[8];
				RandomNumberGenerator.Fill(array2.AsSpan(0, 4));
				byte[] array3 = await device.Ask(1, 0, array2);
				if (array3[9] != 124 || array3[10] != 14)
					throw new IOException("Эта сборка предназначена только для ATTACK SHARK RS3 Ultra (CID 124 / MID 14).");
				device.Spec = MouseSpec.Find(array3[9], array3[10]) ?? throw new IOException($"Эта модель пока не поддерживается: CID {array3[9]}, MID {array3[10]}. Настройки не изменены.");
				device.Link = array3[11];
				device.IsReceiver = receiver;
				if (device.MaxRate == 0)
				{
					throw new IOException("Неизвестный тип подключения.");
				}
				return device;
			}
			catch (Exception ex)
			{
				text = ex.Message;
				device.Dispose();
			}
		}
		throw new IOException(text ?? "Совместимая Attack Shark не найдена. Подключи приёмник или кабель и разбуди мышь. Если она занята, закрой штатный HUB.");
	}

	private static byte[] Frame(byte cmd, int address, byte[]? data, int? length)
	{
		byte[] array = new byte[17]
		{
			8,
			cmd,
			0,
			(byte)(address >> 8),
			(byte)address,
			(byte)(length ?? data?.Length ?? 0),
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0
		};
		if (data != null && data.Length > 10)
		{
			throw new ArgumentException("Слишком длинная команда.");
		}
		data?.CopyTo(array, 6);
		array[16] = (byte)(85 - array.Take(16).Sum((byte b) => b));
		return array;
	}

	private async Task<byte[]> Ask(byte cmd, int address = 0, byte[]? data = null, int? length = null)
	{
		bool flag;
		switch (cmd)
		{
		case 1:
		case 3:
		case 4:
		case 7:
		case 8:
		case 18:
		case 14:
		case 15:
		case 22:
		case 23:
		case 29:
		case 44:
		case 45:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentException("Команда не разрешена.");
		}
		byte[] frame = Frame(cmd, address, data, length);
		bool trace = Environment.GetEnvironmentVariable("SHARKDESK_TRACE") == "1";
		if (trace) Console.WriteLine("TX " + Convert.ToHexString(frame));
		for (int attempt = 0; attempt < 5; attempt++)
		{
			byte[] array = new byte[outputLength];
			frame.CopyTo(array, 0);
			using (CancellationTokenSource writeTimeout = new CancellationTokenSource(1000))
			{
				await stream.WriteAsync(array, writeTimeout.Token);
			}
			using CancellationTokenSource timeout = new CancellationTokenSource(300);
			_ = 1;
			try
			{
				while (!timeout.IsCancellationRequested)
				{
					byte[] input = new byte[inputLength];
					int bytesRead = await stream.ReadAsync(input, timeout.Token);
					if (trace) Console.WriteLine("RX " + Convert.ToHexString(input.AsSpan(0, bytesRead)));
					if (bytesRead < 17 || input[0] != 8 || (input.Take(17).Sum((byte b) => b) & 0xFF) != 85)
					{
						continue;
					}
					byte[] array2 = input.Skip(1).Take(16).ToArray();
					if (array2[0] == cmd)
					{
						if (array2[1] != 0)
						{
							throw new IOException("Мышь отклонила команду.");
						}
						if ((cmd != 8 && cmd != 7) || (array2[2] == (byte)(address >> 8) && array2[3] == (byte)address && array2[4] == frame[5]))
						{
							return array2;
						}
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
		}
		throw new IOException("Мышь не ответила. Разбуди её и повтори. Закрой штатный HUB, если он перехватывает ответы.");
	}

	private async Task<byte[]> Read(int address, int length)
	{
		byte[] result = new byte[length];
		for (int i = 0; i < length; i += 10)
		{
			int size = Math.Min(10, length - i);
			Array.Copy(await Ask(8, address + i, null, size), 5, result, i, size);
		}
		return result;
	}

	private static int Pair(byte[] b, int i)
	{
		if (((b[i] + b[i + 1]) & 0xFF) != 85)
		{
			throw new IOException($"Неверная контрольная сумма настройки {i}.");
		}
		return b[i];
	}

	public async Task<MouseSettings> ReadSettings()
	{
		if ((await Ask(3))[5] != 1) throw new IOException("Приёмник подключён, но мышь спит или выключена. Пошевели мышью и повтори чтение.");
		byte[] b = await Read(0, 84);
		byte[] b2 = await Read(169, 10);
		MouseSettings s = new MouseSettings
		{
			ModelId = Spec.Mid,
			Count = Pair(b, 2),
			Active = Pair(b, 4),
			Lod = Pair(b, 10),
			Debounce = Pair(b2, 0),
			MotionSync = (Pair(b2, 2) == 1),
			Sleep = Pair(b2, 4) * 10,
			AngleSnap = (Pair(b2, 6) == 1),
			Ripple = (Pair(b2, 8) == 1),
			MaxRate = MaxRate,
			Connection = (IsReceiver ? "2.4 ГГц · приёмник" : "USB · кабель")
		};
		s.Polling = Rates.FirstOrDefault((KeyValuePair<int, int> x) => x.Value == Pair(b, 0)).Key;
		if (Spec.Sensor == "3950")
		{
			for (int num = 0; num < 6; num++)
			{
				int count = 12 + num * 4;
				byte[] array = b.Skip(count).Take(4).ToArray();
				if ((array.Sum((byte x) => x) & 0xFF) != 85)
				{
					throw new IOException("Неверная контрольная сумма DPI.");
				}
				int num2 = array[2];
				int num3 = ((array[0] | (((num2 >> 2) & 3) << 8)) + 1) * 50;
				if ((num2 & 1) != 0)
				{
					num3 *= 2;
				}
				if ((num2 & 2) != 0)
				{
					num3 *= 2;
				}
				s.Dpi[num] = num3;
			}
		}
		if (Spec.Sensor == "3955")
		{
			byte[] array2 = await Read(6912, 36);
			s.DpiY = new int[6];
			for (int num4 = 0; num4 < 6; num4++)
			{
				s.Dpi[num4] = DecodeDpi3955(array2.AsSpan(num4 * 6, 6));
				s.DpiY[num4] = HubCodec.DecodeDpiAxis(array2.AsSpan(num4 * 6, 6), true);
			}
		}
		s.LedMode = ((Pair(b, 82) != 0) ? Pair(b, 76) : 0);
		s.LedBrightness = DecodeBrightness(Pair(b, 78));
		s.LedSpeed = Pair(b, 80);
		s.DpiColors = Enumerable.Range(0, 6).Select(i => HubCodec.DecodeColor(b.AsSpan(44 + i * 4, 4))).ToArray();
		s.Advanced = await ReadAdvanced();
		s.OnboardProfile = await ReadOnboardProfile();
		Validate(s);
		try
		{
			BatteryStatus batteryStatus = await ReadBattery();
			s.Battery = batteryStatus.Percent;
			s.Charging = batteryStatus.Charging;
		}
		catch (IOException)
		{
		}
		try
		{
			byte[] array3 = await Ask(18);
			s.Firmware = $"v{array3[5]}.{array3[6]:x2}";
		}
		catch (IOException)
		{
		}
		return s;
	}

	private static int? Battery(byte[] r)
	{
		if (r[9] == 1)
		{
			if (r[10] > 100)
			{
				return null;
			}
			return r[10];
		}
		int num = (r[7] << 8) | r[8];
		if (num == 0)
		{
			if (r[5] > 100)
			{
				return null;
			}
			return r[5];
		}
		if (num >= Voltages[^1])
		{
			return (r[6] == 1) ? 99 : 100;
		}
		if (num <= Voltages[0])
		{
			return 0;
		}
		for (int i = 1; i < Voltages.Length; i++)
		{
			if (num <= Voltages[i])
			{
				return (int)Math.Round((double)(5 * (i - 1)) + 5.0 * (double)(num - Voltages[i - 1]) / (double)(Voltages[i] - Voltages[i - 1]));
			}
		}
		return null;
	}

	public async Task<BatteryStatus> ReadBattery()
	{
		return DecodeBattery(await Ask(4), DateTimeOffset.UtcNow);
	}

	internal static BatteryStatus DecodeBattery(byte[] reply, DateTimeOffset timestamp)
	{
		if (reply.Length != 16 || reply[0] != 4 || reply[1] != 0)
		{
			throw new IOException("Неверный ответ батареи.");
		}
		int num = ((reply[9] == 1) ? reply[10] : reply[5]);
		int millivolts = (reply[7] << 8) | reply[8];
		return new BatteryStatus((num <= 100) ? new int?(num) : Battery(reply), reply[6] == 1, millivolts, num > 100, timestamp);
	}

	private static int DecodeBrightness(int raw)
	{
		int num = Array.IndexOf(BrightnessValues, raw);
		if (num >= 0)
		{
			return num + 1;
		}
		return 1;
	}

	public async Task<DongleSettings> ReadDongle()
	{
		if (!IsReceiver)
		{
			throw new IOException("Подключи приёмник USB.");
		}
		byte[] version = await Ask(29);
		int[] modes = null;
		try
		{
			byte[] source = await Ask(45);
			if (source.Skip(5).Take(3).All((byte x) => x <= 3))
			{
				modes = source.Skip(5).Take(3).Select((Func<byte, int>)((byte x) => x))
					.ToArray();
			}
		}
		catch (IOException)
		{
		}
		return new DongleSettings($"v{version[5]}.{version[6]:x2}", modes);
	}

	internal static byte[] EncodeDongleModes(int[] modes)
	{
		if (modes.Length != 3 || modes.Any((int x) => x < 0 || x > 3))
		{
			throw new ArgumentException("Недопустимый режим LED.");
		}
		byte[] array = new byte[10];
		for (int num = 0; num < 3; num++)
		{
			array[num] = (byte)modes[num];
		}
		return array;
	}

	public async Task ApplyDongle(int[] modes)
	{
		byte[] payload = EncodeDongleModes(modes);
		DongleSettings dongleSettings = await ReadDongle();
		if (dongleSettings.LedModes == null)
		{
			throw new IOException("Этот приёмник не подтвердил поддержку трёх LED.");
		}
		if (!dongleSettings.LedModes.SequenceEqual(modes))
		{
			await Ask(44, 0, payload);
			DongleSettings dongleSettings2 = await ReadDongle();
			if (dongleSettings2.LedModes == null || !dongleSettings2.LedModes.SequenceEqual(modes))
			{
				throw new IOException("Приёмник не подтвердил запись LED. Перечитай настройки.");
			}
		}
	}

	public async Task ApplyLighting(int mode, int brightness, int speed)
	{
		if (mode < 0 || mode > 2 || brightness < 1 || brightness > 10 || speed < 1 || speed > 5)
		{
			throw new ArgumentException("Недопустимые параметры подсветки.");
		}
		byte[] b = await Read(76, 8);
		List<(int, int)> list = new List<(int, int)>();
		if (mode != 0 && Pair(b, 0) != mode)
		{
			list.Add((76, mode));
		}
		if (mode == 1 && Pair(b, 2) != BrightnessValues[brightness - 1])
		{
			list.Add((78, BrightnessValues[brightness - 1]));
		}
		if (mode == 2 && Pair(b, 4) != speed)
		{
			list.Add((80, speed));
		}
		if (Pair(b, 6) != ((mode != 0) ? 1 : 0))
		{
			list.Add((82, (mode != 0) ? 1 : 0));
		}
		foreach (var item2 in list)
		{
			int address = item2.Item1;
			int item = item2.Item2;
			byte[] data = PairBytes(item);
			await Ask(7, address, data);
			if (!(await Read(address, 2)).SequenceEqual(data))
			{
				throw new IOException("Мышь не подтвердила подсветку. Перечитай устройство.");
			}
		}
	}

	public static void Validate(MouseSettings s)
	{
		MouseSpec spec = MouseSpec.Find(124, s.ModelId) ?? throw new ArgumentException("Неизвестная модель профиля.");
		if (s.Dpi == null || s.Dpi.Length != 6 || s.Dpi.Any((int x) => !spec.ValidDpi(x)))
		{
			throw new ArgumentException($"DPI не соответствует сенсору PAW{spec.Sensor}, максимум {spec.MaxDpi}.");
		}
		if (s.ModelId != 14 || s.DpiY != null && (s.DpiY.Length != 6 || s.DpiY.Any(x => !spec.ValidDpi(x))))
			throw new ArgumentException("Профиль должен соответствовать RS3 Ultra; DPI X/Y — от 1 до 52 000.");
		if (s.DpiColors != null) {
			if (s.DpiColors.Length != 6) throw new ArgumentException("Нужны цвета шести ступеней DPI.");
			foreach (string color in s.DpiColors) HubCodec.EncodeColor(color);
		}
		s.Advanced?.Validate();
		if (s.Buttons != null) HubCodec.ValidateButtons(s.Buttons);
		if (s.Count < 1 || s.Count > 6 || s.Active < 0 || s.Active >= s.Count)
		{
			throw new ArgumentException("Активная ступень должна входить в число включённых ступеней.");
		}
		if (!Rates.ContainsKey(s.Polling) || s.Polling > s.MaxRate)
		{
			throw new ArgumentException("Частота не поддерживается этим подключением.");
		}
		if (!spec.LodCodes.Contains(s.Lod) || s.Debounce < 0 || s.Debounce > 15 || !new int[7] { 10, 30, 60, 120, 300, 600, 900 }.Contains(s.Sleep))
		{
			throw new ArgumentException("Недопустимые параметры сенсора.");
		}
	}

	private static byte[] PairBytes(int value)
	{
		return new byte[2]
		{
			(byte)value,
			(byte)(85 - value)
		};
	}

	internal static byte[] EncodeDpi(int dpi, int maximum = 42000)
	{
		if (dpi < 50 || dpi > maximum || dpi > 60000 || dpi % ((dpi > 30000) ? 100 : 50) != 0)
		{
			throw new ArgumentException("Недопустимый DPI.");
		}
		int num = dpi / ((dpi > 30000) ? 100 : 50) - 1;
		int num2 = (num >> 8) & 3;
		int num3 = (num2 << 2) | (num2 << 6) | ((dpi > 30000) ? 17 : 0);
		byte b = (byte)num;
		return new byte[4]
		{
			b,
			b,
			(byte)num3,
			(byte)(85 - b - b - num3)
		};
	}

	internal static byte[] EncodeDpi3955(int dpi)
	{
		if (dpi < 1 || dpi > 60000 || (dpi > 42000 && dpi % 2 != 0))
		{
			throw new ArgumentException("Недопустимый DPI 3955.");
		}
		bool flag = dpi > 42000;
		int num = (flag ? (dpi / 2) : dpi) - 1;
		int num2 = (num >> 16) & 3;
		byte b = (byte)((num2 << 2) | (num2 << 6) | (flag ? 17 : 0));
		byte b2 = (byte)num;
		byte b3 = (byte)(num >> 8);
		return new byte[6]
		{
			b2,
			b3,
			b2,
			b3,
			b,
			(byte)(85 - b2 - b3 - b2 - b3 - b)
		};
	}

	internal static int DecodeDpi3955(ReadOnlySpan<byte> rec)
	{
		if (rec.Length != 6 || (rec.ToArray().Sum((byte b) => b) & 0xFF) != 85)
		{
			throw new IOException("Неверная контрольная сумма DPI 3955.");
		}
		int num = rec[4];
		int num2 = (rec[0] | (rec[1] << 8) | (((num >> 2) & 3) << 16)) + 1;
		if ((num & 1) != 0)
		{
			num2 *= 2;
		}
		if ((num & 2) != 0)
		{
			num2 *= 2;
		}
		return num2;
	}

	public async Task Apply(MouseSettings settings)
	{
		if (settings.ModelId != Spec.Mid)
		{
			throw new IOException("Подключена другая модель. Настройки не записаны; обнови устройство.");
		}
		settings.MaxRate = MaxRate;
		Validate(settings);
		MouseSettings mouseSettings = await ReadSettings();
		List<(int, byte[])> changes = new List<(int, byte[])>();
		for (int i = 0; i < 6; i++)
		{
			if (settings.Dpi[i] != mouseSettings.Dpi[i])
			{
				changes.Add((Spec.Sensor == "3955") ? (6912 + 6 * i, EncodeDpi3955(settings.Dpi[i])) : (12 + 4 * i, EncodeDpi(settings.Dpi[i], Spec.MaxDpi)));
			}
		}
		// PAW3955 stores separate X/Y values. Preserve Y unless it was explicitly edited.
		changes.Clear();
		for (int i = 0; i < 6; i++) {
			int y = settings.DpiY?[i] ?? settings.Dpi[i];
			if (settings.Dpi[i] != mouseSettings.Dpi[i] || y != mouseSettings.DpiY[i])
				changes.Add((6912 + i * 6, HubCodec.EncodeDpiAxes(settings.Dpi[i], y)));
			if (settings.DpiColors != null && !settings.DpiColors[i].Equals(mouseSettings.DpiColors[i], StringComparison.OrdinalIgnoreCase))
				changes.Add((44 + i * 4, HubCodec.EncodeColor(settings.DpiColors[i])));
		}
		Add(2, settings.Count, mouseSettings.Count);
		Add(4, settings.Active, mouseSettings.Active);
		Add(10, settings.Lod, mouseSettings.Lod);
		Add(169, settings.Debounce, mouseSettings.Debounce);
		Add(171, settings.MotionSync ? 1 : 0, mouseSettings.MotionSync ? 1 : 0);
		Add(173, settings.Sleep / 10, mouseSettings.Sleep / 10);
		Add(175, settings.AngleSnap ? 1 : 0, mouseSettings.AngleSnap ? 1 : 0);
		Add(177, settings.Ripple ? 1 : 0, mouseSettings.Ripple ? 1 : 0);
		Add(0, Rates[settings.Polling], Rates[mouseSettings.Polling]);
		foreach (var (address, value) in changes)
		{
			await Ask(7, address, value);
			if (!(await Read(address, value.Length)).SequenceEqual(value))
			{
				throw new IOException($"Запись {address} не подтверждена. Часть настроек могла примениться; перечитай устройство.");
			}
		}
		if (settings.Advanced != null) await ApplyAdvanced(settings.Advanced, mouseSettings.Advanced);
		if (settings.Buttons != null) await ApplyButtons(settings.Buttons);
		void Add(int item, int num, int previous)
		{
			if (num != previous)
			{
				changes.Add((item, PairBytes(num)));
			}
		}
	}

	public void Dispose()
	{
		stream.Dispose();
	}

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern uint CM_Get_Device_Interface_List_SizeW(ref uint length, ref Guid guid, string? id, uint flags);

	[DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
	private static extern uint CM_Get_Device_Interface_ListW(ref Guid guid, string? id, [Out] char[] buffer, uint length, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

	[DllImport("hid.dll")]
	private static extern bool HidD_GetPreparsedData(SafeFileHandle file, out nint data);

	[DllImport("hid.dll")]
	private static extern bool HidD_FreePreparsedData(nint data);

	[DllImport("hid.dll")]
	private static extern int HidP_GetCaps(nint data, out HidCaps caps);
}
