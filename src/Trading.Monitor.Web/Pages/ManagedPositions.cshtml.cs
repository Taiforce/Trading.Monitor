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
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions;
    private readonly IOptionsMonitor<RiskOptions> _riskOptions;
    private readonly ILogger<ManagedPositionsModel> _logger;

    public ManagedPositionsModel(
        IOpportunityRepository opportunityRepository,
        IOptionsMonitor<ReportingOptions> reportingOptions,
        IOptionsMonitor<RiskOptions> riskOptions,
        ILogger<ManagedPositionsModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _reportingOptions = reportingOptions;
        _riskOptions = riskOptions;
        _logger = logger;
    }

    public IReadOnlyList<OpportunityReportRow> Rows { get; private set; } = [];

    public IReadOnlyList<string> Symbols { get; private set; } = [];

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
            return RedirectToPage(new { Capital = capital, Estado, Symbol, TipoSenal, TargetNetPercent });

        var percent = CloseNetPercent == 0m ? ResolveTargetPercent() : CloseNetPercent;
        var exitPrice = TradeCostCalculator.ResolveExitPriceForNetPercent(row.Side, row.Capital, row.EstimatedQuantity, row.EntryPrice, percent, _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var breakdown = CostBreakdown(row, exitPrice);
        await _opportunityRepository.UpdateManagedTargetAsync(row.Id, percent, cancellationToken);

        var exit = new OpportunityExit(OpportunityStatus.ManuallyClosed, DateTimeOffset.UtcNow, exitPrice, $"Cierre manual web con resultado neto objetivo {percent:N2}% después de comisiones.");

        await _opportunityRepository.UpdateExitAsync(row.Id, exit, breakdown.GrossBenefit, breakdown.NetBenefit, cancellationToken);

        return RedirectToPage(new { Capital = capital, Estado = "cerradas", Symbol, TipoSenal, TargetNetPercent = percent });
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
        Symbols = BuildSymbolList(Report.RecentSignals.Select(row => row.Symbol));
        Rows = ApplyFilters(Report.RecentSignals);
    }

    private IReadOnlyList<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol, StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => MatchesSignalType(row, TipoSenal));

        rows = Estado?.Trim().ToLowerInvariant() switch
        {
            "cerradas" => rows.Where(row => row.Status != OpportunityStatus.Open),
            "todas" => rows,
            _ => rows.Where(row => row.Status == OpportunityStatus.Open)
        };

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
