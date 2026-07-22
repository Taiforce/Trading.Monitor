using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class ConnectionsModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<ConnectionsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public IReadOnlyList<IGrouping<DataSourceKind, SourceHealthReportRow>> SourcesByKind { get; private set; } = [];

    public IReadOnlyList<SourceHealthReportRow> FilteredSources { get; private set; } = [];

    public IReadOnlyList<DataSourceKind> AvailableKinds { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Estado { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public string Tipo { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Buscar { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading connections page.");
        await LoadReportAsync(cancellationToken);

        AvailableKinds = Report.SourceHealth.Select(row => row.Kind).Distinct().OrderBy(row => row).ToArray();
        FilteredSources = ApplyFilters(Report.SourceHealth);
        SourcesByKind = FilteredSources.GroupBy(row => row.Kind).OrderBy(group => group.Key).ToArray();
    }

    private IReadOnlyList<SourceHealthReportRow> ApplyFilters(IEnumerable<SourceHealthReportRow> sources)
    {
        if (!string.IsNullOrWhiteSpace(Buscar))
        {
            sources = sources.Where(source =>
                source.SourceName.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ||
                (source.Url?.Contains(Buscar, StringComparison.OrdinalIgnoreCase) ?? false) ||
                source.LastMessage.Contains(Buscar, StringComparison.OrdinalIgnoreCase));
        }

        if (Enum.TryParse<DataSourceKind>(Tipo, true, out var kind))
            sources = sources.Where(source => source.Kind == kind);

        sources = Estado?.Trim().ToLowerInvariant() switch
        {
            "sanas" => sources.Where(source => source.Status == DataSourceStatus.Healthy),
            "degradadas" => sources.Where(source => source.Status == DataSourceStatus.Degraded),
            "fallidas" => sources.Where(source => source.Status == DataSourceStatus.Failed),
            _ => sources
        };

        return sources.OrderBy(source => source.Kind).ThenBy(source => source.Status).ThenBy(source => source.SourceName).ToArray();
    }
}
