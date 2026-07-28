using System.Globalization;
using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Web.Services;

public sealed class LiveOperationsSnapshotService(
    IOpportunityRepository opportunityRepository,
    TradeInstructionService instructionService,
    IWalletRepository walletRepository,
    Microsoft.Extensions.Options.IOptionsMonitor<ReportingOptions> reportingOptions)
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private const int PreEntryLeadMinutes = 3;
    private static readonly TradeInstructionService ClassicInstructionService = new(new RiskOptions { ManagedProfitExitEnabled = false });

    public async Task<LiveOperationsSnapshot> GetAsync(decimal? capital, string? estado, string? symbol, string? tipoSenal, string? mode, string? selectedSignalId, CancellationToken cancellationToken)
    {
        var resolvedCapital = capital.GetValueOrDefault();
        if (resolvedCapital <= 0m)
            resolvedCapital = reportingOptions.CurrentValue.DefaultCapital;

        var report = await opportunityRepository.GetDashboardReportAsync(resolvedCapital, cancellationToken);
        var wallet = await walletRepository.GetSnapshotAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var resolvedInstructionService = IsClassicMode(mode) ? ClassicInstructionService : instructionService;

        var filteredRows = ApplyFilters(report.RecentSignals, estado, symbol, tipoSenal)
            .Where(row => WalletSignalPolicy.CanShowSignal(row, wallet))
            .ToArray();
        var selectedRow = ResolveSelectedRow(filteredRows, selectedSignalId);
        var operations = filteredRows
            .OrderByDescending(row => row.Status == OpportunityStatus.Open)
            .ThenBy(row => SignalTypeFormatter.Priority(row.Side))
            .ThenByDescending(row => resolvedInstructionService.Create(row).Highlight)
            .ThenByDescending(row => row.ObservedAt)
            .Take(18)
            .Append(selectedRow)
            .Where(row => row is not null)
            .DistinctBy(row => row!.Id)
            .Select(row => ToDto(row!, now, resolvedInstructionService, mode))
            .ToArray();

        return new LiveOperationsSnapshot(now, operations.Count(row => row.Status == "Abierta"), operations.Count(row => row.Highlight), operations);
    }

    private LiveOperationDto ToDto(Application.Reporting.OpportunityReportRow row, DateTimeOffset now, TradeInstructionService resolvedInstructionService, string? mode)
    {
        var instruction = resolvedInstructionService.Create(row);
        var secondsRemaining = row.Status == OpportunityStatus.Open ? Math.Max(0, (int)Math.Round((row.ExpiresAt - now).TotalSeconds)) : 0;
        var preEntryUntil = row.ObservedAt.AddMinutes(PreEntryLeadMinutes);
        var preEntrySecondsRemaining = row.Status == OpportunityStatus.Open ? Math.Max(0, (int)Math.Round((preEntryUntil - now).TotalSeconds)) : 0;
        var maxLifeMinutes = Math.Max(1, (int)Math.Ceiling((row.ExpiresAt - row.ObservedAt).TotalMinutes));
        var timeText = row.Status == OpportunityStatus.Open
            ? $"{FormatDuration(secondsRemaining)} viva | entrada {FormatDuration(preEntrySecondsRemaining)}"
            : row.ExitTime.HasValue ? $"Cerro {row.ExitTime.Value.ToLocalTime():HH:mm}" : "Cerrada";
        var markPrice = row.ExitPrice ?? row.LastPrice;
        var breakdown = TradeCostCalculator.Build(
            row.Side,
            row.Capital,
            row.EstimatedQuantity,
            row.EntryPrice,
            markPrice,
            reportingOptions.CurrentValue.EstimatedFeePercentPerSide);
        var conversion = TradeConversionCalculator.Build(
            row.Symbol,
            row.Side,
            row.Capital,
            row.EstimatedQuantity,
            row.EntryPrice,
            row.ExitPrice,
            row.Status == OpportunityStatus.Open ? null : row.ExitPrice,
            row.RealizedNetPnL,
            breakdown.TotalFees);
        var costText = $"Comisiones: entrada {Money(breakdown.EntryFee)} | salida {Money(breakdown.ExitFee)} | total {Money(breakdown.TotalFees)}";

        return new LiveOperationDto(
            row.Id.ToString("N"),
            row.Symbol,
            row.Side.ToString(),
            SignalTypeFormatter.Value(row.Side),
            SignalTypeFormatter.Label(row.Side),
            SignalTypeFormatter.Description(row.Side),
            row.Side == MarketSide.Long,
            HorizonFor(row),
            StatusLabel(row.Status),
            instruction.ActionLabel,
            instruction.ConvictionLabel,
            instruction.CssClass,
            instruction.Highlight,
            row.Score,
            row.ObservedAt,
            row.ExpiresAt,
            row.ExitTime,
            row.ObservedAt,
            preEntryUntil,
            row.ExpiresAt,
            secondsRemaining,
            preEntrySecondsRemaining,
            maxLifeMinutes,
            row.LastPrice,
            row.EntryLower,
            row.EntryUpper,
            row.EntryPrice,
            row.StopLoss,
            row.TakeProfit1,
            row.TakeProfit2,
            row.ExitPrice,
            row.Capital,
            row.EstimatedQuantity,
            breakdown.TotalFees,
            row.ManagedTargetNetPercent,
            row.ManagedTargetNetPnL,
            row.ManagedTargetExitPrice,
            markPrice,
            reportingOptions.CurrentValue.EstimatedFeePercentPerSide,
            breakdown.EntryFee,
            breakdown.ExitFee,
            breakdown.NetBenefit,
            breakdown.NetPercent,
            breakdown.TotalObtained,
            instruction.EntryTiming,
            instruction.ExitTiming,
            instruction.ProfitReport,
            instruction.RiskReport,
            timeText,
            Money(row.NetProfitAtTakeProfit1),
            Money(row.NetProfitAtTakeProfit2),
            Money(row.NetLossAtStop),
            RealizedText(row),
            RealizedPercent(row),
            row.RealizedNetPercent,
            row.RealizedTotalObtained,
            EntryExitText(row),
            QuantityText(row),
            RealizedFormulaText(row),
            conversion.DetailText,
            conversion.EntryText,
            conversion.ExitText,
            conversion.ResultText,
            costText,
            conversion.BreakEvenText,
            BuildLinks(row, mode));
    }

    private static bool IsClassicMode(string? mode)
    {
        return string.Equals(mode, "classic", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Application.Reporting.OpportunityReportRow> ApplyFilters(IEnumerable<Application.Reporting.OpportunityReportRow> rows, string? estado, string? symbol, string? tipoSenal)
    {
        if (!string.IsNullOrWhiteSpace(symbol))
            rows = rows.Where(row => string.Equals(row.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase));

        rows = rows.Where(row => SignalTypeFormatter.Matches(row.Side, tipoSenal));

        return estado?.Trim().ToLowerInvariant() switch
        {
            "cerradas" => rows.Where(row => row.Status != OpportunityStatus.Open),
            "todas" => rows,
            _ => rows.Where(row => row.Status == OpportunityStatus.Open)
        };
    }

    private static Application.Reporting.OpportunityReportRow? ResolveSelectedRow(IEnumerable<Application.Reporting.OpportunityReportRow> rows, string? selectedSignalId)
    {
        var normalizedId = NormalizeSignalId(selectedSignalId);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return null;

        return rows.FirstOrDefault(row => string.Equals(row.Id.ToString("N"), normalizedId, StringComparison.OrdinalIgnoreCase));
    }

    private static string StatusLabel(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.Open => "Abierta",
            OpportunityStatus.HitTakeProfit1 => "Ganada",
            OpportunityStatus.HitTakeProfit2 => "Ganancia extra",
            OpportunityStatus.ManagedProfitExit => "Ganancia administrada",
            OpportunityStatus.HitStopLoss => "Pérdida",
            OpportunityStatus.Expired => "Vencida",
            OpportunityStatus.ManuallyClosed => "Cerrada",
            _ => status.ToString()
        };
    }

    private static string HorizonFor(Application.Reporting.OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (row.ExpiresAt - row.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "Rápida",
            <= 240 => "Intradía",
            <= 2880 => "Swing",
            <= 10080 => "Semanal",
            _ => "Mensual"
        };
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds <= 0)
            return "0m";

        var minutes = seconds / 60;
        var remainder = seconds % 60;
        return minutes <= 0 ? $"{remainder}s" : $"{minutes}m {remainder:00}s";
    }

    private static string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }

    private static string RealizedText(Application.Reporting.OpportunityReportRow row)
    {
        return row.RealizedNetPnL.HasValue ? Money(row.RealizedNetPnL.Value) : "Abierta";
    }

    private static string RealizedPercent(Application.Reporting.OpportunityReportRow row)
    {
        if (row.RealizedNetPercent.HasValue)
            return $"{row.RealizedNetPercent.Value:N2}%";

        if (!row.RealizedNetPnL.HasValue || row.Capital <= 0m)
            return "-";

        return $"{row.RealizedNetPnL.Value / row.Capital * 100m:N2}%";
    }

    private static string EntryExitText(Application.Reporting.OpportunityReportRow row)
    {
        return row.ExitPrice.HasValue
            ? $"{FormatPrice(row.EntryPrice)} -> {FormatPrice(row.ExitPrice.Value)}"
            : $"{FormatPrice(row.EntryPrice)} -> pendiente";
    }

    private static string QuantityText(Application.Reporting.OpportunityReportRow row)
    {
        return $"{row.EstimatedQuantity:N8} {MapAsset(row.Symbol)}";
    }

    private static string RealizedFormulaText(Application.Reporting.OpportunityReportRow row)
    {
        var side = SignalTypeFormatter.Label(row.Side);

        if (!row.ExitPrice.HasValue || !row.RealizedNetPnL.HasValue)
            return $"{Money(row.Capital)} / {FormatPrice(row.EntryPrice)} = {QuantityText(row)}. Cierre pendiente.";

        return $"{side}: {Money(row.Capital)} / {FormatPrice(row.EntryPrice)} = {QuantityText(row)}; salida {FormatPrice(row.ExitPrice.Value)}; neto {Money(row.RealizedNetPnL.Value)}.";
    }

    private static string FormatPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return value.ToString($"N{decimals}", CurrencyCulture);
    }

    private static IReadOnlyList<TradeLinkDto> BuildLinks(Application.Reporting.OpportunityReportRow row, string? mode)
    {
        var symbol = row.Symbol;
        var asset = MapAsset(symbol);
        var quote = symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) ? "USDT" : "USD";
        var coinbaseProduct = $"{asset}-USD";
        var binancePair = $"{asset}_{quote}";
        var page = IsClassicMode(mode) ? "/acciones" : "/posiciones";
        var internalUrl = $"{page}?Capital={Uri.EscapeDataString(row.Capital.ToString(CultureInfo.InvariantCulture))}&Estado=todas&Symbol={Uri.EscapeDataString(row.Symbol)}&TipoSenal={Uri.EscapeDataString(SignalTypeFormatter.Value(row.Side))}&senal={row.Id:N}&interval={Uri.EscapeDataString(DefaultIntervalFor(row))}";

        return
        [
            new TradeLinkDto("Abrir señal", internalUrl),
            new TradeLinkDto("Binance", $"https://www.binance.com/en/trade/{binancePair}?type=spot"),
            new TradeLinkDto("TradingView", $"https://www.tradingview.com/chart/?symbol=BINANCE:{asset}{quote}"),
            new TradeLinkDto("Coinbase", $"https://advanced.coinbase.com/trade/{coinbaseProduct}"),
            new TradeLinkDto("Kraken", $"https://pro.kraken.com/app/trade/{asset}-USD")
        ];
    }

    private static string DefaultIntervalFor(Application.Reporting.OpportunityReportRow row)
    {
        var minutes = Math.Max(1, (row.ExpiresAt - row.ObservedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "1m",
            <= 240 => "5m",
            <= 2880 => "15m",
            <= 10080 => "1h",
            <= 43200 => "4h",
            _ => "1d"
        };
    }

    private static string NormalizeSignalId(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string MapAsset(string symbol)
    {
        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return symbol[..^4].ToUpperInvariant();

        if (symbol.EndsWith("USD", StringComparison.OrdinalIgnoreCase))
            return symbol[..^3].ToUpperInvariant();

        return symbol.ToUpperInvariant();
    }
}
