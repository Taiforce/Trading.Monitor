using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public abstract class TradingPageModel(IOpportunityRepository opportunityRepository, IOptionsMonitor<ReportingOptions> reportingOptions) : PageModel
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly string[] SupportedTradingSymbols = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];

    [BindProperty(SupportsGet = true)]
    public decimal Capital { get; set; }

    public DashboardReport Report { get; protected set; } = EmptyReport(1000m);

    public decimal EstimatedFeePercentPerSide => reportingOptions.CurrentValue.EstimatedFeePercentPerSide;

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
            OpportunityStatus.HitTakeProfit1 => "Ganada",
            OpportunityStatus.HitTakeProfit2 => "Ganancia extra",
            OpportunityStatus.ManagedProfitExit => "Ganancia administrada",
            OpportunityStatus.HitStopLoss => "Perdida",
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
            OpportunityStatus.HitTakeProfit1 or OpportunityStatus.HitTakeProfit2 or OpportunityStatus.ManagedProfitExit => "status-win",
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

    public TradeCostBreakdown CostBreakdown(OpportunityReportRow row, decimal? exitPrice = null)
    {
        return TradeCostCalculator.Build(
            row.Side,
            row.Capital,
            row.EstimatedQuantity,
            row.EntryPrice,
            exitPrice ?? row.ExitPrice ?? row.TakeProfit1,
            reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
    }

    public string NetResultClass(TradeCostBreakdown breakdown)
    {
        if (breakdown.NetBenefit > 0.01m)
            return "result-green";

        if (breakdown.NetBenefit < -0.01m)
            return "result-red";

        return "result-yellow";
    }

    public string NetResultLabel(TradeCostBreakdown breakdown)
    {
        if (breakdown.NetBenefit > 0.01m)
            return "Ganancia despues de comisiones";

        if (breakdown.NetBenefit < -0.01m)
            return "Sin ganancias despues de comisiones";

        return "Ganancia nula despues de comisiones";
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
        return SignalTypeFormatter.Description(side);
    }

    public string BuyLowSellHighValue => SignalTypeFormatter.BuyLowSellHigh;

    public string SellHighBuyLowValue => SignalTypeFormatter.SellHighBuyLow;

    public string SignalTypeLabel(MarketSide side)
    {
        return SignalTypeFormatter.Label(side);
    }

    public string SymbolButtonLabel(string symbol)
    {
        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return symbol[..^4];

        if (symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return symbol[..^3];

        return symbol;
    }

    public string SignalTypeMeaning(MarketSide side)
    {
        return SignalTypeFormatter.Description(side);
    }

    public string SignalTypeRequirement(MarketSide side)
    {
        return SignalTypeFormatter.Requirement(side);
    }

    public string SignalTypeClass(MarketSide side)
    {
        return side == MarketSide.Long ? "signal-type-buy" : "signal-type-sell";
    }

    protected static bool MatchesSignalType(OpportunityReportRow row, string? signalType)
    {
        return SignalTypeFormatter.Matches(row.Side, signalType);
    }

    protected static int SignalTypePriority(OpportunityReportRow row)
    {
        return SignalTypeFormatter.Priority(row.Side);
    }

    protected static IReadOnlyList<string> BuildSymbolList(IEnumerable<string> symbols)
    {
        var configured = SupportedTradingSymbols
            .Concat(symbols.Where(symbol => !string.IsNullOrWhiteSpace(symbol)).Select(symbol => symbol.Trim().ToUpperInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured
            .OrderBy(symbol =>
            {
                var index = Array.FindIndex(SupportedTradingSymbols, configuredSymbol => string.Equals(configuredSymbol, symbol, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string StatusMeaning(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.Open => "Todavia necesita seguimiento.",
            OpportunityStatus.HitTakeProfit1 => "Alcanzo la ganancia objetivo.",
            OpportunityStatus.HitTakeProfit2 => "Alcanzo una ganancia mas alta.",
            OpportunityStatus.ManagedProfitExit => "El sistema detecto beneficio neto suficiente despues de comisiones.",
            OpportunityStatus.HitStopLoss => "La idea llego a la perdida maxima.",
            OpportunityStatus.Expired => "La ventana de oportunidad vencio.",
            OpportunityStatus.ManuallyClosed => "Fue cerrada manualmente.",
            _ => "Estado registrado por el sistema."
        };
    }

    public string HorizonFor(OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (row.ExpiresAt - row.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "Rapida",
            <= 240 => "Intradia",
            <= 2880 => "Swing",
            <= 10080 => "Semanal",
            _ => "Mensual"
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
