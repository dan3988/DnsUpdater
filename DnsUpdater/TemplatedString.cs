using System.Text;

namespace DnsUpdater;

public interface ITemplateResolver
{
	object? Resolve(string name);
}

public sealed class TemplatedString
{
	private readonly record struct Entry(string Text, int PlaceholderIndex);

	private readonly record struct Placeholder(string Key, string? Format);

	public static TemplatedString Parse(ReadOnlySpan<char> text)
	{
		var minLength = 0;
		var last = 0;
		var entries = new ArrayBuilder<Entry>();
		var placeholders = new ArrayBuilder<Placeholder>();
		var i = 0;

		while (true)
		{
			if ((i = text.IndexOf('$', i)) < 0)
				break;

			if (i > 0 && text[i - 1] == '\\')
			{
				var begin = text[last..(i - 1)].ToString();
				last = i++;
				minLength += begin.Length;
				entries.Add(new(begin, -1));
				continue;
			}

			var ch = text[++i];
			if (ch == '{')
			{
				var close = text.IndexOf('}', i);
				if (close < 0)
					throw new FormatException("Placeholder not closed.");

				var begin = text[last..(i - 1)].ToString();
				var key = text[++i..close].ToString();

				entries.Add(new(begin, placeholders.Count));
				placeholders.Add(new(key, null));
				minLength += begin.Length;
				last = i = close + 1;
			}
		}

		var end = text[last..].ToString();

		return new(minLength + end.Length, end, entries.ToArray(), placeholders.ToArray());
	}

	private readonly int _minLength;
	private readonly string _end;
	private readonly Entry[] _entries;
	private readonly Placeholder[] _placeholders;

	private TemplatedString(int minLength, string end, Entry[] entries, Placeholder[] placeholders)
	{
		_minLength = minLength;
		_end = end;
		_entries = entries;
		_placeholders = placeholders;
	}

	public override string ToString()
	{
		if (_entries.Length == 0)
			return _end;

		var sb = new StringBuilder(_minLength);

		for (var i = 0; i < _entries.Length; i++)
		{
			var (text, placeholder) = _entries[i];
			sb.Append(text);
			if (placeholder < 0)
			{
				sb.Append('\\');
			}
			else
			{
				var (key, format) = _placeholders[placeholder];
				sb.Append("${").Append(key);
				if (format != null)
					sb.Append(',').Append(format);

				sb.Append('}');
			}
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
			var (text, placeholder) = _entries[i];
			sb.Append(text);
			if (placeholder >= 0)
			{
				var (key, format) = _placeholders[placeholder];
				var value = resolver.Resolve(key);
				if (!string.IsNullOrEmpty(format) && value is IFormattable fmt)
					value = fmt.ToString(format, provider);

				sb.Append(value);
			}
		}

		return sb.Append(_end).ToString();
	}
}
