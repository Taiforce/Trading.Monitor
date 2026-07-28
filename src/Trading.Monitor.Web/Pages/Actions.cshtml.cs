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

public sealed class ActionsModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<ActionsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");
    private const int PreEntryLeadMinutes = 3;
    private static readonly TradeInstructionService ClassicInstructionService = new(new RiskOptions { ManagedProfitExitEnabled = false });

    public IReadOnlyList<OpportunityReportRow> Rows { get; private set; } = [];

    public IReadOnlyList<OpportunityReportRow> HighlightedRows { get; private set; } = [];

    public IReadOnlyList<string> Symbols { get; private set; } = [];

    public decimal FilteredWonAmount { get; private set; }

    public decimal FilteredLostAmount { get; private set; }

    public decimal FilteredRealizedNet { get; private set; }

    public decimal SimulatedBalanceAfterClosedOperations { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "abiertas";

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string TipoSenal { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading actions page for capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);

        Symbols = BuildSymbolList(Report.RecentSignals.Select(row => row.Symbol));
        Rows = ApplyFilters(Report.RecentSignals);
        HighlightedRows = Rows.Where(row => InstructionFor(row).Highlight).Take(6).ToArray();
        CalculateMoneySummary();
    }

    public TradeInstruction InstructionFor(OpportunityReportRow row)
    {
        return ClassicInstructionService.Create(row);
    }

    public string TimeLeft(OpportunityReportRow row)
    {
        if (row.Status != OpportunityStatus.Open)
            return row.ExitTime.HasValue ? $"Cerro {row.ExitTime.Value.ToLocalTime():HH:mm}" : "Cerrada";

        var remaining = row.ExpiresAt - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero ? "0m" : FormatDuration(remaining);
    }

    public string PreEntryLeft(OpportunityReportRow row)
    {
        if (row.Status != OpportunityStatus.Open)
            return "-";

        var remaining = row.ObservedAt.AddMinutes(PreEntryLeadMinutes) - DateTimeOffset.UtcNow;
        return remaining <= TimeSpan.Zero ? "0m" : FormatDuration(remaining);
    }

    public string MaxLife(OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((row.ExpiresAt - row.ObservedAt).TotalMinutes));
        return $"{minutes} min";
    }

    public string ShortReason(OpportunityReportRow row)
    {
        return SplitNotes(row.Reasons).FirstOrDefault() ?? "Confluencia tecnica detectada.";
    }

    public string Quantity(OpportunityReportRow row)
    {
        return $"{row.EstimatedQuantity.ToString("N8", NumberCulture)} {Asset(row.Symbol)}";
    }

    public string EntryExit(OpportunityReportRow row)
    {
        return row.ExitPrice.HasValue
            ? $"{Price(row.EntryPrice)} -> {Price(row.ExitPrice.Value)}"
            : $"{Price(row.EntryPrice)} -> pendiente";
    }

    public decimal BreakEvenPrice(OpportunityReportRow row)
    {
        if (row.EstimatedQuantity <= 0m)
            return row.EntryPrice;

        var move = row.EstimatedFees / row.EstimatedQuantity;
        return row.Side == MarketSide.Long ? row.EntryPrice + move : row.EntryPrice - move;
    }

    public string RealizedHeadline(OpportunityReportRow row)
    {
        if (!row.RealizedNetPnL.HasValue)
            return $"Con {Money(row.Capital)}: resultado pendiente";

        var percent = row.Capital <= 0m ? 0m : row.RealizedNetPnL.Value / row.Capital * 100m;
        return $"Con {Money(row.Capital)}: {Money(row.RealizedNetPnL.Value)} ({percent:N2}%)";
    }

    public string ExecutionFormula(OpportunityReportRow row)
    {
        var side = SignalTypeLabel(row.Side);
        if (!row.ExitPrice.HasValue || !row.RealizedNetPnL.HasValue)
            return $"{side}: {Money(row.Capital)} / {Price(row.EntryPrice)} = {Quantity(row)}. Cuando cierre, se calcula contra el precio de salida.";

        return $"{side}: {Money(row.Capital)} / {Price(row.EntryPrice)} = {Quantity(row)}; salida {Price(row.ExitPrice.Value)}; neto después de comisiones {Money(row.RealizedNetPnL.Value)}.";
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

    private void CalculateMoneySummary()
    {
        var closed = Rows.Where(row => row.Status != OpportunityStatus.Open && row.RealizedNetPnL.HasValue).ToArray();
        FilteredWonAmount = closed.Where(row => row.RealizedNetPnL > 0m).Sum(row => row.RealizedNetPnL!.Value);
        FilteredLostAmount = Math.Abs(closed.Where(row => row.RealizedNetPnL < 0m).Sum(row => row.RealizedNetPnL!.Value));
        FilteredRealizedNet = FilteredWonAmount - FilteredLostAmount;
        SimulatedBalanceAfterClosedOperations = Report.Capital + FilteredRealizedNet;
    }

    private static string FormatDuration(TimeSpan value)
    {
        var totalSeconds = Math.Max(0, (int)Math.Round(value.TotalSeconds));
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return minutes <= 0 ? $"{seconds}s" : $"{minutes}m {seconds:00}s";
    }

    private static string Asset(string symbol)
    {
        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return symbol[..^4].ToUpperInvariant();

        if (symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return symbol[..^3].ToUpperInvariant();

        return symbol.ToUpperInvariant();
    }
}
