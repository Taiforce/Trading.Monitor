using Serilog;
using Serilog.Events;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Infrastructure;
using Trading.Monitor.Infrastructure.Persistence;
using Trading.Monitor.Web.Services;

Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
                                      .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                                      .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                                      .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                                      .Enrich.FromLogContext()
                                      .WriteTo.Console()
                                      .WriteTo.File("logs/web-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
                                      .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddJsonFile("appsettings.Local.json", true, true);
    builder.Host.UseSerilog();

    builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
    builder.Services.Configure<ReportingOptions>(builder.Configuration.GetSection("Reporting"));
    builder.Services.Configure<RiskOptions>(builder.Configuration.GetSection("Risk"));
    builder.Services.Configure<ExchangeExecutionOptions>(builder.Configuration.GetSection("ExchangeExecution"));
    builder.Services.AddSingleton<OpportunityProjectionService>();
    builder.Services.AddSingleton(serviceProvider => new TradeInstructionService(serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<RiskOptions>>().CurrentValue));
    builder.Services.AddSingleton<VirtualPortfolioSimulator>();
    builder.Services.AddSingleton<TraderFollowSimulator>();
    builder.Services.AddSingleton<OperationalLogReader>();
    builder.Services.AddScoped<LiveOperationsSnapshotService>();
    builder.Services.AddHttpClient<LiveChartSnapshotService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
    });
    builder.Services.AddHttpClient<ExchangeConnectionStatusService>((serviceProvider, client) =>
    {
        var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ExchangeExecutionOptions>>().CurrentValue;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Trading.Monitor/1.0");
    });
    builder.Services.AddTradingMonitorDatabase(builder.Configuration, builder.Environment.ContentRootPath);
    builder.Services.AddRazorPages();

    var app = builder.Build();
    await DatabaseInitializer.EnsureCreatedAsync(app.Services);

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        app.UseHttpsRedirection();

    app.UseRouting();
    app.UseAuthorization();
    app.MapStaticAssets();
    app.MapGet("/api/operaciones-vivas", async (decimal? capital, string? estado, string? symbol, string? tipoSenal, LiveOperationsSnapshotService snapshotService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await snapshotService.GetAsync(capital, estado, symbol, tipoSenal, cancellationToken));
    });
    app.MapGet("/api/grafico-vivo", async (string? symbol, string? interval, decimal? capital, string? estado, string? tipoSenal, DateTimeOffset? from, DateTimeOffset? to,
        LiveChartSnapshotService chartService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await chartService.GetAsync(symbol, interval, capital, estado, tipoSenal, from, to, cancellationToken));
    });
    app.MapGet("/api/exchange/status", async (ExchangeConnectionStatusService statusService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await statusService.GetAsync(cancellationToken));
    });
    app.MapRazorPages().WithStaticAssets();
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "Trading monitor web terminated unexpectedly.");
}
finally
{
    await Log.CloseAndFlushAsync();
}
