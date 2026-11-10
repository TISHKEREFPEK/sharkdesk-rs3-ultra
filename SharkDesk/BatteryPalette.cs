namespace SharkDesk;

public static class BatteryPalette
{
	public static string ColorFor(int? percent, bool charging)
	{
		if (!charging)
		{
			if (percent.HasValue)
			{
				int valueOrDefault = percent.GetValueOrDefault();
				if (valueOrDefault > 20)
				{
					if (valueOrDefault > 50)
					{
						return "#159447";
					}
					return "#C18A08";
				}
				return "#D83B39";
			}
			return "#83838B";
		}
		return "#159447";
	}
}
