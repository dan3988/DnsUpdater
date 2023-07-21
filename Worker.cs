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
		var parent = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		var dir = Path.Combine(parent, AppDomain.CurrentDomain.FriendlyName);
		var file = Path.Combine(dir, "last_ip");

		Directory.CreateDirectory(dir);
		return File.Open(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
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

	private static async Task<IPAddress> GetIpAddressAsync(HttpClient client, Uri provider, CancellationToken cancellationToken = default)
	{
		using var res = await client.GetAsync(provider, cancellationToken);

		if (res.IsSuccessStatusCode)
		{
			string data;

			using (var content = res.Content.ReadAsStream(cancellationToken))
			using (var reader = new StreamReader(content))
				data = await reader.ReadLineAsync(cancellationToken) ?? "";

			return IPAddress.Parse(data);
		}
		else
		{
			throw new HttpRequestException("Request failed.", null, res.StatusCode);
		}
	}

	private static bool VpnEnabled()
	{
		var interfaces = NetworkInterface.GetAllNetworkInterfaces();
		return interfaces.Any(v => v.OperationalStatus != OperationalStatus.Down && (v.NetworkInterfaceType == NetworkInterfaceType.Ppp || v.NetworkInterfaceType == NetworkInterfaceType.Tunnel));
	}

	private readonly ILogger<Worker> _logger;
	private readonly Config _config;
	private readonly IHttpClientFactory _httpClientFactory;

	public Worker(ILogger<Worker> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
	{
		_config = configuration.GetRequiredSection("Config").Get<Config>()!;
		_logger = logger;
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

	private Task OnChangeAsync(HttpClient client, IPAddress address, CancellationToken stoppingToken)
	{
		_logger.LogInformation("Updating IP address to {address}", address);
		return Task.CompletedTask;
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
				await OnChangeAsync(client, ip, stoppingToken);

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

	private async Task<IPAddress?> CheckAddressAsync(HttpClient client, CancellationToken cancellationToken = default)
	{
		try
		{
			var ip = await GetIpAddressAsync(client, _config.IpProvider, cancellationToken);
			var vpn = VpnEnabled();
			if (vpn)
			{
				foreach (var ignore in _config.Ignore)
				{
					var dns = await Dns.GetHostEntryAsync(ignore, ip.AddressFamily, cancellationToken);

					foreach (var address in dns.AddressList)
					{
						if (address.Equals(ip))
						{
							_logger.LogInformation("Ignoring address change to {ip} as it matchese ignore address {ignore}", ip, ignore);
							return null;
						}
					}
				}

				_logger.LogInformation("Detected IP change to {ip}", ip);
			}
			else
			{
				_logger.LogInformation("No VPN detected, IP address: {IP}", ip);
			}

			return ip;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve IP address.");
			return null;
		}
	}
}
