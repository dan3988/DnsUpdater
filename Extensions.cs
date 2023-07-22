namespace DnsUpdater;

internal static class Extensions
{
	public static int IndexOf<T>(this ReadOnlySpan<T> span, T value, int startIndex)
		where T : IEquatable<T>
	{
		var i = span[startIndex..].IndexOf(value);
		return i < 0 ? i : i + startIndex;
	}
}
