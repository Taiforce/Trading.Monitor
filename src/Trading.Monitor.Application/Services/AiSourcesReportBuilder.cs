using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

/// <summary>
/// Builds the "Propias / Ajenas / Traders" comparison report purely from already-loaded
/// <see cref="OpportunityReportRow"/> data (no direct DB access), the same pattern used by
/// <see cref="AiConsensusEngine"/> and <see cref="VirtualPortfolioSimulator"/>.
/// </summary>
public static class AiSourcesReportBuilder
{
    public static AiSourcesReport Build(IReadOnlyList<OpportunityReportRow> rows)
    {
        var own = BuildStats(rows, SignalOriginKind.OwnAi, "Propias",
            "Motor propio del sistema: analisis tecnico multi-temporal + auto-aprendizaje que ajusta la confianza segun resultados reales.");
        var external = BuildStats(rows, SignalOriginKind.ExternalAi, "Ajenas",
            "Ensemble independiente de estrategias publicas conocidas (Ichimoku, canal de volatilidad estilo Supertrend, reversion Bollinger).");
        var trader = BuildStats(rows, SignalOriginKind.Trader, "Traders",
            "Posiciones reales, actualmente abiertas, de los traders mejor rankeados en el leaderboard publico de Binance Futures.");

        return new AiSourcesReport(own, external, trader, BuildSummary(own, external, trader));
    }

    private static AiSourceStats BuildStats(IReadOnlyList<OpportunityReportRow> rows, SignalOriginKind origin, string label, string description)
    {
        var filtered = rows.Where(row => row.OriginKind == origin).OrderByDescending(row => row.ObservedAt).ToArray();
        var closed = filtered.Where(row => row.Status != OpportunityStatus.Open).ToArray();
        var winners = closed.Count(row => row.RealizedNetPnL > 0m);
        var losers = closed.Count(row => row.RealizedNetPnL < 0m);
        var winRate = closed.Length == 0 ? 0m : Math.Round((decimal)winners / closed.Length * 100m, 2);
        var net = Math.Round(closed.Sum(row => row.RealizedNetPnL ?? 0m), 2);
        var averageScore = filtered.Length == 0 ? 0m : Math.Round(filtered.Average(row => (decimal)row.Score), 1);
        var averageRiskReward = filtered.Length == 0 ? 0m : Math.Round(filtered.Average(row => row.RiskReward), 2);

        var bySymbol = closed.GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var symbolWinRate = group.Count() == 0 ? 0m : Math.Round((decimal)group.Count(row => row.RealizedNetPnL > 0m) / group.Count() * 100m, 1);
                return new AiSourceSymbolBreakdown(group.Key, group.Count(), group.Count(), symbolWinRate, Math.Round(group.Sum(row => row.RealizedNetPnL ?? 0m), 2));
            })
            .OrderByDescending(row => row.NetPnL)
            .ToArray();

        var notes = BuildLearningNotes(closed, winRate, bySymbol);

        return new AiSourceStats(origin, label, description, filtered.Length, filtered.Length - closed.Length, closed.Length, winners, losers, winRate, net, averageScore, averageRiskReward, bySymbol,
            filtered.Take(15).ToArray(), notes);
    }

    private static IReadOnlyList<string> BuildLearningNotes(OpportunityReportRow[] closed, decimal winRate, AiSourceSymbolBreakdown[] bySymbol)
    {
        var notes = new List<string>();

        if (closed.Length < 5)
        {
            notes.Add($"Todavia hay pocos cierres ({closed.Length}) para sacar conclusiones solidas de esta fuente; el sistema sigue observando antes de ajustar su confianza.");
            return notes;
        }

        notes.Add(winRate switch
        {
            >= 55m => $"Win rate {winRate:N1}% en {closed.Length} cierres: patron favorable. Esta fuente esta ganando peso en las decisiones combinadas.",
            < 42m => $"Win rate {winRate:N1}% en {closed.Length} cierres: patron desfavorable. El auto-aprendizaje reduce o bloquea senales similares de esta fuente.",
            _ => $"Win rate {winRate:N1}% en {closed.Length} cierres: patron neutral, sin ajuste de confianza todavia."
        });

        if (bySymbol.Length > 0)
        {
            var best = bySymbol[0];
            notes.Add($"Mejor simbolo: {best.Symbol} ({best.TotalSignals} cierres, neto {best.NetPnL:N2}).");

            var worst = bySymbol[^1];
            if (!string.Equals(worst.Symbol, best.Symbol, StringComparison.OrdinalIgnoreCase))
                notes.Add($"Peor simbolo: {worst.Symbol} ({worst.TotalSignals} cierres, neto {worst.NetPnL:N2}).");
        }

        return notes;
    }

    private static string BuildSummary(AiSourceStats own, AiSourceStats external, AiSourceStats trader)
    {
        var withHistory = new[] { own, external, trader }.Where(source => source.ClosedSignals >= 5).OrderByDescending(source => source.WinRatePercent).ToArray();

        if (withHistory.Length == 0)
            return "Todavia no hay suficientes cierres en ninguna de las 3 fuentes (Propias/Ajenas/Traders) para comparar su desempeno real.";

        var best = withHistory[0];
        return $"Con los datos actuales, la fuente mas confiable es \"{best.Label}\" ({best.WinRatePercent:N1}% de acierto en {best.ClosedSignals} cierres, neto {best.NetPnL:N2}).";
    }
}
