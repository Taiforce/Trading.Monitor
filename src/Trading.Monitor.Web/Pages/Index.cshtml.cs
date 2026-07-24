using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Web.Pages;

public sealed class IndexModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<IndexModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public string PainLine { get; private set; } = "";

    public int FailingSources => Report.SourceHealth.Count(source => source.FailureCount > 0);

    public int DegradedSources => Report.SourceHealth.Count(source => source.Status != Trading.Monitor.Domain.DataSourceStatus.Healthy);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading trading dashboard for hypothetical capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);
        PainLine = BuildPainLine(Report);
    }

    private static string BuildPainLine(DashboardReport report)
    {
        if (report.TotalSignals == 0)
            return "Todavia no hay nada que lamentar. El tablero esta esperando la primera oportunidad.";

        if (report.PotentialNetAtTakeProfit1 > 0m)
            return $"Si hubieras puesto {report.Capital:C2} en cada senal y salido en la ganancia objetivo, el mercado habria dejado {report.PotentialNetAtTakeProfit1:C2} mirandote desde la mesa.";

        if (report.PotentialLossAtStop < 0m)
            return $"La version honesta tambien duele: con {report.Capital:C2} por senal, la perdida maxima acumulada seria {report.PotentialLossAtStop:C2}.";

        return "No hay ventaja clara todavia. A veces no operar tambien es una posicion.";
    }
}
