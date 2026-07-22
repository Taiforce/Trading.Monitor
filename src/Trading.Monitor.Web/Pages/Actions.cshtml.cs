using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class ActionsModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<ActionsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public IReadOnlyList<OpportunityReportRow> Rows { get; private set; } = [];

    public IReadOnlyList<string> Symbols { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "abiertas";

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading actions page for capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);

        Symbols = Report.RecentSignals.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row).ToArray();
        Rows = ApplyFilters(Report.RecentSignals);
    }

    private IReadOnlyList<OpportunityReportRow> ApplyFilters(IEnumerable<OpportunityReportRow> rows)
    {
        if (!string.IsNullOrWhiteSpace(Symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, Symbol, StringComparison.OrdinalIgnoreCase));

        rows = Estado?.Trim().ToLowerInvariant() switch
        {
            "cerradas" => rows.Where(row => row.Status != OpportunityStatus.Open),
            "todas" => rows,
            _ => rows.Where(row => row.Status == OpportunityStatus.Open)
        };

        return rows.OrderByDescending(row => row.ObservedAt).ToArray();
    }
}
