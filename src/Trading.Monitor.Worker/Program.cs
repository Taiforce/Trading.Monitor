using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Infrastructure;
using Trading.Monitor.Infrastructure.Ai;
using Trading.Monitor.Infrastructure.MarketData;
using Trading.Monitor.Infrastructure.News;
using Trading.Monitor.Infrastructure.Notifications;
using Trading.Monitor.Infrastructure.Persistence;
using Trading.Monitor.Worker;

Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                                      .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                                      .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                                      .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                                      .Enrich.FromLogContext()
                                      .WriteTo.Console()
                                      .WriteTo.File("logs/worker-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
                                      .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    LocalEnvFile.TryLoadNearest(builder.Environment.ContentRootPath, ".env.local");
    builder.Configuration.AddJsonFile("appsettings.Local.json", true, true);
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, true);

    builder.Services.Configure<TradingMonitorOptions>(builder.Configuration.GetSection("TradingMonitor"));
    builder.Services.Configure<RiskOptions>(builder.Configuration.GetSection("Risk"));
    builder.Services.Configure<NewsOptions>(builder.Configuration.GetSection("News"));
    builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
    builder.Services.Configure<MarketDataSourceOptions>(builder.Configuration.GetSection("MarketSources"));
    builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAi"));
    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
    builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection("Notifications"));
    builder.Services.Configure<ReportingOptions>(builder.Configuration.GetSection("Reporting"));

    builder.Services.AddSingleton<TechnicalAnalysisService>();
    builder.Services.AddSingleton<TradingSignalEngine>();
    builder.Services.AddSingleton<OpportunityProjectionService>();
    builder.Services.AddSingleton<MarketScanner>();

    builder.Services.AddSingleton<IMarketDataProvider>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<MarketDataSourceOptions>>().Value;
        var providers = new List<IMarketDataProvider>();

        if (options.BinanceEnabled)
            providers.Add(new BinanceRestMarketDataProvider(CreateMarketClient(options.BinanceBaseUrl, options.TimeoutSeconds), "Binance"));

        if (options.BinanceUsEnabled)
            providers.Add(new BinanceRestMarketDataProvider(CreateMarketClient(options.BinanceUsBaseUrl, options.TimeoutSeconds), "Binance US"));

        if (options.CoinbaseEnabled)
            providers.Add(new CoinbaseExchangeMarketDataProvider(CreateMarketClient(options.CoinbaseBaseUrl, options.TimeoutSeconds)));

        if (options.KrakenEnabled)
            providers.Add(new KrakenMarketDataProvider(CreateMarketClient(options.KrakenBaseUrl, options.TimeoutSeconds)));

        return new CompositeMarketDataProvider(providers, serviceProvider.GetRequiredService<ISourceTelemetryRecorder>());
    });

    builder.Services.AddSingleton<INewsProvider>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NewsOptions>>().Value;

        if (!options.Enabled)
            return new NoopNewsProvider();

        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 2, 60)) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
        return new RssNewsProvider(client, options, serviceProvider.GetRequiredService<ISourceTelemetryRecorder>());
    });

    builder.Services.AddSingleton<IResearchAnalyzer>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<OpenAiOptions>>().Value;

        if (!options.Enabled)
            return new NoopResearchAnalyzer();

        var client = new HttpClient { BaseAddress = new Uri(options.BaseUrl), Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120)) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
        return new OpenAiResearchAnalyzer(client, options, serviceProvider.GetRequiredService<ISourceTelemetryRecorder>());
    });

    builder.Services.AddTradingMonitorDatabase(builder.Configuration, builder.Environment.ContentRootPath);
    builder.Services.AddSingleton<INotificationChannel, ConsoleNotificationChannel>();

    builder.Services.AddSingleton<INotificationChannel>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value;
        return new EmailNotificationChannel(options.Email, serviceProvider.GetRequiredService<OpportunityProjectionService>(), serviceProvider.GetRequiredService<IOptionsMonitor<ReportingOptions>>());
    });

    builder.Services.AddSingleton<INotificationChannel>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value;

        return new TelegramNotificationChannel(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, options.Telegram, serviceProvider.GetRequiredService<OpportunityProjectionService>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<ReportingOptions>>());
    });

    builder.Services.AddHostedService<MarketMonitorWorker>();

    var host = builder.Build();
    await DatabaseInitializer.EnsureCreatedAsync(host.Services);
    host.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Trading monitor worker terminated unexpectedly.");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static HttpClient CreateMarketClient(string baseUrl, int timeoutSeconds)
{
    var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 60));
    var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = timeout }) { BaseAddress = new Uri(baseUrl), Timeout = timeout };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
    return client;
}
