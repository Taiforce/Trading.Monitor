using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class ManagedPositionsModel : TradingPageModel
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly IWalletRepository _walletRepository;
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions;
    private readonly IOptionsMonitor<RiskOptions> _riskOptions;
    private readonly ILogger<ManagedPositionsModel> _logger;

    public ManagedPositionsModel(
        IOpportunityRepository opportunityRepository,
        IWalletRepository walletRepository,
        IOptionsMonitor<ReportingOptions> reportingOptions,
        IOptionsMonitor<RiskOptions> riskOptions,
        ILogger<ManagedPositionsModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _walletRepository = walletRepository;
        _reportingOptions = reportingOptions;
        _riskOptions = riskOptions;
        _logger = logger;
    }

    public IReadOnlyList<OpportunityReportRow> Rows { get; private set; } = [];

    public IReadOnlyList<string> Symbols { get; private set; } = [];

    public IReadOnlyDictionary<string, int> OpenCountBySymbol { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "abiertas";

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string TipoSenal { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public decimal TargetNetPercent { get; set; }

    [BindProperty]
    public Guid CloseId { get; set; }

    [BindProperty]
    public decimal CloseNetPercent { get; set; }

    public decimal DefaultTargetNetPercent => Math.Max(0.01m, _riskOptions.CurrentValue.ManagedProfitExitPercentAfterCosts);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading managed positions page for capital {Capital}.", Capital);
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCloseAsync(CancellationToken cancellationToken)
    {
        var capital = Capital <= 0m ? _reportingOptions.CurrentValue.DefaultCapital : Capital;
        var rows = await _opportunityRepository.GetSignalsAsync(capital, cancellationToken);
        var row = rows.FirstOrDefault(item => item.Id == CloseId);

        if (row is null || row.Status != OpportunityStatus.Open)
            return RedirectToPage(new { Mercado, Capital = capital, Symbol, TipoSenal, TargetNetPercent });

        var percent = CloseNetPercent == 0m ? ResolveTargetPercent() : CloseNetPercent;
        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, percent, _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var breakdown = CostBreakdown(row, exitPrice);
        await _opportunityRepository.UpdateManagedTargetAsync(row.Id, percent, cancellationToken);

        var exit = new OpportunityExit(OpportunityStatus.ManuallyClosed, DateTimeOffset.UtcNow, exitPrice, $"Cierre manual web con resultado neto objetivo {percent:N2}% después de comisiones.");

        await _opportunityRepository.UpdateExitAsync(row.Id, exit, breakdown.GrossBenefit, breakdown.NetBenefit, cancellationToken);

        return RedirectToPage(new { Mercado, Capital = capital, Symbol, TipoSenal, TargetNetPercent = percent });
    }

    public TradeCostBreakdown TargetBreakdown(OpportunityReportRow row)
    {
        var targetPrice = TargetExitPrice(row);
        return CostBreakdown(row, targetPrice);
    }

    public decimal TargetExitPrice(OpportunityReportRow row)
    {
        return TradeCostCalculator.ResolveExitPriceForNetPercent(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, ResolveTargetPercent(), _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
    }

    public string Quantity(OpportunityReportRow row)
    {
        return $"{row.EstimatedQuantity.ToString("N8", NumberCulture)} {TradeConversionCalculator.Asset(row.Symbol)}";
    }

    public string TargetAction(OpportunityReportRow row)
    {
        return row.Side == MarketSide.Long ? "Vender" : "Comprar de regreso";
    }

    public string EntryAction(OpportunityReportRow row)
    {
        return row.Side == MarketSide.Long ? "Comprar" : "Vender";
    }

    public string ManagedStatusText(OpportunityReportRow row)
    {
        if (row.Status == OpportunityStatus.Open)
            return $"Viva hasta detectar salida neta >= {ResolveTargetPercent():N2}% o cierre manual.";

        return row.ExitTime.HasValue ? $"Cerrada {row.ExitTime.Value.ToLocalTime():dd MMM HH:mm}" : "Cerrada";
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (TargetNetPercent <= 0m)
            TargetNetPercent = DefaultTargetNetPercent;

        await LoadReportAsync(cancellationToken);
        var wallet = await _walletRepository.GetSnapshotAsync(Mercado, cancellationToken);
        Symbols = BuildSymbolListForMarket(Report.RecentSignals.Select(row => row.Symbol));
        Rows = ApplyFilters(Report.RecentSignals)
            .Where(row => WalletSignalPolicy.CanShowSignal(row, wallet))
            .ToArray();
        OpenCountBySymbol = Rows
            .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
    }

    public int OpenCountFor(string symbol)
    {
        return OpenCountBySymbol.TryGetValue(symbol, out var count) ? count : 0;
    }

    private IReadOnlyList<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        rows = rows.Where(MatchesCurrentMarket);

        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol, StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => MatchesSignalType(row, TipoSenal));
        rows = rows.Where(row => row.Status == OpportunityStatus.Open);

        return rows
            .OrderBy(SignalTypePriority)
            .ThenByDescending(row => row.Score)
            .ThenByDescending(row => row.ObservedAt)
            .ToArray();
    }

    private decimal ResolveTargetPercent()
    {
        return TargetNetPercent <= 0m ? DefaultTargetNetPercent : TargetNetPercent;
    }
}
