using System.Globalization;
using System.Net;
using System.Text;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public static class AlertFormatter
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public static string OriginLabel(SignalOriginKind origin)
    {
        return origin switch
        {
            SignalOriginKind.ExternalAi => "Ajenas (ensemble de estrategias publicas)",
            SignalOriginKind.Trader => "Traders (leaderboard real)",
            _ => "Propias (auto-aprendizaje)"
        };
    }

    public static string ToPlainText(TradingOpportunity opportunity, OpportunityProjection projection, TradeInstruction instruction)
    {
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);
        var builder = new StringBuilder();
        builder.AppendLine($"{instruction.ActionLabel} | {opportunity.Symbol} {signalType} | {opportunity.Score}/100");
        builder.AppendLine($"Fuente: {OriginLabel(opportunity.OriginKind)}");
        builder.AppendLine(instruction.EntryTiming);
        builder.AppendLine(instruction.ExitTiming);
        builder.AppendLine(instruction.ProfitReport);
        builder.AppendLine(instruction.RiskReport);
        builder.AppendLine(instruction.ManagementPlan);

        if (opportunity.Reasons.Count > 0)
            builder.AppendLine($"Motivo: {opportunity.Reasons[0]}");

        if (opportunity.Risks.Count > 0)
            builder.AppendLine($"Riesgo: {opportunity.Risks[0]}");

        return builder.ToString();
    }

    public static string ToHtml(TradingOpportunity opportunity, OpportunityProjection projection, TradeInstruction instruction)
    {
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);
        return $$"""
                 <h2>{{WebUtility.HtmlEncode(instruction.ActionLabel)}} | {{WebUtility.HtmlEncode(opportunity.Symbol)}} {{WebUtility.HtmlEncode(signalType)}}</h2>
                 <p><strong>{{WebUtility.HtmlEncode(instruction.ConvictionLabel)}}</strong> | {{opportunity.Score}}/100 | Fuente: {{WebUtility.HtmlEncode(OriginLabel(opportunity.OriginKind))}}</p>
                 <table>
                     <tr><td><strong>Entrar</strong></td><td>{{WebUtility.HtmlEncode(instruction.EntryTiming)}}</td></tr>
                     <tr><td><strong>Salir</strong></td><td>{{WebUtility.HtmlEncode(instruction.ExitTiming)}}</td></tr>
                     <tr><td><strong>Ganancia</strong></td><td>{{WebUtility.HtmlEncode(instruction.ProfitReport)}}</td></tr>
                     <tr><td><strong>Riesgo</strong></td><td>{{WebUtility.HtmlEncode(instruction.RiskReport)}}</td></tr>
                 </table>
                 <p>{{WebUtility.HtmlEncode(instruction.ManagementPlan)}}</p>
                 <p><strong>Motivo:</strong> {{(opportunity.Reasons.Count > 0 ? WebUtility.HtmlEncode(opportunity.Reasons[0]) : "Confluencia detectada")}}</p>
                 """;
    }

    public static string ToExitPlainText(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, TradeInstruction instruction)
    {
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);
        var builder = new StringBuilder();
        builder.AppendLine($"{instruction.ActionLabel} - {opportunity.Symbol} {signalType}");
        builder.AppendLine($"Fuente: {OriginLabel(opportunity.OriginKind)}");
        builder.AppendLine($"Salida: {exit.ExitPrice}");
        builder.AppendLine($"Resultado: {Money(realizedNetPnL)}");
        builder.AppendLine(instruction.ExitTiming);
        return builder.ToString();
    }

    public static string ToExitHtml(OpportunityReportRow opportunity, OpportunityExit exit, decimal realizedNetPnL, TradeInstruction instruction)
    {
        var signalType = SignalTypeDescriptor.Label(opportunity.Side);
        return $$"""
                 <h2>{{WebUtility.HtmlEncode(instruction.ActionLabel)}} - {{WebUtility.HtmlEncode(opportunity.Symbol)}} {{WebUtility.HtmlEncode(signalType)}}</h2>
                 <p><strong>{{WebUtility.HtmlEncode(instruction.ConvictionLabel)}}</strong> | Fuente: {{WebUtility.HtmlEncode(OriginLabel(opportunity.OriginKind))}}</p>
                 <table>
                     <tr><td><strong>Salida</strong></td><td>{{exit.ExitPrice}}</td></tr>
                     <tr><td><strong>Resultado neto estimado</strong></td><td>{{Money(realizedNetPnL)}}</td></tr>
                     <tr><td><strong>Capital del reporte</strong></td><td>{{Money(opportunity.Capital)}}</td></tr>
                     <tr><td><strong>Motivo</strong></td><td>{{WebUtility.HtmlEncode(exit.Reason)}}</td></tr>
                 </table>
                 <h3>Lectura</h3>
                 <ul>
                     <li>{{WebUtility.HtmlEncode(instruction.ExitTiming)}}</li>
                     <li>{{WebUtility.HtmlEncode(instruction.ManagementPlan)}}</li>
                 </ul>
                 """;
    }

    private static string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }
}
