using System;

namespace SharkDesk;

public sealed class BatteryDisplayFilter
{
	private double? displayed;

	private int? model;

	private bool charging;

	private DateTimeOffset lastAt;

	private DateTimeOffset settleUntil;

	public BatteryDisplay Observe(BatteryStatus sample, int modelId)
	{
		int? percent = sample.Percent;
		if (percent.HasValue)
		{
			int valueOrDefault = percent.GetValueOrDefault();
			if (!displayed.HasValue || model != modelId || sample.ReadAt - lastAt > TimeSpan.FromMinutes(2L))
			{
				displayed = valueOrDefault;
				model = modelId;
				charging = sample.Charging;
				lastAt = sample.ReadAt;
				settleUntil = sample.ReadAt;
				return new BatteryDisplay(valueOrDefault, sample.Estimated, valueOrDefault);
			}
			double num = Math.Clamp((sample.ReadAt - lastAt).TotalSeconds, 0.0, 5.0);
			lastAt = sample.ReadAt;
			if (charging != sample.Charging)
			{
				charging = sample.Charging;
				settleUntil = sample.ReadAt.AddSeconds(20.0);
			}
			if (!sample.Charging && valueOrDefault <= 10)
			{
				displayed = valueOrDefault;
			}
			else if (sample.ReadAt >= settleUntil)
			{
				double value = (double)valueOrDefault - displayed.Value;
				double num2 = Math.Min(Math.Abs(value) * (1.0 - Math.Exp((0.0 - num) / 30.0)), num / 15.0);
				displayed += (double)Math.Sign(value) * num2;
			}
			int num3 = Math.Clamp((int)Math.Round(displayed.Value), 0, 100);
			return new BatteryDisplay(num3, sample.Estimated || num3 != valueOrDefault, valueOrDefault);
		}
		return new BatteryDisplay(null, Approximate: true, null);
	}
}
