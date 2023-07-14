using DnsUpdater;

using Microsoft.Extensions.Logging.EventLog;

IHost host = Host.CreateDefaultBuilder(args)
	.ConfigureLogging((ctx, logging) =>
	{
		if (OperatingSystem.IsWindows())
		{
			var options = ctx.Configuration.GetSection("Logging:EventLog").Get<EventLogSettings>();
			logging.AddEventLog(options ?? new());
		}
	})
	.ConfigureServices(services =>
	{
		services.AddHttpClient();
		services.AddHostedService<Worker>();
	})
	.Build();

host.Run();
