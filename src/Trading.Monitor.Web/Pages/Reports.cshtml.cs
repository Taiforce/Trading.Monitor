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

public sealed class ReportsModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, TradeInstructionService tradeInstructionService, ILogger<ReportsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");

    public decimal MaxSymbolValue { get; private set; }

    public SymbolReportRow? BestSymbol { get; private set; }

    public SymbolReportRow? WorstSymbol { get; private set; }

    public DailyReportRow? BestDay { get; private set; }

    public decimal AveragePotentialTp1 { get; private set; }

    public decimal AverageStopLoss { get; private set; }

    public string ExecutiveReadout { get; private set; } = "";

    public IReadOnlyList<OpportunityReportRow> HighConvictionRows { get; private set; } = [];

    public decimal AverageHighConvictionTp1 { get; private set; }

    public decimal AverageHighConvictionStop { get; private set; }

    public IReadOnlyList<string> Symbols { get; private set; } = [];

    public IReadOnlyList<OpportunityReportRow> FilteredRows { get; private set; } = [];

    public IReadOnlyList<SymbolReportRow> FilteredSymbolBreakdown { get; private set; } = [];

    public IReadOnlyList<DailyReportRow> FilteredDailyBreakdown { get; private set; } = [];

    public int FilteredTotalSignals { get; private set; }

    public int FilteredOpenSignals { get; private set; }

    public int FilteredClosedSignals { get; private set; }

    public int FilteredWinners { get; private set; }

    public int FilteredLosers { get; private set; }

    public decimal FilteredWinRate { get; private set; }

    public decimal FilteredRealizedNetPnL { get; private set; }

    public decimal FilteredPotentialTarget { get; private set; }

    public decimal FilteredPotentialLoss { get; private set; }

    public decimal FilteredAverageScore { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string TipoSenal { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ScoreMinimo { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading reports page for capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);

        Symbols = Report.RecentSignals.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row).ToArray();
        FilteredRows = ApplyFilters(Report.RecentSignals).ToArray();
        BuildFilteredMetrics();

        MaxSymbolValue = FilteredSymbolBreakdown
            .Select(row => Math.Max(Math.Abs(row.PotentialNetAtTakeProfit1), Math.Abs(row.PotentialLossAtStop)))
            .DefaultIfEmpty(0m)
            .Max();

        BestSymbol = FilteredSymbolBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        WorstSymbol = FilteredSymbolBreakdown.OrderBy(row => row.RealizedNetPnL).FirstOrDefault();
        BestDay = FilteredDailyBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        AveragePotentialTp1 = FilteredTotalSignals == 0 ? 0m : Math.Round(FilteredPotentialTarget / FilteredTotalSignals, 2);
        AverageStopLoss = FilteredTotalSignals == 0 ? 0m : Math.Round(FilteredPotentialLoss / FilteredTotalSignals, 2);
        HighConvictionRows = FilteredRows.Where(row => tradeInstructionService.Create(row).Highlight).ToArray();
        AverageHighConvictionTp1 = HighConvictionRows.Count == 0 ? 0m : Math.Round(HighConvictionRows.Average(row => row.NetProfitAtTakeProfit1), 2);
        AverageHighConvictionStop = HighConvictionRows.Count == 0 ? 0m : Math.Round(HighConvictionRows.Average(row => row.NetLossAtStop), 2);
        ExecutiveReadout = BuildExecutiveReadout();
    }

    private IEnumerable<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol.Trim(), StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => MatchesSignalType(row, TipoSenal));

        rows = Estado?.Trim().ToLowerInvariant() switch
        {
            "abiertas" => rows.Where(row => row.Status == OpportunityStatus.Open),
            "cerradas" => rows.Where(row => row.Status != OpportunityStatus.Open),
            "ganadas" => rows.Where(row => row.RealizedNetPnL > 0m),
            "perdidas" => rows.Where(row => row.RealizedNetPnL < 0m),
            _ => rows
        };

        if (Desde.HasValue)
            rows = rows.Where(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime) >= Desde.Value);

        if (Hasta.HasValue)
            rows = rows.Where(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime) <= Hasta.Value);

        if (ScoreMinimo.HasValue)
            rows = rows.Where(row => row.Score >= ScoreMinimo.Value);

        return rows.OrderBy(SignalTypePriority).ThenByDescending(row => row.ObservedAt);
    }

    private void BuildFilteredMetrics()
    {
        var rows = FilteredRows;
        var closed = rows.Where(row => row.Status != OpportunityStatus.Open).ToArray();

        FilteredTotalSignals = rows.Count;
        FilteredOpenSignals = rows.Count(row => row.Status == OpportunityStatus.Open);
        FilteredClosedSignals = closed.Length;
        FilteredWinners = closed.Count(row => row.RealizedNetPnL > 0m);
        FilteredLosers = closed.Count(row => row.RealizedNetPnL < 0m);
        FilteredWinRate = closed.Length == 0 ? 0m : Math.Round((decimal)FilteredWinners / closed.Length * 100m, 2);
        FilteredRealizedNetPnL = rows.Sum(row => row.RealizedNetPnL ?? 0m);
        FilteredPotentialTarget = rows.Sum(row => row.NetProfitAtTakeProfit1);
        FilteredPotentialLoss = rows.Sum(row => row.NetLossAtStop);
        FilteredAverageScore = rows.Count == 0 ? 0m : Math.Round(rows.Average(row => (decimal)row.Score), 2);

        FilteredSymbolBreakdown = rows.GroupBy(row => row.Symbol)
            .Select(group => new SymbolReportRow(
                group.Key,
                group.Count(),
                group.Count(row => row.Status == OpportunityStatus.Open),
                group.Count(row => row.RealizedNetPnL > 0m),
                group.Count(row => row.RealizedNetPnL < 0m),
                group.Sum(row => row.RealizedNetPnL ?? 0m),
                group.Sum(row => row.NetProfitAtTakeProfit1),
                group.Sum(row => row.NetLossAtStop)))
            .OrderByDescending(row => row.TotalSignals)
            .ToArray();

        FilteredDailyBreakdown = rows.GroupBy(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime.Date))
            .Select(group => new DailyReportRow(
                group.Key,
                group.Count(),
                group.Count(row => row.Status != OpportunityStatus.Open),
                group.Sum(row => row.RealizedNetPnL ?? 0m),
                group.Sum(row => row.NetProfitAtTakeProfit1),
                group.Sum(row => row.NetLossAtStop)))
            .OrderByDescending(row => row.Day)
            .Take(30)
            .ToArray();
    }

    private string BuildExecutiveReadout()
    {
        if (Report.TotalSignals == 0)
            return "Todavia no hay suficiente historial. Primero deja que el worker acumule oportunidades.";

        if (FilteredTotalSignals == 0)
            return "No hay operaciones para esos filtros. Cambia activo, fecha, estado o score minimo.";

        if (FilteredWinRate >= 55m && FilteredRealizedNetPnL > 0m)
            return "El historial cerrado tiene ventaja positiva. La prioridad es repetir setups similares sin aumentar riesgo.";

        if (FilteredOpenSignals > 0 && FilteredClosedSignals == 0)
            return "Hay oportunidades abiertas, pero aun no hay resultados cerrados. Decide solo con entrada, perdida maxima y tamano de posicion claros.";

        if (FilteredPotentialLoss < 0m && FilteredPotentialTarget <= Math.Abs(FilteredPotentialLoss))
            return "La recompensa potencial todavia no compensa el dano posible. Aqui el filtro debe ser mas exigente.";

        return "El sistema esta generando datos utiles, pero la muestra aun necesita mas cierres para juzgar calidad real.";
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

    public TradeConversionSummary ConversionFor(OpportunityReportRow row)
    {
        return TradeConversionCalculator.Build(
            row.Symbol,
            row.Side,
            row.Capital,
            row.EstimatedQuantity,
            row.EntryPrice,
            row.ExitPrice,
            null,
            row.RealizedNetPnL,
            row.EstimatedFees);
    }

    public TradeInstruction InstructionFor(OpportunityReportRow row)
    {
        return tradeInstructionService.Create(row);
    }

    public string OperationType(OpportunityReportRow row)
    {
        return SignalTypeLabel(row.Side);
    }

    public string OperationMeaning(OpportunityReportRow row)
    {
        return SignalTypeRequirement(row.Side);
    }

    public string EntryWindow(OpportunityReportRow row)
    {
        return $"{row.ObservedAt.ToLocalTime():HH:mm}-{row.ObservedAt.AddMinutes(3).ToLocalTime():HH:mm}";
    }

    public string ExitLimit(OpportunityReportRow row)
    {
        return row.ExitTime.HasValue
            ? row.ExitTime.Value.ToLocalTime().ToString("dd MMM HH:mm")
            : row.ExpiresAt.ToLocalTime().ToString("dd MMM HH:mm");
    }

    public string SimulationLine(OpportunityReportRow row)
    {
        return $"{Money(row.Capital)} -> {Quantity(row)}";
    }

    public string RealNet(OpportunityReportRow row)
    {
        return row.RealizedNetPnL.HasValue ? Money(row.RealizedNetPnL.Value) : "Pendiente";
    }

    public string RealTotal(TradeConversionSummary conversion)
    {
        return conversion.FinalTotal.HasValue ? Money(conversion.FinalTotal.Value) : "Pendiente";
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
