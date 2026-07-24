using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class PortfolioModel : TradingPageModel
{
    private static readonly CultureInfo NumberCulture = CultureInfo.GetCultureInfo("en-US");
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions;
    private readonly ITraderResearchRepository _traderRepository;
    private readonly VirtualPortfolioSimulator _simulator;
    private readonly TraderFollowSimulator _traderFollowSimulator;
    private readonly ILogger<PortfolioModel> _logger;

    public PortfolioModel(
        IOpportunityRepository opportunityRepository,
        ITraderResearchRepository traderRepository,
        IOptionsMonitor<ReportingOptions> reportingOptions,
        VirtualPortfolioSimulator simulator,
        TraderFollowSimulator traderFollowSimulator,
        ILogger<PortfolioModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _traderRepository = traderRepository;
        _reportingOptions = reportingOptions;
        _simulator = simulator;
        _traderFollowSimulator = traderFollowSimulator;
        _logger = logger;
    }

    public VirtualPortfolioReport Simulation { get; private set; } = VirtualPortfolioReport.Empty(1000m);

    public IReadOnlyList<string> Symbols { get; private set; } = [];

    public IReadOnlyList<OpportunityReportRow> FilteredSignals { get; private set; } = [];

    public string EquityPolyline { get; private set; } = "";

    public TraderFollowSimulationReport TraderSimulation { get; private set; } = TraderFollowSimulationReport.Empty(1000m);

    public IReadOnlyList<TraderProfileReportRow> Traders { get; private set; } = [];

    public string TraderEquityPolyline { get; private set; } = "";

    [BindProperty(SupportsGet = true)]
    public decimal InitialCapital { get; set; }

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

    [BindProperty(SupportsGet = true)]
    public Guid? TraderId { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (InitialCapital <= 0m)
            InitialCapital = _reportingOptions.CurrentValue.DefaultCapital;

        _logger.LogInformation("Loading virtual portfolio for initial capital {InitialCapital}.", InitialCapital);
        var allSignals = await _opportunityRepository.GetSignalsAsync(InitialCapital, cancellationToken);
        Symbols = allSignals.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row).ToArray();
        FilteredSignals = ApplyFilters(allSignals).ToArray();
        Simulation = _simulator.Simulate(FilteredSignals, InitialCapital, _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        EquityPolyline = BuildEquityPolyline(Simulation.EquityPoints);

        Traders = await _traderRepository.GetTradersAsync(cancellationToken);
        if (TraderId.HasValue)
        {
            var traderTrades = await _traderRepository.GetTradesAsync(TraderId.Value, Desde, Hasta, cancellationToken);
            TraderSimulation = _traderFollowSimulator.Simulate(traderTrades, InitialCapital, _reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
            TraderEquityPolyline = BuildTraderEquityPolyline(TraderSimulation.EquityPoints);
        }
        else
        {
            TraderSimulation = TraderFollowSimulationReport.Empty(InitialCapital);
            TraderEquityPolyline = BuildTraderEquityPolyline(TraderSimulation.EquityPoints);
        }
    }

    public string Quantity(VirtualPortfolioTradeRow row)
    {
        return $"{row.Quantity.ToString("N8", NumberCulture)} {Asset(row.Symbol)}";
    }

    public string Quantity(TraderFollowTradeRow row)
    {
        return $"{row.Quantity.ToString("N8", NumberCulture)} {Asset(row.Symbol)}";
    }

    public decimal BreakEvenPrice(VirtualPortfolioTradeRow row)
    {
        if (!row.WasApplied || row.Quantity <= 0m)
            return row.EntryPrice;

        var move = row.Fees / row.Quantity;
        return row.OperationType.StartsWith("Compra bajo", StringComparison.OrdinalIgnoreCase)
            ? row.EntryPrice + move
            : row.EntryPrice - move;
    }

    public string ResultClass(decimal value)
    {
        return value > 0m ? "gain" : value < 0m ? "loss" : "flat";
    }

    public string AppliedLabel(VirtualPortfolioTradeRow row)
    {
        return row.WasApplied ? "Aplicada" : row.SkipReason;
    }

    public string WinRate()
    {
        return Simulation.AppliedTrades == 0 ? "0.00%" : $"{(decimal)Simulation.Winners / Simulation.AppliedTrades * 100m:N2}%";
    }

    public string TraderWinRate()
    {
        return TraderSimulation.AppliedTrades == 0 ? "0.00%" : $"{(decimal)TraderSimulation.Winners / TraderSimulation.AppliedTrades * 100m:N2}%";
    }

    public string SelectedTraderName()
    {
        return Traders.FirstOrDefault(row => row.Id == TraderId)?.DisplayName ?? "Selecciona un trader";
    }

    private IEnumerable<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol.Trim(), StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => MatchesSignalType(row, TipoSenal));

        if (Desde.HasValue)
            rows = rows.Where(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime) >= Desde.Value);

        if (Hasta.HasValue)
            rows = rows.Where(row => DateOnly.FromDateTime(row.ObservedAt.LocalDateTime) <= Hasta.Value);

        if (ScoreMinimo.HasValue)
            rows = rows.Where(row => row.Score >= ScoreMinimo.Value);

        return rows.OrderBy(SignalTypePriority).ThenBy(row => row.ObservedAt);
    }

    private static string BuildEquityPolyline(IReadOnlyList<VirtualPortfolioEquityPoint> points)
    {
        if (points.Count == 0)
            return "";

        if (points.Count == 1)
            return "0,80 1000,80";

        var min = points.Min(point => point.Balance);
        var max = points.Max(point => point.Balance);
        var range = max - min;
        if (range <= 0m)
            range = 1m;

        return string.Join(" ", points.Select((point, index) =>
        {
            var x = points.Count == 1 ? 0m : index / (decimal)(points.Count - 1) * 1000m;
            var y = 150m - (point.Balance - min) / range * 130m;
            return $"{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";
        }));
    }

    private static string BuildTraderEquityPolyline(IReadOnlyList<TraderFollowEquityPoint> points)
    {
        if (points.Count == 0)
            return "";

        if (points.Count == 1)
            return "0,80 1000,80";

        var min = points.Min(point => point.Balance);
        var max = points.Max(point => point.Balance);
        var range = max - min;
        if (range <= 0m)
            range = 1m;

        return string.Join(" ", points.Select((point, index) =>
        {
            var x = points.Count == 1 ? 0m : index / (decimal)(points.Count - 1) * 1000m;
            var y = 150m - (point.Balance - min) / range * 130m;
            return $"{x.ToString("0.##", CultureInfo.InvariantCulture)},{y.ToString("0.##", CultureInfo.InvariantCulture)}";
        }));
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
