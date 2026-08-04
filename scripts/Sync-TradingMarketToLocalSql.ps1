param(
    [string]$SourceServer = "tcp:127.0.0.1,14333",
    [string]$SourceDatabase = "TradingMarket",
    [string]$SourceUser = "sa",
    [string]$SourcePassword = $env:SQLSERVER_SA_PASSWORD,
    [string]$TargetServer = "localhost",
    [string]$TargetDatabase = "TradingMarket",
    [string]$WorkDirectory = (Join-Path $env:TEMP "TradingMarketSqlSync")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SourcePassword)) {
    throw "No password was provided. Pass -SourcePassword explicitly or set the SQLSERVER_SA_PASSWORD environment variable (the same value you put in .env). This script never ships with a default credential."
}

function Invoke-TargetSql {
    param([string]$Query, [string]$Database = "master")

    sqlcmd -S $TargetServer -d $Database -E -C -b -Q $Query
}

function Invoke-SourceSql {
    param([string]$Query)

    sqlcmd -S $SourceServer -d $SourceDatabase -U $SourceUser -P $SourcePassword -C -b -Q $Query
}

function Require-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is required. Install SQL Server command line tools and try again."
    }
}

Require-Command "sqlcmd"
Require-Command "bcp"

New-Item -ItemType Directory -Force -Path $WorkDirectory | Out-Null

$schemaSql = @"
IF DB_ID(N'$TargetDatabase') IS NULL
BEGIN
    CREATE DATABASE [$TargetDatabase];
END;
"@
Invoke-TargetSql -Query $schemaSql

$tablesSql = @"
IF OBJECT_ID(N'dbo.trading_opportunities', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.trading_opportunities
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_trading_opportunities PRIMARY KEY,
        AlertKey nvarchar(128) NOT NULL,
        Symbol nvarchar(32) NOT NULL,
        Side nvarchar(16) NOT NULL,
        Status nvarchar(24) NOT NULL,
        Score int NOT NULL,
        ObservedAt datetimeoffset NOT NULL,
        ExpiresAt datetimeoffset NOT NULL,
        ExitTime datetimeoffset NULL,
        LastPrice decimal(18,8) NOT NULL,
        EntryLower decimal(18,8) NOT NULL,
        EntryUpper decimal(18,8) NOT NULL,
        EntryPrice decimal(18,8) NOT NULL,
        StopLoss decimal(18,8) NOT NULL,
        TakeProfit1 decimal(18,8) NOT NULL,
        TakeProfit2 decimal(18,8) NOT NULL,
        ExitPrice decimal(18,8) NULL,
        ExitReason nvarchar(512) NOT NULL,
        RiskReward decimal(18,4) NOT NULL,
        Capital decimal(18,2) NOT NULL,
        EstimatedQuantity decimal(18,8) NOT NULL,
        EstimatedFees decimal(18,2) NOT NULL,
        NetProfitAtTakeProfit1 decimal(18,2) NOT NULL,
        NetProfitAtTakeProfit2 decimal(18,2) NOT NULL,
        NetLossAtStop decimal(18,2) NOT NULL,
        RealizedGrossPnL decimal(18,2) NULL,
        RealizedNetPnL decimal(18,2) NULL,
        ConfirmingIntervalsJson nvarchar(max) NOT NULL,
        ReasonsJson nvarchar(max) NOT NULL,
        RisksJson nvarchar(max) NOT NULL,
        RelatedNewsJson nvarchar(max) NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
    CREATE UNIQUE INDEX IX_trading_opportunities_AlertKey ON dbo.trading_opportunities(AlertKey);
    CREATE INDEX IX_trading_opportunities_Status ON dbo.trading_opportunities(Status);
    CREATE INDEX IX_trading_opportunities_Symbol_Side_ObservedAt ON dbo.trading_opportunities(Symbol, Side, ObservedAt);
END;

IF OBJECT_ID(N'dbo.data_sources', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.data_sources
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_data_sources PRIMARY KEY,
        Name nvarchar(128) NOT NULL,
        Kind nvarchar(32) NOT NULL,
        Status nvarchar(24) NOT NULL,
        Url nvarchar(1024) NULL,
        LastSuccessAt datetimeoffset NULL,
        LastFailureAt datetimeoffset NULL,
        FailureCount int NOT NULL,
        LastMessage nvarchar(2048) NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
    CREATE UNIQUE INDEX IX_data_sources_Name_Kind ON dbo.data_sources(Name, Kind);
END;

IF OBJECT_ID(N'dbo.ingestion_events', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ingestion_events
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ingestion_events PRIMARY KEY,
        SourceName nvarchar(128) NOT NULL,
        Kind nvarchar(32) NOT NULL,
        Status nvarchar(24) NOT NULL,
        Url nvarchar(1024) NULL,
        Message nvarchar(2048) NOT NULL,
        StartedAt datetimeoffset NOT NULL,
        CompletedAt datetimeoffset NOT NULL,
        ItemsCount int NOT NULL
    );
    CREATE INDEX IX_ingestion_events_SourceName_CompletedAt ON dbo.ingestion_events(SourceName, CompletedAt);
END;

IF OBJECT_ID(N'dbo.research_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.research_items
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_research_items PRIMARY KEY,
        Source nvarchar(128) NOT NULL,
        Kind nvarchar(32) NOT NULL,
        Title nvarchar(2048) NOT NULL,
        Url nvarchar(2048) NOT NULL,
        PublishedAt datetimeoffset NOT NULL,
        Sentiment nvarchar(16) NOT NULL,
        SymbolsJson nvarchar(max) NOT NULL,
        RawJson nvarchar(max) NOT NULL,
        CreatedAt datetimeoffset NOT NULL
    );
    CREATE UNIQUE INDEX IX_research_items_Url ON dbo.research_items(Url);
    CREATE INDEX IX_research_items_PublishedAt ON dbo.research_items(PublishedAt);
END;

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
"@
Invoke-TargetSql -Database $TargetDatabase -Query $tablesSql

$tables = @(
    "data_sources",
    "ingestion_events",
    "research_items",
    "trading_opportunities",
    "trade_executions",
    "trader_sources",
    "trader_profiles",
    "trader_trades"
)

$clearSql = @"
DELETE FROM dbo.trader_trades;
DELETE FROM dbo.trader_profiles;
DELETE FROM dbo.trader_sources;
DELETE FROM dbo.trade_executions;
DELETE FROM dbo.trading_opportunities;
DELETE FROM dbo.research_items;
DELETE FROM dbo.ingestion_events;
DELETE FROM dbo.data_sources;
"@
Invoke-TargetSql -Database $TargetDatabase -Query $clearSql

foreach ($table in $tables) {
    $file = Join-Path $WorkDirectory "$table.bcp"
    if (Test-Path $file) {
        Remove-Item -LiteralPath $file -Force
    }

    bcp "$SourceDatabase.dbo.$table" out $file -S $SourceServer -U $SourceUser -P $SourcePassword -n -q
    bcp "$TargetDatabase.dbo.$table" in $file -S $TargetServer -T -n -q -b 10000
}

$countsSql = @"
SET NOCOUNT ON;
SELECT 'trading_opportunities' AS table_name, COUNT(*) AS rows_count FROM dbo.trading_opportunities
UNION ALL SELECT 'data_sources', COUNT(*) FROM dbo.data_sources
UNION ALL SELECT 'ingestion_events', COUNT(*) FROM dbo.ingestion_events
UNION ALL SELECT 'research_items', COUNT(*) FROM dbo.research_items
UNION ALL SELECT 'trade_executions', COUNT(*) FROM dbo.trade_executions
UNION ALL SELECT 'trader_sources', COUNT(*) FROM dbo.trader_sources
UNION ALL SELECT 'trader_profiles', COUNT(*) FROM dbo.trader_profiles
UNION ALL SELECT 'trader_trades', COUNT(*) FROM dbo.trader_trades;
"@
Invoke-TargetSql -Database $TargetDatabase -Query $countsSql
