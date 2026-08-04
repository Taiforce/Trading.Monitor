using Trading.Monitor.Application.Abstractions;
using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Services;

public sealed record SignalLearningDecision(bool Allow, int ScoreAdjustment, string Reason);

/// <summary>
/// The "Propias" self-learning gate: adjusts (or blocks) a signal based on how similar signals
/// have actually performed, and boosts it when the independent "Ajenas" ensemble and/or the real
/// "Traders" leaderboard source currently agree on the same symbol/side. This is what makes the
/// own engine "learn from everything" (its own history, the external ensemble, and real traders)
/// rather than only from its own past signals.
/// </summary>
public sealed class SelfLearningSignalPolicy
{
    private const int MinimumSampleSize = 5;
    private const decimal FavorableWinRate = 55m;
    private const decimal UnfavorableWinRate = 42m;

    public async Task<SignalLearningDecision> EvaluateAsync(IOpportunityRepository repository, TradingOpportunity opportunity, CancellationToken cancellationToken)
    {
        var history = await repository.GetSignalsAsync(1000m, cancellationToken);
        var patternDecision = EvaluatePattern(history, opportunity);

        if (!patternDecision.Allow)
            return patternDecision;

        var confirmationBoost = EvaluateCrossSourceConfirmation(history, opportunity);

        if (confirmationBoost is null)
            return patternDecision;

        return new SignalLearningDecision(true, patternDecision.ScoreAdjustment + confirmationBoost.ScoreAdjustment,
            string.IsNullOrEmpty(patternDecision.Reason) ? confirmationBoost.Reason : $"{patternDecision.Reason} {confirmationBoost.Reason}");
    }

    private static SignalLearningDecision EvaluatePattern(IReadOnlyList<OpportunityReportRow> history, TradingOpportunity opportunity)
    {
        var horizon = ResolveHorizon(opportunity.ObservedAt, opportunity.ExpiresAt);
        var similar = history
            .Where(row => row.Status != OpportunityStatus.Open)
            .Where(row => string.Equals(row.Symbol, opportunity.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Side == opportunity.Side)
            .Where(row => ResolveHorizon(row.ObservedAt, row.ExpiresAt) == horizon)
            .ToArray();

        if (similar.Length < MinimumSampleSize)
            return new SignalLearningDecision(true, 0, $"Aprendizaje propio: muestra pequena ({similar.Length}/{MinimumSampleSize}) para {horizon}; se permite sin ajuste.");

        var winners = similar.Count(row => row.RealizedNetPnL > 0m);
        var winRate = (decimal)winners / similar.Length * 100m;
        var net = similar.Sum(row => row.RealizedNetPnL ?? 0m);

        if (winRate < UnfavorableWinRate && net < 0m)
            return new SignalLearningDecision(false, 0, $"patron {horizon} con {similar.Length} cierres, win rate {winRate:N1}% y neto {net:C2}.");

        if (winRate >= FavorableWinRate && net > 0m)
            return new SignalLearningDecision(true, 2, $"Aprendizaje propio: patron {horizon} favorable; {similar.Length} cierres, win rate {winRate:N1}%, neto {net:C2}.");

        return new SignalLearningDecision(true, 0, $"Aprendizaje propio: patron {horizon} neutral; {similar.Length} cierres, win rate {winRate:N1}%, neto {net:C2}.");
    }

    /// <summary>
    /// Only applies to Own-AI signals: rewards agreement with the independent external ensemble
    /// and/or real trader positions on the same symbol/side, observed recently or still open.
    /// </summary>
    private static SignalLearningDecision? EvaluateCrossSourceConfirmation(IReadOnlyList<OpportunityReportRow> history, TradingOpportunity opportunity)
    {
        if (opportunity.OriginKind != SignalOriginKind.OwnAi)
            return null;

        var recentCutoff = DateTimeOffset.UtcNow.AddHours(-6);
        var confirmingOrigins = history
            .Where(row => row.OriginKind != SignalOriginKind.OwnAi)
            .Where(row => string.Equals(row.Symbol, opportunity.Symbol, StringComparison.OrdinalIgnoreCase))
            .Where(row => row.Side == opportunity.Side)
            .Where(row => row.Status == OpportunityStatus.Open || row.ObservedAt >= recentCutoff)
            .Select(row => row.OriginKind)
            .Distinct()
            .OrderBy(origin => origin)
            .ToArray();

        if (confirmingOrigins.Length == 0)
            return null;

        var boost = Math.Min(4, confirmingOrigins.Length * 2);
        var labels = string.Join(" y ", confirmingOrigins.Select(origin => origin == SignalOriginKind.ExternalAi ? "Ajenas" : "Traders"));

        return new SignalLearningDecision(true, boost, $"Confirmado por {labels}: fuentes independientes coinciden en el mismo simbolo/lado.");
    }

    private static string ResolveHorizon(DateTimeOffset observedAt, DateTimeOffset expiresAt)
    {
        var minutes = Math.Max(1, (expiresAt - observedAt).TotalMinutes);

        return minutes switch
        {
            <= 30 => "Rápida",
            <= 240 => "Intradía",
            <= 2880 => "Swing",
            <= 10080 => "Semanal",
            _ => "Mensual"
        };
    }
}
