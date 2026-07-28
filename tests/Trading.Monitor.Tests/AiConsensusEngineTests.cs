using Trading.Monitor.Application.Reporting;
using Trading.Monitor.Application.Services;
using Trading.Monitor.Domain;

namespace Trading.Monitor.Tests;

public sealed class AiConsensusEngineTests
{
    private readonly AiConsensusEngine _engine = new();

    [Fact]
    public void Evaluate_keeps_strong_net_positive_signal_actionable()
    {
        var row = BuildRow(
            score: 94,
            riskReward: 3.2m,
            netProfit: 62m,
            netLoss: -18m,
            intervals: "1m | 5m | 15m | 1h",
            reasons: "ruptura con volumen | ema alcista | noticia positiva | sentimiento social fuerte",
            risks: "spread normal");

        var result = _engine.Evaluate(row, [row]);

        Assert.False(result.HasVeto, string.Join(" | ", result.VetoReasons));
        Assert.True(result.CompositeScore >= 70);
        Assert.Contains(result.Models, model => model.Name == "Numerai");
    }

    [Fact]
    public void Evaluate_blocks_signal_that_does_not_pay_after_fees()
    {
        var row = BuildRow(
            score: 61,
            riskReward: 0.8m,
            netProfit: -2m,
            netLoss: -45m,
            intervals: "15m",
            reasons: "rechazo en resistencia | volumen debil",
            risks: "riesgo alto | perdida mayor que ganancia");

        var result = _engine.Evaluate(row, [row]);

        Assert.True(result.HasVeto);
        Assert.Contains(result.VetoReasons, reason => reason.Contains("comisiones", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Esperar", result.ConsensusLabel);
    }

    private static OpportunityReportRow BuildRow(
        int score,
        decimal riskReward,
        decimal netProfit,
        decimal netLoss,
        string intervals,
        string reasons,
        string risks)
    {
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var entryPrice = 100m;
        var quantity = 10m;

        return new OpportunityReportRow(
            Guid.NewGuid(),
            "ETHUSDT",
            MarketSide.Long,
            OpportunityStatus.Open,
            score,
            observedAt,
            observedAt.AddMinutes(30),
            null,
            101m,
            99.8m,
            100.2m,
            entryPrice,
            98m,
            106m,
            108m,
            null,
            1000m,
            quantity,
            2m,
            netProfit,
            netProfit * 1.4m,
            netLoss,
            5m,
            50m,
            105m,
            null,
            null,
            null,
            riskReward,
            intervals,
            reasons,
            risks);
    }
}
