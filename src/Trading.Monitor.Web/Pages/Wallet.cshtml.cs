using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class WalletModel(IWalletRepository walletRepository, IOptionsMonitor<ExchangeExecutionOptions> exchangeOptions) : PageModel
{
    private static readonly string[] CryptoSymbols = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];
    private static readonly string[] ForexSymbols = ["EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "USDMXN", "EURMXN", "GBPJPY", "EURJPY", "EURGBP"];

    [BindProperty]
    public WalletInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string Mercado { get; set; } = MarketSymbolClassifier.CryptoMarket;

    [TempData]
    public string? StatusMessage { get; set; }

    public WalletSnapshot Snapshot { get; private set; } = new(0m, false, []);

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
                asset.AutoTradingEnabled))
            .ToArray();

        await walletRepository.SaveAsync(Mercado, Input.CashCapital, Input.AutoTradingEnabled, assets, cancellationToken);
        StatusMessage = $"Wallet {MarketLabel()} actualizada. Las señales se filtrarán con estos saldos.";

        return RedirectToPage(new { Mercado });
    }

    public string MarketLabel()
    {
        return MarketSymbolClassifier.MarketLabel(Mercado);
    }

    public string MarketRouteValue()
    {
        return MarketSymbolClassifier.NormalizeMarket(Mercado);
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
            return "Forex protegido: broker real requiere integracion dedicada y pruebas antes de operar dinero real.";

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
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Broker forex automatico" : "Binance automatico";
    }

    public string ConnectionLinkText()
    {
        return MarketRouteValue() == MarketSymbolClassifier.ForexMarket ? "Ver conexiones Forex" : "Ver conexion Binance";
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

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Mercado = MarketSymbolClassifier.NormalizeMarket(Mercado);
        Snapshot = await walletRepository.GetSnapshotAsync(Mercado, cancellationToken);
        Input = BuildInput(Snapshot);
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
                    AutoTradingEnabled = stored?.AutoTradingEnabled ?? false
                };
            })
            .ToList();

        return new WalletInput
        {
            CashCapital = snapshot.CashCapital,
            AutoTradingEnabled = snapshot.AutoTradingEnabled,
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
