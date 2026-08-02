using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Trading.Monitor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    LastMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "historical_market_candles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Interval = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    OpenTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CloseTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    QuoteVolume = table.Column<decimal>(type: "decimal(28,8)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_historical_market_candles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ItemsCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ingestion_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "research_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Sentiment = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SymbolsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_research_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trader_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ProfileUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StrategyType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PopularityText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    PerformanceText = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DataAvailability = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trader_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trader_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Market = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DataAccess = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DataQuality = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    SupportsCopyTrading = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trader_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trading_opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OperationKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OriginKind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    ObservedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExitTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    EntryLower = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    EntryUpper = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    StopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakeProfit1 = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    TakeProfit2 = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    ExitReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    RiskReward = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    Capital = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EstimatedQuantity = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    EstimatedFees = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetProfitAtTakeProfit1 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetProfitAtTakeProfit2 = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NetLossAtStop = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ManagedTargetNetPercent = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ManagedTargetNetPnL = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ManagedTargetExitPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    RealizedGrossPnL = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RealizedNetPnL = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RealizedNetPercent = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    RealizedTotalObtained = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ConfirmingIntervalsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RisksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelatedNewsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trading_opportunities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Asset = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CoinQuantity = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    AllowSellHighBuyLow = table.Column<bool>(type: "bit", nullable: false),
                    AutoTradingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "wallet_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Market = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CashCapital = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AutoTradingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ManagedTargetNetPercent = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wallet_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trader_trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TraderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalTradeId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    PnLPercent = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    NetPnL = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Leverage = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trader_trades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trader_trades_trader_profiles_TraderProfileId",
                        column: x => x.TraderProfileId,
                        principalTable: "trader_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Side = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestedCapital = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    ExecutedQuantity = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    ExecutedQuote = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                    ClientOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trade_executions_trading_opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "trading_opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_sources_Name_Kind",
                table: "data_sources",
                columns: new[] { "Name", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_historical_market_candles_Market_Symbol_Interval",
                table: "historical_market_candles",
                columns: new[] { "Market", "Symbol", "Interval" });

            migrationBuilder.CreateIndex(
                name: "IX_historical_market_candles_Symbol_Interval_OpenTime",
                table: "historical_market_candles",
                columns: new[] { "Symbol", "Interval", "OpenTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ingestion_events_SourceName_CompletedAt",
                table: "ingestion_events",
                columns: new[] { "SourceName", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_research_items_PublishedAt",
                table: "research_items",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_research_items_Url",
                table: "research_items",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trade_executions_CreatedAt",
                table: "trade_executions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_trade_executions_OpportunityId_CreatedAt",
                table: "trade_executions",
                columns: new[] { "OpportunityId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trade_executions_Status",
                table: "trade_executions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_trader_profiles_Platform_ExternalId",
                table: "trader_profiles",
                columns: new[] { "Platform", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trader_sources_Platform",
                table: "trader_sources",
                column: "Platform",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trader_trades_TraderProfileId_ExternalTradeId",
                table: "trader_trades",
                columns: new[] { "TraderProfileId", "ExternalTradeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trader_trades_TraderProfileId_OpenedAt",
                table: "trader_trades",
                columns: new[] { "TraderProfileId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_trading_opportunities_AlertKey",
                table: "trading_opportunities",
                column: "AlertKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trading_opportunities_OperationKind",
                table: "trading_opportunities",
                column: "OperationKind");

            migrationBuilder.CreateIndex(
                name: "IX_trading_opportunities_OriginKind",
                table: "trading_opportunities",
                column: "OriginKind");

            migrationBuilder.CreateIndex(
                name: "IX_trading_opportunities_Status",
                table: "trading_opportunities",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_trading_opportunities_Symbol_Side_ObservedAt",
                table: "trading_opportunities",
                columns: new[] { "Symbol", "Side", "ObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_wallet_assets_Symbol",
                table: "wallet_assets",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_settings_Market",
                table: "wallet_settings",
                column: "Market",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_sources");

            migrationBuilder.DropTable(
                name: "historical_market_candles");

            migrationBuilder.DropTable(
                name: "ingestion_events");

            migrationBuilder.DropTable(
                name: "research_items");

            migrationBuilder.DropTable(
                name: "trade_executions");

            migrationBuilder.DropTable(
                name: "trader_sources");

            migrationBuilder.DropTable(
                name: "trader_trades");

            migrationBuilder.DropTable(
                name: "wallet_assets");

            migrationBuilder.DropTable(
                name: "wallet_settings");

            migrationBuilder.DropTable(
                name: "trading_opportunities");

            migrationBuilder.DropTable(
                name: "trader_profiles");
        }
    }
}
