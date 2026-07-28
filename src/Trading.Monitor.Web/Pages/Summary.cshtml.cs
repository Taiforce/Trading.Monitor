using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class SummaryModel : TradingPageModel
{
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly ITraderResearchRepository _traderRepository;
    private readonly AiConsensusEngine _aiConsensusEngine;
    private readonly ILogger<SummaryModel> _logger;

    public SummaryModel(
        IOpportunityRepository opportunityRepository,
        ITraderResearchRepository traderRepository,
        IOptionsMonitor<ReportingOptions> reportingOptions,
        AiConsensusEngine aiConsensusEngine,
        ILogger<SummaryModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _traderRepository = traderRepository;
        _aiConsensusEngine = aiConsensusEngine;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string VistaResumen { get; set; } = "propio";

    public IReadOnlyList<OpportunityReportRow> MarketRows { get; private set; } = [];

    public IReadOnlyList<OpportunityReportRow> OpenRows { get; private set; } = [];

    public IReadOnlyList<OpportunityReportRow> ClosedRows { get; private set; } = [];

    public IReadOnlyList<AiConsensusResult> ConsensusRows { get; private set; } = [];

    public IReadOnlyList<SummaryPatternRow> PatternRows { get; private set; } = [];

    public TraderResearchReport TraderReport { get; private set; } = EmptyTraderReport();

    public decimal RealizedNet { get; private set; }

    public decimal WinRate { get; private set; }

    public decimal AverageScore { get; private set; }

    public string Headline { get; private set; } = "";

    public string RouteLabel => VistaResumen switch
    {
        "ia" => "Operaciones otras IAs",
        "traders" => "Operaciones traders",
        _ => "Operaciones IA propia"
    };

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading summary page for {Market} {View}.", Mercado, VistaResumen);
        await LoadReportAsync(cancellationToken);
        VistaResumen = NormalizeView(VistaResumen);

        var allSignals = await _opportunityRepository.GetSignalsAsync(Capital, cancellationToken);
        MarketRows = allSignals.Where(MatchesCurrentMarket).OrderByDescending(row => row.ObservedAt).ToArray();
        OpenRows = MarketRows.Where(row => row.Status == OpportunityStatus.Open).Take(8).ToArray();
        ClosedRows = MarketRows.Where(row => row.Status != OpportunityStatus.Open).ToArray();
        RealizedNet = ClosedRows.Sum(row => row.RealizedNetPnL ?? 0m);
        WinRate = ClosedRows.Count == 0 ? 0m : Math.Round((decimal)ClosedRows.Count(row => row.RealizedNetPnL > 0m) / ClosedRows.Count * 100m, 2);
        AverageScore = MarketRows.Count == 0 ? 0m : Math.Round(MarketRows.Average(row => (decimal)row.Score), 1);
        ConsensusRows = MarketRows.Take(12).Select(row => _aiConsensusEngine.Evaluate(row, MarketRows)).OrderByDescending(row => row.CompositeScore).Take(5).ToArray();
        PatternRows = BuildPatterns(ClosedRows);
        TraderReport = await _traderRepository.GetReportAsync(new TraderResearchFilter(Mercado, null, null, "todas", null, false), cancellationToken);
        Headline = BuildHeadline();
    }

    public string SummaryCardClass(decimal value)
    {
        return value > 0m ? "result-green" : value < 0m ? "result-red" : "result-yellow";
    }

    public string SummaryLabel()
    {
        return VistaResumen switch
        {
            "ia" => "Resumen basado en el consenso de enfoques externos: compara score, veto, riesgo y costo.",
            "traders" => "Resumen de traders: fuentes, perfiles, historial local, operaciones abiertas y resultados cerrados.",
            _ => "Resumen del sistema propio: mide lo que el motor está generando y cómo le fue al cerrarse."
        };
    }

    public string LiveLink()
    {
        return VistaResumen == "traders" ? "/mercado-traders" : "/acciones";
    }

    private string BuildHeadline()
    {
        if (VistaResumen == "traders")
        {
            if (TraderReport.TotalTraders == 0)
                return "Todavía no hay traders útiles para estudiar en este mercado.";

            if (TraderReport.OpenTrades > 0)
                return $"Hay {TraderReport.OpenTrades} operaciones abiertas de traders para vigilar en {MarketLabel()}.";

            return $"Hay {TraderReport.TotalTraders} traders mapeados; el foco es separar historial verificable de simple popularidad.";
        }

        if (MarketRows.Count == 0)
            return $"Aún no hay señales de {MarketLabel()} para resumir. El worker sigue buscando.";

        if (RealizedNet > 0m && WinRate >= 50m)
            return $"El historial cerrado de {MarketLabel()} va positivo: {Money(RealizedNet)} neto y {WinRate:N1}% de acierto.";

        if (OpenRows.Count > 0)
            return $"Hay {OpenRows.Count} señales abiertas en {MarketLabel()}; conviene mirar costo, comisión y salida antes de actuar.";

        return $"El resumen de {MarketLabel()} todavía no muestra ventaja clara; mejor esperar señales más fuertes.";
    }

    private IReadOnlyList<SummaryPatternRow> BuildPatterns(IReadOnlyList<OpportunityReportRow> rows)
    {
        return rows.GroupBy(row => $"{row.Symbol} | {SignalTypeLabel(row.Side)} | {HorizonFor(row)}")
            .Select(group =>
            {
                var items = group.ToArray();
                var net = items.Sum(row => row.RealizedNetPnL ?? 0m);
                var winners = items.Count(row => row.RealizedNetPnL > 0m);
                var winRate = items.Length == 0 ? 0m : Math.Round((decimal)winners / items.Length * 100m, 1);
                var score = items.Length == 0 ? 0m : Math.Round(items.Average(row => (decimal)row.Score), 1);
                return new SummaryPatternRow(group.Key, items.Length, winRate, score, net);
            })
            .OrderByDescending(row => Math.Abs(row.RealizedNetPnL))
            .Take(6)
            .ToArray();
    }

    private static string NormalizeView(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "ia" => "ia",
            "traders" => "traders",
            _ => "propio"
        };
    }

    private static TraderResearchReport EmptyTraderReport()
    {
        return new TraderResearchReport([], [], [], null, 0, 0, 0, 0, 0);
    }
}

public sealed record SummaryPatternRow(string Pattern, int Total, decimal WinRate, decimal AverageScore, decimal RealizedNetPnL);
