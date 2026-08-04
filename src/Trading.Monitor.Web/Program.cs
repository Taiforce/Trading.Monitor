using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Infrastructure;
using Trading.Monitor.Infrastructure.Persistence;
using Trading.Monitor.Web.Configuration;
using Trading.Monitor.Web.Health;
using Trading.Monitor.Web.Services;

var bootstrapLogger = new LoggerConfiguration().MinimumLevel.Information()
                                               .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                                               .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                                               .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                                               .Enrich.FromLogContext()
                                               .WriteTo.Console();

if (!string.Equals(Environment.GetEnvironmentVariable("TRADING_MONITOR_DISABLE_FILE_LOGS"), "true", StringComparison.OrdinalIgnoreCase))
{
    var logDirectory = Environment.GetEnvironmentVariable("TRADING_MONITOR_LOG_DIRECTORY") ?? "logs";
    Directory.CreateDirectory(logDirectory);
    bootstrapLogger.WriteTo.File(Path.Combine(logDirectory, "web-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, shared: true);
}

Log.Logger = bootstrapLogger.CreateLogger();

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
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        Directory.CreateDirectory(dataProtectionKeysPath);
        builder.Services.AddDataProtection()
                        .SetApplicationName("Trading.Monitor")
                        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
    }
    builder.Services.AddOptions<AdminAccessOptions>()
                    .Bind(builder.Configuration.GetSection(AdminAccessOptions.SectionName));

    var adminAccess = builder.Configuration.GetSection(AdminAccessOptions.SectionName).Get<AdminAccessOptions>() ?? new AdminAccessOptions();

    if (adminAccess.Enabled)
    {
        if (string.IsNullOrWhiteSpace(adminAccess.Username) || string.IsNullOrWhiteSpace(adminAccess.Password))
            throw new InvalidOperationException("AdminAccess is enabled, but AdminAccess:Username or AdminAccess:Password is missing.");

        if (adminAccess.Password.Length < Math.Max(8, adminAccess.MinimumPasswordLength))
            throw new InvalidOperationException($"AdminAccess:Password must be at least {adminAccess.MinimumPasswordLength} characters long.");

        if (AdminAccessOptions.DisallowedPasswords.Any(weak => string.Equals(weak, adminAccess.Password, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("AdminAccess:Password matches a well-known example/default password. Set a unique, strong password.");
    }
    else if (!adminAccess.AllowAnonymousAccess)
    {
        // Fail closed: disabling the login screen without an explicit, auditable
        // acknowledgement would otherwise leave every page and API endpoint anonymous.
        throw new InvalidOperationException(
            "AdminAccess:Enabled is false but AdminAccess:AllowAnonymousAccess is not set. " +
            "Either enable AdminAccess with a username/password, or explicitly set " +
            "AdminAccess:AllowAnonymousAccess=true (not recommended outside local development).");
    }

    // Only trust X-Forwarded-* when explicitly running behind a known, trusted ingress
    // (e.g. Azure Container Apps, which is the sole network entry point). Otherwise keep
    // ASP.NET Core's default proxy allow-list so a client cannot spoof its own IP/scheme
    // and bypass IP-based rate limiting or poison audit logs.
    var trustForwardedHeaders = string.Equals(Environment.GetEnvironmentVariable("TRADING_MONITOR_TRUSTED_PROXY"), "true", StringComparison.OrdinalIgnoreCase);
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        if (trustForwardedHeaders)
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }
    });
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options =>
                    {
                        options.Cookie.Name = "__Host-TradingMonitor";
                        options.Cookie.HttpOnly = true;
                        options.Cookie.IsEssential = true;
                        options.Cookie.SameSite = SameSiteMode.Strict;
                        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                        options.LoginPath = "/account/login";
                        options.AccessDeniedPath = "/account/login";
                        options.ExpireTimeSpan = TimeSpan.FromHours(Math.Clamp(adminAccess.SessionHours, 1, 24));
                        options.SlidingExpiration = true;
                    });
    builder.Services.AddAuthorization(options =>
    {
        if (!adminAccess.AllowAnonymousAccess)
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        }
    });
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
        options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 20,
                AutoReplenishment = true
            }));
        options.AddPolicy("api-mutation", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    });
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
    builder.Services.AddHealthChecks()
                    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
                    .AddCheck<SqlDatabaseHealthCheck>("database", tags: ["ready"]);
    builder.Services.AddRazorPages();

    var app = builder.Build();
    if (app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value.InitializeOnStartup)
        await DatabaseInitializer.EnsureCreatedAsync(app.Services);

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    app.UseForwardedHeaders();

    if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
        app.UseHttpsRedirection();

    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "same-origin";
        context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; object-src 'none'; " +
            "script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self' data:; connect-src 'self'; form-action 'self'");
        await next();
    });

    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapStaticAssets().AllowAnonymous();
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live")
    }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready")
    }).AllowAnonymous();
    app.MapGet("/api/operaciones-vivas", async (decimal? capital, string? estado, string? symbol, string? tipoSenal, string? mode, string? senal, string? mercado, LiveOperationsSnapshotService snapshotService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await snapshotService.GetAsync(capital, estado, symbol, tipoSenal, mode, senal, mercado, cancellationToken));
    }).RequireAuthorization().RequireRateLimiting("api");
    app.MapGet("/api/grafico-vivo", async (string? symbol, string? interval, decimal? capital, string? estado, string? tipoSenal, string? mode, string? senal, string? mercado, DateTimeOffset? from, DateTimeOffset? to,
        LiveChartSnapshotService chartService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await chartService.GetAsync(symbol, interval, capital, estado, tipoSenal, mode, senal, mercado, from, to, cancellationToken));
    }).RequireAuthorization().RequireRateLimiting("api");
    app.MapPost("/api/posiciones/{id:guid}/cerrar", async (Guid id, ManagedCloseRequest request, IOpportunityRepository opportunityRepository,
        Microsoft.Extensions.Options.IOptionsMonitor<ReportingOptions> reportingOptions, CancellationToken cancellationToken) =>
    {
        if (request.Capital is < 0m or > 100_000_000m || request.TargetNetPercent is < -99m or > 1000m || request.ExitPrice is < 0m or > 100_000_000m)
            return Results.BadRequest(new { message = "Parámetros fuera de rango permitido." });

        var capital = request.Capital <= 0m ? reportingOptions.CurrentValue.DefaultCapital : request.Capital;
        var row = await opportunityRepository.GetByIdAsync(id, capital, cancellationToken);

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
    }).RequireAuthorization().RequireRateLimiting("api-mutation");
    app.MapPost("/api/posiciones/{id:guid}/objetivo", async (Guid id, ManagedTargetRequest request, IOpportunityRepository opportunityRepository,
        Microsoft.Extensions.Options.IOptionsMonitor<ReportingOptions> reportingOptions, CancellationToken cancellationToken) =>
    {
        if (request.Capital is < 0m or > 100_000_000m || request.TargetNetPercent is < -99m or > 1000m)
            return Results.BadRequest(new { message = "Parámetros fuera de rango permitido." });

        var capital = request.Capital <= 0m ? reportingOptions.CurrentValue.DefaultCapital : request.Capital;
        var row = await opportunityRepository.GetByIdAsync(id, capital, cancellationToken);

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
    }).RequireAuthorization().RequireRateLimiting("api-mutation");
    app.MapGet("/api/exchange/status", async (ExchangeConnectionStatusService statusService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await statusService.GetAsync(cancellationToken));
    }).RequireAuthorization().RequireRateLimiting("api");
    app.MapPost("/api/conexiones/reintentar", async (ConnectionRetryRequest request, ConnectionRetryService retryService, CancellationToken cancellationToken) =>
    {
        return Results.Json(await retryService.RetryAsync(request, cancellationToken));
    }).RequireAuthorization().RequireRateLimiting("api-mutation");
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
    }).RequireAuthorization().RequireRateLimiting("api");
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
