using DnsUpdater;

using Microsoft.Extensions.Logging.EventLog;

//IHost host = Host.CreateDefaultBuilder(args)
var host = new HostBuilder()
	.ConfigureHostConfiguration(c => c.AddEnvironmentVariables(prefix: "DOTNET_"))
	.ConfigureAppConfiguration((ctx, b) =>
	{
		b.AddJsonFile("appsettings.json");
		b.AddJsonFile($"appsettings.{ctx.HostingEnvironment.EnvironmentName}.json");
		b.AddEnvironmentVariables();
	})
	.ConfigureLogging((ctx, logging) =>
	{
		var config = ctx.Configuration.GetSection("Logging");

		logging.AddConfiguration(config);
		logging.AddSimpleConsole();

		var serilog = config.GetSection("Serilog");
		if (serilog != null && serilog.Exists())
		{
			logging.AddFile(serilog);
		}
		else
		{
			logging.AddFile("Logs\\{Date}.log");
		}

		if (OperatingSystem.IsWindows())
		{
			var options = config.GetSection("EventLog").Get<EventLogSettings>();
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
