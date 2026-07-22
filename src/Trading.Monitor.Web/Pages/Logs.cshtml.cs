using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Trading.Monitor.Web.Services;

namespace Trading.Monitor.Web.Pages;

public sealed class LogsModel(OperationalLogReader logReader, ILogger<LogsModel> logger) : PageModel
{
    public IReadOnlyList<LogFileView> Files { get; private set; } = [];

    public LogSnapshot Snapshot { get; private set; } = new(null, [], "", null);

    [BindProperty(SupportsGet = true)]
    public string LogFile { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public int Lines { get; set; } = 250;

    public void OnGet()
    {
        logger.LogInformation("Loading logs page for {LogFile}.", LogFile);
        Files = logReader.ListFiles();
        Snapshot = logReader.Read(LogFile, Lines);
        LogFile = Snapshot.File?.RelativePath ?? LogFile;
    }

    public string SizeLabel(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / 1024m / 1024m:N2} MB";

        if (bytes >= 1024)
            return $"{bytes / 1024m:N1} KB";

        return $"{bytes} B";
    }
}
