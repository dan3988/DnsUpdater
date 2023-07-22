using System.Text;

namespace DnsUpdater;

public interface ITemplateResolver
{
	object? Resolve(string name);
}

public sealed class TemplatedString
{
	public static TemplatedString Parse(ReadOnlySpan<char> text)
	{
		var minLength = 0;
		var last = 0;
		var entries = new Entry[2];
		var entryCount = 0;

		while (true)
		{
			var i = text.IndexOf('$', last);
			if (i < 0)
				break;

			var ch = text[++i];
			if (ch == '{')
			{
				var close = text.IndexOf('}', i);
				if (close < 0)
					throw new FormatException("Literal not closed.");

				var begin = text[last..(i - 1)].ToString();
				var key = text[++i..close].ToString();

				if (entryCount == entries.Length)
					Array.Resize(ref entries, entryCount << 1);

				entries[entryCount++] = new Entry(begin, key, null);
				minLength += begin.Length;
				last = close + 1;
			}
		}

		var end = text[last..].ToString();
		minLength += end.Length;
		Array.Resize(ref entries, entryCount);

		return new(minLength, end, entries);
	}

	private readonly record struct Entry(string Text, string Key, string? Format);

	private readonly int _minLength;
	private readonly string _end;
	private readonly Entry[] _entries;

	private TemplatedString(int minLength, string end, Entry[] entries)
	{
		_minLength = minLength;
		_end = end;
		_entries = entries;
	}

	public override string ToString()
	{
		if (_entries.Length == 0)
			return _end;

		var sb = new StringBuilder(_minLength);

		for (var i = 0; i < _entries.Length; i++)
		{
			var (text, key, format) = _entries[i];
			sb.Append(text).Append("${").Append(key);
			if (format != null)
				sb.Append(',').Append(format);

			sb.Append('}');
		}

		return sb.Append(_end).ToString();
	}

	public string ToString(ITemplateResolver resolver, IFormatProvider? provider = null)
	{
		if (_entries.Length == 0)
			return _end;

		var sb = new StringBuilder(_minLength);

		for (var i = 0; i < _entries.Length; i++)
		{
			var (text, key, format) = _entries[i];
			var value = resolver.Resolve(key);
			if (!string.IsNullOrEmpty(format) && value is IFormattable fmt)
				value = fmt.ToString(format, provider);

			sb.Append(text).Append(value);
		}

		return sb.Append(_end).ToString();
	}
}
