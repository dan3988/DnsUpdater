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

	public static async Task CopyToAsync(TextReader reader, TextWriter writer, CancellationToken cancellationToken = default)
	{
		var pool = ArrayPool<char>.Shared;
		var buffer = pool.Rent(2048);
		var remaining = -1;

		try
		{
			while (true)
			{
				var count = await reader.ReadAsync(buffer, cancellationToken);
				if (count == 0)
					return;

				await writer.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
			}
		}
		finally
		{
			pool.Return(buffer);
		}
	}

	public static Task LogToAsync(this HttpRequestMessage message, TextWriter writer, CancellationToken cancellationToken)
	{
		writer.WriteLine("{0} {1}", message.Method, message.RequestUri);
		return LogAsync(writer, "Request", message.Headers, message.Content, cancellationToken);
	}

	public static Task LogToAsync(this HttpResponseMessage message, TextWriter writer, CancellationToken cancellationToken)
	{
		writer.WriteLine("{0:D} {1}", message.StatusCode, message.ReasonPhrase);
		return LogAsync(writer, "Response", message.Headers, message.Content, cancellationToken);
	}

	private static async Task LogAsync(TextWriter writer, string prefix, HttpHeaders headers, HttpContent? content, CancellationToken cancellationToken)
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

			await CopyToAsync(reader, writer, cancellationToken);
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
