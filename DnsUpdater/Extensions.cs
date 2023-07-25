using System.Buffers;
using System.Net.Http.Headers;

namespace DnsUpdater;

internal static class Extensions
{
	public static int IndexOf<T>(this ReadOnlySpan<T> span, T value, int startIndex)
		where T : IEquatable<T>
	{
		var i = span[startIndex..].IndexOf(value);
		return i < 0 ? i : i + startIndex;
	}

	public static Task CopyToAsync(this TextReader reader, TextWriter writer, CancellationToken cancellationToken = default)
		=> CopyToAsync(reader, writer, -1, cancellationToken);

	public static async Task<bool> CopyToAsync(TextReader reader, TextWriter writer, int limit, CancellationToken cancellationToken = default)
	{
		var pool = ArrayPool<char>.Shared;
		var buffer = pool.Rent(2048);
		var remaining = limit;

		try
		{
			while (true)
			{
				var count = await reader.ReadAsync(buffer, cancellationToken);
				if (count == 0)
					return false;

				if (remaining >= 0)
				{
					if (remaining <= count)
					{
						await writer.WriteAsync(buffer.AsMemory(0, remaining), cancellationToken);
						return true;
					}

					remaining -= count;
				}

				await writer.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
			}
		}
		finally
		{
			pool.Return(buffer);
		}
	}

	public static Task LogToAsync(this HttpRequestMessage message, TextWriter writer, int bodyTruncation, CancellationToken cancellationToken)
	{
		writer.WriteLine("{0} {1}", message.Method, message.RequestUri);
		return LogAsync(writer, "Request", message.Headers, message.Content, bodyTruncation, cancellationToken);
	}

	public static Task LogToAsync(this HttpResponseMessage message, TextWriter writer, int bodyTruncation, CancellationToken cancellationToken)
	{
		writer.WriteLine("{0:D} {1}", message.StatusCode, message.ReasonPhrase);
		return LogAsync(writer, "Response", message.Headers, message.Content, bodyTruncation, cancellationToken);
	}

	private static async Task LogAsync(TextWriter writer, string prefix, HttpHeaders headers, HttpContent? content, int bodyTruncation, CancellationToken cancellationToken)
	{
		writer.Write(prefix);
		writer.WriteLine(" Headers:");
		WriteHeaders(writer, headers);
		if (content != null)
		{
			WriteHeaders(writer, content.Headers);

			writer.WriteLine();
			writer.Write(prefix);
			writer.WriteLine(" Content:");

			using var stream = await content.ReadAsStreamAsync(cancellationToken);
			using var reader = new StreamReader(stream);

			if (await CopyToAsync(reader, writer, bodyTruncation, cancellationToken))
				writer.Write(" ...(truncated to {0} chars)", bodyTruncation);
		}

		writer.WriteLine();
		writer.WriteLine();
	}

	private static void WriteHeaders(TextWriter writer, HttpHeaders headers)
	{
		foreach (var header in headers)
		{
			foreach (var value in header.Value)
			{
				writer.Write('\t');
				writer.Write(header.Key);
				writer.Write(": ");
				writer.WriteLine(value);
			}
		}
	}
}
