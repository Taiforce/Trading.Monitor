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

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("EXEC sp_getapplock @Resource = N'TradingMonitorTraderResearchSeed', @LockMode = N'Exclusive', @LockOwner = N'Session', @LockTimeout = 15000;", cancellationToken);
            await EnsureTraderResearchSchemaAsync(dbContext, cancellationToken);

            try
            {
                await TraderResearchSeeder.SeedAsync(dbContext, cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
            {
                dbContext.ChangeTracker.Clear();
                logger.LogInformation("Trader research seed already exists. Continuing.");
            }
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync("EXEC sp_releaseapplock @Resource = N'TradingMonitorTraderResearchSeed', @LockOwner = N'Session';", cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static async Task EnsureTraderResearchSchemaAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'dbo.trader_sources', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.trader_sources
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_trader_sources PRIMARY KEY,
                        Platform nvarchar(64) NOT NULL,
                        Name nvarchar(160) NOT NULL,
                        Market nvarchar(128) NOT NULL,
                        Url nvarchar(2048) NOT NULL,
                        DataAccess nvarchar(256) NOT NULL,
                        DataQuality nvarchar(256) NOT NULL,
                        Notes nvarchar(2048) NOT NULL,
                        SupportsCopyTrading bit NOT NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL
                    );
                    CREATE UNIQUE INDEX IX_trader_sources_Platform ON dbo.trader_sources(Platform);
                END;

                IF OBJECT_ID(N'dbo.trader_profiles', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.trader_profiles
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_trader_profiles PRIMARY KEY,
                        Platform nvarchar(64) NOT NULL,
                        DisplayName nvarchar(160) NOT NULL,
                        ExternalId nvarchar(160) NOT NULL,
                        ProfileUrl nvarchar(2048) NOT NULL,
                        Market nvarchar(128) NOT NULL,
                        StrategyType nvarchar(160) NOT NULL,
                        PopularityText nvarchar(512) NOT NULL,
                        PerformanceText nvarchar(512) NOT NULL,
                        DataAvailability nvarchar(512) NOT NULL,
                        Notes nvarchar(2048) NOT NULL,
                        LastSyncedAt datetimeoffset NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL
                    );
                    CREATE UNIQUE INDEX IX_trader_profiles_Platform_ExternalId ON dbo.trader_profiles(Platform, ExternalId);
                END;

                IF OBJECT_ID(N'dbo.trader_trades', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.trader_trades
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_trader_trades PRIMARY KEY,
                        TraderProfileId uniqueidentifier NOT NULL,
                        ExternalTradeId nvarchar(160) NOT NULL,
                        Symbol nvarchar(32) NOT NULL,
                        Side nvarchar(16) NOT NULL,
                        Status nvarchar(24) NOT NULL,
                        OpenedAt datetimeoffset NOT NULL,
                        ClosedAt datetimeoffset NULL,
                        EntryPrice decimal(18,8) NOT NULL,
                        ExitPrice decimal(18,8) NULL,
                        Quantity decimal(18,8) NULL,
                        PnLPercent decimal(18,4) NULL,
                        NetPnL decimal(18,2) NULL,
                        Leverage decimal(18,4) NULL,
                        SourceUrl nvarchar(2048) NOT NULL,
                        Notes nvarchar(2048) NOT NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL,
                        CONSTRAINT FK_trader_trades_trader_profiles_TraderProfileId FOREIGN KEY (TraderProfileId) REFERENCES dbo.trader_profiles(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IX_trader_trades_TraderProfileId_OpenedAt ON dbo.trader_trades(TraderProfileId, OpenedAt);
                    CREATE UNIQUE INDEX IX_trader_trades_TraderProfileId_ExternalTradeId ON dbo.trader_trades(TraderProfileId, ExternalTradeId);
                END;
                """,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2714 or 1913)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
