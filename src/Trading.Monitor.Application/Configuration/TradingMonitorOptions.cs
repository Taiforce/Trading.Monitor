namespace Trading.Monitor.Application.Configuration;

public sealed class TradingMonitorOptions
{
    public bool Enabled { get; set; } = true;

    public string[] Symbols { get; set; } = ["BTCUSDT", "ETHUSDT"];

    public string[] Intervals { get; set; } = ["1m", "5m", "15m", "1h", "4h"];

    public string TriggerInterval { get; set; } = "5m";

    public int CandleLimit { get; set; } = 250;

    public int MinimumScore { get; set; } = 80;

    public int EvaluationIntervalSeconds { get; set; } = 60;

    public int DuplicateWindowMinutes { get; set; } = 20;

    public int SignalExpiryMinutes { get; set; } = 8;

    public bool RunOnce { get; set; }
}