using DnsUpdater;

IHost host = Host.CreateDefaultBuilder(args)
	.ConfigureServices(services =>
	{
		services.AddHttpClient();
		services.AddHostedService<Worker>();
	})
	.Build();

host.Run();
