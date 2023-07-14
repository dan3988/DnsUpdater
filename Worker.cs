using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace DnsUpdater;

public class Worker : BackgroundService
{
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

	private CancellationToken _stopToken;
	private CancellationTokenSource? _lastChangeToken;

	public Worker(ILogger<Worker> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
	{
		_config = configuration.GetRequiredSection("Config").Get<Config>()!;
		_logger = logger;
		_httpClientFactory = httpClientFactory;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		_stopToken = stoppingToken;
		await CheckAddressAsync();
		NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
	}

	private async Task<IPAddress> GetIpAddressAsync()
	{
		using var client = _httpClientFactory.CreateClient();
		return await GetIpAddressAsync(client, _config.IpProvider, _stopToken);
	}

	private async Task CheckAddressAsync([CallerMemberName] string prefix = null!)
	{
		try
		{
			var ip = await GetIpAddressAsync();
			var vpn = VpnEnabled();
			if (vpn)
			{
				foreach (var ignore in _config.Ignore)
				{
					var dns = await Dns.GetHostEntryAsync(ignore, ip.AddressFamily, _stopToken);

					foreach (var address in dns.AddressList)
					{
						if (address.Equals(ip))
						{
							_logger.LogInformation("{prefix}: Ignoring address change to {ip}", prefix, ip);
							return;
						}
					}
				}

				_logger.LogInformation("{prefix}: Detected IP change to {ip}", prefix, ip);
			}
			else
			{
				_logger.LogInformation("{prefix}: No VPN detected, IP address: {IP}", prefix, ip);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "{prefix}: Failed to retrieve IP address.", prefix);
		}
	}

	private async void OnNetworkAddressChanged(object? sender, EventArgs e)
	{
		var delay = _config.ChangeDelay;
		if (delay > 0)
		{
			_logger.LogInformation("Network address change event, delaying for {ms}s", delay / 1000D);

			using var newToken = new CancellationTokenSource();
			_lastChangeToken?.Cancel();
			_lastChangeToken = newToken;

			try
			{
				await Task.Delay(delay, newToken.Token);
			}
			catch (TaskCanceledException)
			{
				_logger.LogInformation("Delay interrupted");
				return;
			}
			finally
			{
				Interlocked.CompareExchange(ref _lastChangeToken, null, newToken);
			}
		}

		await CheckAddressAsync();
	}
}
