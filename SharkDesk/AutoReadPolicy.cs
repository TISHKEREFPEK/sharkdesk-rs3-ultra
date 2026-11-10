namespace SharkDesk;

public static class AutoReadPolicy
{
	public static bool CanReplace(bool dirtyBefore, bool dirtyNow, long revisionBefore, long revisionNow)
	{
		if (!dirtyBefore && !dirtyNow)
		{
			return revisionBefore == revisionNow;
		}
		return false;
	}
}
