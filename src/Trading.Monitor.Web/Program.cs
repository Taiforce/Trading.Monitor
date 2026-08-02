using Serilog;
using Serilog.Events;
using Trading.Monitor.Application.Abstractions;
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
    builder.Services.AddSingleton<AiConsensusEngine>();
    builder.Services.AddSingleton(serviceProvider => new TradeInstructionService(serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<RiskOptions>>().CurrentValue));
    builder.Services.AddSingleton<VirtualPortfolioSimulator>();
    builder.Services.AddSingleton<TraderFollowSimulator>();
    builder.Services.AddSingleton<OperationalLogReader>();
    builder.Services.AddSingleton<OperationalLogInterpreter>();
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
    builder.Services.AddHttpClient<ConnectionRetryService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(12);
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
    app.MapGet("/api/operaciones-vivas", async (decimal? capital, string? estado, string? symbol, string? tipoSenal, string? mode, string? senal, string? mercado, LiveOperationsSnapshotService snapshotService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await snapshotService.GetAsync(capital, estado, symbol, tipoSenal, mode, senal, mercado, cancellationToken));
    });
    app.MapGet("/api/grafico-vivo", async (string? symbol, string? interval, decimal? capital, string? estado, string? tipoSenal, string? mode, string? senal, string? mercado, DateTimeOffset? from, DateTimeOffset? to,
        LiveChartSnapshotService chartService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await chartService.GetAsync(symbol, interval, capital, estado, tipoSenal, mode, senal, mercado, from, to, cancellationToken));
    });
    app.MapPost("/api/posiciones/{id:guid}/cerrar", async (Guid id, ManagedCloseRequest request, IOpportunityRepository opportunityRepository,
        Microsoft.Extensions.Options.IOptionsMonitor<ReportingOptions> reportingOptions, CancellationToken cancellationToken) =>
    {
        var capital = request.Capital <= 0m ? reportingOptions.CurrentValue.DefaultCapital : request.Capital;
        var rows = await opportunityRepository.GetSignalsAsync(capital, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Id == id);

        if (row is null)
            return Results.NotFound(new { message = "Señal no encontrada." });

        if (row.Status != Trading.Monitor.Domain.OpportunityStatus.Open)
            return Results.BadRequest(new { message = "La señal ya está cerrada." });

        var exitPrice = request.ExitPrice > 0m
            ? request.ExitPrice
            : TradeCostCalculator.ResolveExitPriceForNetPercent(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, request.TargetNetPercent, reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var breakdown = TradeCostCalculator.Build(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, exitPrice, reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        await opportunityRepository.UpdateManagedTargetAsync(row.Id, request.TargetNetPercent, cancellationToken);

        var reason = $"Cierre manual web al mercado actual. Objetivo neto configurado {request.TargetNetPercent:N2}%. Resultado neto {breakdown.NetPercent:N2}% después de comisiones.";
        var exit = new Trading.Monitor.Domain.OpportunityExit(Trading.Monitor.Domain.OpportunityStatus.ManuallyClosed, DateTimeOffset.UtcNow, exitPrice, reason);

        await opportunityRepository.UpdateExitAsync(row.Id, exit, breakdown.GrossBenefit, breakdown.NetBenefit, cancellationToken);

        return Results.Json(new
        {
            status = "cerrada",
            exitPrice,
            breakdown.NetBenefit,
            breakdown.NetPercent,
            breakdown.TotalObtained
        });
    });
    app.MapPost("/api/posiciones/{id:guid}/objetivo", async (Guid id, ManagedTargetRequest request, IOpportunityRepository opportunityRepository,
        Microsoft.Extensions.Options.IOptionsMonitor<ReportingOptions> reportingOptions, CancellationToken cancellationToken) =>
    {
        var capital = request.Capital <= 0m ? reportingOptions.CurrentValue.DefaultCapital : request.Capital;
        var rows = await opportunityRepository.GetSignalsAsync(capital, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Id == id);

        if (row is null)
            return Results.NotFound(new { message = "Señal no encontrada." });

        var targetNetPercent = request.TargetNetPercent <= 0m ? row.ManagedTargetNetPercent : request.TargetNetPercent;
        await opportunityRepository.UpdateManagedTargetAsync(row.Id, targetNetPercent, cancellationToken);

        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, targetNetPercent, reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var breakdown = TradeCostCalculator.Build(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, exitPrice, reportingOptions.CurrentValue.EstimatedFeePercentPerSide);

        return Results.Json(new
        {
            status = "actualizada",
            targetNetPercent,
            targetExitPrice = exitPrice,
            targetNetPnL = breakdown.NetBenefit,
            targetTotalObtained = breakdown.TotalObtained
        });
    });
    app.MapGet("/api/exchange/status", async (ExchangeConnectionStatusService statusService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await statusService.GetAsync(cancellationToken));
    });
    app.MapPost("/api/conexiones/reintentar", async (ConnectionRetryRequest request, ConnectionRetryService retryService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await retryService.RetryAsync(request, cancellationToken));
    });
    app.MapGet("/api/logs", (string? logFile, int? lines, string? nivel, string? evento, string? buscar, string? ambito,
        OperationalLogReader logReader, OperationalLogInterpreter logInterpreter) =>
    {
        var lineLimit = Math.Clamp(lines ?? 250, 50, 1000);
        var files = logReader.ListFiles();
        var snapshot = logReader.Read(logFile, lineLimit);
        var entries = logInterpreter.ApplyScope(logInterpreter.Interpret(snapshot), ambito);
        var filtered = logInterpreter.ApplyFilters(entries, nivel, evento, buscar, "todo");
        var buckets = logInterpreter.BuildBuckets(filtered);
        var maxBucket = buckets.Select(bucket => bucket.Count).DefaultIfEmpty(0).Max();

        return Results.Json(new
        {
            files,
            file = snapshot.File,
            snapshot.RootPath,
            snapshot.ErrorMessage,
            lines = snapshot.Lines,
            entries = filtered,
            buckets = buckets.Select(bucket => new
            {
                bucket.Hour,
                bucket.Count,
                Width = maxBucket <= 0 ? 0m : Math.Clamp((decimal)bucket.Count / maxBucket * 100m, 4m, 100m)
            }),
            availableLevels = entries.Select(entry => entry.Level).Where(level => level != "-").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(level => level),
            availableEvents = entries.Select(entry => entry.EventType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(type => type),
            errorCount = entries.Count(entry => entry.Level is "ERR" or "FTL"),
            warningCount = entries.Count(entry => entry.Level == "WRN"),
            signalCount = entries.Count(entry => entry.EventType == "Señal"),
            scanCount = entries.Count(entry => entry.EventType == "Barrido"),
            filteredCount = filtered.Count,
            scope = OperationalLogInterpreter.NormalizeScope(ambito)
        });
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

internal sealed record ManagedCloseRequest(decimal Capital, decimal TargetNetPercent, decimal ExitPrice);

internal sealed record ManagedTargetRequest(decimal Capital, decimal TargetNetPercent);
