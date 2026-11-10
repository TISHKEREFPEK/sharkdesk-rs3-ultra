namespace SharkDesk;

public sealed record BatteryDisplay(int? Percent, bool Approximate, int? ReportedPercent);
