using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class WalletModel(
    IWalletRepository walletRepository,
    ITradeExecutionRepository tradeExecutionRepository,
    IOpportunityRepository opportunityRepository,
    IOptionsMonitor<ExchangeExecutionOptions> exchangeOptions,
    IOptionsMonitor<ReportingOptions> reportingOptions)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    private readonly ITradeExecutionRepository _tradeExecutionRepository = tradeExecutionRepository;
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions = reportingOptions;

    private static readonly string[] CryptoSymbols = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];
    private static readonly string[] ForexSymbols = ["EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "USDMXN", "EURMXN", "GBPJPY", "EURJPY", "EURGBP"];

    [BindProperty]
    public WalletInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public WalletSnapshot Snapshot { get; private set; } = new(0m, false, []);

    public IReadOnlyList<WalletOperationRow> WalletOperations { get; private set; } = [];

    public IReadOnlyList<WalletAssetUsageRow> AssetUsage { get; private set; } = [];

    public WalletUsageSummary UsageSummary { get; private set; } = new(0m, 0m, 0m, 0m, 0, 0);

    public ExchangeExecutionOptions ExchangeOptions => exchangeOptions.CurrentValue;

    public bool HasApiKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ExchangeOptions.ApiKeyEnvironmentVariable));

    public bool HasApiSecret => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ExchangeOptions.ApiSecretEnvironmentVariable));

    public bool LiveOrdersReady => ExchangeOptions.Enabled
                                   && ExchangeOptions.AllowLiveOrders
                                   && !ExchangeOptions.UseTestOrderEndpoint
                                   && string.Equals(ExchangeOptions.Mode, "Live", StringComparison.OrdinalIgnoreCase)
                                   && HasApiKey
                                   && HasApiSecret;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Mercado = MarketSymbolClassifier.NormalizeMarket(Mercado);
        var assets = Input.Assets
            .Select(asset => new WalletAssetUpdate(
                WalletSnapshot.NormalizeSymbol(asset.Symbol),
                string.IsNullOrWhiteSpace(asset.Asset) ? ResolveAssetLabel(asset.Symbol) : asset.Asset.Trim().ToUpperInvariant(),
                asset.CoinQuantity,
                asset.CoinQuantity > 0m && asset.AllowSellHighBuyLow,
                asset.CoinQuantity > 0m && asset.AutoTradingEnabled))
            .ToArray();

        await walletRepository.SaveAsync(Mercado, Input.CashCapital, Input.AutoTradingEnabled, Input.ManagedTargetNetPercent, assets, cancellationToken);
        StatusMessage = $"Wallet {MarketLabel()} actualizada. Las señales se filtrarán con estos saldos.";

        return RedirectToPage(new { Mercado });
    }

    public string BaseLabel()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "base USD/divisa" : "base USDT/USD";
    }

    public string AssetListLabel()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Divisas y pares" : "Monedas crypto";
    }

    public string AssetHelper()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket
            ? "En Forex registra la divisa base que realmente tienes para permitir vender alto - comprar bajo."
            : "En Crypto registra cuántas monedas tienes por activo.";
    }

    public string BinanceSafetyText()
    {
        if (MarketRouteValue() == MarketSymbolClassifier.ForexMarket)
            return "Forex protegido: broker real requiere integración dedicada y pruebas antes de operar dinero real.";

        if (LiveOrdersReady)
            return "Live activo: la app puede enviar órdenes reales solo para activos autorizados.";

        if (!ExchangeOptions.Enabled)
            return "Automático general apagado.";

        if (!string.Equals(ExchangeOptions.Mode, "Live", StringComparison.OrdinalIgnoreCase))
            return $"Modo {ExchangeOptions.Mode}: no mueve dinero real.";

        if (!ExchangeOptions.AllowLiveOrders || ExchangeOptions.UseTestOrderEndpoint)
            return "Protegido: live requiere AllowLiveOrders=true y endpoint real.";

        if (!HasApiKey || !HasApiSecret)
            return "Faltan llaves de Binance en variables de entorno.";

        return "Protegido.";
    }

    public string ExecutionPanelTitle()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Broker forex automático" : "Binance automático";
    }

    public string ConnectionLinkText()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Ver conexiones Forex" : "Ver conexión Binance";
    }

    public string QuantityLabel()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Total divisa base" : "Total monedas";
    }

    public string SellHighStatus(WalletAssetInput asset)
    {
        if (asset.CoinQuantity <= 0m)
            return MarketRouteValue() == MarketSymbolClassifier.ForexMarket
                ? "Oculta: no tienes divisa base para vender."
                : "Oculta: no tienes monedas para vender.";

        return asset.AllowSellHighBuyLow ? "Permitida con tu saldo actual." : "Bloqueada por ti.";
    }

    public string OperationClass(WalletOperationRow row)
    {
        if (!row.CanOperate)
            return "result-red";

        if (row.NetBenefit > 0.01m)
            return "result-green";

        if (row.NetBenefit < -0.01m)
            return "result-red";

        return "result-yellow";
    }

    public string QuantityText(decimal value, string asset)
    {
        return $"{value:N8} {asset}";
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Mercado = MarketSymbolClassifier.NormalizeMarket(Mercado);
        Snapshot = await walletRepository.GetSnapshotAsync(Mercado, cancellationToken);
        Input = BuildInput(Snapshot);

        if (Capital <= 0m)
            Capital = Snapshot.CashCapital > 0m ? Snapshot.CashCapital : _reportingOptions.CurrentValue.DefaultCapital;

        await LoadReportAsync(cancellationToken);

        var executions = await _tradeExecutionRepository.GetRecentAsync(250, cancellationToken);
        WalletOperations = BuildWalletOperations(executions).ToArray();
        UsageSummary = BuildUsageSummary(WalletOperations);
        AssetUsage = BuildAssetUsage(WalletOperations).ToArray();
    }

    private IEnumerable<WalletOperationRow> BuildWalletOperations(IReadOnlyList<TradeExecutionAudit> executions)
    {
        var executionMap = executions
            .GroupBy(execution => execution.OpportunityId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(execution => execution.CreatedAt).First());

        foreach (var row in Report.RecentSignals.Where(MatchesCurrentMarket).OrderByDescending(row => row.ObservedAt).Take(80))
        {
            var asset = Snapshot.FindAsset(row.Symbol);
            var assetLabel = MarketSymbolClassifier.BaseAsset(row.Symbol);
            var canOperate = row.Side == MarketSide.Long
                ? Snapshot.CashCapital > 0m
                : asset is { CoinQuantity: > 0m, AllowSellHighBuyLow: true };
            var quantity = row.EstimatedQuantity;
            var investment = row.Capital;

            if (row.Side == MarketSide.Short && asset is { CoinQuantity: > 0m })
            {
                quantity = Math.Min(row.EstimatedQuantity, asset.CoinQuantity);
                investment = Math.Round(quantity * row.EntryPrice, 2);
            }

            var markOrExit = row.ExitPrice ?? row.LastPrice;
            var breakdown = TradeCostCalculator.Build(row.Side, investment, quantity, row.EntryPrice, markOrExit, EstimatedFeePercentPerSide);
            var execution = executionMap.GetValueOrDefault(row.Id);
            var autoStatus = ResolveAutoStatus(row, asset, canOperate, execution);
            var actionLabel = row.Side == MarketSide.Long ? "Comprar bajo - vender alto" : "Vender alto - comprar bajo";

            yield return new WalletOperationRow(
                row.Id,
                row.Symbol,
                assetLabel,
                row.Side,
                row.Status,
                row.ObservedAt,
                row.ExitTime,
                actionLabel,
                canOperate,
                autoStatus,
                execution is null ? "No enviada al exchange/broker" : $"{execution.Status} | {execution.Mode} | {execution.Message}",
                investment,
                row.EntryPrice,
                row.ExitPrice,
                row.LastPrice,
                quantity,
                breakdown.EntryFee,
                breakdown.ExitFee,
                breakdown.NetBenefit,
                breakdown.TotalObtained,
                row.Status == OpportunityStatus.Open);
        }
    }

    private string ResolveAutoStatus(OpportunityReportRow row, WalletAssetPosition? asset, bool canOperate, TradeExecutionAudit? execution)
    {
        if (!canOperate)
            return row.Side == MarketSide.Long ? "Bloqueada: no hay capital para comprar." : "Bloqueada: no tienes saldo para vender primero.";

        if (execution is not null)
            return "Registrada por el motor automático/paper.";

        if (!Snapshot.AutoTradingEnabled)
            return "Habría operado, pero el auto wallet está apagado.";

        if (asset is not { AutoTradingEnabled: true })
            return "Habría operado, pero este activo no tiene automático.";

        return LiveOrdersReady ? "Auto real listo para este activo." : "Auto protegido: se simula en paper/test.";
    }

    private WalletUsageSummary BuildUsageSummary(IReadOnlyList<WalletOperationRow> rows)
    {
        var usable = rows.Where(row => row.CanOperate).ToArray();
        var open = usable.Where(row => row.IsOpen).ToArray();
        var closed = usable.Where(row => !row.IsOpen).ToArray();
        var closedNet = closed.Sum(row => row.NetBenefit);
        var inUse = open.Sum(row => row.Investment);
        var simulatedBalance = Snapshot.CashCapital + closedNet;
        var free = Math.Max(0m, simulatedBalance - inUse);

        return new WalletUsageSummary(
            Math.Round(free, 2),
            Math.Round(inUse, 2),
            Math.Round(closedNet, 2),
            Math.Round(simulatedBalance, 2),
            open.Length,
            closed.Length);
    }

    private IEnumerable<WalletAssetUsageRow> BuildAssetUsage(IReadOnlyList<WalletOperationRow> rows)
    {
        foreach (var asset in Input.Assets)
        {
            var open = rows.Where(row => row.IsOpen && string.Equals(row.Symbol, asset.Symbol, StringComparison.OrdinalIgnoreCase) && row.CanOperate).ToArray();
            var quantityInUse = open.Sum(row => row.Quantity);
            var capitalInUse = open.Sum(row => row.Investment);

            yield return new WalletAssetUsageRow(
                asset.Symbol,
                asset.Asset,
                asset.CoinQuantity,
                Math.Round(quantityInUse, 8),
                Math.Round(capitalInUse, 2),
                Math.Max(0m, asset.CoinQuantity - open.Where(row => row.Side == MarketSide.Short).Sum(row => row.Quantity)));
        }
    }

    private WalletInput BuildInput(WalletSnapshot snapshot)
    {
        var assets = SymbolsForCurrentMarket()
            .Select(symbol =>
            {
                var stored = snapshot.FindAsset(symbol);
                return new WalletAssetInput
                {
                    Symbol = symbol,
                    Asset = ResolveAssetLabel(symbol),
                    CoinQuantity = stored?.CoinQuantity ?? 0m,
                    AllowSellHighBuyLow = stored is { CoinQuantity: > 0m, AllowSellHighBuyLow: true },
                    AutoTradingEnabled = stored is { CoinQuantity: > 0m, AutoTradingEnabled: true }
                };
            })
            .ToList();

        return new WalletInput
        {
            CashCapital = snapshot.CashCapital,
            AutoTradingEnabled = snapshot.AutoTradingEnabled,
            ManagedTargetNetPercent = snapshot.ManagedTargetNetPercent <= 0m ? 5m : snapshot.ManagedTargetNetPercent,
            Assets = assets
        };
    }

    private IReadOnlyList<string> SymbolsForCurrentMarket()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? ForexSymbols : CryptoSymbols;
    }

    private string ResolveAssetLabel(string symbol)
    {
        var normalized = WalletSnapshot.NormalizeSymbol(symbol);
        if (MarketRouteValue() == MarketSymbolClassifier.ForexMarket && normalized.Length >= 6)
            return normalized[..3];

        return WalletSnapshot.ResolveAsset(symbol);
    }

    public sealed class WalletInput
    {
        public decimal CashCapital { get; set; }

        public bool AutoTradingEnabled { get; set; }

        public decimal ManagedTargetNetPercent { get; set; } = 5m;

        public List<WalletAssetInput> Assets { get; set; } = [];
    }

    public sealed class WalletAssetInput
    {
        public string Symbol { get; set; } = "";

        public string Asset { get; set; } = "";

        public decimal CoinQuantity { get; set; }

        public bool AllowSellHighBuyLow { get; set; }

        public bool AutoTradingEnabled { get; set; }
    }
}

public sealed record WalletUsageSummary(decimal FreeCapital, decimal CapitalInUse, decimal ClosedNet, decimal SimulatedBalance, int OpenOperations, int ClosedOperations);

public sealed record WalletAssetUsageRow(string Symbol, string Asset, decimal WalletQuantity, decimal QuantityInUse, decimal CapitalInUse, decimal QuantityFree);

public sealed record WalletOperationRow(
    Guid Id,
    string Symbol,
    string Asset,
    MarketSide Side,
    OpportunityStatus Status,
    DateTimeOffset SignalTime,
    DateTimeOffset? ExitTime,
    string ActionLabel,
    bool CanOperate,
    string AutoStatus,
    string ExecutionStatus,
    decimal Investment,
    decimal EntryPrice,
    decimal? ExitPrice,
    decimal CurrentPrice,
    decimal Quantity,
    decimal EntryFee,
    decimal ExitFee,
    decimal NetBenefit,
    decimal TotalObtained,
    bool IsOpen);
