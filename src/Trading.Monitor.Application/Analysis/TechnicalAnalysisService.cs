using Trading.Monitor.Domain;

namespace Trading.Monitor.Application.Analysis;

public sealed class TechnicalAnalysisService
{
    public TechnicalSnapshot CreateSnapshot(string symbol, string interval, IReadOnlyList<MarketCandle> candles)
    {
        if (candles.Count < 60)
            throw new ArgumentException("At least 60 candles are required for a technical snapshot.", nameof(candles));

        var closes = candles.Select(candle => candle.Close).ToArray();
        var highs = candles.Select(candle => candle.High).ToArray();
        var lows = candles.Select(candle => candle.Low).ToArray();
        var volumes = candles.Select(candle => candle.Volume).ToArray();
        var typicalPrices = candles.Select(candle => candle.TypicalPrice).ToArray();

        var ema9 = IndicatorCalculator.Ema(closes, 9)[^1];
        var ema20 = IndicatorCalculator.Ema(closes, 20)[^1];
        var ema50 = IndicatorCalculator.Ema(closes, 50)[^1];
        var ema200 = IndicatorCalculator.Ema(closes, 200)[^1];
        var macd = IndicatorCalculator.Macd(closes);
        var bollinger = IndicatorCalculator.Bollinger(closes);
        var atr = IndicatorCalculator.Atr(highs, lows, closes);
        var last = candles[^1];
        var lookback = candles.Take(candles.Count - 1).TakeLast(Math.Min(40, candles.Count - 1)).ToArray();
        var support = lookback.Length == 0 ? last.Low : lookback.Min(candle => candle.Low);
        var resistance = lookback.Length == 0 ? last.High : lookback.Max(candle => candle.High);
        var bias = ResolveBias(last.Close, ema9, ema20, ema50, ema200, macd.Histogram);

        return new TechnicalSnapshot(symbol, interval, last.CloseTime, last.Close, ema9, ema20, ema50, ema200, IndicatorCalculator.Rsi(closes), macd.Line,
            macd.Signal, macd.Histogram, bollinger.Upper, bollinger.Middle, bollinger.Lower, atr, IndicatorCalculator.Adx(highs, lows, closes), IndicatorCalculator.Vwap(typicalPrices, volumes),
            IndicatorCalculator.RelativeVolume(volumes), support, resistance, last.Close == 0m ? 0m : atr / last.Close * 100m, bias);
    }

    private static MarketBias ResolveBias(decimal price, decimal ema9, decimal ema20, decimal ema50, decimal ema200, decimal macdHistogram)
    {
        if (price > ema20 && ema9 > ema20 && ema20 > ema50 && price > ema200 && macdHistogram >= 0m)
            return MarketBias.Bullish;

        if (price < ema20 && ema9 < ema20 && ema20 < ema50 && price < ema200 && macdHistogram <= 0m)
            return MarketBias.Bearish;

        return MarketBias.Neutral;
    }
}