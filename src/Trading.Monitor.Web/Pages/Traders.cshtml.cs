using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;

namespace Trading.Monitor.Web.Pages;

public sealed class TradersModel(ITraderResearchRepository traderRepository, ILogger<TradersModel> logger) : PageModel
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public TraderResearchReport Report { get; private set; } = EmptyReport();

    public IReadOnlyList<string> Platforms { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Platform { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string TradeStatus { get; set; } = "todas";

    [BindProperty(SupportsGet = true)]
    public Guid? TraderId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool OnlyWithHistory { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading trader research page for platform {Platform}.", Platform);
        Report = await traderRepository.GetReportAsync(new TraderResearchFilter(Platform, Search, TradeStatus, TraderId, OnlyWithHistory), cancellationToken);
        Platforms = Report.Sources.Select(row => row.Platform).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row).ToArray();
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

    public string Price(decimal? value)
    {
        return value.HasValue ? Price(value.Value) : "-";
    }

    public string SignedClass(decimal? value)
    {
        if (!value.HasValue || value.Value == 0m)
            return "flat";

        return value.Value > 0m ? "gain" : "loss";
    }

    public string TradeStatusClass(string status)
    {
        return status switch
        {
            "Abierta" => "status-open",
            "Cerrada" => "status-win",
            _ => "status-muted"
        };
    }

    public string ReliabilityClass(decimal score)
    {
        return score switch
        {
            >= 75m => "gain",
            >= 55m => "flat",
            _ => "loss"
        };
    }

    public string SelectedTraderName()
    {
        return Report.SelectedTrader?.DisplayName ?? "todos los traders";
    }

    private static TraderResearchReport EmptyReport()
    {
        return new TraderResearchReport([], [], [], null, 0, 0, 0, 0, 0);
    }
}
