using Newtonsoft.Json;

namespace DnsUpdater;

[JsonObject]
public sealed class Config
{
	public static Config Load(string path)
	{
		using var stream = File.OpenRead(path);
		using var reader = new StreamReader(stream);
		using var jr = new JsonTextReader(reader);

		var s = JsonSerializer.Create();
		return s.Deserialize<Config>(jr)!;
	}

	[JsonProperty(Required = Required.Always)]
	public required Uri IpProvider { get; init; }

	[JsonProperty]
	public int ChangeDelay { get; init; } = 1000;

	[JsonProperty]
	public List<string> Ignore { get; init; } = new();

	[JsonProperty]
	public string? HistoryFile { get; init; }
}
