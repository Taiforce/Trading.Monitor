using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Configuration;

namespace Trading.Monitor.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private const string BaselineMigration = "20260802203208_InitialCreate";

    public static async Task EnsureCreatedAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TradingMonitorDbContext>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingMonitorDbContext>();
        var databaseOptions = scope.ServiceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        var connectionString = dbContext.Database.GetConnectionString();
        logger.LogInformation("Applying trading database migrations at {ConnectionString}", RedactConnectionString(connectionString));

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A SQL Server connection string is required to initialize Trading Monitor.");

        var lockConnectionString = databaseOptions.CreateIfMissing
            ? new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString
            : connectionString;

        await using var initializationConnection = new SqlConnection(lockConnectionString);
        await initializationConnection.OpenAsync(cancellationToken);
        await AcquireInitializationLockAsync(initializationConnection, cancellationToken);

        try
        {
            if (await HasLegacySchemaWithoutMigrationHistoryAsync(connectionString, cancellationToken))
            {
                logger.LogInformation("Baselining the legacy database before enabling EF Core migrations.");

                await dbContext.Database.OpenConnectionAsync(cancellationToken);
                try
                {
                    await EnsureOpportunityManagedSchemaAsync(dbContext, cancellationToken);
                    await EnsureTraderResearchSchemaAsync(dbContext, cancellationToken);
                    await EnsureTradeExecutionSchemaAsync(dbContext, cancellationToken);
                    await EnsureWalletSchemaAsync(dbContext, cancellationToken);
                    await EnsureHistoricalMarketCandleSchemaAsync(dbContext, cancellationToken);
                    await RecordBaselineMigrationAsync(dbContext, cancellationToken);
                }
                finally
                {
                    await dbContext.Database.CloseConnectionAsync();
                }
            }
            else
            {
                if (!databaseOptions.CreateIfMissing && !await dbContext.Database.CanConnectAsync(cancellationToken))
                    throw new InvalidOperationException("The production database does not exist or is not reachable. Provision it before starting Trading Monitor.");

                await dbContext.Database.MigrateAsync(cancellationToken);
            }

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
            await ReleaseInitializationLockAsync(initializationConnection);
        }
    }

    private static async Task<bool> HasLegacySchemaWithoutMigrationHistoryAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 4060 or 911)
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CASE WHEN OBJECT_ID(N'dbo.trading_opportunities', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THEN 1 ELSE 0 END;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static async Task RecordBaselineMigrationAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            $"""
            IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.__EFMigrationsHistory
                (
                    MigrationId nvarchar(150) NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                    ProductVersion nvarchar(32) NOT NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'{BaselineMigration}')
                INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES (N'{BaselineMigration}', N'10.0.10');
            """,
            cancellationToken);
    }

    private static async Task AcquireInitializationLockAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'TradingMonitorDatabaseInitialization',
                @LockMode = N'Exclusive',
                @LockOwner = N'Session',
                @LockTimeout = 60000;
            SELECT @result;
            """;

        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
            throw new TimeoutException($"Could not acquire the Trading Monitor database initialization lock. SQL result: {result}.");
    }

    private static async Task ReleaseInitializationLockAsync(SqlConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText =
            "EXEC sys.sp_releaseapplock @Resource = N'TradingMonitorDatabaseInitialization', @LockOwner = N'Session';";
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task EnsureHistoricalMarketCandleSchemaAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'dbo.historical_market_candles', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.historical_market_candles
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_historical_market_candles PRIMARY KEY,
                        Market nvarchar(16) NOT NULL,
                        Source nvarchar(128) NOT NULL,
                        Symbol nvarchar(32) NOT NULL,
                        Interval nvarchar(8) NOT NULL,
                        OpenTime datetimeoffset NOT NULL,
                        CloseTime datetimeoffset NOT NULL,
                        [Open] decimal(18,8) NOT NULL,
                        High decimal(18,8) NOT NULL,
                        Low decimal(18,8) NOT NULL,
                        [Close] decimal(18,8) NOT NULL,
                        Volume decimal(28,8) NOT NULL,
                        QuoteVolume decimal(28,8) NOT NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL
                    );

                    CREATE UNIQUE INDEX IX_historical_market_candles_Symbol_Interval_OpenTime
                        ON dbo.historical_market_candles(Symbol, Interval, OpenTime);

                    CREATE INDEX IX_historical_market_candles_Market_Symbol_Interval
                        ON dbo.historical_market_candles(Market, Symbol, Interval);
                END;
                """,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2714 or 1913)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task EnsureTradeExecutionSchemaAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'dbo.trade_executions', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.trade_executions
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_trade_executions PRIMARY KEY,
                        OpportunityId uniqueidentifier NOT NULL,
                        Symbol nvarchar(32) NOT NULL,
                        Side nvarchar(16) NOT NULL,
                        Action nvarchar(24) NOT NULL,
                        Mode nvarchar(16) NOT NULL,
                        Status nvarchar(16) NOT NULL,
                        RequestedCapital decimal(18,2) NOT NULL,
                        RequestedQuantity decimal(18,8) NULL,
                        ExecutedQuantity decimal(18,8) NULL,
                        ExecutedQuote decimal(18,2) NULL,
                        Price decimal(18,8) NULL,
                        ClientOrderId nvarchar(64) NOT NULL,
                        ExchangeOrderId nvarchar(128) NOT NULL,
                        Reason nvarchar(512) NOT NULL,
                        Message nvarchar(2048) NOT NULL,
                        RequestJson nvarchar(max) NOT NULL,
                        ResponseJson nvarchar(max) NOT NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        CONSTRAINT FK_trade_executions_trading_opportunities_OpportunityId FOREIGN KEY (OpportunityId) REFERENCES dbo.trading_opportunities(Id) ON DELETE CASCADE
                    );

                    CREATE INDEX IX_trade_executions_OpportunityId_CreatedAt ON dbo.trade_executions(OpportunityId, CreatedAt);
                    CREATE INDEX IX_trade_executions_CreatedAt ON dbo.trade_executions(CreatedAt);
                    CREATE INDEX IX_trade_executions_Status ON dbo.trade_executions(Status);
                END;
                """,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2714 or 1913)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task EnsureWalletSchemaAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF OBJECT_ID(N'dbo.wallet_settings', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.wallet_settings
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_wallet_settings PRIMARY KEY,
                        Market nvarchar(16) NOT NULL,
                        CashCapital decimal(18,2) NOT NULL,
                        AutoTradingEnabled bit NOT NULL,
                        ManagedTargetNetPercent decimal(18,4) NOT NULL
                            CONSTRAINT DF_wallet_settings_ManagedTargetNetPercent DEFAULT(5.0000),
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL
                    );

                END;

                IF OBJECT_ID(N'dbo.wallet_assets', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.wallet_assets
                    (
                        Id uniqueidentifier NOT NULL CONSTRAINT PK_wallet_assets PRIMARY KEY,
                        Market nvarchar(16) NOT NULL,
                        Symbol nvarchar(32) NOT NULL,
                        Asset nvarchar(16) NOT NULL,
                        CoinQuantity decimal(18,8) NOT NULL,
                        AllowSellHighBuyLow bit NOT NULL,
                        AutoTradingEnabled bit NOT NULL,
                        CreatedAt datetimeoffset NOT NULL,
                        UpdatedAt datetimeoffset NOT NULL
                    );

                    CREATE UNIQUE INDEX IX_wallet_assets_Symbol ON dbo.wallet_assets(Symbol);
                END;

                IF COL_LENGTH(N'dbo.wallet_settings', N'Market') IS NULL
                    ALTER TABLE dbo.wallet_settings ADD Market nvarchar(16) NOT NULL CONSTRAINT DF_wallet_settings_Market DEFAULT N'crypto';

                IF COL_LENGTH(N'dbo.wallet_assets', N'Market') IS NULL
                    ALTER TABLE dbo.wallet_assets ADD Market nvarchar(16) NOT NULL CONSTRAINT DF_wallet_assets_Market DEFAULT N'crypto';

                IF COL_LENGTH(N'dbo.wallet_settings', N'ManagedTargetNetPercent') IS NULL
                    ALTER TABLE dbo.wallet_settings ADD ManagedTargetNetPercent decimal(18,4) NOT NULL CONSTRAINT DF_wallet_settings_ManagedTargetNetPercent DEFAULT(5.0000);
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_wallet_settings_Market' AND object_id = OBJECT_ID(N'dbo.wallet_settings'))
                    CREATE UNIQUE INDEX IX_wallet_settings_Market ON dbo.wallet_settings(Market);

                DECLARE @walletNow datetimeoffset = SYSDATETIMEOFFSET();

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_settings WHERE Market = N'crypto')
                BEGIN
                    INSERT INTO dbo.wallet_settings (Id, Market, CashCapital, AutoTradingEnabled, ManagedTargetNetPercent, CreatedAt, UpdatedAt)
                    VALUES ('0fa2b2e6-35ec-4cc9-96b8-b8051eb4c2c5', N'crypto', 0, 0, 5.0000, @walletNow, @walletNow);
                END;

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_settings WHERE Market = N'forex')
                BEGIN
                    INSERT INTO dbo.wallet_settings (Id, Market, CashCapital, AutoTradingEnabled, ManagedTargetNetPercent, CreatedAt, UpdatedAt)
                    VALUES ('f8c2765c-0602-42d8-a76f-6510b2342c21', N'forex', 0, 0, 5.0000, @walletNow, @walletNow);
                END;

                UPDATE dbo.wallet_settings
                SET ManagedTargetNetPercent = 5.0000
                WHERE ManagedTargetNetPercent <= 0;

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'BTCUSDT')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('c14a07c8-2e29-4dd9-8bd7-5591f0fc27b8', N'crypto', N'BTCUSDT', N'BTC', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'ETHUSDT')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('4b34c054-5ebf-48ca-893d-b9b8e670ca45', N'crypto', N'ETHUSDT', N'ETH', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'SOLUSDT')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('3b2b3908-54e1-4117-b41d-44f54edc9e95', N'crypto', N'SOLUSDT', N'SOL', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'XRPUSDT')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('482d03f0-9525-45d0-a3db-46698f8a6c48', N'crypto', N'XRPUSDT', N'XRP', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'ADAUSDT')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('f1417324-3555-40a0-a2f9-b4baad790f50', N'crypto', N'ADAUSDT', N'ADA', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'EURUSD')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'EURUSD', N'EUR', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'GBPUSD')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'GBPUSD', N'GBP', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'USDJPY')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'USDJPY', N'USD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'USDCHF')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'USDCHF', N'USD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'AUDUSD')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'AUDUSD', N'AUD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'USDCAD')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'USDCAD', N'USD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'NZDUSD')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'NZDUSD', N'NZD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'USDMXN')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'USDMXN', N'USD', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'EURMXN')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'EURMXN', N'EUR', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'GBPJPY')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'GBPJPY', N'GBP', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'EURJPY')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'EURJPY', N'EUR', 0, 0, 0, @walletNow, @walletNow);

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_assets WHERE Symbol = N'EURGBP')
                    INSERT INTO dbo.wallet_assets (Id, Market, Symbol, Asset, CoinQuantity, AllowSellHighBuyLow, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), N'forex', N'EURGBP', N'EUR', 0, 0, 0, @walletNow, @walletNow);

                UPDATE dbo.wallet_assets
                SET AllowSellHighBuyLow = 0
                WHERE CoinQuantity <= 0;
                """,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2714 or 1913)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task EnsureOpportunityManagedSchemaAsync(TradingMonitorDbContext dbContext, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH(N'dbo.trading_opportunities', N'OperationKind') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD OperationKind nvarchar(24) NOT NULL
                        CONSTRAINT DF_trading_opportunities_OperationKind DEFAULT N'Fixed';
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'OriginKind') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD OriginKind nvarchar(24) NOT NULL
                        CONSTRAINT DF_trading_opportunities_OriginKind DEFAULT N'OwnAi';
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'ManagedTargetNetPercent') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD ManagedTargetNetPercent decimal(18,4) NOT NULL
                        CONSTRAINT DF_trading_opportunities_ManagedTargetNetPercent DEFAULT(5.0000);
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'ManagedTargetNetPnL') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD ManagedTargetNetPnL decimal(18,2) NOT NULL
                        CONSTRAINT DF_trading_opportunities_ManagedTargetNetPnL DEFAULT(0);
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'ManagedTargetExitPrice') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD ManagedTargetExitPrice decimal(18,8) NULL;
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'RealizedNetPercent') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD RealizedNetPercent decimal(18,4) NULL;
                END;

                IF COL_LENGTH(N'dbo.trading_opportunities', N'RealizedTotalObtained') IS NULL
                BEGIN
                    ALTER TABLE dbo.trading_opportunities
                    ADD RealizedTotalObtained decimal(18,2) NULL;
                END;

                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                UPDATE dbo.trading_opportunities
                SET ManagedTargetNetPnL = ROUND(Capital * ManagedTargetNetPercent / 100.0, 2)
                WHERE ManagedTargetNetPnL = 0 AND Capital > 0;

                UPDATE dbo.trading_opportunities
                SET RealizedNetPercent = ROUND(RealizedNetPnL / NULLIF(Capital, 0) * 100.0, 4)
                WHERE RealizedNetPnL IS NOT NULL AND RealizedNetPercent IS NULL;

                UPDATE dbo.trading_opportunities
                SET RealizedTotalObtained = ROUND(Capital + RealizedNetPnL, 2)
                WHERE RealizedNetPnL IS NOT NULL AND RealizedTotalObtained IS NULL;

                UPDATE dbo.trading_opportunities
                SET OperationKind = N'Managed'
                WHERE OperationKind = N'Fixed'
                  AND (
                        Status = N'ManagedProfitExit'
                        OR ExitReason LIKE N'%administrada%'
                        OR (Status = N'Open' AND Score >= 95 AND ManagedTargetExitPrice IS NOT NULL AND DATEDIFF(MINUTE, ObservedAt, ExpiresAt) <= 240)
                      );

                UPDATE dbo.trading_opportunities
                SET OriginKind = N'Trader'
                WHERE OriginKind <> N'Trader'
                  AND (ReasonsJson LIKE N'%trader%' OR ReasonsJson LIKE N'%copy trading%' OR RisksJson LIKE N'%trader%');

                UPDATE dbo.trading_opportunities
                SET OriginKind = N'ExternalAi'
                WHERE OriginKind = N'OwnAi'
                  AND (
                        ReasonsJson LIKE N'%IA ajena%'
                        OR ReasonsJson LIKE N'%Zella%'
                        OR ReasonsJson LIKE N'%Holly%'
                        OR ReasonsJson LIKE N'%TrendSpider%'
                        OR ReasonsJson LIKE N'%Tickeron%'
                        OR ReasonsJson LIKE N'%Numerai%'
                        OR ReasonsJson LIKE N'%Sentifi%'
                        OR ReasonsJson LIKE N'%Q AI%'
                        OR ReasonsJson LIKE N'%fuente externa%'
                      );

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_trading_opportunities_OperationKind' AND object_id = OBJECT_ID(N'dbo.trading_opportunities'))
                    CREATE INDEX IX_trading_opportunities_OperationKind ON dbo.trading_opportunities(OperationKind);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_trading_opportunities_OriginKind' AND object_id = OBJECT_ID(N'dbo.trading_opportunities'))
                    CREATE INDEX IX_trading_opportunities_OriginKind ON dbo.trading_opportunities(OriginKind);
                """,
                cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2705 or 2714)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
