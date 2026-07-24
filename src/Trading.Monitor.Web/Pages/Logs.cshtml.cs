using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class LogsModel(OperationalLogReader logReader, ILogger<LogsModel> logger) : PageModel
{
    public IReadOnlyList<LogFileView> Files { get; private set; } = [];

    public LogSnapshot Snapshot { get; private set; } = new(null, [], "", null);

    public IReadOnlyList<LogEntryView> Entries { get; private set; } = [];

    public IReadOnlyList<LogEntryView> FilteredEntries { get; private set; } = [];

    public IReadOnlyList<LogBucketView> Buckets { get; private set; } = [];

    public IReadOnlyList<string> AvailableLevels { get; private set; } = [];

    public IReadOnlyList<string> AvailableEvents { get; private set; } = [];

    public int MaxBucketCount { get; private set; }

    public int ErrorCount { get; private set; }

    public int WarningCount { get; private set; }

    public int SignalCount { get; private set; }

    public int ScanCount { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string LogFile { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public int Lines { get; set; } = 250;

    [BindProperty(SupportsGet = true)]
    public string Nivel { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Evento { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string Buscar { get; set; } = "";

    public void OnGet()
    {
        logger.LogInformation("Loading logs page for {LogFile}.", LogFile);
        Files = logReader.ListFiles();
        Snapshot = logReader.Read(LogFile, Lines);
        LogFile = Snapshot.File?.RelativePath ?? LogFile;
        Entries = Snapshot.Lines.Select(ParseLine).ToArray();
        AvailableLevels = Entries.Select(entry => entry.Level).Where(level => level != "-").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(level => level).ToArray();
        AvailableEvents = Entries.Select(entry => entry.EventType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(type => type).ToArray();
        FilteredEntries = ApplyFilters(Entries).ToArray();
        Buckets = FilteredEntries.GroupBy(entry => entry.Hour).OrderBy(group => group.Key).Select(group => new LogBucketView(group.Key, group.Count())).ToArray();
        MaxBucketCount = Buckets.Select(bucket => bucket.Count).DefaultIfEmpty(0).Max();
        ErrorCount = Entries.Count(entry => entry.Level is "ERR" or "FTL");
        WarningCount = Entries.Count(entry => entry.Level == "WRN");
        SignalCount = Entries.Count(entry => entry.EventType == "Senal");
        ScanCount = Entries.Count(entry => entry.EventType == "Barrido");
    }

    public string SizeLabel(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / 1024m / 1024m:N2} MB";

        if (bytes >= 1024)
            return $"{bytes / 1024m:N1} KB";

        return $"{bytes} B";
    }

    public decimal BarWidth(int count)
    {
        if (MaxBucketCount <= 0)
            return 0m;

        return Math.Clamp((decimal)count / MaxBucketCount * 100m, 4m, 100m);
    }

    public string LevelClass(string level)
    {
        return level switch
        {
            "ERR" or "FTL" => "status-loss",
            "WRN" => "status-muted",
            "INF" => "status-win",
            _ => "status-open"
        };
    }

    public string EventClass(string eventType)
    {
        return eventType switch
        {
            "Incidente" => "loss",
            "Senal" => "gain",
            "Barrido" => "flat",
            _ => "muted"
        };
    }

    private IEnumerable<LogEntryView> ApplyFilters(IEnumerable<LogEntryView> entries)
    {
        if (!string.IsNullOrWhiteSpace(Nivel))
            entries = entries.Where(entry => string.Equals(entry.Level, Nivel, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(Evento))
            entries = entries.Where(entry => string.Equals(entry.EventType, Evento, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(Buscar))
            entries = entries.Where(entry => entry.Message.Contains(Buscar, StringComparison.OrdinalIgnoreCase) || entry.Service.Contains(Buscar, StringComparison.OrdinalIgnoreCase));

        return entries.Reverse();
    }

    private LogEntryView ParseLine(string line)
    {
        var match = Regex.Match(line, @"^\[(?<time>\d{2}:\d{2}:\d{2})\s+(?<level>[A-Z]{3})\]\s*(?<message>.*)$");
        var time = match.Success ? match.Groups["time"].Value : "--:--:--";
        var level = match.Success ? match.Groups["level"].Value : "-";
        var message = match.Success ? match.Groups["message"].Value : line;
        var service = Snapshot.File?.RelativePath.Contains("worker", StringComparison.OrdinalIgnoreCase) == true ? "Worker" :
            Snapshot.File?.RelativePath.Contains("web", StringComparison.OrdinalIgnoreCase) == true ? "Web" : "Sistema";

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
            return "Senal";

        if (ContainsAny(message, "Scanning", "No valid opportunities", "valid opportunities"))
            return "Barrido";

        if (ContainsAny(message, "HTTP request", "HTTP response", "api.binance", "api.coinbase", "api.kraken"))
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
