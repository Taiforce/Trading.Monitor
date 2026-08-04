using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

/// <summary>
/// Compares the three signal-learning sources side by side: Propias (own self-learning engine),
/// Ajenas (external ensemble of public strategies) and Traders (real leaderboard positions).
/// </summary>
public sealed class AprendizajeIaModel : TradingPageModel
{
    private readonly IOpportunityRepository _opportunityRepository;
    private readonly IOptionsMonitor<ReportingOptions> _reportingOptions;
    private readonly ILogger<AprendizajeIaModel> _logger;

    public AprendizajeIaModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<AprendizajeIaModel> logger)
        : base(opportunityRepository, reportingOptions)
    {
        _opportunityRepository = opportunityRepository;
        _reportingOptions = reportingOptions;
        _logger = logger;
    }

    public AiSourcesReport AiSources { get; private set; } = AiSourcesReportBuilder.Build([]);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (Capital <= 0m)
            Capital = _reportingOptions.CurrentValue.DefaultCapital;

        _logger.LogInformation("Loading AI sources comparison for {Market}.", Mercado);

        var allSignals = await _opportunityRepository.GetSignalsAsync(Capital, cancellationToken);
        var marketRows = allSignals.Where(MatchesCurrentMarket).ToArray();
        AiSources = AiSourcesReportBuilder.Build(marketRows);
    }

    public string SourceCardClass(AiSourceStats stats)
    {
        if (stats.ClosedSignals < 5)
            return "result-yellow";

        return stats.WinRatePercent switch
        {
            >= 55m => "result-green",
            < 42m => "result-red",
            _ => "result-yellow"
        };
    }

    public static string ViewFor(SignalOriginKind origin)
    {
        return origin switch
        {
            SignalOriginKind.ExternalAi => "ia",
            SignalOriginKind.Trader => "traders",
            _ => "propio"
        };
    }
}
