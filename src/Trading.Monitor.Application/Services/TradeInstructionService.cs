using System.Globalization;
using Trading.Monitor.Application.Configuration;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed class TradeInstructionService
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");
    private const int PreEntryLeadMinutes = 3;
    private readonly RiskOptions _riskOptions;

    public TradeInstructionService() : this(new RiskOptions()) { }

    public TradeInstructionService(RiskOptions riskOptions)
    {
        _riskOptions = riskOptions;
    }

    public TradeInstruction Create(TradingOpportunity opportunity, OpportunityProjection projection)
    {
        return CreateCore(
            opportunity.Symbol,
            opportunity.Side,
            OpportunityStatus.Open,
            opportunity.Score,
            opportunity.ObservedAt,
            opportunity.ExpiresAt,
            opportunity.EntryLower,
            opportunity.EntryUpper,
            projection.EntryPrice,
            opportunity.StopLoss,
            opportunity.TakeProfit1,
            opportunity.TakeProfit2,
            projection.Capital,
            projection.NetProfitAtTakeProfit1,
            projection.NetProfitAtTakeProfit2,
            projection.NetLossAtStop,
            opportunity.RiskReward,
            opportunity.ConfirmingIntervals.Count,
            opportunity.Risks.Count,
            null);
    }

    public TradeInstruction Create(OpportunityReportRow row)
    {
        return CreateCore(
            row.Symbol,
            row.Side,
            row.Status,
            row.Score,
            row.ObservedAt,
            row.ExpiresAt,
            row.EntryLower,
            row.EntryUpper,
            row.EntryPrice,
            row.StopLoss,
            row.TakeProfit1,
            row.TakeProfit2,
            row.Capital,
            row.NetProfitAtTakeProfit1,
            row.NetProfitAtTakeProfit2,
            row.NetLossAtStop,
            row.RiskReward,
            SplitNotes(row.ConfirmingIntervals).Count,
            SplitNotes(row.Risks).Count,
            row.RealizedNetPnL);
    }

    public TradeInstruction CreateExit(OpportunityReportRow row, OpportunityExit exit, decimal realizedNetPnL)
    {
        var action = exit.Status switch
        {
            OpportunityStatus.ManagedProfitExit => row.Side == MarketSide.Long ? "VENDER AHORA: ganancia neta" : "COMPRAR AHORA: ganancia neta",
            OpportunityStatus.HitTakeProfit2 => "SALIR: ganancia extra alcanzada",
            OpportunityStatus.HitTakeProfit1 => "SALIR: ganancia objetivo alcanzada",
            OpportunityStatus.HitStopLoss => "SALIR: pérdida máxima tocada",
            OpportunityStatus.Expired => "NO ENTRAR: señal vencida",
            _ => "SALIDA ACTUALIZADA"
        };

        var cssClass = realizedNetPnL > 0m ? "signal-prime" : realizedNetPnL < 0m ? "signal-danger" : "signal-watch";
        var conviction = realizedNetPnL > 0m ? "Operación cerrada con ganancia" : realizedNetPnL < 0m ? "Operación cerrada con pérdida" : "Operación cerrada plana";

        return new TradeInstruction(
            action,
            conviction,
            cssClass,
            realizedNetPnL > 0m,
            $"La señal de entrada ya no debe abrirse. Precio de salida registrado: {FormatPrice(exit.ExitPrice)}.",
            ResolveExitMeaning(exit.Status),
            $"Resultado neto estimado para {Money(row.Capital)}: {Money(realizedNetPnL)}.",
            $"Comisiones estimadas incluidas: {Money(row.EstimatedFees)}.",
            "Registra si respetaste la salida. El aprendizaje real viene de comparar la alerta contra tu ejecucion.",
            exit.Reason);
    }

    private TradeInstruction CreateCore(
        string symbol,
        MarketSide side,
        OpportunityStatus status,
        int score,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        decimal entryLower,
        decimal entryUpper,
        decimal entryPrice,
        decimal stopLoss,
        decimal takeProfit1,
        decimal takeProfit2,
        decimal capital,
        decimal netProfitAtTakeProfit1,
        decimal netProfitAtTakeProfit2,
        decimal netLossAtStop,
        decimal riskReward,
        int confirmingIntervals,
        int riskCount,
        decimal? realizedNetPnL)
    {
        if (status != OpportunityStatus.Open)
        {
            var closedLabel = status switch
            {
                OpportunityStatus.HitTakeProfit2 => "CERRADA CON GANANCIA EXTRA",
                OpportunityStatus.HitTakeProfit1 => "CERRADA CON GANANCIA",
                OpportunityStatus.ManagedProfitExit => "CERRADA POR SALIDA ADMINISTRADA",
                OpportunityStatus.HitStopLoss => "CERRADA CON PERDIDA",
                OpportunityStatus.Expired => "VENCIDA",
                _ => "CERRADA"
            };

            var closedClass = realizedNetPnL > 0m ? "signal-prime" : realizedNetPnL < 0m ? "signal-danger" : "signal-watch";

            return new TradeInstruction(
                closedLabel,
                "Historial",
                closedClass,
                false,
                "No entrar. Ya paso.",
                realizedNetPnL.HasValue ? $"Cierre: {Money(realizedNetPnL.Value)}." : "Cierre pendiente.",
                $"Plan original: ganar {Money(netProfitAtTakeProfit1)}, ganar más {Money(netProfitAtTakeProfit2)}.",
                $"Pérdida máxima original: {Money(netLossAtStop)}.",
                "Usar solo para revisar resultado.",
                "Historial medido.");
        }

        var managedProfitExit = _riskOptions.ManagedProfitExitEnabled;
        var expired = !managedProfitExit && DateTimeOffset.UtcNow > expiresAt;
        if (expired)
        {
            return new TradeInstruction(
                "NO ENTRAR",
                "Vencida",
                "signal-danger",
                false,
                $"Vencio {expiresAt.ToLocalTime():HH:mm}. Esperar otra.",
                "No perseguir precio.",
                "Ganancia ya no aplica.",
                "Entrar tarde rompe el riesgo.",
                "Descartar.",
                "Señal vencida.");
        }

        var tp1Percent = PercentOfCapital(netProfitAtTakeProfit1, capital);
        var stopPercent = Math.Abs(PercentOfCapital(netLossAtStop, capital));
        var highConviction = score >= 90
                             && riskReward >= 2m
                             && confirmingIntervals >= 3
                             && riskCount <= 2
                             && tp1Percent >= 0.35m
                             && stopPercent <= 2.75m;

        var preEntryUntil = observedAt.AddMinutes(PreEntryLeadMinutes);
        var maxLifeMinutes = Math.Max(1, (int)Math.Ceiling((expiresAt - observedAt).TotalMinutes));
        var isInsideLeadWindow = DateTimeOffset.UtcNow <= preEntryUntil;
        var entryAction = SignalTypeDescriptor.EntryVerb(side);
        var exitAction = SignalTypeDescriptor.ExitVerb(side);
        var actionLabel = highConviction && isInsideLeadWindow
            ? (side == MarketSide.Long ? "COMPRAR AHORA" : "VENDER AHORA")
            : highConviction && managedProfitExit
                ? "POSICION VIVA"
                : highConviction ? "NO PERSEGUIR" : "VIGILAR";
        var convictionLabel = highConviction ? "Alta" : score >= 85 ? "Media" : "Baja";
        var cssClass = highConviction ? "signal-prime" : score >= 85 ? "signal-watch" : "signal-muted";

        if (managedProfitExit)
        {
            var minimumManagedProfit = Math.Max(0.01m, _riskOptions.ManagedProfitExitPercentAfterCosts);
            var minimumManagedProfitMoney = capital * minimumManagedProfit / 100m;
            var riskText = _riskOptions.ManagedHardStopExitEnabled
                ? $"Protección activa: si toca pérdida máxima {FormatPrice(stopLoss)}, el sistema puede cerrar."
                : "No se cierra por stop fijo en este modo; si va en contra queda viva y necesita seguimiento.";

            return new TradeInstruction(
                actionLabel,
                convictionLabel,
                cssClass,
                highConviction && isInsideLeadWindow,
                $"{PreEntryLeadMinutes} min: {entryAction} {FormatPrice(entryLower)}-{FormatPrice(entryUpper)}. Vence entrada {expiresAt.ToLocalTime():HH:mm}.",
                $"Salida administrada: cerrar {exitAction} cuando el neto sea >= {minimumManagedProfit:N2}% después de comisiones.",
                $"{Money(capital)} -> buscar mínimo {Money(minimumManagedProfitMoney)} neto antes de cerrar.",
                riskText,
                $"El sistema revisa la posición viva. Si supera {minimumManagedProfit:N2}% neto y luego detecta retroceso, la cierra en histórico y manda alerta.",
                $"{confirmingIntervals} tiempos alineados. El objetivo es vender con beneficio neto, no adivinar una hora fija.");
        }

        return new TradeInstruction(
            actionLabel,
            convictionLabel,
            cssClass,
            highConviction && isInsideLeadWindow,
            $"{PreEntryLeadMinutes} min: {entryAction} {FormatPrice(entryLower)}-{FormatPrice(entryUpper)}. Vence {expiresAt.ToLocalTime():HH:mm}.",
            $"Salir: {exitAction} en ganancia {FormatPrice(takeProfit1)} / ganancia extra {FormatPrice(takeProfit2)}. Cortar pérdida {FormatPrice(stopLoss)}.",
            $"{Money(capital)} -> ganar {Money(netProfitAtTakeProfit1)} | ganar más {Money(netProfitAtTakeProfit2)}.",
            $"Pérdida máxima {Money(netLossAtStop)} | R:B 1:{riskReward:N2}.",
            $"Vida max {maxLifeMinutes} min. Al llegar a ganancia, protege entrada {FormatPrice(entryPrice)}.",
            $"{confirmingIntervals} tiempos alineados. Sin garantia.");
    }

    private static string ResolveExitMeaning(OpportunityStatus status)
    {
        return status switch
        {
            OpportunityStatus.HitTakeProfit2 => "El movimiento alcanzó el objetivo extendido. La operación debería estar fuera.",
            OpportunityStatus.HitTakeProfit1 => "Ganancia objetivo tocada. Proteger ganancia.",
            OpportunityStatus.ManagedProfitExit => "Salida administrada: el sistema detectó beneficio neto suficiente después de comisiones.",
            OpportunityStatus.HitStopLoss => "Pérdida máxima tocada. Salir.",
            OpportunityStatus.Expired => "Venció sin tocar ganancia ni pérdida máxima.",
            _ => "Operación actualizada."
        };
    }

    private static IReadOnlyList<string> SplitNotes(string value)
    {
        return value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static decimal PercentOfCapital(decimal value, decimal capital)
    {
        return capital <= 0m ? 0m : value / capital * 100m;
    }

    private static string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }

    private static string FormatPrice(decimal value)
    {
        var decimals = Math.Abs(value) switch { >= 1000m => 2, >= 1m => 4, _ => 8 };
        return value.ToString($"N{decimals}", CurrencyCulture);
    }
}
