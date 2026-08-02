using System.Text;
using System.Text.RegularExpressions;

namespace Trading.Monitor.Web.Services;

public sealed partial class OperationalLogInterpreter
{
    private static readonly string[] CryptoMarkers =
    [
        "Binance", "Coinbase", "Kraken", "CoinGecko", "CoinGlass", "LunarCrush", "Glassnode", "Santiment",
        "CryptoPanic", "CoinDesk", "Cointelegraph", "CryptoSlate", "Fear & Greed", "DefiLlama"
    ];

    private static readonly string[] ForexMarkers =
    [
        "Forex", "Yahoo Finance FX", "Yahoo Finance Forex", "Alpha Vantage FX", "OANDA", "Myfxbook RSS",
        "EUR/USD", "GBP/USD", "USD/JPY", "USD/CHF", "AUD/USD", "USD/CAD", "NZD/USD", "USD/MXN",
        "EUR/MXN", "GBP/JPY", "EUR/JPY", "EUR/GBP"
    ];

    private static readonly string[] TraderMarkers =
    [
        "copy trading", "copy trader", "copytrader", "eToro", "ZuluTrade", "Axi Copy", "TradingView Ideas",
        "Myfxbook Systems", "Popular Investor", "trader profile", "trader research", "trader history"
    ];

    public IReadOnlyList<LogEntryView> Interpret(LogSnapshot snapshot)
    {
        var parsed = new List<LogEntryView>();
        ParsedHeader? current = null;
        var raw = new StringBuilder();

        foreach (var line in snapshot.Lines)
        {
            if (TryParseHeader(line, out var header))
            {
                AppendCurrent(parsed, snapshot, current, raw);
                current = header;
                raw.Clear();
                raw.Append(line);
                continue;
            }

            if (current is not null)
            {
                raw.AppendLine();
                raw.Append(line);
                continue;
            }

            parsed.Add(BuildEntry(snapshot, "--:--:--", "-", line, line));
        }

        AppendCurrent(parsed, snapshot, current, raw);
        InheritSignalScope(parsed);
        return parsed;
    }

    public IReadOnlyList<LogEntryView> ApplyScope(IEnumerable<LogEntryView> entries, string? scope)
    {
        var normalized = NormalizeScope(scope);
        if (normalized == "todo")
            return entries.ToArray();

        return entries.Where(entry => entry.Scopes.Contains(normalized, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    public IReadOnlyList<LogEntryView> ApplyFilters(IEnumerable<LogEntryView> entries, string? level, string? eventType, string? search, string? scope)
    {
        entries = ApplyScope(entries, scope);

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

    private static void AppendCurrent(List<LogEntryView> entries, LogSnapshot snapshot, ParsedHeader? current, StringBuilder raw)
    {
        if (current is null)
            return;

        entries.Add(BuildEntry(snapshot, current.Time, current.Level, current.Message, raw.ToString()));
    }

    private static LogEntryView BuildEntry(LogSnapshot snapshot, string time, string level, string message, string rawLine)
    {
        var service = snapshot.File?.RelativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) == true ? "Worker" :
            snapshot.File?.RelativePath.Contains("web", StringComparison.OrdinalIgnoreCase) == true ? "Web" : "Sistema";
        var scopes = ResolveScopes($"{message}\n{rawLine}");

        return new LogEntryView(
            time,
            time.Length >= 2 ? time[..2] : "--",
            level,
            ResolveLevelLabel(level),
            ResolveEventType(level, message),
            service,
            message,
            scopes,
            rawLine);
    }

    private static bool TryParseHeader(string line, out ParsedHeader? header)
    {
        var match = LogHeaderRegex().Match(line);
        if (!match.Success)
        {
            header = null;
            return false;
        }

        var time = match.Groups["fileTime"].Success ? match.Groups["fileTime"].Value : match.Groups["consoleTime"].Value;
        var level = match.Groups["fileLevel"].Success ? match.Groups["fileLevel"].Value : match.Groups["consoleLevel"].Value;
        header = new ParsedHeader(time, level, match.Groups["message"].Value);
        return true;
    }

    private static IReadOnlyList<string> ResolveScopes(string value)
    {
        var scopes = new List<string>(3);

        if (CryptoSymbolRegex().IsMatch(value) || CryptoWordRegex().IsMatch(value) || ContainsAny(value, CryptoMarkers))
            scopes.Add("crypto");

        if (ForexSymbolRegex().IsMatch(value) || ContainsAny(value, ForexMarkers))
            scopes.Add("forex");

        if (TraderWordRegex().IsMatch(value) || ContainsAny(value, TraderMarkers))
            scopes.Add("traders");

        return scopes;
    }

    private static void InheritSignalScope(List<LogEntryView> entries)
    {
        IReadOnlyList<string> previousScopes = [];
        string? previousEvent = null;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Scopes.Count == 0 && entry.EventType == "Señal" && previousEvent == "Señal" && previousScopes.Count > 0)
            {
                entry = entry with { Scopes = previousScopes };
                entries[index] = entry;
            }

            if (entry.Scopes.Count > 0)
                previousScopes = entry.Scopes;

            previousEvent = entry.EventType;
        }
    }

    private static string ResolveLevelLabel(string level)
    {
        return level switch
        {
            "INF" => "Info",
            "WRN" => "Alerta",
            "ERR" => "Error",
            "FTL" => "Crítico",
            "DBG" => "Debug",
            _ => "Línea"
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

    [GeneratedRegex(@"^(?:(?:\d{4}-\d{2}-\d{2}\s+)?(?<fileTime>\d{2}:\d{2}:\d{2})(?:\.\d+)?(?:\s+[+-]\d{2}:\d{2})?\s+\[(?<fileLevel>[A-Z]{3})\]|\[(?<consoleTime>\d{2}:\d{2}:\d{2})\s+(?<consoleLevel>[A-Z]{3})\])\s*(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LogHeaderRegex();

    [GeneratedRegex(@"\b(?:BTC|ETH|SOL|XRP|ADA)(?:USDT|USD)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CryptoSymbolRegex();

    [GeneratedRegex(@"\b(?:crypto|cryptocurrency|cripto)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CryptoWordRegex();

    [GeneratedRegex(@"\b(?:EURUSD|GBPUSD|USDJPY|USDCHF|AUDUSD|USDCAD|NZDUSD|USDMXN|EURMXN|GBPJPY|EURJPY|EURGBP)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForexSymbolRegex();

    [GeneratedRegex(@"\btraders?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TraderWordRegex();

    private sealed record ParsedHeader(string Time, string Level, string Message);
}

public sealed record LogEntryView(
    string Time,
    string Hour,
    string Level,
    string LevelLabel,
    string EventType,
    string Service,
    string Message,
    IReadOnlyList<string> Scopes,
    string RawLine);

public sealed record LogBucketView(string Hour, int Count);
