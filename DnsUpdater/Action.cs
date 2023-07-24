using Newtonsoft.Json;

namespace DnsUpdater;

[JsonObject]
public sealed class Action
{
	[JsonProperty]
	public required string Location { get; init; }

	[JsonProperty]
	public string Method { get; init; } = "POST";

	[JsonProperty]
	public IReadOnlyDictionary<string, string>? Headers { get; init; }

	[JsonProperty]
	public string? Body { get; init; }

	public CompiledAction Compile()
	{
		var location = TemplatedString.Parse(Location);
		var body = Body == null ? null : TemplatedString.Parse(Body);
		var headers = new Dictionary<string, TemplatedString>();
		if (Headers != null)
		{
			foreach (var (key, value) in Headers)
			{
				var template = TemplatedString.Parse(value);
				headers.Add(key, template);
			}
		}

		return new(location, Method, headers, body);
	}
}
