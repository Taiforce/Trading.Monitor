using System.Globalization;
using System.Net;
using System.Text;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Infrastructure.Notifications;

public static class AlertFormatter
{
    private static readonly CultureInfo CurrencyCulture = CultureInfo.GetCultureInfo("en-US");

    public static string ToPlainText(TradingOpportunity opportunity, OpportunityProjection projection)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{opportunity.Symbol} {opportunity.Side} - score {opportunity.Score}/100");
        builder.AppendLine($"Precio: {opportunity.LastPrice}");
        builder.AppendLine($"Entrada: {opportunity.EntryLower} - {opportunity.EntryUpper}");
        builder.AppendLine($"Stop: {opportunity.StopLoss}");
        builder.AppendLine($"TP1: {opportunity.TakeProfit1}");
        builder.AppendLine($"TP2: {opportunity.TakeProfit2}");
        builder.AppendLine($"R:R: 1:{opportunity.RiskReward}");
        builder.AppendLine($"Vigencia: {opportunity.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"Temporalidades: {string.Join(", ", opportunity.ConfirmingIntervals)}");
        builder.AppendLine();
        builder.AppendLine("Dolor de oportunidad:");
        builder.AppendLine($"- Si ponias {Money(projection.Capital)}, TP1 neto estimado: {Money(projection.NetProfitAtTakeProfit1)}.");
        builder.AppendLine($"- Si aguantabas hasta TP2: {Money(projection.NetProfitAtTakeProfit2)}.");
        builder.AppendLine($"- Si te pegaba el stop: {Money(projection.NetLossAtStop)}.");
        builder.AppendLine($"- Comisiones estimadas ida/vuelta: {Money(projection.EstimatedFees)}.");
        builder.AppendLine("La parte incomoda: estos numeros no gritan, solo te miran fijo.");
        builder.AppendLine();
        builder.AppendLine("Razones:");

        foreach (var reason in opportunity.Reasons)
            builder.AppendLine($"- {reason}");

        if (opportunity.Risks.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Riesgos:");

            foreach (var risk in opportunity.Risks)
                builder.AppendLine($"- {risk}");
        }

        if (opportunity.RelatedNews.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Noticias relacionadas:");

            foreach (var item in opportunity.RelatedNews)
                builder.AppendLine($"- [{item.Sentiment}] {item.Title} ({item.Source}) {item.Url}");
        }

        return builder.ToString();
    }

    public static string ToHtml(TradingOpportunity opportunity, OpportunityProjection projection)
    {
        var reasons = string.Join("", opportunity.Reasons.Select(reason => $"<li>{WebUtility.HtmlEncode(reason)}</li>"));
        var risks = string.Join("", opportunity.Risks.Select(risk => $"<li>{WebUtility.HtmlEncode(risk)}</li>"));

        var news = string.Join("",
            opportunity.RelatedNews.Select(item =>
                $"<li><strong>{WebUtility.HtmlEncode(item.Sentiment.ToString())}</strong> {WebUtility.HtmlEncode(item.Title)} <small>{WebUtility.HtmlEncode(item.Source)}</small></li>"));

        return $$"""
                 <h2>{{WebUtility.HtmlEncode(opportunity.Symbol)}} {{opportunity.Side}} - {{opportunity.Score}}/100</h2>
                 <table>
                     <tr><td><strong>Precio</strong></td><td>{{opportunity.LastPrice}}</td></tr>
                     <tr><td><strong>Entrada</strong></td><td>{{opportunity.EntryLower}} - {{opportunity.EntryUpper}}</td></tr>
                     <tr><td><strong>Stop</strong></td><td>{{opportunity.StopLoss}}</td></tr>
                     <tr><td><strong>TP1</strong></td><td>{{opportunity.TakeProfit1}}</td></tr>
                     <tr><td><strong>TP2</strong></td><td>{{opportunity.TakeProfit2}}</td></tr>
                     <tr><td><strong>R:R</strong></td><td>1:{{opportunity.RiskReward}}</td></tr>
                     <tr><td><strong>Vigencia UTC</strong></td><td>{{opportunity.ExpiresAt:yyyy-MM-dd HH:mm:ss}}</td></tr>
                     <tr><td><strong>Temporalidades</strong></td><td>{{WebUtility.HtmlEncode(string.Join(", ", opportunity.ConfirmingIntervals))}}</td></tr>
                 </table>
                 <h3>Dolor de oportunidad</h3>
                 <ul>
                     <li>Si ponias {{Money(projection.Capital)}}, TP1 neto estimado: <strong>{{Money(projection.NetProfitAtTakeProfit1)}}</strong>.</li>
                     <li>Si aguantabas hasta TP2: <strong>{{Money(projection.NetProfitAtTakeProfit2)}}</strong>.</li>
                     <li>Si te pegaba el stop: <strong>{{Money(projection.NetLossAtStop)}}</strong>.</li>
                     <li>Comisiones estimadas ida/vuelta: {{Money(projection.EstimatedFees)}}.</li>
                 </ul>
                 <p><em>La parte incomoda: estos numeros no gritan, solo te miran fijo.</em></p>
                 <h3>Razones</h3>
                 <ul>{{reasons}}</ul>
                 <h3>Riesgos</h3>
                 <ul>{{risks}}</ul>
                 <h3>Noticias relacionadas</h3>
                 <ul>{{news}}</ul>
                 """;
    }

    private static string Money(decimal value)
    {
        return value.ToString("C2", CurrencyCulture);
    }
}
