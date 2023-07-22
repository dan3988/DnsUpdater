namespace DnsUpdater;

public sealed record CompiledAction(TemplatedString Location, string Method, IReadOnlyDictionary<string, TemplatedString> Headers, TemplatedString? Body)
{
	public HttpRequestMessage CreateRequest(ITemplateResolver resolver, IFormatProvider? formatProvider = null)
	{
		var url = Location.ToString(resolver, formatProvider);
		var uri = new Uri(url);

		var method = new HttpMethod(Method);
		var message = new HttpRequestMessage(method, uri);

		if (Body != null)
		{
			var body = Body.ToString(resolver, formatProvider);
			message.Content = new StringContent(body);
			message.Content.Headers.ContentType = null;
		}

		foreach (var (key, value) in Headers)
		{
			var headerValue = value.ToString(resolver, formatProvider);
			if (!message.Headers.TryAddWithoutValidation(key, headerValue) && (message.Content == null || !message.Content.Headers.TryAddWithoutValidation(key, headerValue)))
				throw new InvalidOperationException("Failed to set header: " + key);
		}

		return message;
	}
}
