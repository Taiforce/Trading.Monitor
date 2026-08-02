using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Tests;

public sealed class OperationalLogInterpreterTests
{
    private readonly OperationalLogInterpreter _interpreter = new();

    [Fact]
    public void Interpret_ParsesSerilogFormatsAndGroupsContinuationLines()
    {
        var entries = _interpreter.Interpret(Snapshot(
            "2026-08-02 12:14:48.870 -06:00 [INF] New signal BTCUSDT Compra bajo - vende alto.",
            "   at Trading.Monitor.Worker.ExecuteAsync()",
            "[12:14:49 INF] Signal sent through console.",
            "2026-08-02 12:14:50.000 -06:00 [WRN] No XML encryptor configured."));

        Assert.Equal(3, entries.Count);
        Assert.Equal("12:14:48", entries[0].Time);
        Assert.Equal("INF", entries[0].Level);
        Assert.Equal("Señal", entries[0].EventType);
        Assert.Contains("ExecuteAsync", entries[0].RawLine, StringComparison.Ordinal);
        Assert.Contains("crypto", entries[0].Scopes);
        Assert.Contains("crypto", entries[1].Scopes);
        Assert.Empty(entries[2].Scopes);
    }

    [Fact]
    public void ApplyScope_KeepsOnlyExplicitlyRelatedMarketAndTraderEvents()
    {
        var entries = _interpreter.Interpret(Snapshot(
            "2026-08-02 12:00:00.000 -06:00 [INF] Binance returned candles for ETHUSDT.",
            "2026-08-02 12:00:01.000 -06:00 [INF] Yahoo Finance Forex returned EURUSD 15m.",
            "2026-08-02 12:00:02.000 -06:00 [INF] Trader profile imported from eToro CopyTrader.",
            "2026-08-02 12:00:03.000 -06:00 [WRN] No XML encryptor configured.",
            "2026-08-02 12:00:04.000 -06:00 [ERR] CancellationTokenSource failed."));

        var crypto = _interpreter.ApplyScope(entries, "crypto");
        var forex = _interpreter.ApplyScope(entries, "forex");
        var traders = _interpreter.ApplyScope(entries, "traders");

        Assert.Single(crypto);
        Assert.Contains("ETHUSDT", crypto[0].Message, StringComparison.Ordinal);
        Assert.Single(forex);
        Assert.Contains("EURUSD", forex[0].Message, StringComparison.Ordinal);
        Assert.Single(traders);
        Assert.Contains("eToro", traders[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyFilters_FiltersWithinScopeBeforeReversing()
    {
        var entries = _interpreter.Interpret(Snapshot(
            "2026-08-02 12:00:00.000 -06:00 [INF] Binance returned BTCUSDT candles.",
            "2026-08-02 12:00:01.000 -06:00 [ERR] Binance request failed for BTCUSDT.",
            "2026-08-02 12:00:02.000 -06:00 [ERR] OANDA request failed for EURUSD."));

        var filtered = _interpreter.ApplyFilters(entries, "ERR", "Incidente", "failed", "crypto");

        var entry = Assert.Single(filtered);
        Assert.Contains("BTCUSDT", entry.Message, StringComparison.Ordinal);
    }

    private static LogSnapshot Snapshot(params string[] lines)
    {
        return new LogSnapshot(
            new LogFileView("worker/worker-test.log", "worker-test.log", 0, DateTimeOffset.UtcNow),
            lines,
            "logs",
            null);
    }
}
