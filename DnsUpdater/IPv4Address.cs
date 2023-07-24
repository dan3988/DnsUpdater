using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DnsUpdater;

[StructLayout(LayoutKind.Sequential, Size = sizeof(int))]
public readonly unsafe struct IPv4Address : IEquatable<IPv4Address>, IEqualityOperators<IPv4Address, IPv4Address, bool>, IParsable<IPv4Address>, ISpanParsable<IPv4Address>
{
	public static IPv4Address Parse(string s)
		=> Parse(s.AsSpan(), null);

	public static IPv4Address Parse(string s, IFormatProvider? provider)
		=> Parse(s.AsSpan(), provider);

	public static IPv4Address Parse(ReadOnlySpan<char> s)
		=> Parse(s, null);

	public static IPv4Address Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
		=> TryParse(s, provider, out var result) ? result : throw new FormatException("Input string was not in a correct format");

	public static bool TryParse([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out IPv4Address result)
		=> TryParse(s.AsSpan(), null, out result);

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out IPv4Address result)
		=> TryParse(s.AsSpan(), provider, out result);

	public static bool TryParse(ReadOnlySpan<char> s, [MaybeNullWhen(false)] out IPv4Address result)
		=> TryParse(s, null, out result);

	public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out IPv4Address result)
	{
		var copy = s;

		fixed (void* value = &result._value)
		{
			var ptr = (byte*)value;

			return TryParseSegment(ref copy, ref *ptr++, provider)
				&& TryParseSegment(ref copy, ref *ptr++, provider)
				&& TryParseSegment(ref copy, ref *ptr++, provider)
				&& byte.TryParse(copy, provider, out *ptr);
		}
	}

	private static bool TryParseSegment(ref ReadOnlySpan<char> data, ref byte part, IFormatProvider? provider, bool end = false)
	{
		var i = data.IndexOf('.');
		if (i == -1)
			return false;

		if (!byte.TryParse(data[..i], provider, out part))
			return false;

		data = data[(i + 1)..];
		return true;
	}

	private readonly uint _value;

	public IPv4Address(uint value)
	{
		_value = value;
	}

	public override int GetHashCode()
		=> unchecked((int)_value);

	public override bool Equals([NotNullWhen(true)] object? obj)
		=> obj is IPv4Address v && Equals(v);

	public bool Equals(IPv4Address other)
		=> _value == other._value;

	public static bool operator ==(IPv4Address left, IPv4Address right)
		=> left.Equals(right);

	public static bool operator !=(IPv4Address left, IPv4Address right)
		=> !left.Equals(right);

	public override string ToString()
	{
		static void FormatPart(byte* ptr, char* chars, ref int charIndex, bool end)
		{
			chars += charIndex;
			var span = new Span<char>(chars, 3);
			ptr->TryFormat(span, out var written);

			if (!end)
				chars[written++] = '.';

			charIndex += written;
		}

		var chars = stackalloc char[15];
		var index = 0;

		fixed (void* ptr = &_value)
		{
			var b = (byte*)ptr;
			FormatPart(b++, chars, ref index, false);
			FormatPart(b++, chars, ref index, false);
			FormatPart(b++, chars, ref index, false);
			FormatPart(b, chars, ref index, true);
		}

		return new string(chars, 0, index + 1);
	}
}
