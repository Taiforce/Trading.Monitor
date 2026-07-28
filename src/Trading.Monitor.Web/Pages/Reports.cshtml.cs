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

public sealed class ReportsModel : TradingPageModel
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly TradeInstructionService _tradeInstructionService;
    private readonly ILogger<ReportsModel> _logger;

    public ReportsModel(
        IOpportunityRepository opportunityRepository,
        IOptionsMonitor<ReportingOptions> reportingOptions,
        TradeInstructionService tradeInstructionService,
        ILogger<ReportsModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _tradeInstructionService = tradeInstructionService;
        _logger = logger;
    }

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

    public IReadOnlyList<SignalLearningRow> LearningRows { get; private set; } = [];

    public string LearningReadout { get; private set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string TipoSenal { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string ModoOperacion { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public DateOnly? Desde { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? Hasta { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? ScoreMinimo { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading reports page for capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);

        var allSignals = await _opportunityRepository.GetSignalsAsync(Capital, cancellationToken);
        Symbols = BuildSymbolList(allSignals.Select(row => row.Symbol));
        FilteredRows = ApplyFilters(allSignals).ToArray();
        BuildFilteredMetrics();
        LearningRows = BuildLearningRows(FilteredRows);

        MaxSymbolValue = FilteredSymbolBreakdown
            .Select(row => Math.Max(Math.Abs(row.PotentialNetAtTakeProfit1), Math.Abs(row.PotentialLossAtStop)))
            .DefaultIfEmpty(0m)
            .Max();

        BestSymbol = FilteredSymbolBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        WorstSymbol = FilteredSymbolBreakdown.OrderBy(row => row.RealizedNetPnL).FirstOrDefault();
        BestDay = FilteredDailyBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        AveragePotentialTp1 = FilteredTotalSignals == 0 ? 0m : Math.Round(FilteredPotentialTarget / FilteredTotalSignals, 2);
        AverageStopLoss = FilteredTotalSignals == 0 ? 0m : Math.Round(FilteredPotentialLoss / FilteredTotalSignals, 2);
        HighConvictionRows = FilteredRows.Where(row => _tradeInstructionService.Create(row).Highlight).ToArray();
        AverageHighConvictionTp1 = HighConvictionRows.Count == 0 ? 0m : Math.Round(HighConvictionRows.Average(row => row.NetProfitAtTakeProfit1), 2);
        AverageHighConvictionStop = HighConvictionRows.Count == 0 ? 0m : Math.Round(HighConvictionRows.Average(row => row.NetLossAtStop), 2);
        ExecutiveReadout = BuildExecutiveReadout();
        LearningReadout = BuildLearningReadout();
    }

    private IEnumerable<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol.Trim(), StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => MatchesSignalType(row, TipoSenal));
        rows = rows.Where(row => MatchesOperationMode(row, ModoOperacion));

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
            return "No hay operaciones para esos filtros. Cambia activo, fecha, estado o score mínimo.";

        if (FilteredWinRate >= 55m && FilteredRealizedNetPnL > 0m)
            return "El historial cerrado tiene ventaja positiva. La prioridad es repetir setups similares sin aumentar riesgo.";

        if (FilteredOpenSignals > 0 && FilteredClosedSignals == 0)
            return "Hay oportunidades abiertas, pero aún no hay resultados cerrados. Decide solo con entrada, pérdida máxima y tamaño de posición claros.";

        if (FilteredPotentialLoss < 0m && FilteredPotentialTarget <= Math.Abs(FilteredPotentialLoss))
            return "La recompensa potencial todavía no compensa el daño posible. Aquí el filtro debe ser más exigente.";

        return "El sistema está generando datos útiles, pero la muestra aún necesita más cierres para juzgar calidad real.";
    }

    private IReadOnlyList<SignalLearningRow> BuildLearningRows(IReadOnlyList<OpportunityReportRow> rows)
    {
        var closed = rows.Where(row => row.Status != OpportunityStatus.Open).ToArray();
        if (closed.Length == 0)
            return [];

        return closed
            .GroupBy(row => $"{row.Symbol} | {SignalTypeLabel(row.Side)} | {HorizonFor(row)}")
            .Select(group =>
            {
                var items = group.ToArray();
                var winners = items.Count(row => row.RealizedNetPnL > 0m);
                var losers = items.Count(row => row.RealizedNetPnL < 0m);
                var winRate = items.Length == 0 ? 0m : Math.Round((decimal)winners / items.Length * 100m, 2);
                var net = items.Sum(row => row.RealizedNetPnL ?? 0m);
                var averageScore = items.Length == 0 ? 0m : Math.Round(items.Average(row => (decimal)row.Score), 1);
                var recommendation = BuildLearningRecommendation(items.Length, winRate, net, averageScore);

                return new SignalLearningRow(group.Key, items.Length, winners, losers, winRate, net, averageScore, recommendation, LearningClass(winRate, net, items.Length));
            })
            .OrderBy(row => row.ClassName == "loss" ? 0 : row.ClassName == "gain" ? 2 : 1)
            .ThenByDescending(row => row.TotalSignals)
            .Take(12)
            .ToArray();
    }

    private string BuildLearningReadout()
    {
        if (LearningRows.Count == 0)
            return "Aún no hay cierres suficientes para aprender de las propias señales.";

        var risky = LearningRows.FirstOrDefault(row => row.ClassName == "loss");
        if (risky is not null)
            return $"El sistema debe ponerse más exigente con {risky.Pattern}: {risky.WinRate:N1}% win rate y {Money(risky.RealizedNetPnL)} neto.";

        var strong = LearningRows.FirstOrDefault(row => row.ClassName == "gain");
        if (strong is not null)
            return $"El mejor patron medido es {strong.Pattern}: {strong.WinRate:N1}% win rate y {Money(strong.RealizedNetPnL)} neto.";

        return "Los patrones cerrados todavía están neutrales; conviene acumular más muestra antes de ajustar fuerte.";
    }

    private static string BuildLearningRecommendation(int total, decimal winRate, decimal net, decimal averageScore)
    {
        if (total < 5)
            return "Muestra pequena: observar antes de cambiar reglas.";

        if (winRate < 42m && net < 0m)
            return $"Bloquear o subir score mínimo sobre {Math.Min(100, Math.Ceiling(averageScore + 3m)):N0}.";

        if (winRate >= 55m && net > 0m)
            return "Priorizar con el mismo riesgo; no aumentar capital automaticamente.";

        if (net < 0m)
            return "Reducir frecuencia y exigir más confirmaciones.";

        return "Mantener en observacion; ventaja todavia moderada.";
    }

    private static string LearningClass(decimal winRate, decimal net, int total)
    {
        if (total >= 5 && winRate < 42m && net < 0m)
            return "loss";

        if (total >= 5 && winRate >= 55m && net > 0m)
            return "gain";

        return "flat";
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
        return _tradeInstructionService.Create(row);
    }

    public string OperationType(OpportunityReportRow row)
    {
        return SignalTypeLabel(row.Side);
    }

    public string OperationModeLabel(OpportunityReportRow row)
    {
        return IsTrackingSignal(row) ? "Seguimiento" : "Señal fija";
    }

    public string OperationModeHint(OpportunityReportRow row)
    {
        return IsTrackingSignal(row)
            ? "Entrada con salida administrada por objetivo neto y mercado vivo."
            : "Entrada con salida/objetivo definido desde la señal original.";
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

    public string ChartIntervalFor(OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (row.ExpiresAt - row.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "1m",
            <= 240 => "5m",
            <= 2880 => "15m",
            <= 10080 => "1h",
            <= 43200 => "4h",
            _ => "1d"
        };
    }

    public DateTimeOffset ReplayFrom(OpportunityReportRow row)
    {
        var interval = ChartIntervalFor(row);
        var buffer = interval switch
        {
            "1m" => TimeSpan.FromMinutes(30),
            "5m" => TimeSpan.FromHours(2),
            "15m" => TimeSpan.FromHours(8),
            "1h" => TimeSpan.FromDays(2),
            "4h" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(20)
        };

        return row.ObservedAt.Subtract(buffer);
    }

    public DateTimeOffset ReplayTo(OpportunityReportRow row)
    {
        var end = row.ExitTime ?? row.ExpiresAt;
        var interval = ChartIntervalFor(row);
        var buffer = interval switch
        {
            "1m" => TimeSpan.FromMinutes(30),
            "5m" => TimeSpan.FromHours(2),
            "15m" => TimeSpan.FromHours(8),
            "1h" => TimeSpan.FromDays(2),
            "4h" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromDays(20)
        };

        return end.Add(buffer);
    }

    public string RealNet(OpportunityReportRow row)
    {
        return row.RealizedNetPnL.HasValue ? Money(row.RealizedNetPnL.Value) : "Pendiente";
    }

    public string RealTotal(TradeConversionSummary conversion)
    {
        return conversion.FinalTotal.HasValue ? Money(conversion.FinalTotal.Value) : "Pendiente";
    }

    private static bool MatchesOperationMode(OpportunityReportRow row, string? mode)
    {
        return mode?.Trim().ToLowerInvariant() switch
        {
            "fija" => !IsTrackingSignal(row),
            "seguimiento" => IsTrackingSignal(row),
            _ => true
        };
    }

    private static bool IsTrackingSignal(OpportunityReportRow row)
    {
        if (row.Status is OpportunityStatus.ManagedProfitExit or OpportunityStatus.ManuallyClosed)
            return true;

        return false;
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

public sealed record SignalLearningRow(string Pattern, int TotalSignals, int Winners, int Losers, decimal WinRate, decimal RealizedNetPnL, decimal AverageScore, string Recommendation, string ClassName);
