namespace SharkDesk;

public sealed record MouseSpec(int Mid, string Sensor, int MaxDpi)
{
	public string Name => Mid switch
	{
		11 => "X11 Ultra",
		14 => "RS3 Ultra",
		20 => "F1 Air",
		_ => $"Attack Shark · MID {Mid}",
	};

	public bool Experimental => Mid != 11 && Mid != 14;

	public int[] LodCodes
	{
		get
		{
			if (Sensor == "3955")
			{
				return new int[5] { 1, 2, 3, 4, 5 };
			}
			return new int[3] { 3, 1, 2 };
		}
	}

	public string[] LodNames
	{
		get
		{
			if (Sensor == "3955")
			{
				return new string[5] { "0.7", "0.9", "1.2", "1.4", "1.6" };
			}
			return new string[3] { "0.7", "1", "2" };
		}
	}

	public static MouseSpec? Find(int cid, int mid)
	{
		if (cid != 124)
		{
			return null;
		}
		switch (mid)
		{
		case 1:
		case 2:
		case 3:
		case 4:
		case 5:
		case 10:
		case 11:
		case 12:
		case 18:
			return new MouseSpec(mid, "3950", 42000);
		case 24:
			return new MouseSpec(mid, "3950", 60000);
		case 13:
		case 14:
		case 15:
		case 16:
		case 17:
		case 21:
			return new MouseSpec(mid, "3955", 52000);
		case 19:
		case 20:
		case 22:
		case 23:
			return new MouseSpec(mid, "3955", 60000);
		default:
			return null;
		}
	}

	public bool ValidDpi(int dpi)
	{
		if (!(Sensor == "3950"))
		{
			if (dpi >= 1 && dpi <= MaxDpi)
			{
				if (dpi > 42000)
				{
					return dpi % 2 == 0;
				}
				return true;
			}
			return false;
		}
		if (dpi >= 50 && dpi <= MaxDpi)
		{
			return dpi % ((dpi > 30000) ? 100 : 50) == 0;
		}
		return false;
	}
}
