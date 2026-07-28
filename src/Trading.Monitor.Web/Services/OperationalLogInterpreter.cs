using System.Text.RegularExpressions;

namespace Trading.Monitor.Web.Services;

public sealed class OperationalLogInterpreter
{
    public IReadOnlyList<LogEntryView> Interpret(LogSnapshot snapshot)
    {
        return snapshot.Lines.Select(line => ParseLine(snapshot, line)).ToArray();
    }

    public IReadOnlyList<LogEntryView> ApplyFilters(IEnumerable<LogEntryView> entries, string? level, string? eventType, string? search, string? scope)
    {
        if (!string.IsNullOrWhiteSpace(level))
            entries = entries.Where(entry => string.Equals(entry.Level, level, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(eventType))
            entries = entries.Where(entry => string.Equals(entry.EventType, eventType, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            entries = entries.Where(entry =>
                entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Service.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.RawLine.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        entries = NormalizeScope(scope) switch
        {
            "crypto" => entries.Where(entry => ContainsAny(entry.RawLine, "BTC", "ETH", "SOL", "XRP", "ADA", "USDT", "Binance", "Coinbase", "Kraken", "crypto")),
            "forex" => entries.Where(entry => ContainsAny(entry.RawLine, "EURUSD", "GBPUSD", "USDJPY", "USDCHF", "AUDUSD", "USDCAD", "NZDUSD", "USDMXN", "forex", "Yahoo Finance FX", "Alpha Vantage FX", "OANDA")),
            "traders" => entries.Where(entry => ContainsAny(entry.RawLine, "trader", "copy", "research", "source", "perfil", "historial")),
            _ => entries
        };

        return entries.Reverse().ToArray();
    }

    public IReadOnlyList<LogBucketView> BuildBuckets(IEnumerable<LogEntryView> entries)
    {
        return entries.GroupBy(entry => entry.Hour)
            .OrderBy(group => group.Key)
            .Select(group => new LogBucketView(group.Key, group.Count()))
            .ToArray();
    }

    public string ResolveScopeLabel(string? scope)
    {
        return NormalizeScope(scope) switch
        {
            "crypto" => "Crypto",
            "forex" => "Forex",
            "traders" => "Traders",
            _ => "Todo"
        };
    }

    public static string NormalizeScope(string? scope)
    {
        return scope?.Trim().ToLowerInvariant() switch
        {
            "crypto" => "crypto",
            "forex" => "forex",
            "traders" => "traders",
            _ => "todo"
        };
    }

    private static LogEntryView ParseLine(LogSnapshot snapshot, string line)
    {
        var match = Regex.Match(line, @"^\[(?<time>\d{2}:\d{2}:\d{2})\s+(?<level>[A-Z]{3})\]\s*(?<message>.*)$");
        var time = match.Success ? match.Groups["time"].Value : "--:--:--";
        var level = match.Success ? match.Groups["level"].Value : "-";
        var message = match.Success ? match.Groups["message"].Value : line;
        var service = snapshot.File?.RelativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) == true ? "Worker" :
            snapshot.File?.RelativePath.Contains("web", StringComparison.OrdinalIgnoreCase) == true ? "Web" : "Sistema";

        return new LogEntryView(time, time.Length >= 2 ? time[..2] : "--", level, ResolveLevelLabel(level), ResolveEventType(level, message), service, message, line);
    }

    private static string ResolveLevelLabel(string level)
    {
        return level switch
        {
            "INF" => "Info",
            "WRN" => "Alerta",
            "ERR" => "Error",
            "FTL" => "Critico",
            "DBG" => "Debug",
            _ => "Linea"
        };
    }

    private static string ResolveEventType(string level, string message)
    {
        if (level is "ERR" or "FTL" || ContainsAny(message, "failed", "error", "exception", "returned 4", "returned 5"))
            return "Incidente";

        if (ContainsAny(message, "New signal", "Signal sent", "closed as", "Skipping duplicate"))
            return "Señal";

        if (ContainsAny(message, "Scanning", "No valid opportunities", "valid opportunities"))
            return "Barrido";

        if (ContainsAny(message, "HTTP request", "HTTP response", "api.binance", "api.coinbase", "api.kraken", "query1.finance.yahoo.com", "alphavantage"))
            return "API";

        if (ContainsAny(message, "database", "TradingMarket", "SQL"))
            return "Base de datos";

        if (ContainsAny(message, "started", "Hosting environment", "Content root", "shut down"))
            return "Servicio";

        return "General";
    }

    private static bool ContainsAny(string value, params string[] patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record LogEntryView(string Time, string Hour, string Level, string LevelLabel, string EventType, string Service, string Message, string RawLine);

public sealed record LogBucketView(string Hour, int Count);
