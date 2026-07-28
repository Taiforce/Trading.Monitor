using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Web.Pages;

public sealed class WalletModel(IWalletRepository walletRepository, IOptionsMonitor<ExchangeExecutionOptions> exchangeOptions) : PageModel
{
    private static readonly string[] SupportedSymbols = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];

    [BindProperty]
    public WalletInput Input { get; set; } = new();

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
        var assets = Input.Assets
            .Select(asset => new WalletAssetUpdate(
                WalletSnapshot.NormalizeSymbol(asset.Symbol),
                string.IsNullOrWhiteSpace(asset.Asset) ? WalletSnapshot.ResolveAsset(asset.Symbol) : asset.Asset.Trim().ToUpperInvariant(),
                asset.CoinQuantity,
                asset.AllowSellHighBuyLow,
                asset.AutoTradingEnabled))
            .ToArray();

        await walletRepository.SaveAsync(Input.CashCapital, Input.AutoTradingEnabled, assets, cancellationToken);
        StatusMessage = "Wallet actualizada. Las señales se filtrarán con estos saldos.";

        return RedirectToPage();
    }

    public string BinanceSafetyText()
    {
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

    public string SellHighStatus(WalletAssetInput asset)
    {
        if (asset.CoinQuantity <= 0m)
            return "Oculta: no tienes monedas para vender.";

        return asset.AllowSellHighBuyLow ? "Permitida con tus monedas actuales." : "Bloqueada por ti.";
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Snapshot = await walletRepository.GetSnapshotAsync(cancellationToken);
        Input = BuildInput(Snapshot);
    }

    private static WalletInput BuildInput(WalletSnapshot snapshot)
    {
        var assets = SupportedSymbols
            .Select(symbol =>
            {
                var stored = snapshot.FindAsset(symbol);
                return new WalletAssetInput
                {
                    Symbol = symbol,
                    Asset = WalletSnapshot.ResolveAsset(symbol),
                    CoinQuantity = stored?.CoinQuantity ?? 0m,
                    AllowSellHighBuyLow = stored?.AllowSellHighBuyLow ?? true,
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
