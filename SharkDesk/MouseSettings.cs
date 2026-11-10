namespace SharkDesk;

public sealed class MouseSettings
{
	public int ModelId { get; set; } = 14;

	public int[] Dpi { get; set; } = new int[6] { 800, 2400, 3200, 5600, 8000, 52000 };

	public int[]? DpiY { get; set; }

	public string[]? DpiColors { get; set; }

	public AdvancedSettings? Advanced { get; set; }

	public ButtonAssignment[]? Buttons { get; set; }

	public int? OnboardProfile { get; set; }

	public int Count { get; set; } = 6;

	public int Active { get; set; }

	public int Polling { get; set; } = 1000;

	public int Lod { get; set; } = 1;

	public int Debounce { get; set; } = 4;

	public int Sleep { get; set; } = 60;

	public bool MotionSync { get; set; }

	public bool AngleSnap { get; set; }

	public bool Ripple { get; set; }

	public int? Battery { get; set; }

	public bool? Charging { get; set; }

	public int LedMode { get; set; }

	public int LedBrightness { get; set; } = 1;

	public int LedSpeed { get; set; } = 3;

	public string Firmware { get; set; } = "—";

	public int MaxRate { get; set; } = 8000;

	public string Connection { get; set; } = "—";
}
