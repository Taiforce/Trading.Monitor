using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Trading.Monitor.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task EnsureCreatedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TradingMonitorDbContext>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingMonitorDbContext>();

        var connectionString = dbContext.Database.GetConnectionString();
        logger.LogInformation("Ensuring local trading database exists at {ConnectionString}", RedactConnectionString(connectionString));

        try
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number == 1801)
        {
            logger.LogInformation("Database already exists. Continuing schema verification.");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private static string RedactConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return "";

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);

            if (builder.ContainsKey("Password"))
                builder.Password = "***";

            if (builder.ContainsKey("User ID"))
                builder.UserID = string.IsNullOrWhiteSpace(builder.UserID) ? "" : "***";

            return builder.ToString();
        }
        catch (ArgumentException)
        {
            return "<redacted connection string>";
        }
    }
}
