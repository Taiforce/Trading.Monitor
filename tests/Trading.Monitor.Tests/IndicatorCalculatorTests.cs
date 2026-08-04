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

    [Fact]
    public void Ema_SeedsFromSimpleMovingAverageInsteadOfFirstValue()
    {
        var closes = new[] { 10m, 20m, 30m, 5m, 5m, 5m, 5m };

        var ema = IndicatorCalculator.Ema(closes, 3);

        // Standard EMA warm-up: the value at index (period - 1) must equal SMA(period).
        Assert.Equal((10m + 20m + 30m) / 3m, ema[2]);
    }

    [Fact]
    public void Adx_UsesWilderSmoothingNotPlainAverageOfLastValues()
    {
        var (highs, lows, closes) = BuildStrongUptrend(60);

        var adx = IndicatorCalculator.Adx(highs, lows, closes);

        // A clean, sustained one-directional trend should read as a strong trend (>25 is the
        // conventional Wilder ADX "trending" threshold).
        Assert.True(adx > 25m, $"Expected a strong-trend ADX reading, got {adx:F2}.");
    }

    [Fact]
    public void VolatilityChannelBreakout_DetectsBullishBreakoutAboveTheBand()
    {
        var (highs, lows, closes) = BuildStrongUptrend(40);

        var breakout = IndicatorCalculator.VolatilityChannelBreakout(highs, lows, closes, period: 10, multiplier: 1m);

        Assert.Equal(1, breakout);
    }

    [Fact]
    public void MidpointHighLow_ReturnsAverageOfHighestHighAndLowestLow()
    {
        decimal[] highs = [10m, 12m, 15m, 11m];
        decimal[] lows = [8m, 9m, 10m, 7m];

        var midpoint = IndicatorCalculator.MidpointHighLow(highs, lows, 4);

        Assert.Equal((15m + 7m) / 2m, midpoint);
    }

    private static (decimal[] Highs, decimal[] Lows, decimal[] Closes) BuildStrongUptrend(int count)
    {
        var highs = new decimal[count];
        var lows = new decimal[count];
        var closes = new decimal[count];
        var price = 100m;

        for (var i = 0; i < count; i++)
        {
            var open = price;
            var close = price + 2m;
            highs[i] = close + 0.5m;
            lows[i] = open - 0.5m;
            closes[i] = close;
            price = close;
        }

        return (highs, lows, closes);
    }
}