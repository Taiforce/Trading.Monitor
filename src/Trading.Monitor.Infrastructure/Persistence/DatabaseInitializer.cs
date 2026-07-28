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
            await EnsureOpportunityManagedSchemaAsync(dbContext, cancellationToken);
            await EnsureTraderResearchSchemaAsync(dbContext, cancellationToken);
            await EnsureTradeExecutionSchemaAsync(dbContext, cancellationToken);
            await EnsureWalletSchemaAsync(dbContext, cancellationToken);

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
                """,
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_wallet_settings_Market' AND object_id = OBJECT_ID(N'dbo.wallet_settings'))
                    CREATE UNIQUE INDEX IX_wallet_settings_Market ON dbo.wallet_settings(Market);

                DECLARE @walletNow datetimeoffset = SYSDATETIMEOFFSET();

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_settings WHERE Market = N'crypto')
                BEGIN
                    INSERT INTO dbo.wallet_settings (Id, Market, CashCapital, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('0fa2b2e6-35ec-4cc9-96b8-b8051eb4c2c5', N'crypto', 0, 0, @walletNow, @walletNow);
                END;

                IF NOT EXISTS (SELECT 1 FROM dbo.wallet_settings WHERE Market = N'forex')
                BEGIN
                    INSERT INTO dbo.wallet_settings (Id, Market, CashCapital, AutoTradingEnabled, CreatedAt, UpdatedAt)
                    VALUES ('f8c2765c-0602-42d8-a76f-6510b2342c21', N'forex', 0, 0, @walletNow, @walletNow);
                END;

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
