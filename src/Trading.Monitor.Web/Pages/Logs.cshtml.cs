using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class LogsModel(OperationalLogReader logReader, OperationalLogInterpreter logInterpreter, ILogger<LogsModel> logger) : PageModel
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

    [BindProperty(SupportsGet = true)]
    public string Ambito { get; set; } = "todo";

    public void OnGet()
    {
        logger.LogInformation("Loading logs page for {LogFile}.", LogFile);
        Files = logReader.ListFiles();
        Snapshot = logReader.Read(LogFile, Lines);
        LogFile = Snapshot.File?.RelativePath ?? LogFile;
        Ambito = OperationalLogInterpreter.NormalizeScope(Ambito);
        Entries = logInterpreter.Interpret(Snapshot);
        AvailableLevels = Entries.Select(entry => entry.Level).Where(level => level != "-").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(level => level).ToArray();
        AvailableEvents = Entries.Select(entry => entry.EventType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(type => type).ToArray();
        FilteredEntries = logInterpreter.ApplyFilters(Entries, Nivel, Evento, Buscar, Ambito);
        Buckets = logInterpreter.BuildBuckets(FilteredEntries);
        MaxBucketCount = Buckets.Select(bucket => bucket.Count).DefaultIfEmpty(0).Max();
        ErrorCount = Entries.Count(entry => entry.Level is "ERR" or "FTL");
        WarningCount = Entries.Count(entry => entry.Level == "WRN");
        SignalCount = Entries.Count(entry => entry.EventType == "Señal");
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
            "Señal" => "gain",
            "Barrido" => "flat",
            _ => "muted"
        };
    }
}
