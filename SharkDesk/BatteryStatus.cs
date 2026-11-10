using System;

namespace SharkDesk;

public sealed record BatteryStatus(int? Percent, bool Charging, int Millivolts, bool Estimated, DateTimeOffset ReadAt);
