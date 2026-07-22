using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Web.Pages;

public sealed class ReportsModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions, ILogger<ReportsModel> logger)
    : TradingPageModel(opportunityRepository, reportingOptions)
{
    public decimal MaxSymbolValue { get; private set; }

    public SymbolReportRow? BestSymbol { get; private set; }

    public SymbolReportRow? WorstSymbol { get; private set; }

    public DailyReportRow? BestDay { get; private set; }

    public decimal AveragePotentialTp1 { get; private set; }

    public decimal AverageStopLoss { get; private set; }

    public string ExecutiveReadout { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading reports page for capital {Capital}.", Capital);
        await LoadReportAsync(cancellationToken);

        MaxSymbolValue = Report.SymbolBreakdown
            .Select(row => Math.Max(Math.Abs(row.PotentialNetAtTakeProfit1), Math.Abs(row.PotentialLossAtStop)))
            .DefaultIfEmpty(0m)
            .Max();

        BestSymbol = Report.SymbolBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        WorstSymbol = Report.SymbolBreakdown.OrderBy(row => row.RealizedNetPnL).FirstOrDefault();
        BestDay = Report.DailyBreakdown.OrderByDescending(row => row.RealizedNetPnL).FirstOrDefault();
        AveragePotentialTp1 = Report.TotalSignals == 0 ? 0m : Math.Round(Report.PotentialNetAtTakeProfit1 / Report.TotalSignals, 2);
        AverageStopLoss = Report.TotalSignals == 0 ? 0m : Math.Round(Report.PotentialLossAtStop / Report.TotalSignals, 2);
        ExecutiveReadout = BuildExecutiveReadout();
    }

    private string BuildExecutiveReadout()
    {
        if (Report.TotalSignals == 0)
            return "Todavia no hay suficiente historial. Primero deja que el worker acumule oportunidades.";

        if (Report.WinRate >= 55m && Report.RealizedNetPnL > 0m)
            return "El historial cerrado tiene ventaja positiva. La prioridad es repetir setups similares sin aumentar riesgo.";

        if (Report.OpenSignals > 0 && Report.ClosedSignals == 0)
            return "Hay oportunidades abiertas, pero aun no hay resultados cerrados. Decide solo con entrada, stop y tamano de posicion claros.";

        if (Report.PotentialLossAtStop < 0m && Report.PotentialNetAtTakeProfit1 <= Math.Abs(Report.PotentialLossAtStop))
            return "La recompensa potencial todavia no compensa el dano posible. Aqui el filtro debe ser mas exigente.";

        return "El sistema esta generando datos utiles, pero la muestra aun necesita mas cierres para juzgar calidad real.";
    }
}
