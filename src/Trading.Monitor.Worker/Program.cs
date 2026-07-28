using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Analysis;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Infrastructure;
using Trading.Monitor.Infrastructure.Ai;
using Trading.Monitor.Infrastructure.Exchange;
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
    builder.Services.Configure<ExchangeExecutionOptions>(builder.Configuration.GetSection("ExchangeExecution"));

    builder.Services.AddSingleton<TechnicalAnalysisService>();
    builder.Services.AddSingleton<TradingSignalEngine>();
    builder.Services.AddSingleton<OpportunityProjectionService>();
    builder.Services.AddSingleton<OpportunityExitService>();
    builder.Services.AddSingleton(serviceProvider => new TradeInstructionService(serviceProvider.GetRequiredService<IOptionsMonitor<RiskOptions>>().CurrentValue));
    builder.Services.AddSingleton<MarketScanner>();

    builder.Services.AddSingleton<IMarketDataProvider>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<MarketDataSourceOptions>>().Value;
        var cryptoProviders = new List<IMarketDataProvider>();
        var forexProviders = new List<IMarketDataProvider>();

        if (options.BinanceEnabled)
            cryptoProviders.Add(new BinanceRestMarketDataProvider(CreateMarketClient(options.BinanceBaseUrl, options.TimeoutSeconds), "Binance"));

        if (options.BinanceUsEnabled)
            cryptoProviders.Add(new BinanceRestMarketDataProvider(CreateMarketClient(options.BinanceUsBaseUrl, options.TimeoutSeconds), "Binance US"));

        if (options.CoinbaseEnabled)
            cryptoProviders.Add(new CoinbaseExchangeMarketDataProvider(CreateMarketClient(options.CoinbaseBaseUrl, options.TimeoutSeconds)));

        if (options.KrakenEnabled)
            cryptoProviders.Add(new KrakenMarketDataProvider(CreateMarketClient(options.KrakenBaseUrl, options.TimeoutSeconds)));

        if (options.AlphaVantageForexEnabled)
        {
            var apiKey = Environment.GetEnvironmentVariable(options.AlphaVantageApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(apiKey))
                forexProviders.Add(new AlphaVantageForexMarketDataProvider(CreateMarketClient(options.AlphaVantageBaseUrl, options.TimeoutSeconds), apiKey));
        }

        if (options.YahooFinanceForexEnabled)
            forexProviders.Add(new YahooFinanceForexMarketDataProvider(CreateMarketClient(options.YahooFinanceBaseUrl, options.TimeoutSeconds)));

        return new MarketRoutingDataProvider(cryptoProviders, forexProviders, serviceProvider.GetRequiredService<ISourceTelemetryRecorder>());
    });

    builder.Services.AddSingleton<INewsProvider>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NewsOptions>>().Value;

        if (!options.Enabled)
            return new NoopNewsProvider();

        var providers = new List<INewsProvider>();
        var telemetryRecorder = serviceProvider.GetRequiredService<ISourceTelemetryRecorder>();

        var rssClient = CreateResearchClient(null, options.TimeoutSeconds);
        providers.Add(new RssNewsProvider(rssClient, options, telemetryRecorder));

        if (options.FearGreedEnabled)
            providers.Add(new FearGreedNewsProvider(CreateResearchClient(options.FearGreedBaseUrl, options.TimeoutSeconds), telemetryRecorder));

        if (options.CryptoPanicEnabled)
            providers.Add(new CryptoPanicNewsProvider(CreateResearchClient(options.CryptoPanicBaseUrl, options.TimeoutSeconds), options, telemetryRecorder));

        return new CompositeNewsProvider(providers, telemetryRecorder);
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
    builder.Services.AddHttpClient<IExchangeExecutionClient, BinanceSpotExecutionClient>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<ExchangeExecutionOptions>>().CurrentValue;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
    });
    builder.Services.AddScoped<ITradeExecutionService, SafeTradeExecutionService>();
    builder.Services.AddSingleton<INotificationChannel, ConsoleNotificationChannel>();

    builder.Services.AddSingleton<INotificationChannel>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value;
        return new EmailNotificationChannel(options.Email, serviceProvider.GetRequiredService<OpportunityProjectionService>(), serviceProvider.GetRequiredService<TradeInstructionService>(),
            serviceProvider.GetRequiredService<IOptionsMonitor<ReportingOptions>>());
    });

    builder.Services.AddSingleton<INotificationChannel>(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value;

        return new TelegramNotificationChannel(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, options.Telegram, serviceProvider.GetRequiredService<OpportunityProjectionService>(),
            serviceProvider.GetRequiredService<TradeInstructionService>(), serviceProvider.GetRequiredService<IOptionsMonitor<ReportingOptions>>());
    });

    builder.Services.AddHostedService<HistoricalMarketBackfillWorker>();
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

static HttpClient CreateResearchClient(string? baseUrl, int timeoutSeconds)
{
    var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 2, 60));
    var client = new HttpClient(new SocketsHttpHandler { ConnectTimeout = timeout }) { Timeout = timeout };

    if (!string.IsNullOrWhiteSpace(baseUrl))
        client.BaseAddress = new Uri(baseUrl);

    client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
    return client;
}
