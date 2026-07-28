using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Pages;

public sealed class LiveTradersModel(ITraderResearchRepository traderRepository, ILogger<LiveTradersModel> logger) : PageModel
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public TraderResearchReport Report { get; private set; } = EmptyReport();

    public IReadOnlyList<TraderTradeReportRow> OpenTrades { get; private set; } = [];

    public IReadOnlyList<string> Platforms { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string Mercado { get; set; } = MarketSymbolClassifier.CryptoMarket;

    [BindProperty(SupportsGet = true)]
    public string Platform { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; } = "";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Loading live trader operations for {Market}.", Mercado);
        Report = await traderRepository.GetReportAsync(new TraderResearchFilter(Mercado, Platform, Search, "abierta", null, false), cancellationToken);
        OpenTrades = Report.Trades
            .Where(row => string.Equals(row.Status, "Abierta", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.OpenedAt)
            .ToArray();
        Platforms = Report.Sources.Select(row => row.Platform).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(row => row).ToArray();
    }

    public string MarketLabel()
    {
        return MarketSymbolClassifier.MarketLabel(Mercado);
    }

    public string MarketRouteValue()
    {
        return MarketSymbolClassifier.NormalizeMarket(Mercado);
    }

    public string Price(decimal value)
    {
        return value switch { >= 1000m => value.ToString("N2", CurrencyCulture), >= 1m => value.ToString("N4", CurrencyCulture), _ => value.ToString("N8", CurrencyCulture) };
    }

    public string Quantity(decimal? value)
    {
        return value.HasValue ? value.Value.ToString("N8", CurrencyCulture) : "-";
    }

    public string Invariant(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public string Invariant(decimal? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    public string ChartIntervalFor(TraderTradeReportRow row)
    {
        var minutes = Math.Max(1, (DateTimeOffset.UtcNow - row.OpenedAt).TotalMinutes);
        return minutes switch
        {
            <= 30 => "1m",
            <= 240 => "5m",
            <= 2880 => "15m",
            <= 10080 => "1h",
            _ => "4h"
        };
    }

    public string SideLabel(MarketSide side)
    {
        return side == MarketSide.Long ? "Compra bajo - vende alto" : "Vende alto - compra bajo";
    }

    public string EntryVerb(MarketSide side)
    {
        return side == MarketSide.Long ? "Comprar" : "Vender";
    }

    public DateTimeOffset ReplayFrom(TraderTradeReportRow row)
    {
        return row.OpenedAt.Subtract(TimeSpan.FromHours(4));
    }

    private static TraderResearchReport EmptyReport()
    {
        return new TraderResearchReport([], [], [], null, 0, 0, 0, 0, 0);
    }
}
