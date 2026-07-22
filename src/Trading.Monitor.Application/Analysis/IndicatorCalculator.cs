namespace Trading.Monitor.Application.Analysis;

public static class IndicatorCalculator
{
    public static decimal[] Ema(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0)
            return [];

        var result = new decimal[values.Count];
        var multiplier = 2m / (period + 1m);
        var ema = values[0];

        for (var i = 0; i < values.Count; i++)
        {
            ema = i == 0 ? values[i] : (values[i] - ema) * multiplier + ema;
            result[i] = ema;
        }

        return result;
    }

    public static decimal Sma(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0)
            return 0m;

        var sample = values.TakeLast(Math.Min(period, values.Count));
        return sample.Average();
    }

    public static decimal Rsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        if (closes.Count <= period)
            return 50m;

        var gains = 0m;
        var losses = 0m;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];

            if (change >= 0)
                gains += change;
            else
                losses -= change;
        }

        var averageGain = gains / period;
        var averageLoss = losses / period;

        for (var i = period + 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? -change : 0m;

            averageGain = (averageGain * (period - 1) + gain) / period;
            averageLoss = (averageLoss * (period - 1) + loss) / period;
        }

        if (averageLoss == 0m)
            return 100m;

        var relativeStrength = averageGain / averageLoss;
        return 100m - 100m / (1m + relativeStrength);
    }

    public static MacdValue Macd(IReadOnlyList<decimal> closes)
    {
        if (closes.Count == 0)
            return new MacdValue(0m, 0m, 0m);

        var ema12 = Ema(closes, 12);
        var ema26 = Ema(closes, 26);
        var macdLine = new decimal[closes.Count];

        for (var i = 0; i < closes.Count; i++)
            macdLine[i] = ema12[i] - ema26[i];

        var signal = Ema(macdLine, 9);
        var line = macdLine[^1];
        var signalValue = signal[^1];

        return new MacdValue(line, signalValue, line - signalValue);
    }

    public static BollingerBands Bollinger(IReadOnlyList<decimal> closes, int period = 20, decimal standardDeviations = 2m)
    {
        if (closes.Count == 0)
            return new BollingerBands(0m, 0m, 0m);

        var sample = closes.TakeLast(Math.Min(period, closes.Count)).ToArray();
        var middle = sample.Average();
        var variance = sample.Sum(value => Math.Pow((double)(value - middle), 2)) / sample.Length;
        var deviation = (decimal)Math.Sqrt(variance);

        return new BollingerBands(middle + deviation * standardDeviations, middle, middle - deviation * standardDeviations);
    }

    public static decimal Atr(IReadOnlyList<decimal> highs, IReadOnlyList<decimal> lows, IReadOnlyList<decimal> closes, int period = 14)
    {
        if (highs.Count < 2 || highs.Count != lows.Count || highs.Count != closes.Count)
            return 0m;

        var trueRanges = new List<decimal>(highs.Count - 1);

        for (var i = 1; i < highs.Count; i++)
        {
            var highLow = highs[i] - lows[i];
            var highClose = Math.Abs(highs[i] - closes[i - 1]);
            var lowClose = Math.Abs(lows[i] - closes[i - 1]);
            trueRanges.Add(Math.Max(highLow, Math.Max(highClose, lowClose)));
        }

        if (trueRanges.Count <= period)
            return trueRanges.Average();

        var atr = trueRanges.Take(period).Average();

        for (var i = period; i < trueRanges.Count; i++)
            atr = (atr * (period - 1) + trueRanges[i]) / period;

        return atr;
    }

    public static decimal Adx(IReadOnlyList<decimal> highs, IReadOnlyList<decimal> lows, IReadOnlyList<decimal> closes, int period = 14)
    {
        if (highs.Count < period * 2 || highs.Count != lows.Count || highs.Count != closes.Count)
            return 0m;

        var trueRanges = new List<decimal>();
        var plusDm = new List<decimal>();
        var minusDm = new List<decimal>();

        for (var i = 1; i < highs.Count; i++)
        {
            var upMove = highs[i] - highs[i - 1];
            var downMove = lows[i - 1] - lows[i];

            trueRanges.Add(Math.Max(highs[i] - lows[i], Math.Max(Math.Abs(highs[i] - closes[i - 1]), Math.Abs(lows[i] - closes[i - 1]))));
            plusDm.Add(upMove > downMove && upMove > 0m ? upMove : 0m);
            minusDm.Add(downMove > upMove && downMove > 0m ? downMove : 0m);
        }

        var smoothedTr = trueRanges.Take(period).Sum();
        var smoothedPlus = plusDm.Take(period).Sum();
        var smoothedMinus = minusDm.Take(period).Sum();
        var dxValues = new List<decimal>();

        for (var i = period; i < trueRanges.Count; i++)
        {
            smoothedTr = smoothedTr - smoothedTr / period + trueRanges[i];
            smoothedPlus = smoothedPlus - smoothedPlus / period + plusDm[i];
            smoothedMinus = smoothedMinus - smoothedMinus / period + minusDm[i];

            if (smoothedTr == 0m)
                continue;

            var plusDi = 100m * (smoothedPlus / smoothedTr);
            var minusDi = 100m * (smoothedMinus / smoothedTr);
            var denominator = plusDi + minusDi;

            if (denominator > 0m)
                dxValues.Add(100m * Math.Abs(plusDi - minusDi) / denominator);
        }

        return dxValues.Count == 0 ? 0m : dxValues.TakeLast(Math.Min(period, dxValues.Count)).Average();
    }

    public static decimal Vwap(IReadOnlyList<decimal> typicalPrices, IReadOnlyList<decimal> volumes, int period = 48)
    {
        if (typicalPrices.Count == 0 || typicalPrices.Count != volumes.Count)
            return 0m;

        var start = Math.Max(0, typicalPrices.Count - period);
        var numerator = 0m;
        var denominator = 0m;

        for (var i = start; i < typicalPrices.Count; i++)
        {
            numerator += typicalPrices[i] * volumes[i];
            denominator += volumes[i];
        }

        return denominator == 0m ? typicalPrices[^1] : numerator / denominator;
    }

    public static decimal RelativeVolume(IReadOnlyList<decimal> volumes, int period = 20)
    {
        if (volumes.Count < 2)
            return 1m;

        var prior = volumes.Take(volumes.Count - 1).TakeLast(Math.Min(period, volumes.Count - 1)).ToArray();
        var average = prior.Length == 0 ? 0m : prior.Average();

        return average == 0m ? 1m : volumes[^1] / average;
    }
}