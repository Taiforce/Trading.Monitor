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
    builder.Services.AddSingleton<OpportunityProjectionService>();
    builder.Services.AddSingleton<OperationalLogReader>();
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
