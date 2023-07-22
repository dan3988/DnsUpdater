namespace DnsUpdater;

public sealed class WorkerResolver : ITemplateResolver
{
	private readonly IConfiguration _configuration;
	private readonly IReadOnlyDictionary<string, object?> _values;

	public WorkerResolver(IConfiguration configuration, IReadOnlyDictionary<string, object?> values)
	{
		_configuration = configuration;
		_values = values;
	}

	public object? Resolve(string name)
	{
		if (_values.TryGetValue(name, out var value))
			return value;

		return _configuration.GetSection(name).Value;
	}
}
