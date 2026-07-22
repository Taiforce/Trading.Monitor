using Trading.Monitor.Application.Analysis;

namespace Trading.Monitor.Tests;

public class IndicatorCalculatorTests
{
    [Fact]
    public void Rsi_ReportsStrengthForRisingSeries()
    {
        var closes = Enumerable.Range(1, 60).Select(value => (decimal)value).ToArray();

        var rsi = IndicatorCalculator.Rsi(closes);

        Assert.True(rsi > 70m);
    }

    [Fact]
    public void Rsi_ReportsWeaknessForFallingSeries()
    {
        var closes = Enumerable.Range(1, 60).Reverse().Select(value => (decimal)value).ToArray();

        var rsi = IndicatorCalculator.Rsi(closes);

        Assert.True(rsi < 30m);
    }

    [Fact]
    public void Ema_FollowsLatestPrices()
    {
        var closes = new[] { 10m, 11m, 12m, 13m, 14m, 15m };

        var ema = IndicatorCalculator.Ema(closes, 3);

        Assert.Equal(closes.Length, ema.Length);
        Assert.True(ema[^1] > ema[0]);
        Assert.True(ema[^1] < closes[^1]);
    }
}