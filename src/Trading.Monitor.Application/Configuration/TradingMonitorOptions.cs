namespace Trading.Monitor.Application.Configuration;

public sealed class TradingMonitorOptions
{
    public bool Enabled { get; set; } = true;

    public string[] Symbols { get; set; } = ["BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "ADAUSDT"];

    public string[] Intervals { get; set; } = ["1s", "1m", "5m", "15m", "1h", "4h", "1d", "1w", "1M"];

    public string TriggerInterval { get; set; } = "5m";

    public int CandleLimit { get; set; } = 250;

    public int MinimumScore { get; set; } = 80;

    public int EvaluationIntervalSeconds { get; set; } = 60;

    public int DuplicateWindowMinutes { get; set; } = 20;

    public int SignalExpiryMinutes { get; set; } = 8;

    public TradingHorizonOptions[] Horizons { get; set; } =
    [
        new()
        {
            Name = "Rápida",
            TriggerInterval = "5m",
            SignalExpiryMinutes = 15,
            MinimumScore = 88,
            MinimumConfirmedIntervals = 3,
            RequiredAlignedIntervals = ["15m", "1h"]
        },
        new()
        {
            Name = "Intradía",
            TriggerInterval = "15m",
            SignalExpiryMinutes = 180,
            MinimumScore = 90,
            MinimumConfirmedIntervals = 3,
            RequiredAlignedIntervals = ["1h", "4h"]
        },
        new()
        {
            Name = "Swing",
            TriggerInterval = "1h",
            SignalExpiryMinutes = 2880,
            MinimumScore = 92,
            MinimumConfirmedIntervals = 4,
            RequiredAlignedIntervals = ["4h", "1d"]
        },
        new()
        {
            Name = "Semanal",
            TriggerInterval = "1d",
            SignalExpiryMinutes = 10080,
            MinimumScore = 92,
            MinimumConfirmedIntervals = 4,
            RequiredAlignedIntervals = ["1w"]
        },
        new()
        {
            Name = "Mensual",
            TriggerInterval = "1w",
            SignalExpiryMinutes = 43200,
            MinimumScore = 94,
            MinimumConfirmedIntervals = 4,
            RequiredAlignedIntervals = ["1M"]
        }
    ];

    public bool RunOnce { get; set; }

    /// <summary>Enables the "Señales Ajenas" ensemble of independent public-strategy models (ExternalAiSignalEngine).</summary>
    public bool ExternalAiSignalsEnabled { get; set; } = true;
}

public sealed class TradingHorizonOptions
{
    public string Name { get; set; } = "Rápida";

    public string TriggerInterval { get; set; } = "5m";

    public int SignalExpiryMinutes { get; set; } = 8;

    public int MinimumScore { get; set; } = 80;

    public int MinimumConfirmedIntervals { get; set; } = 2;

    public string[] RequiredAlignedIntervals { get; set; } = [];
}
