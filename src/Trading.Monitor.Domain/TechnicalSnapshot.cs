namespace Trading.Monitor.Domain;

public sealed record TechnicalSnapshot(string Symbol, string Interval, DateTimeOffset ObservedAt, decimal LastPrice, decimal Ema9, decimal Ema20, decimal Ema50, decimal Ema200, decimal Rsi14, decimal MacdLine,
    decimal MacdSignal, decimal MacdHistogram, decimal BollingerUpper, decimal BollingerMiddle, decimal BollingerLower, decimal Atr14, decimal Adx14, decimal Vwap, decimal RelativeVolume, decimal RecentSupport,
    decimal RecentResistance, decimal AtrPercent, MarketBias Bias);