using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SharkDesk;

public sealed class BatteryRuntimeEstimator
{
	public sealed record Calibration(double PercentPerHour, DateTimeOffset LearnedAt);

	private readonly string? file;

	private readonly Dictionary<string, Calibration> learned = new Dictionary<string, Calibration>();

	private string? context;

	private DateTimeOffset? previousAt;

	private int initialPercent;

	private int previousPercent;

	private double seconds;

	public BatteryRuntimeEstimator(string? file = null)
	{
		this.file = file;
		if (file == null)
		{
			return;
		}
		try
		{
			Dictionary<string, Calibration> dictionary = JsonSerializer.Deserialize<Dictionary<string, Calibration>>(File.ReadAllText(file));
			if (dictionary == null)
			{
				return;
			}
			foreach (var (key, calibration2) in dictionary)
			{
				if (double.IsFinite(calibration2.PercentPerHour))
				{
					double percentPerHour = calibration2.PercentPerHour;
					if (percentPerHour >= 0.05 && percentPerHour <= 50.0)
					{
						learned[key] = calibration2;
					}
				}
			}
		}
		catch (Exception ex) when ((ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) ? true : false)
		{
		}
	}

	public void Observe(BatteryStatus sample, string usageContext)
	{
		if (!sample.Charging && !sample.Estimated)
		{
			int? percent = sample.Percent;
			if (percent.HasValue)
			{
				int valueOrDefault = percent.GetValueOrDefault();
				if (valueOrDefault >= 0 && valueOrDefault <= 100)
				{
					if (!(context != usageContext) && previousAt.HasValue)
					{
						DateTimeOffset readAt = sample.ReadAt;
						DateTimeOffset? dateTimeOffset = previousAt;
						if (!(readAt <= dateTimeOffset))
						{
							readAt = sample.ReadAt;
							dateTimeOffset = previousAt;
							if (!(readAt - dateTimeOffset > TimeSpan.FromSeconds(15L)) && valueOrDefault <= previousPercent)
							{
								if (previousPercent - valueOrDefault >= 3 && seconds < 300.0)
								{
									Start(sample, usageContext, valueOrDefault);
									return;
								}
								seconds += (sample.ReadAt - previousAt.Value).TotalSeconds;
								bool flag = valueOrDefault < previousPercent;
								previousAt = sample.ReadAt;
								previousPercent = valueOrDefault;
								int num = initialPercent - valueOrDefault;
								if (flag && !(seconds < 1800.0) && num >= 3)
								{
									double num2 = (double)(num * 3600) / seconds;
									if ((!(num2 < 0.05) && !(num2 > 50.0)) || 1 == 0)
									{
										learned[usageContext] = new Calibration(num2, sample.ReadAt);
										Save();
									}
								}
								return;
							}
						}
					}
					Start(sample, usageContext, valueOrDefault);
					return;
				}
			}
		}
		Reset();
	}

	private void Start(BatteryStatus sample, string usageContext, int percent)
	{
		context = usageContext;
		previousAt = sample.ReadAt;
		initialPercent = (previousPercent = percent);
		seconds = 0.0;
	}

	public void Reset()
	{
		context = null;
		previousAt = null;
		seconds = 0.0;
	}

	public TimeSpan? Remaining(int percent, string usageContext, DateTimeOffset now)
	{
		bool flag = ((percent < 0 || percent > 100) ? true : false);
		if (flag || !learned.TryGetValue(usageContext, out Calibration value) || now < value.LearnedAt || now - value.LearnedAt > TimeSpan.FromDays(14))
		{
			return null;
		}
		return TimeSpan.FromHours((double)percent / value.PercentPerHour);
	}

	public void Save()
	{
		if (file == null)
		{
			return;
		}
		try
		{
			string text = file + ".tmp";
			File.WriteAllText(text, JsonSerializer.Serialize(learned));
			File.Move(text, file, overwrite: true);
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
		{
		}
	}
}
