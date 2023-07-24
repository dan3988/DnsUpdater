using System.Diagnostics;

using DnsUpdater;

using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

//IHost host = Host.CreateDefaultBuilder(args)
var host = new HostBuilder()
	.ConfigureHostConfiguration(b =>
	{
		b.AddEnvironmentVariables();
		b.AddJsonFile("appsettings.json");
	})
	.ConfigureAppConfiguration((ctx, b) =>
	{
		b.AddEnvironmentVariables();

		if (ctx.HostingEnvironment.IsDevelopment())
		{
			b.AddJsonFile($"appsettings.Development.json");
		}

		var baseDir = ctx.Configuration.GetValue<string>("BaseDir");
		if (!string.IsNullOrEmpty(baseDir))
		{
			baseDir = Environment.ExpandEnvironmentVariables(baseDir);
			Environment.SetEnvironmentVariable("BASEDIR", baseDir);
			Environment.CurrentDirectory = baseDir;
			var config = Path.GetFullPath("appsettings.json");
			b.AddJsonFile(config, false);
		}
	})
	.ConfigureLogging((ctx, logging) =>
	{
		var config = ctx.Configuration.GetSection("Logging");

		logging.AddConfiguration(config);
		logging.AddSimpleConsole();
		logging.AddDebug();

		var serilog = config.GetSection("Serilog");
		if (serilog != null && serilog.Exists())
		{
			logging.AddFile(serilog);
		}
		else
		{
			logging.AddFile("Logs\\{Date}.log");
		}
	})
	.ConfigureServices((ctx, services) =>
	{
		if (OperatingSystem.IsWindows())
		{
			services.Configure((EventLogSettings settings) =>
			{
				Debug.Assert(OperatingSystem.IsWindows());

				settings.SourceName = ctx.HostingEnvironment.ApplicationName;

				var options = ctx.Configuration.GetSection("Logging:EventLog");
				if (options.Exists())
					options.Bind(settings);
			});

			services.AddLogging(v =>
			{
				Debug.Assert(OperatingSystem.IsWindows());
				v.AddEventLog();
			});

			if (!Environment.UserInteractive)
			{
				services.AddSingleton<IHostLifetime, WindowsServiceLifetime>();
				services.Configure<WindowsServiceLifetimeOptions>(v =>
				{
					v.ServiceName = ctx.HostingEnvironment.ApplicationName;
				});
			}
		}

		services.AddHttpClient();
		services.AddHostedService<Worker>();
	})
	.Build();

await host.RunAsync();
