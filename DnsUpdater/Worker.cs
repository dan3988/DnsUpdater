using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace DnsUpdater;

public class Worker : BackgroundService
{
	private const int ipv4Bytes = 4;
	private const int ipv6Bytes = 16;

	private static FileStream GetLastIpFile()
	{
		return File.Open("last_ip", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
	}

	private static bool TryGetLastIp(Stream stream, [MaybeNullWhen(false)] out IPAddress ip)
	{
		if (stream.Length != ipv4Bytes && stream.Length != ipv6Bytes)
		{
			ip = null;
			return false;
		}

		Span<byte> data = stackalloc byte[(int)stream.Length];
		stream.ReadExactly(data);
		ip = new(data);
		return true;
	}

	private static void WriteIp(Stream stream, IPAddress ip)
	{
		var bytes = ip.GetAddressBytes();
		stream.SetLength(0);
		stream.Write(bytes);
		stream.Flush();
	}

	private static void AppendIp(TextWriter stream, IPAddress ip)
	{
		stream.WriteLine("[{0:O}] {1}", DateTime.UtcNow, ip);
		stream.Flush();
	}

	private static Task<string> LogFailingRequestAsync(HttpResponseMessage response, int bodyTruncation, CancellationToken cancellationToken = default)
		=> LogFailingRequestAsync(response.RequestMessage, response, bodyTruncation, cancellationToken);

	private static async Task<string> LogFailingRequestAsync(HttpRequestMessage? request, HttpResponseMessage response, int bodyTruncation, CancellationToken cancellationToken = default)
	{
		var dir = Path.GetFullPath("Failed Requests");
		var file = Path.Join(dir, $"{DateTime.UtcNow:yyyMMdd-HHmmssfff}.log");

		Directory.CreateDirectory(dir);

		using var writer = File.CreateText(file);

		if (request != null)
			await request.LogToAsync(writer, bodyTruncation, cancellationToken);

		await response.LogToAsync(writer, bodyTruncation, cancellationToken);

		return file;
	}

	private static (bool Online, bool Vpn) GetNetworkStatus()
	{
		var interfaces = NetworkInterface.GetAllNetworkInterfaces();
		var vpn = false;
		var online = false;
		for (var i = 0; i < interfaces.Length && !(online && vpn && false); i++)
		{
			var iface = interfaces[i];
			if (iface.NetworkInterfaceType == NetworkInterfaceType.Tunnel || iface.NetworkInterfaceType == NetworkInterfaceType.Ppp)
			{
				vpn |= iface.OperationalStatus == OperationalStatus.Up;
			}
			else if (iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			{
				online |= iface.OperationalStatus == OperationalStatus.Up;
			}
		}

		return (online, vpn);
	}

	private readonly ILogger<Worker> _logger;
	private readonly IConfiguration _configuration;
	private readonly Config _config;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly CompiledAction[] _actions;

	public Worker(ILogger<Worker> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
	{
		_logger = logger;
		_configuration = configuration;
		_config = configuration.GetRequiredSection("Config").Get<Config>()!;
		_actions = _config.Actions.Select(v => v.Compile()).ToArray();
		_httpClientFactory = httpClientFactory;
	}

	private TextWriter? GetHistoryFile()
	{
		if (string.IsNullOrEmpty(_config.HistoryFile))
			return null;

		var path = Environment.ExpandEnvironmentVariables(_config.HistoryFile)!;
		try
		{
			var dir = Path.GetDirectoryName(path)!;
			Directory.CreateDirectory(dir);
			var stream = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.Read);
			return new StreamWriter(stream, Encoding.UTF8);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to open history file for writing. Resolved path: {path}", path);
			return null;
		}
	}

	private async Task OnChangeAsync(HttpClient client, IPAddress address, IPAddress? oldAddress, CancellationToken stoppingToken)
	{
		_logger.LogInformation("Updating IP address to {address}", address);

		var values = new Dictionary<string, object?>
		{
			["IP"] = address,
			["OLDIP"] = oldAddress,
			["DATE"] = DateTime.Now
		};

		var resolver = new WorkerResolver(_configuration, values);

		for (var i = 0; i < _actions.Length; i++)
		{
			var action = _actions[i];

			using var message = action.CreateRequest(resolver);
			using var response = await client.SendAsync(message, stoppingToken);

			if (response.IsSuccessStatusCode)
			{
				var body = response.Content == null ? null : await response.Content.ReadAsStringAsync(stoppingToken);
				_logger.LogInformation( "Response from {location}: {code} {text}\n{body}", message.RequestUri, (int)response.StatusCode, response.ReasonPhrase, body);
			}
			else
			{
				var file = await LogFailingRequestAsync(message, response, _config.HttpBodyLogTruncation, stoppingToken);
				_logger.LogWarning("Request for action {index}. Response written to {file}", i, file);
			}
		}
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var ss = new SemaphoreSlim(0, 1);
		using var client = _httpClientFactory.CreateClient();
		using var ipFile = GetLastIpFile();
		using var historyFile = GetHistoryFile();

		var lastChangeToken = new CancellationTokenSource();

		async Task<IPAddress?> CheckAsync(IPAddress? lastIp)
		{
			var ip = await CheckAddressAsync(client, stoppingToken);
			if (ip != null && !ip.Equals(lastIp))
			{
				await OnChangeAsync(client, ip, lastIp, stoppingToken);

				WriteIp(ipFile, ip);

				if (historyFile != null)
					AppendIp(historyFile, ip);

				return ip;
			}

			return lastIp;
		}

		async void OnNetworkAddressChanged(object? sender, EventArgs evt)
		{
			_logger.LogInformation("Network address change event");

			var delay = _config.ChangeDelay;
			if (delay > 0)
			{
				_logger.LogInformation("Delaying for {ms}s", delay / 1000D);

				lastChangeToken.Cancel();
				lastChangeToken.Dispose();
				lastChangeToken = new();

				try
				{
					await Task.Delay(delay, lastChangeToken.Token);
				}
				catch (TaskCanceledException)
				{
					_logger.LogInformation("Delay interrupted by another network address changed event.");
					return;
				}
			}

			ss.Release();
		}

		try
		{
			TryGetLastIp(ipFile, out var lastIp);

			_logger.LogInformation("IP address from last_ip file: {ip}", lastIp);

			NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

			while (!stoppingToken.IsCancellationRequested)
			{
				lastIp = await CheckAsync(lastIp);
				await ss.WaitAsync(stoppingToken);
			}
		}
		catch (OperationCanceledException ex)
		{
			_logger.LogInformation(ex, "Program cancelled.");
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "Fatal exception");
		}
		finally
		{
			NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
		}
	}

	private async Task<IPAddress?> TryGetIpAddressAsync(HttpClient client, Uri provider, CancellationToken cancellationToken = default)
	{
		try
		{
			using var res = await client.GetAsync(provider, cancellationToken);

			if (!res.IsSuccessStatusCode)
			{
				var file = await LogFailingRequestAsync(res, _config.HttpBodyLogTruncation, cancellationToken);
				_logger.LogError("IP provider responded with non 200 status code {code}. Response written to {file}", (int)res.StatusCode, file);
				return null;
			}

			var body = await res.Content.ReadAsStringAsync(cancellationToken);
			if (!IPAddress.TryParse(body.Trim(), out var ipAddress))
			{
				var file = await LogFailingRequestAsync(res, _config.HttpBodyLogTruncation, cancellationToken);
				_logger.LogError("IP provider returned an invalid IP address string. Response written to {file}", file);
				return null;
			}

			return ipAddress;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve IP address.");
			return null;
		}
	}

	private async Task<IPAddress?> CheckAddressAsync(HttpClient client, CancellationToken cancellationToken = default)
	{
		var (connected, vpn) = GetNetworkStatus();
		if (!connected)
		{
			_logger.LogInformation("No connection detected.");
			return null;
		}

		var ip = await TryGetIpAddressAsync(client, _config.IpProvider, cancellationToken);
		if (ip == null)
			return null;

		if (vpn)
		{
			var _ignore = default(string);
			try
			{
				foreach (var ignore in _config.Ignore)
				{
					_ignore = ignore;
					var dns = await Dns.GetHostEntryAsync(ignore, ip.AddressFamily, cancellationToken);

					foreach (var address in dns.AddressList)
					{
						if (address.Equals(ip))
						{
							_logger.LogInformation("Ignoring ip {ip} as it matches ignore address {ignore}", ip, ignore);
							return null;
						}
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error checking ignore list item \"{item}\".", _ignore);
				return null;
			}

			_logger.LogInformation("VPN detected, IP not in ignore list: {ip}", ip);
		}
		else
		{
			_logger.LogInformation("No VPN detected: {ip}", ip);
		}

		return ip;
	}
}
