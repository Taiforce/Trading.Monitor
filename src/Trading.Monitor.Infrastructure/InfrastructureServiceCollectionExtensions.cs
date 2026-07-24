using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Infrastructure.Persistence;

namespace Trading.Monitor.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTradingMonitorDatabase(this IServiceCollection services, IConfiguration configuration, string? contentRootPath = null)
    {
        var databaseOptions = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();

        if (!string.Equals(databaseOptions.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Trading Monitor now uses SQL Server. Set Database:Provider to SqlServer.");

        services.AddDbContext<TradingMonitorDbContext>(options => options.UseSqlServer(
            databaseOptions.ConnectionString,
            sqlServer => sqlServer.EnableRetryOnFailure(5, TimeSpan.FromSeconds(8), null)));

        services.AddScoped<IOpportunityRepository, EfOpportunityRepository>();
        services.AddScoped<ITraderResearchRepository, EfTraderResearchRepository>();
        services.AddScoped<ISignalStore>(provider => provider.GetRequiredService<IOpportunityRepository>());
        services.AddSingleton<ISourceTelemetryRecorder, SourceTelemetryRecorder>();

        return services;
    }
}
