using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public abstract class TradingPageModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions) : PageModel
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    [BindProperty(SupportsGet = true)]
    public decimal Capital { get; set; }

    public DashboardReport Report { get; protected set; } = EmptyReport(1000m);

    protected async Task LoadReportAsync(CancellationToken cancellationToken)
    {
        if (Capital <= 0m)
            Capital = reportingOptions.CurrentValue.DefaultCapital;

        Report = await opportunityRepository.GetDashboardReportAsync(Capital, cancellationToken);
    }

    public string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }

    public string Money(decimal? value)
    {
        return value.HasValue ? Money(value.Value) : "-";
    }

    public string Price(decimal value)
    {
        return value switch { >= 1000m => value.ToString("N2", CurrencyCulture), >= 1m => value.ToString("N4", CurrencyCulture), _ => value.ToString("N8", CurrencyCulture) };
    }

    public string SignedClass(decimal? value)
    {
        if (!value.HasValue || value.Value == 0m)
            return "flat";

        return value.Value > 0m ? "gain" : "loss";
    }

    public string StatusLabel(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.Open => "Abierta",
            OpportunityStatus.HitTakeProfit1 => "TP1",
            OpportunityStatus.HitTakeProfit2 => "TP2",
            OpportunityStatus.HitStopLoss => "Stop",
            OpportunityStatus.Expired => "Expirada",
            OpportunityStatus.ManuallyClosed => "Cerrada",
            _ => status.ToString()
        };
    }

    public string StatusClass(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.Open => "status-open",
            OpportunityStatus.HitTakeProfit1 or OpportunityStatus.HitTakeProfit2 => "status-win",
            OpportunityStatus.HitStopLoss => "status-loss",
            _ => "status-muted"
        };
    }

    public string SourceStatusClass(DataSourceStatus status)
    {
        return status switch
        {
            DataSourceStatus.Healthy => "status-win",
            DataSourceStatus.Degraded => "status-muted",
            DataSourceStatus.Failed => "status-loss",
            _ => "status-muted"
        };
    }

    public string SentimentClass(NewsSentiment sentiment)
    {
        return sentiment switch
        {
            NewsSentiment.Positive => "gain",
            NewsSentiment.Negative => "loss",
            _ => "flat"
        };
    }

    public decimal BarWidth(decimal value, decimal max)
    {
        if (max <= 0m)
            return 0m;

        return Math.Clamp(Math.Abs(value) / max * 100m, 4m, 100m);
    }

    public decimal PercentOfCapital(decimal value)
    {
        if (Report.Capital <= 0m)
            return 0m;

        return Math.Round(value / Report.Capital * 100m, 2);
    }

    public decimal PriceMovePercent(decimal from, decimal to)
    {
        if (from <= 0m)
            return 0m;

        return Math.Round((to - from) / from * 100m, 2);
    }

    public string ScoreLabel(int score)
    {
        return score switch
        {
            >= 90 => "Alta confianza",
            >= 80 => "Buena confluencia",
            >= 70 => "Vigilable",
            _ => "Debil"
        };
    }

    public string SideMeaning(MarketSide side)
    {
        return side == MarketSide.Long ? "Busca ganar si el precio sube." : "Busca ganar si el precio baja.";
    }

    public string StatusMeaning(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.Open => "Todavia necesita seguimiento.",
            OpportunityStatus.HitTakeProfit1 => "Alcanzo el primer objetivo.",
            OpportunityStatus.HitTakeProfit2 => "Alcanzo el objetivo extendido.",
            OpportunityStatus.HitStopLoss => "La idea quedo invalidada por stop.",
            OpportunityStatus.Expired => "La ventana de oportunidad vencio.",
            OpportunityStatus.ManuallyClosed => "Fue cerrada manualmente.",
            _ => "Estado registrado por el sistema."
        };
    }

    public IReadOnlyList<string> SplitNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return [];

        return notes.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static DashboardReport EmptyReport(decimal capital)
    {
        return new DashboardReport(capital, 0, 0, 0, 0, 0, 0m, 0m, 0m, 0m,
            0m, 0m, [], [], [], [], []);
    }
}
